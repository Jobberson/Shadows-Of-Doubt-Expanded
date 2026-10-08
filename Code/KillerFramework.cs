using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace ShadowsOfDoubtExpanded;

// ─────────────────────────────────────────────────────────────────────────────
// REGISTRATION HOOK
// Calls KillerRegistry.RegisterAll on every Toolbox.LoadAll — the single
// entry point for all killer types. Adding a new killer means implementing
// KillerMODefinition and adding it to KillerRegistry.All. Nothing else.
// ─────────────────────────────────────────────────────────────────────────────

[HarmonyPatch(typeof(Toolbox), "LoadAll")]
public static class ToolboxLoadAll_AllKillers_Patch
{
    [HarmonyPostfix]
    public static void Postfix(Toolbox __instance)
    {
        KillerRegistry.RegisterAll(__instance);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// PHYSICAL EVIDENCE TEXT FIX
//
// Root cause: MurderController.SpawnItem constructs a spawned Interactable's
// Evidence via Toolbox.GetOrCreateEvidenceForInteractable, passing only a
// murderID and jobTag as Interactable.Passed data. It never forwards
// MurderLeadItem.vmailThread into the item's DDS override — even when a lead
// has both spawnItem AND vmailThread set (exactly the pattern LeadBuilder
// .WithItem() + .WithTree() produces). Result: the physical item spawns
// correctly (right preset, right location, right owner) but reads blank,
// because EvidencePreset.ddsDocumentID is a static, shared, design-time field
// on the preset asset — not something the lead pipeline ever touches.
//
// The engine already has the correct mechanism for dynamic per-instance text:
// Interactable.SetDDSOverride(string). It's safe to call regardless of
// timing — if Evidence already exists it writes straight through via
// Evidence.SetOverrideDDS(); if not, it stashes the ID in the Interactable's
// `pv` (Passed) list so the Evidence constructor picks it up itself via
// PassedVarType.ddsOverride.
//
// In practice Evidence is always already constructed by the time SpawnItem
// returns (MainSetupEnd runs synchronously inside PlaceObject, and SpawnItem
// reads interactable.node/.wPos immediately afterward for logging) — so this
// always hits the fast "evidence != null" path directly.
//
// We match the correct lead by itemTag, since SpawnItem receives itemTag as
// a parameter and Tag() is already the unique-per-lead key the framework
// relies on for dedup/useIf.
// ─────────────────────────────────────────────────────────────────────────────

[HarmonyPatch(typeof(MurderController), nameof(MurderController.SpawnItem))]
public static class SpawnItem_DDSOverrideFix_Patch
{
    [HarmonyPostfix]
    public static void Postfix(Interactable __result, MurderController.Murder murder, JobPreset.JobTag itemTag)
    {
        if (__result == null) return;

        // Guard: only touch this item if its tag actually belongs to a lead
        // the CURRENT murder's own MO registered. JobTag only has 26 possible
        // values and vanilla MurderPresets reuse them heavily for their own
        // unrelated built-in leads — without this check we stomp on any
        // vanilla (or other-MO) item that happens to share a tag letter with
        // one of ours. CONFIRMED happening live via log: tag R (Fanatic's
        // scripture) landed on a vanilla "Note" item during a Broker-only
        // isolated case, because PhysicalTextRegistry is matched purely by
        // tag with no regard for which MO actually owns this murder.
        bool tagBelongsToThisMurdersMO = false;
        if (murder?.mo?.MOleads != null)
        {
            foreach (var l in murder.mo.MOleads)
            {
                if (l.itemTag == itemTag) { tagBelongsToThisMurdersMO = true; break; }
            }
        }
        if (!tagBelongsToThisMurdersMO) return;

        // Preferred path: text registered via LeadBuilder.WithPhysicalText().
        // Never triggers a duplicate digital vmail — physical item only.
        if (PhysicalTextRegistry.TryGet(itemTag, out string physicalTreeID))
        {
            __result.SetDDSOverride(physicalTreeID);
            SoDExpandedPlugin.Logger.LogInfo(
                $"[SoDExpanded] Physical-only DDS override '{physicalTreeID}' applied to " +
                $"spawned item '{__result.name}' (tag: {itemTag}).");
            return;
        }

        // Back-compat path: a lead that set WithTree() directly on a spawnItem
        // lead. Works, but ALSO produces a duplicate digital vmail via the
        // engine's own SpawnItemsCheck handling of vmailThread — see WithTree()
        // doc comment. Prefer WithPhysicalText() for new leads.
        if (murder?.mo?.MOleads == null) return;
        MurderPreset.MurderLeadItem lead = null;
        foreach (var l in murder.mo.MOleads)
        {
            if (l.itemTag == itemTag && !string.IsNullOrEmpty(l.vmailThread))
            {
                lead = l;
                break;
            }
        }
        if (lead == null) return;

        __result.SetDDSOverride(lead.vmailThread);
        SoDExpandedPlugin.Logger.LogInfo(
            $"[SoDExpanded] DDS override '{lead.vmailThread}' applied to spawned item " +
            $"'{__result.name}' (tag: {itemTag}) — note: this lead also produced a " +
            "duplicate digital vmail via WithTree(). Consider switching to WithPhysicalText().");
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// KILLER REGISTRY
// The single list of all killer definitions. To add a new killer type,
// implement KillerMODefinition and add an instance here.
// ─────────────────────────────────────────────────────────────────────────────

public static class KillerRegistry
{
    private static readonly List<KillerMODefinition> All = new()
    {
        new NeighborKillerDefinition(),
        new FanaticKillerDefinition(),
        new BrokerKillerDefinition(),
        // new YourNextKillerDefinition(),
    };

    public static void RegisterAll(Toolbox toolbox)
    {
        foreach (var definition in All)
            definition.Register(toolbox);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// KILLER MO DEFINITION — ABSTRACT BASE
// Each killer type extends this. Override ConfigureMO to set MO fields
// (traits, location rules, weapon pool, moniker). Override RegisterLeads
// to add evidence using DDSBuilder and LeadBuilder.
// ─────────────────────────────────────────────────────────────────────────────

public abstract class KillerMODefinition
{
    // ── Required ──────────────────────────────────────────────────────────────

    /// <summary>The unique name that identifies this MO in the resource cache.</summary>
    public abstract string MOName { get; }

    /// <summary>Configure MO fields: location rules, trait requirements, weapon pool, moniker.</summary>
    protected abstract void ConfigureMO(MurderMO mo, Toolbox toolbox);

    // ── Optional overrides ────────────────────────────────────────────────────

    /// <summary>Add leads (evidence) to the MO using DDSBuilder and LeadBuilder.</summary>
    protected virtual void RegisterLeads(MurderMO mo, Toolbox toolbox) { }

    // ── Framework entry point ─────────────────────────────────────────────────

    public void Register(Toolbox toolbox)
    {
        var moType = Il2CppType.Of<MurderMO>();
        if (!toolbox.resourcesCache.ContainsKey(moType))
        {
            SoDExpandedPlugin.Logger.LogWarning($"[{MOName}] MurderMO cache missing — skipping.");
            return;
        }

        var moCache = toolbox.resourcesCache[moType];
        if (moCache.ContainsKey(MOName)) return; // idempotent across city loads

        var mo        = ScriptableObject.CreateInstance<MurderMO>();
        mo.name       = MOName;
        mo.disabled   = false;
        mo.pickRandomScoreRange   = new Vector2(0f, 1f);
        mo.victimRandomScoreRange = new Vector2(0f, 1f);

        ConfigureMO(mo, toolbox);
        RegisterLeads(mo, toolbox);

        // Compatible with all non-disabled MurderPresets by default so the MO
        // enters the pool for every standard case type.
        var presetType = Il2CppType.Of<MurderPreset>();
        if (toolbox.resourcesCache.ContainsKey(presetType))
        {
            foreach (var pair in toolbox.resourcesCache[presetType])
            {
                var preset = pair.Value.TryCast<MurderPreset>();
                if (preset != null && !preset.disabled)
                    mo.compatibleWith.Add(preset);
            }
        }

        toolbox.ProcessLoadedScriptableObject(mo);
        SoDExpandedPlugin.Logger.LogInfo(
            $"[SoDExpanded] Registered '{MOName}' — " +
            $"{mo.compatibleWith.Count} preset(s), {mo.weaponsPool.Count} weapon pool(s), " +
            $"{mo.MOleads.Count} lead(s).");
    }

    // ── Shared helpers (available to all definitions) ─────────────────────────

    /// <summary>Finds a CharacterTrait from the Toolbox cache by name.</summary>
    protected CharacterTrait FindTrait(Toolbox toolbox, string traitName)
    {
        var cache = toolbox.resourcesCache[Il2CppType.Of<CharacterTrait>()];
        if (!cache.ContainsKey(traitName))
        {
            SoDExpandedPlugin.Logger.LogWarning($"[{MOName}] Trait '{traitName}' not found.");
            return null;
        }
        return cache[traitName].TryCast<CharacterTrait>();
    }

    /// <summary>Finds an InteractablePreset from the Toolbox cache by name.</summary>
    protected InteractablePreset FindInteractable(Toolbox toolbox, string presetName)
    {
        var type = Il2CppType.Of<InteractablePreset>();
        if (!toolbox.resourcesCache.ContainsKey(type))
        {
            SoDExpandedPlugin.Logger.LogWarning($"[{MOName}] InteractablePreset cache not found.");
            return null;
        }
        var cache = toolbox.resourcesCache[type];
        if (!cache.ContainsKey(presetName))
        {
            SoDExpandedPlugin.Logger.LogWarning($"[{MOName}] InteractablePreset '{presetName}' not found.");
            return null;
        }
        return cache[presetName].TryCast<InteractablePreset>();
    }

    /// <summary>
    /// Finds the MO with the richest weapon pool to use as donor.
    /// Skips disabled MOs and our own (prevents self-reference on reload).
    /// </summary>
    protected MurderMO FindWeaponDonor(Toolbox toolbox)
    {
        var cache = toolbox.resourcesCache[Il2CppType.Of<MurderMO>()];
        MurderMO donor = null;
        foreach (var pair in cache)
        {
            var candidate = pair.Value.TryCast<MurderMO>();
            if (candidate == null || candidate.disabled || candidate.name == MOName) continue;
            if (candidate.weaponsPool.Count == 0) continue;
            if (donor == null || candidate.weaponsPool.Count > donor.weaponsPool.Count)
                donor = candidate;
        }
        if (donor == null)
            SoDExpandedPlugin.Logger.LogWarning($"[{MOName}] No weapon donor found.");
        return donor;
    }

    /// <summary>
    /// Gathers individual MurderWeaponPick entries (not whole MurderWeaponsPool
    /// objects) from every registered MO's weaponsPool, excluding any whose
    /// WeaponType is in excludedTypes, and returns them wrapped in a single
    /// freshly-created MurderWeaponsPool instance.
    ///
    /// IMPORTANT: never mutate a borrowed pool/pick in place. MurderWeaponsPool
    /// and MurderMO are Resources-cache SINGLETONS shared across every killer
    /// that references them (same class of bug documented in
    /// MOTestIsolation.cs re: mo.disabled persisting across city loads) --
    /// editing donor.weaponsPool[i].murderWeaponPool directly would corrupt
    /// that pool for every OTHER killer borrowing from the same donor. This
    /// only ever reads from donors and copies matching picks into a new
    /// instance we own outright.
    ///
    /// ASSUMPTION, not yet confirmed live: matches each MurderWeaponPick's
    /// InteractablePreset against a MurderWeaponPreset of the SAME name in
    /// the toolbox cache, since MurderWeaponPick only stores the
    /// InteractablePreset (no direct WeaponType). If that naming assumption
    /// is wrong, this fails safe -- an unmatched weapon is logged and
    /// excluded rather than silently included -- but it's worth confirming
    /// with a debug dump the first time this runs, the same way we confirmed
    /// JobPreset.name earlier.
    /// </summary>
    protected MurderWeaponsPool BuildFilteredWeaponPool(Toolbox toolbox, params MurderWeaponPreset.WeaponType[] excludedTypes)
    {
        var weaponPresetType = Il2CppType.Of<MurderWeaponPreset>();
        if (!toolbox.resourcesCache.ContainsKey(weaponPresetType))
        {
            SoDExpandedPlugin.Logger.LogWarning($"[{MOName}] No MurderWeaponPreset cache found — can't filter by weapon type.");
            return ScriptableObject.CreateInstance<MurderWeaponsPool>();
        }
        var weaponPresetCache = toolbox.resourcesCache[weaponPresetType];
        var moCache = toolbox.resourcesCache[Il2CppType.Of<MurderMO>()];

        var filteredPool = ScriptableObject.CreateInstance<MurderWeaponsPool>();
        var seenWeaponNames = new HashSet<string>();
        int skippedUnclassified = 0;

        foreach (var pair in moCache)
        {
            var candidate = pair.Value.TryCast<MurderMO>();
            if (candidate == null || candidate.name == MOName) continue;

            foreach (var pool in candidate.weaponsPool)
            {
                if (pool == null) continue;
                foreach (var pick in pool.murderWeaponPool)
                {
                    if (pick?.weapon == null) continue;
                    if (!seenWeaponNames.Add(pick.weapon.name)) continue; // de-dupe across donors

                    if (!weaponPresetCache.ContainsKey(pick.weapon.name))
                    {
                        skippedUnclassified++;
                        continue; // can't confirm type — exclude rather than risk a gun slipping through
                    }
                    var weaponPreset = weaponPresetCache[pick.weapon.name].TryCast<MurderWeaponPreset>();
                    if (weaponPreset == null || System.Array.IndexOf(excludedTypes, weaponPreset.type) >= 0)
                        continue;

                    filteredPool.murderWeaponPool.Add(new MurderWeaponsPool.MurderWeaponPick
                    {
                        weapon = pick.weapon,
                        chanceOfDroppingAtScene = pick.chanceOfDroppingAtScene,
                        randomScoreRange = pick.randomScoreRange,
                        traitModifiers = pick.traitModifiers,
                    });
                }
            }
        }

        if (skippedUnclassified > 0)
            SoDExpandedPlugin.Logger.LogInfo(
                $"[{MOName}] Weapon pool filter: {skippedUnclassified} weapon(s) had no matching " +
                "MurderWeaponPreset by name — excluded rather than guessed at.");

        return filteredPool;
    }

    /// <summary>
    /// Builds a hard-required trait modifier rule (mustPassForApplication = true).
    /// The killer cannot be selected without this trait.
    /// </summary>
    protected MurderPreset.MurdererModifierRule RequiredTrait(CharacterTrait trait, float scoreBonus = 8f)
    {
        var rule = new MurderPreset.MurdererModifierRule();
        rule.rule                   = CharacterTrait.RuleType.ifAnyOfThese;
        rule.mustPassForApplication = true;
        rule.scoreModifier          = scoreBonus;
        rule.traitList.Add(trait);
        return rule;
    }

    /// <summary>
    /// Builds a soft/optional modifier rule: contributes scoreModifier to the
    /// TraitTest total when the trait is present, but never gates selection on
    /// its own (mustPassForApplication = false). Use several of these on the
    /// same list to get additive "more matching traits = higher score" scoring
    /// -- TraitTest(Citizen, ref List<MurdererModifierRule>, out float) sums
    /// every rule that matches in one pass, it isn't first-match-wins.
    /// UNVERIFIED: confirmed by signature/shape, not yet observed in a live
    /// game session -- worth a debug log the first time this MO scores a pick.
    /// </summary>
    protected MurderPreset.MurdererModifierRule OptionalTrait(CharacterTrait trait, float scoreBonus)
    {
        var rule = new MurderPreset.MurdererModifierRule();
        rule.rule                   = CharacterTrait.RuleType.ifAnyOfThese;
        rule.mustPassForApplication = false;
        rule.scoreModifier          = scoreBonus;
        rule.traitList.Add(trait);
        return rule;
    }

    /// <summary>
    /// Builds a hard gate requiring AT LEAST ONE of the given traits, with no
    /// score contribution of its own (scoreModifier = 0) -- pair with several
    /// OptionalTrait() rules on the same traits to additionally scale score by
    /// how many of them match.
    /// </summary>
    protected MurderPreset.MurdererModifierRule RequiredAnyOf(List<CharacterTrait> traits)
    {
        var rule = new MurderPreset.MurdererModifierRule();
        rule.rule                   = CharacterTrait.RuleType.ifAnyOfThese;
        rule.mustPassForApplication = true;
        rule.scoreModifier          = 0f;
        foreach (var trait in traits)
            rule.traitList.Add(trait);
        return rule;
    }

    /// <summary>
    /// Builds a single bare DDS message (not a full tree) -- the shape needed
    /// anywhere the engine wants a plain message ID string, e.g.
    /// MurderMO.monkierDDSMessageList or MurderMO.Graffiti.ddsMessageTextList.
    /// Pass multiple textOptions to register several alternates under one
    /// message (same pattern the moniker system uses). For an actual
    /// back-and-forth conversation thread, use DDSBuilder instead.
    /// </summary>
    protected string BuildSimpleDDSMessage(Toolbox toolbox, string name, params string[] textOptions)
    {
        var message = new DDSSaveClasses.DDSMessageSave { id = toolbox.GenerateUniqueID(), name = name };

        foreach (string text in textOptions)
        {
            string blockID = toolbox.GenerateUniqueID();
            Strings.WriteToDictionary("dds.blocks", blockID, "ShadowsOfDoubtExpanded", text);
            var block = new DDSSaveClasses.DDSBlockSave { id = blockID, name = $"{name}_{blockID}" };
            toolbox.allDDSBlocks.Add(blockID, block);
            message.AddBlock(blockID);
        }

        toolbox.allDDSMessages.Add(message.id, message);
        return message.id;
    }

    /// <summary>
    /// Finds the InteractablePreset used by an existing MO's wall graffiti, to
    /// reuse for our own MurderMO.Graffiti entries -- same borrow-from-donor
    /// shape as FindWeaponDonor/FindDocumentStyleDonor. Skips disabled MOs,
    /// our own MO, and MOs with an empty graffiti pool.
    /// </summary>
    /// <summary>
    /// Finds an existing MO's TEXT graffiti entry (non-empty ddsMessageTextList)
    /// to borrow visual setup from -- same borrow-from-donor shape as
    /// FindWeaponDonor/FindDocumentStyleDonor. Returns the whole entry, not
    /// just the preset: artImage is a required-looking field (DebugGraffitiScaler
    /// loads it via LoadArt() to size/render the decal) and leaving it null is
    /// the most likely reason a graffiti entry silently fails to render even
    /// though registration succeeds without error. Copy preset/artImage/color/
    /// size from the returned entry and only override pos + ddsMessageTextList,
    /// rather than constructing a Graffiti from scratch.
    /// </summary>
    protected MurderMO.Graffiti FindTextGraffitiDonor(Toolbox toolbox)
    {
        var cache = toolbox.resourcesCache[Il2CppType.Of<MurderMO>()];
        foreach (var pair in cache)
        {
            var candidate = pair.Value.TryCast<MurderMO>();
            if (candidate == null || candidate.disabled || candidate.name == MOName) continue;
            if (candidate.graffiti == null) continue;

            foreach (var entry in candidate.graffiti)
            {
                if (!string.IsNullOrEmpty(entry.ddsMessageTextList))
                    return entry;
            }
        }
        SoDExpandedPlugin.Logger.LogWarning(
            $"[{MOName}] No existing MO with a TEXT graffiti entry (non-empty ddsMessageTextList) " +
            "found to borrow visual setup from -- graffiti will be skipped rather than spawned " +
            "with a guessed (and possibly non-functional) artImage/preset combo.");
        return null;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// DDS BUILDER — fluent API for building vmail and document trees
//
// Usage:
//   string treeID = DDSBuilder.Vmail(toolbox)
//       .Named("MyVmail")
//       .Message(saidBy: 0, saidTo: 1, "First message", "Block_Name")
//       .Message(saidBy: 1, saidTo: 0, "Reply", "Block_Reply")
//       .Build();
//
//   string docID = DDSBuilder.Document(toolbox)
//       .Named("MyLetter")
//       .Message(saidBy: 0, saidTo: 1, "Letter text", "Block_Body")
//       .Build();
// ─────────────────────────────────────────────────────────────────────────────

public class DDSBuilder
{
    private readonly Toolbox _toolbox;
    private readonly DDSSaveClasses.DDSTreeSave _tree;
    private string _lastInstanceID;

    private DDSBuilder(Toolbox toolbox, DDSSaveClasses.TreeType treeType)
    {
        _toolbox = toolbox;
        _tree = new DDSSaveClasses.DDSTreeSave
        {
            id           = toolbox.GenerateUniqueID(),
            treeType     = treeType,
            // TriggerPoint.never ensures this tree is ONLY used via our
            // explicit murder lead spawn, and not picked up by the game's
            // own ambient vmail / flavor conversation system.
            triggerPoint = DDSSaveClasses.TriggerPoint.never,
            repeat       = DDSSaveClasses.RepeatSetting.never,
            participantA = new DDSSaveClasses.DDSParticipant { required = true },
            participantB = new DDSSaveClasses.DDSParticipant { required = true },
        };
    }

    /// <summary>Creates a vmail-type tree (shown in the in-game mail app).</summary>
    public static DDSBuilder Vmail(Toolbox toolbox)
        => new(toolbox, DDSSaveClasses.TreeType.vmail);

    /// <summary>
    /// Creates a document-type tree (shown when reading a physical note/letter).
    ///
    /// DDSTreeSave.document (background/fill/size/colour) has no default and is
    /// normally hand-set per letter template in Unity's DDS editor. Left null,
    /// WindowContentController.LoadContent() dereferences it unconditionally for
    /// non-vmail trees (content.document.size) and throws before spawning any
    /// text — the InfoWindow frame still renders, but the body never populates.
    /// (Vmail-type trees don't have this problem: LoadContent's vmail branch
    /// uses a shared hardcoded template tree's .document, never its own.)
    ///
    /// We borrow page style from the first existing document-type tree found in
    /// the toolbox cache, same pattern as FindWeaponDonor — a runtime-built
    /// letter has no natural "correct" style of its own, so reuse a real one.
    /// </summary>
    public static DDSBuilder Document(Toolbox toolbox)
    {
        var builder = new DDSBuilder(toolbox, DDSSaveClasses.TreeType.document);
        builder._tree.document = FindDocumentStyleDonor(toolbox);
        return builder;
    }

    private static DDSSaveClasses.DDSDocument FindDocumentStyleDonor(Toolbox toolbox)
    {
        foreach (var pair in toolbox.allDDSTrees)
        {
            var tree = pair.Value;
            if (tree != null && tree.treeType == DDSSaveClasses.TreeType.document && tree.document != null)
                return tree.document;
        }
        SoDExpandedPlugin.Logger.LogWarning(
            "[DDSBuilder] No existing document-type DDS tree found to borrow page " +
            "style from — falling back to a plain page (no background sprite).");
        return new DDSSaveClasses.DDSDocument { size = new Vector2(400f, 500f) };
    }

    /// <summary>Sets the internal debug name of the tree.</summary>
    public DDSBuilder Named(string name) { _tree.name = name; return this; }

    /// <summary>
    /// Hides this thread from participant B's inbox.
    /// Useful if participant B shouldn't see the thread (e.g. victim-only evidence).
    /// </summary>
    public DDSBuilder HideFromRecipient()
    {
        _tree.participantB.disableInbox = true;
        return this;
    }

    /// <summary>
    /// Adds one message line to the tree and links it from the previous one.
    /// saidBy/saidTo: 0 = participantA (writer/killer), 1 = participantB (receiver/victim).
    /// blockName: optional stable name for the DDS block (useful for debugging).
    /// handwriting: renders in the writer citizen's handwriting font (falls back to
    /// DDSControls.defaultHandwritingFont if they don't have one) instead of the
    /// plain typed default. Appropriate for physical notes/letters; leave false
    /// for vmail/digital messages, which should look typed.
    /// </summary>
    public DDSBuilder Message(int saidBy, int saidTo, string text, string blockName = null, bool handwriting = false)
    {
        blockName ??= $"Block_{_toolbox.GenerateUniqueID()}";

        // Register text block
        string blockID = _toolbox.GenerateUniqueID();
        Strings.WriteToDictionary("dds.blocks", blockID, "ShadowsOfDoubtExpanded", text);
        var block = new DDSSaveClasses.DDSBlockSave { id = blockID, name = blockName };
        _toolbox.allDDSBlocks.Add(blockID, block);

        // Wrap in a message
        var message = new DDSSaveClasses.DDSMessageSave
        {
            id   = _toolbox.GenerateUniqueID(),
            name = blockName + "_Msg"
        };
        message.AddBlock(blockID);
        _toolbox.allDDSMessages.Add(message.id, message);

        // Add to tree and configure
        string instanceID     = _tree.AddMessage(message.id);
        var settings          = FindSettings(instanceID);
        settings.saidBy       = saidBy;
        settings.saidTo       = saidTo;
        settings.isHandwriting = handwriting;

        // FIX: WindowContentController.ConstructContent anchors the text box
        // to the page's top-center (SetAnchor(0.5,1)) and sets its RectTransform
        // directly from msg.pos/msg.size. We never set either, so every message
        // defaulted to a ZERO-SIZED box centered on that top anchor point —
        // roughly half of it sitting above the visible page. That's what was
        // producing the inconsistent, often-way-down-the-page text. alignH/
        // alignV don't need setting — 0/0 already maps to TopLeft, which is
        // correct for a normal letter.
        //
        // UNVERIFIED: the exact pivot point Unity has set on the text element
        // prefab itself (not visible from C# source) could mean this margin
        // needs minor visual tuning, but the box will no longer be zero-sized
        // regardless — that was the unambiguous part of the bug.
        if (_tree.treeType == DDSSaveClasses.TreeType.document && _tree.document != null)
        {
            const float margin = 20f;
            var docSize = _tree.document.size;
            settings.pos  = new Vector2(0f, -margin);
            settings.size = new Vector2(docSize.x - margin * 2f, docSize.y - margin * 2f);
        }

        // Link from the previous message (linear chain)
        if (_lastInstanceID != null)
        {
            FindSettings(_lastInstanceID).links.Add(new DDSSaveClasses.DDSMessageLink
            {
                from            = _lastInstanceID,
                to              = instanceID,
                isDialogSuccess = true
            });
        }
        else
        {
            _tree.startingMessage = instanceID;
        }

        _lastInstanceID = instanceID;
        return this;
    }

    /// <summary>
    /// Finalises the tree, populates messageRef (required — [NonSerialized],
    /// never auto-filled when building at runtime), registers it into
    /// allDDSTrees, and returns its ID for use in MurderLeadItem.vmailThread.
    /// </summary>
    public string Build()
    {
        // CRITICAL: messageRef is [NonSerialized] with no initializer in
        // DDSTreeSave. The game reads it without null checks during city prep.
        // We must always populate it manually when building trees at runtime.
        _tree.messageRef = new Il2CppSystem.Collections.Generic.Dictionary<string, DDSSaveClasses.DDSMessageSettings>();
        foreach (var msg in _tree.messages)
            _tree.messageRef[msg.instanceID] = msg;

        _toolbox.allDDSTrees.Add(_tree.id, _tree);
        return _tree.id;
    }

    // IL2CPP .Find(lambda) workaround — see NeighborKillerMO.cs comment.
    private DDSSaveClasses.DDSMessageSettings FindSettings(string instanceID)
    {
        foreach (var s in _tree.messages)
            if (s.instanceID == instanceID) return s;
        return null;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// LEAD BUILDER — fluent API for building MurderLeadItem entries
//
// Usage:
//   mo.MOleads.Add(LeadBuilder.New("MyLead")
//       .WrittenBy(LeadCitizen.killer)
//       .ReceivedBy(LeadCitizen.victim)
//       .At(LeadSpawnWhere.victimHome)
//       .WithVmail(treeID)
//       .Tag(JobPreset.JobTag.V)
//       .Build());
// ─────────────────────────────────────────────────────────────────────────────

public class LeadBuilder
{
    private readonly MurderPreset.MurderLeadItem _lead = new();

    private LeadBuilder() { }

    /// <summary>
    /// Starts a new lead with sensible defaults:
    /// always spawns, spawn phase = acquireEuipment, compatible with all motives.
    /// </summary>
    public static LeadBuilder New(string name)
    {
        var b = new LeadBuilder();
        b._lead.name                      = name;
        b._lead.chance                    = 1f;
        b._lead.compatibleWithAllMotives  = true;
        b._lead.tryToSpawnWithEachNewMurder = false;
        b._lead.spawnOnPhase              = MurderController.MurderState.acquireEuipment;
        b._lead.security                  = 3;
        b._lead.priority                  = 1;
        return b;
    }

    /// <summary>Override the spawn phase (default: acquireEuipment).</summary>
    public LeadBuilder SpawnOn(MurderController.MurderState phase)
        { _lead.spawnOnPhase = phase; return this; }

    /// <summary>Override spawn chance (default: 1.0 = always).</summary>
    public LeadBuilder Chance(float chance)
        { _lead.chance = chance; return this; }

    /// <summary>Who wrote/sent this evidence.</summary>
    public LeadBuilder WrittenBy(MurderPreset.LeadCitizen citizen)
        { _lead.writer = citizen; return this; }

    /// <summary>Who received this evidence.</summary>
    public LeadBuilder ReceivedBy(MurderPreset.LeadCitizen citizen)
        { _lead.receiver = citizen; return this; }

    /// <summary>
    /// Who this evidence belongs to (ownership, default: victim).
    ///
    /// IMPORTANT: this is separate from At()/LeadSpawnWhere, but they need to
    /// agree. Every lead in this mod that targets victimHome happened to work
    /// without ever calling this, because the default (victim) already matches.
    /// The first time a lead targeted killerHome without also calling
    /// BelongsTo(killer), it spawned at the VICTIM's home instead — confirmed
    /// via live testing, addresses were different buildings so it wasn't a
    /// coincidence. Always set this to match whichever citizen At() is really
    /// about.
    /// </summary>
    public LeadBuilder BelongsTo(MurderPreset.LeadCitizen citizen)
        { _lead.belongsTo = citizen; return this; }

    /// <summary>Where in the world this evidence spawns.</summary>
    public LeadBuilder At(MurderPreset.LeadSpawnWhere where)
        { _lead.where = where; return this; }

    /// <summary>
    /// Attaches a DDS tree (vmail or document) as this lead's content source.
    ///
    /// IMPORTANT: this sets MurderLeadItem.vmailThread, which the engine reads
    /// in TWO independent places: (1) our SpawnItem_DDSOverrideFix_Patch, which
    /// applies it as this lead's physical item text if WithItem() is also set,
    /// and (2) MurderController.SpawnItemsCheck itself, which ALWAYS ALSO calls
    /// NewVmailThread with this tree — generating a real digital vmail message
    /// in the participants' inbox, regardless of whether WithItem() is set.
    ///
    /// Use this for leads that should genuinely exist as a digital vmail (with
    /// or without an accompanying physical item). For a lead that should be
    /// PHYSICAL-ONLY — e.g. a fallback letter for citizens without a computer —
    /// use WithPhysicalText() instead, which does not trigger this side effect.
    ///
    /// progress: how many messages deep into the chain to generate (default 3).
    /// </summary>
    public LeadBuilder WithTree(string treeID, float progress = 3f)
    {
        _lead.vmailThread            = treeID;
        _lead.vmailProgressThreshold = new Vector2(progress, progress);
        return this;
    }

    /// <summary>
    /// Attaches a DDS tree as the readable text for this lead's physical
    /// spawnItem ONLY. Unlike WithTree(), this does NOT set
    /// MurderLeadItem.vmailThread, so the engine will not also generate a
    /// duplicate digital vmail from this lead — the tree is applied purely via
    /// SpawnItem_DDSOverrideFix_Patch after the item spawns.
    ///
    /// Use this for a physical-only fallback lead (e.g. a letter that should
    /// exist and be readable even in a city where neither party owns a
    /// computer). Requires WithItem() and Tag() to also be set on this lead —
    /// Tag() must be called before Build().
    /// </summary>
    public LeadBuilder WithPhysicalText(string treeID)
        { _physicalTextTreeID = treeID; return this; }

    /// <summary>Attaches a physical item to spawn alongside (or instead of) a tree.</summary>
    public LeadBuilder WithItem(InteractablePreset preset)
        { _lead.spawnItem = preset; return this; }

    /// <summary>
    /// Job tag for this lead. Used internally for deduplication and conditional
    /// spawning (useIf/orGroup). Avoid A/C/E/F/G — engine-reserved for resolve
    /// questions (murderer ID, killer home, weapon, fingerprints, den).
    /// </summary>
    public LeadBuilder Tag(JobPreset.JobTag tag)
        { _lead.itemTag = tag; return this; }

    /// <summary>
    /// Only spawn this lead if a previous lead with the given tag already spawned.
    /// Useful for conditional fallback evidence.
    /// </summary>
    public LeadBuilder OnlyIf(JobPreset.JobTag prerequisiteTag)
        { _lead.useIf = true; _lead.ifTag = prerequisiteTag; return this; }

    /// <summary>Security level of this evidence (0 = hidden, 5 = in plain sight).</summary>
    public LeadBuilder Security(int level)
        { _lead.security = level; return this; }

    private string _physicalTextTreeID;

    public MurderPreset.MurderLeadItem Build()
    {
        if (_physicalTextTreeID != null)
        {
            if (_lead.spawnItem == null)
                SoDExpandedPlugin.Logger.LogWarning(
                    $"[{_lead.name}] WithPhysicalText() set but WithItem() is not — " +
                    "the tree has nothing to attach to and will be ignored.");
            PhysicalTextRegistry.Register(_lead.itemTag, _physicalTextTreeID);
        }
        return _lead;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// PHYSICAL TEXT REGISTRY
// Side table for LeadBuilder.WithPhysicalText(): maps a lead's itemTag to the
// DDS tree that SpawnItem_DDSOverrideFix_Patch should apply to its spawned
// item. Kept separate from MurderLeadItem.vmailThread specifically so this
// never triggers the engine's own NewVmailThread side effect.
//
// IMPORTANT: this is a single flat dictionary keyed ONLY by JobTag (A-Z), with
// no per-MO or per-murder scoping. JobTag values must be unique ACROSS every
// killer type in this mod, not just within one killer's own leads — reusing a
// tag two killers both use will silently make one killer's RegisterLeads
// overwrite the other's registration (whichever runs later in
// KillerRegistry.All wins), and the earlier one's item will read the wrong
// text with no error. Found this exact collision once already (Neighbor's
// letter and Fanatic's bible passage both used W) — keep a mental tally:
//   Neighbor: V (vmail), W (letter)
//   Fanatic:  T (bible passage), S (journal), R (scripture), U (ledger)
//   Broker:   D (contract), J (panic vmail), M (ledger, confirmed working
//             after switching off SalesLedger/Murder_SightingLog), K (business
//             card), L (debt notice). H was tried for the ledger first and
//             silently failed (vanilla preset already claims it) -- avoid.
// ─────────────────────────────────────────────────────────────────────────────

public static class PhysicalTextRegistry
{
    private static readonly Dictionary<JobPreset.JobTag, string> _overrides = new();

    public static void Register(JobPreset.JobTag tag, string treeID) => _overrides[tag] = treeID;

    public static bool TryGet(JobPreset.JobTag tag, out string treeID) => _overrides.TryGetValue(tag, out treeID);
}