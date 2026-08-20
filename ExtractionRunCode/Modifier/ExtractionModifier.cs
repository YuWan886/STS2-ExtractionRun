using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using ExtractionRun.Data;
using ExtractionRun.Lifecycle;
using ExtractionRun.Networking;
using ExtractionRun.Patches;
using ExtractionRun.UI;

namespace ExtractionRun.Modifier;

/// <summary>
/// Run-lifetime marker + injector for the 搜打撤 (Search-Loot-Extract) game mode. Added to a run's modifiers by the
/// warehouse hub launch flow (never appears in daily/custom modifier lists). Runs on EVERY machine during
/// <c>InitializeNewRun</c> with identical, deterministic input, so the starting loadout matches everywhere.
/// It also places the 撤离点 (extraction point) node: per-act at act generation it rolls (host-authoritative chance) and
/// marks one `?` point via <c>ModifyGeneratedMapLate</c>/<c>AfterMapGenerated</c>, then substitutes the
/// <see cref="ExtractionPointEvent"/> when the party enters that point via <c>ModifyNextEvent</c>.
/// 搜打撤模式的局内标记与注入器。由仓库大厅发起跑局时加入 modifiers（不会出现在每日/自定义列表）。在每台机器上确定性执行。
/// 同时放置撤离点节点：每幕生成时按主机权威概率 roll 并标记一个 `?` 点，玩家进入该点时经 ModifyNextEvent 替换为撤离点事件。
/// </summary>
public sealed class ExtractionModifier : ModifierModel
{
    /// <summary>Sentinel for "this act placed no 撤离点". 本幕未放置撤离点的哨兵值。</summary>
    private const int NoCoord = -1;

    /// <summary>Clears the default character deck before <see cref="AfterRunCreated"/> (no cards unless carried).</summary>
    public override bool ClearsPlayerDeck => true;
    protected override string IconPath => "res://ExtractionRun/images/modifiers/extraction.png";

    /// <summary>True once the party ENTERED the extraction point — the once-per-run gate. Persisted so a
    /// 路过-then-reload run doesn't re-open the spent gate. 进入过撤离点即消耗本局唯一一次；持久化，路过后再读档不会重新开门。</summary>
    [SavedProperty]
    public bool Encountered { get; set; }

    /// <summary>Column of this act's marked `?` point (<see cref="NoCoord"/> = this act rolled no). 本幕撤离点所在 `?` 点列。</summary>
    [SavedProperty]
    public int MarkedCol { get; set; } = NoCoord;

    /// <summary>Row of this act's marked `?` point (<see cref="NoCoord"/> = this act rolled no). 本幕撤离点所在 `?` 点行。</summary>
    [SavedProperty]
    public int MarkedRow { get; set; } = NoCoord;

    /// <summary>
    /// Read-only access for the map patches (the node's icon + the forced Event room type). Backed by the
    /// <c>[SavedProperty]</c> pair so a mid-run save/reload restores the placement instead of re-rolling it away.
    /// 供地图补丁读取。由 [SavedProperty] 列/行支撑，中途读档可还原放置而非把它重掷没了。
    /// </summary>
    public MapCoord? MarkedCoord => MarkedCol < 0 ? null : new MapCoord(MarkedCol, MarkedRow);

    /// <summary>
    /// Challenge ids carried by this run, comma-joined (the game's <c>[SavedProperty]</c> supports scalar types only —
    /// a list wouldn't round-trip; ids are stable tokens so the join is safe). Written by the hub launch flow before the
    /// modifier is set on the lobby; empty string = a normal extraction run, byte-for-byte today's behavior.
    /// 本局携带的挑战 id（逗号拼接——[SavedProperty] 仅支持标量类型，列表无法往返；id 为稳定 token，拼接安全）。空串=普通搜打撤局。
    /// </summary>
    [SavedProperty]
    public string ChallengeIds { get; set; } = "";

    /// <summary>Catalog protocol carried with challenge ids so mismatched multiplayer clients fail before play.</summary>
    [SavedProperty]
    public int ChallengeCatalogSchemaVersion { get; set; }

    /// <summary>Digest of challenge rules and rewards carried with this run's challenge selection.</summary>
    [SavedProperty]
    public string ChallengeCatalogHash { get; set; } = "";

    /// <summary>The parsed challenge id list. 解析出的挑战 id 列表。</summary>
    public IReadOnlyList<string> ActiveChallengeIds =>
        ChallengeSelectionService.ParseRunIds(ChallengeIds).Ids;

    /// <summary>Normalized parameterized rules for this run. 本局挑战归一化后的参数化规则。</summary>
    public ChallengeRuntime Challenges => ChallengeRuntime.FromIds(ActiveChallengeIds);

    /// <summary>True when the run carries <paramref name="id"/>. 本局是否携带该挑战。</summary>
    public bool HasChallenge(string id) => Challenges.ChallengeIds.Contains(id);

    /// <summary>
    /// The base extraction description, plus the selected challenges as a sorted per-line list at the bottom when this
    /// run carries any — a normal extraction run (no challenges) is byte-for-byte the vanilla description. The list
    /// follows the registry order (daily pool then permanents — the challenge page's display order); unknown/stale ids
    /// simply drop out. One override covers every surface that reads the modifier description (in-run top-bar hover,
    /// run-modifier UI). 基础搜打撤描述，本局携带挑战时在其底部追加按注册表顺序、每行一条的挑战列表；普通局字节级不变。
    /// 未知/过期 id 自然过滤。改这一处即可覆盖所有读取 modifier 描述的表面（局内顶栏悬停、开局 modifier 界面）。
    /// </summary>
    public override LocString Description
    {
        get
        {
            IReadOnlyList<string> ids = ActiveChallengeIds;
            if (ids.Count == 0)
            {
                return base.Description;
            }

            string[] lines = ChallengeRegistry.All
                .Where(def => ids.Contains(def.Id))
                .Select(def => "• " + ExtractionLocalization.ChallengeTitle(def.Id))
                .ToArray();
            if (lines.Length == 0)
            {
                return base.Description;
            }

            LocString loc = new LocString("modifiers", "EXTRACTION_MODIFIER.descriptionWithChallenges");
            loc.Add("description", base.Description.GetFormattedText());
            loc.Add("challenges", string.Join("\n", lines));
            return loc;
        }
    }

    protected override void AfterRunCreated(RunState runState)
    {
        ValidateChallengeCatalog();
        ExtractionSettlement.Clear();
        ExtractionPointFlow.Clear();
        CarriedPickupQueue.Reset();
        ChallengeRuntime challenges = Challenges;

        foreach (Player player in runState.Players)
        {
            CarryConfig config = ExtractionRunData.Carry.Get(player);
            // A pre-durability saved carry deserializes with the 0 sentinel; backfill so the consume below matches the
            // warehouse's full-durability copies and the deposit decrements from full. 旧档携带以 0 哨兵反序列化；回填满耐久，
            // 让下方消耗能精确匹配仓库满耐久副本、撤离从满耐久递减。
            WarehouseStore.BackfillCarryDurability(config);

            // Challenge constraints reshape the carry BEFORE anything is injected or consumed, so the local consume
            // below never spends warehouse copies the run never received. 挑战约束在任何注入/消耗前重塑携带，保证下方消耗
            // 不会为局内实际没收到的仓库副本买单。
            // EmptyCarry: the run starts with nothing carried — the starter kit below stands in (deck + starter relics +
            // 99 gold). The hub already forces the draft empty; a stale save is the only way items survive to here.
            // 空携带挑战：开局不带任何物品——由下方起手包兜底（牌组 + 初始遗物 + 99 金币）。大厅已把草稿清空，仅旧档会残留。
            if (challenges.StartsEmpty)
            {
                config.Cards.Clear();
                config.Relics.Clear();
                config.Potions.Clear();
                config.Gold = challenges.StarterGold;
            }

            // BasicCommonOnly: strip any carried card above Basic/Common, and pull it from the config so the consume
            // skips it. The hub greys these out; a stale save is the only way one survives to here.
            // 仅基础+普通：剔除携带中的高稀有度卡并从配置移除（消耗随之跳过）。大厅会灰化此类卡，仅旧档会漏网。
            if (challenges.HasCarryRarityFilter && config.Cards.Count > 0)
            {
                int dropped = config.Cards.RemoveAll(wc =>
                    wc.Card.Id == null ||
                    ModelDb.GetByIdOrNull<CardModel>(wc.Card.Id) is { } m && !challenges.AllowsCarryCard(m));
                if (dropped > 0)
                {
                    Entry.Logger.Warn($"ExtractionModifier: dropped {dropped} card(s) rejected by carry rarity rules.");
                }
            }

            // StrikeOnly: strip any carried card that isn't a 打击 tag card, and pull it from the config so the consume
            // skips it. The hub greys these out (and disables the challenge when no Strike is carryable); a stale save
            // is the only way one survives to here. 只带打击牌：剔除携带中的非打击标签卡并从配置移除（消耗随之跳过）。
            // 大厅会灰化此类卡（无打击可带时整个挑战禁用）；仅旧档会漏网。
            if (challenges.HasCarryTag(CardTag.Strike) && config.Cards.Count > 0)
            {
                int dropped = config.Cards.RemoveAll(wc =>
                    wc.Card.Id == null ||
                    ModelDb.GetByIdOrNull<CardModel>(wc.Card.Id) is { } m && !challenges.AllowsCarryCard(m));
                if (dropped > 0)
                {
                    Entry.Logger.Warn($"ExtractionModifier: dropped {dropped} card(s) rejected by carry tag rules.");
                }
            }

            if (challenges.StartingMaxHp is int maxHp)
            {
                player.Creature.SetMaxHpInternal(maxHp);
            }

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
                    // Unlike the carried path (runState.LoadCard assigns the owner internally), these fallback cards
                    // skip the run-creation owner pass, so they must be registered here or the first hook iteration
                    // NREs on card.Owner.IsActiveForHooks (RunState.IterateHookListeners → Contains).
                    // 与携带路径不同（LoadCard 内部已赋 Owner），此处绕过开跑赋主流程，必须显式注册，
                    // 否则首次钩子遍历在 card.Owner.IsActiveForHooks 处空引用崩溃。
                    runState.AddCard(card, player);
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
                        runState.AddCard(card, player);
                        player.Deck.AddInternal(card, silent: true);
                    }
                }

                // EmptyCarry: the starter kit is the whole point — grant the character's starter relics too (the deck
                // above already granted the starter (or generic-basic) deck). 空携带挑战：起手包即全部——额外发放角色初始遗物
                // （牌组已在上面发放初始（或泛用基础）牌组）。
                if (challenges.StartsEmpty)
                {
                    foreach (RelicModel starterRelic in player.Character.StartingRelics)
                    {
                        RelicModel relic = starterRelic.ToMutable();
                        player.AddRelicInternal(relic, silent: true);
                    }

                    player.Gold = challenges.StarterGold;
                    Entry.Logger.Warn($"ExtractionModifier: player {player.NetId} in EMPTY_CARRY challenge — granted " +
                                      $"starter kit ({player.Deck.Cards.Count} cards, " +
                                      $"{player.Relics.Count} relics, {player.Gold} gold).");
                }
                else
                {
                    Entry.Logger.Warn($"ExtractionModifier: player {player.NetId} carried no cards; granted starter deck " +
                                      $"({player.Deck.Cards.Count} cards).");
                }
            }

            foreach (WarehouseCard wc in config.Cards)
            {
                if (wc.Card.Id == null || ModelDb.GetByIdOrNull<CardModel>(wc.Card.Id) == null)
                {
                    Entry.Logger.Warn($"ExtractionModifier skipping card from an unloaded mod: {wc.Card.Id}");
                    continue;
                }

                CardModel card = runState.LoadCard(wc.Card, player);
                if (card.Type == CardType.None)
                {
                    // Degenerate identity card (repair failed / unknown valid default): the game never plays a
                    // Type=None card and its OnPlay would throw — drop it instead of injecting a crash card.
                    // 退化身份牌（修复失败/未知有效默认）：游戏从不打出 Type=None 的牌，打出即崩，丢弃防崩。
                    runState.RemoveCard(card);
                    Entry.Logger.Warn($"ExtractionModifier skipping degenerate card (Type=None): {wc.Card.Id}");
                    continue;
                }

                player.Deck.AddInternal(card, silent: true);
            }

            foreach (WarehouseRelic wr in config.Relics)
            {
                if (wr.Relic.Id == null || ModelDb.GetByIdOrNull<RelicModel>(wr.Relic.Id) == null)
                {
                    Entry.Logger.Warn($"ExtractionModifier skipping relic from an unloaded mod: {wr.Relic.Id}");
                    continue;
                }

                RelicModel relic = RelicModel.FromSerializable(wr.Relic);
                player.AddRelicInternal(relic, silent: true);
                CarriedPickupQueue.MarkCarried(relic);
            }

            // Ascension (TightBelt) shrinks potion slots AFTER AfterRunCreated runs; when every slot is filled the
            // game's shrink writes to _potionSlots[IndexOf(null) == -1] → IndexOutOfRange, stalling run start. Inject
            // only what the post-ascension slots hold, and remove the excess from the config so the local consume
            // below leaves those copies in the warehouse (nothing injected, nothing consumed).
            // 进阶 A4+（TightBelt）在 AfterRunCreated 之后缩减药水栏位，满栏时游戏收缩越界导致开局卡住。只注入缩减后的
            // 栏位数，并把放不下/模型加载不到的副本从配置移除，本机消耗时多余副本留在仓库（未注入即不消耗）。
            int maxPotions = player.MaxPotionCount - (runState.AscensionLevel >= (int)AscensionLevel.TightBelt ? 1 : 0);
            int addedPotions = 0;
            for (int i = 0; i < config.Potions.Count;)
            {
                if (addedPotions >= maxPotions)
                {
                    config.Potions.RemoveAt(i);
                    continue;
                }

                SerializablePotion sp = config.Potions[i];
                if (sp.Id == null || ModelDb.GetByIdOrNull<PotionModel>(sp.Id) == null)
                {
                    config.Potions.RemoveAt(i);
                    continue;
                }

                PotionModel potion = PotionModel.FromSerializable(sp);
                player.AddPotionInternal(potion, silent: true);
                addedPotions++;
                i++;
            }

            // Curses: 2 random curses into the deck, drawn deterministically from the run RNG (same cards on every
            // machine). 诅咒挑战：随机 2 张诅咒入牌组，用 run RNG 抽取（所有机器同一结果）。
            if (challenges.RandomCurseCount > 0)
            {
                AddRandomCurses(runState, player, challenges.RandomCurseCount);

                Entry.Logger.Warn($"ExtractionModifier: player {player.NetId} challenge rules added " +
                                  $"{challenges.RandomCurseCount} curse(s) to the deck.");
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
                                  
                PendingCarryStore.Clear();
            }
        }
    }

    protected override void AfterRunLoaded(RunState runState)
    {
        // Reloading a saved extraction run must NOT re-inject or re-consume — the deck is already in the save and the
        // carried items were consumed when the run first started. Intentional no-op.
    }

    /// <summary>Applies the per-act curse rule only after an act transition; it never replays on a save load.</summary>
    public override Task AfterActEntered()
    {
        int curseCount = Challenges.RandomCursesPerAct;
        if (curseCount <= 0)
        {
            return Task.CompletedTask;
        }

        foreach (Player player in RunState.Players)
        {
            AddRandomCurses(RunState, player, curseCount);
        }

        return Task.CompletedTask;
    }

    /// <summary>Deals the hand-pressure challenge damage before the engine flushes player hands.</summary>
    public override async Task BeforeSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side,
        IEnumerable<Creature> participants)
    {
        await base.BeforeSideTurnEnd(choiceContext, side, participants);
        int damagePerCard = Challenges.HandEndDamagePerCard;
        if (side != CombatSide.Player || damagePerCard <= 0)
        {
            return;
        }

        HashSet<Creature> endingCreatures = participants.ToHashSet();
        foreach (Player player in RunState.Players.Where(player => endingCreatures.Contains(player.Creature)
            && player.Creature.IsAlive))
        {
            int cardsInHand = PileType.Hand.GetPile(player).Cards.Count;
            if (cardsInHand > 0)
            {
                await CreatureCmd.Damage(choiceContext, player.Creature, cardsInHand * damagePerCard,
                    ValueProp.Unpowered | ValueProp.Move, null, null);
            }
        }
    }

    /// <summary>Blocks an eleventh manual card play using the engine's normal playability hook.</summary>
    public override bool ShouldPlay(CardModel card, AutoPlayType autoPlayType)
    {
        if (!base.ShouldPlay(card, autoPlayType))
        {
            return false;
        }

        int? limit = Challenges.CardPlayLimitPerTurn;
        return limit == null || card.CombatState == null || CombatManager.Instance.History.CardPlaysStarted.Count(entry =>
            entry.CardPlay.Card.Owner == card.Owner && entry.HappenedThisTurn(card.CombatState)) < limit.Value;
    }

    /// <summary>Scales every live enemy at the escalating-enemies challenge's completed-play thresholds.</summary>
    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await base.AfterCardPlayed(choiceContext, cardPlay);
        EnemyCardPlayScalingRule? rule = Challenges.EnemyCardPlayScaling;
        if (rule == null || cardPlay.Card.CombatState == null)
        {
            return;
        }

        int plays = CombatManager.Instance.History.CardPlaysFinished.Count();
        IReadOnlyList<Creature> enemies = cardPlay.Card.CombatState.HittableEnemies.ToList();
        int maxHpSteps = rule.MaxHpPercent / rule.HpPercentPerTrigger;
        if (plays % rule.CardsPerHpIncrease == 0 && plays <= rule.CardsPerHpIncrease * maxHpSteps)
        {
            int hpSteps = Math.Min(plays / rule.CardsPerHpIncrease, maxHpSteps);
            decimal previousMultiplier = 1m + (hpSteps - 1) * rule.HpPercentPerTrigger / 100m;
            decimal multiplier = 1m + hpSteps * rule.HpPercentPerTrigger / 100m;
            foreach (Creature enemy in enemies)
            {
                int previousMaxHp = enemy.MaxHp;
                int previousCurrentHp = enemy.CurrentHp;
                int baseMaxHp = Math.Max(1, (int)Math.Ceiling(previousMaxHp / previousMultiplier));
                int scaledMaxHp = Math.Max(previousMaxHp, (int)Math.Ceiling(baseMaxHp * multiplier));
                enemy.SetMaxHpInternal(scaledMaxHp);
                enemy.SetCurrentHpInternal((decimal)previousCurrentHp * scaledMaxHp / previousMaxHp);
            }
        }

        if (plays % rule.CardsPerStrength == 0
            && plays <= rule.CardsPerStrength * rule.MaxStrength)
        {
            await PowerCmd.Apply<StrengthPower>(choiceContext, enemies, rule.StrengthPerTrigger, null, null);
        }
    }

    private static void AddRandomCurses(RunState runState, Player player, int count)
    {
        // Match the official CursedRun source: only unlocked curses allowed by the current
        // single-/multiplayer constraint and modifier-generation policy may be selected.
        List<CardModel> cursePool = ModelDb.CardPool<CurseCardPool>()
            .GetUnlockedCards(player.UnlockState, player.RunState.CardMultiplayerConstraint)
            .Where(c => c.CanBeGeneratedByModifiers)
            .ToList();
        for (int i = 0; i < count && cursePool.Count > 0; i++)
        {
            CardModel curseTemplate = cursePool[runState.Rng.Niche.NextInt(cursePool.Count)];
            CardModel curse = curseTemplate.ToMutable();
            curse.FloorAddedToDeck = 1;
            runState.AddCard(curse, player);
            player.Deck.AddInternal(curse, silent: true);
        }
    }

    /// <summary>
    /// Engine A — the BASIC_COMMON card-acquisition filter: every card reward (combat / elite / boss / treasure /
    /// event) is constrained to Basic/Common cards. Curses/status cards never enter through this funnel (they are
    /// direct deck grants), so the exemption is automatic. A reward that INSISTS on a rarity the filter forbids
    /// (e.g. a rare-only event) is left alone rather than emptied into a crash — the caller's own constraint wins.
    /// 引擎 A——BASIC_COMMON 卡牌获取过滤：所有卡牌奖励（战斗/精英/Boss/宝箱/事件）都限定为基础+普通。诅咒/状态卡不经由此
    /// 漏斗入队（它们由事件/遗物直接塞入），豁免天然成立。强制要求被禁稀有度的奖励（如仅限稀有的事件）保持原样防止空池崩溃。
    /// </summary>
    public override CardCreationOptions ModifyCardRewardCreationOptions(Player player, CardCreationOptions options)
    {
        ChallengeRuntime challenges = Challenges;
        if (!challenges.HasCardAcquisitionFilter)
        {
            return options;
        }

        try
        {
            if (!options.GetPossibleCards(player).Any(challenges.AllowsAcquiredCard))
            {
                return options; // filtering would empty this reward's pool — let the caller's rarity win
            }

            if (options.CardPools.Count > 0)
            {
                // WithCardPools clears its backing collection before enumerating the input. Snapshot the current
                // view first, otherwise passing options.CardPools back into the same instance empties the reward.
                // WithCardPools 会先清空内部集合再枚举输入；因此必须先快照当前视图，不能把同一实例的 CardPools 直接传回。
                CardPoolModel[] pools = options.CardPools.ToArray();
                Func<CardModel, bool>? existing = options.CardPoolFilter;
#if STS2_CARD_CREATION_OPTIONS_HAS_WITH_FILTER
                return options.WithCardPools(pools).WithFilter(
                    existing == null
                        ? challenges.AllowsAcquiredCard
                        : card => existing(card) && challenges.AllowsAcquiredCard(card));
#else
                return options.WithCardPools(pools,
                    existing == null
                        ? challenges.AllowsAcquiredCard
                        : card => existing(card) && challenges.AllowsAcquiredCard(card));
#endif
            }

#if !STS2_CARD_CREATION_OPTIONS_HAS_WITH_FILTER
            if (options.CustomCardPool != null)
            {
                CardModel[] filtered = options.CustomCardPool.Where(challenges.AllowsAcquiredCard).ToArray();
                bool singleRarity = filtered.Select(c => c.Rarity).Distinct().Count() <= 1;
                return options.WithCustomPool(filtered, singleRarity ? CardRarityOddsType.Uniform : options.RarityOdds);
            }
#endif

            return options;
        }
        catch (Exception)
        {
            return options; // defensive — never break reward generation for the challenge
        }
    }

    /// <summary>
    /// Engine A merchant arm: the in-run merchant sells Basic/Common only under the BASIC_COMMON challenge. Merchant
    /// generation separately requests Attack, Skill and Power cards, while it discards Basic cards after this hook.
    /// Restore the original candidates only for a type that otherwise has no sellable Common card, so the shop remains
    /// valid without unnecessarily leaking other off-challenge cards. 引擎 A 的商人分支：仅基础+普通挑战下，局内商人只售
    /// 基础+普通卡。商店会分别请求攻击、技能、能力牌，且会在此钩子后丢弃基础牌；只有某类型没有可售普通牌时才补回该类型
    /// 的原候选，既保证商店有效，也不无谓漏出其他挑战外卡牌。
    /// </summary>
    public override IEnumerable<CardModel> ModifyMerchantCardPool(Player player, IEnumerable<CardModel> options)
    {
        ChallengeRuntime challenges = Challenges;
        if (!challenges.HasCardAcquisitionFilter)
        {
            return options;
        }

        CardModel[] sellableOptions = options.Where(card => card.Rarity != CardRarity.Basic).ToArray();
        List<CardModel> filtered = sellableOptions.Where(challenges.AllowsAcquiredCard).ToList();

        foreach (CardType type in new[] { CardType.Attack, CardType.Skill, CardType.Power })
        {
            if (sellableOptions.Any(card => card.Type == type) && !filtered.Any(card => card.Type == type))
            {
                filtered.AddRange(sellableOptions.Where(card => card.Type == type));
            }
        }

        return filtered.Count == 0 ? sellableOptions : filtered;
    }

    /// <summary>
    /// Engine F (DOUBLE_ENEMY) — damage arm: damage DEALT BY enemies is multiplied by 2. Guarded to the enemy dealer
    /// (<c>dealer</c> non-player), so player damage is untouched; the modifier only exists in extraction runs, and the
    /// effect flag gates the branch — outside the challenge this is byte-for-byte the base game. The HP arm lives in a
    /// Harmony postfix on <c>CombatState.CreateCreature</c> (<see cref="DoubleEnemyPatch"/>) — it is the single funnel
    /// through which every enemy spawns (initial combat, mid-combat summons, event fights), unlike
    /// <c>AfterCreatureAddedToCombat</c> which only fires for creatures added mid-combat.
    /// 引擎 F（DOUBLE_ENEMY）伤害臂：敌人造成的伤害 ×2。限定 dealer 为敌人（非玩家），玩家输出不受影响；modifier 仅存在于
    /// 搜打撤局，且效果位门控——非挑战局与原版字节级一致。血量臂在 CombatState.CreateCreature 的 Harmony 后缀（见
    /// DoubleEnemyPatch）——那是所有敌人生成的唯一漏斗（初始战斗、中途召唤、事件战），AfterCreatureAddedToCombat 只对
    /// 战斗中后添加的生物触发，覆盖不全。
    /// </summary>
    public override decimal ModifyDamageMultiplicative(
        Creature? target,
        decimal amount,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource
#if STS2_DAMAGE_HOOK_HAS_CARD_PLAY
        , CardPlay? cardPlay
#endif
    )
    {
        ChallengeRuntime challenges = Challenges;
        if (challenges.EnemyDamageMultiplier == 1m || dealer == null || dealer.Side == CombatSide.Player)
        {
            return 1m;
        }

        return challenges.EnemyDamageMultiplier;
    }

    /// <summary>
    /// Places the 撤离点 quest marker on the marked `?` point, then applies the challenge map rules over the generated
    /// map. Engine B (ALL_ELITE): every Monster point is rewritten to an Elite point (pure type rewrite, no RNG — see
    /// the branch below). Engine G (ONE_REST): keeps exactly ONE rest point per act (grill-locked: random pick, run-RNG
    /// deterministic on every machine), and rewrites every other RestSite point to <c>?</c> (Unknown). A `?` can roll
    /// Monster/Event/Treasure/Shop but NEVER RestSite (UnknownMapPointOdds has no RestSite entry), so the rewritten
    /// points turn into those — path connectivity is preserved by construction. Both rewrites restore on a fresh map
    /// (new act / mid-act regen) and are idempotent over a saved map, matching the 撤离点 placement's SavedActMap
    /// behaviour. 放置撤离点任务标记后对生成的地图应用挑战地图规则。引擎 B（ALL_ELITE）：每个 Monster 点重写为 Elite 点
    /// （纯类型重写，无随机）。引擎 G（ONE_REST）：每幕只保留 1 个休息点（grill 锁定：随机抽，run RNG 全机器确定），
    /// 其余 RestSite 点改写为 `?`。`?` 只会 roll 出战斗/事件/宝箱/商店、绝不会是休息室（UnknownMapPointOdds 无 RestSite 项），
    /// 故被改写的点自然变成那些，路径连通性天然保留。两次重写在新地图（新幕/中途重生成）重新执行、对已保存地图幂等。
    /// </summary>
    public override Task AfterMapGenerated(ActMap map, int actIndex)
    {
        if (MarkedCoord is { } coord && map.HasPoint(coord))
        {
            map.GetPoint(coord)?.AddQuest(this);
        }

        // Engine B (ALL_ELITE): every Monster point becomes an Elite point — not just the encounter — so the map icon,
        // the roll, the room, history and rewards are all consistently elite. A pure point-type rewrite (no RNG,
        // deterministic on every machine). No CanBeModified filter on purpose: the base game's row-1 starters (the
        // monsters right after the Ancient starting room) are marked CanBeModified=false, and those ARE the challenge's
        // target — ONE_REST below likewise rewrites structural rest points. `?` keeps vanilla odds (an Unknown that
        // rolls Monster stays a normal fight). 引擎 B（ALL_ELITE）：把每个 Monster 点重写为 Elite 点——不只是遭遇——
        // 图标/roll/房间/历史/奖励全部一致精英。纯类型重写（无随机，全机器确定）。故意不过滤 CanBeModified：基础游戏
        // 第 1 行起始怪（先古房间后的第一排）为 CanBeModified=false，恰是挑战目标——下方 ONE_REST 同样改写结构性休息点。
        // `?` 保持原版概率（滚出普通战斗就是普通战斗）。
        ChallengeRuntime challenges = Challenges;
        foreach (MapPoint point in map.GetAllMapPoints())
        {
            point.PointType = challenges.TransformMapPoint(point.PointType);
        }

        foreach (MapPointLimitRule rule in challenges.MapPointLimits)
        {
            List<MapPoint> points = map.GetAllMapPoints()
                .Where(point => point.PointType == rule.PointType)
                .ToList();
            if (points.Count <= rule.MaxPerAct)
            {
                continue;
            }

            var kept = new HashSet<MapPoint>();
            for (int i = 0; i < rule.MaxPerAct && points.Count > 0; i++)
            {
                int index = RunManager.Instance?.State?.Rng.Niche.NextInt(points.Count) ?? 0;
                kept.Add(points[index]);
                points.RemoveAt(index);
            }

            foreach (MapPoint point in points)
            {
                if (!kept.Contains(point))
                {
                    point.PointType = rule.Replacement;
                }
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// At act generation, rolls whether THIS act places a 撤离点 node (each act rolls independently; the once-per-run
    /// gate only closes when the party ENTERS the node, so a skipped node leaves the gate open for the next act). On a
    /// hit, marks a deterministic `?` point (respecting <c>CanBeModified</c> — never touch points the game protects).
    /// The roll is deterministic on every machine via the run RNG against the host-authoritative chance. The decision
    /// is <c>[SavedProperty]</c>-persisted, and a reload re-presenting the saved map is kept as-is (no re-roll).
    /// 每幕生成时 roll 本幕是否放置撤离点（每幕独立；只有进入节点才消耗本局唯一一次，绕开则下一幕继续 roll）。命中时确定性
    /// 标记一个 `?` 点（尊重 CanBeModified——绝不碰游戏保护的节点）。用 run RNG 对主机权威概率 roll，所有机器结果一致。
    /// 决策经 [SavedProperty] 持久化；读档重新给出已保存地图时原样保留（不重掷）。
    /// </summary>
    public override ActMap ModifyGeneratedMapLate(IRunState runState, ActMap map, int actIndex)
    {
        if (Encountered)
        {
            return map;
        }

        // Loading a saved run re-runs this hook with the act's SAVED map; the placement decision for this act was
        // already made and persisted, so keep it instead of re-rolling from the shifted RNG (the point would move or
        // vanish). A freshly generated map — a new act or a mid-act regen like GoldenCompass — is not a SavedActMap
        // and rolls anew, exactly like a first-time run.
        // 读档会以本幕已保存的地图重跑本钩子；本幕放置决策已定并持久化，保留而非用推进后的 RNG 重掷（否则撤离点会移位或消失）。
        // 新生成的地图（新幕或中途重生成，如 GoldenCompass）不是 SavedActMap，照常掷点，与首次开跑一致。
        if (map is SavedActMap)
        {
            return map;
        }

        MarkedCol = NoCoord;
        MarkedRow = NoCoord;
        if (runState.Rng.Niche.NextDouble() > ExtractionPointSettingsSync.ActChance)
        {
            return map;
        }

        List<MapPoint> candidates = map.GetAllMapPoints()
            .Where(p => p.PointType == MapPointType.Unknown && p.CanBeModified)
            .ToList();
        if (candidates.Count == 0)
        {
            return map;
        }

        MapCoord coord = candidates[runState.Rng.Niche.NextInt(candidates.Count)].coord;
        MarkedCol = coord.col;
        MarkedRow = coord.row;
        Entry.Logger.Info($"ExtractionModifier: placed extraction point at {MarkedCoord} in act {actIndex}.");
        return map;
    }


    /// <summary>
    /// Substitutes the 撤离点 event when the party pulls an event AT the marked point; otherwise pure pass-through.
    /// Scoped by the modifier's existence (it only lives in extraction runs), so every other run and every other event
    /// is untouched. 当玩家在标记点拉取事件时替换为撤离点事件；否则原样放行。仅存在于搜打撤局的 modifier 天然门控。
    /// </summary>
    public override EventModel ModifyNextEvent(EventModel currentEvent)
    {
        if (MarkedCoord is { } coord && !Encountered &&
            RunManager.Instance?.State?.CurrentMapPoint?.coord == coord)
        {
            Encountered = true;
            Entry.Logger.Info("ExtractionModifier: party entered the extraction point.");
            return ModelDb.Event<ExtractionPointEvent>();
        }

        return currentEvent;
    }

    private void ValidateChallengeCatalog()
    {
        if (string.IsNullOrWhiteSpace(ChallengeIds))
        {
            return;
        }

        // Old saves predate the protocol fields. Preserve their playable challenge run, but make the unverified state
        // explicit in logs; every newly launched run always carries the fields below.
        if (ChallengeCatalogSchemaVersion == 0 && string.IsNullOrEmpty(ChallengeCatalogHash))
        {
            Entry.Logger.Warn("ExtractionModifier: loading legacy challenge run without a catalog protocol signature.");
            return;
        }

        if (ChallengeCatalogSchemaVersion != ChallengeRegistry.CatalogSchemaVersion
            || !string.Equals(ChallengeCatalogHash, ChallengeRegistry.CatalogHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Challenge catalog mismatch. All players must use the same challenge catalog.");
        }

        ChallengeSelectionResult parsed = ChallengeSelectionService.ParseRunIds(ChallengeIds);
        if (parsed.RejectedIds.Count > 0)
        {
            Entry.Logger.Warn("ExtractionModifier: ignored invalid/duplicate challenge id(s): " +
                              string.Join(", ", parsed.RejectedIds));
        }
    }
}
