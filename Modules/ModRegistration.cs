using System;
using System.Security.Cryptography;
using System.Text;

namespace TOHE.Modules;

internal static class ModRegistration
{
    private static Guid? ownedGuid;

    // RFC 9562 UUIDv5: DNS namespace, UTF-8 name "TOHEE:" + PluginVersion.
    internal static Guid CreateGuid(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("A mod version is required.", nameof(version));

        // Namespace bytes are in RFC/network order, not Guid.ToByteArray() order.
        byte[] dnsNamespace = [0x6b, 0xa7, 0xb8, 0x10, 0x9d, 0xad, 0x11, 0xd1,
            0x80, 0xb4, 0x00, 0xc0, 0x4f, 0xd4, 0x30, 0xc8];
        var name = Encoding.UTF8.GetBytes("TOHEE:" + version);
        var input = new byte[dnsNamespace.Length + name.Length];
        dnsNamespace.CopyTo(input, 0);
        name.CopyTo(input, dnsNamespace.Length);
        var hash = SHA1.HashData(input);
        hash[6] = (byte)((hash[6] & 0x0f) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3f) | 0x80);

        // Parsing the canonical order avoids .NET's mixed-endian Guid(byte[]) constructor.
        return Guid.ParseExact(Convert.ToHexString(hash, 0, 16), "N");
    }

    internal static string Register(string version)
    {
        var guid = CreateGuid(version);
        var current = CurrentModRegistration.ModRegistrationGuidString;
        if (!string.IsNullOrEmpty(current) &&
            (!Guid.TryParse(current, out var registered) || registered != guid))
        {
            throw new InvalidOperationException(
                $"Cannot register TOHEE {version}: another mod registration is already active ({current}).");
        }

        var guidString = guid.ToString("D");
        CurrentModRegistration.ModRegistrationGuidString = guidString;
        ownedGuid = guid;
        return guidString;
    }

    internal static void Unregister()
    {
        if (ownedGuid.HasValue &&
            Guid.TryParse(CurrentModRegistration.ModRegistrationGuidString, out var current) &&
            current == ownedGuid.Value)
        {
            CurrentModRegistration.ModRegistrationGuidString = string.Empty;
        }
        ownedGuid = null;
    }

    internal static string SuspendForLocalHost()
    {
        var current = CurrentModRegistration.ModRegistrationGuidString;
        if (!ownedGuid.HasValue || !Guid.TryParse(current, out var guid) || guid != ownedGuid.Value)
            return null;

        CurrentModRegistration.ModRegistrationGuidString = string.Empty;
        return current;
    }

    internal static void RestoreAfterLocalHost(string suspended)
    {
        // Unload can revoke ownership, or another mod can replace the field while suspended.
        if (ownedGuid.HasValue && Guid.TryParse(suspended, out var guid) && guid == ownedGuid.Value &&
            string.IsNullOrEmpty(CurrentModRegistration.ModRegistrationGuidString))
        {
            CurrentModRegistration.ModRegistrationGuidString = suspended;
        }
    }
}
