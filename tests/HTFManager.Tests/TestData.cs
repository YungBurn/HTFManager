using HTFManager.Core.Models;

namespace HTFManager.Tests;

internal static class TestData
{
    public static InstalledMod Installed(
        string id,
        string name = "Example Mod",
        string version = "1.0.0",
        string? packageKey = "Author-ExampleMod",
        string? intrinsicId = null,
        string fileName = "ExampleMod.dll",
        ModSourceType source = ModSourceType.Thunderstore,
        ModLoaderKind loader = ModLoaderKind.BepInEx,
        ModComponentKind component = ModComponentKind.Plugin,
        bool enabled = true,
        bool managed = true,
        string? registryId = null)
        => new()
        {
            Id = id,
            Name = name,
            Version = version,
            Author = "Author",
            PackageKey = packageKey,
            IntrinsicId = intrinsicId,
            FilePath = Path.Combine(Path.GetTempPath(), "HTFManager-TestGame", "BepInEx", "plugins", fileName),
            Source = source,
            Loader = loader,
            Component = component,
            Enabled = enabled,
            IsExternal = !managed,
            IsManaged = managed,
            RegistryId = registryId
        };

    public static ProfileModExpectation Expectation(
        string portableId = "portable-a",
        string name = "Example Mod",
        string version = "1.0.0",
        string? packageKey = "Author-ExampleMod",
        string? intrinsicId = null,
        string fileName = "ExampleMod.dll",
        string? resolvedModId = null,
        ProfileExpectationMetadataQuality quality = ProfileExpectationMetadataQuality.Complete,
        ModLoaderKind loader = ModLoaderKind.BepInEx,
        ModComponentKind component = ModComponentKind.Plugin,
        bool enabled = true)
        => new()
        {
            Requirement = new ProfileModRequirement
            {
                PortableId = portableId,
                Name = name,
                Version = version,
                Author = "Author",
                PackageKey = packageKey,
                IntrinsicId = intrinsicId,
                FileName = fileName,
                Source = packageKey is null ? ModSourceType.LocalDll : ModSourceType.Thunderstore,
                Loader = loader,
                Component = component,
                Enabled = enabled
            },
            ResolvedModId = resolvedModId,
            MetadataQuality = quality
        };
}
