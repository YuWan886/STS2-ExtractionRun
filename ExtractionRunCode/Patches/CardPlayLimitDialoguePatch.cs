using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using ExtractionRun.Modifier;
using ExtractionRun.UI;

namespace ExtractionRun.Patches;

/// <summary>Supplies a dialogue line when the extraction modifier blocks a card-play-limit challenge attempt.</summary>
[HarmonyPatch]
public static class CardPlayLimitDialoguePatch
{
    private static MethodBase? TargetMethod()
    {
        Type? extensions = AccessTools.TypeByName("MegaCrit.Sts2.Core.Entities.Cards.UnplayableReasonExtensions");
        return extensions == null
            ? null
            : AccessTools.Method(extensions, "GetPlayerDialogueLine",
                [typeof(UnplayableReason), typeof(AbstractModel)]);
    }

    private static bool Prefix(UnplayableReason reason, AbstractModel? preventer, ref LocString? __result)
    {
        if (!reason.HasFlag(UnplayableReason.BlockedByHook)
            || reason.HasFlag(UnplayableReason.NoLivingAllies)
            || reason.HasFlag(UnplayableReason.EnergyCostTooHigh)
            || reason.HasFlag(UnplayableReason.StarCostTooHigh)
            || preventer is not ExtractionModifier modifier
            || modifier.Challenges.CardPlayLimitPerTurn is not int limit)
        {
            return true;
        }

        LocString dialogue = new(ExtractionLocalization.UiTable, "EXTRACTION_RUN.challenge.cardPlayLimitReached");
        dialogue.Add("Limit", limit);
        __result = dialogue;
        return false;
    }
}
