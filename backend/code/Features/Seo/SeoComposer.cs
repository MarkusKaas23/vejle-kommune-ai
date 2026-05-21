using Microsoft.Extensions.DependencyInjection;
using SeoToolkit.Umbraco.AI.Core.Services;
using SeoToolkit.Umbraco.Common.Core.Helpers;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;

namespace VejleKommune.Code.Features.Seo;

public sealed class SeoComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddSingleton<SchemaOrgRenderer>();
        builder.Services.AddTransient<IAIGenerationService, VejleAiGenerationService>();
        AIHelper.IsAIEnabled = true;
    }
}
