# Search-Loot-Extract · STS2-ExtractionRun

Languages: [中文](README.md) | English

A new game mode for *Slay the Spire 2* —  Search-Loot-Extract Mode. Built on the [RitsuLib](https://github.com/BAKAOLC/STS2-RitsuLib) framework.

> Gear up from your warehouse before you climb. Clear the run and the loot comes home with you. Fall along the way and it stays there — forever.

---

## How it works

Every save slot has its own persistent **warehouse**. Before a run, open the Search-Loot-Extract entry on the main menu to reach the warehouse hub:

1. Pick cards, relics, potions and gold from your warehouse to form a **carry loadout** — cards and relics share a **backpack capacity** (default 15 slots, see "Backpack capacity" below).
2. Start the run. The carried deck replaces the default starting deck, and the carried items are **consumed from the warehouse**.
3. **Winning = successful extraction**: your final deck, relics, potions and gold are all deposited back into the warehouse.
4. **Dying or abandoning = failed extraction**: the loadout you carried in is lost.

On first use, the warehouse is seeded with all Basic and Common cards, all Starter and Common relics, and 1000 gold to get you started.

> Items are **normalized to base state** when deposited (upgrades, enchantments, props stripped) — the warehouse only ever holds plain cards.

## Features

- **Persistent warehouse**: independent per-save storage that accumulates loot across runs (gold capped at 9,999,999).
- **Carry system**: freely assemble a loadout before each run; cards and relics share a **backpack capacity** (default 15 slots).
- **Backpack capacity**: cards take slots by rarity (starter/common 1, uncommon 2, rare 3, ancient 4, other 2) and relics take 2, all sharing one backpack (default 15 slots). The carry panel shows a "Backpack X/15" bar at the top; once the pool is full, no more items can be added. An over-capacity carry is auto-trimmed (heaviest items dropped first) when the hub opens or the run starts, and gear-code imports are clamped to the remaining slots. Toggle and tune it in Settings.
- **Search-Loot-Extract loop**: winner takes all, loser loses everything — death or abandon forfeits every carried item.
- **Durability**: every card and relic copy carries durability. Each successful extraction decrements the carried-and-returned copies by 1; a copy reaching 0 breaks and is not deposited. Death or abandon loses the loadout outright (consumed at run start — durability never decrements on a loss). Tiles show the group's lowest remaining durability (amber "Durability n" badge); 0 shows a red "Broken" badge. Can be disabled entirely (see Settings).
- **Clear reward**: every victory grants the character's full starting deck and starting relics, keeping your baseline kit renewable for future runs.
- **Warehouse hub**: three tabs (cards / relics / potions), each with its own search box and multi-select filters (source pool, rarity, type, cost), a virtualized grid that stays smooth with large warehouses, and background art preloading.
- **Hub shop**: the "Shop" button at the bottom-right of the warehouse hub opens a two-tab shop (Buy / Sell). Stock re-rolls once per real calendar day, or on demand for a fee — see the Shop section below.
- **Extraction report**: an "Extraction Report" button on the game-over screen shows exactly what was deposited or lost.
- **Run seed**: set a custom run seed in the hub's footer before starting — the input is canonicalized live (uppercase, O→0, I→1) with a clear button; blank = random, matching the base game's custom-run field. The seed is a session-only, host-owned run parameter, never persisted with the carry; in MP the whole party follows the host's seed.
- **Singleplayer & multiplayer**: both work; in MP, each player's loadout is settled independently per their own settings.
- **Settings page**: sliders for max carried cards (0–20) and max carried relics (0–6), plus a one-click reset.

## Shop

The "Shop" button at the bottom-right of the warehouse hub opens the shop (ESC or the bottom-left "Warehouse" button returns). It shares the same live data as the hub — gold, stock and the carry draft — with the current warehouse gold always shown top-right. Two main tabs:

- **Buy**: three stacked sections (cards / relics / potions). The stock re-rolls in full on the first open of each real calendar day; prices are rolled from the vanilla base price with variance (±5% cards/potions, ±15% relics) and **frozen for the day**. Buy price = the day's frozen price × the buy-multiplier setting (default ×2.0), and bought items are deposited at full durability. Sold-out slots stay empty until the next refresh. You can also re-roll the whole stock for a fee (50 → +50 → cap 250, resets daily).
- **Sell**: shows the available "warehouse − carry" items with per-copy multi-select (left-click picks one copy, right-click removes one, Shift selects/deselects the whole group), plus search and multi-select filters (source pool, rarity, type, cost, durability). Sell value = the deterministic vanilla base price × the sell-ratio setting (default 50%) × a durability factor (durability / rarity max, floored, minimum 1 gold; potions and no-durability mode factor 1) — the more worn a copy, the less it's worth, and the most worn copies are sold first.

The shop and the hub are mutually exclusive: opening the shop hides the hub, closing it restores and refreshes the hub so gold and stock always stay consistent.

## Settings

- **Backpack capacity**: master toggle, default on. When on, the carry is limited by a shared **backpack slot pool** instead of the max carried cards/relics below; when off, the per-kind count caps apply. Toggling needs a confirmation dialog and is blocked while a run or a character-select lobby is active (same as the durability toggle).
- **Backpack slots**: total slots in the shared backpack, a 1–30 slider, default 15.
- **Slot weights**: how many slots each card rarity and each relic takes (sliders, min 1) — starter/common 1, uncommon 2, rare 3, ancient 4, other 2 (event, token, status, curse, quest and mod cards), relic 2.
- **Max carried cards**: 0–20, default 10 (the per-kind cap used when capacity is off).
- **Max carried relics**: 0–6, default 3 (the per-kind cap used when capacity is off).
- **Hover tooltips**: whether hovering card / relic / potion tiles shows the vanilla tooltip (default on).
- **Durability**: master toggle, default on. Disabling switches the warehouse to a disposable no-durability copy (copies never decrement, nothing is shown); re-enabling returns to the previously frozen durability warehouse, discarding any no-durability progress.
- **Durability caps**: the max durability granted to newly deposited items (new deposits only — existing copies are never retroactively changed), each a 1–20 slider — starter 5 / common 4 / uncommon 3 / rare 2 / ancient 1 / other 1 (event, token, status, curse, quest and mod cards) / relic 3.
- **Shop buy multiplier**: buy price = the day's frozen price × this, a ×1.0–×5.0 slider, default ×2.0.
- **Shop sell ratio**: sell value = the vanilla base price × this (before the durability factor), a 10%–100% slider, default 50%.
- **Reset to defaults**: restores every setting.

Toggling durability requires a confirmation dialog and is blocked while a run or a character-select lobby is active (the carry is already staged and can't be retracted). Settings are stored at global scope, shared across all saves.

## Compatibility

- **Hextech Runes mod**: the first-time warehouse seed does **not** grant that mod's relics.
- **STS2-Game-Lobby**: the lobby's create-room dialog gains a Search-Loot-Extract room type, and the room shows a Search-Loot-Extract pill in the room list; both the host and joining clients are forced to configure their carry first, then run the full extraction inject/consume/deposit loop. Patched from this mod's side only via reflection — no compile-time dependency, and zero side effects when the lobby mod is not installed.

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