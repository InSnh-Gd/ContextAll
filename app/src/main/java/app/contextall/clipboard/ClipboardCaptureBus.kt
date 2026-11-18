package app.contextall.clipboard

import kotlinx.coroutines.channels.BufferOverflow
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.asSharedFlow

/**
 * Process-wide channel that delivers the plain text a transparent capture
 * activity read once it held input focus. The ViewModel collects [captures]
 * and pushes them through the existing clipboard sync path.
 */
object ClipboardCaptureBus {
    private val _captures = MutableSharedFlow<String>(
        extraBufferCapacity = 8,
        onBufferOverflow = BufferOverflow.DROP_OLDEST
    )
    val captures: SharedFlow<String> = _captures.asSharedFlow()

    fun publish(text: String) {
        if (text.isEmpty()) return
        _captures.tryEmit(text)
    }
}
