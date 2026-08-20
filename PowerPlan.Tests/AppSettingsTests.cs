using PowerPlan.Models;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerPlan.Tests;

public sealed class AppSettingsTests
{
    [Fact]
    public void Defaults_MatchApplicationDefaults()
    {
        var settings = new AppSettings();

        Assert.False(settings.AutoStart);
        Assert.True(settings.TrayEnabled);
        Assert.False(settings.LaunchToTray);
        Assert.Equal("auto", settings.Language);
        Assert.Equal(string.Empty, settings.UltimatePerformancePlanGuid);
    }

    [Fact]
    public void JsonContract_PreservesLegacyFieldNames()
    {
        var json = JsonSerializer.Serialize(new AppSettings
        {
            AutoStart = true,
            TrayEnabled = false,
            LaunchToTray = true,
            Language = "fr",
            UltimatePerformancePlanGuid = "saved-guid"
        });

        using var document = JsonDocument.Parse(json);
        var properties = document.RootElement.EnumerateObject().Select(property => property.Name).ToArray();

        Assert.Equal(
            ["startup", "tray", "launchToTray", "language", "UltimatePerformance"],
            properties);
    }

    [Fact]
    public void JsonContract_DeserializesLegacySettingsWithoutDataLoss()
    {
        const string json = """
            {"startup":true,"tray":false,"launchToTray":true,"language":"zh-HK","UltimatePerformance":"saved-guid"}
            """;

        var settings = JsonSerializer.Deserialize<AppSettings>(json);

        Assert.NotNull(settings);
        Assert.True(settings.AutoStart);
        Assert.False(settings.TrayEnabled);
        Assert.True(settings.LaunchToTray);
        Assert.Equal("zh-HK", settings.Language);
        Assert.Equal("saved-guid", settings.UltimatePerformancePlanGuid);
    }
    [Fact]
    public void JsonContract_UsesConfiguredPropertyNames()
    {
        var properties = typeof(AppSettings)
            .GetProperties()
            .Select(property => property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                ?? throw new InvalidOperationException($"{property.Name} has no JSON property name."))
            .ToArray();

        Assert.Equal(
            ["startup", "tray", "launchToTray", "language", "UltimatePerformance"],
            properties);
    }
}
