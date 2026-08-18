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

            ExtractionModifier? modifier = state.Modifiers.OfType<ExtractionModifier>().FirstOrDefault();
            IReadOnlyList<string> challengeIds = modifier?.ActiveChallengeIds ?? Array.Empty<string>();
            ChallengeRuntime challenges = modifier?.Challenges ?? ChallengeRuntime.FromIds(Array.Empty<string>());

            bool success = evt.IsVictory;
            var result = new ExtractionSettlementResult();

            if (success)
            {
                result.Kind = ExtractionSettlementKind.Victory;
                result.Success = true;
                SettleSuccess(result, me, challenges, challengeIds);
            }
            else if (ExtractionPointFlow.IsExtractionChosen)
            {
                // The party extracted at the 撤离点: the run ended as a defeat (the forced kill), but the recorded
                // selection IS deposited. This must be checked before the plain-defeat branch.
                // 撤离点撤离：跑局以失败结束（强制击杀），但记录的选择正常入仓。必须排在普通失败分支之前。
                result.Kind = ExtractionSettlementKind.ExtractionPoint;
                result.Success = true;
                SettleExtractionPoint(result, me);
            }
            else
            {
                result.Kind = ExtractionSettlementKind.Defeat;
                SettleFailure(result, me);
            }

            ExtractionSettlement.Current = result;
        }
        catch (Exception ex)
        {
            Entry.Logger.Error($"ExtractionRunEnd: deposit failed: {ex}");
        }
    }

    private static void SettleSuccess(ExtractionSettlementResult result, Player me,
        ChallengeRuntime challenges, IReadOnlyList<string> challengeIds)
    {
        Entry.Logger.Info($"SettleSuccess: challengeIds=[{string.Join(", ", challengeIds)}]");
        CarryConfig carried = ExtractionRunData.Carry.Get(me);
        WarehouseStore.BackfillCarryDurability(carried);
        bool durability = WarehouseStore.IsDurabilityEnabled;
        (Dictionary<ModelId, List<int>> carriedCardDur, Dictionary<ModelId, List<int>> carriedRelicDur) =
            BuildCarriedDurability(carried);

        var cards = new List<WarehouseCard>();
        var brokenCards = new List<WarehouseCard>();
        var returnedCardCounts = new Dictionary<ModelId, int>();
        CollectCards(me.Deck.Cards, carriedCardDur, durability, cards, brokenCards, returnedCardCounts);

        var relics = new List<WarehouseRelic>();
        var brokenRelics = new List<WarehouseRelic>();
        var returnedRelicCounts = new Dictionary<ModelId, int>();
        CollectRelics(me.Relics, carriedRelicDur, durability, result, relics, brokenRelics, returnedRelicCounts);

        var potions = new List<SerializablePotion>();
        CollectPotions(me, potions);

        int gold = me.Gold;

        // HP_ONE reward: every healthy returned carried copy is duplicated at full durability (a broken one is not —
        // 碎 = 真碎) and the deposited gold doubles. 翻倍奖励：每份健康带回的携带副本补一张满耐久副本（战损不补），金币翻倍。
        Entry.Logger.Info($"Challenge doubling check: flag={challenges.DoublesReturnedCarry}, " +
                           $"returnedCards={returnedCardCounts.Count}, returnedRelics={returnedRelicCounts.Count}");
        if (challenges.DoublesReturnedCarry)
        {
            foreach (KeyValuePair<ModelId, int> kvp in returnedCardCounts)
            {
                WarehouseCard? sample = cards.FirstOrDefault(c => c.Card.Id == kvp.Key);
                if (sample == null)
                {
                    continue;
                }

                for (int i = 0; i < kvp.Value; i++)
                {
                    cards.Add(new WarehouseCard
                    {
                        Card = sample.Card,
                        Durability = WarehouseStore.MaxDurabilityForCard(kvp.Key),
                    });
                }
            }

            foreach (KeyValuePair<ModelId, int> kvp in returnedRelicCounts)
            {
                WarehouseRelic? sample = relics.FirstOrDefault(r => r.Relic.Id == kvp.Key);
                if (sample == null)
                {
                    continue;
                }

                for (int i = 0; i < kvp.Value; i++)
                {
                    relics.Add(new WarehouseRelic
                    {
                        Relic = sample.Relic,
                        Durability = WarehouseStore.MaxDurabilityForRelic(),
                    });
                }
            }

            gold *= 2;
        }

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

        ApplyChallengeRewards(result, me, challengeIds);
        Entry.Logger.Info($"ExtractionRun: extracted {cards.Count} cards, {relics.Count} relics, " +
                          $"{potions.Count} potions, {gold} gold; " +
                          $"broke {brokenCards.Count} card(s) and {brokenRelics.Count} relic(s); " +
                          $"dropped {result.ExpiredRelics.Count} spent relic(s).");
        RitsuToastService.ShowInfo(ExtractionLocalization.DepositSuccessText());
    }

    /// <summary>
    /// Victory-only challenge rewards: each selected definition executes its ordered reward actions, then records its
    /// clear count. Never reached from the extraction-point/defeat paths. 仅在胜利结算发放的挑战奖励：每条选中挑战按顺序
    /// 执行奖励动作，随后记录通关次数。
    /// 撤离点/失败路径到不了这里。
    /// </summary>
    private static void ApplyChallengeRewards(ExtractionSettlementResult result, Player me, IReadOnlyList<string> challengeIds)
    {
        foreach (string id in challengeIds)
        {
            ChallengeDef? def = ChallengeRegistry.Get(id);
            if (def == null)
            {
                continue;
            }

            foreach (ChallengeRewardAction action in def.Rewards)
            {
                ApplyChallengeRewardAction(result, me, action);
            }
        }

        foreach (string id in challengeIds.Distinct())
        {
            ChallengeStore.MarkCleared(id);
        }
    }

    private static void ApplyChallengeRewardAction(ExtractionSettlementResult result, Player player,
        ChallengeRewardAction action)
    {
        switch (action)
        {
            case DoubleReturnedCarryRewardAction:
                // This action is applied before Deposit because it mutates returned copies and gold.
                return;
            case GrantFixedCardsRewardAction fixedCards:
            {
                (List<WarehouseCard> cards, _) = WarehouseStore.GrantFixedCards(fixedCards.CardIds.ToArray(), fixedCards.Count);
                result.Cards.AddRange(cards);
                return;
            }
            case GrantAllCharacterCardsRewardAction:
            {
                (List<WarehouseCard> cards, _) = WarehouseStore.GrantAllCardsReward(player.Character);
                result.Cards.AddRange(cards);
                return;
            }
            case GrantRelicRarityRewardAction relicsByRarity:
            {
                (_, List<WarehouseRelic> relics) = WarehouseStore.GrantRelicRarityReward(player.Character,
                    relicsByRarity.Rarity, relicsByRarity.Count);
                result.Relics.AddRange(relics);
                return;
            }
            case GrantFixedRelicsRewardAction fixedRelics:
            {
                (_, List<WarehouseRelic> relics) = WarehouseStore.GrantFixedRelics(
                    fixedRelics.RelicIds.ToArray(), fixedRelics.Count);
                result.Relics.AddRange(relics);
                return;
            }
            case GrantGoldRewardAction gold:
                result.Gold += WarehouseStore.GrantGold(gold.Amount);
                return;
            case GrantCardRarityRewardAction cardsByRarity:
            {
                (List<WarehouseCard> cards, _) = WarehouseStore.GrantRarityReward(player.Character,
                    cardsByRarity.Rarity, cardsByRarity.Count);
                result.Cards.AddRange(cards);
                return;
            }
            default:
                throw new InvalidOperationException($"Unknown challenge reward action: {action.GetType().Name}");
        }
    }

    /// <summary>
    /// Third-state settlement for a 撤离点 extraction. The run ended as a defeat (forced kill), but the recorded
    /// selection IS deposited: 普通撤离 deposits the selected card/relic copies (per-id counts) + all potions + all gold;
    /// 金币撤离 deposits EVERYTHING minus the gold fee. Durability is matched like a victory (carried copies come back at
    /// carried−1, carried-1 copies break), but there is NO clear reward — no starter deck/relic grant, no victory epoch.
    /// 撤离点撤离的第三态结算：跑局以失败结束，但记录的选择正常入仓——普通撤离入仓选中的牌/遗物副本（按 id 份数）+全部药水+全部
    /// 金币；金币撤离全带（扣除金币费）。耐久按胜利匹配（携带副本带回 携带−1，1 耐久战损），但不发清关奖励。
    /// </summary>
    private static void SettleExtractionPoint(ExtractionSettlementResult result, Player me)
    {
        ExtractionPointSelection? selection = ExtractionPointFlow.Selection;
        if (selection == null)
        {
            // IsExtractionChosen without a recorded selection (stale flag) — fall back to the plain-defeat report.
            // IsExtractionChosen 却无记录选择（陈旧标记）——回退普通失败报告。
            SettleFailure(result, me);
            return;
        }

        CarryConfig carried = ExtractionRunData.Carry.Get(me);
        WarehouseStore.BackfillCarryDurability(carried);
        bool durability = WarehouseStore.IsDurabilityEnabled;
        (Dictionary<ModelId, List<int>> carriedCardDur, Dictionary<ModelId, List<int>> carriedRelicDur) =
            BuildCarriedDurability(carried);

        bool goldKind = selection.Kind == ExtractionPointKind.Gold;
        IEnumerable<CardModel> cardSource = goldKind ? me.Deck.Cards : SelectedCardCopies(me.Deck.Cards, selection.Cards);
        IEnumerable<RelicModel> relicSource = goldKind ? me.Relics : SelectedRelicCopies(me.Relics, selection.Relics);

        var cards = new List<WarehouseCard>();
        var brokenCards = new List<WarehouseCard>();
        CollectCards(cardSource, carriedCardDur, durability, cards, brokenCards);

        var relics = new List<WarehouseRelic>();
        var brokenRelics = new List<WarehouseRelic>();
        CollectRelics(relicSource, carriedRelicDur, durability, result, relics, brokenRelics);

        var potions = new List<SerializablePotion>();
        CollectPotions(me, potions);

        // 金币撤离 deducts the fee from the deposited gold; 普通撤离 deposits all gold.
        int gold = Math.Max(0, me.Gold - (goldKind ? selection.GoldFee : 0));

        result.Cards.AddRange(cards);
        result.Relics.AddRange(relics);
        result.BrokenCards.AddRange(brokenCards);
        result.BrokenRelics.AddRange(brokenRelics);
        result.Potions.AddRange(potions);
        result.Gold = gold;

        WarehouseStore.Deposit(cards, relics, potions, gold);
        Entry.Logger.Info($"ExtractionRun: extraction-point extract — {cards.Count} cards, {relics.Count} relics, " +
                          $"{potions.Count} potions, {gold} gold; " +
                          $"broke {brokenCards.Count} card(s) and {brokenRelics.Count} relic(s); " +
                          $"dropped {result.ExpiredRelics.Count} spent relic(s).");
        RitsuToastService.ShowInfo(ExtractionLocalization.DepositSuccessText());
    }

    /// <summary>Builds the per-id carried-durability queues (order preserved) used by the capped matching.
    /// 构建按 id 封顶匹配用的携带耐久队列（保序）。</summary>
    private static (Dictionary<ModelId, List<int>> Cards, Dictionary<ModelId, List<int>> Relics) BuildCarriedDurability(
        CarryConfig carried)
    {
        var cards = new Dictionary<ModelId, List<int>>();
        foreach (WarehouseCard wc in carried.Cards)
        {
            if (wc.Card.Id is ModelId cardId)
            {
                if (!cards.TryGetValue(cardId, out List<int>? durs))
                {
                    cards[cardId] = durs = new List<int>();
                }

                durs.Add(wc.Durability);
            }
        }

        var relics = new Dictionary<ModelId, List<int>>();
        foreach (WarehouseRelic wr in carried.Relics)
        {
            if (wr.Relic.Id is ModelId relicId)
            {
                if (!relics.TryGetValue(relicId, out List<int>? durs))
                {
                    relics[relicId] = durs = new List<int>();
                }

                durs.Add(wr.Durability);
            }
        }

        return (cards, relics);
    }

    private static void CollectCards(IEnumerable<CardModel> source, Dictionary<ModelId, List<int>> carriedCardDur,
        bool durability, List<WarehouseCard> cards, List<WarehouseCard> broken,
        Dictionary<ModelId, int>? returnedCounts = null)
    {
        foreach (CardModel c in source)
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

            int newDur = NextDurability(carriedCardDur, sc.Id, durability, WarehouseStore.MaxDurabilityForCard(sc.Id), out bool matched);
            if (newDur <= 0)
            {
                broken.Add(new WarehouseCard { Card = sc, Durability = 0 });
            }
            else
            {
                cards.Add(new WarehouseCard { Card = sc, Durability = newDur });
                if (matched && returnedCounts != null && sc.Id is ModelId returnedId)
                {
                    returnedCounts[returnedId] = returnedCounts.GetValueOrDefault(returnedId) + 1;
                }
            }
        }
    }

    private static void CollectRelics(IEnumerable<RelicModel> source, Dictionary<ModelId, List<int>> carriedRelicDur,
        bool durability, ExtractionSettlementResult result, List<WarehouseRelic> relics, List<WarehouseRelic> broken,
        Dictionary<ModelId, int>? returnedCounts = null)
    {
        foreach (RelicModel r in source)
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

            int newDur = NextDurability(carriedRelicDur, sr.Id, durability, WarehouseStore.MaxDurabilityForRelic(), out bool matched);
            if (newDur <= 0)
            {
                broken.Add(new WarehouseRelic { Relic = sr, Durability = 0 });
            }
            else
            {
                relics.Add(new WarehouseRelic { Relic = sr, Durability = newDur });
                if (matched && returnedCounts != null && sr.Id is ModelId returnedId)
                {
                    returnedCounts[returnedId] = returnedCounts.GetValueOrDefault(returnedId) + 1;
                }
            }
        }
    }

    private static void CollectPotions(Player me, List<SerializablePotion> potions)
    {
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
    }

    /// <summary>
    /// Yields the first <paramref name="counts"/>[id] non-clone deck copies of each selected id — matching the panel's
    /// availability (which also excluded clones), so a selection count always resolves to that many real copies.
    /// 按 id 份数产出牌组副本，跳过运行级克隆（与面板可用数口径一致），保证选中份数总能解析到足量真实副本。
    /// </summary>
    private static IEnumerable<CardModel> SelectedCardCopies(IEnumerable<CardModel> source, Dictionary<ModelId, int> counts)
    {
        Dictionary<ModelId, int> remaining = new(counts);
        foreach (CardModel c in source)
        {
            if (CloneMarker.ShouldExclude(c))
            {
                continue;
            }

            if (c.Id is ModelId id && remaining.TryGetValue(id, out int n) && n > 0)
            {
                remaining[id] = n - 1;
                yield return c;
            }
        }
    }

    /// <summary>Same per-id count resolution for relics, skipping expired copies (the panel excluded them too).
    /// 遗物按 id 份数解析，跳过失效副本（面板同样排除了它们）。</summary>
    private static IEnumerable<RelicModel> SelectedRelicCopies(IEnumerable<RelicModel> source, Dictionary<ModelId, int> counts)
    {
        Dictionary<ModelId, int> remaining = new(counts);
        foreach (RelicModel r in source)
        {
            if (IsExpiredRelic(r))
            {
                continue;
            }

            if (r.Id is ModelId id && remaining.TryGetValue(id, out int n) && n > 0)
            {
                remaining[id] = n - 1;
                yield return r;
            }
        }
    }


    /// <summary>
    /// The next durability for one deposited copy: if it matches a carried copy (id matched, capped at the carried
    /// count), the carried value minus one — a copy at 1 breaks (≤0). Otherwise the copy is new: full durability by
    /// rarity when durability is ON, or full when OFF (the whole decrement is skipped). Matching itself is independent
    /// of the durability flag — a copy whose id hits the carried cap is a "returned carried copy" (counted for the
    /// HP_ONE doubling reward) even under OFF mode, where it simply returns at full instead of carried − 1.
    /// Matching is per-id and order-preserving: the first carriedCount copies of an id consume the carried values, so a
    /// carried copy removed mid-run and replaced by a same-id reward mis-decrements that reward by one (accepted edge
    /// of the capped scheme).
    /// 单份入库副本的下一耐久：命中携带副本（按 id 封顶匹配）→ 携带值 − 1（1 耐久副本 → ≤0 战损）；否则为新货 → 满耐久
    /// （耐久 ON 按稀有度，OFF 也满——OFF 整段不递减）。匹配与耐久开关无关——命中携带封顶的副本就是「带回的携带副本」（计入
    /// 纸片人翻倍奖励），只是 OFF 模式下按满耐久带回而非 携带−1。按 id 保序匹配：每 id 前 carriedCount 份消耗携带值，故携带牌
    /// 中途被移除、又同名奖励补进时，会误把该奖励递减一次（封顶方案的已知边角）。
    /// </summary>
    private static int NextDurability(Dictionary<ModelId, List<int>> carriedByKind, ModelId? id, bool durability,
        int fullDurability, out bool matchedCarried)
    {
        if (id != null && carriedByKind.TryGetValue(id, out List<int>? durs) && durs.Count > 0)
        {
            int d = durs[0];
            durs.RemoveAt(0);
            matchedCarried = true;
            return durability ? d - 1 : fullDurability;
        }

        matchedCarried = false;
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
    internal static bool IsExpiredRelic(RelicModel r)
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
