using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime;

namespace ShadowsOfDoubtExpanded;

// ─────────────────────────────────────────────────────────────────────────────
// THE ANGEL — Harmony patches
//
// Contains only the runtime behavior patches for this killer type.
// MO registration and evidence templates live in FanaticKillerDefinition.cs.
// Self-report + enforcer dispatch lives in FanaticSelfReport.cs.
//
// ── Journal dynamic text ────────────────────────────────────────────────────
// FanaticKillerDefinition registers the victim's journal with safe, generic
// fallback text via WithPhysicalText() (JobTag.S), since ConfigureMO/
// RegisterLeads run once at MO registration — before any specific murder (and
// therefore before we know which citizen is the killer, or where he lives).
//
// This patch runs on the SAME SpawnItem call as SpawnItem_DDSOverrideFix_Patch
// (KillerFramework.cs), but AFTER it (HarmonyPriority.Low), specifically for
// our journal's itemTag. By the time SpawnItem fires, murder.murderer is
// resolved, so we can build the REAL text — the organic version that
// references the killer's actual home building — and overwrite the generic
// fallback SpawnItem_DDSOverrideFix_Patch already applied.
//
// UNVERIFIED: using NewBuilding.name (inherited from UnityEngine.Object) as
// the display name. There's also playerEditedBuildingName/isPlayerEditedName
// on NewBuilding, which looks like a player-renamed override — checking that
// first and falling back to .name if it's not set. Worth eyeballing in-game
// the first time this fires: if the text reads a raw/ugly internal name
// instead of the game's normal in-fiction building name, this is the first
// place to look.
// ─────────────────────────────────────────────────────────────────────────────

[HarmonyPatch(typeof(MurderController), nameof(MurderController.SpawnItem))]
public static class FanaticJournal_DDSOverride_Patch
{
    [HarmonyPriority(Priority.Low)] // run after the generic fallback patch, so ours wins
    [HarmonyPostfix]
    public static void Postfix(Interactable __result, MurderController.Murder murder, JobPreset.JobTag itemTag)
    {
        if (__result == null || murder == null) return;
        if (itemTag != JobPreset.JobTag.S) return;
        if (murder.moStr != FanaticKillerDefinition.MO_NAME) return;

        Human killer = murder.murderer;
        if (killer?.home?.building == null)
        {
            SoDExpandedPlugin.Logger.LogWarning(
                $"[{FanaticKillerDefinition.MO_NAME}] Journal spawn: killer/home/building was " +
                "null — leaving the generic fallback text in place.");
            return;
        }

        NewBuilding building = killer.home.building;
        string buildingName = (building.isPlayerEditedName && !string.IsNullOrEmpty(building.playerEditedBuildingName))
            ? building.playerEditedBuildingName
            : building.name;

        if (string.IsNullOrEmpty(buildingName))
        {
            SoDExpandedPlugin.Logger.LogWarning(
                $"[{FanaticKillerDefinition.MO_NAME}] Journal spawn: resolved building name was " +
                "empty — leaving the generic fallback text in place.");
            return;
        }

        // Built fresh per-murder — DDSBuilder.Document gives each call a unique
        // toolbox.GenerateUniqueID(), so no collision across multiple Fanatic
        // murders in the same save.
        //
        // FIX: this used to hardcode "That man" / "he" throughout, which
        // would've read wrong for a female killer (confirmed possible — Ayaka
        // Nakagawa, Sex-Female, was the actual killer in a real test run).
        // Now computed once and used consistently everywhere in the text.
        string genderedNoun = HasTrait(killer, "Sex-Male") ? "man"
                            : HasTrait(killer, "Sex-Female") ? "woman"
                            : "person";
        string pronoun = HasTrait(killer, "Sex-Male") ? "he"
                        : HasTrait(killer, "Sex-Female") ? "she"
                        : "they";
        string appearanceClause = BuildAppearanceClause(killer, pronoun);

        string journalTextID = DDSBuilder.Document(Toolbox.Instance)
            .Named($"SoDExpanded_FanaticKiller_Journal_{murder.murderID}")
            .Message(0, 1,
                $"That {genderedNoun} stopped me again today, right outside {buildingName}. " +
                $"Started going on about sin and judgement, like {pronoun} already knew " +
                $"things about me {pronoun} shouldn't.{appearanceClause} I don't know why " +
                $"{pronoun}'s always right there. I've started taking the long way around.",
                $"FanaticKiller_Journal_{murder.murderID}_Body",
                handwriting: true)
            .Build();

        __result.SetDDSOverride(journalTextID);

        SoDExpandedPlugin.Logger.LogInfo(
            $"[{FanaticKillerDefinition.MO_NAME}] Journal for murder {murder.murderID} references " +
            $"'{buildingName}'.");
    }

    /// <summary>
    /// Builds a short, deliberately vague physical impression from the
    /// killer's REAL traits — same shape as the vanilla Stalker killer's own
    /// diary clue ("skinny, average height, short hair, possibly male"), but
    /// there's no dedicated "GetPhysicalDescription" method anywhere in the
    /// game's code, so Stalker's version is almost certainly hand-authored
    /// flavor text, not derived from real citizen data the way this is.
    ///
    /// Trait names used here (Sex-Male/Sex-Female, Quirk-LongHair,
    /// Quirk-ShoesHeels, Quirk-StatusWealthy) are CONFIRMED — they showed up
    /// in an actual live log dump of a real killer/victim's trait lists.
    /// Deliberately only including a few distinguishing quirks, not every
    /// trait — a baseline like Quirk-ShoesNormal isn't a distinguishing
    /// detail a victim would think to mention, so it's skipped on purpose.
    /// Returns an empty string (not a placeholder) if nothing distinguishing
    /// matched, so the sentence just doesn't gain a clause rather than reading
    /// awkwardly empty.
    /// </summary>
    private static string BuildAppearanceClause(Human killer, string pronoun)
    {
        var details = new List<string>();
        if (HasTrait(killer, "Quirk-LongHair"))      details.Add("long hair");
        if (HasTrait(killer, "Quirk-ShoesHeels"))    details.Add("heels, of all things");
        if (HasTrait(killer, "Quirk-StatusWealthy")) details.Add("dressed better than anyone round here");

        if (details.Count == 0) return "";

        return $" I only really remember the {string.Join(" and ", details)} — " +
               $"{pronoun} didn't stick around long enough for more than that.";
    }

    private static bool HasTrait(Human human, string traitName)
    {
        var traitCache = Toolbox.Instance.resourcesCache[Il2CppType.Of<CharacterTrait>()];
        if (!traitCache.ContainsKey(traitName)) return false;
        var trait = traitCache[traitName].TryCast<CharacterTrait>();
        return trait != null && human.TraitExists(trait);
    }
}