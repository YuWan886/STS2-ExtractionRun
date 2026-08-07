using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Runs;
using ExtractionRun.Lifecycle;
using ExtractionRun.Networking;

namespace ExtractionRun.UI;

/// <summary>
/// Adds a 「查看撤离结算」 button to the vanilla game-over screen's summary page (next to "Return to Main Menu"),
/// shown only for 搜打撤 runs. Clicking it opens <see cref="ExtractionSettlementScreen"/>. The button is a clone of the
/// vanilla main-menu button, so it matches the game-over screen's art; it mirrors that button's visibility so it
/// appears exactly when the summary page is revealed.
/// 在原版 GameOver 屏的总结页（「返回主菜单」按钮旁）添加「查看撤离结算」按钮，仅搜打撤跑局显示；点击打开结算界面。
/// 按钮克隆自原版主菜单按钮以保持画风，并跟随其显隐在总结页出现。
/// </summary>
public static class GameOverSettlementButtonPatch
{
    private const string ButtonName = "ExtractionSettlementButton";
    private const float ButtonGap = 74f;

    // The settlement button and the vanilla main-menu button it mirrors, for the current game-over screen.
    // Used by the Enable/Disable sync postfixes below so the settlement button follows the vanilla button exactly.
    // 当前结算按钮与它所镜像的原版主菜单按钮；供下面的 Enable/Disable 同步后置补丁使用。
    private static NReturnToMainMenuButton? _settlementButton;
    private static NReturnToMainMenuButton? _mainMenuButton;

    [HarmonyPatch(typeof(NGameOverScreen), "_Ready")]
    private static class GameOverReadyPatch
    {
        private static void Postfix(NGameOverScreen __instance)
        {
            AddButton(__instance);
        }
    }

    // Robust reveal/hide: when the vanilla main-menu button is enabled/disabled (summary page reveal / leaderboard /
    // back to page 1), sync the settlement button. Fires at the exact same moment as the VisibilityChanged mirror below,
    // but is not dependent on the signal, so one of the two paths always runs.
    // 稳健显隐：原版主菜单按钮 Enable/Disable（总结页出现/排行榜切换/回第一页）时同步结算按钮。
    [HarmonyPatch(typeof(NClickableControl), nameof(NClickableControl.Enable))]
    private static class MainMenuEnablePatch
    {
        private static void Postfix(NClickableControl __instance)
        {
            if (__instance != _mainMenuButton || !GodotObject.IsInstanceValid(_settlementButton))
            {
                return;
            }

            _settlementButton!.Enable();
            Entry.Logger.Info("GameOverSettlementButtonPatch: settlement button enabled via Enable postfix.");
        }
    }

    [HarmonyPatch(typeof(NClickableControl), nameof(NClickableControl.Disable))]
    private static class MainMenuDisablePatch
    {
        private static void Postfix(NClickableControl __instance)
        {
            if (__instance != _mainMenuButton || !GodotObject.IsInstanceValid(_settlementButton))
            {
                return;
            }

            _settlementButton!.Disable();
        }
    }

    private static void AddButton(NGameOverScreen screen)
    {
        // Only extraction runs get a settlement button.
        RunState? runState = Traverse.Create(screen).Field("_runState").GetValue<RunState>();
        bool isExtractionRun = runState != null && ExtractionCarrySync.HasExtractionModifier(runState.Modifiers);
        if (!isExtractionRun)
        {
            Entry.Logger.Info("GameOverSettlementButtonPatch: skipping (not an extraction run; " +
                              $"runState={runState != null}, modifier={isExtractionRun}).");
            return;
        }

        if (ExtractionSettlement.Current == null)
        {
            Entry.Logger.Info("GameOverSettlementButtonPatch: skipping (no settlement result recorded for this run).");
            return;
        }

        NReturnToMainMenuButton mainMenu = screen.GetNode<NReturnToMainMenuButton>("%MainMenuButton");
        Control ui = mainMenu.GetParent<Control>();
        if (ui == null || ui.FindChild(ButtonName, owned: false) != null)
        {
            return; // Already added (defensive; _Ready runs once).
        }

        // Clone the vanilla main-menu button and repurpose it. NReturnToMainMenuButton._Ready shifts itself 140px left
        // and re-captures _showPosition to the shifted spot; Duplicate() inherits the already-shifted offsets, so the
        // clone would shift a second time and land 140px left of the main-menu button. Compensate by nudging the clone
        // 140px right (and up by the gap) BEFORE AddChild — its _Ready then captures _showPosition directly above the
        // main-menu button. Offsets are mutated verbatim (NOT Position, whose setter recomputes against a zero parent
        // rect off-tree and would misplace the button). 140 is the vanilla _Ready shift; the two are coupled by design.
        // 克隆自原版主菜单按钮。NReturnToMainMenuButton._Ready 会左移 140px 并重捕 _showPosition；Duplicate() 继承的是
        // 已经左移过的 offsets，克隆体会再移一次偏左 140px。故在 AddChild 前把克隆体右移 140px、上移 gap 补偿。
        // 必须直接改 offsets（Position 设值器在未入树时会按零父矩形重算偏移，导致错位）。
        var button = (NReturnToMainMenuButton)mainMenu.Duplicate();
        button.Name = ButtonName;
        button.OffsetLeft += 140f;
        button.OffsetRight += 140f;
        button.OffsetTop -= ButtonGap;
        button.OffsetBottom -= ButtonGap;

        // Duplicate() copies nodes but not resources: the clone's Image would share the original button's
        // resource_local_to_scene ShaderMaterial, so hover HSV tweens (OnFocus/OnUnfocus) would bleed between the two
        // buttons. Give the clone its own material copies BEFORE AddChild (its _Ready caches Image.Material as _hsv).
        // Duplicate() 不复制资源：克隆体 Image 会与原按钮共享 local_to_scene 的 ShaderMaterial，悬停提亮会互相串扰。
        button.GetNode<TextureRect>("Image").Material =
            (ShaderMaterial)mainMenu.GetNode<TextureRect>("Image").Material.Duplicate();
        if (mainMenu.Material != null)
        {
            button.Material = (Material)mainMenu.Material.Duplicate();
        }

        ui.AddChild(button);
        // Force _isEnabled=false regardless of what Duplicate() copied (it clones a *disabled* button; if the flag
        // isn't preserved the later Enable() would be a no-op and the button would never show).
        button.Disable();
        button.Visible = false;

        button.GetNode<MegaLabel>("Label").SetTextAutoSize(ExtractionLocalization.SettlementButtonText());
        button.Connect(NClickableControl.SignalName.Released,
            Callable.From<NButton>(_ => OpenSettlement()));

        // Stash the pair so the Enable/Disable postfixes can sync them.
        _mainMenuButton = mainMenu;
        _settlementButton = button;

        // Secondary reveal path: mirror the main-menu button's visibility so this appears with the summary page and
        // hides with it. (The Enable postfix above is the primary path; this covers any visibility-only transition.)
        mainMenu.VisibilityChanged += () =>
        {
            if (!GodotObject.IsInstanceValid(button))
            {
                return;
            }

            if (mainMenu.Visible)
            {
                button.Enable();
            }
            else
            {
                button.Disable();
            }
        };

        Entry.Logger.Info("GameOverSettlementButtonPatch: settlement button added to game-over summary.");
    }

    private static void OpenSettlement()
    {
        if (ExtractionSettlement.Current is not { } result)
        {
            return;
        }

        NGame? game = NGame.Instance;
        if (game == null)
        {
            return;
        }

        game.AddChild(new ExtractionSettlementScreen(result));
        Entry.Logger.Info("GameOverSettlementButtonPatch: opened settlement screen.");
    }
}
