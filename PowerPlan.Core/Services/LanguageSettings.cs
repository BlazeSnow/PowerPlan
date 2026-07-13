namespace PowerPlan.Services;

public static class LanguageSettings
{
    public const string AutoLanguage = "auto";
    public const string ChineseLanguage = "zh-CN";
    public const string EnglishLanguage = "en-US";
    public const string DefaultLanguage = AutoLanguage;

    public static string Normalize(string? language)
    {
        if (string.Equals(language, ChineseLanguage, StringComparison.OrdinalIgnoreCase))
        {
            return ChineseLanguage;
        }

        if (string.Equals(language, EnglishLanguage, StringComparison.OrdinalIgnoreCase))
        {
            return EnglishLanguage;
        }

        return AutoLanguage;
    }

    public static string Resolve(string? language, string? preferredLanguage)
    {
        var mode = Normalize(language);
        return mode != AutoLanguage
            ? mode
            : IsSimplifiedChinese(preferredLanguage) ? ChineseLanguage : EnglishLanguage;
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
}
