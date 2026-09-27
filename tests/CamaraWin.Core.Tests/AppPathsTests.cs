using CamaraWin.Core;

namespace CamaraWin.Core.Tests;

public class AppPathsTests
{
    [Theory]
    [InlineData("Salón", "Salón")]
    [InlineData("Garaje: puerta/1", "Garaje_ puerta_1")]
    [InlineData("  ", "camara")]
    [InlineData("a*b?c", "a_b_c")]
    public void SanitizeFileName(string input, string expected) =>
        Assert.Equal(expected, AppPaths.SanitizeFileName(input));

    [Fact]
    public void DataDirectory_honors_override_variable_when_set()
    {
        var original = Environment.GetEnvironmentVariable(AppPaths.DataDirectoryVariable);
        var defaultPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CamaraWin");
        try
        {
            Environment.SetEnvironmentVariable(AppPaths.DataDirectoryVariable, @"C:\scratch\camarawin-data");
            Assert.Equal(@"C:\scratch\camarawin-data", AppPaths.DataDirectory);
            Assert.Equal(Path.Combine(@"C:\scratch\camarawin-data", "cameras.json"), CameraStore.DefaultPath);

            Environment.SetEnvironmentVariable(AppPaths.DataDirectoryVariable, "");
            Assert.Equal(defaultPath, AppPaths.DataDirectory);

            Environment.SetEnvironmentVariable(AppPaths.DataDirectoryVariable, null);
            Assert.Equal(defaultPath, AppPaths.DataDirectory);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AppPaths.DataDirectoryVariable, original);
        }
    }

    [Fact]
    public void Override_redirects_every_user_folder_and_isolates_the_instance()
    {
        var original = Environment.GetEnvironmentVariable(AppPaths.DataDirectoryVariable);
        const string root = @"C:\scratch\camarawin-data";
        try
        {
            Environment.SetEnvironmentVariable(AppPaths.DataDirectoryVariable, null);
            Assert.False(AppPaths.IsDataDirectoryOverridden);
            Assert.Equal("", AppPaths.InstanceSuffix);
            Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "CamaraWin"), AppPaths.RecordingsDirectory);
            Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "CamaraWin"), AppPaths.SnapshotsDirectory);
            Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CamaraWin"), AppPaths.DefaultBackupDirectory);

            Environment.SetEnvironmentVariable(AppPaths.DataDirectoryVariable, root);
            Assert.True(AppPaths.IsDataDirectoryOverridden);
            Assert.Equal(Path.Combine(root, "recordings"), AppPaths.RecordingsDirectory);
            Assert.Equal(Path.Combine(root, "snapshots"), AppPaths.SnapshotsDirectory);
            Assert.Equal(Path.Combine(root, "backup"), AppPaths.DefaultBackupDirectory);
            Assert.Equal(Path.Combine(root, "logs"), AppPaths.LogsDirectory);

            var suffix = AppPaths.InstanceSuffix;
            Assert.Matches("^\\.[0-9a-f]{8}$", suffix);
            // Same folder written differently → same instance; another folder → another instance.
            Environment.SetEnvironmentVariable(AppPaths.DataDirectoryVariable, root.ToUpperInvariant() + @"\");
            Assert.Equal(suffix, AppPaths.InstanceSuffix);
            Environment.SetEnvironmentVariable(AppPaths.DataDirectoryVariable, @"C:\scratch\otra");
            Assert.NotEqual(suffix, AppPaths.InstanceSuffix);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AppPaths.DataDirectoryVariable, original);
        }
    }

    [Fact]
    public void RecordingFile_uses_camera_folder_and_timestamp()
    {
        var path = AppPaths.RecordingFile("Jardín", new DateTime(2026, 9, 26, 20, 15, 3));
        Assert.Equal(Path.Combine(AppPaths.RecordingsDirectory, "Jardín", "2026-09-26_20-15-03.mkv"), path);
    }

    [Fact]
    public void SnapshotFile_uses_camera_prefix_and_timestamp()
    {
        var path = AppPaths.SnapshotFile("Jardín", new DateTime(2026, 9, 26, 20, 15, 3));
        Assert.Equal(Path.Combine(AppPaths.SnapshotsDirectory, "Jardín_2026-09-26_20-15-03.png"), path);
    }
}
