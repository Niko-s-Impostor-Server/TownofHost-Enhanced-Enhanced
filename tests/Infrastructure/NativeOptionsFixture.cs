namespace AmongUs.GameOptions;

// Only SetBool/TryGetBool and enums are extracted native code. This fixture
// supplies their data/role collection; byte/int/float plumbing is a minimal stub.
public interface IGameOptions
{
    int MaxPlayers { get; }
    uint Keywords { get; }
    RoleCollectionFixture RoleOptions { get; }
    bool TryGetByte(ByteOptionNames name, out byte value);
    bool TryGetBool(BoolOptionNames name, out bool value);
    bool TryGetFloat(FloatOptionNames name, out float value);
    bool TryGetInt(Int32OptionNames name, out int value);
    void SetByte(ByteOptionNames name, byte value);
    void SetBool(BoolOptionNames name, bool value);
    void SetFloat(FloatOptionNames name, float value);
    void SetInt(Int32OptionNames name, int value);
    void SetUInt(UInt32OptionNames name, uint value);
}

public sealed class ShapeshifterRoleOptionsV11 { public bool ShapeshifterLeaveSkin; }
public sealed class GuardianAngelRoleOptionsV11 { public bool ImpostorsCanSeeProtect; }
public sealed class NoisemakerRoleOptionsV11 { public bool NoisemakerImpostorAlert; }
public sealed class RoleCollectionFixture
{
    private readonly Dictionary<RoleTypes, (int Count, int Chance)> rates = [];
    private readonly Dictionary<Type, object> options = [];
    public bool TryGetRoleOptions<T>(RoleTypes role, out T data) where T : new()
    {
        if (!options.TryGetValue(typeof(T), out var stored)) options[typeof(T)] = stored = new T();
        data = (T)stored;
        return true;
    }
    public int GetNumPerGame(RoleTypes role) => rates.GetValueOrDefault(role).Count;
    public int GetChancePerGame(RoleTypes role) => rates.GetValueOrDefault(role).Chance;
    public void SetRoleRate(RoleTypes role, int count, int chance) => rates[role] = (count, chance);
    public bool AnyRolesEnabled() => rates.Values.Any(rate => rate.Count > 0 && rate.Chance > 0);
}
public sealed class NativeLoggerFixture
{
    public readonly List<string> Errors = [];
    public void WriteError(string message) => Errors.Add(message);
}
public abstract class NativeOptionsFixture : IGameOptions
{
    public bool VisualTasks, ConfirmImpostor, AnonymousVotes, GhostsDoTasks = true, IsDefaults;
    public bool useFlashlight, SeekerFinalMap, SeekerPings, ShowCrewmateNames;
    protected readonly RoleCollectionFixture roleOptions = new();
    public readonly NativeLoggerFixture logger = new();
    public string GameMode => GetType().Name;
    public int Version => 11;
    public int MaxPlayers { get; private set; } = 15;
    public uint Keywords { get; private set; } = 123;
    public RoleCollectionFixture RoleOptions => roleOptions;
    public abstract bool TryGetBool(BoolOptionNames name, out bool value);
    public abstract void SetBool(BoolOptionNames name, bool value);
    public bool TryGetByte(ByteOptionNames name, out byte value) { value = 0; return false; }
    public bool TryGetFloat(FloatOptionNames name, out float value) { value = 0; return false; }
    public bool TryGetInt(Int32OptionNames name, out int value) { value = 0; return false; }
    public void SetByte(ByteOptionNames name, byte value) { }
    public void SetFloat(FloatOptionNames name, float value) { }
    public void SetInt(Int32OptionNames name, int value) { if (name == Int32OptionNames.MaxPlayers) MaxPlayers = value; }
    public void SetUInt(UInt32OptionNames name, uint value) { if (name == UInt32OptionNames.Keywords) Keywords = value; }
}
