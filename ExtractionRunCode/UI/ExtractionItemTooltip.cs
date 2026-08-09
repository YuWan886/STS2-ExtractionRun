using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using ExtractionRun.Settings;

namespace ExtractionRun.UI;

/// <summary>
/// Vanilla-hover-tip glue for the 搜打撤 item tiles. The tile carries its current item's <see cref="ModelId"/> string in
/// Meta (pooled buttons can't hold arbitrary C# objects, and models aren't Godot Variants), and the single hover handler
/// attached once per tile rebuilds the tips for whatever item the tile currently shows. Cards get a text tip (name +
/// fully-resolved description) plus keyword tips and are marked seen like relics/potions; relics/potions reuse their
/// model's own tips. Each kind is gated by its own <see cref="ExtractionSettings"/> toggle. The vanilla set is created
/// into NGame's Layer-0 container, so it's hoisted onto the tile's own CanvasLayer — the hub/settlement/import dialog
/// are Layers 100/200 whose opaque panels would otherwise hide it.
/// 搜打撤物品瓦片的原版悬停提示粘合：瓦片把当前物品的 ModelId 字符串存进 Meta（池化按钮无法持有任意 C# 对象，模型也不是
/// Godot Variant），每个瓦片只挂一次悬停处理器，按瓦片当前内容重建提示。卡牌为文字提示（名称 + 完整解析的描述）加关键词，
/// 与遗物/药水一致地标记已见过；遗物/药水直接复用模型自带提示。三类各自受 ExtractionSettings 里独立开关控制。原版提示集默认
/// 挂到 NGame 的 Layer 0 容器，需搬到瓦片所在 CanvasLayer——仓库/结算/导入弹窗是 100/200 层，不搬会被不透明面板挡住。
/// </summary>
public static class ExtractionItemTooltip
{
    private const string MetaIdKey = "_tooltipId";

    private static readonly string CardCategory = ModelId.SlugifyCategory<CardModel>();
    private static readonly string RelicCategory = ModelId.SlugifyCategory<RelicModel>();
    private static readonly string PotionCategory = ModelId.SlugifyCategory<PotionModel>();

    /// <summary>
    /// Records the tile's current item id in Meta. Returns whether it changed — a recycled tile whose item changed must
    /// have its stale tooltip removed, while a same-item repopulate (background art landing) may keep it open.
    /// 记录瓦片当前物品 id 到 Meta；返回是否变化——换物品的回收瓦片要清掉旧提示，同物品重填（后台贴图落地）则保留。
    /// </summary>
    public static bool SetItem(Button tile, ModelId? id)
    {
        string value = id?.ToString() ?? "";
        bool changed = !tile.HasMeta(MetaIdKey) || tile.GetMeta(MetaIdKey).AsString() != value;
        if (changed)
        {
            tile.SetMeta(MetaIdKey, value);
        }

        return changed;
    }

    public static void Show(Button tile)
    {
        ModelId? id = ReadId(tile);
        if (id == null)
        {
            return;
        }

        AbstractModel? model = ResolveModel(id);
        if (model == null)
        {
            return;
        }

        ExtractionSettings settings = ExtractionSettingsPage.Current;
        bool enabled = model switch
        {
            CardModel => settings.ShowCardHoverTips,
            RelicModel => settings.ShowRelicHoverTips,
            PotionModel => settings.ShowPotionHoverTips,
            _ => false,
        };
        if (!enabled)
        {
            return;
        }

        try
        {
            IEnumerable<IHoverTip>? tips = BuildTips(model);
            if (tips == null)
            {
                return;
            }

            NHoverTipSet? set = NHoverTipSet.CreateAndShow(tile, tips, HoverTipAlignment.Right);
            if (set == null)
            {
                return;
            }

            CanvasLayer? canvas = FindCanvasLayer(tile);
            if (canvas != null && set.GetParent() != canvas)
            {
                set.Reparent(canvas, keepGlobalTransform: true);
            }

            set.SetFollowOwner();
        }
        catch (Exception)
        {
            NHoverTipSet.Remove(tile);
        }
    }

    /// <summary>Closes any tooltip tied to the tile. Safe to call on every repopulate — a no-op when none is showing.
    /// 关闭绑定该瓦片的提示；可在每次重填/释放时安全调用（无提示时为无操作）。</summary>
    public static void Hide(Button tile) => NHoverTipSet.Remove(tile);

    private static ModelId? ReadId(Button tile)
    {
        if (!tile.HasMeta(MetaIdKey))
        {
            return null;
        }

        string raw = tile.GetMeta(MetaIdKey).AsString();
        int dot = raw.IndexOf('.');
        if (dot <= 0 || dot >= raw.Length - 1)
        {
            return null; 
        }

        return new ModelId(raw[..dot], raw[(dot + 1)..]);
    }

    /// <summary>
    /// Resolves the model for an id by dispatching on its category. ModelDb.GetByIdOrNull casts the stored model to T
    /// unchecked, so probing a different kind with the same id throws InvalidCastException — the kind must come from the
    /// id's own category, never from a probe (this is why relics/potions silently failed before). 按 id 自带类别分发解析：
    /// ModelDb.GetByIdOrNull 把存储模型无检查转型为 T，用同一 id 探测别的类别会抛 InvalidCastException——类别必须取自 id 自身
    /// 而非盲试（这正是之前遗物/药水静默失效的原因）。
    /// </summary>
    private static AbstractModel? ResolveModel(ModelId id)
    {
        if (id.Category == CardCategory)
        {
            return ModelDb.GetByIdOrNull<CardModel>(id);
        }

        if (id.Category == RelicCategory)
        {
            return ModelDb.GetByIdOrNull<RelicModel>(id);
        }

        if (id.Category == PotionCategory)
        {
            return ModelDb.GetByIdOrNull<PotionModel>(id);
        }

        return null;
    }

    /// <summary>
    /// Builds the vanilla tooltips for a resolved model, or null when building throws — one bad item must never crash
    /// the hover handler. 为已解析的模型构建原版提示；构建异常时返回 null——单个坏物品绝不能崩掉悬停处理器。
    /// </summary>
    private static IEnumerable<IHoverTip>? BuildTips(AbstractModel model)
    {
        try
        {
            switch (model)
            {
                case CardModel card:
                {
                    HoverTip tip = new(card.TitleLocString, card.GetDescriptionForPile(PileType.None));
                    tip.SetCanonicalModel(card);
                    return new IHoverTip[] { tip }.Concat(card.HoverTips);
                }
                case RelicModel relic:
                    return relic.HoverTips;
                case PotionModel potion:
                    return potion.HoverTips;
                default:
                    return null;
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static CanvasLayer? FindCanvasLayer(Node node)
    {
        Node? current = node;
        while (current != null)
        {
            if (current is CanvasLayer layer)
            {
                return layer;
            }

            current = current.GetParent();
        }

        return null;
    }
}
