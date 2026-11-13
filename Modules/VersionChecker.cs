using System;
using UnityEngine;

namespace TOHE.Modules;

// https://github.com/tukasa0001/TownOfHost/blob/main/Modules/VersionChecker.cs
public static class VersionChecker
{
    private static readonly Version[] SupportedVersions = Main.SupportedVersionAU.Select(Version.Parse).ToArray();

    public static bool IsSupported { get; private set; } = true;
    private static bool Ischecked = false;

    public static void Check()
    {
        if (Ischecked) return;

        Version.TryParse(Application.version, out var amongUsVersion);
        Logger.Info($" {amongUsVersion}", "Among Us Version Check");

        Logger.Info($" {string.Join<Version>(", ", SupportedVersions)}", "Supported Version Check");
        IsSupported = amongUsVersion != null
            && SupportedVersions.Any(version => amongUsVersion.Major == version.Major
                && amongUsVersion.Minor == version.Minor
                && amongUsVersion.Build == version.Build);
        Logger.Info($" {IsSupported}", "Version Is Supported?");

        if (!IsSupported)
        {
            ErrorText.Instance.AddError(ErrorCode.UnsupportedVersion);
        }

        Ischecked = true;
    }
}
