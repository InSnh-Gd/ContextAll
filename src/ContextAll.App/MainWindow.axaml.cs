using Avalonia.Controls;

namespace ContextAll.App;

public sealed partial class MainWindow : Window
{
    bool hasSelection;

    public MainWindow()
    {
        InitializeComponent();
        Devices.SelectionChanged += (_, _) =>
        {
            hasSelection = Devices.SelectedItem is not null;
            ApplyWidth(Bounds.Width);
        };
        ApplyWidth(Width);
    }

    public void ApplyWidth(double width)
    {
        var columns = ShellLayout.Columns(width, hasSelection);
        Devices.IsVisible = columns.List;
        Detail.IsVisible = columns.Detail;
    }
}
