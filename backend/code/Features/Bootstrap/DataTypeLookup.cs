using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace VejleKommune.Code.Features.Bootstrap;

internal sealed record ResolvedDataTypes(
    IDataType Textstring,
    IDataType Textarea,
    IDataType RichText,
    IDataType DateTimePicker,
    IDataType TrueFalse,
    IDataType MediaPicker,
    IDataType MultiUrlPicker,
    IDataType ContentPicker,
    IDataType Tags,
    IDataType Numeric,
    IDataType ImageCropper,
    IDataType UploadField);

internal static class DataTypeLookup
{
    public static async Task<ResolvedDataTypes> ResolveAsync(IDataTypeService dataTypeService) => new(
        Textstring: await GetSingleAsync(dataTypeService, "Umbraco.TextBox"),
        Textarea: await GetSingleAsync(dataTypeService, "Umbraco.TextArea"),
        RichText: await GetSingleAsync(dataTypeService, "Umbraco.RichText"),
        DateTimePicker: await GetSingleAsync(dataTypeService, "Umbraco.DateTime"),
        TrueFalse: await GetSingleAsync(dataTypeService, "Umbraco.TrueFalse"),
        MediaPicker: await GetSingleAsync(dataTypeService, "Umbraco.MediaPicker3"),
        MultiUrlPicker: await GetSingleAsync(dataTypeService, "Umbraco.MultiUrlPicker"),
        ContentPicker: await GetSingleAsync(dataTypeService, "Umbraco.ContentPicker"),
        Tags: await GetSingleAsync(dataTypeService, "Umbraco.Tags"),
        Numeric: await GetSingleAsync(dataTypeService, "Umbraco.Integer"),
        ImageCropper: await GetSingleAsync(dataTypeService, "Umbraco.ImageCropper"),
        UploadField: await GetSingleAsync(dataTypeService, "Umbraco.UploadField"));

    private static async Task<IDataType> GetSingleAsync(IDataTypeService dataTypeService, string editorAlias)
    {
        var matches = (await dataTypeService.GetByEditorAliasAsync(editorAlias)).ToList();
        if (matches.Count == 0)
            throw new InvalidOperationException($"No built-in data type found for editor alias '{editorAlias}'. Umbraco install is incomplete.");
        return matches[0];
    }
}
