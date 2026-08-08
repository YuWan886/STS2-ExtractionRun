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
/// Extracts loot at run end. On victory — a TEAM victory counts as a personal victory even if the local player died —
/// the final deck (minus cloned cards), relics, potions and gold are deposited into the local player's warehouse, plus
/// the character's full starting deck + starting relics as the clear reward (granted on every clear).
/// Defeat or abandonment deposit nothing — the carried items were already consumed at run start.
/// 跑局结束结算：胜利时（队伍胜利即算个人胜利，本地玩家阵亡也算）把最终牌组（排除克隆牌）、遗物、药水、金币存入本机仓库，
/// 并发放该角色整套初始牌组+初始遗物作为通关奖励（每次通关都发）。失败/放弃不结算——携带物已在开跑时消耗。
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

            bool success = evt.IsVictory;
            var result = new ExtractionSettlementResult { Success = success };

            if (success)
            {
                var cards = new List<SerializableCard>();
                foreach (CardModel c in me.Deck.Cards)
                {
                    if (CloneMarker.ShouldExclude(c))
                    {
                        continue;
                    }

                    try
                    {
                        cards.Add(c.ToSerializable());
                    }
                    catch (Exception ex)
                    {
                        Entry.Logger.Warn($"ExtractionRunEnd: skipping un-serializable card {c.Id}: {ex.Message}");
                    }
                }

                var relics = new List<SerializableRelic>();
                foreach (RelicModel r in me.Relics)
                {
                    try
                    {
                        relics.Add(r.ToSerializable());
                    }
                    catch (Exception ex)
                    {
                        Entry.Logger.Warn($"ExtractionRunEnd: skipping un-serializable relic {r.Id}: {ex.Message}");
                    }
                }

                var potions = new List<SerializablePotion>();
                int slot = 0;
                foreach (PotionModel? p in me.PotionSlots)
                {
                    if (p != null)
                    {
                        try
                        {
                            potions.Add(p.ToSerializable(slot));
                        }
                        catch (Exception ex)
                        {
                            Entry.Logger.Warn($"ExtractionRunEnd: skipping un-serializable potion {p.Id}: {ex.Message}");
                        }
                    }

                    slot++;
                }

                int gold = me.Gold;

                result.Cards.AddRange(cards);
                result.Relics.AddRange(relics);
                result.Potions.AddRange(potions);
                result.Gold = gold;

                WarehouseStore.Deposit(cards, relics, potions, gold);
                (List<SerializableCard> rewardCards, List<SerializableRelic> rewardRelics) =
                    WarehouseStore.GrantCharacterCompletionReward(me.Character);
                result.Cards.AddRange(rewardCards);
                result.Relics.AddRange(rewardRelics);
                Entry.Logger.Info($"ExtractionRun: extracted {cards.Count} cards, {relics.Count} relics, " +
                                  $"{potions.Count} potions, {gold} gold.");
                RitsuToastService.ShowInfo(ExtractionLocalization.DepositSuccessText());
            }
            else
            {
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
