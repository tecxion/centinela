namespace Centinela.Core;

public enum Brand { Tapo, Imou, Custom }

public enum StreamKind { Main, Sub }

public sealed class Camera
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public Brand Brand { get; set; }
    public string Host { get; set; } = "";
    public int Port { get; set; } = 554;
    public string User { get; set; } = "";
    /// <summary>Plaintext, in memory only. Encrypted by CameraStore on disk.</summary>
    public string Password { get; set; } = "";
    public string? MainUrlOverride { get; set; }
    public string? SubUrlOverride { get; set; }
    public bool UseUdp { get; set; }
    public int Order { get; set; }

    public Camera Clone() => (Camera)MemberwiseClone();

    /// <summary>A copy to add as a new camera: new Id, name with " (copia)", same address and credentials.</summary>
    public Camera Duplicate()
    {
        var copy = Clone();
        copy.Id = Guid.NewGuid();
        copy.Name = $"{Name} (copia)";
        return copy;
    }
}
