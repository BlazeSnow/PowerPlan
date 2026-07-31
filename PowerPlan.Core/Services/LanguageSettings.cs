namespace PowerPlan.Services;

public static class LanguageSettings
{
    public const string AutoLanguage = "auto";
    public const string ChineseLanguage = "zh-Hans";
    public const string TraditionalChineseLanguage = "zh-Hant";
    public const string EnglishLanguage = "en-US";
    public const string FrenchLanguage = "fr";
    public const string ItalianLanguage = "it";
    public const string GermanLanguage = "de";
    public const string SpanishLanguage = "es";
    public const string DefaultLanguage = AutoLanguage;

    public static string Normalize(string? language)
    {
        if (string.Equals(language, ChineseLanguage, StringComparison.OrdinalIgnoreCase)
            || string.Equals(language, "zh-CN", StringComparison.OrdinalIgnoreCase))
        {
            return ChineseLanguage;
        }

        if (string.Equals(language, TraditionalChineseLanguage, StringComparison.OrdinalIgnoreCase)
            || string.Equals(language, "zh-HK", StringComparison.OrdinalIgnoreCase))
        {
            return TraditionalChineseLanguage;
        }

        if (string.Equals(language, EnglishLanguage, StringComparison.OrdinalIgnoreCase))
        {
            return EnglishLanguage;
        }

        if (string.Equals(language, FrenchLanguage, StringComparison.OrdinalIgnoreCase))
        {
            return FrenchLanguage;
        }

        if (string.Equals(language, ItalianLanguage, StringComparison.OrdinalIgnoreCase))
        {
            return ItalianLanguage;
        }

        if (string.Equals(language, GermanLanguage, StringComparison.OrdinalIgnoreCase))
        {
            return GermanLanguage;
        }

        if (string.Equals(language, SpanishLanguage, StringComparison.OrdinalIgnoreCase))
        {
            return SpanishLanguage;
        }

        return AutoLanguage;
    }

    public static string Resolve(string? language, string? preferredLanguage)
    {
        var mode = Normalize(language);
        if (mode != AutoLanguage)
        {
            return mode;
        }

        if (IsSimplifiedChinese(preferredLanguage))
        {
            return ChineseLanguage;
        }

        if (IsTraditionalChinese(preferredLanguage))
        {
            return TraditionalChineseLanguage;
        }

        if (IsFrench(preferredLanguage))
        {
            return FrenchLanguage;
        }

        if (IsItalian(preferredLanguage))
        {
            return ItalianLanguage;
        }

        if (IsGerman(preferredLanguage))
        {
            return GermanLanguage;
        }

        if (IsSpanish(preferredLanguage))
        {
            return SpanishLanguage;
        }

        return EnglishLanguage;
    }

    public static bool IsSimplifiedChinese(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return false;
        }

        return language.Equals("zh-Hans", StringComparison.OrdinalIgnoreCase)
            || language.StartsWith("zh-Hans-", StringComparison.OrdinalIgnoreCase)
            || language.Equals("zh-CN", StringComparison.OrdinalIgnoreCase)
            || language.StartsWith("zh-CN-", StringComparison.OrdinalIgnoreCase)
            || language.Equals("zh-SG", StringComparison.OrdinalIgnoreCase)
            || language.StartsWith("zh-SG-", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsTraditionalChinese(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return false;
        }

        return language.Equals("zh-Hant", StringComparison.OrdinalIgnoreCase)
            || language.StartsWith("zh-Hant-", StringComparison.OrdinalIgnoreCase)
            || language.Equals("zh-HK", StringComparison.OrdinalIgnoreCase)
            || language.StartsWith("zh-HK-", StringComparison.OrdinalIgnoreCase)
            || language.Equals("zh-TW", StringComparison.OrdinalIgnoreCase)
            || language.StartsWith("zh-TW-", StringComparison.OrdinalIgnoreCase)
            || language.Equals("zh-MO", StringComparison.OrdinalIgnoreCase)
            || language.StartsWith("zh-MO-", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsFrench(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return false;
        }

        return language.Equals(FrenchLanguage, StringComparison.OrdinalIgnoreCase)
            || language.StartsWith("fr-", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsItalian(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return false;
        }

        return language.Equals(ItalianLanguage, StringComparison.OrdinalIgnoreCase)
            || language.StartsWith("it-", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsGerman(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return false;
        }

        return language.Equals(GermanLanguage, StringComparison.OrdinalIgnoreCase)
            || language.StartsWith("de-", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSpanish(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return false;
        }

        return language.Equals(SpanishLanguage, StringComparison.OrdinalIgnoreCase)
            || language.StartsWith("es-", StringComparison.OrdinalIgnoreCase);
    }
}
