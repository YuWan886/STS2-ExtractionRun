using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
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

    private const string SubmenuIconPath = "res://ExtractionRun/images/ui/submenu_extraction_run.png";

    private const float RowScale = 0.86f;
    private const float RowGap = 70f;
    private const float GoldHue = 0.678f;
    private const float GoldSaturation = 1.2f;
    private const float GoldValue = 0.65f;

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

        var buttons = submenu.GetChildren().OfType<NSubmenuButton>().ToList();

        var button = (NSubmenuButton)template.Duplicate();
        button.Name = ButtonName;
        button.Connect(NClickableControl.SignalName.Released,
            Callable.From<NButton>(_ => OpenWarehouseHub(submenu, loadingOverlay, isMultiplayerHost)));

        Control bgPanel = button.GetNode<Control>("BgPanel");
        if (bgPanel.Material != null)
        {
            var shader = (ShaderMaterial)bgPanel.Material.Duplicate();
            shader.SetShaderParameter("h", GoldHue);
            shader.SetShaderParameter("s", GoldSaturation);
            shader.SetShaderParameter("v", GoldValue);
            bgPanel.Material = shader;
        }

        submenu.AddChild(button);

        TextureRect icon = button.GetNode<TextureRect>("Icon");
        if (icon != null && ResourceLoader.Exists(SubmenuIconPath))
        {
            icon.Texture = ResourceLoader.Load<Texture2D>(SubmenuIconPath);
        }
        button.SetIconAndLocalization(LocKeyPrefix);
        buttons.Add(button);

        RelayoutModeButtons(buttons, submenu.GetViewportRect().Size);
    }

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
