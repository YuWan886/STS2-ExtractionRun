using MegaCrit.Sts2.Core.Localization;
using STS2RitsuLib.Settings;

namespace ExtractionRun.UI;

/// <summary>
/// Localization access for 搜打撤. Settings strings use <see cref="ModSettingsText"/> (required by RitsuLib's fluent
/// settings API); hub UI and toast strings are merged into the base-game <c>main_menu_ui</c> table via the standard
/// game localization pipeline (the base game only auto-merges mod JSON into tables it already defines).
/// 搜打撤的本地化访问：设置用 ModSettingsText，仓库大厅 UI 与提示并入基础游戏表 main_menu_ui。
/// </summary>
public static class ExtractionLocalization
{
    public const string UiTable = "main_menu_ui";
    public const string SettingsTable = "settings_ui";

    // ----- Settings page (ModSettingsText) -----

    public static ModSettingsText ModTitleText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.mod_title", "Search-Loot-Extract");

    public static ModSettingsText SettingsPageTitleText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.page.title", "Search-Loot-Extract Settings");

    public static ModSettingsText GeneralSectionTitleText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.section.general.title", "General");

    public static ModSettingsText MaxCardsText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.maxCards", "Max carried cards");

    public static ModSettingsText MaxCardsDescriptionText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.maxCards.description",
            "Maximum number of cards you can carry into a run.");

    public static ModSettingsText MaxRelicsText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.maxRelics", "Max carried relics");

    public static ModSettingsText MaxRelicsDescriptionText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.maxRelics.description",
            "Maximum number of relics you can carry into a run.");

    public static ModSettingsText ResetSectionTitleText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.section.reset.title", "Reset");

    public static ModSettingsText ResetButtonLabelText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.reset.label", "Restore Defaults");

    public static ModSettingsText ResetButtonText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.reset.button", "Reset");

    public static ModSettingsText ResetButtonDescriptionText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.reset.description",
            "Restore all Search-Loot-Extract settings to their default values.");

    // ----- Warehouse hub UI (LocString on the main_menu_ui table) -----

    public static string HubTitleText() => Text("EXTRACTION_RUN.hub.title");
    public static string SectionCardsText() => Text("EXTRACTION_RUN.hub.cards");
    public static string SectionRelicsText() => Text("EXTRACTION_RUN.hub.relics");
    public static string SectionPotionsText() => Text("EXTRACTION_RUN.hub.potions");
    public static string SectionGoldText() => Text("EXTRACTION_RUN.hub.gold");
    public static string CarryDeckText() => Text("EXTRACTION_RUN.carry.deck");
    public static string CarryRelicsText() => Text("EXTRACTION_RUN.carry.relics");
    public static string CarryPotionsText() => Text("EXTRACTION_RUN.carry.potions");
    public static string CarryGoldText() => Text("EXTRACTION_RUN.carry.gold");
    public static string ButtonStartText() => Text("EXTRACTION_RUN.button.start");
    public static string ButtonBackText() => Text("EXTRACTION_RUN.button.back");
    public static string EmptyWarehouseText() => Text("EXTRACTION_RUN.empty.warehouse");
    public static string EmptyCarryText() => Text("EXTRACTION_RUN.empty.carry");
    public static string DepositSuccessText() => Text("EXTRACTION_RUN.toast.deposit");
    public static string FirstSeedToastText() => Text("EXTRACTION_RUN.toast.firstSeed");
    public static string NeedCardHintText() => Text("EXTRACTION_RUN.hub.needCard");

    public static string LimitCardsText(int current, int max) => Formatted("EXTRACTION_RUN.limit.cards", current, max);
    public static string LimitRelicsText(int current, int max) => Formatted("EXTRACTION_RUN.limit.relics", current, max);
    public static string LimitPotionsText(int current, int max) => Formatted("EXTRACTION_RUN.limit.potions", current, max);
    public static string GoldWarehouseText(int gold) => Formatted("EXTRACTION_RUN.gold.warehouse", gold);
    public static string GoldCarryText(int gold) => Formatted("EXTRACTION_RUN.gold.carry", gold);
    public static string CardUpgradeText(int level) => Formatted("EXTRACTION_RUN.card.upgrade", level);

    // ----- Settlement screen 结算界面 -----

    public static string SettlementButtonText() => Text("EXTRACTION_RUN.settlement.button");
    public static string SettlementSuccessTitleText() => Text("EXTRACTION_RUN.settlement.success.title");
    public static string SettlementSuccessLedeText() => Text("EXTRACTION_RUN.settlement.success.lede");
    public static string SettlementFailTitleText() => Text("EXTRACTION_RUN.settlement.fail.title");
    public static string SettlementFailLedeText() => Text("EXTRACTION_RUN.settlement.fail.lede");
    public static string SettlementCardsText(int count) => Formatted("EXTRACTION_RUN.settlement.cards", count);
    public static string SettlementRelicsText(int count) => Formatted("EXTRACTION_RUN.settlement.relics", count);
    public static string SettlementPotionsText(int count) => Formatted("EXTRACTION_RUN.settlement.potions", count);
    public static string SettlementLostCardsText(int count) => Formatted("EXTRACTION_RUN.settlement.lost.cards", count);
    public static string SettlementLostRelicsText(int count) => Formatted("EXTRACTION_RUN.settlement.lost.relics", count);
    public static string SettlementLostPotionsText(int count) => Formatted("EXTRACTION_RUN.settlement.lost.potions", count);
    public static string SettlementGoldText(int gold) => Formatted("EXTRACTION_RUN.settlement.gold", gold);
    public static string SettlementBackText() => Text("EXTRACTION_RUN.settlement.back");
    public static string SettlementEmptyText() => Text("EXTRACTION_RUN.settlement.empty");

    /// <summary>
    /// Localized source-pool display name for an item tile. Character pools (ironclad / silent / defect /
    /// necrobinder / regent) reuse the base-game <c>characters</c> table title; everything else falls back to
    /// <c>EXTRACTION_RUN.pool.*</c>, then to the raw slug. 物品来源池的显示名：角色卡池复用角色名，其余用自定义键，最后回退到 slug。
    /// </summary>
    public static string PoolNameText(string poolSlug)
    {
        if (string.IsNullOrWhiteSpace(poolSlug))
        {
            return string.Empty;
        }

        string characterKey = poolSlug.ToUpperInvariant() + ".title";
        if (LocString.Exists("characters", characterKey))
        {
            return new LocString("characters", characterKey).GetFormattedText();
        }

        string key = "EXTRACTION_RUN.pool." + poolSlug;
        LocString? loc = LocString.GetIfExists(UiTable, key);
        return loc != null ? loc.GetFormattedText() : poolSlug;
    }

    private static string Text(string key) => new LocString(UiTable, key).GetFormattedText();

    private static string Formatted(string key, params object[] args) => string.Format(new LocString(UiTable, key).GetRawText(), args);
}
