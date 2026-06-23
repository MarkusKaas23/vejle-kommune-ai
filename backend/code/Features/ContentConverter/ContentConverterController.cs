using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace VejleKommune.Code.Features.ContentConverter;

/// <summary>
/// Content Converter — lets editors convert a content node from one document type
/// to a compatible one by mapping properties with the same alias and editor type.
///
/// GET  /umbraco/api/vejle/content-converter/compare
///   → Returns a property-by-property comparison so the editor can preview what will be copied.
///
/// POST /umbraco/api/vejle/content-converter/convert
///   → Creates a new draft node in the target document type, copying all matched properties.
/// </summary>
[ApiController]
[Route("vejle/api/content-converter")]
public sealed class ContentConverterController : ControllerBase
{
    private readonly IContentService _contentService;
    private readonly IContentTypeService _contentTypeService;
    private readonly IContentTypeComparer _comparer;
    private readonly IContentConverter _converter;

    public ContentConverterController(
        IContentService contentService,
        IContentTypeService contentTypeService,
        IContentTypeComparer comparer,
        IContentConverter converter)
    {
        _contentService = contentService;
        _contentTypeService = contentTypeService;
        _comparer = comparer;
        _converter = converter;
    }

    /// <summary>
    /// Search content nodes by name for the target-location picker.
    /// Searches across all non-element content nodes using paged descendants from root.
    /// </summary>
    [HttpGet("content-nodes")]
    [ProducesResponseType(typeof(IReadOnlyList<ContentNodeDto>), 200)]
    public IActionResult SearchContentNodes([FromQuery] string? q = null)
    {
        if (string.IsNullOrWhiteSpace(q))
            return Ok(Array.Empty<ContentNodeDto>());

        // GetPagedDescendants(-1) walks the entire content tree
        var all = _contentService.GetPagedDescendants(
            Umbraco.Cms.Core.Constants.System.Root, 0L, 300, out _);

        var results = all
            .Where(n => n.Name?.Contains(q, StringComparison.OrdinalIgnoreCase) == true)
            .Take(20)
            .Select(n => new ContentNodeDto(
                n.Key,
                n.Name ?? "Untitled",
                n.ContentType.Alias,
                n.ContentType.Icon ?? "icon-document",
                n.Path))   // raw path (IDs) — frontend can display name + level instead
            .ToList();

        return Ok(results);
    }

    /// <summary>
    /// Returns all non-element document types (alias, name, icon) for the picker dropdown.
    /// </summary>
    [HttpGet("document-types")]
    [ProducesResponseType(typeof(IReadOnlyList<DocumentTypeDto>), 200)]
    public IActionResult GetDocumentTypes()
    {
        var types = _contentTypeService.GetAll()
            .Where(ct => !ct.IsElement)
            .OrderBy(ct => ct.Name)
            .Select(ct => new DocumentTypeDto(ct.Alias, ct.Name ?? ct.Alias, ct.Icon ?? "icon-document"))
            .ToList();

        return Ok(types);
    }

    /// <summary>
    /// Preview which properties will be copied, skipped (type mismatch), or lost (unmatched).
    /// </summary>
    /// <param name="sourceId">Key (GUID) of the source content node.</param>
    /// <param name="targetAlias">Alias of the target document type.</param>
    /// <param name="culture">Culture for reading variant property values, e.g. "da-DK".</param>
    [HttpGet("compare")]
    [ProducesResponseType(typeof(CompareResponse), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public IActionResult Compare(
        [FromQuery] Guid sourceId,
        [FromQuery] string targetAlias,
        [FromQuery] string? culture = null)
    {
        IContent? source = _contentService.GetById(sourceId);
        if (source is null)
            return NotFound(new { error = $"Content node '{sourceId}' not found." });

        IContentType? sourceType = _contentTypeService.Get(source.ContentType.Key);
        if (sourceType is null)
            return NotFound(new { error = $"Document type '{source.ContentType.Alias}' not found." });

        IContentType? targetType = _contentTypeService.Get(targetAlias);
        if (targetType is null)
            return NotFound(new { error = $"Target document type '{targetAlias}' not found." });

        ContentTypeComparisonResult result = _comparer.Compare(sourceType, targetType, source, culture);

        IReadOnlyList<PropertyMatchDto> properties = result.Properties
            .Select(p => new PropertyMatchDto(
                Alias: p.Alias,
                Label: p.Label,
                Status: p.Status.ToString(),
                SourceValue: p.SourceValue,
                SourceEditorAlias: p.SourceEditorAlias,
                TargetEditorAlias: p.TargetEditorAlias))
            .ToList();

        CompareResponse response = new(
            SourceNode: new NodeSummary(source.Key, source.Name ?? string.Empty, source.ContentType.Alias),
            TargetDocumentType: new DocTypeSummary(targetType.Alias, targetType.Name),
            Properties: properties,
            MatchedCount: properties.Count(p => p.Status == "Matched"),
            TypeMismatchCount: properties.Count(p => p.Status == "TypeMismatch"),
            UnmatchedCount: properties.Count(p => p.Status == "Unmatched"));

        return Ok(response);
    }

    /// <summary>
    /// Convert a content node to a new document type. Creates a new draft node — does not publish.
    /// </summary>
    [HttpPost("convert")]
    [IgnoreAntiforgeryToken]
    [ProducesResponseType(typeof(ConvertResponse), 201)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> ConvertAsync(
        [FromBody] ConvertRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            ConversionResult result = await _converter.ConvertAsync(
                new ConversionRequest(
                    SourceKey: request.SourceId,
                    TargetDocTypeAlias: request.TargetAlias,
                    TargetParentKey: request.TargetParentKey,
                    NewNodeName: request.NewNodeName,
                    Culture: request.Culture),
                cancellationToken);

            return CreatedAtAction(
                nameof(Compare),
                new { sourceId = result.NewNodeKey, targetAlias = request.TargetAlias },
                new ConvertResponse(
                    NewNodeKey: result.NewNodeKey,
                    NewNodeName: result.NewNodeName,
                    PropertiesCopied: result.PropertiesCopied,
                    PropertiesSkipped: result.PropertiesSkipped));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    // ── Batch endpoints ─────────────────────────────────────────────────────────

    /// <summary>
    /// Preview a batch conversion: returns one merged property table across all source nodes.
    /// The status of each property is the worst-case across all source types
    /// (Unmatched > TypeMismatch > Matched), so the editor sees maximum risk up front.
    /// </summary>
    /// <param name="sourceIds">Comma-separated GUIDs of the source content nodes.</param>
    /// <param name="targetAlias">Alias of the target document type.</param>
    /// <param name="culture">Culture for reading variant property values, e.g. "da-DK".</param>
    [HttpGet("batch-compare")]
    [ProducesResponseType(typeof(BatchCompareResponse), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public IActionResult BatchCompare(
        [FromQuery] string sourceIds,
        [FromQuery] string targetAlias,
        [FromQuery] string? culture = null)
    {
        IContentType? targetType = _contentTypeService.Get(targetAlias);
        if (targetType is null)
            return NotFound(new { error = $"Target document type '{targetAlias}' not found." });

        List<SourceTypeSummaryDto> sourceTypeSummaries = [];

        // Global (merged) worst-case across all source nodes
        Dictionary<string, PropertyMatchStatus> worstStatus      = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, PropertyMatch>        representativeMatch = new(StringComparer.OrdinalIgnoreCase);

        // Per-source-document-type accumulators
        // key = sourceType.Alias
        Dictionary<string, (IContentType Type,
            List<SourceTypeSummaryDto> Nodes,
            Dictionary<string, PropertyMatchStatus> Worst,
            Dictionary<string, PropertyMatch> Rep)> byType = new(StringComparer.OrdinalIgnoreCase);

        List<PlacementWarningDto>    placementWarnings    = [];
        List<SourceNodeBreakdown>    sourceNodeBreakdowns = [];

        foreach (string rawId in sourceIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Guid.TryParse(rawId, out Guid sourceId))
                return BadRequest(new { error = $"Invalid GUID: '{rawId}'." });

            IContent? source = _contentService.GetById(sourceId);
            if (source is null)
                return NotFound(new { error = $"Content node '{sourceId}' not found." });

            IContentType? sourceType = _contentTypeService.Get(source.ContentType.Key);
            if (sourceType is null)
                return NotFound(new { error = $"Document type '{source.ContentType.Alias}' not found." });

            ContentTypeComparisonResult result = _comparer.Compare(sourceType, targetType, source, culture);

            IContent? sourceParent = _contentService.GetParent(source);
            var summary = new SourceTypeSummaryDto(
                NodeKey:           source.Key,
                NodeName:          source.Name ?? source.CultureInfos.Values.Select(ci => ci.Name).FirstOrDefault() ?? "Untitled",
                DocumentTypeAlias: sourceType.Alias,
                ParentKey:         sourceParent?.Key,
                ParentName:        sourceParent?.Name ?? "(root)");
            sourceTypeSummaries.Add(summary);

            // ── Global worst-case merge ──────────────────────────────────────
            foreach (PropertyMatch match in result.Properties)
            {
                if (!worstStatus.TryGetValue(match.Alias, out PropertyMatchStatus existing))
                {
                    worstStatus[match.Alias]         = match.Status;
                    representativeMatch[match.Alias] = match;
                }
                else if ((int)match.Status > (int)existing)
                {
                    worstStatus[match.Alias]         = match.Status;
                    representativeMatch[match.Alias] = match;
                }
            }

            // ── Per-node breakdown ───────────────────────────────────────────
            List<PropertyMatchDto> nodeProps = result.Properties
                .Select(p => new PropertyMatchDto(
                    Alias: p.Alias, Label: p.Label,
                    Status: p.Status.ToString(),
                    SourceValue: p.SourceValue,
                    SourceEditorAlias: p.SourceEditorAlias,
                    TargetEditorAlias: p.TargetEditorAlias))
                .OrderBy(p => p.Alias)
                .ToList();

            sourceNodeBreakdowns.Add(new SourceNodeBreakdown(
                NodeKey:           source.Key,
                NodeName:          summary.NodeName,
                DocumentTypeAlias: sourceType.Alias,
                Properties:        nodeProps,
                MatchedCount:      nodeProps.Count(p => p.Status == "Matched"),
                TypeMismatchCount: nodeProps.Count(p => p.Status == "TypeMismatch"),
                UnmatchedCount:    nodeProps.Count(p => p.Status == "Unmatched")));

            // ── Per-type worst-case merge ────────────────────────────────────
            if (!byType.TryGetValue(sourceType.Alias, out var td))
            {
                td = (sourceType, [], new(StringComparer.OrdinalIgnoreCase), new(StringComparer.OrdinalIgnoreCase));
                byType[sourceType.Alias] = td;
            }
            td.Nodes.Add(summary);

            foreach (PropertyMatch match in result.Properties)
            {
                if (!td.Worst.TryGetValue(match.Alias, out PropertyMatchStatus typeExisting))
                {
                    td.Worst[match.Alias] = match.Status;
                    td.Rep[match.Alias]   = match;
                }
                else if ((int)match.Status > (int)typeExisting)
                {
                    td.Worst[match.Alias] = match.Status;
                    td.Rep[match.Alias]   = match;
                }
            }

            // ── Placement validation ─────────────────────────────────────────
            // Check whether the target document type is an allowed child at the
            // parent of each selected node.
            IContent? parent = _contentService.GetParent(source);
            if (parent is not null)
            {
                IContentType? parentType = _contentTypeService.Get(parent.ContentType.Key);
                IEnumerable<ContentTypeSort>? allowedChildren = parentType?.AllowedContentTypes;

                if (allowedChildren is not null && allowedChildren.Any())
                {
                    bool isAllowed = allowedChildren.Any(a =>
                        string.Equals(a.Alias, targetAlias, StringComparison.OrdinalIgnoreCase));

                    if (!isAllowed)
                    {
                        placementWarnings.Add(new PlacementWarningDto(
                            NodeKey:          source.Key,
                            NodeName:         source.Name ?? "Untitled",
                            ParentName:       parent.Name ?? "Untitled",
                            ParentDocTypeName: parentType?.Name ?? parentType?.Alias ?? "unknown"));
                    }
                }
            }
        }

        IReadOnlyList<PropertyMatchDto> mergedProperties = representativeMatch.Values
            .Select(p => new PropertyMatchDto(
                Alias: p.Alias,
                Label: p.Label,
                Status: worstStatus[p.Alias].ToString(),
                SourceValue: p.SourceValue,
                SourceEditorAlias: p.SourceEditorAlias,
                TargetEditorAlias: p.TargetEditorAlias))
            .OrderBy(p => p.Alias)
            .ToList();

        IReadOnlyList<SourceTypeBreakdown> sourceTypeBreakdowns = byType.Values.Select(td =>
        {
            List<PropertyMatchDto> props = td.Rep.Values
                .Select(p => new PropertyMatchDto(
                    Alias: p.Alias, Label: p.Label,
                    Status: td.Worst[p.Alias].ToString(),
                    SourceValue: p.SourceValue,
                    SourceEditorAlias: p.SourceEditorAlias,
                    TargetEditorAlias: p.TargetEditorAlias))
                .OrderBy(p => p.Alias)
                .ToList();

            return new SourceTypeBreakdown(
                DocumentTypeAlias: td.Type.Alias,
                DocumentTypeName:  td.Type.Name ?? td.Type.Alias,
                NodeCount:         td.Nodes.Count,
                Properties:        props,
                MatchedCount:      props.Count(p => p.Status == "Matched"),
                TypeMismatchCount: props.Count(p => p.Status == "TypeMismatch"),
                UnmatchedCount:    props.Count(p => p.Status == "Unmatched"));
        }).ToList();

        return Ok(new BatchCompareResponse(
            SourceNodes:           sourceTypeSummaries,
            TargetDocumentType:    new DocTypeSummary(targetType.Alias, targetType.Name),
            Properties:            mergedProperties,
            MatchedCount:          mergedProperties.Count(p => p.Status == "Matched"),
            TypeMismatchCount:     mergedProperties.Count(p => p.Status == "TypeMismatch"),
            UnmatchedCount:        mergedProperties.Count(p => p.Status == "Unmatched"),
            SourceTypeBreakdowns:  sourceTypeBreakdowns,
            SourceNodeBreakdowns:  sourceNodeBreakdowns,
            PlacementWarnings:     placementWarnings));
    }

    /// <summary>
    /// Convert multiple content nodes to a new document type in one request.
    /// Each converted node is placed next to its original (same parent).
    /// Originals are left untouched (copy, not move).
    /// </summary>
    [HttpPost("batch-convert")]
    [IgnoreAntiforgeryToken]
    [ProducesResponseType(typeof(BatchConvertResponse), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    public async Task<IActionResult> BatchConvertAsync(
        [FromBody] BatchConvertRequest request,
        CancellationToken cancellationToken)
    {
        List<BatchConvertResultDto> results = [];
        List<BatchConvertFailureDto> failures = [];

        foreach (Guid sourceId in request.SourceIds)
        {
            try
            {
                ConversionResult result = await _converter.ConvertAsync(
                    new ConversionRequest(
                        SourceKey: sourceId,
                        TargetDocTypeAlias: request.TargetAlias,
                        TargetParentKey: request.TargetParentKey ?? Guid.Empty,
                        NewNodeName: null,
                        Culture: request.Culture,
                        UseSourceParent: request.TargetParentKey is null),
                    cancellationToken);

                results.Add(new BatchConvertResultDto(
                    SourceKey: sourceId,
                    NewNodeKey: result.NewNodeKey,
                    NewNodeName: result.NewNodeName,
                    PropertiesCopied: result.PropertiesCopied,
                    PropertiesSkipped: result.PropertiesSkipped));
            }
            catch (Exception ex)
            {
                failures.Add(new BatchConvertFailureDto(SourceKey: sourceId, Error: ex.Message));
            }
        }

        return Ok(new BatchConvertResponse(Results: results, Failures: failures));
    }
}

// ── Request / response records ──────────────────────────────────────────────

public sealed record ConvertRequest(
    /// <summary>Key (GUID) of the source content node.</summary>
    Guid SourceId,
    /// <summary>Alias of the target document type to convert into.</summary>
    string TargetAlias,
    /// <summary>Key (GUID) of the parent node under which the new node is created.</summary>
    Guid TargetParentKey,
    /// <summary>Optional name for the new node. Defaults to the source node name.</summary>
    string? NewNodeName,
    /// <summary>Culture for copying variant properties, e.g. "da-DK". Null for invariant only.</summary>
    string? Culture);

public sealed record ConvertResponse(
    Guid NewNodeKey,
    string NewNodeName,
    int PropertiesCopied,
    int PropertiesSkipped);

public sealed record CompareResponse(
    NodeSummary SourceNode,
    DocTypeSummary TargetDocumentType,
    IReadOnlyList<PropertyMatchDto> Properties,
    int MatchedCount,
    int TypeMismatchCount,
    int UnmatchedCount);

public sealed record NodeSummary(Guid Key, string Name, string DocumentTypeAlias);

public sealed record DocTypeSummary(string Alias, string Name);

public sealed record PropertyMatchDto(
    string Alias,
    string Label,
    /// <summary>"Matched", "TypeMismatch", or "Unmatched".</summary>
    string Status,
    string? SourceValue,
    string SourceEditorAlias,
    string? TargetEditorAlias);

// ── Batch request / response records ────────────────────────────────────────

public sealed record BatchConvertRequest(
    /// <summary>Keys of the source content nodes to convert.</summary>
    IReadOnlyList<Guid> SourceIds,
    /// <summary>Alias of the target document type to convert into.</summary>
    string TargetAlias,
    /// <summary>Culture for copying variant properties, e.g. "da-DK".</summary>
    string? Culture,
    /// <summary>
    /// Optional target parent node key. When set, all converted nodes are placed
    /// under this parent instead of next to their originals.
    /// </summary>
    Guid? TargetParentKey = null);

public sealed record BatchCompareResponse(
    IReadOnlyList<SourceTypeSummaryDto> SourceNodes,
    DocTypeSummary TargetDocumentType,
    IReadOnlyList<PropertyMatchDto> Properties,
    int MatchedCount,
    int TypeMismatchCount,
    int UnmatchedCount,
    /// <summary>Per-source-document-type breakdowns (useful for mixed-type selections).</summary>
    IReadOnlyList<SourceTypeBreakdown> SourceTypeBreakdowns,
    /// <summary>Per-node comparison result — one entry per selected node.</summary>
    IReadOnlyList<SourceNodeBreakdown> SourceNodeBreakdowns,
    /// <summary>Nodes whose target type is not an allowed child at their current parent.</summary>
    IReadOnlyList<PlacementWarningDto> PlacementWarnings);

public sealed record SourceNodeBreakdown(
    Guid   NodeKey,
    string NodeName,
    string DocumentTypeAlias,
    IReadOnlyList<PropertyMatchDto> Properties,
    int MatchedCount,
    int TypeMismatchCount,
    int UnmatchedCount);

public sealed record SourceTypeBreakdown(
    string DocumentTypeAlias,
    string DocumentTypeName,
    int NodeCount,
    IReadOnlyList<PropertyMatchDto> Properties,
    int MatchedCount,
    int TypeMismatchCount,
    int UnmatchedCount);

public sealed record PlacementWarningDto(
    Guid   NodeKey,
    string NodeName,
    string ParentName,
    string ParentDocTypeName);

public sealed record BatchConvertResponse(
    IReadOnlyList<BatchConvertResultDto> Results,
    IReadOnlyList<BatchConvertFailureDto> Failures);

public sealed record SourceTypeSummaryDto(
    Guid   NodeKey,
    string NodeName,
    string DocumentTypeAlias,
    Guid?  ParentKey,
    string ParentName);

public sealed record BatchConvertResultDto(
    Guid SourceKey,
    Guid NewNodeKey,
    string NewNodeName,
    int PropertiesCopied,
    int PropertiesSkipped);

public sealed record BatchConvertFailureDto(
    Guid SourceKey,
    string Error);

public sealed record DocumentTypeDto(
    string Alias,
    string Name,
    string Icon);

public sealed record ContentNodeDto(
    Guid   Key,
    string Name,
    string DocTypeAlias,
    string Icon,
    string Path);
