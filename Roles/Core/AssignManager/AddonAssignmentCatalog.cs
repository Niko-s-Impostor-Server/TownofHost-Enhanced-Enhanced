using System;
using TOHE.Roles.AddOns.Impostor;

namespace TOHE.Roles.Core.AssignManager;

internal static class AddonAssignmentCatalog
{
    internal static bool AvailableAtStart(CustomRoles role) => role.IsAdditionRole() && !role.IsGhostRole() && role switch
    {
        CustomRoles.Lovers or CustomRoles.Workhorse or CustomRoles.LastImpostor => false,
        CustomRoles.Autopsy when Options.EveryoneCanSeeDeathReason.GetBool() => false,
        CustomRoles.Madmate when Madmate.MadmateSpawnMode.GetInt() != 0 => false,
        CustomRoles.Glow or CustomRoles.Mare when GameStates.FungleIsActive => false,
        _ => true
    };

    internal static AddonCandidate[] Capture()
    {
        var result = new List<AddonCandidate>();
        if (CustomRoles.Lovers.IsEnable())
            result.Add(new(CustomRoles.Lovers, CustomRoles.Hater.IsEnable() ? 100 : Options.LoverSpawnChances.GetInt(),
                2, AddonAssignmentStage.Lovers, MinimumCount: 2));
        foreach (var (role, chance) in Options.CustomAdtRoleSpawnRate.OrderBy(pair => pair.Key))
        {
            if (!AvailableAtStart(role) || !role.IsEnable()) continue;
            var stage = role switch
            {
                CustomRoles.Madmate or CustomRoles.Egoist => AddonAssignmentStage.Alignment,
                CustomRoles.Guesser => AddonAssignmentStage.Guesser,
                _ => AddonAssignmentStage.Ordinary
            };
            result.Add(new(role, Math.Clamp((int)chance.GetFloat(), 0, 100), role.GetCount(), stage));
        }
        return result.ToArray();
    }
}
