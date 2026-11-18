package app.contextall.clipboard

import java.io.BufferedReader
import java.io.InputStreamReader
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancelChildren
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

/**
 * Streams `logcat` lines to [onLine] so the coordinator can recognise the
 * platform's clipboard-access denial as a change signal. [ready] reports
 * whether the log stream actually started; when the app cannot read system
 * logs, [ready] stays false and no capture is attempted.
 */
class ClipboardSignalWatcher(
    private val onLine: (String) -> Unit
) {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)
    private val _ready = MutableStateFlow(false)
    val ready: StateFlow<Boolean> = _ready.asStateFlow()

    @Volatile
    private var process: Process? = null

    @Volatile
    private var started = false

    fun start() {
        if (started) return
        started = true
        scope.launch { watch() }
    }

    fun stop() {
        if (!started) return
        started = false
        _ready.value = false
        runCatching { process?.destroy() }
        scope.coroutineContext.cancelChildren()
    }

    private suspend fun watch() {
        val proc = try {
            val since = SimpleDateFormat("MM-dd HH:mm:ss.SSS", Locale.US).format(Date())
            ProcessBuilder("logcat", "-T", since, "-s", "ClipboardService:E").redirectErrorStream(true).start()
        } catch (_: Exception) {
            _ready.value = false
            return
        }
        process = proc
        _ready.value = true
        try {
            withContext(Dispatchers.IO) {
                val reader = BufferedReader(InputStreamReader(proc.inputStream))
                while (started) {
                    val line = reader.readLine() ?: break
                    if (line.isNotEmpty()) onLine(line)
                }
            }
        } catch (_: Exception) {
            // log stream ended, most likely because the process was destroyed
        } finally {
            _ready.value = false
            runCatching { proc.destroy() }
        }
    }
}
