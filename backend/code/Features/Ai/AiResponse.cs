namespace VejleKommune.Code.Features.Ai;

public sealed record AiResponse(
    string Content,
    int InputTokens,
    int OutputTokens,
    decimal CostUsd,
    TimeSpan Latency);
