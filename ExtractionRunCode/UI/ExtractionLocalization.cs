using MegaCrit.Sts2.Core.Localization;
using STS2RitsuLib.Settings;
using ExtractionRun.Data;

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

    public static ModSettingsText ShowCardHoverTipsText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.showCardHoverTips", "Show card tooltips");

    public static ModSettingsText ShowCardHoverTipsDescriptionText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.showCardHoverTips.description",
            "Show vanilla tooltips when hovering over cards.");

    public static ModSettingsText ShowRelicHoverTipsText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.showRelicHoverTips", "Show relic tooltips");

    public static ModSettingsText ShowRelicHoverTipsDescriptionText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.showRelicHoverTips.description",
            "Show vanilla tooltips when hovering over relics.");

    public static ModSettingsText ShowPotionHoverTipsText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.showPotionHoverTips", "Show potion tooltips");

    public static ModSettingsText ShowPotionHoverTipsDescriptionText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.showPotionHoverTips.description",
            "Show vanilla tooltips when hovering over potions.");

    public static ModSettingsText DurabilitySectionTitleText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.section.durability.title", "Durability");

    public static ModSettingsText DurabilityEnabledText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.durability.enabled", "Durability");

    public static ModSettingsText DurabilityEnabledDescriptionText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.durability.enabled.description",
            "Cards and relics lose 1 durability per successful extraction; at 0 a copy is lost. Disabling switches the warehouse to a disposable no-durability copy (progress there is lost when re-enabling).");

    public static ModSettingsText DurabilityBasicText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.durability.basic", "Starter card durability");

    public static ModSettingsText DurabilityBasicDescriptionText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.durability.basic.description",
            "Max durability granted to a new starter-rarity card.");

    public static ModSettingsText DurabilityCommonText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.durability.common", "Common card durability");

    public static ModSettingsText DurabilityCommonDescriptionText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.durability.common.description",
            "Max durability granted to a new common-rarity card.");

    public static ModSettingsText DurabilityUncommonText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.durability.uncommon", "Uncommon card durability");

    public static ModSettingsText DurabilityUncommonDescriptionText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.durability.uncommon.description",
            "Max durability granted to a new uncommon-rarity card.");

    public static ModSettingsText DurabilityRareText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.durability.rare", "Rare card durability");

    public static ModSettingsText DurabilityRareDescriptionText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.durability.rare.description",
            "Max durability granted to a new rare-rarity card.");

    public static ModSettingsText DurabilityAncientText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.durability.ancient", "Ancient card durability");

    public static ModSettingsText DurabilityAncientDescriptionText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.durability.ancient.description",
            "Max durability granted to a new ancient-rarity card.");

    public static ModSettingsText DurabilityOtherText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.durability.other", "Other card durability");

    public static ModSettingsText DurabilityOtherDescriptionText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.durability.other.description",
            "Max durability granted to cards of any other rarity (event/token/status/curse/quest, mod cards).");

    public static ModSettingsText DurabilityRelicText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.durability.relic", "Relic durability");

    public static ModSettingsText DurabilityRelicDescriptionText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.durability.relic.description",
            "Max durability granted to a new relic (all relics share one value).");

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
    public static string ButtonConfirmText() => Text("EXTRACTION_RUN.button.confirmCarry");
    public static string EmptyWarehouseText() => Text("EXTRACTION_RUN.empty.warehouse");
    public static string EmptyCarryText() => Text("EXTRACTION_RUN.empty.carry");
    public static string DepositSuccessText() => Text("EXTRACTION_RUN.toast.deposit");
    public static string FirstSeedToastText() => Text("EXTRACTION_RUN.toast.firstSeed");
    public static string NeedCardHintText() => Text("EXTRACTION_RUN.hub.needCard");

    public static string SearchPlaceholderText() => Text("EXTRACTION_RUN.search.placeholder");
    public static string SearchLimitText(int shownCap, int total) => Formatted("EXTRACTION_RUN.search.limit", shownCap, total);
    public static string SearchNoMatchText(string category) => Formatted("EXTRACTION_RUN.search.emptyTab", category);

    public static string FilterPoolText() => Text("EXTRACTION_RUN.filter.pool");
    public static string FilterRarityText() => Text("EXTRACTION_RUN.filter.rarity");
    public static string FilterTypeText() => Text("EXTRACTION_RUN.filter.type");
    public static string FilterCostText() => Text("EXTRACTION_RUN.filter.cost");
    public static string FilterSourceText() => Text("EXTRACTION_RUN.filter.source");
    public static string FilterAllText() => Text("EXTRACTION_RUN.filter.all");
    public static string FilterClearText() => Text("EXTRACTION_RUN.filter.clear");

    /// <summary>Localized label for a rarity option (falls back to the enum name). 稀有度选项的本地化标签（回退枚举名）。</summary>
    public static string FilterRarityLabel(string slug) => DynamicText("EXTRACTION_RUN.filter.rarity." + slug.ToLowerInvariant(), slug);

    /// <summary>Localized label for a card-type option. 卡牌类型选项的本地化标签。</summary>
    public static string FilterTypeLabel(string slug) => DynamicText("EXTRACTION_RUN.filter.type." + slug.ToLowerInvariant(), slug);

    /// <summary>Localized label for a cost-bucket option. 费用桶选项的本地化标签。</summary>
    public static string FilterCostLabel(string slug) => DynamicText("EXTRACTION_RUN.filter.cost." + slug.ToLowerInvariant(), slug);

    /// <summary>
    /// Localized label for a content-source option: 原版 / 未知 via loc keys, any other key (a mod's normalized stem)
    /// resolves to the loaded mod's manifest display name. 内容来源选项的本地化标签：原版/未知走 loc 键，其余（mod 规范化 stem）
    /// 解析为已加载 mod 的清单显示名。
    /// </summary>
    public static string FilterSourceLabel(string key) => key switch
    {
        ContentSource.BaseKey => Text("EXTRACTION_RUN.filter.source.base"),
        ContentSource.UnknownKey => Text("EXTRACTION_RUN.filter.source.unknown"),
        _ => CarryCodeOwner.ResolveModDisplayName(key),
    };

    public static string LimitCardsText(int current, int max) => Formatted("EXTRACTION_RUN.limit.cards", current, max);
    public static string LimitRelicsText(int current, int max) => Formatted("EXTRACTION_RUN.limit.relics", current, max);
    public static string LimitPotionsText(int current, int max) => Formatted("EXTRACTION_RUN.limit.potions", current, max);
    public static string GoldWarehouseText(int gold) => Formatted("EXTRACTION_RUN.gold.warehouse", gold);
    public static string GoldCarryText(int gold) => Formatted("EXTRACTION_RUN.gold.carry", gold);

    // ----- Run seed 种子 -----

    public static string SeedLabelText() => Text("EXTRACTION_RUN.seed.label");
    public static string SeedPlaceholderText() => Text("EXTRACTION_RUN.seed.placeholder");

    // ----- Durability 耐久 -----

    /// <summary>Tile durability badge text. 瓦片耐久角标文本。</summary>
    public static string DurabilityBadgeText(int durability) => Formatted("EXTRACTION_RUN.durability.badge", durability);

    /// <summary>Tile badge text for a broken (durability-0) copy. 战损（0 耐久）副本的瓦片角标文本。</summary>
    public static string DurabilityBrokenText() => Text("EXTRACTION_RUN.durability.broken");

    // ----- Gear code 战备码 -----

    public static string CodeGenerateText() => Text("EXTRACTION_RUN.code.generate");
    public static string CodeImportText() => Text("EXTRACTION_RUN.code.import");
    public static string CodeTitleText() => Text("EXTRACTION_RUN.code.title");
    public static string CodeInputPlaceholderText() => Text("EXTRACTION_RUN.code.input.placeholder");
    public static string CodePreviewText() => Text("EXTRACTION_RUN.code.preview");
    public static string CodePreviewEmptyText() => Text("EXTRACTION_RUN.code.preview.empty");
    public static string CodeApplyText() => Text("EXTRACTION_RUN.code.apply");
    public static string CodeImportableText(int cards, int relics, int potions, int gold) =>
        Formatted("EXTRACTION_RUN.code.importable", cards, relics, potions, gold);
    public static string CodeMissingText(int missing) => Formatted("EXTRACTION_RUN.code.missing", missing);
    public static string CodeMissingModsText(string names) => Formatted("EXTRACTION_RUN.code.missingMods", names);
    public static string CodeUnrecognizedText() => Text("EXTRACTION_RUN.code.unrecognized");
    public static string CodeUnrecognizedListText(string entries) => Formatted("EXTRACTION_RUN.code.unrecognizedList", entries);
    public static string CodeGoldClampedText(int gold, int balance) => Formatted("EXTRACTION_RUN.code.goldClamped", gold, balance);
    public static string CodeNoneImportableText() => Text("EXTRACTION_RUN.code.noneImportable");
    public static string CodeErrorText(CarryCodec.DecodeError error) => error switch
    {
        CarryCodec.DecodeError.Empty => Text("EXTRACTION_RUN.code.error.empty"),
        CarryCodec.DecodeError.MissingChecksum => Text("EXTRACTION_RUN.code.error.checksum"),
        CarryCodec.DecodeError.BadChecksum => Text("EXTRACTION_RUN.code.error.badChecksum"),
        CarryCodec.DecodeError.BadSegment => Text("EXTRACTION_RUN.code.error.segment"),
        CarryCodec.DecodeError.CountOverflow => Text("EXTRACTION_RUN.code.error.count"),
        _ => string.Empty,
    };
    public static string CodeCopiedText() => Text("EXTRACTION_RUN.toast.codeCopied");

    // ----- Console confirm dialog 控制台确认弹窗 -----

    public static string ConfirmResetHeaderText() => Text("EXTRACTION_RUN.confirm.reset.header");
    public static string ConfirmResetBodyText() => Text("EXTRACTION_RUN.confirm.reset.body");
    public static string ConfirmButtonText() => Text("EXTRACTION_RUN.confirm.confirm");
    public static string CancelButtonText() => Text("EXTRACTION_RUN.confirm.cancel");

    /// <summary>Durability-mode confirm dialog: enabling. 开启耐久的确认弹窗标题/正文。</summary>
    public static string DurabilityEnableHeaderText() => Text("EXTRACTION_RUN.durability.enable.header");
    public static string DurabilityEnableBodyText() => Text("EXTRACTION_RUN.durability.enable.body");

    /// <summary>Durability-mode confirm dialog: disabling. 关闭耐久的确认弹窗标题/正文。</summary>
    public static string DurabilityDisableHeaderText() => Text("EXTRACTION_RUN.durability.disable.header");
    public static string DurabilityDisableBodyText() => Text("EXTRACTION_RUN.durability.disable.body");

    /// <summary>Toast shown when the durability toggle was blocked (run/lobby active). 局内/大厅中切换被拒的提示。</summary>
    public static string DurabilityBlockedText() => Text("EXTRACTION_RUN.durability.blocked");

    // ----- Settlement screen 结算界面 -----

    public static string SettlementButtonText() => Text("EXTRACTION_RUN.settlement.button");
    public static string SettlementSuccessTitleText() => Text("EXTRACTION_RUN.settlement.success.title");
    public static string SettlementSuccessLedeText() => Text("EXTRACTION_RUN.settlement.success.lede");
    public static string SettlementFailTitleText() => Text("EXTRACTION_RUN.settlement.fail.title");
    public static string SettlementFailLedeText() => Text("EXTRACTION_RUN.settlement.fail.lede");
    public static string SettlementCardsText(int count) => Formatted("EXTRACTION_RUN.settlement.cards", count);
    public static string SettlementRelicsText(int count) => Formatted("EXTRACTION_RUN.settlement.relics", count);
    public static string SettlementPotionsText(int count) => Formatted("EXTRACTION_RUN.settlement.potions", count);
    public static string SettlementExpiredRelicsText(int count) => Formatted("EXTRACTION_RUN.settlement.expiredRelics", count);
    public static string SettlementBrokenCardsText(int count) => Formatted("EXTRACTION_RUN.settlement.brokenCards", count);
    public static string SettlementBrokenRelicsText(int count) => Formatted("EXTRACTION_RUN.settlement.brokenRelics", count);
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

    /// <summary>
    /// Localized value for a dynamic (enum-derived) key with a fallback to <paramref name="fallback"/> — used for
    /// filter option labels whose key names follow an enum slug (<c>EXTRACTION_RUN.filter.rarity.common</c> etc.).
    /// 动态键（由枚举派生）的本地化取值，未找到时回退 <paramref name="fallback"/>——用于过滤选项标签。
    /// </summary>
    private static string DynamicText(string key, string fallback)
    {
        LocString? loc = LocString.GetIfExists(UiTable, key);
        return loc != null ? loc.GetFormattedText() : fallback;
    }
}
