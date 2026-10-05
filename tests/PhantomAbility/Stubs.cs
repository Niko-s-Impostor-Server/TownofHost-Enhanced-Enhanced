using System;
using System.Collections.Generic;
using System.Linq;

namespace AmongUs.GameOptions
{
    public enum RoleTypes { Phantom, Shapeshifter, Crewmate }
    public interface IGameOptions { }
}
namespace Hazel
{
    public enum SendOption { Reliable }
    public sealed class MessageWriter
    {
        public uint NetId;
        public byte CallId;
        public int TargetId;
        public SendOption SendOption;
        public readonly List<object> Values = [];
        public void Write(float value) => Values.Add(value);
        public void Write(bool value) => Values.Add(value);
    }
}
namespace UnityEngine
{
    public static class Time { public static float realtimeSinceStartup; }
    public static class Mathf { public static float Max(float a, float b) => Math.Max(a, b); }
}
namespace TOHE
{
    using AmongUs.GameOptions;
    using Hazel;
    public enum CustomRoles { Shapeshifter, Disperser }
    public enum RpcCalls : byte { CheckVanish = 62, StartVanish = 63, StartAppear = 65 }
    public sealed class PlayerState { public byte PlayerId; public RoleBase RoleClass; }
    public partial class RoleBase
    {
        public PlayerState _state;
        public PlayerControl _Player => _state == null ? null : Main.AllAlivePlayerControls.FirstOrDefault(p => p.PlayerId == _state.PlayerId);
        public virtual CustomRoles ThisRoleBase => CustomRoles.Shapeshifter;
        public virtual bool OnCheckShapeshift(PlayerControl player, PlayerControl target, ref bool reset, ref bool animate) => true;
        public virtual void ApplyGameOptions(IGameOptions opt, byte id) { }
        public virtual void SetAbilityButtonText(HudManager hud, byte id) { }
    }
    public class NativeRole
    {
        public RoleTypes Role;
        public object Ability = new();
    }
    public sealed class PhantomRole : NativeRole
    {
        public PhantomRole() => Role = RoleTypes.Phantom;
        public PlayerControl Player;
        public bool IsCoolingDown, fading, invisible, approval;
        public int CooldownCalls;
        public void SetInvisible(bool value) => invisible = value;
        public void SetFading(bool value) => fading = value;
        public void SetServerApproval(bool value) => approval = value;
        public void SetCooldown() { CooldownCalls++; IsCoolingDown = true; }
    }
    public sealed class PlayerData { public bool IsDead, Disconnected; public NativeRole Role; }
    public sealed class PlayerControl
    {
        public byte PlayerId;
        public uint NetId;
        public bool AmOwner, moveable = true, walkingToVent, inMovingPlat, inVent, Teleportable = true;
        public PlayerData Data = new() { Role = new PhantomRole() };
        public int Teleports, Sounds, Notifications, VisibleRestores, VanillaVanishes;
        public RoleBase GetRoleClass() => Main.PlayerStates.GetValueOrDefault(PlayerId)?.RoleClass;
        public bool IsAlive() => !Data.IsDead;
        public bool CanBeTeleported() => Teleportable;
        public void Notify(string value) => Notifications++;
        public void RpcRandomVentTeleport() => Teleports++;
        public void RPCPlayCustomSound(string value) => Sounds++;
        public void ForcePhantomVisible() => VisibleRestores++;
        public void CmdCheckVanish(float duration) { if (PhantomPatchFixture.Cmd(this, duration)) VanillaVanishes++; }
        public void CheckVanish() { if (PhantomPatchFixture.Check(this)) VanillaVanishes++; }
        public void RpcAppear(bool animate)
        {
            var client = AmongUsClient.Instance;
            var writer = client.StartRpcImmediately(NetId, (byte)RpcCalls.StartAppear, SendOption.Reliable, -1);
            writer.Write(animate);
            client.FinishRpcImmediately(writer);
        }
    }
    public sealed class AmongUsClient
    {
        public static AmongUsClient Instance;
        public bool AmHost = true, AmConnected = true, IsGameOver;
        public int HostId = 42;
        public readonly List<MessageWriter> Finished = [];
        public MessageWriter StartRpcImmediately(uint netId, byte call, SendOption send, int target = -1)
            => new() { NetId = netId, CallId = call, SendOption = send, TargetId = target };
        public void FinishRpcImmediately(MessageWriter writer) => Finished.Add(writer);
    }
    public sealed class Minigame
    {
        public static Minigame Instance;
        public static bool operator !(Minigame value) => value == null;
    }
    public static class GameStates { public static bool IsInTask = true; }
    public static class AntiBlackout { public static bool SkipTasks; }
    public static class Main
    {
        public static readonly Dictionary<byte, PlayerState> PlayerStates = [];
        public static readonly List<PlayerControl> AllAlivePlayerControls = [];
    }
    public sealed class OptionItem(float value) { public float GetFloat() => value; }
    public static class AURoleOptions
    {
        public static float ShapeshifterCooldown, ShapeshifterDuration, PhantomCooldown, PhantomDuration;
    }
    public sealed class Button { public int SettingsRestores; public void SetFromSettings(object value) => SettingsRestores++; }
    public sealed class HudManager { public readonly Button AbilityButton = new(); }
    public static class DestroyableSingleton<T> where T : new()
    {
        public static bool InstanceExists = true;
        public static T Instance = new();
    }
    public static class Utils
    {
        public static string ColorString(string color, string text) => text;
        public static string GetRoleColor(CustomRoles role) => "fixture";
    }
    public static class Translator { public static string GetString(string key) => key; }
}
