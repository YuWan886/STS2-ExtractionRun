using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using ExtractionRun.Data;

namespace ExtractionRun.UI;

/// <summary>
/// The gear-code import dialog: paste/type a code, see the shared loadout (as requested) plus what is actually
/// importable from the receiving warehouse, then apply or cancel. Live-validates on every keystroke — a bad code shows
/// the reason inline instead of opening a second dialog; a code whose clamp result is empty (or would leave the carry
/// card-less while cards are possible) disables Apply. Applying replaces the hub's carry draft via the callback — it
/// never writes the pending store, so closing without applying leaks nothing (matching the hub's detached-draft rule).
/// 战备码导入弹窗：粘贴/输入码，实时展示分享的携带配置（原样）与接收者仓库实际可导入的部分，然后应用或取消。每次按键实时
/// 校验——坏码就地报错不开第二个弹窗；收敛结果为空（或导入后无卡而仓库有卡可带）时禁用「应用」。应用通过回调替换仓库大厅
/// 的携带草稿，绝不写 pending store，不应用就关闭不会泄漏任何东西（与大厅草稿隔离规则一致）。
/// </summary>
public sealed partial class CarryCodeImportDialog : CanvasLayer
{
    private static readonly Color OverlayTint = new(0f, 0f, 0f, 0.6f);

    private readonly WarehouseData _warehouse;
    private readonly Action<CarryConfig> _onApply;

    // ----- Controls -----
    private LineEdit _input = null!;
    private Label _errorLabel = null!;
    private Label _missingModsLabel = null!;
    private Label _unrecognizedLabel = null!;
    private Label _importableLabel = null!;
    private Label _missingLabel = null!;
    private Label _goldClampedLabel = null!;
    private Label _capacityClampedLabel = null!;
    private Label _previewEmptyLabel = null!;
    private HFlowContainer _preview = null!;
    private Button _applyButton = null!;

    private CarryCodeImport.Result? _result;

    public CarryCodeImportDialog(WarehouseData warehouse, Action<CarryConfig> onApply)
    {
        _warehouse = warehouse;
        _onApply = onApply;
        Layer = 200;
    }

    public override void _Ready()
    {
        BuildUi();
        _input.CallDeferred(Control.MethodName.GrabFocus);
        Reparse();
    }

    public override void _Input(InputEvent inputEvent)
    {
        if (inputEvent is InputEventKey { Pressed: true, Keycode: Key.Escape } key && !key.IsEcho())
        {
            QueueFree();
            GetViewport().SetInputAsHandled();
        }
    }

    // ----- Build 构建 -----

    private void BuildUi()
    {
        var root = new Panel { Name = "CodeImportOverlay" };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddThemeStyleboxOverride("panel", OverlayBox());
        AddChild(root);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(center);

        var card = new PanelContainer { CustomMinimumSize = new Vector2(880f, 620f) };
        card.AddThemeStyleboxOverride("panel", ExtractionTheme.CardBox());
        card.Theme = ExtractionTheme.Instance;
        center.AddChild(card);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 26);
        margin.AddThemeConstantOverride("margin_right", 26);
        margin.AddThemeConstantOverride("margin_top", 22);
        margin.AddThemeConstantOverride("margin_bottom", 22);
        card.AddChild(margin);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 12);
        margin.AddChild(box);

        box.AddChild(BuildHeader());
        box.AddChild(BuildInputRow());
        box.AddChild(BuildStatusArea());
        box.AddChild(new HSeparator());
        box.AddChild(BuildPreviewHeader());
        box.AddChild(BuildPreviewScroll());
        box.AddChild(BuildFooter());
    }

    private Control BuildHeader()
    {
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 16);

        var title = new Label { Text = ExtractionLocalization.CodeTitleText() };
        title.AddThemeFontOverride("font", ExtractionTheme.Bold);
        title.AddThemeFontSizeOverride("font_size", ExtractionTheme.FontSizeTitle);
        header.AddChild(title);

        header.AddChild(MakeSpacer());

        var close = new Button
        {
            Text = "×",
            ThemeTypeVariation = ExtractionTheme.ButtonSecondary,
            CustomMinimumSize = new Vector2(44f, 44f),
        };
        close.AddThemeFontSizeOverride("font_size", 20);
        close.Pressed += QueueFree;
        header.AddChild(close);

        return header;
    }

    private Control BuildInputRow()
    {
        _input = new LineEdit
        {
            PlaceholderText = ExtractionLocalization.CodeInputPlaceholderText(),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0f, 44f),
        };
        _input.TextChanged += _ => Reparse();
        return _input;
    }

    /// <summary>Status lines under the input: parse error, missing mods, unrecognized ids, importable summary, gold clamp.
    /// All start hidden and toggle on reparse. 输入下方的状态行，默认隐藏，重解析时按需显示。</summary>
    private Control BuildStatusArea()
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 4);

        _errorLabel = MakeStatusLabel(ExtractionTheme.Danger);
        _missingModsLabel = MakeStatusLabel(ExtractionTheme.Danger);
        _unrecognizedLabel = MakeStatusLabel(ExtractionTheme.TextSecondary);
        _importableLabel = MakeStatusLabel(ExtractionTheme.TextSecondary);
        _missingLabel = MakeStatusLabel(ExtractionTheme.Danger);
        _goldClampedLabel = MakeStatusLabel(ExtractionTheme.GoldChipText);
        _capacityClampedLabel = MakeStatusLabel(ExtractionTheme.GoldChipText);

        box.AddChild(_errorLabel);
        box.AddChild(_missingModsLabel);
        box.AddChild(_unrecognizedLabel);
        box.AddChild(_importableLabel);
        box.AddChild(_missingLabel);
        box.AddChild(_goldClampedLabel);
        box.AddChild(_capacityClampedLabel);
        return box;
    }

    private Label MakeStatusLabel(Color color)
    {
        var label = new Label
        {
            Visible = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeFontSizeOverride("font_size", ExtractionTheme.FontSizeSmall);
        return label;
    }

    private Control BuildPreviewHeader()
    {
        var label = new Label { Text = ExtractionLocalization.CodePreviewText() };
        label.AddThemeFontOverride("font", ExtractionTheme.Bold);
        label.AddThemeFontSizeOverride("font_size", ExtractionTheme.FontSizeSection);
        return label;
    }

    private Control BuildPreviewScroll()
    {
        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        box.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.AddChild(box);

        _preview = new HFlowContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        _preview.AddThemeConstantOverride("separation", 8);
        box.AddChild(_preview);

        _previewEmptyLabel = new Label
        {
            Text = ExtractionLocalization.CodePreviewEmptyText(),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0f, 96f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        _previewEmptyLabel.AddThemeColorOverride("font_color", ExtractionTheme.TextSecondary);
        _previewEmptyLabel.AddThemeFontSizeOverride("font_size", ExtractionTheme.FontSizeSmall);
        box.AddChild(_previewEmptyLabel);

        return scroll;
    }

    private Control BuildFooter()
    {
        var footer = new HBoxContainer();
        footer.AddThemeConstantOverride("separation", 12);
        footer.Alignment = BoxContainer.AlignmentMode.Center;

        var cancel = new Button
        {
            Text = ExtractionLocalization.CancelButtonText(),
            ThemeTypeVariation = ExtractionTheme.ButtonSecondary,
            CustomMinimumSize = new Vector2(140f, 48f),
        };
        cancel.Pressed += QueueFree;
        footer.AddChild(cancel);

        _applyButton = new Button
        {
            Text = ExtractionLocalization.CodeApplyText(),
            ThemeTypeVariation = ExtractionTheme.ButtonPrimary,
            CustomMinimumSize = new Vector2(140f, 48f),
        };
        _applyButton.Pressed += Apply;
        footer.AddChild(_applyButton);

        return footer;
    }

    // ----- Reparse 重解析 -----

    private void Reparse()
    {
        HideStatus();
        ClearChildren(_preview);
        _previewEmptyLabel.Visible = false;
        _result = null;
        _applyButton.Disabled = true;

        string input = _input.Text;
        if (string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        if (!CarryCodec.TryDecode(input, out CarryCodec.DecodedCarry decoded, out CarryCodec.DecodeError error))
        {
            ShowError(error);
            return;
        }

        CarryBudget budget = CarryBudget.FromSettings();
        CarryCodeImport.Result result = CarryCodeImport.Apply(decoded, _warehouse, budget);
        _result = result;

        RenderPreview(decoded);
        UpdateStatus(result);

        bool canProceed = result.Applied.Cards.Count > 0 || !CanCarryAnyCards(budget);
        bool applyable = !result.Applied.IsEmpty && canProceed;
        _applyButton.Disabled = !applyable;
        if (!applyable)
        {
            _errorLabel.Text = result.Applied.IsEmpty
                ? ExtractionLocalization.CodeNoneImportableText()
                : ExtractionLocalization.NeedCardHintText();
            _errorLabel.Visible = true;
        }
    }

    private bool CanCarryAnyCards(CarryBudget budget)
    {
        if (_warehouse.Cards.Count == 0)
        {
            return false;
        }

        if (budget.UsesCapacity)
        {
            if (budget.Capacity <= 0)
            {
                return false;
            }

            int cheapest = _warehouse.Cards
                .Select(c => CarryCapacity.WeightForCard(c.Card.Id))
                .Where(w => w > 0)
                .DefaultIfEmpty(int.MaxValue)
                .Min();
            return cheapest <= budget.Capacity;
        }

        return budget.MaxCards > 0;
    }

    private void RenderPreview(CarryCodec.DecodedCarry decoded)
    {
        if (decoded.Items.Count == 0)
        {
            _previewEmptyLabel.Visible = true;
            return;
        }

        foreach (CarryCodec.CodeItem item in decoded.Items)
        {
            (string name, string pool, Texture2D? texture, ModelId? id) = PreviewFor(item);
            _preview.AddChild(ExtractionItemTiles.MakeItemTile(name, pool, item.Count, texture,
                ExtractionItemTiles.ItemTileAction.Display, null, id));
        }
    }

    /// <summary>Resolves a code item's display (name / pool / art / tooltip id) for the shared-loadout preview. Items
    /// whose owner mod is missing render as raw id + missing-mod note (no tooltip id — there is no model to tip);
    /// known-but-unresolvable ids render as unrecognized.
    /// 解析码中物品的展示信息（名称/池/贴图/提示 id）。缺 mod 的物品以原始 id + 缺 mod 提示渲染（无提示 id——没有模型可提示）；
    /// 解析不到的按无法识别渲染。</summary>
    private (string Name, string Pool, Texture2D? Texture, ModelId? Id) PreviewFor(CarryCodec.CodeItem item)
    {
        if (item.OwnerStem != null && !CarryCodeOwner.IsModLoaded(item.OwnerStem))
        {
            return (item.Entry,
                ExtractionLocalization.CodeMissingModsText(CarryCodeOwner.ResolveModDisplayName(item.OwnerStem)), null,
                null);
        }

        if (!CarryCodeImport.TryResolveKind(item.Entry, out CarryCodec.ItemKind kind, out ModelId id))
        {
            return (item.Entry, ExtractionLocalization.CodeUnrecognizedText(), null, null);
        }

        try
        {
            switch (kind)
            {
                case CarryCodec.ItemKind.Card:
                {
                    CardModel? card = ModelDb.GetByIdOrNull<CardModel>(id);
                    return card == null
                        ? (item.Entry, ExtractionLocalization.CodeUnrecognizedText(), null, null)
                        : (card.Title ?? item.Entry,
                            ExtractionLocalization.PoolNameText(ExtractionItemTiles.CardPoolSlug(id)),
                            SafeTexture(() => card.Portrait), id);
                }
                case CarryCodec.ItemKind.Relic:
                {
                    RelicModel? relic = ModelDb.GetByIdOrNull<RelicModel>(id);
                    return relic == null
                        ? (item.Entry, ExtractionLocalization.CodeUnrecognizedText(), null, null)
                        : (relic.Title.GetFormattedText(),
                            ExtractionLocalization.PoolNameText(ExtractionItemTiles.RelicPoolSlug(id)),
                            SafeTexture(() => relic.Icon), id);
                }
                default:
                {
                    PotionModel? potion = ModelDb.GetByIdOrNull<PotionModel>(id);
                    return potion == null
                        ? (item.Entry, ExtractionLocalization.CodeUnrecognizedText(), null, null)
                        : (potion.Title.GetFormattedText(),
                            ExtractionLocalization.PoolNameText(ExtractionItemTiles.PotionPoolSlug(id)),
                            SafeTexture(() => potion.Image), id);
                }
            }
        }
        catch (Exception)
        {
            return (item.Entry, ExtractionLocalization.CodeUnrecognizedText(), null, null);
        }
    }

    private void UpdateStatus(CarryCodeImport.Result result)
    {
        if (result.MissingModStems.Count > 0)
        {
            string names = string.Join(", ", result.MissingModStems.Select(CarryCodeOwner.ResolveModDisplayName));
            _missingModsLabel.Text = ExtractionLocalization.CodeMissingModsText(names);
            _missingModsLabel.Visible = true;
        }

        if (result.Unrecognized.Count > 0)
        {
            string entries = string.Join(", ", result.Unrecognized.Select(i => i.Entry));
            _unrecognizedLabel.Text = ExtractionLocalization.CodeUnrecognizedListText(entries);
            _unrecognizedLabel.Visible = true;
        }

        _importableLabel.Text = ExtractionLocalization.CodeImportableText(
            result.Applied.Cards.Count, result.Applied.Relics.Count, result.Applied.Potions.Count, result.Applied.Gold);
        _importableLabel.Visible = true;

        if (result.MissingCount > 0)
        {
            _missingLabel.Text = ExtractionLocalization.CodeMissingText(result.MissingCount);
            _missingLabel.Visible = true;
        }

        if (result.GoldClamped)
        {
            _goldClampedLabel.Text = ExtractionLocalization.CodeGoldClampedText(result.Applied.Gold, _warehouse.Gold);
            _goldClampedLabel.Visible = true;
        }

        if (result.CapacityShortfall > 0)
        {
            _capacityClampedLabel.Text = ExtractionLocalization.CodeCapacityClampedText(result.CapacityShortfall);
            _capacityClampedLabel.Visible = true;
        }
    }

    private void ShowError(CarryCodec.DecodeError error)
    {
        _errorLabel.Text = ExtractionLocalization.CodeErrorText(error);
        _errorLabel.Visible = true;
    }

    private void HideStatus()
    {
        _errorLabel.Visible = false;
        _missingModsLabel.Visible = false;
        _unrecognizedLabel.Visible = false;
        _importableLabel.Visible = false;
        _missingLabel.Visible = false;
        _goldClampedLabel.Visible = false;
        _capacityClampedLabel.Visible = false;
    }

    // ----- Apply 应用 -----

    private void Apply()
    {
        if (_result == null || _applyButton.Disabled)
        {
            return;
        }

        try
        {
            _onApply(_result.Applied);
        }
        catch (Exception e)
        {
            Entry.Logger.Error($"CarryCodeImportDialog: apply failed: {e}");
        }

        QueueFree();
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

    private static StyleBoxFlat OverlayBox() => new() { BgColor = OverlayTint };

    private static void ClearChildren(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            child.QueueFree();
        }
    }

    private static Control MakeSpacer()
    {
        return new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
    }
}
