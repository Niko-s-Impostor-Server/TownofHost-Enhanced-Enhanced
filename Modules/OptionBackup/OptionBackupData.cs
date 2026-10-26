using AmongUs.GameOptions;
using System;


namespace TOHE;

public class OptionBackupData
{
    internal static readonly RoleTypes[] RoleTypesToBackup =
    [
        RoleTypes.Scientist, RoleTypes.Engineer, RoleTypes.GuardianAngel,
        RoleTypes.Shapeshifter, RoleTypes.Noisemaker, RoleTypes.Phantom,
        RoleTypes.Tracker, RoleTypes.Detective, RoleTypes.Viper,
        RoleTypes.Judge, RoleTypes.SpiritGuide
    ];
    public List<OptionBackupValue> AllValues;
    public OptionBackupData(IGameOptions option)
    {
        AllValues = new(32);

        foreach (ByteOptionNames name in EnumHelper.GetAllValues<ByteOptionNames>())
        {
            if (option.TryGetByte(name, out var value))
                AllValues.Add(new ByteOptionBackupValue(name, value));
        }
        foreach (BoolOptionNames name in EnumHelper.GetAllValues<BoolOptionNames>())
        {
            // TryGetBool also exposes read-only values. Roles summarizes the role
            // rates for lobby filters; neither it nor GhostsDoTasks has a setter.
            if (name is BoolOptionNames.GhostsDoTasks or BoolOptionNames.Roles) continue;

            if (option.TryGetBool(name, out var value))
                AllValues.Add(new BoolOptionBackupValue(name, value));

        }
        foreach (FloatOptionNames name in EnumHelper.GetAllValues<FloatOptionNames>())
        {
            if (option.TryGetFloat(name, out var value))
                AllValues.Add(new FloatOptionBackupValue(name, value));
        }
        foreach (Int32OptionNames name in EnumHelper.GetAllValues<Int32OptionNames>())
        {
            if (name == Int32OptionNames.MaxPlayers) continue;
            if (option.TryGetInt(name, out var value))
                AllValues.Add(new IntOptionBackupValue(name, value));
        }

        // TryGetUInt is not implemented, so get it separately
        AllValues.Add(new UIntOptionBackupValue(UInt32OptionNames.Keywords, (uint)option.Keywords));

        foreach (RoleTypes role in RoleTypesToBackup)
        {
            AllValues.Add(new RoleRateBackupValue(role, option.RoleOptions.GetNumPerGame(role), option.RoleOptions.GetChancePerGame(role)));
        }
    }

    public IGameOptions Restore(IGameOptions option)
    {
        foreach (var value in AllValues.ToArray())
        {
            value.Restore(option);
        }
        return option;
    }

    public byte GetByte(ByteOptionNames name) => Get<ByteOptionNames, byte>(name);
    public bool GetBool(BoolOptionNames name) => Get<BoolOptionNames, bool>(name);
    public float GetFloat(FloatOptionNames name) => Get<FloatOptionNames, float>(name);
    public int GetInt(Int32OptionNames name) => Get<Int32OptionNames, int>(name);
    public uint GetUInt(UInt32OptionNames name) => Get<UInt32OptionNames, uint>(name);
    public TValue Get<TKey, TValue>(TKey name)
    where TKey : Enum
    {
        var value = AllValues
            .OfType<OptionBackupValueBase<TKey, TValue>>()
            .FirstOrDefault(val => val.OptionName.Equals(name));

        return value == null ? default : value.Value;
    }
}
