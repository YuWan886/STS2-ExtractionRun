using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace ExtractionRun.UI;

/// <summary>
/// Shared card-form item tile rendering + grouping for the 搜打撤 UI (warehouse hub and extraction settlement).
/// Groups duplicate serializable items by id (+ upgrade for cards), resolves name / source pool / art, and builds a
/// clickable tile button showing art, name, source pool, quantity and an add/remove affordance.
/// 搜打撤界面共用的物品卡片渲染与分组：按 id（卡牌含升级）合并重复项，解析名称/来源池/贴图，构建带增删角标的卡片按钮。
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

    /// <summary>A group of identical items with one representative for add/remove and display metadata. 一组相同物品。</summary>
    public sealed record CardGroup(SerializableCard Rep, string Name, string Pool, int Count, Texture2D? Texture);

    public sealed record RelicGroup(SerializableRelic Rep, string Name, string Pool, int Count, Texture2D? Texture);

    public sealed record PotionGroup(SerializablePotion Rep, string Name, string Pool, int Count, Texture2D? Texture);

    // ----- Grouping 分组 -----

    public static IEnumerable<CardGroup> GroupCards(IReadOnlyList<SerializableCard> cards)
    {
        var map = new Dictionary<(ModelId?, int), (SerializableCard Rep, int Count)>();
        foreach (SerializableCard sc in cards)
        {
            var key = (sc.Id, sc.CurrentUpgradeLevel);
            if (map.TryGetValue(key, out (SerializableCard Rep, int Count) entry))
            {
                map[key] = (entry.Rep, entry.Count + 1);
            }
            else
            {
                map[key] = (sc, 1);
            }
        }

        foreach ((SerializableCard rep, int count) in map.Values)
        {
            yield return new CardGroup(rep, CardName(rep), CardPoolName(rep.Id), count, CardTexture(rep.Id));
        }
    }

    public static IEnumerable<RelicGroup> GroupRelics(IReadOnlyList<SerializableRelic> relics)
    {
        var map = new Dictionary<ModelId, (SerializableRelic Rep, int Count)>();
        foreach (SerializableRelic sr in relics)
        {
            if (sr.Id is not ModelId id)
            {
                continue; // Corrupt / unloaded entry; nothing to display.
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

        foreach ((SerializableRelic rep, int count) in map.Values)
        {
            yield return new RelicGroup(rep, GetRelicTitle(rep.Id), RelicPoolName(rep.Id), count, RelicTexture(rep.Id));
        }
    }

    public static IEnumerable<PotionGroup> GroupPotions(IReadOnlyList<SerializablePotion> potions)
    {
        var map = new Dictionary<ModelId, (SerializablePotion Rep, int Count)>();
        foreach (SerializablePotion sp in potions)
        {
            if (sp.Id is not ModelId id)
            {
                continue; // Corrupt / unloaded entry; nothing to display.
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

        foreach ((SerializablePotion rep, int count) in map.Values)
        {
            yield return new PotionGroup(rep, GetPotionTitle(rep.Id), PotionPoolName(rep.Id), count, PotionTexture(rep.Id));
        }
    }

    /// <summary>
    /// Stable key used to line up warehouse vs carried counts for the hub's live preview. 用于对齐仓库/携带数量的键。
    /// </summary>
    public static string Key(ModelId? id, int upgrade = 0) => id == null ? $"<null>|{upgrade}" : $"{id}|{upgrade}";

    public static string CardKey(CardGroup g) => Key(g.Rep.Id, g.Rep.CurrentUpgradeLevel);

    public static string RelicKey(RelicGroup g) => Key(g.Rep.Id);

    public static string PotionKey(PotionGroup g) => Key(g.Rep.Id);

    // ----- Tile rendering 卡片渲染 -----

    /// <summary>
    /// Builds one item tile: art, name, source pool, quantity badge, and an add/remove affordance.
    /// 构建一张物品卡片：贴图、名称、来源池、数量角标与增删操作。
    /// </summary>
    public static Button MakeItemTile(string name, string pool, int count, Texture2D? texture,
        ItemTileAction action, Action? onClick)
    {
        var button = new Button
        {
            ThemeTypeVariation = ExtractionTheme.ButtonTile,
            CustomMinimumSize = new Vector2(TileWidth, TileHeight),
        };
        if (onClick != null)
        {
            button.Pressed += onClick;
        }

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

        // Art well: a recessed dark panel the texture sits in.
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
            Texture = texture,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        art.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        well.AddChild(art);

        // Name.
        var nameLabel = new Label
        {
            Text = name,
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
            Text = pool,
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
            Text = $"×{count}",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        badgeLabel.AddThemeFontSizeOverride("font_size", 12);
        badgeLabel.AddThemeColorOverride("font_color", ExtractionTheme.BadgeText);
        badge.AddChild(badgeLabel);
        button.AddChild(badge);

        // Add / remove glyph (top-left); display tiles have none.
        if (action != ItemTileAction.Display)
        {
            bool add = action == ItemTileAction.Add;
            var glyph = new PanelContainer
            {
                Position = new Vector2(4f, 4f),
                Size = new Vector2(24f, 24f),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            glyph.AddThemeStyleboxOverride("panel", ExtractionTheme.GlyphBox(add));
            var glyphLabel = new Label
            {
                Text = add ? "+" : "-",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            glyphLabel.AddThemeFontSizeOverride("font_size", 16);
            glyphLabel.AddThemeColorOverride("font_color", Colors.White);
            glyph.AddChild(glyphLabel);
            button.AddChild(glyph);
        }

        return button;
    }

    // ----- Model lookups 模型解析 -----

    private static string CardName(SerializableCard sc)
    {
        string title = GetCardTitle(sc.Id);
        return sc.CurrentUpgradeLevel > 0
            ? $"{title}  {ExtractionLocalization.CardUpgradeText(sc.CurrentUpgradeLevel)}"
            : title;
    }

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

    private static Texture2D? CardTexture(ModelId? id)
    {
        try
        {
            CardModel? card = id == null ? null : ModelDb.GetByIdOrNull<CardModel>(id);
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

    private static string CardPoolName(ModelId? id)
    {
        try
        {
            CardModel? card = id == null ? null : ModelDb.GetByIdOrNull<CardModel>(id);
            return card?.Pool == null ? string.Empty : ExtractionLocalization.PoolNameText(card.Pool.Title);
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static string RelicPoolName(ModelId? id)
    {
        try
        {
            RelicModel? relic = id == null ? null : ModelDb.GetByIdOrNull<RelicModel>(id);
            return relic == null ? string.Empty : ExtractionLocalization.PoolNameText(relic.Pool.EnergyColorName);
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static string PotionPoolName(ModelId? id)
    {
        try
        {
            PotionModel? potion = id == null ? null : ModelDb.GetByIdOrNull<PotionModel>(id);
            return potion == null ? string.Empty : ExtractionLocalization.PoolNameText(potion.Pool.EnergyColorName);
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }
}
