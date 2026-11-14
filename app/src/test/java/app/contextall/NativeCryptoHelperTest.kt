package app.contextall

import app.contextall.nativebridge.NativeCryptoHelper
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File
import java.security.KeyStore
import java.security.cert.X509Certificate
import java.security.interfaces.ECPublicKey

class NativeCryptoHelperTest {
    @get:Rule
    val tempFolder = TemporaryFolder()

    @Test
    fun ensureIdentityAndBuildPfx_createsReloadsAndResetsValidP256Identity() {
        val dir = tempFolder.newFolder("identity")

        val first = NativeCryptoHelper.ensureIdentityAndBuildPfx(dir, reset = false)
        assertTrue(File(dir, "device.key").exists())
        assertTrue(File(dir, "device.spki").exists())
        assertTrue(File(dir, "device.crt").exists())
        assertTrue(File(dir, "device.crt").readText().contains("-----BEGIN CERTIFICATE-----"))
        assertEquals(91, first.spkiBytes.size)
        assertArrayEquals(first.spkiBytes, File(dir, "device.spki").readBytes())

        // PKCS#12 container opens cleanly with password "contextall" and contains self-signed P-256 X509 cert
        val ks = KeyStore.getInstance("PKCS12")
        ks.load(first.pfxBytes.inputStream(), NativeCryptoHelper.PFX_PASSWORD.toCharArray())
        val cert = ks.getCertificate("contextall") as X509Certificate
        assertEquals("CN=contextall", cert.subjectX500Principal.name)
        assertTrue(cert.publicKey is ECPublicKey)
        cert.verify(cert.publicKey)
        assertArrayEquals(first.spkiBytes, cert.publicKey.encoded)

        // Reloading reuses the persisted keypair and SPKI
        val second = NativeCryptoHelper.ensureIdentityAndBuildPfx(dir, reset = false)
        assertArrayEquals(first.spkiBytes, second.spkiBytes)

        // Resetting replaces the keypair cleanly
        val resetMaterial = NativeCryptoHelper.ensureIdentityAndBuildPfx(dir, reset = true)
        assertFalse(first.spkiBytes.contentEquals(resetMaterial.spkiBytes))
    }

    @Test
    fun ensureIdentityAndBuildPfx_rejectsIncompleteOrMismatchedIdentity() {
        val incomplete = tempFolder.newFolder("incomplete")
        File(incomplete, "device.key").writeText("-----BEGIN PRIVATE KEY-----\n-----END PRIVATE KEY-----\n")
        assertThrows(IllegalStateException::class.java) {
            NativeCryptoHelper.ensureIdentityAndBuildPfx(incomplete, reset = false)
        }

        val mismatchA = tempFolder.newFolder("mismatch-a")
        val mismatchB = tempFolder.newFolder("mismatch-b")
        NativeCryptoHelper.ensureIdentityAndBuildPfx(mismatchA, reset = false)
        NativeCryptoHelper.ensureIdentityAndBuildPfx(mismatchB, reset = false)
        File(mismatchB, "device.spki").copyTo(File(mismatchA, "device.spki"), overwrite = true)

        assertThrows(IllegalStateException::class.java) {
            NativeCryptoHelper.ensureIdentityAndBuildPfx(mismatchA, reset = false)
        }
    }
}

