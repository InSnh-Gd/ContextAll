package app.contextall

import app.contextall.core.FrameTypes
import app.contextall.lab.LabSnapshot
import app.contextall.model.LocalCommand
import app.contextall.model.SeedDataProfile
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Test

class LabSnapshotTest {

    @Test
    fun start_loadsDeveloperLabSnapshot() {
        val lab = LabSnapshot(initialDisplayName = "Pixel Unit")
        assertTrue(lab.start())
        val snap = lab.snapshot.value
        assertEquals(SeedDataProfile.DEVELOPER_LAB, snap.debugState.seedProfile)
        assertTrue(snap.devices.isNotEmpty())
        assertEquals("Pixel Unit", snap.localDisplayName)
        assertTrue(snap.devices.any { it.displayName == "Arch Workstation" })
    }

    @Test
    fun patchTable_renamesLocalDevice_andRejectsUnknownCommand() {
        val lab = LabSnapshot(initialDisplayName = "Pixel Unit")
        lab.start()

        val ok = lab.submitCommand(
            LocalCommand(
                frameType = FrameTypes.SET_DISPLAY_NAME,
                requestId = "req-name",
                displayName = "Lab Phone"
            )
        )
        assertTrue(ok.ok)
        assertEquals("Lab Phone", lab.snapshot.value.localDisplayName)

        val rejected = lab.submitCommand(
            LocalCommand(
                frameType = FrameTypes.SET_DOWNLOAD_DIRECTORY,
                requestId = "req-dl"
            )
        )
        assertFalse(rejected.ok)
        assertEquals("invalid", rejected.error)
    }

    @Test
    fun simulate_inboundPairingFileAndClipboard() {
        val lab = LabSnapshot(initialDisplayName = "Pixel Unit")
        lab.start()

        lab.simulateInboundPairingRequest()
        assertNotNull(lab.snapshot.value.pairing)

        val accept = lab.submitCommand(
            LocalCommand(frameType = FrameTypes.ACCEPT_PAIRING, requestId = "req-pair")
        )
        assertTrue(accept.ok)
        assertEquals(null, lab.snapshot.value.pairing)

        assertTrue(lab.simulateInboundFileOffer())
        assertTrue(lab.snapshot.value.filePrompts.isNotEmpty())

        lab.simulateRemoteClipboardDelta()
        assertTrue(lab.snapshot.value.clipboardState.lastSyncedText.isNotEmpty())
    }
}
