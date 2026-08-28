namespace HTFManager.Core.Models;

public sealed class ConfigurationEntry
{
    public required string Section { get; init; }
    public required string Key { get; init; }
    public string Description { get; init; } = "";
    public string TypeName { get; init; } = "String";
    public ConfigurationValueKind Kind { get; init; } = ConfigurationValueKind.Text;
    public string Value { get; set; } = "";
    public string OriginalValue { get; set; } = "";
    public string? DefaultValue { get; init; }
    public IReadOnlyList<string> AllowedValues { get; init; } = Array.Empty<string>();
    public double? Minimum { get; init; }
    public double? Maximum { get; init; }
    public bool QuoteValue { get; init; }

    public bool IsDirty => !string.Equals(Value, OriginalValue, StringComparison.Ordinal);
}
