using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using HTFManager.Core.Models;

namespace HTFManager.Infrastructure.Mods;

internal static class ManagedAssemblyInspector
{
    public static ManagedAssemblyInfo Inspect(string path)
    {
        using var stream = File.OpenRead(path);
        return Inspect(stream);
    }

    public static ManagedAssemblyInfo Inspect(Stream stream)
    {
        try
        {
            using var pe = new PEReader(stream, PEStreamOptions.LeaveOpen);
            if (!pe.HasMetadata)
                return ManagedAssemblyInfo.Invalid("The DLL is not a managed .NET assembly.");

            var reader = pe.GetMetadataReader();
            if (!reader.IsAssembly)
                return ManagedAssemblyInfo.Invalid("The DLL does not contain an assembly manifest.");

            var definition = reader.GetAssemblyDefinition();
            var assemblyName = reader.GetString(definition.Name);
            var version = definition.Version.ToString();
            var references = reader.AssemblyReferences
                .Select(handle => reader.GetAssemblyReference(handle))
                .Select(reference => reader.GetString(reference.Name))
                .ToArray();

            var referencesBepInEx = references.Any(name => name.StartsWith("BepInEx", StringComparison.OrdinalIgnoreCase));
            var referencesMelonLoader = references.Any(name => name.Equals("MelonLoader", StringComparison.OrdinalIgnoreCase));

            var hasBepInExPlugin = reader.TypeDefinitions.Any(handle =>
                DerivesFrom(reader, handle, "BepInEx", "BaseUnityPlugin", new HashSet<TypeDefinitionHandle>()));
            var hasMelonMod = reader.TypeDefinitions.Any(handle =>
                DerivesFrom(reader, handle, "MelonLoader", "MelonMod", new HashSet<TypeDefinitionHandle>()));
            var hasMelonPlugin = reader.TypeDefinitions.Any(handle =>
                DerivesFrom(reader, handle, "MelonLoader", "MelonPlugin", new HashSet<TypeDefinitionHandle>()));

            if (hasBepInExPlugin && (hasMelonMod || hasMelonPlugin))
                return new ManagedAssemblyInfo(true, assemblyName, version, ModLoaderKind.Unknown, ModComponentKind.Unknown,
                    referencesBepInEx, referencesMelonLoader, "The assembly contains both BepInEx and MelonLoader entry types.");

            if (hasBepInExPlugin)
                return new ManagedAssemblyInfo(true, assemblyName, version, ModLoaderKind.BepInEx, ModComponentKind.Plugin,
                    referencesBepInEx, referencesMelonLoader, null);

            if (hasMelonMod && hasMelonPlugin)
                return new ManagedAssemblyInfo(true, assemblyName, version, ModLoaderKind.MelonLoader, ModComponentKind.Unknown,
                    referencesBepInEx, referencesMelonLoader, null);

            if (hasMelonPlugin)
                return new ManagedAssemblyInfo(true, assemblyName, version, ModLoaderKind.MelonLoader, ModComponentKind.Plugin,
                    referencesBepInEx, referencesMelonLoader, null);

            if (hasMelonMod)
                return new ManagedAssemblyInfo(true, assemblyName, version, ModLoaderKind.MelonLoader, ModComponentKind.Mod,
                    referencesBepInEx, referencesMelonLoader, null);

            var error = referencesMelonLoader
                ? "The DLL references MelonLoader but no MelonMod or MelonPlugin entry type was found."
                : referencesBepInEx
                    ? "The DLL references BepInEx but no BaseUnityPlugin entry type was found."
                    : "The DLL was not recognized as a BepInEx or MelonLoader mod.";

            return new ManagedAssemblyInfo(true, assemblyName, version, ModLoaderKind.Unknown, ModComponentKind.Unknown,
                referencesBepInEx, referencesMelonLoader, error);
        }
        catch (Exception ex)
        {
            return ManagedAssemblyInfo.Invalid(ex.Message);
        }
    }

    private static bool DerivesFrom(
        MetadataReader reader,
        TypeDefinitionHandle typeHandle,
        string targetNamespace,
        string targetName,
        ISet<TypeDefinitionHandle> visited)
    {
        if (!visited.Add(typeHandle))
            return false;

        var definition = reader.GetTypeDefinition(typeHandle);
        var baseType = definition.BaseType;
        if (baseType.IsNil)
            return false;

        if (baseType.Kind == HandleKind.TypeReference)
        {
            var reference = reader.GetTypeReference((TypeReferenceHandle)baseType);
            return reader.GetString(reference.Namespace).Equals(targetNamespace, StringComparison.Ordinal) &&
                   reader.GetString(reference.Name).Equals(targetName, StringComparison.Ordinal);
        }

        if (baseType.Kind == HandleKind.TypeDefinition)
            return DerivesFrom(reader, (TypeDefinitionHandle)baseType, targetNamespace, targetName, visited);

        return false;
    }
}

internal sealed record ManagedAssemblyInfo(
    bool IsManaged,
    string AssemblyName,
    string Version,
    ModLoaderKind Loader,
    ModComponentKind Component,
    bool ReferencesBepInEx,
    bool ReferencesMelonLoader,
    string? Error)
{
    public static ManagedAssemblyInfo Invalid(string error)
        => new(false, "Unknown", "—", ModLoaderKind.Unknown, ModComponentKind.Unknown, false, false, error);
}
