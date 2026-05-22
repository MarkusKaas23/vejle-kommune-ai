using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core.IO;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using VejleKommune.Code.Features.Ai;

namespace VejleKommune.Code.Features.AltText;

/// <summary>
/// Phase 4 – Sync Suggestion (alt text, multimodal).
/// Reads an Umbraco media item, sends the image bytes to Gemini,
/// and returns a concise Danish alt text suggestion.
/// Route: POST /umbraco/api/vejle/media/generate-alt-text
/// </summary>
[ApiController]
[Route("umbraco/api/vejle/media")]
public sealed class AltTextController : ControllerBase
{
    private const string SystemPrompt =
        "Du er en tilgængelighedsekspert for Vejle Kommune. " +
        "Generer en kort, præcis alt-tekst på dansk til billedet. " +
        "Beskriv billedets faktiske indhold objektivt og konkret (max 125 tegn). " +
        "Undgå at starte med 'Billede af', 'Foto af' eller 'Et billede der viser'.";

    private readonly IMediaService _mediaService;
    private readonly MediaFileManager _mediaFileManager;
    private readonly IAiProvider _aiProvider;

    public AltTextController(
        IMediaService mediaService,
        MediaFileManager mediaFileManager,
        IAiProvider aiProvider)
    {
        _mediaService = mediaService;
        _mediaFileManager = mediaFileManager;
        _aiProvider = aiProvider;
    }

    /// <summary>
    /// Generate a Danish alt text suggestion for an Umbraco media item.
    /// </summary>
    [HttpPost("generate-alt-text")]
    [ProducesResponseType(typeof(AltTextResponse), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 429)]
    [ProducesResponseType(typeof(ProblemDetails), 502)]
    public async Task<IActionResult> GenerateAsync(
        [FromBody] AltTextRequest request,
        CancellationToken cancellationToken)
    {
        IMedia? media = _mediaService.GetById(request.MediaKey);
        if (media is null)
            return NotFound(new { error = $"Media item {request.MediaKey} not found." });

        string? rawFilePath = media.GetValue<string>("umbracoFile");
        if (string.IsNullOrWhiteSpace(rawFilePath))
            return BadRequest(new { error = "Media item has no file attached." });

        // umbracoFile is stored either as a plain path "/media/..." or as
        // JSON {"src":"/media/...","crops":[...],"focalPoint":{...}}.
        string filePath = ExtractSrc(rawFilePath);
        string mimeType = GetMimeType(filePath);

        if (!IsSupportedImageMimeType(mimeType))
            return BadRequest(new { error = $"Unsupported image type '{mimeType}'. Supported: JPEG, PNG, GIF, WebP." });

        byte[] imageBytes;
        try
        {
            using Stream stream = _mediaFileManager.FileSystem.OpenFile(filePath);
            using MemoryStream ms = new();
            await stream.CopyToAsync(ms, cancellationToken);
            imageBytes = ms.ToArray();
        }
        catch (Exception ex)
        {
            return Problem(
                title: "Could not read image file",
                detail: ex.Message,
                statusCode: 502);
        }

        try
        {
            AiRequest aiRequest = new(
                Prompt: $"Generer en dansk alt-tekst til dette billede. Billedets navn i Vejle Kommunes mediebibliotek er: \"{media.Name}\".",
                SystemPrompt: SystemPrompt,
                Images: [new AiImage(imageBytes, mimeType)],
                CallContext: new AiCallContext("SyncSuggestion-AltText", media.Key.ToString()));

            AiResponse response = await _aiProvider.CompleteAsync(aiRequest, cancellationToken);
            return Ok(new AltTextResponse(response.Content.Trim()));
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("429"))
        {
            return Problem(
                title: "AI quota exceeded",
                detail: "The Gemini API quota is exhausted. Try again later.",
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

    private static string ExtractSrc(string rawFilePath)
    {
        if (!rawFilePath.TrimStart().StartsWith('{'))
            return rawFilePath;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(rawFilePath);
            if (doc.RootElement.TryGetProperty("src", out JsonElement src))
                return src.GetString() ?? rawFilePath;
        }
        catch { /* not valid JSON — fall through */ }

        return rawFilePath;
    }

    private static string GetMimeType(string filePath) =>
        Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png"            => "image/png",
            ".gif"            => "image/gif",
            ".webp"           => "image/webp",
            _                 => "application/octet-stream",
        };

    private static bool IsSupportedImageMimeType(string mimeType) =>
        mimeType is "image/jpeg" or "image/png" or "image/gif" or "image/webp";
}

public sealed record AltTextRequest(
    /// <summary>Umbraco media item key (GUID from the Media section).</summary>
    Guid MediaKey);

public sealed record AltTextResponse(
    /// <summary>AI-generated Danish alt text (max ~125 chars).</summary>
    string AltText);
