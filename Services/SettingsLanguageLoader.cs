using Windows.Storage;
using Windows.System.UserProfile;

namespace PowerPlan.Services;

public static class SettingsLanguageLoader
{
    public static string LoadSynchronously()
    {
        try
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            var mode = LanguageSettings.Normalize(values.TryGetValue(SettingsService.LanguageKey, out var value) ? value as string : null);
            var preferredLanguage = GlobalizationPreferences.Languages.FirstOrDefault();
            return LanguageSettings.Resolve(mode, preferredLanguage);
        }
        catch
        {
            return LanguageSettings.EnglishLanguage;
        }
    }
}
