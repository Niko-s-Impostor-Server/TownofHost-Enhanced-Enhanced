namespace TOHE;

internal static class ShortNamePolicy
{
    internal static bool ShouldShorten(int mode, bool requested, bool meeting, bool task) => requested && mode switch
    {
        1 => meeting || task,
        2 => meeting,
        3 => task,
        _ => false
    };
}
