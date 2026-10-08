using UnityEngine;

namespace ShadowsOfDoubtExpanded;

// ─────────────────────────────────────────────────────────────────────────────
// THE NEIGHBOR — killer type #1
//
// Design:
//   Trait:    Char-Unforgiving (hard requirement)
//   Victim:   Direct neighbor in the same building, different apartment
//   Location: Always victim's home, always at night
//   Hook:     Social records and city directory become the primary trail
//
// Evidence:
//   - Vmail thread (killer→victim→killer): angry noise complaint that ends
//     with a quietly ominous line. Found on any computer either party owns.
//   - Physical letter (killer→victim): found at victim's apartment. Always
//     present regardless of computer ownership — keeps the case solvable.
// ─────────────────────────────────────────────────────────────────────────────

public class NeighborKillerDefinition : KillerMODefinition
{
    public const string MO_NAME = "MO_NeighborKiller_Unforgiving";
    public override string MOName => MO_NAME;

    // ── MO configuration ──────────────────────────────────────────────────────

    protected override void ConfigureMO(MurderMO mo, Toolbox toolbox)
    {
        mo.baseDifficulty = 1;

        // Murder always happens at victim's home — they open the door to a
        // face they recognise and never suspect anything.
        mo.allowHome    = true;
        mo.allowWork    = false;
        mo.allowPublic  = false;
        mo.allowStreets = false;
        mo.allowDen     = false;

        // Relationship boosts. Proximity (same building) is the real driver —
        // handled in NeighborKillerPatches.cs. These are secondary modifiers.
        mo.acquaintedSuitabilityBoost  = 3;
        mo.likeSuitabilityBoost        = 2;
        mo.attractedToSuitabilityBoost = 0;
        mo.sameWorkplaceBoost          = 0;
        mo.murdererIsTenantBoost       = 0;

        // Char-Unforgiving: hard requirement. A citizen without this trait
        // cannot be selected as this killer type. The kill feels disproportionate
        // because the original grievance was always small — this trait explains
        // how it became lethal.
        var trait = FindTrait(toolbox, "Char-Unforgiving");
        if (trait != null)
            mo.murdererTraitModifiers.Add(RequiredTrait(trait, scoreBonus: 8f));

        // Weapon pool: borrow from the richest available donor MO.
        var donor = FindWeaponDonor(toolbox);
        if (donor != null)
            foreach (var pool in donor.weaponsPool)
                mo.weaponsPool.Add(pool);

        // Moniker: "The Corridor Killer", "The Landing Killer", etc.
        mo.monkierDDSMessageList = BuildMonikerMessage(toolbox);

        // No calling card, no graffiti. The Neighbor is not a showman.
        // They kill quietly and go back to their apartment.
        // The absence of a signature IS the signature.
    }

    // ── Evidence / leads ──────────────────────────────────────────────────────

    protected override void RegisterLeads(MurderMO mo, Toolbox toolbox)
    {
        // ── Lead 1: vmail thread ──────────────────────────────────────────────
        // Digital exchange between killer and victim. Only discoverable if
        // either party owns a computer — but when found, it's damning.

        string vmailID = DDSBuilder.Vmail(toolbox)
            .Named("SoDExpanded_NeighborKiller_GrudgeVmail")
            .Message(0, 1,
                "Music. Again. Three nights this week. " +
                "I can hear it so loud, it's like you're doing it on purpose.",
                "NeighborKiller_Vmail_Line1")
            .Message(1, 0,
                "It's a few songs after work. You're not the only one who lives here.",
                "NeighborKiller_Vmail_Line2")
            .Message(0, 1,
                "Then I guess we'll see who lives here longer.",
                "NeighborKiller_Vmail_Line3")
            .Build();

        mo.MOleads.Add(LeadBuilder.New("NeighborKiller_GrudgeVmail")
            .WrittenBy(MurderPreset.LeadCitizen.killer)
            .ReceivedBy(MurderPreset.LeadCitizen.victim)
            .WithTree(vmailID, progress: 3f)
            .Tag(JobPreset.JobTag.V)
            .Build());

        // ── Lead 2: physical letter ───────────────────────────────────────────
        // Handwritten note from killer, found at victim's apartment. Always
        // present — this is the fallback that keeps the case solvable in
        // cities where neither the killer nor victim owns a computer.
        //
        // WithPhysicalText() (not WithTree()) is deliberate here: WithTree()
        // would also set MurderLeadItem.vmailThread, which makes the engine
        // ALSO generate a duplicate digital vmail of this same letter — wrong
        // for a lead that's specifically meant to be the physical fallback.
        // WithPhysicalText() only feeds SpawnItem_DDSOverrideFix_Patch
        // (KillerFramework.cs), which applies it to this letter's Interactable
        // directly and nothing else.

        string letterID = DDSBuilder.Document(toolbox)
            .Named("SoDExpanded_NeighborKiller_GrudgeLetter")
            .Message(0, 1,
                "I have asked you three times. Three times. " +
                "You don't hear me because you don't care. People like you never do. " +
                "Just know that I notice everything that happens on this floor. " +
                "Everything. This is the last time I write to you.",
                "NeighborKiller_Letter_Body",
                handwriting: true)
            .Build();

        var letterPreset = FindInteractable(toolbox, "LetterGeneral");
        if (letterPreset != null)
        {
            mo.MOleads.Add(LeadBuilder.New("NeighborKiller_GrudgeLetter")
                .WrittenBy(MurderPreset.LeadCitizen.killer)
                .ReceivedBy(MurderPreset.LeadCitizen.victim)
                .At(MurderPreset.LeadSpawnWhere.victimHome)
                .WithItem(letterPreset)
                .WithPhysicalText(letterID)
                .Tag(JobPreset.JobTag.W)
                .Build());
        }
        else
        {
            SoDExpandedPlugin.Logger.LogWarning(
                "[NeighborKiller] LetterGeneral not found — physical letter lead skipped.");
        }
    }

    // ── Neighbor geometry helpers (used by NeighborKillerPatches.cs) ──────────

    // Same building, different apartment.
    // Compared by ID, not reference — NewAddress/NewBuilding are not guaranteed
    // to support == across the IL2CPP managed wrapper boundary. See NeighborKillerPatches.cs.
    public static bool IsNeighbor(Human murderer, Human candidate)
    {
        if (murderer.home == null || candidate.home == null) return false;
        if (murderer.home.id == candidate.home.id) return false; // same apartment = housemate, not neighbor
        if (murderer.home.building == null || candidate.home.building == null) return false;
        return murderer.home.building.buildingID == candidate.home.building.buildingID;
    }

    // Same floor preference — soft scoring bonus, not a hard filter.
    public const float SAME_FLOOR_BONUS = 6f;
    public static bool IsSameFloor(Human murderer, Human candidate)
    {
        if (murderer.home?.floor == null || candidate.home?.floor == null) return false;
        return murderer.home.floor.floor == candidate.home.floor.floor;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private string BuildMonikerMessage(Toolbox toolbox)
    {
        var message = new DDSSaveClasses.DDSMessageSave
        {
            id   = toolbox.GenerateUniqueID(),
            name = "SoDExpanded_NeighborKiller_Moniker"
        };

        foreach (string text in new[] { "The Corridor Killer", "The Hallway Killer", "The Floor Killer", "The Building Killer" })
        {
            string blockID = toolbox.GenerateUniqueID();
            Strings.WriteToDictionary("dds.blocks", blockID, "ShadowsOfDoubtExpanded neighbour killer moniker", text);
            var block = new DDSSaveClasses.DDSBlockSave { id = blockID, name = $"SoDExpanded_NeighborMoniker_{text.Replace(" ", "")}" };
            toolbox.allDDSBlocks.Add(blockID, block);
            message.AddBlock(blockID);
        }

        toolbox.allDDSMessages.Add(message.id, message);
        return message.id;
    }
}