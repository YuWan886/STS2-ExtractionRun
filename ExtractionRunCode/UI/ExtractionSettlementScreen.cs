using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ExtractionRun.Lifecycle;

namespace ExtractionRun.UI;

/// <summary>
/// Post-run settlement screen: shows the extraction result after the vanilla game-over summary page. Success lists the
/// loot deposited into the warehouse (final deck / relics / potions / gold); failure lists the carried loadout that
/// was lost. Read-only card-form tiles, dark themed like the warehouse hub. 跑局结算界面：成功列出存入仓库的战利品，
/// 失败列出损失的携带装备；只读卡片形式，深色主题。
/// </summary>
public sealed partial class ExtractionSettlementScreen : CanvasLayer
{
    private readonly ExtractionSettlementResult _result;

    public ExtractionSettlementScreen(ExtractionSettlementResult result)
    {
        _result = result;
        Layer = 100;
    }

    public override void _Ready()
    {
        BuildUi();
    }

    private void BuildUi()
    {
        var root = new Panel { Name = "SettlementPanel" };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddThemeStyleboxOverride("panel", ExtractionTheme.BackgroundBox());
        root.Theme = ExtractionTheme.Instance;
        AddChild(root);

        // Center the content column horizontally (max ~1400px, mirroring the vanilla game-over summary's gutters).
        // Vertical stays full-height: the body is a scroll container, so content is top-aligned. The column shrinks on
        // narrow viewports so it never clips. 内容列水平居中（约 1400px）；垂直撑满、正文滚动，窄屏自动收窄。
        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        center.MouseFilter = Control.MouseFilterEnum.Ignore;
        root.AddChild(center);

        float columnWidth = Math.Min(1400f, GetViewport().GetVisibleRect().Size.X - 96f);
        var page = new MarginContainer
        {
            CustomMinimumSize = new Vector2(columnWidth, 0f),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        page.AddThemeConstantOverride("margin_left", 24);
        page.AddThemeConstantOverride("margin_right", 24);
        page.AddThemeConstantOverride("margin_top", 28);
        page.AddThemeConstantOverride("margin_bottom", 28);
        center.AddChild(page);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 12);
        page.AddChild(vbox);

        vbox.AddChild(BuildHeader());
        vbox.AddChild(BuildLede());
        vbox.AddChild(BuildBody());
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
            var flow = new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            flow.AddThemeConstantOverride("separation", 8);
            foreach ((string name, string pool, int count, Texture2D? texture) in list)
            {
                flow.AddChild(ExtractionItemTiles.MakeItemTile(name, pool, count, texture,
                    ExtractionItemTiles.ItemTileAction.Display, null));
            }

            box.AddChild(flow);
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

    private static Control MakeSpacer()
    {
        return new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
    }

    private void Close()
    {
        QueueFree();
    }
}
