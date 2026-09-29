namespace Centinela.Core.Platform;

public enum OsKind { Windows, MacOS, Linux }

public static class OsInfo
{
    /// <summary>The system this process runs on; anything that is neither Windows nor macOS counts as Linux.</summary>
    public static OsKind Current =>
        OperatingSystem.IsWindows() ? OsKind.Windows : OperatingSystem.IsMacOS() ? OsKind.MacOS : OsKind.Linux;
}
