using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;

namespace VejleKommune.Code.Features.DocumentIngestion;

/// <summary>
/// Phase 7 – Generative Pipeline (Document → Content).
/// Registers the singleton job queue and the background worker.
/// </summary>
public sealed class DocumentIngestionComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddSingleton<DocumentIngestionQueue>();
        builder.Services.AddHostedService<DocumentIngestionWorker>();
    }
}
