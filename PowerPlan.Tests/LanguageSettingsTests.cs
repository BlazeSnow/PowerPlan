using PowerPlan.Services;

namespace PowerPlan.Tests;

public sealed class LanguageSettingsTests
{
    [Theory]
    [InlineData("zh-CN", LanguageSettings.ChineseLanguage)]
    [InlineData("ZH-hans", LanguageSettings.ChineseLanguage)]
    [InlineData("zh-HK", LanguageSettings.TraditionalChineseLanguage)]
    [InlineData("EN-us", LanguageSettings.EnglishLanguage)]
    [InlineData("fr", LanguageSettings.FrenchLanguage)]
    [InlineData("unknown", LanguageSettings.AutoLanguage)]
    [InlineData(null, LanguageSettings.AutoLanguage)]
    public void Normalize_ReturnsSupportedLanguageOrAuto(string? input, string expected)
    {
        Assert.Equal(expected, LanguageSettings.Normalize(input));
    }

    [Theory]
    [InlineData("zh-CN", LanguageSettings.ChineseLanguage)]
    [InlineData("zh-TW", LanguageSettings.TraditionalChineseLanguage)]
    [InlineData("fr-CA", LanguageSettings.FrenchLanguage)]
    [InlineData("it-CH", LanguageSettings.ItalianLanguage)]
    [InlineData("de-AT", LanguageSettings.GermanLanguage)]
    [InlineData("es-MX", LanguageSettings.SpanishLanguage)]
    [InlineData("ja-JP", LanguageSettings.EnglishLanguage)]
    [InlineData(null, LanguageSettings.EnglishLanguage)]
    public void Resolve_AutoModeUsesPreferredLanguage(string? preferredLanguage, string expected)
    {
        Assert.Equal(expected, LanguageSettings.Resolve(LanguageSettings.AutoLanguage, preferredLanguage));
    }

    [Theory]
    [InlineData("zh-Hant", LanguageSettings.TraditionalChineseLanguage)]
    [InlineData("it", LanguageSettings.ItalianLanguage)]
    [InlineData("de", LanguageSettings.GermanLanguage)]
    [InlineData("es", LanguageSettings.SpanishLanguage)]
    public void Normalize_ReturnsAdditionalSupportedLanguages(string input, string expected)
    {
        Assert.Equal(expected, LanguageSettings.Normalize(input));
    }

    [Fact]
    public void Resolve_UnknownModeStillUsesPreferredLanguage()
    {
        Assert.Equal(LanguageSettings.FrenchLanguage, LanguageSettings.Resolve("unknown", "fr-CA"));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("zh-Hans", true)]
    [InlineData("zh-SG-custom", true)]
    [InlineData("zh-TW", false)]
    public void IsSimplifiedChinese_RecognizesOnlySimplifiedTags(string? language, bool expected)
    {
        Assert.Equal(expected, LanguageSettings.IsSimplifiedChinese(language));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("zh-Hant", true)]
    [InlineData("zh-MO-custom", true)]
    [InlineData("zh-CN", false)]
    public void IsTraditionalChinese_RecognizesOnlyTraditionalTags(string? language, bool expected)
    {
        Assert.Equal(expected, LanguageSettings.IsTraditionalChinese(language));
    }

    [Theory]
    [InlineData("fr", true)]
    [InlineData("fr-CA", true)]
    [InlineData("en-US", false)]
    [InlineData("", false)]
    public void IsFrench_RecognizesExactAndRegionalTags(string language, bool expected)
    {
        Assert.Equal(expected, LanguageSettings.IsFrench(language));
    }

    [Theory]
    [InlineData("it", true)]
    [InlineData("it-CH", true)]
    [InlineData("de-DE", false)]
    public void IsItalian_RecognizesExactAndRegionalTags(string language, bool expected)
    {
        Assert.Equal(expected, LanguageSettings.IsItalian(language));
    }

    [Theory]
    [InlineData("de", true)]
    [InlineData("de-AT", true)]
    [InlineData("it-IT", false)]
    public void IsGerman_RecognizesExactAndRegionalTags(string language, bool expected)
    {
        Assert.Equal(expected, LanguageSettings.IsGerman(language));
    }

    [Theory]
    [InlineData("es", true)]
    [InlineData("es-MX", true)]
    [InlineData("fr-FR", false)]
    public void IsSpanish_RecognizesExactAndRegionalTags(string language, bool expected)
    {
        Assert.Equal(expected, LanguageSettings.IsSpanish(language));
    }

    [Fact]
    public void Resolve_ExplicitLanguageOverridesPreferredLanguage()
    {
        Assert.Equal(
            LanguageSettings.GermanLanguage,
            LanguageSettings.Resolve(LanguageSettings.GermanLanguage, "zh-CN"));
    }
}
