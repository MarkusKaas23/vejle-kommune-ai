using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace VejleKommune.Code.Features.ContentConverter;

public sealed class ContentConverter : IContentConverter
{
    private readonly IContentService _contentService;
    private readonly IContentTypeService _contentTypeService;
    private readonly IContentTypeComparer _comparer;

    public ContentConverter(
        IContentService contentService,
        IContentTypeService contentTypeService,
        IContentTypeComparer comparer)
    {
        _contentService = contentService;
        _contentTypeService = contentTypeService;
        _comparer = comparer;
    }

    public Task<ConversionResult> ConvertAsync(ConversionRequest request, CancellationToken cancellationToken = default)
    {
        IContent? source = _contentService.GetById(request.SourceKey);
        if (source is null)
            throw new InvalidOperationException($"Source content node '{request.SourceKey}' not found.");

        IContentType? sourceType = _contentTypeService.Get(source.ContentType.Key);
        if (sourceType is null)
            throw new InvalidOperationException($"Source document type '{source.ContentType.Alias}' not found.");

        IContentType? targetType = _contentTypeService.Get(request.TargetDocTypeAlias);
        if (targetType is null)
            throw new InvalidOperationException($"Target document type '{request.TargetDocTypeAlias}' not found.");

        ContentTypeComparisonResult comparison = _comparer.Compare(sourceType, targetType, source, request.Culture);

        // UseSourceParent → place the new node next to the source (batch convert behaviour).
        // Otherwise fall back to the explicit TargetParentKey, or -1 for content root.
        int parentIntId;
        if (request.UseSourceParent)
        {
            parentIntId = source.ParentId;
        }
        else if (request.TargetParentKey != Guid.Empty)
        {
            IContent? parent = _contentService.GetById(request.TargetParentKey);
            if (parent is null)
                throw new InvalidOperationException($"Target parent node '{request.TargetParentKey}' not found.");
            parentIntId = parent.Id;
        }
        else
        {
            parentIntId = -1;
        }

        // source.Name is "" (not null) for culture-variant nodes — ?? won't help.
        // Fall back through: explicit override → invariant name → any culture name → "Untitled".
        string newName = request.NewNodeName
            ?? (string.IsNullOrEmpty(source.Name) ? null : source.Name)
            ?? source.CultureInfos.Values
                   .Select(ci => ci.Name)
                   .FirstOrDefault(n => !string.IsNullOrEmpty(n))
            ?? "Untitled";

        IContent newNode = _contentService.Create(newName, parentIntId, request.TargetDocTypeAlias);

        // Culture-variant document types require SetCultureName; the invariant Name alone is
        // rejected by Umbraco with "Cannot save content with an empty name."
        if (targetType.Variations.HasFlag(ContentVariation.Culture))
        {
            IEnumerable<string> cultures = request.Culture is not null
                ? [request.Culture]
                : source.CultureInfos.Keys;

            foreach (string culture in cultures)
            {
                string cultureName = request.NewNodeName
                    ?? source.GetCultureName(culture)
                    ?? newName;
                newNode.SetCultureName(cultureName, culture);
            }
        }

        int copied = 0;
        int skipped = 0;

        foreach (PropertyMatch match in comparison.Properties)
        {
            if (match.Status != PropertyMatchStatus.Matched)
            {
                skipped++;
                continue;
            }

            // Try culture-specific value first (variant properties), then fall back
            // to invariant (null culture) for properties shared across cultures.
            object? value = source.GetValue(match.Alias, request.Culture)
                         ?? source.GetValue(match.Alias, null);

            if (value is null)
            {
                skipped++;
                continue;
            }

            // Mirror the same culture resolution on the target node.
            string? effectiveCulture = source.GetValue(match.Alias, request.Culture) is not null
                ? request.Culture
                : null;

            newNode.SetValue(match.Alias, value, effectiveCulture);
            copied++;
        }

        _contentService.Save(newNode);

        return Task.FromResult(new ConversionResult(
            NewNodeKey: newNode.Key,
            NewNodeName: newNode.Name ?? newName,
            PropertiesCopied: copied,
            PropertiesSkipped: skipped));
    }
}
