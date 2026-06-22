using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;

namespace VejleKommune.Code.Features.ContentConverter;

public sealed class ContentConverterComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddSingleton<IContentTypeComparer, ContentTypeComparer>();
        builder.Services.AddScoped<IContentConverter, ContentConverter>();
    }
}
