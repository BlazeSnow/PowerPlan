using PowerPlan.Models;
using PowerPlan.Services;
using PowerPlan.Tests.TestDoubles;

namespace PowerPlan.Tests;

public sealed class SettingsServiceTests
{
    [Fact]
    public async Task LoadAsync_PrefersSettingsStoreOverLegacySettings()
    {
        var settingsStore = new InMemorySettingsStore();
        settingsStore.SetBoolean(SettingsService.AutoStartKey, true);
        settingsStore.SetBoolean(SettingsService.TrayEnabledKey, false);
        settingsStore.SetBoolean(SettingsService.LaunchToTrayKey, true);
        settingsStore.SetString(SettingsService.LanguageKey, "zh-CN");
        settingsStore.SetString(SettingsService.UltimatePerformancePlanGuidKey, "saved-guid");
        var legacyStore = new FakeLegacySettingsStore
        {
            PrimarySettings = new AppSettings { AutoStart = false, Language = LanguageSettings.FrenchLanguage }
        };
        var service = CreateService(settingsStore, legacyStore);

        var settings = await service.LoadAsync();

        Assert.True(settings.AutoStart);
        Assert.False(settings.TrayEnabled);
        Assert.True(settings.LaunchToTray);
        Assert.Equal(LanguageSettings.ChineseLanguage, settings.Language);
        Assert.Equal("saved-guid", settings.UltimatePerformancePlanGuid);
        Assert.Equal(0, legacyStore.PrimaryMigratedCount);
        Assert.Equal(0, legacyStore.FallbackMigratedCount);
    }

    [Fact]
    public async Task LoadAsync_MigratesPrimaryLegacySettings()
    {
        var settingsStore = new InMemorySettingsStore();
        var legacySettings = new AppSettings
        {
            AutoStart = true,
            TrayEnabled = false,
            LaunchToTray = true,
            Language = "zh-HK",
            UltimatePerformancePlanGuid = "legacy-guid"
        };
        var legacyStore = new FakeLegacySettingsStore { PrimarySettings = legacySettings };
        var service = CreateService(settingsStore, legacyStore);

        var settings = await service.LoadAsync();

        Assert.Same(legacySettings, settings);
        Assert.Equal(1, legacyStore.PrimaryMigratedCount);
        Assert.Equal(0, legacyStore.FallbackMigratedCount);
        Assert.True(settingsStore.GetBoolean(SettingsService.AutoStartKey, false));
        Assert.False(settingsStore.GetBoolean(SettingsService.TrayEnabledKey, true));
        Assert.True(settingsStore.GetBoolean(SettingsService.LaunchToTrayKey, false));
        Assert.Equal(LanguageSettings.TraditionalChineseLanguage, settingsStore.GetString(SettingsService.LanguageKey, string.Empty));
        Assert.Equal("legacy-guid", settingsStore.GetString(SettingsService.UltimatePerformancePlanGuidKey, string.Empty));
    }

    [Fact]
    public async Task LoadAsync_MigratesFallbackWhenPrimaryIsUnavailable()
    {
        var settingsStore = new InMemorySettingsStore();
        var legacyStore = new FakeLegacySettingsStore
        {
            FallbackSettings = new AppSettings { Language = LanguageSettings.GermanLanguage }
        };
        var service = CreateService(settingsStore, legacyStore);

        var settings = await service.LoadAsync();

        Assert.Equal(LanguageSettings.GermanLanguage, settings.Language);
        Assert.Equal(0, legacyStore.PrimaryMigratedCount);
        Assert.Equal(1, legacyStore.FallbackMigratedCount);
    }

    [Fact]
    public async Task LoadAsync_ReturnsDefaultsWhenStorageIsUnavailable()
    {
        var settingsStore = new InMemorySettingsStore { ThrowOnWrite = true };
        var service = CreateService(settingsStore, new FakeLegacySettingsStore());

        var settings = await service.LoadAsync();

        Assert.False(settings.AutoStart);
        Assert.True(settings.TrayEnabled);
        Assert.False(settings.LaunchToTray);
        Assert.Equal(LanguageSettings.AutoLanguage, settings.Language);
    }

    [Fact]
    public async Task SaveAsync_NormalizesLanguageUpdatesCurrentAndRaisesEvent()
    {
        var settingsStore = new InMemorySettingsStore();
        var service = CreateService(settingsStore, new FakeLegacySettingsStore());
        AppSettings? changedSettings = null;
        service.SettingsChanged += (_, settings) => changedSettings = settings;
        var settings = new AppSettings { Language = "zh-CN" };

        await service.SaveAsync(settings);

        Assert.Equal(LanguageSettings.ChineseLanguage, settings.Language);
        Assert.Same(settings, service.Current);
        Assert.Same(settings, changedSettings);
        Assert.Equal(LanguageSettings.ChineseLanguage, settingsStore.GetString(SettingsService.LanguageKey, string.Empty));
    }

    [Fact]
    public async Task LoadAsync_NormalizesStoredLanguageAndWritesCanonicalValue()
    {
        var settingsStore = new InMemorySettingsStore();
        settingsStore.SetString(SettingsService.LanguageKey, "zh-HK");
        var service = CreateService(settingsStore, new FakeLegacySettingsStore());

        var settings = await service.LoadAsync();

        Assert.Equal(LanguageSettings.TraditionalChineseLanguage, settings.Language);
        Assert.Equal(LanguageSettings.TraditionalChineseLanguage, settingsStore.GetString(SettingsService.LanguageKey, string.Empty));
    }

    [Fact]
    public void ResolveLanguage_ReturnsEnglishWhenPreferenceProviderThrows()
    {
        var provider = new FakeLanguagePreferenceProvider { ThrowOnRead = true };
        var service = new SettingsService(new InMemorySettingsStore(), new FakeLegacySettingsStore(), provider);

        Assert.Equal(LanguageSettings.EnglishLanguage, service.ResolveLanguage(LanguageSettings.AutoLanguage));
    }

    [Fact]
    public async Task LoadAsync_SavesDefaultsWhenStorageIsAvailable()
    {
        var settingsStore = new InMemorySettingsStore();
        var service = CreateService(settingsStore, new FakeLegacySettingsStore());

        var settings = await service.LoadAsync();

        Assert.Equal(new[]
        {
            SettingsService.AutoStartKey,
            SettingsService.TrayEnabledKey,
            SettingsService.LaunchToTrayKey
        }, settingsStore.BooleanWriteKeys);
        Assert.Contains(SettingsService.LanguageKey, settingsStore.StringWriteKeys);
        Assert.Contains(SettingsService.UltimatePerformancePlanGuidKey, settingsStore.StringWriteKeys);
        Assert.Equal(LanguageSettings.AutoLanguage, settingsStore.GetString(SettingsService.LanguageKey, string.Empty));
        var reloaded = await service.LoadAsync();
        Assert.Equal(settings.AutoStart, reloaded.AutoStart);
        Assert.Equal(settings.TrayEnabled, reloaded.TrayEnabled);
        Assert.Equal(settings.LaunchToTray, reloaded.LaunchToTray);
    }

    [Fact]
    public async Task LoadAsync_DoesNotRewriteCanonicalStoredLanguage()
    {
        var settingsStore = new InMemorySettingsStore();
        settingsStore.SetString(SettingsService.LanguageKey, LanguageSettings.FrenchLanguage);
        settingsStore.StringWriteKeys.Clear();
        var service = CreateService(settingsStore, new FakeLegacySettingsStore());

        _ = await service.LoadAsync();

        Assert.DoesNotContain(SettingsService.LanguageKey, settingsStore.StringWriteKeys);
    }

    [Fact]
    public async Task LoadAsync_PropagatesMigrationSaveFailureWithoutMarkingMigrated()
    {
        var settingsStore = new InMemorySettingsStore { ThrowOnWrite = true };
        var legacyStore = new FakeLegacySettingsStore
        {
            PrimarySettings = new AppSettings { Language = LanguageSettings.FrenchLanguage }
        };
        var service = CreateService(settingsStore, legacyStore);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.LoadAsync());

        Assert.Equal(0, legacyStore.PrimaryMigratedCount);
    }

    [Fact]
    public async Task InitializeAsync_UpdatesCurrentSettings()
    {
        var settingsStore = new InMemorySettingsStore();
        settingsStore.SetBoolean(SettingsService.AutoStartKey, true);
        var service = CreateService(settingsStore, new FakeLegacySettingsStore());

        await service.InitializeAsync();

        Assert.True(service.Current.AutoStart);
    }

    [Fact]
    public async Task SaveCurrentAsync_SavesCurrentInstanceAndRaisesEvent()
    {
        var settingsStore = new InMemorySettingsStore();
        var service = CreateService(settingsStore, new FakeLegacySettingsStore());
        service.Current.Language = LanguageSettings.ItalianLanguage;
        AppSettings? changedSettings = null;
        service.SettingsChanged += (_, settings) => changedSettings = settings;

        await service.SaveCurrentAsync();

        Assert.Same(service.Current, changedSettings);
        Assert.Equal(LanguageSettings.ItalianLanguage, service.Current.Language);
        Assert.Equal(LanguageSettings.ItalianLanguage, settingsStore.GetString(SettingsService.LanguageKey, string.Empty));
    }

    [Fact]
    public void ResolveLanguage_UsesPreferredLanguage()
    {
        var service = new SettingsService(
            new InMemorySettingsStore(),
            new FakeLegacySettingsStore(),
            new FakeLanguagePreferenceProvider { PreferredLanguage = "fr-CA" });

        Assert.Equal(LanguageSettings.FrenchLanguage, service.ResolveLanguage(LanguageSettings.AutoLanguage));
    }

    [Fact]
    public async Task LoadAsync_UsesDefaultsWhenOnlyUltimateGuidSentinelExists()
    {
        var settingsStore = new InMemorySettingsStore();
        settingsStore.SetString(SettingsService.UltimatePerformancePlanGuidKey, "saved-guid");
        var service = CreateService(settingsStore, new FakeLegacySettingsStore());

        var settings = await service.LoadAsync();

        Assert.False(settings.AutoStart);
        Assert.True(settings.TrayEnabled);
        Assert.False(settings.LaunchToTray);
        Assert.Equal("saved-guid", settings.UltimatePerformancePlanGuid);
    }


    private static SettingsService CreateService(InMemorySettingsStore settingsStore, FakeLegacySettingsStore legacyStore)
    {
        return new SettingsService(settingsStore, legacyStore, new FakeLanguagePreferenceProvider { PreferredLanguage = "en-US" });
    }
}
