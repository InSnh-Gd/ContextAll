package app.contextall

import app.contextall.core.FrameTypes
import app.contextall.model.EngineDebugState
import app.contextall.model.LocalCommand
import app.contextall.model.PeerPresence
import app.contextall.model.SessionSnapshotUi
import app.contextall.model.TransferDirection
import app.contextall.model.UiEventNotice
import app.contextall.nativebridge.MiniJson
import app.contextall.nativebridge.NativeProtocolMapper
import app.contextall.nativebridge.bool
import app.contextall.nativebridge.long
import app.contextall.nativebridge.obj
import app.contextall.nativebridge.str
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class NativeProtocolMapperTest {

    @Test
    fun miniJson_roundTripsEscapedStringsBooleansNumbersAndNestedObjects() {
        val raw = MiniJson.encodeObject(
            mapOf(
                "str" to "Line1\n\"Quoted\"\\Tab\t",
                "ok" to true,
                "count" to 42,
                "omittedNull" to null,
                "nested" to mapOf("port" to 42461)
            )
        )
        val parsed = MiniJson.parseObject(raw)
        assertNotNull(parsed)
        assertEquals("Line1\n\"Quoted\"\\Tab\t", parsed!!.str("str"))
        assertEquals(true, parsed.bool("ok"))
        assertEquals(42L, parsed.long("count"))
        assertFalse(parsed.containsKey("omittedNull"))
        assertEquals(42461L, parsed.obj("nested")?.long("port"))
    }

    @Test
    fun encodeLocalCommand_and_parseCommandResult_matchProtocolContract() {
        val cmd = LocalCommand(
            frameType = FrameTypes.SEND_FILE,
            requestId = "abcd1234",
            deviceId = "ca1peer",
            fileName = "ignored.bin",
            fileSize = 123L
        )
        val encoded = NativeProtocolMapper.encodeLocalCommand(cmd, filePath = "/data/user/0/app/staged.bin")
        val obj = MiniJson.parseObject(encoded)!!
        assertEquals("abcd1234", obj.str("requestId"))
        assertEquals("ca1peer", obj.str("deviceId"))
        assertEquals("/data/user/0/app/staged.bin", obj.str("path"))

        val okRes = NativeProtocolMapper.parseCommandResult("""{"requestId":"abcd1234","ok":true}""")
        assertTrue(okRes.ok)
        assertNull(okRes.error)

        val errRes = NativeProtocolMapper.parseCommandResult("""{"requestId":"abcd1234","ok":false,"error":"busy"}""")
        assertFalse(errRes.ok)
        assertEquals("busy", errRes.error)

        val fallbackRes = NativeProtocolMapper.parseCommandResult("not-json", fallbackRequestId = "fallback")
        assertFalse(fallbackRes.ok)
        assertEquals("fallback", fallbackRes.requestId)
        assertEquals("decode-failed", fallbackRes.error)
    }

    @Test
    fun applyStartEnvelope_populatesDeviceIdLocalNameAndDevices() {
        val startJson = """
            {
              "ok": true,
              "deviceId": "ca1localid",
              "snapshot": {
                "localDisplayName": "Pixel Lab",
                "devices": [
                  {
                    "deviceId": "ca1desktop",
                    "displayName": "Workstation",
                    "trusted": true,
                    "presence": "connected",
                    "clipboardSync": true,
                    "askBeforeSave": false
                  }
                ],
                "filePrompts": [],
                "transfers": []
              }
            }
        """.trimIndent()

        val initial = SessionSnapshotUi(localDisplayName = "Init", localDeviceId = "ca1init")
        val mapped = NativeProtocolMapper.applyStartEnvelope(initial, startJson)
        assertNotNull(mapped)
        assertEquals("ca1localid", mapped!!.localDeviceId)
        assertEquals("Pixel Lab", mapped.localDisplayName)
        assertEquals(1, mapped.devices.size)
        assertEquals(PeerPresence.CONNECTED, mapped.devices.single().presence)
        assertTrue(mapped.devices.single().trusted)
        assertTrue(mapped.devices.single().isMutualClipboardActive)
    }

    @Test
    fun applyPublishedFrames_coversDevicePairingPromptAndTransferLifecycle() {
        var snapshot = SessionSnapshotUi(
            localDisplayName = "Local",
            localDeviceId = "ca1local",
            debugState = EngineDebugState(committedDownloadCount = 0)
        )

        // 1. Device discovered via DEVICE_CHANGED
        val r1 = NativeProtocolMapper.applyPublishedFrame(
            snapshot,
            FrameTypes.DEVICE_CHANGED,
            mapOf(
                "deviceId" to "ca1peer1",
                "displayName" to "ThinkPad X1",
                "trusted" to false,
                "presence" to "reachable"
            ),
            nowMs = 1_000L
        )
        snapshot = r1.snapshot
        assertEquals(1, snapshot.devices.size)
        assertEquals("ca1peer1", snapshot.lastDiscoveredDeviceId)
        assertTrue(r1.notices.isEmpty())

        // 2. Pairing prompt shown
        val r2 = NativeProtocolMapper.applyPublishedFrame(
            snapshot,
            FrameTypes.PAIRING_PROMPT,
            mapOf(
                "deviceId" to "ca1peer1",
                "displayName" to "ThinkPad X1",
                "code" to "ABC234",
                "remainingMilliseconds" to 30_000L
            ),
            nowMs = 2_000L
        )
        snapshot = r2.snapshot
        assertEquals("ABC234", snapshot.pairing?.code)

        // 3. Pairing ended as paired + device becomes trusted
        val r3 = NativeProtocolMapper.applyPublishedFrame(
            snapshot,
            FrameTypes.PAIRING_ENDED,
            mapOf("deviceId" to "ca1peer1", "outcome" to "paired"),
            nowMs = 3_000L
        )
        snapshot = r3.snapshot
        assertNull(snapshot.pairing)
        assertEquals(listOf(UiEventNotice.Paired("ThinkPad X1")), r3.notices)

        // 4. Inbound file prompt -> accept -> progress -> transfer ended
        val r4 = NativeProtocolMapper.applyPublishedFrame(
            snapshot,
            FrameTypes.FILE_PROMPT,
            mapOf(
                "transferId" to "tx-1",
                "deviceId" to "ca1peer1",
                "fileName" to "archive.zip",
                "totalSize" to (2 * 65536L),
                "remainingMilliseconds" to 25_000L
            ),
            nowMs = 4_000L
        )
        snapshot = r4.snapshot
        assertEquals(1, snapshot.filePrompts.size)

        val r5 = NativeProtocolMapper.applyPublishedFrame(
            snapshot,
            FrameTypes.TRANSFER_PROGRESS,
            mapOf(
                "transferId" to "tx-1",
                "deviceId" to "ca1peer1",
                "direction" to "receive",
                "fileName" to "archive.zip",
                "segmentCount" to 2,
                "completedSegments" to 1,
                "moveBlocked" to false
            ),
            nowMs = 5_000L
        )
        snapshot = r5.snapshot
        assertTrue(snapshot.filePrompts.isEmpty())
        assertEquals(1, snapshot.transfers.size)
        assertEquals(TransferDirection.RECEIVE, snapshot.transfers.single().direction)
        assertEquals(1, snapshot.telemetry.verifiedSegmentsCount)

        val r6 = NativeProtocolMapper.applyPublishedFrame(
            snapshot,
            FrameTypes.TRANSFER_ENDED,
            mapOf("transferId" to "tx-1", "outcome" to "done"),
            nowMs = 6_000L
        )
        snapshot = r6.snapshot
        assertTrue(snapshot.transfers.single().completed)
        assertEquals(2, snapshot.transfers.single().completedSegments)
        assertEquals(1, snapshot.debugState.committedDownloadCount)
        assertEquals(listOf(UiEventNotice.TransferCompleted("archive.zip")), r6.notices)
    }
}

