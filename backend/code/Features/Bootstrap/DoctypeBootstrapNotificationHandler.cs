using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;

namespace VejleKommune.Code.Features.Bootstrap;

public sealed class DoctypeBootstrapNotificationHandler : INotificationAsyncHandler<UmbracoApplicationStartedNotification>
{
    private readonly IContentTypeService _contentTypeService;
    private readonly IMediaTypeService _mediaTypeService;
    private readonly IDataTypeService _dataTypeService;
    private readonly ITemplateService _templateService;
    private readonly ILanguageService _languageService;
    private readonly IContentService _contentService;
    private readonly IDatabaseCacheRebuilder _databaseCacheRebuilder;
    private readonly IShortStringHelper _shortStringHelper;
    private readonly ILogger<DoctypeBootstrapNotificationHandler> _logger;

    public DoctypeBootstrapNotificationHandler(
        IContentTypeService contentTypeService,
        IMediaTypeService mediaTypeService,
        IDataTypeService dataTypeService,
        ITemplateService templateService,
        ILanguageService languageService,
        IContentService contentService,
        IDatabaseCacheRebuilder databaseCacheRebuilder,
        IShortStringHelper shortStringHelper,
        ILogger<DoctypeBootstrapNotificationHandler> logger)
    {
        _contentTypeService = contentTypeService;
        _mediaTypeService = mediaTypeService;
        _dataTypeService = dataTypeService;
        _templateService = templateService;
        _languageService = languageService;
        _contentService = contentService;
        _databaseCacheRebuilder = databaseCacheRebuilder;
        _shortStringHelper = shortStringHelper;
        _logger = logger;
    }

    public async Task HandleAsync(UmbracoApplicationStartedNotification notification, CancellationToken cancellationToken)
    {
        LanguageBootstrap.Ensure(_languageService);

        if (_contentTypeService.Get(DoctypeKeys.HomePage) is null)
        {
            _logger.LogInformation("Doctype bootstrap starting");

            var dataTypes = await DataTypeLookup.ResolveAsync(_dataTypeService);
            var templates = TemplateFactory.CreateAll(_templateService, _shortStringHelper);

            var compositions = await CompositionFactory.CreateAllAsync(_contentTypeService, _shortStringHelper, dataTypes);
            await DoctypeFactory.CreateAllAsync(_contentTypeService, _shortStringHelper, dataTypes, compositions, templates);
            await MediaTypeFactory.CreateAllAsync(_mediaTypeService, _shortStringHelper, dataTypes);

            _logger.LogInformation("Doctype bootstrap complete");
        }

        var seeded = ContentSeed.SeedIfEmpty(_contentService, _contentTypeService, _logger);

        if (seeded)
        {
            _logger.LogInformation("Rebuilding published-content database cache after seed");
            await _databaseCacheRebuilder.RebuildAsync(false);
            _logger.LogInformation("Published-content database cache rebuilt");
        }
    }
}
