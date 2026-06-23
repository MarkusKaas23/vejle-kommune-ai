using Umbraco.Cms.Core.Models;

namespace VejleKommune.Code.Features.ContentConverter;

/// <summary>
/// Compares two Umbraco content types and maps their properties into
/// matched / type-mismatch / unmatched buckets.
/// Optionally reads current values from a source content node for preview.
/// </summary>
public interface IContentTypeComparer
{
    ContentTypeComparisonResult Compare(
        IContentType sourceType,
        IContentType targetType,
        IContent? sourceContent = null,
        string? culture = null);
}
