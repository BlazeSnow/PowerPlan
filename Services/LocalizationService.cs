using Microsoft.Windows.ApplicationModel.Resources;
using System.Collections.Concurrent;

namespace PowerPlan.Services;

public static class LocalizationService
{
    private static readonly ResourceLoader Loader = new();
    private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.Ordinal);

    public static string Get(string key, string? fallback = null)
    {
        if (fallback is null && Cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var result = fallback ?? key;
        try
        {
            var resourceKey = key.Replace('.', '/');
            var value = Loader.GetString(resourceKey);
            if (!string.IsNullOrWhiteSpace(value))
            {
                result = value;
            }
        }
        catch
        {
            // Ignore loader failures and fall back.
        }

        if (fallback is null)
        {
            Cache[key] = result;
        }

        return result;
    }

    public static string Format(string key, params object[] args)
    {
        var format = Get(key, key);
        return string.Format(format, args);
    }
}
