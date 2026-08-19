using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using ExtractionRun.Modifier;

namespace ExtractionRun.Compatibility;

/// <summary>
/// Routes an extraction run's resume lobby through the ordinary multiplayer waiting screen. The run remains custom
/// internally because it carries <see cref="ExtractionModifier"/>; only the resume-screen route is standard.
/// 搜打撤存档仍以 ExtractionModifier 标记为自定义局；这里只把读档重连阶段改走普通多人等待界面。
/// </summary>
internal static class ExtractionReconnectCompat
{
    private const string LobbyAssemblyName = "sts2_lan_connect";
    private const string SaveCompatibilityTypeName = "Sts2LanConnect.Scripts.LanConnectMultiplayerSaveCompatibility";

    private static readonly BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static bool _corePatchesInstalled;
    private static bool _lanConnectPatchInstalled;
    private static bool _deferredInstallQueued;

    /// <summary>Installs base-game resume routing plus the optional STS2-Game-Lobby bypass when available.</summary>
    public static void InstallIfNeeded(Harmony harmony)
    {
        InstallCorePatches(harmony);
        if (_lanConnectPatchInstalled || TryInstallLanConnectPatch(harmony))
        {
            return;
        }

        if (!_deferredInstallQueued)
        {
            _deferredInstallQueued = true;
            Callable.From(() => InstallIfNeeded(harmony)).CallDeferred();
        }
    }

    private static void InstallCorePatches(Harmony harmony)
    {
        if (_corePatchesInstalled)
        {
            return;
        }

        try
        {
            MethodInfo? gameModeGetter = AccessTools.PropertyGetter(typeof(LoadRunLobby), nameof(LoadRunLobby.GameMode));
            MethodInfo? startHostAsync = AccessTools.Method(typeof(NMultiplayerSubmenu), "StartHostAsync");
            AsyncStateMachineAttribute? stateMachine = startHostAsync?.GetCustomAttribute<AsyncStateMachineAttribute>();
            MethodInfo? startHostMoveNext = stateMachine?.StateMachineType.GetMethod("MoveNext", AnyStatic | BindingFlags.Instance);

            if (gameModeGetter == null || startHostMoveNext == null)
            {
                Entry.Logger.Warn("ExtractionReconnectCompat: base resume targets unavailable; keeping the game's default resume screen.");
                return;
            }

            harmony.Patch(
                gameModeGetter,
                prefix: new HarmonyMethod(typeof(ExtractionReconnectCompat), nameof(LoadRunLobbyGameModePrefix)));
            harmony.Patch(
                startHostMoveNext,
                transpiler: new HarmonyMethod(typeof(ExtractionReconnectCompat), nameof(StartHostAsyncTranspiler)));
            _corePatchesInstalled = true;
            Entry.Logger.Info("ExtractionReconnectCompat: installed standard waiting-screen routing for extraction resumes.");
        }
        catch (Exception ex)
        {
            Entry.Logger.Warn($"ExtractionReconnectCompat: failed to install base resume routing: {ex.Message}");
        }
    }

    private static bool TryInstallLanConnectPatch(Harmony harmony)
    {
        try
        {
            Assembly? assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(
                candidate => string.Equals(candidate.GetName().Name, LobbyAssemblyName, StringComparison.OrdinalIgnoreCase));
            Type? saveCompatibility = assembly?.GetType(SaveCompatibilityTypeName, throwOnError: false);
            MethodInfo? target = saveCompatibility?.GetMethod("PushLoadedRunScreen", AnyStatic);
            if (target == null)
            {
                return false;
            }

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(typeof(ExtractionReconnectCompat), nameof(LanConnectPushLoadedRunScreenPrefix)));
            _lanConnectPatchInstalled = true;
            Entry.Logger.Info("ExtractionReconnectCompat: installed STS2-Game-Lobby standard waiting-screen routing.");
            return true;
        }
        catch (Exception ex)
        {
            Entry.Logger.Warn($"ExtractionReconnectCompat: failed to install STS2-Game-Lobby resume routing: {ex.Message}");
            return false;
        }
    }

    /// <summary>For a reconnect handshake, report Standard so the peer opens NMultiplayerLoadGameScreen.</summary>
    private static bool LoadRunLobbyGameModePrefix(LoadRunLobby __instance, ref GameMode __result)
    {
        if (!IsExtractionRun(__instance.Run))
        {
            return true;
        }

        __result = GameMode.Standard;
        return false;
    }

    /// <summary>Changes only the base game's host-side screen branch; the SerializableRun is not mutated.</summary>
    private static IEnumerable<CodeInstruction> StartHostAsyncTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo? gameModeGetter = AccessTools.PropertyGetter(typeof(SerializableRun), nameof(SerializableRun.GameMode));
        MethodInfo replacement = AccessTools.Method(typeof(ExtractionReconnectCompat), nameof(GetResumeScreenGameMode))!;

        foreach (CodeInstruction instruction in instructions)
        {
            if (gameModeGetter != null && instruction.Calls(gameModeGetter))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = replacement;
            }

            yield return instruction;
        }
    }

    /// <summary>Returns Standard only for selecting the resume screen; the saved run's original mode remains intact.</summary>
    private static GameMode GetResumeScreenGameMode(SerializableRun run)
    {
        return IsExtractionRun(run) ? GameMode.Standard : run.GameMode;
    }

    /// <summary>STS2-Game-Lobby owns a separate resume-screen factory, so bypass its custom-screen branch too.</summary>
    private static bool LanConnectPushLoadedRunScreenPrefix(
        NSubmenuStack stack,
        NetHostGameService netService,
        SerializableRun run)
    {
        if (!IsExtractionRun(run))
        {
            return true;
        }

        NMultiplayerLoadGameScreen submenu = stack.GetSubmenuType<NMultiplayerLoadGameScreen>();
        submenu.InitializeAsHost(netService, run);
        stack.Push(submenu);
        return false;
    }

    private static bool IsExtractionRun(SerializableRun? run)
    {
        if (run == null)
        {
            return false;
        }

        try
        {
            ModelId extractionId = ModelDb.Modifier<ExtractionModifier>().Id;
            return run.Modifiers.Any(modifier => modifier.Id == extractionId);
        }
        catch (Exception ex)
        {
            Entry.Logger.Warn($"ExtractionReconnectCompat: could not inspect resume modifiers: {ex.Message}");
            return false;
        }
    }
}
