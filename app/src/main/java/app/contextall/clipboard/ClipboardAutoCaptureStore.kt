package app.contextall.clipboard

import android.content.Context
import android.content.SharedPreferences
import androidx.core.content.edit
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

/**
 * Persisted, default-off gate for background auto-capture. Reads and writes
 * only this device's preference; no receipt, history, or notification changes.
 */
class ClipboardAutoCaptureStore(context: Context) {
    private val prefs: SharedPreferences =
        context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)

    private val _enabled = MutableStateFlow(prefs.getBoolean(KEY_ENABLED, false))
    val enabled: StateFlow<Boolean> = _enabled.asStateFlow()

    fun setEnabled(value: Boolean) {
        if (_enabled.value == value) return
        prefs.edit { putBoolean(KEY_ENABLED, value) }
        _enabled.value = value
    }

    private companion object {
        const val PREFS_NAME = "contextall_auto_capture"
        const val KEY_ENABLED = "enabled"
    }
}
