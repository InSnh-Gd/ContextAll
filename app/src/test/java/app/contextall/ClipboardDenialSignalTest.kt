package app.contextall

import app.contextall.clipboard.ClipboardDenialSignal
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class ClipboardDenialSignalTest {
    private val packageName = "app.contextall"

    @Test
    fun recognisesDenialForOurPackage() {
        val line = "ClipboardService: Denying clipboard access to app.contextall, " +
            "application is not in focus nor is it a system service for user 0"
        assertTrue(ClipboardDenialSignal.isDenialFor(line, packageName))
    }

    @Test
    fun ignoresDenialForAnotherPackage() {
        val line = "ClipboardService: Denying clipboard access to com.other.app, " +
            "application is not in focus nor is it a system service for user 0"
        assertFalse(ClipboardDenialSignal.isDenialFor(line, packageName))
    }

    @Test
    fun ignoresLinesWithoutTheDenialMarker() {
        val line = "ClipboardService: something unrelated for app.contextall"
        assertFalse(ClipboardDenialSignal.isDenialFor(line, packageName))
    }

    @Test
    fun ignoresDenialLineForAnEmptyPackage() {
        val line = "ClipboardService: Denying clipboard access to app.contextall"
        assertFalse(ClipboardDenialSignal.isDenialFor(line, ""))
    }
}
