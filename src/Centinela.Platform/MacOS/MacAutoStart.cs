using System.Text;
using Centinela.Core.Platform;

namespace Centinela.Platform.MacOS;

/// <summary>
/// A per-user launchd agent (<c>&lt;agentsDir&gt;/es.tecxart.centinela.plist</c>) that starts this program with
/// <c>--tray</c> at login. Plain file access, so it needs no platform guard.
/// </summary>
public sealed class MacAutoStart(string agentsDir, string exePath) : IAutoStart
{
    // launchd reads the file at login: Enable starts nothing now and Disable does not stop the running copy.
    const string FileName = LaunchAgent.Label + ".plist";
    static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    string AgentPath => Path.Combine(agentsDir, FileName);

    /// <summary><c>&lt;home&gt;/Library/LaunchAgents</c>.</summary>
    public static string DefaultDirectory(string home) => $"{home.TrimEnd('/')}/Library/LaunchAgents";

    /// <summary>The agent exists, runs this program and is not marked <c>Disabled</c>.</summary>
    public bool IsEnabled => Read() is { } text && !LaunchAgent.IsDisabled(text) && LaunchAgent.ProgramOf(text) == exePath;

    public void Enable()
    {
        Directory.CreateDirectory(agentsDir);
        var temp = Path.Combine(agentsDir, $".{FileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temp, LaunchAgent.For(exePath), Utf8NoBom);
            File.Move(temp, AgentPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    public void Disable()
    {
        if (File.Exists(AgentPath)) File.Delete(AgentPath);
    }

    /// <summary>
    /// Rewrites an active agent that runs another Centinela (an older install elsewhere) so it runs this one.
    /// A disabled agent, or one whose program cannot be read, is left alone.
    /// </summary>
    public bool Migrate()
    {
        if (Read() is not { } text || LaunchAgent.IsDisabled(text) || LaunchAgent.ProgramOf(text) is not { } program
            || program == exePath) return false;
        Enable();
        return true;
    }

    string? Read()
    {
        try
        {
            return File.Exists(AgentPath) ? File.ReadAllText(AgentPath) : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
