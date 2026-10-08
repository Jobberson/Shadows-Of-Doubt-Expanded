using HarmonyLib;

namespace ShadowsOfDoubtExpanded;

// ─────────────────────────────────────────────────────────────────────────────
// FANATIC SELF-REPORT
//
// SOLVED. Snog pulled the real decompiled source of ActionController.cs and
// it shows exactly what vanilla "someone reports a body" does -- and it's TWO
// separate calls, not one:
//
//   GameplayController.Instance.CallEnforcers(location, forceCrimeScene: false,
//                                              immediateTeleport: false, delay);
//   human.death.SetReported(who as Human, reportType);
//
// SetReported is just bookkeeping (who found it, how). The ACTUAL dispatch
// trigger — the thing that sends 1-3 Enforcers to the scene — is
// GameplayController.Instance.CallEnforcers(NewGameLocation, bool, bool, float).
// We were only ever calling SetReported, which is exactly why the first test
// did nothing: no error, because SetReported genuinely works, it's just not
// the dispatch trigger.
//
// This version calls both, in the same order vanilla does, using the corpse's
// own location via Death.GetDeathLocation() (also confirmed in the real source).
// ─────────────────────────────────────────────────────────────────────────────

[HarmonyPatch(typeof(MurderController.Murder), nameof(MurderController.Murder.SetMurderState))]
public static class FanaticSelfReport_Patch
{
    [HarmonyPostfix]
    public static void Postfix(MurderController.Murder __instance, MurderController.MurderState newState)
    {
        if (__instance == null) return;
        if (__instance.moStr != FanaticKillerDefinition.MO_NAME) return;
        if (newState != MurderController.MurderState.post) return;

        Human.Death death = __instance.death;
        if (death == null)
        {
            SoDExpandedPlugin.Logger.LogWarning(
                $"[{FanaticKillerDefinition.MO_NAME}] SetMurderState(post) fired but " +
                "Murder.death was null -- can't self-report.");
            return;
        }

        Human killer = __instance.murderer;
        if (killer == null)
        {
            SoDExpandedPlugin.Logger.LogWarning(
                $"[{FanaticKillerDefinition.MO_NAME}] SetMurderState(post) fired but " +
                "Murder.murderer was null -- can't self-report.");
            return;
        }

        if (death.reported)
        {
            SoDExpandedPlugin.Logger.LogInfo(
                $"[{FanaticKillerDefinition.MO_NAME}] Murder {__instance.murderID} already reported, skipping.");
            return;
        }

        var location = death.GetDeathLocation();

        // Real dispatch trigger, confirmed from vanilla ActionController.CallEnforcers.
        GameplayController.Instance.CallEnforcers(location, forceCrimeScene: false, immediateTeleport: false, 0f);

        // Bookkeeping -- who "found" it and how. Matches vanilla's own call order.
        death.SetReported(killer, Human.Death.ReportType.visual);

        SoDExpandedPlugin.Logger.LogInfo(
            $"[{FanaticKillerDefinition.MO_NAME}] Called GameplayController.CallEnforcers + " +
            $"Death.SetReported for murder {__instance.murderID}. Watch for: do 1-3 Enforcers " +
            "actually depart City Hall and head to the scene.");
    }
}