using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace ExtractionRun.UI;

/// <summary>
/// Shared card-form item tile rendering + grouping for the 搜打撤 UI (warehouse hub and extraction settlement).
/// Groups duplicate serializable items by id (cards merge across upgrade levels — the warehouse is base-only),
/// resolves name / source pool / art / filter metadata (rarity, type, cost), and builds a clickable tile button
/// showing art, name, source pool, quantity and an add/remove affordance.
/// 搜打撤界面共用的物品卡片渲染与分组：按 id 合并重复项（卡牌跨升级合并——仓库只存基础态），解析名称/来源池/贴图/过滤元数据
/// （稀有度/类型/费用），构建带增删角标的卡片按钮。瓦片拆成 CreateItemTile（建骨架）+ PopulateItemTile（填数据），
/// 供虚拟网格复用，避免滚动时重建节点。
/// </summary>
public static class ExtractionItemTiles
{
    /// <summary>Item tile footprint. 物品卡片尺寸。</summary>
    public const float TileWidth = 108f;
    public const float TileHeight = 126f;

    /// <summary>The tile's interaction role. 卡片的交互角色。</summary>
    public enum ItemTileAction
    {
        /// <summary>Warehouse side: clicking adds one copy to the carry. 仓库侧：点击加入携带。</summary>
        Add,

        /// <summary>Carry side: clicking removes one copy. 携带侧：点击移除。</summary>
        Remove,

        /// <summary>Read-only (settlement screens): no affordance, no action. 只读展示。</summary>
        Display,
    }

    /// <summary>Card cost filter buckets. 卡牌费用过滤桶。</summary>
    public enum CostBucket
    {
        /// <summary>0-cost and unplayable (negative-cost) cards share one bucket. 0 费与不可打出（负费）同一桶。</summary>
        Zero,

        One,
        Two,

        /// <summary>3 or more. 3 费及以上。</summary>
        ThreePlus,

        /// <summary>X-cost cards. X 费卡。</summary>
        X,
    }

    // ----- Grouped variety metadata (each group = one base item kind) -----

    public sealed record CardGroup(SerializableCard Rep, string Name, string Pool, int Count, Texture2D? Texture,
        CardRarity Rarity, CardType Type, CostBucket Cost, string PoolSlug, string PortraitPath, string Haystack);

    public sealed record RelicGroup(SerializableRelic Rep, string Name, string Pool, int Count, Texture2D? Texture,
        RelicRarity Rarity, string PoolSlug, string IconPath, string Haystack);

    public sealed record PotionGroup(SerializablePotion Rep, string Name, string Pool, int Count, Texture2D? Texture,
        PotionRarity Rarity, string PoolSlug, string ImagePath, string Haystack);

    // ----- Canonical source-pool display order (by slug; language-independent) -----

    private static readonly string[] CardPoolOrder =
    {
        "ironclad", "silent", "defect", "necrobinder", "regent",
        "colorless", "curse", "status", "token", "event", "quest",
    };

    private static readonly string[] RelicPoolOrder =
    {
        "IroncladRelicPool", "SilentRelicPool", "DefectRelicPool", "NecrobinderRelicPool", "RegentRelicPool",
        "SharedRelicPool", "EventRelicPool", "FallbackRelicPool", "DeprecatedRelicPool",
    };

    private static readonly string[] PotionPoolOrder =
    {
        "IroncladPotionPool", "SilentPotionPool", "DefectPotionPool", "NecrobinderPotionPool", "RegentPotionPool",
        "SharedPotionPool", "EventPotionPool", "TokenPotionPool", "MockPotionPool", "DeprecatedPotionPool",
    };

    // ----- Grouping 分组 -----

    /// <summary>
    /// Groups cards by base id (upgrade levels merged — the warehouse stores base state only). Sorted by source pool
    /// (canonical order) then rarity then id. When <paramref name="loadArt"/> is false (hub), textures are NOT touched
    /// so the async-preload path can resolve them lazily instead of synchronously loading every portrait.
    /// 按基础 id 分组（升级合并——仓库只存基础态），按来源池（规范序）→稀有度→id 排序。loadArt=false（大厅）时不触碰贴图，
    /// 交由异步预加载路径延迟解析，避免首开同步加载全部立绘。
    /// </summary>
    public static List<CardGroup> GroupCards(IReadOnlyList<SerializableCard> cards, bool loadArt = true)
    {
        var map = new Dictionary<ModelId, (SerializableCard Rep, int Count)>();
        foreach (SerializableCard sc in cards)
        {
            if (sc.Id is not ModelId id)
            {
                continue; 
            }

            if (map.TryGetValue(id, out (SerializableCard Rep, int Count) entry))
            {
                map[id] = (entry.Rep, entry.Count + 1);
            }
            else
            {
                map[id] = (sc, 1);
            }
        }

        var groups = new List<CardGroup>(map.Count);
        foreach ((SerializableCard rep, int count) in map.Values)
        {
            groups.Add(BuildCardGroup(rep, count, loadArt));
        }

        groups.Sort(CompareCardGroups);
        return groups;
    }

    public static List<RelicGroup> GroupRelics(IReadOnlyList<SerializableRelic> relics, bool loadArt = true)
    {
        var map = new Dictionary<ModelId, (SerializableRelic Rep, int Count)>();
        foreach (SerializableRelic sr in relics)
        {
            if (sr.Id is not ModelId id)
            {
                continue; 
            }

            if (map.TryGetValue(id, out (SerializableRelic Rep, int Count) entry))
            {
                map[id] = (entry.Rep, entry.Count + 1);
            }
            else
            {
                map[id] = (sr, 1);
            }
        }

        var groups = new List<RelicGroup>(map.Count);
        foreach ((SerializableRelic rep, int count) in map.Values)
        {
            groups.Add(BuildRelicGroup(rep, count, loadArt));
        }

        groups.Sort(CompareRelicGroups);
        return groups;
    }

    public static List<PotionGroup> GroupPotions(IReadOnlyList<SerializablePotion> potions, bool loadArt = true)
    {
        var map = new Dictionary<ModelId, (SerializablePotion Rep, int Count)>();
        foreach (SerializablePotion sp in potions)
        {
            if (sp.Id is not ModelId id)
            {
                continue; 
            }

            if (map.TryGetValue(id, out (SerializablePotion Rep, int Count) entry))
            {
                map[id] = (entry.Rep, entry.Count + 1);
            }
            else
            {
                map[id] = (sp, 1);
            }
        }

        var groups = new List<PotionGroup>(map.Count);
        foreach ((SerializablePotion rep, int count) in map.Values)
        {
            groups.Add(BuildPotionGroup(rep, count, loadArt));
        }

        groups.Sort(ComparePotionGroups);
        return groups;
    }

    private static CardGroup BuildCardGroup(SerializableCard rep, int count, bool loadArt)
    {
        CardModel? card = rep.Id == null ? null : ModelDb.GetByIdOrNull<CardModel>(rep.Id);
        string name = card?.Title ?? rep.Id?.ToString() ?? "?";
        string poolSlug = CardPoolSlug(rep.Id);
        string pool = CardPoolName(rep.Id);
        string haystack = string.Join(' ', name, pool, poolSlug, rep.Id?.ToString() ?? "").ToLowerInvariant();

        // Identity cards (MadScience) keep their tinker type/rider in Props — restore them so the tile's type, art and
        // cost reflect the real card instead of the degenerate base model (whose Type is None). Other cards have no
        // Props and display from the base model directly.
        // 身份牌（疯狂科学）的类型/附效存在 Props 里——按 Props 还原，瓦片的类型/贴图/费用才与真实卡一致（基础模型 Type 为 None）。
        // 其余卡无 Props，直接按基础模型展示。
        CardModel? display = card;
        if (card != null && rep.Props != null)
        {
            try
            {
                display = card.ToMutable();
                rep.Props.Fill(display);
            }
            catch (Exception)
            {
                display = card;
            }
        }

        return new CardGroup(rep, name, pool, count,
            loadArt ? CardTexture(display) : null,
            card?.Rarity ?? CardRarity.None,
            display?.Type ?? CardType.None,
            CardCostBucket(display),
            poolSlug,
            display?.PortraitPath ?? "",
            haystack);
    }

    private static RelicGroup BuildRelicGroup(SerializableRelic rep, int count, bool loadArt)
    {
        RelicModel? relic = rep.Id == null ? null : ModelDb.GetByIdOrNull<RelicModel>(rep.Id);
        string name = relic?.Title.GetFormattedText() ?? rep.Id?.ToString() ?? "?";
        string poolSlug = RelicPoolSlug(rep.Id);
        string pool = RelicPoolName(rep.Id);
        string haystack = string.Join(' ', name, pool, poolSlug, rep.Id?.ToString() ?? "").ToLowerInvariant();
        return new RelicGroup(rep, name, pool, count,
            loadArt ? RelicTexture(rep.Id) : null,
            relic?.Rarity ?? RelicRarity.None,
            poolSlug,
            relic?.IconPath ?? "",
            haystack);
    }

    private static PotionGroup BuildPotionGroup(SerializablePotion rep, int count, bool loadArt)
    {
        PotionModel? potion = rep.Id == null ? null : ModelDb.GetByIdOrNull<PotionModel>(rep.Id);
        string name = potion?.Title.GetFormattedText() ?? rep.Id?.ToString() ?? "?";
        string poolSlug = PotionPoolSlug(rep.Id);
        string pool = PotionPoolName(rep.Id);
        string haystack = string.Join(' ', name, pool, poolSlug, rep.Id?.ToString() ?? "").ToLowerInvariant();
        return new PotionGroup(rep, name, pool, count,
            loadArt ? PotionTexture(rep.Id) : null,
            potion?.Rarity ?? PotionRarity.None,
            poolSlug,
            potion?.ImagePath ?? "",
            haystack);
    }

    /// <summary>
    /// Cost filter bucket for a card: X-cost → <see cref="CostBucket.X"/>; canonical energy cost ≤ 0 (0-cost and
    /// unplayable/status) → <see cref="CostBucket.Zero"/>; else 1 / 2 / 3+. The play cost is the ENERGY cost, not the
    /// star cost. 卡牌费用桶：X 费 → X；规范能量费 ≤ 0（0 费与不可打出/状态）→ 0 费；其余 1/2/3+。卡面资源是能量费而非辉星费。
    /// </summary>
    public static CostBucket CardCostBucket(CardModel? card)
    {
        if (card == null)
        {
            return CostBucket.Zero;
        }

        CardEnergyCost cost = card.EnergyCost;
        if (cost.CostsX)
        {
            return CostBucket.X;
        }

        int canonical = cost.Canonical;
        if (canonical <= 0)
        {
            return CostBucket.Zero;
        }

        if (canonical == 1)
        {
            return CostBucket.One;
        }

        if (canonical == 2)
        {
            return CostBucket.Two;
        }

        return CostBucket.ThreePlus;
    }

    /// <summary>
    /// Stable key used to line up warehouse vs carried counts for the hub's live preview (id-only). 用于对齐仓库/携带数量的键（仅 id）。
    /// </summary>
    public static string Key(ModelId? id) => id?.ToString() ?? "<null>";

    public static string CardKey(CardGroup g) => Key(g.Rep.Id);

    public static string RelicKey(RelicGroup g) => Key(g.Rep.Id);

    public static string PotionKey(PotionGroup g) => Key(g.Rep.Id);

    // ----- Tile rendering 卡片渲染 -----

    /// <summary>
    /// Builds one item tile: art, name, source pool, quantity badge, and an add/remove affordance. Convenience wrapper
    /// over <see cref="CreateItemTile"/> + <see cref="PopulateItemTile"/> for one-shot tiles (carry, settlement).
    /// <paramref name="id"/> feeds the tile's vanilla hover tip. 构建一张物品卡片：贴图、名称、来源池、数量角标与增删操作
    /// （一次性瓦片的便捷封装）；id 供悬停提示使用。
    /// </summary>
    public static Button MakeItemTile(string name, string pool, int count, Texture2D? texture,
        ItemTileAction action, Action? onClick, ModelId? id)
    {
        Button button = CreateItemTile();
        PopulateItemTile(button, name, pool, count, texture, action, id);
        if (onClick != null)
        {
            button.Pressed += onClick;
        }

        return button;
    }

    /// <summary>
    /// Creates a tile's full skeleton (art well, labels, badge, glyph) without binding data. The virtual grid pools
    /// these and re-populates them on scroll. Child refs are stashed as Meta on the button. The hover handlers are
    /// attached once here and read the tile's current item id from Meta, so recycled tiles never accumulate listeners.
    /// 创建瓦片完整骨架（不含数据），供虚拟网格池化复用；子节点引用以 Meta 存于按钮上。悬停处理器在此一次性挂载，
    /// 从 Meta 读取瓦片当前物品 id，回收瓦片不会叠加监听。
    /// </summary>
    public static Button CreateItemTile()
    {
        var button = new Button
        {
            ThemeTypeVariation = ExtractionTheme.ButtonTile,
            CustomMinimumSize = new Vector2(TileWidth, TileHeight),
        };

        var inner = new MarginContainer();
        inner.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        inner.AddThemeConstantOverride("margin_left", 6);
        inner.AddThemeConstantOverride("margin_right", 6);
        inner.AddThemeConstantOverride("margin_top", 6);
        inner.AddThemeConstantOverride("margin_bottom", 6);
        inner.MouseFilter = Control.MouseFilterEnum.Ignore;
        button.AddChild(inner);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 4);
        vbox.MouseFilter = Control.MouseFilterEnum.Ignore;
        inner.AddChild(vbox);

        var well = new Panel
        {
            CustomMinimumSize = new Vector2(0f, 56f),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        well.AddThemeStyleboxOverride("panel", ExtractionTheme.TextureWellBox());
        vbox.AddChild(well);

        var art = new TextureRect
        {
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        art.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        well.AddChild(art);

        // Name.
        var nameLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Off,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        nameLabel.AddThemeFontSizeOverride("font_size", ExtractionTheme.FontSizeSmall);
        nameLabel.AddThemeColorOverride("font_color", ExtractionTheme.Text);
        vbox.AddChild(nameLabel);

        // Source pool.
        var poolLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Off,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        poolLabel.AddThemeFontSizeOverride("font_size", 11);
        poolLabel.AddThemeColorOverride("font_color", ExtractionTheme.TextSecondary);
        vbox.AddChild(poolLabel);

        // Quantity badge (top-right, always visible).
        var badge = new PanelContainer
        {
            Position = new Vector2(TileWidth - 48f, 4f),
            Size = new Vector2(44f, 22f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        badge.AddThemeStyleboxOverride("panel", ExtractionTheme.BadgeBox());
        var badgeLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        badgeLabel.AddThemeFontSizeOverride("font_size", 12);
        badgeLabel.AddThemeColorOverride("font_color", ExtractionTheme.BadgeText);
        badge.AddChild(badgeLabel);
        button.AddChild(badge);

        // Add / remove glyph (top-left); hidden for display tiles.
        var glyph = new PanelContainer
        {
            Position = new Vector2(4f, 4f),
            Size = new Vector2(24f, 24f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        glyph.AddThemeStyleboxOverride("panel", ExtractionTheme.GlyphBox(add: true));
        var glyphLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        glyphLabel.AddThemeFontSizeOverride("font_size", 16);
        glyphLabel.AddThemeColorOverride("font_color", Colors.White);
        glyph.AddChild(glyphLabel);
        button.AddChild(glyph);

        button.SetMeta("_name", nameLabel);
        button.SetMeta("_pool", poolLabel);
        button.SetMeta("_art", art);
        button.SetMeta("_badge", badgeLabel);
        button.SetMeta("_glyph", glyph);
        button.SetMeta("_glyphLabel", glyphLabel);
        button.MouseEntered += () => ExtractionItemTooltip.Show(button);
        button.MouseExited += () => ExtractionItemTooltip.Hide(button);
        return button;
    }

    /// <summary>
    /// Re-binds a pooled tile to new data (name / pool / count / texture / action / tooltip id). Idempotent — safe to
    /// call on an already-populated tile. A tooltip open for the previous item is closed when the id changes (a recycled
    /// tile would otherwise keep showing stale content). 把池化瓦片重新绑定到新数据（名称/池/数量/贴图/角色/提示 id）。
    /// 换 id 时关闭旧物品的悬停提示，避免回收瓦片残留上一张卡的内容。
    /// </summary>
    public static void PopulateItemTile(Button button, string name, string pool, int count, Texture2D? texture,
        ItemTileAction action, ModelId? id)
    {
        if (ExtractionItemTooltip.SetItem(button, id))
        {
            ExtractionItemTooltip.Hide(button);
        }

        GetMetaLabel(button, "_name").Text = name;
        GetMetaLabel(button, "_pool").Text = pool;
        GetMetaNode<TextureRect>(button, "_art").Texture = texture;
        GetMetaLabel(button, "_badge").Text = $"×{count}";

        bool display = action == ItemTileAction.Display;
        bool add = action == ItemTileAction.Add;
        var glyph = GetMetaNode<PanelContainer>(button, "_glyph");
        glyph.Visible = !display;
        glyph.AddThemeStyleboxOverride("panel", ExtractionTheme.GlyphBox(add));
        GetMetaLabel(button, "_glyphLabel").Text = add ? "+" : "-";
    }

    private static Label GetMetaLabel(Button button, string key) => button.GetMeta(key).As<Label>();

    private static T GetMetaNode<[MustBeVariant] T>(Button button, string key) => button.GetMeta(key).As<T>();

    // ----- Model lookups 模型解析 -----

    public static string GetCardTitle(ModelId? id)
    {
        CardModel? card = id == null ? null : ModelDb.GetByIdOrNull<CardModel>(id);
        return card?.Title ?? id?.ToString() ?? "?";
    }

    public static string GetRelicTitle(ModelId? id)
    {
        RelicModel? relic = id == null ? null : ModelDb.GetByIdOrNull<RelicModel>(id);
        return relic?.Title.GetFormattedText() ?? id?.ToString() ?? "?";
    }

    public static string GetPotionTitle(ModelId? id)
    {
        PotionModel? potion = id == null ? null : ModelDb.GetByIdOrNull<PotionModel>(id);
        return potion?.Title.GetFormattedText() ?? id?.ToString() ?? "?";
    }

    private static Texture2D? CardTexture(CardModel? card)
    {
        try
        {
            return card?.Portrait;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Texture2D? RelicTexture(ModelId? id)
    {
        try
        {
            RelicModel? relic = id == null ? null : ModelDb.GetByIdOrNull<RelicModel>(id);
            return relic?.Icon;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Texture2D? PotionTexture(ModelId? id)
    {
        try
        {
            PotionModel? potion = id == null ? null : ModelDb.GetByIdOrNull<PotionModel>(id);
            return potion?.Image;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Raw source-pool slug for a card (search haystack + pool filter). Card pools use <c>Pool.Title</c> — a distinct
    /// lowercase slug per pool (ironclad / silent / … / colorless / curse / status / token / event / quest).
    /// 卡牌来源池原始 slug（搜索/池过滤用）：用 Pool.Title，每池一个独立小写 slug。
    /// </summary>
    public static string CardPoolSlug(ModelId? id)
    {
        try
        {
            CardModel? card = id == null ? null : ModelDb.GetByIdOrNull<CardModel>(id);
            return card?.Pool?.Title ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static string CardPoolName(ModelId? id) => ExtractionLocalization.PoolNameText(CardPoolSlug(id));

    /// <summary>
    /// Raw source-pool slug for a relic (search haystack + pool filter). Relic pools classify by CLASS NAME
    /// (SharedRelicPool / IroncladRelicPool / …) — <see cref="RelicPoolModel.EnergyColorName"/> would collapse
    /// shared/event/fallback/deprecated into one "colorless" bucket, losing the granularity the pool filter wants.
    /// 遗物来源池 slug（搜索/池过滤用）：按池类名（SharedRelicPool / IroncladRelicPool / …）分类——EnergyColorName 会把
    /// shared/event/fallback/deprecated 全部塌缩成一个 colorless，粒度不够。
    /// </summary>
    public static string RelicPoolSlug(ModelId? id)
    {
        try
        {
            RelicModel? relic = id == null ? null : ModelDb.GetByIdOrNull<RelicModel>(id);
            return relic?.Pool?.GetType().Name ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static string RelicPoolName(ModelId? id) => ExtractionLocalization.PoolNameText(RelicPoolSlug(id));

    /// <summary>
    /// Raw source-pool slug for a potion (class name, like relics). 药水来源池 slug（同遗物，按类名）。
    /// </summary>
    public static string PotionPoolSlug(ModelId? id)
    {
        try
        {
            PotionModel? potion = id == null ? null : ModelDb.GetByIdOrNull<PotionModel>(id);
            return potion?.Pool?.GetType().Name ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static string PotionPoolName(ModelId? id) => ExtractionLocalization.PoolNameText(PotionPoolSlug(id));

    // ----- Sorting 排序 -----

    private static int CompareCardGroups(CardGroup a, CardGroup b)
    {
        int byPool = PoolOrderIndex(CardPoolOrder, a.PoolSlug).CompareTo(PoolOrderIndex(CardPoolOrder, b.PoolSlug));
        if (byPool != 0)
        {
            return byPool;
        }

        int byRarity = CardRarityIndex(a.Rarity).CompareTo(CardRarityIndex(b.Rarity));
        if (byRarity != 0)
        {
            return byRarity;
        }

        return string.CompareOrdinal(a.Rep.Id?.ToString(), b.Rep.Id?.ToString());
    }

    private static int CompareRelicGroups(RelicGroup a, RelicGroup b)
    {
        int byPool = PoolOrderIndex(RelicPoolOrder, a.PoolSlug).CompareTo(PoolOrderIndex(RelicPoolOrder, b.PoolSlug));
        if (byPool != 0)
        {
            return byPool;
        }

        int byRarity = RelicRarityIndex(a.Rarity).CompareTo(RelicRarityIndex(b.Rarity));
        if (byRarity != 0)
        {
            return byRarity;
        }

        return string.CompareOrdinal(a.Rep.Id?.ToString(), b.Rep.Id?.ToString());
    }

    private static int ComparePotionGroups(PotionGroup a, PotionGroup b)
    {
        int byPool = PoolOrderIndex(PotionPoolOrder, a.PoolSlug).CompareTo(PoolOrderIndex(PotionPoolOrder, b.PoolSlug));
        if (byPool != 0)
        {
            return byPool;
        }

        int byRarity = PotionRarityIndex(a.Rarity).CompareTo(PotionRarityIndex(b.Rarity));
        if (byRarity != 0)
        {
            return byRarity;
        }

        return string.CompareOrdinal(a.Rep.Id?.ToString(), b.Rep.Id?.ToString());
    }

    private static int PoolOrderIndex(string[] order, string slug)
    {
        for (int i = 0; i < order.Length; i++)
        {
            if (order[i] == slug)
            {
                return i;
            }
        }

        return int.MaxValue;
    }

    private static int CardRarityIndex(CardRarity rarity) => rarity switch
    {
        CardRarity.Basic => 0,
        CardRarity.Common => 1,
        CardRarity.Uncommon => 2,
        CardRarity.Rare => 3,
        CardRarity.Event => 4,
        CardRarity.Token => 5,
        CardRarity.Status => 6,
        CardRarity.Curse => 7,
        CardRarity.Quest => 8,
        CardRarity.Ancient => 9,
        _ => 10,
    };

    private static int RelicRarityIndex(RelicRarity rarity) => rarity switch
    {
        RelicRarity.Starter => 0,
        RelicRarity.Common => 1,
        RelicRarity.Uncommon => 2,
        RelicRarity.Rare => 3,
        RelicRarity.Shop => 4,
        RelicRarity.Event => 5,
        RelicRarity.Ancient => 6,
        _ => 7,
    };

    private static int PotionRarityIndex(PotionRarity rarity) => rarity switch
    {
        PotionRarity.Common => 0,
        PotionRarity.Uncommon => 1,
        PotionRarity.Rare => 2,
        PotionRarity.Event => 3,
        PotionRarity.Token => 4,
        _ => 5,
    };
}
