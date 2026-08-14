namespace ExtractionRun.Settings;

/// <summary>
/// Global 搜打撤 settings (not per-profile). Persisted via RitsuLib ModDataStore at <c>SaveScope.Global</c>.
/// 搜打撤全局设置（不分存档位），经 ModDataStore 以 SaveScope.Global 持久化。
/// </summary>
public sealed class ExtractionSettings
{
    /// <summary>Maximum number of cards a player may carry into a run. 每局最多携带的卡牌数。</summary>
    public int MaxCarryCards { get; set; } = 10;

    /// <summary>Maximum number of relics a player may carry into a run. 每局最多携带的遗物数。</summary>
    public int MaxCarryRelics { get; set; } = 3;

    /// <summary>Whether hovering a card tile shows the vanilla tooltip. 悬停卡牌瓦片是否显示原版提示框。</summary>
    public bool ShowCardHoverTips { get; set; } = true;

    /// <summary>Whether hovering a relic tile shows the vanilla tooltip. 悬停遗物瓦片是否显示原版提示框。</summary>
    public bool ShowRelicHoverTips { get; set; } = true;

    /// <summary>Whether hovering a potion tile shows the vanilla tooltip. 悬停药水瓦片是否显示原版提示框。</summary>
    public bool ShowPotionHoverTips { get; set; } = true;

    /// <summary>
    /// Whether the durability system is active. ON: carried card/relic copies lose 1 durability per successful
    /// extraction and the active warehouse is the durability file. OFF: no durability tracking (copies never decrement,
    /// nothing is displayed) and the active warehouse is a disposable no-durability copy — toggling never merges
    /// no-durability progress back into the durability file (see <see cref="WarehouseStore.SwitchDurabilityMode"/>).
    /// 耐久系统是否启用。ON：携带的牌/遗物每次撤离成功 -1 耐久，活动仓库为耐久文件；OFF：不追踪耐久（副本永不递减、不显示），
    /// 活动仓库为一次性无耐久副本——切换永不把无耐久进度并入耐久文件（见 SwitchDurabilityMode）。
    /// </summary>
    public bool DurabilityEnabled { get; set; } = true;

    /// <summary>Max durability granted to a new Basic-rarity card. 新入基础卡牌的耐久上限。</summary>
    public int CardDurabilityBasic { get; set; } = 5;

    /// <summary>Max durability granted to a new Common-rarity card. 新入普通卡牌的耐久上限。</summary>
    public int CardDurabilityCommon { get; set; } = 4;

    /// <summary>Max durability granted to a new Uncommon-rarity card. 新入罕见卡牌的耐久上限。</summary>
    public int CardDurabilityUncommon { get; set; } = 3;

    /// <summary>Max durability granted to a new Rare-rarity card. 新入稀有卡牌的耐久上限。</summary>
    public int CardDurabilityRare { get; set; } = 2;

    /// <summary>Max durability granted to a new Ancient-rarity card. 新入先古卡牌的耐久上限。</summary>
    public int CardDurabilityAncient { get; set; } = 1;

    /// <summary>Max durability granted to a card in any other rarity (None/Event/Token/Status/Curse/Quest, mod cards,
    /// unresolvable ids). 其他稀有度（None/Event/Token/Status/Curse/Quest、mod 卡、解析不到）卡牌的耐久上限。</summary>
    public int CardDurabilityOther { get; set; } = 1;

    /// <summary>Max durability granted to a new relic (all relics share one value). 新入遗物的耐久上限（遗物统一）。</summary>
    public int RelicDurability { get; set; } = 3;

    /// <summary>
    /// Whether copies of the same card/relic with different durability show as separate tiles (each its own badge) or
    /// merge into one tile showing the worst copy. Display-only: never affects stored durability, consumption or gear
    /// codes. Default ON. 同种卡牌/遗物是否按耐久度独立显示——不同耐久的副本各占一块瓦片（各显其角标），还是合并为一块只显
    /// 最破的一份。纯显示：不影响存储耐久、消耗或战备码。默认开启。
    /// </summary>
    public bool SplitDurabilityGroups { get; set; } = true;

    /// <summary>
    /// Whether the backpack capacity system is active. ON: carry is limited by a unified capacity pool — cards cost by
    /// rarity, relics a flat amount, cards + relics share the same pool (potions/gold free). OFF: the legacy
    /// <see cref="MaxCarryCards"/>/<see cref="MaxCarryRelics"/> per-kind count caps apply.
    /// 背包容量系统是否启用。ON：携带受统一容量池限制——卡牌按稀有度占格、遗物统一占格，卡牌与遗物共享同一池（药水/金币不计）；
    /// OFF：回退到旧的 MaxCarryCards / MaxCarryRelics 每类数量上限。
    /// </summary>
    public bool CarryCapacityEnabled { get; set; } = true;

    /// <summary>Total backpack capacity in ON mode (cards + relics share the pool). 背包总容量（ON 模式下卡牌与遗物共享）。</summary>
    public int CarryCapacity { get; set; } = 15;

    /// <summary>Capacity cost of one Basic or Common card. 一张基础/普通卡牌占用的容量。</summary>
    public int CapacityWeightBasicCommon { get; set; } = 1;

    /// <summary>Capacity cost of one Uncommon card. 一张罕见卡牌占用的容量。</summary>
    public int CapacityWeightUncommon { get; set; } = 2;

    /// <summary>Capacity cost of one Rare card. 一张稀有卡牌占用的容量。</summary>
    public int CapacityWeightRare { get; set; } = 3;

    /// <summary>Capacity cost of one Ancient card. 一张先古卡牌占用的容量。</summary>
    public int CapacityWeightAncient { get; set; } = 4;

    /// <summary>Capacity cost of a card in any other rarity (None/Event/Token/Status/Curse/Quest, mod cards,
    /// unresolvable ids). 其他稀有度（None/Event/Token/Status/Curse/Quest、mod 卡、解析不到）卡牌占用的容量。</summary>
    public int CapacityWeightOther { get; set; } = 2;

    /// <summary>Capacity cost of one relic (all relics share one value). 一件遗物占用的容量（遗物统一）。</summary>
    public int CapacityWeightRelic { get; set; } = 2;

    /// <summary>Buy-price multiplier for the hub shop (rolled vanilla price × this). 商店买入价倍率（roll 价 × 此值）。</summary>
    public double ShopPriceMultiplier { get; set; } = 2.0;

    /// <summary>Sell ratio for the hub shop (deterministic vanilla base price × this, before the durability factor).
    /// 商店卖出比例（确定性原版基准价 × 此值，再乘耐久系数）。</summary>
    public double ShopSellRatio { get; set; } = 0.5;

    /// <summary>
    /// Whether the 搜刮 loot-search reveal plays on reward screens (card rewards, treasure chest) in
    /// extraction runs only. Pure cosmetic; default OFF.
    /// 搜刮动画是否启用：仅搜打撤局内，在卡牌奖励 / 宝箱界面播放搜刮揭示动画。纯视觉，默认关闭。
    /// </summary>
    public bool LootAnimationEnabled { get; set; } = false;

    /// <summary>Search duration (seconds) for Basic/Common-rarity items — card Basic+Common, relic Starter+Common,
    /// potion Common. 基础/普通稀有度物品的搜刮时长（秒）——卡 Basic+Common、遗物 Starter+Common、药水 Common。</summary>
    public int LootAnimationBasicCommonDuration { get; set; } = 1;

    /// <summary>Search duration (seconds) for Uncommon-rarity items. 罕见稀有度物品的搜刮时长（秒）。</summary>
    public int LootAnimationUncommonDuration { get; set; } = 2;

    /// <summary>Search duration (seconds) for Rare-rarity items. 稀有稀有度物品的搜刮时长（秒）。</summary>
    public int LootAnimationRareDuration { get; set; } = 4;

    /// <summary>Search duration (seconds) for Ancient-rarity items. 先古稀有度物品的搜刮时长（秒）。</summary>
    public int LootAnimationAncientDuration { get; set; } = 5;

    /// <summary>Search duration (seconds) for any other rarity (event/token/status/curse/quest/shop, unresolvable ids).
    /// 其他稀有度（事件/衍生/状态/诅咒/任务/商店、解析不到）物品的搜刮时长（秒）。</summary>
    public int LootAnimationOtherDuration { get; set; } = 2;

    /// <summary>Key that skips the whole remaining search sequence (RitsuLib hotkey binding string, e.g. "Space").
    /// 跳过整段剩余搜刮序列的按键（RitsuLib 热键绑定串，如 "Space"）。</summary>
    public string LootAnimationSkipKey { get; set; } = "Space";

    /// <summary>普通撤离的带出容量（稀有度重量制）——第一幕。撤离点事件三选项之「普通撤离」的容量上限。Host-authoritative。</summary>
    public int ExtractionPointCapacityAct1 { get; set; } = 15;

    /// <summary>普通撤离带出容量——第二幕。Host-authoritative。</summary>
    public int ExtractionPointCapacityAct2 { get; set; } = 25;

    /// <summary>普通撤离带出容量——第三幕及之后。Host-authoritative。</summary>
    public int ExtractionPointCapacityAct3 { get; set; } = 35;

    /// <summary>金币撤离基础费用（第一幕）。此后每幕复利 +ExtractionPointGoldFeeRate。Host-authoritative。</summary>
    public int ExtractionPointGoldFeeAct1 { get; set; } = 100;

    /// <summary>金币撤离费用每幕复利增幅（如 0.20 = 100→120→144）。Host-authoritative。</summary>
    public double ExtractionPointGoldFeeRate { get; set; } = 0.20;

    /// <summary>每幕出现撤离点事件的基础概率（0–1）。Host-authoritative——所有机器用同一概率保证放置 roll 一致。</summary>
    public double ExtractionPointActChance { get; set; } = 0.30;

    public void ResetToDefaults()
    {
        MaxCarryCards = 10;
        MaxCarryRelics = 3;
        ShowCardHoverTips = true;
        ShowRelicHoverTips = true;
        ShowPotionHoverTips = true;
        DurabilityEnabled = true;
        CardDurabilityBasic = 5;
        CardDurabilityCommon = 4;
        CardDurabilityUncommon = 3;
        CardDurabilityRare = 2;
        CardDurabilityAncient = 1;
        CardDurabilityOther = 1;
        RelicDurability = 3;
        SplitDurabilityGroups = true;
        CarryCapacityEnabled = true;
        CarryCapacity = 15;
        CapacityWeightBasicCommon = 1;
        CapacityWeightUncommon = 2;
        CapacityWeightRare = 3;
        CapacityWeightAncient = 4;
        CapacityWeightOther = 2;
        CapacityWeightRelic = 2;
        ShopPriceMultiplier = 2.0;
        ShopSellRatio = 0.5;
        LootAnimationEnabled = false;
        LootAnimationBasicCommonDuration = 1;
        LootAnimationUncommonDuration = 2;
        LootAnimationRareDuration = 4;
        LootAnimationAncientDuration = 5;
        LootAnimationOtherDuration = 2;
        LootAnimationSkipKey = "Space";
        ExtractionPointCapacityAct1 = 15;
        ExtractionPointCapacityAct2 = 25;
        ExtractionPointCapacityAct3 = 35;
        ExtractionPointGoldFeeAct1 = 100;
        ExtractionPointGoldFeeRate = 0.20;
        ExtractionPointActChance = 0.30;
    }
}
