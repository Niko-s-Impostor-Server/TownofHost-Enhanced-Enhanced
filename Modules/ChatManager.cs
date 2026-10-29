using Hazel;
using AmongUs.QuickChat;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using System;
using System.Security.Cryptography;
using System.Text;
using TOHE.Roles.Impostor;
using static TOHE.Translator;

namespace TOHE.Modules.ChatManager
{
    public class ChatManager
    {
        public static bool cancel = false;
        private static readonly ChatViewHistory history = new();
        internal readonly record struct View(PlayerControl Recipient, ChatViewHistory.Entry[] Entries);
        public static List<string> ChatSentBySystem = [];
        public static void ResetHistory()
        {
            history.Clear();
            ChatSentBySystem.Clear();
            cancel = false;
        }
        internal static void RemoveRecipient(int clientId) => history.Remove(clientId);

        internal static int BufferSize => DestroyableSingleton<HudManager>.Instance.Chat.chatBubblePool.poolSize;

        internal static void SendSystem(PlayerControl actor, string text, byte recipient = byte.MaxValue, string title = null,
            bool recordHistory = true)
        {
            var pages = OfficialChat.Send(actor, text, recipient, title);
            if (!recordHistory) return;
            foreach (var page in pages) RecordSystemPage(page);
        }

        internal static void SendPlayerText(PlayerControl sender, string text)
        {
            if (sender?.Data == null) return;
            if (!sender.IsAlive())
            {
                foreach (var recipient in Main.AllPlayerControls.Where(p => p?.Data != null && !p.Data.Disconnected && p.Data.IsDead))
                    OfficialChat.Send(sender, text, recipient.PlayerId, preserveDeadActor: true);
                return;
            }

            var pages = OfficialChat.Send(sender, text);
            foreach (var page in pages) RecordPlayerPage(page);
        }

        private static void RecordSystemPage(OfficialChat.SentPage page)
        {
            int clientId = page.Recipient.GetClientId();
            if (clientId < 0) return;
            history.Add(clientId, new(ChatViewHistory.Kind.System, page.SpeakerId, page.Heading, page.Text, [], false, []), BufferSize);
        }

        internal static void RecordPlayerChat(PlayerControl sender, string text, bool textHasNamePadding = false)
            => RecordPlayerMessage(sender, ChatViewHistory.Kind.PlayerText, text, [], textHasNamePadding);

        internal static void RecordQuickChat(PlayerControl sender, QuickChatPhraseBuilderResult data)
        {
            var writer = MessageWriter.Get(SendOption.Reliable);
            try
            {
                int offset = writer.Position;
                QuickChatNetData.Serialize(data, writer);
                RecordQuickChat(sender, OfficialPacketBuilder.Snapshot(writer, offset));
            }
            finally { writer.Recycle(); }
        }

        internal static void RecordQuickChat(PlayerControl sender, byte[] payload)
            => RecordPlayerMessage(sender, ChatViewHistory.Kind.QuickChat, string.Empty, payload, false);

        private static void RecordPlayerMessage(PlayerControl sender, ChatViewHistory.Kind type, string text, byte[] payload,
            bool textHasNamePadding)
        {
            if (!AmongUsClient.Instance.AmHost || sender?.Data == null || !sender.IsAlive()) return;
            int capacity = BufferSize;
            var referencedIds = new HashSet<byte>();
            if (type == ChatViewHistory.Kind.QuickChat)
            {
                var reader = MessageReader.Get(new Il2CppStructArray<byte>(payload));
                try
                {
                    var data = QuickChatNetData.Deserialize(reader);
                    if (data.RootPhrase.Type == QuickChatPhraseType.PlayerId) referencedIds.Add(data.RootPhrase.PlayerID);
                    foreach (var phrase in data.SubPhrases)
                        if (phrase != null && phrase.Type == QuickChatPhraseType.PlayerId) referencedIds.Add(phrase.PlayerID);
                }
                finally { reader.Recycle(); }
            }
            foreach (var recipient in Main.AllPlayerControls.Where(p => p?.Data != null && !p.Data.Disconnected))
            {
                int clientId = recipient.GetClientId();
                if (clientId < 0) continue;
                var names = referencedIds.Select(id => id.GetPlayer()).Where(player => player?.Data != null)
                    .Select(player => new ChatViewHistory.ReferencedName(player.PlayerId,
                        recipient.IsModded() ? player.Data.PlayerName : OfficialChat.VisibleName(player, recipient))).ToArray();
                history.Add(clientId, new(type, sender.PlayerId, OfficialChat.VisibleName(sender, recipient), text, payload,
                    textHasNamePadding, names), capacity);
            }
        }

        private static void RecordPlayerPage(OfficialChat.SentPage page)
        {
            int clientId = page.Recipient.GetClientId();
            if (clientId < 0) return;
            history.Add(clientId, new(ChatViewHistory.Kind.PlayerText, page.SpeakerId, page.Heading, page.Text, [], false, []), BufferSize);
        }

        internal static View[] SnapshotViews() => Main.AllPlayerControls
            .Where(p => p?.Data != null && !p.Data.Disconnected && p.GetClientId() >= 0)
            .OrderBy(p => p.PlayerId)
            .Select(p => new View(p, history.Snapshot(p.GetClientId())))
            .ToArray();

        internal static bool CanReplay(View view, ChatViewHistory.Entry entry)
        {
            if (entry.Type == ChatViewHistory.Kind.System) return true;
            var actor = entry.SpeakerId.GetPlayer();
            if (actor?.Data == null || actor.Data.Disconnected) return false;
            // Vanilla rejects a dead speaker's chat for a living recipient.
            return view.Recipient.AmOwner || view.Recipient.IsModded() || !actor.Data.IsDead || view.Recipient.Data.IsDead;
        }

        internal sealed class VanillaReplayBatch(PlayerControl recipient)
        {
            private readonly List<List<OfficialPacketCodec.Record>> groups = [];

            internal void Add(MessageWriter writer)
            {
                byte[] bytes = OfficialPacketBuilder.Snapshot(writer);
                groups.Add(OfficialPacketCodec.ReadRecords(bytes, 3, bytes.Length, AmongUsClient.Instance.GameId));
            }

            internal void Send()
            {
                if (groups.Count == 0) return;
                int target = recipient.GetClientId();
                if (target < 0) return;
                var client = AmongUsClient.Instance;
                using var owner = new OfficialPacketBuilder.BoundedWriter(SendOption.Reliable, bounded: false);
                var writer = owner.Writer;
                int children = 0;
                void StartPacket()
                {
                    writer.StartMessage(6);
                    writer.Write(client.GameId);
                    writer.WritePacked(target);
                }
                void SendPacket()
                {
                    writer.EndMessage();
                    OfficialNetworkSend.Send(client, writer);
                    writer.Clear(SendOption.Reliable);
                    children = 0;
                }

                StartPacket();
                foreach (var group in groups)
                {
                    int bytes = group.Sum(record => record.Child.Length);
                    if (children > 0 && (writer.Length + bytes > OfficialPacketCodec.HardLimit
                        || children + group.Count > OfficialAnticheatPolicy.PackingLimit))
                    {
                        SendPacket();
                        StartPacket();
                    }
                    foreach (var record in group)
                        writer.Write(new Il2CppStructArray<byte>(record.Child.ToArray()));
                    children += group.Count;
                }
                if (children > 0) SendPacket();
            }
        }

        internal static void Replay(View view, Action<MessageWriter> send = null)
        {
            foreach (var entry in view.Entries)
            {
                if (!CanReplay(view, entry)) continue;
                var actor = entry.SpeakerId.GetPlayer();
                if (entry.Type == ChatViewHistory.Kind.System)
                {
                    if (actor?.Data == null || actor.Data.Disconnected || !actor.IsAlive())
                        actor = Main.AllPlayerControls.FirstOrDefault(p => p?.Data != null && !p.Data.Disconnected && p.IsAlive())
                            ?? view.Recipient;
                    if (actor?.Data == null) continue;
                    OfficialChat.Send(actor, entry.Text, view.Recipient.PlayerId, entry.Title, send: send);
                }
                else if (actor?.Data != null && !actor.Data.Disconnected)
                    ReplayPlayer(actor, view.Recipient, entry, send);
            }
        }

        private static void ReplayPlayer(PlayerControl actor, PlayerControl seer, ChatViewHistory.Entry entry, Action<MessageWriter> send)
        {
            bool quickChat = entry.Type == ChatViewHistory.Kind.QuickChat;
            if (seer.AmOwner)
            {
                string text = quickChat ? QuickChatText(entry.QuickChatPayload, entry.ReferencedNames) : entry.Text;
                OfficialChat.ShowLocal(actor, text, entry.Title, censor: !quickChat,
                    textHasNamePadding: entry.TextHasNamePadding, allowDeadSpeaker: true);
                return;
            }

            int target = seer.GetClientId();
            if (target < 0) return;
            if (seer.IsModded())
            {
                CustomRpcTransport.SendPayload(CustomRPC.ReplayPlayerChat, writer =>
                {
                    writer.Write(quickChat);
                    writer.Write(actor.PlayerId);
                    writer.Write(entry.Title);
                    writer.Write(entry.TextHasNamePadding);
                    if (quickChat)
                    {
                        writer.WritePacked(entry.QuickChatPayload.Length);
                        foreach (byte value in entry.QuickChatPayload) writer.Write(value);
                        writer.WritePacked(entry.ReferencedNames.Length);
                        foreach (var name in entry.ReferencedNames)
                        {
                            writer.Write(name.PlayerId);
                            writer.Write(name.Name);
                        }
                    }
                    else writer.Write(entry.Text);
                }, target);
                return;
            }

            using var owner = new OfficialPacketBuilder.BoundedWriter(SendOption.Reliable, bounded: false);
            var writer = owner.Writer;
            writer.StartMessage(6);
            writer.Write(AmongUsClient.Instance.GameId);
            writer.WritePacked(target);
            string currentName = OfficialChat.VisibleName(actor, seer);
            bool rename = entry.Title != currentName;
            if (rename) WriteName(writer, actor, entry.Title);
            var referenced = entry.ReferencedNames.Select(name => (Name: name, Player: name.PlayerId.GetPlayer()))
                .Where(item => item.Player?.Data != null && !item.Player.Data.Disconnected)
                .Select(item => (item.Player, item.Name.Name, Current: OfficialChat.VisibleName(item.Player, seer))).ToArray();
            foreach (var item in referenced)
                if (item.Player != actor && item.Name != item.Current) WriteName(writer, item.Player, item.Name);
            writer.StartMessage(2);
            writer.WritePacked(actor.NetId);
            writer.Write((byte)(quickChat ? RpcCalls.SendQuickChat : RpcCalls.SendChat));
            if (quickChat) writer.Write(new Il2CppStructArray<byte>(entry.QuickChatPayload));
            else writer.Write(entry.Text);
            writer.EndMessage();
            foreach (var item in referenced)
                if (item.Player != actor && item.Name != item.Current) WriteName(writer, item.Player, item.Current);
            if (rename) WriteName(writer, actor, currentName);
            writer.EndMessage();
            if (writer.Length > OfficialPacketCodec.HardLimit)
            {
                Logger.Warn($"Player chat replay exceeds the packet limit for recipient {seer.PlayerId}", "ChatManager");
                return;
            }
            if (send == null) OfficialNetworkSend.Send(AmongUsClient.Instance, writer);
            else send(writer);
        }

        internal static string QuickChatText(byte[] payload, ChatViewHistory.ReferencedName[] names)
        {
            var reader = MessageReader.Get(new Il2CppStructArray<byte>(payload));
            var restore = new List<(NetworkedPlayerInfo Player, string Name)>();
            try
            {
                foreach (var name in names)
                {
                    var player = name.PlayerId.GetPlayer()?.Data;
                    if (player == null) continue;
                    restore.Add((player, player.PlayerName));
                    player.PlayerName = name.Name;
                }
                return QuickChatNetData.Deserialize(reader)?.ToChatText() ?? string.Empty;
            }
            finally
            {
                foreach (var (player, name) in restore) player.PlayerName = name;
                reader.Recycle();
            }
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
        public static bool CheckCommond(ref string msg, string command, bool exact = true)
        {
            var comList = command.Split('|');
            foreach (string comm in comList)
            {
                if (exact)
                {
                    if (msg == "/" + comm) return true;
                }
                else
                {
                    if (msg.StartsWith("/" + comm))
                    {
                        msg = msg.Replace("/" + comm, string.Empty);
                        return true;
                    }
                }
            }
            return false;
        }
        public static bool CheckName(ref string msg, string command, bool exact = true)
        {
            var comList = command.Split('|');
            foreach (var com in comList)
            {
                if (exact)
                {
                    if (msg.Contains(com))
                    {
                        return true;
                    }
                }
                else
                {
                    int index = msg.IndexOf(com);
                    if (index != -1)
                    {
                        msg = msg.Remove(index, com.Length);
                        return true;
                    }
                }
            }
            return false;
        }

        private static string GetTextHash(string text)
        {
            using SHA256 sha256 = SHA256.Create();

            // get sha-256 hash
            byte[] sha256Bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(text));
            string sha256Hash = BitConverter.ToString(sha256Bytes).Replace("-", "").ToLower();

            // pick front 5 and last 4
            return string.Concat(sha256Hash.AsSpan(0, 5), sha256Hash.AsSpan(sha256Hash.Length - 4));
        }

        public static void AddToHostMessage(string text)
        {
            if (text != "")
            {
                ChatSentBySystem.Add(GetTextHash(text));
            }
        }
        public static void SendMessage(PlayerControl player, string message)
        {
            if (HostOnlyChatCommand.IsActive || HostOnlyChatCommand.IsEnvelope(message)) return;
            int operate = 0; // 1:ID 2:猜测
            string msg = message;
            message = message.ToLower().TrimStart().TrimEnd();

            if (GameStates.IsInGame) operate = 3;
            if (CheckCommond(ref msg, "id|guesslist|gl编号|玩家编号|玩家id|id列表|玩家列表|列表|所有id|全部id|編號|玩家編號")) operate = 1;
            else if (CheckCommond(ref msg, "shoot|guess|bet|st|gs|bt|猜|赌|賭|sp|jj|tl|trial|审判|判|审|審判|審|compare|cmp|比较|比較|duel|sw|swap|st|换票|换|換票|換|finish|结束|结束会议|結束|結束會議|reveal|展示", false)) operate = 2;
            else if (ChatSentBySystem.Contains(GetTextHash(msg))) operate = 5;

            if ((operate == 1 || Blackmailer.CheckBlackmaile(player)) && player.IsAlive())
            {
                Logger.Info($"包含特殊信息，不记录", "ChatManager");
                message = msg;
                cancel = true;
            }
            else if (operate == 2)
            {
                Logger.Info($"指令{msg}，不记录", "ChatManager");
                message = msg;
                cancel = false;
            }
            else if (operate == 4)
            {
                Logger.Info($"指令{msg}，不记录", "ChatManager");
                message = msg;
                SendPreviousMessagesToAll();
            }
            else if (operate == 5)
            {
                Logger.Info($"system message{msg}，不记录", "ChatManager");
                message = msg;
                cancel = true;
            }
            else if (operate == 3)
            {
                if (GameStates.IsExilling)
                {
                    if (Options.HideExileChat.GetBool())
                    {
                        Logger.Info($"Message sent in exiling screen, spamming the chat", "ChatManager");
                        _ = new LateTask(SendPreviousMessagesToAll, 0.3f, "Spamming the chat");
                    }
                    return;
                }
                cancel = false;
            }
        }

        public static void SendPreviousMessagesToAll()
        {
            if (HostOnlyChatCommand.IsActive) return;
            if (!AmongUsClient.Instance.AmHost || !GameStates.IsModHost) return;
            int bufferSize = BufferSize;
            foreach (var view in SnapshotViews())
            {
                var batch = !view.Recipient.AmOwner && !view.Recipient.IsModded() ? new VanillaReplayBatch(view.Recipient) : null;
                Action<MessageWriter> send = batch == null ? null : batch.Add;
                int replayCount = view.Entries.Count(entry => CanReplay(view, entry));
                if (GameStates.IsExilling && replayCount < bufferSize)
                {
                    var actor = Main.AllAlivePlayerControls.OrderBy(x => x.PlayerId).FirstOrDefault() ?? view.Recipient;
                    var title = "<color=#aaaaff>" + GetString("DefaultSystemMessageTitle") + "</color>";
                    for (int i = 0; i < bufferSize - replayCount; i++)
                        OfficialChat.Send(actor, GetString("ExileSpamMsg"), view.Recipient.PlayerId, title, send: send);
                }
                Replay(view, send);
                batch?.Send();
            }
        }
    }
}
