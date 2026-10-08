using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime;

namespace ShadowsOfDoubtExpanded;

// ─────────────────────────────────────────────────────────────────────────────
// THE CLOSER — Harmony patches
//
// Contains only the runtime behavior patches for this killer type.
// MO registration and evidence templates live in BrokerKillerDefinition.cs.
// ─────────────────────────────────────────────────────────────────────────────

// ── Murderer selection gate ───────────────────────────────────────────────────
// CONFIRMED against the real PickNewMurderer (MurderController.cs, ~line 1625):
// murderPreset, every compatible MurderMO, and every eligible citizen are all
// scored together in ONE combined pass — the single best (citizen, MO) pair
// becomes currentMurderer/chosenMO simultaneously. There is no point during
// the method where chosenMO is set before currentMurderer, so a prefix that
// only activates "when chosenMO is already ours" (the first version of this
// patch) can never fire — that state doesn't exist yet when the method starts.
//
// murdererJobModifiers / murdererCompanyModifiers (also confirmed from the
// real loop) don't help either: both are pure score BOOSTS applied only when
// job.employer.preset is in a specific named list — never a hard filter, and
// no way to express "any real employer" through them.
//
// So instead of reimplementing the whole scoring pass, this is a bounded
// retry: let vanilla run, and if it lands on The Closer with an unemployed
// murderer, just make it re-roll. Re-entrancy guard is needed because the
// retry calls back into the SAME patched method, which re-triggers this
// postfix — without the guard, each nested call would start its own
// MaxRetries loop (exponential blowup). The guard makes only the outermost
// call responsible for retrying; inner calls triggered by our own retries
// see the guard set and return immediately.
//
// UNVERIFIED: written against the real decompiled method, but not yet run
// in a live session — worth confirming retries actually converge quickly in
// practice rather than routinely hitting MaxRetries (would suggest the
// employed-citizen pool is too small relative to how often The Closer's
// preset/MO combination gets picked).
[HarmonyPatch(typeof(MurderController), nameof(MurderController.PickNewMurderer))]
public static class PickNewMurderer_BrokerPatch
{
    private const int MaxRetries = 15;

    [ThreadStatic]
    private static bool _inRetryLoop;

    [HarmonyPostfix]
    public static void Postfix(MurderController __instance)
    {
        if (_inRetryLoop) return; // nested call from our own retry below — outer call owns the loop

        _inRetryLoop = true;
        try
        {
            int attempts = 0;
            while (IsInvalidBrokerPick(__instance) && attempts < MaxRetries)
            {
                attempts++;
                __instance.PickNewMurderer();
            }

            if (IsInvalidBrokerPick(__instance))
            {
                // Absolute last resort: take The Closer off the table for exactly
                // one more pick, so we never leave an invalid pick in place.
                var brokerMO = FindBrokerMO();
                if (brokerMO != null)
                {
                    bool wasDisabled = brokerMO.disabled;
                    brokerMO.disabled = true;
                    __instance.PickNewMurderer();
                    brokerMO.disabled = wasDisabled;
                }
                SoDExpandedPlugin.Logger.LogWarning(
                    $"[{BrokerKillerDefinition.MO_NAME}] Could not land on an employed murderer after " +
                    $"{MaxRetries} retries — disabled The Closer for one pick to avoid a stuck state. " +
                    "If this warning shows up often, the employed-citizen pool may be too small.");
            }
        }
        finally
        {
            _inRetryLoop = false;
        }
    }

    private static bool IsInvalidBrokerPick(MurderController instance)
    {
        return instance.chosenMO != null
            && instance.chosenMO.name == BrokerKillerDefinition.MO_NAME
            && instance.currentMurderer != null
            && !IsGenuinelyEmployed(instance.currentMurderer);
    }

    private static MurderMO FindBrokerMO()
    {
        var cache = Toolbox.Instance.resourcesCache[Il2CppType.Of<MurderMO>()];
        foreach (var pair in cache)
        {
            var mo = pair.Value.TryCast<MurderMO>();
            if (mo != null && mo.name == BrokerKillerDefinition.MO_NAME) return mo;
        }
        return null;
    }

    // Mirrors CharacterTrait.requiresEmployment's own definition (Human.cs) —
    // "legitimately employed at a real company" as the game itself defines it,
    // rather than a definition we invented.
    private static bool IsGenuinelyEmployed(Human human)
    {
        var job = human.job;
        return job != null
            && job.employer != null
            && !job.preset.selfEmployed
            && job.employer.address != null;
    }
}

// ── Calling card dynamic placement ────────────────────────────────────────────
// The suicide note now runs through the engine's own callingCardPool system
// (registered in BrokerKillerDefinition.RegisterLeads) instead of a regular
// MOlead — PickNewCallingCard() and PlaceCallingCard() are both called
// automatically by MurderController.Murder itself (the former at murder
// creation, the latter during MurderState.post), the same way The Dove's
// lipstick works, so the note now spawns physically right next to the
// victim's body instead of "somewhere in the house."
//
// PlaceCallingCard() creates the item via InteractableCreator directly, NOT
// through MurderController.SpawnItem — so none of the existing SpawnItem-based
// DDS override patches (or the MO-scoping guard on the framework-level one)
// ever see this item at all. This patch applies the note's text separately,
// right after the engine places it.
[HarmonyPatch(typeof(MurderController.Murder), nameof(MurderController.Murder.PlaceCallingCard))]
public static class BrokerCallingCard_DDSOverride_Patch
{
    [HarmonyPostfix]
    public static void Postfix(MurderController.Murder __instance)
    {
        if (__instance == null || __instance.moStr != BrokerKillerDefinition.MO_NAME) return;

        if (__instance.callingCard == null)
        {
            SoDExpandedPlugin.Logger.LogWarning(
                $"[{BrokerKillerDefinition.MO_NAME}] PlaceCallingCard ran but callingCard is null — " +
                "the note was not actually placed for this murder.");
            return;
        }

        __instance.callingCard.SetDDSOverride(BrokerKillerDefinition.SuicideNoteTextID);

        SoDExpandedPlugin.Logger.LogInfo(
            $"[{BrokerKillerDefinition.MO_NAME}] Calling card (forged suicide note) placed for " +
            $"murder {__instance.murderID}, next to the victim's body.");
    }
}

// ── Contract dynamic text ─────────────────────────────────────────────────────
// BrokerKillerDefinition registers the contract with genericized fallback text
// ("the firm") via WithPhysicalText() (JobTag.D), since ConfigureMO/
// RegisterLeads run once at MO registration — before any specific murder.
// This patch runs on the SAME SpawnItem call as SpawnItem_DDSOverrideFix_Patch
// (KillerFramework.cs), but AFTER it (HarmonyPriority.Low), so ours wins —
// exact same pattern as FanaticJournal_DDSOverride_Patch.
[HarmonyPatch(typeof(MurderController), nameof(MurderController.SpawnItem))]
public static class BrokerContract_DDSOverride_Patch
{
    [HarmonyPriority(Priority.Low)]
    [HarmonyPostfix]
    public static void Postfix(Interactable __result, MurderController.Murder murder, JobPreset.JobTag itemTag)
    {
        if (__result == null || murder == null) return;
        if (itemTag != JobPreset.JobTag.D) return;
        if (murder.moStr != BrokerKillerDefinition.MO_NAME) return;

        Human killer = murder.murderer;
        string companyName = killer?.job?.employer?.name;

        if (string.IsNullOrEmpty(companyName))
        {
            SoDExpandedPlugin.Logger.LogWarning(
                $"[{BrokerKillerDefinition.MO_NAME}] Contract spawn: killer/job/employer/name was " +
                "null or empty — leaving the generic fallback text in place.");
            return;
        }

        string contractTextID = DDSBuilder.Document(Toolbox.Instance)
            .Named($"SoDExpanded_BrokerKiller_Contract_{murder.murderID}")
            .Message(0, 1,
                $"This agreement guarantees a return well above anything {companyName} " +
                "would ever offer through normal channels — which is exactly why it stays " +
                "between us. Sign below and consider it handled.",
                $"BrokerKiller_Contract_{murder.murderID}_Body",
                handwriting: false)
            .Build();

        __result.SetDDSOverride(contractTextID);

        SoDExpandedPlugin.Logger.LogInfo(
            $"[{BrokerKillerDefinition.MO_NAME}] Contract for murder {murder.murderID} names '{companyName}'.");
    }
}

// ── Business card dynamic text ────────────────────────────────────────────────
// Confirmed via live debug dump: killer.job.preset.name (== .presetName ==
// .work, all the same underlying OccupationPreset value, e.g. "Enforcer") is
// the real position/title field. Same deferred-until-murder-time reasoning
// as the contract patch — genericized static fallback registered in
// BrokerKillerDefinition.cs, overwritten here with the killer's real name,
// position, and employer once a specific murder actually happens.
[HarmonyPatch(typeof(MurderController), nameof(MurderController.SpawnItem))]
public static class BrokerBusinessCard_DDSOverride_Patch
{
    [HarmonyPriority(Priority.Low)]
    [HarmonyPostfix]
    public static void Postfix(Interactable __result, MurderController.Murder murder, JobPreset.JobTag itemTag)
    {
        if (__result == null || murder == null) return;
        if (itemTag != JobPreset.JobTag.K) return;
        if (murder.moStr != BrokerKillerDefinition.MO_NAME) return;

        Human killer = murder.murderer;
        string companyName = killer?.job?.employer?.name;
        string position = killer?.job?.preset?.name;
        string killerName = killer?.GetCitizenName();

        if (string.IsNullOrEmpty(companyName) || string.IsNullOrEmpty(killerName))
        {
            SoDExpandedPlugin.Logger.LogWarning(
                $"[{BrokerKillerDefinition.MO_NAME}] Business card spawn: killer/company name " +
                "was null or empty — leaving the generic fallback text in place.");
            return;
        }

        string title = string.IsNullOrEmpty(position) ? "Senior Advisor" : position;

        string cardTextID = DDSBuilder.Document(Toolbox.Instance)
            .Named($"SoDExpanded_BrokerKiller_BusinessCard_{murder.murderID}")
            .Message(0, 1,
                $"Your Finantial Solution — {title}, {companyName}. Discreet financial solutions.",
                $"BrokerKiller_BusinessCard_{murder.murderID}_Body",
                handwriting: false)
            .Build();

        __result.SetDDSOverride(cardTextID);

        SoDExpandedPlugin.Logger.LogInfo(
            $"[{BrokerKillerDefinition.MO_NAME}] Business card for murder {murder.murderID} names " +
            $"'{killerName}' — {title}, '{companyName}'.");
    }
}

// ── Ledger dynamic text ───────────────────────────────────────────────────────
// Same deferred-until-murder-time reasoning as the contract patch above, but
// for JobTag.M. Names 2 OTHER real citizens (not the actual victim) who match
// the same wealth/mark trait pool, with made-up dollar amounts — implies an
// ongoing operation without spoiling or confirming the real victim.
[HarmonyPatch(typeof(MurderController), nameof(MurderController.SpawnItem))]
public static class BrokerLedger_DDSOverride_Patch
{
    // Made-up flavor amounts only — not tied to any real in-game economy value.
    private static readonly int[] FlavorAmounts = { 4200, 7850, 12400, 19000, 26500 };

    [HarmonyPriority(Priority.Low)]
    [HarmonyPostfix]
    public static void Postfix(Interactable __result, MurderController.Murder murder, JobPreset.JobTag itemTag)
    {
        if (__result == null || murder == null) return;
        if (itemTag != JobPreset.JobTag.M) return;
        if (murder.moStr != BrokerKillerDefinition.MO_NAME) return;

        var otherMarks = FindOtherMarks(murder, count: 2);
        if (otherMarks.Count == 0)
        {
            SoDExpandedPlugin.Logger.LogWarning(
                $"[{BrokerKillerDefinition.MO_NAME}] Ledger spawn: no other qualifying citizens " +
                "found — leaving the generic fallback text in place.");
            return;
        }

        var lines = new List<string>();
        foreach (var citizen in otherMarks)
        {
            int amount = FlavorAmounts[Toolbox.Instance.Rand(0, FlavorAmounts.Length - 1)];
            lines.Add($"— {citizen.GetCitizenName()}: ${amount} outstanding. handling it quietly.");
        }
        string ledgerBody = string.Join("\n", lines);

        string ledgerTextID = DDSBuilder.Document(Toolbox.Instance)
            .Named($"SoDExpanded_BrokerKiller_Ledger_{murder.murderID}")
            .Message(0, 1, ledgerBody, $"BrokerKiller_Ledger_{murder.murderID}_Body", handwriting: true)
            .Build();

        __result.SetDDSOverride(ledgerTextID);

        SoDExpandedPlugin.Logger.LogInfo(
            $"[{BrokerKillerDefinition.MO_NAME}] Ledger for murder {murder.murderID} names " +
            $"{otherMarks.Count} other citizen(s).");
    }

    // Picks up to `count` real citizens matching Broker's own victim trait
    // pool (wealth OR mark traits), excluding the actual victim of this
    // murder and the killer themselves. Flavor only — these citizens are
    // NOT flagged as future victims by this, just named for atmosphere.
    private static List<Human> FindOtherMarks(MurderController.Murder murder, int count)
    {
        var results = new List<Human>();
        var traitCache = Toolbox.Instance.resourcesCache[Il2CppType.Of<CharacterTrait>()];

        foreach (Citizen candidate in CityData.Instance.citizenDirectory)
        {
            if (results.Count >= count) break;
            if (candidate.isPlayer || candidate.isDead || candidate.isHomeless) continue;
            if (candidate == murder.victim || candidate == murder.murderer) continue;

            var human = candidate.TryCast<Human>();
            if (human == null) continue;
            if (!LooksLikeAMark(human, traitCache)) continue;

            results.Add(human);
        }
        return results;
    }

    private static bool LooksLikeAMark(Human human, Il2CppSystem.Collections.Generic.Dictionary<string, UnityEngine.ScriptableObject> traitCache)
    {
        string[] candidateTraits =
        {
            "Quirk-StatusWealthy", "Quirk-StatusElite", "Principle-Capitalist",
            "Char-Greedy", "Char-Extravagant", "Char-Reckless", "Char-Impulsive",
            "Char-Trusting", "Char-Boastful", "Affliction-GamblingAddict",
            "Interest-Poker", "Secret-SecretMoneyStash",
        };

        foreach (string traitName in candidateTraits)
        {
            if (!traitCache.ContainsKey(traitName)) continue;
            var trait = traitCache[traitName].TryCast<CharacterTrait>();
            if (trait != null && human.TraitExists(trait)) return true;
        }
        return false;
    }
}