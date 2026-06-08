using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using VejleKommune.Code.Features.Ai;

namespace VejleKommune.Code.Features.ToneOfVoice;

/// <summary>
/// Phase 3 – Gate / validator (tone of voice).
/// Fires on ContentPublishingNotification, evaluates the page text against
/// Vejle Kommune's tone of voice guidelines, and cancels the publish if it fails.
/// Fails open: any AI/infrastructure error allows the publish through.
/// </summary>
public sealed class ToneOfVoiceGate : INotificationAsyncHandler<ContentPublishingNotification>
{
    private const string SystemPrompt = """
        Du er kommunikationsrådgiver for Vejle Kommune.
        Evaluer om teksten overholder Vejle Kommunes tone of voice:
        - Borgervenlig og inkluderende sprog
        - Klart og enkelt dansk — undgå fagord og bureaukratisk sprogbrug
        - Positiv, hjælpsom og handlingsorienteret tone
        - Respektfuld og ikke nedladende

        Returner KUN et JSON-objekt — ingen markdown, ingen forklaring uden for JSON.
        """;

    private const string JsonSchema = """
        {
          "type": "object",
          "properties": {
            "passes":      { "type": "boolean" },
            "reason":      { "type": "string"  },
            "suggestions": { "type": "array", "items": { "type": "string" } }
          },
          "required": ["passes", "reason", "suggestions"]
        }
        """;

    // Aliases to skip — technical / system fields that aren't editorial prose.
    private static readonly HashSet<string> SkippedPrefixes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "schema", "meta", "translation", "accessibility", "umbraco",
        };

    private readonly IAiProvider _aiProvider;
    private readonly ILogger<ToneOfVoiceGate> _logger;

    public ToneOfVoiceGate(IAiProvider aiProvider, ILogger<ToneOfVoiceGate> logger)
    {
        _aiProvider = aiProvider;
        _logger = logger;
    }

    public async Task HandleAsync(
        ContentPublishingNotification notification,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "ToneOfVoiceGate fired for {Count} entities",
            notification.PublishedEntities.Count());

        foreach (IContent content in notification.PublishedEntities)
        {
            string text = ExtractText(content);

            _logger.LogWarning(
                "ToneOfVoiceGate extracted {Length} chars from {ContentName}",
                text.Length,
                content.Name);

            // Not enough prose to evaluate — skip silently.
            if (text.Length < 50)
                continue;

            ToneCheckResult? result = null;

            try
            {
                AiRequest request = new(
                    Prompt: $"Evaluer denne tekst fra siden \"{content.Name}\":\n\n{text}",
                    SystemPrompt: SystemPrompt,
                    JsonResponseSchema: JsonSchema,
                    CallContext: new AiCallContext("Gate-ToneOfVoice", content.Key.ToString()));

                AiResponse response = await _aiProvider.CompleteAsync(request, cancellationToken);

                _logger.LogInformation(
                    "ToneOfVoiceGate raw AI response for {ContentName}: {Response}",
                    content.Name,
                    response.Content);

                result = JsonSerializer.Deserialize<ToneCheckResult>(
                    response.Content,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception ex)
            {
                // Fail open — gate must never block publish due to AI/infra issues.
                _logger.LogWarning(
                    ex,
                    "Tone of voice gate check failed for {ContentKey} ({ContentName}), allowing publish",
                    content.Key,
                    content.Name);
                continue;
            }

            // Require a non-null reason — a missing reason means the AI response
            // was malformed or incomplete. Fail open in that case.
            if (result is { Passes: false, Reason: not null })
            {
                string suggestions = result.Suggestions is { Count: > 0 }
                    ? " Forslag: " + string.Join("; ", result.Suggestions)
                    : string.Empty;

                notification.CancelOperation(new EventMessage(
                    "Tone of voice",
                    $"{result.Reason}{suggestions}",
                    EventMessageType.Error));

                _logger.LogInformation(
                    "Tone of voice gate blocked publish for {ContentKey} ({ContentName}). Reason: {Reason}",
                    content.Key,
                    content.Name,
                    result.Reason);

                return;
            }

            _logger.LogInformation(
                "Tone of voice gate passed for {ContentKey} ({ContentName}). Reason: {Reason}",
                content.Key,
                content.Name,
                result?.Reason ?? "(no result)");
        }
    }

    private static string ExtractText(IContent content)
    {
        var parts = new List<string>();

        // Culture-variant properties (e.g. body, headline) return null from GetValue()
        // without a culture — try each available culture, fall back to invariant.
        string[] cultures = content.AvailableCultures.ToArray();

        foreach (IProperty property in content.Properties)
        {
            if (SkippedPrefixes.Any(p => property.Alias.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                continue;

            string? raw = null;

            foreach (string culture in cultures)
            {
                raw = property.GetValue(culture)?.ToString();
                if (!string.IsNullOrWhiteSpace(raw)) break;
            }

            // Fall back to invariant value if no culture hit.
            if (string.IsNullOrWhiteSpace(raw))
                raw = property.GetValue()?.ToString();

            if (string.IsNullOrWhiteSpace(raw))
                continue;

            // Skip block list / block grid JSON — raw JSON cannot be tone-evaluated
            // and causes the gate to always return false for block properties.
            string trimmed = raw.TrimStart();
            if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
                continue;

            // Skip UDI values from content/media pickers (e.g. umb://document/...)
            // and any other URI-like technical values.
            if (trimmed.StartsWith("umb://") || trimmed.StartsWith("http"))
                continue;

            // Strip HTML (rich text editors).
            string plain = Regex.Replace(raw, "<[^>]+>", " ");
            plain = Regex.Replace(plain, @"\s+", " ").Trim();

            if (plain.Length > 20)
                parts.Add(plain);
        }

        return string.Join("\n\n", parts);
    }
}

internal sealed record ToneCheckResult(
    [property: JsonPropertyName("passes")]      bool         Passes,
    [property: JsonPropertyName("reason")]      string       Reason,
    [property: JsonPropertyName("suggestions")] List<string> Suggestions);
