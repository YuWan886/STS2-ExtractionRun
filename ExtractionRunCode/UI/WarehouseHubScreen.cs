using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using STS2RitsuLib.Ui.Toast;
using ExtractionRun.Data;
using ExtractionRun.Lifecycle;
using ExtractionRun.Settings;

namespace ExtractionRun.UI;

/// <summary>
/// The 搜打撤 warehouse hub: a full-screen overlay opened from the main menu. Shows the persistent warehouse
/// (cards / relics / potions / gold), lets the player pick a carry config (deck ≤ MaxCarryCards, relics ≤
/// MaxCarryRelics, potions ≤ slots, gold), seeds + migrates the warehouse on first open, and launches the run.
/// The warehouse side is split into three tabs (cards / relics / potions) with per-tab search + multi-select filters
/// (persisted in <see cref="WarehouseFilterState"/>), rendered by a pooled <see cref="VirtualizedItemGrid"/> with
/// background art preload (<see cref="WarehouseCache"/>); the carry panel stays on the right and never tab-switches.
/// 搜打撤仓库大厅：主菜单打开的全屏覆盖层。展示仓库、编辑携带配置、首次种子/迁移、发起跑局。仓库侧拆为卡牌/遗物/药水三个
/// Tab，各自独立的搜索与多选过滤（持久化于 WarehouseFilterState），用池化虚拟网格 + 后台贴图预载渲染；携带面板常驻右侧。
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

    private enum FilterKind
    {
        CardPools,
        CardRarities,
        CardTypes,
        CardCosts,
        RelicPools,
        RelicRarities,
        PotionPools,
        PotionRarities,
    }

    private readonly NSubmenuStack _stack;
    private readonly Control? _loadingOverlay;
    private readonly HubMode _mode;
    private readonly StartRunLobby? _lobby;
    private readonly WarehouseData _warehouse;
    private readonly CarryConfig _carry;
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
    private Label _carryDeckLabel = null!;
    private Label _carryRelicsLabel = null!;
    private Label _carryPotionsLabel = null!;
    private LineEdit _goldInput = null!;
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

        // Seed on first open (idempotent), run the one-shot legacy normalizations (base-state + identity-card repair),
        // then load the live warehouse and a detached copy of the pending carry — only written back on confirm/start,
        // so closing never leaks edits. 首次种子、一次性旧档归一（基础态 + 身份牌修复）、加载实时仓库与待发携带的独立副本
        // （仅在确认/开跑时写回）。
        WarehouseStore.EnsureSeeded();
        WarehouseStore.EnsureNormalized();
        WarehouseStore.EnsureIdentityRepaired();
        _warehouse = WarehouseStore.Current;
        _carry = PendingCarryStore.Snapshot();
        _carryGold = _carry.Gold;

        // A pending carry saved before the base-only change may still hold upgraded/enchanted items. Normalize it in
        // place so carried items always match the (base-only) warehouse exactly — otherwise a stale +1 carry would
        // consume a base copy while injecting the upgraded one (free upgrade). Identity cards (MadScience) keep their
        // saved props here, matching the warehouse's own normalization. 旧档遗留的待发携带可能仍带升级/附魔；原地归一到与
        // 仓库一致的基础态（身份牌保留其 Props），否则旧 +1 携带会消耗基础卡却注入升级卡（白嫖升级）。
        for (int i = 0; i < _carry.Cards.Count; i++)
        {
            _carry.Cards[i] = WarehouseStore.NormalizeCard(_carry.Cards[i]);
        }

        for (int i = 0; i < _carry.Relics.Count; i++)
        {
            _carry.Relics[i] = WarehouseStore.NormalizeRelic(_carry.Relics[i]);
        }

        for (int i = 0; i < _carry.Potions.Count; i++)
        {
            _carry.Potions[i] = WarehouseStore.NormalizePotion(_carry.Potions[i]);
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

        var content = new HBoxContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        content.AddThemeConstantOverride("separation", 24);
        rootBox.AddChild(content);

        content.AddChild(BuildWarehouseCard());
        content.AddChild(BuildCarryCard());

        rootBox.AddChild(BuildFooter());
    }

    // ----- Header: title + gold chip + back -----

    private Control BuildHeader()
    {
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 16);

        var title = MakeLabel(ExtractionLocalization.HubTitleText());
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
        // One row, content-width buttons (adaptive to the four card filters), left-aligned. 一行四个紧凑按钮（自适应过滤项）。
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        _filters[FilterKind.CardPools] = MakeFilterDropdown(ExtractionLocalization.FilterPoolText());
        _filters[FilterKind.CardRarities] = MakeFilterDropdown(ExtractionLocalization.FilterRarityText());
        _filters[FilterKind.CardTypes] = MakeFilterDropdown(ExtractionLocalization.FilterTypeText());
        _filters[FilterKind.CardCosts] = MakeFilterDropdown(ExtractionLocalization.FilterCostText());
        row.AddChild(_filters[FilterKind.CardPools]);
        row.AddChild(_filters[FilterKind.CardRarities]);
        row.AddChild(_filters[FilterKind.CardTypes]);
        row.AddChild(_filters[FilterKind.CardCosts]);
        return row;
    }

    private Control BuildRelicFilterArea()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        _filters[FilterKind.RelicPools] = MakeFilterDropdown(ExtractionLocalization.FilterPoolText());
        _filters[FilterKind.RelicRarities] = MakeFilterDropdown(ExtractionLocalization.FilterRarityText());
        row.AddChild(_filters[FilterKind.RelicPools]);
        row.AddChild(_filters[FilterKind.RelicRarities]);
        return row;
    }

    private Control BuildPotionFilterArea()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        _filters[FilterKind.PotionPools] = MakeFilterDropdown(ExtractionLocalization.FilterPoolText());
        _filters[FilterKind.PotionRarities] = MakeFilterDropdown(ExtractionLocalization.FilterRarityText());
        row.AddChild(_filters[FilterKind.PotionPools]);
        row.AddChild(_filters[FilterKind.PotionRarities]);
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

    // ----- Footer: primary start action -----

    private Control BuildFooter()
    {
        var footer = new VBoxContainer();
        footer.AddThemeConstantOverride("separation", 6);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 16);
        row.Alignment = BoxContainer.AlignmentMode.Center;

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
        row.AddChild(start);

        footer.AddChild(row);

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
        WarehouseCache.Ensure(_warehouse);

        int availableGold = Math.Max(0, _warehouse.Gold - _carryGold);
        _goldChipLabel.Text = ExtractionLocalization.GoldWarehouseText(availableGold);
        if (!_goldInput.HasFocus())
        {
            _goldInput.Text = _carryGold.ToString();
        }

        int maxCards = Math.Max(0, ExtractionSettingsPage.Current.MaxCarryCards);
        int maxRelics = Math.Max(0, ExtractionSettingsPage.Current.MaxCarryRelics);
        _carryDeckLabel.Text = ExtractionLocalization.LimitCardsText(_carry.Cards.Count, maxCards);
        _carryRelicsLabel.Text = ExtractionLocalization.LimitRelicsText(_carry.Relics.Count, maxRelics);
        _carryPotionsLabel.Text = ExtractionLocalization.LimitPotionsText(_carry.Potions.Count, 3);

        // Empty carry cannot start: ClearsPlayerDeck would give a dead 0-card deck — unless carrying any card is
        // impossible (no cards to carry / MaxCarryCards ≤ 0), in which case the run's starter-deck fallback keeps it
        // playable. 空携带不能开跑（ClearsPlayerDeck 会给一个 0 牌死局）——除非已不可能带任何卡，此时初始牌组兜底仍可玩。
        bool canStart = CanProceed();
        _startButton.Disabled = !canStart;
        _startHintLabel.Visible = !canStart;

        // An empty carry has nothing worth sharing as a gear code. 空携带没有值得分享的战备码。
        _generateButton.Disabled = CarryIsEmpty;

        // Carried counts by item key (id-only), used to compute the available warehouse counts.
        var carriedCards = new Dictionary<string, int>();
        foreach (ExtractionItemTiles.CardGroup g in ExtractionItemTiles.GroupCards(_carry.Cards, loadArt: false))
        {
            carriedCards[ExtractionItemTiles.CardKey(g)] = g.Count;
        }

        var carriedRelics = new Dictionary<string, int>();
        foreach (ExtractionItemTiles.RelicGroup g in ExtractionItemTiles.GroupRelics(_carry.Relics, loadArt: false))
        {
            carriedRelics[ExtractionItemTiles.RelicKey(g)] = g.Count;
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
            foreach (ExtractionItemTiles.CardGroup g in ExtractionItemTiles.GroupCards(_carry.Cards, loadArt: false))
            {
                SerializableCard rep = g.Rep;
                _carryCardList.AddChild(ExtractionItemTiles.MakeItemTile(g.Name, g.Pool, g.Count,
                    WarehouseCache.Resolve(g.PortraitPath), ExtractionItemTiles.ItemTileAction.Remove,
                    () =>
                    {
                        _carry.Cards.Remove(rep);
                        Refresh();
                    }));
            }
        }

        if (_carry.Relics.Count == 0)
        {
            AddEmptyState(_carryRelicList, ExtractionLocalization.EmptyCarryText());
        }
        else
        {
            foreach (ExtractionItemTiles.RelicGroup g in ExtractionItemTiles.GroupRelics(_carry.Relics, loadArt: false))
            {
                SerializableRelic rep = g.Rep;
                _carryRelicList.AddChild(ExtractionItemTiles.MakeItemTile(g.Name, g.Pool, g.Count,
                    WarehouseCache.Resolve(g.IconPath), ExtractionItemTiles.ItemTileAction.Remove,
                    () =>
                    {
                        _carry.Relics.Remove(rep);
                        Refresh();
                    }));
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
                SerializablePotion rep = g.Rep;
                _carryPotionList.AddChild(ExtractionItemTiles.MakeItemTile(g.Name, g.Pool, g.Count,
                    WarehouseCache.Resolve(g.ImagePath), ExtractionItemTiles.ItemTileAction.Remove,
                    () =>
                    {
                        _carry.Potions.Remove(rep);
                        Refresh();
                    }));
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

        List<VirtualizedItemGrid.RenderData> rows = BuildCardRows(varieties, carried);
        _grids[(int)Tab.Cards].SetItems(rows);
        UpdateTabHints(Tab.Cards, rows.Count, varieties.Count == 0, IsFilterActive(FilterKind.CardPools)
            || IsFilterActive(FilterKind.CardRarities) || IsFilterActive(FilterKind.CardTypes)
            || IsFilterActive(FilterKind.CardCosts));
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

        List<VirtualizedItemGrid.RenderData> rows = BuildRelicRows(varieties, carried);
        _grids[(int)Tab.Relics].SetItems(rows);
        UpdateTabHints(Tab.Relics, rows.Count, varieties.Count == 0,
            IsFilterActive(FilterKind.RelicPools) || IsFilterActive(FilterKind.RelicRarities));
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

        List<VirtualizedItemGrid.RenderData> rows = BuildPotionRows(varieties, carried);
        _grids[(int)Tab.Potions].SetItems(rows);
        UpdateTabHints(Tab.Potions, rows.Count, varieties.Count == 0,
            IsFilterActive(FilterKind.PotionPools) || IsFilterActive(FilterKind.PotionRarities));
    }

    private List<VirtualizedItemGrid.RenderData> BuildCardRows(
        IReadOnlyList<ExtractionItemTiles.CardGroup> varieties, Dictionary<string, int> carried)
    {
        var rows = new List<VirtualizedItemGrid.RenderData>();
        List<string> selPools = _warehouse.Filters.CardPools;
        List<string> selRarities = _warehouse.Filters.CardRarities;
        List<string> selTypes = _warehouse.Filters.CardTypes;
        List<string> selCosts = _warehouse.Filters.CardCosts;
        bool poolOn = selPools.Count > 0;
        bool rarityOn = selRarities.Count > 0;
        bool typeOn = selTypes.Count > 0;
        bool costOn = selCosts.Count > 0;
        string query = _query;

        int filtered = 0;
        foreach (ExtractionItemTiles.CardGroup g in varieties)
        {
            int available = g.Count - carried.GetValueOrDefault(ExtractionItemTiles.CardKey(g));
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
                || (costOn && !selCosts.Contains(g.Cost.ToString())))
            {
                continue;
            }

            filtered++;
            if (rows.Count >= MaxTileKinds)
            {
                continue; // Capped; counted toward the hint but not rendered.
            }

            SerializableCard rep = g.Rep;
            rows.Add(new VirtualizedItemGrid.RenderData(g.Name, g.Pool, available,
                () => WarehouseCache.Resolve(g.PortraitPath), ExtractionItemTiles.ItemTileAction.Add,
                () => AddToCarryCards(rep)));
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
        bool poolOn = selPools.Count > 0;
        bool rarityOn = selRarities.Count > 0;
        string query = _query;

        int filtered = 0;
        foreach (ExtractionItemTiles.RelicGroup g in varieties)
        {
            int available = g.Count - carried.GetValueOrDefault(ExtractionItemTiles.RelicKey(g));
            if (available <= 0)
            {
                continue;
            }

            if (query.Length > 0 && !g.Haystack.Contains(query))
            {
                continue;
            }

            if ((poolOn && !selPools.Contains(g.PoolSlug))
                || (rarityOn && !selRarities.Contains(g.Rarity.ToString())))
            {
                continue;
            }

            filtered++;
            if (rows.Count >= MaxTileKinds)
            {
                continue;
            }

            SerializableRelic rep = g.Rep;
            rows.Add(new VirtualizedItemGrid.RenderData(g.Name, g.Pool, available,
                () => WarehouseCache.Resolve(g.IconPath), ExtractionItemTiles.ItemTileAction.Add,
                () => AddToCarryRelics(rep)));
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
        bool poolOn = selPools.Count > 0;
        bool rarityOn = selRarities.Count > 0;
        string query = _query;

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
                || (rarityOn && !selRarities.Contains(g.Rarity.ToString())))
            {
                continue;
            }

            filtered++;
            if (rows.Count >= MaxTileKinds)
            {
                continue;
            }

            SerializablePotion rep = g.Rep;
            rows.Add(new VirtualizedItemGrid.RenderData(g.Name, g.Pool, available,
                () => WarehouseCache.Resolve(g.ImagePath), ExtractionItemTiles.ItemTileAction.Add,
                () => AddToCarryPotions(rep)));
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

    private void AddToCarryCards(SerializableCard sc)
    {
        if (_carry.Cards.Count >= Math.Max(0, ExtractionSettingsPage.Current.MaxCarryCards))
        {
            return;
        }

        _carry.Cards.Add(sc);
        Refresh();
    }

    private void AddToCarryRelics(SerializableRelic sr)
    {
        if (_carry.Relics.Count >= Math.Max(0, ExtractionSettingsPage.Current.MaxCarryRelics))
        {
            return;
        }

        _carry.Relics.Add(sr);
        Refresh();
    }

    private void AddToCarryPotions(SerializablePotion sp)
    {
        if (_carry.Potions.Count >= 3)
        {
            return;
        }

        _carry.Potions.Add(sp);
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
        _warehouse.Filters.RelicPools = _filters[FilterKind.RelicPools].Selected.ToList();
        _warehouse.Filters.RelicRarities = _filters[FilterKind.RelicRarities].Selected.ToList();
        _warehouse.Filters.PotionPools = _filters[FilterKind.PotionPools].Selected.ToList();
        _warehouse.Filters.PotionRarities = _filters[FilterKind.PotionRarities].Selected.ToList();
    }

    private void StartRun()
    {
        // Defense-in-depth: the button is disabled when CanProceed is false, but never launch a 0-card run.
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
        if (!CanProceed())
        {
            Entry.Logger.Info("WarehouseHub: blocked empty client carry (carry at least one card).");
            return;
        }

        _carry.Gold = _carryGold;
        PendingCarryStore.Set(_carry);
        if (_lobby is StartRunLobby lobby)
        {
            ExtractionRunData.Carry.Lobby.Set(lobby, lobby.NetService.NetId, _carry);
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
    /// (the run's starter-deck fallback keeps it playable). 是否可继续：携带非空；或携带为空但已不可能带任何卡（初始牌组兜底可玩）。</summary>
    private bool CanProceed() => _carry.Cards.Count > 0 || !CanCarryAnyCards;

    private bool CanCarryAnyCards => ExtractionSettingsPage.Current.MaxCarryCards > 0 && _warehouse.Cards.Count > 0;

    private void CloseHub()
    {
        SaveFilters();
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
