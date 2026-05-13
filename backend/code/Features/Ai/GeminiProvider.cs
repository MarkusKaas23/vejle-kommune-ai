using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace VejleKommune.Code.Features.Ai;

public sealed class GeminiProvider : IAiProvider
{
    private readonly HttpClient _httpClient;
    private readonly GeminiOptions _options;
    private readonly ILogger<GeminiProvider> _logger;

    public GeminiProvider(HttpClient httpClient, IOptions<GeminiOptions> options, ILogger<GeminiProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public string ProviderName => "Gemini";
    public string ModelName => _options.Model;

    public async Task<AiResponse> CompleteAsync(AiRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException(
                $"Gemini API key missing. Set {GeminiOptions.SectionName}:ApiKey in configuration.");
        }

        var url = $"{_options.Endpoint.TrimEnd('/')}/models/{_options.Model}:generateContent?key={_options.ApiKey}";
        var body = BuildRequestBody(request);

        var stopwatch = Stopwatch.StartNew();
        using var response = await _httpClient.PostAsJsonAsync(url, body, cancellationToken);
        stopwatch.Stop();

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Gemini call failed {Status}: {Error}", response.StatusCode, error);
            throw new HttpRequestException($"Gemini API returned {(int)response.StatusCode}: {error}");
        }

        var json = await response.Content.ReadFromJsonAsync<GeminiResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Gemini returned empty response body.");

        var content = ExtractText(json);
        var inputTokens = json.UsageMetadata?.PromptTokenCount ?? 0;
        var outputTokens = json.UsageMetadata?.CandidatesTokenCount ?? 0;
        var costUsd = CalculateCost(inputTokens, outputTokens);

        return new AiResponse(content, inputTokens, outputTokens, costUsd, stopwatch.Elapsed);
    }

    private JsonObject BuildRequestBody(AiRequest request)
    {
        var parts = new JsonArray { new JsonObject { ["text"] = request.Prompt } };

        if (request.Images is { Count: > 0 })
        {
            foreach (var image in request.Images)
            {
                parts.Add(new JsonObject
                {
                    ["inline_data"] = new JsonObject
                    {
                        ["mime_type"] = image.MimeType,
                        ["data"] = Convert.ToBase64String(image.Bytes),
                    },
                });
            }
        }

        var body = new JsonObject
        {
            ["contents"] = new JsonArray { new JsonObject { ["parts"] = parts } },
        };

        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            body["systemInstruction"] = new JsonObject
            {
                ["parts"] = new JsonArray { new JsonObject { ["text"] = request.SystemPrompt } },
            };
        }

        if (!string.IsNullOrWhiteSpace(request.JsonResponseSchema))
        {
            body["generationConfig"] = new JsonObject
            {
                ["responseMimeType"] = "application/json",
                ["responseSchema"] = JsonNode.Parse(request.JsonResponseSchema),
            };
        }

        return body;
    }

    private decimal CalculateCost(int inputTokens, int outputTokens)
    {
        var input = (decimal)inputTokens / 1_000_000m * _options.InputUsdPerMillionTokens;
        var output = (decimal)outputTokens / 1_000_000m * _options.OutputUsdPerMillionTokens;
        return decimal.Round(input + output, 6);
    }

    private static string ExtractText(GeminiResponse response)
    {
        var firstCandidate = response.Candidates?.FirstOrDefault();
        var firstPart = firstCandidate?.Content?.Parts?.FirstOrDefault();
        return firstPart?.Text ?? string.Empty;
    }

    private sealed record GeminiResponse(
        [property: JsonPropertyName("candidates")] List<Candidate>? Candidates,
        [property: JsonPropertyName("usageMetadata")] UsageMetadata? UsageMetadata);

    private sealed record Candidate(
        [property: JsonPropertyName("content")] Content? Content);

    private sealed record Content(
        [property: JsonPropertyName("parts")] List<Part>? Parts);

    private sealed record Part(
        [property: JsonPropertyName("text")] string? Text);

    private sealed record UsageMetadata(
        [property: JsonPropertyName("promptTokenCount")] int PromptTokenCount,
        [property: JsonPropertyName("candidatesTokenCount")] int CandidatesTokenCount);
}
