using System.Windows.Input;
using ContextAll.App.Controls;

namespace ContextAll.App.ViewModels;

public sealed class EndpointRowItem
{
    public required string Endpoint { get; init; }

    public required GroupPosition Position { get; init; }

    public required ICommand CopyCommand { get; init; }
}

public sealed class PeerOverviewItem
{
    public required string DeviceId { get; init; }

    public required string DisplayName { get; init; }

    public required string PresenceLabel { get; init; }

    public required BadgeTone PresenceTone { get; init; }

    public required string EndpointLabel { get; init; }

    public required bool IsTrusted { get; init; }

    public required GroupPosition Position { get; init; }

    public required ICommand StartPairingCommand { get; init; }
}

public sealed class LanPresetItem
{
    public required string Preset { get; init; }

    public required ICommand ApplyCommand { get; init; }
}

public sealed record GuideStepItem(string Tag, string Title, string Description);

public sealed class GuideDotItem
{
    public bool IsActive { get; init; }

    public double DotWidth => IsActive ? 24 : 8;
}
