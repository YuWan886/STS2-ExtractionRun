using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using ExtractionRun.Data;
using ExtractionRun.Lifecycle;
using ExtractionRun.Modifier;
using ExtractionRun.Networking;

namespace ExtractionRun.UI;

/// <summary>
/// The 撤离点 carry-out panel (普通撤离). A per-machine modal overlay opened by <see cref="ExtractionPointEvent"/>'s
/// 普通撤离 option, laid out like the warehouse carry panel: the left column stacks the four kinds of the current
/// loadout — cards / relics (click to select one copy into the right column, shared capacity pool by rarity weight)
/// then potions / gold (read-only, always carried out in full); the right column is the carry-out side — a capacity
/// chip on top, then cards / relics. Confirming with an empty cards/relics selection pops a confirm dialog (only
/// potions + gold would carry out). Confirm builds the <see cref="ExtractionPointSelection"/> (per-id counts) and
/// completes the awaiting flow. The panel never backs out — the shared vote already committed the party to extract.
/// 撤离点带出面板（普通撤离）：ExtractionPointEvent 普通撤离选项打开的每机模态覆盖层，布局对齐仓库携带面板——左栏自上而下堆叠
/// 当前装备的四类：卡牌/遗物（点击各选一份进右栏，卡牌与遗物共享按稀有度权重的容量池），随后药水/金币（只读，全部自动带出）；
/// 右栏为带出侧——顶部容量胶囊，其后卡牌/遗物。卡牌遗物选择为空时确认会弹二次确认（仅药水+金币带出）。确认构建
/// ExtractionPointSelection（按 id 份数）并结束等待的流程。面板不可返回——共享投票已决定全队撤离。
/// </summary>
public sealed partial class ExtractionPointPanel : CanvasLayer
{
    private static readonly Color OverlayTint = new(0f, 0f, 0f, 0.62f);

    private readonly Player _me;
    private readonly TaskCompletionSource<ExtractionPointSelection?> _tcs = new();
    private readonly int _capacity;

    /// <summary>Available copies per id (from the filtered deck/relics at open). 各 id 当前可选份数。</summary>
    private readonly Dictionary<ModelId, int> _available = new();

    /// <summary>Selected copies per id. 各 id 已选份数。</summary>
    private readonly Dictionary<ModelId, int> _selected = new();

    private readonly HashSet<ModelId> _relicIds = new();

    private HFlowContainer _leftCardGrid = null!;
    private HFlowContainer _leftRelicGrid = null!;
    private HFlowContainer _leftPotionGrid = null!;
    private HFlowContainer _rightCardGrid = null!;
    private HFlowContainer _rightRelicGrid = null!;
    private Label _capacityLabel = null!;
    private Label _potionsDetail = null!;
    private Label _goldLabel = null!;
    private ExtractionConfirmDialog? _dialog;
    private int _used;

    private ExtractionPointPanel(ExtractionPointEvent evt)
    {
        Layer = 100;
        _me = evt.Owner!;
        _capacity = Math.Max(0, ExtractionPointSettingsSync.CapacityForAct(evt.Owner!.RunState.CurrentActIndex));
    }

    /// <summary>Opens the panel and awaits the player's confirm. Returns the selection, or null if it failed to open.
    /// 打开面板并等待玩家确认；返回选择，打开失败返回 null。</summary>
    public static Task<ExtractionPointSelection?> ShowAndWait(ExtractionPointEvent evt)
    {
        var panel = new ExtractionPointPanel(evt);
        Node? host = NGame.Instance;
        if (host == null)
        {
            Entry.Logger.Error("ExtractionPointPanel: no NGame to host the panel.");
            return Task.FromResult<ExtractionPointSelection?>(null);
        }

        host.AddChild(panel);
        return panel._tcs.Task;
    }

    public override void _Ready()
    {
        BuildSource();
        BuildUi();
        Refresh();
    }

    public override void _Process(double delta)
    {
        // The run can end under the panel (disconnect/abandon) — free it so the local flow unwinds instead of hanging.
        // 面板打开期间跑局可能结束（断线/放弃）——释放面板让本机流程解挂而不是卡死。
        if (!_tcs.Task.IsCompleted && RunManager.Instance?.IsInProgress != true)
        {
            _dialog?.QueueFree();
            QueueFree();
            _tcs.TrySetResult(null);
        }
    }

    private void BuildSource()
    {
        foreach (CardModel card in _me.Deck.Cards)
        {
            if (CloneMarker.ShouldExclude(card))
            {
                continue;
            }

            if (card.Id is ModelId id)
            {
                _available.TryGetValue(id, out int n);
                _available[id] = n + 1;
            }
        }

        foreach (RelicModel relic in _me.Relics)
        {
            if (ExtractionRunEnd.IsExpiredRelic(relic))
            {
                continue;
            }

            if (relic.Id is ModelId id)
            {
                _available.TryGetValue(id, out int n);
                _available[id] = n + 1;
                _relicIds.Add(id);
            }
        }
    }

    private void BuildUi()
    {
        var root = new Panel { Name = "ExtractionPointOverlay" };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = OverlayTint });
        AddChild(root);

        var margin = new MarginContainer();
        margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 40);
        margin.AddThemeConstantOverride("margin_right", 40);
        margin.AddThemeConstantOverride("margin_top", 28);
        margin.AddThemeConstantOverride("margin_bottom", 28);
        root.AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 14);
        column.Theme = ExtractionTheme.Instance;
        margin.AddChild(column);

        var title = new Label { Text = ExtractionLocalization.ExtractionPointPanelTitleText() };
        title.AddThemeFontOverride("font", ExtractionTheme.Bold);
        title.AddThemeFontSizeOverride("font_size", ExtractionTheme.FontSizeSection);
        column.AddChild(title);

        var body = new HBoxContainer();
        body.AddThemeConstantOverride("separation", 20);
        body.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        column.AddChild(body);

        // Left column: the four kinds of the current loadout, top to bottom.
        // 左栏：当前装备四类，自上而下。
        var left = new VBoxContainer();
        left.AddThemeConstantOverride("separation", 8);
        left.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        left.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        body.AddChild(left);

        left.AddChild(MakeSectionHeader(ExtractionLocalization.SectionCardsText()));
        _leftCardGrid = MakeGrid();
        left.AddChild(MakeScroll(_leftCardGrid, 2f));

        left.AddChild(new HSeparator());
        left.AddChild(MakeSectionHeader(ExtractionLocalization.SectionRelicsText()));
        _leftRelicGrid = MakeGrid();
        left.AddChild(MakeScroll(_leftRelicGrid, 2f));

        left.AddChild(new HSeparator());
        left.AddChild(MakeSectionHeaderWithDetail(ExtractionLocalization.SectionPotionsText(), out _potionsDetail));
        _leftPotionGrid = MakeGrid();
        left.AddChild(MakeScroll(_leftPotionGrid, 1f));

        left.AddChild(new HSeparator());
        left.AddChild(MakeSectionHeader(ExtractionLocalization.SectionGoldText()));
        left.AddChild(MakeAmountChip(out _goldLabel, ExtractionTheme.GoldChipText));

        // Right column: the carry-out side — capacity chip on top, then cards / relics (matching the warehouse carry
        // panel's chip-then-sections layout). 右栏：带出侧——顶部容量胶囊，其后卡牌/遗物（对齐仓库携带面板的胶囊+分段布局）。
        var right = new VBoxContainer();
        right.AddThemeConstantOverride("separation", 8);
        right.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        right.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        body.AddChild(right);

        right.AddChild(MakeAmountChip(out _capacityLabel, ExtractionTheme.GoldChipText));
        right.AddChild(MakeSectionHeader(ExtractionLocalization.ExtractionPointCardSectionText()));
        _rightCardGrid = MakeGrid();
        right.AddChild(MakeScroll(_rightCardGrid, 2f));

        right.AddChild(new HSeparator());
        right.AddChild(MakeSectionHeader(ExtractionLocalization.ExtractionPointRelicSectionText()));
        _rightRelicGrid = MakeGrid();
        right.AddChild(MakeScroll(_rightRelicGrid, 2f));

        var footer = new VBoxContainer();
        footer.AddThemeConstantOverride("separation", 10);
        column.AddChild(footer);

        var confirmRow = new HBoxContainer();
        confirmRow.AddThemeConstantOverride("separation", 12);
        confirmRow.Alignment = BoxContainer.AlignmentMode.End;
        footer.AddChild(confirmRow);

        var confirm = new Button { Text = ExtractionLocalization.ExtractionPointConfirmText() };
        confirm.ThemeTypeVariation = ExtractionTheme.ButtonPrimary;
        confirm.CustomMinimumSize = new Vector2(220f, 46f);
        confirm.Pressed += Confirm;
        confirmRow.AddChild(confirm);
    }

    private void Refresh()
    {
        _used = 0;
        foreach ((ModelId id, int count) in _selected)
        {
            _used += count * (_relicIds.Contains(id) ? CarryCapacity.WeightForRelic() : CarryCapacity.WeightForCard(id));
        }

        _capacityLabel.Text = ExtractionLocalization.ExtractionPointCapacityText(_used, _capacity);
        _capacityLabel.AddThemeColorOverride("font_color", _used >= _capacity ? ExtractionTheme.Danger : ExtractionTheme.GoldChipText);

        RebuildGrid(_leftCardGrid, add: true, relic: false);
        RebuildGrid(_leftRelicGrid, add: true, relic: true);
        RebuildGrid(_rightCardGrid, add: false, relic: false);
        RebuildGrid(_rightRelicGrid, add: false, relic: true);
        RebuildPotions();
        _goldLabel.Text = ExtractionLocalization.ExtractionPointGoldAllText(_me.Gold);
    }

    private void RebuildGrid(HFlowContainer grid, bool add, bool relic)
    {
        foreach (Node child in grid.GetChildren())
        {
            child.QueueFree();
        }

        var ids = new List<ModelId>();
        foreach ((ModelId id, int available) in _available)
        {
            if (_relicIds.Contains(id) != relic)
            {
                continue;
            }

            int selected = _selected.TryGetValue(id, out int s) ? s : 0;
            if (available > 0 || selected > 0)
            {
                ids.Add(id);
            }
        }

        SortIds(ids, relic);

        foreach (ModelId id in ids)
        {
            int selected = _selected.TryGetValue(id, out int s) ? s : 0;
            int available = _available.TryGetValue(id, out int a) ? a : 0;
            int shown = add ? available : selected;
            if (shown <= 0)
            {
                continue;
            }

            bool canAdd = add && available > 0 && _used + WeightOf(id, relic) <= _capacity;
            string name = relic ? ExtractionItemTiles.GetRelicTitle(id) : ExtractionItemTiles.GetCardTitle(id);
            string pool = ExtractionLocalization.PoolNameText(relic ? ExtractionItemTiles.RelicPoolSlug(id) : ExtractionItemTiles.CardPoolSlug(id));
            Texture2D? texture = ExtractArt(id, relic);

            Button tile = ExtractionItemTiles.MakeItemTile(
                name, pool, shown, texture,
                add ? ExtractionItemTiles.ItemTileAction.Add : ExtractionItemTiles.ItemTileAction.Remove,
                canAdd || !add ? () => Toggle(id, add) : null,
                id, price: null);
            if (add)
            {
                // Capacity-full source tiles grey out, like the warehouse add tile. 容量满时左侧瓦片置灰（同仓库）。
                tile.Disabled = !canAdd;
            }

            grid.AddChild(tile);
        }
    }

    /// <summary>Warehouse order per section: pool → rarity → id. 每段按仓库顺序：池 → 稀有度 → id。</summary>
    private void SortIds(List<ModelId> ids, bool relic)
    {
        if (relic)
        {
            ids.Sort(static (a, b) =>
            {
                int byPool = ExtractionItemTiles.RelicPoolIndex(a).CompareTo(ExtractionItemTiles.RelicPoolIndex(b));
                if (byPool != 0)
                {
                    return byPool;
                }

                int byRarity = ExtractionItemTiles.RelicRarityIndex(ModelDb.GetByIdOrNull<RelicModel>(a)?.Rarity ?? RelicRarity.None)
                    .CompareTo(ExtractionItemTiles.RelicRarityIndex(ModelDb.GetByIdOrNull<RelicModel>(b)?.Rarity ?? RelicRarity.None));
                if (byRarity != 0)
                {
                    return byRarity;
                }

                return string.CompareOrdinal(a.ToString(), b.ToString());
            });
        }
        else
        {
            ids.Sort(static (a, b) =>
            {
                int byPool = ExtractionItemTiles.CardPoolIndex(a).CompareTo(ExtractionItemTiles.CardPoolIndex(b));
                if (byPool != 0)
                {
                    return byPool;
                }

                int byRarity = ExtractionItemTiles.CardRarityIndex(ModelDb.GetByIdOrNull<CardModel>(a)?.Rarity ?? CardRarity.None)
                    .CompareTo(ExtractionItemTiles.CardRarityIndex(ModelDb.GetByIdOrNull<CardModel>(b)?.Rarity ?? CardRarity.None));
                if (byRarity != 0)
                {
                    return byRarity;
                }

                return string.CompareOrdinal(a.ToString(), b.ToString());
            });
        }
    }

    private void RebuildPotions()
    {
        foreach (Node child in _leftPotionGrid.GetChildren())
        {
            child.QueueFree();
        }

        var potions = new Dictionary<ModelId, int>();
        foreach (PotionModel? p in _me.PotionSlots)
        {
            if (p?.Id is ModelId id)
            {
                potions[id] = potions.GetValueOrDefault(id) + 1;
            }
        }

        var ids = new List<ModelId>(potions.Keys);
        ids.Sort(static (a, b) =>
        {
            int byPool = ExtractionItemTiles.PotionPoolIndex(a).CompareTo(ExtractionItemTiles.PotionPoolIndex(b));
            if (byPool != 0)
            {
                return byPool;
            }

            int byRarity = ExtractionItemTiles.PotionRarityIndex(ModelDb.GetByIdOrNull<PotionModel>(a)?.Rarity ?? PotionRarity.None)
                .CompareTo(ExtractionItemTiles.PotionRarityIndex(ModelDb.GetByIdOrNull<PotionModel>(b)?.Rarity ?? PotionRarity.None));
            if (byRarity != 0)
            {
                return byRarity;
            }

            return string.CompareOrdinal(a.ToString(), b.ToString());
        });

        int total = 0;
        foreach (ModelId id in ids)
        {
            int count = potions[id];
            total += count;
            string name = ExtractionItemTiles.GetPotionTitle(id);
            string pool = ExtractionLocalization.PoolNameText(ExtractionItemTiles.PotionPoolSlug(id));
            Texture2D? texture = ExtractPotionArt(id);
            // Read-only display tiles — potions are always carried out in full. 只读展示瓦片——药水全部自动带出。
            Button tile = ExtractionItemTiles.MakeItemTile(name, pool, count, texture,
                ExtractionItemTiles.ItemTileAction.Display, null, id, price: null);
            _leftPotionGrid.AddChild(tile);
        }

        _potionsDetail.Text = ExtractionLocalization.ExtractionPointPotionsAllText(total);
    }

    private int WeightOf(ModelId id, bool relic) => relic ? CarryCapacity.WeightForRelic() : CarryCapacity.WeightForCard(id);

    private void Toggle(ModelId id, bool add)
    {
        bool relic = _relicIds.Contains(id);
        if (add)
        {
            if (_used + WeightOf(id, relic) > _capacity)
            {
                return;
            }

            _selected.TryGetValue(id, out int s);
            _selected[id] = s + 1;
            _available[id] = Math.Max(0, (_available.TryGetValue(id, out int a) ? a : 1) - 1);
        }
        else
        {
            if (!_selected.TryGetValue(id, out int s) || s <= 0)
            {
                return;
            }

            if (s == 1)
            {
                _selected.Remove(id);
            }
            else
            {
                _selected[id] = s - 1;
            }

            _available.TryGetValue(id, out int a);
            _available[id] = a + 1;
        }

        Refresh();
    }

    private Texture2D? ExtractArt(ModelId id, bool relic)
    {
        try
        {
            return relic
                ? ModelDb.GetByIdOrNull<RelicModel>(id)?.Icon
                : ModelDb.GetByIdOrNull<CardModel>(id)?.Portrait;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private Texture2D? ExtractPotionArt(ModelId id)
    {
        try
        {
            return ModelDb.GetByIdOrNull<PotionModel>(id)?.Image;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void Confirm()
    {
        if (_selected.Count == 0)
        {
            // No cards/relics selected — only potions + gold would carry out; confirm before the run ends for real.
            // 未选任何牌/遗物——仅药水金币带出；真正结束跑局前先二次确认。
            _dialog = new ExtractionConfirmDialog(
                ExtractionLocalization.ExtractionPointEmptyHeaderText(),
                ExtractionLocalization.ExtractionPointEmptyBodyText(),
                FinishConfirm);
            if (NGame.Instance is NGame game)
            {
                game.AddChild(_dialog);
            }
            else
            {
                GetTree().Root.AddChild(_dialog);
            }

            return;
        }

        FinishConfirm();
    }

    private void FinishConfirm()
    {
        var selection = new ExtractionPointSelection { Kind = ExtractionPointKind.Normal };
        foreach ((ModelId id, int count) in _selected)
        {
            if (_relicIds.Contains(id))
            {
                selection.Relics[id] = count;
            }
            else
            {
                selection.Cards[id] = count;
            }
        }

        QueueFree();
        _tcs.TrySetResult(selection);
    }

    // ----- Layout helpers 布局辅助 -----

    private static HBoxContainer MakeSectionHeader(string title)
    {
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 10);

        var titleLabel = new Label { Text = title };
        titleLabel.AddThemeFontOverride("font", ExtractionTheme.Bold);
        titleLabel.AddThemeFontSizeOverride("font_size", ExtractionTheme.FontSizeBody);
        header.AddChild(titleLabel);
        return header;
    }

    private static HBoxContainer MakeSectionHeaderWithDetail(string title, out Label detail)
    {
        HBoxContainer header = MakeSectionHeader(title);
        header.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });

        detail = new Label();
        detail.AddThemeColorOverride("font_color", ExtractionTheme.TextSecondary);
        detail.AddThemeFontSizeOverride("font_size", ExtractionTheme.FontSizeSmall);
        header.AddChild(detail);
        return header;
    }

    private static PanelContainer MakeAmountChip(out Label label, Color textColor)
    {
        var chip = new PanelContainer();
        chip.AddThemeStyleboxOverride("panel", ExtractionTheme.ChipBox());

        label = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.AddThemeColorOverride("font_color", textColor);
        label.AddThemeFontSizeOverride("font_size", ExtractionTheme.FontSizeBody);
        chip.AddChild(label);
        return chip;
    }

    private static HFlowContainer MakeGrid()
    {
        var grid = new HFlowContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        grid.AddThemeConstantOverride("separation", 8);
        return grid;
    }

    private static ScrollContainer MakeScroll(Control child, float stretchRatio)
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
}
