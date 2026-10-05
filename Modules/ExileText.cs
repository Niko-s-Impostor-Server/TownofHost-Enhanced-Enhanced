using Hazel;
using System;

namespace TOHE;

// The message is built once by the host's existing exile/winner policy. This owns presentation only.
internal static class ExileText
{
    private const int MaxTextLength = 4096;
    private sealed record Context(uint Generation, nint Client, int Game, int Host, object States, uint Meeting, nint MeetingPointer)
    {
        internal bool Current()
        {
            var client = AmongUsClient.Instance;
            return client != null && client.AmConnected && client.Pointer == Client
                && client.GameId == Game && client.HostId == Host && OnGameJoinedPatch.Generation == Generation
                && ReferenceEquals(Main.PlayerStates, States);
        }
    }
    private sealed record Pending(Context Context, byte PlayerId, string Text, bool AntiBlackout,
        NetworkedPlayerInfo Player, int Owner, string RealName)
    {
        internal bool Renamed;
    }
    private sealed record Cutscene(Context Context, ExileController Controller, byte PlayerId, bool NoExile);
    private static Pending pending;
    private static Cutscene cutscene;

    private static Context Capture(MeetingHud meeting)
    {
        var client = AmongUsClient.Instance;
        return new(OnGameJoinedPatch.Generation, client.Pointer, client.GameId, client.HostId,
            Main.PlayerStates, meeting.NetId, meeting.Pointer);
    }

    internal static void Reset()
    {
        RestoreName();
        Teardown();
    }

    // No old-room name RPC may be sent when joining or disconnecting a session.
    internal static void Teardown()
    {
        pending = null;
        cutscene = null;
    }

    internal static void Publish(NetworkedPlayerInfo player, string message, string realName, bool antiBlackout)
    {
        var client = AmongUsClient.Instance;
        var meeting = MeetingHud.Instance;
        if (client == null || !client.AmHost || !client.AmConnected || meeting == null || player == null) return;
        // The hidden suffix is needed only by vanilla name formatting, not direct TMP text.
        while (message.EndsWith("<size=0>", StringComparison.Ordinal)) message = message[..^8];
        if (string.IsNullOrWhiteSpace(message) || message.Length > MaxTextLength) return;
        var context = Capture(meeting);
        if (pending != null && pending.Context == context && pending.PlayerId == player.PlayerId
            && pending.Text == message && pending.AntiBlackout == antiBlackout) return;
        RestoreName();
        pending = new(context, player.PlayerId, message, antiBlackout, player, player.ClientId, realName);
        var writer = CustomRpcTransport.Start(CustomRPC.SyncExileText, SendOption.Reliable);
        writer.Write(client.GameId);
        writer.Write(meeting.NetId);
        writer.Write(player.PlayerId);
        writer.Write(antiBlackout);
        writer.Write(message);
        CustomRpcTransport.Finish(writer);
    }

    internal static void Receive(PlayerControl sender, MessageReader reader)
    {
        var client = AmongUsClient.Instance;
        if (client == null || !client.AmConnected || client.AmHost || sender == null || sender.OwnerId != client.HostId
            || !RpcCompatibility.IsCurrentClient(sender) || reader == null || reader.BytesRemaining < 11) return;
        var game = reader.ReadInt32();
        var meetingId = reader.ReadUInt32();
        var playerId = reader.ReadByte();
        var antiBlackout = reader.ReadBoolean();
        var message = reader.ReadString();
        if (reader.BytesRemaining != 0 || game != client.GameId || playerId >= 254
            || string.IsNullOrWhiteSpace(message) || message.Length > MaxTextLength) return;
        var meeting = MeetingHud.Instance;
        Context context;
        if (meeting != null && meeting.NetId == meetingId) context = Capture(meeting);
        else if (cutscene != null && cutscene.Context.Current() && cutscene.Controller
            && cutscene.Context.Meeting == meetingId && (cutscene.PlayerId == playerId || antiBlackout && cutscene.NoExile))
            context = cutscene.Context;
        else return;
        if (!Main.PlayerStates.ContainsKey(playerId)) return;
        pending = new(context, playerId, message, antiBlackout, null, -1, null);
        Apply();
    }

    // RpcClose calls Close locally before queuing the reliable close message. Send the vanilla name first.
    internal static void BeforeClose(MeetingHud meeting)
    {
        var entry = pending;
        if (entry == null || !entry.Context.Current() || AmongUsClient.Instance.AmHost != true
            || meeting == null || entry.Context.Meeting != meeting.NetId || entry.Context.MeetingPointer != meeting.Pointer
            || entry.Renamed) return;
        var player = Utils.GetPlayerById(entry.PlayerId);
        if (player == null || player.Data == null || player.Data.Disconnected || entry.Player == null
            || player.OwnerId != entry.Owner || player.Data.Pointer != entry.Player.Pointer) return;
        entry.Renamed = true;
        var name = entry.Text + "<size=0>";
        entry.Player.UpdateName(name, Utils.GetClientById(entry.Owner));
        player.RpcSetName(name);
    }

    internal static void Begin(ExileController controller, NetworkedPlayerInfo player, bool tie)
    {
        var client = AmongUsClient.Instance;
        var meeting = MeetingHud.Instance;
        if (client == null || !client.AmConnected || meeting == null || controller == null) return;
        cutscene = new(Capture(meeting), controller, player == null ? byte.MaxValue : player.PlayerId, player == null || tie);
        Apply();
    }

    private static void Apply()
    {
        var entry = pending;
        var active = cutscene;
        if (entry == null || active == null || !entry.Context.Current() || !active.Context.Current()
            || entry.Context != active.Context || !active.Controller
            || (active.NoExile ? !entry.AntiBlackout : active.PlayerId != entry.PlayerId)) return;
        var controller = active.Controller;
        controller.completeString = entry.Text;
        if (controller.Text && controller.Text.gameObject.activeSelf) controller.Text.text = entry.Text;
        // Counts are already included by the host's configured message policy.
        if (controller.ImpostorText) controller.ImpostorText.gameObject.SetActive(false);
        if (controller.initData != null) controller.initData.confirmImpostor = false;
    }

    internal static void RestoreName()
    {
        var entry = pending;
        if (entry == null || !entry.Renamed || !entry.Context.Current() || AmongUsClient.Instance.AmHost != true) return;
        entry.Renamed = false;
        var player = Utils.GetPlayerById(entry.PlayerId);
        if (player == null || player.Data == null || entry.Player == null || player.OwnerId != entry.Owner
            || player.Data.Pointer != entry.Player.Pointer) return;
        entry.Player.UpdateName(entry.RealName, Utils.GetClientById(entry.Owner));
        if (!player.Data.Disconnected) player.RpcSetName(entry.RealName);
    }
}

[HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Start))]
internal static class ExileTextMeetingStartPatch
{
    internal static void Prefix() => ExileText.Reset();
}

[HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Close))]
internal static class ExileTextMeetingClosePatch
{
    internal static void Prefix(MeetingHud __instance) => ExileText.BeforeClose(__instance);
}

[HarmonyPatch(typeof(ExileController), nameof(ExileController.BeginForGameplay))]
internal static class ExileTextBeginPatch
{
    internal static void Postfix(ExileController __instance, [HarmonyArgument(0)] NetworkedPlayerInfo player, [HarmonyArgument(1)] bool tie)
        => ExileText.Begin(__instance, player, tie);
}

[HarmonyPatch(typeof(ExileController), nameof(ExileController.ReEnableGameplay))]
internal static class ExileTextRestorePatch
{
    internal static void Postfix() => ExileText.Reset();
}

[HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameJoined))]
internal static class ExileTextJoinedPatch
{
    internal static void Prefix() => ExileText.Teardown();
}

[HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnDisconnected))]
internal static class ExileTextDisconnectedPatch
{
    internal static void Prefix() => ExileText.Teardown();
}
