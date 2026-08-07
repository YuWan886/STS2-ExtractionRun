using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using STS2RitsuLib.Utils;

namespace ExtractionRun.Modifier;

/// <summary>
/// Tracks every card produced by a run-level clone (<see cref="RunState.CloneCard"/>) so the 搜打撤 deposit filter can
/// exclude them. Run-level clones (Dolly's Mirror, egg relics, rest-site clone, Reflections event) do NOT set
/// <see cref="CardModel.IsClone"/> — unlike combat clones (<c>CreateClone</c>/<c>CreateDupe</c>) — so we stamp them here.
/// The marker is in-memory only (the card instance persists for the whole run, which is all the deposit filter needs).
/// 追踪所有经 RunState.CloneCard 产生的跑局级克隆，使撤离结算过滤掉它们。该标记仅存内存（整局有效），足以供结算过滤使用。
/// </summary>
public static class CloneMarker
{
    private static readonly AttachedState<CardModel, bool> Marked = new(() => false);

    /// <summary>True when <paramref name="card"/> must not be deposited: a combat clone, a dupe, or a tracked run-level clone.</summary>
    public static bool ShouldExclude(CardModel card)
    {
        return card.IsClone || card.IsDupe || Marked.TryGetValue(card, out bool marked) && marked;
    }

    private static void Mark(CardModel card)
    {
        Marked.Set(card, true);
    }

    /// <summary>Harmony Postfix on <see cref="RunState.CloneCard"/>: stamps every run-level clone.</summary>
    [HarmonyPatch(typeof(RunState), nameof(RunState.CloneCard))]
    private static class RunStateCloneCardPatch
    {
        private static void Postfix(CardModel __result)
        {
            if (__result != null)
            {
                Mark(__result);
            }
        }
    }
}
