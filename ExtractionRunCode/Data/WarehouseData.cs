using MegaCrit.Sts2.Core.Saves.Runs;

namespace ExtractionRun.Data;

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
    /// Monotonic mutation counter. Bumped by every list/gold write so the module-level display cache
    /// (<c>WarehouseCache</c>) can invalidate by (instance reference, version) without needing a profile id.
    /// 单调递增的变更计数。每次列表/金币写入都会自增，供模块级展示缓存（WarehouseCache）按「实例引用 + 版本」失效。
    /// </summary>
    public int Version { get; set; }

    /// <summary>Stored cards, each an independent copy (duplicates allowed), base state only. 仓库中的卡牌（允许重复，仅基础态）。</summary>
    public List<SerializableCard> Cards { get; set; } = new();

    /// <summary>Stored relics (duplicates allowed for stackable relics), base state only. 仓库中的遗物（仅基础态）。</summary>
    public List<SerializableRelic> Relics { get; set; } = new();

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
