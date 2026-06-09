namespace VejleKommune.Code.Features.Accessibility;

public sealed record AccessibilityJob(
    string JobId,
    Guid ContentKey,
    string Culture,
    DateTimeOffset QueuedAt);

public sealed record AccessibilityJobState(
    string JobId,
    AccessibilityStatus Status,
    IReadOnlyList<AccessibilityFinding>? Findings = null,
    string? Error = null,
    DateTimeOffset? CompletedAt = null);

/// <summary>
/// A single actionable finding from the accessibility audit.
/// The CMS is never mutated — findings live in memory until dismissed.
/// </summary>
public sealed record AccessibilityFinding(
    /// <summary>Property alias the finding applies to, e.g. "body", "headline".</summary>
    string Field,
    /// <summary>Short human-readable description of the issue.</summary>
    string Issue,
    /// <summary>Concrete editorial suggestion for fixing the issue.</summary>
    string Suggestion,
    /// <summary>"error" | "warning" | "info"</summary>
    string Severity);

public enum AccessibilityStatus { Queued, Running, Completed, Failed }
