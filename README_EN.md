# Search-Loot-Extract · STS2-ExtractionRun

Languages: [中文](README.md) | English

A new game mode for *Slay the Spire 2* —  Search-Loot-Extract Mode. Built on the [RitsuLib](https://github.com/BAKAOLC/STS2-RitsuLib) framework.

> Gear up from your warehouse before you climb. Clear the run and the loot comes home with you. Fall along the way and it stays there — forever.

---

## How it works

Every save slot has its own persistent **warehouse**. Before a run, open the Search-Loot-Extract entry on the main menu to reach the warehouse hub:

1. Pick cards (≤ 10), relics (≤ 3), potions and gold from your warehouse to form a **carry loadout**.
2. Start the run. The carried deck replaces the default starting deck, and the carried items are **consumed from the warehouse**.
3. **Winning = successful extraction**: your final deck, relics, potions and gold are all deposited back into the warehouse.
4. **Dying or abandoning = failed extraction**: the loadout you carried in is lost.

On first use, the warehouse is seeded with all Basic and Common cards, all Starter and Common relics, and 1000 gold to get you started.

> Items are **normalized to base state** when deposited (upgrades, enchantments, props stripped) — the warehouse only ever holds plain cards.

## Features

- **Persistent warehouse**: independent per-save storage that accumulates loot across runs (gold capped at 9,999,999).
- **Carry system**: freely assemble a loadout before each run; capacity is configurable (default 10 cards / 3 relics).
- **Search-Loot-Extract loop**: winner takes all, loser loses everything — death or abandon forfeits every carried item.
- **Warehouse hub**: three tabs (cards / relics / potions), each with its own search box and multi-select filters (source pool, rarity, type, cost), a virtualized grid that stays smooth with large warehouses, and background art preloading.
- **Extraction report**: an "Extraction Report" button on the game-over screen shows exactly what was deposited or lost.
- **Singleplayer & multiplayer**: both work; in MP, each player's loadout is settled independently per their own settings.
- **Settings page**: sliders for max carried cards (0–20) and max carried relics (0–6), plus a one-click reset.

## Settings

- **Max carried cards**: 0–20, default 10.
- **Max carried relics**: 0–6, default 3.
- **Reset to defaults**: restores every setting.

Settings are stored at global scope, shared across all saves.

## Debug Console Commands

The in-game developer console provides these Search-Loot-Extract commands (local-only, independent of the settings page):

```
extraction reset                                         # reset the warehouse (confirmation dialog)
extraction add <card|relic|potion|gold> <id|amount> [count]
extraction remove <card|relic|potion|gold> <id|amount> [count]
```

- **`extraction reset`**: wipes the warehouse and re-grants the starting items. Shows a confirmation dialog first; **unavailable while a run is in progress or a character-select lobby is open**.
- **`extraction add`**: adds items to the warehouse. `card` / `relic` / `potion` are addressed by model ID (SCREAMING_SNAKE, e.g. `STRIKE`, with Tab completion); `gold` takes an amount directly. `[count]` defaults to 1 and is capped at 999. Adds go through the normal deposit path, so items are normalized to base state.
- **`extraction remove`**: removes items or gold from the warehouse. Same addressing; `[count]` defaults to 1 and is capped at 999. **Unavailable while a run is in progress or a character-select lobby is open**; removes also strip the matching items from any pending carry, preventing free-item dupes.