using AmongUs.GameOptions;
using System;
using TOHE.Roles.AddOns.Common;
using TOHE.Roles.AddOns.Crewmate;
using TOHE.Roles.AddOns.Impostor;
using TOHE.Roles.Core;
using TOHE.Roles.Crewmate;
using TOHE.Roles.Impostor;
using TOHE.Roles.Neutral;
using static TOHE.Roles.Core.CustomRoleManager;

namespace TOHE;

public static class CustomRolesHelper
{
    public static readonly CustomRoles[] AllRoles = EnumHelper.GetAllValues<CustomRoles>();
    public static Dictionary<CustomRoles, Type> DuplicatedRoles;
    public static readonly Custom_Team[] AllRoleTypes = EnumHelper.GetAllValues<Custom_Team>();
    public static CustomRoles GetVNRole(this CustomRoles role) // RoleBase: Impostor, Shapeshifter, Crewmate, Engineer, Scientist
    {
        // Vanilla roles
        if (role.IsVanilla()) return role;

        // Role base
        if (role.GetStaticRoleClass() is not DefaultSetup) return role.GetStaticRoleClass().ThisRoleBase;

        //Default
        return role switch
        {
            CustomRoles.ShapeshifterTOHE => CustomRoles.Shapeshifter,
            CustomRoles.PhantomTOHE => CustomRoles.Phantom,
            CustomRoles.ScientistTOHE => CustomRoles.Scientist,
            CustomRoles.EngineerTOHE => CustomRoles.Engineer,
            CustomRoles.NoisemakerTOHE => CustomRoles.Noisemaker,
            CustomRoles.TrackerTOHE => CustomRoles.Tracker,
            CustomRoles.DetectiveTOHE => CustomRoles.DetectiveVanilla,
            CustomRoles.ViperTOHE => CustomRoles.Viper,
            _ => role.IsImpostor() ? CustomRoles.Impostor : CustomRoles.Crewmate,
        };
    }

    public static RoleTypes GetDYRole(this CustomRoles role) // Role has a kill button (Non-Impostor)
    {
        if (role is CustomRoles.Killer) return RoleTypes.Impostor; // FFA

        return (role.GetStaticRoleClass().ThisRoleBase is CustomRoles.Impostor or CustomRoles.Shapeshifter) && !role.IsImpostor()
            ? role.GetStaticRoleClass().ThisRoleBase.GetRoleTypes()
            : RoleTypes.GuardianAngel;
    }

    public static bool HasImpKillButton(this PlayerControl player, bool considerVanillaShift = false)
    {
        if (player == null) return false;
        var customRole = player.GetCustomRole();
        bool ModSideHasKillButton = customRole.GetDYRole() == RoleTypes.Impostor || customRole.GetVNRole() is CustomRoles.Impostor or CustomRoles.Shapeshifter or CustomRoles.Phantom or CustomRoles.Viper;

        if (player.IsModded() || (!considerVanillaShift && !player.IsModded()))
            return ModSideHasKillButton;

        bool vanillaSideHasKillButton = EAC.OriginalRoles.TryGetValue(player.PlayerId, out var OriginalRole) ?
                                         (OriginalRole.GetDYRole() is RoleTypes.Impostor or RoleTypes.Shapeshifter || OriginalRole.GetVNRole() is CustomRoles.Impostor or CustomRoles.Shapeshifter or CustomRoles.Phantom or CustomRoles.Viper) : ModSideHasKillButton;

        return vanillaSideHasKillButton;
    }
    //This is a overall check for vanilla clients to see if they are imp basis 
    public static bool IsGhostRole(this CustomRoles role)
    {
        if (role.GetStaticRoleClass().ThisRoleType is
            Custom_RoleType.CrewmateGhosts or
            Custom_RoleType.CrewmateVanillaGhosts or
            Custom_RoleType.ImpostorGhosts)
            return true;

        return role is
        CustomRoles.EvilSpirit;

    }
    public static bool HasGhostRole(this PlayerControl player) => player.GetCustomRole().IsGhostRole() || player.IsAnySubRole(x => x.IsGhostRole());


    /*
    public static bool IsExperimental(this CustomRoles role)
    {
        return role is
            CustomRoles.Disperser or
            CustomRoles.Doppelganger or
            CustomRoles.God or
            CustomRoles.Quizmaster;
    }
    */

    // Add-ons
    public static bool IsAdditionRole(this CustomRoles role) => role > CustomRoles.NotAssigned;

    public static bool IsAmneMaverick(this CustomRoles role) // ROLE ASSIGNING, NOT NEUTRAL TYPE
    {
        return role is
            CustomRoles.Jester or
            CustomRoles.Terrorist or
            CustomRoles.Opportunist or
            CustomRoles.PunchingBag or
            CustomRoles.Huntsman or
            CustomRoles.Executioner or
            CustomRoles.Vector or
            CustomRoles.Shaman or
            CustomRoles.Crewpostor or
            CustomRoles.Lawyer or
            CustomRoles.God or
            CustomRoles.Amnesiac or
            CustomRoles.Glitch or
            CustomRoles.Imitator or
            CustomRoles.Bandit or
            CustomRoles.Pestilence or
            CustomRoles.PlagueBearer or
            CustomRoles.Agitater or
            CustomRoles.Innocent or
            CustomRoles.Vulture or
            CustomRoles.Taskinator or
            CustomRoles.Pursuer or
            CustomRoles.Revolutionist or
            CustomRoles.Provocateur or
            CustomRoles.Demon or
            CustomRoles.Hater or
            CustomRoles.Workaholic or
            CustomRoles.Solsticer or
            CustomRoles.Collector or
            CustomRoles.Sunnyboy or
            CustomRoles.Arsonist or
            CustomRoles.Maverick or
            CustomRoles.CursedSoul or
            CustomRoles.Specter or
            CustomRoles.Stalker or
            CustomRoles.Doomsayer or
            CustomRoles.SoulCollector or
            CustomRoles.Death or
            CustomRoles.Berserker or
            CustomRoles.War or
            CustomRoles.Baker or
            CustomRoles.Famine or
            CustomRoles.Pirate or
            CustomRoles.Seeker or
            CustomRoles.Pixie or
            CustomRoles.Romantic or
            CustomRoles.RuthlessRomantic or
            CustomRoles.VengefulRomantic or
            CustomRoles.Doppelganger or
            CustomRoles.SchrodingersCat or
            CustomRoles.Follower;
    }
    public static bool IsAmneCrew(this PlayerControl target)
    {
        return target.IsCrewVenter()
                || target.GetCustomRole() is
                CustomRoles.Sheriff or
                CustomRoles.LazyGuy or
                CustomRoles.SuperStar or
                CustomRoles.Celebrity or
                CustomRoles.Mayor or
                CustomRoles.Dictator or
                CustomRoles.NiceGuesser or
                CustomRoles.Bodyguard or
                CustomRoles.Observer or
                CustomRoles.Retributionist or
                CustomRoles.Lookout or
                CustomRoles.Admirer or
                CustomRoles.Cleanser or
                CustomRoles.CopyCat or
                CustomRoles.Deceiver or
                CustomRoles.Crusader or
                CustomRoles.Overseer or
                CustomRoles.Jailer or
                CustomRoles.Judge or
                CustomRoles.Medic or
                CustomRoles.Medium or
                CustomRoles.Monarch or
                CustomRoles.Telecommunication or
                CustomRoles.Swapper or
                CustomRoles.Mechanic;
    }
    public static bool IsAmneNK(this CustomRoles role)
    {
        return role is
            CustomRoles.Sidekick or
            CustomRoles.Infectious or
            CustomRoles.Pyromaniac or
            CustomRoles.Medusa or
            CustomRoles.Necromancer or
            CustomRoles.Wraith or
            CustomRoles.Shroud or
            CustomRoles.Pelican or
            CustomRoles.Refugee or
            CustomRoles.Parasite or
            CustomRoles.PlagueDoctor or
            CustomRoles.SerialKiller or
            CustomRoles.Werewolf or
            CustomRoles.Pickpocket or
            CustomRoles.Traitor or
            CustomRoles.Virus or
            CustomRoles.Spiritcaller or
            CustomRoles.Jackal or
            CustomRoles.Juggernaut or
            CustomRoles.BloodKnight or
            CustomRoles.Cultist;
    }
    public static bool IsTasklessCrewmate(this CustomRoles role)
    {
        // Based on Imp but counted as crewmate
        return role.GetVNRole() is CustomRoles.Impostor && role.IsCrewmate();
    }
    public static bool IsTaskBasedCrewmate(this CustomRoles role)
    {
        return role is
            CustomRoles.Snitch or
            CustomRoles.FortuneTeller or
            CustomRoles.Marshall or
            CustomRoles.TimeManager or
            CustomRoles.Guardian or
            CustomRoles.Merchant or
            CustomRoles.Mayor or
            CustomRoles.Captain or
            CustomRoles.Transporter or
            CustomRoles.Retributionist or
            CustomRoles.Benefactor or
            CustomRoles.Alchemist;
    }
    public static bool IsCrewKiller(this CustomRoles role)
    {
        return role.GetStaticRoleClass().ThisRoleType is Custom_RoleType.CrewmateKilling;
    }
    public static bool IsCrewVenter(this PlayerControl target)
    {
        return target.Is(CustomRoles.EngineerTOHE)
            || target.Is(CustomRoles.Mechanic)
            || target.Is(CustomRoles.CopyCat)
            || target.Is(CustomRoles.Telecommunication) && Telecommunication.CanUseVent()
            || Knight.CheckCanUseVent(target)
            || target.Is(CustomRoles.Nimble);
    }
    public static bool IsNeutral(this CustomRoles role)
    {
        if (role is
            //FFA
            CustomRoles.Killer) return true;

        return role.IsNK() || role.IsNonNK() || role.IsNA() || role.IsMadmate();
    }
    public static bool IsNK(this CustomRoles role)
    {
        return role.GetStaticRoleClass().ThisRoleType is Custom_RoleType.NeutralKilling;
    }
    public static bool IsNonNK(this CustomRoles role) // ROLE ASSIGNING, NOT NEUTRAL TYPE
    {
        return role.IsNB() || role.IsNE() || role.IsNC();
    }
    public static bool IsNA(this CustomRoles role)
    {
        return role.GetStaticRoleClass().ThisRoleType is Custom_RoleType.NeutralApocalypse;
    }
    public static bool IsTNA(this CustomRoles role) // Transformed Neutral Apocalypse
    {
        return role is
            CustomRoles.Pestilence or
            CustomRoles.War or
            CustomRoles.Death or
            CustomRoles.Famine;
    }
    public static bool IsNB(this CustomRoles role)
    {
        return role.GetStaticRoleClass().ThisRoleType
            is Custom_RoleType.NeutralBenign;
    }
    public static bool IsNE(this CustomRoles role)
    {
        return role.GetStaticRoleClass().ThisRoleType
            is Custom_RoleType.NeutralEvil;
    }
    public static bool IsNC(this CustomRoles role)
    {
        return role.GetStaticRoleClass().ThisRoleType
            is Custom_RoleType.NeutralChaos;
    }
    public static bool IsImpostor(this CustomRoles role) // IsImp
    {
        if (role.GetStaticRoleClass().ThisRoleType is
            Custom_RoleType.ImpostorVanilla or
            Custom_RoleType.ImpostorKilling or
            Custom_RoleType.ImpostorSupport or
            Custom_RoleType.ImpostorConcealing or
            Custom_RoleType.ImpostorHindering or
            Custom_RoleType.ImpostorGhosts) return true;

        return role is
            CustomRoles.Impostor or
            CustomRoles.Shapeshifter or
            CustomRoles.Phantom or CustomRoles.Viper;
    }

    public static bool IsAbleToBeSidekicked(this CustomRoles role)
        => role.GetDYRole() == RoleTypes.Impostor && !role.IsImpostor() && !role.IsRecruitingRole();

    public static bool IsRecruitingRole(this CustomRoles role)
        => role is
            CustomRoles.Jackal or
            CustomRoles.Cultist or
            CustomRoles.Necromancer or
            CustomRoles.Virus or
            CustomRoles.Spiritcaller;

    public static bool IsMadmate(this CustomRoles role)
    {
        return role.GetStaticRoleClass().ThisRoleType is Custom_RoleType.Madmate;
    }
    /// <summary>
    /// Role Changes the Crewmates Team, Including changing to Impostor.
    /// </summary>
    public static bool IsConverted(this CustomRoles role) => (role is CustomRoles.Egoist && Egoist.EgoistCountAsConverted.GetBool())
        || role is
            CustomRoles.Charmed or
            CustomRoles.Recruit or
            CustomRoles.Infected or
            CustomRoles.Contagious or
            CustomRoles.Soulless or
            CustomRoles.Madmate;

    public static bool IsNotKnightable(this CustomRoles role)
    {
        return role is
            CustomRoles.Mayor or
            CustomRoles.Vindicator or
            CustomRoles.Dictator or
            CustomRoles.Knighted or
            CustomRoles.Glitch or
            CustomRoles.Pickpocket or
            CustomRoles.Stubborn or
            CustomRoles.Stealer;
    }
    public static bool IsSpeedRole(this CustomRoles role)
    {
        return role is
            CustomRoles.Flash or
            CustomRoles.Spurt or
            CustomRoles.Statue or
            CustomRoles.Alchemist or
            CustomRoles.Tired or
            CustomRoles.Sloth;
    }
    public static bool IsRevealingRole(this CustomRoles role, PlayerControl target)
    {
        return (role is CustomRoles.Mayor && Mayor.VisibleToEveryone(target))
            || (role is CustomRoles.SuperStar && SuperStar.VisibleToEveryone(target))
            || (role is CustomRoles.Marshall && target.AllTasksCompleted())
            || (role is CustomRoles.Workaholic && Workaholic.WorkaholicVisibleToEveryone.GetBool())
            || (role is CustomRoles.Doctor && Doctor.VisibleToEveryone(target))
            || (role is CustomRoles.Bait && Bait.BaitNotification.GetBool() && Inspector.CheckBaitCountType)
            || (role is CustomRoles.President && President.CheckReveal(target.PlayerId))
            || (role is CustomRoles.Captain && Captain.CrewCanFindCaptain());
    }
    public static bool IsBetrayalAddon(this CustomRoles role)
    {
        return role is CustomRoles.Madmate
            or CustomRoles.Egoist
            or CustomRoles.Charmed
            or CustomRoles.Recruit
            or CustomRoles.Infected
            or CustomRoles.Contagious
            or CustomRoles.Rascal
            or CustomRoles.Soulless;
    }

    public static bool IsImpOnlyAddon(this CustomRoles role)
    {
        return role is CustomRoles.Mare or
            CustomRoles.LastImpostor or
            CustomRoles.Tricky or
            CustomRoles.Mare or
            CustomRoles.Clumsy or
            CustomRoles.Mimic or
            CustomRoles.Stealer or
            CustomRoles.Circumvent or
            CustomRoles.Swift;
    }
    // Retain the existing runtime API for Merchant and other gameplay grants.
    public static bool CheckAddonConfilct(CustomRoles role, PlayerControl pc, bool checkLimitAddons = true)
        => TOHE.Roles.Core.AssignManager.AddonEligibility.CanAssign(role, pc, checkLimitAddons);

    public static RoleTypes GetRoleTypes(this CustomRoles role)
        => GetVNRole(role) switch
        {
            CustomRoles.Crewmate => RoleTypes.Crewmate,
            CustomRoles.Impostor => RoleTypes.Impostor,
            CustomRoles.Scientist => RoleTypes.Scientist,
            CustomRoles.Engineer => RoleTypes.Engineer,
            CustomRoles.GuardianAngel => RoleTypes.GuardianAngel,
            CustomRoles.Shapeshifter => RoleTypes.Shapeshifter,
            CustomRoles.Noisemaker => RoleTypes.Noisemaker,
            CustomRoles.Phantom => RoleTypes.Phantom,
            CustomRoles.Tracker => RoleTypes.Tracker,
            CustomRoles.DetectiveVanilla => RoleTypes.Detective,
            CustomRoles.Viper => RoleTypes.Viper,
            _ => role.IsImpostor() ? RoleTypes.Impostor : RoleTypes.Crewmate,
        };
    public static bool IsDesyncRole(this CustomRoles role) => role.GetDYRole() != RoleTypes.GuardianAngel;
    /// <summary>
    /// Role is Madmate Or Impostor
    /// </summary>
    public static bool IsImpostorTeam(this CustomRoles role) => role.IsImpostor() || role == CustomRoles.Madmate;
    /// <summary>
    /// Role Is Not Impostor nor Madmate Nor Neutral.
    /// </summary>
    public static bool IsCrewmate(this CustomRoles role) => !role.IsImpostor() && !role.IsNeutral() && !role.IsMadmate();
    /// <summary>
    /// Role is Rascal or Madmate and not trickster.
    /// </summary>
    public static bool IsImpostorTeamV2(this CustomRoles role) => role == CustomRoles.Rascal || role == CustomRoles.Madmate || (role.IsImpostorTeamV3() && role != CustomRoles.Trickster && (!role.IsConverted() || role is CustomRoles.Madmate));
    /// <summary>
    /// Role Is Converting or neutral.
    /// </summary>
    public static bool IsNeutralTeamV2(this CustomRoles role) => (role.IsConverted() && role != CustomRoles.Madmate || role.IsNeutral()) && role != CustomRoles.Madmate;
    /// <summary>
    /// Role is not impostor nor rascal nor madmate nor converting nor neutral or role is trickster.
    /// </summary>
    public static bool IsCrewmateTeamV2(this CustomRoles role) => !(role.IsImpostorTeamV2() || role.IsNeutralTeamV2()) || role == CustomRoles.Trickster;
    public static bool IsImpostorTeamV3(this CustomRoles role) => role.IsImpostor() || role.IsMadmate();
    public static bool IsNeutralKillerTeam(this CustomRoles role) => role.IsNK() && !role.IsMadmate();
    public static bool IsPassiveNeutralTeam(this CustomRoles role) => role.IsNonNK() && !role.IsMadmate();
    public static bool IsNNK(this CustomRoles role) => role.IsNeutral() && !role.IsNK();
    public static bool IsVanilla(this CustomRoles role)
    {
        return role is
            CustomRoles.Crewmate or
            CustomRoles.Impostor or
            CustomRoles.Scientist or
            CustomRoles.Engineer or
            CustomRoles.GuardianAngel or
            CustomRoles.Shapeshifter or
            CustomRoles.Noisemaker or
            CustomRoles.Phantom or
            CustomRoles.Tracker or CustomRoles.DetectiveVanilla or CustomRoles.Viper;
    }
    public static Custom_Team GetCustomRoleTeam(this CustomRoles role)
    {
        Custom_Team team = Custom_Team.Crewmate;
        if (role.IsImpostor()) team = Custom_Team.Impostor;
        if (role.IsNeutral()) team = Custom_Team.Neutral;
        if (role.IsAdditionRole()) team = Custom_Team.Addon;
        return team;
    }
    public static Custom_RoleType GetCustomRoleType(this CustomRoles role)
    {
        return role.GetStaticRoleClass().ThisRoleType;
    }
    public static bool RoleExist(this CustomRoles role, bool countDead = false) => Main.AllPlayerControls.Any(x => x.Is(role) && (x.IsAlive() || countDead));
    public static int GetCount(this CustomRoles role)
    {
        if (role.IsVanilla())
        {
            if (Options.DisableVanillaRoles.GetBool()) return 0;
            var roleOpt = Main.NormalOptions.RoleOptions;
            return role switch
            {
                CustomRoles.Crewmate => roleOpt.GetNumPerGame(RoleTypes.Crewmate),
                CustomRoles.Scientist => roleOpt.GetNumPerGame(RoleTypes.Scientist),
                CustomRoles.Engineer => roleOpt.GetNumPerGame(RoleTypes.Engineer),
                CustomRoles.GuardianAngel => roleOpt.GetNumPerGame(RoleTypes.GuardianAngel),
                CustomRoles.Shapeshifter => roleOpt.GetNumPerGame(RoleTypes.Shapeshifter),
                CustomRoles.Noisemaker => roleOpt.GetNumPerGame(RoleTypes.Noisemaker),
                CustomRoles.Phantom => roleOpt.GetNumPerGame(RoleTypes.Phantom),
                CustomRoles.Tracker => roleOpt.GetNumPerGame(RoleTypes.Tracker),
                CustomRoles.DetectiveVanilla => roleOpt.GetNumPerGame(RoleTypes.Detective),
                CustomRoles.Viper => roleOpt.GetNumPerGame(RoleTypes.Viper),
                _ => 0
            };
        }
        else
        {
            return Options.GetRoleCount(role);
        }
    }
    public static int GetMode(this CustomRoles role) => Options.GetRoleSpawnMode(role);
    public static float GetChance(this CustomRoles role)
    {
        if (role.IsVanilla())
        {
            var roleOpt = Main.NormalOptions.RoleOptions;
            return role switch
            {
                CustomRoles.Crewmate => roleOpt.GetChancePerGame(RoleTypes.Crewmate),
                CustomRoles.Scientist => roleOpt.GetChancePerGame(RoleTypes.Scientist),
                CustomRoles.Engineer => roleOpt.GetChancePerGame(RoleTypes.Engineer),
                CustomRoles.GuardianAngel => roleOpt.GetChancePerGame(RoleTypes.GuardianAngel),
                CustomRoles.Shapeshifter => roleOpt.GetChancePerGame(RoleTypes.Shapeshifter),
                CustomRoles.Noisemaker => roleOpt.GetChancePerGame(RoleTypes.Noisemaker),
                CustomRoles.Phantom => roleOpt.GetChancePerGame(RoleTypes.Phantom),
                CustomRoles.Tracker => roleOpt.GetChancePerGame(RoleTypes.Tracker),
                CustomRoles.DetectiveVanilla => roleOpt.GetChancePerGame(RoleTypes.Detective),
                CustomRoles.Viper => roleOpt.GetChancePerGame(RoleTypes.Viper),
                _ => 0
            } / 100f;
        }
        else
        {
            return Options.GetRoleChance(role);
        }
    }
    public static bool IsEnable(this CustomRoles role) => role.GetCount() > 0;
    public static CountTypes GetCountTypes(this CustomRoles role)
       => role switch
       {
           CustomRoles.GM => CountTypes.OutOfGame,
           CustomRoles.Jackal => CountTypes.Jackal,
           CustomRoles.Sidekick => CountTypes.Jackal,
           CustomRoles.Doppelganger => CountTypes.Doppelganger,
           CustomRoles.Bandit => CountTypes.Bandit,
           CustomRoles.Poisoner => CountTypes.Poisoner,
           CustomRoles.Pelican => CountTypes.Pelican,
           CustomRoles.Minion => CountTypes.Impostor,
           CustomRoles.Bloodmoon => CountTypes.Impostor,
           CustomRoles.Possessor => CountTypes.Impostor,
           CustomRoles.Demon => CountTypes.Demon,
           CustomRoles.BloodKnight => CountTypes.BloodKnight,
           CustomRoles.Cultist => CountTypes.Cultist,
           CustomRoles.HexMaster => CountTypes.HexMaster,
           CustomRoles.Necromancer => CountTypes.Necromancer,
           CustomRoles.Stalker => Stalker.SnatchesWins ? CountTypes.Crew : CountTypes.Stalker,
           CustomRoles.Arsonist => Arsonist.CanIgniteAnytime() ? CountTypes.Arsonist : CountTypes.Crew,
           CustomRoles.Shroud => CountTypes.Shroud,
           CustomRoles.Werewolf => CountTypes.Werewolf,
           CustomRoles.Wraith => CountTypes.Wraith,
           var r when r.IsNA() => CountTypes.Apocalypse,
           CustomRoles.Agitater => CountTypes.Agitater,
           CustomRoles.Parasite => CountTypes.Impostor,
           CustomRoles.SerialKiller => CountTypes.SerialKiller,
           CustomRoles.Quizmaster => CountTypes.Quizmaster,
           CustomRoles.Juggernaut => CountTypes.Juggernaut,
           CustomRoles.Jinx => CountTypes.Jinx,
           CustomRoles.Infectious or CustomRoles.Infected => CountTypes.Infectious,
           CustomRoles.Crewpostor => CountTypes.Impostor,
           CustomRoles.Pyromaniac => CountTypes.Pyromaniac,
           CustomRoles.PlagueDoctor => CountTypes.PlagueDoctor,
           CustomRoles.Virus => CountTypes.Virus,
           CustomRoles.PotionMaster => CountTypes.PotionMaster,
           CustomRoles.Pickpocket => CountTypes.Pickpocket,
           CustomRoles.Traitor => CountTypes.Traitor,
           CustomRoles.Medusa => CountTypes.Medusa,
           CustomRoles.Refugee => CountTypes.Impostor,
           CustomRoles.Huntsman => CountTypes.Huntsman,
           CustomRoles.Glitch => CountTypes.Glitch,
           CustomRoles.Spiritcaller => CountTypes.Spiritcaller,
           CustomRoles.RuthlessRomantic => CountTypes.RuthlessRomantic,
           CustomRoles.SchrodingersCat => CountTypes.None,
           CustomRoles.Solsticer => CountTypes.None,
           _ => role.IsImpostorTeam() ? CountTypes.Impostor : CountTypes.Crew,

           // CustomRoles.Phantom => CountTypes.OutOfGame,
           //   CustomRoles.CursedSoul => CountTypes.OutOfGame, // if they count as OutOfGame, it prevents them from winning lmao
       };
    public static CustomWinner GetNeutralCustomWinnerFromRole(this CustomRoles role) // only to be used for Neutrals
        => role switch
        {
            CustomRoles.Jester => CustomWinner.Jester,
            CustomRoles.Terrorist => CustomWinner.Terrorist,
            CustomRoles.Lovers => CustomWinner.Lovers,
            CustomRoles.Executioner => CustomWinner.Executioner,
            CustomRoles.Arsonist => CustomWinner.Arsonist,
            CustomRoles.Pyromaniac => CustomWinner.Pyromaniac,
            CustomRoles.Agitater => CustomWinner.Agitater,
            CustomRoles.Revolutionist => CustomWinner.Revolutionist,
            CustomRoles.Jackal => CustomWinner.Jackal,
            CustomRoles.Sidekick => CustomWinner.Sidekick,
            CustomRoles.God => CustomWinner.God,
            CustomRoles.Vector => CustomWinner.Vector,
            CustomRoles.Innocent => CustomWinner.Innocent,
            CustomRoles.Pelican => CustomWinner.Pelican,
            CustomRoles.Youtuber => CustomWinner.Youtuber,
            CustomRoles.Egoist => CustomWinner.Egoist,
            CustomRoles.Demon => CustomWinner.Demon,
            CustomRoles.Stalker => CustomWinner.Stalker,
            CustomRoles.Workaholic => CustomWinner.Workaholic,
            CustomRoles.Solsticer => CustomWinner.Solsticer,
            CustomRoles.Collector => CustomWinner.Collector,
            CustomRoles.BloodKnight => CustomWinner.BloodKnight,
            CustomRoles.Poisoner => CustomWinner.Poisoner,
            CustomRoles.HexMaster => CustomWinner.HexMaster,
            CustomRoles.Cultist => CustomWinner.Cultist,
            CustomRoles.Wraith => CustomWinner.Wraith,
            CustomRoles.Bandit => CustomWinner.Bandit,
            CustomRoles.Pirate => CustomWinner.Pirate,
            CustomRoles.SerialKiller => CustomWinner.SerialKiller,
            CustomRoles.Quizmaster => CustomWinner.Quizmaster,
            CustomRoles.Werewolf => CustomWinner.Werewolf,
            CustomRoles.Necromancer => CustomWinner.Necromancer,
            CustomRoles.Huntsman => CustomWinner.Huntsman,
            CustomRoles.Juggernaut => CustomWinner.Juggernaut,
            CustomRoles.Infectious => CustomWinner.Infectious,
            CustomRoles.Virus => CustomWinner.Virus,
            CustomRoles.Specter => CustomWinner.Specter,
            CustomRoles.Jinx => CustomWinner.Jinx,
            CustomRoles.CursedSoul => CustomWinner.CursedSoul,
            CustomRoles.PotionMaster => CustomWinner.PotionMaster,
            CustomRoles.Pickpocket => CustomWinner.Pickpocket,
            CustomRoles.Traitor => CustomWinner.Traitor,
            CustomRoles.Vulture => CustomWinner.Vulture,
            CustomRoles.Apocalypse => CustomWinner.Apocalypse,
            CustomRoles.Medusa => CustomWinner.Medusa,
            CustomRoles.Spiritcaller => CustomWinner.Spiritcaller,
            CustomRoles.Glitch => CustomWinner.Glitch,
            CustomRoles.PunchingBag => CustomWinner.PunchingBag,
            CustomRoles.Doomsayer => CustomWinner.Doomsayer,
            CustomRoles.Shroud => CustomWinner.Shroud,
            CustomRoles.Seeker => CustomWinner.Seeker,
            CustomRoles.SoulCollector => CustomWinner.SoulCollector,
            CustomRoles.RuthlessRomantic => CustomWinner.RuthlessRomantic,
            CustomRoles.Mini => CustomWinner.NiceMini,
            CustomRoles.Doppelganger => CustomWinner.Doppelganger,
            _ => throw new NotImplementedException()

        };
    public static CustomRoles GetNeutralCustomRoleFromCountType(this CountTypes type) //only to be used for NKs
        => type switch
        {
            CountTypes.OutOfGame => CustomRoles.GM,
            CountTypes.Jackal => CustomRoles.Jackal,
            CountTypes.Doppelganger => CustomRoles.Doppelganger,
            CountTypes.Bandit => CustomRoles.Bandit,
            CountTypes.Poisoner => CustomRoles.Poisoner,
            CountTypes.Pelican => CustomRoles.Pelican,
            CountTypes.Demon => CustomRoles.Demon,
            CountTypes.BloodKnight => CustomRoles.BloodKnight,
            CountTypes.Cultist => CustomRoles.Cultist,
            CountTypes.HexMaster => CustomRoles.HexMaster,
            CountTypes.Necromancer => CustomRoles.Necromancer,
            CountTypes.Shroud => CustomRoles.Shroud,
            CountTypes.Werewolf => CustomRoles.Werewolf,
            CountTypes.Wraith => CustomRoles.Wraith,
            CountTypes.Apocalypse => CustomRoles.Apocalypse,
            CountTypes.Agitater => CustomRoles.Agitater,
            CountTypes.SerialKiller => CustomRoles.SerialKiller,
            CountTypes.Quizmaster => CustomRoles.Quizmaster,
            CountTypes.Juggernaut => CustomRoles.Juggernaut,
            CountTypes.Jinx => CustomRoles.Jinx,
            CountTypes.Infectious => CustomRoles.Infectious,
            CountTypes.Pyromaniac => CustomRoles.Pyromaniac,
            CountTypes.Virus => CustomRoles.Virus,
            CountTypes.PotionMaster => CustomRoles.PotionMaster,
            CountTypes.Pickpocket => CustomRoles.Pickpocket,
            CountTypes.Traitor => CustomRoles.Traitor,
            CountTypes.Medusa => CustomRoles.Medusa,
            CountTypes.Huntsman => CustomRoles.Huntsman,
            CountTypes.Glitch => CustomRoles.Glitch,
            CountTypes.Stalker => CustomRoles.Stalker,
            CountTypes.Spiritcaller => CustomRoles.Spiritcaller,
            CountTypes.Arsonist => CustomRoles.Arsonist,
            CountTypes.RuthlessRomantic => CustomRoles.RuthlessRomantic,
            _ => throw new NotImplementedException()
        };
    public static bool HasSubRole(this PlayerControl pc) => Main.PlayerStates[pc.PlayerId].SubRoles.Any();
}
public enum Custom_Team
{
    Crewmate,
    Impostor,
    Neutral,
    Addon,
}
public enum Custom_RoleType
{
    // Impostors
    ImpostorVanilla,
    ImpostorKilling,
    ImpostorSupport,
    ImpostorConcealing,
    ImpostorHindering,
    ImpostorGhosts,

    Madmate,

    // Crewmate
    CrewmateVanilla,
    CrewmateVanillaGhosts,
    CrewmateBasic,
    CrewmateSupport,
    CrewmateKilling,
    CrewmatePower,
    CrewmateGhosts,

    // Neutral
    NeutralBenign,
    NeutralEvil,
    NeutralChaos,
    NeutralKilling,
    NeutralApocalypse,

    None
}
public enum CountTypes
{
    OutOfGame,
    None,
    Crew,
    Impostor,
    Jackal,
    Doppelganger,
    Bandit,
    Pelican,
    PlagueDoctor,
    Demon,
    BloodKnight,
    Poisoner,
    Charmed,
    Cultist,
    HexMaster,
    Wraith,
    SerialKiller,
    Juggernaut,
    Infectious,
    Virus,
    Stalker,
    Jinx,
    PotionMaster,
    Pickpocket,
    Traitor,
    Medusa,
    Spiritcaller,
    Quizmaster,
    Apocalypse,
    Glitch,
    Arsonist,
    Huntsman,
    Pyromaniac,
    Shroud,
    Werewolf,
    Agitater,
    RuthlessRomantic,
    Necromancer
}
