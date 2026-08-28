using System.Text.Json.Serialization;

namespace HTFManager.Core.Models;

public sealed class RemoteModVersion
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("full_name")]
    public string FullName { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("icon")]
    public string Icon { get; set; } = "";

    [JsonPropertyName("version_number")]
    public string VersionNumber { get; set; } = "0.0.0";

    [JsonPropertyName("dependencies")]
    public List<string> Dependencies { get; set; } = new();

    [JsonPropertyName("download_url")]
    public string DownloadUrl { get; set; } = "";

    [JsonPropertyName("downloads")]
    public long Downloads { get; set; }

    [JsonPropertyName("date_created")]
    public DateTimeOffset DateCreated { get; set; }

    [JsonPropertyName("website_url")]
    public string? WebsiteUrl { get; set; }

    [JsonPropertyName("is_active")]
    public bool IsActive { get; set; } = true;

    [JsonPropertyName("file_size")]
    public long FileSize { get; set; }
}
