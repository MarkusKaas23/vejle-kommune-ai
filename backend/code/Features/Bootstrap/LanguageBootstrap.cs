using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace VejleKommune.Code.Features.Bootstrap;

internal static class LanguageBootstrap
{
    public const string Danish = "da-DK";
    public const string English = "en-US";

    public static void Ensure(ILanguageService languageService)
    {
        EnsureLanguage(languageService, Danish, "Dansk (Danmark)", isDefault: true, isMandatory: true);
        EnsureLanguage(languageService, English, "English (United States)", isDefault: false, isMandatory: false);
    }

    private static void EnsureLanguage(
        ILanguageService languageService,
        string isoCode,
        string cultureName,
        bool isDefault,
        bool isMandatory)
    {
        var existing = languageService.GetAsync(isoCode).GetAwaiter().GetResult();
        if (existing is not null)
        {
            if (isDefault && !existing.IsDefault)
            {
                existing.IsDefault = true;
                languageService.UpdateAsync(existing, Constants.Security.SuperUserKey).GetAwaiter().GetResult();
            }
            return;
        }

        var language = new Language(isoCode, cultureName)
        {
            IsDefault = isDefault,
            IsMandatory = isMandatory,
        };
        languageService.CreateAsync(language, Constants.Security.SuperUserKey).GetAwaiter().GetResult();
    }
}
