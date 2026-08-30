using Godot;

namespace ExtractionRun.UI;

/// <summary>
/// Three-choice import confirmation (覆盖 / 合并 / 取消) rendered like <see cref="ExtractionConfirmDialog"/>: its own high
/// CanvasLayer so it draws above the settings overlay, a full-rect input trap, and Cancel focused by default so Enter /
/// space / Esc never fire a destructive action.
/// 三选一导入确认弹窗（覆盖 / 合并 / 取消），样式同 ExtractionConfirmDialog：独立高层 CanvasLayer（盖在设置覆盖层之上）、
/// 整屏输入拦截、默认聚焦「取消」（Enter/空格/Esc 不触发破坏性动作）。
/// </summary>
public sealed partial class ExtractionImportDialog : CanvasLayer
{
    private static readonly Color OverlayTint = new(0f, 0f, 0f, 0.55f);

    private readonly string _title;
    private readonly string _body;
    private readonly Action _onOverwrite;
    private readonly Action _onMerge;
    private readonly Action? _onCancel;

    private Button _cancelButton = null!;

    public ExtractionImportDialog(string title, string body, Action onOverwrite, Action onMerge, Action? onCancel = null)
    {
        Layer = 200;
        _title = title;
        _body = body;
        _onOverwrite = onOverwrite;
        _onMerge = onMerge;
        _onCancel = onCancel;
    }

    public override void _Ready()
    {
        BuildUi();
        _cancelButton.CallDeferred(Control.MethodName.GrabFocus);
    }

    public override void _Input(InputEvent inputEvent)
    {
        if (inputEvent is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            Cancel();
            GetViewport().SetInputAsHandled();
        }
    }

    private void BuildUi()
    {
        var root = new Panel { Name = "ImportOverlay" };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddThemeStyleboxOverride("panel", OverlayBox());
        AddChild(root);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(center);

        var card = new PanelContainer
        {
            CustomMinimumSize = new Vector2(560f, 0f),
        };
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
        box.AddThemeConstantOverride("separation", 14);
        margin.AddChild(box);

        var titleLabel = new Label
        {
            Text = _title,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        titleLabel.AddThemeFontOverride("font", ExtractionTheme.Bold);
        titleLabel.AddThemeFontSizeOverride("font_size", ExtractionTheme.FontSizeSection);
        box.AddChild(titleLabel);

        var bodyLabel = new Label
        {
            Text = _body,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        bodyLabel.AddThemeColorOverride("font_color", ExtractionTheme.TextSecondary);
        bodyLabel.AddThemeFontSizeOverride("font_size", ExtractionTheme.FontSizeBody);
        box.AddChild(bodyLabel);

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 12);
        buttons.Alignment = BoxContainer.AlignmentMode.Center;
        box.AddChild(buttons);

        _cancelButton = MakeButton(ExtractionLocalization.CancelButtonText(), ExtractionTheme.ButtonSecondary);
        _cancelButton.CustomMinimumSize = new Vector2(130f, 44f);
        _cancelButton.Pressed += Cancel;
        buttons.AddChild(_cancelButton);

        var merge = MakeButton(ExtractionLocalization.SaveImportMergeButtonText(), ExtractionTheme.ButtonSecondary);
        merge.CustomMinimumSize = new Vector2(130f, 44f);
        merge.Pressed += Merge;
        buttons.AddChild(merge);

        var overwrite = MakeButton(ExtractionLocalization.SaveImportOverwriteButtonText(), ExtractionTheme.ButtonPrimary);
        overwrite.CustomMinimumSize = new Vector2(130f, 44f);
        overwrite.Pressed += Overwrite;
        buttons.AddChild(overwrite);
    }

    private void Overwrite() => RunAndClose(_onOverwrite);

    private void Merge() => RunAndClose(_onMerge);

    private void Cancel() => RunAndClose(_onCancel);

    private void RunAndClose(Action? action)
    {
        try
        {
            action?.Invoke();
        }
        catch (Exception e)
        {
            Entry.Logger.Error($"ExtractionImportDialog: action failed: {e}");
        }

        QueueFree();
    }

    private static StyleBoxFlat OverlayBox() => new() { BgColor = OverlayTint };

    private static Button MakeButton(string text, string variation) => new() { Text = text, ThemeTypeVariation = variation };
}
