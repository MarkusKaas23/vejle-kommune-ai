namespace VejleKommune.Code.Features.ContentConverter;

public enum PropertyMatchStatus
{
    /// <summary>Same alias and same property editor — value will be copied.</summary>
    Matched,

    /// <summary>Same alias but different property editor — value will NOT be copied.</summary>
    TypeMismatch,

    /// <summary>Property exists on source but has no alias match on the target type.</summary>
    Unmatched,
}

public sealed record PropertyMatch(
    string Alias,
    string Label,
    PropertyMatchStatus Status,
    string? SourceValue,
    string SourceEditorAlias,
    string? TargetEditorAlias);

public sealed record ContentTypeComparisonResult(
    IReadOnlyList<PropertyMatch> Properties);
