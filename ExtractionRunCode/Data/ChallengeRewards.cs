using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Relics;

namespace ExtractionRun.Data;

/// <summary>One ordered boss-victory reward action. Multiple actions on a definition compose in declaration order.</summary>
public abstract record ChallengeRewardAction
{
    internal abstract string CatalogToken { get; }
}

/// <summary>Duplicates healthy returned carried copies at full durability and doubles deposited gold.</summary>
public sealed record DoubleReturnedCarryRewardAction : ChallengeRewardAction
{
    internal override string CatalogToken => "double-returned-carry";
}

/// <summary>Grants cards of one rarity; count 0 means one of every qualifying card.</summary>
public sealed record GrantCardRarityRewardAction(CardRarity Rarity, int Count) : ChallengeRewardAction
{
    internal override string CatalogToken => $"grant-card-rarity:{(int)Rarity}:{Count}";
}

/// <summary>Grants a fixed set of cards, each repeated <see cref="Count"/> times.</summary>
public sealed record GrantFixedCardsRewardAction(IReadOnlyList<string> CardIds, int Count) : ChallengeRewardAction
{
    internal override string CatalogToken => "grant-fixed:" + Count + ":" + string.Join(',', CardIds);
}

/// <summary>Grants one of every card in the cleared character's pools.</summary>
public sealed record GrantAllCharacterCardsRewardAction : ChallengeRewardAction
{
    internal override string CatalogToken => "grant-all-character-cards";
}

/// <summary>Grants relics of one rarity.</summary>
public sealed record GrantRelicRarityRewardAction(RelicRarity Rarity, int Count) : ChallengeRewardAction
{
    internal override string CatalogToken => $"grant-relic-rarity:{(int)Rarity}:{Count}";
}
