using System.Reflection;

namespace Centinela.App;

static class AppInfo
{
    public const string Repository = "tecxion/centinela";
    public const string RepositoryUrl = "https://github.com/" + Repository;

    /// <summary>The app version from Directory.Build.props, always major.minor.patch.</summary>
    public static Version Version { get; } = Normalize(Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0));

    static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));
}
