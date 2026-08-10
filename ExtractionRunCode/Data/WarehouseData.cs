using System.Text.Json;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace ExtractionRun.Data;

/// <summary>
/// A card copy stored in the warehouse or carry, paired with its remaining durability. Durability is per-copy: a group
/// of N copies of the same card can each hold a different value, so it lives alongside the serializable, never inside it
/// (SerializableCard's wire format is fixed and NormalizeCard would strip any in-prop field). A Durability of 0 is the
/// sentinel the JsonConverter writes when it reads a pre-durability (bare SerializableCard) save — the one-shot
/// migration backfills those to the rarity's max before the warehouse is used, and the warehouse never stores a legal 0
/// (a carried copy at 1 breaks on extraction and is dropped, not stored).
/// 仓库/携带中的一张卡牌副本及其剩余耐久。耐久按副本计：同一张卡的 N 份可各自不同，因此放在序列化对象之外（SerializableCard
/// 的线上格式固定，且 NormalizeCard 会剥掉任何属性字段）。Durability 为 0 是 JsonConverter 读到「无耐久旧档（裸 SerializableCard）」
/// 时写入的哨兵——一次性迁移在仓库被使用前回填为稀有度上限；仓库从不合法存储 0 耐久（携带 1 耐久卡撤离时损坏、被丢弃而非入库）。
/// </summary>
[JsonConverter(typeof(WarehouseCardJsonConverter))]
public sealed class WarehouseCard
{
    /// <summary>The card itself, normalized to base state. 卡牌本体（已归一到基础态）。</summary>
    public SerializableCard Card { get; set; } = new();

    /// <summary>Remaining durability: a successful extraction reduces it by 1; 1 → 0 breaks the copy (not deposited).
    /// 剩余耐久：撤离成功 -1；1 → 0 视为战损（不入库）。</summary>
    public int Durability { get; set; }
}

/// <summary>
/// A relic copy stored in the warehouse or carry with its remaining durability. Same per-copy semantics as
/// <see cref="WarehouseCard"/>.
/// 仓库/携带中的一个遗物副本及其剩余耐久，语义同 WarehouseCard。
/// </summary>
[JsonConverter(typeof(WarehouseRelicJsonConverter))]
public sealed class WarehouseRelic
{
    /// <summary>The relic itself, normalized to base state. 遗物本体（已归一到基础态）。</summary>
    public SerializableRelic Relic { get; set; } = new();

    /// <summary>Remaining durability. 剩余耐久。</summary>
    public int Durability { get; set; }
}

/// <summary>
/// Reads/writes a <see cref="WarehouseCard"/> in either the current shape (<c>{"card": {...}, "durability": N}</c>) or
/// the legacy pre-durability shape (a bare SerializableCard object, stamped with the 0 sentinel for the one-shot
/// backfill). Attribute-registered so it applies to every JSON pipeline (ModDataStore and RitsuLib RunSavedData both
/// honor type-level JsonConverter attributes, which is what lets the same wrapper survive the MP carry sync).
/// 读写 WarehouseCard 的两种形状：现行 {card:{...}, durability:N}，或旧版裸 SerializableCard（标 0 哨兵，交由一次性迁移回填）。
/// 以特性注册，使其作用于所有 JSON 管线（ModDataStore 与 RitsuLib RunSavedData 都尊重类型级 JsonConverter，包装因此能
/// 随 MP 携带同步存活）。
/// </summary>
public sealed class WarehouseCardJsonConverter : JsonConverter<WarehouseCard>
{
    public override WarehouseCard? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected an object.");
        }

        using JsonDocument doc = JsonDocument.ParseValue(ref reader);
        JsonElement root = doc.RootElement;
        if (root.TryGetProperty("card", out JsonElement cardEl))
        {
            SerializableCard? card = cardEl.ValueKind == JsonValueKind.Null
                ? null
                : cardEl.Deserialize<SerializableCard>(options);
            int durability = root.TryGetProperty("durability", out JsonElement d) && d.ValueKind == JsonValueKind.Number
                ? d.GetInt32()
                : 0;
            return new WarehouseCard { Card = card ?? new SerializableCard(), Durability = durability };
        }

        SerializableCard? legacy = root.Deserialize<SerializableCard>(options);
        return new WarehouseCard { Card = legacy ?? new SerializableCard(), Durability = 0 };
    }

    public override void Write(Utf8JsonWriter writer, WarehouseCard value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("card");
        if (value.Card == null)
        {
            writer.WriteNullValue();
        }
        else
        {
            JsonSerializer.Serialize(writer, value.Card, options);
        }

        writer.WriteNumber("durability", value.Durability);
        writer.WriteEndObject();
    }
}

/// <summary>Reads/writes a <see cref="WarehouseRelic"/> in the current or legacy shape (mirrors the card converter).
/// 读写 WarehouseRelic 的现行/旧版形状（同卡牌转换器）。</summary>
public sealed class WarehouseRelicJsonConverter : JsonConverter<WarehouseRelic>
{
    public override WarehouseRelic? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected an object.");
        }

        using JsonDocument doc = JsonDocument.ParseValue(ref reader);
        JsonElement root = doc.RootElement;
        if (root.TryGetProperty("relic", out JsonElement relicEl))
        {
            SerializableRelic? relic = relicEl.ValueKind == JsonValueKind.Null
                ? null
                : relicEl.Deserialize<SerializableRelic>(options);
            int durability = root.TryGetProperty("durability", out JsonElement d) && d.ValueKind == JsonValueKind.Number
                ? d.GetInt32()
                : 0;
            return new WarehouseRelic { Relic = relic ?? new SerializableRelic(), Durability = durability };
        }

        SerializableRelic? legacy = root.Deserialize<SerializableRelic>(options);
        return new WarehouseRelic { Relic = legacy ?? new SerializableRelic(), Durability = 0 };
    }

    public override void Write(Utf8JsonWriter writer, WarehouseRelic value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("relic");
        if (value.Relic == null)
        {
            writer.WriteNullValue();
        }
        else
        {
            JsonSerializer.Serialize(writer, value.Relic, options);
        }

        writer.WriteNumber("durability", value.Durability);
        writer.WriteEndObject();
    }
}

/// <summary>
/// Persistent per-profile stash for the 搜打撤 (Search-Loot-Extract) mode.
/// Cards/relics/potions are stored in their serializable form, normalized to base on entry: upgrades, enchantments
/// and run-scoped growth are stripped, but "identity" cards (whose base model is degenerate without its saved props —
/// e.g. MadScience's tinker type/rider) keep those props so they stay playable (see <see cref="WarehouseStore.NormalizeCard"/>).
/// Stored in <c>ModDataStore</c> with <c>SaveScope.Profile</c> (one warehouse per save slot / player).
/// 搜打撤模式的持久仓库（每个存档位一份）。卡牌/遗物/药水以可序列化形式存储，进库时归一：升级/附魔/局内成长剥除，
/// 但"身份牌"（剥离保存态后基础模型退化，如疯狂科学的敲钟类型/附效）保留其 Props 以保证可用
/// （见 <see cref="WarehouseStore.NormalizeCard"/>）。
/// </summary>
public sealed class WarehouseData
{
    /// <summary>Whether the first-time seed (starter/common cards + relics + 1000 gold) has been granted. 是否已发放首次种子。</summary>
    public bool Seeded { get; set; }

    /// <summary>
    /// Whether pre-base-only data has been normalized in place (one-shot legacy migration on first open after update).
    /// 是否已把升级前的旧数据原地归一（更新后首次打开的一次性迁移）。
    /// </summary>
    public bool Normalized { get; set; }

    /// <summary>
    /// Whether identity cards whose props were stripped by the pre-identity-aware normalize have been repaired in place
    /// (one-shot migration on first open after the identity fix — a base MadScience has <c>Type = None</c> and would
    /// crash on play). 是否已把旧版归一化抹掉 Props 的身份牌原地修复（身份修复更新后首次打开的一次性迁移——基础态疯狂科学
    /// 的 Type 为 None，打出即崩）。
    /// </summary>
    public bool IdentityRepaired { get; set; }

    /// <summary>
    /// Whether every copy's durability has been backfilled (one-shot migration on first open after the durability
    /// update — legacy saves deserialize with the 0 sentinel and get the rarity's max). 是否已回填每份耐久（耐久更新后
    /// 首次打开的一次性迁移——旧档以 0 哨兵反序列化，回填为稀有度上限）。
    /// </summary>
    public bool DurabilityInitialized { get; set; }

    /// <summary>
    /// Monotonic mutation counter. Bumped by every list/gold write so the module-level display cache
    /// (<c>WarehouseCache</c>) can invalidate by (instance reference, version) without needing a profile id.
    /// 单调递增的变更计数。每次列表/金币写入都会自增，供模块级展示缓存（WarehouseCache）按「实例引用 + 版本」失效。
    /// </summary>
    public int Version { get; set; }

    /// <summary>Stored cards, each an independent copy (duplicates allowed), base state only. 仓库中的卡牌（允许重复，仅基础态）。</summary>
    public List<WarehouseCard> Cards { get; set; } = new();

    /// <summary>Stored relics (duplicates allowed for stackable relics), base state only. 仓库中的遗物（仅基础态）。</summary>
    public List<WarehouseRelic> Relics { get; set; } = new();

    /// <summary>Stored potions. 仓库中的药水。</summary>
    public List<SerializablePotion> Potions { get; set; } = new();

    /// <summary>Stored gold. 仓库中的金币。</summary>
    public int Gold { get; set; }

    /// <summary>Persisted warehouse-hub UI state (per-tab search queries + filter selections). 仓库界面 UI 状态（各 Tab 搜索词 + 过滤选择）。</summary>
    public WarehouseFilterState Filters { get; set; } = new();
}

/// <summary>
/// Persisted warehouse-hub filter/search state. Queries and multi-select filter sets are kept per category so each tab
/// independently remembers what the player was looking at (survives tab switches and warehouse closes).
/// 仓库界面的持久化搜索/过滤状态：搜索词与多选过滤按品类分开保存，每个 Tab 独立记忆。
/// </summary>
public sealed class WarehouseFilterState
{
    public string QueryCards { get; set; } = "";
    public string QueryRelics { get; set; } = "";
    public string QueryPotions { get; set; } = "";

    public List<string> CardPools { get; set; } = new();

    public List<string> CardRarities { get; set; } = new();
    public List<string> CardTypes { get; set; } = new();
    public List<string> CardCosts { get; set; } = new();
    public List<string> CardSources { get; set; } = new();

    public List<string> RelicPools { get; set; } = new();
    public List<string> RelicRarities { get; set; } = new();
    public List<string> RelicSources { get; set; } = new();

    public List<string> PotionPools { get; set; } = new();
    public List<string> PotionRarities { get; set; } = new();
    public List<string> PotionSources { get; set; } = new();
}
