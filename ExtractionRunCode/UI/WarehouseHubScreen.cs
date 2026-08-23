using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using STS2RitsuLib.Ui.Toast;
using ExtractionRun.Data;
using ExtractionRun.Lifecycle;
using ExtractionRun.Modifier;
using ExtractionRun.Networking;
using ExtractionRun.Settings;

namespace ExtractionRun.UI;

/// <summary>
/// The 搜打撤 warehouse hub: a full-screen overlay opened from the main menu. Shows the persistent warehouse
/// (cards / relics / potions / gold), lets the player pick a carry config (by default capacity-limited — cards cost by
/// rarity, relics a flat amount, potions ≤ slots, gold; the OFF mode reverts to the MaxCarryCards/MaxCarryRelics count
/// caps), seeds + migrates the warehouse on first open, and launches the run.
/// The warehouse side is split into three tabs (cards / relics / potions) with per-tab search + multi-select filters
/// (persisted in <see cref="WarehouseFilterState"/>), rendered by a pooled <see cref="VirtualizedItemGrid"/> with
/// background art preload (<see cref="WarehouseCache"/>); the carry panel stays on the right and never tab-switches.
/// 搜打撤仓库大厅：主菜单打开的全屏覆盖层。展示仓库、编辑携带配置（默认按背包容量限制——卡牌按稀有度占格、遗物统一占格、
/// 药水 ≤ 栏位、金币；OFF 模式回退到 MaxCarryCards/MaxCarryRelics 数量上限）、首次种子/迁移、发起跑局。仓库侧拆为卡牌/遗物/
/// 药水三个 Tab，各自独立的搜索与多选过滤（持久化于 WarehouseFilterState），用池化虚拟网格 + 后台贴图预载渲染；携带面板常驻右侧。
/// </summary>
public sealed partial class WarehouseHubScreen : CanvasLayer
{
    /// <summary>How the hub was opened — decides the footer action and whether it launches a run or configures a
    /// client's carry inside an already-joined lobby. 打开仓库的方式：决定底部按钮动作与启动/配置语义。</summary>
    public enum HubMode
    {
        /// <summary>Main-menu singleplayer launch. 主菜单单机开跑。</summary>
        Singleplayer,

        /// <summary>Main-menu multiplayer-host launch. 主菜单联机主机开跑。</summary>
        MultiplayerHost,

        /// <summary>Forced modal shown to a client who joined an extraction room: confirm stages + re-stages the carry
        /// into the lobby, back leaves the room. 客机加入搜打撤房间时的强制配置模态：确认暂存/重暂存携带，返回退出房间。</summary>
        MultiplayerClient,
    }

    private const int GoldStep = 50;

    /// <summary>
    /// Soft per-tab variety cap as a dirty-data guard. Virtualization already bounds live nodes, so this only stops a
    /// single tab from ever enumerating an absurd list. 每 Tab 软上限（脏数据护栏）：虚拟化已约束活动节点数，此上限仅防止单页枚举离谱数量。
    /// </summary>
    private const int MaxTileKinds = 2000;

    /// <summary>Art preload requests submitted per frame. 每帧提交的贴图预载请求数。</summary>
    private const int PrewarmPerFrame = 8;

    private enum Tab { Cards, Relics, Potions }

    /// <summary>Top-level hub pages (shown one at a time under the header). 大厅的顶层页面（头部下方同时只显示一个）。</summary>
    private enum HubPage { Warehouse, Shop, Challenge }

    private HubPage _activePage = HubPage.Warehouse;
    private Button _tabWarehouse = null!;
    private Button _tabShop = null!;
    private Button _tabChallenge = null!;
    private Control _warehousePage = null!;
    private Control _shopPage = null!;
    private Control _challengePage = null!;

    /// <summary>Challenges selected for the next launch (hub-global session draft, like <c>_carry</c>). Multi-select;
    /// bumped into <c>ExtractionRunContext.PendingChallenges</c> at StartRun. 下次开跑选定的挑战（大厅全局会话草稿，同 _carry）。
    /// 可多选；开跑时写入 PendingChallenges。</summary>
    private readonly List<string> _pendingChallenges = new();

    private Label _challengeSummaryLabel = null!;
    private Label _challengeHintLabel = null!;
    private VBoxContainer _dailyChallengeList = null!;
    private VBoxContainer _permanentChallengeList = null!;

    private enum FilterKind
    {
        CardPools,
        CardRarities,
        CardTypes,
        CardCosts,
        CardSources,
        RelicPools,
        RelicRarities,
        RelicSources,
        PotionPools,
        PotionRarities,
        PotionSources,
    }

    private readonly NSubmenuStack _stack;
    private readonly Control? _loadingOverlay;
    private readonly HubMode _mode;
    private readonly StartRunLobby? _lobby;
    private readonly WarehouseData _warehouse;
    private readonly CarryConfig _carry;
    private readonly bool _showDurability;

    /// <summary>Whether copies of one card/relic at different durability render as separate tiles (durability ON + the
    /// SplitDurabilityGroups setting). Drives the warehouse/carry grouping, the preview keys and click-to-add/remove.
    /// 同种牌/遗物是否按耐久独立显示（耐久开启且设置开启）：驱动仓库/携带分组、预览键与点击带/取。 </summary>
    private readonly bool _splitByDurability;
    private int _carryGold;

    /// <summary>The hub currently open in the scene tree (null when closed) — lets the console command refresh it after
    /// mutating the stores underneath. 当前打开中的仓库大厅（关闭时为 null）——供控制台指令在底层改动仓库后刷新界面。</summary>
    public static WarehouseHubScreen? Current { get; private set; }

    private Tab _activeTab = Tab.Cards;

    // Per-tab search queries (persisted). 各 Tab 搜索词（持久化）。
    private readonly string[] _tabQueries = new string[3];
    private string _query = "";

    // ----- Controls -----
    private LineEdit _searchEdit = null!;
    private Button _clearButton = null!;
    private Button _tabCards = null!;
    private Button _tabRelics = null!;
    private Button _tabPotions = null!;

    private readonly VBoxContainer[] _tabContent = new VBoxContainer[3];
    private readonly ScrollContainer[] _scrolls = new ScrollContainer[3];
    private readonly VirtualizedItemGrid[] _grids = new VirtualizedItemGrid[3];
    private readonly Label[] _limitHints = new Label[3];
    private readonly Label[] _noMatchLabels = new Label[3];
    private readonly Label[] _emptyLabels = new Label[3];
    private readonly Dictionary<FilterKind, FilterDropdown> _filters = new();

    // ----- Carry panel -----
    private HFlowContainer _carryCardList = null!;
    private HFlowContainer _carryRelicList = null!;
    private HFlowContainer _carryPotionList = null!;
    private Label _goldChipLabel = null!;
    private PanelContainer _capacityChip = null!;
    private Label _capacityChipLabel = null!;
    private Label _carryDeckLabel = null!;
    private Label _carryRelicsLabel = null!;
    private Label _carryPotionsLabel = null!;
    private LineEdit _goldInput = null!;
    private LineEdit _seedInput = null!;
    private Button _startButton = null!;
    private Label _startHintLabel = null!;
    private Button _generateButton = null!;

    public WarehouseHubScreen(NSubmenuStack stack, Control? loadingOverlay, HubMode mode, StartRunLobby? lobby = null)
    {
        _stack = stack;
        _loadingOverlay = loadingOverlay;
        _mode = mode;
        _lobby = lobby;
        Layer = 100;

        // Seed on first open (idempotent), run the one-shot legacy normalizations (base-state + identity-card repair +
        // durability backfill), ensure the no-durability copy exists while durability is OFF, then load the live
        // warehouse and a detached copy of the pending carry — only written back on confirm/start, so closing never
        // leaks edits. 首次种子、一次性旧档归一（基础态 + 身份牌修复 + 耐久回填）、耐久关闭时确保无耐久副本存在，然后加载
        // 实时仓库与待发携带的独立副本（仅在确认/开跑时写回）。
        WarehouseStore.EnsureNoDurabilityCopy();
        WarehouseStore.EnsureSeeded();
        WarehouseStore.EnsureNormalized();
        WarehouseStore.EnsureIdentityRepaired();
        WarehouseStore.EnsureDurabilityInitialized();
        _warehouse = WarehouseStore.Current;
        _carry = PendingCarryStore.Snapshot();
        _carryGold = _carry.Gold;
        _showDurability = WarehouseStore.IsDurabilityEnabled;
        _splitByDurability = _showDurability && ExtractionSettingsPage.Current.SplitDurabilityGroups;

        // A pending carry saved before the base-only change may still hold upgraded/enchanted items. Normalize it in
        // place so carried items always match the (base-only) warehouse exactly — otherwise a stale +1 carry would
        // consume a base copy while injecting the upgraded one (free upgrade). Identity cards (MadScience) keep their
        // saved props here, matching the warehouse's own normalization. Durability ≤0 (pre-durability sentinel) is
        // backfilled to full so the carry decrements from full at extraction.
        // 旧档遗留的待发携带可能仍带升级/附魔；原地归一到与仓库一致的基础态（身份牌保留其 Props），否则旧 +1 携带会消耗基础卡却
        // 注入升级卡（白嫖升级）。耐久 ≤0（无耐久旧档哨兵）回填满耐久，保证撤离时从满耐久递减。
        for (int i = 0; i < _carry.Cards.Count; i++)
        {
            WarehouseCard wc = _carry.Cards[i];
            _carry.Cards[i] = new WarehouseCard { Card = WarehouseStore.NormalizeCard(wc.Card), Durability = wc.Durability };
        }

        for (int i = 0; i < _carry.Relics.Count; i++)
        {
            WarehouseRelic wr = _carry.Relics[i];
            _carry.Relics[i] = new WarehouseRelic { Relic = WarehouseStore.NormalizeRelic(wr.Relic), Durability = wr.Durability };
        }

        for (int i = 0; i < _carry.Potions.Count; i++)
        {
            _carry.Potions[i] = WarehouseStore.NormalizePotion(_carry.Potions[i]);
        }

        WarehouseStore.BackfillCarryDurability(_carry);

        // Natural-node clamp: a pending carry saved under an older (larger) limit — or re-sized after a settings change
        // — is cut to the current budget on open, before anything renders. Heaviest-first (the carry clamp rule); the
        // toast tells the player what was dropped. Settings edits never touch the draft; this open is the re-sync point.
        // 自然节点钳制：旧版（更大）限制下保存的待发携带、或设置调整后超限的携带，打开时先按当前预算收敛再渲染（先丢最重）；
        // 提示告知玩家被挤掉的数量。设置页改配置不碰草稿，本处打开即重同步点。
        int dropped = CarryCapacity.ClampToBudget(_carry, CarryBudget.FromSettings());
        if (dropped > 0)
        {
            RitsuToastService.ShowInfo(ExtractionLocalization.CapacityClampedText(dropped));
        }

        // Defensive: a hand-edited/corrupt save could deserialize Filters as null.
        _warehouse.Filters ??= new WarehouseFilterState();
        _tabQueries[0] = _warehouse.Filters.QueryCards ?? "";
        _tabQueries[1] = _warehouse.Filters.QueryRelics ?? "";
        _tabQueries[2] = _warehouse.Filters.QueryPotions ?? "";
        _query = _tabQueries[0];
    }

    public override void _Ready()
    {
        Current = this;
        BuildUi();
        Refresh();
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
        // ESC mirrors 「返回」. The raw key must be consumed in the _input phase — otherwise it reaches the unhandled
        // phase, where NHotkeyManager fires the vanilla main-menu back button's ui_cancel binding underneath the hub.
        // ESC 与「返回」等价：必须在 _input 阶段吞掉原始按键，否则其进入未处理阶段，NHotkeyManager 会在仓库大厅底下触发
        // 原版主菜单返回按钮的 ui_cancel 绑定。
        if (inputEvent is InputEventKey { Pressed: true, Keycode: Key.Escape } key && !key.IsEcho())
        {
            OnBack();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        if (WarehouseCache.Tick(PrewarmPerFrame))
        {
            RefreshVisibleTextures();
        }
    }

    private void BuildUi()
    {
        // Root surface: the hub background, themed for every descendant.
        var root = new Panel { Name = "HubPanel" };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddThemeStyleboxOverride("panel", ExtractionTheme.BackgroundBox());
        root.Theme = ExtractionTheme.Instance;
        AddChild(root);

        // Page gutters: generous negative space.
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

        _warehousePage = BuildWarehousePage();
        rootBox.AddChild(_warehousePage);

        _challengePage = BuildChallengePage();
        rootBox.AddChild(_challengePage);

        _shopPage = BuildShopPage();
        rootBox.AddChild(_shopPage);

        ShowPage(HubPage.Warehouse);
    }

    /// <summary>
    /// The warehouse page: the existing warehouse card + carry card + launch footer, wrapped as one page. 仓库页：
    /// 仓库卡片 + 携带卡片 + 开跑底部，作为一页包裹。</summary>
    private Control BuildWarehousePage()
    {
        var page = new VBoxContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        page.AddThemeConstantOverride("separation", 20);

        var content = new HBoxContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        content.AddThemeConstantOverride("separation", 24);
        content.AddChild(BuildWarehouseCard());
        content.AddChild(BuildCarryCard());
        page.AddChild(content);

        page.AddChild(BuildFooter());
        return page;
    }

    private Control BuildShopPage()
    {
        return new ShopScreen(this, _warehouse, _carry);
    }

    /// <summary>Shows one hub page, hiding the others; the tab bar follows. Leaving the shop page persists its sell state.
    /// 显示指定页面并隐藏其余；顶部标签同步。离开商店页时持久化其出售状态。</summary>
    private void ShowPage(HubPage page)
    {
        if (_activePage == HubPage.Shop && page != HubPage.Shop && _shopPage is ShopScreen leaving)
        {
            leaving.PersistState();
        }

        _activePage = page;
        _warehousePage.Visible = page == HubPage.Warehouse;
        _challengePage.Visible = page == HubPage.Challenge;
        _shopPage.Visible = page == HubPage.Shop;

        if (page == HubPage.Challenge)
        {
            RefreshChallengePage();
        }
        else if (page == HubPage.Shop)
        {
            if (_shopPage is ShopScreen shop)
            {
                shop.Refresh();
            }
        }

        _tabWarehouse.ButtonPressed = page == HubPage.Warehouse;
        _tabShop.ButtonPressed = page == HubPage.Shop;
        _tabChallenge.ButtonPressed = page == HubPage.Challenge;
    }

    private void SwitchPage(HubPage page)
    {
        if (_activePage == page)
        {
            return;
        }

        ShowPage(page);
    }

    // ----- Header: title + gold chip + back -----

    private Control BuildHeader()
    {
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 16);

        // Page switcher, top-left: one framed strip split evenly into 仓库 / 商店 / 挑战. 页面切换（左上角，一个等分为仓库/商店/挑战的长条）。
        var pageSwitcher = new PanelContainer
        {
            CustomMinimumSize = new Vector2(390f, 44f),
            ClipContents = true,
        };
        pageSwitcher.AddThemeStyleboxOverride("panel", ExtractionTheme.PageSwitcherBox());
        var segments = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        segments.AddThemeConstantOverride("separation", 0);

        var group = new ButtonGroup();
        _tabWarehouse = MakePageTabButton(ExtractionLocalization.PageWarehouseText(), group, HubPage.Warehouse);
        _tabShop = MakePageTabButton(ExtractionLocalization.PageShopText(), group, HubPage.Shop);
        _tabChallenge = MakePageTabButton(ExtractionLocalization.PageChallengeText(), group, HubPage.Challenge);
        _tabWarehouse.ButtonPressed = true;
        segments.AddChild(_tabWarehouse);
        segments.AddChild(_tabShop);
        segments.AddChild(_tabChallenge);
        pageSwitcher.AddChild(segments);
        header.AddChild(pageSwitcher);

        // The client modal has no challenge page — the host's challenges apply to the whole party. 客机模态无挑战页
        // （挑战由主机选定，全队共享）。
        if (_mode == HubMode.MultiplayerClient)
        {
            _tabChallenge.Visible = false;
        }

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

        var back = MakeButton(ExtractionLocalization.ButtonBackText(), ExtractionTheme.ButtonSecondary);
        back.CustomMinimumSize = new Vector2(0f, 44f);
        back.Pressed += OnBack;
        header.AddChild(back);

        return header;
    }

    // ----- Warehouse card (left, wider): tabs + search + filters + virtualized grid -----

    private Control BuildWarehouseCard()
    {
        PanelContainer card = MakeCard(stretchRatio: 3f, out VBoxContainer body);

        body.AddChild(BuildTabBar());
        body.AddChild(BuildSearchRow());

        _tabContent[(int)Tab.Cards] = BuildTabContent(Tab.Cards, BuildCardFilterArea(), _grids);
        _tabContent[(int)Tab.Relics] = BuildTabContent(Tab.Relics, BuildRelicFilterArea(), _grids);
        _tabContent[(int)Tab.Potions] = BuildTabContent(Tab.Potions, BuildPotionFilterArea(), _grids);
        foreach (VBoxContainer tab in _tabContent)
        {
            body.AddChild(tab);
        }

        _tabContent[(int)Tab.Cards].Visible = true;
        _tabContent[(int)Tab.Relics].Visible = false;
        _tabContent[(int)Tab.Potions].Visible = false;
        return card;
    }

    private Control BuildTabBar()
    {
        var bar = new HBoxContainer();
        bar.AddThemeConstantOverride("separation", 8);

        var group = new ButtonGroup();
        _tabCards = MakeTabButton(ExtractionLocalization.SectionCardsText(), group, Tab.Cards);
        _tabRelics = MakeTabButton(ExtractionLocalization.SectionRelicsText(), group, Tab.Relics);
        _tabPotions = MakeTabButton(ExtractionLocalization.SectionPotionsText(), group, Tab.Potions);
        _tabCards.ButtonPressed = true;

        bar.AddChild(_tabCards);
        bar.AddChild(_tabRelics);
        bar.AddChild(_tabPotions);
        return bar;
    }

    private Button MakeTabButton(string text, ButtonGroup group, Tab tab)
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
                SwitchTab(tab);
            }
        };
        return button;
    }

    private Button MakePageTabButton(string text, ButtonGroup group, HubPage page)
    {
        var button = new Button
        {
            Text = text,
            ThemeTypeVariation = ExtractionTheme.ButtonSegment,
            ToggleMode = true,
            ButtonGroup = group,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 1f,
        };
        button.Toggled += on =>
        {
            // Guard against the construction-time ButtonPressed=true firing before the pages exist. 构建期
            // ButtonPressed=true 会在页面创建前触发 Toggled，此守卫忽略之。
            if (on && _warehousePage != null)
            {
                SwitchPage(page);
            }
        };
        return button;
    }

    /// <summary>A single tab's content: filter dropdowns, then the per-tab hints, then the pooled virtual grid.
    /// 单个 Tab 内容：过滤下拉、该 Tab 的提示标签、池化虚拟网格。</summary>
    private VBoxContainer BuildTabContent(Tab tab, Control filterArea, VirtualizedItemGrid[] grids)
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
        _limitHints[(int)tab] = limit;
        _noMatchLabels[(int)tab] = noMatch;
        _emptyLabels[(int)tab] = empty;

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 2f,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        var grid = new VirtualizedItemGrid();
        scroll.AddChild(grid);
        box.AddChild(scroll);
        _scrolls[(int)tab] = scroll;
        grids[(int)tab] = grid;
        return box;
    }

    private Label MakeHintLabel()
    {
        var label = new Label { Visible = false, HorizontalAlignment = HorizontalAlignment.Center };
        label.AddThemeColorOverride("font_color", ExtractionTheme.TextSecondary);
        label.AddThemeFontSizeOverride("font_size", ExtractionTheme.FontSizeSmall);
        return label;
    }

    // ----- Filter dropdowns (multi-select, present-only options) -----

    private Control BuildCardFilterArea()
    {
        // One row, content-width buttons (adaptive to the five card filters), left-aligned. 一行五个紧凑按钮（自适应过滤项）。
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        _filters[FilterKind.CardPools] = MakeFilterDropdown(ExtractionLocalization.FilterPoolText());
        _filters[FilterKind.CardRarities] = MakeFilterDropdown(ExtractionLocalization.FilterRarityText());
        _filters[FilterKind.CardTypes] = MakeFilterDropdown(ExtractionLocalization.FilterTypeText());
        _filters[FilterKind.CardCosts] = MakeFilterDropdown(ExtractionLocalization.FilterCostText());
        _filters[FilterKind.CardSources] = MakeFilterDropdown(ExtractionLocalization.FilterSourceText());
        row.AddChild(_filters[FilterKind.CardPools]);
        row.AddChild(_filters[FilterKind.CardRarities]);
        row.AddChild(_filters[FilterKind.CardTypes]);
        row.AddChild(_filters[FilterKind.CardCosts]);
        row.AddChild(_filters[FilterKind.CardSources]);
        return row;
    }

    private Control BuildRelicFilterArea()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        _filters[FilterKind.RelicPools] = MakeFilterDropdown(ExtractionLocalization.FilterPoolText());
        _filters[FilterKind.RelicRarities] = MakeFilterDropdown(ExtractionLocalization.FilterRarityText());
        _filters[FilterKind.RelicSources] = MakeFilterDropdown(ExtractionLocalization.FilterSourceText());
        row.AddChild(_filters[FilterKind.RelicPools]);
        row.AddChild(_filters[FilterKind.RelicRarities]);
        row.AddChild(_filters[FilterKind.RelicSources]);
        return row;
    }

    private Control BuildPotionFilterArea()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        _filters[FilterKind.PotionPools] = MakeFilterDropdown(ExtractionLocalization.FilterPoolText());
        _filters[FilterKind.PotionRarities] = MakeFilterDropdown(ExtractionLocalization.FilterRarityText());
        _filters[FilterKind.PotionSources] = MakeFilterDropdown(ExtractionLocalization.FilterSourceText());
        row.AddChild(_filters[FilterKind.PotionPools]);
        row.AddChild(_filters[FilterKind.PotionRarities]);
        row.AddChild(_filters[FilterKind.PotionSources]);
        return row;
    }

    private FilterDropdown MakeFilterDropdown(string title)
    {
        var dropdown = new FilterDropdown { Title = title };
        dropdown.SelectionChanged += () =>
        {
            SaveFilters();
            ResetActiveScroll();
            Refresh();
        };
        return dropdown;
    }

    // ----- Search 搜索 -----

    private Control BuildSearchRow()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);

        _searchEdit = new LineEdit
        {
            Text = _query,
            PlaceholderText = ExtractionLocalization.SearchPlaceholderText(),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0f, 40f),
        };
        _searchEdit.TextChanged += OnSearchTextChanged;
        row.AddChild(_searchEdit);

        // Explicit clear button (the game's own NSearchBar does the same rather than LineEdit.ClearButtonEnabled,
        // so it stays themed by our palette). Visible only while a query is active.
        _clearButton = MakeButton("×", ExtractionTheme.ButtonSecondary);
        _clearButton.CustomMinimumSize = new Vector2(40f, 40f);
        _clearButton.AddThemeFontSizeOverride("font_size", 18);
        _clearButton.Visible = _query.Length > 0;
        _clearButton.Pressed += ClearSearch;
        row.AddChild(_clearButton);

        return row;
    }

    private void OnSearchTextChanged(string text)
    {
        // Live filtering: every keystroke re-runs the active tab's filter against the cached groups.
        _query = text.Trim().ToLowerInvariant();
        _tabQueries[(int)_activeTab] = _query;
        _clearButton.Visible = _query.Length > 0;
        ResetActiveScroll();
        Refresh();
    }

    private void ClearSearch()
    {
        _searchEdit.Text = "";
        // Programmatic Text= does not reliably fire TextChanged in Godot — the game's own NSearchBar.ClearText
        // manually re-emits QueryChanged for the same reason. Refresh explicitly so the full tab restores.
        _query = "";
        _tabQueries[(int)_activeTab] = "";
        _clearButton.Visible = false;
        ResetActiveScroll();
        Refresh();
    }

    private void SwitchTab(Tab tab)
    {
        if (_activeTab == tab)
        {
            return;
        }

        _tabQueries[(int)_activeTab] = _query;
        _activeTab = tab;
        _query = _tabQueries[(int)tab];
        _searchEdit.Text = _query; // Programmatic set: does not re-fire TextChanged.
        _clearButton.Visible = _query.Length > 0;
        for (int i = 0; i < 3; i++)
        {
            _tabContent[i].Visible = (Tab)i == tab;
        }

        Refresh();
    }

    private void ResetActiveScroll() => _scrolls[(int)_activeTab].ScrollVertical = 0;

    // ----- Carry card (right, narrower) -----

    private Control BuildCarryCard()
    {
        PanelContainer card = MakeCard(stretchRatio: 2f, out VBoxContainer body);

        // Global capacity chip (ON mode): the shared card+relic budget, visible regardless of the active tab — the
        // carry panel never tab-switches. Hidden in OFF mode, where the per-section count labels carry the limit.
        // 全局容量胶囊（ON 模式）：卡牌+遗物共享预算，携带面板不随标签页切换，常驻显示。OFF 模式下隐藏（每节数量标签已带上限）。
        var capacityChip = new PanelContainer();
        capacityChip.AddThemeStyleboxOverride("panel", ExtractionTheme.ChipBox());
        _capacityChipLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _capacityChipLabel.AddThemeColorOverride("font_color", ExtractionTheme.GoldChipText);
        _capacityChipLabel.AddThemeFontSizeOverride("font_size", ExtractionTheme.FontSizeBody);
        capacityChip.AddChild(_capacityChipLabel);
        _capacityChip = capacityChip;
        body.AddChild(capacityChip);

        // Gear-code row sits above the carried-deck section in every mode (singleplayer / host / client modal).
        // 战备码行放在携带牌组上方，所有模式（单机 / 主机 / 客机模态）都有。
        body.AddChild(BuildCodeRow());

        body.AddChild(MakeSectionHeaderWithDetail(ExtractionLocalization.CarryDeckText(), out _carryDeckLabel));
        _carryCardList = MakeList();
        body.AddChild(Scroll(_carryCardList, stretchRatio: 2f));

        body.AddChild(new HSeparator());

        body.AddChild(MakeSectionHeaderWithDetail(ExtractionLocalization.CarryRelicsText(), out _carryRelicsLabel));
        _carryRelicList = MakeList();
        body.AddChild(Scroll(_carryRelicList, stretchRatio: 2f));

        body.AddChild(new HSeparator());

        body.AddChild(MakeSectionHeaderWithDetail(ExtractionLocalization.CarryPotionsText(), out _carryPotionsLabel));
        _carryPotionList = MakeList();
        body.AddChild(Scroll(_carryPotionList, stretchRatio: 2f));

        body.AddChild(new HSeparator());

        body.AddChild(BuildGoldRow());

        return card;
    }

    // ----- Gear code (战备码) 生成 / 导入 -----

    private Control BuildCodeRow()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);

        _generateButton = MakeButton(ExtractionLocalization.CodeGenerateText(), ExtractionTheme.ButtonSecondary);
        _generateButton.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _generateButton.Pressed += OnGenerateCode;
        row.AddChild(_generateButton);

        var import = MakeButton(ExtractionLocalization.CodeImportText(), ExtractionTheme.ButtonSecondary);
        import.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        import.Pressed += OnImportCode;
        row.AddChild(import);

        return row;
    }

    /// <summary>True when nothing is carried (including the live gold field — the stepper only writes <c>_carry.Gold</c>
    /// on start/confirm). An empty carry has nothing worth encoding. 是否未携带任何物品（含实时金币字段——步进器只在开跑/确认时写
    /// _carry.Gold）。空携带没有值得编码的内容。</summary>
    private bool CarryIsEmpty =>
        _carry.Cards.Count == 0 && _carry.Relics.Count == 0 && _carry.Potions.Count == 0 && _carryGold == 0;

    /// <summary>Encodes the current carry draft (WYSIWYG gold included), copies it to the clipboard, and toasts. Pure
    /// read of the detached draft — nothing is persisted. 把当前携带草稿（含所见即所得的金币）编码为战备码并复制到剪贴板，
    /// 弹提示。只读草稿，不持久化任何东西。</summary>
    private void OnGenerateCode()
    {
        if (CarryIsEmpty)
        {
            return;
        }

        // Encode the live gold working value, not the stale _carry.Gold (only written on start/confirm).
        // 编码用实时金币工作值，而非仅在开跑/确认时才写的 _carry.Gold。
        var encodeCarry = new CarryConfig
        {
            Cards = _carry.Cards,
            Relics = _carry.Relics,
            Potions = _carry.Potions,
            Gold = _carryGold,
        };
        string code = CarryCodec.Encode(encodeCarry, CarryCodeOwner.ResolveOwnerStem);
        DisplayServer.ClipboardSet(code);
        RitsuToastService.ShowInfo(ExtractionLocalization.CodeCopiedText());
        Entry.Logger.Info($"WarehouseHub: generated gear code ({encodeCarry.Cards.Count}c/" +
                          $"{encodeCarry.Relics.Count}r/{encodeCarry.Potions.Count}p/{encodeCarry.Gold}g).");
    }

    /// <summary>Opens the import dialog against the live warehouse; on apply, replaces the carry draft in place.
    /// 打开导入弹窗（对着实时仓库）；应用时原地替换携带草稿。</summary>
    private void OnImportCode()
    {
        var dialog = new CarryCodeImportDialog(_warehouse, ApplyImportedCarry);
        if (NGame.Instance is NGame game)
        {
            game.AddChild(dialog);
        }
        else
        {
            GetTree().Root.AddChild(dialog);
        }
    }

    /// <summary>Replaces the carry draft with the imported (already clamped) config. The import never wrote the pending
    /// store, so this stays consistent with the detached-draft rule: confirming/starting persists it, backing out drops it.
    /// 用导入的（已收敛）配置替换携带草稿。导入从未写 pending store，因此仍符合草稿隔离规则：确认/开跑才落盘，返回则丢弃。</summary>
    private void ApplyImportedCarry(CarryConfig applied)
    {
        _carry.Cards.Clear();
        _carry.Cards.AddRange(applied.Cards);
        _carry.Relics.Clear();
        _carry.Relics.AddRange(applied.Relics);
        _carry.Potions.Clear();
        _carry.Potions.AddRange(applied.Potions);
        _carryGold = applied.Gold;
        _carry.Gold = applied.Gold;
        Refresh();
        Entry.Logger.Info($"WarehouseHub: applied imported carry ({_carry.Cards.Count}c/" +
                          $"{_carry.Relics.Count}r/{_carry.Potions.Count}p/{_carry.Gold}g).");
    }

    // ----- Gold stepper (custom SpinBox replacement, fully themeable) -----

    private Control BuildGoldRow()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);

        var label = MakeLabel(ExtractionLocalization.CarryGoldText());
        label.AddThemeColorOverride("font_color", ExtractionTheme.Text);
        label.AddThemeFontSizeOverride("font_size", ExtractionTheme.FontSizeSmall);
        row.AddChild(label);

        row.AddChild(MakeSpacer());

        var minus = MakeButton("-", ExtractionTheme.ButtonSecondary);
        minus.CustomMinimumSize = new Vector2(44f, 42f);
        minus.AddThemeFontSizeOverride("font_size", 20);
        minus.Pressed += () => ChangeCarryGold(-GoldStep);
        row.AddChild(minus);

        _goldInput = new LineEdit
        {
            Text = _carryGold.ToString(),
            Alignment = HorizontalAlignment.Center,
            CustomMinimumSize = new Vector2(96f, 42f),
            PlaceholderText = "0",
        };
        _goldInput.AddThemeFontSizeOverride("font_size", 18);
        _goldInput.TextChanged += OnGoldTextChanged;
        _goldInput.TextSubmitted += _ => CommitGoldInput();
        _goldInput.FocusExited += () => CommitGoldInput();
        row.AddChild(_goldInput);

        var plus = MakeButton("+", ExtractionTheme.ButtonSecondary);
        plus.CustomMinimumSize = new Vector2(44f, 42f);
        plus.AddThemeFontSizeOverride("font_size", 20);
        plus.Pressed += () => ChangeCarryGold(GoldStep);
        row.AddChild(plus);

        return row;
    }

    private void OnGoldTextChanged(string text)
    {
        if (text.Any(c => !char.IsDigit(c)))
        {
            string filtered = new string(text.Where(char.IsDigit).ToArray());
            _goldInput.Text = filtered;
            _goldInput.CaretColumn = filtered.Length;
        }
    }

    private void CommitGoldInput()
    {
        string text = _goldInput.Text.Trim();
        int value = _carryGold;
        if (text.Length > 0 && int.TryParse(text, out int parsed) && parsed >= 0)
        {
            value = parsed;
        }

        int max = Math.Min(_warehouse.Gold, WarehouseStore.MaxGold);
        _carryGold = Math.Clamp(value, 0, max);
        _goldInput.Text = _carryGold.ToString();
    }

    // ----- Run seed 种子 -----

    /// <summary>The run-seed input shown to the left of the start button: a LineEdit (live-canonicalized: uppercase,
    /// O→0, I→1) + a clear button. Blank = random, matching the base game's custom-run seed field. The seed is a
    /// session-only, host-owned run parameter — read into <c>ExtractionRunContext.PendingSeed</c> at <c>StartRun</c>,
    /// never persisted with the carry. 开跑按钮左侧的跑局种子输入：LineEdit（实时规范化：大写、O→0、I→1）+ 清空按钮。
    /// 留空=随机，与基础游戏 Custom 界面种子框一致。种子为仅本会话、主机所有的跑局参数——开跑时读入 PendingSeed，不随携带持久化。</summary>
    private Control BuildSeedRow()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);

        var label = MakeLabel(ExtractionLocalization.SeedLabelText());
        label.AddThemeColorOverride("font_color", ExtractionTheme.Text);
        label.AddThemeFontSizeOverride("font_size", ExtractionTheme.FontSizeSmall);
        row.AddChild(label);

        _seedInput = new LineEdit
        {
            Alignment = HorizontalAlignment.Center,
            CustomMinimumSize = new Vector2(200f, 42f),
            PlaceholderText = ExtractionLocalization.SeedPlaceholderText(),
        };
        _seedInput.AddThemeFontSizeOverride("font_size", 18);
        _seedInput.TextChanged += OnSeedTextChanged;
        _seedInput.TextSubmitted += _ => CommitSeedInput();
        _seedInput.FocusExited += () => CommitSeedInput();
        row.AddChild(_seedInput);

        var clear = MakeButton("×", ExtractionTheme.ButtonSecondary);
        clear.CustomMinimumSize = new Vector2(42f, 42f);
        clear.AddThemeFontSizeOverride("font_size", 20);
        clear.Pressed += () => _seedInput.Text = string.Empty;
        row.AddChild(clear);

        return row;
    }

    /// <summary>Live-canonicalizes the seed as the player types so what is shown equals what is used. The base game's
    /// <c>CanonicalizeSeed</c> is idempotent (uppercase, O→0, I→1, trim), so pre-canonicalizing changes nothing.
    /// 输入时实时规范化种子，所见即所用——基础游戏 CanonicalizeSeed 幂等，预先规范化不影响最终种子。</summary>
    private void OnSeedTextChanged(string text)
    {
        string canonical = CanonicalizeSeedText(text);
        if (canonical != text)
        {
            _seedInput.Text = canonical;
            _seedInput.CaretColumn = canonical.Length;
        }
    }

    /// <summary>Trims + canonicalizes on commit (Enter / blur). 提交（回车/失焦）时去首尾空白并规范化。</summary>
    private void CommitSeedInput()
    {
        _seedInput.Text = CanonicalizeSeedText(_seedInput.Text.Trim());
    }

    private static string CanonicalizeSeedText(string seed) =>
        seed.ToUpperInvariant().Replace('O', '0').Replace('I', '1');

    // ----- Footer: primary start action -----

    private Control BuildFooter()
    {
        var footer = new VBoxContainer();
        footer.AddThemeConstantOverride("separation", 6);

        // The action row is a full-width surface: the primary button sits at the exact horizontal center (a full-rect
        // CenterContainer) while the run-seed input pins to the left edge — a centered primary action with an auxiliary
        // left-aligned field, instead of the whole pair floating as a centered block. The client modal hides the seed
        // row, leaving only the centered confirm button.
        // 操作行是整宽面板：主按钮精确水平居中（整幅 CenterContainer），种子输入框贴靠左缘——主操作居中、辅助输入靠左，
        // 而非整组作为居中块悬浮。客机模态隐藏种子行，仅剩居中的确认按钮。
        var row = new Control { CustomMinimumSize = new Vector2(0f, 54f) };

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        center.MouseFilter = Control.MouseFilterEnum.Ignore;
        row.AddChild(center);

        // Client modal: the primary action confirms the carry into the lobby instead of launching a run.
        // 客户端模态：主按钮为「确认携带」而非发起跑局。
        bool isClient = _mode == HubMode.MultiplayerClient;
        var start = MakeButton(isClient ? ExtractionLocalization.ButtonConfirmText() : ExtractionLocalization.ButtonStartText(),
            ExtractionTheme.ButtonPrimary);
        start.CustomMinimumSize = new Vector2(320f, 54f);
        if (isClient)
        {
            start.Pressed += ConfirmCarryForClient;
        }
        else
        {
            start.Pressed += StartRun;
        }

        _startButton = start;
        center.AddChild(start);

        // Run seed input pinned to the footer's left edge — host/singleplayer only: the host owns the run seed,
        // clients join under it. ShrinkBegin keeps the row at natural width, left-aligned in the Ignore host.
        // 跑局种子输入贴靠底部左缘——仅主机/单机：种子归主机，客户端沿用主机种子。ShrinkBegin 令其保持自然宽度、左对齐。
        var seedHost = new VBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        seedHost.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        row.AddChild(seedHost);

        Control seedRow = BuildSeedRow();
        seedRow.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        seedRow.Visible = !isClient;
        seedHost.AddChild(seedRow);

        footer.AddChild(row);

        // Selected-challenge summary (the client modal has no challenge page, so it stays blank there). 已选挑战摘要
        // （客机模态无挑战页，恒为空）。
        _challengeSummaryLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Visible = false,
        };
        _challengeSummaryLabel.AddThemeColorOverride("font_color", ExtractionTheme.TextSecondary);
        _challengeSummaryLabel.AddThemeFontSizeOverride("font_size", ExtractionTheme.FontSizeSmall);
        footer.AddChild(_challengeSummaryLabel);

        // Hint shown while the carry is empty: the deck-clearing modifier would otherwise start a dead 0-card run.
        _startHintLabel = new Label
        {
            Text = ExtractionLocalization.NeedCardHintText(),
            HorizontalAlignment = HorizontalAlignment.Center,
            Visible = false,
        };
        _startHintLabel.AddThemeColorOverride("font_color", ExtractionTheme.Danger);
        _startHintLabel.AddThemeFontSizeOverride("font_size", ExtractionTheme.FontSizeSmall);
        footer.AddChild(_startHintLabel);

        return footer;
    }

    private void ChangeCarryGold(int delta)
    {
        int max = Math.Min(_warehouse.Gold, WarehouseStore.MaxGold);
        _carryGold = Math.Clamp(_carryGold + delta, 0, max);
        _goldInput.Text = _carryGold.ToString();
    }

    // ----- Refresh 重建 -----

    /// <summary>
    /// Rebuilds the carry panel, the active tab's filter options + grid, and the hints from the current state.
    /// The warehouse itself is immutable during the hub session (only the carry mutates), so the grouped metadata and
    /// art preload live in the module-level <see cref="WarehouseCache"/> and are never rebuilt here.
    /// 从当前状态重建携带面板、活动 Tab 的过滤选项与网格、提示。仓库在大厅会话内不可变（只有携带在变），分组元数据与贴图
    /// 预载都在模块级 WarehouseCache 中，这里不重建。
    /// </summary>
    private void Refresh()
    {
        WarehouseCache.Ensure(_warehouse, _splitByDurability);

        // A challenge selection that invalidates the draft (empty-carry, basic/common-only) clamps it live so the
        // tiles, carry panel and start gate always agree. 选中挑战后草稿可能失效（空携带/仅基础+普通），实时钳制保证瓦片、
        // 携带面板与开跑门一致。
        ClampDraftToChallenges();

        int availableGold = Math.Max(0, _warehouse.Gold - _carryGold);
        _goldChipLabel.Text = ExtractionLocalization.GoldWarehouseText(availableGold);
        if (!_goldInput.HasFocus())
        {
            _goldInput.Text = _carryGold.ToString();
        }

        CarryBudget budget = CarryBudget.FromSettings();
        if (budget.UsesCapacity)
        {
            // Capacity mode: one shared budget, no per-kind cap — the section labels show counts + per-kind slot usage
            // (which always sum to the chip's used count), the chip shows the pool. Sections never turn red; the
            // pool-full red stays on the chip.
            // 容量模式：一个共享预算、无每类上限——节标签显示数量 + 该节占格（两者之和恒等于胶囊占用），胶囊显池占用。
            // 小节永不红，满池的红色只留在胶囊上。
            int used = CarryCapacity.UsedCapacity(_carry);
            _capacityChip.Visible = true;
            _capacityChipLabel.Text = ExtractionLocalization.CapacityBarText(used, budget.Capacity);
            _capacityChipLabel.AddThemeColorOverride("font_color",
                used >= budget.Capacity ? ExtractionTheme.Danger : ExtractionTheme.GoldChipText);
            _carryDeckLabel.Text = ExtractionLocalization.CarryDeckCountText(_carry.Cards.Count, CarryCapacity.CardCapacity(_carry));
            _carryRelicsLabel.Text = ExtractionLocalization.CarryRelicsCountText(_carry.Relics.Count, CarryCapacity.RelicCapacity(_carry));
        }
        else
        {
            // OFF mode: the legacy per-kind count caps, shown per section.
            _capacityChip.Visible = false;
            _carryDeckLabel.Text = ExtractionLocalization.LimitCardsText(_carry.Cards.Count, budget.MaxCards);
            _carryRelicsLabel.Text = ExtractionLocalization.LimitRelicsText(_carry.Relics.Count, budget.MaxRelics);
        }

        _carryPotionsLabel.Text = ExtractionLocalization.LimitPotionsText(_carry.Potions.Count, 3);

        // Empty carry cannot start: ClearsPlayerDeck would give a dead 0-card deck — unless carrying any card is
        // impossible (no cards to carry, count cap ≤ 0, or the capacity pool too small for the lightest card), in which
        // case the run's starter-deck fallback keeps it playable. 空携带不能开跑（ClearsPlayerDeck 会给一个 0 牌死局）——除非
        // 已不可能带任何卡（无卡可带、数量上限 ≤ 0、或容量池装不下最轻的卡），此时初始牌组兜底仍可玩。
        bool canStart = CanProceed();
        _startButton.Disabled = !canStart;
        _startHintLabel.Visible = !canStart;

        // Selected-challenge summary: host/singleplayer shows the session draft; the client modal shows the HOST's
        // challenges read off the lobby modifier (zero extra sync). 已选挑战摘要：单机/主机显示会话草稿；客机模态显示主机挑战
        // （从大厅 modifier 读取，零额外同步）。
        if (_mode == HubMode.MultiplayerClient)
        {
            string hostChallenges = _lobby?.Modifiers.OfType<ExtractionModifier>().FirstOrDefault()?.ActiveChallengeIds
                .Select(ExtractionLocalization.ChallengeTitle)
                is { } titles && titles.Any()
                ? string.Join(" / ", titles)
                : "";
            _challengeSummaryLabel.Text = hostChallenges.Length > 0
                ? ExtractionLocalization.ChallengeSummaryText(hostChallenges)
                : "";
        }
        else
        {
            _challengeSummaryLabel.Text = ChallengeSummaryText();
        }

        _challengeSummaryLabel.Visible = _challengeSummaryLabel.Text.Length > 0;

        // An empty carry has nothing worth sharing as a gear code. 空携带没有值得分享的战备码。
        _generateButton.Disabled = CarryIsEmpty;

        // Carried counts by item key (id-only, or id@durability when split), used to compute the available warehouse
        // counts. Both sides of the preview use the same split flag so the keys line up.
        // 携带计数按键（拆分时 id@耐久）——两侧预览用同一拆分标志，键才对得上。
        var carriedCards = new Dictionary<string, int>();
        foreach (ExtractionItemTiles.CardGroup g in ExtractionItemTiles.GroupCards(_carry.Cards, loadArt: false, _splitByDurability))
        {
            carriedCards[ExtractionItemTiles.CardKey(g, _splitByDurability)] = g.Count;
        }

        var carriedRelics = new Dictionary<string, int>();
        foreach (ExtractionItemTiles.RelicGroup g in ExtractionItemTiles.GroupRelics(_carry.Relics, loadArt: false, _splitByDurability))
        {
            carriedRelics[ExtractionItemTiles.RelicKey(g, _splitByDurability)] = g.Count;
        }

        var carriedPotions = new Dictionary<string, int>();
        foreach (ExtractionItemTiles.PotionGroup g in ExtractionItemTiles.GroupPotions(_carry.Potions, loadArt: false))
        {
            carriedPotions[ExtractionItemTiles.PotionKey(g)] = g.Count;
        }

        switch (_activeTab)
        {
            case Tab.Cards:
                UpdateCardsTab(carriedCards);
                break;
            case Tab.Relics:
                UpdateRelicsTab(carriedRelics);
                break;
            case Tab.Potions:
                UpdatePotionsTab(carriedPotions);
                break;
        }

        UpdateCarryTiles();
        _clearButton.Visible = _query.Length > 0;
    }

    // ----- External mutation refresh (console command) 控制台外部改动后的刷新 -----

    /// <summary>Rebuilds the hub UI after the console grew the warehouse underneath (add). The carry is untouched.
    /// 控制台增仓后重建界面（add）；携带不变。</summary>
    public void RefreshForExternalMutation() => Refresh();

    /// <summary>
    /// Rebuilds the hub UI and re-syncs the carry-gold working value to the revalidated carry config. Used after
    /// console reset/remove — ops that can shrink the warehouse the carry draws from, so the local gold field must
    /// follow the clamp. 重置/删仓后重建界面，并把携带金币工作值同步到重校验后的携带配置。
    /// </summary>
    public void RefreshForExternalMutationAfterShrink()
    {
        _carryGold = Math.Min(_carry.Gold, _warehouse.Gold);
        Refresh();
    }

    private void UpdateCarryTiles()
    {
        ClearChildren(_carryCardList);
        ClearChildren(_carryRelicList);
        ClearChildren(_carryPotionList);

        if (_carry.Cards.Count == 0)
        {
            AddEmptyState(_carryCardList, ExtractionLocalization.EmptyCarryText());
        }
        else
        {
            foreach (ExtractionItemTiles.CardGroup g in ExtractionItemTiles.GroupCards(_carry.Cards, loadArt: false, _splitByDurability))
            {
                _carryCardList.AddChild(ExtractionItemTiles.MakeItemTile(g.Name, g.Pool, g.Count,
                    WarehouseCache.Resolve(g.PortraitPath), ExtractionItemTiles.ItemTileAction.Remove,
                    () => RemoveFromCarryCards(g.Rep.Id, g.Durability),
                    g.Rep.Id, _showDurability ? g.Durability : null));
            }
        }

        if (_carry.Relics.Count == 0)
        {
            AddEmptyState(_carryRelicList, ExtractionLocalization.EmptyCarryText());
        }
        else
        {
            foreach (ExtractionItemTiles.RelicGroup g in ExtractionItemTiles.GroupRelics(_carry.Relics, loadArt: false, _splitByDurability))
            {
                _carryRelicList.AddChild(ExtractionItemTiles.MakeItemTile(g.Name, g.Pool, g.Count,
                    WarehouseCache.Resolve(g.IconPath), ExtractionItemTiles.ItemTileAction.Remove,
                    () => RemoveFromCarryRelics(g.Rep.Id, g.Durability),
                    g.Rep.Id, _showDurability ? g.Durability : null));
            }
        }

        if (_carry.Potions.Count == 0)
        {
            AddEmptyState(_carryPotionList, ExtractionLocalization.EmptyCarryText());
        }
        else
        {
            foreach (ExtractionItemTiles.PotionGroup g in ExtractionItemTiles.GroupPotions(_carry.Potions, loadArt: false))
            {
                _carryPotionList.AddChild(ExtractionItemTiles.MakeItemTile(g.Name, g.Pool, g.Count,
                    WarehouseCache.Resolve(g.ImagePath), ExtractionItemTiles.ItemTileAction.Remove,
                    () => RemoveFromCarryPotions(g.Rep.Id),
                    g.Rep.Id));
            }
        }
    }

    /// <summary>Repopulates the visible tiles after a preload tick delivered art. 预载完成后重新填充可见瓦片。</summary>
    private void RefreshVisibleTextures()
    {
        _grids[0].RefreshTextures();
        _grids[1].RefreshTextures();
        _grids[2].RefreshTextures();
        UpdateCarryTiles();
    }

    // ----- Per-tab filtering 各 Tab 过滤 -----

    private void UpdateCardsTab(Dictionary<string, int> carried)
    {
        IReadOnlyList<ExtractionItemTiles.CardGroup> varieties = WarehouseCache.Cards;

        // Present-only filter options in canonical order (the sorted groups already walk pools in canonical order).
        SetFilterOptions(_filters[FilterKind.CardPools],
            varieties.Select(g => g.PoolSlug).Where(s => s.Length > 0).Distinct().ToList(),
            _warehouse.Filters.CardPools, s => ExtractionLocalization.PoolNameText(s));
        SetFilterOptions(_filters[FilterKind.CardRarities],
            varieties.Select(g => g.Rarity).Distinct().OrderBy(r => (int)r).Select(r => r.ToString()).ToList(),
            _warehouse.Filters.CardRarities, ExtractionLocalization.FilterRarityLabel);
        SetFilterOptions(_filters[FilterKind.CardTypes],
            varieties.Select(g => g.Type).Distinct().OrderBy(t => (int)t).Select(t => t.ToString()).ToList(),
            _warehouse.Filters.CardTypes, ExtractionLocalization.FilterTypeLabel);
        SetFilterOptions(_filters[FilterKind.CardCosts],
            varieties.Select(g => g.Cost).Distinct().OrderBy(c => (int)c).Select(c => c.ToString()).ToList(),
            _warehouse.Filters.CardCosts, ExtractionLocalization.FilterCostLabel);
        SetFilterOptions(_filters[FilterKind.CardSources],
            OrderSourceKeys(varieties.Select(g => g.Source.SourceKey)),
            _warehouse.Filters.CardSources, ExtractionLocalization.FilterSourceLabel);

        List<VirtualizedItemGrid.RenderData> rows = BuildCardRows(varieties, carried);
        _grids[(int)Tab.Cards].SetItems(rows);
        UpdateTabHints(Tab.Cards, rows.Count, varieties.Count == 0, IsFilterActive(FilterKind.CardPools)
            || IsFilterActive(FilterKind.CardRarities) || IsFilterActive(FilterKind.CardTypes)
            || IsFilterActive(FilterKind.CardCosts) || IsFilterActive(FilterKind.CardSources));
    }

    private void UpdateRelicsTab(Dictionary<string, int> carried)
    {
        IReadOnlyList<ExtractionItemTiles.RelicGroup> varieties = WarehouseCache.Relics;

        SetFilterOptions(_filters[FilterKind.RelicPools],
            varieties.Select(g => g.PoolSlug).Where(s => s.Length > 0).Distinct().ToList(),
            _warehouse.Filters.RelicPools, s => ExtractionLocalization.PoolNameText(s));
        SetFilterOptions(_filters[FilterKind.RelicRarities],
            varieties.Select(g => g.Rarity).Distinct().OrderBy(r => (int)r).Select(r => r.ToString()).ToList(),
            _warehouse.Filters.RelicRarities, ExtractionLocalization.FilterRarityLabel);
        SetFilterOptions(_filters[FilterKind.RelicSources],
            OrderSourceKeys(varieties.Select(g => g.Source.SourceKey)),
            _warehouse.Filters.RelicSources, ExtractionLocalization.FilterSourceLabel);

        List<VirtualizedItemGrid.RenderData> rows = BuildRelicRows(varieties, carried);
        _grids[(int)Tab.Relics].SetItems(rows);
        UpdateTabHints(Tab.Relics, rows.Count, varieties.Count == 0,
            IsFilterActive(FilterKind.RelicPools) || IsFilterActive(FilterKind.RelicRarities)
            || IsFilterActive(FilterKind.RelicSources));
    }

    private void UpdatePotionsTab(Dictionary<string, int> carried)
    {
        IReadOnlyList<ExtractionItemTiles.PotionGroup> varieties = WarehouseCache.Potions;

        SetFilterOptions(_filters[FilterKind.PotionPools],
            varieties.Select(g => g.PoolSlug).Where(s => s.Length > 0).Distinct().ToList(),
            _warehouse.Filters.PotionPools, s => ExtractionLocalization.PoolNameText(s));
        SetFilterOptions(_filters[FilterKind.PotionRarities],
            varieties.Select(g => g.Rarity).Distinct().OrderBy(r => (int)r).Select(r => r.ToString()).ToList(),
            _warehouse.Filters.PotionRarities, ExtractionLocalization.FilterRarityLabel);
        SetFilterOptions(_filters[FilterKind.PotionSources],
            OrderSourceKeys(varieties.Select(g => g.Source.SourceKey)),
            _warehouse.Filters.PotionSources, ExtractionLocalization.FilterSourceLabel);

        List<VirtualizedItemGrid.RenderData> rows = BuildPotionRows(varieties, carried);
        _grids[(int)Tab.Potions].SetItems(rows);
        UpdateTabHints(Tab.Potions, rows.Count, varieties.Count == 0,
            IsFilterActive(FilterKind.PotionPools) || IsFilterActive(FilterKind.PotionRarities)
            || IsFilterActive(FilterKind.PotionSources));
    }

    private List<VirtualizedItemGrid.RenderData> BuildCardRows(
        IReadOnlyList<ExtractionItemTiles.CardGroup> varieties, Dictionary<string, int> carried)
    {
        var rows = new List<VirtualizedItemGrid.RenderData>();
        List<string> selPools = _warehouse.Filters.CardPools;
        List<string> selRarities = _warehouse.Filters.CardRarities;
        List<string> selTypes = _warehouse.Filters.CardTypes;
        List<string> selCosts = _warehouse.Filters.CardCosts;
        List<string> selSources = _warehouse.Filters.CardSources;
        bool poolOn = selPools.Count > 0;
        bool rarityOn = selRarities.Count > 0;
        bool typeOn = selTypes.Count > 0;
        bool costOn = selCosts.Count > 0;
        bool sourceOn = selSources.Count > 0;
        string query = _query;
        CarryBudget budget = CarryBudget.FromSettings();

        int filtered = 0;
        foreach (ExtractionItemTiles.CardGroup g in varieties)
        {
            int available = g.Count - carried.GetValueOrDefault(ExtractionItemTiles.CardKey(g, _splitByDurability));
            if (available <= 0)
            {
                continue; // Every copy is already staged to carry.
            }

            if (query.Length > 0 && !g.Haystack.Contains(query))
            {
                continue;
            }

            if ((poolOn && !selPools.Contains(g.PoolSlug))
                || (rarityOn && !selRarities.Contains(g.Rarity.ToString()))
                || (typeOn && !selTypes.Contains(g.Type.ToString()))
                || (costOn && !selCosts.Contains(g.Cost.ToString()))
                || (sourceOn && !selSources.Contains(g.Source.SourceKey)))
            {
                continue;
            }

            filtered++;
            if (rows.Count >= MaxTileKinds)
            {
                continue; // Capped; counted toward the hint but not rendered.
            }

            rows.Add(new VirtualizedItemGrid.RenderData(g.Name, g.Pool, available,
                () => WarehouseCache.Resolve(g.PortraitPath), ExtractionItemTiles.ItemTileAction.Add,
                () =>
                {
                    if (g.Rep.Id is ModelId id)
                    {
                        AddToCarryCards(id, g.Durability);
                    }
                },
                g.Rep.Id, _showDurability ? g.Durability : null,
                Disabled: !CanCarryCardTile(g) ||
                          (g.Rep.Id is ModelId addId && budget.MoreAllowed(_carry, CarryCodec.ItemKind.Card, addId) <= 0)));
        }

        UpdateLimitHint(Tab.Cards, filtered, rows.Count);
        return rows;
    }

    private List<VirtualizedItemGrid.RenderData> BuildRelicRows(
        IReadOnlyList<ExtractionItemTiles.RelicGroup> varieties, Dictionary<string, int> carried)
    {
        var rows = new List<VirtualizedItemGrid.RenderData>();
        List<string> selPools = _warehouse.Filters.RelicPools;
        List<string> selRarities = _warehouse.Filters.RelicRarities;
        List<string> selSources = _warehouse.Filters.RelicSources;
        bool poolOn = selPools.Count > 0;
        bool rarityOn = selRarities.Count > 0;
        bool sourceOn = selSources.Count > 0;
        string query = _query;
        CarryBudget budget = CarryBudget.FromSettings();
        bool emptyCarry = PendingChallengeRuntime.StartsEmpty;

        int filtered = 0;
        foreach (ExtractionItemTiles.RelicGroup g in varieties)
        {
            int available = g.Count - carried.GetValueOrDefault(ExtractionItemTiles.RelicKey(g, _splitByDurability));
            if (available <= 0)
            {
                continue;
            }

            if (query.Length > 0 && !g.Haystack.Contains(query))
            {
                continue;
            }

            if ((poolOn && !selPools.Contains(g.PoolSlug))
                || (rarityOn && !selRarities.Contains(g.Rarity.ToString()))
                || (sourceOn && !selSources.Contains(g.Source.SourceKey)))
            {
                continue;
            }

            filtered++;
            if (rows.Count >= MaxTileKinds)
            {
                continue;
            }

            rows.Add(new VirtualizedItemGrid.RenderData(g.Name, g.Pool, available,
                () => WarehouseCache.Resolve(g.IconPath), ExtractionItemTiles.ItemTileAction.Add,
                () =>
                {
                    if (g.Rep.Id is ModelId id)
                    {
                        AddToCarryRelics(id, g.Durability);
                    }
                },
                g.Rep.Id, _showDurability ? g.Durability : null,
                Disabled: emptyCarry ||
                          (g.Rep.Id is ModelId addId && budget.MoreAllowed(_carry, CarryCodec.ItemKind.Relic, addId) <= 0)));
        }

        UpdateLimitHint(Tab.Relics, filtered, rows.Count);
        return rows;
    }

    private List<VirtualizedItemGrid.RenderData> BuildPotionRows(
        IReadOnlyList<ExtractionItemTiles.PotionGroup> varieties, Dictionary<string, int> carried)
    {
        var rows = new List<VirtualizedItemGrid.RenderData>();
        List<string> selPools = _warehouse.Filters.PotionPools;
        List<string> selRarities = _warehouse.Filters.PotionRarities;
        List<string> selSources = _warehouse.Filters.PotionSources;
        bool poolOn = selPools.Count > 0;
        bool rarityOn = selRarities.Count > 0;
        bool sourceOn = selSources.Count > 0;
        string query = _query;
        bool emptyCarry = PendingChallengeRuntime.StartsEmpty;

        int filtered = 0;
        foreach (ExtractionItemTiles.PotionGroup g in varieties)
        {
            int available = g.Count - carried.GetValueOrDefault(ExtractionItemTiles.PotionKey(g));
            if (available <= 0)
            {
                continue;
            }

            if (query.Length > 0 && !g.Haystack.Contains(query))
            {
                continue;
            }

            if ((poolOn && !selPools.Contains(g.PoolSlug))
                || (rarityOn && !selRarities.Contains(g.Rarity.ToString()))
                || (sourceOn && !selSources.Contains(g.Source.SourceKey)))
            {
                continue;
            }

            filtered++;
            if (rows.Count >= MaxTileKinds)
            {
                continue;
            }

            rows.Add(new VirtualizedItemGrid.RenderData(g.Name, g.Pool, available,
                () => WarehouseCache.Resolve(g.ImagePath), ExtractionItemTiles.ItemTileAction.Add,
                () =>
                {
                    if (g.Rep.Id is ModelId id)
                    {
                        AddToCarryPotions(id);
                    }
                },
                g.Rep.Id,
                Disabled: emptyCarry));
        }

        UpdateLimitHint(Tab.Potions, filtered, rows.Count);
        return rows;
    }

    private void SetFilterOptions(FilterDropdown dropdown, List<string> values, IReadOnlyList<string> persisted,
        Func<string, string> label)
    {
        dropdown.SetOptions(values.Select(v => (v, label(v))));
        dropdown.SetSelected(persisted);
    }

    /// <summary>
    /// Content-source filter options in canonical order: 原版 first, then mods by display name, 未知 last. 内容来源过滤
    /// 选项规范序：原版 → mods（按显示名）→ 未知。
    /// </summary>
    private static List<string> OrderSourceKeys(IEnumerable<string> keys)
    {
        var present = keys.Where(k => k.Length > 0).Distinct().ToList();
        List<string> mods = present
            .Where(k => k != ContentSource.BaseKey && k != ContentSource.UnknownKey)
            .OrderBy(k => ExtractionLocalization.FilterSourceLabel(k), StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new[] { ContentSource.BaseKey }.Where(present.Contains)
            .Concat(mods)
            .Concat(new[] { ContentSource.UnknownKey }.Where(present.Contains))
            .ToList();
    }

    private bool IsFilterActive(FilterKind kind) => _filters[kind].Selected.Count > 0;

    /// <summary>Per-tab hint line when the per-section cap dropped some matching varieties. 每 Tab 封顶提示。</summary>
    private void UpdateLimitHint(Tab tab, int filtered, int rendered)
    {
        bool capped = filtered > rendered;
        _limitHints[(int)tab].Text = ExtractionLocalization.SearchLimitText(MaxTileKinds, filtered);
        _limitHints[(int)tab].Visible = capped;
    }

    /// <summary>
    /// Renders a tab's state lines: "warehouse empty" / "no match for this tab" / blank (all carried), plus the grid.
    /// 渲染单 Tab 状态行：仓库为空 / 当前 Tab 无匹配 / 空白（已全部携带），以及网格。
    /// </summary>
    private void UpdateTabHints(Tab tab, int rendered, bool categoryEmpty, bool anyFilter)
    {
        bool noMatch = !categoryEmpty && rendered == 0 && anyFilter;
        _emptyLabels[(int)tab].Text = ExtractionLocalization.EmptyWarehouseText();
        _emptyLabels[(int)tab].Visible = categoryEmpty;
        _noMatchLabels[(int)tab].Text = ExtractionLocalization.SearchNoMatchText(SectionTitle(tab));
        _noMatchLabels[(int)tab].Visible = noMatch;
        _grids[(int)tab].Visible = rendered > 0;
    }

    private static string SectionTitle(Tab tab) => tab switch
    {
        Tab.Cards => ExtractionLocalization.SectionCardsText(),
        Tab.Relics => ExtractionLocalization.SectionRelicsText(),
        _ => ExtractionLocalization.SectionPotionsText(),
    };

    private static void ClearChildren(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            child.QueueFree();
        }
    }

    private static void AddEmptyState(HFlowContainer list, string text)
    {
        var empty = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0f, 72f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        empty.AddThemeColorOverride("font_color", ExtractionTheme.TextSecondary);
        empty.AddThemeFontSizeOverride("font_size", ExtractionTheme.FontSizeSmall);
        list.AddChild(empty);
    }

    /// <summary>
    /// Carries one warehouse copy of <paramref name="id"/>: with split display the click targets that tile's exact
    /// durability; merged takes the next lowest-durability available copy (worst gear first, Tarkov-style). The draft
    /// always holds its own clones, so the warehouse instance is never mutated by carrying. 携带该 id 的一份仓库副本：拆分显示
    /// 时按点击瓦片的精确耐久取，合并时取下一份最低耐久（先带最破）。草稿持有自己的克隆，携带永不改动仓库实例。
    /// </summary>
    private void AddToCarryCards(ModelId id, int durability)
    {
        // Belt-and-suspenders over the tile's Disabled state: the budget guard (OFF count cap / ON capacity) is the
        // single source of truth, so an enabled tile can never overdraw the shared pool.
        CarryBudget budget = CarryBudget.FromSettings();
        if (budget.MoreAllowed(_carry, CarryCodec.ItemKind.Card, id) <= 0)
        {
            return;
        }

        WarehouseCard? copy = _splitByDurability
            ? _warehouse.Cards
                .Where(c => c.Card.Id == id && c.Durability == durability)
                .Skip(_carry.Cards.Count(c => c.Card.Id == id && c.Durability == durability))
                .FirstOrDefault()
            : _warehouse.Cards
                .Where(c => c.Card.Id == id)
                .OrderBy(c => c.Durability)
                .Skip(_carry.Cards.Count(c => c.Card.Id == id))
                .FirstOrDefault();
        if (copy == null)
        {
            return;
        }

        _carry.Cards.Add(new WarehouseCard { Card = copy.Card, Durability = copy.Durability });
        Refresh();
    }

    private void AddToCarryRelics(ModelId id, int durability)
    {
        CarryBudget budget = CarryBudget.FromSettings();
        if (budget.MoreAllowed(_carry, CarryCodec.ItemKind.Relic, id) <= 0)
        {
            return;
        }

        WarehouseRelic? copy = _splitByDurability
            ? _warehouse.Relics
                .Where(r => r.Relic.Id == id && r.Durability == durability)
                .Skip(_carry.Relics.Count(r => r.Relic.Id == id && r.Durability == durability))
                .FirstOrDefault()
            : _warehouse.Relics
                .Where(r => r.Relic.Id == id)
                .OrderBy(r => r.Durability)
                .Skip(_carry.Relics.Count(r => r.Relic.Id == id))
                .FirstOrDefault();
        if (copy == null)
        {
            return;
        }

        _carry.Relics.Add(new WarehouseRelic { Relic = copy.Relic, Durability = copy.Durability });
        Refresh();
    }

    private void AddToCarryPotions(ModelId id)
    {
        if (_carry.Potions.Count >= 3)
        {
            return;
        }

        SerializablePotion? copy = _warehouse.Potions.FirstOrDefault(p => p.Id == id);
        if (copy == null)
        {
            return;
        }

        _carry.Potions.Add(copy);
        Refresh();
    }

    /// <summary>Drops a carried copy of <paramref name="id"/>: with split display the click removes that tile's exact
    /// durability; merged removes the lowest-durability carried copy. 移除该 id 携带的一份：拆分显示时按点击瓦片的精确耐久取，
    /// 合并时移除最低耐久的一份。</summary>
    private void RemoveFromCarryCards(ModelId? id, int durability)
    {
        if (id == null)
        {
            return;
        }

        WarehouseCard? copy = _splitByDurability
            ? _carry.Cards.FirstOrDefault(c => c.Card.Id == id && c.Durability == durability)
            : _carry.Cards.Where(c => c.Card.Id == id).OrderBy(c => c.Durability).FirstOrDefault();
        if (copy != null)
        {
            _carry.Cards.Remove(copy);
        }

        Refresh();
    }

    private void RemoveFromCarryRelics(ModelId? id, int durability)
    {
        if (id == null)
        {
            return;
        }

        WarehouseRelic? copy = _splitByDurability
            ? _carry.Relics.FirstOrDefault(r => r.Relic.Id == id && r.Durability == durability)
            : _carry.Relics.Where(r => r.Relic.Id == id).OrderBy(r => r.Durability).FirstOrDefault();
        if (copy != null)
        {
            _carry.Relics.Remove(copy);
        }

        Refresh();
    }

    private void RemoveFromCarryPotions(ModelId? id)
    {
        if (id == null)
        {
            return;
        }

        SerializablePotion? copy = _carry.Potions.FirstOrDefault(p => p.Id == id);
        if (copy != null)
        {
            _carry.Potions.Remove(copy);
        }

        Refresh();
    }

    // ----- Persistence 持久化 -----

    /// <summary>Copies the live hub filter/search state into <see cref="WarehouseData.Filters"/> (in-memory; persisted on close).
    /// 把当前界面过滤/搜索状态写入 WarehouseData.Filters（内存；关闭时落盘）。</summary>
    private void SaveFilters()
    {
        _warehouse.Filters.QueryCards = _tabQueries[0];
        _warehouse.Filters.QueryRelics = _tabQueries[1];
        _warehouse.Filters.QueryPotions = _tabQueries[2];
        _warehouse.Filters.CardPools = _filters[FilterKind.CardPools].Selected.ToList();
        _warehouse.Filters.CardRarities = _filters[FilterKind.CardRarities].Selected.ToList();
        _warehouse.Filters.CardTypes = _filters[FilterKind.CardTypes].Selected.ToList();
        _warehouse.Filters.CardCosts = _filters[FilterKind.CardCosts].Selected.ToList();
        _warehouse.Filters.CardSources = _filters[FilterKind.CardSources].Selected.ToList();
        _warehouse.Filters.RelicPools = _filters[FilterKind.RelicPools].Selected.ToList();
        _warehouse.Filters.RelicRarities = _filters[FilterKind.RelicRarities].Selected.ToList();
        _warehouse.Filters.RelicSources = _filters[FilterKind.RelicSources].Selected.ToList();
        _warehouse.Filters.PotionPools = _filters[FilterKind.PotionPools].Selected.ToList();
        _warehouse.Filters.PotionRarities = _filters[FilterKind.PotionRarities].Selected.ToList();
        _warehouse.Filters.PotionSources = _filters[FilterKind.PotionSources].Selected.ToList();
    }

    private void StartRun()
    {
        NormalizePendingChallengeDraft();
        // Defense-in-depth: a stale draft is re-clamped to the current budget first (should be a no-op — the open-time
        // clamp + the guarded adds already kept it in bounds), then never launch a 0-card run.
        int dropped = CarryCapacity.ClampToBudget(_carry, CarryBudget.FromSettings());
        if (dropped > 0)
        {
            RitsuToastService.ShowInfo(ExtractionLocalization.CapacityClampedText(dropped));
        }

        // Challenge constraints (empty-carry / basic-common) may have invalidated the draft since the last refresh —
        // clamp once more so the persisted carry always matches what the run enforces. 挑战约束可能在上次刷新后使草稿失效——再次
        // 钳制，保证持久化携带与局内执行的约束一致。
        ClampDraftToChallenges();

        if (!CanProceed())
        {
            Entry.Logger.Info("WarehouseHub: blocked empty-carry start (carry at least one card).");
            return;
        }

        _carry.Gold = _carryGold;
        PendingCarryStore.Set(_carry);
        Entry.Logger.Info($"WarehouseHub: starting extraction run with {_carry.Cards.Count} cards, " +
                          $"{_carry.Relics.Count} relics, {_carry.Potions.Count} potions, {_carry.Gold} gold.");

        SaveFilters();
        WarehouseStore.Persist();

        // The seed is a session-only, host-owned run parameter; blank means random. Always written (null when blank)
        // so a stale PendingSeed from a cancelled launch never leaks into the next run.
        // 种子为仅本次会话、主机所有的跑局参数；留空即随机。无论有无都写入（无则 null），杜绝取消发起残留的旧种子泄漏进下一局。
        string seed = _seedInput.Text.Trim();
        ExtractionRunContext.PendingSeed = seed.Length > 0 ? seed : null;
        ExtractionRunContext.PendingChallenges = _pendingChallenges.Count > 0
            ? new List<string>(_pendingChallenges)
            : null;
        ExtractionRunContext.IsExtractionLaunch = true;
        CloseHub();

        // Launch the run through the existing character-select flow; CharacterSelectPatch applies the modifier and
        // stages the pending carry into the lobby.
        if (_mode == HubMode.MultiplayerHost)
        {
            TaskHelper.RunSafely(NMultiplayerHostSubmenu.StartHostAsync(GameMode.Standard, _loadingOverlay!, _stack));
        }
        else
        {
            NCharacterSelectScreen characterSelect = _stack.GetSubmenuType<NCharacterSelectScreen>();
            characterSelect.InitializeSingleplayer();
            _stack.Push(characterSelect);
        }
    }

    /// <summary>Client-modal confirm: persist the carry draft, then re-stage it into the lobby — the init postfix already
    /// staged the pre-edit pending value, and this overwrites it via <c>SyncLobbyOnChange</c> — before revealing the
    /// lobby. No <c>IsExtractionLaunch</c>: the host forwards the modifier, not the client. 客户端模态确认：持久化草稿、
    /// 重暂存进大厅（init postfix 已暂存旧值，此处覆盖并同步），然后露出大厅。不设 IsExtractionLaunch（修正项由主机转发）。</summary>
    private void ConfirmCarryForClient()
    {
        int dropped = CarryCapacity.ClampToBudget(_carry, CarryBudget.FromSettings());
        if (dropped > 0)
        {
            RitsuToastService.ShowInfo(ExtractionLocalization.CapacityClampedText(dropped));
        }

        if (!CanProceed())
        {
            Entry.Logger.Info("WarehouseHub: blocked empty client carry (carry at least one card).");
            return;
        }

        _carry.Gold = _carryGold;
        PendingCarryStore.Set(_carry);
        if (_lobby is StartRunLobby lobby)
        {
            ExtractionCarrySync.SendConfirmedCarry(lobby, _carry);
            try
            {
                // Keep the RitsuLib staging path as a compatibility fallback for hosts where it is available.
                ExtractionRunData.Carry.Lobby.Set(lobby, lobby.NetService.NetId, _carry);
            }
            catch (Exception ex)
            {
                Entry.Logger.Warn($"WarehouseHub: legacy carry staging failed; direct handoff remains active: {ex.Message}");
            }
        }

        Entry.Logger.Info($"WarehouseHub: client confirmed carry {_carry.Cards.Count} cards, " +
                          $"{_carry.Relics.Count} relics, {_carry.Potions.Count} potions, {_carry.Gold} gold.");
        CloseHub();
    }

    /// <summary>The back flow shared by the 「返回」 button and ESC. Client modal: leave the room by popping the
    /// character-select screen, whose <c>OnSubmenuClosed</c> disconnects the lobby session; otherwise just close the hub
    /// (no unconfirmed draft is persisted in either case). 「返回」按钮与 ESC 共用的返回流程：客户端模态弹出角色选择界面退出
    /// 房间（其 OnSubmenuClosed 断开大厅会话）；其余情况仅关闭大厅。两种情况下都不持久化未确认的草稿。</summary>
    private void OnBack()
    {
        if (_mode == HubMode.MultiplayerClient)
        {
            LeaveRoomAndClose();
        }
        else
        {
            CloseHub();
        }
    }

    /// <summary>Client-modal back: leave the room by popping the character-select screen, whose <c>OnSubmenuClosed</c>
    /// disconnects the lobby session. Does not persist the unconfirmed draft. 客户端模态「返回」：弹出角色选择界面退出房间
    /// （其 OnSubmenuClosed 断开大厅会话），不持久化未确认的草稿。</summary>
    private void LeaveRoomAndClose()
    {
        if (_stack.Peek() is NCharacterSelectScreen)
        {
            _stack.Pop();
        }

        CloseHub();
    }

    /// <summary>True when the hub may proceed: a non-empty carry, or an empty carry when carrying any card is impossible
    /// (the run's starter-deck fallback keeps it playable) — or the EMPTY_CARRY challenge, which demands the empty carry
    /// and supplies the starter kit. 是否可继续：携带非空；或携带为空但已不可能带任何卡（初始牌组兜底可玩）；或选中 EMPTY_CARRY
    /// 挑战（要求空携带并自带起手包）。</summary>
    private bool CanProceed() =>
        PendingChallengeRuntime.StartsEmpty
        || _carry.Cards.Count > 0
        || !CanCarryAnyCards;

    private bool CanCarryAnyCards
    {
        get
        {
            CarryBudget budget = CarryBudget.FromSettings();
            ChallengeRuntime challenges = PendingChallengeRuntime;
            if (budget.UsesCapacity)
            {
                // Possible iff some carryable card's weight fits the whole pool (weights ≥ 1, so capacity ≥ 1 usually
                // qualifies; a tiny capacity with only heavyweight stock makes carrying impossible → starter fallback).
                if (budget.Capacity <= 0 || _warehouse.Cards.Count == 0)
                {
                    return false;
                }

                int cheapest = _warehouse.Cards
                    .Where(c => c.Card.Id is ModelId id
                        && ModelDb.GetByIdOrNull<CardModel>(id) is { } card
                        && challenges.AllowsCarryCard(card))
                    .Select(c => CarryCapacity.WeightForCard(c.Card.Id))
                    .Where(w => w > 0)
                    .DefaultIfEmpty(int.MaxValue)
                    .Min();
                return cheapest <= budget.Capacity;
            }

            return budget.MaxCards > 0 && _warehouse.Cards.Any(c => c.Card.Id is ModelId id
                && ModelDb.GetByIdOrNull<CardModel>(id) is { } card
                && challenges.AllowsCarryCard(card));
        }
    }

    // ----- Challenge-constraint helpers 挑战约束助手 -----

    /// <summary>
    /// Projects the carry draft through the selected parameterized rules. The authoritative projection lives in
    /// <c>ExtractionModifier.AfterRunCreated</c>; this is the hub-side mirror so the UI never shows an un-injectable
    /// carry. 将携带草稿投影到选中的参数化规则；权威校验仍在 modifier 注入处，本处仅镜像 UI。
    /// </summary>
    private void ClampDraftToChallenges()
    {
        ChallengeRuntime challenges = PendingChallengeRuntime;
        if (challenges.StartsEmpty)
        {
            _carry.Cards.Clear();
            _carry.Relics.Clear();
            _carry.Potions.Clear();
            _carryGold = 0;
            _carry.Gold = 0;
            return;
        }

        if ((challenges.HasCarryRarityFilter || challenges.HasCarryTagFilter) && _carry.Cards.Count > 0)
        {
            _carry.Cards.RemoveAll(wc =>
                wc.Card.Id == null ||
                ModelDb.GetByIdOrNull<CardModel>(wc.Card.Id) is { } m && !challenges.AllowsCarryCard(m));
        }
    }

    /// <summary>Whether a card group's add-tile may be pressed under the selected challenges. 该卡牌瓦片在所选挑战下可否添加。</summary>
    private bool CanCarryCardTile(ExtractionItemTiles.CardGroup g)
    {
        ChallengeRuntime challenges = PendingChallengeRuntime;
        if (challenges.StartsEmpty)
        {
            return false;
        }

        if ((challenges.HasCarryRarityFilter || challenges.HasCarryTagFilter)
            && (g.Rep.Id is not ModelId id
                || ModelDb.GetByIdOrNull<CardModel>(id) is not { } card
                || !challenges.AllowsCarryCard(card)))
        {
            return false;
        }

        return true;
    }

    private ChallengeRuntime PendingChallengeRuntime => ChallengeRuntime.FromIds(_pendingChallenges);

    private string ChallengeSummaryText()
    {
        if (_pendingChallenges.Count == 0)
        {
            return "";
        }

        return ExtractionLocalization.ChallengeSummaryText(
            string.Join(" / ", _pendingChallenges.Select(ExtractionLocalization.ChallengeTitle)));
    }

    private void CloseHub()
    {
        SaveFilters();
        if (_shopPage is ShopScreen shop)
        {
            shop.PersistState();
        }

        WarehouseStore.Persist();
        QueueFree();
    }

    // ----- Layout helpers 布局辅助 -----

    /// <summary>A floating dark card with inner gutters, returning its content VBox. 悬浮深色卡片。</summary>
    private static PanelContainer MakeCard(float stretchRatio, out VBoxContainer body)
    {
        var card = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        card.SizeFlagsStretchRatio = stretchRatio;

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 22);
        margin.AddThemeConstantOverride("margin_right", 22);
        margin.AddThemeConstantOverride("margin_top", 20);
        margin.AddThemeConstantOverride("margin_bottom", 20);
        card.AddChild(margin);

        body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 12);
        margin.AddChild(body);
        return card;
    }

    private static Control MakeSectionHeader(string title)
    {
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 10);

        var titleLabel = MakeLabel(title);
        titleLabel.AddThemeFontOverride("font", ExtractionTheme.Bold);
        titleLabel.AddThemeFontSizeOverride("font_size", ExtractionTheme.FontSizeSection);
        header.AddChild(titleLabel);

        return header;
    }

    private static Control MakeSectionHeaderWithDetail(string title, out Label detail)
    {
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 10);

        var titleLabel = MakeLabel(title);
        titleLabel.AddThemeFontOverride("font", ExtractionTheme.Bold);
        titleLabel.AddThemeFontSizeOverride("font_size", ExtractionTheme.FontSizeSection);
        header.AddChild(titleLabel);

        header.AddChild(MakeSpacer());

        detail = MakeLabel("");
        detail.AddThemeColorOverride("font_color", ExtractionTheme.TextSecondary);
        detail.AddThemeFontSizeOverride("font_size", ExtractionTheme.FontSizeSmall);
        header.AddChild(detail);

        return header;
    }

    /// <summary>A wrapping grid of small carry tiles. 携带区自动换行网格。</summary>
    private static HFlowContainer MakeList()
    {
        var box = new HFlowContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        box.AddThemeConstantOverride("separation", 8);
        return box;
    }

    private static ScrollContainer Scroll(Control child, float stretchRatio)
    {
        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = stretchRatio,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        scroll.AddChild(child);
        return scroll;
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
}
