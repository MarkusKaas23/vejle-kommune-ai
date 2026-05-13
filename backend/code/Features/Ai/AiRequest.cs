namespace VejleKommune.Code.Features.Ai;

public sealed record AiRequest(
    string Prompt,
    string? SystemPrompt = null,
    IReadOnlyList<AiImage>? Images = null,
    string? JsonResponseSchema = null,
    AiCallContext? CallContext = null);

public sealed record AiImage(byte[] Bytes, string MimeType);

public sealed record AiCallContext(
    string PatternName,
    string? NodeId = null);
