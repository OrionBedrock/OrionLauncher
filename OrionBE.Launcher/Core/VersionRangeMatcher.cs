namespace OrionBE.Launcher.Core;

public static class VersionRangeMatcher
{
    public static bool Matches(string? version, string? range)
    {
        if (string.IsNullOrWhiteSpace(range) || range == "*")
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        var clauses = range.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var clause in clauses)
        {
            if (!MatchesClause(version, clause))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesClause(string version, string clause)
    {
        if (clause.EndsWith(".*", StringComparison.Ordinal))
        {
            var prefix = clause[..^2];
            return version.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(version, prefix, StringComparison.OrdinalIgnoreCase);
        }

        if (clause.EndsWith(".x", StringComparison.OrdinalIgnoreCase))
        {
            var prefix = clause[..^2];
            return version.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(version, prefix, StringComparison.OrdinalIgnoreCase);
        }

        if (clause.StartsWith(">=", StringComparison.Ordinal))
        {
            return Compare(version, clause[2..]) >= 0;
        }

        if (clause.StartsWith("<=", StringComparison.Ordinal))
        {
            return Compare(version, clause[2..]) <= 0;
        }

        if (clause.StartsWith(">", StringComparison.Ordinal))
        {
            return Compare(version, clause[1..]) > 0;
        }

        if (clause.StartsWith("<", StringComparison.Ordinal))
        {
            return Compare(version, clause[1..]) < 0;
        }

        if (clause.StartsWith("=", StringComparison.Ordinal))
        {
            return Compare(version, clause[1..]) == 0;
        }

        return Compare(version, clause) == 0;
    }

    private static int Compare(string a, string b)
    {
        var pa = a.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var pb = b.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var n = Math.Max(pa.Length, pb.Length);
        for (var i = 0; i < n; i++)
        {
            var va = i < pa.Length && int.TryParse(pa[i], out var ai) ? ai : 0;
            var vb = i < pb.Length && int.TryParse(pb[i], out var bi) ? bi : 0;
            var c = va.CompareTo(vb);
            if (c != 0)
            {
                return c;
            }
        }

        return 0;
    }
}
