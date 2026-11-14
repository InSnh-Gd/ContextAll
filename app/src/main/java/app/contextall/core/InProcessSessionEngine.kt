package app.contextall.core

import app.contextall.model.ClipboardSyncState
import app.contextall.model.CommandResult
import app.contextall.model.DebugFrameLogEntry
import app.contextall.model.DeviceFormFactor
import app.contextall.model.EngineDebugState
import app.contextall.model.FilePromptSnapshot
import app.contextall.model.LocalCommand
import app.contextall.model.OutgoingFileSpec
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
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch

/**
 * In-process session engine faithfully implementing ContextAll's SessionHost / LocalDesk /
 * PairingSession / TransferBook / ClipboardNode invariants without opening any local socket.
 */
class InProcessSessionEngine(
    private val scope: CoroutineScope,
    initialDisplayName: String = "Pixel 9 · ContextAll",
    initialDebugMode: Boolean = true,
    autoStartTicker: Boolean = true
) {
    private var localSpki: ByteArray = DeviceIds.generateP256Spki()
    private val seenRequests = HashSet<String>()
    private val committedDownloadNames = LinkedHashSet<String>().apply {
        add("周末旅行合影.jpg")
    }
    private var discoveryCounter = 1
    private var frameSeqCounter = 1
    private val timeFormat = SimpleDateFormat("HH:mm:ss.SSS", Locale.US)
    private var tickerJob: Job? = null
    private var scanJob: Job? = null
    private var highlightClearJob: Job? = null

    private val _snapshot = MutableStateFlow(
        createInitialSnapshot(
            displayName = initialDisplayName,
            deferInitialNearbyPeer = autoStartTicker,
            initialDebugMode = initialDebugMode,
            seedProfile = SeedDataProfile.CONSUMER
        )
    )
    val snapshot: StateFlow<SessionSnapshotUi> = _snapshot.asStateFlow()

    private val _notices = MutableSharedFlow<UiEventNotice>(extraBufferCapacity = 16)
    val notices: SharedFlow<UiEventNotice> = _notices.asSharedFlow()

    init {
        recordDebugFrame(
            category = "BOOT",
            frameCode = FrameTypes.SNAPSHOT,
            frameName = "SNAPSHOT",
            summary = "SessionHost ready · port=${ProtocolLimits.DEFAULT_QUIC_PORT} · spki=${_snapshot.value.localDeviceId.take(10)}…",
            ok = true
        )
        if (autoStartTicker) {
            startTicker()
            triggerDiscoveryScan(initialBootSweep = true)
        }
    }

    private fun createInitialSnapshot(
        displayName: String,
        deferInitialNearbyPeer: Boolean,
        initialDebugMode: Boolean = true,
        seedProfile: SeedDataProfile = SeedDataProfile.CONSUMER
    ): SessionSnapshotUi {
        val selfId = DeviceIds.text(localSpki)
        val desktopSpki = DeviceIds.generateP256Spki()
        val laptopSpki = DeviceIds.generateP256Spki()
        val desktopId = DeviceIds.text(desktopSpki)
        val laptopId = DeviceIds.text(laptopSpki)

        val isDevLab = seedProfile == SeedDataProfile.DEVELOPER_LAB
        val desktopName = if (isDevLab) "Arch Workstation" else "我的台式电脑"
        val laptopName = if (isDevLab) "ThinkPad X1 Carbon" else "MacBook Air"
        val nearbyName = if (isDevLab) "Studio Mini" else "书房电脑"
        val pausedFile = if (isDevLab) "dataset-shard-02.parquet" else "项目演示文稿_终版.pptx"
        val doneFile = if (isDevLab) "wayland-capture-04.png" else "周末旅行合影.jpg"
        val initialClip = if (isDevLab) {
            "export WAYLAND_DISPLAY=wayland-1"
        } else {
            "下午 3:00 产品设计评审纪要已整理完成，相关文档可直接查收。"
        }

        val baseDevices = mutableListOf(
            PeerDeviceSnapshot(
                deviceId = desktopId,
                displayName = desktopName,
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
                displayName = laptopName,
                spkiBytes = laptopSpki,
                formFactor = DeviceFormFactor.LAPTOP,
                trusted = true,
                presence = PeerPresence.OFFLINE,
                clipboardSync = false,
                remoteClipboardOptIn = true,
                askBeforeSave = true
            )
        )

        if (!deferInitialNearbyPeer) {
            val studySpki = DeviceIds.generateP256Spki()
            baseDevices.add(
                PeerDeviceSnapshot(
                    deviceId = DeviceIds.text(studySpki),
                    displayName = nearbyName,
                    spkiBytes = studySpki,
                    formFactor = DeviceFormFactor.DESKTOP,
                    trusted = false,
                    presence = PeerPresence.REACHABLE
                )
            )
        }

        return SessionSnapshotUi(
            localDisplayName = displayName,
            localDeviceId = selfId,
            wifiLanReady = true,
            linkServiceRunning = true,
            isScanningLan = deferInitialNearbyPeer,
            devices = baseDevices,
            transfers = listOf(
                TransferSnapshot(
                    transferId = newTransferId(),
                    deviceId = laptopId,
                    peerDisplayName = laptopName,
                    direction = TransferDirection.RECEIVE,
                    fileName = pausedFile,
                    totalSize = 26_200_000L,
                    completedSegments = 128,
                    segmentCount = 400,
                    waitingReconnect = true
                ),
                TransferSnapshot(
                    transferId = newTransferId(),
                    deviceId = desktopId,
                    peerDisplayName = desktopName,
                    direction = TransferDirection.RECEIVE,
                    fileName = doneFile,
                    savedFileName = doneFile,
                    totalSize = 4_192_000L,
                    completedSegments = 64,
                    segmentCount = 64,
                    completed = true
                )
            ),
            telemetry = createInitialTelemetry(),
            clipboardState = ClipboardSyncState(
                lastSyncedText = initialClip,
                lastOriginName = desktopName,
                sequence = 1L,
                byteCount = initialClip.toByteArray(Charsets.UTF_8).size
            ),
            debugState = EngineDebugState(
                developerUiEnabled = initialDebugMode,
                seedProfile = seedProfile,
                dedupedRequestCount = seenRequests.size,
                committedDownloadCount = committedDownloadNames.size
            )
        )
    }

    private fun createInitialTelemetry(): SessionTelemetrySnapshot = SessionTelemetrySnapshot(
        buckets = listOf(
            TelemetryBucketSnapshot(
                label = "-30m",
                connectionAttempts = 12,
                connectionSucceeded = 11,
                uploadBytes = 6_400_000L,
                downloadBytes = 3_200_000L
            ),
            TelemetryBucketSnapshot(
                label = "-25m",
                connectionAttempts = 14,
                connectionSucceeded = 14,
                uploadBytes = 14_800_000L,
                downloadBytes = 8_600_000L
            ),
            TelemetryBucketSnapshot(
                label = "-20m",
                connectionAttempts = 15,
                connectionSucceeded = 13,
                uploadBytes = 9_200_000L,
                downloadBytes = 19_400_000L
            ),
            TelemetryBucketSnapshot(
                label = "-15m",
                connectionAttempts = 16,
                connectionSucceeded = 15,
                uploadBytes = 22_500_000L,
                downloadBytes = 11_300_000L
            ),
            TelemetryBucketSnapshot(
                label = "-10m",
                connectionAttempts = 12,
                connectionSucceeded = 11,
                uploadBytes = 11_600_000L,
                downloadBytes = 8_384_000L
            ),
            TelemetryBucketSnapshot(
                label = "-5m",
                connectionAttempts = 18,
                connectionSucceeded = 18,
                uploadBytes = 18_900_000L,
                downloadBytes = 15_720_000L
            ),
            TelemetryBucketSnapshot(
                label = "当前",
                connectionAttempts = 10,
                connectionSucceeded = 9,
                uploadBytes = 5_240_000L,
                downloadBytes = 12_576_000L
            )
        ),
        verifiedSegmentsCount = 192
    )

    fun startTicker() {
        if (tickerJob?.isActive == true) return
        tickerJob = scope.launch {
            while (isActive) {
                delay(250L)
                advanceClock(250L)
            }
        }
    }

    /**
     * Advances countdowns and sliding-window segment transfers deterministically.
     * Also exposed publicly so JVM unit tests can step time without sleeping.
     */
    fun advanceClock(deltaMs: Long) {
        var pairingTimedOutPeer: String? = null
        val timedOutPromptPeers = mutableListOf<String>()
        val newlyCompletedFiles = mutableListOf<String>()

        _snapshot.update { current ->
            // 1. Pairing 60s countdown
            val updatedPairing = current.pairing?.let { p ->
                val rem = p.remainingMilliseconds - deltaMs
                if (rem <= 0L) {
                    pairingTimedOutPeer = p.displayName
                    null
                } else {
                    p.copy(remainingMilliseconds = rem)
                }
            }

            // 2. Incoming FilePrompt 60s countdowns
            val updatedPrompts = current.filePrompts.mapNotNull { fp ->
                val rem = fp.remainingMilliseconds - deltaMs
                if (rem <= 0L) {
                    timedOutPromptPeers.add(fp.peerDisplayName)
                    null
                } else {
                    fp.copy(remainingMilliseconds = rem)
                }
            }

            // 3. Sliding-window (8 segments/tick when connected on Wi-Fi) transfer progress
            val canStream = current.wifiLanReady && current.linkServiceRunning
            val connectedPeerIds = current.devices
                .filter { it.trusted && it.presence == PeerPresence.CONNECTED }
                .map { it.deviceId }
                .toSet()

            var tickUploadedBytes = 0L
            var tickDownloadedBytes = 0L
            var tickVerifiedSegments = 0
            val finishedOutboundPeerIds = LinkedHashSet<String>()

            val updatedTransfers = current.transfers.map { tx ->
                if (tx.completed || tx.moveBlocked || tx.pausedByOperator) {
                    tx
                } else if (!canStream || tx.deviceId !in connectedPeerIds) {
                    if (tx.waitingReconnect) tx else tx.copy(waitingReconnect = true)
                } else {
                    val step = ProtocolLimits.WINDOW_SEGMENTS * 2
                    val nextDone = (tx.completedSegments + step).coerceAtMost(tx.segmentCount)
                    val deltaSegments = (nextDone - tx.completedSegments).coerceAtLeast(0)
                    if (deltaSegments > 0) {
                        val prevBytes = (tx.completedSegments.toLong() * ProtocolLimits.SEGMENT_SIZE)
                            .coerceAtMost(tx.totalSize)
                        val nextBytes = (nextDone.toLong() * ProtocolLimits.SEGMENT_SIZE)
                            .coerceAtMost(tx.totalSize)
                        val deltaBytes = (nextBytes - prevBytes).coerceAtLeast(0L)
                        if (tx.direction == TransferDirection.SEND) {
                            tickUploadedBytes += deltaBytes
                        } else {
                            tickDownloadedBytes += deltaBytes
                        }
                        tickVerifiedSegments += deltaSegments
                    }

                    if (nextDone >= tx.segmentCount) {
                        val finalSavedName = if (tx.direction == TransferDirection.RECEIVE) {
                            val clean = DownloadNames.sanitize(tx.fileName)
                            val unique = DownloadNames.avoidCollision(clean) { committedDownloadNames.contains(it) }
                            committedDownloadNames.add(unique)
                            unique
                        } else {
                            finishedOutboundPeerIds.add(tx.deviceId)
                            tx.fileName
                        }
                        newlyCompletedFiles.add(finalSavedName)
                        tx.copy(
                            completedSegments = tx.segmentCount,
                            savedFileName = finalSavedName,
                            waitingReconnect = false,
                            completed = true
                        )
                    } else {
                        tx.copy(
                            completedSegments = nextDone,
                            waitingReconnect = false
                        )
                    }
                }
            }

            // Promote next queued upload per peer whose single outbound slot just freed up
            var nextQueue = current.queuedUploads
            val promotedTransfers = mutableListOf<TransferSnapshot>()
            for (freedPeerId in finishedOutboundPeerIds) {
                val nextQueued = nextQueue.firstOrNull { it.deviceId == freedPeerId } ?: continue
                nextQueue = nextQueue.filterNot { it.queueId == nextQueued.queueId }
                val peerOnline = canStream && freedPeerId in connectedPeerIds
                val zeroComplete = nextQueued.segmentCount == 0 && peerOnline
                promotedTransfers.add(
                    TransferSnapshot(
                        transferId = newTransferId(),
                        deviceId = nextQueued.deviceId,
                        peerDisplayName = nextQueued.peerDisplayName,
                        direction = TransferDirection.SEND,
                        fileName = nextQueued.fileName,
                        totalSize = nextQueued.totalSize,
                        completedSegments = 0,
                        segmentCount = nextQueued.segmentCount,
                        waitingReconnect = !peerOnline,
                        completed = zeroComplete
                    )
                )
                if (zeroComplete) {
                    newlyCompletedFiles.add(nextQueued.fileName)
                }
            }

            val updatedTelemetry = current.telemetry.withLiveDelta(
                uploadDelta = tickUploadedBytes,
                downloadDelta = tickDownloadedBytes,
                segmentsDelta = tickVerifiedSegments,
                attemptDelta = if (pairingTimedOutPeer != null) 1 else 0,
                successDelta = 0
            )

            current.copy(
                pairing = updatedPairing,
                filePrompts = updatedPrompts,
                transfers = promotedTransfers + updatedTransfers,
                queuedUploads = nextQueue,
                telemetry = updatedTelemetry
            )
        }

        pairingTimedOutPeer?.let { _notices.tryEmit(UiEventNotice.PairingTimeout(it)) }
        timedOutPromptPeers.forEach { _notices.tryEmit(UiEventNotice.InboundTimeout(it)) }
        newlyCompletedFiles.forEach { _notices.tryEmit(UiEventNotice.TransferCompleted(it)) }
    }

    /**
     * Submits a local operator command (FrameTypes 64..75) with requestId deduplication.
     */
    fun submitCommand(command: LocalCommand): CommandResult {
        if (command.frameType !in FrameTypes.START_PAIRING..FrameTypes.CANCEL_TRANSFER) {
            val invalidRes = CommandResult(command.requestId, ok = false, error = "invalid")
            recordCommandTrace(command, invalidRes)
            return invalidRes
        }
        if (!seenRequests.add(command.requestId)) {
            val dupRes = CommandResult(command.requestId, ok = false, error = "duplicate-request")
            recordCommandTrace(command, dupRes)
            return dupRes
        }

        val result = when (command.frameType) {
            FrameTypes.START_PAIRING -> handleStartPairing(command)
            FrameTypes.ACCEPT_PAIRING -> handleAcceptPairing(command)
            FrameTypes.REJECT_PAIRING -> handleRejectPairing(command)
            FrameTypes.UNPAIR_LOCAL -> handleUnpairLocal(command)
            FrameTypes.SET_DISPLAY_NAME -> handleSetDisplayName(command)
            FrameTypes.SET_CLIPBOARD_SYNC -> handleSetClipboardSync(command)
            FrameTypes.SET_ASK_BEFORE_SAVE -> handleSetAskBeforeSave(command)
            FrameTypes.SEND_FILE -> handleSendFile(command)
            FrameTypes.ACCEPT_FILE -> handleAcceptFile(command)
            FrameTypes.REJECT_FILE -> handleRejectFile(command)
            FrameTypes.CANCEL_TRANSFER -> handleCancelTransfer(command)
            else -> CommandResult(command.requestId, ok = true)
        }
        recordCommandTrace(command, result)
        return result
    }

    private fun handleStartPairing(cmd: LocalCommand): CommandResult {
        val peer = _snapshot.value.devices.firstOrNull { it.deviceId == cmd.deviceId }
            ?: return CommandResult(cmd.requestId, ok = false, error = "unknown-device")
        if (peer.trusted) {
            return CommandResult(cmd.requestId, ok = false, error = "already-trusted")
        }
        if (peer.presence == PeerPresence.OFFLINE || !_snapshot.value.wifiLanReady) {
            return CommandResult(cmd.requestId, ok = false, error = "unreachable")
        }

        val code = DeviceIds.shortCode(localSpki, peer.spkiBytes)
        _snapshot.update {
            it.copy(
                pairing = PairingPromptSnapshot(
                    deviceId = peer.deviceId,
                    displayName = peer.displayName,
                    code = code,
                    remainingMilliseconds = ProtocolLimits.PROMPT_TIMEOUT_MS,
                    localAccepted = false,
                    remoteAccepted = false,
                    initiatedLocally = true
                )
            )
        }
        return CommandResult(cmd.requestId, ok = true)
    }

    private fun handleAcceptPairing(cmd: LocalCommand): CommandResult {
        val active = _snapshot.value.pairing
            ?: return CommandResult(cmd.requestId, ok = false, error = "no-active-pairing")

        if (active.remoteAccepted) {
            finalizeTrust(active.deviceId, active.displayName)
        } else {
            _snapshot.update {
                it.copy(pairing = active.copy(localAccepted = true))
            }
            // Simulate remote operator confirming the matching 6-char code shortly after
            scope.launch {
                delay(550L)
                val currentPairing = _snapshot.value.pairing
                if (currentPairing != null &&
                    currentPairing.deviceId == active.deviceId &&
                    currentPairing.localAccepted
                ) {
                    finalizeTrust(currentPairing.deviceId, currentPairing.displayName)
                }
            }
        }
        return CommandResult(cmd.requestId, ok = true)
    }

    private fun finalizeTrust(deviceId: String, displayName: String) {
        _snapshot.update { current ->
            val nextPresence = if (current.wifiLanReady && current.linkServiceRunning) {
                PeerPresence.CONNECTED
            } else {
                PeerPresence.OFFLINE
            }
            current.copy(
                pairing = null,
                telemetry = current.telemetry.withLiveDelta(
                    attemptDelta = 1,
                    successDelta = if (nextPresence == PeerPresence.CONNECTED) 1 else 0
                ),
                devices = current.devices.map { dev ->
                    if (dev.deviceId == deviceId) {
                        // Spec invariant: new trust starts with clipboardSync=false, askBeforeSave=true
                        dev.copy(
                            trusted = true,
                            presence = nextPresence,
                            clipboardSync = false,
                            askBeforeSave = true
                        )
                    } else {
                        dev
                    }
                }
            )
        }
        _notices.tryEmit(UiEventNotice.Paired(displayName))
    }

    private fun handleRejectPairing(cmd: LocalCommand): CommandResult {
        val active = _snapshot.value.pairing
            ?: return CommandResult(cmd.requestId, ok = false, error = "no-active-pairing")
        _snapshot.update {
            it.copy(
                pairing = null,
                telemetry = if (active.initiatedLocally) {
                    it.telemetry
                } else {
                    it.telemetry.withLiveDelta(attemptDelta = 1, successDelta = 0)
                }
            )
        }
        _notices.tryEmit(
            if (active.initiatedLocally) UiEventNotice.PairingCancelled
            else UiEventNotice.PairingRejected
        )
        return CommandResult(cmd.requestId, ok = true)
    }

    private fun handleUnpairLocal(cmd: LocalCommand): CommandResult {
        val targetId = cmd.deviceId ?: return CommandResult(cmd.requestId, ok = false, error = "missing-device")
        val targetPeer = _snapshot.value.devices.firstOrNull { it.deviceId == targetId }
            ?: return CommandResult(cmd.requestId, ok = false, error = "unknown-device")

        _snapshot.update { current ->
            val nextPresence = if (current.wifiLanReady && current.linkServiceRunning &&
                targetPeer.presence != PeerPresence.OFFLINE
            ) {
                PeerPresence.REACHABLE
            } else {
                PeerPresence.OFFLINE
            }
            current.copy(
                selectedDeviceId = if (current.selectedDeviceId == targetId) null else current.selectedDeviceId,
                // Cancel & remove all incomplete transfers, queued uploads, and prompts for this peer
                filePrompts = current.filePrompts.filterNot { it.deviceId == targetId },
                transfers = current.transfers.filter { it.deviceId != targetId || it.completed },
                queuedUploads = current.queuedUploads.filterNot { it.deviceId == targetId },
                devices = current.devices.map { dev ->
                    if (dev.deviceId == targetId) {
                        dev.copy(
                            trusted = false,
                            presence = nextPresence,
                            clipboardSync = false,
                            askBeforeSave = true
                        )
                    } else {
                        dev
                    }
                }
            )
        }
        _notices.tryEmit(UiEventNotice.Unpaired(targetPeer.displayName))
        return CommandResult(cmd.requestId, ok = true)
    }

    private fun handleSetDisplayName(cmd: LocalCommand): CommandResult {
        val candidate = cmd.displayName
        if (candidate == null || !DisplayNames.isLegal(candidate)) {
            _notices.tryEmit(UiEventNotice.InvalidDisplayName)
            return CommandResult(cmd.requestId, ok = false, error = "invalid-name")
        }
        _snapshot.update { it.copy(localDisplayName = candidate) }
        _notices.tryEmit(UiEventNotice.DisplayNameSaved(candidate))
        return CommandResult(cmd.requestId, ok = true)
    }

    private fun handleSetClipboardSync(cmd: LocalCommand): CommandResult {
        val id = cmd.deviceId ?: return CommandResult(cmd.requestId, ok = false, error = "invalid")
        val enabled = cmd.enabled ?: return CommandResult(cmd.requestId, ok = false, error = "invalid")
        _snapshot.update { current ->
            current.copy(
                devices = current.devices.map {
                    if (it.deviceId == id && it.trusted) it.copy(clipboardSync = enabled) else it
                }
            )
        }
        return CommandResult(cmd.requestId, ok = true)
    }

    private fun handleSetAskBeforeSave(cmd: LocalCommand): CommandResult {
        val id = cmd.deviceId ?: return CommandResult(cmd.requestId, ok = false, error = "invalid")
        val enabled = cmd.enabled ?: return CommandResult(cmd.requestId, ok = false, error = "invalid")
        _snapshot.update { current ->
            current.copy(
                devices = current.devices.map {
                    if (it.deviceId == id && it.trusted) it.copy(askBeforeSave = enabled) else it
                }
            )
        }
        return CommandResult(cmd.requestId, ok = true)
    }

    private fun handleSendFile(cmd: LocalCommand): CommandResult {
        val peerId = cmd.deviceId ?: return CommandResult(cmd.requestId, ok = false, error = "missing-device")
        val rawName = cmd.fileName ?: return CommandResult(cmd.requestId, ok = false, error = "not-a-file")
        val totalBytes = cmd.fileSize ?: return CommandResult(cmd.requestId, ok = false, error = "not-a-file")

        val peer = _snapshot.value.devices.firstOrNull { it.deviceId == peerId && it.trusted }
            ?: return CommandResult(cmd.requestId, ok = false, error = "untrusted")

        if (totalBytes < 0L || totalBytes > ProtocolLimits.MAX_FILE_BYTES) {
            _notices.tryEmit(UiEventNotice.SendTooLarge)
            return CommandResult(cmd.requestId, ok = false, error = "too-large")
        }

        // Single outbound slot per peer invariant
        val hasOutboundInFlight = _snapshot.value.transfers.any {
            it.deviceId == peerId && it.direction == TransferDirection.SEND && !it.completed
        }
        if (hasOutboundInFlight) {
            _notices.tryEmit(UiEventNotice.SendBusy(peer.displayName))
            return CommandResult(cmd.requestId, ok = false, error = "busy")
        }

        val cleanName = DownloadNames.sanitize(rawName)
        val segments = computeSegmentCount(totalBytes)
        val isOnline = _snapshot.value.wifiLanReady &&
            _snapshot.value.linkServiceRunning &&
            peer.presence == PeerPresence.CONNECTED

        val zeroByteImmediateComplete = segments == 0 && isOnline
        val newTx = TransferSnapshot(
            transferId = newTransferId(),
            deviceId = peer.deviceId,
            peerDisplayName = peer.displayName,
            direction = TransferDirection.SEND,
            fileName = cleanName,
            totalSize = totalBytes,
            completedSegments = 0,
            segmentCount = segments,
            waitingReconnect = !isOnline,
            completed = zeroByteImmediateComplete
        )

        _snapshot.update { current ->
            current.copy(transfers = listOf(newTx) + current.transfers)
        }

        if (zeroByteImmediateComplete) {
            _notices.tryEmit(UiEventNotice.TransferCompleted(cleanName))
        } else if (!isOnline) {
            _notices.tryEmit(UiEventNotice.SendHeldOffline)
        } else {
            _notices.tryEmit(UiEventNotice.SendStarted(peer.displayName, cleanName, segments))
        }
        return CommandResult(cmd.requestId, ok = true)
    }

    private fun handleAcceptFile(cmd: LocalCommand): CommandResult {
        val transferId = cmd.transferId ?: return CommandResult(cmd.requestId, ok = false, error = "invalid")
        val prompt = _snapshot.value.filePrompts.firstOrNull { it.transferId == transferId }
            ?: return CommandResult(cmd.requestId, ok = false, error = "already-decided")

        val isBlockedDemo = prompt.simulateMoveBlockOnFinish
        val newTx = TransferSnapshot(
            transferId = prompt.transferId,
            deviceId = prompt.deviceId,
            peerDisplayName = prompt.peerDisplayName,
            direction = TransferDirection.RECEIVE,
            fileName = prompt.fileName,
            totalSize = prompt.totalSize,
            completedSegments = if (isBlockedDemo) prompt.segmentCount else 0,
            segmentCount = prompt.segmentCount,
            moveBlocked = isBlockedDemo,
            completed = prompt.segmentCount == 0 && !isBlockedDemo
        )

        _snapshot.update { current ->
            current.copy(
                filePrompts = current.filePrompts.filterNot { it.transferId == transferId },
                transfers = listOf(newTx) + current.transfers
            )
        }
        return CommandResult(cmd.requestId, ok = true)
    }

    private fun handleRejectFile(cmd: LocalCommand): CommandResult {
        val transferId = cmd.transferId ?: return CommandResult(cmd.requestId, ok = false, error = "invalid")
        val existed = _snapshot.value.filePrompts.any { it.transferId == transferId }
        if (!existed) {
            return CommandResult(cmd.requestId, ok = false, error = "already-decided")
        }
        _snapshot.update { current ->
            current.copy(filePrompts = current.filePrompts.filterNot { it.transferId == transferId })
        }
        return CommandResult(cmd.requestId, ok = true)
    }

    private fun handleCancelTransfer(cmd: LocalCommand): CommandResult {
        val transferId = cmd.transferId ?: return CommandResult(cmd.requestId, ok = false, error = "invalid")
        var autoStartedPeer: Pair<String, TransferSnapshot>? = null
        _snapshot.update { current ->
            val cancelledTx = current.transfers.firstOrNull { it.transferId == transferId && !it.completed }
            val remainingTransfers = current.transfers.filterNot { it.transferId == transferId && !it.completed }
            var updatedQueue = current.queuedUploads
            val promoted = mutableListOf<TransferSnapshot>()

            if (cancelledTx != null && cancelledTx.direction == TransferDirection.SEND) {
                val nextInLine = updatedQueue.firstOrNull { it.deviceId == cancelledTx.deviceId }
                if (nextInLine != null) {
                    updatedQueue = updatedQueue.filterNot { it.queueId == nextInLine.queueId }
                    val peer = current.devices.firstOrNull { it.deviceId == nextInLine.deviceId }
                    val isOnline = current.wifiLanReady &&
                        current.linkServiceRunning &&
                        peer?.presence == PeerPresence.CONNECTED
                    val promotedTx = TransferSnapshot(
                        transferId = newTransferId(),
                        deviceId = nextInLine.deviceId,
                        peerDisplayName = nextInLine.peerDisplayName,
                        direction = TransferDirection.SEND,
                        fileName = nextInLine.fileName,
                        totalSize = nextInLine.totalSize,
                        completedSegments = 0,
                        segmentCount = nextInLine.segmentCount,
                        waitingReconnect = !isOnline,
                        completed = nextInLine.segmentCount == 0 && isOnline
                    )
                    promoted.add(promotedTx)
                    autoStartedPeer = nextInLine.peerDisplayName to promotedTx
                }
            }

            current.copy(
                filePrompts = current.filePrompts.filterNot { it.transferId == transferId },
                transfers = promoted + remainingTransfers,
                queuedUploads = updatedQueue
            )
        }
        autoStartedPeer?.let { (peerName, tx) ->
            if (tx.completed) {
                _notices.tryEmit(UiEventNotice.TransferCompleted(tx.fileName))
            } else {
                _notices.tryEmit(UiEventNotice.SendStarted(peerName, tx.fileName, tx.segmentCount))
            }
        }
        return CommandResult(cmd.requestId, ok = true)
    }

    /**
     * Uploads one or multiple files to a trusted peer: immediately occupies the peer's single
     * outbound slot if free, and queues additional files to dispatch sequentially upon completion.
     */
    fun enqueueOrSendFiles(deviceId: String, files: List<OutgoingFileSpec>): Boolean {
        if (files.isEmpty()) return false
        val peer = _snapshot.value.devices.firstOrNull { it.deviceId == deviceId && it.trusted }
            ?: return false

        val validFiles = mutableListOf<OutgoingFileSpec>()
        var rejectedOversize = false
        for (spec in files) {
            if (spec.totalBytes < 0L || spec.totalBytes > ProtocolLimits.MAX_FILE_BYTES) {
                rejectedOversize = true
            } else {
                validFiles.add(spec)
            }
        }
        if (rejectedOversize) {
            _notices.tryEmit(UiEventNotice.SendTooLarge)
        }
        if (validFiles.isEmpty()) return false

        val hasOutboundInFlight = _snapshot.value.transfers.any {
            it.deviceId == deviceId && it.direction == TransferDirection.SEND && !it.completed
        }

        val queueCandidates = if (!hasOutboundInFlight) {
            val first = validFiles.first()
            submitCommand(
                LocalCommand(
                    frameType = FrameTypes.SEND_FILE,
                    requestId = newTransferId(),
                    deviceId = deviceId,
                    fileName = first.fileName,
                    fileSize = first.totalBytes
                )
            )
            validFiles.drop(1)
        } else {
            validFiles
        }

        if (queueCandidates.isNotEmpty()) {
            val newQueuedItems = queueCandidates.map { spec ->
                val clean = DownloadNames.sanitize(spec.fileName)
                QueuedUploadSnapshot(
                    queueId = newTransferId(),
                    deviceId = peer.deviceId,
                    peerDisplayName = peer.displayName,
                    fileName = clean,
                    totalSize = spec.totalBytes,
                    segmentCount = computeSegmentCount(spec.totalBytes)
                )
            }
            _snapshot.update { current ->
                current.copy(queuedUploads = current.queuedUploads + newQueuedItems)
            }
            _notices.tryEmit(UiEventNotice.FilesQueued(newQueuedItems.size, peer.displayName))
        }
        return true
    }

    fun togglePauseTransfer(transferId: String) {
        var pausedFile: String? = null
        var resumedFile: String? = null
        _snapshot.update { current ->
            current.copy(
                transfers = current.transfers.map { tx ->
                    if (tx.transferId == transferId && !tx.completed && !tx.moveBlocked) {
                        val nextPaused = !tx.pausedByOperator
                        if (nextPaused) pausedFile = tx.fileName else resumedFile = tx.fileName
                        tx.copy(pausedByOperator = nextPaused)
                    } else {
                        tx
                    }
                }
            )
        }
        pausedFile?.let { _notices.tryEmit(UiEventNotice.TransferPaused(it)) }
        resumedFile?.let { _notices.tryEmit(UiEventNotice.TransferResumed(it)) }
    }

    fun removeQueuedUpload(queueId: String) {
        _snapshot.update { current ->
            current.copy(queuedUploads = current.queuedUploads.filterNot { it.queueId == queueId })
        }
    }

    fun clearQueuedUploads() {
        _snapshot.update { current ->
            current.copy(queuedUploads = emptyList())
        }
    }

    fun resendCompletedTransfer(transferId: String) {
        val completedTx = _snapshot.value.transfers.firstOrNull {
            it.transferId == transferId && it.completed && it.direction == TransferDirection.SEND
        } ?: return
        enqueueOrSendFiles(
            deviceId = completedTx.deviceId,
            files = listOf(OutgoingFileSpec(completedTx.fileName, completedTx.totalSize))
        )
    }

    fun retryBlockedCommit(transferId: String) {
        var resolvedFileName: String? = null
        _snapshot.update { current ->
            current.copy(
                transfers = current.transfers.map { tx ->
                    if (tx.transferId == transferId && tx.moveBlocked) {
                        val clean = DownloadNames.sanitize(tx.fileName)
                        val unique = DownloadNames.avoidCollision(clean) { committedDownloadNames.contains(it) }
                        committedDownloadNames.add(unique)
                        resolvedFileName = unique
                        tx.copy(
                            savedFileName = unique,
                            moveBlocked = false,
                            completed = true
                        )
                    } else {
                        tx
                    }
                }
            )
        }
        resolvedFileName?.let { _notices.tryEmit(UiEventNotice.TransferCompleted(it)) }
    }

    fun clearCompletedTransfers() {
        _snapshot.update { current ->
            current.copy(transfers = current.transfers.filterNot { it.completed })
        }
    }

    fun pushLocalPlainClipboard(text: String) {
        if (text.isEmpty()) {
            _notices.tryEmit(UiEventNotice.ClipboardEmpty)
            return
        }
        val utf8Bytes = text.toByteArray(Charsets.UTF_8)
        if (utf8Bytes.size > ProtocolLimits.MAX_CLIPBOARD_BYTES) {
            _notices.tryEmit(UiEventNotice.ClipboardTooLarge)
            return
        }
        val state = _snapshot.value
        val mutualPeers = if (state.wifiLanReady && state.linkServiceRunning) {
            state.devices.filter { it.isMutualClipboardActive }
        } else {
            emptyList()
        }
        if (mutualPeers.isEmpty()) {
            _notices.tryEmit(UiEventNotice.ClipboardNoMutualPeers)
            return
        }
        val totalSentBytes = utf8Bytes.size.toLong() * mutualPeers.size.toLong()
        _snapshot.update { current ->
            current.copy(
                telemetry = current.telemetry.withLiveDelta(uploadDelta = totalSentBytes),
                clipboardState = ClipboardSyncState(
                    lastSyncedText = text,
                    lastOriginName = current.localDisplayName,
                    sequence = current.clipboardState.sequence + 1L,
                    byteCount = utf8Bytes.size
                )
            )
        }
        _notices.tryEmit(UiEventNotice.ClipboardPushed(mutualPeers.size, utf8Bytes.size))
    }

    fun resetIdentity() {
        localSpki = DeviceIds.generateP256Spki()
        val freshId = DeviceIds.text(localSpki)
        _snapshot.update { current ->
            val canReach = current.wifiLanReady && current.linkServiceRunning
            current.copy(
                localDeviceId = freshId,
                selectedDeviceId = null,
                pairing = null,
                filePrompts = emptyList(),
                transfers = current.transfers.filter { it.completed },
                queuedUploads = emptyList(),
                devices = current.devices.map { dev ->
                    dev.copy(
                        trusted = false,
                        presence = if (canReach && dev.presence != PeerPresence.OFFLINE) {
                            PeerPresence.REACHABLE
                        } else {
                            PeerPresence.OFFLINE
                        },
                        clipboardSync = false,
                        askBeforeSave = true
                    )
                }
            )
        }
        _notices.tryEmit(UiEventNotice.IdentityResetDone)
    }

    fun setWifiLanReady(ready: Boolean) {
        if (!ready) {
            scanJob?.cancel()
        }
        _snapshot.update { current ->
            current.copy(
                wifiLanReady = ready,
                isScanningLan = if (!ready) false else current.isScanningLan,
                telemetry = current.telemetry.withLiveDelta(
                    attemptDelta = 1,
                    successDelta = if (ready) 1 else 0
                ),
                devices = current.devices.map { dev ->
                    if (!ready) {
                        dev
                    } else if (dev.trusted && dev.presence == PeerPresence.REACHABLE) {
                        dev.copy(presence = PeerPresence.CONNECTED)
                    } else {
                        dev
                    }
                }
            )
        }
        if (ready && _snapshot.value.linkServiceRunning) {
            triggerDiscoveryScan(discoverNew = false)
        }
    }

    fun setLinkServiceState(running: Boolean) {
        val wasRunning = _snapshot.value.linkServiceRunning
        if (!running) {
            scanJob?.cancel()
        }
        _snapshot.update {
            it.copy(
                linkServiceRunning = running,
                isScanningLan = if (!running) false else it.isScanningLan
            )
        }
        if (running && !wasRunning && _snapshot.value.wifiLanReady) {
            triggerDiscoveryScan(discoverNew = false)
        }
    }

    fun setRuntimePermissionsGranted(granted: Boolean) {
        _snapshot.update { it.copy(runtimePermissionsGranted = granted) }
    }

    fun updateUiPreferences(
        darkTheme: Boolean? = null,
        dynamicColor: Boolean? = null,
        tab: app.contextall.model.TopTab? = null,
        selectedDeviceId: String? = _snapshot.value.selectedDeviceId
    ) {
        _snapshot.update { current ->
            current.copy(
                darkTheme = darkTheme ?: current.darkTheme,
                dynamicColor = dynamicColor ?: current.dynamicColor,
                currentTab = tab ?: current.currentTab,
                selectedDeviceId = selectedDeviceId
            )
        }
    }

    /**
     * Connects directly to a peer by explicit IP/hostname and QUIC port,
     * either reconnecting an already-trusted endpoint or opening 6-char Base32 pairing verification.
     */
    fun connectManualEndpoint(
        rawHost: String,
        port: Int,
        optionalAlias: String = ""
    ): Boolean {
        if (!EndpointValidator.isValidHost(rawHost) || port !in 1..65535) {
            _notices.tryEmit(UiEventNotice.ManualInvalidEndpoint)
            return false
        }

        val current = _snapshot.value
        if (!current.wifiLanReady || !current.linkServiceRunning) {
            _notices.tryEmit(UiEventNotice.ManualConnectUnavailable)
            return false
        }

        val cleanHost = EndpointValidator.normalizeHost(rawHost)
        val formattedEndpoint = EndpointValidator.formatEndpoint(cleanHost, port)
        val trimmedAlias = optionalAlias.trim()
        val validAlias = trimmedAlias.takeIf { it.isNotEmpty() && DisplayNames.isLegal(it) }

        val existing = current.devices.firstOrNull {
            it.endpoint != null && it.endpoint.equals(formattedEndpoint, ignoreCase = true)
        }

        if (existing != null) {
            val resolvedName = validAlias ?: existing.displayName
            if (existing.trusted) {
                _snapshot.update { state ->
                    state.copy(
                        devices = state.devices.map { dev ->
                            if (dev.deviceId == existing.deviceId) {
                                dev.copy(
                                    displayName = resolvedName,
                                    presence = PeerPresence.CONNECTED
                                )
                            } else {
                                dev
                            }
                        }
                    )
                }
                _notices.tryEmit(UiEventNotice.ManualEndpointReconnected(resolvedName, formattedEndpoint))
                return true
            } else {
                val code = DeviceIds.shortCode(localSpki, existing.spkiBytes)
                _snapshot.update { state ->
                    state.copy(
                        devices = state.devices.map { dev ->
                            if (dev.deviceId == existing.deviceId) {
                                dev.copy(
                                    displayName = resolvedName,
                                    presence = PeerPresence.REACHABLE
                                )
                            } else {
                                dev
                            }
                        },
                        pairing = PairingPromptSnapshot(
                            deviceId = existing.deviceId,
                            displayName = resolvedName,
                            code = code,
                            remainingMilliseconds = ProtocolLimits.PROMPT_TIMEOUT_MS,
                            localAccepted = false,
                            remoteAccepted = false,
                            initiatedLocally = true
                        )
                    )
                }
                return true
            }
        }

        val spki = DeviceIds.generateP256Spki()
        val peerId = DeviceIds.text(spki)
        val fallbackName = "LAN 节点 (${cleanHost.take(44)})"
        val resolvedName = validAlias ?: fallbackName
        val code = DeviceIds.shortCode(localSpki, spki)

        val manualPeer = PeerDeviceSnapshot(
            deviceId = peerId,
            displayName = resolvedName,
            spkiBytes = spki,
            formFactor = DeviceFormFactor.DESKTOP,
            trusted = false,
            presence = PeerPresence.REACHABLE,
            endpoint = formattedEndpoint
        )

        _snapshot.update { state ->
            state.copy(
                devices = state.devices + manualPeer,
                lastDiscoveredDeviceId = peerId,
                pairing = PairingPromptSnapshot(
                    deviceId = peerId,
                    displayName = resolvedName,
                    code = code,
                    remainingMilliseconds = ProtocolLimits.PROMPT_TIMEOUT_MS,
                    localAccepted = false,
                    remoteAccepted = false,
                    initiatedLocally = true
                )
            )
        }
        scheduleClearDiscoveredHighlight(peerId)
        return true
    }

    /**
     * Runs an animated LAN QUIC beacon discovery sweep (driving CircularProgressIndicator + ripples)
     * and smoothly transitions newly discovered peers into the snapshot.
     */
    fun triggerDiscoveryScan(
        initialBootSweep: Boolean = false,
        discoverNew: Boolean = true
    ) {
        val current = _snapshot.value
        if (!current.wifiLanReady || !current.linkServiceRunning) {
            _notices.tryEmit(UiEventNotice.ManualConnectUnavailable)
            return
        }

        scanJob?.cancel()
        _snapshot.update { it.copy(isScanningLan = true) }

        scanJob = scope.launch {
            delay(if (initialBootSweep) 1350L else 1250L)
            if (initialBootSweep) {
                val hasUntrusted = _snapshot.value.devices.any { !it.trusted }
                if (!hasUntrusted) {
                    val isDevLab = _snapshot.value.debugState.seedProfile == SeedDataProfile.DEVELOPER_LAB
                    val studySpki = DeviceIds.generateP256Spki()
                    val studyPeer = PeerDeviceSnapshot(
                        deviceId = DeviceIds.text(studySpki),
                        displayName = if (isDevLab) "Studio Mini" else "书房电脑",
                        spkiBytes = studySpki,
                        formFactor = DeviceFormFactor.DESKTOP,
                        trusted = false,
                        presence = PeerPresence.REACHABLE
                    )
                    _snapshot.update { s ->
                        s.copy(
                            devices = s.devices + studyPeer,
                            lastDiscoveredDeviceId = studyPeer.deviceId
                        )
                    }
                    recordDebugFrame(
                        category = "RX",
                        frameCode = FrameTypes.DEVICE_CHANGED,
                        frameName = "DEVICE_CHANGED",
                        summary = "Discovered ${studyPeer.displayName} (${studyPeer.deviceId.take(10)}…)"
                    )
                    scheduleClearDiscoveredHighlight(studyPeer.deviceId)
                }
            } else if (discoverNew) {
                simulateDiscoverNearbyPeer()
            }
            delay(420L)
            _snapshot.update { it.copy(isScanningLan = false) }
        }
    }

    private fun scheduleClearDiscoveredHighlight(peerId: String) {
        highlightClearJob?.cancel()
        highlightClearJob = scope.launch {
            delay(4200L)
            _snapshot.update { s ->
                if (s.lastDiscoveredDeviceId == peerId) {
                    s.copy(lastDiscoveredDeviceId = null)
                } else {
                    s
                }
            }
        }
    }

    // --- Developer Debug & Functional Preview Triggers ---

    fun setDeveloperUiEnabled(enabled: Boolean, emitNotice: Boolean = true) {
        _snapshot.update { current ->
            current.copy(debugState = current.debugState.copy(developerUiEnabled = enabled))
        }
        recordDebugFrame(
            category = "DBG",
            frameCode = FrameTypes.SNAPSHOT,
            frameName = "DEBUG_VIEW",
            summary = "developerUiEnabled=$enabled"
        )
        if (emitNotice) {
            _notices.tryEmit(
                if (enabled) UiEventNotice.DebugModeUnlocked else UiEventNotice.DebugPreviewingRelease
            )
        }
    }

    fun applySeedDataProfile(profile: SeedDataProfile) {
        val current = _snapshot.value
        val existingLogs = current.debugState.frameLogs
        committedDownloadNames.clear()
        committedDownloadNames.add(
            if (profile == SeedDataProfile.DEVELOPER_LAB) "wayland-capture-04.png" else "周末旅行合影.jpg"
        )
        val rebuilt = createInitialSnapshot(
            displayName = current.localDisplayName,
            deferInitialNearbyPeer = false,
            initialDebugMode = current.debugState.developerUiEnabled,
            seedProfile = profile
        ).copy(
            darkTheme = current.darkTheme,
            dynamicColor = current.dynamicColor,
            currentTab = current.currentTab,
            runtimePermissionsGranted = current.runtimePermissionsGranted,
            wifiLanReady = current.wifiLanReady,
            linkServiceRunning = current.linkServiceRunning,
            debugState = EngineDebugState(
                developerUiEnabled = current.debugState.developerUiEnabled,
                seedProfile = profile,
                dedupedRequestCount = seenRequests.size,
                committedDownloadCount = committedDownloadNames.size,
                frameLogs = existingLogs
            )
        )
        _snapshot.value = rebuilt
        recordDebugFrame(
            category = "DBG",
            frameCode = FrameTypes.SNAPSHOT,
            frameName = "SEED_PROFILE",
            summary = "Switched dataset -> ${profile.name}"
        )
        _notices.tryEmit(UiEventNotice.DebugSeedSwitched(profile == SeedDataProfile.DEVELOPER_LAB))
    }

    fun clearDebugFrameLogs() {
        _snapshot.update { current ->
            current.copy(
                debugState = current.debugState.copy(
                    dedupedRequestCount = seenRequests.size,
                    committedDownloadCount = committedDownloadNames.size,
                    frameLogs = emptyList()
                )
            )
        }
        _notices.tryEmit(UiEventNotice.DebugLogsCleared)
    }

    fun simulateTelemetryJitterBurst() {
        val attempts = (14..22).random()
        val succeeded = (attempts - (1..4).random()).coerceAtLeast(1)
        val upBytes = (18_000_000L..64_000_000L).random()
        val downBytes = (12_000_000L..48_000_000L).random()
        val segs = computeSegmentCount(upBytes + downBytes)

        _snapshot.update { current ->
            val currentBuckets = current.telemetry.buckets
            val burstBucket = TelemetryBucketSnapshot(
                label = "压测#${(10..99).random()}",
                connectionAttempts = attempts,
                connectionSucceeded = succeeded,
                uploadBytes = upBytes,
                downloadBytes = downBytes
            )
            val nextBuckets = (currentBuckets + burstBucket).takeLast(8)
            current.copy(
                telemetry = current.telemetry.copy(
                    buckets = nextBuckets,
                    verifiedSegmentsCount = current.telemetry.verifiedSegmentsCount + segs
                )
            )
        }
        recordDebugFrame(
            category = "SIM",
            frameCode = FrameTypes.TRANSFER_PROGRESS,
            frameName = "TELEMETRY_BURST",
            summary = "ok=$succeeded/$attempts · up=${formatByteSize(upBytes)} · down=${formatByteSize(downBytes)}"
        )
    }

    fun simulateDiscoverNearbyPeer() {
        val isDevLab = _snapshot.value.debugState.seedProfile == SeedDataProfile.DEVELOPER_LAB
        val names = if (isDevLab) {
            listOf("Framework 13 Fedora", "MacBook Air M3", "NixOS Homelab", "Pixel Tablet")
        } else {
            listOf("办公笔记本", "客厅平板", "Windows 台式机", "备用手机")
        }
        val forms = if (isDevLab) {
            listOf(
                DeviceFormFactor.LAPTOP,
                DeviceFormFactor.LAPTOP,
                DeviceFormFactor.DESKTOP,
                DeviceFormFactor.PHONE
            )
        } else {
            listOf(
                DeviceFormFactor.LAPTOP,
                DeviceFormFactor.PHONE,
                DeviceFormFactor.DESKTOP,
                DeviceFormFactor.PHONE
            )
        }
        val idx = (discoveryCounter - 1) % names.size
        val suffix = if (discoveryCounter > names.size) " #${discoveryCounter}" else ""
        discoveryCounter++
        val spki = DeviceIds.generateP256Spki()
        val peerId = DeviceIds.text(spki)
        val newPeer = PeerDeviceSnapshot(
            deviceId = peerId,
            displayName = names[idx] + suffix,
            spkiBytes = spki,
            formFactor = forms[idx],
            trusted = false,
            presence = PeerPresence.REACHABLE
        )
        _snapshot.update { current ->
            current.copy(
                devices = current.devices + newPeer,
                lastDiscoveredDeviceId = peerId
            )
        }
        recordDebugFrame(
            category = "RX",
            frameCode = FrameTypes.DEVICE_CHANGED,
            frameName = "DEVICE_CHANGED",
            summary = "Beacon ${newPeer.displayName} (${peerId.take(10)}…)"
        )
        scheduleClearDiscoveredHighlight(peerId)
    }

    fun simulateInboundPairingRequest() {
        val state = _snapshot.value
        val candidate = state.devices.firstOrNull { !it.trusted && it.presence != PeerPresence.OFFLINE }
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
        recordDebugFrame(
            category = "RX",
            frameCode = FrameTypes.PAIRING_PROMPT,
            frameName = "PAIRING_PROMPT",
            summary = "Inbound from ${candidate.displayName} · code=$code · ttl=60s"
        )
    }

    fun simulateInboundFileOffer(simulateMoveBlocked: Boolean = false): Boolean {
        val state = _snapshot.value
        val sender = state.devices.firstOrNull { it.trusted && it.presence == PeerPresence.CONNECTED }
            ?: state.devices.firstOrNull { it.trusted }
            ?: return false

        // Single inbound slot per peer invariant (check both pending prompts and active receive slots)
        val inboundBusy = state.filePrompts.any { it.deviceId == sender.deviceId } ||
            state.transfers.any {
                it.deviceId == sender.deviceId &&
                    it.direction == TransferDirection.RECEIVE &&
                    !it.completed
            }
        if (inboundBusy) {
            recordDebugFrame(
                category = "ERR",
                frameCode = FrameTypes.FILE_PROMPT,
                frameName = "FILE_PROMPT_BUSY",
                summary = "Rejected inbound offer from ${sender.displayName} (slot busy)",
                ok = false
            )
            _notices.tryEmit(UiEventNotice.SendBusy(sender.displayName))
            return false
        }

        val isDevLab = state.debugState.seedProfile == SeedDataProfile.DEVELOPER_LAB
        val sampleName = when {
            simulateMoveBlocked && isDevLab -> "wayland-capture-04.png"
            simulateMoveBlocked -> "周末旅行合影.jpg"
            isDevLab -> "release-bundle-${(10..99).random()}.tar.zst"
            else -> "扫描文档_${(10..99).random()}.pdf"
        }
        val sampleSize = if (simulateMoveBlocked) 5_245_000L else 18_640_000L
        val segs = computeSegmentCount(sampleSize)
        val txId = newTransferId()

        if (sender.askBeforeSave) {
            val prompt = FilePromptSnapshot(
                transferId = txId,
                deviceId = sender.deviceId,
                peerDisplayName = sender.displayName,
                fileName = sampleName,
                totalSize = sampleSize,
                segmentCount = segs,
                remainingMilliseconds = ProtocolLimits.PROMPT_TIMEOUT_MS,
                simulateMoveBlockOnFinish = simulateMoveBlocked
            )
            _snapshot.update { current ->
                current.copy(filePrompts = listOf(prompt) + current.filePrompts)
            }
        } else {
            val autoTx = TransferSnapshot(
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
            _snapshot.update { current ->
                current.copy(transfers = listOf(autoTx) + current.transfers)
            }
            _notices.tryEmit(UiEventNotice.InboundAutoAccepted(sender.displayName, sampleName))
        }
        recordDebugFrame(
            category = "RX",
            frameCode = FrameTypes.FILE_PROMPT,
            frameName = "FILE_OFFER",
            summary = "from=${sender.displayName} · file=$sampleName · segs=$segs · moveBlocked=$simulateMoveBlocked"
        )
        return true
    }

    fun simulateRemoteClipboardDelta() {
        val sender = _snapshot.value.devices.firstOrNull { it.isMutualClipboardActive }
        if (sender == null) {
            _notices.tryEmit(UiEventNotice.ClipboardNoMutualPeers)
            return
        }
        val isDevLab = _snapshot.value.debugState.seedProfile == SeedDataProfile.DEVELOPER_LAB
        val snippets = if (isDevLab) {
            listOf(
                "ContextAll.AppImage file send ${_snapshot.value.localDeviceId.take(12)} ./report.pdf",
                "curl -fsSL https://lan.local:42461/health # QUIC ALPN contextall",
                "ssh -J workstation.lan dev@10.0.0.42"
            )
        } else {
            listOf(
                "明天上午 10:00 线上同步会链接：https://meet.example.com/team-sync",
                "收件地址：上海市徐汇区虹桥路 1 号 12 层（前台代收）",
                "周末出行清单：充电宝、相机存储卡、身份证、机票行程单"
            )
        }
        val picked = snippets[(_snapshot.value.clipboardState.sequence.toInt()) % snippets.size]
        val bytes = picked.toByteArray(Charsets.UTF_8)
        _snapshot.update { current ->
            current.copy(
                telemetry = current.telemetry.withLiveDelta(downloadDelta = bytes.size.toLong()),
                clipboardState = ClipboardSyncState(
                    lastSyncedText = picked,
                    lastOriginName = sender.displayName,
                    sequence = current.clipboardState.sequence + 1L,
                    byteCount = bytes.size
                )
            )
        }
        recordDebugFrame(
            category = "RX",
            frameCode = FrameTypes.SNAPSHOT,
            frameName = "CLIPBOARD_TEXT",
            summary = "from=${sender.displayName} · bytes=${bytes.size} · seq=${_snapshot.value.clipboardState.sequence}"
        )
        _notices.tryEmit(UiEventNotice.ClipboardReceived(sender.displayName, picked))
    }

    fun toggleTrustedPeerConnectivity(deviceId: String? = null) {
        var toggledName = ""
        var toggledState = ""
        _snapshot.update { current ->
            val targetId = deviceId
                ?: current.devices.firstOrNull { it.trusted }?.deviceId
                ?: return@update current

            var reconnected = false
            val updatedDevices = current.devices.map { dev ->
                if (dev.deviceId == targetId) {
                    val next = if (dev.presence == PeerPresence.CONNECTED) {
                        PeerPresence.OFFLINE
                    } else {
                        reconnected = true
                        PeerPresence.CONNECTED
                    }
                    toggledName = dev.displayName
                    toggledState = next.wireValue
                    dev.copy(presence = next)
                } else {
                    dev
                }
            }

            current.copy(
                devices = updatedDevices,
                telemetry = current.telemetry.withLiveDelta(
                    attemptDelta = 1,
                    successDelta = if (reconnected) 1 else 0
                )
            )
        }
        if (toggledName.isNotEmpty()) {
            recordDebugFrame(
                category = "SIM",
                frameCode = FrameTypes.DEVICE_CHANGED,
                frameName = if (toggledState == "connected") "FILE_RESUME" else "QUIC_DROP",
                summary = "peer=$toggledName -> presence=$toggledState"
            )
        }
    }

    private fun recordCommandTrace(cmd: LocalCommand, res: CommandResult) {
        val name = when (cmd.frameType) {
            FrameTypes.START_PAIRING -> "START_PAIRING"
            FrameTypes.ACCEPT_PAIRING -> "ACCEPT_PAIRING"
            FrameTypes.REJECT_PAIRING -> "REJECT_PAIRING"
            FrameTypes.UNPAIR_LOCAL -> "UNPAIR_LOCAL"
            FrameTypes.SET_DISPLAY_NAME -> "SET_DISPLAY_NAME"
            FrameTypes.SET_CLIPBOARD_SYNC -> "SET_CLIPBOARD_SYNC"
            FrameTypes.SET_ASK_BEFORE_SAVE -> "SET_ASK_BEFORE_SAVE"
            FrameTypes.SEND_FILE -> "SEND_FILE"
            FrameTypes.ACCEPT_FILE -> "ACCEPT_FILE"
            FrameTypes.REJECT_FILE -> "REJECT_FILE"
            FrameTypes.CANCEL_TRANSFER -> "CANCEL_TRANSFER"
            else -> "CMD_${cmd.frameType}"
        }
        val detail = buildString {
            append("req=${cmd.requestId.take(6)}")
            cmd.deviceId?.let { append(" peer=${it.take(8)}…") }
            cmd.fileName?.let { append(" file=$it") }
            cmd.fileSize?.let { append(" (${formatByteSize(it)})") }
            cmd.enabled?.let { append(" enabled=$it") }
            append(" -> ")
            append(if (res.ok) "ok" else "err:${res.error}")
        }
        recordDebugFrame(
            category = if (res.ok) "CMD" else "ERR",
            frameCode = cmd.frameType,
            frameName = name,
            summary = detail,
            ok = res.ok
        )
    }

    private fun recordDebugFrame(
        category: String,
        frameCode: Int,
        frameName: String,
        summary: String,
        ok: Boolean = true
    ) {
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
            val nextLogs = (listOf(entry) + current.debugState.frameLogs).take(60)
            current.copy(
                debugState = current.debugState.copy(
                    dedupedRequestCount = seenRequests.size,
                    committedDownloadCount = committedDownloadNames.size,
                    frameLogs = nextLogs
                )
            )
        }
    }

    private fun SessionTelemetrySnapshot.withLiveDelta(
        uploadDelta: Long = 0L,
        downloadDelta: Long = 0L,
        segmentsDelta: Int = 0,
        attemptDelta: Int = 0,
        successDelta: Int = 0
    ): SessionTelemetrySnapshot {
        if (uploadDelta == 0L && downloadDelta == 0L && segmentsDelta == 0 && attemptDelta == 0 && successDelta == 0) {
            return this
        }
        val updatedBuckets = if (buckets.isEmpty()) {
            listOf(
                TelemetryBucketSnapshot(
                    label = "当前",
                    connectionAttempts = attemptDelta.coerceAtLeast(1),
                    connectionSucceeded = successDelta.coerceAtLeast(0),
                    uploadBytes = uploadDelta.coerceAtLeast(0L),
                    downloadBytes = downloadDelta.coerceAtLeast(0L)
                )
            )
        } else {
            buckets.mapIndexed { idx, bucket ->
                if (idx == buckets.lastIndex) {
                    bucket.copy(
                        connectionAttempts = bucket.connectionAttempts + attemptDelta,
                        connectionSucceeded = bucket.connectionSucceeded + successDelta,
                        uploadBytes = bucket.uploadBytes + uploadDelta,
                        downloadBytes = bucket.downloadBytes + downloadDelta
                    )
                } else {
                    bucket
                }
            }
        }
        return copy(
            buckets = updatedBuckets,
            verifiedSegmentsCount = verifiedSegmentsCount + segmentsDelta
        )
    }

    private fun newTransferId(): String =
        UUID.randomUUID().toString().replace("-", "").lowercase()
}
