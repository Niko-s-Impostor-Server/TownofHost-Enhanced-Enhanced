using System;
using System.Linq;
using AmongUs.GameOptions;
using TOHE;
using TOHE.Roles.Core;
using UnityEngine;

static class Program
{
    static int assertions;
    static void Check(bool value, string message)
    {
        if (!value) throw new Exception("FAIL: " + message);
        assertions++;
    }
    static PlayerControl Reset(bool host = true, bool owner = true)
    {
        Time.realtimeSinceStartup = 0;
        TOHE.Main.PlayerStates.Clear();
        TOHE.Main.AllAlivePlayerControls.Clear();
        AmongUsClient.Instance = new() { AmHost = host };
        GameStates.IsInTask = true;
        AntiBlackout.SkipTasks = false;
        Minigame.Instance = null;
        var player = new PlayerControl { PlayerId = 1, NetId = 1001, AmOwner = owner };
        ((PhantomRole)player.Data.Role).Player = player;
        var role = new Disperser();
        var state = new PlayerState { PlayerId = 1, RoleClass = role };
        role._state = state;
        TOHE.Main.PlayerStates.Add(1, state);
        TOHE.Main.AllAlivePlayerControls.Add(player);
        TOHE.Main.AllAlivePlayerControls.Add(new() { PlayerId = 2, NetId = 1002 });
        return player;
    }
    static void HostAbilityAndFallback()
    {
        var player = Reset();
        var role = player.GetRoleClass();
        role.ApplyGameOptions(null, player.PlayerId);
        Check(AURoleOptions.PhantomCooldown == AURoleOptions.ShapeshifterCooldown && AURoleOptions.PhantomDuration == 0,
            "both buttons keep original cooldown and silent Phantom duration");
        Check(role.ThisRoleBase == CustomRoles.Shapeshifter, "vanilla assignment keeps original Shapeshifter fallback");
        Check(!PhantomPatchFixture.Use((PhantomRole)player.Data.Role), "silent button skips native animation path");
        Check(TOHE.Main.AllAlivePlayerControls.All(p => p.Teleports == 1 && p.Sounds == 1 && p.Notifications == 1),
            "Disperser retains teleport/sound/notification effects for eligible alive players");
        var writer = AmongUsClient.Instance.Finished.Single();
        Check(writer.CallId == 65 && writer.NetId == 1001 && writer.TargetId == -1 && writer.Values.SequenceEqual(new object[] { false }),
            "host confirmation is a legal StartAppear broadcast with false animation");
        var phantom = (PhantomRole)player.Data.Role;
        Check(!phantom.invisible && !phantom.fading && !phantom.approval && player.VisibleRestores == 1,
            "successful use stays visible without fading or server-approval wait");
        Check(!PhantomAbility.TryActivate(player) && player.Teleports == 1, "repeat request is rejected by host cooldown");
        Time.realtimeSinceStartup = 19.99f;
        Check(!PhantomAbility.TryActivate(player), "host deadline retains original twenty-second cooldown");
        player.Data.Role = new NativeRole { Role = RoleTypes.Shapeshifter };
        bool reset = true, animate = true;
        Check(!role.OnCheckShapeshift(player, TOHE.Main.AllAlivePlayerControls[1], ref reset, ref animate) && !reset && player.Teleports == 1,
            "fallback cannot bypass cooldown reserved by Phantom button");
        Time.realtimeSinceStartup = 20f;
        Check(!role.OnCheckShapeshift(player, TOHE.Main.AllAlivePlayerControls[1], ref reset, ref animate) && reset && player.Teleports == 2,
            "Shapeshifter fallback uses the same ability once deadline expires");
        Check(AmongUsClient.Instance.Finished.All(w => w.CallId != 63), "no native StartVanish is sent");
    }
    static void RemoteOwnerRequest()
    {
        var player = Reset(host: false);
        PhantomPatchFixture.Use((PhantomRole)player.Data.Role);
        var writer = AmongUsClient.Instance.Finished.Single();
        Check(writer.CallId == 62 && writer.NetId == player.NetId && writer.TargetId == AmongUsClient.Instance.HostId
            && writer.SendOption == Hazel.SendOption.Reliable && writer.Values.SequenceEqual(new object[] { 0f }),
            "non-host sends only own Phantom request to current host");
        Check(player.Teleports == 0 && !((PhantomRole)player.Data.Role).fading && !((PhantomRole)player.Data.Role).invisible,
            "non-host neither executes ability nor starts invisibility");
        Check(!PhantomAbility.TryActivate(player), "non-host cannot adjudicate skill");
        Check(!PhantomPatchFixture.HandleServerAppear_Prefix(player) && player.VisibleRestores == 1,
            "host response restores modded visible button locally");
        Check(AmongUsClient.Instance.Finished.Count == 1, "response restoration sends no client host-only RPC");
        player.AmOwner = false;
        PhantomAbility.Request(player, 0f);
        Check(AmongUsClient.Instance.Finished.Count == 1, "non-owner cannot send another player's request");
        player.AmOwner = true;
        AmongUsClient.Instance.AmConnected = false;
        PhantomAbility.Request(player, 0f);
        Check(AmongUsClient.Instance.Finished.Count == 1, "disconnected owner request sends nothing");
    }
    static void InvalidContext()
    {
        Action<PlayerControl>[] changes = [
            _ => AmongUsClient.Instance.AmHost = false,
            _ => AmongUsClient.Instance.AmConnected = false,
            _ => AmongUsClient.Instance.IsGameOver = true,
            _ => GameStates.IsInTask = false,
            _ => AntiBlackout.SkipTasks = true,
            p => p.Data.IsDead = true,
            p => p.Data.Disconnected = true,
            p => p.Data.Role = null,
            p => p.Data.Role = new NativeRole { Role = RoleTypes.Crewmate },
            p => p.inVent = true,
            p => p.walkingToVent = true,
            p => p.inMovingPlat = true,
            p => p.GetRoleClass()._state = new() { PlayerId = 1, RoleClass = p.GetRoleClass() },
            p => TOHE.Main.PlayerStates.Remove(p.PlayerId),
        ];
        foreach (var change in changes)
        {
            var player = Reset();
            change(player);
            Check(!PhantomAbility.TryActivate(player) && player.Teleports == 0, "invalid host context cannot consume ability");
            PhantomPatchFixture.Check(player);
            Check(AmongUsClient.Instance.Finished.Count == 0, "invalid host context sends no confirmation");
        }
        var valid = Reset();
        var invalid = TOHE.Main.AllAlivePlayerControls[1];
        invalid.Teleportable = false;
        Check(PhantomAbility.TryActivate(valid) && valid.Teleports == 1 && invalid.Teleports == 0 && invalid.Notifications == 1,
            "Disperser skips ineligible target and keeps error notification");
    }
    sealed class LimitedRole : RoleBase
    {
        public override bool UsesPhantomAbility => true;
        public override float PhantomAbilityCooldown => 2;
        public int Uses = 1;
        public bool Reentered;
        public override bool OnPhantomAbility(PlayerControl player)
        {
            if (Uses == 0) return false;
            Reentered = PhantomAbility.TryActivate(player);
            Uses--;
            return true;
        }
    }
    static void LimitedUsesAndReentry()
    {
        var player = Reset();
        var limited = new LimitedRole { _state = TOHE.Main.PlayerStates[1] };
        TOHE.Main.PlayerStates[1].RoleClass = limited;
        Check(PhantomAbility.TryActivate(player) && limited.Uses == 0 && !limited.Reentered,
            "API allows role-owned use limits and rejects nested duplicate activation");
        Time.realtimeSinceStartup = 2;
        Check(!PhantomAbility.TryActivate(player) && limited.Uses == 0 && limited.PhantomAbilityReadyAt == 2,
            "exhausted ability does not consume another charge or reserve new cooldown");
    }
    public static void Main()
    {
        HostAbilityAndFallback(); RemoteOwnerRequest(); InvalidContext(); LimitedUsesAndReentry();
        Console.WriteLine($"PHANTOM_ABILITY_PASS ({assertions} assertions; linked helper and extracted production statements, offline stubs)");
    }
}
