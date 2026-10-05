namespace TOHE.Roles.Core.AssignManager;

// Unconditional exclusions are data, shared by startup and runtime eligibility.
// Addon-to-addon conflicts are symmetric even when the historical rule was one-sided.
public static class AddonCompatibility
{
    private static readonly Dictionary<CustomRoles, CustomRoles[]> Exclusions = new()
    {
        [CustomRoles.Autopsy] = [CustomRoles.Doctor, CustomRoles.Tracefinder, CustomRoles.ScientistTOHE, CustomRoles.Sunnyboy],
        [CustomRoles.Trapper] =
        [
            CustomRoles.Bait, CustomRoles.Burst, CustomRoles.Randomizer, CustomRoles.Solsticer,
            CustomRoles.GuardianAngelTOHE, CustomRoles.PunchingBag,
        ],
        [CustomRoles.Guesser] =
        [
            CustomRoles.EvilGuesser, CustomRoles.NiceGuesser, CustomRoles.Judge, CustomRoles.CopyCat, CustomRoles.Doomsayer,
            CustomRoles.Nemesis, CustomRoles.Councillor, CustomRoles.GuardianAngelTOHE, CustomRoles.PunchingBag,
        ],
        [CustomRoles.Mundane] =
        [
            CustomRoles.CopyCat, CustomRoles.Doomsayer, CustomRoles.GuardianAngelTOHE, CustomRoles.Collector,
            CustomRoles.Ghoul,
        ],
        [CustomRoles.Cyber] = [CustomRoles.Doppelganger, CustomRoles.Celebrity, CustomRoles.SuperStar],
        [CustomRoles.Ghoul] = [CustomRoles.Lazy, CustomRoles.LazyGuy, CustomRoles.Mundane],
        [CustomRoles.Bloodthirst] =
        [
            CustomRoles.Lazy, CustomRoles.Merchant, CustomRoles.Alchemist, CustomRoles.LazyGuy, CustomRoles.Crewpostor,
            CustomRoles.Bodyguard,
        ],
        [CustomRoles.Torch] =
        [
            CustomRoles.Bewilder, CustomRoles.Lighter, CustomRoles.Tired, CustomRoles.GuardianAngelTOHE,
            CustomRoles.KillingMachine,
        ],
        [CustomRoles.VoidBallot] =
        [
            CustomRoles.Mayor, CustomRoles.Vindicator, CustomRoles.Stealer, CustomRoles.Pickpocket, CustomRoles.Dictator,
            CustomRoles.Influenced, CustomRoles.Silent, CustomRoles.Tiebreaker, CustomRoles.Paranoia,
        ],
        [CustomRoles.Glow] = [CustomRoles.KillingMachine],
        [CustomRoles.Antidote] = [CustomRoles.Diseased, CustomRoles.Solsticer],
        [CustomRoles.Diseased] = [CustomRoles.Antidote, CustomRoles.Solsticer],
        [CustomRoles.Seer] = [CustomRoles.Mortician, CustomRoles.EvilTracker, CustomRoles.GuardianAngelTOHE],
        [CustomRoles.Sleuth] =
        [
            CustomRoles.Oblivious, CustomRoles.Detective, CustomRoles.Mortician, CustomRoles.Cleaner, CustomRoles.Medusa,
            CustomRoles.Vulture, CustomRoles.Coroner,
        ],
        [CustomRoles.Necroview] = [CustomRoles.Doctor, CustomRoles.God, CustomRoles.Visionary, CustomRoles.GuardianAngelTOHE],
        [CustomRoles.Lucky] =
        [
            CustomRoles.Guardian, CustomRoles.Unlucky, CustomRoles.Solsticer, CustomRoles.Fragile, CustomRoles.PunchingBag,
        ],
        [CustomRoles.Madmate] =
        [
            CustomRoles.Sidekick, CustomRoles.SuperStar, CustomRoles.Egoist, CustomRoles.Rascal, CustomRoles.NiceMini,
        ],
        [CustomRoles.Oblivious] =
        [
            CustomRoles.Detective, CustomRoles.Vulture, CustomRoles.Sleuth, CustomRoles.Cleaner, CustomRoles.Amnesiac,
            CustomRoles.Coroner, CustomRoles.Medusa, CustomRoles.Mortician, CustomRoles.Medium, CustomRoles.KillingMachine,
            CustomRoles.GuardianAngelTOHE, CustomRoles.Altruist,
        ],
        [CustomRoles.Tiebreaker] =
        [
            CustomRoles.Dictator, CustomRoles.VoidBallot, CustomRoles.Influenced, CustomRoles.GuardianAngelTOHE,
        ],
        [CustomRoles.Youtuber] =
        [
            CustomRoles.Madmate, CustomRoles.NiceMini, CustomRoles.Randomizer, CustomRoles.Sheriff, CustomRoles.Hurried,
            CustomRoles.Solsticer, CustomRoles.GuardianAngelTOHE,
        ],
        [CustomRoles.Egoist] =
        [
            CustomRoles.Sidekick, CustomRoles.Madmate, CustomRoles.Hurried, CustomRoles.Gangster, CustomRoles.Admirer,
            CustomRoles.GuardianAngelTOHE,
        ],
        [CustomRoles.Mimic] = [CustomRoles.Nemesis],
        [CustomRoles.Rascal] = [CustomRoles.SuperStar, CustomRoles.NiceMini, CustomRoles.Madmate],
        [CustomRoles.Stealer] = [CustomRoles.Vindicator, CustomRoles.Bomber, CustomRoles.VoidBallot, CustomRoles.Swift],
        [CustomRoles.Tricky] =
        [
            CustomRoles.Mastermind, CustomRoles.Vampire, CustomRoles.Puppeteer, CustomRoles.Scavenger,
            CustomRoles.Lightning, CustomRoles.Swift, CustomRoles.Swooper, CustomRoles.DoubleAgent,
        ],
        [CustomRoles.Mare] =
        [
            CustomRoles.Underdog, CustomRoles.Berserker, CustomRoles.Inhibitor, CustomRoles.Saboteur, CustomRoles.Swift,
            CustomRoles.Nemesis, CustomRoles.Sniper, CustomRoles.Fireworker, CustomRoles.Ludopath, CustomRoles.Swooper,
            CustomRoles.Vampire, CustomRoles.Arrogance, CustomRoles.LastImpostor, CustomRoles.Bomber, CustomRoles.Trapster,
            CustomRoles.Onbound, CustomRoles.Rebound, CustomRoles.Tired,
        ],
        [CustomRoles.Swift] =
        [
            CustomRoles.Bomber, CustomRoles.Trapster, CustomRoles.Kamikaze, CustomRoles.Swooper, CustomRoles.Vampire,
            CustomRoles.Scavenger, CustomRoles.Puppeteer, CustomRoles.Mastermind, CustomRoles.Warlock, CustomRoles.Witch,
            CustomRoles.Penguin, CustomRoles.Nemesis, CustomRoles.Mare, CustomRoles.Clumsy, CustomRoles.Wildling,
            CustomRoles.Consigliere, CustomRoles.Butcher, CustomRoles.KillingMachine, CustomRoles.Gangster,
            CustomRoles.BountyHunter, CustomRoles.Lightning, CustomRoles.Hangman, CustomRoles.Stealer, CustomRoles.Tricky,
            CustomRoles.DoubleAgent, CustomRoles.YinYanger,
        ],
        [CustomRoles.Clumsy] = [CustomRoles.Swift, CustomRoles.Bomber, CustomRoles.KillingMachine],
        [CustomRoles.Burst] =
        [
            CustomRoles.Avanger, CustomRoles.Trapper, CustomRoles.Solsticer, CustomRoles.Bait, CustomRoles.PunchingBag,
        ],
        [CustomRoles.Avanger] =
        [
            CustomRoles.Burst, CustomRoles.Randomizer, CustomRoles.Solsticer, CustomRoles.NiceMini, CustomRoles.PunchingBag,
        ],
        [CustomRoles.Paranoia] = [CustomRoles.Dictator, CustomRoles.Madmate, CustomRoles.GuardianAngelTOHE],
        [CustomRoles.Gravestone] =
        [
            CustomRoles.SuperStar, CustomRoles.Innocent, CustomRoles.Solsticer, CustomRoles.NiceMini, CustomRoles.Marshall,
        ],
        [CustomRoles.Unreportable] = [CustomRoles.Randomizer, CustomRoles.Solsticer, CustomRoles.Bait],
        [CustomRoles.Flash] =
        [
            CustomRoles.Swooper, CustomRoles.Solsticer, CustomRoles.Tired, CustomRoles.Statue, CustomRoles.Seeker,
            CustomRoles.Doppelganger, CustomRoles.DollMaster, CustomRoles.Sloth, CustomRoles.Zombie, CustomRoles.Wraith,
            CustomRoles.Spurt,
        ],
        [CustomRoles.Fool] = [CustomRoles.Mechanic, CustomRoles.GuardianAngelTOHE, CustomRoles.Alchemist, CustomRoles.Troller],
        [CustomRoles.Influenced] =
        [
            CustomRoles.Dictator, CustomRoles.Loyal, CustomRoles.VoidBallot, CustomRoles.Tiebreaker, CustomRoles.Collector,
            CustomRoles.Keeper,
        ],
        [CustomRoles.Oiiai] = [CustomRoles.Loyal, CustomRoles.Solsticer, CustomRoles.Innocent, CustomRoles.PunchingBag],
        [CustomRoles.Hurried] = [CustomRoles.Youtuber, CustomRoles.Egoist, CustomRoles.Cleanser, CustomRoles.Solsticer],
        [CustomRoles.Silent] = [CustomRoles.Dictator, CustomRoles.VoidBallot],
        [CustomRoles.Rainbow] =
        [
            CustomRoles.Doppelganger, CustomRoles.DollMaster, CustomRoles.Chameleon, CustomRoles.Swooper,
            CustomRoles.Alchemist, CustomRoles.Wraith,
        ],
        [CustomRoles.Tired] =
        [
            CustomRoles.Overseer, CustomRoles.Alchemist, CustomRoles.Torch, CustomRoles.Bewilder, CustomRoles.Lighter,
            CustomRoles.Flash, CustomRoles.Mare, CustomRoles.Sloth, CustomRoles.Troller,
        ],
        [CustomRoles.Statue] = [CustomRoles.Alchemist, CustomRoles.Flash, CustomRoles.Tired, CustomRoles.Sloth],
        [CustomRoles.Susceptible] = [CustomRoles.Jester],
        [CustomRoles.Sloth] =
        [
            CustomRoles.Swooper, CustomRoles.Solsticer, CustomRoles.Tired, CustomRoles.Statue, CustomRoles.Seeker,
            CustomRoles.Doppelganger, CustomRoles.DollMaster, CustomRoles.Flash, CustomRoles.Zombie, CustomRoles.Wraith,
            CustomRoles.Spurt,
        ],
    };

    public static bool Conflicts(CustomRoles addon, CustomRoles mainRole, IEnumerable<CustomRoles> subRoles)
        => Excludes(addon, mainRole) || subRoles.Any(existing => AreIncompatible(addon, existing));

    public static bool AreIncompatible(CustomRoles first, CustomRoles second)
        => Excludes(first, second) || Excludes(second, first);

    private static bool Excludes(CustomRoles role, CustomRoles other)
        => Exclusions.TryGetValue(role, out var conflicts) && conflicts.Contains(other);
}
