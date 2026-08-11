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
/// Registers the 搜打撤 settings page: max carried cards / relics sliders, per-kind hover-tooltip toggles, the
/// durability section (on/off toggle + per-rarity durability caps), and a reset button. Values are bound to the
/// <see cref="ExtractionSettings"/> POCO via <see cref="ModSettingsValueBinding{TData,TValue}"/> at SaveScope.Global.
/// The durability toggle is confirm-gated: flipping it opens an <see cref="ExtractionConfirmDialog"/> (确定/取消) and is
/// blocked while a run or character-select lobby is active — a lobby has already staged the pending carry, which a mode
/// switch can't retract. Confirming calls <see cref="WarehouseStore.SwitchDurabilityMode"/>, which freezes/restores the
/// durability warehouse file and re-syncs the pending carry; cancelling writes the old value back.
/// 搜打撤设置页：最大携带牌数/遗物数滑条、卡牌/遗物/药水各自的悬停提示开关、耐久区（开关 + 各稀有度耐久上限）、重置按钮，
/// 通过 ModSettingsValueBinding 绑定到 ExtractionSettings。耐久开关需确认弹窗：翻转时弹 ExtractionConfirmDialog（确定/取消），
/// 局内或角色选择大厅中禁止切换（大厅已暂存携带，切换无法收回）。确定时调用 SwitchDurabilityMode（冻结/还原耐久文件并重同步
/// 携带），取消时写回旧值。
/// </summary>
public static class ExtractionSettingsPage
{
    public const string DataKey = "settings";

    private const int MaxCardsSlider = 20;
    private const int MaxRelicsSlider = 6;

    /// <summary>All durability caps share one slider range; defaults sit at 1–5. 各耐久上限共用同一滑条范围。</summary>
    private const int MaxDurabilitySlider = 20;
    private const int MinDurabilitySlider = 1;

    /// <summary>Guards the durability-toggle handler against re-entry (the revert write re-fires ValueWritten) and
    /// against the reset button flipping the toggle without a confirm dialog. 耐久切换处理器防重入（回退写会再次触发
    /// ValueWritten），也用于重置按钮直接翻转开关而不弹确认框。</summary>
    private static bool _suppressDurabilityToggle;

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

    private static readonly ModSettingsValueBinding<ExtractionSettings, double> ShopPriceMultiplierBinding = new(
        Entry.ModId, DataKey, SaveScope.Global,
        static s => s.ShopPriceMultiplier,
        static (s, v) => s.ShopPriceMultiplier = v);

    private static readonly ModSettingsValueBinding<ExtractionSettings, double> ShopSellRatioBinding = new(
        Entry.ModId, DataKey, SaveScope.Global,
        static s => s.ShopSellRatio,
        static (s, v) => s.ShopSellRatio = v);

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
                .AddIntSlider("max_cards", ExtractionLocalization.MaxCardsText(), MaxCardsBinding,
                    0, MaxCardsSlider, 1, description: ExtractionLocalization.MaxCardsDescriptionText())
                .AddIntSlider("max_relics", ExtractionLocalization.MaxRelicsText(), MaxRelicsBinding,
                    0, MaxRelicsSlider, 1, description: ExtractionLocalization.MaxRelicsDescriptionText())
                .AddToggle("show_card_hover_tips", ExtractionLocalization.ShowCardHoverTipsText(),
                    ShowCardHoverTipsBinding, description: ExtractionLocalization.ShowCardHoverTipsDescriptionText())
                .AddToggle("show_relic_hover_tips", ExtractionLocalization.ShowRelicHoverTipsText(),
                    ShowRelicHoverTipsBinding, description: ExtractionLocalization.ShowRelicHoverTipsDescriptionText())
                .AddToggle("show_potion_hover_tips", ExtractionLocalization.ShowPotionHoverTipsText(),
                    ShowPotionHoverTipsBinding, description: ExtractionLocalization.ShowPotionHoverTipsDescriptionText()))
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
                    description: ExtractionLocalization.DurabilityRelicDescriptionText()))
            .AddSection("shop", section => section
                .WithTitle(ExtractionLocalization.ShopSectionTitleText())
                .AddSlider("shop_buy_multiplier", ExtractionLocalization.ShopPriceMultiplierText(),
                    ShopPriceMultiplierBinding, 1.0, 5.0, 0.1,
                    static d => $"×{d:0.0}", description: ExtractionLocalization.ShopPriceMultiplierDescriptionText())
                .AddSlider("shop_sell_ratio", ExtractionLocalization.ShopSellRatioText(),
                    ShopSellRatioBinding, 0.1, 1.0, 0.05,
                    static d => $"{d:P0}", description: ExtractionLocalization.ShopSellRatioDescriptionText()))
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
    /// Confirm-gates the durability toggle. Runs synchronously after the binding flips <c>DurabilityEnabled</c>, so the
    /// old value is its inverse. While a run/lobby is active (the pending carry was already staged) the flip is reverted
    /// immediately; otherwise an <see cref="ExtractionConfirmDialog"/> asks 确定/取消 — confirm calls the warehouse mode
    /// switch, cancel writes the old value back (guarded so the revert doesn't re-enter this handler).
    /// 对耐久开关做确认门控。在绑定翻转 DurabilityEnabled 后同步触发，旧值即其反相。局内/大厅（携带已暂存）时立即回退；
    /// 否则弹确认框——确定时调用仓库模式切换，取消时写回旧值（以防重入守卫，回退写不会再次进入本处理器）。
    /// </summary>
    private static void OnBindingValueWritten(IModSettingsBinding binding)
    {
        if (_suppressDurabilityToggle || binding != DurabilityEnabledBinding)
        {
            return;
        }

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
        try
        {
            DurabilityEnabledBinding.Write(Current.DurabilityEnabled);
            DurabilityEnabledBinding.Save();
            MaxCardsBinding.Write(Current.MaxCarryCards);
            MaxCardsBinding.Save();
            MaxRelicsBinding.Write(Current.MaxCarryRelics);
            MaxRelicsBinding.Save();
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
            ShopPriceMultiplierBinding.Write(Current.ShopPriceMultiplier);
            ShopPriceMultiplierBinding.Save();
            ShopSellRatioBinding.Write(Current.ShopSellRatio);
            ShopSellRatioBinding.Save();
        }
        finally
        {
            _suppressDurabilityToggle = false;
        }

        // Reset flips durability back to ON without a confirm dialog — apply the mode switch directly if it changed.
        // 重置直接把耐久恢复到 ON，不弹确认框——若模式确实变化则直接应用切换。
        if (oldEnabled != Current.DurabilityEnabled)
        {
            WarehouseStore.SwitchDurabilityMode(Current.DurabilityEnabled);
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
