using TOHE.Modules.ChatManager;
using TOHE.Roles.Core;

namespace TOHE;

internal static class ChatBufferCover
{
    internal enum Kind { Guess, Compare, President, Duel }

    internal static void Send(Kind kind)
    {
        if (HostOnlyChatCommand.IsActive) return;

        int bufferSize = ChatManager.BufferSize;
        var views = ChatManager.SnapshotViews();
        var rd = IRandom.Instance;
        string[] guessCommands = ["bet", "bt", "guess", "gs", "shoot", "st", "赌", "猜", "审判", "tl", "判", "审"];
        string[] compareCommands = ["cmp", "compare", "比较"];
        string[] duelCommands = ["duel", "rps"];
        var roles = kind == Kind.Guess
            ? CustomRolesHelper.AllRoles.Where(role => role is not CustomRoles.NotAssigned).ToArray()
            : null;

        ChatUpdatePatch.DoBlockChat = true;
        try
        {
            foreach (var view in views)
            {
                var batch = !view.Recipient.AmOwner && !view.Recipient.IsModded()
                    ? new ChatManager.VanillaReplayBatch(view.Recipient) : null;
                System.Action<Hazel.MessageWriter> send = batch == null ? null : batch.Add;
                var fillerActor = Main.AllAlivePlayerControls.OrderBy(x => x.PlayerId).FirstOrDefault() ?? view.Recipient;
                int replayCount = view.Entries.Count(entry => ChatManager.CanReplay(view, entry));
                for (int i = 0; i < bufferSize - replayCount; i++)
                {
                    string msg = "/";
                    if (kind == Kind.President)
                        msg += rd.Next(1, 100) < 20 ? "finish" : "reveal";
                    else if (rd.Next(1, 100) < 20)
                        msg += "id";
                    else if (kind == Kind.Guess)
                    {
                        msg += guessCommands[rd.Next(guessCommands.Length)];
                        msg += rd.Next(1, 100) < 50 ? string.Empty : " ";
                        msg += rd.Next(0, 15);
                        msg += rd.Next(1, 100) < 50 ? string.Empty : " ";
                        CustomRoles role = roles[rd.Next(roles.Length)];
                        msg += rd.Next(1, 100) < 50 ? string.Empty : " ";
                        msg += Utils.GetRoleName(role);
                    }
                    else if (kind == Kind.Compare)
                        msg += compareCommands[rd.Next(compareCommands.Length)] + " " + rd.Next(0, 15) + " " + rd.Next(0, 15);
                    else
                        msg += duelCommands[rd.Next(duelCommands.Length)] + " " + rd.Next(0, 3);

                    OfficialChat.Send(fillerActor, msg, view.Recipient.PlayerId, send: send);
                }
                ChatManager.Replay(view, send);
                batch?.Send();
            }
        }
        finally { ChatUpdatePatch.DoBlockChat = false; }

    }
}
