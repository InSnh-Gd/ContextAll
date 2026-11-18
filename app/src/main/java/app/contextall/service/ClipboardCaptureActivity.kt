package app.contextall.service

import android.app.Activity
import android.content.ClipboardManager
import android.content.Context
import app.contextall.clipboard.ClipboardAutoCapture
import app.contextall.clipboard.ClipboardCaptureBus

/**
 * Transparent capture activity. It only runs because the user enabled
 * background auto-capture and the platform denied clipboard access to this
 * package while it was unfocused. It reads the current primary-clip text once
 * it actually holds input focus, publishes non-empty text, then finishes. It
 * never acts as an input method.
 */
class ClipboardCaptureActivity : Activity() {
    private var captured = false

    override fun onWindowFocusChanged(hasFocus: Boolean) {
        super.onWindowFocusChanged(hasFocus)
        if (hasFocus) maybeCapture()
    }

    private fun maybeCapture() {
        if (captured) return
        captured = true
        if (ClipboardAutoCapture.canCapture(this) && LinkService.isRunning.value) {
            val text = readPrimaryClip(this)
            if (text.isNotEmpty()) ClipboardCaptureBus.publish(text)
        }
        finishAndRemoveTask()
    }

    private fun readPrimaryClip(context: Context): String {
        val manager = context.getSystemService(ClipboardManager::class.java) ?: return ""
        val clip = manager.primaryClip ?: return ""
        val session = LinkService.session(context)
        if (clip.itemCount != 1 || session.isOwnClipboard(clip) || session.isSensitive(clip)) return ""
        return clip.getItemAt(0).text?.toString().orEmpty()
    }
}
