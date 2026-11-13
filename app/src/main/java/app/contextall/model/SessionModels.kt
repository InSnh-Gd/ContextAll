package app.contextall.model

enum class TopTab {
    DEVICES,
    TRANSFERS,
    THIS_DEVICE
}

enum class PeerPresence(val wireValue: String) {
    CONNECTED("connected"),
    REACHABLE("reachable"),
    OFFLINE("offline")
}

enum class TransferDirection(val wireValue: String) {
    SEND("send"),
    RECEIVE("receive")
}

enum class DeviceFormFactor {
    DESKTOP,
    LAPTOP,
    PHONE
}

/**
 * Mirrors ContextAll.Protocol.DeviceSnapshotPayload with UI form-factor metadata.
 */
data class PeerDeviceSnapshot(
    val deviceId: String,
    val displayName: String,
    val spkiBytes: ByteArray,
    val formFactor: DeviceFormFactor,
    val trusted: Boolean,
    val presence: PeerPresence,
    val clipboardSync: Boolean = false,
    val remoteClipboardOptIn: Boolean = true,
    val askBeforeSave: Boolean = true,
    val endpoint: String? = null
) {
    val isMutualClipboardActive: Boolean
        get() = trusted && presence == PeerPresence.CONNECTED && clipboardSync && remoteClipboardOptIn

    override fun equals(other: Any?): Boolean {
        if (this === other) return true
        if (other !is PeerDeviceSnapshot) return false
        return deviceId == other.deviceId &&
            displayName == other.displayName &&
            formFactor == other.formFactor &&
            trusted == other.trusted &&
            presence == other.presence &&
            clipboardSync == other.clipboardSync &&
            remoteClipboardOptIn == other.remoteClipboardOptIn &&
            askBeforeSave == other.askBeforeSave &&
            endpoint == other.endpoint
    }

    override fun hashCode(): Int {
        var result = deviceId.hashCode()
        result = 31 * result + displayName.hashCode()
        result = 31 * result + formFactor.hashCode()
        result = 31 * result + trusted.hashCode()
        result = 31 * result + presence.hashCode()
        result = 31 * result + clipboardSync.hashCode()
        result = 31 * result + remoteClipboardOptIn.hashCode()
        result = 31 * result + askBeforeSave.hashCode()
        result = 31 * result + (endpoint?.hashCode() ?: 0)
        return result
    }
}

/**
 * Mirrors ContextAll.Protocol.PairingSnapshotPayload with bilateral acceptance flags.
 */
data class PairingPromptSnapshot(
    val deviceId: String,
    val displayName: String,
    val code: String,
    val remainingMilliseconds: Long,
    val localAccepted: Boolean = false,
    val remoteAccepted: Boolean = false
) {
    val remainingSeconds: Int
        get() = ((remainingMilliseconds + 999L) / 1000L).toInt().coerceIn(0, 60)
}

/**
 * Mirrors ContextAll.Protocol.FilePromptSnapshotPayload.
 */
data class FilePromptSnapshot(
    val transferId: String,
    val deviceId: String,
    val peerDisplayName: String,
    val fileName: String,
    val totalSize: Long,
    val segmentCount: Int,
    val remainingMilliseconds: Long,
    val simulateMoveBlockOnFinish: Boolean = false
) {
    val remainingSeconds: Int
        get() = ((remainingMilliseconds + 999L) / 1000L).toInt().coerceIn(0, 60)
}

/**
 * Mirrors ContextAll.Protocol.TransferSnapshotPayload + completed history state.
 */
data class TransferSnapshot(
    val transferId: String,
    val deviceId: String,
    val peerDisplayName: String,
    val direction: TransferDirection,
    val fileName: String,
    val savedFileName: String = fileName,
    val totalSize: Long,
    val completedSegments: Int,
    val segmentCount: Int,
    val waitingReconnect: Boolean = false,
    val moveBlocked: Boolean = false,
    val completed: Boolean = false
) {
    val progressFraction: Float
        get() = when {
            completed -> 1f
            segmentCount <= 0 -> 1f
            else -> (completedSegments.toFloat() / segmentCount.toFloat()).coerceIn(0f, 1f)
        }
}

data class ClipboardSyncState(
    val lastSyncedText: String = "",
    val lastOriginName: String = "",
    val sequence: Long = 0L,
    val byteCount: Int = 0
)

/**
 * Mirrors ContextAll.Protocol.LocalCommandPayload.
 */
data class LocalCommand(
    val frameType: Int,
    val requestId: String,
    val deviceId: String? = null,
    val displayName: String? = null,
    val fileName: String? = null,
    val fileSize: Long? = null,
    val enabled: Boolean? = null,
    val transferId: String? = null
)

data class CommandResult(
    val requestId: String,
    val ok: Boolean,
    val error: String? = null
)

/**
 * Immutable UI snapshot matching ContextAll.Protocol.SnapshotPayload.
 */
data class SessionSnapshotUi(
    val localDisplayName: String,
    val localDeviceId: String,
    val wifiLanReady: Boolean = true,
    val linkServiceRunning: Boolean = true,
    val runtimePermissionsGranted: Boolean = false,
    val darkTheme: Boolean = false,
    val dynamicColor: Boolean = true,
    val currentTab: TopTab = TopTab.DEVICES,
    val selectedDeviceId: String? = null,
    val devices: List<PeerDeviceSnapshot> = emptyList(),
    val pairing: PairingPromptSnapshot? = null,
    val filePrompts: List<FilePromptSnapshot> = emptyList(),
    val transfers: List<TransferSnapshot> = emptyList(),
    val clipboardState: ClipboardSyncState = ClipboardSyncState()
)

sealed interface UiEventNotice {
    data class Paired(val peerName: String) : UiEventNotice
    data class PairingTimeout(val peerName: String) : UiEventNotice
    data object PairingRejected : UiEventNotice
    data class Unpaired(val peerName: String) : UiEventNotice
    data class SendBusy(val peerName: String) : UiEventNotice
    data object SendTooLarge : UiEventNotice
    data class SendStarted(val peerName: String, val fileName: String, val segments: Int) : UiEventNotice
    data object SendHeldOffline : UiEventNotice
    data class InboundAutoAccepted(val peerName: String, val fileName: String) : UiEventNotice
    data class InboundTimeout(val peerName: String) : UiEventNotice
    data class TransferCompleted(val fileName: String) : UiEventNotice
    data class ClipboardPushed(val peerCount: Int, val byteCount: Int) : UiEventNotice
    data object ClipboardTooLarge : UiEventNotice
    data object ClipboardEmpty : UiEventNotice
    data object ClipboardNoMutualPeers : UiEventNotice
    data class ClipboardReceived(val peerName: String, val text: String) : UiEventNotice
    data object CopiedId : UiEventNotice
    data object IdentityResetDone : UiEventNotice
    data class DisplayNameSaved(val newName: String) : UiEventNotice
    data object InvalidDisplayName : UiEventNotice
    data class ManualEndpointReconnected(val peerName: String, val endpoint: String) : UiEventNotice
    data object ManualConnectUnavailable : UiEventNotice
    data object ManualInvalidEndpoint : UiEventNotice
}
