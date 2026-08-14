using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using ExtractionRun.Data;
using ExtractionRun.Lifecycle;
using ExtractionRun.Networking;

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

    protected override void AfterRunCreated(RunState runState)
    {
        ExtractionSettlement.Clear();
        ExtractionPointFlow.Clear();
        CarriedPickupQueue.Reset();

        foreach (Player player in runState.Players)
        {
            CarryConfig config = ExtractionRunData.Carry.Get(player);
            // A pre-durability saved carry deserializes with the 0 sentinel; backfill so the consume below matches the
            // warehouse's full-durability copies and the deposit decrements from full. 旧档携带以 0 哨兵反序列化；回填满耐久，
            // 让下方消耗能精确匹配仓库满耐久副本、撤离从满耐久递减。
            WarehouseStore.BackfillCarryDurability(config);

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

                Entry.Logger.Warn($"ExtractionModifier: player {player.NetId} carried no cards; granted starter deck " +
                                  $"({player.Deck.Cards.Count} cards).");
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

    /// <summary>Marks the placed `?` point so the map shows its quest marker (the 撤离点 icon is swapped in by the
    /// map-node patch). Runs after <c>ModifyGeneratedMapLate</c> on the same act entry. 给放置的 `?` 点挂标记，地图显示任务
    /// 角标（撤离点图标由地图节点补丁替换）。与 ModifyGeneratedMapLate 同一次章节入口先后执行。</summary>
    public override Task AfterMapGenerated(ActMap map, int actIndex)
    {
        if (MarkedCoord is { } coord && map.HasPoint(coord))
        {
            map.GetPoint(coord)?.AddQuest(this);
        }

        return Task.CompletedTask;
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
}
