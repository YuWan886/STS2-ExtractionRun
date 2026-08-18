using HarmonyLib;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using ExtractionRun.Data;
using ExtractionRun.Modifier;

namespace ExtractionRun.UI;

/// <summary>
/// Applies challenge map rules: the ALL_ELITE challenge converts every Monster point into an Elite room at room-roll
/// time (the same <c>RunManager.RollRoomTypeFor</c> funnel the 撤离点 patch uses for its `?`). Boss/Elite/Ancient
/// points and the extraction point's `?` are untouched — only ordinary Monster points go elite, deterministically on
/// every machine (no RNG, a pure point-type rewrite).
/// 挑战地图规则：ALL_ELITE 挑战在房间 roll 时把每个 Monster 点变成精英房间（与撤离点补丁共用 RunManager.RollRoomTypeFor 漏斗）。
/// Boss/Elite/Ancient 点与撤离点的 `?` 不动——只有普通 Monster 点变精英，全机器确定性一致（无随机，纯点类型改写）。
/// </summary>
public static class ChallengeMapPatch
{
    [HarmonyPatch(typeof(RunManager), "RollRoomTypeFor")]
    private static class AllElitePatch
    {
        private static bool Prefix(RunManager __instance, MapPointType pointType, ref RoomType __result)
        {
            if (pointType != MapPointType.Monster)
            {
                return true; // elites/bosses/`?`/rest/shop untouched
            }

            RunState? state = RunManager.Instance?.State;
            ExtractionModifier? modifier = state?.Modifiers.OfType<ExtractionModifier>().FirstOrDefault();
            if (modifier?.Effects.HasFlag(ChallengeEffects.AllElite) != true)
            {
                return true;
            }

            __result = RoomType.Elite;
            return false;
        }
    }
}