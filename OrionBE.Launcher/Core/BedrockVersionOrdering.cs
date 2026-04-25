namespace OrionBE.Launcher.Core;

public static class BedrockVersionOrdering
{
    /// <summary>Descending: returns positive if <paramref name="a"/> is greater than <paramref name="b"/>.</summary>
    public static int CompareDescending(string a, string b) => CompareAscending(b, a);

    public static int CompareAscending(string a, string b)
    {
        var pa = a.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var pb = b.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var n = Math.Max(pa.Length, pb.Length);
        for (var i = 0; i < n; i++)
        {
            var va = i < pa.Length && int.TryParse(pa[i], out var x) ? x : int.MinValue;
            var vb = i < pb.Length && int.TryParse(pb[i], out var y) ? y : int.MinValue;
            var c = va.CompareTo(vb);
            if (c != 0)
            {
                return c;
            }
        }

        return string.CompareOrdinal(a, b);
    }
}
