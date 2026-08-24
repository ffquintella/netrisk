using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using JetBrains.Annotations;
using Model.Configuration;
using Model.Globalization;
using SharedServices.Interfaces;
using SharedServices.Services;
using Xunit;

namespace SharedServices.Tests.Services;

[TestSubject(typeof(LanguageManager))]
public class LanguageManagerTest
{
    private static ILanguageManager Create(string defaultLocale = "en-US", params string[] available)
    {
        var configuration = new LanguagesConfiguration
        {
            DefaultLocale = defaultLocale,
            AvailableLocales = available.Length == 0
                ? new List<string> { "en-US", "pt-BR" }
                : available.ToList()
        };

        return new LanguageManager(configuration);
    }

    [Fact]
    public void TestDefaultLanguageComesFromConfiguration()
    {
        var manager = Create("pt-BR");

        Assert.Equal("pt", manager.DefaultLanguage.Code);
        Assert.Equal(CultureInfo.GetCultureInfo("pt-BR").EnglishName, manager.DefaultLanguage.Name);
        Assert.Equal(CultureInfo.GetCultureInfo("pt-BR").NativeName, manager.DefaultLanguage.NativeName);
    }

    [Fact]
    public void TestAllLanguagesListsEveryConfiguredLocaleOnce()
    {
        var manager = Create("en-US", "en-US", "pt-BR");

        var codes = manager.AllLanguages.Select(l => l.Code).OrderBy(c => c).ToList();

        Assert.Equal(new[] { "en", "pt" }, codes);
    }

    [Fact]
    public void TestAllLanguagesCollapsesTwoLocalesSharingALanguageCode()
    {
        // AllLanguages is keyed by the two-letter ISO code, so two locales of the same language
        // collapse onto a single entry instead of making the dictionary build throw.
        var manager = Create("en-US", "en-US", "en-GB");

        var languages = manager.AllLanguages.ToList();

        var language = Assert.Single(languages);
        Assert.Equal("en", language.Code);
        // The first locale configured for a language wins.
        Assert.Equal(CultureInfo.GetCultureInfo("en-US").EnglishName, language.Name);
        Assert.Equal(CultureInfo.GetCultureInfo("en-US").NativeName, language.NativeName);
    }

    [Fact]
    public void TestAllLanguagesKeepsTheFirstOfSeveralLocalesSharingALanguageCode()
    {
        var manager = Create("en-US", "pt-PT", "pt-BR", "en-GB");

        var languages = manager.AllLanguages.ToList();

        Assert.Equal(new[] { "en", "pt" }, languages.Select(l => l.Code).OrderBy(c => c));
        Assert.Equal(CultureInfo.GetCultureInfo("pt-PT").EnglishName,
            languages.Single(l => l.Code == "pt").Name);
        Assert.Equal(CultureInfo.GetCultureInfo("en-GB").EnglishName,
            languages.Single(l => l.Code == "en").Name);
    }

    [Fact]
    public void TestSetLanguageByCodeChangesCurrentLanguage()
    {
        var manager = Create();
        var original = Thread.CurrentThread.CurrentUICulture;

        try
        {
            manager.SetLanguage("pt-BR");

            Assert.Equal("pt", manager.CurrentLanguage.Code);
        }
        finally
        {
            Thread.CurrentThread.CurrentUICulture = original;
        }
    }

    [Fact]
    public void TestSetLanguageByModelChangesCurrentLanguage()
    {
        var manager = Create();
        var original = Thread.CurrentThread.CurrentUICulture;

        try
        {
            manager.SetLanguage(new LanguageModel("Portuguese", "português", "pt"));

            Assert.Equal("pt", manager.CurrentLanguage.Code);
        }
        finally
        {
            Thread.CurrentThread.CurrentUICulture = original;
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void TestSetLanguageRejectsEmptyCode(string languageCode)
    {
        var manager = Create();

        Assert.Throws<ArgumentException>(() => manager.SetLanguage(languageCode));
    }

    [Fact]
    public void TestSetLanguageAcceptsALocaleThatIsNotInAllLanguages()
    {
        var manager = Create("en-US", "en-US");
        var original = Thread.CurrentThread.CurrentUICulture;

        try
        {
            manager.SetLanguage("fr-FR");

            Assert.Equal("fr", manager.CurrentLanguage.Code);
            Assert.DoesNotContain("fr", manager.AllLanguages.Select(l => l.Code));
        }
        finally
        {
            Thread.CurrentThread.CurrentUICulture = original;
        }
    }
}
