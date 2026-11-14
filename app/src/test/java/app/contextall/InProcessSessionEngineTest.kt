package app.contextall

import app.contextall.core.FrameTypes
import app.contextall.core.InProcessSessionEngine
import app.contextall.model.LocalCommand
import app.contextall.model.SeedDataProfile
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Test

class InProcessSessionEngineTest {

    @Test
    fun labEngine_switchesProfilesAndValidatesDuplicateCommandIds() {
        val scope = CoroutineScope(SupervisorJob() + Dispatchers.Unconfined)
        try {
            val engine = InProcessSessionEngine(
                scope = scope,
                initialDisplayName = "Pixel Unit",
                initialDebugMode = true,
                autoStartTicker = false
            )

            assertEquals(SeedDataProfile.CONSUMER, engine.snapshot.value.debugState.seedProfile)
            engine.applySeedDataProfile(SeedDataProfile.DEVELOPER_LAB)
            assertEquals(SeedDataProfile.DEVELOPER_LAB, engine.snapshot.value.debugState.seedProfile)
            assertTrue(engine.snapshot.value.devices.isNotEmpty())

            val cmd = LocalCommand(
                frameType = FrameTypes.SET_DISPLAY_NAME,
                requestId = "req0001",
                displayName = "Pixel Renamed"
            )
            val first = engine.submitCommand(cmd)
            assertTrue(first.ok)
            assertEquals("Pixel Renamed", engine.snapshot.value.localDisplayName)

            val dup = engine.submitCommand(cmd)
            assertFalse(dup.ok)
            assertNotNull(dup.error)

            val oldId = engine.snapshot.value.localDeviceId
            engine.resetIdentity()
            assertNotEquals(oldId, engine.snapshot.value.localDeviceId)
        } finally {
            scope.cancel()
        }
    }
}

