namespace ExtractionRun.Data;

/// <summary>Normalized selection plus rejected raw ids for logging or UI feedback.</summary>
public sealed record ChallengeSelectionResult(IReadOnlyList<string> Ids, IReadOnlyList<string> RejectedIds)
{
    public bool IsRejected(string id) => RejectedIds.Contains(id);
}

/// <summary>
/// Owns all id normalization and hub eligibility checks. Run payloads deliberately validate against the full catalog:
/// clients may not have the host's local daily offer, but they must still enforce the host-selected daily rule.
/// </summary>
public static class ChallengeSelectionService
{
    public static ChallengeSelectionResult NormalizeRunIds(IEnumerable<string>? ids)
    {
        var accepted = new List<string>();
        var rejected = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (string rawId in ids ?? Array.Empty<string>())
        {
            if (!ChallengeRegistry.TryResolveId(rawId, out string id) || !seen.Add(id))
            {
                rejected.Add(rawId);
                continue;
            }

            accepted.Add(id);
        }

        return new ChallengeSelectionResult(accepted, rejected);
    }

    /// <summary>Validates a host/singleplayer hub draft against today's daily offer and definition dependencies.</summary>
    public static ChallengeSelectionResult NormalizeHubDraft(IEnumerable<string>? ids, IEnumerable<string>? dailyOfferIds)
    {
        ChallengeSelectionResult normalized = NormalizeRunIds(ids);
        var accepted = new List<string>();
        var rejected = new List<string>(normalized.RejectedIds);
        HashSet<string> dailyOffer = NormalizeRunIds(dailyOfferIds).Ids.ToHashSet(StringComparer.Ordinal);

        foreach (string id in normalized.Ids)
        {
            ChallengeDef definition = ChallengeRegistry.Get(id)!;
            if (definition.Kind == ChallengeKind.Daily && !dailyOffer.Contains(id))
            {
                rejected.Add(id);
                continue;
            }

            if (definition.RequiredChallengeIds.Any(required => !accepted.Contains(required))
                || definition.ConflictGroups.Any(group => accepted
                    .Select(ChallengeRegistry.Get)
                    .Where(other => other != null)
                    .Any(other => other!.ConflictGroups.Contains(group))))
            {
                rejected.Add(id);
                continue;
            }

            accepted.Add(id);
        }

        return new ChallengeSelectionResult(accepted, rejected);
    }

    public static string SerializeRunIds(IEnumerable<string>? ids) =>
        string.Join(',', NormalizeRunIds(ids).Ids);

    public static ChallengeSelectionResult ParseRunIds(string? payload) =>
        NormalizeRunIds(string.IsNullOrWhiteSpace(payload)
            ? Array.Empty<string>()
            : payload.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
