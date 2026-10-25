using AmongUs.QuickChat;
using Hazel;
using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace TOHE;

/// <summary>Host output uses custom RPCs for mod clients and a reliable name transaction for vanilla.</summary>
internal static class OfficialChat
{
    private static readonly object TransactionLock = new();
    private const int SoftPacketBytes = 1000;
    private const int HardPacketBytes = 1200;
    internal static PlayerControl DisplayActor { get; private set; }
    internal static string DisplayTitle { get; private set; }

    public static void Send(PlayerControl actor, string text, byte recipient = byte.MaxValue, string title = null)
    {
        var client = AmongUsClient.Instance;
        if (client == null || !client.AmHost || actor?.Data == null) return;
        lock (TransactionLock)
        {
            // A dead actor's chat is hidden from living recipients by vanilla AddChat.
            title ??= actor.Data.PlayerName;
            actor = Main.AllPlayerControls.Where(p => p?.Data != null && !p.Data.Disconnected && !p.Data.IsDead)
                .OrderBy(p => p.PlayerId).FirstOrDefault(p => p == actor)
                ?? Main.AllPlayerControls.Where(p => p?.Data != null && !p.Data.Disconnected && !p.Data.IsDead)
                    .OrderBy(p => p.PlayerId).FirstOrDefault() ?? actor;
            var carrier = Main.AllPlayerControls.Where(p => p?.Data != null && !p.Data.Disconnected && p != actor)
                .OrderBy(p => p.PlayerId).FirstOrDefault() ?? actor;
            var context = OfficialSessionContext.Capture();
            bool Current() => context.IsCurrent() && client.AmHost;

            var recipients = Main.AllPlayerControls.Where(p => p?.Data != null && !p.Data.Disconnected
                && (recipient == byte.MaxValue || p.PlayerId == recipient)).OrderBy(p => p.PlayerId).ToArray();
            foreach (var seer in recipients)
            {
                if (!Current()) return;
                if (seer.AmOwner)
                {
                    ShowLocal(actor, text, title);
                    continue;
                }
                int target = seer.GetClientId();
                if (target < 0) continue; // A missing private recipient must never become a broadcast.
                if (seer.IsModded())
                {
                    // The transport already bounds and fragments large logical payloads. Never send
                    // native SetName/QuickChat to mod clients, including in a mixed broadcast.
                    try
                    {
                        CustomRpcTransport.SendPayload(CustomRPC.SystemChat, writer =>
                        {
                            writer.Write(actor.PlayerId);
                            writer.Write(title ?? string.Empty);
                            writer.Write(text ?? string.Empty);
                        }, target);
                    }
                    catch (Exception error)
                    {
                        Logger.Error($"Custom chat aborted for recipient {seer.PlayerId}: {error.GetType().Name}", "OfficialChat");
                    }
                    continue;
                }
                string actorRestore = VisibleName(actor, seer), carrierRestore = VisibleName(carrier, seer);
                string heading = title ?? string.Empty, body = text ?? string.Empty;
                try
                {
                    // Preflight bounded names before creating a Hazel buffer. Large headings become body text.
                    if (Encoding.UTF8.GetByteCount(actorRestore) > HardPacketBytes
                        || Encoding.UTF8.GetByteCount(carrierRestore) > HardPacketBytes)
                        throw new InvalidOperationException("The recipient name restoration exceeds the packet budget.");
                    if (Encoding.UTF8.GetByteCount(heading) > 256)
                    {
                        body = heading + "\n" + body;
                        heading = "TOHE";
                    }
                    // Worst-case packed uint and UTF-8 string prefixes are five bytes.
                    // Include the reliable transport header, root, every child, and both restore values
                    // before allocating a writer; the actual serialized length is checked as well.
                    int overhead = actor == carrier ? 62 + Encoding.UTF8.GetByteCount(carrierRestore)
                        : 98 + Encoding.UTF8.GetByteCount(heading) + Encoding.UTF8.GetByteCount(actorRestore)
                            + Encoding.UTF8.GetByteCount(carrierRestore);
                    int budget = SoftPacketBytes - overhead;
                    if (budget <= 0) budget = HardPacketBytes - overhead;
                    if (budget <= 0) throw new InvalidOperationException("The name restoration leaves no message budget.");
                    string padding = TitlePadding(heading);
                    // Materialize all pages before sending so a later invalid token cannot leave
                    // a partially delivered message. Padding is repeated, outside the rich text.
                    var pages = Paginate(body, budget - Encoding.UTF8.GetByteCount(padding)).ToArray();
                    foreach (string page in pages)
                    {
                        if (!Current()) return;
                        using var packet = BuildTransaction(actor, carrier, target, heading, padding + page, actorRestore, carrierRestore);
                        if (packet.Writer.Length > HardPacketBytes)
                            throw new InvalidOperationException("The complete chat transaction exceeds 1200 bytes.");
                        OfficialNetworkSend.Send(client, packet.Writer);
                    }
                }
                catch (Exception error)
                {
                    // Never include private message text or player names in transport diagnostics.
                    Logger.Error($"Chat transaction aborted for recipient {seer.PlayerId}: {error.GetType().Name}", "OfficialChat");
                }
            }
        }
    }

    private static string VisibleName(PlayerControl actor, PlayerControl seer)
        // Role-name views can survive the round; lobby chat must restore current tags.
        => !GameStates.IsLobby && Main.LastNotifyNames.TryGetValue((actor.PlayerId, seer.PlayerId), out var name)
            ? name : actor.Data.PlayerName;

    internal static void ShowLocal(PlayerControl actor, string text, string title)
    {
        if (actor?.Data == null || !DestroyableSingleton<HudManager>.Instance) return;
        title ??= actor.Data.PlayerName;
        text ??= string.Empty;
        if (Encoding.UTF8.GetByteCount(title) > 256)
        {
            text = title + "\n" + text;
            title = "TOHE";
        }
        string padding = TitlePadding(title);
        var pages = Paginate(text, SoftPacketBytes - Encoding.UTF8.GetByteCount(padding)).ToArray();
        var previousActor = DisplayActor;
        string previousTitle = DisplayTitle;
        try
        {
            // Override only the bubble's name at the native presentation boundary. Player data,
            // cosmetics, notifications and name caches continue to see the real player name.
            DisplayActor = actor;
            DisplayTitle = title;
            foreach (string page in pages)
                DestroyableSingleton<HudManager>.Instance.Chat.AddChat(actor, padding + page, false);
        }
        finally { DisplayActor = previousActor; DisplayTitle = previousTitle; }
    }

    // Vanilla positions the body at a fixed Y even though its background includes the full
    // name height. Reserve body lines for explicit multi-line tags/names on both client types.
    private static string TitlePadding(string title)
        => new('\n', Regex.Matches(title ?? string.Empty, @"\n|<br\s*/?>", RegexOptions.IgnoreCase).Count);

    private static OfficialPacketBuilder.BoundedWriter BuildTransaction(PlayerControl actor, PlayerControl carrier, int target, string heading,
        string body, string actorRestore, string carrierRestore)
    {
        var owner = new OfficialPacketBuilder.BoundedWriter(SendOption.Reliable);
        var writer = owner.Writer;
        try
        {
            writer.StartMessage(6);
            writer.Write(AmongUsClient.Instance.GameId);
            writer.WritePacked(target);
            if (actor != carrier) WriteName(writer, actor, heading);
            WriteName(writer, carrier, body);
            writer.StartMessage(2);
            writer.WritePacked(actor.NetId);
            writer.Write((byte)RpcCalls.SendQuickChat);
            // Use the current native serializer: PlayerId is a valid root phrase, unlike Empty.
            QuickChatNetData.Serialize(new QuickChatPhraseBuilderResult(QuickChatPhraseType.PlayerId,
                StringNames.None, carrier.PlayerId, null), writer);
            writer.EndMessage();
            WriteName(writer, carrier, carrierRestore);
            if (actor != carrier) WriteName(writer, actor, actorRestore);
            writer.EndMessage();
            return owner;
        }
        catch { owner.Dispose(); throw; }
    }

    private static void WriteName(MessageWriter writer, PlayerControl player, string name)
    {
        writer.StartMessage(2);
        writer.WritePacked(player.NetId);
        writer.Write((byte)RpcCalls.SetName);
        writer.Write(player.Data.NetId);
        writer.Write(name);
        writer.EndMessage();
    }

    // Keep grapheme clusters intact, and close/reopen paired TMP tags at page boundaries.
    internal static IEnumerable<string> Paginate(string text, int maxUtf8Bytes)
    {
        var active = new List<(string Name, string Open)>();
        var page = new StringBuilder();
        bool hasContent = false;
        foreach (string token in Tokens(text ?? string.Empty))
        {
            var next = new List<(string Name, string Open)>(active);
            var tag = Regex.Match(token, @"^<(/?)([a-zA-Z-]+)(?:[ =][^<>]*)?>$");
            bool shorthandColor = Regex.IsMatch(token, @"^<#[0-9a-fA-F]{6}(?:[0-9a-fA-F]{2})?>$");
            if (shorthandColor) next.Add(("color", token));
            else if (tag.Success)
            {
                string name = tag.Groups[2].Value.ToLowerInvariant();
                if (tag.Groups[1].Value == "/")
                {
                    int at = next.FindLastIndex(t => t.Name == name);
                    if (at >= 0) next.RemoveRange(at, next.Count - at);
                }
                else if (name is "b" or "i" or "u" or "s" or "color" or "size" or "align" or "font"
                    or "mark" or "link" or "sub" or "sup" or "voffset" or "cspace" or "mspace" or "line-height"
                    or "indent" or "line-indent" or "margin" or "style" or "nobr") next.Add((name, token));
            }
            string close = string.Concat(next.AsEnumerable().Reverse().Select(t => $"</{t.Name}>"));
            if (Encoding.UTF8.GetByteCount(page.ToString() + token + close) > maxUtf8Bytes)
            {
                if (!hasContent) throw new InvalidOperationException("A text element or rich text tag exceeds the chat budget.");
                yield return page + string.Concat(active.AsEnumerable().Reverse().Select(t => $"</{t.Name}>"));
                page.Clear();
                foreach (var open in active) page.Append(open.Open);
                hasContent = false;
                if (Encoding.UTF8.GetByteCount(page.ToString() + token + close) > maxUtf8Bytes)
                    throw new InvalidOperationException("A text element or rich text tag exceeds the chat budget.");
            }
            page.Append(token);
            active = next;
            hasContent |= !tag.Success && !shorthandColor;
        }
        if (page.Length != 0 || string.IsNullOrEmpty(text))
            yield return page + string.Concat(active.AsEnumerable().Reverse().Select(t => $"</{t.Name}>"));
    }

    private static IEnumerable<string> Tokens(string text)
    {
        for (int i = 0; i < text.Length;)
        {
            if (text[i] == '<')
            {
                int end = text.IndexOf('>', i);
                if (end >= 0) { yield return text[i..(end + 1)]; i = end + 1; continue; }
            }
            string element = StringInfo.GetNextTextElement(text, i);
            yield return element;
            i += element.Length;
        }
    }
}

[HarmonyPatch(typeof(ChatController), nameof(ChatController.SetChatBubbleName))]
internal static class OfficialChatNamePatch
{
    private static bool Prefix([HarmonyArgument(0)] ChatBubble bubble,
        [HarmonyArgument(1)] NetworkedPlayerInfo playerInfo, [HarmonyArgument(2)] bool isDead,
        [HarmonyArgument(3)] bool didVote, [HarmonyArgument(4)] Color nameColor)
    {
        if (OfficialChat.DisplayActor == null || OfficialChat.DisplayActor.Data != playerInfo) return true;
        bubble.SetName(OfficialChat.DisplayTitle, isDead, didVote, nameColor);
        return false;
    }
}
