using STS2RitsuLib;
using STS2RitsuLib.Data;
using STS2RitsuLib.Settings;
using STS2RitsuLib.Utils.Persistence;
using ExtractionRun.UI;

namespace ExtractionRun.Settings;

/// <summary>
/// Registers the 搜打撤 settings page: max carried cards / relics sliders, per-kind hover-tooltip toggles, and a reset
/// button. Values are bound to the <see cref="ExtractionSettings"/> POCO via <see cref="ModSettingsValueBinding{TData,TValue}"/>
/// at SaveScope.Global.
/// 搜打撤设置页：最大携带牌数/遗物数滑条、卡牌/遗物/药水各自的悬停提示开关，与重置按钮，通过 ModSettingsValueBinding 绑定到
/// ExtractionSettings。
/// </summary>
public static class ExtractionSettingsPage
{
    public const string DataKey = "settings";

    private const int MaxCardsSlider = 20;
    private const int MaxRelicsSlider = 6;

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
            .AddSection("reset", section => section
                .WithTitle(ExtractionLocalization.ResetSectionTitleText())
                .AddButton(
                    "reset_defaults",
                    ExtractionLocalization.ResetButtonLabelText(),
                    ExtractionLocalization.ResetButtonText(),
                    ResetToDefaults,
                    ModSettingsButtonTone.Danger,
                    ExtractionLocalization.ResetButtonDescriptionText())));
    }

    private static void ResetToDefaults(IModSettingsUiActionHost host)
    {
        Current.ResetToDefaults();
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
    }
}
