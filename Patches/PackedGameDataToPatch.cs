using Hazel;
using InnerNet;
using System.Reflection;

namespace TOHE.Patches;

[HarmonyPatch(typeof(InnerNetServer), "HandleMessage")]
internal static class PackedGameDataToPatch
{
    private static bool Prefix(InnerNetServer __instance, [HarmonyArgument(1)] MessageReader reader,
        [HarmonyArgument(2)] SendOption sendOption, object[] __args, MethodBase __originalMethod)
    {
        if (reader.Tag != 26) return true;

        if (reader.ReadPackedInt32() == InnerNetServer.LocalGameId)
        {
            while (reader.Position < reader.Length)
            {
                var packed = reader.ReadMessage();
                try
                {
                    if (packed.Tag == 6)
                        __originalMethod.Invoke(__instance, [__args[0], packed, sendOption]);
                }
                finally
                {
                    packed.Recycle();
                }
            }
        }

        return false;
    }
}
