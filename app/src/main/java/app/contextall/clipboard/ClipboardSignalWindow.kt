package app.contextall.clipboard

/**
 * Coalesces change signals so a single external copy does not open several
 * transparent capture activities. A signal inside [cooldownMs] of the last
 * accepted one is ignored; the first is accepted and recorded.
 */
class ClipboardSignalWindow(
    private val cooldownMs: Long = DEFAULT_COOLDOWN_MS
) {
    @Volatile
    private var lastAcceptedAtMs: Long = 0L

    fun shouldTrigger(nowMs: Long): Boolean {
        if (nowMs - lastAcceptedAtMs < cooldownMs) return false
        lastAcceptedAtMs = nowMs
        return true
    }

    fun reset() {
        lastAcceptedAtMs = 0L
    }

    private companion object {
        const val DEFAULT_COOLDOWN_MS = 600L
    }
}
