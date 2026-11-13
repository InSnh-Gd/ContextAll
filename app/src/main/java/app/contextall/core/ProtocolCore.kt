package app.contextall.core

import java.math.BigInteger
import java.security.KeyPairGenerator
import java.security.MessageDigest
import java.security.spec.ECGenParameterSpec
import java.util.Locale

/**
 * Protocol frame type constants matching ContextAll.Protocol.FrameTypes (64..96).
 */
object FrameTypes {
    const val START_PAIRING: Int = 64
    const val ACCEPT_PAIRING: Int = 65
    const val REJECT_PAIRING: Int = 66
    const val UNPAIR_LOCAL: Int = 67
    const val SET_DISPLAY_NAME: Int = 68
    const val SET_CLIPBOARD_SYNC: Int = 69
    const val SET_ASK_BEFORE_SAVE: Int = 70
    const val SET_DOWNLOAD_DIRECTORY: Int = 71
    const val SEND_FILE: Int = 72
    const val ACCEPT_FILE: Int = 73
    const val REJECT_FILE: Int = 74
    const val CANCEL_TRANSFER: Int = 75

    const val SNAPSHOT: Int = 80
    const val DEVICE_CHANGED: Int = 81
    const val PAIRING_PROMPT: Int = 82
    const val PAIRING_ENDED: Int = 83
    const val FILE_PROMPT: Int = 84
    const val TRANSFER_PROGRESS: Int = 85
    const val TRANSFER_ENDED: Int = 86
    const val COMMAND_RESULT: Int = 96
}

object ProtocolLimits {
    const val MAX_PAYLOAD_BYTES: Int = 65_536
    const val SEGMENT_SIZE: Int = MAX_PAYLOAD_BYTES - 36 // 65,500 bytes
    const val MAX_FILE_BYTES: Long = 1_000_000_000L
    const val MAX_CLIPBOARD_BYTES: Int = 1024 * 1024 // 1 MiB
    const val PROMPT_TIMEOUT_MS: Long = 60_000L
    const val WINDOW_SEGMENTS: Int = 8
    const val DEFAULT_QUIC_PORT: Int = 42461
}

/**
 * Exact port of ContextAll.Core.Base32 ("abcdefghijklmnopqrstuvwxyz234567").
 */
object Base32 {
    private const val ALPHABET = "abcdefghijklmnopqrstuvwxyz234567"

    fun encode(data: ByteArray): String = encodeBits(data, data.size * 8)

    fun encodeBits(data: ByteArray, bits: Int): String {
        if (bits == 0) return ""
        val chars = (bits + 4) / 5
        var number = BigInteger(1, data)
        val total = data.size * 8
        if (total > bits) {
            number = number.shiftRight(total - bits)
        }
        val padLeft = chars * 5 - bits
        if (padLeft > 0) {
            number = number.shiftLeft(padLeft)
        }
        val mask = BigInteger.valueOf(31L)
        val output = CharArray(chars)
        for (i in chars - 1 downTo 0) {
            val idx = number.and(mask).toInt()
            output[i] = ALPHABET[idx]
            number = number.shiftRight(5)
        }
        return String(output)
    }
}

/**
 * Exact port of ContextAll.Core.DeviceIds using real ECDSA P-256 SPKI public keys.
 */
object DeviceIds {
    fun generateP256Spki(): ByteArray {
        val kpg = KeyPairGenerator.getInstance("EC")
        kpg.initialize(ECGenParameterSpec("secp256r1"))
        return kpg.generateKeyPair().public.encoded
    }

    fun sha256(bytes: ByteArray): ByteArray =
        MessageDigest.getInstance("SHA-256").digest(bytes)

    fun sha256Hex(bytes: ByteArray): String =
        sha256(bytes).joinToString("") { "%02x".format(it) }

    fun digest(spki: ByteArray): ByteArray =
        sha256(spki).copyOfRange(0, 16)

    fun text(spki: ByteArray): String =
        "ca1" + Base32.encode(digest(spki))

    fun compareDigests(left: ByteArray, right: ByteArray): Int {
        val len = minOf(left.size, right.size)
        for (i in 0 until len) {
            val l = left[i].toInt() and 0xFF
            val r = right[i].toInt() and 0xFF
            if (l != r) return l.compareTo(r)
        }
        return left.size.compareTo(right.size)
    }

    /**
     * Derives the 6-character 30-bit Base32 pairing verification short code from two SPKI keys.
     */
    fun shortCode(spkiA: ByteArray, spkiB: ByteArray): String {
        val firstIsA = compareDigests(digest(spkiA), digest(spkiB)) <= 0
        val first = if (firstIsA) spkiA else spkiB
        val second = if (firstIsA) spkiB else spkiA
        val joined = ByteArray(first.size + second.size)
        System.arraycopy(first, 0, joined, 0, first.size)
        System.arraycopy(second, 0, joined, first.size, second.size)
        return Base32.encodeBits(sha256(joined), 30).uppercase(Locale.ROOT)
    }
}

/**
 * Exact port of ContextAll.Core.DisplayNames.
 */
object DisplayNames {
    fun isLegal(name: String): Boolean {
        var count = 0
        var offset = 0
        while (offset < name.length) {
            val cp = name.codePointAt(offset)
            if (cp < 0x20) return false
            count++
            if (count > 64) return false
            offset += Character.charCount(cp)
        }
        return count >= 1
    }

    fun codePointCount(name: String): Int =
        name.codePointCount(0, name.length)
}

/**
 * Exact port of ContextAll.Core.DownloadNames.
 */
object DownloadNames {
    private val RESERVED = setOf(
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    )
    private const val FORBIDDEN = "\":*?<>|"

    fun sanitize(original: String): String {
        val segment = original.split('/', '\\').lastOrNull().orEmpty()
        val kept = StringBuilder()
        var count = 0
        var offset = 0
        while (offset < segment.length && count < 200) {
            val cp = segment.codePointAt(offset)
            val chStr = String(Character.toChars(cp))
            if (cp >= 0x20 && !FORBIDDEN.contains(chStr)) {
                kept.append(chStr)
                count++
            }
            offset += Character.charCount(cp)
        }
        val name = kept.toString()
        if (name.isEmpty() || name == "." || name == ".." || isReserved(name)) {
            return "download"
        }
        return name
    }

    fun avoidCollision(name: String, exists: (String) -> Boolean): String {
        if (!exists(name)) return name
        val dot = name.lastIndexOf('.')
        val stem = if (dot > 0) name.substring(0, dot) else name
        val ext = if (dot > 0) name.substring(dot) else ""
        var n = 2
        while (true) {
            val candidate = "$stem ($n)$ext"
            if (!exists(candidate)) return candidate
            n++
        }
    }

    private fun isReserved(name: String): Boolean {
        val upper = name.uppercase(Locale.ROOT)
        val stem = upper.substringBefore('.')
        return RESERVED.contains(upper) || RESERVED.contains(stem)
    }
}

fun formatByteSize(bytes: Long): String {
    if (bytes < 1024L) return "$bytes B"
    val kb = bytes / 1024.0
    if (kb < 1024.0) return String.format(Locale.US, "%.1f KB", kb)
    val mb = kb / 1024.0
    if (mb < 1024.0) return String.format(Locale.US, "%.1f MB", mb)
    val gb = mb / 1024.0
    return String.format(Locale.US, "%.2f GB", gb)
}

fun computeSegmentCount(totalBytes: Long): Int {
    if (totalBytes <= 0L) return 0
    return ((totalBytes + ProtocolLimits.SEGMENT_SIZE - 1L) / ProtocolLimits.SEGMENT_SIZE).toInt()
}

/**
 * Validates manual QUIC peer IPv4 / IPv6 / LAN hostname and port inputs,
 * with smart splitting when a user pastes "192.168.1.42:42461" or "[fd00::1]:42461".
 */
object EndpointValidator {
    private val HOSTNAME_LABEL_REGEX = Regex("^[a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?$")

    fun parsePastedInput(raw: String): Pair<String, String?> {
        val trimmed = raw.trim()
        // Bracketed IPv6 with port: [fd00::1]:42461
        if (trimmed.startsWith("[")) {
            val closeIdx = trimmed.indexOf(']')
            if (closeIdx > 1) {
                val ipv6 = trimmed.substring(1, closeIdx)
                val rest = trimmed.substring(closeIdx + 1)
                if (rest.startsWith(":") && rest.length > 1 && rest.substring(1).all { it.isDigit() }) {
                    return ipv6 to rest.substring(1)
                }
                return ipv6 to null
            }
        }
        // Single-colon host:port (IPv4 or hostname)
        val firstColon = trimmed.indexOf(':')
        val lastColon = trimmed.lastIndexOf(':')
        if (firstColon > 0 && firstColon == lastColon) {
            val hostPart = trimmed.substring(0, firstColon).trim()
            val portPart = trimmed.substring(firstColon + 1).trim()
            if (portPart.isNotEmpty() && portPart.all { it.isDigit() }) {
                return hostPart to portPart
            }
        }
        return raw to null
    }

    fun normalizeHost(raw: String): String =
        raw.trim().removeSurrounding("[", "]")

    fun isValidHost(raw: String): Boolean {
        val host = normalizeHost(raw)
        if (host.isEmpty() || host.length > 253 || host.any { it.isWhitespace() }) return false
        return isValidIpv4(host) || isValidIpv6(host) || isValidHostname(host)
    }

    fun parseValidPort(raw: String): Int? {
        val port = raw.trim().toIntOrNull() ?: return null
        return if (port in 1..65535) port else null
    }

    fun formatEndpoint(rawHost: String, port: Int): String {
        val cleanHost = normalizeHost(rawHost)
        return if (cleanHost.contains(':')) {
            "[$cleanHost]:$port"
        } else {
            "$cleanHost:$port"
        }
    }

    private fun isValidIpv4(s: String): Boolean {
        val parts = s.split('.')
        if (parts.size != 4) return false
        return parts.all { part ->
            if (part.isEmpty() || part.length > 3 || !part.all { it.isDigit() }) return false
            if (part.length > 1 && part.startsWith('0')) return false
            val num = part.toIntOrNull() ?: return false
            num in 0..255
        }
    }

    private fun isValidIpv6(s: String): Boolean {
        if (!s.contains(':')) return false
        val zoneStripped = s.substringBefore('%')
        if (zoneStripped.isEmpty()) return false
        val doubleColonCount = Regex("::").findAll(zoneStripped).count()
        if (doubleColonCount > 1) return false
        if (doubleColonCount == 1) {
            val halves = zoneStripped.split("::", limit = 2)
            val leftGroups = if (halves[0].isEmpty()) emptyList() else halves[0].split(':')
            val rightGroups = if (halves[1].isEmpty()) emptyList() else halves[1].split(':')
            if (leftGroups.size + rightGroups.size >= 8) return false
            return (leftGroups + rightGroups).all { isHexHextet(it) }
        } else {
            val groups = zoneStripped.split(':')
            return groups.size == 8 && groups.all { isHexHextet(it) }
        }
    }

    private fun isHexHextet(group: String): Boolean =
        group.length in 1..4 && group.all {
            it in '0'..'9' || it in 'a'..'f' || it in 'A'..'F'
        }

    private fun isValidHostname(s: String): Boolean {
        if (s.startsWith('.') || s.endsWith('.')) return false
        val labels = s.split('.')
        // Avoid treating partial numeric IPv4 prefixes like "192.168.1." or "192.168" as hostnames
        if (labels.all { it.all(Char::isDigit) }) return false
        return labels.all { HOSTNAME_LABEL_REGEX.matches(it) }
    }
}
