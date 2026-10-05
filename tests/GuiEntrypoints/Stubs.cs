public enum TabGroup { SystemSettings, ModSettings }
public enum StringNames { GameMapName, Other, Skeld, Mira, Polus, Dleks, Airship }

public class GameObject
{
    public bool activeInHierarchy = true;
    public bool Destroyed;
    public readonly List<string> Events = new();
    public void SetActive(bool value) { activeInHierarchy = value; Events.Add(value ? "show" : "hide"); }
}
public class Transform
{
    public Transform Parent;
    public bool IsChildOf(Transform ancestor) => this == ancestor || Parent != null && Parent.IsChildOf(ancestor);
}
public class Coroutine { public bool Stopped; }
public class OptionBehaviour { public GameObject gameObject = new(); }
public class CategoryHeaderMasked
{
    public GameObject gameObject = new();
    public Transform transform = new();
}
public class GameOptionsMenu
{
    private static int nextId;
    private readonly int id = ++nextId;
    public GameObject gameObject = new();
    public List<OptionBehaviour> Children = new();
    public List<object> ControllerSelectable = new();
    public Transform settingsContainer = new();
    public int GetInstanceID() => id;
    public void StopCoroutine(Coroutine coroutine) { coroutine.Stopped = true; gameObject.Events.Add("stop"); }
}
public class TextLabel { public string text; }
public class StringOption : OptionBehaviour
{
    public StringNames Title;
    public StringNames[] Values;
    public int Value, oldValue;
    public TextLabel ValueText = new();
    public bool Plus, Minus;
    public int AdjustCalls;
    public void AdjustButtonsActiveState()
    {
        AdjustCalls++;
        Minus = Value != 0;
        Plus = Value != Values.Length - 1;
    }
}
public class GameOptions { public byte MapId; }
public class GameOptionsManager
{
    public static GameOptionsManager Instance = new();
    public GameOptions CurrentGameOptions = new();
}
public class TranslationController { public string GetString(StringNames name) => name.ToString(); }
public class DestroyableSingleton<T> where T : new() { public static T Instance = new(); }
public class LobbyBehaviour
{
    public static LobbyBehaviour Instance;
    public GameObject gameObject = new();
    public object MapTheme = new();
}
public interface ISoundPlayer { string Name { get; } }
public record SoundPlayer(string Name) : ISoundPlayer;
public class SoundManager
{
    public static SoundManager Instance = new();
    public List<ISoundPlayer> soundPlayers = new();
    public int Stops, Starts;
    public void StopNamedSound(string name) { Stops++; soundPlayers.RemoveAll(player => player.Name == name); }
    public void CrossFadeSound(string name, object clip, float duration) { Starts++; soundPlayers.Add(new SoundPlayer(name)); }
}
namespace UnityEngine
{
    public static class Object
    {
        public static void Destroy(GameObject value) => value.Destroyed = true;
    }
}
namespace TOHE
{
    public static class ModGameOptionsMenu
    {
        public static Dictionary<OptionBehaviour, int> OptionList = new();
        public static Dictionary<int, OptionBehaviour> BehaviourList = new();
        public static Dictionary<int, CategoryHeaderMasked> CategoryHeaderList = new();
    }
    public sealed class BooleanConfig { public bool Value; }
    public static class Main { public static readonly BooleanConfig DisableLobbyMusic = new(); }
    public static class GameStates { public static bool IsLobby = true; }
}
