using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using System.Security.Cryptography;
using System.Text;

namespace ExtractionRun.Data;

/// <summary>Challenge kinds served by the 挑战 page. 挑战页提供的两类挑战。</summary>
public enum ChallengeKind
{
    Daily,
    Permanent,
}

/// <summary>Browse-oriented mechanism categories. A challenge may belong to more than one category.</summary>
public enum ChallengeTag
{
    Carry,
    Deck,
    Combat,
    Map,
    Survival,
}

/// <summary>
/// One immutable challenge entry: stable id, kind, parameterized rules + ordered reward actions. 一条不可变挑战定义：
/// 稳定 id、类别、参数化规则和有序奖励动作。
/// </summary>
public sealed class ChallengeDef
{
    public required string Id { get; init; }

    public required ChallengeKind Kind { get; init; }

    public required IReadOnlyList<ChallengeRule> Rules { get; init; }

    /// <summary>Other ids that must be selected before this challenge can be selected.</summary>
    public IReadOnlyList<string> RequiredChallengeIds { get; init; } = Array.Empty<string>();

    /// <summary>Mutually exclusive tags. Two selected definitions may not share one.</summary>
    public IReadOnlyList<string> ConflictGroups { get; init; } = Array.Empty<string>();

    /// <summary>Stable browse categories shown by the hub filter.</summary>
    public IReadOnlyList<ChallengeTag> Tags { get; init; } = Array.Empty<ChallengeTag>();

    /// <summary>Ordered reward actions granted only on a boss-victory clear. 常规 Boss 胜利才发放的有序奖励动作。</summary>
    public IReadOnlyList<ChallengeRewardAction> Rewards { get; init; } = Array.Empty<ChallengeRewardAction>();
}

/// <summary>
/// Data-driven challenge definitions. Everything the hub shows and the run enforces derives from this list
/// (plus <see cref="ChallengeRuntime"/> for the modifier). Every new entry reuses typed rule primitives; no feature
/// may address an id directly outside this catalog. 挑战定义（数据驱动）。挑战页展示与局内执行全部由此表派生；modifier
/// 只消费运行时聚合的规则。
/// </summary>
public static class ChallengeRegistry
{
    public const int CatalogSchemaVersion = 1;
    public const string IdBasicCommon = "BASIC_COMMON";
    public const string IdHpOne = "HP_ONE";
    public const string IdCurses = "CURSES";
    public const string IdDoubleEnemy = "DOUBLE_ENEMY";
    public const string IdOneRest = "ONE_REST";
    public const string IdStrikeOnly = "STRIKE_ONLY";
    public const string IdHandPressure = "HAND_PRESSURE";
    public const string IdTenCardsCurses = "TEN_CARDS_CURSES";
    public const string IdEscalatingEnemies = "ESCALATING_ENEMIES";
    public const string IdAllElite = "ALL_ELITE";
    public const string IdEmptyCarry = "EMPTY_CARRY";
    public const string IdTwoCardRewards = "TWO_CARD_REWARDS";

    private static readonly ChallengeDef[] AllDefs =
    {
        // Daily — entries are sampled without replacement for the hub's five daily slots. 每日条目在大厅的五个槽位中无放回抽取。
        new()
        {
            Id = IdBasicCommon,
            Kind = ChallengeKind.Daily,
            Rules =
            [
                new CarryCardRarityRule(CardRarity.Basic, CardRarity.Common),
                new CardAcquisitionRarityRule(CardRarity.Basic, CardRarity.Common),
            ],
            Tags = [ChallengeTag.Carry, ChallengeTag.Deck],
            Rewards = [new GrantCardRarityRewardAction(CardRarity.Rare, 0)],
        },
        new()
        {
            Id = IdHpOne,
            Kind = ChallengeKind.Daily,
            Rules = [new StartingMaxHpRule(1)],
            Tags = [ChallengeTag.Survival],
            Rewards = [new DoubleReturnedCarryRewardAction()],
        },
        new()
        {
            Id = IdCurses,
            Kind = ChallengeKind.Daily,
            Rules = [new AddRandomCursesRule(2)],
            Tags = [ChallengeTag.Deck],
            Rewards = [new GrantCardRarityRewardAction(CardRarity.Ancient, 3)],
        },
        new()
        {
            Id = IdDoubleEnemy,
            Kind = ChallengeKind.Daily,
            Rules = [new EnemyStatMultiplierRule(2m, 2m)],
            Tags = [ChallengeTag.Combat],
            Rewards = [new GrantAllCharacterCardsRewardAction()],
        },
        new()
        {
            Id = IdOneRest,
            Kind = ChallengeKind.Daily,
            Rules = [new MapPointLimitRule(MegaCrit.Sts2.Core.Map.MapPointType.RestSite, 1,
                MegaCrit.Sts2.Core.Map.MapPointType.Unknown)],
            Tags = [ChallengeTag.Map],
            Rewards = [new GrantRelicRarityRewardAction(RelicRarity.Ancient, 5)],
        },
        new()
        {
            Id = IdStrikeOnly,
            Kind = ChallengeKind.Daily,
            Rules = [new CarryCardTagRule(CardTag.Strike)],
            Tags = [ChallengeTag.Carry, ChallengeTag.Deck],
            Rewards = [new GrantFixedCardsRewardAction(["HELLRAISER"], 3)],
        },
        new()
        {
            Id = IdHandPressure,
            Kind = ChallengeKind.Daily,
            Rules = [new HandEndDamageRule(1)],
            Tags = [ChallengeTag.Combat, ChallengeTag.Survival],
            Rewards = [new GrantFixedRelicsRewardAction(
            [
                ModelDb.Relic<FakeAnchor>().Id.Entry,
                ModelDb.Relic<FakeBloodVial>().Id.Entry,
                ModelDb.Relic<FakeHappyFlower>().Id.Entry,
                ModelDb.Relic<FakeLeesWaffle>().Id.Entry,
                ModelDb.Relic<FakeMango>().Id.Entry,
                ModelDb.Relic<FakeMerchantsRug>().Id.Entry,
                ModelDb.Relic<FakeOrichalcum>().Id.Entry,
                ModelDb.Relic<FakeSneckoEye>().Id.Entry,
                ModelDb.Relic<FakeStrikeDummy>().Id.Entry,
                ModelDb.Relic<FakeVenerableTeaSet>().Id.Entry,
            ], 3)],
        },
        new()
        {
            Id = IdTenCardsCurses,
            Kind = ChallengeKind.Daily,
            Rules = [new CardPlayLimitRule(10), new AddRandomCursesPerActRule(1)],
            Tags = [ChallengeTag.Combat, ChallengeTag.Deck],
            Rewards = [
                new GrantRelicRarityRewardAction(RelicRarity.Ancient, 1),
                new GrantCardRarityRewardAction(CardRarity.Ancient, 2),
            ],
        },
        new()
        {
            Id = IdEscalatingEnemies,
            Kind = ChallengeKind.Daily,
            Rules = [new EnemyCardPlayScalingRule(5, 3, 100, 3, 1, 6)],
            Tags = [ChallengeTag.Combat],
            Rewards = [
                new GrantCardRarityRewardAction(CardRarity.Rare, 4),
                new GrantGoldRewardAction(300),
            ],
        },

        // Permanent — no reward; a clear is tracked as a ✓ on the page. 常驻无奖励，通关页面打勾。
        new()
        {
            Id = IdAllElite,
            Kind = ChallengeKind.Permanent,
            Rules = [new MapPointReplaceRule(MegaCrit.Sts2.Core.Map.MapPointType.Monster,
                MegaCrit.Sts2.Core.Map.MapPointType.Elite)],
            Tags = [ChallengeTag.Map, ChallengeTag.Combat],
        },
        new()
        {
            Id = IdEmptyCarry,
            Kind = ChallengeKind.Permanent,
            Rules = [new EmptyCarryRule(99)],
            Tags = [ChallengeTag.Carry, ChallengeTag.Survival],
        },
        new()
        {
            Id = IdTwoCardRewards,
            Kind = ChallengeKind.Permanent,
            Rules = [new CardRewardChoiceCountRule(2)],
            Tags = [ChallengeTag.Deck],
        },
    };

    public static IReadOnlyList<ChallengeDef> All { get; } = AllDefs;

    private static readonly IReadOnlyDictionary<string, string> LegacyIdAliases =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, ChallengeDef> ById;

    /// <summary>Stable digest of rule and reward semantics carried with multiplayer challenge selections.</summary>
    public static string CatalogHash { get; }

    static ChallengeRegistry()
    {
        ById = AllDefs.ToDictionary(def => def.Id, StringComparer.Ordinal);
        if (ById.Count != AllDefs.Length || AllDefs.Any(def => string.IsNullOrWhiteSpace(def.Id)
            || def.Id.Contains(',') || def.Rules.Count == 0 || def.Rewards.Any(action => !IsValidReward(action))))
        {
            throw new InvalidOperationException("Challenge catalog contains duplicate/invalid ids or an empty rule set.");
        }

        foreach (ChallengeDef definition in AllDefs)
        {
            if (definition.RequiredChallengeIds.Any(id => !ById.ContainsKey(id))
                || definition.ConflictGroups.Any(string.IsNullOrWhiteSpace)
                || definition.Tags.Any(tag => !Enum.IsDefined(tag)))
            {
                throw new InvalidOperationException($"Challenge catalog has invalid dependencies: {definition.Id}");
            }
        }
        ValidateDependencyGraph();

        string catalog = string.Join('\n', AllDefs.Select(CatalogSignature));
        CatalogHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(catalog)))[..16];
        ValidateSelectionRegressionContracts();
    }

    public static IEnumerable<ChallengeDef> Dailies => AllDefs.Where(d => d.Kind == ChallengeKind.Daily);

    public static IEnumerable<ChallengeDef> Permanents => AllDefs.Where(d => d.Kind == ChallengeKind.Permanent);

    public static ChallengeDef? Get(string id) => TryResolveId(id, out string resolvedId)
        ? ById[resolvedId]
        : null;

    public static bool TryResolveId(string? rawId, out string resolvedId)
    {
        resolvedId = "";
        if (string.IsNullOrWhiteSpace(rawId))
        {
            return false;
        }

        string candidate = rawId.Trim();
        if (LegacyIdAliases.TryGetValue(candidate, out string? alias))
        {
            candidate = alias;
        }

        if (!ById.ContainsKey(candidate))
        {
            return false;
        }

        resolvedId = candidate;
        return true;
    }

    public static ChallengeKind KindOf(string id) =>
        Get(id)?.Kind ?? throw new InvalidOperationException($"Unknown challenge id: {id}");

    public static bool IsDaily(string id) => Get(id)?.Kind == ChallengeKind.Daily;

    private static string CatalogSignature(ChallengeDef definition) => string.Join('|',
        definition.Id,
        definition.Kind,
        string.Join(';', definition.Rules.Select(rule => rule.CatalogToken)),
        string.Join(';', definition.RequiredChallengeIds),
        string.Join(';', definition.ConflictGroups),
        string.Join(';', definition.Tags.Select(tag => (int)tag)),
        string.Join(';', definition.Rewards.Select(action => action.CatalogToken)));

    private static bool IsValidReward(ChallengeRewardAction action) => action switch
    {
        DoubleReturnedCarryRewardAction => true,
        GrantCardRarityRewardAction { Count: < 0 } => false,
        GrantCardRarityRewardAction => true,
        GrantRelicRarityRewardAction { Count: < 0 } => false,
        GrantRelicRarityRewardAction => true,
        GrantFixedCardsRewardAction { Count: <= 0 } => false,
        GrantFixedCardsRewardAction { CardIds.Count: 0 } => false,
        GrantFixedCardsRewardAction fixedCards => fixedCards.CardIds.All(id => !string.IsNullOrWhiteSpace(id)
            && !id.Contains(',')),
        GrantFixedRelicsRewardAction { Count: <= 0 } => false,
        GrantFixedRelicsRewardAction { RelicIds.Count: 0 } => false,
        GrantFixedRelicsRewardAction fixedRelics => fixedRelics.RelicIds.All(id => !string.IsNullOrWhiteSpace(id)
            && !id.Contains(',')),
        GrantGoldRewardAction { Amount: <= 0 } => false,
        GrantGoldRewardAction => true,
        GrantAllCharacterCardsRewardAction => true,
        _ => false,
    };

    private static void ValidateDependencyGraph()
    {
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);

        foreach (ChallengeDef definition in AllDefs)
        {
            Visit(definition.Id);
        }

        return;

        void Visit(string id)
        {
            if (visited.Contains(id))
            {
                return;
            }
            if (!visiting.Add(id))
            {
                throw new InvalidOperationException($"Challenge catalog has a dependency cycle at {id}.");
            }

            foreach (string required in ById[id].RequiredChallengeIds)
            {
                Visit(required);
            }

            visiting.Remove(id);
            visited.Add(id);
        }
    }

    /// <summary>Small no-framework regression suite for catalog data; executes during static catalog initialization.</summary>
    private static void ValidateSelectionRegressionContracts()
    {
        foreach (ChallengeDef definition in AllDefs)
        {
            ChallengeSelectionResult selection = ChallengeSelectionService.NormalizeRunIds(
                [definition.Id, definition.Id, "__UNKNOWN_CHALLENGE__"]);
            if (selection.Ids.Count != 1 || selection.Ids[0] != definition.Id || selection.RejectedIds.Count != 2)
            {
                throw new InvalidOperationException($"Challenge selection regression failed for {definition.Id}.");
            }

            ChallengeRuntime runtime = ChallengeRuntime.FromIds([definition.Id]);
            if (runtime.ChallengeIds.Count != 1 || runtime.ChallengeIds[0] != definition.Id)
            {
                throw new InvalidOperationException($"Challenge runtime regression failed for {definition.Id}.");
            }
        }
    }
}
