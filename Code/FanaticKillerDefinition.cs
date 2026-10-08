using System.Collections.Generic;
using UnityEngine;

namespace ShadowsOfDoubtExpanded;

// ─────────────────────────────────────────────────────────────────────────────
// THE ANGEL — killer type #2
//
// Design:
//   Trait:    a personality-disorder / religious-fanatic trait (hard requirement,
//             UNVERIFIED — see FANATIC_TRAIT below)
//   Victim:   any citizen carrying 1+ traits from the sin pool. More matching
//             sins = higher selection score (see the OptionalTrait stack below).
//   Location: always victim's home.
//   Time:     intended to be night-only (6pm–6am). NOT implemented yet — this
//             is the deferred "difficult" piece, tracked separately.
//
// Evidence:
//   - Bible passage, physical letter/note at the crime scene (same
//     WithPhysicalText() pattern as Neighbor's letter).
//   - Victim's journal: one-sided, physical, organically references the
//     killer's actual home building without naming him or saying he lives
//     there — the real investigative thread. Text is built dynamically per
//     murder; see FanaticKillerMO.cs.
//   - Annotated scripture + a private ledger, both found in the KILLER's own
//     apartment (LeadSpawnWhere.killerHome) — confirming evidence for once
//     the player already has a suspect, not suspect-generating on their own.
//   - Wall graffiti: "eight eyes" — confirmed to be pure text via
//     MurderMO.graffiti, no new art required. See ConfigureMO.
//   - Self-report + Enforcer dispatch after the kill — solved, see
//     FanaticSelfReport.cs.
//
// NOT implemented yet (tracked as follow-up, not blocking this file):
//   - Night-only time gating.
// ─────────────────────────────────────────────────────────────────────────────

public class FanaticKillerDefinition : KillerMODefinition
{
    public const string MO_NAME = "MO_FanaticKiller_Angel";
    public override string MOName => MO_NAME;

    // CONFIRMED (from live log output): "Principle-Religious" is the real
    // trait name. Was "Char-Devout" (a guess) before that — if this file ever
    // reverts to guessed trait names again, treat it the same way: log a
    // warning and skip the hard requirement rather than crash, don't assume.
    private const string FANATIC_TRAIT = "Principle-Religious";

    // Sin pool — v1 draft, exactly as scoped in conversation.
    private static readonly string[] SinTraitNames =
    {
        "Secret-Affair", "Secret-Paramour", "Secret-Abusive", "Secret-AbusivePartner",
        "Secret-CrimeSyndicate", "Secret-StealsFromWork", "Secret-KilledSomeone", "Secret-Addict",
        "Affliction-DrugAddict", "Affliction-GamblingAddict", "Affliction-Alcoholic",
        "Char-Dishonest", "Char-Greedy", "Char-Spiteful", "Char-Hostile",
    };

    /// <summary>How much each individual matching sin trait adds to victim score.</summary>
    public const float SIN_WEIGHT = 4f;

    // ── MO configuration ──────────────────────────────────────────────────────

    protected override void ConfigureMO(MurderMO mo, Toolbox toolbox)
    {
        mo.baseDifficulty = 2;

        // Murder always happens at the victim's home — he needs privacy for
        // the ritual (passage placement, graffiti) and to make his call after.
        mo.allowHome    = true;
        mo.allowWork    = false;
        mo.allowPublic  = false;
        mo.allowStreets = false;
        mo.allowDen     = false;

        // No personal-relationship bonus — he doesn't need to know his victim,
        // just to have judged them. All selection weight lives in sin scoring.
        mo.acquaintedSuitabilityBoost  = 0;
        mo.likeSuitabilityBoost        = 0;
        mo.attractedToSuitabilityBoost = 0;
        mo.sameWorkplaceBoost          = 0;
        mo.murdererIsTenantBoost       = 0;

        var fanaticTrait = FindTrait(toolbox, FANATIC_TRAIT);
        if (fanaticTrait != null)
        {
            mo.murdererTraitModifiers.Add(RequiredTrait(fanaticTrait, scoreBonus: 8f));
        }
        else
        {
            SoDExpandedPlugin.Logger.LogWarning(
                $"[{MOName}] Trait '{FANATIC_TRAIT}' not found — The Angel currently has NO " +
                "hard murderer-trait requirement. Verify the real trait name before relying " +
                "on this killer type.");
        }

        // ── Sin-based victim scoring ────────────────────────────────────────
        // Resolve the pool once; skip (log, don't fail) any name that doesn't
        // exist in this game's trait cache rather than breaking registration
        // over one bad string.
        var sinTraits = new List<CharacterTrait>();
        foreach (string traitName in SinTraitNames)
        {
            var sinTrait = FindTrait(toolbox, traitName);
            if (sinTrait != null)
                sinTraits.Add(sinTrait);
        }
        SoDExpandedPlugin.Logger.LogInfo(
            $"[{MOName}] Resolved {sinTraits.Count}/{SinTraitNames.Length} sin trait(s) from the pool.");

        if (sinTraits.Count > 0)
        {
            // Hard gate: must match at least one sin trait to be eligible at all.
            mo.victimTraitModifiers.Add(RequiredAnyOf(sinTraits));

            // Additive scoring: each matching sin trait stacks on top, so more
            // sins = higher score = more likely to be picked as victim.
            // UNVERIFIED assumption (see OptionalTrait doc comment) — if in
            // testing victims don't actually skew toward "more sins," this is
            // the first thing to check; fallback is a Neighbor-style custom
            // PickNewVictim prefix that scores manually instead of relying on
            // TraitTest's summation.
            foreach (var sinTrait in sinTraits)
                mo.victimTraitModifiers.Add(OptionalTrait(sinTrait, SIN_WEIGHT));
        }
        else
        {
            SoDExpandedPlugin.Logger.LogWarning(
                $"[{MOName}] No sin traits resolved at all — victim selection has no sin " +
                "requirement and will behave like a MO with an empty trait pool. Fix the " +
                "trait name pool before testing this killer.");
        }

        // Weapon pool: borrow from the richest available donor MO, same as Neighbor.
        var donor = FindWeaponDonor(toolbox);
        if (donor != null)
            foreach (var pool in donor.weaponsPool)
                mo.weaponsPool.Add(pool);

        // Moniker.
        mo.monkierDDSMessageList = BuildSimpleDDSMessage(toolbox, "SoDExpanded_FanaticKiller_Moniker",
            "The Angel", "The Confessor", "The Watchful", "The Zealot");

        // ── Graffiti: "eight eyes" scrawled on the wall ─────────────────────
        // Confirmed mechanism: MurderMO.graffiti is a List<MurderMO.Graffiti>,
        // spawned automatically by the engine's own Murder.GenerateGraffiti()
        // — pure data, exactly like leads and weapons. ddsMessageTextList is a
        // bare DDS message ID string (not a full tree, despite the "List" in
        // the name), so this is plain wall text — no new art asset needed,
        // matching how the game's own vanilla killers do wall graffiti.
        //
        // FIX: v1 built a Graffiti from scratch and left artImage null. There's
        // a DebugGraffitiScaler component (art/decal/pixelScaleMultiplier +
        // LoadArt()) that strongly suggests artImage is what actually gets
        // rendered onto the wall — a null artImage very plausibly registers
        // fine but renders nothing, which matches "not appearing, no crash."
        // Now borrowing preset + artImage + color + size wholesale from an
        // existing killer's own TEXT graffiti entry (proven to render in
        // vanilla) and only swapping the message ID and position.
        var donorGraffiti = FindTextGraffitiDonor(toolbox);
        if (donorGraffiti != null)
        {
            string graffitiMsgID = BuildSimpleDDSMessage(toolbox, "SoDExpanded_FanaticKiller_Graffiti",
                "GOD IS WATCHING");

            mo.graffiti.Add(new MurderMO.Graffiti
            {
                preset             = donorGraffiti.preset,
                artImage           = donorGraffiti.artImage,
                pos                = MurderMO.Graffiti.GraffitiPosition.nearbyWall,
                ddsMessageTextList = graffitiMsgID,
                color              = donorGraffiti.color,
                size               = donorGraffiti.size,
            });
        }
        // (FindTextGraffitiDonor already logs its own warning if it fails.)

        // No calling card — his signature is the passage and the graffiti,
        // not an object he leaves behind.
    }

    // ── Evidence / leads ──────────────────────────────────────────────────────

    protected override void RegisterLeads(MurderMO mo, Toolbox toolbox)
    {
        // Physical-only, same reasoning as Neighbor's letter: this should
        // always exist regardless of computer ownership, since it's the
        // killer's calling card, not correspondence.
        string verseID = DDSBuilder.Document(toolbox)
            .Named("SoDExpanded_FanaticKiller_BiblePassage")
            .Message(0, 1,
                "\"Vengeance is mine; I will repay,\" saith the Lord. " +
                "I am only the hand. You were weighed, and you were found wanting.",
                "FanaticKiller_Passage_Body",
                handwriting: true)
            .Build();

        var passagePreset = FindInteractable(toolbox, "KillerTauntNoteGeneral")
                          ?? FindInteractable(toolbox, "LetterGeneral");
        if (passagePreset != null)
        {
            mo.MOleads.Add(LeadBuilder.New("FanaticKiller_BiblePassage")
                .WrittenBy(MurderPreset.LeadCitizen.killer)
                .ReceivedBy(MurderPreset.LeadCitizen.victim)
                .At(MurderPreset.LeadSpawnWhere.victimHome)
                .WithItem(passagePreset)
                .WithPhysicalText(verseID)
                .Tag(JobPreset.JobTag.T)
                .Build());
        }
        else
        {
            SoDExpandedPlugin.Logger.LogWarning(
                $"[{MOName}] LetterGeneral not found — bible passage lead skipped.");
        }

        // ── Lead 2: victim's journal ─────────────────────────────────────────
        // One-sided, physical only — doesn't depend on the victim owning a
        // computer, unlike a vmail. The organic hook: the entry references
        // the killer's own home building by name, without saying he lives
        // there or naming him. Static text here is a SAFE FALLBACK ONLY —
        // the real, per-murder version (with the actual building name filled
        // in) is applied dynamically by FanaticJournal_DDSOverride_Patch in
        // FanaticKillerMO.cs, since we don't know which citizen is the killer
        // (or where they live) until a specific murder actually happens.
        // JobTag.S — see the tag ledger on PhysicalTextRegistry in
        // KillerFramework.cs before reusing this in a future killer.
        string journalFallbackID = DDSBuilder.Document(toolbox)
            .Named("SoDExpanded_FanaticKiller_Journal_Fallback")
            .Message(0, 1,
                "That person stopped me again today. Started going on about sin and " +
                "judgement, like he already knew things about me he shouldn't. " +
                "I don't know why he's always right there. I've started taking " +
                "the long way around.",
                "FanaticKiller_Journal_Fallback_Body",
                handwriting: true)
            .Build();

        var journalPreset = FindInteractable(toolbox, "Murder_Journal")
                          ?? FindInteractable(toolbox, "Diary")
                          ?? FindInteractable(toolbox, "LetterGeneral");
        if (journalPreset != null)
        {
            mo.MOleads.Add(LeadBuilder.New("FanaticKiller_Journal")
                .WrittenBy(MurderPreset.LeadCitizen.victim)
                .ReceivedBy(MurderPreset.LeadCitizen.nobody) // one-sided — a diary, not addressed to anyone
                .At(MurderPreset.LeadSpawnWhere.victimHome)
                .WithItem(journalPreset)
                .WithPhysicalText(journalFallbackID)
                .Tag(JobPreset.JobTag.S)
                .Build());
        }
        else
        {
            SoDExpandedPlugin.Logger.LogWarning(
                $"[{MOName}] LetterGeneral not found — journal lead skipped.");
        }

        // ── Lead 3: annotated scripture, found in the KILLER's own apartment ──
        // LeadSpawnWhere.killerHome (confirmed enum value, alongside victimHome/
        // victimWork/killerWork/killerDen/ransom) — same lead mechanism as
        // everything else, just pointed at his home instead of the victim's.
        // This isn't a suspect-generating lead like the journal; it's a
        // confirming one, for once the player already has him as a suspect.
        // Deliberately messier/more private than the composed passage he
        // leaves at scenes — that one's practiced; this one shows the seams.
        string scriptureID = DDSBuilder.Document(toolbox)
            .Named("SoDExpanded_FanaticKiller_Scripture")
            .Message(0, 1,
                "How long, O Lord, holy and true, dost thou not judge? " +
                "\"Someone has to. " +
                "No one else will look at them and see.\"",
                "FanaticKiller_Scripture_Body",
                handwriting: true)
            .Build();

        // CONFIRMED real preset names (from Snog's item list). AssortedBooks1/2
        // are real book-shaped props — a proper physical book, unlike the
        // earlier guesses (BookGeneral/Book/Novel) which didn't exist at all.
        var scripturePreset = FindInteractable(toolbox, "LetterGeneral")
                            ?? FindInteractable(toolbox, "AssortedBooks2")
                            ?? FindInteractable(toolbox, "AssortedBooks1");
        if (scripturePreset != null)
        {
            mo.MOleads.Add(LeadBuilder.New("FanaticKiller_Scripture")
                .WrittenBy(MurderPreset.LeadCitizen.killer)
                .ReceivedBy(MurderPreset.LeadCitizen.nobody)
                .BelongsTo(MurderPreset.LeadCitizen.killer)
                .At(MurderPreset.LeadSpawnWhere.killerHome)
                .WithItem(scripturePreset)
                .WithPhysicalText(scriptureID)
                .Tag(JobPreset.JobTag.R)
                .Build());
        }
        else
        {
            SoDExpandedPlugin.Logger.LogWarning(
                $"[{MOName}] LetterGeneral not found — scripture lead skipped.");
        }

        // ── Lead 4: private ledger, also in the killer's apartment ────────────
        // Names nobody real — we can't know who he'd be "watching" next at
        // design time — but implies there's a list, and that it's ongoing.
        // Meant to unsettle more than to inform: the case might be closed,
        // but he clearly hasn't stopped looking.
        string ledgerID = DDSBuilder.Document(toolbox)
            .Named("SoDExpanded_FanaticKiller_Ledger")
            .Message(0, 1,
                "the one who works late. pride, he doesn't even hide it. " +
                "third floor, corner unit. still watching. not yet. " +
                "loud laugh, no shame. greed, maybe. maybe both. " +
                "the one two floors down. not sure yet. watching a little longer.",
                "FanaticKiller_Ledger_Body",
                handwriting: true)
            .Build();

        var ledgerPreset = FindInteractable(toolbox, "CrumpledPaper")
                         ?? FindInteractable(toolbox, "CrumpledHalfWrittenLetter")
                         ?? FindInteractable(toolbox, "Murder_SightingLog")
                         ?? FindInteractable(toolbox, "LetterGeneral");
        if (ledgerPreset != null)
        {
            mo.MOleads.Add(LeadBuilder.New("FanaticKiller_Ledger")
                .WrittenBy(MurderPreset.LeadCitizen.killer)
                .ReceivedBy(MurderPreset.LeadCitizen.nobody)
                .BelongsTo(MurderPreset.LeadCitizen.killer)
                .At(MurderPreset.LeadSpawnWhere.killerHome)
                .WithItem(ledgerPreset)
                .WithPhysicalText(ledgerID)
                .Tag(JobPreset.JobTag.U)
                .Build());
        }
        else
        {
            SoDExpandedPlugin.Logger.LogWarning(
                $"[{MOName}] LetterGeneral not found — ledger lead skipped.");
        }
    }
}