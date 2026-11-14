package app.contextall.nativebridge

import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import java.io.File
import java.security.KeyFactory
import java.security.KeyPair
import java.security.KeyPairGenerator
import java.security.KeyStore
import java.security.PrivateKey
import java.security.PublicKey
import java.security.SecureRandom
import java.security.Signature
import java.security.cert.CertificateFactory
import java.security.cert.X509Certificate
import java.security.spec.ECGenParameterSpec
import java.security.spec.PKCS8EncodedKeySpec
import java.security.spec.X509EncodedKeySpec
import java.text.SimpleDateFormat
import java.util.Base64
import java.util.Date
import java.util.Locale
import java.util.TimeZone

data class PortableIdentityMaterial(
    val pfxBytes: ByteArray,
    val spkiBytes: ByteArray
)

/**
 * OpenSSL-free P-256 identity and self-signed PKCS#12 builder backed by JVM/Android JCA (`java.security`).
 *
 * Keeps `<noBackupDir>/identity/device.key` (PKCS#8 PEM) and `device.spki` (SubjectPublicKeyInfo DER)
 * aligned with `IdentityStore` on desktop while avoiding `libSystem.Security.Cryptography.Native.OpenSsl.a`
 * inside the `.NET` NativeAOT binary on Android.
 */
object NativeCryptoHelper {
    const val PFX_PASSWORD: String = "contextall"
    private const val CURVE_NAME = "secp256r1"
    private const val pemHeader = "-----BEGIN PRIVATE KEY-----"
    private const val pemFooter = "-----END PRIVATE KEY-----"
    private const val certPemHeader = "-----BEGIN CERTIFICATE-----"
    private const val certPemFooter = "-----END CERTIFICATE-----"

    private val random = SecureRandom()

    fun ensureIdentityAndBuildPfx(identityDir: File, reset: Boolean = false): PortableIdentityMaterial {
        identityDir.mkdirs()
        restrictDirectoryPermissions(identityDir)

        val keyFile = File(identityDir, "device.key")
        val spkiFile = File(identityDir, "device.spki")
        val certFile = File(identityDir, "device.crt")

        val keyPair = if (reset) {
            generateAndWriteIdentity(keyFile, spkiFile)
        } else {
            val keyExists = keyFile.exists()
            val spkiExists = spkiFile.exists()
            if (keyExists != spkiExists) {
                throw IllegalStateException("identity-incomplete")
            }
            if (!keyExists) {
                generateAndWriteIdentity(keyFile, spkiFile)
            } else {
                loadAndVerifyIdentity(keyFile, spkiFile)
            }
        }

        return generatePfxAndSpki(keyPair, certOutputFile = certFile)
    }

    fun generatePfxAndSpki(
        keyPair: KeyPair,
        nowMs: Long = System.currentTimeMillis(),
        certOutputFile: File? = null
    ): PortableIdentityMaterial {
        val spki = keyPair.public.encoded
        val certDer = buildSelfSignedX509Der(keyPair, nowMs)
        if (certOutputFile != null) {
            certOutputFile.writeText(encodePem(certPemHeader, certPemFooter, certDer), Charsets.US_ASCII)
            restrictFilePermissions(certOutputFile)
        }
        val certFactory = CertificateFactory.getInstance("X.509")
        val x509 = certFactory.generateCertificate(ByteArrayInputStream(certDer)) as X509Certificate

        val ks = KeyStore.getInstance("PKCS12")
        ks.load(null, null)
        val pwd = PFX_PASSWORD.toCharArray()
        ks.setKeyEntry("contextall", keyPair.private, pwd, arrayOf(x509))
        val out = ByteArrayOutputStream()
        ks.store(out, pwd)
        return PortableIdentityMaterial(pfxBytes = out.toByteArray(), spkiBytes = spki)
    }

    private fun generateAndWriteIdentity(keyFile: File, spkiFile: File): KeyPair {
        val generator = KeyPairGenerator.getInstance("EC")
        generator.initialize(ECGenParameterSpec(CURVE_NAME), random)
        val pair = generator.generateKeyPair()
        val pem = encodePkcs8Pem(pair.private.encoded)
        keyFile.writeText(pem, Charsets.US_ASCII)
        restrictFilePermissions(keyFile)
        spkiFile.writeBytes(pair.public.encoded)
        restrictFilePermissions(spkiFile)
        return pair
    }

    private fun loadAndVerifyIdentity(keyFile: File, spkiFile: File): KeyPair {
        val pkcs8 = decodePkcs8Pem(keyFile.readText(Charsets.US_ASCII))
        val spki = spkiFile.readBytes()
        val kf = KeyFactory.getInstance("EC")
        val priv = kf.generatePrivate(PKCS8EncodedKeySpec(pkcs8))
        val pub = kf.generatePublic(X509EncodedKeySpec(spki))
        if (!keyPairMatches(priv, pub)) {
            throw IllegalStateException("identity-mismatch")
        }
        return KeyPair(pub, priv)
    }

    private fun keyPairMatches(privateKey: PrivateKey, publicKey: PublicKey): Boolean {
        val challenge = ByteArray(32)
        random.nextBytes(challenge)
        val signer = Signature.getInstance("SHA256withECDSA")
        signer.initSign(privateKey, random)
        signer.update(challenge)
        val sig = signer.sign()

        val verifier = Signature.getInstance("SHA256withECDSA")
        verifier.initVerify(publicKey)
        verifier.update(challenge)
        return verifier.verify(sig)
    }

    private fun encodePkcs8Pem(der: ByteArray): String =
        encodePem(pemHeader, pemFooter, der)

    private fun encodePem(header: String, footer: String, der: ByteArray): String {
        val b64 = Base64.getEncoder().encodeToString(der)
        val sb = StringBuilder(header.length + footer.length + b64.length + 16)
        sb.append(header).append('\n')
        var idx = 0
        while (idx < b64.length) {
            val end = minOf(idx + 64, b64.length)
            sb.append(b64, idx, end).append('\n')
            idx = end
        }
        sb.append(footer).append('\n')
        return sb.toString()
    }

    private fun decodePkcs8Pem(pem: String): ByteArray {
        val body = pem.lineSequence()
            .map { it.trim() }
            .filter { it.isNotEmpty() && !it.startsWith("-----") }
            .joinToString("")
        if (body.isEmpty()) {
            throw IllegalStateException("identity-empty-key")
        }
        return Base64.getDecoder().decode(body)
    }

    private fun buildSelfSignedX509Der(keyPair: KeyPair, nowMs: Long): ByteArray {
        val version = derTlv(0xA0, derIntegerPositive(byteArrayOf(2)))
        val serialBytes = ByteArray(16).also { bytes ->
            random.nextBytes(bytes)
            bytes[0] = (bytes[0].toInt() and 0x7F).toByte()
            if (bytes.all { it.toInt() == 0 }) bytes[15] = 1
        }
        val serial = derIntegerPositive(serialBytes)
        // ecdsa-with-SHA256 (1.2.840.10045.4.3.2), parameters omitted per RFC 5758
        val sigAlg = derSequence(derOid(1, 2, 840, 10045, 4, 3, 2))
        // CN=contextall (2.5.4.3)
        val commonName = derSequence(
            derTlv(0x31, derSequence(derOid(2, 5, 4, 3), derUtf8("contextall")))
        )
        val notBeforeMs = nowMs - 86_400_000L
        val notAfterMs = nowMs + 5L * 365L * 86_400_000L
        val validity = derSequence(derX509Time(notBeforeMs), derX509Time(notAfterMs))
        val spki = keyPair.public.encoded

        val tbs = derSequence(version, serial, sigAlg, commonName, validity, commonName, spki)

        val signer = Signature.getInstance("SHA256withECDSA")
        signer.initSign(keyPair.private, random)
        signer.update(tbs)
        val signatureDer = signer.sign()
        val signatureBitString = derTlv(0x03, byteArrayOf(0) + signatureDer)

        return derSequence(tbs, sigAlg, signatureBitString)
    }

    private fun derX509Time(epochMs: Long): ByteArray {
        val utc = TimeZone.getTimeZone("UTC")
        val yearFmt = SimpleDateFormat("yyyy", Locale.US).apply { timeZone = utc }
        val year = yearFmt.format(Date(epochMs)).toInt()
        return if (year in 1950..2049) {
            val fmt = SimpleDateFormat("yyMMddHHmmss'Z'", Locale.US).apply { timeZone = utc }
            derTlv(0x17, fmt.format(Date(epochMs)).toByteArray(Charsets.US_ASCII))
        } else {
            val fmt = SimpleDateFormat("yyyyMMddHHmmss'Z'", Locale.US).apply { timeZone = utc }
            derTlv(0x18, fmt.format(Date(epochMs)).toByteArray(Charsets.US_ASCII))
        }
    }

    private fun derUtf8(value: String): ByteArray =
        derTlv(0x0C, value.toByteArray(Charsets.UTF_8))

    private fun derSequence(vararg elements: ByteArray): ByteArray {
        val total = elements.sumOf { it.size }
        val buf = ByteArrayOutputStream(total)
        for (el in elements) buf.write(el)
        return derTlv(0x30, buf.toByteArray())
    }

    private fun derIntegerPositive(magnitude: ByteArray): ByteArray {
        var firstNonZero = 0
        while (firstNonZero < magnitude.size - 1 && magnitude[firstNonZero].toInt() == 0) {
            firstNonZero++
        }
        val slice = magnitude.copyOfRange(firstNonZero, magnitude.size)
        val content = if ((slice[0].toInt() and 0x80) != 0) byteArrayOf(0) + slice else slice
        return derTlv(0x02, content)
    }

    private fun derOid(vararg arcs: Int): ByteArray {
        require(arcs.size >= 2)
        val out = ByteArrayOutputStream()
        out.write(arcs[0] * 40 + arcs[1])
        for (i in 2 until arcs.size) {
            writeBase128(out, arcs[i])
        }
        return derTlv(0x06, out.toByteArray())
    }

    private fun writeBase128(out: ByteArrayOutputStream, value: Int) {
        if (value == 0) {
            out.write(0)
            return
        }
        val parts = ByteArray(5)
        var count = 0
        var remaining = value
        while (remaining > 0) {
            parts[count++] = (remaining and 0x7F).toByte()
            remaining = remaining ushr 7
        }
        for (i in count - 1 downTo 0) {
            var b = parts[i].toInt() and 0x7F
            if (i > 0) b = b or 0x80
            out.write(b)
        }
    }

    private fun derTlv(tag: Int, content: ByteArray): ByteArray {
        val len = content.size
        val lenBytes = when {
            len < 0x80 -> byteArrayOf(len.toByte())
            len <= 0xFF -> byteArrayOf(0x81.toByte(), len.toByte())
            len <= 0xFFFF -> byteArrayOf(0x82.toByte(), (len ushr 8).toByte(), (len and 0xFF).toByte())
            else -> byteArrayOf(
                0x83.toByte(),
                (len ushr 16).toByte(),
                ((len ushr 8) and 0xFF).toByte(),
                (len and 0xFF).toByte()
            )
        }
        val out = ByteArray(1 + lenBytes.size + content.size)
        out[0] = tag.toByte()
        System.arraycopy(lenBytes, 0, out, 1, lenBytes.size)
        System.arraycopy(content, 0, out, 1 + lenBytes.size, content.size)
        return out
    }

    private fun restrictDirectoryPermissions(dir: File) {
        dir.setReadable(false, false)
        dir.setReadable(true, true)
        dir.setWritable(false, false)
        dir.setWritable(true, true)
        dir.setExecutable(false, false)
        dir.setExecutable(true, true)
    }

    private fun restrictFilePermissions(file: File) {
        file.setReadable(false, false)
        file.setReadable(true, true)
        file.setWritable(false, false)
        file.setWritable(true, true)
        file.setExecutable(false, false)
    }
}

