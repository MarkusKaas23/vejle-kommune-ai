using System.ClientModel;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using OpenAI;
using OpenAI.Chat;

namespace VejleKommune.Code.Features.Ai;

/// <summary>
/// Implements <see cref="IAiProvider"/> by calling the Gemini Chat Completions API
/// via Google's OpenAI-compatible endpoint.
///
/// Why not IAIChatService: Umbraco.AI.OpenAI (M.E.AI 10.6.0) defaults to
/// OpenAIResponsesChatClient which calls POST /responses. Gemini's OpenAI-compat
/// endpoint only supports POST /chat/completions, so every call via IAIChatService
/// returns 404. Using OpenAI.Chat.ChatClient directly avoids that pipeline entirely.
///
/// Backoffice features (Copilot, Prompts) have their own registered pipeline and
/// are not affected by this class.
/// </summary>
public sealed class UmbracoAiChatProvider : IAiProvider
{
    // Gemini's OpenAI-compatible base URL — /chat/completions is appended by the SDK.
    private const string GeminiOpenAiEndpoint = "https://generativelanguage.googleapis.com/v1beta/openai/";

    private readonly ChatClient _chatClient;

    public string ProviderName => "Gemini/OpenAI-compat";
    public string ModelName    { get; }

    public UmbracoAiChatProvider(IConfiguration configuration)
    {
        var apiKey = configuration["VejleKommune:Ai:Gemini:ApiKey"]
            ?? throw new InvalidOperationException(
                "VejleKommune:Ai:Gemini:ApiKey not found. " +
                "Set it via: dotnet user-secrets set \"VejleKommune:Ai:Gemini:ApiKey\" \"YOUR-KEY\"");

        ModelName = configuration["VejleKommune:Ai:Gemini:Model"] ?? "gemini-2.5-flash";

        var client = new OpenAIClient(
            new ApiKeyCredential(apiKey),
            new OpenAIClientOptions { Endpoint = new Uri(GeminiOpenAiEndpoint) });

        _chatClient = client.GetChatClient(ModelName);
    }

    public async Task<AiResponse> CompleteAsync(
        AiRequest request,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        var messages = new List<ChatMessage>();

        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
            messages.Add(new SystemChatMessage(request.SystemPrompt));

        if (request.Images is { Count: > 0 })
        {
            var parts = new List<ChatMessageContentPart>
            {
                ChatMessageContentPart.CreateTextPart(request.Prompt),
            };

            foreach (AiImage image in request.Images)
                parts.Add(ChatMessageContentPart.CreateImagePart(
                    new BinaryData(image.Bytes), image.MimeType));

            messages.Add(new UserChatMessage(parts));
        }
        else
        {
            messages.Add(new UserChatMessage(request.Prompt));
        }

        var options = new ChatCompletionOptions();

        if (request.JsonResponseSchema is not null)
            options.ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat();

        ClientResult<ChatCompletion> result = await _chatClient.CompleteChatAsync(
            messages, options, cancellationToken);

        stopwatch.Stop();

        ChatCompletion completion = result.Value;
        string text         = completion.Content.FirstOrDefault()?.Text ?? string.Empty;
        int    inputTokens  = completion.Usage?.InputTokenCount  ?? 0;
        int    outputTokens = completion.Usage?.OutputTokenCount ?? 0;

        return new AiResponse(text, inputTokens, outputTokens, CostUsd: 0m, stopwatch.Elapsed);
    }
}
