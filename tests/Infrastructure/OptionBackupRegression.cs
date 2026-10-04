using AmongUs.GameOptions;
using TOHE;

static class OptionBackupRegression
{
    internal static void Run(Action<bool, string> check)
    {
        var normal = new NativeNormalFixture();
        int index = 1;
        foreach (var role in NativeRoleContract.Available)
            normal.RoleOptions.SetRoleRate(role, index++, 20 + index);
        check(normal.TryGetBool(BoolOptionNames.Roles, out var enabled) && enabled,
            "actual native TryGetBool exposes derived Roles as readable");
        normal.SetBool(BoolOptionNames.Roles, false);
        check(normal.logger.Errors.Count == 1,
            "actual native SetBool rejects the readable Roles key");
        normal.logger.Errors.Clear();

        RoundTrip(normal, new NativeNormalFixture(), check);
        RoundTrip(new NativeHideNSeekFixture(), new NativeHideNSeekFixture(), check);

        var backup = new OptionBackupData(normal);
        var rates = backup.AllValues.OfType<RoleRateBackupValue>().Select(value => value.roleType).ToArray();
        check(rates.Length == 10 && rates.Distinct().Count() == 10 && rates.Order().SequenceEqual(NativeRoleContract.Available.Order()),
            "backup rates match all ten actual native available roles without base or ghost roles");
        var restored = new NativeNormalFixture();
        backup.Restore(restored);
        foreach (var role in NativeRoleContract.Available)
            check(restored.RoleOptions.GetNumPerGame(role) == normal.RoleOptions.GetNumPerGame(role) &&
                  restored.RoleOptions.GetChancePerGame(role) == normal.RoleOptions.GetChancePerGame(role),
                "old or newly introduced role rate survives restore: " + role);
        check(restored.TryGetBool(BoolOptionNames.Roles, out enabled) && enabled,
            "Roles derived display value remains available through restored role rates");

        new BoolOptionBackupValue(BoolOptionNames.Roles, false).Restore(restored);
        new BoolOptionBackupValue(BoolOptionNames.GhostsDoTasks, false).Restore(restored);
        check(restored.logger.Errors.Count == 0 && restored.GhostsDoTasks,
            "direct or legacy readonly bool entries cannot call unsupported setters");
    }

    static void RoundTrip(NativeOptionsFixture source, NativeOptionsFixture target, Action<bool, string> check)
    {
        var writable = new List<BoolOptionNames>();
        foreach (var name in Enum.GetValues<BoolOptionNames>())
        {
            if (name is BoolOptionNames.GhostsDoTasks or BoolOptionNames.Roles || !source.TryGetBool(name, out _)) continue;
            source.SetBool(name, true);
            writable.Add(name);
        }
        var backup = new OptionBackupData(source);
        check(!backup.AllValues.OfType<BoolOptionBackupValue>().Any(value => value.OptionName is BoolOptionNames.Roles or BoolOptionNames.GhostsDoTasks),
            source.GetType().Name + " excludes only readonly bool backup entries");
        backup.Restore(target);
        foreach (var name in writable)
            check(target.TryGetBool(name, out var actual) && actual,
                source.GetType().Name + " preserves real writable bool: " + name);
        check(target.logger.Errors.Count == 0 && source.logger.Errors.Count == 0,
            source.GetType().Name + " restore calls no unsupported native bool setter");
    }
}
