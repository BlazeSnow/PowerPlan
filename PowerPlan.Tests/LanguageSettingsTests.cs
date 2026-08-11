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

    [Fact]
    public void Resolve_ExplicitLanguageOverridesPreferredLanguage()
    {
        Assert.Equal(
            LanguageSettings.GermanLanguage,
            LanguageSettings.Resolve(LanguageSettings.GermanLanguage, "zh-CN"));
    }
}
