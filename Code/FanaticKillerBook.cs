using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace ShadowsOfDoubtExpanded;

// ─────────────────────────────────────────────────────────────────────────────
// AMBIENT WORLD CONTENT — a real, ownable book
//
// Separate from FanaticKillerDefinition's killerHome leads (scripture/ledger),
// which are guaranteed evidence tied to a specific murder. This is a genuine
// BookPreset asset that plugs into the game's own book-ownership system —
// it can turn up on any citizen's shelf, not just the killer's, weighted via
// pickRules toward citizens who share his trait. Pure atmosphere/world
// texture, not a lead.
//
// Registration mirrors KillerMODefinition.Register exactly: create via
// ScriptableObject.CreateInstance, borrow visual data (mesh/material) from an
// existing donor so no new art is needed, then hand it to
// toolbox.ProcessLoadedScriptableObject. That call already works for MurderMO
// in this mod (that's how both killer types get registered) and isn't
// MurderMO-specific, so it should register any ScriptableObject type the same
// way — but this is the first time we've used it for something other than
// MurderMO, so worth confirming in the log that it actually shows up.
//
// UNVERIFIED: ddsMessage's expected format. Guessing it wants the same kind
// of document/tree ID DDSBuilder.Document() produces (used everywhere else in
// this mod for multi-line readable text), rather than a bare message ID like
// BuildSimpleDDSMessage produces for the moniker/graffiti. If the book spawns
// with blank or garbled text, this field is the first thing to check.
// ─────────────────────────────────────────────────────────────────────────────

[HarmonyPatch(typeof(Toolbox), "LoadAll")]
public static class ToolboxLoadAll_FanaticBook_Patch
{
    // Runs after ToolboxLoadAll_AllKillers_Patch (KillerFramework.cs), which
    // is what actually creates the first document-type DDS trees (bible
    // passage, journal, etc.) — this book's own DDSBuilder.Document() call
    // needs one of those to exist already to borrow a page style from.
    // Confirmed via log: without this, "No existing document-type DDS tree
    // found to borrow page style from" fired, because this patch was running
    // before KillerRegistry.RegisterAll had created anything to borrow from.
    [HarmonyPriority(Priority.Low)]
    [HarmonyPostfix]
    public static void Postfix(Toolbox __instance)
    {
        FanaticBibleBook.Register(__instance);
    }
}

public static class FanaticBibleBook
{
    public const string BOOK_NAME = "SoDExpanded_FanaticKiller_BibleBook";

    // CONFIRMED (from live log output) — was "Char-Devout" (a guess) before.
    private const string FANATIC_TRAIT = "Principle-Religious";

    public static void Register(Toolbox toolbox)
    {
        var bookType = Il2CppType.Of<BookPreset>();
        if (!toolbox.resourcesCache.ContainsKey(bookType))
        {
            SoDExpandedPlugin.Logger.LogWarning($"[{BOOK_NAME}] BookPreset cache missing — skipping.");
            return;
        }

        var cache = toolbox.resourcesCache[bookType];
        if (cache.ContainsKey(BOOK_NAME)) return; // idempotent across city loads

        // Prefer a donor that already looks the part, rather than whichever
        // book happens to iterate first in the cache. Kolob is real LDS
        // cosmological terminology (a celestial body near the divine throne),
        // so KolobGrieving is a good bet for something already modeled to
        // look like a serious/mystical tome rather than a paperback. Falls
        // through to other plausible-sounding titles, then to "any book with
        // a mesh" if none of these exist — this list came from Snog checking
        // the actual game's book names, but which MESH each one uses is still
        // a guess, worth eyeballing in-game.
        string[] preferredDonorNames = { "KolobGrieving", "HowIAscended", "GoddessInTheWaitingRoom",
                                          "TheCandorBookOfAncientHistory", "CompendiumOfDiseases" };

        BookPreset donor = null;
        foreach (string donorName in preferredDonorNames)
        {
            if (cache.ContainsKey(donorName))
            {
                var candidate = cache[donorName].TryCast<BookPreset>();
                if (candidate != null && candidate.bookMesh != null)
                {
                    donor = candidate;
                    break;
                }
            }
        }

        if (donor == null)
        {
            // Fallback: any book with a mesh, same as before.
            foreach (var pair in cache)
            {
                var candidate = pair.Value.TryCast<BookPreset>();
                if (candidate != null && candidate.bookMesh != null)
                {
                    donor = candidate;
                    break;
                }
            }
        }
        if (donor == null)
        {
            SoDExpandedPlugin.Logger.LogWarning(
                $"[{BOOK_NAME}] No donor BookPreset with a mesh found — skipping registration.");
            return;
        }

        var book = ScriptableObject.CreateInstance<BookPreset>();
        book.name       = BOOK_NAME;
        book.bookName   = "Testimony of the Watchful";
        book.author     = "Unknown";
        book.genre      = new Il2CppSystem.Collections.Generic.List<BookPreset.BookGenre>();
        book.genre.Add(BookPreset.BookGenre.esoteric);
        book.isSeries   = false;
        book.common     = 0.2f;   // deliberately rare — this shouldn't be everywhere
        book.baseChance = 0.05f;
        book.spawnRule  = BookPreset.SpawnRules.onlyAtHome;
        book.bookMesh     = donor.bookMesh;
        book.bookMaterial = donor.bookMaterial;

        // BookPreset.cs shows pickRules with a default initializer, but that's
        // not guaranteed to run via ScriptableObject.CreateInstance under
        // IL2CPP interop — same reasoning as genre above. Set explicitly
        // rather than assume, so .Add() below can't NRE.
        book.pickRules = new Il2CppSystem.Collections.Generic.List<CharacterTrait.TraitPickRule>();

        var traitCache = toolbox.resourcesCache[Il2CppType.Of<CharacterTrait>()];
        if (traitCache.ContainsKey(FANATIC_TRAIT))
        {
            var trait = traitCache[FANATIC_TRAIT].TryCast<CharacterTrait>();
            var pickRule = new CharacterTrait.TraitPickRule();
            pickRule.rule                   = CharacterTrait.RuleType.ifAnyOfThese;
            pickRule.mustPassForApplication = false;
            pickRule.baseChance             = 0.6f; // much likelier to own this if devout
            pickRule.reasonChance           = 1; // UNVERIFIED meaning — mirroring baseChance for now
            pickRule.traitList.Add(trait);
            book.pickRules.Add(pickRule);
        }
        else
        {
            SoDExpandedPlugin.Logger.LogWarning(
                $"[{BOOK_NAME}] Trait '{FANATIC_TRAIT}' not found — book registers with no " +
                "trait weighting, will spawn at its flat base rarity only.");
        }

        // Original in-universe scripture, not a real religious text — this is
        // a fictional book in a fictional city, and it's easier to make it
        // sound exactly as unsettling as we want without borrowing real verses.
        book.ddsMessage = DDSBuilder.Document(toolbox)
            .Named("SoDExpanded_FanaticKiller_BibleBook_Text")
            .Message(0, 1,
                "They will tell you the eyes are a metaphor. They are not. Eight watch " +
                "always: two for what is done, two for what is hidden, two for what is " +
                "planned, and two for what is only felt. None of the eight ever sleep, " +
                "and none of them forgive. To be seen by all eight at once is to be judged " +
                "already; what comes after is only paperwork.",
                "FanaticKiller_BibleBook_Body",
                handwriting: false)
            .Build();

        toolbox.ProcessLoadedScriptableObject(book);
        SoDExpandedPlugin.Logger.LogInfo(
            $"[SoDExpanded] Registered book '{BOOK_NAME}' (mesh borrowed from '{donor.name}') " +
            $"({book.pickRules.Count} pick rule(s)).");
    }
}