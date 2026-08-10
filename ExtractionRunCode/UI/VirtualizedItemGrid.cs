using Godot;
using MegaCrit.Sts2.Core.Models;

namespace ExtractionRun.UI;

/// <summary>
/// A pooled, virtualized grid of item tiles for the warehouse hub. Renders only the tiles inside the parent
/// ScrollContainer's visible window (plus a one-row margin), recycling a small pool of tile buttons via absolute
/// positioning — scrolling never creates or frees nodes, and art is resolved lazily through per-tile resolvers so a
/// warehouse of any size costs a bounded number of live nodes and decodes.
/// 仓库大厅的池化虚拟网格：只渲染父级 ScrollContainer 可视窗口内（外加一行余量）的瓦片，用绝对定位复用一小池瓦片按钮——
/// 滚动绝不新建/释放节点；贴图经每瓦片的解析器延迟解析，仓库再大也只需有限的活动节点与解码。
/// </summary>
public sealed partial class VirtualizedItemGrid : Control
{
    private const float Gap = 8f;

    /// <summary>A tile's render payload. Texture is a resolver so the preload path can fill art in place; durability
    /// is the tile's durability badge (null hides it — potions / no-durability mode).
    /// 单瓦片渲染数据；贴图为解析器，供预载路径原地补图；耐久为瓦片耐久角标（null 隐藏——药水/无耐久模式）。</summary>
    public sealed record RenderData(string Name, string Pool, int Count, Func<Texture2D?> Texture,
        ExtractionItemTiles.ItemTileAction Action, Action? OnClick, ModelId? Id, int? Durability = null);

    private readonly List<Button> _pool = new();
    private readonly Dictionary<Button, Action?> _callbacks = new();
    private readonly List<(int Index, Button Tile)> _active = new();

    private IReadOnlyList<RenderData> _items = Array.Empty<RenderData>();
    private ScrollContainer? _scroll;
    private int _columns = 1;
    private float _contentHeight;

    public override void _Ready()
    {
        MouseFilter = Control.MouseFilterEnum.Ignore;
        // Fill the ScrollContainer's viewport width so the column count (computed from the available width) matches
        // the visible area. 撑满 ScrollContainer 视口宽度，让列数与可视区域一致。
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _scroll = GetParent() as ScrollContainer;
        if (_scroll != null)
        {
            _scroll.GetVScrollBar().ValueChanged += _ => OnViewportChanged();
            // The scroll is the authority on the real viewport width: a bare Control child of a ScrollContainer is not
            // guaranteed to be stretched to it, so re-layout whenever the scroll resizes. This also covers the first
            // real layout after _Ready, when SetItems may already have run with a zero width (which used to collapse
            // the grid to a single column). 以 ScrollContainer 的视口宽度为准并随其尺寸变化重排——裸子控件不保证被拉伸到视口
            // 宽度，且 _Ready 时 SetItems 可能已在宽度为 0 的情况下跑过（之前正是因此塌成单列）。
            _scroll.Resized += OnViewportChanged;
        }

        Resized += OnViewportChanged;
    }

    /// <summary>Replaces the grid's items and lays out the visible window. 替换网格内容并布局可视窗口。</summary>
    public void SetItems(IReadOnlyList<RenderData> items)
    {
        _items = items;
        OnViewportChanged();
    }

    /// <summary>Re-populates the visible window, re-invoking the texture resolvers (after a preload finishes).
    /// 重新填充可视窗口（预载完成后重新解析贴图）。</summary>
    public void RefreshTextures() => OnViewportChanged();

    /// <summary>
    /// The grid's available width: the ScrollContainer's viewport width minus the vertical scrollbar when visible,
    /// falling back to this control's own size. The scroll is authoritative because a bare Control child may never be
    /// stretched to the viewport width — reading Size.X directly is what collapsed the grid to one column.
    /// 网格可用宽度：ScrollContainer 视口宽度（可见垂直滚动条时扣除），回退到自身 Size。滚动容器是权威宽度来源——
    /// 直接读自身 Size.X 正是之前塌成单列的原因。
    /// </summary>
    private float ContentWidth()
    {
        if (_scroll == null)
        {
            return Size.X;
        }

        float width = _scroll.Size.X;
        VScrollBar? vbar = _scroll.GetVScrollBar();
        if (vbar != null && vbar.Visible)
        {
            width -= vbar.Size.X;
        }

        return Mathf.Max(width, Size.X);
    }

    private void OnViewportChanged()
    {
        if (_items.Count == 0)
        {
            if (_contentHeight != 0f)
            {
                _contentHeight = 0f;
                CustomMinimumSize = Vector2.Zero;
            }

            ReleaseAll();
            return;
        }

        _columns = Math.Max(1, (int)((ContentWidth() + Gap) / (ExtractionItemTiles.TileWidth + Gap)));
        _columns = Math.Min(_columns, _items.Count);

        float stride = ExtractionItemTiles.TileHeight + Gap;
        int rows = (_items.Count + _columns - 1) / _columns;
        float contentHeight = rows * stride - Gap;
        if (contentHeight != _contentHeight)
        {
            _contentHeight = contentHeight;
            CustomMinimumSize = new Vector2(0f, contentHeight);
        }

        // Visible window (one row of margin above and below).
        float viewTop = _scroll?.ScrollVertical ?? 0f;
        float viewBottom = viewTop + Math.Max(1f, _scroll?.Size.Y ?? Size.Y);
        int firstIndex = Math.Max(0, ((int)Math.Floor(viewTop / stride) - 1) * _columns);
        int lastIndex = Math.Min(_items.Count - 1, ((int)Math.Ceiling(viewBottom / stride) + 1) * _columns - 1);

        // Retire tiles scrolled out of the window.
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            (int index, Button tile) = _active[i];
            if (index < firstIndex || index > lastIndex)
            {
                Release(tile);
                _active.RemoveAt(i);
            }
        }

        // Reposition + repopulate every tile that stays visible.
        foreach ((int index, Button tile) in _active)
        {
            Place(tile, index);
        }

        // Acquire + place tiles entering the window.
        var activeIndices = new HashSet<int>();
        foreach ((int index, _) in _active)
        {
            activeIndices.Add(index);
        }

        for (int index = firstIndex; index <= lastIndex; index++)
        {
            if (activeIndices.Contains(index))
            {
                continue;
            }

            Button tile = Acquire();
            _active.Add((index, tile));
            Place(tile, index);
        }
    }

    private void Place(Button tile, int index)
    {
        RenderData data = _items[index];
        int row = index / _columns;
        int col = index % _columns;
        float stride = ExtractionItemTiles.TileHeight + Gap;
        tile.Position = new Vector2(col * (ExtractionItemTiles.TileWidth + Gap), row * stride);
        ExtractionItemTiles.PopulateItemTile(tile, data.Name, data.Pool, data.Count, data.Texture(), data.Action,
            data.Id, data.Durability);
        _callbacks[tile] = data.OnClick;
        tile.Visible = true;
    }

    private Button Acquire()
    {
        if (_pool.Count > 0)
        {
            Button button = _pool[^1];
            _pool.RemoveAt(_pool.Count - 1);
            return button;
        }

        Button tile = ExtractionItemTiles.CreateItemTile();
        // One handler reads the current callback so recycled tiles never accumulate event subscriptions.
        tile.Pressed += () =>
        {
            if (_callbacks.TryGetValue(tile, out Action? callback))
            {
                callback?.Invoke();
            }
        };
        AddChild(tile);
        return tile;
    }

    private void Release(Button tile)
    {
        ExtractionItemTooltip.Hide(tile);
        tile.Visible = false;
        _callbacks[tile] = null;
        _pool.Add(tile);
    }

    private void ReleaseAll()
    {
        foreach ((_, Button tile) in _active)
        {
            Release(tile);
        }

        _active.Clear();
    }
}
