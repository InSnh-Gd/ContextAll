package app.contextall.service

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.os.Build
import android.os.IBinder
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

/**
 * Foreground connectedDevice service for the ContextAll Android app.
 */
class LinkService : Service() {

    override fun onCreate() {
        super.onCreate()
        promoteToForegroundSafely()
        _isRunning.value = true
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (intent?.action == ACTION_STOP) {
            _isRunning.value = false
            stopForeground(STOP_FOREGROUND_REMOVE)
            stopSelf()
            return START_NOT_STICKY
        }
        promoteToForegroundSafely()
        _isRunning.value = true
        return START_STICKY
    }

    override fun onDestroy() {
        _isRunning.value = false
        super.onDestroy()
    }

    override fun onBind(intent: Intent?): IBinder? = null

    private fun promoteToForegroundSafely() {
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
        } catch (_: Exception) {
            // Gracefully tolerate environments restricting background service promotion
        }
    }

    companion object {
        const val CHANNEL_ID = "contextall_link"
        const val NOTIFICATION_ID = 42461
        const val ACTION_STOP = "app.contextall.service.STOP_LINK"

        private val _isRunning = MutableStateFlow(false)
        val isRunning: StateFlow<Boolean> = _isRunning.asStateFlow()

        fun start(context: Context) {
            try {
                val intent = Intent(context, LinkService::class.java)
                context.startForegroundService(intent)
                _isRunning.value = true
            } catch (_: Exception) {
                _isRunning.value = true
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
