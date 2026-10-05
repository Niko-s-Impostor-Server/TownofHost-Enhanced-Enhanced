using System;

namespace TOHE.Roles.Core.AssignManager;

// Versioned SplitMix64 streams. Do not replace with System.Random, string.GetHashCode,
// or the gameplay RNG: their state/implementation is outside the replay contract.
public sealed class AssignmentRandom
{
    public const int Version = 1;
    private ulong state;

    public AssignmentRandom(int seed, uint stream = 0)
    {
        state = ((ulong)(uint)seed << 32) | stream;
    }

    private ulong NextUInt64()
    {
        unchecked
        {
            ulong value = (state += 0x9E3779B97F4A7C15UL);
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }
    }

    public int Next(int maxExclusive)
    {
        if (maxExclusive <= 0) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
        ulong bound = (uint)maxExclusive;
        ulong threshold = unchecked(0UL - bound) % bound;
        ulong value;
        do { value = NextUInt64(); } while (value < threshold);
        return (int)(value % bound);
    }

    public bool Roll(int percent) => percent >= 100 || (percent > 0 && Next(100) < percent);

    public void Shuffle<T>(IList<T> values)
    {
        for (int i = values.Count - 1; i > 0; i--)
        {
            int other = Next(i + 1);
            (values[i], values[other]) = (values[other], values[i]);
        }
    }
}

internal static class AssignmentStreams
{
    internal const uint Roles = 1;
    internal const uint Players = 2;
    internal const uint NeutralCounts = 3;
    internal const uint Mini = 4;
    internal const uint HiddenRoles = 5;
    internal const uint AddonOrder = 6;
    internal const uint AddonChance = 0x10000;
    internal const uint AddonPlayers = 0x20000;
}
