using System;
using TOHE.Modules;
using TOHE.Roles.Core.AssignManager;
using static TOHE.Translator;

namespace TOHE;

// Both local chat and native /cmd reception reach this host-authoritative route.
internal static class FeatureChatCommands
{
    private static int loadedHost = -1;
    private static uint loadedGeneration;

    internal static void UpdateSession()
    {
        var client = AmongUsClient.Instance;
        if (!client || !client.AmConnected || !client.AmHost)
        {
            if (loadedHost != -1) { LocalPlayerTags.Reset(); loadedHost = -1; }
            return;
        }
        if (loadedHost != client.HostId || loadedGeneration != OnGameJoinedPatch.Generation)
        { LocalPlayerTags.Reset(); loadedHost = -1; }
        if (!Options.IsLoaded || (loadedHost == client.HostId && loadedGeneration == OnGameJoinedPatch.Generation)) return;
        loadedHost = client.HostId;
        loadedGeneration = OnGameJoinedPatch.Generation;
        if (!LocalPlayerTags.Reload(out _)) Logger.Warn("Local player tags could not be loaded; local grants revoked", "LocalPlayerTags");
    }

    internal static bool TryHandle(PlayerControl sender, string text)
    {
        var client = AmongUsClient.Instance;
        if (!client || !client.AmConnected || !client.AmHost || !sender || sender.Data == null || sender.Data.Disconnected || string.IsNullOrWhiteSpace(text)) return false;
        UpdateSession();
        var args = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var command = args[0].ToLowerInvariant();
        bool host = sender.OwnerId == client.HostId;
        bool Granted(LocalPlayerPermission permission) => host || LocalPlayerTags.HasPermission(sender, permission);
        void Reply(string message) => Utils.SendMessage(message, sender.PlayerId, noReplay: true);
        void Denied() => Reply(GetString("FeatureCommandNoAccess"));
        PlayerControl Target(int index) => args.Length > index && byte.TryParse(args[index], out var id) ? Utils.GetPlayerById(id) : null;

        switch (command)
        {
            case "/seed":
                if (!host || !sender.AmOwner) { Denied(); return true; }
                if (args.Length == 1)
                {
                    Reply(string.Format(GetString("AssignmentSeedStatus"),
                        RoundAssignment.NextSeed?.ToString() ?? GetString("AssignmentSeedAutomatic"),
                        RoundAssignment.CurrentSeed?.ToString() ?? "-"));
                    return true;
                }
                if (!GameStates.IsLobby) { Reply(GetString("AssignmentSeedLobbyOnly")); return true; }
                if (args.Length == 2 && args[1].Equals("auto", StringComparison.OrdinalIgnoreCase))
                {
                    RoundAssignment.SetNextSeed(null);
                    Reply(GetString("AssignmentSeedReset"));
                }
                else if (args.Length == 2 && int.TryParse(args[1], out int seed))
                {
                    RoundAssignment.SetNextSeed(seed);
                    Reply(string.Format(GetString("AssignmentSeedSet"), seed));
                }
                else Reply(GetString("AssignmentSeedUsage"));
                return true;
            case "/save":
            case "/load":
                // A granted administrator still cannot read/write the host filesystem.
                if (!host || !sender.AmOwner) { Denied(); return true; }
                if (args.Length < 2) { Reply(GetString("PresetSharingUsage")); return true; }
                var name = string.Join(" ", args.Skip(1));
                var result = command == "/save" ? PresetSharing.Save(name) : PresetSharing.Load(name);
                Reply(result.Success ? string.Format(GetString(command == "/save" ? "PresetSharingSaved" : "PresetSharingLoaded"), result.FileName, result.OptionCount)
                    : GetString(result.TranslationKey));
                return true;
            case "/tags":
                if (!host || !sender.AmOwner) { Denied(); return true; }
                if (args.Length != 2 || args[1] != "reload") { Reply(GetString("LocalTagsUsage")); return true; }
                bool loaded = LocalPlayerTags.Reload(out _);
                if (GameStates.IsLobby) foreach (var player in Main.AllPlayerControls) Utils.ApplySuffix(player);
                Reply(GetString(loaded ? "LocalTagsReloaded" : "LocalTagsFailed"));
                return true;
            case "/afk":
                if (!host) { Denied(); return true; }
                if (args.Length == 2 && args[1] == "status") { Reply(AfkMonitor.GetStatus()); return true; }
                var afkTarget = Target(2);
                if (args.Length != 3 || !afkTarget || afkTarget.Data == null || afkTarget.Data.Disconnected)
                { Reply(GetString("AfkCommandUsage")); return true; }
                if (args[1] == "exempt" || args[1] == "include")
                    AfkMonitor.SetExempt(afkTarget, args[1] == "exempt");
                else if (args[1] != "status") { Reply(GetString("AfkCommandUsage")); return true; }
                Reply(AfkMonitor.GetStatus(afkTarget));
                return true;
            case "/fixblack":
                if (!host) { Denied(); return true; }
                var recoveryTarget = args.Length == 1 ? sender : Target(1);
                Reply(GetString(PresentationRecovery.TryRequest(recoveryTarget)));
                return true;
            case "/end":
                if (!Granted(LocalPlayerPermission.End)) { Denied(); return true; }
                if (!GameStates.IsInGame || !GameManager.Instance) { Reply(GetString("Message.CanNotUseInLobby")); return true; }
                CustomWinnerHolder.ResetAndSetWinner(CustomWinner.Draw);
                GameManager.Instance.LogicFlow.CheckEndCriteria();
                return true;
            case "/exe":
                if (!Granted(LocalPlayerPermission.Execute)) { Denied(); return true; }
                var executed = Target(1);
                if (!GameStates.IsInGame || !executed || executed.Data == null || executed.Data.Disconnected ||
                    !Main.PlayerStates.TryGetValue(executed.PlayerId, out var executedState) || !executed.IsAlive() ||
                    (!host && executed.OwnerId == client.HostId))
                { Reply(GetString("FeatureInvalidTarget")); return true; }
                executed.SetDeathReason(PlayerState.DeathReason.etc);
                executed.SetRealKiller(sender);
                executedState.SetDead();
                executed.Data.IsDead = true;
                executed.RpcExileV2();
                MurderPlayerPatch.AfterPlayerDeathTasks(sender, executed, GameStates.IsMeeting);
                Reply(string.Format(GetString("Message.Executed"), executed.GetRealName().RemoveHtmlTags()));
                return true;
            case "/say":
            case "/s":
                // Keep the legacy host/dev/moderator path unless this file grants chat.
                if (host || !LocalPlayerTags.HasPermission(sender, LocalPlayerPermission.Chat)) return false;
                if (args.Length > 1) Utils.SendMessage(string.Join(" ", args.Skip(1)), title: sender.GetRealName().RemoveHtmlTags());
                return true;
            case "/ban":
            case "/kick":
                if (host || !LocalPlayerTags.HasPermission(sender, LocalPlayerPermission.Moderate)) return false;
                var target = Target(1);
                if (args.Length < 3 || !target || target.Data == null || target.Data.Disconnected || target.OwnerId == client.HostId ||
                    LocalPlayerTags.HasPermission(target, LocalPlayerPermission.Moderate) || Utils.IsPlayerModerator(target.Data.FriendCode))
                { Reply(GetString("FeatureInvalidTarget")); return true; }
                client.KickPlayer(target.OwnerId, command == "/ban");
                Reply(GetString("FeatureCommandCompleted"));
                return true;
            default: return false;
        }
    }
}
