using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Saves;
using ExtractionRun.UI;

namespace ExtractionRun.Patches;

/// <summary>
/// Hooks the 搜刮 loot-search reveal into the reward screens (card reward choice, treasure chest).
/// Every patch gates on <see cref="LootSearch.ShouldRun"/> (setting ON + extraction run),
/// then hides the items and covers them with their final-position gray rects at open; <see cref="LootSearch.Play"/> waits
/// out the screen's entrance before revealing all of them at once, each as its own duration elapses.
/// One round per screen open; re-rolls never replay.
/// 把搜刮揭示动画挂进奖励界面（卡牌三选一、宝箱遗物）。每个补丁先过 LootSearch.ShouldRun 门（设置开启 + 搜打撤局），
/// 开场即隐藏物品并按最终位置铺上灰格；LootSearch.Play 等该界面入场动画播完再同时揭示全部，各按自身时长。
/// 每屏打开只播一轮，重 roll 不重播。
/// </summary>
public static class LootSearchPatch
{
    /// <summary>Card fly-in tween is 1s (modulate); wait it out before revealing. 卡牌飞入 tween 为 1s。</summary>
    private const float CardEntranceSeconds = 1.0f;

    /// <summary>Treasure holders animate in over up to ~1s (0.6s + 0.2–0.4s per-holder delay); wait it out plus a frame
    /// margin. 宝箱遗物入场最长约 1s；等其结束（留一帧余量）。</summary>
    private const float TreasureEntranceSeconds = 1.1f;

    [HarmonyPatch(typeof(NCardRewardSelectionScreen), nameof(NCardRewardSelectionScreen.AfterOverlayOpened))]
    private static class CardRewardSelectionPatch
    {
        private static void Postfix(NCardRewardSelectionScreen __instance)
        {
            if (!LootSearch.ShouldRun())
            {
                return;
            }

            // Hide the offered cards immediately and cover them with their final-slot gray rects — the search reveal is
            // the first time a card is seen. 开场即隐藏卡牌并按最终槽位铺上灰格——搜刮揭示成为首次亮相。
            HideCards(__instance);
            List<LootSearchEntry> entries = CollectCards(__instance);
            if (entries.Count == 0)
            {
                return;
            }

            // The screen frees itself on close, so the overlay dies with it.
            LootSearch.Play(__instance, entries, CardEntranceSeconds, () => __instance.IsInsideTree());
        }

        /// <summary>Builds the search entries at open time. Each holder's final rect is its current rect shifted to the
        /// fly-in target: the holders tween from their origin to vector + Right*350*i — a pure X translation in
        /// _cardRow local space (scale 1), so the final rect = current rect + (finalX − position.X, 0). Child order is
        /// left → right. 开场时构建搜刮条目。每个容器的最终矩形 = 当前矩形平移到飞入目标：容器从原点 tween 到
        /// vector + Right*350*i——纯 X 平移（_cardRow 局部空间，缩放 1），故最终矩形 = 当前 + (finalX − position.X, 0)。
        /// 子节点顺序即从左到右。</summary>
        private static List<LootSearchEntry> CollectCards(NCardRewardSelectionScreen screen)
        {
            Control? row = Traverse.Create(screen).Field("_cardRow").GetValue<Control>();
            if (row == null)
            {
                return new();
            }

            List<NGridCardHolder> holders = row.GetChildren()
                .OfType<NGridCardHolder>()
                .Where(h => GodotObject.IsInstanceValid(h) && h.Hitbox != null)
                .ToList();

            float startX = -(holders.Count - 1) * 175f; // Left*(count-1)*350*0.5 → the leftmost slot
            var entries = new List<LootSearchEntry>(holders.Count);
            for (int i = 0; i < holders.Count; i++)
            {
                NGridCardHolder holder = holders[i];
                // The holder/card roots are 0-sized (the card renders via the centered Hitbox child), so bound the
                // search rect on the Hitbox. 卡牌容器根节点 0 尺寸（卡牌由居中的 Hitbox 子节点渲染），故搜刮矩形以 Hitbox 为基准。
                Rect2 current = holder.Hitbox!.GetGlobalRect();
                float finalX = startX + 350f * i;
                entries.Add(new LootSearchEntry(
                    holder, LootSearch.DurationFor(holder.CardModel.Rarity),
                    new Rect2(current.Position + new Vector2(finalX - holder.Position.X, 0), current.Size),
                    holder.CardNode));
            }

            return entries;
        }

        /// <summary>Hides the offered cards (the NCard visuals, not the holders — the holder keeps focus/nav and the
        /// collect filter below reads its Hitbox) so the fly-in plays unseen.
        /// 隐藏提供的卡牌视觉（NCard 而非容器——容器保持焦点/导航，下方收集逻辑要读它的 Hitbox），让飞入在不可见状态下播完。</summary>
        private static void HideCards(NCardRewardSelectionScreen screen)
        {
            Control? row = Traverse.Create(screen).Field("_cardRow").GetValue<Control>();
            if (row == null)
            {
                return;
            }

            foreach (NGridCardHolder holder in row.GetChildren().OfType<NGridCardHolder>())
            {
                if (holder.CardNode != null && GodotObject.IsInstanceValid(holder.CardNode))
                {
                    holder.CardNode.Visible = false;
                }
            }
        }
    }

    [HarmonyPatch(typeof(NTreasureRoomRelicCollection), nameof(NTreasureRoomRelicCollection.AnimIn))]
    private static class TreasureRelicCollectionPatch
    {
        private static void Postfix(NTreasureRoomRelicCollection __instance)
        {
            if (!LootSearch.ShouldRun())
            {
                return;
            }

            // The one-time relic FTUE modal opens on top of the collection and would cover the search — skip that room.
            if (!SaveManager.Instance.SeenFtue("obtain_relic_ftue"))
            {
                return;
            }

            // Hide each active relic immediately and cover it with its final-position gray rect — the search reveal is
            // the first time it's seen. 开场即隐藏各激活遗物并按最终位置铺上灰格——搜刮揭示成为首次亮相。
            HideRelics(__instance);
            List<LootSearchEntry> entries = CollectRelics(__instance);
            if (entries.Count == 0)
            {
                return;
            }

            LootSearch.Play(__instance, entries, TreasureEntranceSeconds, () => __instance.IsInsideTree());
        }

        /// <summary>Builds the search entries at AnimIn time. AnimIn drops each holder down by num (150 single / 50 MP)
        /// then animates it back up — the final rect is the open-time rect minus num on Y. HideTarget is the relic node:
        /// it's hidden up front (HideRelics), so the reveal must restore it too, not just the holder.
        /// 开场时构建搜刮条目。AnimIn 先把容器下移 num（单人 150 / 多人 50）再动画移回——最终矩形 = 开启时 − num·Y。
        /// 隐藏目标为遗物节点：开场已隐藏（HideRelics），揭示时需一并还原它。</summary>
        private static List<LootSearchEntry> CollectRelics(NTreasureRoomRelicCollection collection)
        {
            List<NTreasureRoomRelicHolder> holders = Traverse.Create(collection).Field("_holdersInUse")
                .GetValue<List<NTreasureRoomRelicHolder>>();
            if (holders == null)
            {
                return new();
            }

            float num = holders.Count == 1 ? 150f : 50f;
            var entries = new List<LootSearchEntry>();
            foreach (NTreasureRoomRelicHolder holder in holders
                         .Where(h => GodotObject.IsInstanceValid(h) && h.Visible)
                         .OrderBy(h => h.GetGlobalRect().Position.X))
            {
                Rect2 current = holder.GetGlobalRect();
                Rect2 final = new Rect2(current.Position - new Vector2(0, num), current.Size);
                entries.Add(new LootSearchEntry(holder, LootSearch.DurationFor(holder.Relic.Model.Rarity), final, holder.Relic));
            }
            return entries;
        }

        /// <summary>Hides each active holder's relic node. The holder itself stays visible — _holdersInUse also holds
        /// the inactive (Visible=false) multiplayer slots, so the Visible filter below still needs them distinguishable.
        /// 隐藏各激活容器的遗物节点。容器本身保持可见——_holdersInUse 也含不可用的多人槽位（Visible=false），
        /// 下方收集逻辑仍要靠 Visible 过滤区分。</summary>
        private static void HideRelics(NTreasureRoomRelicCollection collection)
        {
            List<NTreasureRoomRelicHolder> holders = Traverse.Create(collection).Field("_holdersInUse")
                .GetValue<List<NTreasureRoomRelicHolder>>();
            if (holders == null)
            {
                return;
            }

            foreach (NTreasureRoomRelicHolder holder in holders)
            {
                if (GodotObject.IsInstanceValid(holder) && holder.Visible
                    && holder.Relic != null && GodotObject.IsInstanceValid(holder.Relic))
                {
                    holder.Relic.Visible = false;
                }
            }
        }
    }
}
