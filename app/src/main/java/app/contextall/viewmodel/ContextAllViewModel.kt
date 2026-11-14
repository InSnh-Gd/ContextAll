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
import app.contextall.service.LinkService
import java.util.UUID
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch

class ContextAllViewModel(application: Application) : AndroidViewModel(application) {

    private val uiPrefs = application.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
    private val _showOnboardingGuide = MutableStateFlow(
        !uiPrefs.getBoolean(KEY_HAS_SEEN_ONBOARDING, false)
    )
    val showOnboardingGuide: StateFlow<Boolean> = _showOnboardingGuide.asStateFlow()

    private val engine = InProcessSessionEngine(
        scope = viewModelScope,
        initialDisplayName = initialDeviceDisplayName(),
        initialDebugMode = BuildConfig.DEBUG
    )

    val state: StateFlow<SessionSnapshotUi> = engine.snapshot
    val notices: SharedFlow<UiEventNotice> = engine.notices

    init {
        LinkService.start(application)
        viewModelScope.launch {
            LinkService.isRunning.collect { running ->
                engine.setLinkServiceState(running)
            }
        }
    }

    fun selectTab(tab: TopTab) {
        engine.updateUiPreferences(
            tab = tab,
            selectedDeviceId = if (tab != TopTab.DEVICES) null else state.value.selectedDeviceId
        )
    }

    fun selectTrustedDevice(deviceId: String?) {
        engine.updateUiPreferences(selectedDeviceId = deviceId)
    }

    fun setDarkTheme(enabled: Boolean) {
        engine.updateUiPreferences(darkTheme = enabled)
    }

    fun setDynamicColor(enabled: Boolean) {
        engine.updateUiPreferences(dynamicColor = enabled)
    }

    fun toggleLanWifiState() {
        engine.setWifiLanReady(!state.value.wifiLanReady)
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
        engine.setRuntimePermissionsGranted(granted)
    }

    fun startPairing(deviceId: String) {
        engine.submitCommand(
            LocalCommand(
                frameType = FrameTypes.START_PAIRING,
                requestId = nextRequestId(),
                deviceId = deviceId
            )
        )
    }

    fun connectManualPeer(host: String, portText: String, alias: String = ""): Boolean {
        val port = EndpointValidator.parseValidPort(portText) ?: -1
        return engine.connectManualEndpoint(
            rawHost = host,
            port = port,
            optionalAlias = alias
        )
    }

    fun acceptPairing() {
        engine.submitCommand(
            LocalCommand(
                frameType = FrameTypes.ACCEPT_PAIRING,
                requestId = nextRequestId()
            )
        )
    }

    fun rejectPairing() {
        engine.submitCommand(
            LocalCommand(
                frameType = FrameTypes.REJECT_PAIRING,
                requestId = nextRequestId()
            )
        )
    }

    fun setClipboardSync(deviceId: String, enabled: Boolean) {
        engine.submitCommand(
            LocalCommand(
                frameType = FrameTypes.SET_CLIPBOARD_SYNC,
                requestId = nextRequestId(),
                deviceId = deviceId,
                enabled = enabled
            )
        )
    }

    fun setAskBeforeSave(deviceId: String, enabled: Boolean) {
        engine.submitCommand(
            LocalCommand(
                frameType = FrameTypes.SET_ASK_BEFORE_SAVE,
                requestId = nextRequestId(),
                deviceId = deviceId,
                enabled = enabled
            )
        )
    }

    fun unpairDevice(deviceId: String) {
        engine.submitCommand(
            LocalCommand(
                frameType = FrameTypes.UNPAIR_LOCAL,
                requestId = nextRequestId(),
                deviceId = deviceId
            )
        )
    }

    fun sendPickedDocument(deviceId: String, uri: Uri) {
        val spec = resolveUriSpec(uri)
        sendFilePayload(deviceId = deviceId, fileName = spec.fileName, totalBytes = spec.totalBytes)
    }

    fun uploadPickedUris(deviceId: String, uris: List<Uri>) {
        if (uris.isEmpty()) return
        val specs = uris.map { resolveUriSpec(it) }
        engine.enqueueOrSendFiles(deviceId = deviceId, files = specs)
    }

    fun uploadTextAsFile(deviceId: String, fileName: String, content: String) {
        val totalBytes = content.toByteArray(Charsets.UTF_8).size.toLong()
        engine.enqueueOrSendFiles(
            deviceId = deviceId,
            files = listOf(OutgoingFileSpec(fileName = fileName, totalBytes = totalBytes))
        )
    }

    fun uploadPresetFromTransfers(deviceId: String, fileName: String, totalBytes: Long) {
        engine.enqueueOrSendFiles(
            deviceId = deviceId,
            files = listOf(OutgoingFileSpec(fileName = fileName, totalBytes = totalBytes))
        )
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
        val result = engine.submitCommand(
            LocalCommand(
                frameType = FrameTypes.SEND_FILE,
                requestId = nextRequestId(),
                deviceId = deviceId,
                fileName = fileName,
                fileSize = totalBytes
            )
        )
        if (result.ok) {
            engine.updateUiPreferences(tab = TopTab.TRANSFERS, selectedDeviceId = null)
        }
    }

    fun acceptIncomingFile(transferId: String) {
        engine.submitCommand(
            LocalCommand(
                frameType = FrameTypes.ACCEPT_FILE,
                requestId = nextRequestId(),
                transferId = transferId
            )
        )
    }

    fun rejectIncomingFile(transferId: String) {
        engine.submitCommand(
            LocalCommand(
                frameType = FrameTypes.REJECT_FILE,
                requestId = nextRequestId(),
                transferId = transferId
            )
        )
    }

    fun togglePauseTransfer(transferId: String) {
        engine.togglePauseTransfer(transferId)
    }

    fun cancelTransfer(transferId: String) {
        engine.submitCommand(
            LocalCommand(
                frameType = FrameTypes.CANCEL_TRANSFER,
                requestId = nextRequestId(),
                transferId = transferId
            )
        )
    }

    fun retryBlockedCommit(transferId: String) {
        engine.retryBlockedCommit(transferId)
    }

    fun removeQueuedUpload(queueId: String) {
        engine.removeQueuedUpload(queueId)
    }

    fun clearQueuedUploads() {
        engine.clearQueuedUploads()
    }

    fun resendCompletedTransfer(transferId: String) {
        engine.resendCompletedTransfer(transferId)
    }

    fun clearCompletedTransfers() {
        engine.clearCompletedTransfers()
    }

    fun updateLocalDisplayName(newName: String) {
        engine.submitCommand(
            LocalCommand(
                frameType = FrameTypes.SET_DISPLAY_NAME,
                requestId = nextRequestId(),
                displayName = newName
            )
        )
    }

    fun pushSystemClipboard() {
        val cm = getApplication<Application>().getSystemService(ClipboardManager::class.java)
        val clip = cm?.primaryClip
        val text = if (clip != null && clip.itemCount > 0) {
            clip.getItemAt(0).text?.toString().orEmpty()
        } else {
            ""
        }
        engine.pushLocalPlainClipboard(text)
    }

    fun pushCustomClipboardText(text: String) {
        val cm = getApplication<Application>().getSystemService(ClipboardManager::class.java)
        cm?.setPrimaryClip(ClipData.newPlainText("ContextAll", text))
        engine.pushLocalPlainClipboard(text)
    }

    fun copyTextToSystemClipboard(label: String, value: String) {
        val cm = getApplication<Application>().getSystemService(ClipboardManager::class.java)
        cm?.setPrimaryClip(ClipData.newPlainText(label, value))
    }

    fun resetIdentity() {
        engine.resetIdentity()
    }

    fun triggerLanScan() {
        engine.triggerDiscoveryScan(initialBootSweep = false, discoverNew = true)
    }

    // --- Developer Debug & Scenario Lab Actions ---

    fun setDeveloperUiEnabled(enabled: Boolean) {
        engine.setDeveloperUiEnabled(enabled, emitNotice = true)
    }

    fun switchSeedProfile(profile: SeedDataProfile) {
        engine.applySeedDataProfile(profile)
    }

    fun labDiscoverPeer() = engine.triggerDiscoveryScan(initialBootSweep = false, discoverNew = true)

    fun labInboundPairing() = engine.simulateInboundPairingRequest()

    fun labInboundFile(moveBlocked: Boolean = false) {
        if (engine.simulateInboundFileOffer(simulateMoveBlocked = moveBlocked)) {
            engine.updateUiPreferences(tab = TopTab.TRANSFERS, selectedDeviceId = null)
        }
    }

    fun labRemoteClipboard() {
        engine.simulateRemoteClipboardDelta()
        val latest = state.value.clipboardState.lastSyncedText
        if (latest.isNotEmpty()) {
            val cm = getApplication<Application>().getSystemService(ClipboardManager::class.java)
            cm?.setPrimaryClip(ClipData.newPlainText("ContextAll Remote", latest))
        }
    }

    fun labTogglePeerConnection(deviceId: String? = null) {
        engine.toggleTrustedPeerConnectivity(deviceId)
    }

    fun labTelemetryBurst() {
        engine.simulateTelemetryJitterBurst()
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
        val cm = getApplication<Application>().getSystemService(ClipboardManager::class.java)
        cm?.setPrimaryClip(ClipData.newPlainText("ContextAll Frame Trace", formatted))
    }

    fun clearDebugFrameLogs() {
        engine.clearDebugFrameLogs()
    }

    fun openOnboardingGuide() {
        _showOnboardingGuide.value = true
    }

    fun dismissOnboardingGuide() {
        uiPrefs.edit().putBoolean(KEY_HAS_SEEN_ONBOARDING, true).apply()
        _showOnboardingGuide.value = false
        if (!state.value.isScanningLan) {
            engine.triggerDiscoveryScan(initialBootSweep = false, discoverNew = false)
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
