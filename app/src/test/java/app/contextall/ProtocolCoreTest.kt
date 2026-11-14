package app.contextall

import app.contextall.core.Base32
import app.contextall.core.DeviceIds
import app.contextall.core.DisplayNames
import app.contextall.core.DownloadNames
import app.contextall.core.EndpointValidator
import app.contextall.core.ProtocolLimits
import app.contextall.core.computeSegmentCount
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class ProtocolCoreTest {

    @Test
    fun deviceIds_generatesValidCa1IdentifierAndSymmetricShortCode() {
        val spkiA = DeviceIds.generateP256Spki()
        val spkiB = DeviceIds.generateP256Spki()

        val idA = DeviceIds.text(spkiA)
        val idB = DeviceIds.text(spkiB)

        assertTrue(idA.startsWith("ca1"))
        assertTrue(idB.startsWith("ca1"))
        assertNotEquals(idA, idB)

        val codeAB = DeviceIds.shortCode(spkiA, spkiB)
        val codeBA = DeviceIds.shortCode(spkiB, spkiA)
        assertEquals(6, codeAB.length)
        assertEquals(codeAB, codeBA)
    }

    @Test
    fun base32_encodesExpectedBitWidths() {
        assertEquals("", Base32.encodeBits(byteArrayOf(), 0))
        assertEquals("aaaaaa", Base32.encodeBits(ByteArray(4), 30))
    }

    @Test
    fun displayNames_validatesLengthAndControlChars() {
        assertTrue(DisplayNames.isLegal("Studio Pixel"))
        assertFalse(DisplayNames.isLegal(""))
        assertFalse(DisplayNames.isLegal("Bad\nName"))
        assertFalse(DisplayNames.isLegal("a".repeat(65)))
        assertEquals(64, DisplayNames.codePointCount("a".repeat(64)))
        assertTrue(DisplayNames.isLegal("a".repeat(64)))
    }

    @Test
    fun downloadNames_sanitizesReservedAndHandlesCollisions() {
        assertEquals("download", DownloadNames.sanitize("CON"))
        assertEquals("download", DownloadNames.sanitize("aux.txt"))
        assertEquals("download", DownloadNames.sanitize(".."))
        assertEquals("report.pdf", DownloadNames.sanitize("/tmp/../../dir/report.pdf"))
        assertEquals("ab", DownloadNames.sanitize("a:b*?<>|"))

        val existing = setOf("notes.txt", "notes (2).txt")
        assertEquals("notes (3).txt", DownloadNames.avoidCollision("notes.txt") { it in existing })
        assertEquals("fresh.txt", DownloadNames.avoidCollision("fresh.txt") { it in existing })
    }

    @Test
    fun endpointValidator_validatesHostsPortsAndPastedInputs() {
        assertEquals("192.168.1.42" to "42461", EndpointValidator.parsePastedInput("192.168.1.42:42461"))
        assertEquals("fd00::1" to "42461", EndpointValidator.parsePastedInput("[fd00::1]:42461"))
        assertTrue(EndpointValidator.isValidHost("192.168.1.42"))
        assertTrue(EndpointValidator.isValidHost("[fe80::1]"))
        assertTrue(EndpointValidator.isValidHost("studio.local"))
        assertFalse(EndpointValidator.isValidHost("192.168.1"))
        assertFalse(EndpointValidator.isValidHost("bad host"))

        assertEquals(42461, EndpointValidator.parseValidPort("42461"))
        assertNull(EndpointValidator.parseValidPort("0"))
        assertNull(EndpointValidator.parseValidPort("65536"))
        assertEquals("[fd00::1]:42461", EndpointValidator.formatEndpoint("fd00::1", 42461))
        assertEquals("10.0.2.2:42461", EndpointValidator.formatEndpoint("10.0.2.2", 42461))
    }

    @Test
    fun computeSegmentCount_roundsUpBySegmentSize() {
        assertEquals(0, computeSegmentCount(0))
        assertEquals(1, computeSegmentCount(1))
        assertEquals(1, computeSegmentCount(ProtocolLimits.SEGMENT_SIZE.toLong()))
        assertEquals(2, computeSegmentCount(ProtocolLimits.SEGMENT_SIZE.toLong() + 1L))
    }
}

