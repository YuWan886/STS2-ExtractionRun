using STS2RitsuLib;
using STS2RitsuLib.Data;
using STS2RitsuLib.Settings;
using STS2RitsuLib.Utils.Persistence;
using ExtractionRun.UI;

namespace ExtractionRun.Settings;

/// <summary>
/// Registers the 搜打撤 settings page: max carried cards / relics sliders and a reset button. Values are bound to the
/// <see cref="ExtractionSettings"/> POCO via <see cref="ModSettingsValueBinding{TData,TValue}"/> at SaveScope.Global.
/// 搜打撤设置页：最大携带牌数/遗物数滑条与重置按钮，通过 ModSettingsValueBinding 绑定到 ExtractionSettings。
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
                    0, MaxRelicsSlider, 1, description: ExtractionLocalization.MaxRelicsDescriptionText()))
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
    }
}
