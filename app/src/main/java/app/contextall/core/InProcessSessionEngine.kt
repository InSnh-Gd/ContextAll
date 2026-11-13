package app.contextall.core

import app.contextall.model.ClipboardSyncState
import app.contextall.model.CommandResult
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
    autoStartTicker: Boolean = true
) {
    private var localSpki: ByteArray = DeviceIds.generateP256Spki()
    private val seenRequests = HashSet<String>()
    private val committedDownloadNames = LinkedHashSet<String>().apply {
        add("wayland-capture-04.png")
    }
    private var discoveryCounter = 1
    private var tickerJob: Job? = null

    private val _snapshot = MutableStateFlow(createInitialSnapshot(initialDisplayName))
    val snapshot: StateFlow<SessionSnapshotUi> = _snapshot.asStateFlow()

    private val _notices = MutableSharedFlow<UiEventNotice>(extraBufferCapacity = 16)
    val notices: SharedFlow<UiEventNotice> = _notices.asSharedFlow()

    init {
        if (autoStartTicker) {
            startTicker()
        }
    }

    private fun createInitialSnapshot(displayName: String): SessionSnapshotUi {
        val selfId = DeviceIds.text(localSpki)
        val archSpki = DeviceIds.generateP256Spki()
        val thinkpadSpki = DeviceIds.generateP256Spki()
        val studioSpki = DeviceIds.generateP256Spki()
        val archId = DeviceIds.text(archSpki)
        val thinkpadId = DeviceIds.text(thinkpadSpki)
        val studioId = DeviceIds.text(studioSpki)

        return SessionSnapshotUi(
            localDisplayName = displayName,
            localDeviceId = selfId,
            wifiLanReady = true,
            linkServiceRunning = true,
            devices = listOf(
                PeerDeviceSnapshot(
                    deviceId = archId,
                    displayName = "Arch Workstation",
                    spkiBytes = archSpki,
                    formFactor = DeviceFormFactor.DESKTOP,
                    trusted = true,
                    presence = PeerPresence.CONNECTED,
                    clipboardSync = true,
                    remoteClipboardOptIn = true,
                    askBeforeSave = true
                ),
                PeerDeviceSnapshot(
                    deviceId = thinkpadId,
                    displayName = "ThinkPad X1 Carbon",
                    spkiBytes = thinkpadSpki,
                    formFactor = DeviceFormFactor.LAPTOP,
                    trusted = true,
                    presence = PeerPresence.OFFLINE,
                    clipboardSync = false,
                    remoteClipboardOptIn = true,
                    askBeforeSave = true
                ),
                PeerDeviceSnapshot(
                    deviceId = studioId,
                    displayName = "Studio Mini",
                    spkiBytes = studioSpki,
                    formFactor = DeviceFormFactor.DESKTOP,
                    trusted = false,
                    presence = PeerPresence.REACHABLE
                )
            ),
            transfers = listOf(
                TransferSnapshot(
                    transferId = newTransferId(),
                    deviceId = thinkpadId,
                    peerDisplayName = "ThinkPad X1 Carbon",
                    direction = TransferDirection.RECEIVE,
                    fileName = "dataset-shard-02.parquet",
                    totalSize = 26_200_000L,
                    completedSegments = 128,
                    segmentCount = 400,
                    waitingReconnect = true
                ),
                TransferSnapshot(
                    transferId = newTransferId(),
                    deviceId = archId,
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
            clipboardState = ClipboardSyncState(
                lastSyncedText = "export WAYLAND_DISPLAY=wayland-1",
                lastOriginName = "Arch Workstation",
                sequence = 1L,
                byteCount = 32
            )
        )
    }

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

            val updatedTransfers = current.transfers.map { tx ->
                if (tx.completed || tx.moveBlocked) {
                    tx
                } else if (!canStream || tx.deviceId !in connectedPeerIds) {
                    if (tx.waitingReconnect) tx else tx.copy(waitingReconnect = true)
                } else {
                    val step = ProtocolLimits.WINDOW_SEGMENTS * 2
                    val nextDone = (tx.completedSegments + step).coerceAtMost(tx.segmentCount)
                    if (nextDone >= tx.segmentCount) {
                        val finalSavedName = if (tx.direction == TransferDirection.RECEIVE) {
                            val clean = DownloadNames.sanitize(tx.fileName)
                            val unique = DownloadNames.avoidCollision(clean) { committedDownloadNames.contains(it) }
                            committedDownloadNames.add(unique)
                            unique
                        } else {
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

            current.copy(
                pairing = updatedPairing,
                filePrompts = updatedPrompts,
                transfers = updatedTransfers
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
            return CommandResult(command.requestId, ok = false, error = "invalid")
        }
        if (!seenRequests.add(command.requestId)) {
            return CommandResult(command.requestId, ok = false, error = "duplicate-request")
        }

        return when (command.frameType) {
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
                    remoteAccepted = false
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
        if (_snapshot.value.pairing == null) {
            return CommandResult(cmd.requestId, ok = false, error = "no-active-pairing")
        }
        _snapshot.update { it.copy(pairing = null) }
        _notices.tryEmit(UiEventNotice.PairingRejected)
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
                // Cancel & remove all incomplete transfers and prompts for this peer
                filePrompts = current.filePrompts.filterNot { it.deviceId == targetId },
                transfers = current.transfers.filter { it.deviceId != targetId || it.completed },
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
        _snapshot.update { current ->
            current.copy(
                filePrompts = current.filePrompts.filterNot { it.transferId == transferId },
                transfers = current.transfers.filterNot { it.transferId == transferId && !it.completed }
            )
        }
        return CommandResult(cmd.requestId, ok = true)
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
        _snapshot.update { current ->
            current.copy(
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
        _snapshot.update { current ->
            current.copy(
                wifiLanReady = ready,
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
    }

    fun setLinkServiceState(running: Boolean) {
        _snapshot.update { it.copy(linkServiceRunning = running) }
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
                            remoteAccepted = false
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
                pairing = PairingPromptSnapshot(
                    deviceId = peerId,
                    displayName = resolvedName,
                    code = code,
                    remainingMilliseconds = ProtocolLimits.PROMPT_TIMEOUT_MS,
                    localAccepted = false,
                    remoteAccepted = false
                )
            )
        }
        return true
    }

    // --- Interactive Scenario Lab Triggers ---

    fun simulateDiscoverNearbyPeer() {
        val names = listOf("Framework 13 Fedora", "MacBook Air M3", "NixOS Homelab", "Pixel Tablet")
        val forms = listOf(
            DeviceFormFactor.LAPTOP,
            DeviceFormFactor.LAPTOP,
            DeviceFormFactor.DESKTOP,
            DeviceFormFactor.PHONE
        )
        val idx = (discoveryCounter - 1) % names.size
        val suffix = if (discoveryCounter > names.size) " #${discoveryCounter}" else ""
        discoveryCounter++
        val spki = DeviceIds.generateP256Spki()
        val newPeer = PeerDeviceSnapshot(
            deviceId = DeviceIds.text(spki),
            displayName = names[idx] + suffix,
            spkiBytes = spki,
            formFactor = forms[idx],
            trusted = false,
            presence = PeerPresence.REACHABLE
        )
        _snapshot.update { current ->
            current.copy(devices = current.devices + newPeer)
        }
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
                    remoteAccepted = true
                )
            )
        }
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
            _notices.tryEmit(UiEventNotice.SendBusy(sender.displayName))
            return false
        }

        val sampleName = if (simulateMoveBlocked) {
            "wayland-capture-04.png"
        } else {
            "release-bundle-${(10..99).random()}.tar.zst"
        }
        val sampleSize = if (simulateMoveBlocked) 5_245_000L else 28_820_000L
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
        return true
    }

    fun simulateRemoteClipboardDelta() {
        val sender = _snapshot.value.devices.firstOrNull { it.isMutualClipboardActive }
        if (sender == null) {
            _notices.tryEmit(UiEventNotice.ClipboardNoMutualPeers)
            return
        }
        val snippets = listOf(
            "ContextAll.AppImage file send ${ _snapshot.value.localDeviceId.take(12) } ./report.pdf",
            "curl -fsSL https://lan.local:42461/health # QUIC ALPN contextall",
            "ssh -J workstation.lan dev@10.0.0.42"
        )
        val picked = snippets[(_snapshot.value.clipboardState.sequence.toInt()) % snippets.size]
        val bytes = picked.toByteArray(Charsets.UTF_8)
        _snapshot.update { current ->
            current.copy(
                clipboardState = ClipboardSyncState(
                    lastSyncedText = picked,
                    lastOriginName = sender.displayName,
                    sequence = current.clipboardState.sequence + 1L,
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
                    if (dev.deviceId == targetId) {
                        val next = if (dev.presence == PeerPresence.CONNECTED) {
                            PeerPresence.OFFLINE
                        } else {
                            PeerPresence.CONNECTED
                        }
                        dev.copy(presence = next)
                    } else {
                        dev
                    }
                }
            )
        }
    }

    private fun newTransferId(): String =
        UUID.randomUUID().toString().replace("-", "").lowercase()
}
