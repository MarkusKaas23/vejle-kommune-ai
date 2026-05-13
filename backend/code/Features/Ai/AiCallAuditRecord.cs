namespace VejleKommune.Code.Features.Ai;

public sealed record AiCallAuditRecord(
    string PatternName,
    string? NodeId,
    string ProviderName,
    string ModelName,
    int ContentLengthChars,
    long LatencyMs,
    decimal CostUsd,
    int InputTokens,
    int OutputTokens,
    DateTimeOffset TimestampUtc,
    bool Success,
    string? ErrorMessage = null);
