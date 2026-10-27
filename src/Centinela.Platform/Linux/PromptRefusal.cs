namespace Centinela.Platform.Linux;

/// <summary>
/// Remembers that the keychain's prompt was turned down (dismissed or left to time out), so it is not shown again
/// for every camera: until the keychain is found unlocked (or Centinela restarts), the keychain is simply not used.
/// </summary>
internal sealed class PromptRefusal
{
    volatile bool _refused;

    public bool MayPrompt => !_refused;

    public void Record(bool accepted) => _refused = !accepted;

    /// <summary>The keychain answered without a prompt: it is unlocked, so a later prompt is welcome again.</summary>
    public void Unlocked() => _refused = false;
}
