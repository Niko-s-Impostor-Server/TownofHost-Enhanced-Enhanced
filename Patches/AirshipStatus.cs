using BepInEx.Unity.IL2CPP.Utils.Collections;
using System.Collections;
using UnityEngine;

namespace TOHE;

//参考元:https://github.com/yukieiji/ExtremeRoles/blob/master/ExtremeRoles/Patches/AirshipStatusPatch.cs
[HarmonyPatch(typeof(AirshipStatus), nameof(AirshipStatus.PrespawnStep))]
public static class AirshipStatusPrespawnStepPatch
{
    public static bool Prefix(ref Il2CppSystem.Collections.IEnumerator __result)
    {
        if (PlayerControl.LocalPlayer.Is(CustomRoles.GM))
        {
            RandomSpawn.AirshipSpawn(PlayerControl.LocalPlayer);
            // GM skips gushing screen
            return false;
        }
        if (!AmongUsClient.Instance.AmHost && GameStates.IsModHost && RandomSpawn.IsRandomSpawn())
        {
            __result = RequestRandomSpawn(OfficialSessionContext.Capture()).WrapToIl2Cpp();
            return false;
        }
        return true;
    }

    private static IEnumerator RequestRandomSpawn(OfficialSessionContext context)
    {
        var player = PlayerControl.LocalPlayer;
        if (!context.IsCurrent() || !player) yield break;

        var netTransform = player.NetTransform;
        bool wasKinematic = player.rigidbody2D.isKinematic;
        bool wasPaused = netTransform.isPaused;
        bool completed = false;
        player.SetKinematic(true);
        netTransform.SetPaused(true);
        try
        {
            // A vanilla spawn point requests the host's authoritative random
            // spawn through the same SnapTo path as the original picker.
            netTransform.RpcSnapTo(new Vector2(-0.7f, 8.5f));
            // Match SpawnInMinigame.CoSpawnAt's physics-settle interval.
            for (int step = 0; step < 3; step++)
            {
                yield return new WaitForFixedUpdate();
                if (!context.IsCurrent()) yield break;
            }
            completed = true;
        }
        finally
        {
            if (context.IsCurrent() && player && netTransform)
            {
                player.SetKinematic(completed ? false : wasKinematic);
                netTransform.SetPaused(completed ? false : wasPaused);
            }
        }
    }
}
