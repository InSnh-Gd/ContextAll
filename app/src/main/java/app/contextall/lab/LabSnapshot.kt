package app.contextall.lab

import app.contextall.OutboundFile
import app.contextall.ShellOperator
import app.contextall.core.DeviceIds
import app.contextall.core.DisplayNames
import app.contextall.core.DownloadNames
import app.contextall.core.EndpointValidator
import app.contextall.core.FrameTypes
import app.contextall.core.ProtocolLimits
import app.contextall.core.computeSegmentCount
import app.contextall.model.ClipboardSyncState
import app.contextall.model.CommandResult
import app.contextall.model.DebugFrameLogEntry
import app.contextall.model.DeviceFormFactor
import app.contextall.model.EngineDebugState
import app.contextall.model.FilePromptSnapshot
import app.contextall.model.LocalCommand
import app.contextall.model.PairingPromptSnapshot
import app.contextall.model.PeerDeviceSnapshot
import app.contextall.model.PeerPresence
import app.contextall.model.QueuedUploadSnapshot
import app.contextall.model.SeedDataProfile
import app.contextall.model.SessionSnapshotUi
import app.contextall.model.SessionTelemetrySnapshot
import app.contextall.model.TelemetryBucketSnapshot
import app.contextall.model.TransferDirection
import app.contextall.model.TransferSnapshot
import app.contextall.model.UiEventNotice
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import java.util.UUID
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update

/**
 * Canned Shell snapshot for DEVELOPER_LAB. Not a session engine.
 */
internal class LabSnapshot(
    initialDisplayName: String,
    initialDebugMode: Boolean = true
) : ShellOperator {
    private var localSpki: ByteArray = DeviceIds.generateP256Spki()
    private val committedDownloadNames = LinkedHashSet<String>()
    private var discoveryCounter = 1
    private var frameSeqCounter = 1
    private val timeFormat = SimpleDateFormat("HH:mm:ss.SSS", Locale.US)
    private var started = false

    private val _snapshot = MutableStateFlow(
        SessionSnapshotUi(
            localDisplayName = initialDisplayName,
            localDeviceId = DeviceIds.text(localSpki),
            debugState = EngineDebugState(
                developerUiEnabled = initialDebugMode,
                seedProfile = SeedDataProfile.CONSUMER
            )
        )
    )
    override val snapshot: StateFlow<SessionSnapshotUi> = _snapshot.asStateFlow()

    private val _notices = MutableSharedFlow<UiEventNotice>(extraBufferCapacity = 16)
    override val notices: SharedFlow<UiEventNotice> = _notices.asSharedFlow()

    override fun start(): Boolean {
        started = true
        _snapshot.value = cannedLabSnapshot(_snapshot.value.localDisplayName, _snapshot.value.debugState.developerUiEnabled)
        recordDebug("DBG", FrameTypes.SNAPSHOT, "SEED_PROFILE", "DEVELOPER_LAB")
        _notices.tryEmit(UiEventNotice.DebugSeedSwitched(isDeveloperProfile = true))
        return true
    }

    override fun stop() {
        started = false
    }

    override fun submitCommand(command: LocalCommand): CommandResult {
        if (!started) {
            return CommandResult(command.requestId, false, "lab-stopped")
        }
        val result = when (command.frameType) {
            FrameTypes.SET_DISPLAY_NAME -> patchDisplayName(command)
            FrameTypes.ACCEPT_PAIRING -> patchAcceptPairing(command)
            FrameTypes.REJECT_PAIRING -> patchRejectPairing(command)
            FrameTypes.UNPAIR_LOCAL -> patchUnpair(command)
            FrameTypes.SET_CLIPBOARD_SYNC -> patchPeerFlag(command, clipboard = true)
            FrameTypes.SET_ASK_BEFORE_SAVE -> patchPeerFlag(command, clipboard = false)
            FrameTypes.SEND_FILE -> patchSendFile(command)
            FrameTypes.ACCEPT_FILE -> patchAcceptFile(command)
            FrameTypes.REJECT_FILE -> patchRejectFile(command)
            FrameTypes.CANCEL_TRANSFER -> patchCancelTransfer(command)
            else -> CommandResult(command.requestId, false, "invalid")
        }
        recordDebug(
            if (result.ok) "CMD" else "ERR",
            command.frameType,
            "CMD_${command.frameType}",
            if (result.ok) "ok" else "err:${result.error}",
            result.ok
        )
        return result
    }

    override fun sendFiles(deviceId: String, files: List<OutboundFile>) {
        if (files.isEmpty() || !started) return
        val named = files.map { file ->
            when (file) {
                is OutboundFile.Named -> file
                is OutboundFile.UriRef -> OutboundFile.Named(
                    fileName = file.uri.lastPathSegment?.substringAfterLast('/') ?: "document.bin",
                    totalBytes = 1_048_576L
                )
            }
        }
        val first = named.first()
        submitCommand(
            LocalCommand(
                frameType = FrameTypes.SEND_FILE,
                requestId = newId(),
                deviceId = deviceId,
                fileName = first.fileName,
                fileSize = first.totalBytes
            )
        )
        val rest = named.drop(1)
        if (rest.isEmpty()) return
        val peer = _snapshot.value.devices.firstOrNull { it.deviceId == deviceId } ?: return
        val queued = rest.map { spec ->
            QueuedUploadSnapshot(
                queueId = newId(),
                deviceId = deviceId,
                peerDisplayName = peer.displayName,
                fileName = DownloadNames.sanitize(spec.fileName),
                totalSize = spec.totalBytes,
                segmentCount = computeSegmentCount(spec.totalBytes)
            )
        }
        _snapshot.update { it.copy(queuedUploads = it.queuedUploads + queued) }
        _notices.tryEmit(UiEventNotice.FilesQueued(queued.size, peer.displayName))
    }

    override fun connectManual(host: String, port: Int, alias: String): Boolean {
        if (!started) {
            _notices.tryEmit(UiEventNotice.ManualConnectUnavailable)
            return false
        }
        if (!EndpointValidator.isValidHost(host) || port !in 1..65535) {
            _notices.tryEmit(UiEventNotice.ManualInvalidEndpoint)
            return false
        }
        val cleanHost = EndpointValidator.normalizeHost(host)
        val endpoint = EndpointValidator.formatEndpoint(cleanHost, port)
        val name = alias.trim().takeIf { it.isNotEmpty() && DisplayNames.isLegal(it) }
            ?: "LAN 节点 (${cleanHost.take(44)})"
        val existing = _snapshot.value.devices.firstOrNull { it.endpoint.equals(endpoint, ignoreCase = true) }
        if (existing != null) {
            _notices.tryEmit(UiEventNotice.ManualEndpointReconnected(name, endpoint))
            return true
        }
        val spki = DeviceIds.generateP256Spki()
        val peerId = DeviceIds.text(spki)
        val peer = PeerDeviceSnapshot(
            deviceId = peerId,
            displayName = name,
            spkiBytes = spki,
            formFactor = DeviceFormFactor.DESKTOP,
            trusted = false,
            presence = PeerPresence.REACHABLE,
            endpoint = endpoint
        )
        _snapshot.update {
            it.copy(
                devices = it.devices + peer,
                lastDiscoveredDeviceId = peerId,
                pairing = PairingPromptSnapshot(
                    deviceId = peerId,
                    displayName = name,
                    code = DeviceIds.shortCode(localSpki, spki),
                    remainingMilliseconds = ProtocolLimits.PROMPT_TIMEOUT_MS,
                    localAccepted = false,
                    remoteAccepted = false,
                    initiatedLocally = true
                )
            )
        }
        return true
    }

    override fun pushClipboard(text: String) {
        if (text.isEmpty()) {
            _notices.tryEmit(UiEventNotice.ClipboardEmpty)
            return
        }
        val bytes = text.toByteArray(Charsets.UTF_8)
        if (bytes.size > ProtocolLimits.MAX_CLIPBOARD_BYTES) {
            _notices.tryEmit(UiEventNotice.ClipboardTooLarge)
            return
        }
        val mutual = _snapshot.value.devices.count { it.isMutualClipboardActive }
        if (mutual == 0) {
            _notices.tryEmit(UiEventNotice.ClipboardNoMutualPeers)
            return
        }
        _snapshot.update {
            it.copy(
                clipboardState = ClipboardSyncState(
                    lastSyncedText = text,
                    lastOriginName = it.localDisplayName,
                    sequence = it.clipboardState.sequence + 1L,
                    byteCount = bytes.size
                )
            )
        }
        _notices.tryEmit(UiEventNotice.ClipboardPushed(mutual, bytes.size))
    }

    override fun resetIdentity() {
        localSpki = DeviceIds.generateP256Spki()
        _snapshot.update { it.copy(localDeviceId = DeviceIds.text(localSpki)) }
        _notices.tryEmit(UiEventNotice.IdentityResetDone)
    }

    override fun scanLan() {
        if (!started) return
        _snapshot.update { it.copy(isScanningLan = true) }
        simulateDiscoverNearbyPeer()
        _snapshot.update { it.copy(isScanningLan = false) }
    }

    override fun removeQueuedUpload(queueId: String) {
        _snapshot.update { it.copy(queuedUploads = it.queuedUploads.filterNot { q -> q.queueId == queueId }) }
    }

    override fun clearQueuedUploads() {
        _snapshot.update { it.copy(queuedUploads = emptyList()) }
    }

    override fun clearCompletedTransfers() {
        _snapshot.update { it.copy(transfers = it.transfers.filterNot { tx -> tx.completed }) }
    }

    override fun setDeveloperUiEnabled(enabled: Boolean) {
        _snapshot.update { it.copy(debugState = it.debugState.copy(developerUiEnabled = enabled)) }
        _notices.tryEmit(if (enabled) UiEventNotice.DebugModeUnlocked else UiEventNotice.DebugPreviewingRelease)
    }

    override fun clearDebugFrameLogs() {
        _snapshot.update { it.copy(debugState = it.debugState.copy(frameLogs = emptyList())) }
        _notices.tryEmit(UiEventNotice.DebugLogsCleared)
    }

    fun togglePauseTransfer(transferId: String) {
        var paused: String? = null
        var resumed: String? = null
        _snapshot.update { current ->
            current.copy(
                transfers = current.transfers.map { tx ->
                    if (tx.transferId == transferId && !tx.completed && !tx.moveBlocked) {
                        val next = !tx.pausedByOperator
                        if (next) paused = tx.fileName else resumed = tx.fileName
                        tx.copy(pausedByOperator = next)
                    } else {
                        tx
                    }
                }
            )
        }
        paused?.let { _notices.tryEmit(UiEventNotice.TransferPaused(it)) }
        resumed?.let { _notices.tryEmit(UiEventNotice.TransferResumed(it)) }
    }

    fun retryBlockedCommit(transferId: String) {
        var resolved: String? = null
        _snapshot.update { current ->
            current.copy(
                transfers = current.transfers.map { tx ->
                    if (tx.transferId == transferId && tx.moveBlocked) {
                        val unique = DownloadNames.avoidCollision(DownloadNames.sanitize(tx.fileName)) {
                            committedDownloadNames.contains(it)
                        }
                        committedDownloadNames.add(unique)
                        resolved = unique
                        tx.copy(savedFileName = unique, moveBlocked = false, completed = true)
                    } else {
                        tx
                    }
                }
            )
        }
        resolved?.let { _notices.tryEmit(UiEventNotice.TransferCompleted(it)) }
    }

    fun resendCompletedTransfer(transferId: String) {
        val done = _snapshot.value.transfers.firstOrNull {
            it.transferId == transferId && it.completed && it.direction == TransferDirection.SEND
        } ?: return
        sendFiles(done.deviceId, listOf(OutboundFile.Named(done.fileName, done.totalSize)))
    }

    fun simulateDiscoverNearbyPeer() {
        val names = listOf("Framework 13 Fedora", "MacBook Air M3", "NixOS Homelab", "Pixel Tablet")
        val forms = listOf(
            DeviceFormFactor.LAPTOP,
            DeviceFormFactor.LAPTOP,
            DeviceFormFactor.DESKTOP,
            DeviceFormFactor.PHONE
        )
        val idx = (discoveryCounter - 1) % names.size
        val suffix = if (discoveryCounter > names.size) " #$discoveryCounter" else ""
        discoveryCounter++
        val spki = DeviceIds.generateP256Spki()
        val peerId = DeviceIds.text(spki)
        val peer = PeerDeviceSnapshot(
            deviceId = peerId,
            displayName = names[idx] + suffix,
            spkiBytes = spki,
            formFactor = forms[idx],
            trusted = false,
            presence = PeerPresence.REACHABLE
        )
        _snapshot.update { it.copy(devices = it.devices + peer, lastDiscoveredDeviceId = peerId) }
        recordDebug("RX", FrameTypes.DEVICE_CHANGED, "DEVICE_CHANGED", peer.displayName)
    }

    fun simulateInboundPairingRequest() {
        val candidate = _snapshot.value.devices.firstOrNull { !it.trusted && it.presence != PeerPresence.OFFLINE }
            ?: run {
                simulateDiscoverNearbyPeer()
                _snapshot.value.devices.last()
            }
        val code = DeviceIds.shortCode(localSpki, candidate.spkiBytes)
        _snapshot.update {
            it.copy(
                pairing = PairingPromptSnapshot(
                    deviceId = candidate.deviceId,
                    displayName = candidate.displayName,
                    code = code,
                    remainingMilliseconds = ProtocolLimits.PROMPT_TIMEOUT_MS,
                    localAccepted = false,
                    remoteAccepted = true,
                    initiatedLocally = false
                )
            )
        }
        recordDebug("RX", FrameTypes.PAIRING_PROMPT, "PAIRING_PROMPT", candidate.displayName)
    }

    fun simulateInboundFileOffer(simulateMoveBlocked: Boolean = false): Boolean {
        val sender = _snapshot.value.devices.firstOrNull { it.trusted && it.presence == PeerPresence.CONNECTED }
            ?: _snapshot.value.devices.firstOrNull { it.trusted }
            ?: return false
        val busy = _snapshot.value.filePrompts.any { it.deviceId == sender.deviceId } ||
            _snapshot.value.transfers.any {
                it.deviceId == sender.deviceId && it.direction == TransferDirection.RECEIVE && !it.completed
            }
        if (busy) {
            _notices.tryEmit(UiEventNotice.SendBusy(sender.displayName))
            return false
        }
        val sampleName = if (simulateMoveBlocked) "wayland-capture-04.png" else "release-bundle.tar.zst"
        val sampleSize = if (simulateMoveBlocked) 5_245_000L else 18_640_000L
        val segs = computeSegmentCount(sampleSize)
        val txId = newId()
        if (sender.askBeforeSave) {
            _snapshot.update {
                it.copy(
                    filePrompts = listOf(
                        FilePromptSnapshot(
                            transferId = txId,
                            deviceId = sender.deviceId,
                            peerDisplayName = sender.displayName,
                            fileName = sampleName,
                            totalSize = sampleSize,
                            segmentCount = segs,
                            remainingMilliseconds = ProtocolLimits.PROMPT_TIMEOUT_MS,
                            simulateMoveBlockOnFinish = simulateMoveBlocked
                        )
                    ) + it.filePrompts
                )
            }
        } else {
            _snapshot.update {
                it.copy(
                    transfers = listOf(
                        TransferSnapshot(
                            transferId = txId,
                            deviceId = sender.deviceId,
                            peerDisplayName = sender.displayName,
                            direction = TransferDirection.RECEIVE,
                            fileName = sampleName,
                            totalSize = sampleSize,
                            completedSegments = if (simulateMoveBlocked) segs else 0,
                            segmentCount = segs,
                            moveBlocked = simulateMoveBlocked
                        )
                    ) + it.transfers
                )
            }
            _notices.tryEmit(UiEventNotice.InboundAutoAccepted(sender.displayName, sampleName))
        }
        return true
    }

    fun simulateRemoteClipboardDelta() {
        val sender = _snapshot.value.devices.firstOrNull { it.isMutualClipboardActive }
        if (sender == null) {
            _notices.tryEmit(UiEventNotice.ClipboardNoMutualPeers)
            return
        }
        val snippets = listOf(
            "ContextAll.AppImage file send ${_snapshot.value.localDeviceId.take(12)} ./report.pdf",
            "curl -fsSL https://lan.local:42461/health # QUIC ALPN contextall",
            "ssh -J workstation.lan dev@10.0.0.42"
        )
        val picked = snippets[(_snapshot.value.clipboardState.sequence.toInt()) % snippets.size]
        val bytes = picked.toByteArray(Charsets.UTF_8)
        _snapshot.update {
            it.copy(
                clipboardState = ClipboardSyncState(
                    lastSyncedText = picked,
                    lastOriginName = sender.displayName,
                    sequence = it.clipboardState.sequence + 1L,
                    byteCount = bytes.size
                )
            )
        }
        _notices.tryEmit(UiEventNotice.ClipboardReceived(sender.displayName, picked))
    }

    fun toggleTrustedPeerConnectivity(deviceId: String? = null) {
        _snapshot.update { current ->
            val targetId = deviceId
                ?: current.devices.firstOrNull { it.trusted }?.deviceId
                ?: return@update current
            current.copy(
                devices = current.devices.map { dev ->
                    if (dev.deviceId != targetId) {
                        dev
                    } else {
                        dev.copy(
                            presence = if (dev.presence == PeerPresence.CONNECTED) {
                                PeerPresence.OFFLINE
                            } else {
                                PeerPresence.CONNECTED
                            }
                        )
                    }
                }
            )
        }
    }

    fun simulateTelemetryJitterBurst() {
        val attempts = 18
        val succeeded = 16
        val upBytes = 32_000_000L
        val downBytes = 24_000_000L
        _snapshot.update { current ->
            val burst = TelemetryBucketSnapshot(
                label = "压测",
                connectionAttempts = attempts,
                connectionSucceeded = succeeded,
                uploadBytes = upBytes,
                downloadBytes = downBytes
            )
            current.copy(
                telemetry = current.telemetry.copy(
                    buckets = (current.telemetry.buckets + burst).takeLast(8),
                    verifiedSegmentsCount = current.telemetry.verifiedSegmentsCount + computeSegmentCount(upBytes + downBytes)
                )
            )
        }
    }

    private fun patchDisplayName(cmd: LocalCommand): CommandResult {
        val candidate = cmd.displayName
        if (candidate == null || !DisplayNames.isLegal(candidate)) {
            _notices.tryEmit(UiEventNotice.InvalidDisplayName)
            return CommandResult(cmd.requestId, false, "invalid-name")
        }
        _snapshot.update { it.copy(localDisplayName = candidate) }
        _notices.tryEmit(UiEventNotice.DisplayNameSaved(candidate))
        return CommandResult(cmd.requestId, true)
    }

    private fun patchAcceptPairing(cmd: LocalCommand): CommandResult {
        val active = _snapshot.value.pairing ?: return CommandResult(cmd.requestId, false, "no-active-pairing")
        _snapshot.update { current ->
            current.copy(
                pairing = null,
                devices = current.devices.map { dev ->
                    if (dev.deviceId == active.deviceId) {
                        dev.copy(
                            trusted = true,
                            presence = PeerPresence.CONNECTED,
                            clipboardSync = false,
                            askBeforeSave = true
                        )
                    } else {
                        dev
                    }
                }
            )
        }
        _notices.tryEmit(UiEventNotice.Paired(active.displayName))
        return CommandResult(cmd.requestId, true)
    }

    private fun patchRejectPairing(cmd: LocalCommand): CommandResult {
        val active = _snapshot.value.pairing ?: return CommandResult(cmd.requestId, false, "no-active-pairing")
        _snapshot.update { it.copy(pairing = null) }
        _notices.tryEmit(
            if (active.initiatedLocally) UiEventNotice.PairingCancelled else UiEventNotice.PairingRejected
        )
        return CommandResult(cmd.requestId, true)
    }

    private fun patchUnpair(cmd: LocalCommand): CommandResult {
        val targetId = cmd.deviceId ?: return CommandResult(cmd.requestId, false, "missing-device")
        val peer = _snapshot.value.devices.firstOrNull { it.deviceId == targetId }
            ?: return CommandResult(cmd.requestId, false, "unknown-device")
        _snapshot.update { current ->
            current.copy(
                filePrompts = current.filePrompts.filterNot { it.deviceId == targetId },
                transfers = current.transfers.filter { it.deviceId != targetId || it.completed },
                queuedUploads = current.queuedUploads.filterNot { it.deviceId == targetId },
                devices = current.devices.map { dev ->
                    if (dev.deviceId == targetId) {
                        dev.copy(trusted = false, presence = PeerPresence.REACHABLE, clipboardSync = false, askBeforeSave = true)
                    } else {
                        dev
                    }
                }
            )
        }
        _notices.tryEmit(UiEventNotice.Unpaired(peer.displayName))
        return CommandResult(cmd.requestId, true)
    }

    private fun patchPeerFlag(cmd: LocalCommand, clipboard: Boolean): CommandResult {
        val id = cmd.deviceId ?: return CommandResult(cmd.requestId, false, "invalid")
        val enabled = cmd.enabled ?: return CommandResult(cmd.requestId, false, "invalid")
        _snapshot.update { current ->
            current.copy(
                devices = current.devices.map { dev ->
                    if (dev.deviceId == id && dev.trusted) {
                        if (clipboard) dev.copy(clipboardSync = enabled) else dev.copy(askBeforeSave = enabled)
                    } else {
                        dev
                    }
                }
            )
        }
        return CommandResult(cmd.requestId, true)
    }

    private fun patchSendFile(cmd: LocalCommand): CommandResult {
        val peerId = cmd.deviceId ?: return CommandResult(cmd.requestId, false, "missing-device")
        val rawName = cmd.fileName ?: return CommandResult(cmd.requestId, false, "not-a-file")
        val totalBytes = cmd.fileSize ?: return CommandResult(cmd.requestId, false, "not-a-file")
        val peer = _snapshot.value.devices.firstOrNull { it.deviceId == peerId && it.trusted }
            ?: return CommandResult(cmd.requestId, false, "untrusted")
        if (totalBytes < 0L || totalBytes > ProtocolLimits.MAX_FILE_BYTES) {
            _notices.tryEmit(UiEventNotice.SendTooLarge)
            return CommandResult(cmd.requestId, false, "too-large")
        }
        val busy = _snapshot.value.transfers.any {
            it.deviceId == peerId && it.direction == TransferDirection.SEND && !it.completed
        }
        if (busy) {
            _notices.tryEmit(UiEventNotice.SendBusy(peer.displayName))
            return CommandResult(cmd.requestId, false, "busy")
        }
        val cleanName = DownloadNames.sanitize(rawName)
        val segments = computeSegmentCount(totalBytes)
        val online = peer.presence == PeerPresence.CONNECTED
        val tx = TransferSnapshot(
            transferId = newId(),
            deviceId = peer.deviceId,
            peerDisplayName = peer.displayName,
            direction = TransferDirection.SEND,
            fileName = cleanName,
            totalSize = totalBytes,
            completedSegments = 0,
            segmentCount = segments,
            waitingReconnect = !online,
            completed = segments == 0 && online
        )
        _snapshot.update { it.copy(transfers = listOf(tx) + it.transfers) }
        if (tx.completed) {
            _notices.tryEmit(UiEventNotice.TransferCompleted(cleanName))
        } else if (!online) {
            _notices.tryEmit(UiEventNotice.SendHeldOffline)
        } else {
            _notices.tryEmit(UiEventNotice.SendStarted(peer.displayName, cleanName, segments))
        }
        return CommandResult(cmd.requestId, true)
    }

    private fun patchAcceptFile(cmd: LocalCommand): CommandResult {
        val transferId = cmd.transferId ?: return CommandResult(cmd.requestId, false, "invalid")
        val prompt = _snapshot.value.filePrompts.firstOrNull { it.transferId == transferId }
            ?: return CommandResult(cmd.requestId, false, "already-decided")
        val blocked = prompt.simulateMoveBlockOnFinish
        val tx = TransferSnapshot(
            transferId = prompt.transferId,
            deviceId = prompt.deviceId,
            peerDisplayName = prompt.peerDisplayName,
            direction = TransferDirection.RECEIVE,
            fileName = prompt.fileName,
            totalSize = prompt.totalSize,
            completedSegments = if (blocked) prompt.segmentCount else 0,
            segmentCount = prompt.segmentCount,
            moveBlocked = blocked,
            completed = prompt.segmentCount == 0 && !blocked
        )
        _snapshot.update {
            it.copy(
                filePrompts = it.filePrompts.filterNot { p -> p.transferId == transferId },
                transfers = listOf(tx) + it.transfers
            )
        }
        return CommandResult(cmd.requestId, true)
    }

    private fun patchRejectFile(cmd: LocalCommand): CommandResult {
        val transferId = cmd.transferId ?: return CommandResult(cmd.requestId, false, "invalid")
        if (_snapshot.value.filePrompts.none { it.transferId == transferId }) {
            return CommandResult(cmd.requestId, false, "already-decided")
        }
        _snapshot.update { it.copy(filePrompts = it.filePrompts.filterNot { p -> p.transferId == transferId }) }
        return CommandResult(cmd.requestId, true)
    }

    private fun patchCancelTransfer(cmd: LocalCommand): CommandResult {
        val transferId = cmd.transferId ?: return CommandResult(cmd.requestId, false, "invalid")
        _snapshot.update { current ->
            current.copy(
                filePrompts = current.filePrompts.filterNot { it.transferId == transferId },
                transfers = current.transfers.filterNot { it.transferId == transferId && !it.completed }
            )
        }
        return CommandResult(cmd.requestId, true)
    }

    private fun cannedLabSnapshot(displayName: String, debugMode: Boolean): SessionSnapshotUi {
        val selfId = DeviceIds.text(localSpki)
        val desktopSpki = DeviceIds.generateP256Spki()
        val laptopSpki = DeviceIds.generateP256Spki()
        val studySpki = DeviceIds.generateP256Spki()
        val desktopId = DeviceIds.text(desktopSpki)
        val laptopId = DeviceIds.text(laptopSpki)
        val clip = "export WAYLAND_DISPLAY=wayland-1"
        committedDownloadNames.clear()
        committedDownloadNames.add("wayland-capture-04.png")
        return SessionSnapshotUi(
            localDisplayName = displayName,
            localDeviceId = selfId,
            wifiLanReady = true,
            linkServiceRunning = true,
            devices = listOf(
                PeerDeviceSnapshot(
                    deviceId = desktopId,
                    displayName = "Arch Workstation",
                    spkiBytes = desktopSpki,
                    formFactor = DeviceFormFactor.DESKTOP,
                    trusted = true,
                    presence = PeerPresence.CONNECTED,
                    clipboardSync = true,
                    remoteClipboardOptIn = true,
                    askBeforeSave = true
                ),
                PeerDeviceSnapshot(
                    deviceId = laptopId,
                    displayName = "ThinkPad X1 Carbon",
                    spkiBytes = laptopSpki,
                    formFactor = DeviceFormFactor.LAPTOP,
                    trusted = true,
                    presence = PeerPresence.OFFLINE,
                    clipboardSync = false,
                    remoteClipboardOptIn = true,
                    askBeforeSave = true
                ),
                PeerDeviceSnapshot(
                    deviceId = DeviceIds.text(studySpki),
                    displayName = "Studio Mini",
                    spkiBytes = studySpki,
                    formFactor = DeviceFormFactor.DESKTOP,
                    trusted = false,
                    presence = PeerPresence.REACHABLE
                )
            ),
            transfers = listOf(
                TransferSnapshot(
                    transferId = newId(),
                    deviceId = laptopId,
                    peerDisplayName = "ThinkPad X1 Carbon",
                    direction = TransferDirection.RECEIVE,
                    fileName = "dataset-shard-02.parquet",
                    totalSize = 26_200_000L,
                    completedSegments = 128,
                    segmentCount = 400,
                    waitingReconnect = true
                ),
                TransferSnapshot(
                    transferId = newId(),
                    deviceId = desktopId,
                    peerDisplayName = "Arch Workstation",
                    direction = TransferDirection.RECEIVE,
                    fileName = "wayland-capture-04.png",
                    savedFileName = "wayland-capture-04.png",
                    totalSize = 4_192_000L,
                    completedSegments = 64,
                    segmentCount = 64,
                    completed = true
                )
            ),
            telemetry = SessionTelemetrySnapshot(
                buckets = listOf(
                    TelemetryBucketSnapshot("-30m", 12, 11, 6_400_000L, 3_200_000L),
                    TelemetryBucketSnapshot("当前", 10, 9, 5_240_000L, 12_576_000L)
                ),
                verifiedSegmentsCount = 192
            ),
            clipboardState = ClipboardSyncState(
                lastSyncedText = clip,
                lastOriginName = "Arch Workstation",
                sequence = 1L,
                byteCount = clip.toByteArray(Charsets.UTF_8).size
            ),
            debugState = EngineDebugState(
                developerUiEnabled = debugMode,
                seedProfile = SeedDataProfile.DEVELOPER_LAB,
                committedDownloadCount = 1
            )
        )
    }

    private fun recordDebug(category: String, frameCode: Int, frameName: String, summary: String, ok: Boolean = true) {
        val entry = DebugFrameLogEntry(
            seq = frameSeqCounter++,
            timestampText = timeFormat.format(Date()),
            category = category,
            frameCode = frameCode,
            frameName = frameName,
            summary = summary,
            ok = ok
        )
        _snapshot.update { current ->
            current.copy(
                debugState = current.debugState.copy(
                    frameLogs = (listOf(entry) + current.debugState.frameLogs).take(60)
                )
            )
        }
    }

    private fun newId(): String =
        UUID.randomUUID().toString().replace("-", "").lowercase()
}
