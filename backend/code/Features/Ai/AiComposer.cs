using Microsoft.Extensions.DependencyInjection;
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

        builder.Services.AddHttpClient<IAiProvider, GeminiProvider>();
    }
}
