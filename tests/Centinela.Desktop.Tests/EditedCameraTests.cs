using Centinela.Core;
using Centinela.Desktop.Services.Cameras;

namespace Centinela.Desktop.Tests;

public class EditedCameraTests
{
    [Fact]
    public void Merge_keeps_the_dialog_fields_and_the_stored_options()
    {
        var current = new Camera
        {
            Name = "A", Host = "a", User = "u", Password = "p", Order = 3,
            LowQualityWhenBig = true, Smoothing = true, MotionEnabled = true,
            MotionSensitivity = MotionSensitivity.High, MotionCooldownSeconds = 300,
            ConnectionAlerts = false, MotionAlerts = false, OriginalEncoders = "enc",
        };
        // The dialog's copy was taken before those options changed elsewhere.
        var edited = new Camera
        {
            Id = current.Id, Name = "Patio", Brand = Brand.Imou, Host = "10.0.0.9", Port = 8554, User = "v",
            Password = "q", MainUrlOverride = "rtsp://m", SubUrlOverride = "rtsp://s", UseUdp = true,
        };

        var merged = EditedCamera.Merge(edited, current);

        Assert.NotSame(edited, merged);
        Assert.NotSame(current, merged);
        Assert.Equal((current.Id, "Patio", Brand.Imou, "10.0.0.9", 8554, "v", "q"),
            (merged.Id, merged.Name, merged.Brand, merged.Host, merged.Port, merged.User, merged.Password));
        Assert.Equal(("rtsp://m", "rtsp://s", true), (merged.MainUrlOverride, merged.SubUrlOverride, merged.UseUdp));
        Assert.Equal((3, true, true, true), (merged.Order, merged.LowQualityWhenBig, merged.Smoothing, merged.MotionEnabled));
        Assert.Equal((MotionSensitivity.High, 300, false, false, "enc"),
            (merged.MotionSensitivity, merged.MotionCooldownSeconds, merged.ConnectionAlerts, merged.MotionAlerts,
                merged.OriginalEncoders));
        // Inputs untouched.
        Assert.Equal(("Patio", 0, false), (edited.Name, edited.Order, edited.Smoothing));
        Assert.Equal("A", current.Name);
    }
}
