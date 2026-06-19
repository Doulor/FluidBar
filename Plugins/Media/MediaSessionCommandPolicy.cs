namespace FluidBar;

public static class MediaSessionCommandPolicy
{
    public static IReadOnlyList<TSession> OrderCandidates<TSession>(
        IEnumerable<TSession> sessions,
        string? preferredSourceId,
        string? currentAumid,
        Func<TSession, string?> sourceId,
        Func<TSession, bool> isCurrent,
        Func<TSession, bool> isCommandEnabled)
    {
        var sessionList = sessions.ToList();
        var preferredMatches = string.IsNullOrWhiteSpace(preferredSourceId)
            ? []
            : sessionList
                .Where(session => IsSourceMatch(sourceId(session), preferredSourceId))
                .OrderByDescending(isCommandEnabled)
                .ThenByDescending(isCurrent)
                .ToList();

        if (preferredMatches.Count > 0)
            return preferredMatches;

        if (!ShouldFallbackToAnySession(preferredSourceId))
            return [];

        var currentMatches = string.IsNullOrWhiteSpace(currentAumid)
            ? []
            : sessionList
                .Where(session => IsSourceMatch(sourceId(session), currentAumid))
                .OrderByDescending(isCommandEnabled)
                .ThenByDescending(isCurrent)
                .ToList();

        if (currentMatches.Count > 0)
            return currentMatches;

        return sessionList
            .OrderByDescending(isCommandEnabled)
            .ThenByDescending(isCurrent)
            .ToList();
    }

    public static bool ShouldFallbackToAnySession(string? preferredSourceId)
    {
        if (string.IsNullOrWhiteSpace(preferredSourceId))
            return true;

        return MediaSnapshotSelectionPolicy.GetSourcePriority(preferredSourceId) < 100;
    }

    private static bool IsSourceMatch(string? sessionSourceId, string? requestedSourceId)
    {
        if (string.IsNullOrWhiteSpace(sessionSourceId) ||
            string.IsNullOrWhiteSpace(requestedSourceId))
        {
            return false;
        }

        return string.Equals(sessionSourceId, requestedSourceId, StringComparison.OrdinalIgnoreCase) ||
               MediaSnapshotSelectionPolicy.IsSamePlayerApp(sessionSourceId, requestedSourceId);
    }
}
