using System.Collections.Generic;

namespace TOHE;

// One monotonic nonce per owner, reset with the meeting. Invalid actions also
// consume their nonce, so a rejected request cannot be replayed with another target.
internal sealed class MeetingAbilityReplayWindow
{
    private int game;
    private uint meeting;
    private bool active;
    private readonly Dictionary<int, uint> last = [];

    internal void Begin(int gameId, uint meetingId)
    {
        game = gameId;
        meeting = meetingId;
        active = true;
        last.Clear();
    }

    internal bool Accept(int gameId, uint meetingId, int owner, uint nonce, bool authorized)
    {
        if (!active || gameId != game || meetingId != meeting || owner < 0 || nonce == 0 ||
            last.TryGetValue(owner, out uint previous) && nonce <= previous) return false;
        last[owner] = nonce;
        return authorized;
    }

    internal void Clear()
    {
        last.Clear();
        active = false;
    }
}
