using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using ExtractionRun.Modifier;

namespace ExtractionRun.Patches;

/// <summary>
/// Turns the modifier-marked `?` point into a real 撤离点 node:
/// <list type="bullet">
/// <item><c>RollRoomTypeFor</c> prefix — the marked point always resolves to <see cref="RoomType.Event"/> (it stays a
/// `?` on the map; no new room type, so the event is injected by <c>ExtractionModifier.ModifyNextEvent</c>).</item>
/// <item><c>NNormalMapPoint.UpdateIcon</c> postfix — the marked point shows the custom 撤离点 icon instead of the
/// generic `?` / resolved icon.</item>
/// </list>
/// Both are gated on the run carrying the extraction modifier with a marked coord, so every other run and node renders
/// exactly as vanilla.
/// 把 modifier 标记的 `?` 点变成真正的撤离点节点：RollRoomTypeFor 前缀——标记点恒解析为 Event 房间（地图上仍是 `?`，不新增
/// 房间类型，事件由 ExtractionModifier.ModifyNextEvent 注入）；NNormalMapPoint.UpdateIcon 后缀——标记点显示自定义撤离点图标
/// 而非通用 `?`。两者都仅在搜打撤局且存在标记点时生效，其余局面渲染与原版完全一致。
/// </summary>
public static class ExtractionPointMapPatch
{
    private const string IconPath = "res://ExtractionRun/images/ui/extraction_point_node.png";

    /// <summary>Canonical custom node icon (cached once). 撤离点节点图标（只加载一次）。</summary>
    private static Texture2D? _iconTexture;

    [HarmonyPatch(typeof(RunManager), "RollRoomTypeFor")]
    private static class RollRoomTypeForPatch
    {
        private static bool Prefix(RunManager __instance, MapPointType pointType, ref RoomType __result)
        {
            if (pointType != MapPointType.Unknown)
            {
                return true;
            }

            RunState? state = RunManager.Instance?.State;
            MapCoord? marked = state?.Modifiers.OfType<ExtractionModifier>().FirstOrDefault()?.MarkedCoord;
            if (marked != null && state?.CurrentMapPoint?.coord == marked.Value)
            {
                // The party is entering the extraction point — force it to an event room (the event content itself is
                // substituted by ModifyNextEvent). Skipping the modifier check above leaves every other `?` untouched.
                // 队伍正在进入撤离点——强制为事件房（事件内容由 ModifyNextEvent 替换）。非撤离点 `?` 完全不受影响。
                __result = RoomType.Event;
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(NNormalMapPoint), "UpdateIcon")]
    private static class MapNodeIconPatch
    {
        private static void Postfix(NNormalMapPoint __instance)
        {
            RunState? state = RunManager.Instance?.State;
            MapCoord? marked = state?.Modifiers.OfType<ExtractionModifier>().FirstOrDefault()?.MarkedCoord;
            if (marked == null || __instance.Point.coord != marked.Value)
            {
                return;
            }

            Texture2D? texture = _iconTexture ??= TryLoadIcon();
            if (texture == null)
            {
                return;
            }

            try
            {
                TextureRect? icon = Traverse.Create(__instance).Field("_icon").GetValue<TextureRect>();
                if (icon != null)
                {
                    icon.Texture = texture;
                }
            }
            catch (Exception)
            {
                // Field shape drift across game versions — never break the map for a cosmetic icon.
            }
        }

        private static Texture2D? TryLoadIcon()
        {
            try
            {
                return ResourceLoader.Load<Texture2D>(IconPath);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    [HarmonyPatch(typeof(EventModel), nameof(EventModel.CreateInitialPortrait))]
    private static class EventPortraitPatch
    {
        /// <summary>
        /// The event's portrait path resolves to the base game's <c>res://images/events/</c> — a mod can't write there.
        /// Redirect the extraction event's portrait to the mod's own asset. 事件立绘路径解析到基础游戏的 res://images/events/，
        /// mod 写不进去——把撤离点事件的立绘改指 mod 自身资源。
        /// </summary>
        private static bool Prefix(EventModel __instance, ref Texture2D __result)
        {
            if (__instance is not ExtractionPointEvent)
            {
                return true;
            }

            Texture2D? texture = null;
            try
            {
                texture = ResourceLoader.Load<Texture2D>(ExtractionPointEvent.PortraitPath);
            }
            catch (Exception)
            {
            }

            if (texture == null)
            {
                return true; // fall back to the original (also missing) — behaves like vanilla
            }

            __result = texture;
            return false;
        }
    }
}
