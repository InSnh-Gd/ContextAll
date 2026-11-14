package app.contextall.nativebridge

import android.content.ContentResolver
import android.content.ContentValues
import android.content.Context
import android.net.Uri
import android.os.Build
import android.os.Environment
import android.provider.MediaStore
import app.contextall.core.DownloadNames
import java.io.File
import java.io.FileInputStream
import java.security.MessageDigest

internal object MediaStoreTransferSink {

    private val lock = Any()

    fun commitInbound(
        context: Context,
        tempFilePath: String,
        fileName: String,
        rootHashHex: String,
        fallbackDirectory: String
    ): Pair<Boolean, String?> {
        val source = File(tempFilePath)
        if (!source.isFile || !verifySha256(source, rootHashHex)) {
            return false to null
        }

        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.Q) {
            return commitToDirectory(source, fileName, fallbackDirectory)
        }

        val resolver = context.contentResolver
        val collection = MediaStore.Downloads.EXTERNAL_CONTENT_URI
        val sanitized = DownloadNames.sanitize(fileName)
        val uniqueName = synchronized(lock) {
            DownloadNames.avoidCollision(sanitized) { candidate ->
                existsInMediaStoreDownloads(resolver, collection, candidate)
            }
        }

        val values = ContentValues().apply {
            put(MediaStore.MediaColumns.DISPLAY_NAME, uniqueName)
            put(MediaStore.MediaColumns.RELATIVE_PATH, Environment.DIRECTORY_DOWNLOADS)
            put(MediaStore.MediaColumns.IS_PENDING, 1)
        }

        var itemUri: Uri? = null
        return try {
            itemUri = resolver.insert(collection, values)
            if (itemUri == null) {
                return false to null
            }

            resolver.openOutputStream(itemUri)?.use { out ->
                FileInputStream(source).use { input ->
                    input.copyTo(out)
                }
                out.flush()
            } ?: run {
                resolver.delete(itemUri, null, null)
                return false to null
            }

            val publishValues = ContentValues().apply {
                put(MediaStore.MediaColumns.IS_PENDING, 0)
            }
            resolver.update(itemUri, publishValues, null, null)
            source.delete()
            true to itemUri.toString()
        } catch (_: Exception) {
            itemUri?.let { uri ->
                runCatching { resolver.delete(uri, null, null) }
            }
            false to null
        }
    }

    private fun commitToDirectory(source: File, fileName: String, fallbackDir: String): Pair<Boolean, String?> {
        return try {
            val dir = File(fallbackDir).apply { mkdirs() }
            val safe = DownloadNames.sanitize(fileName)
            val unique = DownloadNames.avoidCollision(safe) { candidate -> File(dir, candidate).exists() }
            val target = File(dir, unique)
            source.copyTo(target, overwrite = false)
            source.delete()
            true to target.absolutePath
        } catch (_: Exception) {
            false to null
        }
    }

    private fun existsInMediaStoreDownloads(resolver: ContentResolver, collection: Uri, displayName: String): Boolean {
        return try {
            resolver.query(
                collection,
                arrayOf(MediaStore.MediaColumns._ID),
                "${MediaStore.MediaColumns.DISPLAY_NAME} = ?",
                arrayOf(displayName),
                null
            )?.use { cursor -> cursor.moveToFirst() } ?: false
        } catch (_: Exception) {
            false
        }
    }

    private fun verifySha256(file: File, expectedHex: String): Boolean {
        if (expectedHex.isBlank()) return false
        return try {
            val digest = MessageDigest.getInstance("SHA-256")
            val buffer = ByteArray(16 * 1024)
            FileInputStream(file).use { input ->
                while (true) {
                    val read = input.read(buffer)
                    if (read <= 0) break
                    digest.update(buffer, 0, read)
                }
            }
            val actualHex = digest.digest().joinToString("") { "%02x".format(it) }
            actualHex.equals(expectedHex, ignoreCase = true)
        } catch (_: Exception) {
            false
        }
    }
}

