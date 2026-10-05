using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using TOHE.Modules;

// Compile-only check of the production lifecycle calls against the installed APIs.
internal sealed class PluginLifecycle : BasePlugin
{
    private readonly Harmony harmony = new("local.tohee.mod-registration.compile-check");

    public override void Load()
    {
        Log.LogInfo($"AU MCI GUID: {ModRegistration.Register("2026.1005.211.1")}");
    }

    public override bool Unload()
    {
        harmony.UnpatchSelf();
        ModRegistration.Unregister();
        return true;
    }
}
