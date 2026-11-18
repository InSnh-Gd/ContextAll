package app.contextall.clipboard

import android.Manifest
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import app.contextall.service.ClipboardCaptureActivity
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.stateIn

/**
 * Application-wide coordinator for the KDE-style background auto-capture.
 *
 * The foreground service keeps [ClipboardSignalWatcher] streaming system logs;
 * when the user has enabled capture and a clipboard-access denial for this
 * package is observed, a transparent activity is opened so the clipboard can
 * be read once while it holds focus. [capturing] is only true while the gate
 * is on, the log stream is reachable, and the app holds `READ_LOGS`.
 */
object ClipboardAutoCapture {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)
    private val window = ClipboardSignalWindow()
    private val watcher = ClipboardSignalWatcher(onLine = ::onLogLine)

    @Volatile
    private var appContext: Context? = null

    @Volatile
    private var store: ClipboardAutoCaptureStore? = null

    private val _enabled = MutableStateFlow(false)
    val enabled: StateFlow<Boolean> = _enabled.asStateFlow()

    private val _canReadLogs = MutableStateFlow(false)
    val capturing: StateFlow<Boolean> = combine(
        _enabled,
        watcher.ready,
        _canReadLogs
    ) { isEnabled, streamReady, hasLogs ->
        isEnabled && streamReady && hasLogs
    }.stateIn(scope, SharingStarted.Eagerly, false)

    fun start(context: Context) {
        init(context.applicationContext)
        watcher.start()
    }

    fun stop() {
        watcher.stop()
    }

    fun setEnabled(context: Context, value: Boolean) {
        init(context.applicationContext)
        store?.setEnabled(value)
        _enabled.value = value
        window.reset()
    }

    private fun init(app: Context) {
        if (appContext != null) return
        appContext = app
        val createdStore = ClipboardAutoCaptureStore(app)
        store = createdStore
        _enabled.value = createdStore.enabled.value
        _canReadLogs.value = canReadLogs(app)
    }

    private fun canReadLogs(context: Context): Boolean = try {
        context.checkSelfPermission(Manifest.permission.READ_LOGS) ==
            PackageManager.PERMISSION_GRANTED
    } catch (_: Exception) {
        false
    }

    private fun onLogLine(line: String) {
        val app = appContext ?: return
        if (!_enabled.value) return
        if (!canReadLogs(app)) return
        if (!ClipboardDenialSignal.isDenialFor(line, app.packageName)) return
        if (!window.shouldTrigger(System.currentTimeMillis())) return
        launchCapture(app)
    }

    private fun launchCapture(context: Context) {
        if (!_enabled.value) return
        val intent = Intent(context, ClipboardCaptureActivity::class.java)
            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        runCatching { context.startActivity(intent) }
    }
}
