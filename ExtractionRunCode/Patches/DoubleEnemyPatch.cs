using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Runs;
using ExtractionRun.Data;
using ExtractionRun.Modifier;

namespace ExtractionRun.Compatibility;

/// <summary>
/// Engine F (DOUBLE_ENEMY) — HP arm. A postfix on <c>CombatState.CreateCreature</c> doubles every enemy's max HP and
/// fills its current HP to the new max. <c>CreateCreature</c> is the single funnel through which every enemy spawns —
/// initial combat (<c>CombatRoom.StartCombat</c>), mid-combat summons (<c>CreatureCmd.Add</c>) and event fights — so
/// the postfix covers all of them, whereas an <c>AfterCreatureAddedToCombat</c> override only fires for creatures added
/// mid-combat. Runs on the mutable combat copy (never the ModelDb canonical instance), gated on the DOUBLE_ENEMY
/// challenge; bosses included (grill-locked). Identical on every machine via the run's own combat creation.
/// 引擎 F（DOUBLE_ENEMY）血量臂：CombatState.CreateCreature 后缀，把每只敌人的最大血量翻倍并补满当前血量。CreateCreature
/// 是敌人生成的唯一漏斗（初始战斗 / 中途召唤 / 事件战），后缀覆盖全部路径——AfterCreatureAddedToCombat override 只对战斗
/// 中途添加的生物触发，覆盖不全。操作可变战斗副本（绝不碰 ModelDb 规范实例），以 DOUBLE_ENEMY 挑战门控；Boss 也翻倍。
/// </summary>
[HarmonyPatch(typeof(CombatState), "CreateCreature")]
public static class DoubleEnemyPatch
{
    private static void Postfix(Creature __result)
    {
        if (__result.Side == CombatSide.Player)
        {
            return;
        }

        RunState? state = RunManager.Instance?.State;
        ExtractionModifier? modifier = state?.Modifiers.OfType<ExtractionModifier>().FirstOrDefault();
        if (modifier?.Effects.HasFlag(ChallengeEffects.DoubleEnemy) != true)
        {
            return;
        }

        __result.SetMaxHpInternal(__result.MaxHp * 2);
        __result.SetCurrentHpInternal(__result.MaxHp);
    }
}
