package app.contextall.nativebridge

import android.content.Context
import android.net.ConnectivityManager
import android.net.LinkProperties
import android.net.Network
import android.net.NetworkCapabilities
import android.net.NetworkRequest
import android.net.nsd.NsdManager
import android.net.nsd.NsdServiceInfo
import android.net.wifi.WifiManager
import android.os.Build
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.Inet4Address
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.NetworkInterface
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch

/**
 * Coordinates Android LAN discovery across:
 * 1) ConnectivityManager on-link prefixes -> SessionHost.NotifyNetworkChanged
 * 2) NsdManager (_contextall._udp) registration + browsing
 * 3) UDP beacon broadcast/receive (port 42462) encoded/decoded by SessionHost BeaconCodec
 */
internal class LanDiscoveryController(
    context: Context,
    private val scope: CoroutineScope,
    private val backend: NativeSessionBackend
) {
    private val appContext = context.applicationContext
    private val connectivityManager = appContext.getSystemService(ConnectivityManager::class.java)
    private val nsdManager = appContext.getSystemService(NsdManager::class.java)
    private val wifiManager = appContext.getSystemService(WifiManager::class.java)

    private val _wifiLanReady = MutableStateFlow(true)
    val wifiLanReady: StateFlow<Boolean> = _wifiLanReady.asStateFlow()

    @Volatile private var localDeviceId: String = ""
    @Volatile private var localDisplayName: String = "Android"
    @Volatile private var localQuicPort: Int = QUIC_PORT
    @Volatile private var started = false

    private var networkCallback: ConnectivityManager.NetworkCallback? = null
    private var multicastLock: WifiManager.MulticastLock? = null
    private var registrationListener: NsdManager.RegistrationListener? = null
    private var discoveryListener: NsdManager.DiscoveryListener? = null
    private var beaconSocket: DatagramSocket? = null
    private var beaconReceiveJob: Job? = null
    private var beaconBroadcastJob: Job? = null

    fun start(deviceId: String, displayName: String, quicPort: Int = QUIC_PORT) {
        if (started) return
        started = true
        localDeviceId = deviceId
        localDisplayName = displayName
        localQuicPort = quicPort

        registerNetworkMonitoring()
        refreshNetworkPrefixes()
        acquireMulticastLock()
        startNsd(deviceId, displayName, quicPort)
        startUdpBeaconLoops()
    }

    fun updateAdvertisement(deviceId: String, displayName: String, quicPort: Int) {
        val nameChanged = displayName != localDisplayName || deviceId != localDeviceId
        localDeviceId = deviceId
        localDisplayName = displayName
        localQuicPort = quicPort
        if (started && nameChanged) {
            restartNsdRegistration(deviceId, displayName, quicPort)
            sendSingleBeaconBurst()
        }
    }

    fun triggerRescan() {
        refreshNetworkPrefixes()
        sendSingleBeaconBurst()
        if (started) {
            restartNsdDiscovery()
        }
    }

    fun refreshNetworkPrefixes() {
        val prefixes = collectLanPrefixes()
        val csv = prefixes.joinToString(",")
        _wifiLanReady.value = prefixes.isNotEmpty()
        if (backend.isAvailable) {
            backend.notifyNetworkChanged(csv)
        }
    }

    fun stop() {
        if (!started) return
        started = false

        beaconReceiveJob?.cancel()
        beaconBroadcastJob?.cancel()
        beaconReceiveJob = null
        beaconBroadcastJob = null

        runCatching { beaconSocket?.close() }
        beaconSocket = null

        stopNsd()
        runCatching {
            if (multicastLock?.isHeld == true) {
                multicastLock?.release()
            }
        }
        multicastLock = null

        networkCallback?.let { cb ->
            runCatching { connectivityManager?.unregisterNetworkCallback(cb) }
        }
        networkCallback = null
    }

    private fun registerNetworkMonitoring() {
        val cm = connectivityManager ?: return
        val req = NetworkRequest.Builder()
            .addCapability(NetworkCapabilities.NET_CAPABILITY_NOT_VPN)
            .build()
        val cb = object : ConnectivityManager.NetworkCallback() {
            override fun onAvailable(network: Network) = refreshNetworkPrefixes()
            override fun onLost(network: Network) = refreshNetworkPrefixes()
            override fun onLinkPropertiesChanged(network: Network, linkProperties: LinkProperties) =
                refreshNetworkPrefixes()
        }
        networkCallback = cb
        runCatching { cm.registerNetworkCallback(req, cb) }
    }

    private fun collectLanPrefixes(): List<String> {
        val prefixes = LinkedHashSet<String>()
        val cm = connectivityManager
        if (cm != null) {
            val active = cm.activeNetwork
            val caps = active?.let { cm.getNetworkCapabilities(it) }
            val isLanTransport = caps == null ||
                caps.hasTransport(NetworkCapabilities.TRANSPORT_WIFI) ||
                caps.hasTransport(NetworkCapabilities.TRANSPORT_ETHERNET)
            if (active != null && isLanTransport) {
                cm.getLinkProperties(active)?.linkAddresses?.forEach { linkAddr ->
                    val addr = linkAddr.address
                    if (!addr.isLoopbackAddress && !addr.isMulticastAddress && !addr.isAnyLocalAddress) {
                        val host = addr.hostAddress?.substringBefore('%') ?: return@forEach
                        prefixes += "$host/${linkAddr.prefixLength}"
                    }
                }
            }
        }

        if (prefixes.isEmpty()) {
            runCatching {
                val ifaces = NetworkInterface.getNetworkInterfaces()
                while (ifaces != null && ifaces.hasMoreElements()) {
                    val ni = ifaces.nextElement()
                    if (!ni.isUp || ni.isLoopback) continue
                    for (ia in ni.interfaceAddresses) {
                        val addr = ia.address ?: continue
                        if (addr.isLoopbackAddress || addr.isMulticastAddress || addr.isAnyLocalAddress) continue
                        val host = addr.hostAddress?.substringBefore('%') ?: continue
                        prefixes += "$host/${ia.networkPrefixLength}"
                    }
                }
            }
        }

        return prefixes.toList()
    }

    private fun acquireMulticastLock() {
        runCatching {
            val lock = wifiManager?.createMulticastLock("ContextAllLan")
            lock?.setReferenceCounted(false)
            lock?.acquire()
            multicastLock = lock
        }
    }

    private fun startNsd(deviceId: String, displayName: String, port: Int) {
        registerNsdService(deviceId, displayName, port)
        startNsdDiscovery()
    }

    private fun registerNsdService(deviceId: String, displayName: String, port: Int) {
        val manager = nsdManager ?: return
        val info = NsdServiceInfo().apply {
            serviceName = displayName.ifBlank { "ContextAll" }
            serviceType = NSD_SERVICE_TYPE
            setPort(port)
            setAttribute("id", deviceId)
        }
        val listener = object : NsdManager.RegistrationListener {
            override fun onRegistrationFailed(serviceInfo: NsdServiceInfo?, errorCode: Int) {}
            override fun onUnregistrationFailed(serviceInfo: NsdServiceInfo?, errorCode: Int) {}
            override fun onServiceRegistered(serviceInfo: NsdServiceInfo?) {}
            override fun onServiceUnregistered(serviceInfo: NsdServiceInfo?) {}
        }
        registrationListener = listener
        runCatching { manager.registerService(info, NsdManager.PROTOCOL_DNS_SD, listener) }
    }

    private fun startNsdDiscovery() {
        val manager = nsdManager ?: return
        val listener = object : NsdManager.DiscoveryListener {
            override fun onDiscoveryStarted(regType: String?) {}
            override fun onDiscoveryStopped(serviceType: String?) {}
            override fun onStartDiscoveryFailed(serviceType: String?, errorCode: Int) {}
            override fun onStopDiscoveryFailed(serviceType: String?, errorCode: Int) {}
            override fun onServiceLost(serviceInfo: NsdServiceInfo?) {}

            override fun onServiceFound(serviceInfo: NsdServiceInfo?) {
                val found = serviceInfo ?: return
                resolveNsdService(manager, found)
            }
        }
        discoveryListener = listener
        runCatching { manager.discoverServices(NSD_SERVICE_TYPE, NsdManager.PROTOCOL_DNS_SD, listener) }
    }

    @Suppress("DEPRECATION")
    private fun resolveNsdService(manager: NsdManager, found: NsdServiceInfo) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.UPSIDE_DOWN_CAKE) {
            runCatching {
                manager.registerServiceInfoCallback(
                    found,
                    { r -> scope.launch(Dispatchers.IO) { r.run() } },
                    object : NsdManager.ServiceInfoCallback {
                        override fun onServiceInfoCallbackRegistrationFailed(errorCode: Int) {}
                        override fun onServiceUpdated(info: NsdServiceInfo) {
                            handleResolvedService(info)
                            runCatching { manager.unregisterServiceInfoCallback(this) }
                        }
                        override fun onServiceLost() {}
                        override fun onServiceInfoCallbackUnregistered() {}
                    }
                )
            }
        } else {
            runCatching {
                manager.resolveService(
                    found,
                    object : NsdManager.ResolveListener {
                        override fun onResolveFailed(serviceInfo: NsdServiceInfo?, errorCode: Int) {}
                        override fun onServiceResolved(serviceInfo: NsdServiceInfo?) {
                            if (serviceInfo != null) {
                                handleResolvedService(serviceInfo)
                            }
                        }
                    }
                )
            }
        }
    }

    @Suppress("DEPRECATION")
    private fun handleResolvedService(info: NsdServiceInfo) {
        val attrs = info.attributes ?: return
        val rawId = attrs["id"] ?: return
        val peerId = String(rawId, Charsets.UTF_8).trim()
        if (peerId.isEmpty() || peerId == localDeviceId) return

        val hostAddr = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.UPSIDE_DOWN_CAKE) {
            info.hostAddresses.firstOrNull { !it.isLoopbackAddress && !it.isAnyLocalAddress }?.hostAddress
        } else {
            info.host?.hostAddress
        }?.substringBefore('%') ?: return

        val port = info.port.takeIf { it in 1..65535 } ?: QUIC_PORT
        backend.pushDiscoveredPeer(peerId, hostAddr, port, info.serviceName)
    }

    private fun restartNsdRegistration(deviceId: String, displayName: String, port: Int) {
        val manager = nsdManager ?: return
        registrationListener?.let { old -> runCatching { manager.unregisterService(old) } }
        registrationListener = null
        registerNsdService(deviceId, displayName, port)
    }

    private fun restartNsdDiscovery() {
        val manager = nsdManager ?: return
        discoveryListener?.let { old -> runCatching { manager.stopServiceDiscovery(old) } }
        discoveryListener = null
        startNsdDiscovery()
    }

    private fun stopNsd() {
        val manager = nsdManager ?: return
        registrationListener?.let { runCatching { manager.unregisterService(it) } }
        discoveryListener?.let { runCatching { manager.stopServiceDiscovery(it) } }
        registrationListener = null
        discoveryListener = null
    }

    private fun startUdpBeaconLoops() {
        beaconReceiveJob = scope.launch(Dispatchers.IO) {
            val socket = runCatching {
                DatagramSocket(null).apply {
                    reuseAddress = true
                    broadcast = true
                    bind(InetSocketAddress(BEACON_PORT))
                }
            }.getOrNull() ?: return@launch

            beaconSocket = socket
            val buf = ByteArray(2048)
            while (isActive && started) {
                try {
                    val packet = DatagramPacket(buf, buf.size)
                    socket.receive(packet)
                    val addr = packet.address?.hostAddress?.substringBefore('%') ?: continue
                    val payload = packet.data.copyOfRange(packet.offset, packet.offset + packet.length)
                    backend.notifyBeaconPacket(payload, addr)
                } catch (_: Exception) {
                    if (!started) break
                }
            }
        }

        beaconBroadcastJob = scope.launch(Dispatchers.IO) {
            while (isActive && started) {
                sendSingleBeaconBurst()
                delay(BEACON_INTERVAL_MS)
            }
        }
    }

    private fun sendSingleBeaconBurst() {
        scope.launch(Dispatchers.IO) {
            val payload = backend.encodeLocalBeacon(localQuicPort, localDisplayName) ?: return@launch
            val targets = LinkedHashSet<InetAddress>()
            targets += InetAddress.getByName("255.255.255.255")
            runCatching {
                val ifaces = NetworkInterface.getNetworkInterfaces()
                while (ifaces != null && ifaces.hasMoreElements()) {
                    val ni = ifaces.nextElement()
                    if (!ni.isUp || ni.isLoopback) continue
                    for (ia in ni.interfaceAddresses) {
                        val bcast = ia.broadcast
                        if (bcast is Inet4Address) {
                            targets += bcast
                        }
                    }
                }
            }

            runCatching {
                DatagramSocket().use { sender ->
                    sender.broadcast = true
                    for (target in targets) {
                        runCatching {
                            sender.send(DatagramPacket(payload, payload.size, target, BEACON_PORT))
                        }
                    }
                }
            }
        }
    }

    companion object {
        const val NSD_SERVICE_TYPE = "_contextall._udp"
        const val QUIC_PORT = 42461
        const val BEACON_PORT = 42462
        private const val BEACON_INTERVAL_MS = 2_500L
    }
}
