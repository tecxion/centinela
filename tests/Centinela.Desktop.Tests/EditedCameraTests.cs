using System.Reflection;
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

    // Every settable Camera property must be listed on one side; a new property fails here until it is decided.
    static readonly string[] FromDialog =
    [
        nameof(Camera.Id), nameof(Camera.Name), nameof(Camera.Brand), nameof(Camera.Host), nameof(Camera.Port),
        nameof(Camera.User), nameof(Camera.Password), nameof(Camera.MainUrlOverride), nameof(Camera.SubUrlOverride),
        nameof(Camera.UseUdp),
    ];

    static readonly string[] FromStored =
    [
        nameof(Camera.Order), nameof(Camera.LowQualityWhenBig), nameof(Camera.Smoothing), nameof(Camera.MotionEnabled),
        nameof(Camera.MotionSensitivity), nameof(Camera.MotionCooldownSeconds), nameof(Camera.ConnectionAlerts),
        nameof(Camera.MotionAlerts), nameof(Camera.OriginalEncoders),
    ];

    static IEnumerable<PropertyInfo> Settable =>
        typeof(Camera).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanWrite);

    /// <summary>A camera whose every settable property has a value that depends on <paramref name="seed"/> (1 or 2).</summary>
    static Camera Filled(int seed)
    {
        var camera = new Camera();
        foreach (var p in Settable)
        {
            var type = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
            object value = type == typeof(string) ? $"{p.Name}-{seed}"
                : type == typeof(int) ? 100 + seed
                : type == typeof(bool) ? seed == 1
                : type == typeof(Guid) ? new Guid(seed, 0, 0, new byte[8])
                : type.IsEnum ? Enum.GetValues(type).GetValue(seed % Enum.GetValues(type).Length)!
                : throw new InvalidOperationException($"Tipo sin valor de prueba: {type}");
            p.SetValue(camera, value);
        }
        return camera;
    }

    [Fact]
    public void Merge_takes_every_property_from_the_dialog_or_from_the_stored_camera()
    {
        Assert.Equal(FromDialog.Concat(FromStored).Order(), Settable.Select(p => p.Name).Order());
        Camera edited = Filled(1), current = Filled(2);
        var merged = EditedCamera.Merge(edited, current);
        foreach (var p in Settable)
            Assert.Equal(p.GetValue(FromDialog.Contains(p.Name) ? edited : current), p.GetValue(merged));
    }
}
