using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;

namespace ExtractionRun.Data;

/// <summary>Challenge kinds served by the 挑战 page. 挑战页提供的两类挑战。</summary>
public enum ChallengeKind
{
    Daily,
    Permanent,
}

/// <summary>
/// A challenge's in-run effects. Multi-select aggregates into one <see cref="ChallengeEffects"/> via
/// <see cref="ChallengeRegistry.ComputeEffects"/> — a run carries every selected challenge's effect (bitwise OR).
/// 一条挑战对局内产生的效果。多选按位聚合（一局携带所有选中的挑战效果）。
/// </summary>
[Flags]
public enum ChallengeEffects
{
    None = 0,
    /// <summary>整局牌组只有基础+普通：携带钳制 + 卡牌奖励/商人过滤。</summary>
    BasicCommonOnly = 1 << 0,
    /// <summary>初始血量上限 = 1。</summary>
    HpOne = 1 << 1,
    /// <summary>开局塞入 2 张随机诅咒。</summary>
    Curses = 1 << 2,
    /// <summary>敌人房间全部变为精英房间。</summary>
    AllElite = 1 << 3,
    /// <summary>空携带进入跑局（初始牌组 + 初始遗物 + 99 金币）。</summary>
    EmptyCarry = 1 << 4,
    /// <summary>敌人血量与伤害翻倍（含 Boss）。</summary>
    DoubleEnemy = 1 << 5,
    /// <summary>每一幕只有一个休息处（随机保留一个，其余改 `?`）。</summary>
    OneRest = 1 << 6,
    /// <summary>只携带带「打击」标签的卡牌进入跑局（局内获取不锁）。</summary>
    StrikeOnly = 1 << 7,
}

/// <summary>
/// One challenge entry: stable id, kind, effect flags + a reward descriptor. The whole 池 is data-driven — a new
/// challenge is a new row here (plus its loc keys), never a new hook. 一条挑战条目：稳定 id、种类、效果位 + 奖励描述。
/// 整池数据驱动——加挑战就是加一行（外加本地化键），不写新钩子。
/// </summary>
public sealed class ChallengeDef
{
    public required string Id;

    public required ChallengeKind Kind;

    public required ChallengeEffects Effects;

    /// <summary>Rarity granted on a boss-victory clear, when <see cref="RewardCount"/> is set. 通关奖励稀有度。</summary>
    public CardRarity? RewardRarity;

    /// <summary>Relic rarity granted on a boss-victory clear (ONE_REST's 5 random Ancient relics).
    /// 通关奖励的遗物稀有度（ONE_REST 的 5 个随机先古遗物）。</summary>
    public RelicRarity? RewardRelicRarity;

    /// <summary>Grant count: 0 = one of every qualifying card, N = N random qualifying cards. 发放数量：0=该稀有度各一张；N=N 张随机。</summary>
    public int RewardCount;

    /// <summary>All-of-pool reward: every card in the cleared character's pool, one each (all rarities).
    /// 全池奖励：通关角色池中每张卡各一张（所有稀有度）。</summary>
    public bool AllCardsReward;

    /// <summary>Fixed reward: exact card ids granted (each once), bypassing the rarity/count path. 固定奖励：精确发放这些卡牌
    /// （各一张），不走稀有度/数量路径。</summary>
    public string[]? RewardCardIds;

    /// <summary>HP_ONE's special reward: carried copies come back doubled at extraction. 翻倍奖励（仅 HP_ONE）。</summary>
    public bool DoublesReward;
}

/// <summary>
/// Data-driven challenge definitions. Everything the hub shows and the run enforces derives from this list
/// (plus <see cref="ChallengeRegistry.ComputeEffects"/> for the modifier). 挑战定义（数据驱动）。挑战页展示与局内执行
/// 全部由此表派生；modifier 只消费 ComputeEffects 聚合后的效果位。
/// </summary>
public static class ChallengeRegistry
{
    public const string IdBasicCommon = "BASIC_COMMON";
    public const string IdHpOne = "HP_ONE";
    public const string IdCurses = "CURSES";
    public const string IdDoubleEnemy = "DOUBLE_ENEMY";
    public const string IdOneRest = "ONE_REST";
    public const string IdStrikeOnly = "STRIKE_ONLY";
    public const string IdAllElite = "ALL_ELITE";
    public const string IdEmptyCarry = "EMPTY_CARRY";

    private static readonly ChallengeDef[] AllDefs =
    {
        // Daily — first batch: 3 entries so a day's 3 slots are all distinct. 每日首批 3 条，保证每日 3 槽互不重复。
        new()
        {
            Id = IdBasicCommon,
            Kind = ChallengeKind.Daily,
            Effects = ChallengeEffects.BasicCommonOnly,
            RewardRarity = CardRarity.Rare,
            RewardCount = 0, // 该角色池内所有 Rare 各一张
        },
        new()
        {
            Id = IdHpOne,
            Kind = ChallengeKind.Daily,
            Effects = ChallengeEffects.HpOne,
            DoublesReward = true,
        },
        new()
        {
            Id = IdCurses,
            Kind = ChallengeKind.Daily,
            Effects = ChallengeEffects.Curses,
            RewardRarity = CardRarity.Ancient,
            RewardCount = 3, // 3 张随机 Ancient
        },
        new()
        {
            Id = IdDoubleEnemy,
            Kind = ChallengeKind.Daily,
            Effects = ChallengeEffects.DoubleEnemy,
            AllCardsReward = true, // 通关角色的所有卡牌各一张
        },
        new()
        {
            Id = IdOneRest,
            Kind = ChallengeKind.Daily,
            Effects = ChallengeEffects.OneRest,
            RewardRelicRarity = RelicRarity.Ancient,
            RewardCount = 5, // 5 张随机先古遗物
        },
        new()
        {
            Id = IdStrikeOnly,
            Kind = ChallengeKind.Daily,
            Effects = ChallengeEffects.StrikeOnly,
            RewardCardIds = new[] { "HELLRAISER" },
            RewardCount = 3, // 固定 3 张地狱狂徒
        },

        // Permanent — no reward; a clear is tracked as a ✓ on the page. 常驻无奖励，通关页面打勾。
        new()
        {
            Id = IdAllElite,
            Kind = ChallengeKind.Permanent,
            Effects = ChallengeEffects.AllElite,
        },
        new()
        {
            Id = IdEmptyCarry,
            Kind = ChallengeKind.Permanent,
            Effects = ChallengeEffects.EmptyCarry,
        },
    };

    public static IReadOnlyList<ChallengeDef> All { get; } = AllDefs;

    public static IEnumerable<ChallengeDef> Dailies => AllDefs.Where(d => d.Kind == ChallengeKind.Daily);

    public static IEnumerable<ChallengeDef> Permanents => AllDefs.Where(d => d.Kind == ChallengeKind.Permanent);

    public static ChallengeDef? Get(string id) =>
        string.IsNullOrEmpty(id) ? null : AllDefs.FirstOrDefault(d => d.Id == id);

    public static ChallengeKind KindOf(string id) =>
        Get(id)?.Kind ?? throw new InvalidOperationException($"Unknown challenge id: {id}");

    public static bool IsDaily(string id) => Get(id)?.Kind == ChallengeKind.Daily;

    public static ChallengeEffects ComputeEffects(IEnumerable<string> ids)
    {
        ChallengeEffects effects = ChallengeEffects.None;
        foreach (string id in ids)
        {
            if (Get(id) is { } def)
            {
                effects |= def.Effects;
            }
        }

        return effects;
    }

    /// <summary>True when any selected challenge carries <paramref name="effect"/>. 选中挑战中是否含某效果位。</summary>
    public static bool HasEffect(IEnumerable<string> ids, ChallengeEffects effect) =>
        (ComputeEffects(ids) & effect) != 0;

    /// <summary>
    /// Basic/Common-only rule for the Engine A reward filter and the carry clamp. Curses/status cards are NOT caught
    /// here — they are never card *rewards* (they enter the deck via direct event/relic grants, which bypass the reward
    /// pool filter), so the exemption is automatic. 基础+普通判定：诅咒/状态卡不走卡牌奖励池（事件/遗物直接塞入牌组），
    /// 因此不经过本过滤——「诅咒豁免」天然成立。
    /// </summary>
    public static bool IsBasicCommonRarity(CardModel card) =>
        card.Rarity is CardRarity.Basic or CardRarity.Common;

    /// <summary>True when a card carries the 打击 tag (StrikeIronclad, PommelStrike, PerfectedStrike, etc. —
    /// <c>CardTag.Strike</c>, not a <c>CardKeyword</c>). Used by the STRIKE_ONLY carry filter. 是否「打击」标签卡
    /// （CardTag.Strike，非 CardKeyword）——STRIKE_ONLY 携带过滤器用。</summary>
    public static bool IsStrikeCard(CardModel card) => card.Tags.Contains(CardTag.Strike);
}