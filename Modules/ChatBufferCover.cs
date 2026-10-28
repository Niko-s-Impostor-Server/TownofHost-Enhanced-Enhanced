using TOHE.Modules.ChatManager;
using TOHE.Roles.Core;

namespace TOHE;

internal static class ChatBufferCover
{
    internal enum Kind { Guess, Compare, President, Duel }

    internal static void Send(Kind kind)
    {
        if (HostOnlyChatCommand.IsActive) return;

        int fillCount = ChatManager.GetChatBufferFillCount();
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
            for (int i = 0; i < fillCount; i++)
            {
                string msg = "/";
                if (kind == Kind.President)
                    msg += rd.Next(1, 100) < 20 ? "finish" : "reveal";
                else if (rd.Next(1, 100) < 20)
                    msg += "id";
                else if (kind == Kind.Guess)
                {
                    msg += guessCommands[rd.Next(0, guessCommands.Length - 1)];
                    msg += rd.Next(1, 100) < 50 ? string.Empty : " ";
                    msg += rd.Next(0, 15);
                    msg += rd.Next(1, 100) < 50 ? string.Empty : " ";
                    CustomRoles role = roles.RandomElement();
                    msg += rd.Next(1, 100) < 50 ? string.Empty : " ";
                    msg += Utils.GetRoleName(role);
                }
                else if (kind == Kind.Compare)
                    msg += compareCommands[rd.Next(0, compareCommands.Length - 1)] + " " + rd.Next(0, 15) + " " + rd.Next(0, 15);
                else
                    msg += duelCommands[rd.Next(0, duelCommands.Length - 1)] + " " + rd.Next(0, 3);

                OfficialChat.Send(Main.AllAlivePlayerControls.RandomElement(), msg);
            }
        }
        finally { ChatUpdatePatch.DoBlockChat = false; }

        ChatManager.SendPreviousMessagesToAll();
    }
}
