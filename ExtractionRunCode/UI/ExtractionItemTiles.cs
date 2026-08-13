using Godot;
using ExtractionRun.Data;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace ExtractionRun.UI;

/// <summary>
/// Shared card-form item tile rendering + grouping for the 搜打撤 UI (warehouse hub, shop and extraction settlement).
/// Groups duplicate serializable items by id — and, when split by durability is on, additionally by durability so
/// copies at different durability render as separate tiles (cards merge across upgrade levels — the warehouse is
/// base-only) — resolves name / source pool / art / filter metadata (rarity, type, cost), and builds a clickable tile
/// button showing art, name, source pool, quantity and an add/remove affordance.
/// 搜打撤界面共用的物品卡片渲染与分组：按 id 合并重复项，开启拆分时再按耐久分组（不同耐久的副本独立成块；卡牌跨升级合并——
/// 仓库只存基础态），解析名称/来源池/贴图/过滤元数据（稀有度/类型/费用），构建带增删角标的卡片按钮。瓦片拆成
/// CreateItemTile（建骨架）+ PopulateItemTile（填数据），供虚拟网格复用，避免滚动时重建节点。
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

        /// <summary>Shop buy side: clicking buys the entry (a gold "+"; the count badge is hidden, the price pill shows
        /// the buy price). 商店购买侧：点击买入（金色 +；数量角标隐藏，价格胶囊显示买价）。</summary>
        Buy,

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
        CardRarity Rarity, CardType Type, CostBucket Cost, string PoolSlug, string PortraitPath, string Haystack,
        ContentSource Source, int Durability);

    public sealed record RelicGroup(SerializableRelic Rep, string Name, string Pool, int Count, Texture2D? Texture,
        RelicRarity Rarity, string PoolSlug, string IconPath, string Haystack, ContentSource Source, int Durability);

    public sealed record PotionGroup(SerializablePotion Rep, string Name, string Pool, int Count, Texture2D? Texture,
        PotionRarity Rarity, string PoolSlug, string ImagePath, string Haystack, ContentSource Source);

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
    /// Groups cards by base id — and, when <paramref name="splitByDurability"/> is on, additionally by durability, so
    /// copies at different durability show as separate tiles (each with its own badge) instead of one tile labeled with
    /// the worst copy. The key is (id, durability) with a -1 sentinel for the merged key; copies sharing the key merge
    /// into one group's <c>Count</c>. In merged mode the group's <c>Durability</c> is the LOWEST remaining durability;
    /// in split mode it is the stack's exact value. Sorted by source pool (canonical order) then rarity then id, then
    /// durability (best-first) when split. When <paramref name="loadArt"/> is false (hub), textures are NOT touched so
    /// the async-preload path can resolve them lazily instead of synchronously loading every portrait.
    /// 按基础 id 分组——开启拆分时再按耐久分组：不同耐久的副本各占一块瓦片（各显其角标），不再合并为一块只显最破。键为
    /// (id, 耐久)，合并模式用 -1 哨兵；同键副本合并为一组的 Count。合并模式分组 Durability 为组内最低；拆分模式为该堆精确值。
    /// 按来源池（规范序）→稀有度→id 排序，拆分时再按耐久降序（满耐久在前）。loadArt=false（大厅）时不触碰贴图。
    /// </summary>
    public static List<CardGroup> GroupCards(IReadOnlyList<WarehouseCard> cards, bool loadArt = true, bool splitByDurability = true)
    {
        var map = new Dictionary<(ModelId, int), (SerializableCard Rep, int Count, int MinDur)>();
        foreach (WarehouseCard wc in cards)
        {
            SerializableCard sc = wc.Card;
            if (sc.Id is not ModelId id)
            {
                continue;
            }

            var key = (id, splitByDurability ? wc.Durability : -1);
            if (map.TryGetValue(key, out (SerializableCard Rep, int Count, int MinDur) entry))
            {
                map[key] = (entry.Rep, entry.Count + 1, Math.Min(entry.MinDur, wc.Durability));
            }
            else
            {
                map[key] = (sc, 1, wc.Durability);
            }
        }

        var groups = new List<CardGroup>(map.Count);
        foreach (KeyValuePair<(ModelId, int), (SerializableCard Rep, int Count, int MinDur)> kv in map)
        {
            int durability = splitByDurability ? kv.Key.Item2 : kv.Value.MinDur;
            groups.Add(BuildCardGroup(kv.Value.Rep, kv.Value.Count, durability, loadArt));
        }

        groups.Sort(CompareCardGroups);
        return groups;
    }

    public static List<RelicGroup> GroupRelics(IReadOnlyList<WarehouseRelic> relics, bool loadArt = true, bool splitByDurability = true)
    {
        var map = new Dictionary<(ModelId, int), (SerializableRelic Rep, int Count, int MinDur)>();
        foreach (WarehouseRelic wr in relics)
        {
            SerializableRelic sr = wr.Relic;
            if (sr.Id is not ModelId id)
            {
                continue;
            }

            var key = (id, splitByDurability ? wr.Durability : -1);
            if (map.TryGetValue(key, out (SerializableRelic Rep, int Count, int MinDur) entry))
            {
                map[key] = (entry.Rep, entry.Count + 1, Math.Min(entry.MinDur, wr.Durability));
            }
            else
            {
                map[key] = (sr, 1, wr.Durability);
            }
        }

        var groups = new List<RelicGroup>(map.Count);
        foreach (KeyValuePair<(ModelId, int), (SerializableRelic Rep, int Count, int MinDur)> kv in map)
        {
            int durability = splitByDurability ? kv.Key.Item2 : kv.Value.MinDur;
            groups.Add(BuildRelicGroup(kv.Value.Rep, kv.Value.Count, durability, loadArt));
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

    private static CardGroup BuildCardGroup(SerializableCard rep, int count, int minDurability, bool loadArt)
    {
        CardModel? card = rep.Id == null ? null : ModelDb.GetByIdOrNull<CardModel>(rep.Id);
        ContentSource source = rep.Id == null ? ContentSource.Unknown
            : CarryCodeOwner.ResolveSource(CarryCodec.ItemKind.Card, rep.Id);
        string name = card?.Title ?? rep.Id?.ToString() ?? "?";
        string poolSlug = CardPoolSlug(rep.Id);
        string pool = CardPoolName(rep.Id);
        string haystack = SearchHaystack(name, pool, poolSlug, rep.Id, source);

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
            haystack,
            source,
            minDurability);
    }

    private static RelicGroup BuildRelicGroup(SerializableRelic rep, int count, int minDurability, bool loadArt)
    {
        RelicModel? relic = rep.Id == null ? null : ModelDb.GetByIdOrNull<RelicModel>(rep.Id);
        ContentSource source = rep.Id == null ? ContentSource.Unknown
            : CarryCodeOwner.ResolveSource(CarryCodec.ItemKind.Relic, rep.Id);
        string name = relic?.Title.GetFormattedText() ?? rep.Id?.ToString() ?? "?";
        string poolSlug = RelicPoolSlug(rep.Id);
        string pool = RelicPoolName(rep.Id);
        string haystack = SearchHaystack(name, pool, poolSlug, rep.Id, source);
        return new RelicGroup(rep, name, pool, count,
            loadArt ? RelicTexture(rep.Id) : null,
            relic?.Rarity ?? RelicRarity.None,
            poolSlug,
            relic?.IconPath ?? "",
            haystack,
            source,
            minDurability);
    }

    private static PotionGroup BuildPotionGroup(SerializablePotion rep, int count, bool loadArt)
    {
        PotionModel? potion = rep.Id == null ? null : ModelDb.GetByIdOrNull<PotionModel>(rep.Id);
        ContentSource source = rep.Id == null ? ContentSource.Unknown
            : CarryCodeOwner.ResolveSource(CarryCodec.ItemKind.Potion, rep.Id);
        string name = potion?.Title.GetFormattedText() ?? rep.Id?.ToString() ?? "?";
        string poolSlug = PotionPoolSlug(rep.Id);
        string pool = PotionPoolName(rep.Id);
        string haystack = SearchHaystack(name, pool, poolSlug, rep.Id, source);
        return new PotionGroup(rep, name, pool, count,
            loadArt ? PotionTexture(rep.Id) : null,
            potion?.Rarity ?? PotionRarity.None,
            poolSlug,
            potion?.ImagePath ?? "",
            haystack,
            source);
    }

    /// <summary>
    /// Search haystack for a group: base name + pool display name + pool slug + model id, plus — for mod items — the
    /// owning mod's display name and stem (free search-by-mod across languages, matching the id/pool-slug rationale).
    /// 分组搜索堆：基础名称 + 池显示名 + 池 slug + 模型 id；mod 物品再加归属 mod 的显示名与 stem（跨语言按 mod 搜索）。
    /// </summary>
    private static string SearchHaystack(string name, string pool, string poolSlug, ModelId? id, ContentSource source)
    {
        string basePart = string.Join(' ', name, pool, poolSlug, id?.ToString() ?? "");
        if (source.IsMod && source.ModStem is string stem)
        {
            basePart += " " + CarryCodeOwner.ResolveModDisplayName(stem) + " " + stem;
        }

        return basePart.ToLowerInvariant();
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

    /// <summary>
    /// Preview/sell-selection key for a card group: id-only when merged, <c>id@durability</c> when split — both sides of
    /// the warehouse-vs-carried preview must pass the same flag or the counts won't line up. 卡片预览/出售选中键：合并模式仅 id，
    /// 拆分模式 id@耐久——预览两侧必须传同一标志，否则数量对不上。
    /// </summary>
    public static string CardKey(CardGroup g, bool splitByDurability) =>
        splitByDurability ? DurabilityKey(g.Rep.Id, g.Durability) : Key(g.Rep.Id);

    public static string RelicKey(RelicGroup g, bool splitByDurability) =>
        splitByDurability ? DurabilityKey(g.Rep.Id, g.Durability) : Key(g.Rep.Id);

    public static string PotionKey(PotionGroup g) => Key(g.Rep.Id);

    /// <summary>Key of one exact-durability copy stack (<c>id@durability</c>); ids never contain <c>@</c>, so it can't
    /// collide with an id-only key. 单份耐久堆的键（id@耐久）；id 不含 @，不会与纯 id 键冲突。</summary>
    public static string DurabilityKey(ModelId? id, int durability) => Key(id) + "@" + durability;

    // ----- Tile rendering 卡片渲染 -----

    /// <summary>
    /// Builds one item tile: art, name, source pool, quantity badge, an add/remove affordance, and (for cards/relics
    /// with a known durability) a durability badge. Convenience wrapper over <see cref="CreateItemTile"/> +
    /// <see cref="PopulateItemTile"/> for one-shot tiles (carry, settlement). <paramref name="id"/> feeds the tile's
    /// vanilla hover tip; <paramref name="price"/> shows a gold price pill (shop buy price / group sell value);
    /// <paramref name="selectedCount"/> highlights the tile for multi-select (primary border + a green count glyph);
    /// 0 means not selected.
    /// 构建一张物品卡片：贴图、名称、来源池、数量角标、增删操作，以及（已知耐久的牌/遗物）耐久角标（一次性瓦片的便捷封装）；
    /// id 供悬停提示使用；price 显示金色价格胶囊（商店买价/分组卖价）；selectedCount 高亮瓦片表示多选（主题蓝描边 + 绿色份数角标）；
    /// 0 表示未选中。
    /// </summary>
    public static Button MakeItemTile(string name, string pool, int count, Texture2D? texture,
        ItemTileAction action, Action? onClick, ModelId? id, int? durability = null, int? price = null,
        int selectedCount = 0)
    {
        Button button = CreateItemTile();
        PopulateItemTile(button, name, pool, count, texture, action, id, durability, price, selectedCount);
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

        // Durability pill (bottom-right of the art well): shows the lowest remaining durability of a copy group
        // (cards/relics only), value-coded (white ≥ 2, amber at 1 — one extraction from breaking — red for a broken
        // 0), hidden for potions and no-durability mode. A bottom-aligned VBox + ShrinkEnd pill pins it to the corner
        // and sizes to both the well's dynamic height and the string's width (Durability 20 vs 耐久 20).
        // 耐久胶囊（贴图凹槽右下角）：显示该组最低剩余耐久（仅牌/遗物），按剩余值分级着色（≥2 近白，1 琥珀——再撤一次即战损，
        // 0 战损红「耗尽」），药水与无耐久模式隐藏。VBox 底对齐 + ShrinkEnd 右对齐把胶囊钉在凹槽右下角，随凹槽动态高度与
        // 文案宽度（Durability 20 / 耐久 20）自适应。
        var durabilityHost = new VBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Alignment = BoxContainer.AlignmentMode.End,
        };
        durabilityHost.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        // Right edge matches the quantity badge's right edge — that badge sits at tile-x 104 while the well ends at
        // 102, so the pill overhangs the well by the same 2px (both badges share the right axis). Bottom sits flush
        // with the well's bottom edge.
        // 右缘与右上角数量角标对齐（数量角标在瓦片 x=104，凹槽右缘 102，胶囊随之凸出凹槽 2px，两角标共用右轴）；
        // 下缘与凹槽底缘齐平。
        durabilityHost.OffsetRight = 2f;
        durabilityHost.OffsetBottom = 0f;
        well.AddChild(durabilityHost);

        var durabilityPill = new PanelContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd,
        };
        durabilityPill.AddThemeStyleboxOverride("panel", ExtractionTheme.BadgeBox());
        durabilityHost.AddChild(durabilityPill);

        var durabilityLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        durabilityLabel.AddThemeFontSizeOverride("font_size", 11);
        durabilityLabel.AddThemeColorOverride("font_color", ExtractionTheme.Text);
        durabilityPill.AddChild(durabilityLabel);

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

        // Price pill (bottom of the tile): a compact gold chip for shop tiles (buy price / group sell value),
        // hidden for warehouse/settlement tiles (a hidden control contributes zero layout size).
        // 价格胶囊（瓦片底部）：商店瓦片用的紧凑金色胶囊（买价/分组卖价），仓库/结算瓦片隐藏（隐藏控件不占布局）。
        var pricePill = new PanelContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
        };
        pricePill.AddThemeStyleboxOverride("panel", ExtractionTheme.PriceBox());
        var priceLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        priceLabel.AddThemeFontSizeOverride("font_size", 12);
        priceLabel.AddThemeColorOverride("font_color", ExtractionTheme.GoldChipText);
        pricePill.AddChild(priceLabel);
        vbox.AddChild(pricePill);

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
        button.SetMeta("_badgePill", badge);
        button.SetMeta("_glyph", glyph);
        button.SetMeta("_glyphLabel", glyphLabel);
        button.SetMeta("_durability", durabilityLabel);
        button.SetMeta("_durabilityPill", durabilityPill);
        button.SetMeta("_pricePill", pricePill);
        button.SetMeta("_priceLabel", priceLabel);
        button.MouseEntered += () => ExtractionItemTooltip.Show(button);
        button.MouseExited += () => ExtractionItemTooltip.Hide(button);
        return button;
    }

    /// <summary>
    /// Re-binds a pooled tile to new data (name / pool / count / texture / action / tooltip id / durability / price /
    /// selection). Idempotent — safe to call on an already-populated tile. A tooltip open for the previous item is
    /// closed when the id changes (a recycled tile would otherwise keep showing stale content). The durability pill is
    /// hidden for potions and no-durability mode (null) and value-coded when shown: white ≥ 2, amber at 1 (one
    /// extraction from breaking), red "Broken" for 0 (a broken-copy display). A buy tile hides its count badge (one
    /// copy per slot) and shows the price pill instead. A selected tile gets a primary border and a green count glyph
    /// (multi-select); a selected Display tile otherwise keeps no glyph.
    /// 把池化瓦片重新绑定到新数据（名称/池/数量/贴图/角色/提示 id/耐久/价格/选中数）。换 id 时关闭旧物品的悬停提示。
    /// 耐久胶囊对药水与无耐久模式隐藏（null），显示时按剩余值分级：≥2 近白，1 琥珀（再撤一次即战损），0（战损副本展示）为红色
    /// 「耗尽」。购买瓦片隐藏数量角标（每槽一份）并改显价格胶囊。选中瓦片加主题蓝描边 + 绿色份数角标（多选，0 表示未选中）。
    /// </summary>
    public static void PopulateItemTile(Button button, string name, string pool, int count, Texture2D? texture,
        ItemTileAction action, ModelId? id, int? durability = null, int? price = null, int selectedCount = 0)
    {
        if (ExtractionItemTooltip.SetItem(button, id))
        {
            ExtractionItemTooltip.Hide(button);
        }

        GetMetaLabel(button, "_name").Text = name;
        GetMetaLabel(button, "_pool").Text = pool;
        GetMetaNode<TextureRect>(button, "_art").Texture = texture;

        // Count badge: hidden for buy tiles (a shop slot holds one copy; the price pill carries the number).
        bool showCount = action != ItemTileAction.Buy;
        GetMetaNode<PanelContainer>(button, "_badgePill").Visible = showCount;
        if (showCount)
        {
            GetMetaLabel(button, "_badge").Text = $"×{count}";
        }

        Label durabilityLabel = GetMetaLabel(button, "_durability");
        PanelContainer durabilityPill = GetMetaNode<PanelContainer>(button, "_durabilityPill");
        if (durability.HasValue)
        {
            durabilityLabel.Text = durability.Value <= 0
                ? ExtractionLocalization.DurabilityBrokenText()
                : ExtractionLocalization.DurabilityBadgeText(durability.Value);
            durabilityLabel.AddThemeColorOverride("font_color", DurabilityColor(durability.Value));
            durabilityPill.Visible = true;
        }
        else
        {
            durabilityPill.Visible = false;
        }

        // Price pill (shop tiles only).
        PanelContainer pricePill = GetMetaNode<PanelContainer>(button, "_pricePill");
        if (price.HasValue)
        {
            GetMetaLabel(button, "_priceLabel").Text = price.Value.ToString();
            pricePill.Visible = true;
        }
        else
        {
            pricePill.Visible = false;
        }

        bool display = action == ItemTileAction.Display;
        bool buy = action == ItemTileAction.Buy;
        bool add = action == ItemTileAction.Add;
        var glyph = GetMetaNode<PanelContainer>(button, "_glyph");
        if (selectedCount > 0)
        {
            // Multi-select highlight: primary border + green count glyph (how many copies of the group are selected);
            // the add/remove/buy glyph is replaced. The round chip widens by digit count so multi-digit counts aren't
            // clipped (one digit = 24px, +10px each). 多选高亮：主题蓝描边 + 绿色份数角标（该组选中了几件），替换增删/购买角标。
            // 圆形角标按位数加宽（1 位 24px，每多一位 +10px），避免多位数字被裁切。
            button.AddThemeStyleboxOverride("normal", ExtractionTheme.SelectedTileBox());
            glyph.Visible = true;
            glyph.AddThemeStyleboxOverride("panel", ExtractionTheme.SelectedGlyphBox());
            Label glyphLabel = GetMetaLabel(button, "_glyphLabel");
            string countText = selectedCount.ToString();
            glyphLabel.Text = countText;
            glyphLabel.AddThemeColorOverride("font_color", Colors.White);
            glyph.Size = new Vector2(24f + Math.Max(0, countText.Length - 1) * 10f, 24f);
        }
        else
        {
            button.RemoveThemeStyleboxOverride("normal");
            glyph.Visible = !display;
            glyph.AddThemeStyleboxOverride("panel", ExtractionTheme.GlyphBox(add || buy));
            GetMetaLabel(button, "_glyphLabel").Text = buy ? "+" : add ? "+" : "-";
            // Reset the chip to the default square width for a recycled tile that just showed a multi-digit count.
            // 回收瓦片从多位数状态回到未选中时，把角标复位为默认宽度。
            glyph.Size = new Vector2(24f, 24f);
        }
    }

    private static Label GetMetaLabel(Button button, string key) => button.GetMeta(key).As<Label>();

    private static T GetMetaNode<[MustBeVariant] T>(Button button, string key) => button.GetMeta(key).As<T>();

    /// <summary>Durability badge text color, value-coded: near-white while healthy, amber at 1 (one extraction from
    /// breaking), red for a broken (0) copy. 耐久角标文字颜色按剩余值分级：健康近白，1（再撤一次即战损）琥珀警示，战损红。</summary>
    private static Color DurabilityColor(int durability) => durability switch
    {
        <= 0 => ExtractionTheme.Danger,
        1 => ExtractionTheme.GoldChipText,
        _ => ExtractionTheme.Text,
    };

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

        int byId = string.CompareOrdinal(a.Rep.Id?.ToString(), b.Rep.Id?.ToString());
        if (byId != 0)
        {
            return byId;
        }

        // Split stacks of one id sort best-first (full durability leads). 同一 id 的拆分堆满耐久在前。
        return b.Durability.CompareTo(a.Durability);
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

        int byId = string.CompareOrdinal(a.Rep.Id?.ToString(), b.Rep.Id?.ToString());
        if (byId != 0)
        {
            return byId;
        }

        return b.Durability.CompareTo(a.Durability);
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
