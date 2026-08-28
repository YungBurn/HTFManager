using System.Text.Json.Serialization;

namespace HTFManager.Core.Models;

public sealed class RemoteModPackage
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("full_name")]
    public string FullName { get; set; } = "";

    [JsonPropertyName("owner")]
    public string Owner { get; set; } = "";

    [JsonPropertyName("package_url")]
    public string PackageUrl { get; set; } = "";

    [JsonPropertyName("date_created")]
    public DateTimeOffset DateCreated { get; set; }

    [JsonPropertyName("date_updated")]
    public DateTimeOffset DateUpdated { get; set; }

    [JsonPropertyName("rating_score")]
    public int RatingScore { get; set; }

    [JsonPropertyName("is_pinned")]
    public bool IsPinned { get; set; }

    [JsonPropertyName("is_deprecated")]
    public bool IsDeprecated { get; set; }

    [JsonPropertyName("has_nsfw_content")]
    public bool HasNsfwContent { get; set; }

    [JsonPropertyName("categories")]
    public List<string> Categories { get; set; } = new();

    [JsonPropertyName("versions")]
    public List<RemoteModVersion> Versions { get; set; } = new();

    [JsonIgnore]
    public RemoteModVersion? LatestVersion => Versions
        .Where(v => v.IsActive)
        .OrderByDescending(v => v.DateCreated)
        .FirstOrDefault() ?? Versions.OrderByDescending(v => v.DateCreated).FirstOrDefault();

    [JsonIgnore]
    public long TotalDownloads => Versions.Sum(v => v.Downloads);

    [JsonIgnore]
    public bool IsModpack => Categories.Any(c => c.Equals("Modpacks", StringComparison.OrdinalIgnoreCase));
}
