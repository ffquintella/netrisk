using System.Globalization;
using Model.Configuration;
using Model.Globalization;
using SharedServices.Interfaces;

namespace SharedServices.Services;

public class LanguageManager : ILanguageManager
{
    private readonly LanguagesConfiguration _configuration;
    private readonly Lazy<Dictionary<string, LanguageModel>> _availableLanguages;

    public LanguageModel DefaultLanguage { get; }

    public LanguageModel CurrentLanguage => CreateLanguageModel(Thread.CurrentThread.CurrentUICulture);

    public IEnumerable<LanguageModel> AllLanguages => _availableLanguages.Value.Values;

    public LanguageManager(LanguagesConfiguration configuration)
    {
        _configuration = configuration;
        _availableLanguages = new Lazy<Dictionary<string, LanguageModel>>(GetAvailableLanguages);

        //DefaultLanguage = CreateLanguageModel(CultureInfo.GetCultureInfo("en_US"));
        DefaultLanguage = CreateLanguageModel(CultureInfo.GetCultureInfo(configuration.DefaultLocale));
    }

    public void SetLanguage(string languageCode)
    {
        if (string.IsNullOrEmpty(languageCode))
        {
            throw new ArgumentException($"{nameof(languageCode)} can't be empty.");
        }

        Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(languageCode);
    }

    public void SetLanguage(LanguageModel languageModel) => SetLanguage((string) languageModel.Code);

    // AvailableLocales holds regional locales ("en-US", "pt-BR") but LanguageModel.Code is the
    // two-letter ISO language code, so two locales of the same language collapse onto one key.
    // De-duplicate here instead of letting ToDictionary throw: the first locale configured for a
    // language wins, and AllLanguages lists every configured language exactly once.
    private Dictionary<string, LanguageModel> GetAvailableLanguages() =>
        _configuration
            .AvailableLocales
            .Select(locale => CreateLanguageModel(new CultureInfo(locale)))
            .GroupBy(lm => lm.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.First().Code, group => group.First(),
                StringComparer.OrdinalIgnoreCase);

    private LanguageModel CreateLanguageModel(CultureInfo cultureInfo) =>
        cultureInfo is null
            ? DefaultLanguage
            : new LanguageModel(cultureInfo.EnglishName, cultureInfo.NativeName,
                cultureInfo.TwoLetterISOLanguageName);
}