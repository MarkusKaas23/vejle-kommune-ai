namespace VejleKommune.Code.Features.Translation;

public sealed record TranslationJob(
    string JobId,
    Guid ContentKey,
    string SourceCulture,
    string TargetCulture,
    DateTimeOffset QueuedAt);

public sealed record TranslationJobState(
    string JobId,
    TranslationStatus Status,
    string? Error = null,
    DateTimeOffset? CompletedAt = null);

public enum TranslationStatus { Queued, Running, Completed, Failed }
