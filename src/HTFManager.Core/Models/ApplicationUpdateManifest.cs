using System.Text.Json.Serialization;

namespace HTFManager.Core.Models;

public sealed class ApplicationUpdateManifest
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("channel")]
    public string Channel { get; set; } = "stable";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("rid")]
    public string Rid { get; set; } = "win-x64";

    [JsonPropertyName("asset")]
    public string Asset { get; set; } = "";

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = "";

    [JsonPropertyName("publishedAt")]
    public DateTimeOffset PublishedAt { get; set; }
}
