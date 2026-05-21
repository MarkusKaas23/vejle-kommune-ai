using Microsoft.AspNetCore.Mvc;
using SeoToolkit.Umbraco.AI.Core.Services;

namespace VejleKommune.Code.Features.Seo;

/// <summary>
/// Phase 2 – Sync Suggestion (SEO meta description).
/// Exposes a testable endpoint that exercises the full IAiProvider → Gemini → audit-log stack.
/// Route: POST /umbraco/api/vejle/seo/generate-meta-description
/// </summary>
[ApiController]
[Route("umbraco/api/vejle/seo")]
public sealed class SeoMetaController : ControllerBase
{
    private readonly IAIGenerationService _generationService;

    public SeoMetaController(IAIGenerationService generationService)
    {
        _generationService = generationService;
    }

    /// <summary>
    /// Generate an SEO meta description for the supplied page content.
    /// </summary>
    /// <remarks>
    /// Sends <paramref name="request"/> through VejleAiGenerationService → GeminiProvider.
    /// The audit record (PatternName, tokens, cost, latency) is written to the log.
    /// </remarks>
    [HttpPost("generate-meta-description")]
    [ProducesResponseType(typeof(SeoMetaResponse), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 429)]
    [ProducesResponseType(typeof(ProblemDetails), 502)]
    public async Task<IActionResult> GenerateAsync(
        [FromBody] SeoMetaRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            string result = await _generationService.GenerateRawResponseAsync(
                systemPrompt: string.Empty, // VejleAiGenerationService supplies its own system prompt
                userPrompt: request.PageContent,
                cancellationToken: cancellationToken);

            return Ok(new SeoMetaResponse(result));
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("429"))
        {
            return Problem(
                title: "AI quota exceeded",
                detail: "The Gemini API free-tier quota is exhausted. Wait for the daily reset or enable billing on the API key project.",
                statusCode: 429);
        }
        catch (HttpRequestException ex)
        {
            return Problem(
                title: "AI provider error",
                detail: ex.Message,
                statusCode: 502);
        }
    }
}

public sealed record SeoMetaRequest(
    /// <summary>Plain-text body of the page to summarise (paste a paragraph or two).</summary>
    string PageContent);

public sealed record SeoMetaResponse(
    /// <summary>AI-generated Danish SEO meta description (max ~160 chars).</summary>
    string MetaDescription);
