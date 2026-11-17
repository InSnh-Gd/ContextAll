package app.contextall

import android.net.Uri
import app.contextall.model.CommandResult
import app.contextall.model.LocalCommand
import app.contextall.model.SessionSnapshotUi
import app.contextall.model.UiEventNotice
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow

sealed class OutboundFile {
    data class UriRef(val uri: Uri) : OutboundFile()
    data class Named(val fileName: String, val totalBytes: Long, val content: ByteArray? = null) : OutboundFile()
}

internal interface ShellOperator {
    val snapshot: StateFlow<SessionSnapshotUi>
    val notices: SharedFlow<UiEventNotice>

    fun start(): Boolean
    fun stop()
    fun submitCommand(command: LocalCommand): CommandResult
    fun sendFiles(deviceId: String, files: List<OutboundFile>)
    fun connectManual(host: String, port: Int, alias: String): Boolean
    fun pushClipboard(text: String)
    fun resetIdentity()
    fun scanLan()
    fun removeQueuedUpload(queueId: String)
    fun clearQueuedUploads()
    fun clearCompletedTransfers()
    fun setDeveloperUiEnabled(enabled: Boolean)
    fun clearDebugFrameLogs()
}
