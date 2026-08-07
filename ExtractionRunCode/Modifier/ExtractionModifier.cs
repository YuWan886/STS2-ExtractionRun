using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using ExtractionRun.Data;
using ExtractionRun.Lifecycle;

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
    protected override string IconPath => "res://ExtractionRun/images/modifiers/extraction.png";

    protected override void AfterRunCreated(RunState runState)
    {
        ExtractionSettlement.Clear();

        foreach (Player player in runState.Players)
        {
            CarryConfig config = ExtractionRunData.Carry.Get(player);

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

            player.Gold = config.Gold;

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

            PendingCarryStore.Clear();
        }
    }

    protected override void AfterRunLoaded(RunState runState)
    {
        // Reloading a saved extraction run must NOT re-inject or re-consume — the deck is already in the save and the
        // carried items were consumed when the run first started. Intentional no-op.
    }
}
