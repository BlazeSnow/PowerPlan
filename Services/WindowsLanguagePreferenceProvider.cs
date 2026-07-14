using Windows.System.UserProfile;

namespace PowerPlan.Services;

public sealed class WindowsLanguagePreferenceProvider : ILanguagePreferenceProvider
{
    public string? GetPreferredLanguage()
    {
        return GlobalizationPreferences.Languages.FirstOrDefault();
    }
}
