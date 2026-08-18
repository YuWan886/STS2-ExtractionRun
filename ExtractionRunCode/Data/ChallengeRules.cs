using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using System.Globalization;

namespace ExtractionRun.Data;

/// <summary>Parameterized rule primitives consumed by <see cref="ChallengeRuntime"/>.</summary>
public abstract record ChallengeRule
{
    internal abstract string CatalogToken { get; }
}

/// <summary>Only cards with one of these rarities may be carried into the run.</summary>
public sealed record CarryCardRarityRule(params CardRarity[] AllowedRarities) : ChallengeRule
{
    internal override string CatalogToken => "carry-rarity:" + string.Join(',', AllowedRarities.Select(r => (int)r));
}

/// <summary>Only cards carrying this tag may be carried into the run.</summary>
public sealed record CarryCardTagRule(CardTag RequiredTag) : ChallengeRule
{
    internal override string CatalogToken => "carry-tag:" + (int)RequiredTag;
}

/// <summary>Starts without carried items and grants the character starter kit.</summary>
public sealed record EmptyCarryRule(int StarterGold) : ChallengeRule
{
    internal override string CatalogToken => "empty-carry:" + StarterGold;
}

/// <summary>Caps a player's maximum HP at run creation.</summary>
public sealed record StartingMaxHpRule(int MaxHp) : ChallengeRule
{
    internal override string CatalogToken => "max-hp:" + MaxHp;
}

/// <summary>Adds a deterministic number of random curses at run creation.</summary>
public sealed record AddRandomCursesRule(int Count) : ChallengeRule
{
    internal override string CatalogToken => "curses:" + Count;
}

/// <summary>Scales every enemy's max HP and outgoing damage.</summary>
public sealed record EnemyStatMultiplierRule(decimal HpMultiplier, decimal DamageMultiplier) : ChallengeRule
{
    internal override string CatalogToken => "enemy-scale:"
        + HpMultiplier.ToString(CultureInfo.InvariantCulture) + ":"
        + DamageMultiplier.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Filters card rewards and merchant card pools to these rarities.</summary>
public sealed record CardAcquisitionRarityRule(params CardRarity[] AllowedRarities) : ChallengeRule
{
    internal override string CatalogToken => "acquire-rarity:" + string.Join(',', AllowedRarities.Select(r => (int)r));
}

/// <summary>Rewrites a generated map point type.</summary>
public sealed record MapPointReplaceRule(MapPointType Source, MapPointType Replacement) : ChallengeRule
{
    internal override string CatalogToken => $"map-replace:{(int)Source}:{(int)Replacement}";
}

/// <summary>Keeps at most one deterministic subset of a map point type per act.</summary>
public sealed record MapPointLimitRule(MapPointType PointType, int MaxPerAct, MapPointType Replacement) : ChallengeRule
{
    internal override string CatalogToken => $"map-limit:{(int)PointType}:{MaxPerAct}:{(int)Replacement}";
}

/// <summary>Limits every generated card-reward offer to this many choices.</summary>
public sealed record CardRewardChoiceCountRule(int Count) : ChallengeRule
{
    internal override string CatalogToken => "card-reward-choices:" + Count;
}

/// <summary>Deals this much unblockable damage for every card left in hand when a player ends their turn.</summary>
public sealed record HandEndDamageRule(int DamagePerCard) : ChallengeRule
{
    internal override string CatalogToken => "hand-end-damage:" + DamagePerCard;
}

/// <summary>Limits each player's manual card plays during one turn.</summary>
public sealed record CardPlayLimitRule(int MaxPlaysPerTurn) : ChallengeRule
{
    internal override string CatalogToken => "card-play-limit:" + MaxPlaysPerTurn;
}

/// <summary>Adds a deterministic number of random curses after every completed act.</summary>
public sealed record AddRandomCursesPerActRule(int Count) : ChallengeRule
{
    internal override string CatalogToken => "curses-per-act:" + Count;
}

/// <summary>Scales all current enemies at configured completed-card-play thresholds in a combat.</summary>
public sealed record EnemyCardPlayScalingRule(int HpPercentPerTrigger, int CardsPerHpIncrease, int MaxHpPercent,
    int CardsPerStrength, int StrengthPerTrigger, int MaxStrength) : ChallengeRule
{
    internal override string CatalogToken => $"enemy-card-scale:{HpPercentPerTrigger}:{CardsPerHpIncrease}:{MaxHpPercent}:"
        + $"{CardsPerStrength}:{StrengthPerTrigger}:{MaxStrength}";
}

/// <summary>
/// The normalized, immutable runtime view of a challenge selection. It is the sole place that composes rules, so Hub,
/// modifier hooks and Harmony patches never need to know individual challenge ids.
/// </summary>
public sealed class ChallengeRuntime
{
    private readonly IReadOnlyList<ChallengeRule> _rules;

    private ChallengeRuntime(IReadOnlyList<string> ids, IReadOnlyList<ChallengeRule> rules)
    {
        ChallengeIds = ids;
        _rules = rules;
    }

    public IReadOnlyList<string> ChallengeIds { get; }

    public bool HasChallenges => ChallengeIds.Count > 0;

    public bool DoublesReturnedCarry => ChallengeIds
        .Select(ChallengeRegistry.Get)
        .Where(definition => definition != null)
        .SelectMany(definition => definition!.Rewards)
        .OfType<DoubleReturnedCarryRewardAction>()
        .Any();

    public bool StartsEmpty => _rules.OfType<EmptyCarryRule>().Any();

    public int StarterGold => _rules.OfType<EmptyCarryRule>().Select(rule => rule.StarterGold).DefaultIfEmpty(0).Max();

    public int? StartingMaxHp
    {
        get
        {
            int[] values = _rules.OfType<StartingMaxHpRule>().Select(rule => rule.MaxHp).ToArray();
            return values.Length == 0 ? null : values.Min();
        }
    }

    public int RandomCurseCount => _rules.OfType<AddRandomCursesRule>().Sum(rule => rule.Count);

    public int RandomCursesPerAct => _rules.OfType<AddRandomCursesPerActRule>().Sum(rule => rule.Count);

    public int? CardRewardChoiceCount
    {
        get
        {
            int[] counts = _rules.OfType<CardRewardChoiceCountRule>().Select(rule => rule.Count).ToArray();
            return counts.Length == 0 ? null : counts.Min();
        }
    }

    public int? CardPlayLimitPerTurn
    {
        get
        {
            int[] limits = _rules.OfType<CardPlayLimitRule>().Select(rule => rule.MaxPlaysPerTurn).ToArray();
            return limits.Length == 0 ? null : limits.Min();
        }
    }

    public int HandEndDamagePerCard => _rules.OfType<HandEndDamageRule>().Sum(rule => rule.DamagePerCard);

    public EnemyCardPlayScalingRule? EnemyCardPlayScaling => _rules.OfType<EnemyCardPlayScalingRule>()
        .OrderByDescending(rule => rule.HpPercentPerTrigger)
        .FirstOrDefault();

    public decimal EnemyHpMultiplier => _rules.OfType<EnemyStatMultiplierRule>()
        .Aggregate(1m, (value, rule) => value * rule.HpMultiplier);

    public decimal EnemyDamageMultiplier => _rules.OfType<EnemyStatMultiplierRule>()
        .Aggregate(1m, (value, rule) => value * rule.DamageMultiplier);

    public int ScaleEnemyMaxHp(int maxHp) => Math.Max(1, (int)Math.Ceiling(maxHp * EnemyHpMultiplier));

    public IReadOnlyList<MapPointLimitRule> MapPointLimits => _rules.OfType<MapPointLimitRule>().ToArray();

    public static ChallengeRuntime FromIds(IEnumerable<string>? ids)
    {
        ChallengeSelectionResult selection = ChallengeSelectionService.NormalizeRunIds(ids);
        List<ChallengeRule> rules = selection.Ids
            .Select(ChallengeRegistry.Get)
            .Where(def => def != null)
            .SelectMany(def => def!.Rules)
            .ToList();
        return new ChallengeRuntime(selection.Ids, rules);
    }

    public static ChallengeRuntime FromDefinition(ChallengeDef definition) =>
        new([definition.Id], definition.Rules);

    public bool AllowsCarryCard(CardModel card)
    {
        foreach (CarryCardRarityRule rule in _rules.OfType<CarryCardRarityRule>())
        {
            if (!rule.AllowedRarities.Contains(card.Rarity))
            {
                return false;
            }
        }

        foreach (CarryCardTagRule rule in _rules.OfType<CarryCardTagRule>())
        {
            if (!card.Tags.Contains(rule.RequiredTag))
            {
                return false;
            }
        }

        return !StartsEmpty;
    }

    public bool AllowsAcquiredCard(CardModel card) => _rules.OfType<CardAcquisitionRarityRule>()
        .All(rule => rule.AllowedRarities.Contains(card.Rarity));

    public bool HasCardAcquisitionFilter => _rules.OfType<CardAcquisitionRarityRule>().Any();

    public MapPointType TransformMapPoint(MapPointType pointType)
    {
        foreach (MapPointReplaceRule rule in _rules.OfType<MapPointReplaceRule>())
        {
            if (pointType == rule.Source)
            {
                pointType = rule.Replacement;
            }
        }

        return pointType;
    }

    public bool HasCarryTag(CardTag tag) => _rules.OfType<CarryCardTagRule>().Any(rule => rule.RequiredTag == tag);

    public bool HasCarryRarityFilter => _rules.OfType<CarryCardRarityRule>().Any();

    public bool HasCarryTagFilter => _rules.OfType<CarryCardTagRule>().Any();

    public bool HasRule<T>() where T : ChallengeRule => _rules.OfType<T>().Any();
}
