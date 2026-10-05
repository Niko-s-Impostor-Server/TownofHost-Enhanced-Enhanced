using System;
using UnityEngine;

namespace TOHE.Modules;

// https://github.com/tukasa0001/TownOfHost/blob/main/Modules/VersionChecker.cs
public static class VersionChecker
{
    public static bool IsSupported { get; private set; } = true;
    private static bool Ischecked = false;

    public static void Check()
    {
        if (Ischecked) return;

        Version.TryParse(Application.version, out var amongUsVersion);
        Logger.Info($" {amongUsVersion}", "Among Us Version Check");

        var SupportedVersion = Version.Parse(Main.SupportedVersionAU);
        Logger.Info($" {SupportedVersion}", "Supported Version Check");

        IsSupported = amongUsVersion != null
            && amongUsVersion.Major == SupportedVersion.Major
            && amongUsVersion.Minor == SupportedVersion.Minor
            && amongUsVersion.Build == SupportedVersion.Build;
        Logger.Info($" {IsSupported}", "Version Is Supported?");

        if (!IsSupported)
        {
            ErrorText.Instance.AddError(ErrorCode.UnsupportedVersion);
        }

        Ischecked = true;
    }
}
