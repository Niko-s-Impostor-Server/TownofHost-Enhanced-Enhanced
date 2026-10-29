using System.Collections.Generic;
using System.Linq;

namespace TOHE;

internal sealed class ChatViewHistory
{
    internal enum Kind { System, PlayerText, QuickChat }
    internal readonly record struct ReferencedName(byte PlayerId, string Name);

    internal sealed record Entry(Kind Type, byte SpeakerId, string Title, string Text, byte[] QuickChatPayload,
        bool TextHasNamePadding, ReferencedName[] ReferencedNames);

    private readonly Dictionary<int, List<Entry>> entriesByRecipient = [];

    internal void Add(int recipientId, Entry entry, int capacity)
    {
        if (capacity <= 0)
        {
            entriesByRecipient.Remove(recipientId);
            return;
        }

        if (!entriesByRecipient.TryGetValue(recipientId, out var entries))
            entriesByRecipient.Add(recipientId, entries = []);

        entries.Add(Copy(entry));
        int excess = entries.Count - capacity;
        if (excess > 0) entries.RemoveRange(0, excess);
    }

    internal Entry[] Snapshot(int recipientId)
        => entriesByRecipient.TryGetValue(recipientId, out var entries)
            ? entries.Select(Copy).ToArray()
            : [];

    internal void Clear() => entriesByRecipient.Clear();
    internal void Remove(int recipientId) => entriesByRecipient.Remove(recipientId);

    private static Entry Copy(Entry entry)
        => entry with { QuickChatPayload = entry.QuickChatPayload.ToArray(), ReferencedNames = entry.ReferencedNames.ToArray() };
}
