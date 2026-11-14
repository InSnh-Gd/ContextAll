package app.contextall.nativebridge

/**
 * Dependency-free RFC 8259 JSON parser and serializer that works identically
 * on Android ART and in plain JVM unit tests (where android.jar stubs throw).
 */
internal object MiniJson {

    fun parseObject(json: String): Map<String, Any?>? =
        try {
            Parser(json).parseRoot() as? Map<*, *>
        } catch (_: Exception) {
            null
        }?.entries?.associate { (k, v) -> k.toString() to v }

    fun encodeObject(fields: Map<String, Any?>): String {
        val sb = StringBuilder()
        appendValue(sb, fields)
        return sb.toString()
    }

    private fun appendValue(sb: StringBuilder, value: Any?) {
        when (value) {
            null -> sb.append("null")
            is Boolean -> sb.append(if (value) "true" else "false")
            is Number -> sb.append(value.toString())
            is String -> appendString(sb, value)
            is Map<*, *> -> {
                sb.append('{')
                var first = true
                for ((k, v) in value) {
                    if (v == null) continue
                    if (!first) sb.append(',')
                    first = false
                    appendString(sb, k.toString())
                    sb.append(':')
                    appendValue(sb, v)
                }
                sb.append('}')
            }
            is Iterable<*> -> {
                sb.append('[')
                var first = true
                for (item in value) {
                    if (!first) sb.append(',')
                    first = false
                    appendValue(sb, item)
                }
                sb.append(']')
            }
            else -> appendString(sb, value.toString())
        }
    }

    private fun appendString(sb: StringBuilder, s: String) {
        sb.append('"')
        for (ch in s) {
            when (ch) {
                '\\' -> sb.append("\\\\")
                '"' -> sb.append("\\\"")
                '\b' -> sb.append("\\b")
                '\u000C' -> sb.append("\\f")
                '\n' -> sb.append("\\n")
                '\r' -> sb.append("\\r")
                '\t' -> sb.append("\\t")
                else -> if (ch.code < 0x20) {
                    sb.append("\\u%04x".format(ch.code))
                } else {
                    sb.append(ch)
                }
            }
        }
        sb.append('"')
    }

    private class Parser(private val src: String) {
        private var pos = 0

        fun parseRoot(): Any? {
            skipWs()
            val value = readValue()
            skipWs()
            return value
        }

        private fun readValue(): Any? {
            skipWs()
            if (pos >= src.length) throw IllegalStateException("Unexpected EOF")
            return when (val c = src[pos]) {
                '{' -> readObject()
                '[' -> readArray()
                '"' -> readString()
                't', 'f' -> readBool()
                'n' -> readNull()
                else -> if (c == '-' || c.isDigit()) readNumber() else throw IllegalStateException("Bad token '$c'")
            }
        }

        private fun readObject(): Map<String, Any?> {
            pos++ // '{'
            val map = LinkedHashMap<String, Any?>()
            skipWs()
            if (pos < src.length && src[pos] == '}') {
                pos++
                return map
            }
            while (true) {
                skipWs()
                val key = readString()
                skipWs()
                if (pos >= src.length || src[pos] != ':') throw IllegalStateException("Expected ':'")
                pos++
                map[key] = readValue()
                skipWs()
                if (pos >= src.length) throw IllegalStateException("Unterminated object")
                when (src[pos++]) {
                    '}' -> return map
                    ',' -> continue
                    else -> throw IllegalStateException("Expected ',' or '}'")
                }
            }
        }

        private fun readArray(): List<Any?> {
            pos++ // '['
            val list = ArrayList<Any?>()
            skipWs()
            if (pos < src.length && src[pos] == ']') {
                pos++
                return list
            }
            while (true) {
                list.add(readValue())
                skipWs()
                if (pos >= src.length) throw IllegalStateException("Unterminated array")
                when (src[pos++]) {
                    ']' -> return list
                    ',' -> continue
                    else -> throw IllegalStateException("Expected ',' or ']'")
                }
            }
        }

        private fun readString(): String {
            if (src[pos] != '"') throw IllegalStateException("Expected '\"'")
            pos++
            val sb = StringBuilder()
            while (pos < src.length) {
                val c = src[pos++]
                when (c) {
                    '"' -> return sb.toString()
                    '\\' -> {
                        if (pos >= src.length) throw IllegalStateException("Unterminated escape")
                        when (val esc = src[pos++]) {
                            '"', '\\', '/' -> sb.append(esc)
                            'b' -> sb.append('\b')
                            'f' -> sb.append('\u000C')
                            'n' -> sb.append('\n')
                            'r' -> sb.append('\r')
                            't' -> sb.append('\t')
                            'u' -> {
                                val hex = src.substring(pos, pos + 4)
                                sb.append(hex.toInt(16).toChar())
                                pos += 4
                            }
                            else -> throw IllegalStateException("Bad escape \\$esc")
                        }
                    }
                    else -> sb.append(c)
                }
            }
            throw IllegalStateException("Unterminated string")
        }

        private fun readBool(): Boolean {
            return if (src.startsWith("true", pos)) {
                pos += 4
                true
            } else if (src.startsWith("false", pos)) {
                pos += 5
                false
            } else {
                throw IllegalStateException("Invalid boolean")
            }
        }

        private fun readNull(): Any? {
            if (src.startsWith("null", pos)) {
                pos += 4
                return null
            }
            throw IllegalStateException("Invalid null")
        }

        private fun readNumber(): Number {
            val start = pos
            if (src[pos] == '-') pos++
            while (pos < src.length && src[pos].isDigit()) pos++
            var isFloat = false
            if (pos < src.length && src[pos] == '.') {
                isFloat = true
                pos++
                while (pos < src.length && src[pos].isDigit()) pos++
            }
            if (pos < src.length && (src[pos] == 'e' || src[pos] == 'E')) {
                isFloat = true
                pos++
                if (pos < src.length && (src[pos] == '+' || src[pos] == '-')) pos++
                while (pos < src.length && src[pos].isDigit()) pos++
            }
            val token = src.substring(start, pos)
            return if (isFloat) token.toDouble() else token.toLong()
        }

        private fun skipWs() {
            while (pos < src.length && src[pos].isWhitespace()) pos++
        }
    }
}

internal fun Map<String, Any?>.str(key: String): String? = this[key] as? String
internal fun Map<String, Any?>.bool(key: String): Boolean? = this[key] as? Boolean
internal fun Map<String, Any?>.long(key: String): Long? = (this[key] as? Number)?.toLong()
internal fun Map<String, Any?>.int(key: String): Int? = (this[key] as? Number)?.toInt()

@Suppress("UNCHECKED_CAST")
internal fun Map<String, Any?>.obj(key: String): Map<String, Any?>? = this[key] as? Map<String, Any?>

@Suppress("UNCHECKED_CAST")
internal fun Map<String, Any?>.objList(key: String): List<Map<String, Any?>> =
    (this[key] as? List<*>)?.mapNotNull { it as? Map<String, Any?> } ?: emptyList()

