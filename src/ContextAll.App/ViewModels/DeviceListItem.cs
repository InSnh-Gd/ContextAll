using System.Windows.Input;
using ContextAll.App.Controls;
using ContextAll.Protocol;

namespace ContextAll.App.ViewModels;

public sealed class DeviceListItem
{
    public required DeviceSnapshotPayload Device { get; init; }

    public required GroupPosition Position { get; init; }

    public required bool IsSelected { get; init; }

    public required bool IsTrusted { get; init; }

    public required string PresenceLabel { get; init; }

    public required BadgeTone PresenceTone { get; init; }

    public required string ClipboardPolicy { get; init; }

    public required bool ClipboardPolicyOn { get; init; }

    public required string SavePolicy { get; init; }

    public required string Subtitle { get; init; }

    public required ICommand SelectCommand { get; init; }

    public ICommand? StartPairingCommand { get; init; }

    public bool HasEndpoint => !string.IsNullOrWhiteSpace(Device.Endpoint);
}

public sealed class DeviceHistoryItem
{
    public required string Title { get; init; }

    public required string Subtitle { get; init; }

    public required GroupPosition Position { get; init; }
}
