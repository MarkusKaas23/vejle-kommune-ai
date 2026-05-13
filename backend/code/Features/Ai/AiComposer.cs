using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;

namespace VejleKommune.Code.Features.Ai;

public sealed class AiComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services
            .AddOptions<GeminiOptions>()
            .BindConfiguration(GeminiOptions.SectionName);

        builder.Services.TryAddSingletonTimeProvider();
        builder.Services.AddSingleton<IAiCallAuditSink, LoggerAiCallAuditSink>();
        builder.Services.AddHttpClient<GeminiProvider>();

        builder.Services.AddTransient<IAiProvider>(sp =>
            new AuditingAiProvider(
                sp.GetRequiredService<GeminiProvider>(),
                sp.GetRequiredService<IAiCallAuditSink>(),
                sp.GetRequiredService<TimeProvider>()));
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
