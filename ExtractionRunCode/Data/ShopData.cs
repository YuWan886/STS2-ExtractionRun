namespace ExtractionRun.Data;

/// <summary>
/// One purchasable slot in the persistent shop. The id is stored as a <c>ModelId</c> string ("Category.Entry") so it
/// survives JSON as a stable, self-describing value; the price is the vanilla price ROLLED at stock generation
/// (with the vanilla per-item variance — cards/potions ±5%, relics ±15%) and frozen for the day, so the same stock
/// shows the same price until the next refresh/day rollover. <see cref="Sold"/> keeps a bought-out slot empty until
/// the next refresh.
/// 商店中的一个可购买槽位。id 以 ModelId 字符串（"Category.Entry"）存储，价格是生成库存时按原版浮动 roll 好并冻结当天的原版价。
/// Sold 让已售空的槽位保持空缺直到下次刷新。
/// </summary>
public sealed class ShopEntry
{
    /// <summary>Item kind: "card" / "relic" / "potion". 物品种类：card / relic / potion。</summary>
    public string Kind { get; set; } = "";

    /// <summary>Model id in "Category.Entry" form. 模型 id（"Category.Entry" 形式）。</summary>
    public string Id { get; set; } = "";

    /// <summary>Rolled vanilla price (with variance), frozen until the stock is re-rolled. 生成时 roll 好并冻结的原版价。</summary>
    public int Price { get; set; }

    /// <summary>True once bought — the slot stays empty until the next refresh/day. 已售出——槽位保持空缺直到下次刷新/新一天。</summary>
    public bool Sold { get; set; }
}

/// <summary>
/// Persistent per-profile shop state. The stock is rolled once on the first open of each real calendar day
/// (<see cref="ShopStore.EnsureStocked"/>) and <see cref="RefreshCount"/> tracks manual refreshes since the last
/// rollover so the refresh fee (50 → +50 → cap 250) resets daily.
/// 持久化的商店状态（每个存档位一份）。库存在每个现实日历日首次打开时 roll 一次；RefreshCount 记录自上次翻页以来的手动刷新次数，
/// 用于刷新费（50 → +50 → 封顶 250）每天重置。
/// </summary>
public sealed class ShopData
{
    /// <summary>Stock layout version (slot counts); a bump makes <see cref="ShopStore.EnsureStocked"/> re-roll once.
    /// 库存布局版本（槽位数）；递增后 EnsureStocked 会重 roll 一次。</summary>
    public int Version { get; set; }

    /// <summary>Local date (yyyy-MM-dd) the current stock was rolled for. 当前库存所属的本地日期。</summary>
    public string StockDate { get; set; } = "";

    /// <summary>Manual refreshes since the last day rollover. 距上次翻页以来的手动刷新次数。</summary>
    public int RefreshCount { get; set; }

    /// <summary>The current stock, one entry per slot. 当前库存（每槽一条）。</summary>
    public List<ShopEntry> Entries { get; set; } = new();
}
