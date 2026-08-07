using System;
using System.Linq;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using STS2RitsuLib;
using STS2RitsuLib.Ui.Toast;
using ExtractionRun.Data;
using ExtractionRun.Modifier;
using ExtractionRun.UI;

namespace ExtractionRun.Lifecycle;

/// <summary>
/// Extracts loot at run end. On victory with the local player alive, the final deck (minus cloned cards), relics,
/// potions and gold are deposited into the local player's warehouse. Death, abandonment, or "team won but I died"
/// deposit nothing — the carried items were already consumed at run start.
/// 跑局结束结算：胜利且本地玩家存活时，把最终牌组（排除克隆牌）、遗物、药水、金币存入本机仓库。死亡/放弃/队赢我死均不结算。
/// </summary>
public static class ExtractionRunEnd
{
    public static void Register()
    {
        RitsuLibFramework.SubscribeLifecycle<RunEndedEvent>(OnRunEnded, replayCurrentState: false);
    }

    private static void OnRunEnded(RunEndedEvent evt)
    {
        try
        {
            // Only extraction runs deposit.
            if (evt.Run.Modifiers.All(sm => sm.Id != ModelDb.Modifier<ExtractionModifier>().Id))
            {
                Entry.Logger.Info("ExtractionRunEnd: run has no extraction modifier; nothing to settle.");
                return;
            }

            RunState? state = RunManager.Instance?.State;
            if (state == null)
            {
                return;
            }

            Player? me = LocalContext.GetMe(state);
            if (me == null)
            {
                return;
            }

            // Extraction succeeds only when the run is won AND the local player survived.
            bool success = evt.IsVictory && me.Creature.IsAlive;
            var result = new ExtractionSettlementResult { Success = success };

            if (success)
            {
                System.Collections.Generic.List<SerializableCard> cards = me.Deck.Cards
                    .Where(c => !CloneMarker.ShouldExclude(c))
                    .Select(c => c.ToSerializable())
                    .ToList();
                System.Collections.Generic.List<SerializableRelic> relics = me.Relics
                    .Select(r => r.ToSerializable())
                    .ToList();
                System.Collections.Generic.List<SerializablePotion> potions = me.PotionSlots
                    .Select((p, i) => p?.ToSerializable(i))
                    .OfType<SerializablePotion>()
                    .ToList();
                int gold = me.Gold;

                result.Cards.AddRange(cards);
                result.Relics.AddRange(relics);
                result.Potions.AddRange(potions);
                result.Gold = gold;

                WarehouseStore.Deposit(cards, relics, potions, gold);
                Entry.Logger.Info($"ExtractionRun: extracted {cards.Count} cards, {relics.Count} relics, " +
                                  $"{potions.Count} potions, {gold} gold.");
                RitsuToastService.ShowInfo(ExtractionLocalization.DepositSuccessText());
            }
            else
            {
                // Death / abandon / "team won but I died": the carried loadout was consumed at run start and is lost.
                CarryConfig carried = ExtractionRunData.Carry.Get(me);
                result.Cards.AddRange(carried.Cards);
                result.Relics.AddRange(carried.Relics);
                result.Potions.AddRange(carried.Potions);
                result.Gold = carried.Gold;
                Entry.Logger.Info($"ExtractionRun: no deposit (victory={evt.IsVictory}, alive={me.Creature.IsAlive}). " +
                                  "Carried items were consumed at run start.");
            }

            ExtractionSettlement.Current = result;
        }
        catch (Exception ex)
        {
            Entry.Logger.Error($"ExtractionRunEnd: deposit failed: {ex}");
        }
    }
}
