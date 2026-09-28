namespace Centinela.Core;

public sealed record MergeResult(IReadOnlyList<Camera> Cameras, int Added, int Updated, int WithoutPassword, IReadOnlySet<Guid> UpdatedIds);

public static class BackupMerge
{
    /// <summary>Returns a new list (clones); inputs are not modified.</summary>
    public static MergeResult Merge(IReadOnlyList<Camera> existing, IReadOnlyList<Camera> imported)
    {
        var result = existing.Select(c => c.Clone()).ToList();
        var updatedIds = new HashSet<Guid>();
        var addedIds = new HashSet<Guid>();
        var touched = new Dictionary<Guid, Camera>();
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
                addedIds.Add(copy.Id);
                touched[copy.Id] = copy;
                continue;
            }
            // A password-less entry must not redirect a stored password to different URLs.
            var keepsStoredPassword = incoming.Password.Length == 0 && match.Password.Length > 0;
            var before = match.Clone();
            match.Name = incoming.Name;
            match.Brand = incoming.Brand;
            match.Host = incoming.Host;
            match.Port = incoming.Port;
            match.User = incoming.User;
            if (incoming.Password.Length > 0) match.Password = incoming.Password;
            if (!keepsStoredPassword || SameUrl(match.MainUrlOverride, incoming.MainUrlOverride))
                match.MainUrlOverride = incoming.MainUrlOverride;
            if (!keepsStoredPassword || SameUrl(match.SubUrlOverride, incoming.SubUrlOverride))
                match.SubUrlOverride = incoming.SubUrlOverride;
            match.UseUdp = incoming.UseUdp;
            match.MotionEnabled = incoming.MotionEnabled;
            match.MotionSensitivity = incoming.MotionSensitivity;
            match.MotionCooldownSeconds = incoming.MotionCooldownSeconds;
            match.ConnectionAlerts = incoming.ConnectionAlerts;
            match.MotionAlerts = incoming.MotionAlerts;
            match.LowQualityWhenBig = incoming.LowQualityWhenBig;
            // Only real changes count: an identical re-import must not restart any live view or recording.
            if (!addedIds.Contains(match.Id) && Differs(before, match)) updatedIds.Add(match.Id);
            touched[match.Id] = match;
        }
        return new MergeResult(result, addedIds.Count, updatedIds.Count, touched.Values.Count(c => c.Password.Length == 0), updatedIds);
    }

    static bool Differs(Camera a, Camera b) =>
        a.Name != b.Name || a.Brand != b.Brand || a.Host != b.Host || a.Port != b.Port || a.User != b.User
        || a.Password != b.Password || a.UseUdp != b.UseUdp
        || a.MotionEnabled != b.MotionEnabled || a.MotionSensitivity != b.MotionSensitivity
        || a.MotionCooldownSeconds != b.MotionCooldownSeconds
        || a.ConnectionAlerts != b.ConnectionAlerts || a.MotionAlerts != b.MotionAlerts
        || a.LowQualityWhenBig != b.LowQualityWhenBig
        || !SameUrl(a.MainUrlOverride, b.MainUrlOverride) || !SameUrl(a.SubUrlOverride, b.SubUrlOverride);

    static bool SameUrl(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    static bool SameCamera(Camera a, Camera b) =>
        a.Host.Length > 0 && b.Host.Length > 0
            ? string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase) && a.Port == b.Port
            : a.Host.Length == 0 && b.Host.Length == 0 && a.MainUrlOverride is { } x && b.MainUrlOverride is { } y
              && string.Equals(x, y, StringComparison.OrdinalIgnoreCase);
}
