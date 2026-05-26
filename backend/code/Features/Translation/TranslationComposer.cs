using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Composing;

namespace VejleKommune.Code.Features.Translation;

/// <summary>
/// Phase 5 – Async Transform (translation).
/// Registers the singleton queue and the background worker.
/// </summary>
public sealed class TranslationComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddSingleton<TranslationQueue>();
        builder.Services.AddHostedService<TranslationWorker>();
    }
}
