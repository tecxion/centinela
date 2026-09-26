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
