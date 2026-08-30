using Godot;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Runs;
using STS2RitsuLib;
using STS2RitsuLib.Data;
using STS2RitsuLib.Settings;
using STS2RitsuLib.Ui.Toast;
using STS2RitsuLib.Utils.Persistence;
using ExtractionRun.Data;
using ExtractionRun.UI;

namespace ExtractionRun.Settings;

/// <summary>
/// Registers the 搜打撤 settings page: per-kind hover-tooltip toggles, the capacity section (backpack-capacity toggle +
/// rarity weights / legacy count caps, swapped by the toggle), the durability section (on/off toggle + per-rarity
/// durability caps), and a reset button. Values are bound to the
/// <see cref="ExtractionSettings"/> POCO via <see cref="ModSettingsValueBinding{TData,TValue}"/> at SaveScope.Global.
/// The durability toggle is confirm-gated: flipping it opens an <see cref="ExtractionConfirmDialog"/> (确定/取消) and is
/// blocked while a run or character-select lobby is active — a lobby has already staged the pending carry, which a mode
/// switch can't retract. Confirming calls <see cref="WarehouseStore.SwitchDurabilityMode"/>, which freezes/restores the
/// durability warehouse file and re-syncs the pending carry; cancelling writes the old value back.
/// 搜打撤设置页：悬停提示开关、背包容量区（容量开关 + 稀有度权重 / 旧数量上限，按开关互斥显示）、耐久区（开关 + 各稀有度耐久上限）、
/// 重置按钮，通过 ModSettingsValueBinding 绑定到 ExtractionSettings。耐久开关需确认弹窗：翻转时弹 ExtractionConfirmDialog（确定/取消），
/// 局内或角色选择大厅中禁止切换（大厅已暂存携带，切换无法收回）。确定时调用 SwitchDurabilityMode（冻结/还原耐久文件并重同步
/// 携带），取消时写回旧值。容量开关同样确认门控 + 局中阻断，但切换不动仓库文件（仅改携带限制，自然节点钳制重新应用）。
/// </summary>
public static class ExtractionSettingsPage
{
    public const string DataKey = "settings";

    private const int MaxCardsSlider = 20;
    private const int MaxRelicsSlider = 6;

    /// <summary>All durability caps share one slider range; defaults sit at 1–5. 各耐久上限共用同一滑条范围。</summary>
    private const int MaxDurabilitySlider = 20;
    private const int MinDurabilitySlider = 1;

    /// <summary>Backpack capacity slider range (default 15). 背包容量滑条范围（默认 15）。</summary>
    private const int MinCapacitySlider = 1;
    private const int MaxCapacitySlider = 30;

    /// <summary>All capacity weights share one slider range (min 1 — a free card would open a no-cost slot). 各容量权重共用
    /// 同一滑条范围（下限 1——0 权重等于白嫖一格）。</summary>
    private const int MinWeightSlider = 1;
    private const int MaxWeightSlider = 20;

    /// <summary>All loot-search durations share one slider range (min 1s). 各搜刮时长共用同一滑条范围（下限 1 秒）。</summary>
    private const int MinLootSlider = 1;
    private const int MaxLootSlider = 20;

    /// <summary>撤离点 ordinary-extraction capacity slider range. 撤离点「普通撤离」容量滑条范围。</summary>
    private const int MinExtractionPointCapacitySlider = 5;
    private const int MaxExtractionPointCapacitySlider = 99;

    /// <summary>撤离点 gold-fee base slider range. 撤离点「金币撤离」基础费用滑条范围。</summary>
    private const int MinExtractionPointFeeSlider = 0;
    private const int MaxExtractionPointFeeSlider = 999;

    /// <summary>Guards the durability-toggle handler against re-entry (the revert write re-fires ValueWritten) and
    /// against the reset button flipping the toggle without a confirm dialog. 耐久切换处理器防重入（回退写会再次触发
    /// ValueWritten），也用于重置按钮直接翻转开关而不弹确认框。</summary>
    private static bool _suppressDurabilityToggle;

    /// <summary>Same re-entry + reset guard for the capacity toggle. 容量开关的同类防重入/重置守卫。</summary>
    private static bool _suppressCapacityToggle;

    // ----- 存档管理 (save transfer) section state 存档管理区状态 -----

    /// <summary>The live status row of the save-transfer section (re-captured on every settings-page refresh).
    /// 存档管理区状态行（每次设置页刷新时重新捕获）。</summary>
    private static Label? _statusLabel;

    private static string? _lastExportPath;
    private static string? _lastBackupName;
    private static int _lastImportedSlotCount;

    private static readonly ModSettingsValueBinding<ExtractionSettings, int> MaxCardsBinding = new(
        Entry.ModId, DataKey, SaveScope.Global,
        static s => s.MaxCarryCards,
        static (s, v) => s.MaxCarryCards = v);

    private static readonly ModSettingsValueBinding<ExtractionSettings, int> MaxRelicsBinding = new(
        Entry.ModId, DataKey, SaveScope.Global,
        static s => s.MaxCarryRelics,
        static (s, v) => s.MaxCarryRelics = v);

    private static readonly ModSettingsValueBinding<ExtractionSettings, bool> ShowCardHoverTipsBinding = new(
        Entry.ModId, DataKey, SaveScope.Global,
        static s => s.ShowCardHoverTips,
        static (s, v) => s.ShowCardHoverTips = v);

    private static readonly ModSettingsValueBinding<ExtractionSettings, bool> ShowRelicHoverTipsBinding = new(
        Entry.ModId, DataKey, SaveScope.Global,
        static s => s.ShowRelicHoverTips,
        static (s, v) => s.ShowRelicHoverTips = v);

    private static readonly ModSettingsValueBinding<ExtractionSettings, bool> ShowPotionHoverTipsBinding = new(
        Entry.ModId, DataKey, SaveScope.Global,
        static s => s.ShowPotionHoverTips,
        static (s, v) => s.ShowPotionHoverTips = v);

    private static readonly ModSettingsValueBinding<ExtractionSettings, bool> DurabilityEnabledBinding = new(
        Entry.ModId, DataKey, SaveScope.Global,
        static s => s.DurabilityEnabled,
        static (s, v) => s.DurabilityEnabled = v);

    private static readonly ModSettingsValueBinding<ExtractionSettings, int> CardDurabilityBasicBinding = new(
        Entry.ModId, DataKey, SaveScope.Global,
        static s => s.CardDurabilityBasic,
        static (s, v) => s.CardDurabilityBasic = v);

    private static readonly ModSettingsValueBinding<ExtractionSettings, int> CardDurabilityCommonBinding = new(
        Entry.ModId, DataKey, SaveScope.Global,
        static s => s.CardDurabilityCommon,
        static (s, v) => s.CardDurabilityCommon = v);

    private static readonly ModSettingsValueBinding<ExtractionSettings, int> CardDurabilityUncommonBinding = new(
        Entry.ModId, DataKey, SaveScope.Global,
        static s => s.CardDurabilityUncommon,
        static (s, v) => s.CardDurabilityUncommon = v);

    private static readonly ModSettingsValueBinding<ExtractionSettings, int> CardDurabilityRareBinding = new(
        Entry.ModId, DataKey, SaveScope.Global,
        static s => s.CardDurabilityRare,
        static (s, v) => s.CardDurabilityRare = v);

    private static readonly ModSettingsValueBinding<ExtractionSettings, int> CardDurabilityAncientBinding = new(
        Entry.ModId, DataKey, SaveScope.Global,
        static s => s.CardDurabilityAncient,
        static (s, v) => s.CardDurabilityAncient = v);

    private static readonly ModSettingsValueBinding<ExtractionSettings, int> CardDurabilityOtherBinding = new(
        Entry.ModId, DataKey, SaveScope.Global,
        static s => s.CardDurabilityOther,
        static (s, v) => s.CardDurabilityOther = v);

    private static readonly ModSettingsValueBinding<ExtractionSettings, int> RelicDurabilityBinding = new(
        Entry.ModId, DataKey, SaveScope.Global,
        static s => s.RelicDurability,
        static (s, v) => s.RelicDurability = v);

    private static readonly ModSettingsValueBinding<ExtractionSettings, bool> SplitDurabilityBinding = new(
        Entry.ModId, DataKey, SaveScope.Global,
        static s => s.SplitDurabilityGroups,
        static (s, v) => s.SplitDurabilityGroups = v);

    private static readonly ModSettingsValueBinding<ExtractionSettings, bool> CapacityEnabledBinding = new(
        Entry.ModId, DataKey, SaveScope.Global,
        static s => s.CarryCapacityEnabled,
        static (s, v) => s.CarryCapacityEnabled = v);

    private static readonly ModSettingsValueBinding<ExtractionSettings, int> CapacityTotalBinding = new(
        Entry.ModId, DataKey, SaveScope.Global,
        static s => s.CarryCapacity,
        static (s, v) => s.CarryCapacity = v);

    private static readonly ModSettingsValueBinding<ExtractionSettings, int> CapacityBasicCommonBinding = new(
        Entry.ModId, DataKey, SaveScope.Global,
        static s => s.CapacityWeightBasicCommon,
        static (s, v) => s.CapacityWeightBasicCommon = v);

    private static readonly ModSettingsValueBinding<ExtractionSettings, int> CapacityUncommonBinding = new(
        Entry.ModId, DataKey, SaveScope.Global,
        static s => s.CapacityWeightUncommon,
        static (s, v) => s.CapacityWeightUncommon = v);

    private static readonly ModSettingsValueBinding<ExtractionSettings, int> CapacityRareBinding = new(
        Entry.ModId, DataKey, SaveScope.Global,
        static s => s.CapacityWeightRare,
        static (s, v) => s.CapacityWeightRare = v);

    private static readonly ModSettingsValueBinding<ExtractionSettings, int> CapacityAncientBinding = new(
        Entry.ModId, DataKey, SaveScope.Global,
        static s => s.CapacityWeightAncient,
        static (s, v) => s.CapacityWeightAncient = v);

    private static readonly ModSettingsValueBinding<ExtractionSettings, int> CapacityOtherBinding = new(
        Entry.ModId, DataKey, SaveScope.Global,
        static s => s.CapacityWeightOther,
        static (s, v) => s.CapacityWeightOther = v);

    private static readonly ModSettingsValueBinding<ExtractionSettings, int> CapacityRelicBinding = new(
        Entry.ModId, DataKey, SaveScope.Global,
        static s => s.CapacityWeightRelic,
        static (s, v) => s.CapacityWeightRelic = v);

    private static readonly ModSettingsValueBinding<ExtractionSettings, double> ShopPriceMultiplierBinding = new(
        Entry.ModId, DataKey, SaveScope.Global,
        static s => s.ShopPriceMultiplier,
        static (s, v) => s.ShopPriceMultiplier = v);

    private static readonly ModSettingsValueBinding<ExtractionSettings, double> ShopSellRatioBinding = new(
        Entry.ModId, DataKey, SaveScope.Global,
        static s => s.ShopSellRatio,
        static (s, v) => s.ShopSellRatio = v);

    private static readonly ModSettingsValueBinding<ExtractionSettings, bool> IncludeMultiplayerOnlyShopContentBinding = new(
        Entry.ModId, DataKey, SaveScope.Global,
        static s => s.IncludeMultiplayerOnlyShopContent,
        static (s, v) => s.IncludeMultiplayerOnlyShopContent = v);

    private static readonly ModSettingsValueBinding<ExtractionSettings, bool> LootEnabledBinding = new(
        Entry.ModId, DataKey, SaveScope.Global,
        static s => s.LootAnimationEnabled,
        static (s, v) => s.LootAnimationEnabled = v);

    private static readonly ModSettingsValueBinding<ExtractionSettings, int> LootBasicCommonBinding = new(
        Entry.ModId, DataKey, SaveScope.Global,
        static s => s.LootAnimationBasicCommonDuration,
        static (s, v) => s.LootAnimationBasicCommonDuration = v);

    private static readonly ModSettingsValueBinding<ExtractionSettings, int> LootUncommonBinding = new(
        Entry.ModId, DataKey, SaveScope.Global,
        static s => s.LootAnimationUncommonDuration,
        static (s, v) => s.LootAnimationUncommonDuration = v);

    private static readonly ModSettingsValueBinding<ExtractionSettings, int> LootRareBinding = new(
        Entry.ModId, DataKey, SaveScope.Global,
        static s => s.LootAnimationRareDuration,
        static (s, v) => s.LootAnimationRareDuration = v);

    private static readonly ModSettingsValueBinding<ExtractionSettings, int> LootAncientBinding = new(
        Entry.ModId, DataKey, SaveScope.Global,
        static s => s.LootAnimationAncientDuration,
        static (s, v) => s.LootAnimationAncientDuration = v);

    private static readonly ModSettingsValueBinding<ExtractionSettings, int> LootOtherBinding = new(
        Entry.ModId, DataKey, SaveScope.Global,
        static s => s.LootAnimationOtherDuration,
        static (s, v) => s.LootAnimationOtherDuration = v);

    private static readonly ModSettingsValueBinding<ExtractionSettings, string> LootSkipKeyBinding = new(
        Entry.ModId, DataKey, SaveScope.Global,
        static s => s.LootAnimationSkipKey,
        static (s, v) => s.LootAnimationSkipKey = v);

    private static readonly ModSettingsValueBinding<ExtractionSettings, int> ExtractionPointCapacityAct1Binding = new(
        Entry.ModId, DataKey, SaveScope.Global,
        static s => s.ExtractionPointCapacityAct1,
        static (s, v) => s.ExtractionPointCapacityAct1 = v);

    private static readonly ModSettingsValueBinding<ExtractionSettings, int> ExtractionPointCapacityAct2Binding = new(
        Entry.ModId, DataKey, SaveScope.Global,
        static s => s.ExtractionPointCapacityAct2,
        static (s, v) => s.ExtractionPointCapacityAct2 = v);

    private static readonly ModSettingsValueBinding<ExtractionSettings, int> ExtractionPointCapacityAct3Binding = new(
        Entry.ModId, DataKey, SaveScope.Global,
        static s => s.ExtractionPointCapacityAct3,
        static (s, v) => s.ExtractionPointCapacityAct3 = v);

    private static readonly ModSettingsValueBinding<ExtractionSettings, int> ExtractionPointGoldFeeAct1Binding = new(
        Entry.ModId, DataKey, SaveScope.Global,
        static s => s.ExtractionPointGoldFeeAct1,
        static (s, v) => s.ExtractionPointGoldFeeAct1 = v);

    private static readonly ModSettingsValueBinding<ExtractionSettings, double> ExtractionPointGoldFeeRateBinding = new(
        Entry.ModId, DataKey, SaveScope.Global,
        static s => s.ExtractionPointGoldFeeRate,
        static (s, v) => s.ExtractionPointGoldFeeRate = v);

    private static readonly ModSettingsValueBinding<ExtractionSettings, double> ExtractionPointActChanceBinding = new(
        Entry.ModId, DataKey, SaveScope.Global,
        static s => s.ExtractionPointActChance,
        static (s, v) => s.ExtractionPointActChance = v);

    public static ExtractionSettings Current =>
        RitsuLibFramework.GetDataStore(Entry.ModId).Get<ExtractionSettings>(DataKey);

    public static void Register()
    {
        ModDataStore.For(Entry.ModId).Register(
            key: DataKey,
            fileName: "settings.json",
            scope: SaveScope.Global,
            defaultFactory: () => new ExtractionSettings(),
            autoCreateIfMissing: true);

        RitsuLibFramework.RegisterModSettings(Entry.ModId, page => page
            .WithTitle(ExtractionLocalization.SettingsPageTitleText())
            .WithModDisplayName(ExtractionLocalization.ModTitleText())
            .WithVisibleOnHostSurfaces(ModSettingsHostSurface.All)
            .AddSection("general", section => section
                .WithTitle(ExtractionLocalization.GeneralSectionTitleText())
                .AddToggle("show_card_hover_tips", ExtractionLocalization.ShowCardHoverTipsText(),
                    ShowCardHoverTipsBinding, description: ExtractionLocalization.ShowCardHoverTipsDescriptionText())
                .AddToggle("show_relic_hover_tips", ExtractionLocalization.ShowRelicHoverTipsText(),
                    ShowRelicHoverTipsBinding, description: ExtractionLocalization.ShowRelicHoverTipsDescriptionText())
                .AddToggle("show_potion_hover_tips", ExtractionLocalization.ShowPotionHoverTipsText(),
                    ShowPotionHoverTipsBinding, description: ExtractionLocalization.ShowPotionHoverTipsDescriptionText()))
            .AddSection("capacity", section => section
                .WithTitle(ExtractionLocalization.CapacitySectionTitleText())
                .AddToggle("capacity_enabled", ExtractionLocalization.CapacityEnabledText(),
                    CapacityEnabledBinding, description: ExtractionLocalization.CapacityEnabledDescriptionText())
                // The two limit systems share one section and swap by the toggle: ON shows the capacity pool + rarity
                // weights, OFF shows the legacy per-kind count caps. RitsuLib re-evaluates WithEntryVisibleWhen on every
                // settings-screen refresh, so a flip swaps the visible set immediately.
                // 两套限制系统共用一个 section，按开关互斥显示：ON 显示容量池 + 稀有度权重，OFF 显示旧的每类数量上限。
                .AddIntSlider("capacity_total", ExtractionLocalization.CapacityTotalText(),
                    CapacityTotalBinding, MinCapacitySlider, MaxCapacitySlider, 1,
                    description: ExtractionLocalization.CapacityTotalDescriptionText())
                    .WithEntryVisibleWhen("capacity_total", () => Current.CarryCapacityEnabled)
                .AddIntSlider("capacity_weight_basic_common", ExtractionLocalization.CapacityBasicCommonText(),
                    CapacityBasicCommonBinding, MinWeightSlider, MaxWeightSlider, 1,
                    description: ExtractionLocalization.CapacityBasicCommonDescriptionText())
                    .WithEntryVisibleWhen("capacity_weight_basic_common", () => Current.CarryCapacityEnabled)
                .AddIntSlider("capacity_weight_uncommon", ExtractionLocalization.CapacityUncommonText(),
                    CapacityUncommonBinding, MinWeightSlider, MaxWeightSlider, 1,
                    description: ExtractionLocalization.CapacityUncommonDescriptionText())
                    .WithEntryVisibleWhen("capacity_weight_uncommon", () => Current.CarryCapacityEnabled)
                .AddIntSlider("capacity_weight_rare", ExtractionLocalization.CapacityRareText(),
                    CapacityRareBinding, MinWeightSlider, MaxWeightSlider, 1,
                    description: ExtractionLocalization.CapacityRareDescriptionText())
                    .WithEntryVisibleWhen("capacity_weight_rare", () => Current.CarryCapacityEnabled)
                .AddIntSlider("capacity_weight_ancient", ExtractionLocalization.CapacityAncientText(),
                    CapacityAncientBinding, MinWeightSlider, MaxWeightSlider, 1,
                    description: ExtractionLocalization.CapacityAncientDescriptionText())
                    .WithEntryVisibleWhen("capacity_weight_ancient", () => Current.CarryCapacityEnabled)
                .AddIntSlider("capacity_weight_other", ExtractionLocalization.CapacityOtherText(),
                    CapacityOtherBinding, MinWeightSlider, MaxWeightSlider, 1,
                    description: ExtractionLocalization.CapacityOtherDescriptionText())
                    .WithEntryVisibleWhen("capacity_weight_other", () => Current.CarryCapacityEnabled)
                .AddIntSlider("capacity_weight_relic", ExtractionLocalization.CapacityRelicText(),
                    CapacityRelicBinding, MinWeightSlider, MaxWeightSlider, 1,
                    description: ExtractionLocalization.CapacityRelicDescriptionText())
                    .WithEntryVisibleWhen("capacity_weight_relic", () => Current.CarryCapacityEnabled)
                .AddIntSlider("max_cards", ExtractionLocalization.MaxCardsText(), MaxCardsBinding,
                    0, MaxCardsSlider, 1, description: ExtractionLocalization.MaxCardsDescriptionText())
                    .WithEntryVisibleWhen("max_cards", () => !Current.CarryCapacityEnabled)
                .AddIntSlider("max_relics", ExtractionLocalization.MaxRelicsText(), MaxRelicsBinding,
                    0, MaxRelicsSlider, 1, description: ExtractionLocalization.MaxRelicsDescriptionText())
                    .WithEntryVisibleWhen("max_relics", () => !Current.CarryCapacityEnabled))
            .AddSection("durability", section => section
                .WithTitle(ExtractionLocalization.DurabilitySectionTitleText())
                .AddToggle("durability_enabled", ExtractionLocalization.DurabilityEnabledText(),
                    DurabilityEnabledBinding, description: ExtractionLocalization.DurabilityEnabledDescriptionText())
                .AddIntSlider("durability_basic", ExtractionLocalization.DurabilityBasicText(),
                    CardDurabilityBasicBinding, MinDurabilitySlider, MaxDurabilitySlider, 1,
                    description: ExtractionLocalization.DurabilityBasicDescriptionText())
                .AddIntSlider("durability_common", ExtractionLocalization.DurabilityCommonText(),
                    CardDurabilityCommonBinding, MinDurabilitySlider, MaxDurabilitySlider, 1,
                    description: ExtractionLocalization.DurabilityCommonDescriptionText())
                .AddIntSlider("durability_uncommon", ExtractionLocalization.DurabilityUncommonText(),
                    CardDurabilityUncommonBinding, MinDurabilitySlider, MaxDurabilitySlider, 1,
                    description: ExtractionLocalization.DurabilityUncommonDescriptionText())
                .AddIntSlider("durability_rare", ExtractionLocalization.DurabilityRareText(),
                    CardDurabilityRareBinding, MinDurabilitySlider, MaxDurabilitySlider, 1,
                    description: ExtractionLocalization.DurabilityRareDescriptionText())
                .AddIntSlider("durability_ancient", ExtractionLocalization.DurabilityAncientText(),
                    CardDurabilityAncientBinding, MinDurabilitySlider, MaxDurabilitySlider, 1,
                    description: ExtractionLocalization.DurabilityAncientDescriptionText())
                .AddIntSlider("durability_other", ExtractionLocalization.DurabilityOtherText(),
                    CardDurabilityOtherBinding, MinDurabilitySlider, MaxDurabilitySlider, 1,
                    description: ExtractionLocalization.DurabilityOtherDescriptionText())
                .AddIntSlider("durability_relic", ExtractionLocalization.DurabilityRelicText(),
                    RelicDurabilityBinding, MinDurabilitySlider, MaxDurabilitySlider, 1,
                    description: ExtractionLocalization.DurabilityRelicDescriptionText())
                // Display-only — no confirm, no run/lobby block, safe to flip mid-session (only affects the next
                // refresh). Hidden while durability is OFF, where the split is a no-op (every copy sits at rarity max).
                // 纯显示——无需确认、局内可改（只影响下次刷新）。耐久关闭时隐藏（此时拆分无意义——所有副本都在稀有度上限）。
                .AddToggle("durability_split", ExtractionLocalization.SplitDurabilityText(),
                    SplitDurabilityBinding, description: ExtractionLocalization.SplitDurabilityDescriptionText())
                    .WithEntryVisibleWhen("durability_split", () => Current.DurabilityEnabled))
            .AddSection("shop", section => section
                .WithTitle(ExtractionLocalization.ShopSectionTitleText())
                .AddSlider("shop_buy_multiplier", ExtractionLocalization.ShopPriceMultiplierText(),
                    ShopPriceMultiplierBinding, 1.0, 5.0, 0.1,
                    static d => $"×{d:0.0}", description: ExtractionLocalization.ShopPriceMultiplierDescriptionText())
                .AddSlider("shop_sell_ratio", ExtractionLocalization.ShopSellRatioText(),
                    ShopSellRatioBinding, 0.1, 1.0, 0.05,
                    static d => $"{d:P0}", description: ExtractionLocalization.ShopSellRatioDescriptionText())
                .AddToggle("shop_multiplayer_content", ExtractionLocalization.ShopMultiplayerContentText(),
                    IncludeMultiplayerOnlyShopContentBinding,
                    description: ExtractionLocalization.ShopMultiplayerContentDescriptionText()))
            .AddSection("loot", section => section
                .WithTitle(ExtractionLocalization.LootSectionTitleText())
                // Pure cosmetic — flipping the toggle needs no confirm and is safe mid-run (only affects the next screen
                // opening). The durations + skip key hide until the toggle is on, like the capacity/durability swap.
                // 纯视觉——切换无需确认、局内可改（只影响下次开屏）。时长与跳过键在开关开启前隐藏（同容量/耐久的互斥显示）。
                .AddToggle("loot_enabled", ExtractionLocalization.LootEnabledText(),
                    LootEnabledBinding, description: ExtractionLocalization.LootEnabledDescriptionText())
                .AddIntSlider("loot_basic_common", ExtractionLocalization.LootBasicCommonText(),
                    LootBasicCommonBinding, MinLootSlider, MaxLootSlider, 1,
                    description: ExtractionLocalization.LootBasicCommonDescriptionText())
                    .WithEntryVisibleWhen("loot_basic_common", () => Current.LootAnimationEnabled)
                .AddIntSlider("loot_uncommon", ExtractionLocalization.LootUncommonText(),
                    LootUncommonBinding, MinLootSlider, MaxLootSlider, 1,
                    description: ExtractionLocalization.LootUncommonDescriptionText())
                    .WithEntryVisibleWhen("loot_uncommon", () => Current.LootAnimationEnabled)
                .AddIntSlider("loot_rare", ExtractionLocalization.LootRareText(),
                    LootRareBinding, MinLootSlider, MaxLootSlider, 1,
                    description: ExtractionLocalization.LootRareDescriptionText())
                    .WithEntryVisibleWhen("loot_rare", () => Current.LootAnimationEnabled)
                .AddIntSlider("loot_ancient", ExtractionLocalization.LootAncientText(),
                    LootAncientBinding, MinLootSlider, MaxLootSlider, 1,
                    description: ExtractionLocalization.LootAncientDescriptionText())
                    .WithEntryVisibleWhen("loot_ancient", () => Current.LootAnimationEnabled)
                .AddIntSlider("loot_other", ExtractionLocalization.LootOtherText(),
                    LootOtherBinding, MinLootSlider, MaxLootSlider, 1,
                    description: ExtractionLocalization.LootOtherDescriptionText())
                    .WithEntryVisibleWhen("loot_other", () => Current.LootAnimationEnabled)
                .AddKeyBinding("loot_skip_key", ExtractionLocalization.LootSkipKeyText(),
                    LootSkipKeyBinding, allowModifierOnly: false,
                    description: ExtractionLocalization.LootSkipKeyDescriptionText())
                    .WithEntryVisibleWhen("loot_skip_key", () => Current.LootAnimationEnabled))
            .AddSection("extraction_point", section => section
                .WithTitle(ExtractionLocalization.ExtractionPointSectionTitleText())
                // Host-authoritative: the numbers a client enforces come from the host's machine, synced via the
                // custom settings message; they're locked while a run/lobby is active (see the networking rule).
                // 主机权威：客机执行的是主机的数值（经自定义设置消息同步）；局内/大厅中锁定。
                .AddIntSlider("extraction_point_capacity_act1",
                    ExtractionLocalization.ExtractionPointCapacityAct1Text(),
                    ExtractionPointCapacityAct1Binding, MinExtractionPointCapacitySlider, MaxExtractionPointCapacitySlider, 1,
                    description: ExtractionLocalization.ExtractionPointCapacityAct1DescriptionText())
                .AddIntSlider("extraction_point_capacity_act2",
                    ExtractionLocalization.ExtractionPointCapacityAct2Text(),
                    ExtractionPointCapacityAct2Binding, MinExtractionPointCapacitySlider, MaxExtractionPointCapacitySlider, 1,
                    description: ExtractionLocalization.ExtractionPointCapacityAct2DescriptionText())
                .AddIntSlider("extraction_point_capacity_act3",
                    ExtractionLocalization.ExtractionPointCapacityAct3Text(),
                    ExtractionPointCapacityAct3Binding, MinExtractionPointCapacitySlider, MaxExtractionPointCapacitySlider, 1,
                    description: ExtractionLocalization.ExtractionPointCapacityAct3DescriptionText())
                .AddIntSlider("extraction_point_gold_fee_act1",
                    ExtractionLocalization.ExtractionPointGoldFeeAct1Text(),
                    ExtractionPointGoldFeeAct1Binding, MinExtractionPointFeeSlider, MaxExtractionPointFeeSlider, 1,
                    description: ExtractionLocalization.ExtractionPointGoldFeeAct1DescriptionText())
                .AddSlider("extraction_point_gold_fee_rate",
                    ExtractionLocalization.ExtractionPointGoldFeeRateText(),
                    ExtractionPointGoldFeeRateBinding, 0.0, 1.0, 0.05,
                    static d => $"{d:P0}", description: ExtractionLocalization.ExtractionPointGoldFeeRateDescriptionText())
                .AddSlider("extraction_point_act_chance",
                    ExtractionLocalization.ExtractionPointActChanceText(),
                    ExtractionPointActChanceBinding, 0.0, 1.0, 0.05,
                    static d => $"{d:P0}", description: ExtractionLocalization.ExtractionPointActChanceDescriptionText()))
            .AddSection("save_transfer", section => section
                .WithTitle(ExtractionLocalization.SaveTransferSectionTitleText())
                // Export is always allowed (reads the live instances); import is press-time guarded to the main menu
                // outside a run/lobby (the settings page itself can be opened anywhere, incl. via the hotkey).
                // 导出随时允许（读活实例）；导入在按下时护栏：仅限主菜单且非局内/大厅（设置页本身可在任意界面打开，含热键）。
                .AddButton(
                    "save_export",
                    ExtractionLocalization.SaveExportLabelText(),
                    ExtractionLocalization.SaveExportButtonText(),
                    ExportSaveClicked,
                    ModSettingsButtonTone.Normal,
                    ExtractionLocalization.SaveExportDescriptionText())
                .AddButton(
                    "save_import",
                    ExtractionLocalization.SaveImportLabelText(),
                    ExtractionLocalization.SaveImportButtonText(),
                    ImportSaveClicked,
                    ModSettingsButtonTone.Danger,
                    ExtractionLocalization.SaveImportDescriptionText())
                .AddCustom(
                    "save_status",
                    ExtractionLocalization.SaveStatusLabelText(),
                    _ => CreateStatusControl(),
                    ExtractionLocalization.SaveStatusDescriptionText()))
            .AddSection("reset", section => section
                .WithTitle(ExtractionLocalization.ResetSectionTitleText())
                .AddButton(
                    "reset_defaults",
                    ExtractionLocalization.ResetButtonLabelText(),
                    ExtractionLocalization.ResetButtonText(),
                    ResetToDefaults,
                    ModSettingsButtonTone.Danger,
                    ExtractionLocalization.ResetButtonDescriptionText())));

        ModSettingsBindingWriteEvents.ValueWritten += OnBindingValueWritten;
    }

    /// <summary>
    /// Routes binding writes to the confirm-gated toggle handlers (durability / capacity). Both are gated on their
    /// suppress flag so the confirm-dialog revert and the reset button never re-enter the handler they are reverting.
    /// 把绑定写入路由到带确认门控的开关处理器（耐久 / 容量）。两者都以其 suppress 标志门控，保证确认框回退与重置按钮写回时
    /// 不会再次进入正在回退的那个处理器。
    /// </summary>
    private static void OnBindingValueWritten(IModSettingsBinding binding)
    {
        if (!_suppressDurabilityToggle && binding == DurabilityEnabledBinding)
        {
            HandleDurabilityToggle();
            return;
        }

        if (!_suppressCapacityToggle && binding == CapacityEnabledBinding)
        {
            HandleCapacityToggle();
        }
    }

    /// <summary>
    /// Confirm-gates the durability toggle. Runs synchronously after the binding flips <c>DurabilityEnabled</c>, so the
    /// old value is its inverse. While a run/lobby is active (the pending carry was already staged) the flip is reverted
    /// immediately; otherwise an <see cref="ExtractionConfirmDialog"/> asks 确定/取消 — confirm calls the warehouse mode
    /// switch, cancel writes the old value back (guarded so the revert doesn't re-enter this handler).
    /// 对耐久开关做确认门控。在绑定翻转 DurabilityEnabled 后同步触发，旧值即其反相。局内/大厅（携带已暂存）时立即回退；
    /// 否则弹确认框——确定时调用仓库模式切换，取消时写回旧值（以防重入守卫，回退写不会再次进入本处理器）。
    /// </summary>
    private static void HandleDurabilityToggle()
    {
        bool newValue = DurabilityEnabledBinding.Read();
        bool oldValue = !newValue;

        if (IsRunOrLobbyActive() || WarehouseHubScreen.Current != null)
        {
            // A run/lobby has already staged the pending carry, and an open hub holds a captured warehouse instance —
            // neither can be re-pointed by a mode switch. Revert the flip.
            // 局内/大厅已暂存携带，打开中的仓库大厅持有捕获的仓库实例——模式切换都无法收回。回退翻转。
            _suppressDurabilityToggle = true;
            try
            {
                DurabilityEnabledBinding.Write(oldValue);
                DurabilityEnabledBinding.Save();
            }
            finally
            {
                _suppressDurabilityToggle = false;
            }

            RitsuToastService.ShowInfo(ExtractionLocalization.DurabilityBlockedText());
            return;
        }

        _suppressDurabilityToggle = true;
        var dialog = new ExtractionConfirmDialog(
            newValue
                ? ExtractionLocalization.DurabilityEnableHeaderText()
                : ExtractionLocalization.DurabilityDisableHeaderText(),
            newValue
                ? ExtractionLocalization.DurabilityEnableBodyText()
                : ExtractionLocalization.DurabilityDisableBodyText(),
            () =>
            {
                WarehouseStore.SwitchDurabilityMode(newValue);
                _suppressDurabilityToggle = false;
            },
            () =>
            {
                DurabilityEnabledBinding.Write(oldValue);
                DurabilityEnabledBinding.Save();
                _suppressDurabilityToggle = false;
            });
        AddOverlay(dialog);
    }

    /// <summary>
    /// Confirm-gates the capacity toggle. A capacity switch never moves warehouse files (unlike durability) — it only
    /// changes the carry limit, which the natural-node clamp re-applies on the next hub open / confirm. While a run/lobby
    /// is active (the pending carry was already staged at the old limit) the flip is reverted immediately; otherwise an
    /// <see cref="ExtractionConfirmDialog"/> asks 确定/取消 — cancel writes the old value back, confirm just clears the
    /// guard (the pending carry is re-clamped from the new limit on next use).
    /// 对容量开关做确认门控。容量切换不动仓库文件（不同于耐久）——只改携带限制，由自然节点钳制在下次打开仓库/确认时重新应用。
    /// 局内/大厅（携带已按旧限制暂存）时立即回退；否则弹确认框——取消写回旧值，确定仅清守卫（待发携带下次使用时按新限制重钳）。
    /// </summary>
    private static void HandleCapacityToggle()
    {
        bool newValue = CapacityEnabledBinding.Read();
        bool oldValue = !newValue;

        if (IsRunOrLobbyActive() || WarehouseHubScreen.Current != null)
        {
            _suppressCapacityToggle = true;
            try
            {
                CapacityEnabledBinding.Write(oldValue);
                CapacityEnabledBinding.Save();
            }
            finally
            {
                _suppressCapacityToggle = false;
            }

            RitsuToastService.ShowInfo(ExtractionLocalization.CapacityBlockedText());
            return;
        }

        _suppressCapacityToggle = true;
        var dialog = new ExtractionConfirmDialog(
            newValue
                ? ExtractionLocalization.CapacityEnableHeaderText()
                : ExtractionLocalization.CapacityDisableHeaderText(),
            newValue
                ? ExtractionLocalization.CapacityEnableBodyText()
                : ExtractionLocalization.CapacityDisableBodyText(),
            () => _suppressCapacityToggle = false,
            () =>
            {
                CapacityEnabledBinding.Write(oldValue);
                CapacityEnabledBinding.Save();
                _suppressCapacityToggle = false;
            });
        AddOverlay(dialog);
    }

    /// <summary>Adds a high-layer overlay dialog to the scene root (the settings screen may be open without NGame).
    /// 把高层覆盖弹窗加到场景根（设置页可能在无 NGame 的主菜单打开）。</summary>
    private static void AddOverlay(Node overlay)
    {
        try
        {
            if (Engine.GetMainLoop() is SceneTree tree)
            {
                tree.Root.AddChild(overlay);
                return;
            }
        }
        catch (Exception)
        {
            // Fall through to NGame below.
        }

        if (NGame.Instance is NGame game)
        {
            game.AddChild(overlay);
        }
    }

    private static void ResetToDefaults(IModSettingsUiActionHost host)
    {
        bool oldEnabled = Current.DurabilityEnabled;
        Current.ResetToDefaults();

        _suppressDurabilityToggle = true;
        _suppressCapacityToggle = true;
        try
        {
            DurabilityEnabledBinding.Write(Current.DurabilityEnabled);
            DurabilityEnabledBinding.Save();
            MaxCardsBinding.Write(Current.MaxCarryCards);
            MaxCardsBinding.Save();
            MaxRelicsBinding.Write(Current.MaxCarryRelics);
            MaxRelicsBinding.Save();
            CapacityEnabledBinding.Write(Current.CarryCapacityEnabled);
            CapacityEnabledBinding.Save();
            CapacityTotalBinding.Write(Current.CarryCapacity);
            CapacityTotalBinding.Save();
            CapacityBasicCommonBinding.Write(Current.CapacityWeightBasicCommon);
            CapacityBasicCommonBinding.Save();
            CapacityUncommonBinding.Write(Current.CapacityWeightUncommon);
            CapacityUncommonBinding.Save();
            CapacityRareBinding.Write(Current.CapacityWeightRare);
            CapacityRareBinding.Save();
            CapacityAncientBinding.Write(Current.CapacityWeightAncient);
            CapacityAncientBinding.Save();
            CapacityOtherBinding.Write(Current.CapacityWeightOther);
            CapacityOtherBinding.Save();
            CapacityRelicBinding.Write(Current.CapacityWeightRelic);
            CapacityRelicBinding.Save();
            ShowCardHoverTipsBinding.Write(Current.ShowCardHoverTips);
            ShowCardHoverTipsBinding.Save();
            ShowRelicHoverTipsBinding.Write(Current.ShowRelicHoverTips);
            ShowRelicHoverTipsBinding.Save();
            ShowPotionHoverTipsBinding.Write(Current.ShowPotionHoverTips);
            ShowPotionHoverTipsBinding.Save();
            CardDurabilityBasicBinding.Write(Current.CardDurabilityBasic);
            CardDurabilityBasicBinding.Save();
            CardDurabilityCommonBinding.Write(Current.CardDurabilityCommon);
            CardDurabilityCommonBinding.Save();
            CardDurabilityUncommonBinding.Write(Current.CardDurabilityUncommon);
            CardDurabilityUncommonBinding.Save();
            CardDurabilityRareBinding.Write(Current.CardDurabilityRare);
            CardDurabilityRareBinding.Save();
            CardDurabilityAncientBinding.Write(Current.CardDurabilityAncient);
            CardDurabilityAncientBinding.Save();
            CardDurabilityOtherBinding.Write(Current.CardDurabilityOther);
            CardDurabilityOtherBinding.Save();
            RelicDurabilityBinding.Write(Current.RelicDurability);
            RelicDurabilityBinding.Save();
            SplitDurabilityBinding.Write(Current.SplitDurabilityGroups);
            SplitDurabilityBinding.Save();
            ShopPriceMultiplierBinding.Write(Current.ShopPriceMultiplier);
            ShopPriceMultiplierBinding.Save();
            ShopSellRatioBinding.Write(Current.ShopSellRatio);
            ShopSellRatioBinding.Save();
            IncludeMultiplayerOnlyShopContentBinding.Write(Current.IncludeMultiplayerOnlyShopContent);
            IncludeMultiplayerOnlyShopContentBinding.Save();
            LootEnabledBinding.Write(Current.LootAnimationEnabled);
            LootEnabledBinding.Save();
            LootBasicCommonBinding.Write(Current.LootAnimationBasicCommonDuration);
            LootBasicCommonBinding.Save();
            LootUncommonBinding.Write(Current.LootAnimationUncommonDuration);
            LootUncommonBinding.Save();
            LootRareBinding.Write(Current.LootAnimationRareDuration);
            LootRareBinding.Save();
            LootAncientBinding.Write(Current.LootAnimationAncientDuration);
            LootAncientBinding.Save();
            LootOtherBinding.Write(Current.LootAnimationOtherDuration);
            LootOtherBinding.Save();
            LootSkipKeyBinding.Write(Current.LootAnimationSkipKey);
            LootSkipKeyBinding.Save();
            ExtractionPointCapacityAct1Binding.Write(Current.ExtractionPointCapacityAct1);
            ExtractionPointCapacityAct1Binding.Save();
            ExtractionPointCapacityAct2Binding.Write(Current.ExtractionPointCapacityAct2);
            ExtractionPointCapacityAct2Binding.Save();
            ExtractionPointCapacityAct3Binding.Write(Current.ExtractionPointCapacityAct3);
            ExtractionPointCapacityAct3Binding.Save();
            ExtractionPointGoldFeeAct1Binding.Write(Current.ExtractionPointGoldFeeAct1);
            ExtractionPointGoldFeeAct1Binding.Save();
            ExtractionPointGoldFeeRateBinding.Write(Current.ExtractionPointGoldFeeRate);
            ExtractionPointGoldFeeRateBinding.Save();
            ExtractionPointActChanceBinding.Write(Current.ExtractionPointActChance);
            ExtractionPointActChanceBinding.Save();
        }
        finally
        {
            _suppressDurabilityToggle = false;
            _suppressCapacityToggle = false;
        }

        // Reset flips durability back to ON without a confirm dialog — apply the mode switch directly if it changed.
        // 重置直接把耐久恢复到 ON，不弹确认框——若模式确实变化则直接应用切换。
        if (oldEnabled != Current.DurabilityEnabled)
        {
            WarehouseStore.SwitchDurabilityMode(Current.DurabilityEnabled);
        }
    }

    // ----- 存档管理 (save transfer) handlers 存档管理处理器 -----

    /// <summary>
    /// Creates the status row control for the save-transfer section and captures it so export/import results can be
    /// shown immediately. RitsuLib re-invokes the factory on every settings-page refresh (the captured instance always
    /// matches the live page). 创建存档管理区的状态行控件并捕获引用，供导出/导入后立即刷新。RitsuLib 每次刷新设置页都会重跑
    /// 工厂（重新捕获），因此引用始终对应当前页面。
    /// </summary>
    private static Control CreateStatusControl()
    {
        var label = new Label
        {
            Text = BuildStatusText(),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        label.AddThemeColorOverride("font_color", ExtractionTheme.TextSecondary);
        label.AddThemeFontSizeOverride("font_size", ExtractionTheme.FontSizeBody);
        _statusLabel = label;
        return label;
    }

    private static void UpdateStatus()
    {
        if (_statusLabel != null && GodotObject.IsInstanceValid(_statusLabel))
        {
            _statusLabel.Text = BuildStatusText();
        }
    }

    /// <summary>Status text: cloud-sync toggle state plus the latest export / import / backup results.
    /// 状态文本：云同步开关状态 + 最近一次导出/导入/备份结果。</summary>
    private static string BuildStatusText()
    {
        var parts = new List<string> { ExtractionLocalization.SaveStatusCloudText(SaveTransfer.IsCloudSyncEnabled()) };
        if (_lastExportPath != null)
        {
            parts.Add(ExtractionLocalization.SaveStatusLastExportText(_lastExportPath));
        }

        if (_lastImportedSlotCount > 0)
        {
            parts.Add(ExtractionLocalization.SaveStatusLastImportText(_lastImportedSlotCount));
        }

        if (_lastBackupName != null)
        {
            parts.Add(ExtractionLocalization.SaveStatusLastBackupText(_lastBackupName));
        }

        return string.Join("\n", parts);
    }

    /// <summary>Suggested file name for the export dialog (profile-scoped + timestamp). 导出对话框的默认文件名。</summary>
    private static string DefaultExportFileName()
    {
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        return $"ExtractionRun_profile{SaveTransfer.CurrentProfileId}_{stamp}.zip";
    }

    private static void ExportSaveClicked(IModSettingsUiActionHost host)
    {
        if (Engine.GetMainLoop() is not SceneTree { Root: not null })
        {
            RitsuToastService.ShowWarning(ExtractionLocalization.SaveExportFailedText());
            return;
        }

        var dialog = new FileDialog
        {
            Title = ExtractionLocalization.SaveExportDialogTitleText(),
            FileMode = FileDialog.FileModeEnum.SaveFile,
            Access = FileDialog.AccessEnum.Filesystem,
            CurrentFile = DefaultExportFileName(),
        };
        dialog.AddFilter("*.zip", "ZIP");
        dialog.FileSelected += path =>
        {
            dialog.QueueFree();
            try
            {
                SaveTransfer.ExportTo(path);
                _lastExportPath = path;
                UpdateStatus();
                RitsuToastService.ShowInfo(ExtractionLocalization.SaveExportDoneText());
            }
            catch (Exception ex)
            {
                Entry.Logger.Error($"SaveTransfer export failed: {ex}");
                RitsuToastService.ShowError(ExtractionLocalization.SaveExportFailedText());
            }
        };
        PopupFileDialog(dialog);
    }

    private static void ImportSaveClicked(IModSettingsUiActionHost host)
    {
        if (IsRunOrLobbyActive() || WarehouseHubScreen.Current != null)
        {
            RitsuToastService.ShowWarning(ExtractionLocalization.SaveImportBlockedText());
            return;
        }

        if (Engine.GetMainLoop() is not SceneTree { Root: not null })
        {
            RitsuToastService.ShowWarning(ExtractionLocalization.SaveImportBlockedText());
            return;
        }

        var dialog = new FileDialog
        {
            Title = ExtractionLocalization.SaveImportDialogTitleText(),
            FileMode = FileDialog.FileModeEnum.OpenFile,
            Access = FileDialog.AccessEnum.Filesystem,
        };
        dialog.AddFilter("*.zip", "ZIP");
        dialog.FileSelected += path =>
        {
            dialog.QueueFree();
            BeginImport(path, host);
        };
        PopupFileDialog(dialog);
    }

    /// <summary>
    /// Shows a <see cref="FileDialog"/> as an embedded modal over the settings overlay, mirroring RitsuLib's native-file
    /// dialog chrome: a shielded CanvasLayer holds the dialog so <c>PopupCenteredRatio</c> works (a Godot Window must be
    /// inside the tree to pop up), the shield traps clicks so the settings UI behind stays inert, and everything is
    /// cleaned up when the dialog closes. 以嵌入模态方式在设置覆盖层之上弹出 FileDialog（镜像 RitsuLib 的原生文件对话框
    /// chrome）：带屏蔽层的 CanvasLayer 承载对话框（Godot Window 必须在树内才能弹窗），屏蔽层拦截点击使背后的设置界面
    /// 不可操作，对话框关闭时整体清理。
    /// </summary>
    private static void PopupFileDialog(FileDialog dialog)
    {
        if (Engine.GetMainLoop() is not SceneTree { Root: not null } tree)
        {
            dialog.QueueFree();
            return;
        }

        var layer = new CanvasLayer { Name = "SaveTransferFileDialogModal", Layer = 300 };
        tree.Root.AddChild(layer);

        var shield = new Control
        {
            Name = "SaveTransferFileDialogShield",
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        shield.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(shield);

        var dim = new ColorRect
        {
            Name = "SaveTransferFileDialogDim",
            Color = new Color(0f, 0f, 0f, 0.45f),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        shield.AddChild(dim);

        layer.AddChild(dialog);
        dialog.Name = "SaveTransferNativeFileDialog";
        dialog.Exclusive = true;
        dialog.Unresizable = false;
        dialog.MinSize = new Vector2I(760, 520);
        dialog.Size = dialog.MinSize;

        dialog.Canceled += () => CloseFileDialogChrome(layer, dialog);
        dialog.CloseRequested += () => CloseFileDialogChrome(layer, dialog);
        dialog.TreeExiting += () =>
        {
            if (GodotObject.IsInstanceValid(layer))
            {
                layer.QueueFree();
            }
        };

        dialog.PopupCenteredRatio(0.68f);
    }

    private static void CloseFileDialogChrome(CanvasLayer layer, FileDialog dialog)
    {
        if (GodotObject.IsInstanceValid(dialog))
        {
            dialog.QueueFree();
        }

        if (GodotObject.IsInstanceValid(layer))
        {
            layer.QueueFree();
        }
    }

    /// <summary>
    /// Validates the selected archive (nothing is written yet), then shows the 覆盖/合并/取消 confirm dialog listing the
    /// slots that would change. 校验所选包（此时不写任何数据），然后弹出「覆盖/合并/取消」确认框并列出将变化的槽。
    /// </summary>
    private static void BeginImport(string path, IModSettingsUiActionHost host)
    {
        List<SaveTransfer.ImportedSlot> slots;
        try
        {
            slots = SaveTransfer.ReadAndValidate(path);
        }
        catch (SaveTransfer.SaveTransferException ex)
        {
            RitsuToastService.ShowError(ExtractionLocalization.SaveImportFailedText(ex.Message));
            return;
        }
        catch (Exception ex)
        {
            Entry.Logger.Error($"SaveTransfer import read failed: {ex}");
            RitsuToastService.ShowError(
                ExtractionLocalization.SaveImportFailedText(ExtractionLocalization.SaveImportErrorUnexpectedText()));
            return;
        }

        string names = string.Join(", ", slots.Select(s => s.FileName));
        var dialog = new ExtractionImportDialog(
            ExtractionLocalization.SaveImportConfirmHeaderText(),
            ExtractionLocalization.SaveImportConfirmBodyText(slots.Count, names),
            () => FinishImport(slots, SaveTransfer.ImportMode.Overwrite, host),
            () => FinishImport(slots, SaveTransfer.ImportMode.Merge, host));
        AddOverlay(dialog);
    }

    /// <summary>
    /// Runs on a confirmed import: backs up the current state first, then applies the validated slots through the live
    /// ModDataStore instances and refreshes the settings page. 确认导入后执行：先备份当前状态，再通过 ModDataStore 活实例
    /// 应用校验通过的槽，并刷新设置页。
    /// </summary>
    private static void FinishImport(List<SaveTransfer.ImportedSlot> slots, SaveTransfer.ImportMode mode,
        IModSettingsUiActionHost host)
    {
        string? backupName = SaveTransfer.CreateBackup();
        if (backupName == null)
        {
            RitsuToastService.ShowWarning(ExtractionLocalization.SaveImportBackupFailedText());
        }

        try
        {
            SaveTransfer.Apply(slots, mode);
            _lastImportedSlotCount = slots.Count;
            _lastBackupName = backupName;
            UpdateStatus();
            host.RequestRefreshAfterDataModelBatchChange();
            RitsuToastService.ShowInfo(ExtractionLocalization.SaveImportDoneText());
        }
        catch (Exception ex)
        {
            Entry.Logger.Error($"SaveTransfer import apply failed: {ex}");
            RitsuToastService.ShowError(
                ExtractionLocalization.SaveImportFailedText(ExtractionLocalization.SaveImportErrorUnexpectedText()));
        }
    }

    /// <summary>
    /// Blocks the durability-mode toggle while a run is in progress or a character-select lobby is open. A lobby has
    /// already staged the pending carry into run saved-data, which a warehouse-mode switch cannot retract — allowing the
    /// switch would consume the carry from one warehouse and deposit into the other.
    /// 跑局进行中或角色选择大厅打开时禁止耐久模式切换：大厅已把携带暂存进局内 RunSavedData，切换无法收回——允许切换会让携带
    /// 从一个仓库消耗、却存入另一个仓库。
    /// </summary>
    private static bool IsRunOrLobbyActive()
    {
        if (RunManager.Instance?.IsInProgress == true)
        {
            return true;
        }

        return ActiveScreenContext.Instance.GetCurrentScreen() is NCharacterSelectScreen;
    }
}
