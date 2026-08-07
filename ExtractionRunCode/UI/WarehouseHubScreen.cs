using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using ExtractionRun.Data;
using ExtractionRun.Lifecycle;
using ExtractionRun.Settings;

namespace ExtractionRun.UI;

/// <summary>
/// The 搜打撤 warehouse hub: a full-screen overlay opened from the main menu. Shows the persistent warehouse
/// (cards / relics / potions / gold), lets the player pick a carry config (deck ≤ MaxCarryCards, relics ≤
/// MaxCarryRelics, potions ≤ slots, gold), seeds the warehouse on first use, and launches the run.
/// Built as a modern flat card layout on top of <see cref="ExtractionTheme"/>: two floating dark cards on a dark
/// background, adaptive via MarginContainer + VBox/HBox. Warehouse and carry items render as card-form tiles
/// (art, name, source pool, quantity) in a wrapping <see cref="HFlowContainer"/> grid.
/// 搜打撤仓库大厅：主菜单打开的全屏覆盖层。展示仓库、编辑携带配置、首次种子、发起跑局。扁平深色卡片式布局，
/// 物品以卡片形式（贴图、名称、来源池、数量）在 HFlowContainer 网格中展示。
/// </summary>
public sealed partial class WarehouseHubScreen : CanvasLayer
{
    private const int GoldStep = 50;

    private readonly NSubmenuStack _stack;
    private readonly Control? _loadingOverlay;
    private readonly bool _isMultiplayerHost;
    private readonly WarehouseData _warehouse;
    private readonly CarryConfig _carry;

    // Rebuilt on every refresh; keep references to the flow containers only.
    private HFlowContainer _cardList = null!;
    private HFlowContainer _relicList = null!;
    private HFlowContainer _potionList = null!;
    private HFlowContainer _carryCardList = null!;
    private HFlowContainer _carryRelicList = null!;
    private HFlowContainer _carryPotionList = null!;
    private Label _goldChipLabel = null!;
    private Label _carryDeckLabel = null!;
    private Label _carryRelicsLabel = null!;
    private Label _carryPotionsLabel = null!;
    private Label _goldValueLabel = null!;
    private Button _startButton = null!;
    private Label _startHintLabel = null!;
    private int _carryGold;

    public WarehouseHubScreen(NSubmenuStack stack, Control? loadingOverlay, bool isMultiplayerHost)
    {
        _stack = stack;
        _loadingOverlay = loadingOverlay;
        _isMultiplayerHost = isMultiplayerHost;
        Layer = 100;

        // Seed on first open (idempotent) and load the live warehouse + pending carry.
        WarehouseStore.EnsureSeeded();
        _warehouse = WarehouseStore.Current;
        _carry = PendingCarryStore.Current;
        _carryGold = _carry.Gold;
    }

    public override void _Ready()
    {
        BuildUi();
        Refresh();
    }

    private void BuildUi()
    {
        // Root surface: the hub background, themed for every descendant.
        var root = new Panel { Name = "HubPanel" };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddThemeStyleboxOverride("panel", ExtractionTheme.BackgroundBox());
        root.Theme = ExtractionTheme.Instance;
        AddChild(root);

        // Page gutters: generous negative space (16-24px per the design spec).
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
        back.Pressed += CloseHub;
        header.AddChild(back);

        return header;
    }

    // ----- Warehouse card (left, wider) -----

    private Control BuildWarehouseCard()
    {
        PanelContainer card = MakeCard(stretchRatio: 3f, out VBoxContainer body);

        body.AddChild(MakeSectionHeader(ExtractionLocalization.SectionCardsText()));
        _cardList = MakeList();
        body.AddChild(Scroll(_cardList, stretchRatio: 2f));

        body.AddChild(new HSeparator());

        body.AddChild(MakeSectionHeader(ExtractionLocalization.SectionRelicsText()));
        _relicList = MakeList();
        body.AddChild(Scroll(_relicList, stretchRatio: 2f));

        body.AddChild(new HSeparator());

        body.AddChild(MakeSectionHeader(ExtractionLocalization.SectionPotionsText()));
        _potionList = MakeList();
        body.AddChild(Scroll(_potionList, stretchRatio: 2f));

        return card;
    }

    // ----- Carry card (right, narrower) -----

    private Control BuildCarryCard()
    {
        PanelContainer card = MakeCard(stretchRatio: 2f, out VBoxContainer body);

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

        _goldValueLabel = MakeLabel("");
        _goldValueLabel.CustomMinimumSize = new Vector2(96f, 42f);
        _goldValueLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _goldValueLabel.VerticalAlignment = VerticalAlignment.Center;
        _goldValueLabel.AddThemeFontOverride("font", ExtractionTheme.Bold);
        _goldValueLabel.AddThemeFontSizeOverride("font_size", 18);
        row.AddChild(_goldValueLabel);

        var plus = MakeButton("+", ExtractionTheme.ButtonSecondary);
        plus.CustomMinimumSize = new Vector2(44f, 42f);
        plus.AddThemeFontSizeOverride("font_size", 20);
        plus.Pressed += () => ChangeCarryGold(GoldStep);
        row.AddChild(plus);

        return row;
    }

    // ----- Footer: primary start action -----

    private Control BuildFooter()
    {
        var footer = new VBoxContainer();
        footer.AddThemeConstantOverride("separation", 6);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 16);
        row.Alignment = BoxContainer.AlignmentMode.Center;

        var start = MakeButton(ExtractionLocalization.ButtonStartText(), ExtractionTheme.ButtonPrimary);
        start.CustomMinimumSize = new Vector2(320f, 54f);
        start.Pressed += StartRun;
        _startButton = start;
        row.AddChild(start);

        footer.AddChild(row);

        // Hint shown while the carry is empty: the deck-clearing modifier would otherwise start a dead 0-card run.
        // 携带为空时禁用开始按钮并提示，防止空牌组开跑。
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
        RefreshGoldLabel();
    }

    private void RefreshGoldLabel()
    {
        _goldValueLabel.Text = _carryGold.ToString();
    }

    /// <summary>Rebuilds every grid from the current warehouse + carry state. 从当前仓库与携带状态重建所有网格。</summary>
    private void Refresh()
    {
        ClearChildren(_cardList);
        ClearChildren(_relicList);
        ClearChildren(_potionList);
        ClearChildren(_carryCardList);
        ClearChildren(_carryRelicList);
        ClearChildren(_carryPotionList);

        // Live preview: the warehouse side shows what is still AVAILABLE after the items staged into the carry, so
        // taking an item visibly decrements the warehouse count. Nothing is persisted until the run actually starts
        // (ExtractionModifier.AfterRunCreated -> ConsumeCarried), so closing the hub without running loses nothing.
        // 实时预览：仓库侧显示「扣除已携带后的可用数量」，取走物品时数量实时减少；真正扣减在开跑时由 ConsumeCarried 落盘。
        int availableGold = Math.Max(0, _warehouse.Gold - _carryGold);
        _goldChipLabel.Text = ExtractionLocalization.GoldWarehouseText(availableGold);
        RefreshGoldLabel();

        int maxCards = Math.Max(0, ExtractionSettingsPage.Current.MaxCarryCards);
        int maxRelics = Math.Max(0, ExtractionSettingsPage.Current.MaxCarryRelics);
        _carryDeckLabel.Text = ExtractionLocalization.LimitCardsText(_carry.Cards.Count, maxCards);
        _carryRelicsLabel.Text = ExtractionLocalization.LimitRelicsText(_carry.Relics.Count, maxRelics);
        _carryPotionsLabel.Text = ExtractionLocalization.LimitPotionsText(_carry.Potions.Count, 3);

        // Empty carry cannot start: ClearsPlayerDeck would give a dead 0-card deck. 空携带不可开跑。
        bool canStart = _carry.Cards.Count > 0;
        _startButton.Disabled = !canStart;
        _startHintLabel.Visible = !canStart;

        // Carried counts by item key, used to compute the available warehouse counts.
        // 按物品键统计已携带数量，用于计算仓库可用数。
        var carriedCards = new Dictionary<string, int>();
        foreach (var c in ExtractionItemTiles.GroupCards(_carry.Cards))
        {
            carriedCards[ExtractionItemTiles.CardKey(c)] = c.Count;
        }

        var carriedRelics = new Dictionary<string, int>();
        foreach (var r in ExtractionItemTiles.GroupRelics(_carry.Relics))
        {
            carriedRelics[ExtractionItemTiles.RelicKey(r)] = r.Count;
        }

        var carriedPotions = new Dictionary<string, int>();
        foreach (var p in ExtractionItemTiles.GroupPotions(_carry.Potions))
        {
            carriedPotions[ExtractionItemTiles.PotionKey(p)] = p.Count;
        }

        if (_warehouse.Cards.Count == 0)
        {
            AddEmptyState(_cardList, ExtractionLocalization.EmptyWarehouseText());
        }
        else
        {
            foreach (var g in ExtractionItemTiles.GroupCards(_warehouse.Cards))
            {
                int available = g.Count - carriedCards.GetValueOrDefault(ExtractionItemTiles.CardKey(g));
                if (available <= 0)
                {
                    continue; // Every copy is already staged to carry.
                }

                _cardList.AddChild(ExtractionItemTiles.MakeItemTile(g.Name, g.Pool, available, g.Texture,
                    ExtractionItemTiles.ItemTileAction.Add, () => AddToCarryCards(g.Rep)));
            }
        }

        if (_warehouse.Relics.Count == 0)
        {
            AddEmptyState(_relicList, ExtractionLocalization.EmptyWarehouseText());
        }
        else
        {
            foreach (var g in ExtractionItemTiles.GroupRelics(_warehouse.Relics))
            {
                int available = g.Count - carriedRelics.GetValueOrDefault(ExtractionItemTiles.RelicKey(g));
                if (available <= 0)
                {
                    continue;
                }

                _relicList.AddChild(ExtractionItemTiles.MakeItemTile(g.Name, g.Pool, available, g.Texture,
                    ExtractionItemTiles.ItemTileAction.Add, () => AddToCarryRelics(g.Rep)));
            }
        }

        if (_warehouse.Potions.Count == 0)
        {
            AddEmptyState(_potionList, ExtractionLocalization.EmptyWarehouseText());
        }
        else
        {
            foreach (var g in ExtractionItemTiles.GroupPotions(_warehouse.Potions))
            {
                int available = g.Count - carriedPotions.GetValueOrDefault(ExtractionItemTiles.PotionKey(g));
                if (available <= 0)
                {
                    continue;
                }

                _potionList.AddChild(ExtractionItemTiles.MakeItemTile(g.Name, g.Pool, available, g.Texture,
                    ExtractionItemTiles.ItemTileAction.Add, () => AddToCarryPotions(g.Rep)));
            }
        }

        if (_carry.Cards.Count == 0)
        {
            AddEmptyState(_carryCardList, ExtractionLocalization.EmptyCarryText());
        }
        else
        {
            foreach (var g in ExtractionItemTiles.GroupCards(_carry.Cards))
            {
                _carryCardList.AddChild(ExtractionItemTiles.MakeItemTile(g.Name, g.Pool, g.Count, g.Texture,
                    ExtractionItemTiles.ItemTileAction.Remove, () =>
                    {
                        _carry.Cards.Remove(g.Rep);
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
            foreach (var g in ExtractionItemTiles.GroupRelics(_carry.Relics))
            {
                _carryRelicList.AddChild(ExtractionItemTiles.MakeItemTile(g.Name, g.Pool, g.Count, g.Texture,
                    ExtractionItemTiles.ItemTileAction.Remove, () =>
                    {
                        _carry.Relics.Remove(g.Rep);
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
            foreach (var g in ExtractionItemTiles.GroupPotions(_carry.Potions))
            {
                _carryPotionList.AddChild(ExtractionItemTiles.MakeItemTile(g.Name, g.Pool, g.Count, g.Texture,
                    ExtractionItemTiles.ItemTileAction.Remove, () =>
                    {
                        _carry.Potions.Remove(g.Rep);
                        Refresh();
                    }));
            }
        }
    }

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

    // Item grouping + tile rendering now live in ExtractionItemTiles (shared with the settlement screen).
    // 物品分组与卡片渲染已抽到 ExtractionItemTiles（与结算界面共用）。

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

    private static string GetCardTitle(ModelId? id)
    {
        CardModel? card = id == null ? null : ModelDb.GetByIdOrNull<CardModel>(id);
        return card?.Title ?? id?.ToString() ?? "?";
    }

    private static string GetRelicTitle(ModelId? id)
    {
        RelicModel? relic = id == null ? null : ModelDb.GetByIdOrNull<RelicModel>(id);
        return relic?.Title.GetFormattedText() ?? id?.ToString() ?? "?";
    }

    private static string GetPotionTitle(ModelId? id)
    {
        PotionModel? potion = id == null ? null : ModelDb.GetByIdOrNull<PotionModel>(id);
        return potion?.Title.GetFormattedText() ?? id?.ToString() ?? "?";
    }

    private void StartRun()
    {
        // Defense-in-depth: the button is disabled when the carry is empty, but never launch a 0-card run.
        // 兜底校验：按钮已在空携带时禁用，但绝不允许 0 牌组开跑。
        if (_carry.Cards.Count == 0)
        {
            Entry.Logger.Info("WarehouseHub: blocked empty-carry start (carry at least one card).");
            return;
        }

        _carry.Gold = _carryGold;
        PendingCarryStore.Set(_carry);
        Entry.Logger.Info($"WarehouseHub: starting extraction run with {_carry.Cards.Count} cards, " +
                          $"{_carry.Relics.Count} relics, {_carry.Potions.Count} potions, {_carry.Gold} gold.");

        ExtractionRunContext.IsExtractionLaunch = true;
        CloseHub();

        // Launch the run through the existing character-select flow; CharacterSelectPatch applies the modifier and
        // stages the pending carry into the lobby.
        if (_isMultiplayerHost)
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

    private void CloseHub()
    {
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

    /// <summary>A wrapping grid of item tiles. 物品卡片自动换行网格。</summary>
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
