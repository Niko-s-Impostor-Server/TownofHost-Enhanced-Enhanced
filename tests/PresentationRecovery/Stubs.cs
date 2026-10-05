namespace Hazel
{
    enum SendOption { Reliable }
    sealed class MessageWriter;
}

namespace UnityEngine
{
    class Object
    {
        public nint Pointer;
        public bool Destroyed;
        public static implicit operator bool(Object value) => value != null && !value.Destroyed;
    }
    record struct Color(float r, float g, float b, float a)
    {
        public static Color clear => new(0, 0, 0, 0);
    }
    static class Time { public static float realtimeSinceStartup; }
    sealed class SpriteRenderer : Object
    {
        public bool enabled;
        public Color color;
    }
}

namespace TOHE
{
    enum CustomRPC { RecoverPresentation }
    static class CustomRpcTransport
    {
        public static readonly List<(CustomRPC Rpc, int Recipient)> Requests = [];
        public static int Finished;
        public static Hazel.MessageWriter Start(CustomRPC rpc, Hazel.SendOption option, int recipient)
        {
            Requests.Add((rpc, recipient));
            return new();
        }
        public static void Finish(Hazel.MessageWriter writer) => Finished++;
    }
    sealed class AmongUsClient : UnityEngine.Object
    {
        public static AmongUsClient Instance;
        public bool AmHost, AmConnected;
    }
    sealed class PlayerControl : UnityEngine.Object
    {
        public static PlayerControl LocalPlayer;
        public byte PlayerId;
        public int OwnerId;
        public bool AmOwner, inVent, walkingToVent, onLadder, inMovingPlat;
        public Physics MyPhysics = new();
        public PlayerData Data = new();
        public bool moveable;
        public (float X, float Y) Position;
        public float KillCooldown;
        public List<int> Tasks = [];
    }
    sealed class PlayerData
    {
        public bool Disconnected, IsDead;
        public Role Role = new();
    }
    sealed class Role : UnityEngine.Object { public int RoleId; }
    sealed class Physics
    {
        public Animations Animations = new();
    }
    sealed class Animations
    {
        public bool EnterVent, Ladder;
        public bool IsPlayingEnterVentAnimation() => EnterVent;
        public bool IsPlayingAnyLadderAnimation() => Ladder;
    }
    sealed class PlayerState { public bool IsBlackOut; }
    sealed class PlayerVersion
    {
        public Version version;
        public string forkId, tag;
    }
    static class Main
    {
        public static bool IntroDestroyed;
        public static Dictionary<byte, PlayerState> PlayerStates = [];
        public static Dictionary<int, PlayerVersion> playerVersion = [];
        public static Version version = new(1, 2, 3);
        public static string ForkId = "test-fork";
    }
    static class ThisAssembly
    {
        public static class Git
        {
            public const string Commit = "abc123";
            public const string Branch = "test";
        }
    }
    static class GameStates { public static bool IsInTask, IsEnded; }
    static class OnGameJoinedPatch { public static uint Generation; }
    sealed class ShipStatus : UnityEngine.Object { public static ShipStatus Instance; }
    sealed class ExileController : UnityEngine.Object { public static ExileController Instance; }
    sealed class Minigame : UnityEngine.Object { public static Minigame Instance; }
    sealed class Camera : UnityEngine.Object
    {
        public PlayerControl Target;
        public bool Locked;
        public int Snaps;
        public void SnapToTarget() => Snaps++;
    }
    sealed class HudManager : UnityEngine.Object
    {
        public static HudManager Instance;
        public bool IsIntroDisplayed;
        public Camera PlayerCam;
        public UnityEngine.SpriteRenderer FullScreen;
        public bool Active;
        public float taskDirtyTimer;
        public void SetHudActive(bool active) => Active = active;
    }
}
