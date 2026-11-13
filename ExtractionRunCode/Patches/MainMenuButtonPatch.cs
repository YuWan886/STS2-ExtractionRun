using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using ExtractionRun.UI;

namespace ExtractionRun.Patches;

/// <summary>
/// Adds a 「搜打撤」 button to the singleplayer and multiplayer-host main-menu submenus, opening the warehouse hub.
/// 在主菜单「单人」「联机」子菜单添加「搜打撤」按钮，打开仓库大厅。
/// </summary>
public static class MainMenuButtonPatch
{
    private const string ButtonName = "ExtractionRunButton";
    private const string LocKeyPrefix = "EXTRACTION_RUN";

    private const string SubmenuIconPath = "res://ExtractionRun/images/ui/submenu_extraction_run.png";

    // Legacy layout (0.111 and older): the mode buttons are plain children of the submenu, so they are placed by hand.
    private const float RowScale = 0.86f;
    private const float RowGap = 70f;

#if STS2_API_0_113_OR_LATER
    // 0.113 layout: the mode buttons live in a %ButtonContainer HBoxContainer that lays them out itself. Vanilla renders
    // five slots at 0.85 (362 wide each), so that same row width is the budget our extra slot has to fit into.
    private const float VanillaSlotWidth = 362f;
    private const float VanillaSlotCount = 5f;
    private const float VanillaContainerScale = 0.85f;
    private static readonly float RowWidthBudget = VanillaSlotWidth * VanillaSlotCount * VanillaContainerScale;
#endif

    private const float GoldHue = 0.678f;
    private const float GoldSaturation = 1.2f;
    private const float GoldValue = 0.65f;

    [HarmonyPatch(typeof(NSingleplayerSubmenu), "_Ready")]
    private static class SingleplayerSubmenuPatch
    {
        private static void Postfix(NSingleplayerSubmenu __instance)
        {
            AddButton(__instance, __instance._customButton, WarehouseHubScreen.HubMode.Singleplayer, loadingOverlay: null);
        }
    }

    [HarmonyPatch(typeof(NMultiplayerHostSubmenu), "_Ready")]
    private static class MultiplayerHostSubmenuPatch
    {
        private static void Postfix(NMultiplayerHostSubmenu __instance)
        {
            AddButton(__instance, __instance._customButton, WarehouseHubScreen.HubMode.MultiplayerHost, __instance._loadingOverlay);
        }
    }

#if STS2_API_0_113_OR_LATER
    /// <summary>
    /// 0.113 fits the (now five-slot) mode row into <c>%ButtonContainer</c> in <c>RefreshButtons</c>, which also hides the
    /// dread slot until its epoch is revealed. Refit after every refresh so our sixth slot keeps the row inside the screen
    /// whenever the visible slot count changes.
    /// </summary>
    [HarmonyPatch(typeof(NSingleplayerSubmenu), "RefreshButtons")]
    private static class SingleplayerRefreshPatch
    {
        private static void Postfix(NSingleplayerSubmenu __instance) => FitButtonRow(__instance);
    }

    [HarmonyPatch(typeof(NMultiplayerHostSubmenu), "RefreshButtons")]
    private static class MultiplayerHostRefreshPatch
    {
        private static void Postfix(NMultiplayerHostSubmenu __instance) => FitButtonRow(__instance);
    }
#endif

    private static void AddButton(NSubmenu submenu, NSubmenuButton template, WarehouseHubScreen.HubMode mode, Control? loadingOverlay)
    {
        if (template == null)
        {
            return;
        }

#if STS2_API_0_113_OR_LATER
        // 0.113 moved the mode buttons under %ButtonContainer so the container drives their placement.
        Control? container = submenu.GetNodeOrNull<Control>("%ButtonContainer");
#else
        Control? container = null;
#endif
        var buttons = submenu.GetChildren().OfType<NSubmenuButton>().ToList();

        var button = (NSubmenuButton)template.Duplicate();
        button.Name = ButtonName;
        button.UniqueNameInOwner = false;
        button.Connect(NClickableControl.SignalName.Released,
            Callable.From<NButton>(_ => OpenWarehouseHub(submenu, loadingOverlay, mode)));

        // Adding through the container keeps it in the HBox flow; the legacy path appends to the submenu itself.
        if (container != null)
        {
            container.AddChild(button);
        }
        else
        {
            submenu.AddChild(button);
        }

        // The clone inherits the template's current look, including a locked/dimmed tint when custom runs are still
        // locked, so force the unlocked state. Enable() resets the shader saturation as well, hence the tint after it.
        button.Enable();

        Control bgPanel = button.GetNode<Control>("BgPanel");
        if (bgPanel.Material != null)
        {
            var shader = (ShaderMaterial)bgPanel.Material.Duplicate();
            shader.SetShaderParameter("h", GoldHue);
            shader.SetShaderParameter("s", GoldSaturation);
            shader.SetShaderParameter("v", GoldValue);
            bgPanel.Material = shader;
        }

#if STS2_API_0_113_OR_LATER
        // 0.113 renamed the submenu icon node to %SubmenuPortrait and split SetIconAndLocalization
        // into SetLocalization + SetIcon.
        // %-lookup works through the duplicate's owner chain, but fall back to a plain recursive name search in case a
        // future host stops exporting the unique name on runtime-instantiated clones.
        var portrait = button.GetNodeOrNull<TextureRect>("%SubmenuPortrait")
                       ?? button.FindChild("SubmenuPortrait", recursive: true, owned: false) as TextureRect
                       ?? button.GetNodeOrNull<TextureRect>("Icon");
        if (portrait != null && ResourceLoader.Exists(SubmenuIconPath))
        {
            portrait.Texture = ResourceLoader.Load<Texture2D>(SubmenuIconPath);
        }
        button.SetLocalization(LocKeyPrefix);

        FitButtonRow(submenu);
#else
        TextureRect icon = button.GetNode<TextureRect>("Icon");
        if (icon != null && ResourceLoader.Exists(SubmenuIconPath))
        {
            icon.Texture = ResourceLoader.Load<Texture2D>(SubmenuIconPath);
        }
        button.SetIconAndLocalization(LocKeyPrefix);

        buttons.Add(button);
        RelayoutModeButtons(buttons, submenu.GetViewportRect().Size);
#endif
    }

#if STS2_API_0_113_OR_LATER
    /// <summary>
    /// Scales the whole mode row so the extra slot still fits: with our button the singleplayer submenu has five visible
    /// slots instead of vanilla's four, so the container is scaled down to the width vanilla occupies at five slots.
    /// </summary>
    private static void FitButtonRow(NSubmenu submenu)
    {
        Control? container = submenu.GetNodeOrNull<Control>("%ButtonContainer");
        if (container == null)
        {
            return;
        }

        List<NSubmenuButton> visible = container.GetChildren()
            .OfType<NSubmenuButton>()
            .Where(button => button.Visible)
            .ToList();
        if (visible.Count == 0)
        {
            return;
        }

        float slotWidth = visible.Max(button => Mathf.Max(button.CustomMinimumSize.X, button.Size.X));
        if (slotWidth <= 0f)
        {
            slotWidth = VanillaSlotWidth;
        }

        float scale = Mathf.Min(1f, RowWidthBudget / (visible.Count * slotWidth));
        container.Scale = new Vector2(scale, scale);
    }
#else
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
#endif

    private static void OpenWarehouseHub(NSubmenu submenu, Control? loadingOverlay, WarehouseHubScreen.HubMode mode)
    {
        NGame? game = NGame.Instance;
        if (game == null)
        {
            return;
        }

        var hub = new WarehouseHubScreen(submenu._stack, loadingOverlay, mode);
        game.AddChild(hub);
        Entry.Logger.Info($"MainMenuButtonPatch: opened warehouse hub (mode={mode}).");
    }
}
