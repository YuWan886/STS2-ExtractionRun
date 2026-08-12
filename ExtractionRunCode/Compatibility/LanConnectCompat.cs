using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Runs;
using ExtractionRun.Lifecycle;
using ExtractionRun.UI;

namespace ExtractionRun.Compatibility;

/// <summary>
/// Runtime compatibility with the third-party STS2-Game-Lobby mod ("联机大厅", assembly <c>sts2_lan_connect</c>): adds a
/// 搜打撤 room type to its create-room dialog and bridges the selection into the standard 搜打撤 launch flow
/// (<c>ExtractionRunContext.IsExtractionLaunch</c> + <c>HostCarrySetupRequired</c>), so a lobby room runs the
/// extraction modifier with per-player carry setup. The lobby's types are resolved by name and patched via
/// reflection — no compile-time dependency; every patch is defensive (a failure only logs and leaves the lobby's
/// base flow untouched). Inert when the lobby mod is not installed.
///
/// Flag lifecycle: <c>GetSelectedCreateGameMode</c> mirrors the dialog selection into the launch flags; opening or
/// closing the create dialog clears them; <c>CharacterSelectPatch</c> consumes them once the host's character-select
/// screen initializes. The room is published as <c>GameMode.Standard</c> (so the host lands on the character-select
/// screen) while <c>GetLobbyGameMode</c> reports <c>"extraction"</c> — an opaque string the lobby service passes
/// through, shown as 搜打撤 by the label/pill patches.
/// 与第三方联机大厅 mod（STS2-Game-Lobby，程序集 sts2_lan_connect）的运行时兼容：在其建房表单加入「搜打撤」房间类型，
/// 并把该选择桥接进标准搜打撤开跑链路（IsExtractionLaunch + HostCarrySetupRequired），使大厅房间跑搜打撤修正项并逐人
/// 配置携带。大厅类型按名反射解析并打补丁——无编译期依赖，每个补丁防御式（失败仅记日志，不破坏大厅原流程）。未装大厅 mod 时完全惰性。
///
/// flag 生命周期：GetSelectedCreateGameMode 把建房表单选择镜像进开跑 flag；打开/关闭建房弹窗清零；主机角色选择屏初始化时由
/// CharacterSelectPatch 消费。房间以 GameMode.Standard 发布（使房主进入角色选择屏），同时 GetLobbyGameMode 上报
/// "extraction"——大厅服务视为不透明串透传，由标签/pill 补丁显示为「搜打撤」。
/// </summary>
internal static class LanConnectCompat
{
    private const string LobbyAssemblyName = "sts2_lan_connect";
    private const string OverlayTypeName = "Sts2LanConnect.Scripts.LanConnectLobbyOverlay";
    private const string SaveRoomBindingTypeName = "Sts2LanConnect.Scripts.LanConnectMultiplayerSaveRoomBinding";

    /// <summary>Id of the 搜打撤 item appended to the lobby's room-type OptionButton. 追加到大建房类型下拉的搜打撤项 id。</summary>
    private const int ExtractionRoomTypeId = 3;

    /// <summary>Opaque room game-mode string the lobby service passes through untouched. 大厅服务透传的不透明房间模式串。</summary>
    private const string ExtractionGameMode = "extraction";

    private static readonly BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static FieldInfo? _roomTypeOptionField;
    private static int _installAttempts;

    /// <summary>Installs the lobby-compat patches once, deferring past mod loading if the lobby assembly isn't loaded
    /// yet (all mod initializers run before the scene tree's first frame). 安装一次大厅兼容补丁；若大厅程序集尚未加载，
    /// 延迟到 mod 加载完成后重试。</summary>
    public static void InstallIfNeeded(Harmony harmony)
    {
        if (_installAttempts++ > 0)
        {
            return;
        }

        if (!TryInstall(harmony))
        {
            Callable.From(() => InstallDeferred(harmony)).CallDeferred();
        }
    }

    private static void InstallDeferred(Harmony harmony)
    {
        // Mirror the lobby mod's own deferred-install guard: wait for a ready scene tree before patching.
        if (Engine.GetMainLoop() is not SceneTree tree || tree.Root == null)
        {
            Callable.From(() => InstallDeferred(harmony)).CallDeferred();
            return;
        }

        TryInstall(harmony);
    }

    private static bool TryInstall(Harmony harmony)
    {
        try
        {
            Assembly? lobbyAssembly = ResolveLobbyAssembly();
            if (lobbyAssembly == null)
            {
                return false;
            }

            Type? overlay = lobbyAssembly.GetType(OverlayTypeName, throwOnError: false);
            Type? saveBinding = lobbyAssembly.GetType(SaveRoomBindingTypeName, throwOnError: false);
            if (overlay == null || saveBinding == null)
            {
                return false;
            }

            _roomTypeOptionField = overlay.GetField("_roomTypeOption", AnyInstance);
            if (_roomTypeOptionField == null)
            {
                Entry.Logger.Warn("LanConnectCompat: lobby '_roomTypeOption' field not found; 搜打撤 room-type option unavailable.");
            }

            int applied = 0;
            applied += PatchMethod(harmony, overlay, "BuildCreateDialog", AnyInstance, postfix: nameof(BuildCreateDialogPostfix)) ? 1 : 0;
            applied += PatchMethod(harmony, overlay, "GetSelectedCreateGameMode", AnyInstance, postfix: nameof(GetSelectedCreateGameModePostfix)) ? 1 : 0;
            applied += PatchMethod(harmony, overlay, "CloseCreateDialog", AnyInstance, postfix: nameof(CloseCreateDialogPostfix)) ? 1 : 0;
            applied += PatchMethod(harmony, overlay, "OpenCreateDialogInternal", AnyInstance, postfix: nameof(OpenCreateDialogInternalPostfix)) ? 1 : 0;
            applied += PatchMethod(harmony, overlay, "GetRoomGameModePill", AnyStatic, postfix: nameof(GetRoomGameModePillPostfix)) ? 1 : 0;
            applied += PatchMethod(harmony, saveBinding, "GetLobbyGameMode", AnyStatic, prefix: nameof(GetLobbyGameModePrefix),
                filter: static m => m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(GameMode)) ? 1 : 0;
            applied += PatchMethod(harmony, saveBinding, "GetLobbyGameModeLabel", AnyStatic, postfix: nameof(GetLobbyGameModeLabelPostfix),
                filter: static m => m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string)) ? 1 : 0;

            if (applied == 0)
            {
                Entry.Logger.Warn("LanConnectCompat: lobby mod detected but no patch targets resolvable; compat disabled.");
                return false;
            }

            Entry.Logger.Info($"LanConnectCompat: installed {applied} STS2-Game-Lobby patches (搜打撤 rooms).");
            return true;
        }
        catch (Exception ex)
        {
            Entry.Logger.Warn($"LanConnectCompat: failed to install lobby compat: {ex.Message}");
            return false;
        }
    }

    private static bool PatchMethod(
        Harmony harmony,
        Type type,
        string name,
        BindingFlags flags,
        string? prefix = null,
        string? postfix = null,
        Func<MethodInfo, bool>? filter = null)
    {
        try
        {
            MethodInfo? target = type.GetMethods(flags).Where(m => m.Name == name).FirstOrDefault(filter ?? (static _ => true));
            if (target == null)
            {
                return false;
            }

            harmony.Patch(
                target,
                prefix: prefix != null ? new HarmonyMethod(typeof(LanConnectCompat).GetMethod(prefix, AnyStatic)!) : null,
                postfix: postfix != null ? new HarmonyMethod(typeof(LanConnectCompat).GetMethod(postfix, AnyStatic)!) : null);
            return true;
        }
        catch (Exception ex)
        {
            Entry.Logger.Warn($"LanConnectCompat: failed to patch {type.Name}.{name}: {ex.Message}");
            return false;
        }
    }

    private static Assembly? ResolveLobbyAssembly()
    {
        try
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (string.Equals(assembly.GetName().Name, LobbyAssemblyName, StringComparison.OrdinalIgnoreCase))
                    {
                        return assembly;
                    }
                }
                catch
                {
                    // Dynamic/unloadable assembly — skip.
                }
            }
        }
        catch (Exception ex)
        {
            Entry.Logger.Warn($"LanConnectCompat: failed to scan loaded assemblies: {ex.Message}");
        }

        return null;
    }

    // ----- Patch bodies -----

    /// <summary>Appends the 搜打撤 room type to the lobby's create-room dialog (idempotent).</summary>
    private static void BuildCreateDialogPostfix(object __instance)
    {
        try
        {
            if (_roomTypeOptionField?.GetValue(__instance) is OptionButton option)
            {
                for (int i = 0; i < option.ItemCount; i++)
                {
                    if (option.GetItemId(i) == ExtractionRoomTypeId)
                    {
                        return;
                    }
                }

                option.AddItem(ExtractionLocalization.LanConnectRoomTypeText(), ExtractionRoomTypeId);
            }
        }
        catch (Exception ex)
        {
            Entry.Logger.Warn($"LanConnectCompat: BuildCreateDialog postfix failed: {ex.Message}");
        }
    }

    /// <summary>Mirrors the create-dialog selection into the launch flags; clears stale seed/modifier handoff values.</summary>
    private static void GetSelectedCreateGameModePostfix(object __instance)
    {
        try
        {
            bool extraction = _roomTypeOptionField?.GetValue(__instance) is OptionButton option &&
                              option.GetSelectedId() == ExtractionRoomTypeId;
            ExtractionRunContext.IsExtractionLaunch = extraction;
            ExtractionRunContext.HostCarrySetupRequired = extraction;
            if (extraction)
            {
                ExtractionRunContext.PendingSeed = null;
                ExtractionRunContext.PendingRunModifiers = null;
            }
        }
        catch (Exception ex)
        {
            Entry.Logger.Warn($"LanConnectCompat: GetSelectedCreateGameMode postfix failed: {ex.Message}");
        }
    }

    private static void CloseCreateDialogPostfix()
    {
        ClearLaunchFlags();
    }

    private static void OpenCreateDialogInternalPostfix()
    {
        // Every dialog open resets to Standard (the lobby Select(0)s the option), so no extraction launch is pending.
        ClearLaunchFlags();
    }

    private static void ClearLaunchFlags()
    {
        ExtractionRunContext.IsExtractionLaunch = false;
        ExtractionRunContext.HostCarrySetupRequired = false;
    }

    /// <summary>Reports the extraction room's opaque game-mode string during the create flow (room metadata + binding).</summary>
    private static bool GetLobbyGameModePrefix(ref string __result)
    {
        if (!ExtractionRunContext.IsExtractionLaunch)
        {
            return true;
        }

        __result = ExtractionGameMode;
        return false;
    }

    /// <summary>Maps the opaque extraction string to the localized room label (create toast / continue-run binding).</summary>
    private static void GetLobbyGameModeLabelPostfix(ref string __result)
    {
        if (string.Equals(__result, ExtractionGameMode, StringComparison.OrdinalIgnoreCase))
        {
            __result = ExtractionLocalization.LanConnectModeText();
        }
    }

    /// <summary>Renders a 搜打撤 pill for extraction rooms (the lobby hardcodes "STD" for unknown modes).</summary>
    private static void GetRoomGameModePillPostfix(
        string? gameMode,
        ref (string Text, Color Border, Color Background) __result)
    {
        if (string.Equals(gameMode?.Trim(), ExtractionGameMode, StringComparison.OrdinalIgnoreCase))
        {
            // Warm amber tint matching the lobby's card/border palette (BorderColor / SecondaryColor).
            __result = (
                ExtractionLocalization.LanConnectModeText(),
                new Color(0.80f, 0.65f, 0.53f, 1f),
                new Color(0.93f, 0.89f, 0.82f, 1f));
        }
    }
}
