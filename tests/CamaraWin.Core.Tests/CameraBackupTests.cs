using System.Text.Json.Nodes;
using CamaraWin.Core;

namespace CamaraWin.Core.Tests;

public class CameraBackupTests
{
    static List<Camera> Sample() =>
    [
        new() { Name = "Garaje", Brand = Brand.Tapo, Host = "192.168.1.20", Port = 554, User = "camuser", Password = "Secreta#1", Order = 0 },
        new() { Name = "Jardín", Brand = Brand.Imou, Host = "192.168.1.30", Port = 554, User = "admin", Password = "L2ABCD", Order = 1, UseUdp = true },
        new() { Name = "Otra", Brand = Brand.Custom, MainUrlOverride = "rtsp://h/x", Order = 2 },
    ];

    [Fact]
    public void Round_trip_without_passphrase_drops_passwords()
    {
        var json = CameraBackup.Export(Sample(), null);
        Assert.DoesNotContain("Secreta#1", json);
        var result = CameraBackup.Import(json, () => throw new InvalidOperationException("must not ask"));
        Assert.False(result.WasEncrypted);
        Assert.Equal(new[] { "Garaje", "Jardín", "Otra" }, result.Cameras.Select(c => c.Name).ToArray());
        Assert.All(result.Cameras, c => Assert.Equal("", c.Password));
        Assert.True(result.Cameras[1].UseUdp);
        Assert.Equal("rtsp://h/x", result.Cameras[2].MainUrlOverride);
    }

    [Fact]
    public void Round_trip_with_passphrase_restores_passwords()
    {
        var json = CameraBackup.Export(Sample(), "clave-larga");
        Assert.DoesNotContain("Secreta#1", json);
        Assert.DoesNotContain("L2ABCD", json);
        var result = CameraBackup.Import(json, () => "clave-larga");
        Assert.True(result.WasEncrypted);
        Assert.Equal(new[] { "Secreta#1", "L2ABCD", "" }, result.Cameras.Select(c => c.Password).ToArray());
    }

    [Fact]
    public void Wrong_passphrase_throws() =>
        Assert.Throws<BackupPassphraseException>(() =>
            CameraBackup.Import(CameraBackup.Export(Sample(), "clave-larga"), () => "otra-clave"));

    [Fact]
    public void Cancelled_passphrase_throws_OperationCanceled() =>
        Assert.Throws<OperationCanceledException>(() =>
            CameraBackup.Import(CameraBackup.Export(Sample(), "clave-larga"), () => null));

    [Fact]
    public void Swapped_encrypted_passwords_fail_authentication()
    {
        var node = JsonNode.Parse(CameraBackup.Export(Sample(), "clave-larga"))!;
        var cams = node["cameras"]!.AsArray();
        (cams[0]!["password"], cams[1]!["password"]) = (cams[1]!["password"]!.DeepClone(), cams[0]!["password"]!.DeepClone());
        Assert.Throws<BackupPassphraseException>(() => CameraBackup.Import(node.ToJsonString(), () => "clave-larga"));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{ "format": "otra-cosa", "version": 1, "cameras": [] }""")]
    [InlineData("""{ "format": "camarawin-cameras", "version": 2, "cameras": [] }""")]
    public void Foreign_corrupt_or_future_files_throw_format(string json) =>
        Assert.Throws<BackupFormatException>(() => CameraBackup.Import(json, () => "x"));

    [Fact]
    public void Credentials_in_override_urls_are_not_exported()
    {
        var cam = new Camera { Name = "U", Brand = Brand.Custom, MainUrlOverride = "rtsp://bob:s3cret@h/x" };
        Assert.DoesNotContain("s3cret", CameraBackup.Export([cam], null));
    }

    [Fact]
    public void Example_file_imports()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CamaraWin.slnx"))) dir = dir.Parent;
        var json = File.ReadAllText(Path.Combine(dir!.FullName, "docs", "ejemplo-camaras.json"));
        Assert.Equal(2, CameraBackup.Import(json, () => null).Cameras.Count);
    }

    [Fact]
    public void Automatic_copy_has_no_passwords_and_replaces_previous()
    {
        var dir = Path.Combine(Path.GetTempPath(), "camarawin-bk-" + Guid.NewGuid());
        try
        {
            BackupWriter.WriteAutomatic(dir, Sample());
            var path = BackupWriter.WriteAutomatic(dir, Sample().Take(1));
            var text = File.ReadAllText(path);
            Assert.Equal(Path.Combine(dir, BackupWriter.AutomaticFileName), path);
            Assert.DoesNotContain("Secreta#1", text);
            Assert.Single(CameraBackup.Import(text, () => null).Cameras);
            Assert.Single(Directory.GetFiles(dir));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Unsupported_iteration_count_throws_format()
    {
        var node = JsonNode.Parse(CameraBackup.Export(Sample(), "clave-larga"))!;
        node["encryption"]!["iterations"] = 1000;
        Assert.Throws<BackupFormatException>(() =>
            CameraBackup.Import(node.ToJsonString(), () => throw new InvalidOperationException("must not ask")));
    }
}
