namespace PowerPlan.Services;

public static class LanguageSettings
{
    public const string AutoLanguage = "auto";
    public const string ChineseLanguage = "zh-CN";
    public const string TraditionalChineseLanguage = "zh-HK";
    public const string EnglishLanguage = "en-US";
    public const string FrenchLanguage = "fr";
    public const string DefaultLanguage = AutoLanguage;

    public static string Normalize(string? language)
    {
        if (string.Equals(language, ChineseLanguage, StringComparison.OrdinalIgnoreCase))
        {
            return ChineseLanguage;
        }

        if (string.Equals(language, TraditionalChineseLanguage, StringComparison.OrdinalIgnoreCase))
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
            || language.Equals(ChineseLanguage, StringComparison.OrdinalIgnoreCase)
            || language.StartsWith($"{ChineseLanguage}-", StringComparison.OrdinalIgnoreCase)
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
            || language.Equals(TraditionalChineseLanguage, StringComparison.OrdinalIgnoreCase)
            || language.StartsWith($"{TraditionalChineseLanguage}-", StringComparison.OrdinalIgnoreCase)
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
}
