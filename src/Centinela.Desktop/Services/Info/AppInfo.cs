using System.Reflection;

namespace Centinela.Desktop.Services.Info;

/// <summary>The app's version, repository and support addresses.</summary>
static class AppInfo
{
    public const string Repository = "tecxion/centinela";
    public const string RepositoryUrl = "https://github.com/" + Repository;
    public const string WebsiteUri = "https://www.tecxart.es";
    public const string EmailUri = "mailto:tecxart@gmail.com";

    /// <summary>The app version from Directory.Build.props, always major.minor.patch.</summary>
    public static Version Version { get; } = Normalize(Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0));

    public static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));
}
