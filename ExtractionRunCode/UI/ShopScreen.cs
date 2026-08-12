using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves.Runs;
using STS2RitsuLib.Ui.Toast;
using ExtractionRun.Data;

namespace ExtractionRun.UI;

/// <summary>
/// The 搜打撤 hub shop: a full-screen screen (Layer 110) opened from the warehouse hub's bottom-right 商店 button and
/// closed by its bottom-left 仓库 button / ESC. Opening it hides the warehouse hub (the two pages are exclusive — the
/// hub is re-shown and refreshed on close), so gold/stock stay coherent when switching back. Two main tabs — 购买 (three
/// stacked 卡牌 / 遗物 / 药水 sections with a doubled stock, refresh-shop button replacing card removal, stock rolled
/// once per real calendar day) and 出售 (warehouse − carry groups with per-copy multi-select — left-click selects one
/// copy, right-click removes one, shift selects/deselects the whole group — and filters incl. durability). The shop
/// reads the SAME live warehouse and carry draft the hub holds.
/// 搜打撤商店：从仓库大厅右下角「商店」按钮打开的全屏页面（Layer 110），由其左下角「仓库」按钮 / ESC 关闭。打开时隐藏仓库
/// 大厅（两页互斥——关闭时恢复并刷新大厅，金币/库存保持一致）。两个主 Tab——购买（卡牌/遗物/药水三个竖排分区，库存翻倍，
/// 原版商人布局的卡牌移除位置换成刷新商店；库存每个现实日历日 roll 一次）与出售（「仓库 − 携带」分组，按份多选——左键选
/// 一件、右键减一件、Shift 全选/全不选，过滤含耐久度）。商店与大厅共享同一实时仓库与携带草稿。
/// </summary>
public sealed partial class ShopScreen : CanvasLayer
{
    private enum MainTab { Buy, Sell }
    private enum SellTab { Cards, Relics, Potions }

    /// <summary>Soft per-tab variety cap as a dirty-data guard (mirrors the hub). 每 Tab 软上限（脏数据护栏，同大厅）。</summary>
    private const int MaxTileKinds = 2000;

    /// <summary>Art preload requests submitted per frame (mirrors the hub). 每帧提交的贴图预载请求数（同大厅）。</summary>
    private const int PrewarmPerFrame = 8;

    private enum FilterKind
    {
        CardPools,
        CardRarities,
        CardTypes,
        CardCosts,
        CardSources,
        CardDurabilities,
        RelicPools,
        RelicRarities,
        RelicSources,
        RelicDurabilities,
        PotionPools,
        PotionRarities,
        PotionSources,
    }

    /// <summary>Durability filter option values (labels via <see cref="ExtractionLocalization.FilterDurabilityLabel"/>).
    /// 耐久度过滤选项值（标签走 FilterDurabilityLabel）。</summary>
    private static readonly string[] DurabilityFilterValues = { "full", "ge2", "le1" };

    private readonly WarehouseHubScreen _hub;
    private readonly WarehouseData _warehouse;
    private readonly CarryConfig _carry;
    private readonly ShopData _shop;
    private readonly bool _showDurability;

    /// <summary>The shop currently open in the scene tree (null when closed). 当前打开中的商店（关闭时为 null）。</summary>
    public static ShopScreen? Current { get; private set; }

    private MainTab _activeMain = MainTab.Buy;
    private SellTab _activeSell = SellTab.Cards;

    // Per-kind sell selection, keyed by model id string → how many copies of that group are selected (left-click adds
    // one, right-click removes one, shift selects/deselects the whole group). 各品类出售选中数（按模型 id 字符串 → 该组选中
    // 的份数：左键 +1、右键 −1、Shift 全选/全不选）。
    private readonly Dictionary<string, int> _selectedCards = new();
    private readonly Dictionary<string, int> _selectedRelics = new();
    private readonly Dictionary<string, int> _selectedPotions = new();

    // Per sell-sub-tab search queries (persisted). 出售子 Tab 搜索词（持久化）。
    private readonly string[] _sellQueries = new string[3];
    private string _sellQuery = "";

    private Label _goldChipLabel = null!;
    private Control _buyContent = null!;
    private Label _buyEmptyLabel = null!;
    private Button _refreshButton = null!;
    private readonly Control[] _buySections = new Control[3];
    private readonly VirtualizedItemGrid[] _buyGrids = new VirtualizedItemGrid[3];

    private Control _sellContent = null!;
    private LineEdit _sellSearchEdit = null!;
    private Button _sellClearButton = null!;
    private Button _sellSelectedButton = null!;
    private Label _sellSummaryLabel = null!;
    private readonly VBoxContainer[] _sellTabContent = new VBoxContainer[3];
    private readonly VirtualizedItemGrid[] _sellGrids = new VirtualizedItemGrid[3];
    private readonly Label[] _sellLimitHints = new Label[3];
    private readonly Label[] _sellNoMatchLabels = new Label[3];
    private readonly Label[] _sellEmptyLabels = new Label[3];
    private readonly Dictionary<FilterKind, FilterDropdown> _sellFilters = new();

    public ShopScreen(WarehouseHubScreen hub, WarehouseData warehouse, CarryConfig carry)
    {
        _hub = hub;
        _warehouse = warehouse;
        _carry = carry;
        Layer = 110;

        // Day rollover + first open: re-rolls stock when the stored date is stale, then load the live shop.
        // 翻页/首次打开：日期过期则全量重 roll 库存，然后加载实时商店。
        ShopStore.EnsureStocked();
        _shop = ShopStore.Current;
        _showDurability = WarehouseStore.IsDurabilityEnabled;

        // Defensive: a hand-edited/corrupt save could deserialize Filters as null.
        _warehouse.Filters ??= new WarehouseFilterState();
        _sellQueries[0] = _warehouse.Filters.SellQueryCards ?? "";
        _sellQueries[1] = _warehouse.Filters.SellQueryRelics ?? "";
        _sellQueries[2] = _warehouse.Filters.SellQueryPotions ?? "";
        _sellQuery = _sellQueries[0];
    }

    public override void _Ready()
    {
        Current = this;
        BuildUi();
        Refresh();
        // Don't leave focus on a hub control underneath (typing would go to it).
        GetViewport().GuiReleaseFocus();
    }

    public override void _ExitTree()
    {
        if (Current == this)
        {
            Current = null;
        }

        base._ExitTree();
    }

    public override void _Input(InputEvent inputEvent)
    {
        if (inputEvent is InputEventKey { Pressed: true, Keycode: Key.Escape } key && !key.IsEcho())
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        // The hub below also ticks WarehouseCache; this only re-resolves the sell grids' art when new art lands after
        // a Version bump invalidated the cache (buy/sell) — without it, recycled sell tiles would stay artless.
        // 下层大厅也在推进预载；这里仅在交易使缓存失效后、新贴图到位时重解析出售网格贴图。
        if (WarehouseCache.Tick(PrewarmPerFrame))
        {
            for (int i = 0; i < 3; i++)
            {
                _sellGrids[i].RefreshTextures();
            }
        }
    }

    // ----- Build 构建 -----

    private void BuildUi()
    {
        var root = new Panel { Name = "ShopPanel" };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddThemeStyleboxOverride("panel", ExtractionTheme.BackgroundBox());
        root.Theme = ExtractionTheme.Instance;
        AddChild(root);

        var page = new MarginContainer();
        page.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        page.AddThemeConstantOverride("margin_left", 36);
        page.AddThemeConstantOverride("margin_right", 36);
        page.AddThemeConstantOverride("margin_top", 28);
        page.AddThemeConstantOverride("margin_bottom", 28);
        root.AddChild(page);

        var rootBox = new VBoxContainer();
        rootBox.AddThemeConstantOverride("separation", 20);
        page.AddChild(rootBox);

        rootBox.AddChild(BuildHeader());
        rootBox.AddChild(BuildMainTabBar());

        _buyContent = BuildBuyContent();
        rootBox.AddChild(_buyContent);

        _sellContent = BuildSellContent();
        rootBox.AddChild(_sellContent);
        _sellContent.Visible = false;

        rootBox.AddChild(BuildFooter());
    }

    private Control BuildHeader()
    {
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 16);

        var title = MakeLabel(ExtractionLocalization.ShopTitleText());
        title.AddThemeFontOverride("font", ExtractionTheme.Bold);
        title.AddThemeFontSizeOverride("font_size", ExtractionTheme.FontSizeTitle);
        header.AddChild(title);

        header.AddChild(MakeSpacer());

        var chip = new PanelContainer();
        chip.AddThemeStyleboxOverride("panel", ExtractionTheme.ChipBox());
        var chipLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        chipLabel.AddThemeColorOverride("font_color", ExtractionTheme.GoldChipText);
        chipLabel.AddThemeFontSizeOverride("font_size", ExtractionTheme.FontSizeBody);
        chip.AddChild(chipLabel);
        _goldChipLabel = chipLabel;
        header.AddChild(chip);

        return header;
    }

    private Control BuildMainTabBar()
    {
        var bar = new HBoxContainer();
        bar.AddThemeConstantOverride("separation", 8);

        var group = new ButtonGroup();
        var buy = MakeMainTabButton(ExtractionLocalization.ShopTabBuyText(), group, MainTab.Buy);
        var sell = MakeMainTabButton(ExtractionLocalization.ShopTabSellText(), group, MainTab.Sell);
        buy.ButtonPressed = true;
        bar.AddChild(buy);
        bar.AddChild(sell);
        return bar;
    }

    private Button MakeMainTabButton(string text, ButtonGroup group, MainTab tab)
    {
        var button = new Button
        {
            Text = text,
            ThemeTypeVariation = ExtractionTheme.ButtonTab,
            ToggleMode = true,
            ButtonGroup = group,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0f, 40f),
        };
        button.Toggled += on =>
        {
            if (on)
            {
                SwitchMainTab(tab);
            }
        };
        return button;
    }

    private void SwitchMainTab(MainTab tab)
    {
        if (_activeMain == tab)
        {
            return;
        }

        _activeMain = tab;
        _buyContent.Visible = tab == MainTab.Buy;
        _sellContent.Visible = tab == MainTab.Sell;
        _refreshButton.Visible = tab == MainTab.Buy;
        Refresh();
    }

    // ----- Buy 购买 -----

    private Control BuildBuyContent()
    {
        var box = new VBoxContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 2f,
        };
        box.AddThemeConstantOverride("separation", 8);

        _buyEmptyLabel = MakeHintLabel();
        box.AddChild(_buyEmptyLabel);

        // One outer scroll holding three stacked sections (卡牌 / 遗物 / 药水, top to bottom), each a title + its own
        // tile grid. A section grid has no ScrollContainer parent, so it renders all its tiles — fine, since the
        // doubled stock is bounded (≤16 cards / 8 relics / 6 potions); only the warehouse needs virtualization.
        // 单个外层滚动容器装三个竖排分区（卡牌/遗物/药水，自上而下），每区 = 标题 + 自己的瓦片网格。分区网格没有
        // ScrollContainer 父节点，会渲染全部瓦片——翻倍后数量有界，无需虚拟化；只有仓库才需要。
        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 2f,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        var content = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", 20);
        _buySections[0] = BuildBuySection(content, ExtractionLocalization.SectionCardsText(), 0);
        _buySections[1] = BuildBuySection(content, ExtractionLocalization.SectionRelicsText(), 1);
        _buySections[2] = BuildBuySection(content, ExtractionLocalization.SectionPotionsText(), 2);
        scroll.AddChild(content);
        box.AddChild(scroll);
        return box;
    }

    private Control BuildBuySection(VBoxContainer content, string title, int index)
    {
        var section = new VBoxContainer();
        section.AddThemeConstantOverride("separation", 10);
        var header = MakeLabel(title);
        header.AddThemeFontSizeOverride("font_size", ExtractionTheme.FontSizeTitle);
        section.AddChild(header);

        var grid = new VirtualizedItemGrid();
        grid.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        section.AddChild(grid);
        _buyGrids[index] = grid;

        content.AddChild(section);
        return section;
    }

    private void RefreshBuy()
    {
        var cardRows = new List<VirtualizedItemGrid.RenderData>();
        var relicRows = new List<VirtualizedItemGrid.RenderData>();
        var potionRows = new List<VirtualizedItemGrid.RenderData>();
        foreach (ShopEntry entry in _shop.Entries)
        {
            if (entry.Sold)
            {
                continue;
            }

            if (!TryResolveBuy(entry, out string name, out string pool, out Texture2D? texture, out ModelId id))
            {
                continue;
            }

            int price = ShopStore.BuyPrice(entry);
            // Durability badge only for cards/relics — potions never decrement, and passing a potion id to
            // MaxDurabilityForCard would hard-cast and throw. 耐久角标只给牌/遗物——药水不递减，且把药水 id 交给
            // MaxDurabilityForCard 会硬转崩溃。
            int? durability = _showDurability && entry.Kind != ShopStore.KindPotion
                ? ShopStore.MaxDurabilityFor(entry.Kind, id)
                : null;
            var data = new VirtualizedItemGrid.RenderData(name, pool, 1, () => texture,
                ExtractionItemTiles.ItemTileAction.Buy, null, id, durability, price,
                OnTileClick: tile => TryBuy(entry, tile));
            switch (entry.Kind)
            {
                case ShopStore.KindCard:
                    cardRows.Add(data);
                    break;
                case ShopStore.KindRelic:
                    relicRows.Add(data);
                    break;
                default:
                    potionRows.Add(data);
                    break;
            }
        }

        _buyGrids[0].SetItems(cardRows);
        _buySections[0].Visible = cardRows.Count > 0;
        _buyGrids[1].SetItems(relicRows);
        _buySections[1].Visible = relicRows.Count > 0;
        _buyGrids[2].SetItems(potionRows);
        _buySections[2].Visible = potionRows.Count > 0;

        bool any = cardRows.Count + relicRows.Count + potionRows.Count > 0;
        _buyEmptyLabel.Text = ExtractionLocalization.ShopBuyEmptyText();
        _buyEmptyLabel.Visible = !any;
    }

    private bool TryResolveBuy(ShopEntry entry, out string name, out string pool, out Texture2D? texture, out ModelId id)
    {
        name = "";
        pool = "";
        texture = null;
        id = ModelId.none;
        try
        {
            id = ModelId.Deserialize(entry.Id);
        }
        catch (Exception)
        {
            return false;
        }

        try
        {
            switch (entry.Kind)
            {
                case ShopStore.KindCard:
                {
                    CardModel? card = ModelDb.GetByIdOrNull<CardModel>(id);
                    if (card == null)
                    {
                        return false;
                    }

                    name = card.Title ?? entry.Id;
                    pool = ExtractionLocalization.PoolNameText(ExtractionItemTiles.CardPoolSlug(id));
                    texture = SafeTexture(() => card.Portrait);
                    return true;
                }
                case ShopStore.KindRelic:
                {
                    RelicModel? relic = ModelDb.GetByIdOrNull<RelicModel>(id);
                    if (relic == null)
                    {
                        return false;
                    }

                    name = relic.Title.GetFormattedText();
                    pool = ExtractionLocalization.PoolNameText(ExtractionItemTiles.RelicPoolSlug(id));
                    texture = SafeTexture(() => relic.Icon);
                    return true;
                }
                default:
                {
                    PotionModel? potion = ModelDb.GetByIdOrNull<PotionModel>(id);
                    if (potion == null)
                    {
                        return false;
                    }

                    name = potion.Title.GetFormattedText();
                    pool = ExtractionLocalization.PoolNameText(ExtractionItemTiles.PotionPoolSlug(id));
                    texture = SafeTexture(() => potion.Image);
                    return true;
                }
            }
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void TryBuy(ShopEntry entry, Button tile)
    {
        if (ShopStore.TryBuy(entry, _warehouse))
        {
            Refresh();
            _hub.RefreshForExternalMutation();
            RitsuToastService.ShowInfo(ExtractionLocalization.ShopBoughtText());
        }
        else
        {
            Shake(tile);
            RitsuToastService.ShowInfo(ExtractionLocalization.ShopGoldShortText());
        }
    }

    private void TryRefresh()
    {
        int cost = ShopStore.RefreshCost(_shop);
        if (ShopStore.TryManualRefresh(_warehouse))
        {
            Refresh();
            _hub.RefreshForExternalMutation();
            RitsuToastService.ShowInfo(ExtractionLocalization.ShopRefreshedText(cost));
        }
    }

    // ----- Sell 出售 -----

    private Control BuildSellContent()
    {
        var box = new VBoxContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 2f,
        };
        box.AddThemeConstantOverride("separation", 8);

        var bar = new HBoxContainer();
        bar.AddThemeConstantOverride("separation", 8);
        var group = new ButtonGroup();
        Button subCards = MakeSellTabButton(ExtractionLocalization.SectionCardsText(), group, SellTab.Cards);
        Button subRelics = MakeSellTabButton(ExtractionLocalization.SectionRelicsText(), group, SellTab.Relics);
        Button subPotions = MakeSellTabButton(ExtractionLocalization.SectionPotionsText(), group, SellTab.Potions);
        subCards.ButtonPressed = true;
        bar.AddChild(subCards);
        bar.AddChild(subRelics);
        bar.AddChild(subPotions);
        box.AddChild(bar);

        box.AddChild(BuildSellSearchRow());

        _sellTabContent[(int)SellTab.Cards] = BuildSellTabContent(SellTab.Cards, BuildCardSellFilterArea());
        _sellTabContent[(int)SellTab.Relics] = BuildSellTabContent(SellTab.Relics, BuildRelicSellFilterArea());
        _sellTabContent[(int)SellTab.Potions] = BuildSellTabContent(SellTab.Potions, BuildPotionSellFilterArea());
        foreach (VBoxContainer tab in _sellTabContent)
        {
            box.AddChild(tab);
        }

        _sellTabContent[(int)SellTab.Cards].Visible = true;
        _sellTabContent[(int)SellTab.Relics].Visible = false;
        _sellTabContent[(int)SellTab.Potions].Visible = false;

        box.AddChild(BuildSellActionRow());
        return box;
    }

    private Button MakeSellTabButton(string text, ButtonGroup group, SellTab tab)
    {
        var button = new Button
        {
            Text = text,
            ThemeTypeVariation = ExtractionTheme.ButtonTab,
            ToggleMode = true,
            ButtonGroup = group,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0f, 40f),
        };
        button.Toggled += on =>
        {
            if (on)
            {
                SwitchSellTab(tab);
            }
        };
        return button;
    }

    private Control BuildSellSearchRow()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);

        _sellSearchEdit = new LineEdit
        {
            Text = _sellQuery,
            PlaceholderText = ExtractionLocalization.SearchPlaceholderText(),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0f, 40f),
        };
        _sellSearchEdit.TextChanged += OnSellSearchTextChanged;
        row.AddChild(_sellSearchEdit);

        _sellClearButton = MakeButton("×", ExtractionTheme.ButtonSecondary);
        _sellClearButton.CustomMinimumSize = new Vector2(40f, 40f);
        _sellClearButton.AddThemeFontSizeOverride("font_size", 18);
        _sellClearButton.Visible = _sellQuery.Length > 0;
        _sellClearButton.Pressed += ClearSellSearch;
        row.AddChild(_sellClearButton);
        return row;
    }

    private VBoxContainer BuildSellTabContent(SellTab tab, Control filterArea)
    {
        var box = new VBoxContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 2f,
        };
        box.AddThemeConstantOverride("separation", 8);
        box.AddChild(filterArea);

        var limit = MakeHintLabel();
        var noMatch = MakeHintLabel();
        var empty = MakeHintLabel();
        box.AddChild(limit);
        box.AddChild(noMatch);
        box.AddChild(empty);
        _sellLimitHints[(int)tab] = limit;
        _sellNoMatchLabels[(int)tab] = noMatch;
        _sellEmptyLabels[(int)tab] = empty;

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 2f,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        var grid = new VirtualizedItemGrid();
        scroll.AddChild(grid);
        box.AddChild(scroll);
        _sellGrids[(int)tab] = grid;
        return box;
    }

    private Control BuildCardSellFilterArea()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        _sellFilters[FilterKind.CardPools] = MakeSellFilterDropdown(ExtractionLocalization.FilterPoolText());
        _sellFilters[FilterKind.CardRarities] = MakeSellFilterDropdown(ExtractionLocalization.FilterRarityText());
        _sellFilters[FilterKind.CardTypes] = MakeSellFilterDropdown(ExtractionLocalization.FilterTypeText());
        _sellFilters[FilterKind.CardCosts] = MakeSellFilterDropdown(ExtractionLocalization.FilterCostText());
        _sellFilters[FilterKind.CardSources] = MakeSellFilterDropdown(ExtractionLocalization.FilterSourceText());
        if (_showDurability)
        {
            _sellFilters[FilterKind.CardDurabilities] = MakeSellFilterDropdown(ExtractionLocalization.FilterDurabilityText());
        }

        row.AddChild(_sellFilters[FilterKind.CardPools]);
        row.AddChild(_sellFilters[FilterKind.CardRarities]);
        row.AddChild(_sellFilters[FilterKind.CardTypes]);
        row.AddChild(_sellFilters[FilterKind.CardCosts]);
        row.AddChild(_sellFilters[FilterKind.CardSources]);
        if (_showDurability)
        {
            row.AddChild(_sellFilters[FilterKind.CardDurabilities]);
        }

        return row;
    }

    private Control BuildRelicSellFilterArea()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        _sellFilters[FilterKind.RelicPools] = MakeSellFilterDropdown(ExtractionLocalization.FilterPoolText());
        _sellFilters[FilterKind.RelicRarities] = MakeSellFilterDropdown(ExtractionLocalization.FilterRarityText());
        _sellFilters[FilterKind.RelicSources] = MakeSellFilterDropdown(ExtractionLocalization.FilterSourceText());
        if (_showDurability)
        {
            _sellFilters[FilterKind.RelicDurabilities] = MakeSellFilterDropdown(ExtractionLocalization.FilterDurabilityText());
        }

        row.AddChild(_sellFilters[FilterKind.RelicPools]);
        row.AddChild(_sellFilters[FilterKind.RelicRarities]);
        row.AddChild(_sellFilters[FilterKind.RelicSources]);
        if (_showDurability)
        {
            row.AddChild(_sellFilters[FilterKind.RelicDurabilities]);
        }

        return row;
    }

    private Control BuildPotionSellFilterArea()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        _sellFilters[FilterKind.PotionPools] = MakeSellFilterDropdown(ExtractionLocalization.FilterPoolText());
        _sellFilters[FilterKind.PotionRarities] = MakeSellFilterDropdown(ExtractionLocalization.FilterRarityText());
        _sellFilters[FilterKind.PotionSources] = MakeSellFilterDropdown(ExtractionLocalization.FilterSourceText());
        row.AddChild(_sellFilters[FilterKind.PotionPools]);
        row.AddChild(_sellFilters[FilterKind.PotionRarities]);
        row.AddChild(_sellFilters[FilterKind.PotionSources]);
        return row;
    }

    private FilterDropdown MakeSellFilterDropdown(string title)
    {
        var dropdown = new FilterDropdown { Title = title };
        dropdown.SelectionChanged += () =>
        {
            SaveSellFilters();
            RefreshSell();
        };
        return dropdown;
    }

    private Control BuildSellActionRow()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);

        _sellSummaryLabel = MakeLabel("");
        _sellSummaryLabel.AddThemeColorOverride("font_color", ExtractionTheme.TextSecondary);
        _sellSummaryLabel.AddThemeFontSizeOverride("font_size", ExtractionTheme.FontSizeSmall);
        row.AddChild(_sellSummaryLabel);

        row.AddChild(MakeSpacer());

        _sellSelectedButton = MakeButton(ExtractionLocalization.ShopSellSelectedText(), ExtractionTheme.ButtonPrimary);
        _sellSelectedButton.CustomMinimumSize = new Vector2(150f, 44f);
        _sellSelectedButton.Pressed += SellSelected;
        row.AddChild(_sellSelectedButton);

        return row;
    }

    // ----- Sell refresh 出售刷新 -----

    private void RefreshSell()
    {
        List<WarehouseCard> availableCards = AvailableCards(_warehouse, _carry);
        List<WarehouseRelic> availableRelics = AvailableRelics(_warehouse, _carry);
        List<SerializablePotion> availablePotions = AvailablePotions(_warehouse, _carry);

        switch (_activeSell)
        {
            case SellTab.Cards:
                UpdateSellCards(availableCards);
                break;
            case SellTab.Relics:
                UpdateSellRelics(availableRelics);
                break;
            case SellTab.Potions:
                UpdateSellPotions(availablePotions);
                break;
        }

        UpdateSellSummary();
    }

    private void UpdateSellCards(List<WarehouseCard> available)
    {
        List<ExtractionItemTiles.CardGroup> groups = ExtractionItemTiles.GroupCards(available, loadArt: false);
        Dictionary<string, int> valueByKey = SellValueByKey(ShopStore.KindCard, available, c => c.Card.Id, c => c.Durability);

        SetSellFilterOptions(_sellFilters[FilterKind.CardPools],
            groups.Select(g => g.PoolSlug).Where(s => s.Length > 0).Distinct().ToList(),
            _warehouse.Filters.SellCardPools, ExtractionLocalization.PoolNameText);
        SetSellFilterOptions(_sellFilters[FilterKind.CardRarities],
            groups.Select(g => g.Rarity).Distinct().OrderBy(r => (int)r).Select(r => r.ToString()).ToList(),
            _warehouse.Filters.SellCardRarities, ExtractionLocalization.FilterRarityLabel);
        SetSellFilterOptions(_sellFilters[FilterKind.CardTypes],
            groups.Select(g => g.Type).Distinct().OrderBy(t => (int)t).Select(t => t.ToString()).ToList(),
            _warehouse.Filters.SellCardTypes, ExtractionLocalization.FilterTypeLabel);
        SetSellFilterOptions(_sellFilters[FilterKind.CardCosts],
            groups.Select(g => g.Cost).Distinct().OrderBy(c => (int)c).Select(c => c.ToString()).ToList(),
            _warehouse.Filters.SellCardCosts, ExtractionLocalization.FilterCostLabel);
        SetSellFilterOptions(_sellFilters[FilterKind.CardSources],
            OrderSourceKeys(groups.Select(g => g.Source.SourceKey)),
            _warehouse.Filters.SellCardSources, ExtractionLocalization.FilterSourceLabel);
        if (_showDurability)
        {
            SetSellFilterOptions(_sellFilters[FilterKind.CardDurabilities],
                DurabilityFilterValues.ToList(), _warehouse.Filters.SellCardDurabilities,
                ExtractionLocalization.FilterDurabilityLabel);
        }

        var rows = new List<VirtualizedItemGrid.RenderData>();
        int filtered = 0;
        foreach (ExtractionItemTiles.CardGroup g in groups)
        {
            if (!MatchesCardFilters(g))
            {
                continue;
            }

            filtered++;
            if (rows.Count >= MaxTileKinds)
            {
                continue;
            }

            string key = ExtractionItemTiles.CardKey(g);
            rows.Add(new VirtualizedItemGrid.RenderData(g.Name, g.Pool, g.Count,
                () => WarehouseCache.Resolve(g.PortraitPath), ExtractionItemTiles.ItemTileAction.Display,
                () => ChangeSellSelection(SellTab.Cards, key, step: +1, max: Input.IsKeyPressed(Key.Shift), available: g.Count),
                g.Rep.Id,
                _showDurability ? g.Durability : null, valueByKey.GetValueOrDefault(key),
                _selectedCards.GetValueOrDefault(key),
                OnRightClick: () => ChangeSellSelection(SellTab.Cards, key, step: -1,
                    max: Input.IsKeyPressed(Key.Shift), available: g.Count)));
        }

        _sellGrids[(int)SellTab.Cards].SetItems(rows);
        UpdateSellLimitHint(SellTab.Cards, filtered, rows.Count);
        UpdateSellTabHints(SellTab.Cards, rows.Count, groups.Count == 0, AnySellFilterActive(FilterKind.CardPools)
            || AnySellFilterActive(FilterKind.CardRarities) || AnySellFilterActive(FilterKind.CardTypes)
            || AnySellFilterActive(FilterKind.CardCosts) || AnySellFilterActive(FilterKind.CardSources)
            || (_showDurability && AnySellFilterActive(FilterKind.CardDurabilities)));
    }

    private void UpdateSellRelics(List<WarehouseRelic> available)
    {
        List<ExtractionItemTiles.RelicGroup> groups = ExtractionItemTiles.GroupRelics(available, loadArt: false);
        Dictionary<string, int> valueByKey = SellValueByKey(ShopStore.KindRelic, available, r => r.Relic.Id, r => r.Durability);

        SetSellFilterOptions(_sellFilters[FilterKind.RelicPools],
            groups.Select(g => g.PoolSlug).Where(s => s.Length > 0).Distinct().ToList(),
            _warehouse.Filters.SellRelicPools, ExtractionLocalization.PoolNameText);
        SetSellFilterOptions(_sellFilters[FilterKind.RelicRarities],
            groups.Select(g => g.Rarity).Distinct().OrderBy(r => (int)r).Select(r => r.ToString()).ToList(),
            _warehouse.Filters.SellRelicRarities, ExtractionLocalization.FilterRarityLabel);
        SetSellFilterOptions(_sellFilters[FilterKind.RelicSources],
            OrderSourceKeys(groups.Select(g => g.Source.SourceKey)),
            _warehouse.Filters.SellRelicSources, ExtractionLocalization.FilterSourceLabel);
        if (_showDurability)
        {
            SetSellFilterOptions(_sellFilters[FilterKind.RelicDurabilities],
                DurabilityFilterValues.ToList(), _warehouse.Filters.SellRelicDurabilities,
                ExtractionLocalization.FilterDurabilityLabel);
        }

        var rows = new List<VirtualizedItemGrid.RenderData>();
        int filtered = 0;
        foreach (ExtractionItemTiles.RelicGroup g in groups)
        {
            if (!MatchesRelicFilters(g))
            {
                continue;
            }

            filtered++;
            if (rows.Count >= MaxTileKinds)
            {
                continue;
            }

            string key = ExtractionItemTiles.RelicKey(g);
            rows.Add(new VirtualizedItemGrid.RenderData(g.Name, g.Pool, g.Count,
                () => WarehouseCache.Resolve(g.IconPath), ExtractionItemTiles.ItemTileAction.Display,
                () => ChangeSellSelection(SellTab.Relics, key, step: +1, max: Input.IsKeyPressed(Key.Shift), available: g.Count),
                g.Rep.Id,
                _showDurability ? g.Durability : null, valueByKey.GetValueOrDefault(key),
                _selectedRelics.GetValueOrDefault(key),
                OnRightClick: () => ChangeSellSelection(SellTab.Relics, key, step: -1,
                    max: Input.IsKeyPressed(Key.Shift), available: g.Count)));
        }

        _sellGrids[(int)SellTab.Relics].SetItems(rows);
        UpdateSellLimitHint(SellTab.Relics, filtered, rows.Count);
        UpdateSellTabHints(SellTab.Relics, rows.Count, groups.Count == 0, AnySellFilterActive(FilterKind.RelicPools)
            || AnySellFilterActive(FilterKind.RelicRarities) || AnySellFilterActive(FilterKind.RelicSources)
            || (_showDurability && AnySellFilterActive(FilterKind.RelicDurabilities)));
    }

    private void UpdateSellPotions(List<SerializablePotion> available)
    {
        List<ExtractionItemTiles.PotionGroup> groups = ExtractionItemTiles.GroupPotions(available, loadArt: false);
        Dictionary<string, int> valueByKey = SellValueByKey(ShopStore.KindPotion, available, p => p.Id, _ => 0);

        SetSellFilterOptions(_sellFilters[FilterKind.PotionPools],
            groups.Select(g => g.PoolSlug).Where(s => s.Length > 0).Distinct().ToList(),
            _warehouse.Filters.SellPotionPools, ExtractionLocalization.PoolNameText);
        SetSellFilterOptions(_sellFilters[FilterKind.PotionRarities],
            groups.Select(g => g.Rarity).Distinct().OrderBy(r => (int)r).Select(r => r.ToString()).ToList(),
            _warehouse.Filters.SellPotionRarities, ExtractionLocalization.FilterRarityLabel);
        SetSellFilterOptions(_sellFilters[FilterKind.PotionSources],
            OrderSourceKeys(groups.Select(g => g.Source.SourceKey)),
            _warehouse.Filters.SellPotionSources, ExtractionLocalization.FilterSourceLabel);

        var rows = new List<VirtualizedItemGrid.RenderData>();
        int filtered = 0;
        foreach (ExtractionItemTiles.PotionGroup g in groups)
        {
            if (!MatchesPotionFilters(g))
            {
                continue;
            }

            filtered++;
            if (rows.Count >= MaxTileKinds)
            {
                continue;
            }

            string key = ExtractionItemTiles.PotionKey(g);
            rows.Add(new VirtualizedItemGrid.RenderData(g.Name, g.Pool, g.Count,
                () => WarehouseCache.Resolve(g.ImagePath), ExtractionItemTiles.ItemTileAction.Display,
                () => ChangeSellSelection(SellTab.Potions, key, step: +1, max: Input.IsKeyPressed(Key.Shift), available: g.Count),
                g.Rep.Id, null,
                valueByKey.GetValueOrDefault(key), _selectedPotions.GetValueOrDefault(key),
                OnRightClick: () => ChangeSellSelection(SellTab.Potions, key, step: -1,
                    max: Input.IsKeyPressed(Key.Shift), available: g.Count)));
        }

        _sellGrids[(int)SellTab.Potions].SetItems(rows);
        UpdateSellLimitHint(SellTab.Potions, filtered, rows.Count);
        UpdateSellTabHints(SellTab.Potions, rows.Count, groups.Count == 0, AnySellFilterActive(FilterKind.PotionPools)
            || AnySellFilterActive(FilterKind.PotionRarities) || AnySellFilterActive(FilterKind.PotionSources));
    }

    /// <summary>Per-group total sell value keyed by id string (sum of each available copy's sell value).
    /// 每分组的可售总价（按 id 字符串，各可售副本卖价之和）。</summary>
    private Dictionary<string, int> SellValueByKey<T>(string kind, List<T> copies, Func<T, ModelId?> idOf, Func<T, int> durabilityOf)
    {
        var map = new Dictionary<string, int>();
        foreach (T copy in copies)
        {
            if (idOf(copy) is ModelId id)
            {
                string key = ExtractionItemTiles.Key(id);
                map[key] = map.GetValueOrDefault(key) + ShopStore.SellValue(kind, id, durabilityOf(copy));
            }
        }

        return map;
    }

    private bool MatchesCardFilters(ExtractionItemTiles.CardGroup g)
    {
        if (_sellQuery.Length > 0 && !g.Haystack.Contains(_sellQuery))
        {
            return false;
        }

        List<string> pools = _warehouse.Filters.SellCardPools;
        List<string> rarities = _warehouse.Filters.SellCardRarities;
        List<string> types = _warehouse.Filters.SellCardTypes;
        List<string> costs = _warehouse.Filters.SellCardCosts;
        List<string> sources = _warehouse.Filters.SellCardSources;
        List<string> durabilities = _warehouse.Filters.SellCardDurabilities;
        if ((pools.Count > 0 && !pools.Contains(g.PoolSlug))
            || (rarities.Count > 0 && !rarities.Contains(g.Rarity.ToString()))
            || (types.Count > 0 && !types.Contains(g.Type.ToString()))
            || (costs.Count > 0 && !costs.Contains(g.Cost.ToString()))
            || (sources.Count > 0 && !sources.Contains(g.Source.SourceKey)))
        {
            return false;
        }

        return durabilities.Count == 0 || MatchesDurability(ShopStore.KindCard, g.Durability, g.Rep.Id, durabilities);
    }

    private bool MatchesRelicFilters(ExtractionItemTiles.RelicGroup g)
    {
        if (_sellQuery.Length > 0 && !g.Haystack.Contains(_sellQuery))
        {
            return false;
        }

        List<string> pools = _warehouse.Filters.SellRelicPools;
        List<string> rarities = _warehouse.Filters.SellRelicRarities;
        List<string> sources = _warehouse.Filters.SellRelicSources;
        List<string> durabilities = _warehouse.Filters.SellRelicDurabilities;
        if ((pools.Count > 0 && !pools.Contains(g.PoolSlug))
            || (rarities.Count > 0 && !rarities.Contains(g.Rarity.ToString()))
            || (sources.Count > 0 && !sources.Contains(g.Source.SourceKey)))
        {
            return false;
        }

        return durabilities.Count == 0 || MatchesDurability(ShopStore.KindRelic, g.Durability, g.Rep.Id, durabilities);
    }

    private bool MatchesPotionFilters(ExtractionItemTiles.PotionGroup g)
    {
        if (_sellQuery.Length > 0 && !g.Haystack.Contains(_sellQuery))
        {
            return false;
        }

        List<string> pools = _warehouse.Filters.SellPotionPools;
        List<string> rarities = _warehouse.Filters.SellPotionRarities;
        List<string> sources = _warehouse.Filters.SellPotionSources;
        return (pools.Count == 0 || pools.Contains(g.PoolSlug))
               && (rarities.Count == 0 || rarities.Contains(g.Rarity.ToString()))
               && (sources.Count == 0 || sources.Contains(g.Source.SourceKey));
    }

    /// <summary>Durability filter predicate on a group's lowest durability: full (== its rarity max), ≥2, ≤1.
    /// Selected tiers OR together. <paramref name="kind"/> picks the right max-durability table (cards per-rarity, relics
    /// unified). 耐久度过滤谓词（按分组最低耐久）：完整（== 稀有度上限）、≥2、≤1；选中项取或。kind 决定满耐久查哪张表。</summary>
    private bool MatchesDurability(string kind, int minDurability, ModelId? id, List<string> selected)
    {
        if (id == null)
        {
            return true;
        }

        foreach (string option in selected)
        {
            switch (option)
            {
                case "full" when minDurability >= ShopStore.MaxDurabilityFor(kind, id):
                    return true;
                case "ge2" when minDurability >= 2:
                    return true;
                case "le1" when minDurability <= 1:
                    return true;
            }
        }

        return false;
    }

    private void UpdateSellLimitHint(SellTab tab, int filtered, int rendered)
    {
        bool capped = filtered > rendered;
        _sellLimitHints[(int)tab].Text = ExtractionLocalization.SearchLimitText(MaxTileKinds, filtered);
        _sellLimitHints[(int)tab].Visible = capped;
    }

    private void UpdateSellTabHints(SellTab tab, int rendered, bool categoryEmpty, bool anyFilter)
    {
        bool noMatch = !categoryEmpty && rendered == 0 && anyFilter;
        _sellEmptyLabels[(int)tab].Text = ExtractionLocalization.ShopSellEmptyText();
        _sellEmptyLabels[(int)tab].Visible = categoryEmpty;
        _sellNoMatchLabels[(int)tab].Text = ExtractionLocalization.SearchNoMatchText(SectionTitle(tab));
        _sellNoMatchLabels[(int)tab].Visible = noMatch;
        _sellGrids[(int)tab].Visible = rendered > 0;
    }

    private void UpdateSellSummary()
    {
        (_, _, _, int gold, int count) = CollectSelectedCopies();
        _sellSummaryLabel.Text = ExtractionLocalization.ShopSelectionSummaryText(count, gold);
        _sellSelectedButton.Disabled = count == 0;
    }

    private void ChangeSellSelection(SellTab tab, string key, int step, bool max, int available)
    {
        Dictionary<string, int> counts = tab switch
        {
            SellTab.Cards => _selectedCards,
            SellTab.Relics => _selectedRelics,
            _ => _selectedPotions,
        };
        int current = counts.GetValueOrDefault(key, 0);
        int target = max ? (step > 0 ? available : 0) : Math.Clamp(current + step, 0, available);
        if (target > 0)
        {
            counts[key] = target;
        }
        else
        {
            counts.Remove(key);
        }

        RefreshSell();
    }

    private void OnSellSearchTextChanged(string text)
    {
        _sellQuery = text.Trim().ToLowerInvariant();
        _sellQueries[(int)_activeSell] = _sellQuery;
        _sellClearButton.Visible = _sellQuery.Length > 0;
        RefreshSell();
    }

    private void ClearSellSearch()
    {
        _sellSearchEdit.Text = "";
        _sellQuery = "";
        _sellQueries[(int)_activeSell] = "";
        _sellClearButton.Visible = false;
        RefreshSell();
    }

    private void SwitchSellTab(SellTab tab)
    {
        if (_activeSell == tab)
        {
            return;
        }

        _sellQueries[(int)_activeSell] = _sellQuery;
        _activeSell = tab;
        _sellQuery = _sellQueries[(int)tab];
        _sellSearchEdit.Text = _sellQuery;
        _sellClearButton.Visible = _sellQuery.Length > 0;
        for (int i = 0; i < 3; i++)
        {
            _sellTabContent[i].Visible = (SellTab)i == tab;
        }

        RefreshSell();
    }

    private bool AnySellFilterActive(FilterKind kind) => _sellFilters.TryGetValue(kind, out FilterDropdown? d) && d.Selected.Count > 0;

    private void SetSellFilterOptions(FilterDropdown dropdown, List<string> values, IReadOnlyList<string> persisted,
        Func<string, string> label)
    {
        dropdown.SetOptions(values.Select(v => (v, label(v))));
        dropdown.SetSelected(persisted);
    }

    private static List<string> OrderSourceKeys(IEnumerable<string> keys)
    {
        var present = keys.Where(k => k.Length > 0).Distinct().ToList();
        List<string> mods = present
            .Where(k => k != ContentSource.BaseKey && k != ContentSource.UnknownKey)
            .OrderBy(ExtractionLocalization.FilterSourceLabel, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new[] { ContentSource.BaseKey }.Where(present.Contains)
            .Concat(mods)
            .Concat(new[] { ContentSource.UnknownKey }.Where(present.Contains))
            .ToList();
    }

    private void SaveSellFilters()
    {
        _warehouse.Filters.SellQueryCards = _sellQueries[0];
        _warehouse.Filters.SellQueryRelics = _sellQueries[1];
        _warehouse.Filters.SellQueryPotions = _sellQueries[2];
        _warehouse.Filters.SellCardPools = _sellFilters[FilterKind.CardPools].Selected.ToList();
        _warehouse.Filters.SellCardRarities = _sellFilters[FilterKind.CardRarities].Selected.ToList();
        _warehouse.Filters.SellCardTypes = _sellFilters[FilterKind.CardTypes].Selected.ToList();
        _warehouse.Filters.SellCardCosts = _sellFilters[FilterKind.CardCosts].Selected.ToList();
        _warehouse.Filters.SellCardSources = _sellFilters[FilterKind.CardSources].Selected.ToList();
        _warehouse.Filters.SellRelicPools = _sellFilters[FilterKind.RelicPools].Selected.ToList();
        _warehouse.Filters.SellRelicRarities = _sellFilters[FilterKind.RelicRarities].Selected.ToList();
        _warehouse.Filters.SellRelicSources = _sellFilters[FilterKind.RelicSources].Selected.ToList();
        _warehouse.Filters.SellPotionPools = _sellFilters[FilterKind.PotionPools].Selected.ToList();
        _warehouse.Filters.SellPotionRarities = _sellFilters[FilterKind.PotionRarities].Selected.ToList();
        _warehouse.Filters.SellPotionSources = _sellFilters[FilterKind.PotionSources].Selected.ToList();
        if (_showDurability)
        {
            _warehouse.Filters.SellCardDurabilities = _sellFilters[FilterKind.CardDurabilities].Selected.ToList();
            _warehouse.Filters.SellRelicDurabilities = _sellFilters[FilterKind.RelicDurabilities].Selected.ToList();
        }
    }

    // ----- Sell actions 出售动作 -----

    private void SellSelected()
    {
        (List<WarehouseCard> cards, List<WarehouseRelic> relics, List<SerializablePotion> potions, int gold, int count) =
            CollectSelectedCopies();
        if (count == 0)
        {
            return;
        }

        WarehouseStore.Sell(cards, relics, potions, gold);
        ClearSelection();
        AfterSell(count, gold);
    }

    private void AfterSell(int count, int gold)
    {
        Refresh();
        _hub.RefreshForExternalMutation();
        RitsuToastService.ShowInfo(ExtractionLocalization.ShopSoldText(count, gold));
    }

    private void ClearSelection()
    {
        _selectedCards.Clear();
        _selectedRelics.Clear();
        _selectedPotions.Clear();
    }

    /// <summary>Collects the exact available copies matching <paramref name="predicates"/>, plus their total value.
    /// 按谓词收集匹配的可售副本及总价。</summary>
    private (List<WarehouseCard> Cards, List<WarehouseRelic> Relics, List<SerializablePotion> Potions, int Gold, int Count)
        CollectCopies(Func<WarehouseCard, bool> cardPredicate, Func<WarehouseRelic, bool> relicPredicate,
            Func<SerializablePotion, bool> potionPredicate)
    {
        List<WarehouseCard> cards = AvailableCards(_warehouse, _carry).Where(cardPredicate).ToList();
        List<WarehouseRelic> relics = AvailableRelics(_warehouse, _carry).Where(relicPredicate).ToList();
        List<SerializablePotion> potions = AvailablePotions(_warehouse, _carry).Where(potionPredicate).ToList();
        int gold = cards.Sum(c => ShopStore.SellValue(ShopStore.KindCard, c.Card.Id!, c.Durability))
                   + relics.Sum(r => ShopStore.SellValue(ShopStore.KindRelic, r.Relic.Id!, r.Durability))
                   + potions.Sum(p => ShopStore.SellValue(ShopStore.KindPotion, p.Id!, 1));
        return (cards, relics, potions, gold, cards.Count + relics.Count + potions.Count);
    }

    /// <summary>Collects the exact SELECTED copies (per-group counts, lowest durability first), plus their total value.
    /// The tile selection is per-copy, so each group is capped at its selected count instead of collecting every copy.
    /// 收集精确选中的副本（按分组选中数，最低耐久优先）及总价。瓦片按份选择，因此每组只收集其选中数，而不是整组全收。</summary>
    private (List<WarehouseCard> Cards, List<WarehouseRelic> Relics, List<SerializablePotion> Potions, int Gold, int Count)
        CollectSelectedCopies()
    {
        List<WarehouseCard> cards = TakeSelected(AvailableCards(_warehouse, _carry), _selectedCards,
            c => ExtractionItemTiles.Key(c.Card.Id), c => c.Durability);
        List<WarehouseRelic> relics = TakeSelected(AvailableRelics(_warehouse, _carry), _selectedRelics,
            r => ExtractionItemTiles.Key(r.Relic.Id), r => r.Durability);
        List<SerializablePotion> potions = TakeSelected(AvailablePotions(_warehouse, _carry), _selectedPotions,
            p => ExtractionItemTiles.Key(p.Id), _ => 0);
        int gold = cards.Sum(c => ShopStore.SellValue(ShopStore.KindCard, c.Card.Id!, c.Durability))
                   + relics.Sum(r => ShopStore.SellValue(ShopStore.KindRelic, r.Relic.Id!, r.Durability))
                   + potions.Sum(p => ShopStore.SellValue(ShopStore.KindPotion, p.Id!, 1));
        return (cards, relics, potions, gold, cards.Count + relics.Count + potions.Count);
    }

    /// <summary>Takes up to the selected count of each group's available copies, lowest durability first (scrap the
    /// most-worn copies first, matching the mod's gear-use ordering). 每组可用副本按选中数各取若干份，最低耐久优先。</summary>
    private static List<T> TakeSelected<T>(List<T> available, Dictionary<string, int> selection,
        Func<T, string> keyOf, Func<T, int> durabilityOf)
    {
        var result = new List<T>();
        foreach (IGrouping<string, T> group in available.GroupBy(keyOf))
        {
            int want = selection.GetValueOrDefault(group.Key, 0);
            if (want <= 0)
            {
                continue;
            }

            result.AddRange(group.OrderBy(durabilityOf).Take(want));
        }

        return result;
    }

    // ----- Availability (warehouse − carry) 可用性（仓库 − 携带） -----

    private static List<WarehouseCard> AvailableCards(WarehouseData warehouse, CarryConfig carry)
    {
        var consumed = ConsumedByKey(carry.Cards, c => c.Card.Id, c => c.Durability);
        var result = new List<WarehouseCard>();
        foreach (WarehouseCard c in warehouse.Cards)
        {
            if (c.Card.Id is not ModelId id)
            {
                continue;
            }

            if (Consume(consumed, id, c.Durability))
            {
                continue;
            }

            result.Add(c);
        }

        return result;
    }

    private static List<WarehouseRelic> AvailableRelics(WarehouseData warehouse, CarryConfig carry)
    {
        var consumed = ConsumedByKey(carry.Relics, r => r.Relic.Id, r => r.Durability);
        var result = new List<WarehouseRelic>();
        foreach (WarehouseRelic r in warehouse.Relics)
        {
            if (r.Relic.Id is not ModelId id)
            {
                continue;
            }

            if (Consume(consumed, id, r.Durability))
            {
                continue;
            }

            result.Add(r);
        }

        return result;
    }

    private static List<SerializablePotion> AvailablePotions(WarehouseData warehouse, CarryConfig carry)
    {
        var consumed = new Dictionary<string, int>();
        foreach (SerializablePotion p in carry.Potions)
        {
            if (p.Id is ModelId id)
            {
                string key = id.ToString();
                consumed[key] = consumed.GetValueOrDefault(key) + 1;
            }
        }

        var result = new List<SerializablePotion>();
        foreach (SerializablePotion p in warehouse.Potions)
        {
            if (p.Id is not ModelId id)
            {
                continue;
            }

            string key = id.ToString();
            if (consumed.TryGetValue(key, out int n) && n > 0)
            {
                consumed[key] = n - 1;
                continue;
            }

            result.Add(p);
        }

        return result;
    }

    private static Dictionary<string, int> ConsumedByKey<T>(IEnumerable<T> copies, Func<T, ModelId?> idOf, Func<T, int> durOf)
    {
        var map = new Dictionary<string, int>();
        foreach (T copy in copies)
        {
            if (idOf(copy) is ModelId id)
            {
                string key = id.ToString() + "|" + durOf(copy);
                map[key] = map.GetValueOrDefault(key) + 1;
            }
        }

        return map;
    }

    private static bool Consume(Dictionary<string, int> consumed, ModelId id, int durability)
    {
        string key = id.ToString() + "|" + durability;
        if (consumed.TryGetValue(key, out int n) && n > 0)
        {
            consumed[key] = n - 1;
            return true;
        }

        return false;
    }

    // ----- Footer 底部 -----

    private Control BuildFooter()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);

        // Bottom-left: back to the warehouse hub (the shop's only destination). 左下角：返回仓库大厅。
        var warehouse = MakeButton(ExtractionLocalization.ShopWarehouseButtonText(), ExtractionTheme.ButtonSecondary);
        warehouse.CustomMinimumSize = new Vector2(0f, 44f);
        warehouse.Pressed += Close;
        row.AddChild(warehouse);

        row.AddChild(MakeSpacer());

        // Bottom-right: refresh shop (replaces the vanilla card-removal slot). Shown on the buy tab only.
        _refreshButton = MakeButton("", ExtractionTheme.ButtonSecondary);
        _refreshButton.CustomMinimumSize = new Vector2(0f, 44f);
        _refreshButton.Pressed += TryRefresh;
        row.AddChild(_refreshButton);

        return row;
    }

    // ----- Refresh 重建 -----

    private void Refresh()
    {
        _goldChipLabel.Text = ExtractionLocalization.GoldWarehouseText(_warehouse.Gold);

        int refreshCost = ShopStore.RefreshCost(_shop);
        _refreshButton.Text = ExtractionLocalization.ShopRefreshText(refreshCost);
        _refreshButton.Disabled = _warehouse.Gold < refreshCost;

        if (_activeMain == MainTab.Buy)
        {
            RefreshBuy();
        }
        else
        {
            RefreshSell();
        }
    }

    private void Close()
    {
        SaveSellFilters();
        WarehouseStore.Persist();
        // The hub was hidden when the shop opened — bring it back (defensively guarded) and refresh it so gold/stock
        // reflect the shop's trades. 打开商店时大厅被隐藏——这里恢复（防御性判活）并刷新，让金币/库存反映商店的交易。
        if (GodotObject.IsInstanceValid(_hub))
        {
            _hub.Visible = true;
            _hub.RefreshForExternalMutation();
        }

        QueueFree();
    }

    private static string SectionTitle(SellTab tab) => tab switch
    {
        SellTab.Cards => ExtractionLocalization.SectionCardsText(),
        SellTab.Relics => ExtractionLocalization.SectionRelicsText(),
        _ => ExtractionLocalization.SectionPotionsText(),
    };

    // ----- Helpers 辅助 -----

    private static Label MakeHintLabel()
    {
        var label = new Label { Visible = false, HorizontalAlignment = HorizontalAlignment.Center };
        label.AddThemeColorOverride("font_color", ExtractionTheme.TextSecondary);
        label.AddThemeFontSizeOverride("font_size", ExtractionTheme.FontSizeSmall);
        return label;
    }

    private static Button MakeButton(string text, string variation)
    {
        return new Button
        {
            Text = text,
            ThemeTypeVariation = variation,
        };
    }

    private static Label MakeLabel(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeColorOverride("font_color", ExtractionTheme.Text);
        return label;
    }

    private static Control MakeSpacer()
    {
        return new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
    }

    private static Texture2D? SafeTexture(Func<Texture2D?> getter)
    {
        try
        {
            return getter();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void Shake(Control node)
    {
        float x = node.Position.X;
        var tween = node.CreateTween();
        tween.TweenProperty(node, "position:x", x + 7f, 0.06).SetTrans(Tween.TransitionType.Quad);
        tween.TweenProperty(node, "position:x", x - 7f, 0.12).SetTrans(Tween.TransitionType.Quad);
        tween.TweenProperty(node, "position:x", x, 0.06).SetTrans(Tween.TransitionType.Quad);
    }
}
