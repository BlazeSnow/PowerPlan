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
        HomeItem.Content = "\u4E3B\u9875";
        SettingsItem.Content = "\u8BBE\u7F6E";
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

    public MainPage? GetMainPage() => ContentFrame.Content as MainPage;
}