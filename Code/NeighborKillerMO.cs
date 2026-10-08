using HarmonyLib;

namespace ShadowsOfDoubtExpanded;

// ─────────────────────────────────────────────────────────────────────────────
// THE NEIGHBOR — Harmony patches
//
// Contains only the runtime behavior patches for this killer type.
// MO registration, evidence, and geometry helpers live in:
//   KillerFramework.cs          (DDSBuilder, LeadBuilder, base class)
//   NeighborKillerDefinition.cs (ConfigureMO, RegisterLeads, IsNeighbor)
// ─────────────────────────────────────────────────────────────────────────────

// ── Victim selection override ─────────────────────────────────────────────────
// Replaces the standard PickNewVictim with our neighbor-constrained version
// when The Neighbor MO is active. Returns false to skip the original method.

[HarmonyPatch(typeof(MurderController), nameof(MurderController.PickNewVictim))]
public static class PickNewVictim_NeighborPatch
{
    [HarmonyPrefix]
    public static bool Prefix(MurderController __instance)
    {
        if (__instance.chosenMO?.name != NeighborKillerDefinition.MO_NAME)
            return true; // not our MO — let the original run

        float bestScore = float.MinValue;
        __instance.currentVictim     = null;
        __instance.currentVictimSite = null;

        var mo = __instance.chosenMO;

        foreach (Citizen candidate in CityData.Instance.citizenDirectory)
        {
            if (candidate.isPlayer
                || candidate.isDead
                || candidate.isHomeless
                || candidate == __instance.currentMurderer
                || __instance.previousMurderers.Contains(candidate))
                continue;

            // Core constraint: same building, different apartment.
            // IL2CPP reference equality is unreliable on address/building objects —
            // we compare by .id / .buildingID instead. See NeighborKillerDefinition.IsNeighbor.
            if (!NeighborKillerDefinition.IsNeighbor(__instance.currentMurderer, candidate))
                continue;

            float traitBonus = 0f;
            var victimTraitModifiers = mo.victimTraitModifiers;
            if (!MurderController.Instance.TraitTest(candidate, ref victimTraitModifiers, out traitBonus))
                continue;
            mo.victimTraitModifiers = victimTraitModifiers;

            float score = Toolbox.Instance.Rand(mo.victimRandomScoreRange.x, mo.victimRandomScoreRange.y);
            score += traitBonus;

            // Same-floor preference: soft bonus, not a hard filter.
            // If nobody on the killer's floor qualifies, anyone else in the
            // building is still a valid pick — just ranked lower.
            if (NeighborKillerDefinition.IsSameFloor(__instance.currentMurderer, candidate))
                score += NeighborKillerDefinition.SAME_FLOOR_BONUS;

            // MO relationship modifiers.
            Acquaintance existingAcq = null;
            if (__instance.currentMurderer.FindAcquaintanceExists(candidate, out existingAcq))
            {
                score += mo.acquaintedSuitabilityBoost;
                score += mo.likeSuitabilityBoost * existingAcq.like;
            }

            if (__instance.currentMurderer.attractedTo.Contains(candidate.gender))
                score += mo.attractedToSuitabilityBoost;

            if (score > bestScore)
            {
                bestScore = score;
                __instance.currentVictim = candidate;
            }
        }

        if (__instance.currentVictim == null)
        {
            // No valid neighbors found — fall back so the game doesn't softlock.
            SoDExpandedPlugin.Logger.LogWarning(
                "[NeighborKiller] No valid neighbor victim found — falling back to default selection.");
            return true;
        }

        SoDExpandedPlugin.Logger.LogInfo(
            $"[NeighborKiller] Victim selected: {__instance.currentVictim.GetCitizenName()}.");

        return false; // skip the original PickNewVictim
    }
}