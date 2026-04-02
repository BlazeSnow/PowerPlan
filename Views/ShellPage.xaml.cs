using PowerPlan.Services;

namespace PowerPlan.Views;

public sealed partial class ShellPage : Page
{
    public ShellPage()
    {
        InitializeComponent();
        ApplyLocalization();

        AppNavigationView.SelectedItem = HomeItem;
        _ = ContentFrame.Navigate(typeof(MainPage));
    }

    private void ApplyLocalization()
    {
        HomeItem.Content = LocalizationService.Get("Shell.Home");
        SettingsItem.Content = LocalizationService.Get("Shell.Settings");
    }

    private void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is not string tag)
        {
            return;
        }

        var target = tag == "settings" ? typeof(SettingsPage) : typeof(MainPage);
        if (ContentFrame.CurrentSourcePageType != target)
        {
            _ = ContentFrame.Navigate(target);
        }
    }

    public void NavigateToSettings()
    {
        AppNavigationView.SelectedItem = SettingsItem;
        if (ContentFrame.CurrentSourcePageType != typeof(SettingsPage))
        {
            _ = ContentFrame.Navigate(typeof(SettingsPage));
        }
    }

    public MainPage? GetMainPage() => ContentFrame.Content as MainPage;
}
