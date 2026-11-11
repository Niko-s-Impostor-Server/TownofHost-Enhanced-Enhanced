using Hazel;

namespace TOHE;

[HarmonyPatch(typeof(GameManager), nameof(GameManager.Serialize))]
class GameManagerSerializeFix
{
    public static bool Prefix(GameManager __instance, [HarmonyArgument(0)] MessageWriter writer, [HarmonyArgument(1)] bool initialState, ref bool __result)
    {
        bool flag = false;
        for (int index = 0; index < __instance.LogicComponents.Count; ++index)
        {
            GameLogicComponent logicComponent = __instance.LogicComponents[index];
            // The mod sends player-specific options after the initial spawn.
            if (logicComponent.TryCast<LogicOptions>() != null)
            {
                if (!initialState)
                {
                    logicComponent.ClearDirtyFlag();
                    continue;
                }
                if (TOHE.Modules.RoundGameOptions.LobbyOptions != null)
                {
                    flag = true;
                    writer.StartMessage((byte)index);
                    writer.WriteBytesAndSize(GameOptionsManager.Instance.gameOptionsFactory.ToBytes(
                        TOHE.Modules.RoundGameOptions.LobbyOptions, AprilFoolsMode.IsAprilFoolsModeToggledOn));
                    writer.EndMessage();
                    logicComponent.ClearDirtyFlag();
                    continue;
                }
            }
            if (initialState || logicComponent.IsDirty)
            {
                flag = true;
                writer.StartMessage((byte)index);
                var hasBody = logicComponent.Serialize(writer);
                if (hasBody) writer.EndMessage();
                else writer.CancelMessage();
                logicComponent.ClearDirtyFlag();
            }
        }
        __instance.ClearDirtyBits();
        __result = flag;
        return false;
    }
}
