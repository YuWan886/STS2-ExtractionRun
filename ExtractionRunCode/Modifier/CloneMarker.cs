using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using STS2RitsuLib.Utils;

namespace ExtractionRun.Modifier;

/// <summary>
/// Tracks every card produced by a run-level clone (<see cref="RunState.CloneCard"/>) so the 搜打撤 deposit filter can
/// exclude the FREE duplicates (Dolly's Mirror, rest-site clone, Reflections event, Hoarder/Specialized). Run-level
/// clones do NOT set <see cref="CardModel.IsClone"/> — unlike combat clones (<c>CreateClone</c>/<c>CreateDupe</c>) — so
/// we stamp them here. Two follow-up postfixes then un-stamp clones that are the EARNED copy: a relic that clones a
/// reward card to modify it (egg relics, WingCharm, SilverCrucible, Glitter, LavaLamp, SilkenTress, FresnelLens)
/// replaces the offered card with the clone — the original is discarded — so excluding it would lose legitimate loot.
/// The marker is in-memory only (the card instance persists for the whole run, which is all the deposit filter needs).
/// 追踪所有经 RunState.CloneCard 产生的跑局级克隆，使撤离结算过滤掉其中的"免费复制品"（Dolly's Mirror、篝火克隆、
/// Reflections 事件、Hoarder/Specialized）。跑局级克隆不设 IsClone（战斗克隆 CreateClone/CreateDupe 才会），故在此打标。
/// 随后两个补丁会撤销"实际所得卡"的标记：遗物为修改奖励牌而克隆（蛋遗物、WingCharm、SilverCrucible、Glitter、
/// LavaLamp、SilkenTress、FresnelLens）会用克隆体替换 offer（原始牌被丢弃），继续排除会丢掉正当战利品。该标记仅存内存
/// （整局有效），足以供结算过滤使用。
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

    /// <summary>Clears a clone's mark so the deposit keeps it — the card is the actual earned copy, not a free duplicate.</summary>
    private static void AllowDeposit(CardModel card)
    {
        Marked.Remove(card);
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

    /// <summary>
    /// Harmony Postfix on <see cref="CardCreationResult.ModifyCard(CardModel, RelicModel)"/>: a card that becomes the
    /// offered reward card IS the earned card. Relics that clone a reward to modify it (egg relics MoltenEgg/FrozenEgg/
    /// ToxicEgg, WingCharm, SilverCrucible, Glitter, LavaLamp, SilkenTress, FresnelLens) all route through this two-arg
    /// overload — the one relics use — and the original offer is discarded, so the clone is the single copy the player
    /// earns. Unmarking keeps it depositable; a never-marked card is a no-op.
    /// 结算奖励修改的遗物克隆：遗物把克隆体设为实际 offer（蛋遗物/WingCharm/SilverCrucible/Glitter/LavaLamp/SilkenTress/
    /// FresnelLens 都走这个双参重载），原始 offer 被丢弃，克隆体即玩家唯一所得。撤销标记使其可入库；未打标的卡为空操作。
    /// </summary>
    [HarmonyPatch(typeof(CardCreationResult), nameof(CardCreationResult.ModifyCard), new[] { typeof(CardModel), typeof(RelicModel) })]
    private static class CardRewardModifiedPatch
    {
        private static void Postfix(CardCreationResult __instance)
        {
            if (__instance.Card is { } card)
            {
                AllowDeposit(card);
            }
        }
    }

    /// <summary>
    /// Harmony Postfix on <see cref="Hook.ModifyCardBeingAddedToDeck"/>: when a relic REPLACED a card being added to the
    /// deck with a modified clone (FrozenEgg's deck-add upgrade, FresnelLens's enchant), the replacement is the earned
    /// card — unmark it. Reference inequality (the hook returns a different instance) is the replacement signal; a
    /// free-duplicate clone added unmodified (Dolly's Mirror, Reflections) keeps its mark and stays excluded.
    /// 入组时被遗物替换的克隆（FrozenEgg 的入组升级、FresnelLens 的附魔）：替换体即所得卡，撤销标记。以引用不等（hook 返回了
    /// 不同实例）作为替换信号；未修改的免费复制品克隆（Dolly's Mirror、Reflections）保留标记，仍被排除。
    /// </summary>
    [HarmonyPatch(typeof(Hook), nameof(Hook.ModifyCardBeingAddedToDeck))]
    private static class CardAddedToDeckReplacedPatch
    {
        private static void Postfix(CardModel __result, CardModel card)
        {
            if (__result != null && !ReferenceEquals(__result, card))
            {
                AllowDeposit(__result);
            }
        }
    }
}
