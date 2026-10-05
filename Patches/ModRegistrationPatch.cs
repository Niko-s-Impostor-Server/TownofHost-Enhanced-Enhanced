using InnerNet;
using System;
using TOHE.Modules;

namespace TOHE.Patches;

[HarmonyPatch(typeof(InnerNetClient), nameof(InnerNetClient.HostGame))]
internal static class LocalHostModRegistrationPatch
{
    public static void Prefix(InnerNetClient __instance, out string __state)
    {
        __state = __instance.NetworkMode is NetworkModes.LocalGame or NetworkModes.FreePlay
            ? ModRegistration.SuspendForLocalHost()
            : null;
    }

    public static Exception Finalizer(string __state, Exception __exception)
    {
        ModRegistration.RestoreAfterLocalHost(__state);
        return __exception;
    }
}
