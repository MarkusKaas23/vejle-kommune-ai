using Umbraco.Cms.Core.Models;

namespace VejleKommune.Code.Features.ContentConverter;

public sealed class ContentTypeComparer : IContentTypeComparer
{
    public ContentTypeComparisonResult Compare(
        IContentType sourceType,
        IContentType targetType,
        IContent? sourceContent = null,
        string? culture = null)
    {
        // CompositionPropertyTypes includes own + inherited composition properties
        Dictionary<string, IPropertyType> targetByAlias = targetType.CompositionPropertyTypes
            .ToDictionary(p => p.Alias, StringComparer.OrdinalIgnoreCase);

        List<PropertyMatch> matches = [];

        foreach (IPropertyType sourceProp in sourceType.CompositionPropertyTypes)
        {
            string? value = sourceContent is not null
                ? GetStringValue(sourceContent, sourceProp.Alias, culture)
                : null;

            if (targetByAlias.TryGetValue(sourceProp.Alias, out IPropertyType? targetProp))
            {
                PropertyMatchStatus status = string.Equals(
                    sourceProp.PropertyEditorAlias,
                    targetProp.PropertyEditorAlias,
                    StringComparison.OrdinalIgnoreCase)
                    ? PropertyMatchStatus.Matched
                    : PropertyMatchStatus.TypeMismatch;

                matches.Add(new PropertyMatch(
                    Alias: sourceProp.Alias,
                    Label: sourceProp.Name ?? sourceProp.Alias,
                    Status: status,
                    SourceValue: value,
                    SourceEditorAlias: sourceProp.PropertyEditorAlias,
                    TargetEditorAlias: targetProp.PropertyEditorAlias));
            }
            else
            {
                matches.Add(new PropertyMatch(
                    Alias: sourceProp.Alias,
                    Label: sourceProp.Name ?? sourceProp.Alias,
                    Status: PropertyMatchStatus.Unmatched,
                    SourceValue: value,
                    SourceEditorAlias: sourceProp.PropertyEditorAlias,
                    TargetEditorAlias: null));
            }
        }

        return new ContentTypeComparisonResult(matches);
    }

    private static string? GetStringValue(IContent content, string alias, string? culture)
    {
        object? value = content.GetValue(alias, culture);
        return value?.ToString();
    }
}
