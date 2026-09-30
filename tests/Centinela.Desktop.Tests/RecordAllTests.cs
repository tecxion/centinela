using Centinela.Core;
using Centinela.Desktop.Services.Recording;

namespace Centinela.Desktop.Tests;

public class RecordAllTests
{
    [Fact]
    public void Toolbar_records_the_cameras_on_screen_and_the_tray_records_all()
    {
        var cams = Enumerable.Range(0, 4).Select(i => new Camera { Name = $"C{i}" }).ToList();
        var onScreen = new[] { cams[2].Id, cams[0].Id };
        Assert.Equal([cams[0].Id, cams[2].Id], RecordAll.Targets(cams, onScreen, allCameras: false));
        Assert.Equal(cams.Select(c => c.Id), RecordAll.Targets(cams, onScreen, allCameras: true));
    }
}
