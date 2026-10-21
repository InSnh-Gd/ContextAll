namespace ContextAll.Protocol;

public static class FrameTypes
{
    public const byte Hello = 1;
    public const byte PairAccept = 2;
    public const byte PairReject = 3;
    public const byte SessionDuplicateClose = 4;
    public const byte Unpair = 5;
    public const byte DisplayName = 6;
    public const byte PathAnnounce = 7;
    public const byte FileOffer = 8;
    public const byte FileAccept = 9;
    public const byte FileReject = 10;
    public const byte FileResume = 11;
    public const byte FileAck = 12;
    public const byte FileCancel = 13;
    public const byte CrdtStateVector = 14;
    public const byte CrdtDeltaBatch = 15;
    public const byte FileBitmap = 16;
    public const byte FileSegment = 17;
    public const byte ClipboardChunk = 18;

    public const byte StartPairing = 64;
    public const byte AcceptPairing = 65;
    public const byte RejectPairing = 66;
    public const byte UnpairLocal = 67;
    public const byte SetDisplayName = 68;
    public const byte SetClipboardSync = 69;
    public const byte SetAskBeforeSave = 70;
    public const byte SetDownloadDirectory = 71;
    public const byte SendFile = 72;
    public const byte AcceptFile = 73;
    public const byte RejectFile = 74;
    public const byte CancelTransfer = 75;

    public const byte Snapshot = 80;
    public const byte DeviceChanged = 81;
    public const byte PairingPrompt = 82;
    public const byte PairingEnded = 83;
    public const byte FilePrompt = 84;
    public const byte TransferProgress = 85;
    public const byte TransferEnded = 86;
    public const byte CommandResult = 96;

    public const byte HighestPriority = 0;
    public const byte LowestPriority = 3;
    public const ushort BitmapContinued = 1;

    public const uint SessionStream = 0;
    public const uint SyncStream = 1;
    public const uint FirstBulkStream = 2;
}
