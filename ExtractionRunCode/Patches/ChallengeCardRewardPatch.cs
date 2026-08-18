using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using ExtractionRun.Modifier;

namespace ExtractionRun.Patches;

/// <summary>Reduces standard generated card rewards to the selected permanent challenge's choice count.</summary>
[HarmonyPatch]
public static class ChallengeCardRewardPatch
{
    private static ConstructorInfo? TargetMethod() => AccessTools.Constructor(typeof(CardReward),
        [typeof(CardCreationOptions), typeof(int), typeof(Player), typeof(PlayerChoiceSynchronizer)]);

    private static void Prefix(ref int cardCount)
    {
        ExtractionModifier? modifier = RunManager.Instance?.State?.Modifiers.OfType<ExtractionModifier>()
            .FirstOrDefault();
        int? limit = modifier?.Challenges.CardRewardChoiceCount;
        if (limit is > 0)
        {
            cardCount = Math.Min(cardCount, limit.Value);
        }
    }
}
