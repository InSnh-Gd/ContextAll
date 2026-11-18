package app.contextall

import app.contextall.clipboard.ClipboardSignalWindow
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class ClipboardSignalWindowTest {
    private val cooldownMs = 600L

    @Test
    fun firstSignalTriggers() {
        val window = ClipboardSignalWindow(cooldownMs)
        assertTrue(window.shouldTrigger(1_000L))
    }

    @Test
    fun signalInsideCooldownIsSkipped() {
        val window = ClipboardSignalWindow(cooldownMs)
        assertTrue(window.shouldTrigger(1_000L))
        assertFalse(window.shouldTrigger(1_300L))
    }

    @Test
    fun signalAfterCooldownTriggers() {
        val window = ClipboardSignalWindow(cooldownMs)
        assertTrue(window.shouldTrigger(1_000L))
        assertTrue(window.shouldTrigger(1_700L))
    }

    @Test
    fun resetAllowsImmediateTrigger() {
        val window = ClipboardSignalWindow(cooldownMs)
        assertTrue(window.shouldTrigger(1_000L))
        window.reset()
        assertTrue(window.shouldTrigger(1_100L))
    }
}
