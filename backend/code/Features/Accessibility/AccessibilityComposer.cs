using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;

namespace VejleKommune.Code.Features.Accessibility;

/// <summary>
/// Phase 6 – Async Analyze (accessibility audit).
/// Registers the singleton findings store and the background worker.
/// </summary>
public sealed class AccessibilityComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddSingleton<AccessibilityQueue>();
        builder.Services.AddHostedService<AccessibilityWorker>();
    }
}
