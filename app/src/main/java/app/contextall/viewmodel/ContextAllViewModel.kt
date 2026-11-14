package app.contextall.viewmodel

import android.app.Application
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.net.Uri
import android.os.Build
import android.provider.OpenableColumns
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import app.contextall.BuildConfig
import app.contextall.core.EndpointValidator
import app.contextall.core.FrameTypes
import app.contextall.core.InProcessSessionEngine
import app.contextall.model.LocalCommand
import app.contextall.model.OutgoingFileSpec
import app.contextall.model.SeedDataProfile
import app.contextall.model.SessionSnapshotUi
import app.contextall.model.TopTab
import app.contextall.model.UiEventNotice
import app.contextall.nativebridge.NativeSessionController
import app.contextall.service.LinkService
import java.util.UUID
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.channels.BufferOverflow
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.flatMapLatest
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch

@OptIn(ExperimentalCoroutinesApi::class)
class ContextAllViewModel(application: Application) : AndroidViewModel(application) {

    private val uiPrefs = application.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
    private val _showOnboardingGuide = MutableStateFlow(
        !uiPrefs.getBoolean(KEY_HAS_SEEN_ONBOARDING, false)
    )
    val showOnboardingGuide: StateFlow<Boolean> = _showOnboardingGuide.asStateFlow()

    @Volatile
    private var lastAppliedRemoteClipboard: String? = null

    private val nativeController = NativeSessionController(
        context = application,
        scope = viewModelScope,
        initialDisplayName = initialDeviceDisplayName(),
        initialDebugMode = BuildConfig.DEBUG,
        onApplyRemoteClipboard = { text ->
            viewModelScope.launch(Dispatchers.Main.immediate) {
                lastAppliedRemoteClipboard = text
                copyTextToSystemClipboard("ContextAll Remote", text)
            }
        }
    )

    private val labEngine = InProcessSessionEngine(
        scope = viewModelScope,
        initialDisplayName = initialDeviceDisplayName(),
        initialDebugMode = BuildConfig.DEBUG
    )

    private val useLabEngine = MutableStateFlow(!nativeController.isNativeReady)

    val state: StateFlow<SessionSnapshotUi> = useLabEngine
        .flatMapLatest { useLab ->
            if (useLab) labEngine.snapshot else nativeController.snapshot
        }
        .stateIn(
            scope = viewModelScope,
            started = SharingStarted.Eagerly,
            initialValue = if (useLabEngine.value) labEngine.snapshot.value else nativeController.snapshot.value
        )

    private val _notices = MutableSharedFlow<UiEventNotice>(
        extraBufferCapacity = 32,
        onBufferOverflow = BufferOverflow.DROP_OLDEST
    )
    val notices: SharedFlow<UiEventNotice> = _notices.asSharedFlow()

    private val clipboardManager = application.getSystemService(ClipboardManager::class.java)
    private val clipChangedListener = ClipboardManager.OnPrimaryClipChangedListener {
        val clip = clipboardManager?.primaryClip ?: return@OnPrimaryClipChangedListener
        if (clip.itemCount <= 0) return@OnPrimaryClipChangedListener
        val text = clip.getItemAt(0).text?.toString().orEmpty()
        if (text.isEmpty() || text == lastAppliedRemoteClipboard) {
            lastAppliedRemoteClipboard = null
            return@OnPrimaryClipChangedListener
        }
        if (!useLabEngine.value) {
            nativeController.pushLocalPlainClipboard(text)
        }
    }

    init {
        viewModelScope.launch {
            nativeController.notices.collect { notice ->
                if (!useLabEngine.value) {
                    _notices.tryEmit(notice)
                }
            }
        }
        viewModelScope.launch {
            labEngine.notices.collect { notice ->
                if (useLabEngine.value) {
                    _notices.tryEmit(notice)
                }
            }
        }
        clipboardManager?.addPrimaryClipChangedListener(clipChangedListener)
        LinkService.start(application)
        viewModelScope.launch {
            LinkService.isRunning.collect { running ->
                if (useLabEngine.value) {
                    labEngine.setLinkServiceState(running)
                } else {
                    nativeController.setLinkServiceState(running)
                }
            }
        }
    }

    override fun onCleared() {
        clipboardManager?.removePrimaryClipChangedListener(clipChangedListener)
        nativeController.stop()
        super.onCleared()
    }

    fun selectTab(tab: TopTab) {
        val nextSelected = if (tab != TopTab.DEVICES) null else state.value.selectedDeviceId
        if (useLabEngine.value) {
            labEngine.updateUiPreferences(tab = tab, selectedDeviceId = nextSelected)
        } else {
            nativeController.updateUiPreferences(tab = tab, selectedDeviceId = nextSelected)
        }
    }

    fun selectTrustedDevice(deviceId: String?) {
        if (useLabEngine.value) {
            labEngine.updateUiPreferences(selectedDeviceId = deviceId)
        } else {
            nativeController.updateUiPreferences(selectedDeviceId = deviceId)
        }
    }

    fun setDarkTheme(enabled: Boolean) {
        if (useLabEngine.value) {
            labEngine.updateUiPreferences(darkTheme = enabled)
        } else {
            nativeController.updateUiPreferences(darkTheme = enabled)
        }
    }

    fun setDynamicColor(enabled: Boolean) {
        if (useLabEngine.value) {
            labEngine.updateUiPreferences(dynamicColor = enabled)
        } else {
            nativeController.updateUiPreferences(dynamicColor = enabled)
        }
    }

    fun toggleLanWifiState() {
        if (useLabEngine.value) {
            labEngine.setWifiLanReady(!state.value.wifiLanReady)
        } else {
            nativeController.triggerDiscoveryScan()
        }
    }

    fun toggleLinkService() {
        val app = getApplication<Application>()
        if (state.value.linkServiceRunning) {
            LinkService.stop(app)
        } else {
            LinkService.start(app)
        }
    }

    fun onPermissionsResult(granted: Boolean) {
        if (useLabEngine.value) {
            labEngine.setRuntimePermissionsGranted(granted)
        } else {
            nativeController.setRuntimePermissionsGranted(granted)
        }
    }

    private fun submitBridgeCommand(command: LocalCommand) {
        if (useLabEngine.value) {
            labEngine.submitCommand(command)
        } else {
            nativeController.submitCommand(command)
        }
    }

    fun startPairing(deviceId: String) {
        submitBridgeCommand(
            LocalCommand(
                frameType = FrameTypes.START_PAIRING,
                requestId = nextRequestId(),
                deviceId = deviceId
            )
        )
    }

    fun connectManualPeer(host: String, portText: String, alias: String = ""): Boolean {
        val port = EndpointValidator.parseValidPort(portText) ?: -1
        return if (useLabEngine.value) {
            labEngine.connectManualEndpoint(rawHost = host, port = port, optionalAlias = alias)
        } else {
            nativeController.connectManualEndpoint(rawHost = host, port = port, optionalAlias = alias)
        }
    }

    fun acceptPairing() {
        submitBridgeCommand(
            LocalCommand(
                frameType = FrameTypes.ACCEPT_PAIRING,
                requestId = nextRequestId()
            )
        )
    }

    fun rejectPairing() {
        submitBridgeCommand(
            LocalCommand(
                frameType = FrameTypes.REJECT_PAIRING,
                requestId = nextRequestId()
            )
        )
    }

    fun setClipboardSync(deviceId: String, enabled: Boolean) {
        submitBridgeCommand(
            LocalCommand(
                frameType = FrameTypes.SET_CLIPBOARD_SYNC,
                requestId = nextRequestId(),
                deviceId = deviceId,
                enabled = enabled
            )
        )
    }

    fun setAskBeforeSave(deviceId: String, enabled: Boolean) {
        submitBridgeCommand(
            LocalCommand(
                frameType = FrameTypes.SET_ASK_BEFORE_SAVE,
                requestId = nextRequestId(),
                deviceId = deviceId,
                enabled = enabled
            )
        )
    }

    fun unpairDevice(deviceId: String) {
        submitBridgeCommand(
            LocalCommand(
                frameType = FrameTypes.UNPAIR_LOCAL,
                requestId = nextRequestId(),
                deviceId = deviceId
            )
        )
    }

    fun sendPickedDocument(deviceId: String, uri: Uri) {
        if (useLabEngine.value) {
            val spec = resolveUriSpec(uri)
            sendFilePayload(deviceId = deviceId, fileName = spec.fileName, totalBytes = spec.totalBytes)
        } else {
            nativeController.sendUriList(deviceId = deviceId, uris = listOf(uri))
        }
    }

    fun uploadPickedUris(deviceId: String, uris: List<Uri>) {
        if (uris.isEmpty()) return
        if (useLabEngine.value) {
            val specs = uris.map { resolveUriSpec(it) }
            labEngine.enqueueOrSendFiles(deviceId = deviceId, files = specs)
        } else {
            nativeController.sendUriList(deviceId = deviceId, uris = uris)
        }
    }

    fun uploadTextAsFile(deviceId: String, fileName: String, content: String) {
        if (useLabEngine.value) {
            val totalBytes = content.toByteArray(Charsets.UTF_8).size.toLong()
            labEngine.enqueueOrSendFiles(
                deviceId = deviceId,
                files = listOf(OutgoingFileSpec(fileName = fileName, totalBytes = totalBytes))
            )
        } else {
            nativeController.sendTextAsStagedFile(deviceId = deviceId, fileName = fileName, content = content)
        }
    }

    fun uploadPresetFromTransfers(deviceId: String, fileName: String, totalBytes: Long) {
        if (useLabEngine.value) {
            labEngine.enqueueOrSendFiles(
                deviceId = deviceId,
                files = listOf(OutgoingFileSpec(fileName = fileName, totalBytes = totalBytes))
            )
        } else {
            nativeController.sendPresetSampleFile(deviceId = deviceId, fileName = fileName, targetSizeBytes = totalBytes)
        }
    }

    private fun resolveUriSpec(uri: Uri): OutgoingFileSpec {
        val resolver = getApplication<Application>().contentResolver
        var resolvedName = uri.lastPathSegment ?: "document.bin"
        var resolvedSize = 0L

        try {
            resolver.query(uri, arrayOf(OpenableColumns.DISPLAY_NAME, OpenableColumns.SIZE), null, null, null)
                ?.use { cursor ->
                    if (cursor.moveToFirst()) {
                        val nameIdx = cursor.getColumnIndex(OpenableColumns.DISPLAY_NAME)
                        val sizeIdx = cursor.getColumnIndex(OpenableColumns.SIZE)
                        if (nameIdx >= 0 && !cursor.isNull(nameIdx)) {
                            resolvedName = cursor.getString(nameIdx)
                        }
                        if (sizeIdx >= 0 && !cursor.isNull(sizeIdx)) {
                            resolvedSize = cursor.getLong(sizeIdx)
                        }
                    }
                }
        } catch (_: Exception) {
            resolvedSize = 1_048_576L
        }

        return OutgoingFileSpec(fileName = resolvedName, totalBytes = resolvedSize)
    }

    fun sendFilePayload(deviceId: String, fileName: String, totalBytes: Long) {
        val cmd = LocalCommand(
            frameType = FrameTypes.SEND_FILE,
            requestId = nextRequestId(),
            deviceId = deviceId,
            fileName = fileName,
            fileSize = totalBytes
        )
        if (useLabEngine.value) {
            val result = labEngine.submitCommand(cmd)
            if (result.ok) {
                labEngine.updateUiPreferences(tab = TopTab.TRANSFERS, selectedDeviceId = null)
            }
        } else {
            val result = nativeController.submitCommand(cmd)
            if (result.ok) {
                nativeController.updateUiPreferences(tab = TopTab.TRANSFERS, selectedDeviceId = null)
            }
        }
    }

    fun acceptIncomingFile(transferId: String) {
        submitBridgeCommand(
            LocalCommand(
                frameType = FrameTypes.ACCEPT_FILE,
                requestId = nextRequestId(),
                transferId = transferId
            )
        )
    }

    fun rejectIncomingFile(transferId: String) {
        submitBridgeCommand(
            LocalCommand(
                frameType = FrameTypes.REJECT_FILE,
                requestId = nextRequestId(),
                transferId = transferId
            )
        )
    }

    fun togglePauseTransfer(transferId: String) {
        if (useLabEngine.value) {
            labEngine.togglePauseTransfer(transferId)
        }
    }

    fun cancelTransfer(transferId: String) {
        submitBridgeCommand(
            LocalCommand(
                frameType = FrameTypes.CANCEL_TRANSFER,
                requestId = nextRequestId(),
                transferId = transferId
            )
        )
    }

    fun retryBlockedCommit(transferId: String) {
        if (useLabEngine.value) {
            labEngine.retryBlockedCommit(transferId)
        }
    }

    fun removeQueuedUpload(queueId: String) {
        if (useLabEngine.value) {
            labEngine.removeQueuedUpload(queueId)
        } else {
            nativeController.removeQueuedUpload(queueId)
        }
    }

    fun clearQueuedUploads() {
        if (useLabEngine.value) {
            labEngine.clearQueuedUploads()
        } else {
            nativeController.clearQueuedUploads()
        }
    }

    fun resendCompletedTransfer(transferId: String) {
        if (useLabEngine.value) {
            labEngine.resendCompletedTransfer(transferId)
        }
    }

    fun clearCompletedTransfers() {
        if (useLabEngine.value) {
            labEngine.clearCompletedTransfers()
        } else {
            nativeController.clearCompletedTransfers()
        }
    }

    fun updateLocalDisplayName(newName: String) {
        submitBridgeCommand(
            LocalCommand(
                frameType = FrameTypes.SET_DISPLAY_NAME,
                requestId = nextRequestId(),
                displayName = newName
            )
        )
    }

    fun pushSystemClipboard() {
        val clip = clipboardManager?.primaryClip
        val text = if (clip != null && clip.itemCount > 0) {
            clip.getItemAt(0).text?.toString().orEmpty()
        } else {
            ""
        }
        if (useLabEngine.value) {
            labEngine.pushLocalPlainClipboard(text)
        } else {
            nativeController.pushLocalPlainClipboard(text)
        }
    }

    fun pushCustomClipboardText(text: String) {
        lastAppliedRemoteClipboard = text
        clipboardManager?.setPrimaryClip(ClipData.newPlainText("ContextAll", text))
        if (useLabEngine.value) {
            labEngine.pushLocalPlainClipboard(text)
        } else {
            nativeController.pushLocalPlainClipboard(text)
        }
    }

    fun copyTextToSystemClipboard(label: String, value: String) {
        clipboardManager?.setPrimaryClip(ClipData.newPlainText(label, value))
    }

    fun resetIdentity() {
        if (useLabEngine.value) {
            labEngine.resetIdentity()
        } else {
            nativeController.resetIdentity()
        }
    }

    fun triggerLanScan() {
        if (useLabEngine.value) {
            labEngine.triggerDiscoveryScan(initialBootSweep = false, discoverNew = true)
        } else {
            nativeController.triggerDiscoveryScan()
        }
    }

    // --- Developer Debug & Scenario Lab Actions ---

    fun setDeveloperUiEnabled(enabled: Boolean) {
        if (useLabEngine.value) {
            labEngine.setDeveloperUiEnabled(enabled, emitNotice = true)
        } else {
            nativeController.setDeveloperUiEnabled(enabled)
        }
    }

    fun switchSeedProfile(profile: SeedDataProfile) {
        when (profile) {
            SeedDataProfile.DEVELOPER_LAB -> {
                nativeController.stop()
                useLabEngine.value = true
                labEngine.applySeedDataProfile(SeedDataProfile.DEVELOPER_LAB)
                labEngine.setLinkServiceState(LinkService.isRunning.value)
            }
            SeedDataProfile.CONSUMER -> {
                if (nativeController.isNativeReady) {
                    useLabEngine.value = false
                    if (LinkService.isRunning.value) {
                        nativeController.start()
                    }
                    _notices.tryEmit(UiEventNotice.DebugSeedSwitched(isDeveloperProfile = false))
                } else {
                    useLabEngine.value = true
                    labEngine.applySeedDataProfile(SeedDataProfile.CONSUMER)
                }
            }
        }
    }

    fun labDiscoverPeer() = labEngine.triggerDiscoveryScan(initialBootSweep = false, discoverNew = true)

    fun labInboundPairing() = labEngine.simulateInboundPairingRequest()

    fun labInboundFile(moveBlocked: Boolean = false) {
        if (labEngine.simulateInboundFileOffer(simulateMoveBlocked = moveBlocked)) {
            labEngine.updateUiPreferences(tab = TopTab.TRANSFERS, selectedDeviceId = null)
        }
    }

    fun labRemoteClipboard() {
        labEngine.simulateRemoteClipboardDelta()
        val latest = labEngine.snapshot.value.clipboardState.lastSyncedText
        if (latest.isNotEmpty()) {
            lastAppliedRemoteClipboard = latest
            copyTextToSystemClipboard("ContextAll Remote", latest)
        }
    }

    fun labTogglePeerConnection(deviceId: String? = null) {
        labEngine.toggleTrustedPeerConnectivity(deviceId)
    }

    fun labTelemetryBurst() {
        labEngine.simulateTelemetryJitterBurst()
    }

    fun copyDebugFrameLogs() {
        val logs = state.value.debugState.frameLogs
        val formatted = logs.joinToString("\n") { entry ->
            "#%03d [%s] %s (%d=%s) %s".format(
                entry.seq,
                entry.timestampText,
                entry.category,
                entry.frameCode,
                entry.frameName,
                entry.summary
            )
        }
        clipboardManager?.setPrimaryClip(ClipData.newPlainText("ContextAll Frame Trace", formatted))
    }

    fun clearDebugFrameLogs() {
        if (useLabEngine.value) {
            labEngine.clearDebugFrameLogs()
        } else {
            nativeController.clearDebugFrameLogs()
        }
    }

    fun openOnboardingGuide() {
        _showOnboardingGuide.value = true
    }

    fun dismissOnboardingGuide() {
        uiPrefs.edit().putBoolean(KEY_HAS_SEEN_ONBOARDING, true).apply()
        _showOnboardingGuide.value = false
        if (!state.value.isScanningLan) {
            if (useLabEngine.value) {
                labEngine.triggerDiscoveryScan(initialBootSweep = false, discoverNew = false)
            } else {
                nativeController.triggerDiscoveryScan()
            }
        }
    }

    private fun nextRequestId(): String =
        UUID.randomUUID().toString().replace("-", "").lowercase()

    private fun initialDeviceDisplayName(): String {
        val model = Build.MODEL?.trim().orEmpty()
        return if (model.isNotEmpty() && model.length <= 48) {
            "$model · ContextAll"
        } else {
            "Android · ContextAll"
        }
    }

    companion object {
        private const val PREFS_NAME = "contextall_ui_prefs"
        private const val KEY_HAS_SEEN_ONBOARDING = "has_seen_onboarding_v1"
    }
}
