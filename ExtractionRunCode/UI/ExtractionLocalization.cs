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

    public static ModSettingsText SplitDurabilityText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.durability.split", "Show stacks by durability");

    public static ModSettingsText SplitDurabilityDescriptionText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.durability.split.description",
            "Show copies of the same card or relic with different durability as separate tiles, each with its own badge, instead of merging them into one tile labeled with the worst copy.");

    public static ModSettingsText CapacitySectionTitleText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.section.capacity.title", "Backpack Capacity");

    public static ModSettingsText CapacityEnabledText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.capacity.enabled", "Backpack capacity system");

    public static ModSettingsText CapacityEnabledDescriptionText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.capacity.enabled.description",
            "Limit carried cards and relics by a shared backpack capacity (cards cost by rarity, relics a flat amount). Disabling reverts to the max card / relic count caps.");

    public static ModSettingsText CapacityTotalText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.capacity.total", "Backpack slots");

    public static ModSettingsText CapacityTotalDescriptionText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.capacity.total.description",
            "Total capacity shared by carried cards and relics.");

    public static ModSettingsText CapacityBasicCommonText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.capacity.basicCommon", "Starter/Common card weight");

    public static ModSettingsText CapacityBasicCommonDescriptionText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.capacity.basicCommon.description",
            "Backpack slots one starter- or common-rarity card occupies.");

    public static ModSettingsText CapacityUncommonText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.capacity.uncommon", "Uncommon card weight");

    public static ModSettingsText CapacityUncommonDescriptionText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.capacity.uncommon.description",
            "Backpack slots one uncommon-rarity card occupies.");

    public static ModSettingsText CapacityRareText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.capacity.rare", "Rare card weight");

    public static ModSettingsText CapacityRareDescriptionText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.capacity.rare.description",
            "Backpack slots one rare-rarity card occupies.");

    public static ModSettingsText CapacityAncientText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.capacity.ancient", "Ancient card weight");

    public static ModSettingsText CapacityAncientDescriptionText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.capacity.ancient.description",
            "Backpack slots one ancient-rarity card occupies.");

    public static ModSettingsText CapacityOtherText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.capacity.other", "Other card weight");

    public static ModSettingsText CapacityOtherDescriptionText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.capacity.other.description",
            "Backpack slots a card of any other rarity occupies (event/token/status/curse/quest, mod cards).");

    public static ModSettingsText CapacityRelicText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.capacity.relic", "Relic weight");

    public static ModSettingsText CapacityRelicDescriptionText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.capacity.relic.description",
            "Backpack slots one relic occupies (all relics share one value).");

    public static ModSettingsText ShopSectionTitleText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.section.shop.title", "Shop");

    public static ModSettingsText ShopPriceMultiplierText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.shop.buyMultiplier", "Buy price multiplier");

    public static ModSettingsText ShopPriceMultiplierDescriptionText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.shop.buyMultiplier.description",
            "Multiplier applied to the rolled vanilla shop price when buying.");

    public static ModSettingsText ShopSellRatioText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.shop.sellRatio", "Sell ratio");

    public static ModSettingsText ShopSellRatioDescriptionText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.shop.sellRatio.description",
            "Ratio of the vanilla base price paid when selling (before the durability factor).");

    public static ModSettingsText ShopMultiplayerContentText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.shop.multiplayerContent",
            "Include multiplayer-only content");

    public static ModSettingsText ShopMultiplayerContentDescriptionText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.shop.multiplayerContent.description",
            "Allow shop refreshes to roll multiplayer-only cards. Relics and potions have no separate multiplayer-only pool in this game version. Takes effect on the next refresh.");

    public static ModSettingsText LootSectionTitleText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.section.loot.title", "Loot Animation");

    public static ModSettingsText LootEnabledText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.loot.enabled", "Loot search animation");

    public static ModSettingsText LootEnabledDescriptionText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.loot.enabled.description",
            "Before revealing rewards on card-choice, merchant and treasure-chest screens, play a magnifier search reveal. Extraction runs only.");

    public static ModSettingsText LootBasicCommonText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.loot.basicCommon", "Starter/Common search time");

    public static ModSettingsText LootBasicCommonDescriptionText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.loot.basicCommon.description",
            "Seconds spent searching a starter/common-rarity item (cards Basic+Common, relics Starter+Common, potions Common).");

    public static ModSettingsText LootUncommonText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.loot.uncommon", "Uncommon search time");

    public static ModSettingsText LootUncommonDescriptionText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.loot.uncommon.description",
            "Seconds spent searching an uncommon-rarity item.");

    public static ModSettingsText LootRareText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.loot.rare", "Rare search time");

    public static ModSettingsText LootRareDescriptionText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.loot.rare.description",
            "Seconds spent searching a rare-rarity item.");

    public static ModSettingsText LootAncientText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.loot.ancient", "Ancient search time");

    public static ModSettingsText LootAncientDescriptionText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.loot.ancient.description",
            "Seconds spent searching an ancient-rarity item.");

    public static ModSettingsText LootOtherText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.loot.other", "Other search time");

    public static ModSettingsText LootOtherDescriptionText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.loot.other.description",
            "Seconds spent searching any other rarity (event/token/status/curse/quest, shop relics, mod items).");

    public static ModSettingsText LootSkipKeyText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.loot.skipKey", "Skip animation key");

    public static ModSettingsText LootSkipKeyDescriptionText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.loot.skipKey.description",
            "Press this key to reveal every remaining item instantly. Clicking the item being searched reveals it and advances.");

    public static ModSettingsText ExtractionPointSectionTitleText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.section.extractionPoint.title", "Extraction Point");

    public static ModSettingsText ExtractionPointCapacityAct1Text() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.extractionPoint.capacityAct1", "Act 1 carry capacity");

    public static ModSettingsText ExtractionPointCapacityAct1DescriptionText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.extractionPoint.capacityAct1.description",
            "Capacity for the carry-out panel in act 1 (rarity-weight slots, cards + relics share the pool).");

    public static ModSettingsText ExtractionPointCapacityAct2Text() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.extractionPoint.capacityAct2", "Act 2 carry capacity");

    public static ModSettingsText ExtractionPointCapacityAct2DescriptionText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.extractionPoint.capacityAct2.description",
            "Capacity for the carry-out panel in act 2.");

    public static ModSettingsText ExtractionPointCapacityAct3Text() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.extractionPoint.capacityAct3", "Act 3+ carry capacity");

    public static ModSettingsText ExtractionPointCapacityAct3DescriptionText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.extractionPoint.capacityAct3.description",
            "Capacity for the carry-out panel in act 3 and beyond.");

    public static ModSettingsText ExtractionPointGoldFeeAct1Text() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.extractionPoint.goldFeeAct1", "Act 1 gold fee");

    public static ModSettingsText ExtractionPointGoldFeeAct1DescriptionText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.extractionPoint.goldFeeAct1.description",
            "Base gold cost of the 金币撤离 option in act 1; the cost compounds by the rate below each act.");

    public static ModSettingsText ExtractionPointGoldFeeRateText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.extractionPoint.goldFeeRate", "Gold fee growth per act");

    public static ModSettingsText ExtractionPointGoldFeeRateDescriptionText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.extractionPoint.goldFeeRate.description",
            "Compounded per-act increase of the gold fee (e.g. 20% = 100 → 120 → 144).");

    public static ModSettingsText ExtractionPointActChanceText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.extractionPoint.actChance", "Per-act appearance chance");

    public static ModSettingsText ExtractionPointActChanceDescriptionText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.extractionPoint.actChance.description",
            "Chance each act rolls an extraction point at act start. At most one is ever placed per run.");

    public static ModSettingsText ResetSectionTitleText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.section.reset.title", "Reset");

    public static ModSettingsText ResetButtonLabelText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.reset.label", "Restore Defaults");

    public static ModSettingsText ResetButtonText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.reset.button", "Reset");

    public static ModSettingsText ResetButtonDescriptionText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.reset.description",
            "Restore all Search-Loot-Extract settings to their default values.");

    // ----- Save transfer 存档管理 (settings page strings) -----

    public static ModSettingsText SaveTransferSectionTitleText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.section.saveTransfer.title", "Save Transfer");

    public static ModSettingsText SaveExportLabelText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.save.export.label", "Export save");

    public static ModSettingsText SaveExportButtonText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.save.export.button", "Export");

    public static ModSettingsText SaveExportDescriptionText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.save.export.description",
            "Pack the complete Search-Loot-Extract save (warehouse, pending carry, shop, challenges and settings) into a ZIP file.");

    public static ModSettingsText SaveImportLabelText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.save.import.label", "Import save");

    public static ModSettingsText SaveImportButtonText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.save.import.button", "Import");

    public static ModSettingsText SaveImportDescriptionText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.save.import.description",
            "Restore a save from a previously exported ZIP. Only available from the main menu outside a run or lobby.");

    public static ModSettingsText SaveStatusLabelText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.save.status.label", "Status");

    public static ModSettingsText SaveStatusDescriptionText() =>
        ModSettingsText.LocString(SettingsTable, "extractionrun.settings.save.status.description",
            "Latest export/import results and the mod-data cloud sync toggle state.");

    // ----- Warehouse hub UI (LocString on the main_menu_ui table) -----

    public static string HubTitleText() => Text("EXTRACTION_RUN.hub.title");

    // ----- Challenge system 挑战系统 -----

    public static string PageWarehouseText() => Text("EXTRACTION_RUN.page.warehouse");
    public static string PageShopText() => Text("EXTRACTION_RUN.page.shop");
    public static string PageChallengeText() => Text("EXTRACTION_RUN.page.challenge");
    public static string ChallengePageTitleText() => Text("EXTRACTION_RUN.challenge.title");
    public static string ChallengeSectionDailyText() => Text("EXTRACTION_RUN.challenge.section.daily");
    public static string ChallengeSectionPermanentText() => Text("EXTRACTION_RUN.challenge.section.permanent");
    public static string ChallengeClearCountText(int count) => Formatted("EXTRACTION_RUN.challenge.cleared", count);
    public static string ChallengeSelectedText() => Text("EXTRACTION_RUN.challenge.selected");
    public static string ChallengeNoneHintText() => Text("EXTRACTION_RUN.challenge.noneHint");
    public static string ChallengeSelectedHintText(int count) => Formatted("EXTRACTION_RUN.challenge.selectedHint", count);
    public static string ChallengeTitle(string id) => Text("EXTRACTION_RUN.challenge." + id + ".title");
    public static string ChallengeDesc(string id) => Text("EXTRACTION_RUN.challenge." + id + ".desc");
    public static string ChallengeRewardDoubleText() => Text("EXTRACTION_RUN.challenge.reward.double");
    public static string ChallengeRewardAllText(string rarity) => Formatted("EXTRACTION_RUN.challenge.reward.all", rarity);
    public static string ChallengeRewardRandomText(int count, string rarity) =>
        Formatted("EXTRACTION_RUN.challenge.reward.random", count, rarity);
    public static string ChallengeRewardAllCardsText() => Text("EXTRACTION_RUN.challenge.reward.allCards");
    public static string ChallengeRewardRandomRelicText(int count, string rarity) =>
        Formatted("EXTRACTION_RUN.challenge.reward.randomRelic", count, rarity);
    public static string ChallengeRewardAncientRelicsText(int count) =>
        Formatted("EXTRACTION_RUN.challenge.reward.ancientRelics", count);
    public static string ChallengeRewardFixedText(int count, string name) =>
        Formatted("EXTRACTION_RUN.challenge.reward.fixed", count, name);
    public static string ChallengeRewardFixedRelicsText(int count) =>
        Formatted("EXTRACTION_RUN.challenge.reward.fixedRelics", count);
    public static string ChallengeRewardGoldText(int amount) =>
        Formatted("EXTRACTION_RUN.challenge.reward.gold", amount);
    public static string ChallengeSummaryText(string joined) => Formatted("EXTRACTION_RUN.challenge.summary", joined);
    public static string ChallengeSearchPlaceholderText() => Text("EXTRACTION_RUN.challenge.search");
    public static string ChallengeFilterAllText() => Text("EXTRACTION_RUN.challenge.filter.all");
    public static string ChallengeTagText(ChallengeTag tag) =>
        Text("EXTRACTION_RUN.challenge.tag." + tag.ToString().ToLowerInvariant());
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

    // ----- STS2-Game-Lobby compat 联机大厅兼容 -----

    /// <summary>Room-type option label added to the lobby mod's create-room dialog. 联机大厅建房表单的房间类型项。</summary>
    public static string LanConnectRoomTypeText() => Text("EXTRACTION_RUN.lanConnect.roomType");

    /// <summary>Room mode label + list pill for an extraction room in the lobby mod. 联机大厅里搜打撤房间的模式标签。</summary>
    public static string LanConnectModeText() => Text("EXTRACTION_RUN.lanConnect.mode");

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

    // ----- Backpack capacity 背包容量 -----

    /// <summary>Global capacity bar text. 全局容量条文本。</summary>
    public static string CapacityBarText(int used, int total) => Formatted("EXTRACTION_RUN.capacity.bar", used, total);

    /// <summary>Per-section carry count + capacity used in capacity mode (no per-kind cap to show). The two numbers stay
    /// on one line so the count reads first and the slot usage second. 容量模式下每节显示数量 + 该节占用的容量（无每类上限）。
    /// 两个数字同一行，数量在前、占格在后。</summary>
    public static string CarryDeckCountText(int count, int capacity) => Formatted("EXTRACTION_RUN.carry.count.deck", count, capacity);

    public static string CarryRelicsCountText(int count, int capacity) => Formatted("EXTRACTION_RUN.carry.count.relics", count, capacity);

    /// <summary>Toast after the natural-node clamp dropped over-capacity copies. 自然节点钳制挤掉超容副本后的提示。</summary>
    public static string CapacityClampedText(int count) => Formatted("EXTRACTION_RUN.capacity.clamped", count);

    // ----- Run seed 种子 -----

    public static string SeedLabelText() => Text("EXTRACTION_RUN.seed.label");
    public static string SeedPlaceholderText() => Text("EXTRACTION_RUN.seed.placeholder");

    // ----- Durability 耐久 -----

    /// <summary>Tile durability badge text. 瓦片耐久角标文本。</summary>
    public static string DurabilityBadgeText(int durability) => Formatted("EXTRACTION_RUN.durability.badge", durability);

    /// <summary>Tile badge text for a broken (durability-0) copy. 战损（0 耐久）副本的瓦片角标文本。</summary>
    public static string DurabilityBrokenText() => Text("EXTRACTION_RUN.durability.broken");

    // ----- Shop 商店 -----

    public static string ShopTitleText() => Text("EXTRACTION_RUN.shop.title");
    public static string ShopTabBuyText() => Text("EXTRACTION_RUN.shop.tab.buy");
    public static string ShopTabSellText() => Text("EXTRACTION_RUN.shop.tab.sell");
    public static string ShopOpenButtonText() => Text("EXTRACTION_RUN.shop.button.open");
    public static string ShopWarehouseButtonText() => Text("EXTRACTION_RUN.shop.button.warehouse");
    public static string ShopRefreshText(int cost) => Formatted("EXTRACTION_RUN.shop.refresh", cost);
    public static string ShopBuyEmptyText() => Text("EXTRACTION_RUN.shop.buy.empty");
    public static string ShopSellEmptyText() => Text("EXTRACTION_RUN.shop.sell.empty");
    public static string ShopSellSelectedText() => Text("EXTRACTION_RUN.shop.sellSelected");
    public static string ShopSelectionSummaryText(int count, int gold) =>
        Formatted("EXTRACTION_RUN.shop.selectionSummary", count, gold);
    public static string ShopGoldShortText() => Text("EXTRACTION_RUN.toast.goldShort");
    public static string ShopRefreshedText(int cost) => Formatted("EXTRACTION_RUN.toast.shopRefreshed", cost);
    public static string ShopBoughtText() => Text("EXTRACTION_RUN.toast.bought");
    public static string ShopSoldText(int count, int gold) => Formatted("EXTRACTION_RUN.toast.sold", count, gold);

    /// <summary>Sell-tab durability filter title. 出售页耐久度筛选标题。</summary>
    public static string FilterDurabilityText() => Text("EXTRACTION_RUN.filter.durability");

    /// <summary>Localized label for a sell-tab durability option (full / ge2 / le1). 出售页耐久度选项的本地化标签。</summary>
    public static string FilterDurabilityLabel(string slug) =>
        DynamicText("EXTRACTION_RUN.filter.durability." + slug.ToLowerInvariant(), slug);

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

    /// <summary>Import summary line when the backpack capacity dropped some items. 导入摘要：容量不足导致部分物品未导入。</summary>
    public static string CodeCapacityClampedText(int missing) => Formatted("EXTRACTION_RUN.code.capacityClamped", missing);
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

    /// <summary>Capacity-toggle confirm dialog: enabling. 开启容量的确认弹窗标题/正文。</summary>
    public static string CapacityEnableHeaderText() => Text("EXTRACTION_RUN.capacity.enable.header");
    public static string CapacityEnableBodyText() => Text("EXTRACTION_RUN.capacity.enable.body");

    /// <summary>Capacity-toggle confirm dialog: disabling. 关闭容量的确认弹窗标题/正文。</summary>
    public static string CapacityDisableHeaderText() => Text("EXTRACTION_RUN.capacity.disable.header");
    public static string CapacityDisableBodyText() => Text("EXTRACTION_RUN.capacity.disable.body");

    /// <summary>Toast shown when the capacity toggle was blocked (run/lobby active). 局内/大厅中切换被拒的提示。</summary>
    public static string CapacityBlockedText() => Text("EXTRACTION_RUN.capacity.blocked");

    // ----- Save transfer 存档管理 (dialogs, toasts, status) -----

    public static string SaveExportDialogTitleText() => Text("EXTRACTION_RUN.save.exportDialogTitle");
    public static string SaveImportDialogTitleText() => Text("EXTRACTION_RUN.save.importDialogTitle");
    public static string SaveImportBlockedText() => Text("EXTRACTION_RUN.save.importBlocked");
    public static string SaveImportConfirmHeaderText() => Text("EXTRACTION_RUN.save.import.header");
    public static string SaveImportConfirmBodyText(int count, string names) =>
        Formatted("EXTRACTION_RUN.save.import.body", count, names);
    public static string SaveImportOverwriteButtonText() => Text("EXTRACTION_RUN.save.import.overwrite");
    public static string SaveImportMergeButtonText() => Text("EXTRACTION_RUN.save.import.merge");
    public static string SaveExportDoneText() => Text("EXTRACTION_RUN.save.export.done");
    public static string SaveExportFailedText() => Text("EXTRACTION_RUN.save.export.failed");
    public static string SaveImportDoneText() => Text("EXTRACTION_RUN.save.import.done");
    public static string SaveImportFailedText(string reason) => Formatted("EXTRACTION_RUN.save.import.failed", reason);
    public static string SaveImportErrorUnexpectedText() => Text("EXTRACTION_RUN.save.import.error.unexpected");
    public static string SaveImportBackupFailedText() => Text("EXTRACTION_RUN.save.import.backupFailed");
    public static string SaveErrorNotZipText() => Text("EXTRACTION_RUN.save.error.notZip");
    public static string SaveErrorNoManifestText() => Text("EXTRACTION_RUN.save.error.noManifest");
    public static string SaveErrorBadManifestText() => Text("EXTRACTION_RUN.save.error.badManifest");
    public static string SaveErrorVersionText(int found, int supported) =>
        Formatted("EXTRACTION_RUN.save.error.version", found, supported);
    public static string SaveErrorBadJsonText(string fileName) => Formatted("EXTRACTION_RUN.save.error.badJson", fileName);
    public static string SaveErrorMissingFieldText(string fileName) =>
        Formatted("EXTRACTION_RUN.save.error.missingField", fileName);
    public static string SaveErrorEmptyText() => Text("EXTRACTION_RUN.save.error.empty");
    public static string SaveStatusCloudText(bool on) =>
        Text(on ? "EXTRACTION_RUN.save.status.cloudOn" : "EXTRACTION_RUN.save.status.cloudOff");
    public static string SaveStatusLastExportText(string path) =>
        Formatted("EXTRACTION_RUN.save.status.lastExport", path);
    public static string SaveStatusLastImportText(int count) =>
        Formatted("EXTRACTION_RUN.save.status.lastImport", count);
    public static string SaveStatusLastBackupText(string name) =>
        Formatted("EXTRACTION_RUN.save.status.lastBackup", name);

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
    public static string ExtractionPointSettlementTitleText() => Text("EXTRACTION_RUN.settlement.extractionPoint.title");
    public static string ExtractionPointSettlementLedeText() => Text("EXTRACTION_RUN.settlement.extractionPoint.lede");

    // ----- Extraction point panel 撤离点面板 -----

    public static string ExtractionPointPanelTitleText() => Text("EXTRACTION_RUN.extractionPoint.panel.title");
    public static string ExtractionPointCardSectionText() => Text("EXTRACTION_RUN.extractionPoint.panel.cards");
    public static string ExtractionPointRelicSectionText() => Text("EXTRACTION_RUN.extractionPoint.panel.relics");
    public static string ExtractionPointCapacityText(int used, int capacity) =>
        Formatted("EXTRACTION_RUN.extractionPoint.panel.capacity", used, capacity);
    public static string ExtractionPointPotionsAllText(int count) =>
        Formatted("EXTRACTION_RUN.extractionPoint.panel.potionsAll", count);
    public static string ExtractionPointGoldAllText(int gold) =>
        Formatted("EXTRACTION_RUN.extractionPoint.panel.goldAll", gold);
    public static string ExtractionPointEmptyHeaderText() => Text("EXTRACTION_RUN.extractionPoint.panel.empty.header");
    public static string ExtractionPointEmptyBodyText() => Text("EXTRACTION_RUN.extractionPoint.panel.empty.body");
    public static string ExtractionPointConfirmText() => Text("EXTRACTION_RUN.extractionPoint.panel.confirm");

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
