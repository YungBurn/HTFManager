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
            var assemblyVersion = definition.Version.ToString();
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

            var bepinIdentity = hasBepInExPlugin ? TryReadBepInPluginIdentity(reader) : null;
            var displayName = bepinIdentity?.Name ?? assemblyName;
            var version = NormalizeVersion(bepinIdentity?.Version, assemblyVersion);
            var intrinsicId = NormalizeIntrinsicId(bepinIdentity?.Guid);

            if (hasBepInExPlugin && (hasMelonMod || hasMelonPlugin))
                return new ManagedAssemblyInfo(true, assemblyName, version, displayName, intrinsicId,
                    ModLoaderKind.Unknown, ModComponentKind.Unknown,
                    referencesBepInEx, referencesMelonLoader, "The assembly contains both BepInEx and MelonLoader entry types.");

            if (hasBepInExPlugin)
                return new ManagedAssemblyInfo(true, assemblyName, version, displayName, intrinsicId,
                    ModLoaderKind.BepInEx, ModComponentKind.Plugin,
                    referencesBepInEx, referencesMelonLoader, null);

            if (hasMelonMod && hasMelonPlugin)
                return new ManagedAssemblyInfo(true, assemblyName, assemblyVersion, assemblyName, null,
                    ModLoaderKind.MelonLoader, ModComponentKind.Unknown,
                    referencesBepInEx, referencesMelonLoader, null);

            if (hasMelonPlugin)
                return new ManagedAssemblyInfo(true, assemblyName, assemblyVersion, assemblyName, null,
                    ModLoaderKind.MelonLoader, ModComponentKind.Plugin,
                    referencesBepInEx, referencesMelonLoader, null);

            if (hasMelonMod)
                return new ManagedAssemblyInfo(true, assemblyName, assemblyVersion, assemblyName, null,
                    ModLoaderKind.MelonLoader, ModComponentKind.Mod,
                    referencesBepInEx, referencesMelonLoader, null);

            var error = referencesMelonLoader
                ? "The DLL references MelonLoader but no MelonMod or MelonPlugin entry type was found."
                : referencesBepInEx
                    ? "The DLL references BepInEx but no BaseUnityPlugin entry type was found."
                    : "The DLL was not recognized as a BepInEx or MelonLoader mod.";

            return new ManagedAssemblyInfo(true, assemblyName, assemblyVersion, assemblyName, null,
                ModLoaderKind.Unknown, ModComponentKind.Unknown,
                referencesBepInEx, referencesMelonLoader, error);
        }
        catch (Exception ex)
        {
            return ManagedAssemblyInfo.Invalid(ex.Message);
        }
    }

    private static BepInPluginIdentity? TryReadBepInPluginIdentity(MetadataReader reader)
    {
        BepInPluginIdentity? found = null;
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(typeHandle);
            foreach (var attributeHandle in type.GetCustomAttributes())
            {
                var attribute = reader.GetCustomAttribute(attributeHandle);
                if (!AttributeTypeMatches(reader, attribute.Constructor, "BepInEx", "BepInPlugin"))
                    continue;

                var value = reader.GetBlobReader(attribute.Value);
                if (value.RemainingBytes < 2 || value.ReadUInt16() != 1)
                    continue;

                var guid = value.ReadSerializedString();
                var name = value.ReadSerializedString();
                var version = value.ReadSerializedString();
                if (string.IsNullOrWhiteSpace(guid) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(version))
                    continue;

                var candidate = new BepInPluginIdentity(guid.Trim(), name.Trim(), version.Trim());
                if (found is null)
                {
                    found = candidate;
                    continue;
                }

                // Multiple different BepInPlugin identities in one assembly are not a single deterministic package identity.
                if (!found.Guid.Equals(candidate.Guid, StringComparison.OrdinalIgnoreCase) ||
                    !found.Name.Equals(candidate.Name, StringComparison.Ordinal) ||
                    !found.Version.Equals(candidate.Version, StringComparison.OrdinalIgnoreCase))
                    return null;
            }
        }

        return found;
    }

    private static bool AttributeTypeMatches(
        MetadataReader reader,
        EntityHandle constructor,
        string targetNamespace,
        string targetName)
    {
        EntityHandle attributeType = constructor.Kind switch
        {
            HandleKind.MemberReference => reader.GetMemberReference((MemberReferenceHandle)constructor).Parent,
            HandleKind.MethodDefinition => reader.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType(),
            _ => default
        };

        if (attributeType.IsNil)
            return false;

        return attributeType.Kind switch
        {
            HandleKind.TypeReference => TypeReferenceMatches(reader, (TypeReferenceHandle)attributeType, targetNamespace, targetName),
            HandleKind.TypeDefinition => TypeDefinitionMatches(reader, (TypeDefinitionHandle)attributeType, targetNamespace, targetName),
            _ => false
        };
    }

    private static bool TypeReferenceMatches(
        MetadataReader reader,
        TypeReferenceHandle handle,
        string targetNamespace,
        string targetName)
    {
        var type = reader.GetTypeReference(handle);
        return reader.GetString(type.Namespace).Equals(targetNamespace, StringComparison.Ordinal) &&
               reader.GetString(type.Name).Equals(targetName, StringComparison.Ordinal);
    }

    private static bool TypeDefinitionMatches(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        string targetNamespace,
        string targetName)
    {
        var type = reader.GetTypeDefinition(handle);
        return reader.GetString(type.Namespace).Equals(targetNamespace, StringComparison.Ordinal) &&
               reader.GetString(type.Name).Equals(targetName, StringComparison.Ordinal);
    }

    private static string NormalizeVersion(string? intrinsicVersion, string assemblyVersion)
        => string.IsNullOrWhiteSpace(intrinsicVersion) ? assemblyVersion : intrinsicVersion.Trim();

    private static string? NormalizeIntrinsicId(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
        {
            var baseHandle = (TypeDefinitionHandle)baseType;
            if (TypeDefinitionMatches(reader, baseHandle, targetNamespace, targetName))
                return true;
            return DerivesFrom(reader, baseHandle, targetNamespace, targetName, visited);
        }

        return false;
    }

    private sealed record BepInPluginIdentity(string Guid, string Name, string Version);
}

internal sealed record ManagedAssemblyInfo(
    bool IsManaged,
    string AssemblyName,
    string Version,
    string DisplayName,
    string? IntrinsicId,
    ModLoaderKind Loader,
    ModComponentKind Component,
    bool ReferencesBepInEx,
    bool ReferencesMelonLoader,
    string? Error)
{
    public static ManagedAssemblyInfo Invalid(string error)
        => new(false, "Unknown", "—", "Unknown", null,
            ModLoaderKind.Unknown, ModComponentKind.Unknown, false, false, error);
}
