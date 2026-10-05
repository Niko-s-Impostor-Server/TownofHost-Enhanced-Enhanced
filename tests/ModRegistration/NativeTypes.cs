// Offline stand-in for the native field; no game client is initialized.
internal static class CurrentModRegistration
{
    internal static string ModRegistrationGuidString = string.Empty;
}

internal enum NetworkModes { LocalGame, OnlineGame, FreePlay }

namespace InnerNet
{
    internal sealed class InnerNetClient
    {
        internal NetworkModes NetworkMode;
        public void HostGame() { }
    }
}

namespace HarmonyLib
{
    [System.AttributeUsage(System.AttributeTargets.Class)]
    internal sealed class HarmonyPatch : System.Attribute
    {
        public HarmonyPatch(System.Type type, string method) { }
    }
}
