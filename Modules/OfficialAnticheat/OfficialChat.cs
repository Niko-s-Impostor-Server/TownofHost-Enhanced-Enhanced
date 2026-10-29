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
    internal readonly record struct SentPage(byte SpeakerId, PlayerControl Recipient, string Heading, string Text);
    internal static PlayerControl DisplayActor { get; private set; }
    internal static string DisplayTitle { get; private set; }

    // Transport only. Higher-level modules decide whether a delivered page belongs in history.
    internal static SentPage[] Send(PlayerControl actor, string text, byte recipient = byte.MaxValue, string title = null,
        bool preserveDeadActor = false, Action<MessageWriter> send = null)
    {
        var client = AmongUsClient.Instance;
        if (client == null || !client.AmHost || actor?.Data == null) return [];
        var sentPages = new List<SentPage>();
        lock (TransactionLock)
        {
            title ??= actor.Data.PlayerName;
            byte speakerId = actor.PlayerId;
            // System messages need a living carrier; ghost chat preserves its dead sender
            // because ChatManager sends it only to recipients whose Data.IsDead is true.
            if (!preserveDeadActor)
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
                if (!Current()) return sentPages.ToArray();
                if (seer.AmOwner)
                {
                    var (localHeading, localPages) = ShowLocal(actor, text, title);
                    foreach (var page in localPages)
                        sentPages.Add(new(speakerId, seer, localHeading, page));
                    continue;
                }
                int target = seer.GetClientId();
                if (target < 0) continue; // A missing private recipient must never become a broadcast.
                if (seer.IsModded())
                {
                    // Mod clients receive SystemChat; official packed mode also fragments
                    // large payloads. Never send native SetName/QuickChat to mod clients.
                    try
                    {
                        var (modHeading, modPages) = LocalPages(text ?? string.Empty, title ?? string.Empty);
                        CustomRpcTransport.SendPayload(CustomRPC.SystemChat, writer =>
                        {
                            writer.Write(actor.PlayerId);
                            writer.Write(title ?? string.Empty);
                            writer.Write(text ?? string.Empty);
                        }, target);
                        foreach (var page in modPages)
                            sentPages.Add(new(speakerId, seer, modHeading, page));
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
                    int inputNameBytes = Encoding.UTF8.GetByteCount(heading);
                    int actorRestoreBytes = Encoding.UTF8.GetByteCount(actorRestore);
                    int carrierRestoreBytes = Encoding.UTF8.GetByteCount(carrierRestore);
                    int textBytes = Encoding.UTF8.GetByteCount(body);
                    if (actorRestoreBytes > HardPacketBytes || carrierRestoreBytes > HardPacketBytes)
                        throw new InvalidOperationException("The recipient name restoration exceeds the packet budget.");
                    if (inputNameBytes > 256)
                    {
                        body = heading + "\n" + body;
                        heading = "TOHE";
                    }
                    string padding = TitlePadding(heading);
                    // An empty-body transaction measures the actual reliable header, target,
                    // name changes, QuickChat payload, and both name restorations.
                    int available;
                    using (var baseline = BuildTransaction(actor, carrier, target, heading, padding, actorRestore, carrierRestore))
                        available = HardPacketBytes - baseline.Writer.Length;
                    if (available <= 0) throw new InvalidOperationException("The name restoration leaves no message budget.");

                    // Hazel's string length prefix can grow as the body grows. Measure that
                    // change with the same writer serializer before setting the page limit.
                    var probe = MessageWriter.Get(SendOption.Reliable);
                    try
                    {
                        int start = probe.Length;
                        probe.Write(padding);
                        int paddedStringLength = probe.Length - start;
                        probe.Clear(SendOption.Reliable);
                        start = probe.Length;
                        probe.Write(padding + new string('A', available));
                        int prefixGrowth = (probe.Length - start) - paddedStringLength - available;
                        available -= prefixGrowth;
                    }
                    finally { probe.Recycle(); }
                    if (available <= 0) throw new InvalidOperationException("The name restoration leaves no message budget.");
                    Logger.Test($"recipient={seer.PlayerId}, inputNameUtf8Bytes={inputNameBytes}, " +
                        $"setHeadingUtf8Bytes={(actor == carrier ? 0 : Encoding.UTF8.GetByteCount(heading))}, " +
                        $"actorRestoreUtf8Bytes={actorRestoreBytes}, carrierRestoreUtf8Bytes={carrierRestoreBytes}, " +
                        $"textUtf8Bytes={textBytes}, paginatedBodyUtf8Bytes={Encoding.UTF8.GetByteCount(body)}, " +
                        $"pageLimitUtf8Bytes={available}", "OfficialChat.Pagination");
                    // Materialize all pages before sending so a later invalid token cannot leave
                    // a partially delivered message. Padding is repeated, outside the rich text.
                    var pages = Paginate(body, available).ToArray();
                    Logger.Test($"recipient={seer.PlayerId}, pageCount={pages.Length}", "OfficialChat.Pagination");
                    int pageIndex = 0;
                    foreach (string page in pages)
                    {
                        if (!Current()) return sentPages.ToArray();
                        using var packet = BuildTransaction(actor, carrier, target, heading, padding + page, actorRestore, carrierRestore);
                        if (packet.Writer.Length > HardPacketBytes)
                            throw new InvalidOperationException("The complete chat transaction exceeds 1200 bytes.");
                        Logger.Test($"recipient={seer.PlayerId}, page={++pageIndex}/{pages.Length}, " +
                            $"setCarrierNameUtf8Bytes={Encoding.UTF8.GetByteCount(padding + page)}, " +
                            $"packetBytes={packet.Writer.Length}", "OfficialChat.Pagination");
                        if (send == null) OfficialNetworkSend.Send(client, packet.Writer);
                        else send(packet.Writer);
                        sentPages.Add(new(speakerId, seer, heading, page));
                    }
                }
                catch (Exception error)
                {
                    // Never include private message text or player names in transport diagnostics.
                    Logger.Error($"Chat transaction aborted for recipient {seer.PlayerId}: {error.GetType().Name}", "OfficialChat");
                }
            }
        }
        return sentPages.ToArray();
    }

    internal static string VisibleName(PlayerControl actor, PlayerControl seer)
        // Role-name views can survive the round; lobby chat must restore current tags.
        => !GameStates.IsLobby && Main.LastNotifyNames.TryGetValue((actor.PlayerId, seer.PlayerId), out var name)
            ? name : actor.Data.PlayerName;

    internal static (string Heading, string[] Pages) ShowLocal(PlayerControl actor, string text, string title, bool censor = false,
        bool textHasNamePadding = false, bool allowDeadSpeaker = false)
    {
        if (actor?.Data == null || !DestroyableSingleton<HudManager>.Instance) return (title ?? string.Empty, []);
        title ??= actor.Data.PlayerName;
        text ??= string.Empty;
        var (heading, pages) = LocalPages(text, title, includeTitlePadding: !textHasNamePadding);
        string padding = textHasNamePadding ? string.Empty : TitlePadding(heading);
        var previousActor = DisplayActor;
        string previousTitle = DisplayTitle;
        bool showDeadSpeaker = allowDeadSpeaker && actor.Data.IsDead && PlayerControl.LocalPlayer?.Data != null
            && !PlayerControl.LocalPlayer.Data.IsDead;
        try
        {
            // Override only the bubble's name at the native presentation boundary. Player data,
            // cosmetics, notifications and name caches continue to see the real player name.
            DisplayActor = actor;
            DisplayTitle = heading;
            if (showDeadSpeaker) actor.Data.IsDead = false;
            foreach (string page in pages)
                DestroyableSingleton<HudManager>.Instance.Chat.AddChat(actor, padding + page, censor);
        }
        finally
        {
            if (showDeadSpeaker) actor.Data.IsDead = true;
            DisplayActor = previousActor;
            DisplayTitle = previousTitle;
        }
        return (heading, pages);
    }

    private static (string Heading, string[] Pages) LocalPages(string text, string title, bool includeTitlePadding = true)
    {
        if (Encoding.UTF8.GetByteCount(title) > 256)
        {
            text = title + "\n" + text;
            title = "TOHE";
        }
        string padding = includeTitlePadding ? TitlePadding(title) : string.Empty;
        var pages = Paginate(text, SoftPacketBytes - Encoding.UTF8.GetByteCount(padding)).ToArray();
        return (title, pages);
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
