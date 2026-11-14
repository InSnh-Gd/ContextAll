package app.contextall.nativebridge

import app.contextall.core.FrameTypes
import app.contextall.core.ProtocolLimits
import app.contextall.model.CommandResult
import app.contextall.model.DebugFrameLogEntry
import app.contextall.model.DeviceFormFactor
import app.contextall.model.FilePromptSnapshot
import app.contextall.model.LocalCommand
import app.contextall.model.PairingPromptSnapshot
import app.contextall.model.PeerDeviceSnapshot
import app.contextall.model.PeerPresence
import app.contextall.model.SessionSnapshotUi
import app.contextall.model.TransferDirection
import app.contextall.model.TransferSnapshot
import app.contextall.model.UiEventNotice
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

internal data class NativeMappedFrameResult(
    val snapshot: SessionSnapshotUi,
    val notices: List<UiEventNotice>
)

internal object NativeProtocolMapper {

    fun encodeLocalCommand(command: LocalCommand, filePath: String? = null): String =
        MiniJson.encodeObject(
            mapOf(
                "requestId" to command.requestId,
                "deviceId" to command.deviceId,
                "displayName" to command.displayName,
                "path" to filePath,
                "enabled" to command.enabled,
                "transferId" to command.transferId
            )
        )

    fun parseCommandResult(json: String, fallbackRequestId: String = "0"): CommandResult {
        val obj = MiniJson.parseObject(json) ?: return CommandResult(fallbackRequestId, false, "decode-failed")
        return CommandResult(
            requestId = obj.str("requestId") ?: fallbackRequestId,
            ok = obj.bool("ok") ?: false,
            error = obj.str("error")
        )
    }

    fun applyStartEnvelope(current: SessionSnapshotUi, startJson: String): SessionSnapshotUi? {
        val root = MiniJson.parseObject(startJson) ?: return null
        if (root.bool("ok") != true) return null
        val deviceId = root.str("deviceId") ?: current.localDeviceId
        val snapshotObj = root.obj("snapshot") ?: return current.copy(localDeviceId = deviceId)
        return applySnapshotPayload(current.copy(localDeviceId = deviceId), snapshotObj)
    }

    fun applySnapshotEnvelope(current: SessionSnapshotUi, envelopeJson: String): SessionSnapshotUi? {
        val root = MiniJson.parseObject(envelopeJson) ?: return null
        val deviceId = root.str("deviceId") ?: current.localDeviceId
        val snapshotObj = root.obj("snapshot") ?: return null
        return applySnapshotPayload(current.copy(localDeviceId = deviceId), snapshotObj)
    }

    fun applyPublishedFrame(
        current: SessionSnapshotUi,
        frameType: Int,
        payload: Map<String, Any?>,
        nowMs: Long = System.currentTimeMillis()
    ): NativeMappedFrameResult {
        return when (frameType) {
            FrameTypes.SNAPSHOT -> {
                val next = applySnapshotPayload(current, payload)
                NativeMappedFrameResult(
                    snapshot = appendDebugLog(next, "ENGINE", frameType, "Snapshot", "devices=${next.devices.size} transfers=${next.transfers.size}", true, nowMs),
                    notices = emptyList()
                )
            }
            FrameTypes.DEVICE_CHANGED -> applyDeviceChanged(current, payload, nowMs)
            FrameTypes.PAIRING_PROMPT -> applyPairingPrompt(current, payload, nowMs)
            FrameTypes.PAIRING_ENDED -> applyPairingEnded(current, payload, nowMs)
            FrameTypes.FILE_PROMPT -> applyFilePrompt(current, payload, nowMs)
            FrameTypes.TRANSFER_PROGRESS -> applyTransferProgress(current, payload, nowMs)
            FrameTypes.TRANSFER_ENDED -> applyTransferEnded(current, payload, nowMs)
            else -> NativeMappedFrameResult(current, emptyList())
        }
    }

    private fun applySnapshotPayload(current: SessionSnapshotUi, payload: Map<String, Any?>): SessionSnapshotUi {
        val localName = payload.str("localDisplayName") ?: current.localDisplayName
        val existingById = current.devices.associateBy { it.deviceId }

        val devices = payload.objList("devices").mapNotNull { obj ->
            val id = obj.str("deviceId") ?: return@mapNotNull null
            val name = obj.str("displayName") ?: id
            val trusted = obj.bool("trusted") ?: false
            val presence = parsePresence(obj.str("presence"))
            val prev = existingById[id]
            PeerDeviceSnapshot(
                deviceId = id,
                displayName = name,
                spkiBytes = prev?.spkiBytes ?: ByteArray(0),
                formFactor = prev?.formFactor ?: inferFormFactor(name),
                trusted = trusted,
                presence = presence,
                clipboardSync = obj.bool("clipboardSync") ?: false,
                remoteClipboardOptIn = prev?.remoteClipboardOptIn ?: trusted,
                askBeforeSave = obj.bool("askBeforeSave") ?: true,
                endpoint = prev?.endpoint
            )
        }

        val devicesById = devices.associateBy { it.deviceId }
        val pairing = payload.obj("pairing")?.let { p ->
            val peerId = p.str("deviceId") ?: ""
            PairingPromptSnapshot(
                deviceId = peerId,
                displayName = p.str("displayName") ?: devicesById[peerId]?.displayName ?: peerId,
                code = p.str("code") ?: "",
                remainingMilliseconds = p.long("remainingMilliseconds") ?: 0L,
                localAccepted = current.pairing?.takeIf { it.deviceId == peerId && it.code == (p.str("code") ?: "") }?.localAccepted ?: false,
                remoteAccepted = current.pairing?.takeIf { it.deviceId == peerId }?.remoteAccepted ?: false,
                initiatedLocally = current.pairing?.takeIf { it.deviceId == peerId }?.initiatedLocally ?: false
            )
        }

        val prompts = payload.objList("filePrompts").mapNotNull { fp ->
            val transferId = fp.str("transferId") ?: return@mapNotNull null
            val peerId = fp.str("deviceId") ?: ""
            val totalSize = fp.long("totalSize") ?: 0L
            FilePromptSnapshot(
                transferId = transferId,
                deviceId = peerId,
                peerDisplayName = devicesById[peerId]?.displayName ?: peerId,
                fileName = fp.str("fileName") ?: "file",
                totalSize = totalSize,
                segmentCount = estimateSegments(totalSize),
                remainingMilliseconds = fp.long("remainingMilliseconds") ?: 0L
            )
        }

        val prevTransfers = current.transfers.associateBy { it.transferId }
        val activeTransfers = payload.objList("transfers").mapNotNull { tr ->
            val transferId = tr.str("transferId") ?: return@mapNotNull null
            val peerId = tr.str("deviceId") ?: ""
            val segCount = (tr.int("segmentCount") ?: 0).coerceAtLeast(1)
            val completedSegs = (tr.int("completedSegments") ?: 0).coerceIn(0, segCount)
            val prev = prevTransfers[transferId]
            TransferSnapshot(
                transferId = transferId,
                deviceId = peerId,
                peerDisplayName = devicesById[peerId]?.displayName ?: prev?.peerDisplayName ?: peerId,
                direction = parseDirection(tr.str("direction")),
                fileName = tr.str("fileName") ?: prev?.fileName ?: "file",
                savedFileName = tr.str("fileName") ?: prev?.savedFileName ?: "file",
                totalSize = prev?.totalSize ?: (segCount.toLong() * ProtocolLimits.SEGMENT_SIZE),
                completedSegments = completedSegs,
                segmentCount = segCount,
                moveBlocked = tr.bool("moveBlocked") ?: false,
                completed = prev?.completed ?: false
            )
        }
        val activeIds = activeTransfers.map { it.transferId }.toSet()
        val preservedCompleted = current.transfers.filter { it.completed && it.transferId !in activeIds }

        val nextSelected = current.selectedDeviceId?.takeIf { sel -> devices.any { it.deviceId == sel } }
        return current.copy(
            localDisplayName = localName,
            selectedDeviceId = nextSelected,
            devices = devices,
            pairing = pairing,
            filePrompts = prompts,
            transfers = activeTransfers + preservedCompleted
        )
    }

    private fun applyDeviceChanged(current: SessionSnapshotUi, payload: Map<String, Any?>, nowMs: Long): NativeMappedFrameResult {
        val id = payload.str("deviceId") ?: return NativeMappedFrameResult(current, emptyList())
        val name = payload.str("displayName") ?: id
        val trusted = payload.bool("trusted") ?: false
        val presence = parsePresence(payload.str("presence"))
        val existing = current.devices.firstOrNull { it.deviceId == id }
        val updatedPeer = PeerDeviceSnapshot(
            deviceId = id,
            displayName = name,
            spkiBytes = existing?.spkiBytes ?: ByteArray(0),
            formFactor = existing?.formFactor ?: inferFormFactor(name),
            trusted = trusted,
            presence = presence,
            clipboardSync = payload.bool("clipboardSync") ?: existing?.clipboardSync ?: false,
            remoteClipboardOptIn = existing?.remoteClipboardOptIn ?: trusted,
            askBeforeSave = payload.bool("askBeforeSave") ?: existing?.askBeforeSave ?: true,
            endpoint = existing?.endpoint
        )

        val updatedDevices = if (existing == null) {
            current.devices + updatedPeer
        } else {
            current.devices.map { if (it.deviceId == id) updatedPeer else it }
        }

        val lastDiscovered = if (existing == null) id else current.lastDiscoveredDeviceId
        val notices = mutableListOf<UiEventNotice>()
        if (existing != null && !existing.trusted && trusted) {
            notices += UiEventNotice.Paired(name)
        } else if (existing != null && existing.trusted && !trusted) {
            notices += UiEventNotice.Unpaired(name)
        }

        val logged = appendDebugLog(
            current.copy(devices = updatedDevices, lastDiscoveredDeviceId = lastDiscovered),
            "PEER",
            FrameTypes.DEVICE_CHANGED,
            "DeviceChanged",
            "$name (${presence.wireValue}, trusted=$trusted)",
            true,
            nowMs
        )
        return NativeMappedFrameResult(logged, notices)
    }

    private fun applyPairingPrompt(current: SessionSnapshotUi, payload: Map<String, Any?>, nowMs: Long): NativeMappedFrameResult {
        val id = payload.str("deviceId") ?: return NativeMappedFrameResult(current, emptyList())
        val name = payload.str("displayName") ?: current.devices.firstOrNull { it.deviceId == id }?.displayName ?: id
        val code = payload.str("code") ?: ""
        val remaining = payload.long("remainingMilliseconds") ?: 0L
        val prompt = PairingPromptSnapshot(
            deviceId = id,
            displayName = name,
            code = code,
            remainingMilliseconds = remaining,
            localAccepted = current.pairing?.takeIf { it.deviceId == id && it.code == code }?.localAccepted ?: false,
            remoteAccepted = current.pairing?.takeIf { it.deviceId == id }?.remoteAccepted ?: false,
            initiatedLocally = current.pairing?.takeIf { it.deviceId == id }?.initiatedLocally ?: false
        )
        val logged = appendDebugLog(
            current.copy(pairing = prompt),
            "PAIR",
            FrameTypes.PAIRING_PROMPT,
            "PairingPrompt",
            "$name SAS=$code",
            true,
            nowMs
        )
        return NativeMappedFrameResult(logged, emptyList())
    }

    private fun applyPairingEnded(current: SessionSnapshotUi, payload: Map<String, Any?>, nowMs: Long): NativeMappedFrameResult {
        val id = payload.str("deviceId") ?: ""
        val outcome = payload.str("outcome") ?: "cancelled"
        val peerName = current.devices.firstOrNull { it.deviceId == id }?.displayName
            ?: current.pairing?.takeIf { it.deviceId == id }?.displayName
            ?: id
        val notices = when (outcome) {
            "paired", "trusted" -> listOf(UiEventNotice.Paired(peerName))
            "timeout", "expired" -> listOf(UiEventNotice.PairingTimeout(peerName))
            "rejected" -> listOf(UiEventNotice.PairingRejected)
            else -> listOf(UiEventNotice.PairingCancelled)
        }
        val clearedPairing = if (current.pairing?.deviceId == id || id.isEmpty()) null else current.pairing
        val logged = appendDebugLog(
            current.copy(pairing = clearedPairing),
            "PAIR",
            FrameTypes.PAIRING_ENDED,
            "PairingEnded",
            "$peerName -> $outcome",
            outcome == "paired" || outcome == "trusted",
            nowMs
        )
        return NativeMappedFrameResult(logged, notices)
    }

    private fun applyFilePrompt(current: SessionSnapshotUi, payload: Map<String, Any?>, nowMs: Long): NativeMappedFrameResult {
        val transferId = payload.str("transferId") ?: return NativeMappedFrameResult(current, emptyList())
        val peerId = payload.str("deviceId") ?: ""
        val fileName = payload.str("fileName") ?: "file"
        val totalSize = payload.long("totalSize") ?: 0L
        val remaining = payload.long("remainingMilliseconds") ?: 0L
        val peerName = current.devices.firstOrNull { it.deviceId == peerId }?.displayName ?: peerId
        val prompt = FilePromptSnapshot(
            transferId = transferId,
            deviceId = peerId,
            peerDisplayName = peerName,
            fileName = fileName,
            totalSize = totalSize,
            segmentCount = estimateSegments(totalSize),
            remainingMilliseconds = remaining
        )
        val filtered = current.filePrompts.filterNot { it.transferId == transferId } + prompt
        val logged = appendDebugLog(
            current.copy(filePrompts = filtered),
            "XFER",
            FrameTypes.FILE_PROMPT,
            "FilePrompt",
            "$fileName ($totalSize B) from $peerName",
            true,
            nowMs
        )
        return NativeMappedFrameResult(logged, emptyList())
    }

    private fun applyTransferProgress(current: SessionSnapshotUi, payload: Map<String, Any?>, nowMs: Long): NativeMappedFrameResult {
        val transferId = payload.str("transferId") ?: return NativeMappedFrameResult(current, emptyList())
        val peerId = payload.str("deviceId") ?: ""
        val direction = parseDirection(payload.str("direction"))
        val fileName = payload.str("fileName") ?: "file"
        val segCount = (payload.int("segmentCount") ?: 0).coerceAtLeast(1)
        val completedSegs = (payload.int("completedSegments") ?: 0).coerceIn(0, segCount)
        val moveBlocked = payload.bool("moveBlocked") ?: false

        val existing = current.transfers.firstOrNull { it.transferId == transferId }
        val peerName = current.devices.firstOrNull { it.deviceId == peerId }?.displayName ?: existing?.peerDisplayName ?: peerId
        val prompt = current.filePrompts.firstOrNull { it.transferId == transferId }
        val totalSize = existing?.totalSize ?: prompt?.totalSize ?: (segCount.toLong() * ProtocolLimits.SEGMENT_SIZE)

        val item = TransferSnapshot(
            transferId = transferId,
            deviceId = peerId,
            peerDisplayName = peerName,
            direction = direction,
            fileName = fileName,
            savedFileName = existing?.savedFileName ?: fileName,
            totalSize = totalSize,
            completedSegments = completedSegs,
            segmentCount = segCount,
            moveBlocked = moveBlocked,
            completed = existing?.completed ?: false
        )

        val updatedTransfers = if (existing == null) {
            listOf(item) + current.transfers
        } else {
            current.transfers.map { if (it.transferId == transferId) item else it }
        }
        val remainingPrompts = current.filePrompts.filterNot { it.transferId == transferId }

        val deltaSegs = (completedSegs - (existing?.completedSegments ?: 0)).coerceAtLeast(0)
        val nextTelemetry = if (deltaSegs > 0) {
            current.telemetry.copy(verifiedSegmentsCount = current.telemetry.verifiedSegmentsCount + deltaSegs)
        } else {
            current.telemetry
        }

        val logged = appendDebugLog(
            current.copy(
                filePrompts = remainingPrompts,
                transfers = updatedTransfers,
                telemetry = nextTelemetry
            ),
            "XFER",
            FrameTypes.TRANSFER_PROGRESS,
            "TransferProgress",
            "$fileName $completedSegs/$segCount",
            true,
            nowMs
        )
        return NativeMappedFrameResult(logged, emptyList())
    }

    private fun applyTransferEnded(current: SessionSnapshotUi, payload: Map<String, Any?>, nowMs: Long): NativeMappedFrameResult {
        val transferId = payload.str("transferId") ?: return NativeMappedFrameResult(current, emptyList())
        val outcome = payload.str("outcome") ?: "done"
        val existing = current.transfers.firstOrNull { it.transferId == transferId }
        val prompt = current.filePrompts.firstOrNull { it.transferId == transferId }
        val fileName = existing?.fileName ?: prompt?.fileName ?: transferId
        val remainingPrompts = current.filePrompts.filterNot { it.transferId == transferId }

        val isDone = outcome == "done" || outcome == "completed"
        val updatedTransfers = if (isDone && existing != null) {
            current.transfers.map {
                if (it.transferId == transferId) {
                    it.copy(completedSegments = it.segmentCount, moveBlocked = false, completed = true)
                } else {
                    it
                }
            }
        } else {
            current.transfers.filterNot { it.transferId == transferId }
        }

        val notices = when {
            isDone -> listOf(UiEventNotice.TransferCompleted(fileName))
            outcome == "timeout" || outcome == "expired" -> listOf(UiEventNotice.InboundTimeout(prompt?.peerDisplayName ?: "",))
            else -> emptyList()
        }

        val nextCommitted = if (isDone && existing?.direction == TransferDirection.RECEIVE) {
            current.debugState.committedDownloadCount + 1
        } else {
            current.debugState.committedDownloadCount
        }

        val logged = appendDebugLog(
            current.copy(
                filePrompts = remainingPrompts,
                transfers = updatedTransfers,
                debugState = current.debugState.copy(committedDownloadCount = nextCommitted)
            ),
            "XFER",
            FrameTypes.TRANSFER_ENDED,
            "TransferEnded",
            "$fileName -> $outcome",
            isDone,
            nowMs
        )
        return NativeMappedFrameResult(logged, notices)
    }

    internal fun appendDebugLog(
        snapshot: SessionSnapshotUi,
        category: String,
        frameCode: Int,
        frameName: String,
        summary: String,
        ok: Boolean,
        nowMs: Long = System.currentTimeMillis()
    ): SessionSnapshotUi {
        val nextSeq = (snapshot.debugState.frameLogs.firstOrNull()?.seq ?: 0) + 1
        val formatter = SimpleDateFormat("HH:mm:ss", Locale.US)
        val entry = DebugFrameLogEntry(
            seq = nextSeq,
            timestampText = formatter.format(Date(nowMs)),
            category = category,
            frameCode = frameCode,
            frameName = frameName,
            summary = summary,
            ok = ok
        )
        val updatedLogs = (listOf(entry) + snapshot.debugState.frameLogs).take(120)
        return snapshot.copy(debugState = snapshot.debugState.copy(frameLogs = updatedLogs))
    }

    private fun parsePresence(wire: String?): PeerPresence =
        when (wire?.lowercase(Locale.ROOT)) {
            "connected" -> PeerPresence.CONNECTED
            "reachable" -> PeerPresence.REACHABLE
            else -> PeerPresence.OFFLINE
        }

    private fun parseDirection(wire: String?): TransferDirection =
        if (wire.equals("send", ignoreCase = true)) TransferDirection.SEND else TransferDirection.RECEIVE

    private fun estimateSegments(totalBytes: Long): Int =
        if (totalBytes <= 0L) 1 else ((totalBytes + ProtocolLimits.SEGMENT_SIZE - 1L) / ProtocolLimits.SEGMENT_SIZE).toInt().coerceAtLeast(1)

    private fun inferFormFactor(name: String): DeviceFormFactor {
        val lower = name.lowercase(Locale.ROOT)
        return when {
            lower.contains("phone") || lower.contains("pixel") || lower.contains("android") || lower.contains("手机") -> DeviceFormFactor.PHONE
            lower.contains("macbook") || lower.contains("thinkpad") || lower.contains("laptop") || lower.contains("笔记本") -> DeviceFormFactor.LAPTOP
            else -> DeviceFormFactor.DESKTOP
        }
    }
}

