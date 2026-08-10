using Godot;

namespace ExtractionRun.UI;

/// <summary>
/// A minimal themed modal confirmation (确定/取消) rendered on its own high CanvasLayer, so it draws above both the
/// warehouse hub (Layer 100) and the dev console (default Layer 1). The base game's <c>NModalContainer</c> sits on the
/// root canvas (Layer 0), so a popup there would be hidden behind the hub — this standalone overlay sidesteps that.
/// Blocks clicks with a full-rect input trap and focuses Cancel by default, so Enter/space dismiss without firing the destructive action.
/// 高层的代码构建确认弹窗（确定/取消）：独立 CanvasLayer(200)，盖在仓库大厅(100)与控制台(1)之上。游戏自带 NModalContainer
/// 在根画布(0)，弹窗会被大厅盖住——这个独立覆盖层绕开了该问题。整屏输入拦截 + 默认聚焦「取消」，Enter/空格/Esc 都不触发破坏性动作。
/// </summary>
public sealed partial class ExtractionConfirmDialog : CanvasLayer
{
    private static readonly Color OverlayTint = new(0f, 0f, 0f, 0.55f);

    private readonly string _title;
    private readonly string _body;
    private readonly Action _onConfirm;
    private readonly Action? _onCancel;

    private Button _cancelButton = null!;

    public ExtractionConfirmDialog(string title, string body, Action onConfirm, Action? onCancel = null)
    {
        Layer = 200;
        _title = title;
        _body = body;
        _onConfirm = onConfirm;
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
        var root = new Panel { Name = "ConfirmOverlay" };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddThemeStyleboxOverride("panel", OverlayBox());
        AddChild(root);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(center);

        var card = new PanelContainer
        {
            CustomMinimumSize = new Vector2(520f, 0f),
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
        _cancelButton.CustomMinimumSize = new Vector2(140f, 44f);
        _cancelButton.Pressed += Cancel;
        buttons.AddChild(_cancelButton);

        var confirm = MakeButton(ExtractionLocalization.ConfirmButtonText(), ExtractionTheme.ButtonPrimary);
        confirm.CustomMinimumSize = new Vector2(140f, 44f);
        confirm.Pressed += Confirm;
        buttons.AddChild(confirm);
    }

    private void Confirm()
    {
        try
        {
            _onConfirm();
        }
        catch (Exception e)
        {
            Entry.Logger.Error($"ExtractionConfirmDialog: confirm action failed: {e}");
        }

        QueueFree();
    }

    private void Cancel()
    {
        try
        {
            _onCancel?.Invoke();
        }
        catch (Exception e)
        {
            Entry.Logger.Error($"ExtractionConfirmDialog: cancel action failed: {e}");
        }

        QueueFree();
    }

    private static StyleBoxFlat OverlayBox() => new() { BgColor = OverlayTint };

    private static Button MakeButton(string text, string variation) => new() { Text = text, ThemeTypeVariation = variation };
}
