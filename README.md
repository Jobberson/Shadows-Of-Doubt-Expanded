<h1 align=center>Shadows of Doubt Expanded Mod</h1>

Adds three new serial killer archetypes to **Shadows of Doubt**, each with its own
victim/murderer selection logic, weapon pool, moniker, and a full set of
hand-written evidence.

This is a v1 release. More killer types, tuning, and the deferred features
listed below are planned for future updates.

## New killers

### The Neighbor

A neighbor got so angry that they couldn't cold the grudge back. These people act on impulse, some don't even regret it.

### The Angel

They're convinced they're an angel sent by God to watch the sinful and cleanse the world from them.

### The Closer

Don't let their charm fool you, they're opportunistic and very smart in what they do. They're greedy and want money above anything.

## Requirements

- [BepInEx IL2CPP pack](https://thunderstore.io/c/shadows-of-doubt/p/BepInEx/BepInExPack_IL2CPP/) 6.0.0+
- [SOD.Common](https://thunderstore.io/c/shadows-of-doubt/p/Venomaus/SODCommon/) 2.1.4+
- [AssetBundleLoader](https://thunderstore.io/c/shadows-of-doubt/p/Piepieonline/AssetBundleLoader/) 2.0.3+
- [DDSLoader](https://thunderstore.io/c/shadows-of-doubt/p/Piepieonline/DDSLoader/) 1.0.9+

All four are pulled in automatically by any mod manager (r2modman, Thunderstore
Mod Manager, Overwolf) — you don't need to install them separately.

## Installation

**Mod manager (recommended):** search for "ShadowsOfDoubtExpanded" and click
install — dependencies are handled for you.

**Manual:** extract this package into your `BepInEx/plugins/` folder, making
sure `ShadowsOfDoubtExpanded.dll` ends up somewhere under `BepInEx/plugins`
(a subfolder is fine). Make sure BepInEx, SOD.Common, AssetBundleLoader, and
DDSLoader are installed first.

## Compatibility notes

- Adds itself alongside vanilla killer types — it doesn't remove or replace
  any existing case types, so it should be compatible with most other
  content mods.
- Should be safe to add to an existing save. Removing it mid-save isn't
  tested; a killer type disappearing mid-investigation could leave an
  orphaned case, so finish or abandon any active case involving these
  killers before uninstalling.

## Known limitations (planned for later updates)

- Some evidence item placements rely on a generic letter/note prop rather
  than a bespoke asset, since not every "ideal" prop (e.g. a dedicated
  ledger or diary mesh) reliably spawns inside a residential apartment.
  This affects flavor only, not functionality.

## Update Plans

- More killer archetypes with different ways of investigating.
- New props/items/evidences/leads/weapons.
- New types of cases other than murder and kidnapping.

## Credits

Built with [SOD.Common](https://github.com/Ven0maus/SOD.Common) by Venomaus,
[AssetBundleLoader](https://thunderstore.io/c/shadows-of-doubt/p/Piepieonline/AssetBundleLoader/)
and [DDSLoader](https://thunderstore.io/c/shadows-of-doubt/p/Piepieonline/DDSLoader/)
by Piepieonline, and [BepInEx](https://thunderstore.io/c/shadows-of-doubt/p/BepInEx/BepInExPack_IL2CPP/).

See [CHANGELOG.md](CHANGELOG.md) for version history.
