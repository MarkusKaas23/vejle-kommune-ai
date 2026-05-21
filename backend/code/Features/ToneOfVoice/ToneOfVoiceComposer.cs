using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Notifications;

namespace VejleKommune.Code.Features.ToneOfVoice;

public sealed class ToneOfVoiceComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.AddNotificationAsyncHandler<ContentPublishingNotification, ToneOfVoiceGate>();
    }
}
