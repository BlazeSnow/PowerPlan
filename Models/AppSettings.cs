using System.Text.Json.Serialization;

namespace PowerPlan.Models;

public sealed class AppSettings
{
    [JsonPropertyName("startup")]
    public bool AutoStart { get; set; } = false;

    [JsonPropertyName("tray")]
    public bool TrayEnabled { get; set; } = true;
}
