using SeoToolkit.Umbraco.AI.Core.Services;
using VejleKommune.Code.Features.Ai;

namespace VejleKommune.Code.Features.Seo;

public sealed class VejleAiGenerationService : IAIGenerationService
{
    private const string SystemPrompt = "Du er en SEO-ekspert for Vejle Kommune. Skriv dansk SEO-metadata for denne side.";

    private readonly IAiProvider _aiProvider;

    public VejleAiGenerationService(IAiProvider aiProvider)
    {
        _aiProvider = aiProvider;
    }

    public async Task<string> GenerateRawResponseAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        AiRequest request = new(
            Prompt: userPrompt,
            SystemPrompt: SystemPrompt,
            CallContext: new AiCallContext("SyncSuggestion-SeoMeta"));

        AiResponse response = await _aiProvider.CompleteAsync(request, cancellationToken);
        return response.Content;
    }
}
