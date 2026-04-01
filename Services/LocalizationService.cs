using Microsoft.Windows.ApplicationModel.Resources;

namespace PowerPlan.Services;

public static class LocalizationService
{
    private static readonly ResourceLoader Loader = new();

    public static string Get(string key, string? fallback = null)
    {
        try
        {
            var value = Loader.GetString(key);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }
        catch
        {
            // Ignore loader failures and fall back.
        }

        return fallback ?? key;
    }

    public static string Format(string key, params object[] args)
    {
        var format = Get(key, key);
        return string.Format(format, args);
    }
}