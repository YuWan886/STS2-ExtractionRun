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
    /// <summary>
    /// 0.113 appended <c>CardSelectorStack</c> to this constructor (and made the trailing args optional), so the old
    /// frozen 4-parameter signature no longer exists. Match the stable prefix (options, count, player) instead of an
    /// exact signature, so 0.107 ~ 0.111 and 0.113+ all resolve. Returning null here aborts Harmony.PatchAll for the
    /// whole assembly, which silently disabled every patch class ordered after this one.
    /// </summary>
    private static ConstructorInfo? TargetMethod()
        => AccessTools.GetDeclaredConstructors(typeof(CardReward)).FirstOrDefault(ctor =>
        {
            ParameterInfo[] parameters = ctor.GetParameters();
            return parameters.Length >= 3
                   && parameters[0].ParameterType == typeof(CardCreationOptions)
                   && parameters[1].ParameterType == typeof(int)
                   && parameters[2].ParameterType == typeof(Player);
        });

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
