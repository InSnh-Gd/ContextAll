using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;

namespace ContextAll.App.Controls;

/// <summary>Item descriptor inside a connected <see cref="ExpressiveButtonGroup"/>.</summary>
public sealed record ExpressiveGroupButtonItem(string Id, string Label, bool IsSelected = false);

/// <summary>
/// MD3E connected button group with pill outer corners, segmented inner corners, and press squish feedback.
/// </summary>
[TemplatePart("PART_ItemsPanel", typeof(Panel))]
public class ExpressiveButtonGroup : TemplatedControl
{
    public const double OuterRadius = 20.0;
    public const double InnerRadius = 6.0;

    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<ExpressiveButtonGroup, double>(nameof(Spacing), 2.0);

    private readonly List<(ExpressiveGroupButtonItem Item, Button Button, bool IsPressed)> _entries = [];
    private IReadOnlyList<ExpressiveGroupButtonItem> _items = [];
    private Panel? _itemsPanel;

    public event Action<string>? ItemClicked;

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>Currently materialized button controls in group order.</summary>
    public IReadOnlyList<Button> Buttons => _entries.Select(e => e.Button).ToArray();

    /// <summary>Replaces the items rendered in this connected group.</summary>
    public void SetItems(IReadOnlyList<ExpressiveGroupButtonItem> items)
    {
        IReadOnlyList<ExpressiveGroupButtonItem> next = items ?? [];
        if (ItemsEqual(_items, next))
        {
            return;
        }

        var canReuse = _items.Select(item => item.Id).SequenceEqual(next.Select(item => item.Id));
        _items = [..next];
        if (!canReuse)
        {
            RebuildButtons();
            return;
        }

        for (var index = 0; index < _entries.Count; index++)
        {
            var (_, button, isPressed) = _entries[index];
            var item = _items[index];
            UpdateButton(button, item, index, isPressed);
            _entries[index] = (item, button, isPressed);
        }
    }

    private static bool ItemsEqual(
        IReadOnlyList<ExpressiveGroupButtonItem> left,
        IReadOnlyList<ExpressiveGroupButtonItem> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (left[i] != right[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Computes positional connected radii with active/pressed pill squish.</summary>
    public static CornerRadius ComputeItemRadius(int index, int count, bool activeOrPressed)
    {
        if (count <= 1 || activeOrPressed)
        {
            return new CornerRadius(OuterRadius);
        }

        if (index == 0)
        {
            return new CornerRadius(OuterRadius, InnerRadius, InnerRadius, OuterRadius);
        }

        if (index == count - 1)
        {
            return new CornerRadius(InnerRadius, OuterRadius, OuterRadius, InnerRadius);
        }

        return new CornerRadius(InnerRadius);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _itemsPanel = e.NameScope.Find<Panel>("PART_ItemsPanel");
        PopulatePanel();
    }

    private void RebuildButtons()
    {
        _entries.Clear();
        var count = _items.Count;
        for (var i = 0; i < count; i++)
        {
            var item = _items[i];
            var index = i;
            var button = new Button
            {
                Padding = new Thickness(16, 10),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
            };

            button.Classes.Add("FilledTonal");
            UpdateButton(button, item, index, isPressed: false);
            button.PointerPressed += (_, _) => SetEntryPressed(index, true);
            button.PointerReleased += (_, _) => SetEntryPressed(index, false);
            button.PointerCaptureLost += (_, _) => SetEntryPressed(index, false);
            button.Click += (_, _) => ItemClicked?.Invoke(item.Id);

            _entries.Add((item, button, false));
        }

        PopulatePanel();
    }

    private void UpdateButton(Button button, ExpressiveGroupButtonItem item, int index, bool isPressed)
    {
        button.Content = item.Label;
        button.Classes.Set("GroupSelected", item.IsSelected);
        button.CornerRadius = ComputeItemRadius(index, _items.Count, item.IsSelected || isPressed);
    }

    private void SetEntryPressed(int index, bool pressed)
    {
        if (index < 0 || index >= _entries.Count)
        {
            return;
        }

        var entry = _entries[index];
        _entries[index] = (entry.Item, entry.Button, pressed);
        entry.Button.CornerRadius = ComputeItemRadius(index, _entries.Count, entry.Item.IsSelected || pressed);
    }

    private void PopulatePanel()
    {
        if (_itemsPanel is null)
        {
            return;
        }

        _itemsPanel.Children.Clear();
        foreach (var (_, button, _) in _entries)
        {
            _itemsPanel.Children.Add(button);
        }
    }
}
