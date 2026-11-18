package app.contextall.clipboard

import android.Manifest
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.provider.Settings
import app.contextall.BuildConfig
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
    private val denialListener = ClipboardManager.OnPrimaryClipChangedListener {}
    private var listening = false
    @Volatile
    private var running = false

    @Volatile
    private var appContext: Context? = null

    @Volatile
    private var store: ClipboardAutoCaptureStore? = null

    private val _enabled = MutableStateFlow(false)
    val enabled: StateFlow<Boolean> = _enabled.asStateFlow()

    private val permissionsGranted = MutableStateFlow(false)
    val capturing: StateFlow<Boolean> = combine(
        _enabled,
        watcher.ready,
        permissionsGranted
    ) { isEnabled, streamReady, hasPermissions ->
        isEnabled && streamReady && hasPermissions
    }.stateIn(scope, SharingStarted.Eagerly, false)

    fun start(context: Context) {
        init(context.applicationContext)
        running = true
        refreshListener()
    }

    fun stop() {
        running = false
        refreshListener()
    }

    fun setEnabled(context: Context, value: Boolean) {
        init(context.applicationContext)
        store?.setEnabled(value)
        _enabled.value = value
        window.reset()
        refreshListener()
        if (BuildConfig.DEBUG && value && !Settings.canDrawOverlays(context)) {
            val intent = Intent(Settings.ACTION_MANAGE_OVERLAY_PERMISSION, Uri.parse("package:${context.packageName}"))
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
            runCatching { context.startActivity(intent) }
        }
    }

    private fun init(app: Context) {
        if (appContext != null) return
        appContext = app
        val createdStore = ClipboardAutoCaptureStore(app)
        store = createdStore
        _enabled.value = createdStore.enabled.value
    }

    fun refreshListener() {
        val app = appContext ?: return
        val clipboard = app.getSystemService(ClipboardManager::class.java) ?: return
        val hasPermissions = hasCapturePermissions(app)
        permissionsGranted.value = hasPermissions
        val shouldListen = running && _enabled.value && hasPermissions
        if (shouldListen == listening) return
        listening = shouldListen
        if (shouldListen) {
            clipboard.addPrimaryClipChangedListener(denialListener)
            watcher.start()
        } else {
            clipboard.removePrimaryClipChangedListener(denialListener)
            watcher.stop()
        }
    }

    private fun canReadLogs(context: Context): Boolean = try {
        BuildConfig.DEBUG && context.checkSelfPermission(Manifest.permission.READ_LOGS) ==
            PackageManager.PERMISSION_GRANTED
    } catch (_: Exception) {
        false
    }

    private fun hasCapturePermissions(context: Context): Boolean =
        canReadLogs(context) && Settings.canDrawOverlays(context)

    fun canCapture(context: Context): Boolean = running && _enabled.value && hasCapturePermissions(context)

    private fun onLogLine(line: String) {
        val app = appContext ?: return
        if (!canCapture(app)) return
        if (!ClipboardDenialSignal.isDenialFor(line, app.packageName)) return
        if (!window.shouldTrigger(System.currentTimeMillis())) return
        launchCapture(app)
    }

    private fun launchCapture(context: Context) {
        if (!canCapture(context)) return
        val intent = Intent(context, ClipboardCaptureActivity::class.java)
            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        runCatching { context.startActivity(intent) }
    }
}
