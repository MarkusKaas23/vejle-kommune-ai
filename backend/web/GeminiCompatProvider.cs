using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;
using Umbraco.AI.Core.Models;
using Umbraco.AI.Core.Providers;
using Umbraco.AI.Extensions;
using Umbraco.AI.OpenAI;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;

namespace VejleKommune;

// ─────────────────────────────────────────────────────────────────────────────
// Composer — swap the built-in OpenAI provider for our Gemini-compatible one
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Replaces Umbraco.AI.OpenAI's <see cref="OpenAIProvider"/> with
/// <see cref="GeminiCompatProvider"/>, which creates an
/// <see cref="OpenAI.Chat.ChatClient"/> (Chat Completions API) instead of the
/// default <c>OpenAIResponsesChatClient</c> (Responses API).
///
/// Gemini's OpenAI-compatible endpoint only supports <c>POST /chat/completions</c>.
/// The Responses API (<c>POST /responses</c>) returns 404.
/// </summary>
public sealed class GeminiCompatComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder) =>
        builder.AIProviders()
               .Exclude<OpenAIProvider>()
               .Add<GeminiCompatProvider>();
}

// ─────────────────────────────────────────────────────────────────────────────
// Provider — same id/settings type as built-in, so DB needs no changes
// ─────────────────────────────────────────────────────────────────────────────

[AIProvider("openai", "OpenAI / Gemini-compat")]
public sealed class GeminiCompatProvider : AIProviderBase<OpenAIProviderSettings>
{
    public GeminiCompatProvider(IAIProviderInfrastructure infrastructure)
        : base(infrastructure)
    {
        WithCapability<GeminiCompatChatCapability>();
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Chat capability — the only part that matters
// ─────────────────────────────────────────────────────────────────────────────

public sealed class GeminiCompatChatCapability : AIChatCapabilityBase<OpenAIProviderSettings>
{
    public GeminiCompatChatCapability(IAIProvider provider) : base(provider) { }

    // ── Client creation — THE KEY FIX ─────────────────────────────────────────
    // GetChatClient(modelId).AsChatClient() → OpenAIChatCompletionsChatClient → POST /chat/completions ✓
    // AsChatClient()                         → OpenAIResponsesChatClient       → POST /responses      ✗ (404 on Gemini)

    protected override IChatClient CreateClient(OpenAIProviderSettings settings, string? modelId) =>
        BuildOpenAIClient(settings).GetChatClient(modelId ?? "gemini-2.5-flash").AsIChatClient();

    // ── Model list — static Gemini chat models ─────────────────────────────────
    // Avoids calling GET /models which returns embeddings, vision-only models, etc.

    protected override Task<IReadOnlyList<AIModelDescriptor>> GetModelsAsync(
        OpenAIProviderSettings settings,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<AIModelDescriptor> models =
        [
            Descriptor("gemini-2.5-flash", "Gemini 2.5 Flash"),
            Descriptor("gemini-2.5-pro",   "Gemini 2.5 Pro"),
            Descriptor("gemini-2.0-flash", "Gemini 2.0 Flash"),
            Descriptor("gemini-1.5-flash", "Gemini 1.5 Flash"),
            Descriptor("gemini-1.5-pro",   "Gemini 1.5 Pro"),
        ];
        return Task.FromResult(models);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static AIModelDescriptor Descriptor(string modelId, string displayName) =>
        new(new AIModelRef("openai", modelId), displayName, new Dictionary<string, string>());

    private static OpenAIClient BuildOpenAIClient(OpenAIProviderSettings settings)
    {
        var endpoint = string.IsNullOrWhiteSpace(settings.Endpoint)
            ? "https://generativelanguage.googleapis.com/v1beta/openai/"
            : settings.Endpoint;

        return new OpenAIClient(
            new ApiKeyCredential(settings.ApiKey ?? string.Empty),
            new OpenAIClientOptions { Endpoint = new Uri(endpoint) });
    }
}
