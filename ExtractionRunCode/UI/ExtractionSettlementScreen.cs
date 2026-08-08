using Godot;
using ExtractionRun.Lifecycle;

namespace ExtractionRun.UI;

/// <summary>
/// Post-run settlement screen: shows the extraction result after the vanilla game-over summary page. Success lists the
/// loot deposited into the warehouse (final deck / relics / potions / gold); failure lists the carried loadout that
/// was lost. Read-only card-form tiles, dark themed like the warehouse hub. The report sits in a horizontally-centered
/// capped-width column; each section's items render as rows×columns grids whose column count derives from the column
/// width (see <see cref="ApplyColumns"/>).
/// 跑局结算界面：成功列出存入仓库的战利品，失败列出损失的携带装备；只读卡片形式，深色主题。结算内容置于水平居中的
/// 封顶宽度列，各分区物品以行列网格展示，列数由列宽推导（见 <see cref="ApplyColumns"/>）。
/// </summary>
public sealed partial class ExtractionSettlementScreen : CanvasLayer
{
    private readonly ExtractionSettlementResult _result;

    /// <summary>Item grid gap in px (matches the warehouse hub's 8px). 物品网格间距。</summary>
    private const int GridGap = 8;

    /// <summary>Content column inner left/right gutter (24 each). 内容列左右内边距。</summary>
    private const int ColumnInnerMargin = 24;

    /// <summary>Content column max width; wider viewports center it with side gutters. 内容列最大宽度。</summary>
    private const float MaxColumnWidth = 1600f;

    /// <summary>Side breathing room so the centered column never touches the viewport edges. 两侧留白，避免贴边。</summary>
    private const float SideMargin = 96f;

    // One strict grid per non-empty section; column counts recompute from the column width on resize. 每个非空分区的网格，列数随列宽重算。
    private readonly List<GridContainer> _gridSections = new();

    private Panel _root = null!;
    private MarginContainer _column = null!;
    private float _columnWidth;

    public ExtractionSettlementScreen(ExtractionSettlementResult result)
    {
        _result = result;
        Layer = 100;
    }

    public override void _Ready()
    {
        BuildUi();
    }

    public override void _Input(InputEvent inputEvent)
    {
        if (inputEvent is InputEventKey { Pressed: true, Keycode: Key.Escape } key && !key.IsEcho())
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    private void BuildUi()
    {
        _root = new Panel { Name = "SettlementPanel" };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddThemeStyleboxOverride("panel", ExtractionTheme.BackgroundBox());
        _root.Theme = ExtractionTheme.Instance;
        AddChild(_root);

        _columnWidth = CurrentColumnWidth();
        _column = new MarginContainer();
        _column.AnchorLeft = 0.5f;
        _column.AnchorRight = 0.5f;
        _column.AnchorTop = 0f;
        _column.AnchorBottom = 1f;
        _column.OffsetLeft = -_columnWidth / 2f;
        _column.OffsetRight = _columnWidth / 2f;
        _column.OffsetTop = 0f;
        _column.OffsetBottom = 0f;
        _column.AddThemeConstantOverride("margin_left", ColumnInnerMargin);
        _column.AddThemeConstantOverride("margin_right", ColumnInnerMargin);
        _column.AddThemeConstantOverride("margin_top", 28);
        _column.AddThemeConstantOverride("margin_bottom", 28);
        _root.AddChild(_column);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 12);
        _column.AddChild(vbox);

        vbox.AddChild(BuildHeader());
        vbox.AddChild(BuildLede());
        vbox.AddChild(BuildBody());

        _root.Resized += ApplyColumns;
    }

    private Control BuildHeader()
    {
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 16);

        var title = new Label
        {
            Text = _result.Success
                ? ExtractionLocalization.SettlementSuccessTitleText()
                : ExtractionLocalization.SettlementFailTitleText(),
        };
        title.AddThemeFontOverride("font", ExtractionTheme.Bold);
        title.AddThemeFontSizeOverride("font_size", ExtractionTheme.FontSizeTitle);
        title.AddThemeColorOverride("font_color",
            _result.Success ? ExtractionTheme.Primary : ExtractionTheme.Text);
        header.AddChild(title);

        header.AddChild(MakeSpacer());

        var back = new Button
        {
            Text = ExtractionLocalization.SettlementBackText(),
            ThemeTypeVariation = ExtractionTheme.ButtonSecondary,
        };
        back.CustomMinimumSize = new Vector2(0f, 44f);
        back.Pressed += Close;
        header.AddChild(back);

        return header;
    }

    private Control BuildLede()
    {
        var lede = new Label
        {
            Text = _result.Success
                ? ExtractionLocalization.SettlementSuccessLedeText()
                : ExtractionLocalization.SettlementFailLedeText(),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        lede.AddThemeColorOverride("font_color", ExtractionTheme.TextSecondary);
        lede.AddThemeFontSizeOverride("font_size", ExtractionTheme.FontSizeSmall);
        return lede;
    }

    private Control BuildBody()
    {
        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 16);
        scroll.AddChild(body);

        bool success = _result.Success;

        body.AddChild(BuildSection(
            success
                ? ExtractionLocalization.SettlementCardsText(_result.Cards.Count)
                : ExtractionLocalization.SettlementLostCardsText(_result.Cards.Count),
            ExtractionItemTiles.GroupCards(_result.Cards).Select(g => (g.Name, g.Pool, g.Count, g.Texture))));

        body.AddChild(new HSeparator());

        body.AddChild(BuildSection(
            success
                ? ExtractionLocalization.SettlementRelicsText(_result.Relics.Count)
                : ExtractionLocalization.SettlementLostRelicsText(_result.Relics.Count),
            ExtractionItemTiles.GroupRelics(_result.Relics).Select(g => (g.Name, g.Pool, g.Count, g.Texture))));

        body.AddChild(new HSeparator());

        body.AddChild(BuildSection(
            success
                ? ExtractionLocalization.SettlementPotionsText(_result.Potions.Count)
                : ExtractionLocalization.SettlementLostPotionsText(_result.Potions.Count),
            ExtractionItemTiles.GroupPotions(_result.Potions).Select(g => (g.Name, g.Pool, g.Count, g.Texture))));

        body.AddChild(new HSeparator());

        body.AddChild(BuildGoldRow());

        return scroll;
    }

    private Control BuildSection(string header, IEnumerable<(string Name, string Pool, int Count, Texture2D? Texture)> tiles)
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);

        var headerLabel = new Label { Text = header };
        headerLabel.AddThemeFontOverride("font", ExtractionTheme.Bold);
        headerLabel.AddThemeFontSizeOverride("font_size", ExtractionTheme.FontSizeSection);
        headerLabel.AddThemeColorOverride("font_color", ExtractionTheme.Text);
        box.AddChild(headerLabel);

        var list = tiles.ToList();
        if (list.Count == 0)
        {
            var empty = new Label
            {
                Text = ExtractionLocalization.SettlementEmptyText(),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                CustomMinimumSize = new Vector2(0f, 56f),
            };
            empty.AddThemeColorOverride("font_color", ExtractionTheme.TextSecondary);
            empty.AddThemeFontSizeOverride("font_size", ExtractionTheme.FontSizeSmall);
            box.AddChild(empty);
        }
        else
        {
            var grid = new GridContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            grid.AddThemeConstantOverride("h_separation", GridGap);
            grid.AddThemeConstantOverride("v_separation", GridGap);
            grid.Columns = ComputeColumns();
            foreach ((string name, string pool, int count, Texture2D? texture) in list)
            {
                grid.AddChild(ExtractionItemTiles.MakeItemTile(name, pool, count, texture,
                    ExtractionItemTiles.ItemTileAction.Display, null));
            }

            box.AddChild(grid);
            _gridSections.Add(grid);
        }

        return box;
    }

    private Control BuildGoldRow()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);

        var label = new Label { Text = ExtractionLocalization.SettlementGoldText(_result.Gold) };
        label.AddThemeFontOverride("font", ExtractionTheme.Bold);
        label.AddThemeFontSizeOverride("font_size", ExtractionTheme.FontSizeSection);
        label.AddThemeColorOverride("font_color", ExtractionTheme.GoldChipText);
        row.AddChild(label);

        return row;
    }

    /// <summary>
    /// Content column width = min(MaxColumnWidth, viewport − side margin), floored at a usable minimum. The column is
    /// anchored to the viewport center, so this width is what actually renders on the game screen.
    /// 内容列宽度 = min(封顶 1600, 视口宽 − 两侧留白 96)，列以视口中心为锚，此宽度即实际渲染宽度。
    /// </summary>
    private float CurrentColumnWidth()
    {
        float available = GetViewport().GetVisibleRect().Size.X - SideMargin;
        return Math.Max(320f, Math.Min(MaxColumnWidth, available));
    }

    /// <summary>
    /// Column count from the content column width (deterministic, no dependency on the grid's laid-out size):
    /// grid width = column width − inner gutters, divided by (tile + gap), floored, min 1, no cap. 13 @1080p
    /// (1600px column), fewer on narrow windows.
    /// 由内容列宽确定性计算列数（不依赖网格布局尺寸）：网格宽 = 列宽 − 内边距，除以（卡片 + 间距）向下取整、至少 1、无上限。
    /// 1080p（1600px 列）为 13 列，窄屏更少。
    /// </summary>
    private int ComputeColumns()
    {
        float gridWidth = _columnWidth - ColumnInnerMargin * 2;
        return Math.Max(1, (int)(gridWidth / (ExtractionItemTiles.TileWidth + GridGap)));
    }

    /// <summary>
    /// Re-applies the centered column width and grid column counts on the root panel's Resized (window resize).
    /// 在根面板 Resized（窗口缩放）时重算居中列宽与网格列数。
    /// </summary>
    private void ApplyColumns()
    {
        _columnWidth = CurrentColumnWidth();
        _column.OffsetLeft = -_columnWidth / 2f;
        _column.OffsetRight = _columnWidth / 2f;

        int columns = ComputeColumns();
        foreach (GridContainer grid in _gridSections)
        {
            if (grid.Columns != columns)
            {
                grid.Columns = columns;
            }
        }
    }

    private static Control MakeSpacer()
    {
        return new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
    }

    private void Close()
    {
        QueueFree();
    }
}
