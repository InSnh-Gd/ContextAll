package app.contextall.nativebridge

import android.content.Context
import android.net.Uri
import app.contextall.OutboundFile
import app.contextall.ShellOperator
import app.contextall.core.DisplayNames
import app.contextall.core.EndpointValidator
import app.contextall.core.FrameTypes
import app.contextall.core.ProtocolLimits
import app.contextall.model.ClipboardSyncState
import app.contextall.model.CommandResult
import app.contextall.model.LocalCommand
import app.contextall.model.QueuedUploadSnapshot
import app.contextall.model.SeedDataProfile
import app.contextall.model.SessionSnapshotUi
import app.contextall.model.TopTab
import app.contextall.model.UiEventNotice
import java.io.File
import java.io.FileOutputStream
import java.util.UUID
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.channels.BufferOverflow
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

internal data class StagedOutboundFile(
    val fileName: String,
    val stagedFile: File,
    val totalBytes: Long
)

/**
 * Live Android session coordinator backed by NativeAOT SessionHost + libmsquic.so.
 */
internal class NativeSessionController(
    private val context: Context,
    private val scope: CoroutineScope,
    initialDisplayName: String,
    initialDebugMode: Boolean,
    private val backend: NativeSessionBackend = NativeSessionJni,
    private val onApplyRemoteClipboard: (String) -> Unit = {}
) : ShellOperator {
    private val appContext = context.applicationContext
    private val discovery = LanDiscoveryController(appContext, scope, backend)
    private val noBackupDir: File = appContext.noBackupFilesDir ?: File(appContext.filesDir, "no_backup")
    private val filesDir: File = appContext.filesDir
    private val stagingDir: File = File(noBackupDir, "outbound-staging")

    private val _snapshot = MutableStateFlow(
        SessionSnapshotUi(
            localDisplayName = initialDisplayName,
            localDeviceId = "ca1initializing",
            debugState = app.contextall.model.EngineDebugState(
                developerUiEnabled = initialDebugMode,
                seedProfile = SeedDataProfile.CONSUMER,
                committedDownloadCount = 0
            )
        )
    )
    override val snapshot: StateFlow<SessionSnapshotUi> = _snapshot.asStateFlow()

    private val _notices = MutableSharedFlow<UiEventNotice>(
        extraBufferCapacity = 32,
        onBufferOverflow = BufferOverflow.DROP_OLDEST
    )
    override val notices: SharedFlow<UiEventNotice> = _notices.asSharedFlow()

    private val stagedQueuePaths = LinkedHashMap<String, StagedOutboundFile>()
    private var eventPumpJob: Job? = null
    private var started = false

    val isNativeReady: Boolean
        get() = backend.isAvailable

    init {
        scope.launch {
            discovery.wifiLanReady.collect { ready ->
                _snapshot.update { it.copy(wifiLanReady = ready) }
            }
        }
    }

    override fun start(): Boolean {
        if (started) return true
        if (!backend.isAvailable) {
            _snapshot.update {
                NativeProtocolMapper.appendDebugLog(
                    it,
                    category = "NATIVE",
                    frameCode = 0,
                    frameName = "NativeUnavailable",
                    summary = backend.loadError ?: "Native library not loaded",
                    ok = false
                )
            }
            return false
        }

        val initialName = _snapshot.value.localDisplayName
        val material = runCatching {
            NativeCryptoHelper.ensureIdentityAndBuildPfx(File(noBackupDir, "identity"), reset = false)
        }.getOrElse { err ->
            _snapshot.update {
                NativeProtocolMapper.appendDebugLog(
                    it,
                    "NATIVE",
                    0,
                    "StartFailed",
                    err.message ?: "identity-init-failed",
                    ok = false
                )
            }
            return false
        }
        val startJson = backend.start(
            noBackupDir.absolutePath,
            filesDir.absolutePath,
            initialName,
            material.pfxBytes,
            material.spkiBytes
        )
        val mapped = NativeProtocolMapper.applyStartEnvelope(_snapshot.value, startJson)
        if (mapped == null) {
            val err = MiniJson.parseObject(startJson)?.str("error") ?: "start-failed"
            _snapshot.update {
                NativeProtocolMapper.appendDebugLog(it, "NATIVE", 0, "StartFailed", err, ok = false)
            }
            return false
        }

        started = true
        _snapshot.value = NativeProtocolMapper.appendDebugLog(
            mapped.copy(linkServiceRunning = true),
            category = "NATIVE",
            frameCode = FrameTypes.SNAPSHOT,
            frameName = "SessionHostStarted",
            summary = "deviceId=${mapped.localDeviceId.take(12)}… port=42461",
            ok = true
        )

        discovery.start(mapped.localDeviceId, mapped.localDisplayName, LanDiscoveryController.QUIC_PORT)
        startEventPump()
        return true
    }

    override fun stop() {
        started = false
        eventPumpJob?.cancel()
        eventPumpJob = null
        discovery.stop()
        if (backend.isAvailable) {
            backend.stop()
        }
        _snapshot.update { it.copy(linkServiceRunning = false) }
    }

    fun setLinkServiceState(running: Boolean) {
        _snapshot.update { it.copy(linkServiceRunning = running) }
        if (running && !started) {
            start()
        } else if (!running && started) {
            stop()
        }
    }

    fun setRuntimePermissionsGranted(granted: Boolean) {
        _snapshot.update { it.copy(runtimePermissionsGranted = granted) }
        if (granted) {
            discovery.triggerRescan()
        }
    }

    fun updateUiPreferences(
        tab: TopTab? = null,
        selectedDeviceId: String? = _snapshot.value.selectedDeviceId,
        darkTheme: Boolean? = null,
        dynamicColor: Boolean? = null
    ) {
        _snapshot.update { cur ->
            cur.copy(
                currentTab = tab ?: cur.currentTab,
                selectedDeviceId = selectedDeviceId,
                darkTheme = darkTheme ?: cur.darkTheme,
                dynamicColor = dynamicColor ?: cur.dynamicColor
            )
        }
    }

    override fun submitCommand(command: LocalCommand): CommandResult = submitNative(command, null)

    fun submitNative(command: LocalCommand, filePath: String? = null): CommandResult {
        if (!started && !start()) {
            return CommandResult(command.requestId, false, "native-unavailable")
        }

        if (command.frameType == FrameTypes.SET_DISPLAY_NAME) {
            val candidate = command.displayName.orEmpty().trim()
            if (!DisplayNames.isLegal(candidate)) {
                emitNotice(UiEventNotice.InvalidDisplayName)
                return CommandResult(command.requestId, false, "invalid-name")
            }
        }

        val json = NativeProtocolMapper.encodeLocalCommand(command, filePath)
        val rawResult = backend.submitCommandJson(command.frameType, json)
        val result = NativeProtocolMapper.parseCommandResult(rawResult, command.requestId)

        _snapshot.update { cur ->
            NativeProtocolMapper.appendDebugLog(
                cur,
                category = "CMD",
                frameCode = command.frameType,
                frameName = "Command(${command.frameType})",
                summary = if (result.ok) "ok req=${result.requestId.take(6)}" else "err=${result.error}",
                ok = result.ok
            )
        }

        if (result.ok) {
            if (command.frameType == FrameTypes.SET_DISPLAY_NAME && !command.displayName.isNullOrBlank()) {
                emitNotice(UiEventNotice.DisplayNameSaved(command.displayName.trim()))
            }
            refreshSnapshotFromNative()
        } else if (result.error == "too-large") {
            emitNotice(UiEventNotice.SendTooLarge)
        } else if (result.error == "busy") {
            val peerName = _snapshot.value.devices.firstOrNull { it.deviceId == command.deviceId }?.displayName ?: "对方设备"
            emitNotice(UiEventNotice.SendBusy(peerName))
        }

        return result
    }

    override fun sendFiles(deviceId: String, files: List<OutboundFile>) {
        val uris = files.filterIsInstance<OutboundFile.UriRef>().map { it.uri }
        if (uris.isNotEmpty()) {
            sendUriList(deviceId, uris)
        }
        files.filterIsInstance<OutboundFile.Named>().forEach { named ->
            val bytes = named.content
            if (bytes != null) {
                sendTextAsStagedFile(deviceId, named.fileName, String(bytes, Charsets.UTF_8))
            } else {
                sendPresetSampleFile(deviceId, named.fileName, named.totalBytes)
            }
        }
    }

    override fun connectManual(host: String, port: Int, alias: String): Boolean =
        connectManualEndpoint(host, port, alias)

    override fun pushClipboard(text: String) = pushLocalPlainClipboard(text)

    override fun scanLan() = triggerDiscoveryScan()

    fun sendUriList(deviceId: String, uris: List<Uri>) {
        if (uris.isEmpty()) return
        scope.launch(Dispatchers.IO) {
            val staged = uris.mapNotNull { stageContentUri(it) }
            if (staged.isNotEmpty()) {
                enqueueOrSendStaged(deviceId, staged)
            }
        }
    }

    fun sendTextAsStagedFile(deviceId: String, fileName: String, content: String) {
        scope.launch(Dispatchers.IO) {
            val staged = stageBytesFile(fileName, content.toByteArray(Charsets.UTF_8)) ?: return@launch
            enqueueOrSendStaged(deviceId, listOf(staged))
        }
    }

    fun sendPresetSampleFile(deviceId: String, fileName: String, targetSizeBytes: Long) {
        scope.launch(Dispatchers.IO) {
            val boundedSize = targetSizeBytes.coerceIn(1L, 8L * 1024L * 1024L).toInt()
            val payload = ByteArray(boundedSize) { idx -> ((idx * 31 + 17) and 0x7F).toByte() }
            val staged = stageBytesFile(fileName, payload) ?: return@launch
            enqueueOrSendStaged(deviceId, listOf(staged))
        }
    }

    fun connectManualEndpoint(rawHost: String, port: Int, optionalAlias: String): Boolean {
        val cleanHost = EndpointValidator.normalizeHost(rawHost)
        if (!EndpointValidator.isValidHost(cleanHost) || port !in 1..65535) {
            emitNotice(UiEventNotice.ManualInvalidEndpoint)
            return false
        }

        if (!started && !start()) {
            emitNotice(UiEventNotice.ManualConnectUnavailable)
            return false
        }

        scope.launch(Dispatchers.IO) {
            val alias = optionalAlias.trim().ifEmpty { null }
            val resultJson = backend.connectEndpoint(cleanHost, port, alias)
            val obj = MiniJson.parseObject(resultJson)
            val ok = obj?.bool("ok") == true
            val peerId = obj?.str("deviceId")
            val err = obj?.str("error")

            _snapshot.update { cur ->
                NativeProtocolMapper.appendDebugLog(
                    cur,
                    category = "QUIC",
                    frameCode = FrameTypes.DEVICE_CHANGED,
                    frameName = "ManualConnect",
                    summary = if (ok) "$cleanHost:$port -> ${peerId?.take(10)}" else "$cleanHost:$port failed ($err)",
                    ok = ok
                )
            }

            if (ok && peerId != null) {
                refreshSnapshotFromNative()
                val peerName = alias ?: _snapshot.value.devices.firstOrNull { it.deviceId == peerId }?.displayName ?: peerId
                emitNotice(UiEventNotice.ManualEndpointReconnected(peerName, "$cleanHost:$port"))
            } else {
                emitNotice(UiEventNotice.ManualConnectUnavailable)
            }
        }
        return true
    }

    fun pushLocalPlainClipboard(text: String) {
        if (text.isEmpty()) {
            emitNotice(UiEventNotice.ClipboardEmpty)
            return
        }
        val bytes = text.toByteArray(Charsets.UTF_8)
        if (bytes.size > ProtocolLimits.MAX_CLIPBOARD_BYTES) {
            emitNotice(UiEventNotice.ClipboardTooLarge)
            return
        }

        if (!started && !start()) return
        backend.pushClipboardText(text)
        val activePeers = _snapshot.value.devices.count { it.isMutualClipboardActive }
        _snapshot.update { cur ->
            cur.copy(
                clipboardState = ClipboardSyncState(
                    lastSyncedText = text,
                    lastOriginName = cur.localDisplayName,
                    sequence = cur.clipboardState.sequence + 1,
                    byteCount = bytes.size
                )
            )
        }
        if (activePeers > 0) {
            emitNotice(UiEventNotice.ClipboardPushed(activePeers, bytes.size))
        } else {
            emitNotice(UiEventNotice.ClipboardNoMutualPeers)
        }
    }

    fun triggerDiscoveryScan() {
        _snapshot.update { it.copy(isScanningLan = true) }
        discovery.triggerRescan()
        refreshSnapshotFromNative()
        scope.launch {
            delay(850L)
            _snapshot.update { it.copy(isScanningLan = false) }
        }
    }

    override fun resetIdentity() {
        if (!backend.isAvailable) return
        scope.launch(Dispatchers.IO) {
            discovery.stop()
            val material = runCatching {
                NativeCryptoHelper.ensureIdentityAndBuildPfx(File(noBackupDir, "identity"), reset = true)
            }.getOrElse { err ->
                _snapshot.update {
                    NativeProtocolMapper.appendDebugLog(
                        it,
                        "NATIVE",
                        0,
                        "ResetIdentityFailed",
                        err.message ?: "identity-reset-failed",
                        ok = false
                    )
                }
                return@launch
            }
            val resetJson = backend.resetIdentity(
                noBackupDir.absolutePath,
                filesDir.absolutePath,
                _snapshot.value.localDisplayName,
                material.pfxBytes,
                material.spkiBytes
            )
            NativeProtocolMapper.applyStartEnvelope(_snapshot.value, resetJson)?.let { next ->
                started = true
                _snapshot.value = NativeProtocolMapper.appendDebugLog(
                    next.copy(linkServiceRunning = true),
                    category = "NATIVE",
                    frameCode = FrameTypes.SNAPSHOT,
                    frameName = "IdentityReset",
                    summary = "deviceId=${next.localDeviceId.take(12)}…",
                    ok = true
                )
                discovery.start(next.localDeviceId, next.localDisplayName, LanDiscoveryController.QUIC_PORT)
                if (eventPumpJob?.isActive != true) {
                    startEventPump()
                }
                emitNotice(UiEventNotice.IdentityResetDone)
            }
        }
    }

    override fun removeQueuedUpload(queueId: String) {
        stagedQueuePaths.remove(queueId)
        _snapshot.update { cur ->
            cur.copy(queuedUploads = cur.queuedUploads.filterNot { it.queueId == queueId })
        }
    }

    override fun clearQueuedUploads() {
        stagedQueuePaths.clear()
        _snapshot.update { it.copy(queuedUploads = emptyList()) }
    }

    override fun clearCompletedTransfers() {
        _snapshot.update { cur ->
            cur.copy(transfers = cur.transfers.filterNot { it.completed })
        }
    }

    override fun clearDebugFrameLogs() {
        _snapshot.update { cur ->
            cur.copy(debugState = cur.debugState.copy(frameLogs = emptyList()))
        }
    }

    override fun setDeveloperUiEnabled(enabled: Boolean) {
        _snapshot.update { cur ->
            cur.copy(debugState = cur.debugState.copy(developerUiEnabled = enabled))
        }
        emitNotice(if (enabled) UiEventNotice.DebugModeUnlocked else UiEventNotice.DebugPreviewingRelease)
    }

    private fun startEventPump() {
        eventPumpJob?.cancel()
        eventPumpJob = scope.launch(Dispatchers.IO) {
            while (isActive && started) {
                val eventJson = backend.pollEventJson(350) ?: continue
                handleNativeEvent(eventJson)
            }
        }
    }

    internal fun handleNativeEvent(eventJson: String) {
        val root = MiniJson.parseObject(eventJson) ?: return
        when (root.str("kind")) {
            "frames" -> {
                val frames = root.objList("frames")
                for (frameObj in frames) {
                    val frameType = frameObj.int("type") ?: continue
                    val payload = frameObj.obj("payload") ?: continue
                    var emittedNotices: List<UiEventNotice> = emptyList()
                    _snapshot.update { cur ->
                        val mapped = NativeProtocolMapper.applyPublishedFrame(cur, frameType, payload)
                        emittedNotices = mapped.notices
                        mapped.snapshot
                    }
                    emittedNotices.forEach { emitNotice(it) }
                    if (frameType == FrameTypes.TRANSFER_ENDED) {
                        drainNextQueuedUpload()
                    }
                }
            }
            "clipboard" -> {
                val text = root.str("text") ?: return
                onApplyRemoteClipboard(text)
                val firstMutual = _snapshot.value.devices.firstOrNull { it.isMutualClipboardActive }
                _snapshot.update { cur ->
                    cur.copy(
                        clipboardState = ClipboardSyncState(
                            lastSyncedText = text,
                            lastOriginName = firstMutual?.displayName ?: "远端设备",
                            sequence = cur.clipboardState.sequence + 1,
                            byteCount = text.toByteArray(Charsets.UTF_8).size
                        )
                    )
                }
                emitNotice(UiEventNotice.ClipboardReceived(firstMutual?.displayName ?: "远端设备", text))
            }
            "commitInbound" -> {
                val commitId = root.long("commitId") ?: return
                val tempPath = root.str("tempFilePath") ?: ""
                val fileName = root.str("fileName") ?: "download.bin"
                val rootHashHex = root.str("rootHashHex") ?: ""
                val downloadDir = root.str("downloadDirectory") ?: noBackupDir.absolutePath
                val (ok, target) = MediaStoreTransferSink.commitInbound(
                    appContext,
                    tempPath,
                    fileName,
                    rootHashHex,
                    downloadDir
                )
                backend.completeInboundCommit(commitId, ok, target)
            }
            "discoveryAnnounce" -> {
                val deviceId = root.str("deviceId") ?: return
                val displayName = root.str("displayName") ?: _snapshot.value.localDisplayName
                val port = root.int("port") ?: LanDiscoveryController.QUIC_PORT
                discovery.updateAdvertisement(deviceId, displayName, port)
            }
        }
    }

    private fun enqueueOrSendStaged(deviceId: String, files: List<StagedOutboundFile>) {
        val peer = _snapshot.value.devices.firstOrNull { it.deviceId == deviceId } ?: return
        val hasActive = _snapshot.value.transfers.any { it.deviceId == deviceId && !it.completed }
        var startIndex = 0

        if (!hasActive) {
            val first = files.first()
            val reqId = nextRequestId()
            val res = submitNative(
                LocalCommand(
                    frameType = FrameTypes.SEND_FILE,
                    requestId = reqId,
                    deviceId = deviceId,
                    fileName = first.fileName,
                    fileSize = first.totalBytes
                ),
                filePath = first.stagedFile.absolutePath
            )
            if (res.ok) {
                val segs = ((first.totalBytes + ProtocolLimits.SEGMENT_SIZE - 1L) / ProtocolLimits.SEGMENT_SIZE).toInt().coerceAtLeast(1)
                emitNotice(UiEventNotice.SendStarted(peer.displayName, first.fileName, segs))
                updateUiPreferences(tab = TopTab.TRANSFERS, selectedDeviceId = null)
                startIndex = 1
            }
        }

        if (startIndex < files.size) {
            val additions = files.drop(startIndex).map { staged ->
                val queueId = UUID.randomUUID().toString()
                stagedQueuePaths[queueId] = staged
                val segs = ((staged.totalBytes + ProtocolLimits.SEGMENT_SIZE - 1L) / ProtocolLimits.SEGMENT_SIZE).toInt().coerceAtLeast(1)
                QueuedUploadSnapshot(
                    queueId = queueId,
                    deviceId = deviceId,
                    peerDisplayName = peer.displayName,
                    fileName = staged.fileName,
                    totalSize = staged.totalBytes,
                    segmentCount = segs
                )
            }
            _snapshot.update { cur -> cur.copy(queuedUploads = cur.queuedUploads + additions) }
            emitNotice(UiEventNotice.FilesQueued(additions.size, peer.displayName))
        }
    }

    private fun drainNextQueuedUpload() {
        val cur = _snapshot.value
        val next = cur.queuedUploads.firstOrNull { queued ->
            cur.transfers.none { it.deviceId == queued.deviceId && !it.completed }
        } ?: return

        val staged = stagedQueuePaths.remove(next.queueId)
        _snapshot.update { it.copy(queuedUploads = it.queuedUploads.filterNot { q -> q.queueId == next.queueId }) }
        if (staged == null || !staged.stagedFile.isFile) return

        val res = submitNative(
            LocalCommand(
                frameType = FrameTypes.SEND_FILE,
                requestId = nextRequestId(),
                deviceId = next.deviceId,
                fileName = staged.fileName,
                fileSize = staged.totalBytes
            ),
            filePath = staged.stagedFile.absolutePath
        )
        if (res.ok) {
            emitNotice(UiEventNotice.SendStarted(next.peerDisplayName, next.fileName, next.segmentCount))
        }
    }

    private fun stageContentUri(uri: Uri): StagedOutboundFile? {
        return try {
            val resolver = appContext.contentResolver
            var displayName = uri.lastPathSegment?.substringAfterLast('/') ?: "file.bin"
            resolver.query(uri, arrayOf(android.provider.OpenableColumns.DISPLAY_NAME), null, null, null)?.use { c ->
                val idx = c.getColumnIndex(android.provider.OpenableColumns.DISPLAY_NAME)
                if (idx >= 0 && c.moveToFirst() && !c.isNull(idx)) {
                    displayName = c.getString(idx)
                }
            }
            stagingDir.mkdirs()
            val safeName = app.contextall.core.DownloadNames.sanitize(displayName)
            val staged = File(stagingDir, "${UUID.randomUUID().toString().take(8)}-$safeName")
            resolver.openInputStream(uri)?.use { input ->
                FileOutputStream(staged).use { output ->
                    input.copyTo(output)
                }
            } ?: return null
            StagedOutboundFile(safeName, staged, staged.length())
        } catch (_: Exception) {
            null
        }
    }

    private fun stageBytesFile(fileName: String, bytes: ByteArray): StagedOutboundFile? {
        return try {
            stagingDir.mkdirs()
            val safeName = app.contextall.core.DownloadNames.sanitize(fileName)
            val staged = File(stagingDir, "${UUID.randomUUID().toString().take(8)}-$safeName")
            staged.writeBytes(bytes)
            StagedOutboundFile(safeName, staged, staged.length())
        } catch (_: Exception) {
            null
        }
    }

    private fun refreshSnapshotFromNative() {
        val json = backend.getSnapshotJson() ?: return
        NativeProtocolMapper.applySnapshotEnvelope(_snapshot.value, json)?.let { next ->
            _snapshot.value = next
        }
    }

    private fun emitNotice(notice: UiEventNotice) {
        _notices.tryEmit(notice)
    }

    private fun nextRequestId(): String =
        UUID.randomUUID().toString().replace("-", "").lowercase()
}
