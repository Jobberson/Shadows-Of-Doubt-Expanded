using System.Collections.Generic;
using UnityEngine;
using static MurderWeaponPreset;

namespace ShadowsOfDoubtExpanded;

// ─────────────────────────────────────────────────────────────────────────────
// THE CLOSER — killer type #3
//
// Design:
//   Trait:    NONE required. Motive is opportunity + greed, not disposition —
//             same reasoning as Fanatic having no personal-relationship bonus.
//   Murderer: HARD requirement — must be legitimately employed at a real
//             company. Not a CharacterTrait, so murdererTraitModifiers can't
//             express it; enforced instead via a custom PickNewMurderer
//             prefix in BrokerKillerMO.cs. The employment check itself
//             mirrors CharacterTrait.requiresEmployment's own definition from
//             Human.cs (confirmed, lines ~1393/1855/1958/1982):
//               job != null && job.employer != null
//               && !job.preset.selfEmployed && job.employer.address != null
//   Victim:   pure trait scoring — no relationship needed, since the victim
//             is the killer's private, off-books client, not a real
//             acquaintance or company customer. Hard gate on wealth/status
//             (any one of 3), then flat additive scoring across 9 "mark"
//             traits — every match counts the same (MARK_WEIGHT), no tiering.
//   Location: victim's home OR work. Never public/streets/den — staging as
//             a suicide requires privacy.
//   Staging:  "looks like a suicide" is FLAVOR TEXT ONLY for now (confirmed
//             with the person this mod is for — no engine mechanic for
//             disguised cause of death exists; see KillerFramework.cs notes
//             on the suicide string search coming up empty). May become a
//             real mechanic later; not blocking this file.
//
// Evidence:
//   - Forged suicide note (calling card) — physical-only, always present,
//     at victim's home.
//   - Shady contract/agreement — physical-only, at victim's home. Static
//     fallback text is genericized ("the firm", "my associates"); the real
//     per-murder version names the killer's actual employer dynamically,
//     same mechanism as FanaticJournal_DDSOverride_Patch. See BrokerKillerMO.cs.
//   - Victim's own vmail to a friend, panicked about the debt — real digital
//     vmail (WithTree, not WithPhysicalText), so it only surfaces if the
//     victim owns a computer. UNVERIFIED: LeadCitizen enum values beyond
//     killer/victim/nobody are unconfirmed in this codebase — using
//     WrittenBy(victim)/ReceivedBy(nobody) and keeping "the friend" purely
//     in the text rather than assuming a LeadCitizen.friend exists.
//   - Killer's private ledger, found in the KILLER's own apartment — same
//     "confirming, not suspect-generating" role as Fanatic's scripture/ledger.
//     Static fallback lists no names; the real per-murder version names 2
//     other real citizens (matching the same wealth/mark trait pool, picked
//     fresh from CityData.Instance.citizenDirectory, excluding the actual
//     victim) with made-up dollar amounts. See BrokerKillerMO.cs.
// ─────────────────────────────────────────────────────────────────────────────

public class BrokerKillerDefinition : KillerMODefinition
{
    public const string MO_NAME = "MO_BrokerKiller_Closer";
    public override string MOName => MO_NAME;

    // Set once in RegisterLeads, read by BrokerCallingCard_DDSOverride_Patch
    // in BrokerKillerMO.cs — PlaceCallingCard() creates the note item via
    // InteractableCreator directly, bypassing MurderController.SpawnItem
    // entirely, so it needs its own DDS-override patch rather than going
    // through PhysicalTextRegistry like the regular MOleads do.
    public static string SuicideNoteTextID { get; private set; }

    // ── Victim trait pools ────────────────────────────────────────────────────

    // Tier 1 — hard gate (RequiredAnyOf only, no score contribution of its
    // own). Establishes "rich enough to be worth targeting."
    private static readonly string[] WealthTraitNames =
    {
        "Quirk-StatusWealthy", "Quirk-StatusElite", "Principle-Capitalist",
    };

    // Tier 2 — optional, flat additive scoring. Explains WHY this particular
    // rich person fell for a predatory deal. All equal weight, per explicit
    // instruction — no tiered weighting between e.g. GamblingAddict and
    // Boastful.
    private static readonly string[] MarkTraitNames =
    {
        "Char-Greedy", "Char-Extravagant", "Char-Reckless", "Char-Impulsive",
        "Char-Trusting", "Char-Boastful", "Affliction-GamblingAddict",
        "Interest-Poker", "Secret-SecretMoneyStash",
    };

    /// <summary>Flat score contribution per matching mark trait — all equal weight.</summary>
    public const float MARK_WEIGHT = 4f;

    // ── MO configuration ──────────────────────────────────────────────────────

    protected override void ConfigureMO(MurderMO mo, Toolbox toolbox)
    {
        mo.baseDifficulty = 2;

        // Home or work — never public/streets/den. Staging requires privacy.
        mo.allowHome    = true;
        mo.allowWork    = true;
        mo.allowPublic  = false;
        mo.allowStreets = false;
        mo.allowDen     = false;

        // No personal-relationship bonus — same reasoning as Fanatic. The
        // killer doesn't need to know the victim socially; the relationship
        // is entirely the killer's own off-books side business.
        mo.acquaintedSuitabilityBoost  = 0;
        mo.likeSuitabilityBoost        = 0;
        mo.attractedToSuitabilityBoost = 0;
        mo.sameWorkplaceBoost          = 0;
        mo.murdererIsTenantBoost       = 0;

        // No murderer CharacterTrait requirement. The real hard requirement
        // (employed at a real company) is enforced in BrokerKillerMO.cs via
        // a custom PickNewMurderer prefix, since employment isn't expressible
        // as a CharacterTrait rule.

        // ── Victim scoring ───────────────────────────────────────────────────
        var wealthTraits = new List<CharacterTrait>();
        foreach (string traitName in WealthTraitNames)
        {
            var trait = FindTrait(toolbox, traitName);
            if (trait != null) wealthTraits.Add(trait);
        }
        SoDExpandedPlugin.Logger.LogInfo(
            $"[{MOName}] Resolved {wealthTraits.Count}/{WealthTraitNames.Length} wealth trait(s).");

        if (wealthTraits.Count > 0)
        {
            mo.victimTraitModifiers.Add(RequiredAnyOf(wealthTraits));
        }
        else
        {
            SoDExpandedPlugin.Logger.LogWarning(
                $"[{MOName}] No wealth traits resolved — victim selection has no wealth " +
                "requirement at all. Fix the trait name pool before testing this killer.");
        }

        var markTraits = new List<CharacterTrait>();
        foreach (string traitName in MarkTraitNames)
        {
            var trait = FindTrait(toolbox, traitName);
            if (trait != null) markTraits.Add(trait);
        }
        SoDExpandedPlugin.Logger.LogInfo(
            $"[{MOName}] Resolved {markTraits.Count}/{MarkTraitNames.Length} mark trait(s).");

        // Flat additive scoring — every matching mark trait contributes the
        // SAME weight (MARK_WEIGHT). Deliberately not gating on these at all;
        // the wealth tier above is the only hard requirement.
        foreach (var trait in markTraits)
            mo.victimTraitModifiers.Add(OptionalTrait(trait, MARK_WEIGHT));

        // ── Weapon pool ──────────────────────────────────────────────────────
        // Filtered to poison/melee only (no firearms — bullet holes/casings
        // don't fit a staged suicide). fists deliberately excluded too --
        // bare-handed doesn't fit "The Closer" staging a death as a suicide
        // any better than a gunshot does.
        //
        // IMPORTANT: WeaponType lives on MurderWeaponPreset, NOT on
        // MurderWeaponsPool -- donor.weaponsPool is a List<MurderWeaponsPool>
        // (whole pool objects), and each pool's murderWeaponPool is a list of
        // MurderWeaponPick, which only stores an InteractablePreset with no
        // direct WeaponType at all. Filtering has to happen per-PICK, cross-
        // referencing each pick's InteractablePreset against a
        // MurderWeaponPreset of the same name -- see BuildFilteredWeaponPool
        // in KillerFramework.cs.
        var filteredWeapons = BuildFilteredWeaponPool(toolbox,
            MurderWeaponPreset.WeaponType.handgun,
            MurderWeaponPreset.WeaponType.rifle,
            MurderWeaponPreset.WeaponType.shotgun,
            MurderWeaponPreset.WeaponType.fists);

        if (filteredWeapons.murderWeaponPool.Count > 0)
        {
            mo.weaponsPool.Add(filteredWeapons);
            SoDExpandedPlugin.Logger.LogInfo(
                $"[{MOName}] Weapon pool: {filteredWeapons.murderWeaponPool.Count} poison/weapon gathered across all registered MOs (firearms and fists excluded).");
        }
        else
        {
            SoDExpandedPlugin.Logger.LogWarning(
                $"[{MOName}] No poison/weapon resolved by type — falling back to the " +
                "unfiltered donor-borrow so the killer stays functional.");
            var donor = FindWeaponDonor(toolbox);
            if (donor != null)
                foreach (var pool in donor.weaponsPool)
                    mo.weaponsPool.Add(pool);
        }

        // Moniker.
        mo.monkierDDSMessageList = BuildSimpleDDSMessage(toolbox, "SoDExpanded_BrokerKiller_Moniker",
            "The Closer", "The Advisor", "The Broker", "The Silent Partner");

        // No wall graffiti. The forged suicide note IS the calling card (see
        // RegisterLeads below) — it goes through the engine's own
        // callingCardPool/PlaceCallingCard system, same as The Dove's
        // lipstick, so it spawns physically next to the victim's body
        // instead of as a regular MOlead somewhere in the house.
    }

    // ── Evidence / leads ──────────────────────────────────────────────────────

    protected override void RegisterLeads(MurderMO mo, Toolbox toolbox)
    {
        // ── Calling card: forged suicide note, placed next to the body ───────
        // Registered via mo.callingCardPool instead of a regular MOlead.
        // PickNewCallingCard() and PlaceCallingCard() are both called
        // automatically by the base game (MurderController.Murder — the
        // former at murder creation, the latter during MurderState.post),
        // so no custom spawn-timing or spawn-location code is needed here —
        // the engine places it physically at the victim's location on its
        // own, same mechanism The Dove's lipstick uses. traitModifiers is
        // left at its class default (empty List<MurdererModifierRule>) so
        // PickNewCallingCard()'s trait-test guard is skipped entirely and
        // this is guaranteed to be chosen every single murder — there's
        // only one entry in the pool anyway.
        string noteID = DDSBuilder.Document(toolbox)
            .Named("SoDExpanded_BrokerKiller_SuicideNote")
            .Message(0, 1,
                "I can't make the numbers work anymore. I've tried everything. " +
                "I didn't want anyone to have to deal with this, but I don't " +
                "see another way out of it. I'm sorry.",
                "BrokerKiller_Note_Body",
                handwriting: true)
            .Build();
        SuicideNoteTextID = noteID;

        var notePreset = FindInteractable(toolbox, "SuicideNote")
                       ?? FindInteractable(toolbox, "LetterGeneral");
        if (notePreset != null)
        {
            mo.callingCardPool.Add(new MurderMO.CallingCardPick
            {
                item = notePreset,
                origin = MurderMO.CallingCardOrigin.createAtScene,
                randomScoreRange = new Vector2(0f, 0f),
            });
        }
        else
        {
            SoDExpandedPlugin.Logger.LogWarning(
                $"[{MOName}] No note-shaped preset found — calling card skipped entirely.");
        }

        // ── Lead 2: the shady contract ─────────────────────────────────────────
        // Static fallback is deliberately genericized ("the firm") since
        // ConfigureMO/RegisterLeads run once at MO registration, before any
        // specific murder — before we know which citizen is the killer or
        // where they work. BrokerContract_DDSOverride_Patch (BrokerKillerMO.cs)
        // overwrites this per-murder with the killer's real employer name.
        //
        // Confirmed: EmploymentContractHome and DocumentGeneral both resolve
        // as real presets, but neither spawned as a properly findable/labeled
        // pickup (blank Unity object name, hard to distinguish in-world).
        // LetterGeneral is what actually worked in a live test (found and
        // read as "Letter" alongside the real employer name) — using it
        // directly instead of guessing between the other two.
        string contractFallbackID = DDSBuilder.Document(toolbox)
            .Named("SoDExpanded_BrokerKiller_Contract_Fallback")
            .Message(0, 1,
                "This agreement guarantees a return well above anything the firm " +
                "would ever offer through normal channels — which is exactly why " +
                "it stays between us. Sign below and consider it handled.",
                "BrokerKiller_Contract_Fallback_Body",
                handwriting: false)
            .Build();

        var contractPreset = FindInteractable(toolbox, "LetterGeneral");

        SoDExpandedPlugin.Logger.LogInfo(
            $"[{MOName}] Contract preset resolved to: {contractPreset?.name ?? "null"}");

        if (contractPreset != null)
        {
            mo.MOleads.Add(LeadBuilder.New("BrokerKiller_Contract")
                .WrittenBy(MurderPreset.LeadCitizen.killer)
                .ReceivedBy(MurderPreset.LeadCitizen.victim)
                .At(MurderPreset.LeadSpawnWhere.victimHome)
                .WithItem(contractPreset)
                .WithPhysicalText(contractFallbackID)
                .SpawnOn(MurderController.MurderState.acquireEuipment)
                .Security(4)
                .Tag(JobPreset.JobTag.D)
                .Build());
        }
        else
        {
            SoDExpandedPlugin.Logger.LogWarning(
                $"[{MOName}] LetterGeneral not found — contract lead skipped.");
        }

        // ── Lead 3: victim's own panicked vmail to a friend ───────────────────
        // Real digital vmail (WithTree) — only discoverable if the victim owns
        // a computer, same tradeoff Neighbor's grudge-vmail has. One-sided in
        // spirit (the "friend" side is flavor only, not a tracked LeadCitizen).
        string vmailID = DDSBuilder.Vmail(toolbox)
            .Named("SoDExpanded_BrokerKiller_PanicVmail")
            .Message(0, 1,
                "I think I made a horrible mistake with some money. " +
                "I can't talk about it properly over this but I'm scared.",
                "BrokerKiller_Vmail_Line1")
            .Message(1, 0,
                "What kind of mistake? You're worrying me now.",
                "BrokerKiller_Vmail_Line2")
            .Message(0, 1,
                "I'll sort it. Forget I said anything, okay? Please.",
                "BrokerKiller_Vmail_Line3")
            .Build();

        mo.MOleads.Add(LeadBuilder.New("BrokerKiller_PanicVmail")
            .WrittenBy(MurderPreset.LeadCitizen.victim)
            .ReceivedBy(MurderPreset.LeadCitizen.victimsClosest)
            .WithTree(vmailID, progress: 3f)
            .SpawnOn(MurderController.MurderState.acquireEuipment)
            .Tag(JobPreset.JobTag.J)
            .Build());

        // ── Lead 4: killer's private ledger, in the KILLER's own apartment ────
        // Confirming evidence, not suspect-generating — same role as Fanatic's
        // scripture/ledger. Static fallback names nobody; the real per-murder
        // version names 2 other real citizens + made-up dollar amounts. See
        // BrokerLedger_DDSOverride_Patch in BrokerKillerMO.cs.
        //
        // Tag moved from H to M: confirmed via decompiled MurderController.
        // SpawnItemsCheck that a lead only spawns if murder.activeMurderItems
        // doesn't already have an entry for its tag, with NO warning either
        // way if something else already claimed it. Tag H was almost
        // certainly already being claimed by one of the vanilla SerialKiller
        // preset's own built-in leads (same mechanism that put Fanatic's
        // scripture text on a vanilla "Note" item using tag R, earlier).
        // ReceivedBy/BelongsTo/location were all tested and ruled out as
        // factors before finding this — none of them ever mattered, since
        // this check happens before any of them are read.
        string ledgerFallbackID = DDSBuilder.Document(toolbox)
            .Named("SoDExpanded_BrokerKiller_Ledger_Fallback")
            .Message(0, 1,
                "— paid in full, quietly. no further contact needed.\n" +
                "— still deciding. good prospect, careless with money.\n" +
                "— close to the end of the line. will need resolving soon.",
                "BrokerKiller_Ledger_Fallback_Body",
                handwriting: true)
            .Build();

        // NOTE: previously tried "SalesLedger" / "Murder_SightingLog" first —
        // both resolved successfully from the InteractablePreset cache (no
        // "not found" warning), but SpawnItem returned a NULL Interactable at
        // runtime every time (confirmed via diagnostic logging). Almost
        // certainly fixture-bound presets (e.g. a shop-counter ledger) with
        // no valid placement point inside a residential apartment — this MO's
        // OTHER leads all target victimHome and place fine; this is the only
        // one targeting killerHome, and only it failed. LetterGeneral is the
        // one preset in this file already PROVEN to place successfully
        // (tags D and L both resolve to it), so use that directly instead of
        // re-gambling on another unverified fixture preset.
        var ledgerPreset = FindInteractable(toolbox, "LetterGeneral");
        SoDExpandedPlugin.Logger.LogInfo(
            $"[{MOName}] Ledger preset resolved to: {ledgerPreset?.name ?? "null"}");
        if (ledgerPreset != null)
        {
            mo.MOleads.Add(LeadBuilder.New("BrokerKiller_Ledger")
                .WrittenBy(MurderPreset.LeadCitizen.killer)
                .ReceivedBy(MurderPreset.LeadCitizen.nobody)
                .BelongsTo(MurderPreset.LeadCitizen.killer)
                .At(MurderPreset.LeadSpawnWhere.killerHome)
                .WithItem(ledgerPreset)
                .WithPhysicalText(ledgerFallbackID)
                .SpawnOn(MurderController.MurderState.acquireEuipment)
                .Security(3)
                .Tag(JobPreset.JobTag.M)
                .Build());
        }
        else
        {
            SoDExpandedPlugin.Logger.LogWarning(
                $"[{MOName}] LetterGeneral not found — ledger lead skipped.");
        }

        // ── Lead 5: killer's business card, left with the victim ─────────────
        // Direct link from victim straight to the killer's employer — no
        // dynamic-text patch needed, since business cards appear to auto-
        // populate from the owning citizen's own printed name/occupation
        // rather than needing an authored DDS body (the same way WorkID/
        // IDCard show the holder's real info without us writing any text
        // for them). ASSUMPTION, not confirmed against BusinessCardKiller's
        // actual class — if it turns out to need WithPhysicalText like every
        // other lead here, this will just render with no body text rather
        // than fail outright, so it's a safe thing to be wrong about.
        // Static fallback in case BrokerBusinessCard_DDSOverride_Patch (BrokerKillerMO.cs)
        // ever fails to resolve killer/employer/position — same genericized-then-
        // dynamically-overwritten pattern as the contract and ledger above, so this
        // never renders blank.
        string businessCardFallbackID = DDSBuilder.Document(toolbox)
            .Named("SoDExpanded_BrokerKiller_BusinessCard_Fallback")
            .Message(0, 1,
                "Discreet financial solutions. Call anytime — day or night.",
                "BrokerKiller_BusinessCard_Fallback_Body",
                handwriting: false)
            .Build();

        var businessCardPreset = FindInteractable(toolbox, "BusinessCardKiller")
                               ?? FindInteractable(toolbox, "BusinessCard");
        if (businessCardPreset != null)
        {
            mo.MOleads.Add(LeadBuilder.New("BrokerKiller_BusinessCard")
                .WrittenBy(MurderPreset.LeadCitizen.killer)
                .ReceivedBy(MurderPreset.LeadCitizen.victim)
                .BelongsTo(MurderPreset.LeadCitizen.killer)
                .At(MurderPreset.LeadSpawnWhere.victimHome)
                .WithItem(businessCardPreset)
                .WithPhysicalText(businessCardFallbackID)
                .SpawnOn(MurderController.MurderState.acquireEuipment)
                .Tag(JobPreset.JobTag.K)
                .Build());
        }
        else
        {
            SoDExpandedPlugin.Logger.LogWarning(
                $"[{MOName}] Neither BusinessCardKiller nor BusinessCard found — card lead skipped.");
        }

        // ── Lead 6: victim's own real debt notice ─────────────────────────────
        // Pure atmosphere/context — reinforces "already drowning in debt before
        // the killer ever showed up," unrelated to the killer specifically.
        // WrittenBy(nobody): an impersonal notice from an unnamed collection
        // agency, not authored by victim or killer.
        string debtNoticeID = DDSBuilder.Document(toolbox)
            .Named("SoDExpanded_BrokerKiller_DebtNotice")
            .Message(0, 1,
                "FINAL NOTICE: your account remains significantly overdue. " +
                "Immediate payment in full is required to avoid further action.",
                "BrokerKiller_DebtNotice_Body",
                handwriting: false)
            .Build();

        var debtNoticePreset = FindInteractable(toolbox, "DebtCollectionLetter")
                             ?? FindInteractable(toolbox, "BillFinalNotice")
                             ?? FindInteractable(toolbox, "RepossessionNotice")
                             ?? FindInteractable(toolbox, "LetterGeneral");
        if (debtNoticePreset != null)
        {
            mo.MOleads.Add(LeadBuilder.New("BrokerKiller_DebtNotice")
                .WrittenBy(MurderPreset.LeadCitizen.nobody)
                .ReceivedBy(MurderPreset.LeadCitizen.victim)
                .At(MurderPreset.LeadSpawnWhere.victimHome)
                .WithItem(debtNoticePreset)
                .WithPhysicalText(debtNoticeID)
                .SpawnOn(MurderController.MurderState.acquireEuipment)
                .Tag(JobPreset.JobTag.L)
                .Build());
        }
        else
        {
            SoDExpandedPlugin.Logger.LogWarning(
                $"[{MOName}] No debt-notice-shaped preset found — debt notice lead skipped.");
        }
    }
}