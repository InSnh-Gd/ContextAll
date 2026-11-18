package app.contextall.viewmodel

import android.app.Application
import android.content.ClipboardManager
import android.content.Context
import android.net.Uri
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import app.contextall.BuildConfig
import app.contextall.OutboundFile
import app.contextall.ShellOperator
import app.contextall.clipboard.ClipboardAutoCapture
import app.contextall.core.EndpointValidator
import app.contextall.core.FrameTypes
import app.contextall.core.ProtocolLimits
import app.contextall.lab.LabSnapshot
import app.contextall.model.LocalCommand
import app.contextall.model.SeedDataProfile
import app.contextall.model.SessionSnapshotUi
import app.contextall.model.TopTab
import app.contextall.model.UiEventNotice
import app.contextall.service.LinkService
import java.util.UUID
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.channels.BufferOverflow
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.flatMapLatest
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

private data class ShellChrome(
    val currentTab: TopTab = TopTab.DEVICES,
    val selectedDeviceId: String? = null,
    val darkTheme: Boolean = false,
    val dynamicColor: Boolean = true,
    val linkServiceRunning: Boolean = false,
    val runtimePermissionsGranted: Boolean = false,
    val autoCaptureEnabled: Boolean = false,
    val autoCaptureCapturing: Boolean = false
)

@OptIn(ExperimentalCoroutinesApi::class)
class ContextAllViewModel(application: Application) : AndroidViewModel(application) {

    private val uiPrefs = application.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
    private val _showOnboardingGuide = MutableStateFlow(
        !uiPrefs.getBoolean(KEY_HAS_SEEN_ONBOARDING, false)
    )
    val showOnboardingGuide: StateFlow<Boolean> = _showOnboardingGuide.asStateFlow()
    val foregroundClipboard = MutableStateFlow(uiPrefs.getBoolean("foreground_clipboard", false))

    fun setForegroundClipboard(enabled: Boolean) {
        foregroundClipboard.value = enabled
        uiPrefs.edit().putBoolean("foreground_clipboard", enabled).apply()
    }

    private val linkSession = LinkService.session(application)
    private val nativeController = linkSession.controller

    private val lab = LabSnapshot(
        initialDisplayName = linkSession.displayName,
        initialDebugMode = BuildConfig.DEBUG
    )

    private val useLab = MutableStateFlow(false)
    private val chrome = MutableStateFlow(ShellChrome())

    private fun operator(): ShellOperator = if (useLab.value) lab else nativeController

    val state: StateFlow<SessionSnapshotUi> = combine(
        useLab.flatMapLatest { labMode ->
            if (labMode) lab.snapshot else nativeController.snapshot
        },
        chrome
    ) { snap, ch ->
        snap.copy(
            currentTab = ch.currentTab,
            selectedDeviceId = ch.selectedDeviceId,
            darkTheme = ch.darkTheme,
            dynamicColor = ch.dynamicColor,
            linkServiceRunning = ch.linkServiceRunning,
            runtimePermissionsGranted = ch.runtimePermissionsGranted,
            autoCaptureEnabled = ch.autoCaptureEnabled,
            autoCaptureCapturing = ch.autoCaptureCapturing
        )
    }.stateIn(
        scope = viewModelScope,
        started = SharingStarted.Eagerly,
        initialValue = nativeController.snapshot.value
    )

    private val _notices = MutableSharedFlow<UiEventNotice>(
        extraBufferCapacity = 32,
        onBufferOverflow = BufferOverflow.DROP_OLDEST
    )
    val notices: SharedFlow<UiEventNotice> = _notices.asSharedFlow()

    private val clipboardManager = application.getSystemService(ClipboardManager::class.java)
    private var clipboardFocused = false
    private var clipboardRevision = 0L
    private val clipChangedListener = ClipboardManager.OnPrimaryClipChangedListener {
        val revision = ++clipboardRevision
        if (!clipboardFocused || !foregroundClipboard.value || useLab.value || !LinkService.isRunning.value) {
            return@OnPrimaryClipChangedListener
        }
        val clip = clipboardManager?.primaryClip ?: return@OnPrimaryClipChangedListener
        if (clip.itemCount != 1 || linkSession.isOwnClipboard(clip) || linkSession.isSensitive(clip)) {
            return@OnPrimaryClipChangedListener
        }
        val item = clip.getItemAt(0)
        val uri = item.uri
        if (uri?.scheme == "content") {
            pushClipboardUri(uri, revision)
        } else {
            item.text?.toString()?.let(nativeController::pushCapturedClipboard)
        }
    }

    init {
        viewModelScope.launch {
            useLab.flatMapLatest { labMode ->
                if (labMode) lab.notices else nativeController.notices
            }.collect { notice ->
                _notices.tryEmit(notice)
            }
        }
        LinkService.start(application)
        viewModelScope.launch {
            ClipboardAutoCapture.enabled.collect { enabled ->
                chrome.update { it.copy(autoCaptureEnabled = enabled) }
            }
        }
        viewModelScope.launch {
            ClipboardAutoCapture.capturing.collect { capturing ->
                chrome.update { it.copy(autoCaptureCapturing = capturing) }
            }
        }
        viewModelScope.launch {
            LinkService.isRunning.collect { running ->
                chrome.update { it.copy(linkServiceRunning = running) }
            }
        }
    }

    fun setClipboardFocused(focused: Boolean) {
        if (clipboardFocused == focused) return
        clipboardFocused = focused
        if (focused) {
            clipboardManager?.addPrimaryClipChangedListener(clipChangedListener)
        } else {
            clipboardManager?.removePrimaryClipChangedListener(clipChangedListener)
        }
    }

    override fun onCleared() {
        setClipboardFocused(false)
        lab.stop()
        super.onCleared()
    }

    fun selectTab(tab: TopTab) {
        val nextSelected = if (tab != TopTab.DEVICES) null else chrome.value.selectedDeviceId
        chrome.update { it.copy(currentTab = tab, selectedDeviceId = nextSelected) }
    }

    fun selectTrustedDevice(deviceId: String?) {
        chrome.update { it.copy(selectedDeviceId = deviceId) }
    }

    fun setDarkTheme(enabled: Boolean) {
        chrome.update { it.copy(darkTheme = enabled) }
    }

    fun setDynamicColor(enabled: Boolean) {
        chrome.update { it.copy(dynamicColor = enabled) }
    }

    fun setAutoCaptureEnabled(enabled: Boolean) {
        ClipboardAutoCapture.setEnabled(getApplication(), enabled)
        chrome.update { it.copy(autoCaptureEnabled = enabled) }
    }

    fun toggleLanWifiState() {
        operator().scanLan()
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
        chrome.update { it.copy(runtimePermissionsGranted = granted) }
        if (!useLab.value) {
            nativeController.setRuntimePermissionsGranted(granted)
        }
    }

    private fun submitBridgeCommand(command: LocalCommand) {
        operator().submitCommand(command)
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
        return operator().connectManual(host, port, alias)
    }

    fun acceptPairing() {
        submitBridgeCommand(LocalCommand(frameType = FrameTypes.ACCEPT_PAIRING, requestId = nextRequestId()))
    }

    fun rejectPairing() {
        submitBridgeCommand(LocalCommand(frameType = FrameTypes.REJECT_PAIRING, requestId = nextRequestId()))
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
        operator().sendFiles(deviceId, listOf(OutboundFile.UriRef(uri)))
        chrome.update { it.copy(currentTab = TopTab.TRANSFERS, selectedDeviceId = null) }
    }

    fun uploadPickedUris(deviceId: String, uris: List<Uri>) {
        if (uris.isEmpty()) return
        operator().sendFiles(deviceId, uris.map { OutboundFile.UriRef(it) })
        chrome.update { it.copy(currentTab = TopTab.TRANSFERS, selectedDeviceId = null) }
    }

    fun uploadTextAsFile(deviceId: String, fileName: String, content: String) {
        operator().sendFiles(
            deviceId,
            listOf(OutboundFile.Named(fileName, content.toByteArray(Charsets.UTF_8).size.toLong(), content.toByteArray(Charsets.UTF_8)))
        )
        chrome.update { it.copy(currentTab = TopTab.TRANSFERS, selectedDeviceId = null) }
    }

    fun uploadPresetFromTransfers(deviceId: String, fileName: String, totalBytes: Long) {
        operator().sendFiles(deviceId, listOf(OutboundFile.Named(fileName, totalBytes)))
        chrome.update { it.copy(currentTab = TopTab.TRANSFERS, selectedDeviceId = null) }
    }

    fun sendFilePayload(deviceId: String, fileName: String, totalBytes: Long) {
        val result = operator().submitCommand(
            LocalCommand(
                frameType = FrameTypes.SEND_FILE,
                requestId = nextRequestId(),
                deviceId = deviceId,
                fileName = fileName,
                fileSize = totalBytes
            )
        )
        if (result.ok) {
            chrome.update { it.copy(currentTab = TopTab.TRANSFERS, selectedDeviceId = null) }
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
        if (useLab.value) {
            lab.togglePauseTransfer(transferId)
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
        if (useLab.value) {
            lab.retryBlockedCommit(transferId)
        }
    }

    fun removeQueuedUpload(queueId: String) {
        operator().removeQueuedUpload(queueId)
    }

    fun clearQueuedUploads() {
        operator().clearQueuedUploads()
    }

    fun resendCompletedTransfer(transferId: String) {
        if (useLab.value) {
            lab.resendCompletedTransfer(transferId)
        }
    }

    fun clearCompletedTransfers() {
        operator().clearCompletedTransfers()
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
        if (clip != null && linkSession.isSensitive(clip)) return
        if (clip?.itemCount == 1 && !useLab.value) {
            val uri = clip.getItemAt(0).uri
            if (uri?.scheme == "content") {
                pushClipboardUri(uri, ++clipboardRevision)
                return
            }
        }
        val text = if (clip != null && clip.itemCount > 0) {
            clip.getItemAt(0).text?.toString().orEmpty()
        } else {
            ""
        }
        operator().pushClipboard(text)
    }

    private fun pushClipboardUri(uri: Uri, revision: Long) {
        val app = getApplication<Application>()
        viewModelScope.launch {
            val group = clipboardHistoryCommand(FrameTypes.READ_CLIPBOARD_HISTORY, org.json.JSONObject())
                ?.optJSONObject("clipboardHistory") ?: return@launch
            if (!group.has("groupOwner") || group.isNull("groupOwner") || revision != clipboardRevision || !clipboardFocused) return@launch
            val folder = java.io.File(app.cacheDir, "clipboard-capture/${UUID.randomUUID()}")
            try {
                val staged = kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.IO) {
                    runCatching {
                        val resolver = app.contentResolver
                        val mime = resolver.getType(uri)
                        val providerName = resolver.query(uri, arrayOf(android.provider.OpenableColumns.DISPLAY_NAME), null, null, null)
                            ?.use { cursor -> if (cursor.moveToFirst()) cursor.getString(0) else null }
                        val fileName = providerName?.takeIf { it.isNotBlank() && it.length <= 128 && it.none { ch -> ch == '/' || ch == '\\' || ch.isISOControl() } }
                            ?.takeUnless { it.startsWith('.') } ?: if (mime == "image/png") "clipboard.png" else "clipboard.bin"
                        folder.mkdirs()
                        val file = java.io.File(folder, fileName)
                        val limit = ProtocolLimits.MAX_CLIPBOARD_ATTACHMENT_BYTES
                        resolver.openInputStream(uri)?.use { input ->
                            file.outputStream().use { output ->
                                val buffer = ByteArray(64 * 1024)
                                var total = 0
                                while (true) {
                                    val count = input.read(buffer)
                                    if (count < 0) break
                                    total += count
                                    if (total > limit) return@runCatching null
                                    output.write(buffer, 0, count)
                                }
                            }
                        } ?: return@runCatching null
                        org.json.JSONObject().put("path", file.absolutePath).put("displayName", mime)
                    }.getOrNull()
                }
                if (staged != null && clipboardFocused && revision == clipboardRevision) {
                    clipboardHistoryCommand(FrameTypes.PUSH_CLIPBOARD_ATTACHMENT, staged)
                }
            } finally {
                folder.deleteRecursively()
            }
        }
    }

    fun pushCustomClipboardText(text: String) {
        copyTextToSystemClipboard("ContextAll", text)
        operator().pushClipboard(text)
    }

    fun copyTextToSystemClipboard(label: String, value: String) {
        linkSession.writeClipboard(label, value)
    }

    suspend fun clipboardHistoryCommand(frameType: Int, body: org.json.JSONObject): org.json.JSONObject? =
        kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.IO) {
            if (useLab.value || !LinkService.isRunning.value) null
            else nativeController.clipboardHistoryCommand(frameType, body)
        }

    fun resetIdentity() {
        operator().resetIdentity()
    }

    fun triggerLanScan() {
        operator().scanLan()
    }

    fun setDeveloperUiEnabled(enabled: Boolean) {
        operator().setDeveloperUiEnabled(enabled)
    }

    fun switchSeedProfile(profile: SeedDataProfile) {
        when (profile) {
            SeedDataProfile.DEVELOPER_LAB -> {
                linkSession.setLabMode(true)
                lab.start()
                useLab.value = true
            }
            SeedDataProfile.CONSUMER -> {
                if (!nativeController.isNativeReady) {
                    return
                }
                lab.stop()
                useLab.value = false
                linkSession.setLabMode(false)
                _notices.tryEmit(UiEventNotice.DebugSeedSwitched(isDeveloperProfile = false))
            }
        }
    }

    fun labDiscoverPeer() {
        if (useLab.value) {
            lab.scanLan()
        }
    }

    fun labInboundPairing() {
        if (useLab.value) {
            lab.simulateInboundPairingRequest()
        }
    }

    fun labInboundFile(moveBlocked: Boolean = false) {
        if (!useLab.value) return
        if (lab.simulateInboundFileOffer(simulateMoveBlocked = moveBlocked)) {
            chrome.update { it.copy(currentTab = TopTab.TRANSFERS, selectedDeviceId = null) }
        }
    }

    fun labRemoteClipboard() {
        if (!useLab.value) return
        lab.simulateRemoteClipboardDelta()
        val latest = lab.snapshot.value.clipboardState.lastSyncedText
        if (latest.isNotEmpty()) {
            copyTextToSystemClipboard("ContextAll Remote", latest)
        }
    }

    fun labTogglePeerConnection(deviceId: String? = null) {
        if (useLab.value) {
            lab.toggleTrustedPeerConnectivity(deviceId)
        }
    }

    fun labTelemetryBurst() {
        if (useLab.value) {
            lab.simulateTelemetryJitterBurst()
        }
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
        copyTextToSystemClipboard("ContextAll Frame Trace", formatted)
    }

    fun clearDebugFrameLogs() {
        operator().clearDebugFrameLogs()
    }

    fun openOnboardingGuide() {
        _showOnboardingGuide.value = true
    }

    fun dismissOnboardingGuide() {
        uiPrefs.edit().putBoolean(KEY_HAS_SEEN_ONBOARDING, true).apply()
        _showOnboardingGuide.value = false
        if (!state.value.isScanningLan) {
            operator().scanLan()
        }
    }

    private fun nextRequestId(): String =
        UUID.randomUUID().toString().replace("-", "").lowercase()

    companion object {
        private const val PREFS_NAME = "contextall_ui_prefs"
        private const val KEY_HAS_SEEN_ONBOARDING = "has_seen_onboarding_v1"
    }
}
