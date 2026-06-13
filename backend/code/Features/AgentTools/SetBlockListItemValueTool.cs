using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Umbraco.AI.Core.Tools;
using Umbraco.AI.Extensions;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace VejleKommune.Code.Features.AgentTools;

/// <summary>
/// Limbo custom Umbraco.AI tool — closes the gap identified in ADR-0013.
///
/// The built-in set_value tool replaces an entire property value. It cannot
/// target a specific item at index N inside a Block List's contentData array.
/// This tool reads the current block list JSON, mutates the named field on the
/// item at the given index, and writes the full JSON back — giving the Copilot
/// agent block-item-level write access.
///
/// IsDestructive = true: the agent must surface a confirmation to the editor
/// before calling this tool on shared/global element nodes.
/// </summary>
[AITool(
    "set_block_list_item_value",
    "Set Block List Item Value",
    IsDestructive = true)]
public sealed class SetBlockListItemValueTool : AIToolBase<SetBlockListItemValueArgs>
{
    private readonly IContentService _contentService;
    private readonly ILogger<SetBlockListItemValueTool> _logger;

    public SetBlockListItemValueTool(
        IContentService contentService,
        ILogger<SetBlockListItemValueTool> logger)
    {
        _contentService = contentService;
        _logger = logger;
    }

    public override string Description =>
        "Updates a single field on one item inside a Block List property. " +
        "Use this when set_value fails because the target is a nested block item. " +
        "Provide the node key of the content node that OWNS the block list (may differ " +
        "from the current page for global/shared elements — warn the editor in that case). " +
        "itemIndex is zero-based: 0 = first item, 1 = second, and so on.";

    protected override async Task<object> ExecuteAsync(
        SetBlockListItemValueArgs args,
        CancellationToken cancellationToken = default)
    {
        // ── 1. Load the content node ──────────────────────────────────────────

        IContent? content = _contentService.GetById(args.NodeKey);
        if (content is null)
            return new { success = false, error = $"Content node '{args.NodeKey}' not found." };

        // ── 2. Read the block list property ──────────────────────────────────

        IProperty? property = content.Properties
            .FirstOrDefault(p => p.Alias.Equals(args.PropertyAlias, StringComparison.OrdinalIgnoreCase));
        if (property is null)
            return new { success = false, error = $"Property '{args.PropertyAlias}' not found on '{content.Name}'." };

        string? raw = property.GetValue()?.ToString();
        if (string.IsNullOrWhiteSpace(raw))
            return new { success = false, error = $"Property '{args.PropertyAlias}' is empty." };

        // ── 3. Parse and mutate the block list JSON ───────────────────────────

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(raw);
        }
        catch (JsonException ex)
        {
            return new { success = false, error = $"Property value is not valid JSON: {ex.Message}" };
        }

        if (root is not JsonObject rootObj)
            return new { success = false, error = "Block list JSON root is not an object." };

        JsonArray? contentData = rootObj["contentData"]?.AsArray();
        if (contentData is null)
            return new { success = false, error = "Block list JSON has no 'contentData' array." };

        if (args.ItemIndex < 0 || args.ItemIndex >= contentData.Count)
            return new
            {
                success = false,
                error = $"itemIndex {args.ItemIndex} is out of range — the block list has {contentData.Count} item(s) (0-based)."
            };

        JsonObject? item = contentData[args.ItemIndex]?.AsObject();
        if (item is null)
            return new { success = false, error = $"Block item at index {args.ItemIndex} is not a JSON object." };

        // Store the old value for logging, then replace it.
        string? oldValue = item[args.FieldAlias]?.ToString();
        item[args.FieldAlias] = JsonValue.Create(args.Value);

        // ── 4. Write the mutated JSON back to the property ───────────────────

        string updated = rootObj.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        property.SetValue(updated);
        _contentService.Save(content);

        _logger.LogInformation(
            "SetBlockListItemValue: node={NodeKey} ({NodeName}) property={Property} " +
            "index={Index} field={Field} old={Old} new={New}",
            args.NodeKey,
            content.Name,
            args.PropertyAlias,
            args.ItemIndex,
            args.FieldAlias,
            oldValue,
            args.Value);

        return new
        {
            success = true,
            message  = $"Updated '{args.FieldAlias}' on block item {args.ItemIndex} of '{content.Name}'.",
            nodeName = content.Name,
            nodeKey  = content.Key,
            propertyAlias = args.PropertyAlias,
            itemIndex     = args.ItemIndex,
            fieldAlias    = args.FieldAlias,
            oldValue,
            newValue = args.Value,
        };
    }
}

public sealed record SetBlockListItemValueArgs(
    [property: Description(
        "GUID key of the Umbraco content node that owns the block list property. " +
        "Get this from get_umbraco_content. " +
        "If this node is different from the current page, the block is shared/global content " +
        "— you MUST warn the editor that the change will affect every page referencing it.")]
    Guid NodeKey,

    [property: Description(
        "Property alias of the Block List on the content node, e.g. 'elementer', 'items', 'modules'.")]
    string PropertyAlias,

    [property: Description(
        "Zero-based index of the block item to update. 0 = first item, 1 = second, etc. " +
        "Use get_umbraco_content to list the items and confirm the correct index before writing.")]
    int ItemIndex,

    [property: Description(
        "The field alias within the block item's element type to update, e.g. 'title', 'body', 'heading'.")]
    string FieldAlias,

    [property: Description(
        "The new plain-text value for the field. " +
        "For Rich Text fields, wrap in a <p> tag, e.g. '<p>New body text.</p>'.")]
    string Value);
