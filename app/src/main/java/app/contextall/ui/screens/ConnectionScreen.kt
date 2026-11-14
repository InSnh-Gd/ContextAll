package app.contextall.ui.screens

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.rounded.KeyboardArrowRight
import androidx.compose.material.icons.rounded.AddLink
import androidx.compose.material.icons.rounded.Lan
import androidx.compose.material.icons.rounded.PowerSettingsNew
import androidx.compose.material.icons.rounded.Security
import androidx.compose.material.icons.rounded.SignalCellularAlt
import androidx.compose.material.icons.rounded.Sync
import androidx.compose.material.icons.rounded.Wifi
import androidx.compose.material3.FilledTonalButton
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.minimumInteractiveComponentSize
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import app.contextall.R
import app.contextall.model.PeerDeviceSnapshot
import app.contextall.model.PeerPresence
import app.contextall.model.SessionTelemetrySnapshot
import app.contextall.ui.components.ExpressiveGroupTile
import app.contextall.ui.components.ExpressiveIconBadge
import app.contextall.ui.components.ExpressiveScreenHeader
import app.contextall.ui.components.TelemetryAnalyticsCard
import app.contextall.ui.theme.JetBrainsMonoFamily
import app.contextall.ui.theme.LocalExpressivePalette

@Composable
fun ConnectionScreen(
    wifiLanReady: Boolean,
    linkServiceRunning: Boolean,
    devices: List<PeerDeviceSnapshot>,
    telemetry: SessionTelemetrySnapshot,
    headerActions: @Composable () -> Unit,
    onToggleLanWifi: () -> Unit,
    onToggleLinkService: () -> Unit,
    onOpenManualConnect: () -> Unit,
    onTogglePeerConnection: (String) -> Unit,
    onPairPeer: (PeerDeviceSnapshot) -> Unit,
    modifier: Modifier = Modifier
) {
    val palette = LocalExpressivePalette.current

    Box(modifier = modifier.fillMaxSize(), contentAlignment = Alignment.TopCenter) {
        LazyColumn(
            modifier = Modifier
                .fillMaxWidth()
                .widthIn(max = 640.dp)
                .testTag("connection_screen"),
            contentPadding = PaddingValues(start = 16.dp, end = 16.dp, top = 8.dp, bottom = 128.dp),
            verticalArrangement = Arrangement.spacedBy(20.dp)
        ) {
            item {
                ExpressiveScreenHeader(
                    title = stringResource(R.string.header_connection_title),
                    subtitle = stringResource(R.string.header_connection_subtitle),
                    trailingActions = headerActions
                )
            }

            // 1. Live LAN QUIC Link Control + Manual Direct Connect
            item {
                Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    ConnectionLinkControlCard(
                        wifiLanReady = wifiLanReady,
                        linkServiceRunning = linkServiceRunning,
                        onToggleLanWifi = onToggleLanWifi,
                        onToggleLinkService = onToggleLinkService
                    )
                    ExpressiveGroupTile(
                        index = 0,
                        count = 1,
                        onClick = onOpenManualConnect,
                        modifier = Modifier.testTag("connection_manual_connect_tile")
                    ) {
                        Row(
                            modifier = Modifier.padding(horizontal = 18.dp, vertical = 14.dp),
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            ExpressiveIconBadge(
                                icon = Icons.Rounded.AddLink,
                                backgroundColor = palette.skyIconBg,
                                iconTint = palette.skyIconFg
                            )
                            Spacer(modifier = Modifier.width(14.dp))
                            Column(modifier = Modifier.weight(1f)) {
                                Text(
                                    text = stringResource(R.string.manual_connect_entry_title),
                                    style = MaterialTheme.typography.titleSmall
                                )
                                Spacer(modifier = Modifier.height(2.dp))
                                Text(
                                    text = stringResource(R.string.manual_connect_entry_subtitle),
                                    style = MaterialTheme.typography.bodySmall,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant
                                )
                            }
                            Icon(
                                imageVector = Icons.AutoMirrored.Rounded.KeyboardArrowRight,
                                contentDescription = null,
                                tint = MaterialTheme.colorScheme.onSurfaceVariant
                            )
                        }
                    }
                }
            }

            // 2. Interactive Connection Success Rate & Throughput Visualization
            if (telemetry.buckets.isNotEmpty()) {
                item {
                    SectionHeader(text = stringResource(R.string.section_telemetry_analytics))
                    Spacer(modifier = Modifier.height(10.dp))
                    TelemetryAnalyticsCard(telemetry = telemetry)
                }
            }

            // 3. Local Listener & Cryptographic Protocol Specs
            item {
                SectionHeader(text = stringResource(R.string.connection_protocol_section))
                Spacer(modifier = Modifier.height(10.dp))
                Column(verticalArrangement = Arrangement.spacedBy(2.dp)) {
                    ExpressiveGroupTile(index = 0, count = 2) {
                        Row(
                            modifier = Modifier.padding(horizontal = 18.dp, vertical = 15.dp),
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            ExpressiveIconBadge(
                                icon = Icons.Rounded.Lan,
                                backgroundColor = palette.mintIconBg,
                                iconTint = palette.mintIconFg
                            )
                            Spacer(modifier = Modifier.width(16.dp))
                            Column(modifier = Modifier.weight(1f)) {
                                Text(
                                    text = stringResource(R.string.connection_quic_port_label),
                                    style = MaterialTheme.typography.bodySmall,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant
                                )
                                Spacer(modifier = Modifier.height(2.dp))
                                Text(
                                    text = stringResource(R.string.connection_quic_port_value),
                                    fontFamily = JetBrainsMonoFamily,
                                    fontSize = 13.sp,
                                    color = MaterialTheme.colorScheme.onSurface
                                )
                            }
                        }
                    }

                    ExpressiveGroupTile(index = 1, count = 2) {
                        Row(
                            modifier = Modifier.padding(horizontal = 18.dp, vertical = 15.dp),
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            ExpressiveIconBadge(
                                icon = Icons.Rounded.Security,
                                backgroundColor = palette.violetIconBg,
                                iconTint = palette.violetIconFg
                            )
                            Spacer(modifier = Modifier.width(16.dp))
                            Column(modifier = Modifier.weight(1f)) {
                                Text(
                                    text = stringResource(R.string.connection_crypto_label),
                                    style = MaterialTheme.typography.bodySmall,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant
                                )
                                Spacer(modifier = Modifier.height(2.dp))
                                Text(
                                    text = stringResource(R.string.connection_crypto_value),
                                    style = MaterialTheme.typography.titleSmall
                                )
                            }
                        }
                    }
                }
            }

            // 4. Per-Peer Active Session Links & Endpoints
            if (devices.isNotEmpty()) {
                item {
                    SectionHeader(
                        text = stringResource(R.string.connection_peers_section, devices.size)
                    )
                    Spacer(modifier = Modifier.height(10.dp))
                    Column(verticalArrangement = Arrangement.spacedBy(2.dp)) {
                        devices.forEachIndexed { index, peer ->
                            val (badgeBg, badgeFg) = when {
                                peer.presence == PeerPresence.CONNECTED ->
                                    palette.mintIconBg to palette.mintIconFg
                                !peer.trusted ->
                                    palette.amberIconBg to palette.amberIconFg
                                else ->
                                    palette.skyIconBg to palette.skyIconFg
                            }
                            ExpressiveGroupTile(
                                index = index,
                                count = devices.size,
                                modifier = Modifier.testTag("connection_peer_row_${peer.deviceId}")
                            ) {
                                Row(
                                    modifier = Modifier.padding(horizontal = 18.dp, vertical = 15.dp),
                                    verticalAlignment = Alignment.CenterVertically
                                ) {
                                    ExpressiveIconBadge(
                                        icon = peer.formFactor.toIcon(),
                                        backgroundColor = badgeBg,
                                        iconTint = badgeFg
                                    )
                                    Spacer(modifier = Modifier.width(14.dp))
                                    Column(modifier = Modifier.weight(1f)) {
                                        Row(
                                            verticalAlignment = Alignment.CenterVertically,
                                            horizontalArrangement = Arrangement.spacedBy(8.dp)
                                        ) {
                                            Text(
                                                text = peer.displayName,
                                                style = MaterialTheme.typography.titleSmall,
                                                maxLines = 1,
                                                overflow = TextOverflow.Ellipsis,
                                                modifier = Modifier.weight(1f, fill = false)
                                            )
                                            PresenceBadge(presence = peer.presence)
                                        }
                                        Spacer(modifier = Modifier.height(3.dp))
                                        Text(
                                            text = if (peer.endpoint != null) {
                                                stringResource(R.string.device_endpoint_format, peer.endpoint)
                                            } else {
                                                stringResource(R.string.connection_peer_discovery_mdns)
                                            },
                                            fontFamily = JetBrainsMonoFamily,
                                            fontSize = 11.5.sp,
                                            color = MaterialTheme.colorScheme.onSurfaceVariant
                                        )
                                    }
                                    Spacer(modifier = Modifier.width(8.dp))
                                    if (peer.trusted) {
                                        FilledTonalButton(
                                            onClick = { onTogglePeerConnection(peer.deviceId) },
                                            shape = CircleShape,
                                            contentPadding = PaddingValues(horizontal = 12.dp, vertical = 6.dp),
                                            modifier = Modifier
                                                .minimumInteractiveComponentSize()
                                                .testTag("connection_peer_toggle_${peer.deviceId}")
                                        ) {
                                            Icon(
                                                imageVector = Icons.Rounded.Sync,
                                                contentDescription = null,
                                                modifier = Modifier.size(15.dp)
                                            )
                                            Spacer(modifier = Modifier.width(4.dp))
                                            Text(
                                                text = stringResource(
                                                    if (peer.presence == PeerPresence.CONNECTED) {
                                                        R.string.connection_action_disconnect
                                                    } else {
                                                        R.string.connection_action_reconnect
                                                    }
                                                ),
                                                style = MaterialTheme.typography.labelMedium
                                            )
                                        }
                                    } else if (peer.presence != PeerPresence.OFFLINE) {
                                        FilledTonalButton(
                                            onClick = { onPairPeer(peer) },
                                            shape = CircleShape,
                                            contentPadding = PaddingValues(horizontal = 14.dp, vertical = 6.dp),
                                            modifier = Modifier
                                                .minimumInteractiveComponentSize()
                                                .testTag("connection_peer_pair_${peer.deviceId}")
                                        ) {
                                            Text(
                                                text = stringResource(R.string.connection_action_pair),
                                                style = MaterialTheme.typography.labelMedium
                                            )
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun ConnectionLinkControlCard(
    wifiLanReady: Boolean,
    linkServiceRunning: Boolean,
    onToggleLanWifi: () -> Unit,
    onToggleLinkService: () -> Unit
) {
    val active = wifiLanReady && linkServiceRunning
    val containerColor = when {
        !linkServiceRunning -> MaterialTheme.colorScheme.errorContainer.copy(alpha = 0.72f)
        wifiLanReady -> MaterialTheme.colorScheme.primaryContainer
        else -> MaterialTheme.colorScheme.surfaceContainerHigh
    }
    val contentColor = when {
        !linkServiceRunning -> MaterialTheme.colorScheme.onErrorContainer
        wifiLanReady -> MaterialTheme.colorScheme.onPrimaryContainer
        else -> MaterialTheme.colorScheme.onSurface
    }
    val pillBg = when {
        !linkServiceRunning -> MaterialTheme.colorScheme.error
        active -> MaterialTheme.colorScheme.primary
        else -> MaterialTheme.colorScheme.surfaceContainerHighest
    }
    val pillFg = when {
        !linkServiceRunning -> MaterialTheme.colorScheme.onError
        active -> MaterialTheme.colorScheme.onPrimary
        else -> MaterialTheme.colorScheme.onSurfaceVariant
    }
    val icon = when {
        !linkServiceRunning -> Icons.Rounded.PowerSettingsNew
        wifiLanReady -> Icons.Rounded.Wifi
        else -> Icons.Rounded.SignalCellularAlt
    }
    val titleRes = when {
        !linkServiceRunning -> R.string.status_service_stopped_title
        wifiLanReady -> R.string.status_lan_ready_title
        else -> R.string.status_cellular_paused_title
    }
    val subRes = when {
        !linkServiceRunning -> R.string.status_service_stopped_subtitle
        wifiLanReady -> R.string.status_lan_ready_subtitle
        else -> R.string.status_cellular_paused_subtitle
    }
    val pillRes = when {
        !linkServiceRunning -> R.string.status_service_stopped_pill
        wifiLanReady -> R.string.status_lan_ready_pill
        else -> R.string.status_cellular_paused_pill
    }

    Surface(
        shape = RoundedCornerShape(28.dp),
        color = containerColor,
        modifier = Modifier
            .fillMaxWidth()
            .testTag("connection_link_hero_card")
    ) {
        Column(modifier = Modifier.padding(horizontal = 20.dp, vertical = 18.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Surface(
                    shape = CircleShape,
                    color = pillBg,
                    modifier = Modifier.size(46.dp)
                ) {
                    Box(contentAlignment = Alignment.Center) {
                        Icon(
                            imageVector = icon,
                            contentDescription = null,
                            tint = pillFg,
                            modifier = Modifier.size(24.dp)
                        )
                    }
                }
                Spacer(modifier = Modifier.width(16.dp))
                Column(modifier = Modifier.weight(1f)) {
                    Text(
                        text = stringResource(titleRes),
                        style = MaterialTheme.typography.titleMedium,
                        color = contentColor
                    )
                    Spacer(modifier = Modifier.height(2.dp))
                    Text(
                        text = stringResource(subRes),
                        style = MaterialTheme.typography.bodySmall,
                        color = contentColor.copy(alpha = 0.82f)
                    )
                }
                Spacer(modifier = Modifier.width(10.dp))
                Surface(shape = CircleShape, color = pillBg) {
                    Text(
                        text = stringResource(pillRes),
                        style = MaterialTheme.typography.labelMedium,
                        color = pillFg,
                        modifier = Modifier.padding(horizontal = 12.dp, vertical = 6.dp)
                    )
                }
            }

            Spacer(modifier = Modifier.height(14.dp))

            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(10.dp)
            ) {
                FilledTonalButton(
                    onClick = onToggleLanWifi,
                    shape = CircleShape,
                    modifier = Modifier
                        .weight(1f)
                        .height(48.dp)
                        .testTag("connection_toggle_wifi_button")
                ) {
                    Icon(
                        imageVector = Icons.Rounded.Wifi,
                        contentDescription = null,
                        modifier = Modifier.size(18.dp)
                    )
                    Spacer(modifier = Modifier.width(6.dp))
                    Text(
                        text = stringResource(R.string.connection_toggle_wifi_btn),
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis
                    )
                }

                OutlinedButton(
                    onClick = onToggleLinkService,
                    shape = RoundedCornerShape(16.dp),
                    modifier = Modifier
                        .weight(1f)
                        .height(48.dp)
                        .testTag("connection_toggle_service_button")
                ) {
                    Icon(
                        imageVector = Icons.Rounded.PowerSettingsNew,
                        contentDescription = null,
                        modifier = Modifier.size(18.dp)
                    )
                    Spacer(modifier = Modifier.width(6.dp))
                    Text(
                        text = stringResource(R.string.connection_toggle_service_btn),
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis
                    )
                }
            }
        }
    }
}
