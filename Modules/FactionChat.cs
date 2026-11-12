using System;
using TOHE.Modules.ChatManager;
using TOHE.Roles.Impostor;
using TOHE.Roles.Neutral;
using static TOHE.Translator;

namespace TOHE;

internal static class FactionChat
{
    private sealed record Channel(int OptionId, string Command, CustomRoles Faction)
    {
        public string NameKey => $"FactionChat.{Faction}";
        public OptionItem Option;
    }

    private static readonly Channel[] Channels =
    [
        new(60920, "/crew", CustomRoles.Crewmate),
        new(60921, "/imp", CustomRoles.Impostor),
        new(60922, "/lo", CustomRoles.Lovers),
        new(60923, "/ja", CustomRoles.Jackal),
        new(60924, "/apoc", CustomRoles.Apocalypse),
        new(60925, "/cult", CustomRoles.Cultist),
        new(60926, "/infect", CustomRoles.Infectious),
        new(60927, "/virus", CustomRoles.Virus),
        new(60928, "/souls", CustomRoles.CursedSoul)
    ];

    internal static void SetupOptions()
    {
        var master = BooleanOptionItem.Create(60461, "EnableFactionChat", false, TabGroup.ModSettings, false)
            .SetGameMode(CustomGameMode.Standard)
            .SetHeader(true);
        foreach (var channel in Channels)
            channel.Option = BooleanOptionItem.Create(channel.OptionId, $"{channel.NameKey}.Enable", false, TabGroup.ModSettings, false)
                .SetParent(master);
    }

    private static bool InStandardMeeting => GameStates.IsMeeting && Options.CurrentGameMode == CustomGameMode.Standard;

    private static bool CanParticipate(PlayerControl player) =>
        player != null && player.Data is { Disconnected: false, IsDead: false }
        && player.IsAlive() && !Pelican.IsEaten(player.PlayerId)
        // Vanilla /cmd is private only with a server that routes it to the host.
        && (player.IsModded() || GameStates.IsOnlineGame);

    private static bool IsMember(PlayerControl player, CustomRoles faction)
    {
        if (!Main.PlayerStates.TryGetValue(player.PlayerId, out var state)) return false;
        // Lovers can use this channel in addition to their current faction's channel.
        if (faction == CustomRoles.Lovers) return state.SubRoles.Contains(CustomRoles.Lovers);

        // Converted players may retain their original role and parity count.
        foreach (var addon in state.SubRoles)
        {
            switch (addon)
            {
                case CustomRoles.Admired: return faction == CustomRoles.Crewmate;
                case CustomRoles.Recruit: return faction == CustomRoles.Jackal;
                case CustomRoles.Charmed: return faction == CustomRoles.Cultist;
                case CustomRoles.Infected: return faction == CustomRoles.Infectious;
                case CustomRoles.Contagious: return faction == CustomRoles.Virus;
                case CustomRoles.Soulless: return faction == CustomRoles.CursedSoul;
                case CustomRoles.Madmate: return false;
                case CustomRoles.Egoist when addon.IsConverted(): return false;
            }
        }

        var role = state.MainRole;
        return faction switch
        {
            CustomRoles.Crewmate => role is not (CustomRoles.GM or CustomRoles.NotAssigned) && role.IsCrewmate(),
            CustomRoles.Impostor => role.IsImpostor(),
            CustomRoles.Jackal => role is CustomRoles.Jackal or CustomRoles.Sidekick,
            CustomRoles.Apocalypse => role.IsNA(),
            _ => role == faction
        };
    }

    private static Channel FindChannel(ReadOnlySpan<char> text, out int end)
    {
        end = 0;
        if (text.IsEmpty || text[0] != '/') return null;
        while (end < text.Length && !char.IsWhiteSpace(text[end])) end++;
        foreach (var channel in Channels)
            if (text[..end].Equals(channel.Command.AsSpan(), StringComparison.OrdinalIgnoreCase))
                return channel;
        return null;
    }

    internal static bool IsCommand(string text) => FindChannel(text.AsSpan().TrimStart(), out _) != null;

    // Call only from host dispatch, before public chat is recorded.
    internal static bool TryHandle(PlayerControl sender, string text)
    {
        var input = text.AsSpan().TrimStart();
        var channel = FindChannel(input, out int end);
        if (channel == null) return false;
        if (sender == null || sender.PlayerId == byte.MaxValue) return true;

        string channelName = GetString(channel.NameKey);
        if (!InStandardMeeting || !channel.Option.GetBool() || !CanParticipate(sender)
            || !IsMember(sender, channel.Faction) || Blackmailer.CheckBlackmaile(sender))
        {
            Send(sender, GetString("FactionChat.Unavailable"), channelName);
            return true;
        }

        var recipients = Main.AllPlayerControls.Where(pc => CanParticipate(pc) && IsMember(pc, channel.Faction)).ToArray();
        if (recipients.Length < 2)
        {
            Send(sender, GetString("FactionChat.Unavailable"), channelName);
            return true;
        }

        string message = input[end..].ToString().RemoveHtmlTags().Trim();
        if (message.Length == 0)
        {
            Send(sender, string.Format(GetString("FactionChat.Usage"), CommandFor(sender, channel)), channelName);
            return true;
        }

        string name = sender.GetRealName(clientData: true).RemoveHtmlTags();
        string title = Utils.ColorString(Utils.GetRoleColor(channel.Faction), $"[{channelName}] {name}");
        ChatManager.SendPrivateSystem(sender, message, recipients, title);
        return true;
    }

    internal static void SendMeetingHints()
    {
        if (!InStandardMeeting) return;
        var enabled = Channels.Where(channel => channel.Option.GetBool()).ToArray();
        if (enabled.Length == 0) return;
        var players = Main.AllPlayerControls.Where(CanParticipate).ToArray();
        var channels = enabled.Where(channel => players.Count(pc => IsMember(pc, channel.Faction)) >= 2).ToArray();
        var batch = OfficialAnticheatPolicy.Enabled ? new OfficialPacketBuilder.TargetedTransactionBatch() : null;
        foreach (var player in players)
        {
            if (Blackmailer.CheckBlackmaile(player)) continue;
            var lines = channels.Where(channel => IsMember(player, channel.Faction))
                .Select(channel => $"{GetString(channel.NameKey)}: {string.Format(GetString("FactionChat.Usage"), CommandFor(player, channel))}");
            string hint = string.Join('\n', lines);
            if (hint.Length > 0)
                OfficialChat.Send(player, hint, player.PlayerId, GetString("FactionChat.Channels"),
                    send: batch == null ? null : batch.Add, privateTransport: true);
        }
        batch?.Send();
    }

    private static string CommandFor(PlayerControl player, Channel channel) =>
        player.IsModded() ? channel.Command : $"/cmd {channel.Command[1..]}";

    private static void Send(PlayerControl recipient, string text, string title) =>
        Utils.SendMessage(text, recipient.PlayerId, title, logforChatManager: true, noReplay: true);
}
