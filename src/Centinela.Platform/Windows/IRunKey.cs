namespace Centinela.Platform.Windows;

/// <summary>The HKCU Run key, behind an interface so tests never touch the real registry.</summary>
public interface IRunKey
{
    string? Get(string name);
    void Set(string name, string value);
    void Delete(string name);
}
