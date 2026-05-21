namespace VejleKommune.Code.Features.Ai;

public sealed class GeminiOptions
{
    public const string SectionName = "VejleKommune:Ai:Gemini";

    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gemini-2.5-flash";
    public string Endpoint { get; set; } = "https://generativelanguage.googleapis.com/v1";

    public decimal InputUsdPerMillionTokens { get; set; } = 0.10m;
    public decimal OutputUsdPerMillionTokens { get; set; } = 0.40m;
}
