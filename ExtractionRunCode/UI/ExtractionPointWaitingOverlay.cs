using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes;
using ExtractionRun.Modifier;

namespace ExtractionRun.UI;

/// <summary>
/// The 撤离点 waiting overlay — shown after the local player confirms their extraction selection while the rest of
/// the party is still picking. Mirrors the vanilla "waiting for other players" overlay (<c>NRewardsScreen</c>'s
/// <c>%WaitingForOtherPlayers</c>): a full-screen panel + centered text using the same <c>gameplay_ui.MULTIPLAYER_WAITING</c>
/// key and palette. Only ever shown when there are actually peers still to confirm; singleplayer never shows it.
/// 撤离点等待覆盖层——本地玩家确认带出选择、其余队友仍在挑选时显示。对齐原版「等待其他玩家」覆盖层（NRewardsScreen 的
/// %WaitingForOtherPlayers）：整屏面板 + 居中文本，复用同一 loc 键（gameplay_ui.MULTIPLAYER_WAITING）与配色。仅在仍有
/// 队友未确认时显示；单机从不显示。
/// </summary>
public sealed partial class ExtractionPointWaitingOverlay : CanvasLayer
{
    private static readonly Color OverlayTint = new(0f, 0f, 0f, 0.62f);

    /// <summary>The vanilla waiting text — same table/key as the base game's own overlays. 原版等待文本，与游戏自带覆盖层同表同键。</summary>
    private static readonly LocString WaitingLoc = new("gameplay_ui", "MULTIPLAYER_WAITING");

    private ExtractionPointWaitingOverlay()
    {
        Layer = 100;
    }

    /// <summary>
    /// Opens the waiting overlay when there are peers still confirming. Returns null when there's nothing to wait for
    /// (singleplayer, or every other player already confirmed) so callers can skip the show/close dance.
    /// 若有队友仍未确认则打开等待覆盖层；无可等待对象（单机或其余玩家均已确认）时返回 null，调用方无需展示/关闭。
    /// </summary>
    public static ExtractionPointWaitingOverlay? ShowIfWaiting(IReadOnlyList<Player> players)
    {
        if (players.Count <= 1 || players.All(p => ExtractionPointFlow.ConfirmedPlayers.Contains(p.NetId)))
        {
            return null;
        }

        var overlay = new ExtractionPointWaitingOverlay();
        Node? host = NGame.Instance;
        if (host == null)
        {
            Entry.Logger.Error("ExtractionPointWaitingOverlay: no NGame to host the overlay.");
            return null;
        }

        host.AddChild(overlay);
        return overlay;
    }

    public override void _Ready()
    {
        var root = new Panel { Name = "WaitingForOtherPlayers" };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = OverlayTint });
        AddChild(root);

        // The vanilla waiting label's palette: warm off-white on a dark shadow/outline, 44px, centered. Font is the
        // game's per-language font (CJK substitute when needed), same resolution the vanilla label uses.
        // 沿用原版等待标签的配色：暖白文字配深色阴影描边、44px、居中；字体用游戏按语言提供的原版字体（CJK 等需要替换时）。
        var label = new Label { Text = WaitingLoc.GetFormattedText() };
        label.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.AddThemeFontOverride("font", ExtractionTheme.Regular);
        label.AddThemeColorOverride("font_color", new Color("FFF6E2"));
        label.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.25f));
        label.AddThemeColorOverride("font_outline_color", new Color(0.29f, 0.24f, 0.16f, 0.75f));
        label.AddThemeConstantOverride("shadow_offset_x", 6);
        label.AddThemeConstantOverride("shadow_offset_y", 5);
        label.AddThemeConstantOverride("outline_size", 16);
        label.AddThemeFontSizeOverride("font_size", 44);
        root.AddChild(label);
    }

    public void Close() => QueueFree();
}
