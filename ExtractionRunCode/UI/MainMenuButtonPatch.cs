using System.Collections.Generic;
using System.Linq;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace ExtractionRun.UI;

/// <summary>
/// Adds a 「搜打撤」 button to the singleplayer and multiplayer-host main-menu submenus, opening the warehouse hub.
/// 在主菜单「单人」「联机」子菜单添加「搜打撤」按钮，打开仓库大厅。
/// </summary>
public static class MainMenuButtonPatch
{
    private const string ButtonName = "ExtractionRunButton";
    private const string LocKeyPrefix = "EXTRACTION_RUN";

    // The vanilla 3-across layout already fills the 1920x1080 screen at each button's original 330x705 size,
    // leaving no room for a 4th. Scale the whole row down and spread the 4 buttons evenly across it.
    // 原版三连排按钮各 330x705 已占满 1080p 屏幕，放不下第 4 个；整体缩放后一行排 4 个。
    private const float RowScale = 0.68f;
    private const float RowGap = 70f;

    [HarmonyPatch(typeof(NSingleplayerSubmenu), "_Ready")]
    private static class SingleplayerSubmenuPatch
    {
        private static void Postfix(NSingleplayerSubmenu __instance)
        {
            AddButton(__instance, __instance._customButton, isMultiplayerHost: false, loadingOverlay: null);
        }
    }

    [HarmonyPatch(typeof(NMultiplayerHostSubmenu), "_Ready")]
    private static class MultiplayerHostSubmenuPatch
    {
        private static void Postfix(NMultiplayerHostSubmenu __instance)
        {
            AddButton(__instance, __instance._customButton, isMultiplayerHost: true, __instance._loadingOverlay);
        }
    }

    private static void AddButton(NSubmenu submenu, NSubmenuButton template, bool isMultiplayerHost, Control? loadingOverlay)
    {
        if (template == null)
        {
            return;
        }

        // The 3 vanilla mode buttons (Standard/Daily/Custom) plus the new one. BackButton / LoadingOverlay
        // are not NSubmenuButtons so they are excluded automatically.
        // 3 个原版模式按钮（标准/每日/自定）加新按钮；返回按钮与加载遮罩不是 NSubmenuButton，自动被排除。
        var buttons = submenu.GetChildren().OfType<NSubmenuButton>().ToList();

        var button = (NSubmenuButton)template.Duplicate();
        button.Name = ButtonName;
        button.Connect(NClickableControl.SignalName.Released,
            Callable.From<NButton>(_ => OpenWarehouseHub(submenu, loadingOverlay, isMultiplayerHost)));
        // Add to the tree first so _Ready()/ConnectSignals() runs on the duplicate and resolves its child
        // nodes (_title/_description/...). Otherwise SetIconAndLocalization -> RefreshLabels touches null
        // node references on the not-yet-ready duplicate (the NRE reported at startup).
        // 先加入场景树触发副本的 _Ready，解析出 _title 等子节点引用，否则 SetIconAndLocalization 会空引用。
        submenu.AddChild(button);
        // The duplicate is cloned from the Custom button, so it already shows the Custom icon as placeholder.
        // 副本克隆自「自定」按钮，天然带自定模式图标作占位符。
        button.SetIconAndLocalization(LocKeyPrefix);
        buttons.Add(button);

        RelayoutModeButtons(buttons, submenu.GetViewportRect().Size);
    }

    /// <summary>
    /// Scales every mode button's rect and child offsets by <see cref="RowScale"/> and lays them out in a single
    /// centered row so the added 4th button is visible and clickable on 1920x1080.
    /// 将每个模式按钮连同内部子节点整体缩放并居中排成一行，让第 4 个按钮在 1080p 下可见可点。
    /// </summary>
    private static void RelayoutModeButtons(List<NSubmenuButton> buttons, Vector2 viewportSize)
    {
        if (buttons.Count == 0)
        {
            return;
        }

        float w = buttons[0].Size.X * RowScale;
        float h = buttons[0].Size.Y * RowScale;
        float totalWidth = buttons.Count * w + (buttons.Count - 1) * RowGap;
        float left = (viewportSize.X - totalWidth) / 2f;
        float top = (viewportSize.Y - h) / 2f;

        for (int i = 0; i < buttons.Count; i++)
        {
            var b = buttons[i];
            b.Position = new Vector2(left + i * (w + RowGap), top);
            b.Size = new Vector2(w, h);
            ScaleButtonContents(b, RowScale);
        }
    }

    /// <summary>
    /// Scales the icon/lock/title/description rects proportionally. BgPanel is left alone because it is full-rect
    /// anchored and stretches with the button. Each child's offsets are relative to its own anchor, so multiplying
    /// them scales around that anchor (centered children stay centered, top-left stays top-left).
    /// 按比例缩放 Icon/Lock/Title/Description 的 rect。BgPanel 全矩锚定，随按钮自动拉伸，无需处理。
    /// </summary>
    private static void ScaleButtonContents(NSubmenuButton button, float s)
    {
        foreach (string path in new[] { "Icon", "Lock", "Title", "Description" })
        {
            Control child = button.GetNode<Control>(path);
            child.OffsetLeft *= s;
            child.OffsetTop *= s;
            child.OffsetRight *= s;
            child.OffsetBottom *= s;
        }
    }

    private static void OpenWarehouseHub(NSubmenu submenu, Control? loadingOverlay, bool isMultiplayerHost)
    {
        NGame? game = NGame.Instance;
        if (game == null)
        {
            return;
        }

        var hub = new WarehouseHubScreen(submenu._stack, loadingOverlay, isMultiplayerHost);
        game.AddChild(hub);
        Entry.Logger.Info($"MainMenuButtonPatch: opened warehouse hub (multiplayerHost={isMultiplayerHost}).");
    }
}
