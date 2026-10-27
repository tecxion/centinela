using Centinela.Core;
using Centinela.Desktop.Services.Backup;

namespace Centinela.Desktop.Tests;

/// <summary>Reading a backup for «Importar JSON…»: passphrase, retry after a wrong one, cancel and errors.</summary>
public sealed class BackupReaderTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "centinela-desktop-" + Guid.NewGuid());

    public BackupReaderTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    string Write(string text)
    {
        var path = Path.Combine(_dir, Guid.NewGuid() + ".json");
        File.WriteAllText(path, text);
        return path;
    }

    static List<Camera> Sample() =>
    [
        new() { Name = "Patio", Host = "10.0.0.1", User = "admin", Password = "secreta", Order = 0 },
        new() { Name = "Garaje", Host = "10.0.0.2", User = "admin", Password = "otra", Order = 1 },
    ];

    [Fact]
    public async Task Plain_copy_is_read_without_asking_the_passphrase()
    {
        var path = Write(CameraBackup.Export(Sample(), null));
        var statuses = new List<string>();
        var asked = 0;
        var reader = new BackupReader(() => { asked++; return Task.FromResult<string?>("x"); },
            _ => Task.FromResult(false), statuses.Add);

        var result = await reader.ReadAsync(path);

        Assert.Null(result.Error);
        Assert.Equal(["Patio", "Garaje"], result.Import!.Cameras.Select(c => c.Name));
        Assert.Equal(0, asked);
        Assert.Equal(["Leyendo la copia…", ""], statuses);
    }

    [Fact]
    public async Task Wrong_passphrase_asks_to_retry_and_the_right_one_decrypts()
    {
        var path = Write(CameraBackup.Export(Sample(), "buena-clave"));
        var answers = new Queue<string>(["mala-clave", "buena-clave"]);
        var retries = new List<string>();
        var statuses = new List<string>();
        var reader = new BackupReader(() => Task.FromResult<string?>(answers.Dequeue()),
            message => { retries.Add(message); return Task.FromResult(true); }, statuses.Add);

        var result = await reader.ReadAsync(path);

        Assert.Equal(["La clave no es correcta o el archivo está dañado. ¿Probar con otra clave?"], retries);
        Assert.Null(result.Error);
        Assert.Equal(["secreta", "otra"], result.Import!.Cameras.Select(c => c.Password));
        Assert.Equal(["Descifrando la copia…", "", "Descifrando la copia…", ""], statuses);
    }

    [Fact]
    public async Task Cancelling_the_passphrase_gives_an_empty_result()
    {
        var path = Write(CameraBackup.Export(Sample(), "buena-clave"));
        var reader = new BackupReader(() => Task.FromResult<string?>(null), _ => Task.FromResult(false), _ => { });

        var result = await reader.ReadAsync(path);

        Assert.Null(result.Import);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task Not_a_backup_gives_an_error()
    {
        var path = Write("{}");
        var statuses = new List<string>();
        var reader = new BackupReader(() => Task.FromResult<string?>(null), _ => Task.FromResult(false), statuses.Add);

        var result = await reader.ReadAsync(path);

        Assert.Null(result.Import);
        Assert.NotNull(result.Error);
        Assert.Equal("", statuses[^1]);
    }
}
