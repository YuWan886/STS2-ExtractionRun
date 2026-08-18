using HarmonyLib;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using ExtractionRun.Data;
using ExtractionRun.Modifier;

namespace ExtractionRun.Patches;

/// <summary>
/// Safety net for the ALL_ELITE challenge: any Monster point that still reaches <c>RunManager.RollRoomTypeFor</c> at
/// entry time resolves to an Elite room. The map rewrite in <c>ExtractionModifier.AfterMapGenerated</c> already converts
/// every generated Monster point (including the structural row-1 starters), so this only fires for Monster points
/// assigned AFTER that hook — e.g. the base game's act-0 starting-point override in <c>RunManager.GenerateMap</c> when
/// the Neow epoch isn't revealed. `?` points are never touched: their pointType stays Unknown at roll time, so vanilla
/// odds hold by construction.  ALL_ELITE 挑战的进房兜底：任何在进房时仍是 Monster 的点都解析为精英房间。地图重写已转换
/// 所有已生成的 Monster 点（含结构性的第 1 行起始怪），此处只罩住钩子之后才赋值成 Monster 的点——如 Neow 未解锁时
/// 基础游戏在 GenerateMap 里对 act0 起点的后置覆盖。`?` 点从不被触碰（roll 时 pointType 仍是 Unknown），原版概率天然保留。
/// </summary>
public static class ChallengeMapPatch
{
    [HarmonyPatch(typeof(RunManager), "RollRoomTypeFor")]
    private static class AllEliteSafetyNetPatch
    {
        private static bool Prefix(RunManager __instance, MapPointType pointType, ref RoomType __result)
        {
            if (pointType != MapPointType.Monster)
            {
                return true; // `?`/elite/boss/rest untouched — the map rewrite already handles generated monsters
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