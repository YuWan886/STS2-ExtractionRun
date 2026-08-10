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
/// the character's full starting deck + starting relics as the clear reward (granted on every clear). Defeat or
/// abandonment deposit nothing — the carried items were already consumed at run start.
/// When durability is ON, the deposit runs the per-id carried matching against the persisted carry config: carried
/// copies come back at (their pre-run durability − 1), copies at 1 break (战损, not deposited), and every other deck
/// copy (rewards, purchases) is new at full durability. No instance stamps — the persisted carry config survives a
/// mid-run save/reload, so a reloaded run still decrements the correct copies instead of returning them at full
/// durability (which would be a free durability repair).
/// 跑局结束结算：胜利时（队伍胜利即算个人胜利，本地玩家阵亡也算）把最终牌组（排除克隆牌）、遗物、药水、金币存入本机仓库，
/// 并发放该角色整套初始牌组+初始遗物作为通关奖励（每次通关都发）。失败/放弃不结算——携带物已在开跑时消耗。
/// 耐久开启时按 id 封顶匹配持久化携带配置：携带副本以（局前耐久 − 1）带回，1 耐久副本战损（不入库），其余牌组副本（奖励/
/// 购买）按满耐久新货。不做实例盖章——持久化携带配置能活过局中存档/续玩，续玩的跑局仍递减正确副本，而非按满耐久带回
/// （那会是免费修耐久）。
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
            CarriedPickupQueue.Reset();

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
                SettleSuccess(result, me);
            }
            else
            {
                SettleFailure(result, me);
            }

            ExtractionSettlement.Current = result;
        }
        catch (Exception ex)
        {
            Entry.Logger.Error($"ExtractionRunEnd: deposit failed: {ex}");
        }
    }

    private static void SettleSuccess(ExtractionSettlementResult result, Player me)
    {
        CarryConfig carried = ExtractionRunData.Carry.Get(me);
        WarehouseStore.BackfillCarryDurability(carried);
        bool durability = WarehouseStore.IsDurabilityEnabled;

        // Per-id carried durability (order preserved): the deposit matches the first carriedCount deck copies of each
        // id against these values, decrementing each; the rest of the deck is new at full durability. 各 id 的携带耐久
        // 队列（保序）：结算把每 id 前 carriedCount 份牌组副本对齐这些值逐个递减，其余副本按满耐久新货。
        var carriedCardDur = new Dictionary<ModelId, List<int>>();
        foreach (WarehouseCard wc in carried.Cards)
        {
            if (wc.Card.Id is ModelId cardId)
            {
                if (!carriedCardDur.TryGetValue(cardId, out List<int>? durs))
                {
                    carriedCardDur[cardId] = durs = new List<int>();
                }

                durs.Add(wc.Durability);
            }
        }

        var carriedRelicDur = new Dictionary<ModelId, List<int>>();
        foreach (WarehouseRelic wr in carried.Relics)
        {
            if (wr.Relic.Id is ModelId relicId)
            {
                if (!carriedRelicDur.TryGetValue(relicId, out List<int>? durs))
                {
                    carriedRelicDur[relicId] = durs = new List<int>();
                }

                durs.Add(wr.Durability);
            }
        }

        var cards = new List<WarehouseCard>();
        var brokenCards = new List<WarehouseCard>();
        foreach (CardModel c in me.Deck.Cards)
        {
            if (CloneMarker.ShouldExclude(c))
            {
                continue;
            }

            SerializableCard sc;
            try
            {
                sc = c.ToSerializable();
            }
            catch (Exception ex)
            {
                Entry.Logger.Warn($"ExtractionRunEnd: skipping un-serializable card {c.Id}: {ex.Message}");
                continue;
            }

            int newDur = NextDurability(carriedCardDur, sc.Id, durability, WarehouseStore.MaxDurabilityForCard(sc.Id));
            if (newDur <= 0)
            {
                brokenCards.Add(new WarehouseCard { Card = sc, Durability = 0 });
            }
            else
            {
                cards.Add(new WarehouseCard { Card = sc, Durability = newDur });
            }
        }

        var relics = new List<WarehouseRelic>();
        var brokenRelics = new List<WarehouseRelic>();
        foreach (RelicModel r in me.Relics)
        {
            SerializableRelic sr;
            try
            {
                sr = r.ToSerializable();
            }
            catch (Exception ex)
            {
                Entry.Logger.Warn($"ExtractionRunEnd: skipping un-serializable relic {r.Id}: {ex.Message}");
                continue;
            }

            if (IsExpiredRelic(r))
            {
                result.ExpiredRelics.Add(sr);
                continue;
            }

            int newDur = NextDurability(carriedRelicDur, sr.Id, durability, WarehouseStore.MaxDurabilityForRelic());
            if (newDur <= 0)
            {
                brokenRelics.Add(new WarehouseRelic { Relic = sr, Durability = 0 });
            }
            else
            {
                relics.Add(new WarehouseRelic { Relic = sr, Durability = newDur });
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
        result.BrokenCards.AddRange(brokenCards);
        result.BrokenRelics.AddRange(brokenRelics);
        result.Potions.AddRange(potions);
        result.Gold = gold;

        WarehouseStore.Deposit(cards, relics, potions, gold);
        (List<WarehouseCard> rewardCards, List<WarehouseRelic> rewardRelics) =
            WarehouseStore.GrantCharacterCompletionReward(me.Character);
        result.Cards.AddRange(rewardCards);
        result.Relics.AddRange(rewardRelics);
        Entry.Logger.Info($"ExtractionRun: extracted {cards.Count} cards, {relics.Count} relics, " +
                          $"{potions.Count} potions, {gold} gold; " +
                          $"broke {brokenCards.Count} card(s) and {brokenRelics.Count} relic(s); " +
                          $"dropped {result.ExpiredRelics.Count} spent relic(s).");
        RitsuToastService.ShowInfo(ExtractionLocalization.DepositSuccessText());
    }

    /// <summary>
    /// The next durability for one deposited copy: if it matches a carried copy (id matched, capped at the carried
    /// count), the carried value minus one — a copy at 1 breaks (≤0). Otherwise the copy is new: full durability by
    /// rarity when durability is ON, or full when OFF (the whole decrement is skipped). Matching is per-id and
    /// order-preserving: the first carriedCount copies of an id consume the carried values, so a carried copy removed
    /// mid-run and replaced by a same-id reward mis-decrements that reward by one (accepted edge of the capped scheme).
    /// 单份入库副本的下一耐久：命中携带副本（按 id 封顶匹配）→ 携带值 − 1（1 耐久副本 → ≤0 战损）；否则为新货 → 满耐久
    /// （耐久 ON 按稀有度，OFF 也满——OFF 整段不递减）。按 id 保序匹配：每 id 前 carriedCount 份消耗携带值，故携带牌中途被
    /// 移除、又同名奖励补进时，会误把该奖励递减一次（封顶方案的已知边角）。
    /// </summary>
    private static int NextDurability(Dictionary<ModelId, List<int>> carriedByKind, ModelId? id, bool durability,
        int fullDurability)
    {
        if (durability && id != null && carriedByKind.TryGetValue(id, out List<int>? durs) && durs.Count > 0)
        {
            int d = durs[0];
            durs.RemoveAt(0);
            return d - 1;
        }

        return fullDurability;
    }

    private static void SettleFailure(ExtractionSettlementResult result, Player me)
    {
        CarryConfig carried = ExtractionRunData.Carry.Get(me);
        WarehouseStore.BackfillCarryDurability(carried);
        result.Cards.AddRange(carried.Cards);
        result.Relics.AddRange(carried.Relics);
        result.Potions.AddRange(carried.Potions);
        result.Gold = carried.Gold;
        Entry.Logger.Info($"ExtractionRun: no deposit (victory=false, alive={me.Creature.IsAlive}). " +
                          "Carried items were consumed at run start.");
    }

    /// <summary>
    /// A relic is expired (dropped, not carried out) if it's a limited-use relic whose uses are all spent
    /// (<c>IsUsedUp</c>), or a melted wax relic (<c>IsMelted</c>). An unreadable expiry counts as NOT expired —
    /// a valid relic is never dropped because the check threw. Checked before durability: an expired carried relic is
    /// dropped as expired, never decremented and never 战损.
    /// 失效判定：次数用尽的有限次遗物（IsUsedUp），或融化的蜡质遗物（IsMelted）。判定抛异常按有效处理——绝不因判断失败丢掉有效遗物。
    /// 在耐久之前判断：失效的携带遗物按失效丢弃，不递减、也不进战损。
    /// </summary>
    private static bool IsExpiredRelic(RelicModel r)
    {
        try
        {
            return r.IsUsedUp || r.IsMelted;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
