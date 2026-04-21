using PowerPlan.Services;

namespace PowerPlan.Views;

public sealed partial class ShellPage : Page
{
    public ShellPage(bool navigateToHomeOnStartup = true)
    {
        InitializeComponent();
        ApplyLocalization();

        AppNavigationView.SelectedItem = HomeItem;
        if (navigateToHomeOnStartup)
        {
            _ = ContentFrame.Navigate(typeof(MainPage));
        }
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

    public MainPage EnsureMainPageLoaded()
    {
        if (ContentFrame.CurrentSourcePageType != typeof(MainPage))
        {
            AppNavigationView.SelectedItem = HomeItem;
            _ = ContentFrame.Navigate(typeof(MainPage));
        }

        return (MainPage)ContentFrame.Content;
    }

    public MainPage? GetMainPage() => ContentFrame.Content as MainPage;
}
