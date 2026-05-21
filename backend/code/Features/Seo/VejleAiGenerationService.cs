using SeoToolkit.Umbraco.AI.Core.Services;
using VejleKommune.Code.Features.Ai;

namespace VejleKommune.Code.Features.Seo;

/// <summary>
/// Phase 2 – Sync Suggestion (SEO meta description).
/// Implements SeoToolkit's IAIGenerationService so the backoffice
/// "✨ Generate with AI" button is backed by our IAiProvider → Gemini stack.
/// </summary>
public sealed class VejleAiGenerationService : IAIGenerationService
{
    // SeoToolkit passes its own system prompt; we prepend Vejle-specific context.
    private const string VejleContext =
        "Du er en SEO-ekspert for Vejle Kommune (dansk kommunal hjemmeside). " +
        "Skriv kort, præcist og på dansk. " +
        "Title: max 60 tegn. Meta description: max 160 tegn. " +
        "Open Graph title og description må gerne være lidt mere engagerende end de rene SEO-felter.";

    // SeoToolkit expects this exact JSON shape back.
    private const string JsonSchema = """
        {
          "type": "object",
          "properties": {
            "title":                { "type": "string" },
            "metaDescription":      { "type": "string" },
            "openGraphTitle":       { "type": "string" },
            "openGraphDescription": { "type": "string" }
          },
          "required": ["title", "metaDescription", "openGraphTitle", "openGraphDescription"]
        }
        """;

    private readonly IAiProvider _aiProvider;

    public VejleAiGenerationService(IAiProvider aiProvider)
    {
        _aiProvider = aiProvider;
    }

    public async Task<string> GenerateRawResponseAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        // Combine SeoToolkit's system prompt (page context) with our municipality branding.
        string combinedSystemPrompt = string.IsNullOrWhiteSpace(systemPrompt)
            ? VejleContext
            : $"{VejleContext}\n\n{systemPrompt}";

        AiRequest request = new(
            Prompt: userPrompt,
            SystemPrompt: combinedSystemPrompt,
            JsonResponseSchema: JsonSchema,
            CallContext: new AiCallContext("SyncSuggestion-SeoMeta"));

        AiResponse response = await _aiProvider.CompleteAsync(request, cancellationToken);
        return response.Content;
    }
}
