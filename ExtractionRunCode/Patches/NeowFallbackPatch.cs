using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Runs;
using ExtractionRun.Modifier;

namespace ExtractionRun.Patches;

/// <summary>
/// Neow only offers its starting bonus when the run has no modifiers, or when at least one modifier contributes a
/// Neow option (<see cref="ModifierModel.GenerateNeowOption"/>). The extraction modifier contributes none — every
/// vanilla modifier overrides GenerateNeowOption, the extraction modifier doesn't — so an extraction run reaches Neow
/// with an empty option list and the event is pre-finished: no starting bonus is ever offered. This patch makes Neow
/// fall back to the standard starting-bonus options when the run carries the extraction modifier (and no modifier
/// offers a Neow option).
/// 原版 Neow 只在「无 modifier」或「有 modifier 提供 Neow 选项」时给出开局馈赠；搜打撤 modifier 不提供（原版每个 modifier
/// 都覆写 GenerateNeowOption，而本 mod 没有），于是搜打撤局到达 Neow 时选项为空、事件被直接提前结束，拿不到任何开局
/// 馈赠。本补丁仅在搜打撤局（modifiers 含 ExtractionModifier 且无任何 modifier 提供 Neow 选项）时，让 Neow 回落到标准
/// 开局馈赠选项。。
/// </summary>
[HarmonyPatch(typeof(Neow), "GenerateInitialOptions")]
public static class NeowFallbackPatch
{
    /// <summary>Prefix→finalizer handoff: the run whose modifiers are temporarily hidden during standard generation.</summary>
    private static RunState? _runState;
    private static IReadOnlyList<ModifierModel>? _savedModifiers;

    private static void Prefix(Neow __instance)
    {
        RunState? rs = __instance.Owner?.RunState as RunState;
        if (rs == null || !rs.Modifiers.Any(m => m is ExtractionModifier) ||
            rs.Modifiers.Any(m => m.GenerateNeowOption(__instance) != null))
        {
            return; // not an extraction run, or a modifier already offers a Neow option — vanilla behavior
        }

        // No modifier offers a Neow option → the original would return an empty list. Hide the modifiers for the
        // duration of the call so the original runs its standard-options branch; the finalizer restores them.
        _runState = rs;
        _savedModifiers = rs.Modifiers;
        SetModifiers(rs, Array.Empty<ModifierModel>());
    }

    private static void Finalizer()
    {
        if (_runState != null)
        {
            SetModifiers(_runState, _savedModifiers ?? Array.Empty<ModifierModel>());
            _runState = null;
            _savedModifiers = null;
        }
    }

    private static void SetModifiers(RunState rs, IReadOnlyList<ModifierModel> value)
    {
        typeof(RunState).GetProperty(nameof(RunState.Modifiers))!.GetSetMethod(true)!.Invoke(rs, new object[] { value });
    }
}
