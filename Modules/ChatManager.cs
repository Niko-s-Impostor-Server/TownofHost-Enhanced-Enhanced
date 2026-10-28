using Hazel;
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
        private static readonly List<Dictionary<byte, string>> chatHistory = [];
        private static readonly Dictionary<byte, string> LastSystemChatMsg = [];
        private const int maxHistorySize = 20;
        public static List<string> ChatSentBySystem = [];
        public static void ResetHistory()
        {
            chatHistory.Clear();
            LastSystemChatMsg.Clear();
            ChatSentBySystem.Clear();
            cancel = false;
        }
        public static void ClearLastSysMsg()
        {
            LastSystemChatMsg.Clear();
        }
        public static void AddSystemChatHistory(byte playerId, string msg)
        {
            LastSystemChatMsg[playerId] = msg;
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
            string playername = player.GetNameWithRole();
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
                if (!player.IsAlive()) return;
                message = msg;
                //Logger.Warn($"Logging msg : {message}","Checking Exile");
                Dictionary<byte, string> newChatEntry = new()
                    {
                        { player.PlayerId, message }
                    };
                chatHistory.Add(newChatEntry);

                if (chatHistory.Count > maxHistorySize)
                {
                    chatHistory.RemoveAt(0);
                }
                cancel = false;
            }
        }

        public static void SendPreviousMessagesToAll()
        {
            if (HostOnlyChatCommand.IsActive) return;
            if (!AmongUsClient.Instance.AmHost || !GameStates.IsModHost) return;
            int bufferSize = DestroyableSingleton<HudManager>.Instance.Chat.chatBubblePool.poolSize;
            var history = GetReplayableHistory(bufferSize);
            //This should never function for non host
            if (GameStates.IsExilling && history.Length < bufferSize)
            {
                var firstAlivePlayer = Main.AllAlivePlayerControls.OrderBy(x => x.PlayerId).FirstOrDefault();
                if (firstAlivePlayer == null) return;

                var title = "<color=#aaaaff>" + GetString("DefaultSystemMessageTitle") + "</color>";
                string spamMsg = GetString("ExileSpamMsg");

                for (int i = 0; i < bufferSize - history.Length; i++)
                {
                    OfficialChat.Send(firstAlivePlayer, spamMsg, title: title);
                }
            }
            foreach (var (senderPlayer, senderMessage) in history)
            {
                OfficialChat.Send(senderPlayer, senderMessage);
            }
            foreach (var playerId in LastSystemChatMsg.Keys.ToArray())
            {
                var pc = playerId.GetPlayer();
                if (pc == null && playerId != byte.MaxValue) continue;
                var title = "<color=#FF0000>" + GetString("LastMessageReplay") + "</color>";
                Utils.SendMessage(LastSystemChatMsg[playerId], playerId, title: title, noReplay: true);
            }
        }

        internal static int GetChatBufferFillCount()
        {
            int bufferSize = DestroyableSingleton<HudManager>.Instance.Chat.chatBubblePool.poolSize;
            return bufferSize - GetReplayableHistory(bufferSize).Length;
        }

        private static (PlayerControl Sender, string Message)[] GetReplayableHistory(int bufferSize) =>
            chatHistory.Select(entry => (Sender: entry.Keys.First().GetPlayer(), Message: entry.Values.First()))
                .Where(entry => entry.Sender?.Data != null).TakeLast(bufferSize).ToArray();
    }
}
