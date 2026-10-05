namespace TOHE.Roles.Core.AssignManager;

// Compatibility facade for chat presets and the vanilla/desync role bridge.
public static class RoleAssign
{
    public static Dictionary<byte, CustomRoles> SetRoles = [];
    public static Dictionary<byte, CustomRoles> RoleResult = [];
    public static CustomRoles[] AllRoles => [.. RoleResult.OrderBy(p => p.Key).Select(p => p.Value)];

    public static void StartSelect()
    {
        var players = Main.AllPlayerControls.Where(pc => pc != null && pc.Data != null && !pc.Data.Disconnected)
            .OrderBy(pc => pc.PlayerId).ToArray();
        int seed = RoundAssignment.Begin(players.Select(pc => pc.PlayerId));
        if (Options.CurrentGameMode == CustomGameMode.FFA)
            RoleResult = players.ToDictionary(pc => pc.PlayerId, _ => CustomRoles.Killer);
        else
        {
            RoundAssignment.RoleInput = RoleAssignmentCatalog.Capture(seed, players);
            RoleResult = RoleAssignmentEngine.Assign(RoundAssignment.RoleInput);
        }
        // Consume presets only after a complete, successful allocation.
        SetRoles.Clear();
        foreach (var (id, role) in RoleResult)
            Logger.Info($"PlayerId={id} => {role}", "RoleAssign");
    }

    public static int AddScientistNum;
    public static int AddEngineerNum;
    public static int AddShapeshifterNum;
    public static int AddNoisemakerNum;
    public static int AddPhantomNum;
    public static int AddTrackerNum;
    public static int AddDetectiveNum;
    public static int AddViperNum;
    public static void CalculateVanillaRoleCount()
    {
        // Calculate the number of base roles
        AddEngineerNum = 0;
        AddScientistNum = 0;
        AddShapeshifterNum = 0;
        AddNoisemakerNum = 0;
        AddPhantomNum = 0;
        AddTrackerNum = 0;
        AddDetectiveNum = 0;
        AddViperNum = 0;
        foreach (var role in AllRoles)
        {
            switch (role.GetVNRole())
            {
                case CustomRoles.Scientist:
                    AddScientistNum++;
                    break;
                case CustomRoles.Engineer:
                    AddEngineerNum++;
                    break;
                case CustomRoles.Shapeshifter:
                    AddShapeshifterNum++;
                    break;
                case CustomRoles.Noisemaker:
                    AddNoisemakerNum++;
                    break;
                case CustomRoles.Phantom:
                    AddPhantomNum++;
                    break;
                case CustomRoles.Tracker:
                    AddTrackerNum++;
                    break;
                case CustomRoles.DetectiveVanilla:
                    AddDetectiveNum++;
                    break;
                case CustomRoles.Viper:
                    AddViperNum++;
                    break;
            }
        }
    }
}
