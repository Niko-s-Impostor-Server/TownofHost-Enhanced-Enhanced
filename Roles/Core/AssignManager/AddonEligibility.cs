using TOHE.Roles.AddOns.Common;
using TOHE.Roles.AddOns.Crewmate;
using TOHE.Roles.AddOns.Impostor;
using TOHE.Roles.Crewmate;
using TOHE.Roles.Impostor;
using TOHE.Roles.Neutral;

namespace TOHE.Roles.Core.AssignManager;

public static class AddonEligibility
{
    public static bool CanAssign(CustomRoles role, PlayerControl pc, bool checkLimitAddons = true, bool initialAssignment = false)
    {
        // Only add-ons
        if (!role.IsAdditionRole() || pc == null) return false;

        if (Options.AddonCanBeSettings.TryGetValue(role, out var o) && ((!o.Imp.GetBool() && pc.GetCustomRole().IsImpostor()) || (!o.Neutral.GetBool() && pc.GetCustomRole().IsNeutral()) || (!o.Crew.GetBool() && pc.GetCustomRole().IsCrewmate())))
            return false;

        // if player already has this addon
        else if (pc.Is(role)) return false;

        // Checking Lovers and Romantics
        else if ((pc.Is(CustomRoles.RuthlessRomantic) || pc.Is(CustomRoles.Romantic) || pc.Is(CustomRoles.VengefulRomantic)) && role is CustomRoles.Lovers) return false;

        // Checking for conflicts with roles
        else if (pc.Is(CustomRoles.GM) || role is CustomRoles.Lovers || pc.Is(CustomRoles.LazyGuy)) return false;

        if (checkLimitAddons)
            if (pc.GetCustomSubRoles().Count >= Options.NoLimitAddonsNumMax.GetInt()) return false;


        if (AddonCompatibility.Conflicts(role, pc.GetCustomRole(), pc.GetCustomSubRoles())) return false;

        // SetSubRole removes these identities when converting a player. Random
        // allocation must reject the combination instead of silently replacing an addon.
        if (role.IsConverted() && pc.IsAnySubRole(existing => existing.IsConverted()
            || existing is CustomRoles.Loyal or CustomRoles.Rascal or CustomRoles.Admired)) return false;
        if (role is CustomRoles.Loyal or CustomRoles.Rascal or CustomRoles.Admired
            && pc.IsAnySubRole(existing => existing.IsConverted())) return false;

        // Conditional rules remain centralized here for both startup and runtime grants.
        // Initial allocation uses role capabilities, not shield/cooldown/previous-round state.
        switch (role)
        {
            case var Addon when (pc.IsAnySubRole(x => x.IsSpeedRole()) || pc.GetCustomRole().IsSpeedRole()) && Addon.IsSpeedRole():
                return false;

            case CustomRoles.Bait:
                if (pc.Is(CustomRoles.Trapper)
                    || pc.Is(CustomRoles.Provocateur)
                    || pc.Is(CustomRoles.Unreportable)
                    || pc.Is(CustomRoles.Burst)
                    || pc.Is(CustomRoles.NiceMini)
                    || pc.Is(CustomRoles.Randomizer)
                    || pc.Is(CustomRoles.Solsticer)
                    || pc.Is(CustomRoles.PunchingBag)
                    || (pc.Is(CustomRoles.Onbound) && Bait.BaitNotification.GetBool())
                    || (pc.Is(CustomRoles.Rebound) && Bait.BaitNotification.GetBool())
                    || pc.Is(CustomRoles.GuardianAngelTOHE))
                    return false;
                break;

            case CustomRoles.Guesser:
                if (Options.GuesserMode.GetBool() && ((pc.GetCustomRole().IsCrewmate() && !Guesser.CrewCanBeGuesser.GetBool()) || (pc.GetCustomRole().IsNeutral() && !Guesser.NeutralCanBeGuesser.GetBool()) || (pc.GetCustomRole().IsImpostor() && !Guesser.ImpCanBeGuesser.GetBool())))
                    return false;

                if ((pc.Is(CustomRoles.Specter) && !Specter.CanGuess.GetBool())
                    || (pc.Is(CustomRoles.Terrorist) && (!Terrorist.TerroristCanGuess.GetBool() || Terrorist.CanTerroristSuicideWin.GetBool()))
                    || (pc.Is(CustomRoles.Workaholic) && !Workaholic.WorkaholicCanGuess.GetBool())
                    || (pc.Is(CustomRoles.Solsticer) && !Solsticer.SolsticerCanGuess.GetBool())
                    || (pc.Is(CustomRoles.God) && !God.CanGuess.GetBool()))
                    return false; //Based on guess manager
                if ((pc.GetCustomRole().IsCrewmate() && !Guesser.CrewCanBeGuesser.GetBool()) || (pc.GetCustomRole().IsNeutral() && !Guesser.NeutralCanBeGuesser.GetBool()) || (pc.GetCustomRole().IsImpostor() && !Guesser.ImpCanBeGuesser.GetBool()))
                    return false;
                break;

            case CustomRoles.Mundane:
                if (pc.HasImpKillButton() || !Utils.HasTasks(pc.Data, false) || pc.GetCustomRole().IsTasklessCrewmate() || pc.Is(Custom_Team.Impostor))
                    return false;
                if ((pc.GetCustomRole().IsCrewmate() && !Mundane.CanBeOnCrew.GetBool()) || (pc.GetCustomRole().IsNeutral() && !Mundane.CanBeOnNeutral.GetBool()))
                    return false;

                if ((pc.Is(CustomRoles.Specter) && !Specter.CanGuess.GetBool())
                    || (pc.Is(CustomRoles.Terrorist) && (!Terrorist.TerroristCanGuess.GetBool() || Terrorist.CanTerroristSuicideWin.GetBool()))
                    || (pc.Is(CustomRoles.Workaholic) && !Workaholic.WorkaholicCanGuess.GetBool())
                    || (pc.Is(CustomRoles.Solsticer) && !Solsticer.SolsticerCanGuess.GetBool())
                    || (pc.Is(CustomRoles.God) && !God.CanGuess.GetBool()))
                    return false; //Based on guess manager

                // return true only when its a guesser, NG, guesser mode on with crew can guess (if crew role) and nnk can guess (if nnk)
                if (pc.Is(CustomRoles.Guesser) || pc.Is(CustomRoles.NiceGuesser)) return true;
                if (Options.GuesserMode.GetBool())
                {
                    if (pc.GetCustomRole().IsNonNK() && Options.PassiveNeutralsCanGuess.GetBool())
                        return true;
                    if (pc.GetCustomRole().IsCrewmate() && Options.CrewmatesCanGuess.GetBool())
                        return true;
                    else return false;
                }
                else return false;

            case CustomRoles.Onbound:
                if (pc.Is(CustomRoles.SuperStar)
                    || Doctor.VisibleToEveryone(pc)
                    || (pc.Is(CustomRoles.Bait) && Bait.BaitNotification.GetBool())
                    || pc.Is(CustomRoles.LastImpostor)
                    || pc.Is(CustomRoles.NiceMini)
                    || pc.Is(CustomRoles.Mare)
                    || pc.Is(CustomRoles.Solsticer)
                    || pc.Is(CustomRoles.Rebound)
                    || pc.Is(CustomRoles.Workaholic) && !Workaholic.WorkaholicVisibleToEveryone.GetBool()
                    || pc.Is(CustomRoles.PunchingBag))
                    return false; //Based on guess manager
                break;

            case CustomRoles.Rebound:
                if (pc.Is(CustomRoles.SuperStar)
                    || Doctor.VisibleToEveryone(pc)
                    || (pc.Is(CustomRoles.Bait) && Bait.BaitNotification.GetBool())
                    || pc.Is(CustomRoles.LastImpostor)
                    || pc.Is(CustomRoles.NiceMini)
                    || pc.Is(CustomRoles.Mare)
                    || pc.Is(CustomRoles.Solsticer)
                    || pc.Is(CustomRoles.Onbound)
                    || pc.Is(CustomRoles.Workaholic) && !Workaholic.WorkaholicVisibleToEveryone.GetBool()
                    || pc.Is(CustomRoles.PunchingBag))
                {
                    return false;
                } //Based on guess manager
                break;

            case CustomRoles.DoubleShot:

                //Guesser roles when not guesser mode
                if (!Options.GuesserMode.GetBool() && !pc.Is(CustomRoles.EvilGuesser) && !pc.Is(CustomRoles.NiceGuesser) && (!pc.Is(CustomRoles.Doomsayer)) && !pc.Is(CustomRoles.Guesser))
                    return false;

                //If guesser mode but doomsayer can't die anyways
                if (pc.Is(CustomRoles.Doomsayer) && Doomsayer.DoesNotSuicideWhenMisguessing.GetBool())
                    return false;


                if (pc.Is(CustomRoles.CopyCat)
                    || pc.Is(CustomRoles.Workaholic) && !Workaholic.WorkaholicCanGuess.GetBool()
                    || (pc.Is(CustomRoles.Terrorist) && (!Terrorist.TerroristCanGuess.GetBool() || Terrorist.CanTerroristSuicideWin.GetBool())
                    || (pc.Is(CustomRoles.Specter) && !Specter.CanGuess.GetBool()))
                    || (pc.Is(CustomRoles.Solsticer) && !Solsticer.SolsticerCanGuess.GetBool())
                    || (pc.Is(CustomRoles.God) && !God.CanGuess.GetBool()))
                    return false;
                if (Options.GuesserMode.GetBool())
                {
                    if (DoubleShot.ImpCanBeDoubleShot.GetBool() && !pc.Is(CustomRoles.Guesser) && !pc.Is(CustomRoles.EvilGuesser) && (pc.Is(Custom_Team.Impostor) || pc.GetCustomRole().IsMadmate()) && !Options.ImpostorsCanGuess.GetBool())
                        return false;
                    if (DoubleShot.CrewCanBeDoubleShot.GetBool() && !pc.Is(CustomRoles.Guesser) && !pc.Is(CustomRoles.NiceGuesser) && (pc.Is(Custom_Team.Crewmate) && !Options.CrewmatesCanGuess.GetBool()))
                        return false;
                    if (DoubleShot.NeutralCanBeDoubleShot.GetBool() && !pc.Is(CustomRoles.Guesser) && !pc.Is(CustomRoles.Doomsayer) && ((pc.GetCustomRole().IsNonNK() && !Options.PassiveNeutralsCanGuess.GetBool()) || (pc.GetCustomRole().IsNK() && !Options.NeutralKillersCanGuess.GetBool()) || (pc.GetCustomRole().IsNA() && !Options.NeutralApocalypseCanGuess.GetBool())))
                        return false;
                }
                if ((pc.Is(Custom_Team.Impostor) && !DoubleShot.ImpCanBeDoubleShot.GetBool()) || (pc.Is(Custom_Team.Crewmate) && !DoubleShot.CrewCanBeDoubleShot.GetBool()) || (pc.Is(Custom_Team.Neutral) && !DoubleShot.NeutralCanBeDoubleShot.GetBool()))
                    return false;
                break;

            case CustomRoles.Reach:
                if (!(initialAssignment ? pc.GetRoleClass()?.CanUseKillButton(pc) == true : pc.CanUseKillButton()))
                    return false;
                break;

            case CustomRoles.Overclocked:
                if (!(initialAssignment ? pc.GetRoleClass()?.CanUseKillButton(pc) == true : pc.CanUseKillButton()))
                    return false;
                break;

            case CustomRoles.Lazy:
                if (!Lazy.CheckConflicts(pc))
                    return false;
                break;

            case CustomRoles.Ghoul:

                if (pc.GetCustomRole().IsNeutral() || pc.GetCustomRole().IsImpostor() || pc.GetCustomRole().IsTasklessCrewmate() || pc.GetCustomRole().IsTaskBasedCrewmate())
                    return false;
                break;

            case CustomRoles.Bloodthirst:

                if (!pc.GetCustomRole().IsCrewmate() || pc.GetCustomRole().IsTasklessCrewmate())
                    return false;
                break;

            case CustomRoles.Torch:

                if (!pc.GetCustomRole().IsCrewmate())
                    return false;
                break;

            case CustomRoles.Fragile:
                if (pc.Is(CustomRoles.Lucky)
                    || pc.Is(CustomRoles.Veteran)
                    || pc.Is(CustomRoles.Guardian)
                    || pc.Is(CustomRoles.Medic)
                    || pc.Is(CustomRoles.Bomber)
                    || pc.Is(CustomRoles.Jinx)
                    || pc.Is(CustomRoles.Solsticer)
                    || pc.Is(CustomRoles.CursedWolf)
                    || pc.Is(CustomRoles.PunchingBag)
                    || pc.Is(CustomRoles.SchrodingersCat)
                    || pc.IsNeutralApocalypse()
                    || pc.Is(CustomRoles.Spy)
                    || pc.Is(CustomRoles.Necromancer)
                    || pc.Is(CustomRoles.Demon)
                    || pc.Is(CustomRoles.Shaman)
                    || pc.Is(CustomRoles.Monarch)
                    || pc.Is(CustomRoles.Opportunist) && Opportunist.OppoImmuneToAttacksWhenTasksDone.GetBool())
                    return false;
                break;

            case CustomRoles.Bewilder:
                if (pc.Is(CustomRoles.Torch)
                    //|| pc.Is(CustomRoles.Sunglasses)
                    || pc.Is(CustomRoles.Randomizer)
                    || pc.Is(CustomRoles.Lighter)
                    || pc.Is(CustomRoles.Solsticer)
                    || pc.Is(CustomRoles.Tired)
                    || pc.Is(CustomRoles.GuardianAngelTOHE)
                    || pc.Is(CustomRoles.PunchingBag)
                    || pc.Is(CustomRoles.KillingMachine))
                    return false;
                break;

            case CustomRoles.Unlucky:
                if (pc.Is(CustomRoles.Vector)
                    || pc.Is(CustomRoles.Lucky)
                    || pc.Is(CustomRoles.Vector)
                    || pc.Is(CustomRoles.Solsticer)
                    || pc.Is(CustomRoles.Taskinator)
                    || pc.Is(CustomRoles.NiceMini)
                    || pc.Is(CustomRoles.PunchingBag)
                    || pc.IsTransformedNeutralApocalypse())
                    return false;
                break;

            case CustomRoles.Madmate:

                if (!pc.CanBeMadmate() || pc.IsAnySubRole(sub => sub.IsConverted()))
                    return false;
                break;

            case CustomRoles.Rebirth:
                if (pc.Is(CustomRoles.Doppelganger)
                    || pc.Is(CustomRoles.Jester)
                    || pc.Is(CustomRoles.Zombie)
                    || pc.Is(CustomRoles.Solsticer) || pc.IsNeutralApocalypse()) return false;
                break;

            case CustomRoles.Youtuber:

                if (!pc.GetCustomRole().IsCrewmate())
                    return false;
                break;

            case CustomRoles.Egoist:

                if (pc.GetCustomRole().IsNeutral() || pc.GetCustomRole().IsMadmate() || pc.IsAnySubRole(sub => sub.IsConverted()))
                    return false;
                if ((pc.GetCustomRole().IsImpostor() && !Egoist.ImpCanBeEgoist.GetBool()) || (pc.GetCustomRole().IsCrewmate() && !Egoist.CrewCanBeEgoist.GetBool()))
                    return false;
                break;

            case CustomRoles.Mimic:

                if (!pc.GetCustomRole().IsImpostor())
                    return false;
                break;

            case CustomRoles.Rascal:

                if (!pc.GetCustomRole().IsCrewmate())
                    return false;
                break;

            case CustomRoles.Stealer:

                if (!pc.GetCustomRole().IsImpostor())
                    return false;
                break;
            case CustomRoles.Tricky:

                if (!pc.GetCustomRole().IsImpostor())
                    return false;
                break;
            case CustomRoles.Mare:

                if (!pc.GetCustomRole().IsImpostor())
                    return false;
                break;

            case CustomRoles.Swift:

                if (!pc.GetCustomRole().IsImpostor())
                    return false;
                break;

            case CustomRoles.Nimble:
                if (Knight.CheckCanUseVent(pc)
                    || pc.Is(CustomRoles.CopyCat))
                    return false;
                if (!pc.GetCustomRole().IsTasklessCrewmate())
                    return false;
                break;

            case CustomRoles.Circumvent:
                if (pc.GetCustomRole() is CustomRoles.Vampire && !Vampire.CheckCanUseVent()
                    || pc.Is(CustomRoles.Witch) && Witch.ModeSwitchActionOpt.GetValue() == 1
                    || pc.Is(CustomRoles.Swooper)
                    || pc.Is(CustomRoles.Wildling)
                    || pc.Is(CustomRoles.KillingMachine)
                    || pc.Is(CustomRoles.Lurker)
                    || pc.Is(CustomRoles.Miner)
                    || pc.Is(CustomRoles.Prohibited)
                    || pc.Is(CustomRoles.DoubleAgent))
                    return false;
                if (!pc.Is(Custom_Team.Impostor))
                    return false;
                break;

            case CustomRoles.Clumsy:

                if (!pc.GetCustomRole().IsImpostor())
                    return false;
                break;

            case CustomRoles.Paranoia:

                if (!pc.GetCustomRole().IsImpostor() && !pc.GetCustomRole().IsCrewmate())
                    return false;
                if ((pc.GetCustomRole().IsImpostor() && !Paranoia.CanBeImp.GetBool()) || (pc.GetCustomRole().IsCrewmate() && !Paranoia.CanBeCrew.GetBool()))
                    return false;
                if (pc.GetCustomRole().IsNotKnightable() && Paranoia.DualVotes.GetBool())
                    return false;
                break;

            case CustomRoles.Loyal:
                if (pc.Is(CustomRoles.Madmate)
                    || pc.Is(CustomRoles.Oiiai)
                    || pc.Is(CustomRoles.GuardianAngelTOHE)
                    || pc.Is(CustomRoles.Influenced)
                    || pc.Is(CustomRoles.Solsticer)
                    || pc.Is(CustomRoles.NiceMini)
                    || pc.Is(CustomRoles.EvilMini)
                    || (pc.Is(CustomRoles.CopyCat) && CopyCat.CanCopyTeamChangingAddon()))
                    return false;
                if (!pc.GetCustomRole().IsImpostor() && !pc.GetCustomRole().IsCrewmate())
                    return false;
                if ((pc.GetCustomRole().IsImpostor() && !Loyal.ImpCanBeLoyal.GetBool()) || (pc.GetCustomRole().IsCrewmate() && !Loyal.CrewCanBeLoyal.GetBool()))
                    return false;
                break;

            case CustomRoles.Prohibited:
                if (Prohibited.GetCountBlokedVents() <= 0 || !pc.CanUseVents())
                    return false;
                if (pc.Is(CustomRoles.Ventguard)
                    || pc.Is(CustomRoles.Circumvent)
                    || pc.Is(CustomRoles.Jester) && Jester.CantMoveInVents.GetBool())
                    return false;
                break;

            case CustomRoles.Hurried:

                if (pc.Is(CustomRoles.Madmate) && !Hurried.CanBeOnMadMate.GetBool()) return false;
                if (!pc.GetCustomRole().IsCrewmate() && !pc.Is(CustomRoles.Madmate)) return false;
                if (pc.GetCustomRole().IsTasklessCrewmate()) return false;
                if (pc.GetCustomRole().IsTaskBasedCrewmate() && !Hurried.CanBeOnTaskBasedCrew.GetBool()) return false;
                break;

            case CustomRoles.Evader:
                if (pc.IsNeutralApocalypse())
                    return false;
                break;
        }

        return true;
    }
}
