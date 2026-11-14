package app.contextall.nativebridge

interface NativeSessionBackend {
    val isAvailable: Boolean
    val loadError: String?
    fun runSmoke(): Int
    fun isRunning(): Boolean
    fun start(noBackupDir: String, filesDir: String, displayName: String, localPfx: ByteArray, localSpki: ByteArray): String
    fun stop()
    fun resetIdentity(noBackupDir: String, filesDir: String, displayName: String, localPfx: ByteArray, localSpki: ByteArray): String
    fun getSnapshotJson(): String?
    fun submitCommandJson(frameType: Int, commandJson: String): String
    fun connectEndpoint(host: String, port: Int, alias: String?): String
    fun pollEventJson(timeoutMs: Int): String?
    fun completeInboundCommit(commitId: Long, ok: Boolean, committedTarget: String?)
    fun pushClipboardText(text: String)
    fun notifyNetworkChanged(prefixesCsv: String)
    fun pushDiscoveredPeer(deviceId: String, address: String, port: Int, displayName: String?)
    fun encodeLocalBeacon(port: Int, displayName: String?): ByteArray?
    fun notifyBeaconPacket(packetBytes: ByteArray, sourceAddress: String)
}

object NativeSessionJni : NativeSessionBackend {
    private val loadResult: Pair<Boolean, String?> by lazy {
        try {
            System.loadLibrary("msquic")
            System.loadLibrary("contextall_native")
            true to null
        } catch (err: UnsatisfiedLinkError) {
            false to (err.message ?: "UnsatisfiedLinkError")
        }
    }

    override val isAvailable: Boolean
        get() = loadResult.first

    override val loadError: String?
        get() = loadResult.second

    override fun runSmoke(): Int =
        if (isAvailable) nativeRunSmoke() else -1

    override fun isRunning(): Boolean =
        isAvailable && nativeIsRunning()

    override fun start(noBackupDir: String, filesDir: String, displayName: String, localPfx: ByteArray, localSpki: ByteArray): String =
        nativeStart(noBackupDir.utf8(), filesDir.utf8(), displayName.utf8(), localPfx, localSpki).fromUtf8()

    override fun stop() {
        if (isAvailable) nativeStop()
    }

    override fun resetIdentity(noBackupDir: String, filesDir: String, displayName: String, localPfx: ByteArray, localSpki: ByteArray): String =
        nativeResetIdentity(noBackupDir.utf8(), filesDir.utf8(), displayName.utf8(), localPfx, localSpki).fromUtf8()

    override fun getSnapshotJson(): String? =
        if (isAvailable) nativeGetSnapshotJson()?.fromUtf8() else null

    override fun submitCommandJson(frameType: Int, commandJson: String): String =
        nativeSubmitCommandJson(frameType, commandJson.utf8()).fromUtf8()

    override fun connectEndpoint(host: String, port: Int, alias: String?): String =
        nativeConnectEndpoint(host.utf8(), port, alias?.utf8()).fromUtf8()

    override fun pollEventJson(timeoutMs: Int): String? =
        if (isAvailable) nativePollEventJson(timeoutMs)?.fromUtf8() else null

    override fun completeInboundCommit(commitId: Long, ok: Boolean, committedTarget: String?) {
        if (isAvailable) {
            nativeCompleteInboundCommit(commitId, ok, committedTarget?.utf8())
        }
    }

    override fun pushClipboardText(text: String) {
        if (isAvailable) nativePushClipboardText(text.utf8())
    }

    override fun notifyNetworkChanged(prefixesCsv: String) {
        if (isAvailable) nativeNotifyNetworkChanged(prefixesCsv.utf8())
    }

    override fun pushDiscoveredPeer(deviceId: String, address: String, port: Int, displayName: String?) {
        if (isAvailable) {
            nativePushDiscoveredPeer(deviceId.utf8(), address.utf8(), port, displayName?.utf8())
        }
    }

    override fun encodeLocalBeacon(port: Int, displayName: String?): ByteArray? =
        if (isAvailable) nativeEncodeLocalBeacon(port, displayName?.utf8()) else null

    override fun notifyBeaconPacket(packetBytes: ByteArray, sourceAddress: String) {
        if (isAvailable) nativeNotifyBeaconPacket(packetBytes, sourceAddress.utf8())
    }

    private fun String.utf8(): ByteArray = toByteArray(Charsets.UTF_8)
    private fun ByteArray.fromUtf8(): String = String(this, Charsets.UTF_8)

    @JvmStatic private external fun nativeRunSmoke(): Int
    @JvmStatic private external fun nativeIsRunning(): Boolean
    @JvmStatic private external fun nativeStart(
        noBackupDirUtf8: ByteArray,
        filesDirUtf8: ByteArray,
        displayNameUtf8: ByteArray,
        localPfxBytes: ByteArray,
        localSpkiBytes: ByteArray
    ): ByteArray
    @JvmStatic private external fun nativeStop()
    @JvmStatic private external fun nativeResetIdentity(
        noBackupDirUtf8: ByteArray,
        filesDirUtf8: ByteArray,
        displayNameUtf8: ByteArray,
        localPfxBytes: ByteArray,
        localSpkiBytes: ByteArray
    ): ByteArray
    @JvmStatic private external fun nativeGetSnapshotJson(): ByteArray?
    @JvmStatic private external fun nativeSubmitCommandJson(frameType: Int, commandJsonUtf8: ByteArray): ByteArray
    @JvmStatic private external fun nativeConnectEndpoint(hostUtf8: ByteArray, port: Int, aliasUtf8: ByteArray?): ByteArray
    @JvmStatic private external fun nativePollEventJson(timeoutMs: Int): ByteArray?
    @JvmStatic private external fun nativeCompleteInboundCommit(commitId: Long, ok: Boolean, committedTargetUtf8: ByteArray?)
    @JvmStatic private external fun nativePushClipboardText(textUtf8: ByteArray)
    @JvmStatic private external fun nativeNotifyNetworkChanged(prefixesCsvUtf8: ByteArray)
    @JvmStatic private external fun nativePushDiscoveredPeer(deviceIdUtf8: ByteArray, addressUtf8: ByteArray, port: Int, displayNameUtf8: ByteArray?)
    @JvmStatic private external fun nativeEncodeLocalBeacon(port: Int, displayNameUtf8: ByteArray?): ByteArray?
    @JvmStatic private external fun nativeNotifyBeaconPacket(packetBytes: ByteArray, sourceAddressUtf8: ByteArray)
}

