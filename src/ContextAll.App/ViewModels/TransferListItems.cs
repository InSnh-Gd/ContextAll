using System.Windows.Input;
using ContextAll.App.Controls;

namespace ContextAll.App.ViewModels;

public sealed class PeerChipItem
{
    public required string DeviceId { get; init; }

    public required string DisplayName { get; init; }

    public required bool IsSelected { get; init; }

    public required ICommand SelectCommand { get; init; }
}

public sealed class FilterChipItem
{
    public required TransferListFilter Filter { get; init; }

    public required string Label { get; init; }

    public required bool IsSelected { get; init; }

    public required ICommand SelectCommand { get; init; }
}

public sealed class PromptCardItem
{
    public required string TransferId { get; init; }

    public required string FileName { get; init; }

    public required string Subtitle { get; init; }

    public required ICommand AcceptCommand { get; init; }

    public required ICommand RejectCommand { get; init; }
}

public sealed class ActiveTransferItem
{
    public required string TransferId { get; init; }

    public required string Title { get; init; }

    public required string Stats { get; init; }

    public required double ProgressMaximum { get; init; }

    public required double ProgressValue { get; init; }

    public required bool MoveBlocked { get; init; }

    public required GroupPosition Position { get; init; }

    public required ICommand CancelCommand { get; init; }
}

public sealed class QueueRowItem
{
    public required string Label { get; init; }

    public required GroupPosition Position { get; init; }
}

public sealed class HistoryRowItem
{
    public required string Title { get; init; }

    public required string Meta { get; init; }

    public required GroupPosition Position { get; init; }

    public required bool CanResend { get; init; }

    public required bool CanOpenFolder { get; init; }

    public required ICommand ResendCommand { get; init; }

    public required ICommand OpenFolderCommand { get; init; }
}
