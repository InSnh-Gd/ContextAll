package app.contextall.service

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.Service
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.os.Build
import android.os.IBinder
import android.os.PersistableBundle
import androidx.core.content.FileProvider
import app.contextall.BuildConfig
import app.contextall.core.FrameTypes
import app.contextall.service.ClipboardHistoryTile.Companion.openHistoryIntent
import app.contextall.nativebridge.NativeSessionController
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch

/**
 * Foreground connectedDevice service for the ContextAll Android app.
 */
class LinkService : Service() {

    override fun onCreate() {
        super.onCreate()
        if (!promoteToForegroundSafely()) {
            stopSelf()
        }
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (intent?.action == ACTION_STOP) {
            _isRunning.value = false
            session(this).setServiceRunning(false)
            stopForeground(STOP_FOREGROUND_REMOVE)
            stopSelf()
            return START_NOT_STICKY
        }
        if (!promoteToForegroundSafely()) {
            stopSelf()
            return START_NOT_STICKY
        }
        _isRunning.value = true
        session(this).setServiceRunning(true)
        return START_STICKY
    }

    override fun onDestroy() {
        _isRunning.value = false
        session(this).setServiceRunning(false)
        super.onDestroy()
    }

    override fun onBind(intent: Intent?): IBinder? = null

    private fun promoteToForegroundSafely(): Boolean {
        try {
            val manager = getSystemService(NotificationManager::class.java)
            if (manager != null && manager.getNotificationChannel(CHANNEL_ID) == null) {
                val channel = NotificationChannel(
                    CHANNEL_ID,
                    "ContextAll",
                    NotificationManager.IMPORTANCE_LOW
                ).apply {
                    description = "ContextAll LAN QUIC session"
                }
                manager.createNotificationChannel(channel)
            }

            val notification = Notification.Builder(this, CHANNEL_ID)
                .setContentTitle("ContextAll")
                .setContentText("LAN QUIC session active · Port 42461")
                .setSmallIcon(android.R.drawable.stat_notify_sync)
                .addAction(Notification.Action.Builder(null, "剪贴板历史", openHistoryIntent(this)).build())
                .setVisibility(Notification.VISIBILITY_PRIVATE)
                .setOngoing(true)
                .build()

            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
                startForeground(
                    NOTIFICATION_ID,
                    notification,
                    ServiceInfo.FOREGROUND_SERVICE_TYPE_CONNECTED_DEVICE
                )
            } else {
                startForeground(NOTIFICATION_ID, notification)
            }
            return true
        } catch (_: RuntimeException) {
            return false
        }
    }

    internal class Session(private val context: Context) {
        private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)
        private val clipboard = context.getSystemService(ClipboardManager::class.java)
        private var running = false
        private var labMode = false
        val displayName = Build.MODEL?.trim()?.takeIf { it.isNotEmpty() && it.length <= 48 }
            ?.let { "$it · ContextAll" } ?: "Android · ContextAll"

        val controller = NativeSessionController(
            context = context,
            scope = scope,
            initialDisplayName = displayName,
            initialDebugMode = BuildConfig.DEBUG,
            onApplyRemoteClipboard = { text, writeId ->
                scope.launch {
                    val accepted = running && !labMode && writeClipboard("ContextAll Remote", text)
                    confirmClipboardWrite(writeId, accepted)
                }
            },
            onApplyRemoteAttachment = { mime, name, path, writeId ->
                scope.launch {
                    val accepted = if (running && !labMode) {
                        try {
                            val uri = FileProvider.getUriForFile(context, "${context.packageName}.clipboard", java.io.File(path))
                            writeClip(ClipData(name, arrayOf(mime), ClipData.Item(uri)))
                        } catch (_: IllegalArgumentException) {
                            android.util.Log.w("ContextAllClipboard", "System clipboard attachment unavailable")
                            false
                        }
                    } else false
                    confirmClipboardWrite(writeId, accepted)
                }
            }
        )

        fun setServiceRunning(value: Boolean) {
            running = value
            controller.setLinkServiceState(running && !labMode)
        }

        fun setLabMode(value: Boolean) {
            labMode = value
            controller.setLinkServiceState(running && !labMode)
        }

        fun isOwnClipboard(clip: ClipData): Boolean =
            clip.description.extras?.getBoolean(LOCAL_CLIP_MARKER) == true

        fun isSensitive(clip: ClipData): Boolean =
            clip.description.extras?.getBoolean("android.content.extra.IS_SENSITIVE") == true

        fun writeClipboard(label: String, text: String): Boolean = writeClip(ClipData.newPlainText(label, text))

        private fun writeClip(clip: ClipData): Boolean {
            clip.description.extras = PersistableBundle().apply { putBoolean(LOCAL_CLIP_MARKER, true) }
            val manager = clipboard ?: return false
            return try {
                manager.setPrimaryClip(clip)
                true
            } catch (_: SecurityException) {
                android.util.Log.w("ContextAllClipboard", "System clipboard write denied")
                false
            }
        }

        private fun confirmClipboardWrite(writeId: String?, accepted: Boolean) {
            if (writeId == null) return
            scope.launch(Dispatchers.IO) {
                controller.clipboardHistoryCommand(FrameTypes.CONFIRM_CLIPBOARD_WRITE,
                    org.json.JSONObject().put("transferId", writeId).put("enabled", accepted))
            }
        }
    }

    companion object {
        const val CHANNEL_ID = "contextall_link"
        const val NOTIFICATION_ID = 42461
        const val ACTION_STOP = "app.contextall.service.STOP_LINK"
        private const val LOCAL_CLIP_MARKER = "app.contextall.local_clipboard_write"

        private val _isRunning = MutableStateFlow(false)
        val isRunning: StateFlow<Boolean> = _isRunning.asStateFlow()
        private var activeSession: Session? = null

        @Synchronized
        internal fun session(context: Context): Session =
            activeSession ?: Session(context.applicationContext).also { activeSession = it }

        fun start(context: Context) {
            try {
                val intent = Intent(context, LinkService::class.java)
                context.startForegroundService(intent)
            } catch (_: RuntimeException) {
                _isRunning.value = false
            }
        }

        fun stop(context: Context) {
            try {
                val intent = Intent(context, LinkService::class.java).apply {
                    action = ACTION_STOP
                }
                context.startService(intent)
            } catch (_: Exception) {
                context.stopService(Intent(context, LinkService::class.java))
            } finally {
                _isRunning.value = false
            }
        }
    }
}
