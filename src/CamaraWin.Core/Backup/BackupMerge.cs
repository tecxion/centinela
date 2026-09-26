namespace CamaraWin.Core;

public sealed record MergeResult(IReadOnlyList<Camera> Cameras, int Added, int Updated, int WithoutPassword, IReadOnlySet<Guid> UpdatedIds);

public static class BackupMerge
{
    /// <summary>Returns a new list (clones); inputs are not modified.</summary>
    public static MergeResult Merge(IReadOnlyList<Camera> existing, IReadOnlyList<Camera> imported)
    {
        var result = existing.Select(c => c.Clone()).ToList();
        var updatedIds = new HashSet<Guid>();
        var touched = new List<Camera>();
        var added = 0;
        var nextOrder = result.Count == 0 ? 0 : result.Max(c => c.Order) + 1;
        foreach (var incoming in imported)
        {
            var match = result.FirstOrDefault(c => SameCamera(c, incoming));
            if (match is null)
            {
                var copy = incoming.Clone();
                copy.Id = Guid.NewGuid();
                copy.Order = nextOrder++;
                result.Add(copy);
                touched.Add(copy);
                added++;
                continue;
            }
            match.Name = incoming.Name;
            match.Brand = incoming.Brand;
            match.Host = incoming.Host;
            match.Port = incoming.Port;
            match.User = incoming.User;
            if (incoming.Password.Length > 0) match.Password = incoming.Password;
            match.MainUrlOverride = incoming.MainUrlOverride;
            match.SubUrlOverride = incoming.SubUrlOverride;
            match.UseUdp = incoming.UseUdp;
            updatedIds.Add(match.Id);
            touched.Add(match);
        }
        return new MergeResult(result, added, updatedIds.Count, touched.Count(c => c.Password.Length == 0), updatedIds);
    }

    static bool SameCamera(Camera a, Camera b) =>
        a.Host.Length > 0 && b.Host.Length > 0
            ? string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase) && a.Port == b.Port
            : a.Host.Length == 0 && b.Host.Length == 0 && a.MainUrlOverride is { } x && b.MainUrlOverride is { } y
              && string.Equals(x, y, StringComparison.OrdinalIgnoreCase);
}
