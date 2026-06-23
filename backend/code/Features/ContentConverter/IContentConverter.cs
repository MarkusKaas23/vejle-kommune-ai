namespace VejleKommune.Code.Features.ContentConverter;

/// <summary>
/// Creates a new content node by copying matched property values
/// from a source node into a target document type.
/// </summary>
public interface IContentConverter
{
    Task<ConversionResult> ConvertAsync(ConversionRequest request, CancellationToken cancellationToken = default);
}

public sealed record ConversionRequest(
    /// <summary>Key of the source content node to convert from.</summary>
    Guid SourceKey,
    /// <summary>Alias of the target document type to convert into.</summary>
    string TargetDocTypeAlias,
    /// <summary>Key (GUID) of the parent node under which the new node is created. Pass Guid.Empty to place at root.</summary>
    Guid TargetParentKey,
    /// <summary>Name for the new content node. Defaults to the source node name.</summary>
    string? NewNodeName,
    /// <summary>Culture to read/write culture-variant property values.</summary>
    string? Culture,
    /// <summary>
    /// When true the new node is placed next to the source node (same parent),
    /// ignoring <see cref="TargetParentKey"/>. Used by batch convert so every
    /// converted node lands next to its original.
    /// </summary>
    bool UseSourceParent = false);

public sealed record ConversionResult(
    Guid NewNodeKey,
    string NewNodeName,
    int PropertiesCopied,
    int PropertiesSkipped);
