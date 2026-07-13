using Windows.ApplicationModel;

namespace PowerPlan.Services;

public sealed class PackageUpdateService : IDisposable
{
    private readonly Action _exitApplication;
    private PackageCatalog? _packageCatalog;
    private int _exitRequested;
    private bool _disposed;

    public PackageUpdateService(Action exitApplication)
    {
        _exitApplication = exitApplication;
    }

    public void Initialize()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _packageCatalog ??= PackageCatalog.OpenForCurrentPackage();
            _packageCatalog.PackageUpdating -= OnPackageUpdating;
            _packageCatalog.PackageUpdating += OnPackageUpdating;
        }
        catch
        {
            // Package catalog is only available when the app has package identity.
        }
    }

    private void OnPackageUpdating(PackageCatalog sender, PackageUpdatingEventArgs args)
    {
        if (Interlocked.Exchange(ref _exitRequested, 1) == 0)
        {
            _exitApplication();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_packageCatalog is not null)
        {
            _packageCatalog.PackageUpdating -= OnPackageUpdating;
            _packageCatalog = null;
        }
    }
}
