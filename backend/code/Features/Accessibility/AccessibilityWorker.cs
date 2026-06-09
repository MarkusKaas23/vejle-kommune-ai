using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using VejleKommune.Code.Features.Ai;

namespace VejleKommune.Code.Features.Accessibility;

/// <summary>
/// Phase 6 – Async Analyze (accessibility audit).
/// BackgroundService that drains AccessibilityQueue, sends content to Gemini
/// for a WCAG/plain-language audit, and stores structured findings in memory.
/// The CMS content tree is NEVER modified — findings live alongside it.
/// Rollback semantics: trivial (dismiss = remove from dictionary).
/// </summary>
public sealed class AccessibilityWorker : BackgroundService
{
    // Same aliases as TranslationWorker — editorial prose fields.
    private static readonly HashSet<string> AuditableAliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "headline", "summary", "body", "title", "description", "text",
        };

    private readonly AccessibilityQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAiProvider _aiProvider;
    private readonly ILogger<AccessibilityWorker> _logger;

    public AccessibilityWorker(
        AccessibilityQueue queue,
        IServiceScopeFactory scopeFactory,
        IAiProvider aiProvider,
        ILogger<AccessibilityWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _aiProvider = aiProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (AccessibilityJob job in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            _queue.SetState(job.JobId, new AccessibilityJobState(job.JobId, AccessibilityStatus.Running));
            _logger.LogInformation(
                "Accessibility job {JobId} started: {ContentKey} [{Culture}]",
                job.JobId, job.ContentKey, job.Culture);

            try
            {
                IReadOnlyList<AccessibilityFinding> findings = await AnalyzeAsync(job, stoppingToken);

                _queue.SetState(job.JobId, new AccessibilityJobState(
                    job.JobId, AccessibilityStatus.Completed,
                    Findings: findings,
                    CompletedAt: DateTimeOffset.UtcNow));

                _logger.LogInformation(
                    "Accessibility job {JobId} completed: {Count} finding(s).",
                    job.JobId, findings.Count);
            }
            catch (Exception ex)
            {
                _queue.SetState(job.JobId, new AccessibilityJobState(
                    job.JobId, AccessibilityStatus.Failed, Error: ex.Message));

                _logger.LogError(ex, "Accessibility job {JobId} failed.", job.JobId);
            }
        }
    }

    private async Task<IReadOnlyList<AccessibilityFinding>> AnalyzeAsync(
        AccessibilityJob job, CancellationToken ct)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        IContentService contentService = scope.ServiceProvider.GetRequiredService<IContentService>();

        IContent? content = contentService.GetById(job.ContentKey);
        if (content is null)
            throw new InvalidOperationException($"Content {job.ContentKey} not found.");

        // Collect auditable text fields — strip HTML to give Gemini clean prose.
        Dictionary<string, string> fieldTexts = new();
        foreach (IProperty property in content.Properties)
        {
            if (!AuditableAliases.Contains(property.Alias)) continue;

            string? raw = property.GetValue(job.Culture)?.ToString()
                       ?? property.GetValue()?.ToString();

            if (string.IsNullOrWhiteSpace(raw)) continue;

            // Unwrap Umbraco rich-text {"markup":"<html>"} envelope if present.
            string text = ExtractText(raw);
            if (!string.IsNullOrWhiteSpace(text))
                fieldTexts[property.Alias] = text;
        }

        if (fieldTexts.Count == 0)
            return Array.Empty<AccessibilityFinding>();

        string contentJson = JsonSerializer.Serialize(fieldTexts, new JsonSerializerOptions
        {
            WriteIndented = true,
        });

        const string SystemPrompt =
            "Du er en tilgængelighedsekspert for Vejle Kommune. " +
            "Analyser følgende JSON-objekt med indholdstekster og identificer tilgængelighedsproblemer. " +
            "Fokusér på: læsbarhed for borgere (Flesch-niveau), passiv sætningsopbygning, bureaukratisk sprog, " +
            "manglende kontekst i overskrifter, og lange sætninger (>25 ord). " +
            "Returner KUN en JSON-array af fund. Hvert fund har fire felter: " +
            "\"field\" (egenskabsalias), \"issue\" (kort beskrivelse), \"suggestion\" (konkret rettelse), " +
            "\"severity\" (\"error\", \"warning\" eller \"info\"). " +
            "Returner en tom array [] hvis ingen problemer findes.";

        const string JsonSchema = """
            {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "field":      { "type": "string" },
                  "issue":      { "type": "string" },
                  "suggestion": { "type": "string" },
                  "severity":   { "type": "string", "enum": ["error", "warning", "info"] }
                },
                "required": ["field", "issue", "suggestion", "severity"]
              }
            }
            """;

        AiRequest request = new(
            Prompt: contentJson,
            SystemPrompt: SystemPrompt,
            JsonResponseSchema: JsonSchema,
            CallContext: new AiCallContext("AsyncAnalyze-Accessibility", job.ContentKey.ToString()));

        AiResponse response = await _aiProvider.CompleteAsync(request, ct);

        List<AccessibilityFinding>? findings =
            JsonSerializer.Deserialize<List<AccessibilityFinding>>(response.Content,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        return findings ?? new List<AccessibilityFinding>();
    }

    /// <summary>
    /// Strips HTML tags and unwraps Umbraco rich-text JSON envelope to return plain text.
    /// </summary>
    private static string ExtractText(string raw)
    {
        string html = raw;

        // Unwrap {"markup":"..."} if present.
        if (raw.TrimStart().StartsWith('{'))
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(raw);
                if (doc.RootElement.TryGetProperty("markup", out JsonElement markup))
                    html = markup.GetString() ?? raw;
            }
            catch { /* not valid JSON */ }
        }

        // Strip HTML tags.
        return Regex.Replace(html, "<[^>]+>", " ").Trim();
    }
}
