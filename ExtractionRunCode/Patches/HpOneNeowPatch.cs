using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using ExtractionRun.Modifier;

namespace ExtractionRun.Patches;

[HarmonyPatch(typeof(AncientEventModel), "BeforeEventStarted")]
public static class HpOneNeowPatch
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(AncientEventModel __instance, bool isPreFinished, ref Task __result)
    {
        if (isPreFinished || __instance is not Neow || __instance.Owner is not Player player)
        {
            return;
        }

        ExtractionModifier? modifier = player.RunState.Modifiers.OfType<ExtractionModifier>().FirstOrDefault();
        if (modifier?.Challenges.StartingMaxHp is not int maxHp)
        {
            return;
        }

        __result = RestoreHpAfterNeowAsync(__result, player, maxHp);
    }

    private static async Task RestoreHpAfterNeowAsync(Task beforeEventStarted, Player player, int maxHp)
    {
        await beforeEventStarted;
        player.Creature.SetMaxHpInternal(maxHp);
        player.Creature.SetCurrentHpInternal(maxHp);
    }
}
