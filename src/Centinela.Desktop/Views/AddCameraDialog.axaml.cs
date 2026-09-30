using Avalonia.Controls;
using Avalonia.Interactivity;
using Centinela.Core;
using Centinela.Desktop.Services.Cameras;
using Centinela.Desktop.ViewModels;

namespace Centinela.Desktop.Views;

/// <summary>«Añadir cámara» / «Editar cámara» with «Probar». <c>ShowDialog&lt;bool&gt;</c> is true when saved.</summary>
public partial class AddCameraDialog : Window
{
    readonly CameraForm _form = null!;
    readonly CameraTest _test = new();

    public AddCameraDialog() => InitializeComponent();

    public AddCameraDialog(Camera? initial, bool isNew) : this()
    {
        _form = new CameraForm(initial ?? new Camera { Brand = Brand.Tapo });
        Title = isNew ? "Añadir cámara" : "Editar cámara";

        var fields = CameraForm.FieldsOf(_form.Result);
        NameBox.Text = fields.Name;
        HostBox.Text = fields.Host;
        PortBox.Text = fields.Port;
        UserBox.Text = fields.User;
        PasswordInput.Text = fields.Password;
        MainUrlBox.Text = fields.MainUrl;
        SubUrlBox.Text = fields.SubUrl;
        UdpBox.IsChecked = fields.UseUdp;
        BrandBox.SelectedIndex = (int)fields.Brand;

        _test.LinesChanged += () => TestResult.Text = $"{_test.MainLine}\n{_test.SubLine}";
        _test.StatusChanged += () => TestStatus.Text = _test.Status;
        Closed += (_, _) =>
        {
            Preview.Session = null;
            _test.Stop();
        };
    }

    /// <summary>The edited copy (a new camera, or the edited one with the same Id); valid once saved.</summary>
    public Camera Result => _form.Result;

    Brand SelectedBrand => (Brand)Math.Max(0, BrandBox.SelectedIndex);

    void Brand_Changed(object? sender, SelectionChangedEventArgs e)
    {
        var brand = SelectedBrand;
        if (brand == Brand.Imou && string.IsNullOrEmpty(UserBox.Text)) UserBox.Text = "admin";
        if (brand == Brand.Custom) AdvancedBox.IsExpanded = true;
        HintText.Text = CameraForm.HintFor(brand);
    }

    bool TryReadForm(out string error)
    {
        var fields = new CameraFields(SelectedBrand, NameBox.Text ?? "", HostBox.Text ?? "", PortBox.Text ?? "",
            UserBox.Text ?? "", PasswordInput.Text ?? "", MainUrlBox.Text ?? "", SubUrlBox.Text ?? "", UdpBox.IsChecked == true);
        if (!_form.TryApply(fields, out error, out var mainUrl, out var subUrl)) return false;
        // The URLs moved to a new IP/port: show them as they will be saved.
        if (mainUrl != fields.MainUrl.Trim()) MainUrlBox.Text = mainUrl;
        if (subUrl != fields.SubUrl.Trim()) SubUrlBox.Text = subUrl;
        return true;
    }

    void Test_Click(object? sender, RoutedEventArgs e)
    {
        if (!TryReadForm(out var error))
        {
            TestStatus.Text = error;
            return;
        }
        _test.Start(_form.Result);
        Preview.Session = _test.Preview;
    }

    void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (!TryReadForm(out var error))
        {
            HintText.Text = error;
            return;
        }
        Close(true);
    }

    void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
