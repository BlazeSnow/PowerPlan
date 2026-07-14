using PowerPlan.Services;

namespace PowerPlan.Pages;

public sealed partial class ShellPage : Page
{
    private readonly IPageHost _pageHost;

    public ShellPage(IPageHost pageHost, bool navigateToHomeOnStartup = true)
    {
        _pageHost = pageHost;
        InitializeComponent();
        ApplyLocalization();

        AppNavigationView.SelectedItem = HomeItem;
        if (navigateToHomeOnStartup)
        {
            _ = ContentFrame.Navigate(typeof(MainPage), _pageHost);
        }
    }

    public TitleBar AppTitleBarElement => AppTitleBar;

    private void ApplyLocalization()
    {
        HomeItem.Content = _pageHost.GetString("Shell.Home");
        SettingsItem.Content = _pageHost.GetString("Shell.Settings");
    }

    private void OnTitleBarPaneToggleRequested(TitleBar sender, object args)
    {
        AppNavigationView.IsPaneOpen = !AppNavigationView.IsPaneOpen;
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
            _ = ContentFrame.Navigate(target, _pageHost);
        }
    }

    public MainPage EnsureMainPageLoaded()
    {
        if (ContentFrame.CurrentSourcePageType != typeof(MainPage))
        {
            AppNavigationView.SelectedItem = HomeItem;
            if (!ContentFrame.Navigate(typeof(MainPage), _pageHost))
            {
                throw new InvalidOperationException("Failed to navigate to MainPage.");
            }
        }

        if (ContentFrame.Content is not MainPage mainPage)
        {
            throw new InvalidOperationException("MainPage is not loaded in the content frame.");
        }

        return mainPage;
    }

    public MainPage? GetMainPage() => ContentFrame.Content as MainPage;
}
