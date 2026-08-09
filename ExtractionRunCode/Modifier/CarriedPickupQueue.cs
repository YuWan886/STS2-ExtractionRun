using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Runs;
using STS2RitsuLib.Utils;

namespace ExtractionRun.Modifier;

/// <summary>
/// Carried relics injected by <see cref="ExtractionModifier"/> have their 拾起时 (<see cref="RelicModel.AfterObtained"/>)
/// effect deferred until the run scene exists. The base game fires AfterObtained for every relic in hand during
/// <see cref="RunManager.FinalizeStartingRelics"/> — before <c>NRun</c> is created — so any carried relic whose pickup
/// opens a selection screen (e.g. 海克斯符文 forge-grant / transmute runes awaiting <see cref="NOverlayStack"/>) stalls run
/// start. Deferring the pickup to after the overlay stack exists keeps the pickup benefit while making run start safe.
/// The queue is process-local: every machine marks and drains its own injected instances, so the MP choice protocol
/// (which syncs who picked what) is unaffected.
/// 携带遗物（ExtractionModifier 注入）的“拾起时”效果推迟到跑局场景就绪后再执行。原版 FinalizeStartingRelics 会在 NRun
/// 创建之前对背包内每件遗物调用 AfterObtained，任何拾起时打开选择界面的携带遗物（如海克斯锻造/转换符文等待 NOverlayStack）
/// 都会卡死开局。推迟到叠层就绪既保留拾取收益又不卡开局。队列进程内唯一：每台机器只标记/排空自己注入的实例，
/// 联机选择协议不受影响。
/// </summary>
public static class CarriedPickupQueue
{
    private static readonly AttachedState<RelicModel, bool> Carried = new(() => false);
    private static readonly List<(RunState Run, RelicModel Relic)> Pending = new();
    private static CancellationTokenSource? _drainCts;

    /// <summary>True when the relic was injected as a carry (its pickup must be deferred). 是否为携带注入的遗物。</summary>
    public static bool IsCarried(RelicModel relic) => Carried.TryGetValue(relic, out bool carried) && carried;

    /// <summary>Marks a relic the modifier injected so the pickup pass defers it. 标记由 modifier 注入的携带遗物。</summary>
    public static void MarkCarried(RelicModel relic) => Carried.Set(relic, true);

    /// <summary>Drops pending pickups and cancels an in-flight drain (new-run creation, run end).
    /// 丢弃未执行的拾取并取消进行中的排空（新跑局创建、跑局结束时调用）。</summary>
    public static void Reset()
    {
        _drainCts?.Cancel();
        _drainCts = null;
        Pending.Clear();
    }

    private static void Enqueue(RunState runState, RelicModel relic) => Pending.Add((runState, relic));

    private static bool IsRunCurrent(RunState runState)
    {
        RunManager? manager = RunManager.Instance;
        return manager != null && ReferenceEquals(manager.State, runState);
    }

    private static bool IsOverlayReady()
    {
        NRun? run = NRun.Instance;
        return run != null && run.GlobalUi != null && run.GlobalUi.Overlays != null;
    }

    private static async Task DrainAsync(RunState runState)
    {
        CancellationTokenSource cts = _drainCts = new CancellationTokenSource();
        try
        {
            while (!IsOverlayReady())
            {
                cts.Token.ThrowIfCancellationRequested();
                if (!IsRunCurrent(runState))
                {
                    return;
                }

                await WaitForFrameOrDelayAsync(cts.Token);
            }

            (RunState Run, RelicModel Relic)[] snapshot = Pending.ToArray();
            Pending.Clear();
            foreach ((RunState queuedRun, RelicModel relic) in snapshot)
            {
                cts.Token.ThrowIfCancellationRequested();
                if (!ReferenceEquals(RunManager.Instance?.State, queuedRun))
                {
                    return;
                }

                try
                {
                    await relic.AfterObtained();
                }
                catch (Exception ex) when (IsSessionAbort(ex))
                {
                    Entry.Logger.Error($"CarriedPickupQueue: aborted drain after session failure in {relic.Id}: {ex.Message}");
                    return;
                }
                catch (Exception ex)
                {
                    Entry.Logger.Warn($"CarriedPickupQueue: skipping carried pickup that threw: {relic.Id}: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelled via Reset (new run / run end) — remaining pickups are dropped.
        }
        finally
        {
            if (ReferenceEquals(_drainCts, cts))
            {
                _drainCts = null;
            }
        }
    }

    /// <summary>Waits for the next process frame (16ms fallback when the tree is gone). Mirrors 海克斯符文's wait helper:
    /// continuations resume on the main thread through the engine sync context, so game state may be touched safely.</summary>
    private static async Task WaitForFrameOrDelayAsync(CancellationToken ct)
    {
        SceneTree? tree = NGame.Instance?.GetTree();
        if (tree == null)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(16), ct);
            return;
        }

        TaskCompletionSource<bool> frame = new(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnProcessFrame() => frame.TrySetResult(true);
        tree.ProcessFrame += OnProcessFrame;
        try
        {
            await Task.WhenAny(frame.Task, Task.Delay(TimeSpan.FromMilliseconds(16), ct));
        }
        finally
        {
            if (GodotObject.IsInstanceValid(tree))
            {
                tree.ProcessFrame -= OnProcessFrame;
            }
        }
    }

    private static bool IsSessionAbort(Exception ex)
    {
        if (ex is OperationCanceledException)
        {
            return true;
        }

        // HextechChoiceProtocolException is the hex mod's MP transaction-abort signal; matched by name since
        // ExtractionRun cannot reference that assembly.
        string? fullName = ex.GetType().FullName;
        return fullName != null && fullName.EndsWith(".HextechChoiceProtocolException", StringComparison.Ordinal);
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.FinalizeStartingRelics))]
    private static class FinalizeStartingRelicsPatch
    {
        private static bool Prefix(RunManager __instance, ref Task __result)
        {
            RunState? state = __instance.State;
            if (state == null || !state.Players.Any(p => p.Relics.Any(IsCarried)))
            {
                return true; // nothing deferred — keep the vanilla pickup pass untouched
            }

            __result = FinalizeAsync(state);
            return false;
        }

        private static async Task FinalizeAsync(RunState state)
        {
            foreach (Player player in state.Players)
            {
                foreach (RelicModel relic in player.Relics.ToList())
                {
                    if (IsCarried(relic))
                    {
                        Enqueue(state, relic);
                    }
                    else
                    {
                        await relic.AfterObtained();
                    }
                }
            }

            if (Pending.Count > 0)
            {
                _ = DrainAsync(state);
            }
        }
    }
}
