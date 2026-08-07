using Godot;

namespace ExtractionRun.UI;

/// <summary>
/// A themed multi-select filter dropdown for the warehouse hub. A Button showing "<see cref="Title"/> (n)" opens a
/// dark <see cref="PopupPanel"/> below it listing the options as toggle buttons (selected = pressed), with 全部 / 清除
/// actions in the header. Stays open across multiple selections; fires <see cref="SelectionChanged"/> on every user
/// toggle. Option lists are rebuilt programmatically via <see cref="SetOptions"/> / <see cref="SetSelected"/> without
/// firing the event, so persisted state can round-trip through the hub's Refresh without feedback loops.
/// 仓库大厅的深色多选过滤下拉：按钮显示「标题 (n)」，点击在其下方弹出 PopupPanel，选项为可切换按钮（选中=按下态），
/// 头部带「全部/清除」。多次选择时保持展开；每次用户切换触发 SelectionChanged。SetOptions/SetSelected 为程序化重建、
/// 不触发事件，可让持久化状态经 Refresh 回环而不产生反馈死循环。
/// </summary>
public sealed partial class FilterDropdown : Button
{
    private readonly List<(string Value, string Label)> _options = new();
    private readonly HashSet<string> _selected = new();
    private readonly PopupPanel _popup;
    private readonly ScrollContainer _scroll;
    private readonly VBoxContainer _list;
    private readonly Label _titleLabel;
    private string _title = "";

    /// <summary>Raised when the user toggles an option (not on programmatic Set* calls). 用户切换选项时触发。</summary>
    public event Action? SelectionChanged;

    public FilterDropdown()
    {
        ThemeTypeVariation = ExtractionTheme.ButtonSecondary;
        // Content-width trigger (compact): the filter row packs these left-aligned in one line. 触发器贴合文字（紧凑），
        // 由过滤行左对齐排成一行。
        CustomMinimumSize = new Vector2(0f, 40f);
        Alignment = HorizontalAlignment.Center;
        Pressed += Open;

        // Popup closes on outside click by default; selections inside keep it open for multi-pick.
        _popup = new PopupPanel();
        _popup.AddThemeStyleboxOverride("panel", ExtractionTheme.CardBox());
        _popup.Theme = ExtractionTheme.Instance;
        AddChild(_popup);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_top", 10);
        margin.AddThemeConstantOverride("margin_bottom", 10);
        _popup.AddChild(margin);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        margin.AddChild(box);

        var titleRow = new HBoxContainer();
        titleRow.AddThemeConstantOverride("separation", 10);
        _titleLabel = new Label();
        _titleLabel.AddThemeFontOverride("font", ExtractionTheme.Bold);
        _titleLabel.AddThemeFontSizeOverride("font_size", ExtractionTheme.FontSizeSection);
        titleRow.AddChild(_titleLabel);
        titleRow.AddChild(MakeSpacer());

        var all = MakeActionButton(ExtractionLocalization.FilterAllText());
        all.Pressed += SelectAll;
        titleRow.AddChild(all);

        var clear = MakeActionButton(ExtractionLocalization.FilterClearText());
        clear.Pressed += ClearAll;
        titleRow.AddChild(clear);
        box.AddChild(titleRow);

        // Popup adapts to its content: width floor + height cap are set in Open() from the trigger width and the
        // option list height — instead of the old fixed 230×300 (which left a big empty area under short option
        // lists). 弹层自适应内容：宽度下限与高度封顶在 Open() 里按触发器宽度与选项列表高度计算，替代原来固定 230×300
        // （短选项列表下方会空一大截）。
        _scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0f, 0f),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _list = new VBoxContainer();
        _list.AddThemeConstantOverride("separation", 2);
        _scroll.AddChild(_list);
        box.AddChild(_scroll);
    }

    /// <summary>Display title (shown on the button and in the popup header). 显示标题。</summary>
    public string Title
    {
        get => _title;
        set
        {
            _title = value;
            _titleLabel.Text = value;
            UpdateText();
        }
    }

    /// <summary>Currently selected option values. 当前选中的选项值。</summary>
    public IReadOnlyCollection<string> Selected => _selected;

    /// <summary>
    /// Replaces the option list; prunes selections that no longer map to an option. Rebuilds only when something
    /// actually changed (so the per-keystroke Refresh doesn't churn the popup contents). 替换选项列表并剪除已失效的选中项；
    /// 仅在真正变化时重建（避免每次击键的 Refresh 反复重建弹层内容）。
    /// </summary>
    public void SetOptions(IEnumerable<(string Value, string Label)> options)
    {
        List<(string Value, string Label)> incoming = options.ToList();
        bool optionsChanged = !_options.SequenceEqual(incoming);
        _options.Clear();
        _options.AddRange(incoming);

        int before = _selected.Count;
        _selected.IntersectWith(_options.Select(o => o.Value));
        bool pruned = _selected.Count != before;

        if (optionsChanged || pruned)
        {
            RebuildList();
            UpdateText();
        }
    }

    /// <summary>Restores the selection from persisted state (pruned to valid options). 从持久化状态恢复选中（剪除无效项）。</summary>
    public void SetSelected(IEnumerable<string> selected)
    {
        HashSet<string> incoming = selected.ToHashSet();
        if (incoming.SetEquals(_selected))
        {
            return;
        }

        _selected.Clear();
        var valid = new HashSet<string>();
        foreach ((string value, _) in _options)
        {
            valid.Add(value);
        }

        foreach (string value in incoming)
        {
            if (valid.Contains(value))
            {
                _selected.Add(value);
            }
        }

        RebuildList();
        UpdateText();
    }

    private void RebuildList()
    {
        foreach (Node child in _list.GetChildren())
        {
            child.QueueFree();
        }

        foreach ((string value, string label) in _options)
        {
            // Content-width option: one line per item, no stretched empty space. 选项贴合文字、每项一行，不撑满留空。
            var toggle = new Button
            {
                Text = label,
                ThemeTypeVariation = ExtractionTheme.ButtonRow,
                ToggleMode = true,
                ButtonPressed = _selected.Contains(value),
            };
            toggle.Toggled += on => Toggle(value, on);
            _list.AddChild(toggle);
        }
    }

    private void Toggle(string value, bool on)
    {
        if (on)
        {
            _selected.Add(value);
        }
        else
        {
            _selected.Remove(value);
        }

        UpdateText();
        SelectionChanged?.Invoke();
    }

    private void SelectAll()
    {
        _selected.Clear();
        foreach ((string value, _) in _options)
        {
            _selected.Add(value);
        }

        RebuildList();
        UpdateText();
        SelectionChanged?.Invoke();
    }

    private void ClearAll()
    {
        _selected.Clear();
        RebuildList();
        UpdateText();
        SelectionChanged?.Invoke();
    }

    private void UpdateText() => Text = _selected.Count == 0 ? _title : $"{_title} ({_selected.Count})";

    private void Open()
    {
        // Width floor = the trigger's own width (plus a small absolute floor) so the popup is never narrower than its
        // button; the option list may widen it further. Height = the option list's natural height, capped so long
        // lists scroll instead of overflowing. 宽度下限 = 触发器宽度（含 ~140px 绝对下限），保证弹层不窄于触发按钮；选项更宽
        // 时再自动加宽。高度 = 选项列表自然高度，封顶后滚动。
        float listHeight = _list.GetCombinedMinimumSize().Y;
        float popupHeight = Mathf.Min(listHeight, 320f);
        _scroll.CustomMinimumSize = new Vector2(Mathf.Max(140f, Size.X), popupHeight);

        Rect2 rect = GetGlobalRect();
        int x = (int)rect.Position.X;
        int y = (int)(rect.Position.Y + rect.Size.Y);
        _popup.Position = new Vector2I(x, y);
        _popup.Popup();

        // Flip upward when opening below the button would overflow the screen bottom (Godot sizes the popup to its
        // natural min size on show, so Size is now the real laid-out size). 若向下展开会超出屏幕底部则改为向上（Popup 显示后
        // Size 已是真实布局尺寸，据此精确修正，无闪烁）。
        Vector2 size = _popup.Size;
        if (y + size.Y > GetViewportRect().Size.Y)
        {
            _popup.Position = new Vector2I(x, (int)Math.Max(0f, rect.Position.Y - size.Y));
        }
    }

    private static Control MakeSpacer() => new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };

    private static Button MakeActionButton(string text)
    {
        return new Button
        {
            Text = text,
            ThemeTypeVariation = ExtractionTheme.ButtonRow,
            CustomMinimumSize = new Vector2(0f, 32f),
        };
    }
}
