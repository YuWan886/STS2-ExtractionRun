using System.Linq;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using ExtractionRun.Data;
using ExtractionRun.Lifecycle;
using ExtractionRun.Modifier;

namespace ExtractionRun.Modifier;

/// <summary>
/// Run-lifetime marker + injector for the 搜打撤 (Search-Loot-Extract) game mode. Added to a run's modifiers by the
/// warehouse hub launch flow (never appears in daily/custom modifier lists). Runs on EVERY machine during
/// <c>InitializeNewRun</c> with identical, deterministic input, so the starting loadout matches everywhere.
/// 搜打撤模式的局内标记与注入器。由仓库大厅发起跑局时加入 modifiers（不会出现在每日/自定义列表）。在每台机器上确定性执行。
/// </summary>
public sealed class ExtractionModifier : ModifierModel
{
    /// <summary>Clears the default character deck before <see cref="AfterRunCreated"/> (no cards unless carried).</summary>
    public override bool ClearsPlayerDeck => true;

    protected override void AfterRunCreated(RunState runState)
    {
        // A fresh extraction run invalidates any previous run's settlement result.
        ExtractionSettlement.Clear();

        foreach (Player player in runState.Players)
        {
            CarryConfig config = ExtractionRunData.Carry.Get(player);

            // Remove the character's default relics and potions (deck is already cleared by ClearsPlayerDeck).
            foreach (RelicModel relic in player.Relics.ToList())
            {
                player.RemoveRelicInternal(relic, silent: true);
            }

            foreach (PotionModel? potion in player.PotionSlots.ToList())
            {
                if (potion != null)
                {
                    player.DiscardPotionInternal(potion, silent: true);
                }
            }

            // Gold: carried amount (0 = no starting gold).
            player.Gold = config.Gold;

            // Empty carry: grant the character's starter deck (deck only — no gold/relic/potion). The warehouse hub
            // blocks empty starts, so only MP clients reach this with 0 cards; without a deck they'd be soft-locked.
            // 空携带：发初始牌组兜底（只发牌）。大厅会阻止空开跑，只有 MP 客户端会走到这；没有牌组就是软锁。
            if (config.Cards.Count == 0)
            {
                foreach (CardModel starter in player.Character.StartingDeck)
                {
                    CardModel card = starter.ToMutable();
                    card.FloorAddedToDeck = 1;
                    player.Deck.AddInternal(card, silent: true);
                }

                if (player.Deck.Cards.Count == 0)
                {
                    // Character has no starter deck (e.g. Deprived) — fall back to a generic Basic pool so the run
                    // stays playable. 角色没有初始牌组（如 Deprived）时，用通用基础牌兜底。
                    foreach (CardModel basic in ModelDb.AllCards
                                 .Where(c => c.Rarity == CardRarity.Basic)
                                 .GroupBy(c => c.Id)
                                 .Select(g => g.First())
                                 .Take(10))
                    {
                        CardModel card = basic.ToMutable();
                        card.FloorAddedToDeck = 1;
                        player.Deck.AddInternal(card, silent: true);
                    }
                }

                Entry.Logger.Warn($"ExtractionModifier: player {player.NetId} carried no cards; granted starter deck " +
                                  $"({player.Deck.Cards.Count} cards).");
            }

            // Cards: restore from their serializable form (upgrades/enchantments/props preserved).
            foreach (SerializableCard sc in config.Cards)
            {
                if (sc.Id == null || ModelDb.GetByIdOrNull<CardModel>(sc.Id) == null)
                {
                    Entry.Logger.Warn($"ExtractionModifier skipping card from an unloaded mod: {sc.Id}");
                    continue;
                }

                CardModel card = runState.LoadCard(sc, player);
                player.Deck.AddInternal(card, silent: true);
            }

            // Relics: restore and let FinalizeStartingRelics run AfterObtained for each.
            foreach (SerializableRelic sr in config.Relics)
            {
                if (sr.Id == null || ModelDb.GetByIdOrNull<RelicModel>(sr.Id) == null)
                {
                    Entry.Logger.Warn($"ExtractionModifier skipping relic from an unloaded mod: {sr.Id}");
                    continue;
                }

                RelicModel relic = RelicModel.FromSerializable(sr);
                player.AddRelicInternal(relic, silent: true);
            }

            // Potions: clamp to the player's available potion slots.
            int addedPotions = 0;
            foreach (SerializablePotion sp in config.Potions)
            {
                if (addedPotions >= player.MaxPotionCount)
                {
                    break;
                }

                if (sp.Id == null || ModelDb.GetByIdOrNull<PotionModel>(sp.Id) == null)
                {
                    continue;
                }

                PotionModel potion = PotionModel.FromSerializable(sp);
                player.AddPotionInternal(potion, silent: true);
                addedPotions++;
            }
        }

        // Consume the LOCAL player's carried items from this machine's own warehouse. Each machine only touches its
        // own profile stash; the carried items are gone for good (dying/abandoning loses them).
        // NOTE: we resolve the local player via NetService.NetId instead of LocalContext.GetMe, because LocalContext's
        // NetId is not assigned until RunManager.Launch(), which runs AFTER AfterRunCreated.
        ulong localNetId = RunManager.Instance?.NetService?.NetId ?? 0;
        Player? me = runState.Players.FirstOrDefault(p => p.NetId == localNetId);
        if (me != null)
        {
            CarryConfig myConfig = ExtractionRunData.Carry.Get(me);
            if (!myConfig.IsEmpty)
            {
                WarehouseStore.ConsumeCarried(myConfig);
                Entry.Logger.Info($"ExtractionModifier consumed {myConfig.Cards.Count} cards, " +
                                  $"{myConfig.Relics.Count} relics, {myConfig.Potions.Count} potions, " +
                                  $"{myConfig.Gold} gold from the local warehouse.");
            }

            // The pending carry is a gear-up draft that must not outlive the run that consumed it: leaving it in
            // pending_carry.json would pre-fill the next warehouse visit with items already gone from the stash, and
            // the next launch would re-inject them for free (item duplication). Wipe it now that the run has started.
            // 开跑后清空待发配置：否则下次仓库会预填已消耗的物品，再次开跑等于免费重注入（刷物品）。
            PendingCarryStore.Clear();
        }
    }

    protected override void AfterRunLoaded(RunState runState)
    {
        // Reloading a saved extraction run must NOT re-inject or re-consume — the deck is already in the save and the
        // carried items were consumed when the run first started. Intentional no-op.
    }
}
