namespace TOHE;

internal enum ManagementAction { Rename, Color, Warn, Start, Kick, Ban, Chat, End, Execute, List }

internal static class ManagementCommandPolicy
{
    internal static ManagementAction? Parse(string command) => command?.ToLowerInvariant() switch
    {
        "/rn" or "/rename" or "/renomear" or "/переименовать" or "/重命名" or "/命名为" => ManagementAction.Rename,
        "/colour" or "/color" or "/cor" or "/цвет" or "/颜色" or "/更改颜色" or "/修改颜色" or "/换颜色" => ManagementAction.Color,
        "/warn" or "/aviso" or "/варн" or "/пред" or "/предупредить" or "/警告" or "/提醒" => ManagementAction.Warn,
        "/start" or "/开始" or "/开始游戏" => ManagementAction.Start,
        "/kick" or "/expulsar" or "/кик" or "/кикнуть" or "/выгнать" or "/踢出" or "/踢" => ManagementAction.Kick,
        "/ban" or "/banir" or "/бан" or "/забанить" or "/封禁" => ManagementAction.Ban,
        "/say" or "/s" or "/с" or "/сказать" or "/说" => ManagementAction.Chat,
        "/end" or "/encerrar" or "/завершить" or "/结束" or "/结束游戏" => ManagementAction.End,
        "/exe" or "/уничтожить" or "/повесить" or "/казнить" or "/казнь" or "/мут" or "/驱逐" or "/驱赶" => ManagementAction.Execute,
        "/mid" or "/玩家列表" or "/玩家信息" or "/玩家编号列表" => ManagementAction.List,
        _ => null
    };

    internal static bool LegacyGrant(ManagementAction action, bool enabledModerator, bool allowSay) =>
        enabledModerator && (action is ManagementAction.Kick or ManagementAction.Ban or ManagementAction.Warn or ManagementAction.List
            || action == ManagementAction.Chat && allowSay);

    internal static bool TagGrant(ManagementAction action, int level, bool say, bool end, bool execute) => action switch
    {
        ManagementAction.Rename or ManagementAction.Color => level >= 1,
        ManagementAction.Warn or ManagementAction.List => level >= 2,
        ManagementAction.Start => level >= 3,
        ManagementAction.Kick => level >= 4,
        ManagementAction.Ban => level >= 5,
        ManagementAction.Chat => say,
        ManagementAction.End => end,
        ManagementAction.Execute => execute,
        _ => false
    };

    internal static bool TagProtectsTarget(ManagementAction action, int level) => action switch
    {
        ManagementAction.Warn => level >= 2,
        ManagementAction.Kick => level >= 4,
        ManagementAction.Ban => level >= 5,
        _ => false
    };

    internal static bool CanModerateTarget(bool actorIsHost, bool targetIsHost, bool targetIsAdministrator) =>
        !targetIsHost && (actorIsHost || !targetIsAdministrator);

    internal static bool TryTarget(string[] args, out byte id) => byte.TryParse(args.Length > 1 ? args[1] : "", out id);
    internal static bool RequiresReason(ManagementAction action) => action == ManagementAction.Ban;
}
