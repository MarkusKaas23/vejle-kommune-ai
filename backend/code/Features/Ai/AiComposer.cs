using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;

namespace VejleKommune.Code.Features.Ai;

/// <summary>
/// Registers the AI provider layer.
///
/// <see cref="UmbracoAiChatProvider"/> calls Google's Gemini Chat Completions API
/// directly (bypassing Umbraco.AI's IAIChatService pipeline, which routes through
/// the Responses API that Gemini does not support).
///
/// Umbraco.AI's own backoffice services (Copilot, Prompts, analytics) are
/// registered automatically by Umbraco.AI.Startup's IComposer and are unaffected.
/// </summary>
public sealed class AiComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.TryAddSingletonTimeProvider();

        // Bind our internal IAiProvider to the Umbraco.AI-backed adapter.
        // The adapter reads the default chat profile configured in
        // backoffice → AI → Settings.
        builder.Services.AddTransient<IAiProvider, UmbracoAiChatProvider>();
    }
}

internal static class TimeProviderRegistration
{
    public static IServiceCollection TryAddSingletonTimeProvider(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        return services;
    }
}
