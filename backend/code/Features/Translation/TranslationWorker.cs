using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using VejleKommune.Code.Features.Ai;

namespace VejleKommune.Code.Features.Translation;

/// <summary>
/// Phase 5 – Async Transform (translation).
/// BackgroundService that drains TranslationQueue, calls Gemini to translate
/// all culture-variant text fields, and saves the result as an unpublished
/// culture variant — ready for editorial review before publishing.
/// </summary>
public sealed class TranslationWorker : BackgroundService
{
    // Aliases that contain editorial prose worth translating.
    private static readonly HashSet<string> TranslatableAliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "headline", "summary", "body", "title", "description", "text",
        };

    private readonly TranslationQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAiProvider _aiProvider;
    private readonly ILogger<TranslationWorker> _logger;

    public TranslationWorker(
        TranslationQueue queue,
        IServiceScopeFactory scopeFactory,
        IAiProvider aiProvider,
        ILogger<TranslationWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _aiProvider = aiProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (TranslationJob job in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            _queue.SetState(job.JobId, new TranslationJobState(job.JobId, TranslationStatus.Running));
            _logger.LogInformation(
                "Translation job {JobId} started: {ContentKey} {Source}→{Target}",
                job.JobId, job.ContentKey, job.SourceCulture, job.TargetCulture);

            try
            {
                await ProcessAsync(job, stoppingToken);

                _queue.SetState(job.JobId, new TranslationJobState(
                    job.JobId, TranslationStatus.Completed, CompletedAt: DateTimeOffset.UtcNow));

                _logger.LogInformation("Translation job {JobId} completed.", job.JobId);
            }
            catch (Exception ex)
            {
                _queue.SetState(job.JobId, new TranslationJobState(
                    job.JobId, TranslationStatus.Failed, Error: ex.Message));

                _logger.LogError(ex, "Translation job {JobId} failed.", job.JobId);
            }
        }
    }

    private async Task ProcessAsync(TranslationJob job, CancellationToken ct)
    {
        // Umbraco services are scoped — create a fresh scope for each job.
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        IContentService contentService = scope.ServiceProvider.GetRequiredService<IContentService>();

        IContent? content = contentService.GetById(job.ContentKey);
        if (content is null)
            throw new InvalidOperationException($"Content {job.ContentKey} not found.");

        // Collect translatable fields from source culture.
        // Rich-text fields are stored as {"markup":"<html>"} — unwrap to plain HTML so
        // Gemini only sees text+markup, not a nested JSON string.
        Dictionary<string, string> sourceTexts = new();
        HashSet<string> richTextAliases = new(StringComparer.OrdinalIgnoreCase);

        foreach (IProperty property in content.Properties)
        {
            if (!TranslatableAliases.Contains(property.Alias)) continue;

            string? raw = property.GetValue(job.SourceCulture)?.ToString();
            if (string.IsNullOrWhiteSpace(raw)) continue;

            string extracted = ExtractMarkup(raw, out bool isRichText);
            if (isRichText) richTextAliases.Add(property.Alias);
            sourceTexts[property.Alias] = extracted;
        }

        if (sourceTexts.Count == 0)
            throw new InvalidOperationException("No translatable fields found in source culture.");

        // Build translation prompt — send all fields as JSON so Gemini translates them
        // consistently in one call and preserves HTML markup in rich text fields.
        string sourceJson = JsonSerializer.Serialize(sourceTexts, new JsonSerializerOptions
        {
            WriteIndented = true,
        });

        string targetLanguageName = job.TargetCulture switch
        {
            "en-US" or "en-GB" => "English",
            "de-DE"            => "German",
            "fr-FR"            => "French",
            _                  => job.TargetCulture,
        };

        string systemPrompt =
            $"Du er oversætter for Vejle Kommune. " +
            $"Oversæt følgende JSON-objekt fra dansk til {targetLanguageName}. " +
            $"Returner KUN et JSON-objekt med de samme nøgler. " +
            $"Bevar al HTML-markup uændret — oversæt kun den synlige tekst. " +
            $"Brug en klar, borgervenlig tone.";

        string schemaProps = string.Join(",\n",
            sourceTexts.Keys.Select(k => $"\"{k}\": {{ \"type\": \"string\" }}"));

        string jsonSchema = $$"""
            {
              "type": "object",
              "properties": { {{schemaProps}} },
              "required": [{{string.Join(", ", sourceTexts.Keys.Select(k => $"\"{k}\""))}}]
            }
            """;

        AiRequest request = new(
            Prompt: sourceJson,
            SystemPrompt: systemPrompt,
            JsonResponseSchema: jsonSchema,
            CallContext: new AiCallContext("AsyncTransform-Translation", job.ContentKey.ToString()));

        AiResponse response = await _aiProvider.CompleteAsync(request, ct);

        Dictionary<string, string>? translated =
            JsonSerializer.Deserialize<Dictionary<string, string>>(response.Content);

        if (translated is null or { Count: 0 })
            throw new InvalidOperationException("Gemini returned empty translation.");

        // Register the culture variant — required so Umbraco creates the ContentCultureInfo
        // entry. Without this, SetValue calls persist to the DB but the variant never appears
        // as "created" in the backoffice. Use the translated headline as the variant name,
        // falling back to the source-culture name.
        string variantName = translated.TryGetValue("headline", out string? translatedHeadline)
            ? translatedHeadline
            : translated.TryGetValue("title", out string? translatedTitle)
                ? translatedTitle
                : content.Name ?? job.TargetCulture;

        content.SetCultureName(variantName, job.TargetCulture);

        // Write translated values to the target culture variant (saved but NOT published
        // — editor must review before publishing, enforcing the variant publish gate).
        // Re-wrap rich-text fields back into {"markup":"..."} format.
        foreach ((string alias, string value) in translated)
        {
            string finalValue = richTextAliases.Contains(alias)
                ? WrapMarkup(value)
                : value;
            content.SetValue(alias, finalValue, job.TargetCulture);
        }

        contentService.Save(content);
    }

    /// <summary>
    /// If <paramref name="raw"/> is a Umbraco rich-text JSON envelope {"markup":"..."},
    /// returns the inner HTML string and sets <paramref name="isRichText"/> = true.
    /// Otherwise returns <paramref name="raw"/> unchanged.
    /// </summary>
    private static string ExtractMarkup(string raw, out bool isRichText)
    {
        isRichText = false;
        if (!raw.TrimStart().StartsWith('{')) return raw;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("markup", out JsonElement markup))
            {
                isRichText = true;
                return markup.GetString() ?? raw;
            }
        }
        catch { /* not valid JSON */ }

        return raw;
    }

    /// <summary>Re-wraps a translated HTML string into Umbraco's rich-text envelope.</summary>
    private static string WrapMarkup(string html) =>
        JsonSerializer.Serialize(new { markup = html });
}
