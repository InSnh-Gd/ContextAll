package app.contextall.ui.screens

import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.animateColorAsState
import androidx.compose.animation.core.Spring
import androidx.compose.animation.core.spring
import androidx.compose.animation.core.tween
import androidx.compose.animation.expandVertically
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.shrinkVertically
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyListState
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.rounded.KeyboardArrowRight
import androidx.compose.material.icons.rounded.AddLink
import androidx.compose.material.icons.rounded.Computer
import androidx.compose.material.icons.rounded.ContentPasteGo
import androidx.compose.material.icons.rounded.EditNote
import androidx.compose.material.icons.rounded.LaptopMac
import androidx.compose.material.icons.rounded.PowerSettingsNew
import androidx.compose.material.icons.rounded.SignalCellularAlt
import androidx.compose.material.icons.rounded.Smartphone
import androidx.compose.material.icons.rounded.VerifiedUser
import androidx.compose.material.icons.rounded.Wifi
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilledTonalButton
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.key
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import app.contextall.R
import app.contextall.core.ProtocolLimits
import app.contextall.model.ClipboardSyncState
import app.contextall.model.DeviceFormFactor
import app.contextall.model.PairingPromptSnapshot
import app.contextall.model.PeerDeviceSnapshot
import app.contextall.model.PeerPresence
import app.contextall.model.SessionTelemetrySnapshot
import app.contextall.ui.components.DiscoveredDeviceEntranceItem
import app.contextall.ui.components.DiscoveryRippleOrb
import app.contextall.ui.components.DiscoveryVisualMode
import app.contextall.ui.components.ExpressiveGapProgressBar
import app.contextall.ui.components.ExpressiveGroupTile
import app.contextall.ui.components.ExpressiveIconBadge
import app.contextall.ui.components.ExpressiveScreenHeader
import app.contextall.ui.components.NearbyDiscoveryScannerCard
import app.contextall.ui.components.NewlyDiscoveredPill
import app.contextall.ui.theme.JetBrainsMonoFamily
import app.contextall.ui.theme.LocalExpressivePalette
import java.util.Locale

@Composable
fun DevicesListScreen(
    wifiLanReady: Boolean,
    linkServiceRunning: Boolean,
    devices: List<PeerDeviceSnapshot>,
    clipboardState: ClipboardSyncState,
    selectedDeviceId: String?,
    headerActions: @Composable () -> Unit,
    onBannerClick: () -> Unit,
    onPushSystemClipboard: () -> Unit,
    onPushCustomClipboard: (String) -> Unit,
    onSelectTrustedDevice: (PeerDeviceSnapshot) -> Unit,
    onStartPairing: (PeerDeviceSnapshot) -> Unit,
    telemetry: SessionTelemetrySnapshot = SessionTelemetrySnapshot(),
    isScanningLan: Boolean = false,
    lastDiscoveredDeviceId: String? = null,
    onTriggerScan: () -> Unit = {},
    onOpenManualConnect: () -> Unit = {},
    modifier: Modifier = Modifier,
    listState: LazyListState = rememberLazyListState()
) {
    val trusted = remember(devices) { devices.filter { it.trusted } }
    val nearby = remember(devices) { devices.filterNot { it.trusted } }
    val lanAvailable = wifiLanReady && linkServiceRunning
    val mutualOnlineCount = remember(devices, lanAvailable) {
        if (lanAvailable) {
            devices.count { it.isMutualClipboardActive }
        } else {
            0
        }
    }
    var showComposeClipDialog by remember { mutableStateOf(false) }
    var clipDraft by remember { mutableStateOf("") }

    // Track already-rendered nearby peer IDs so newly discovered peers smoothly animate in
    val seenNearbyIds = remember {
        mutableSetOf<String>().apply {
            devices.filterNot { it.trusted }.forEach { add(it.deviceId) }
        }
    }

    Box(modifier = modifier.fillMaxSize(), contentAlignment = Alignment.TopCenter) {
        LazyColumn(
            state = listState,
            modifier = Modifier
                .fillMaxWidth()
                .widthIn(max = 640.dp)
                .testTag("devices_list"),
            contentPadding = PaddingValues(start = 16.dp, end = 16.dp, top = 8.dp, bottom = 148.dp),
            verticalArrangement = Arrangement.spacedBy(20.dp)
        ) {
            item {
                ExpressiveScreenHeader(
                    title = stringResource(R.string.header_devices_title),
                    subtitle = stringResource(R.string.header_devices_subtitle),
                    trailingActions = headerActions
                )
            }

            item {
                LinkQuickTileBanner(
                    wifiLanReady = wifiLanReady,
                    linkServiceRunning = linkServiceRunning,
                    onlinePeerCount = devices.count { it.presence == PeerPresence.CONNECTED },
                    successRate = telemetry.overallSuccessRate,
                    onClick = onBannerClick
                )
            }

            item {
                ClipboardQuickSyncCard(
                    mutualOnlineCount = mutualOnlineCount,
                    clipboardState = clipboardState,
                    onPushSystemClipboard = onPushSystemClipboard,
                    onOpenComposeDialog = {
                        clipDraft = clipboardState.lastSyncedText
                        showComposeClipDialog = true
                    }
                )
            }

            if (trusted.isEmpty() && nearby.isEmpty()) {
                item {
                    EmptyDevicesStateCard(
                        isScanning = isScanningLan && lanAvailable,
                        lanReady = lanAvailable,
                        onOpenManualConnect = onOpenManualConnect
                    )
                }
            }

            if (trusted.isNotEmpty()) {
                item {
                    SectionHeader(
                        text = stringResource(R.string.section_trusted_devices, trusted.size)
                    )
                    Spacer(modifier = Modifier.height(10.dp))
                    ExpressiveDeviceGroup(
                        peers = trusted,
                        selectedDeviceId = selectedDeviceId,
                        lastDiscoveredDeviceId = null,
                        seenIds = null,
                        onPeerClick = onSelectTrustedDevice
                    )
                }
            }

            // Nearby Discovery Section: M3 CircularProgressIndicator + expanding ripples + smooth device transition
            item {
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    SectionHeader(
                        text = stringResource(R.string.section_nearby_devices, nearby.size)
                    )
                    TextButton(
                        onClick = onOpenManualConnect,
                        modifier = Modifier.testTag("nearby_manual_connect_button")
                    ) {
                        Icon(
                            imageVector = Icons.Rounded.AddLink,
                            contentDescription = null,
                            modifier = Modifier.size(16.dp)
                        )
                        Spacer(modifier = Modifier.width(4.dp))
                        Text(
                            text = stringResource(R.string.manual_connect_button_short),
                            style = MaterialTheme.typography.labelLarge
                        )
                    }
                }
                Spacer(modifier = Modifier.height(6.dp))
                NearbyDiscoveryScannerCard(
                    isScanning = isScanningLan,
                    lanReady = lanAvailable,
                    nearbyCount = nearby.size,
                    onTriggerScan = onTriggerScan
                )
                AnimatedVisibility(
                    visible = nearby.isNotEmpty(),
                    enter = expandVertically(
                        expandFrom = Alignment.Top,
                        animationSpec = spring(
                            dampingRatio = Spring.DampingRatioNoBouncy,
                            stiffness = Spring.StiffnessMediumLow
                        )
                    ) + fadeIn(animationSpec = tween(240)),
                    exit = shrinkVertically(
                        shrinkTowards = Alignment.Top,
                        animationSpec = tween(180)
                    ) + fadeOut(animationSpec = tween(150))
                ) {
                    Column {
                        Spacer(modifier = Modifier.height(8.dp))
                        ExpressiveDeviceGroup(
                            peers = nearby,
                            selectedDeviceId = null,
                            lastDiscoveredDeviceId = lastDiscoveredDeviceId,
                            seenIds = seenNearbyIds,
                            onPeerClick = onStartPairing
                        )
                    }
                }
            }
        }
    }

    if (showComposeClipDialog) {
        AlertDialog(
            onDismissRequest = { showComposeClipDialog = false },
            shape = RoundedCornerShape(28.dp),
            title = { Text(stringResource(R.string.clipboard_dialog_title)) },
            text = {
                OutlinedTextField(
                    value = clipDraft,
                    onValueChange = { clipDraft = it },
                    placeholder = { Text(stringResource(R.string.clipboard_dialog_hint)) },
                    minLines = 3,
                    maxLines = 6,
                    shape = RoundedCornerShape(18.dp),
                    modifier = Modifier
                        .fillMaxWidth()
                        .testTag("clipboard_compose_input")
                )
            },
            confirmButton = {
                TextButton(
                    onClick = {
                        showComposeClipDialog = false
                        onPushCustomClipboard(clipDraft)
                    },
                    modifier = Modifier.testTag("clipboard_compose_confirm")
                ) {
                    Text(stringResource(R.string.clipboard_dialog_send))
                }
            },
            dismissButton = {
                TextButton(onClick = { showComposeClipDialog = false }) {
                    Text(stringResource(R.string.common_cancel_button))
                }
            }
        )
    }
}

@Composable
private fun LinkQuickTileBanner(
    wifiLanReady: Boolean,
    linkServiceRunning: Boolean,
    onlinePeerCount: Int,
    successRate: Float,
    onClick: () -> Unit
) {
    val active = wifiLanReady && linkServiceRunning
    val containerColor = when {
        !linkServiceRunning -> MaterialTheme.colorScheme.errorContainer.copy(alpha = 0.7f)
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
    val subText = if (active) {
        stringResource(
            R.string.connection_compact_entry_sub,
            onlinePeerCount,
            String.format(Locale.US, "%.1f%%", successRate)
        )
    } else {
        stringResource(R.string.connection_compact_paused_sub)
    }
    val pillRes = when {
        !linkServiceRunning -> R.string.status_service_stopped_pill
        wifiLanReady -> R.string.status_lan_ready_pill
        else -> R.string.status_cellular_paused_pill
    }

    Surface(
        onClick = onClick,
        shape = RoundedCornerShape(26.dp),
        color = containerColor,
        modifier = Modifier
            .fillMaxWidth()
            .testTag("link_status_banner")
    ) {
        Row(
            modifier = Modifier.padding(horizontal = 18.dp, vertical = 14.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            Surface(
                shape = CircleShape,
                color = pillBg,
                modifier = Modifier.size(42.dp)
            ) {
                Box(contentAlignment = Alignment.Center) {
                    Icon(
                        imageVector = icon,
                        contentDescription = null,
                        tint = pillFg,
                        modifier = Modifier.size(22.dp)
                    )
                }
            }
            Spacer(modifier = Modifier.width(14.dp))
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    text = stringResource(titleRes),
                    style = MaterialTheme.typography.titleSmall,
                    color = contentColor
                )
                Spacer(modifier = Modifier.height(2.dp))
                Text(
                    text = subText,
                    style = MaterialTheme.typography.bodySmall,
                    color = contentColor.copy(alpha = 0.82f),
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis
                )
            }
            Spacer(modifier = Modifier.width(8.dp))
            Surface(shape = CircleShape, color = pillBg) {
                Text(
                    text = stringResource(pillRes),
                    style = MaterialTheme.typography.labelMedium,
                    color = pillFg,
                    modifier = Modifier.padding(horizontal = 10.dp, vertical = 5.dp)
                )
            }
        }
    }
}

@Composable
private fun ClipboardQuickSyncCard(
    mutualOnlineCount: Int,
    clipboardState: ClipboardSyncState,
    onPushSystemClipboard: () -> Unit,
    onOpenComposeDialog: () -> Unit
) {
    val palette = LocalExpressivePalette.current
    Column(verticalArrangement = Arrangement.spacedBy(2.dp)) {
        ExpressiveGroupTile(index = 0, count = 2) {
            Row(
                modifier = Modifier.padding(horizontal = 18.dp, vertical = 16.dp),
                verticalAlignment = Alignment.CenterVertically
            ) {
                ExpressiveIconBadge(
                    icon = Icons.Rounded.ContentPasteGo,
                    backgroundColor = palette.mintIconBg,
                    iconTint = palette.mintIconFg
                )
                Spacer(modifier = Modifier.width(14.dp))
                Column(modifier = Modifier.weight(1f)) {
                    Text(
                        text = stringResource(R.string.clipboard_card_title),
                        style = MaterialTheme.typography.titleSmall
                    )
                    Spacer(modifier = Modifier.height(2.dp))
                    Text(
                        text = if (mutualOnlineCount > 0) {
                            stringResource(R.string.clipboard_card_mutual_peers, mutualOnlineCount)
                        } else {
                            stringResource(R.string.clipboard_card_no_peers)
                        },
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                }
            }
        }
        ExpressiveGroupTile(index = 1, count = 2) {
            Column(modifier = Modifier.padding(horizontal = 18.dp, vertical = 14.dp)) {
                Surface(
                    shape = RoundedCornerShape(14.dp),
                    color = MaterialTheme.colorScheme.surfaceContainer,
                    modifier = Modifier.fillMaxWidth()
                ) {
                    Text(
                        text = clipboardState.lastSyncedText.ifEmpty {
                            stringResource(R.string.clipboard_card_empty_preview)
                        },
                        fontFamily = if (clipboardState.lastSyncedText.isNotEmpty()) {
                            JetBrainsMonoFamily
                        } else {
                            null
                        },
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        maxLines = 2,
                        overflow = TextOverflow.Ellipsis,
                        modifier = Modifier.padding(horizontal = 14.dp, vertical = 10.dp)
                    )
                }
                Spacer(modifier = Modifier.height(12.dp))
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.spacedBy(10.dp)
                ) {
                    FilledTonalButton(
                        onClick = onPushSystemClipboard,
                        shape = CircleShape,
                        modifier = Modifier
                            .weight(1f)
                            .height(48.dp)
                            .testTag("push_system_clipboard_button")
                    ) {
                        Icon(
                            imageVector = Icons.Rounded.ContentPasteGo,
                            contentDescription = null,
                            modifier = Modifier.size(18.dp)
                        )
                        Spacer(modifier = Modifier.width(6.dp))
                        Text(
                            text = stringResource(R.string.clipboard_action_push_system),
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis
                        )
                    }
                    OutlinedButton(
                        onClick = onOpenComposeDialog,
                        shape = RoundedCornerShape(16.dp),
                        modifier = Modifier
                            .weight(1f)
                            .height(48.dp)
                            .testTag("compose_clipboard_button")
                    ) {
                        Icon(
                            imageVector = Icons.Rounded.EditNote,
                            contentDescription = null,
                            modifier = Modifier.size(18.dp)
                        )
                        Spacer(modifier = Modifier.width(6.dp))
                        Text(
                            text = stringResource(R.string.clipboard_action_compose),
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis
                        )
                    }
                }
            }
        }
    }
}

@Composable
private fun ManualConnectQuickCard(onClick: () -> Unit) {
    val palette = LocalExpressivePalette.current
    ExpressiveGroupTile(
        index = 0,
        count = 1,
        onClick = onClick,
        modifier = Modifier.testTag("manual_connect_entry_tile")
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

@Composable
private fun EmptyDevicesStateCard(
    isScanning: Boolean,
    lanReady: Boolean,
    onOpenManualConnect: () -> Unit
) {
    val mode = when {
        !lanReady -> DiscoveryVisualMode.PAUSED
        isScanning -> DiscoveryVisualMode.SCANNING
        else -> DiscoveryVisualMode.IDLE_EMPTY
    }
    Surface(
        shape = RoundedCornerShape(28.dp),
        color = MaterialTheme.colorScheme.surfaceContainerLow,
        modifier = Modifier.fillMaxWidth()
    ) {
        Column(
            modifier = Modifier.padding(28.dp),
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
            DiscoveryRippleOrb(
                mode = mode,
                orbSize = 84.dp
            )
            Spacer(modifier = Modifier.height(14.dp))
            Text(
                text = stringResource(
                    if (isScanning) R.string.scan_status_scanning_title
                    else R.string.empty_devices_title
                ),
                style = MaterialTheme.typography.titleMedium
            )
            Spacer(modifier = Modifier.height(6.dp))
            Text(
                text = stringResource(R.string.empty_devices_subtitle),
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                textAlign = TextAlign.Center
            )
            Spacer(modifier = Modifier.height(16.dp))
            FilledTonalButton(
                onClick = onOpenManualConnect,
                shape = CircleShape,
                modifier = Modifier
                    .height(48.dp)
                    .testTag("empty_state_manual_connect_button")
            ) {
                Icon(
                    imageVector = Icons.Rounded.AddLink,
                    contentDescription = null,
                    modifier = Modifier.size(18.dp)
                )
                Spacer(modifier = Modifier.width(8.dp))
                Text(stringResource(R.string.manual_connect_entry_title))
            }
        }
    }
}

@Composable
private fun ExpressiveDeviceGroup(
    peers: List<PeerDeviceSnapshot>,
    selectedDeviceId: String?,
    lastDiscoveredDeviceId: String?,
    seenIds: MutableSet<String>?,
    onPeerClick: (PeerDeviceSnapshot) -> Unit
) {
    val palette = LocalExpressivePalette.current
    Column(verticalArrangement = Arrangement.spacedBy(2.dp)) {
        peers.forEachIndexed { index, peer ->
            key(peer.deviceId) {
                val isFreshEntrance = remember(peer.deviceId) {
                    seenIds != null && !seenIds.contains(peer.deviceId)
                }
                val isNewlyDiscovered = peer.deviceId == lastDiscoveredDeviceId
                val isSelected = peer.deviceId == selectedDeviceId

                val targetTileColor = when {
                    isSelected -> MaterialTheme.colorScheme.secondaryContainer
                    isNewlyDiscovered -> MaterialTheme.colorScheme.primaryContainer.copy(alpha = 0.42f)
                    else -> MaterialTheme.colorScheme.surfaceContainerLow
                }
                val tileColor by animateColorAsState(
                    targetValue = targetTileColor,
                    animationSpec = tween(durationMillis = 420),
                    label = "deviceTileBg"
                )

                val (badgeBg, badgeFg) = when {
                    peer.presence == PeerPresence.CONNECTED -> palette.mintIconBg to palette.mintIconFg
                    !peer.trusted -> palette.amberIconBg to palette.amberIconFg
                    else -> palette.skyIconBg to palette.skyIconFg
                }
                val askLabel = stringResource(
                    if (peer.askBeforeSave) R.string.device_summary_ask_on
                    else R.string.device_summary_ask_off
                )

                DiscoveredDeviceEntranceItem(
                    animateEntrance = isFreshEntrance,
                    onEntered = { seenIds?.add(peer.deviceId) }
                ) {
                    ExpressiveGroupTile(
                        index = index,
                        count = peers.size,
                        highlighted = isSelected,
                        containerColor = tileColor,
                        onClick = { onPeerClick(peer) },
                        modifier = Modifier.testTag("device_row_${peer.deviceId}")
                    ) {
                        Row(
                            modifier = Modifier.padding(horizontal = 18.dp, vertical = 16.dp),
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            ExpressiveIconBadge(
                                icon = peer.formFactor.toIcon(),
                                backgroundColor = badgeBg,
                                iconTint = badgeFg,
                                contentDescription = peer.displayName
                            )
                            Spacer(modifier = Modifier.width(16.dp))
                            Column(modifier = Modifier.weight(1f)) {
                                Row(
                                    verticalAlignment = Alignment.CenterVertically,
                                    horizontalArrangement = Arrangement.spacedBy(8.dp)
                                ) {
                                    Text(
                                        text = peer.displayName,
                                        style = MaterialTheme.typography.titleMedium,
                                        maxLines = 1,
                                        overflow = TextOverflow.Ellipsis,
                                        modifier = Modifier.weight(1f, fill = false)
                                    )
                                    PresenceBadge(presence = peer.presence)
                                    NewlyDiscoveredPill(visible = isNewlyDiscovered)
                                }
                                Spacer(modifier = Modifier.height(3.dp))
                                Text(
                                    text = when {
                                        !peer.trusted && peer.presence == PeerPresence.OFFLINE ->
                                            stringResource(R.string.device_summary_offline_pair_disabled)
                                        !peer.trusted ->
                                            stringResource(R.string.device_summary_tap_to_pair)
                                        peer.clipboardSync ->
                                            stringResource(R.string.device_summary_clipboard_on, askLabel)
                                        else ->
                                            stringResource(R.string.device_summary_clipboard_off, askLabel)
                                    },
                                    style = MaterialTheme.typography.bodySmall,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant
                                )
                                if (peer.endpoint != null) {
                                    Spacer(modifier = Modifier.height(3.dp))
                                    Text(
                                        text = stringResource(R.string.device_endpoint_format, peer.endpoint),
                                        fontFamily = JetBrainsMonoFamily,
                                        fontSize = 11.5.sp,
                                        color = MaterialTheme.colorScheme.primary
                                    )
                                }
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
        }
    }
}

@Composable
fun PresenceBadge(presence: PeerPresence) {
    val palette = LocalExpressivePalette.current
    val (bg, fg, labelRes) = when (presence) {
        PeerPresence.CONNECTED -> Triple(
            palette.connectedContainer,
            palette.onConnectedContainer,
            R.string.presence_connected
        )
        PeerPresence.REACHABLE -> Triple(
            palette.reachableContainer,
            palette.onReachableContainer,
            R.string.presence_reachable
        )
        PeerPresence.OFFLINE -> Triple(
            MaterialTheme.colorScheme.surfaceContainerHighest,
            MaterialTheme.colorScheme.onSurfaceVariant,
            R.string.presence_offline
        )
    }

    Surface(shape = CircleShape, color = bg) {
        Text(
            text = stringResource(labelRes),
            style = MaterialTheme.typography.labelMedium,
            color = fg,
            modifier = Modifier.padding(horizontal = 10.dp, vertical = 3.dp)
        )
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun PairingVerificationBottomSheet(
    prompt: PairingPromptSnapshot,
    onReject: () -> Unit,
    onAccept: () -> Unit
) {
    val chars = remember(prompt.code) {
        prompt.code.filter { !it.isWhitespace() }.padEnd(6, '2').take(6).toList()
    }
    val progress = (prompt.remainingMilliseconds.toFloat() / ProtocolLimits.PROMPT_TIMEOUT_MS.toFloat())
        .coerceIn(0f, 1f)

    val sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true)

    ModalBottomSheet(
        onDismissRequest = onReject,
        sheetState = sheetState,
        shape = RoundedCornerShape(topStart = 32.dp, topEnd = 32.dp),
        containerColor = MaterialTheme.colorScheme.surfaceContainerLow
    ) {
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .navigationBarsPadding()
                .imePadding()
                .padding(horizontal = 24.dp)
                .padding(bottom = 16.dp)
                .testTag("pairing_bottom_sheet"),
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
            Column(
                modifier = Modifier
                    .fillMaxWidth()
                    .weight(1f, fill = false)
                    .verticalScroll(rememberScrollState()),
                horizontalAlignment = Alignment.CenterHorizontally
            ) {
                Surface(
                    shape = RoundedCornerShape(18.dp),
                    color = MaterialTheme.colorScheme.primaryContainer,
                    modifier = Modifier.size(48.dp)
                ) {
                    Box(contentAlignment = Alignment.Center) {
                        Icon(
                            imageVector = Icons.Rounded.VerifiedUser,
                            contentDescription = null,
                            tint = MaterialTheme.colorScheme.onPrimaryContainer,
                            modifier = Modifier.size(25.dp)
                        )
                    }
                }
                Spacer(modifier = Modifier.height(12.dp))
                Text(
                    text = stringResource(R.string.pairing_sheet_title),
                    style = MaterialTheme.typography.headlineSmall
                )
                Spacer(modifier = Modifier.height(4.dp))
                Text(
                    text = stringResource(R.string.pairing_sheet_subtitle, prompt.displayName),
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    textAlign = TextAlign.Center
                )

                if (prompt.localAccepted || prompt.remoteAccepted) {
                    Spacer(modifier = Modifier.height(8.dp))
                    Surface(
                        shape = CircleShape,
                        color = MaterialTheme.colorScheme.secondaryContainer
                    ) {
                        Text(
                            text = stringResource(
                                if (prompt.localAccepted) R.string.pairing_sheet_waiting_remote
                                else R.string.pairing_sheet_remote_accepted
                            ),
                            style = MaterialTheme.typography.labelMedium,
                            color = MaterialTheme.colorScheme.onSecondaryContainer,
                            modifier = Modifier.padding(horizontal = 14.dp, vertical = 5.dp)
                        )
                    }
                }

                Spacer(modifier = Modifier.height(16.dp))
                Row(
                    horizontalArrangement = Arrangement.spacedBy(10.dp),
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    CodeCharTriplet(chars = chars.subList(0, 3))
                    CodeCharTriplet(chars = chars.subList(3, 6))
                }

                Spacer(modifier = Modifier.height(10.dp))
                Text(
                    text = prompt.deviceId,
                    fontFamily = JetBrainsMonoFamily,
                    fontSize = 11.sp,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )

                Spacer(modifier = Modifier.height(14.dp))
                ExpressiveGapProgressBar(progress = progress)
                Spacer(modifier = Modifier.height(6.dp))
                Text(
                    text = stringResource(R.string.pairing_sheet_countdown, prompt.remainingSeconds),
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
            }

            Spacer(modifier = Modifier.height(18.dp))
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(10.dp)
            ) {
                OutlinedButton(
                    onClick = onReject,
                    shape = RoundedCornerShape(18.dp),
                    modifier = Modifier
                        .weight(0.42f)
                        .height(52.dp)
                        .testTag("pairing_reject_button")
                ) {
                    Text(
                        text = stringResource(
                            if (prompt.initiatedLocally) R.string.pairing_cancel_button
                            else R.string.pairing_reject_button
                        ),
                        style = MaterialTheme.typography.titleSmall
                    )
                }
                Button(
                    onClick = onAccept,
                    enabled = !prompt.localAccepted,
                    shape = CircleShape,
                    modifier = Modifier
                        .weight(0.58f)
                        .height(52.dp)
                        .testTag("pairing_accept_button")
                ) {
                    Text(
                        text = stringResource(
                            if (prompt.localAccepted) R.string.pairing_accepted_waiting_button
                            else R.string.pairing_accept_button
                        ),
                        style = MaterialTheme.typography.titleSmall
                    )
                }
            }
        }
    }
}

@Composable
private fun CodeCharTriplet(chars: List<Char>) {
    Row(horizontalArrangement = Arrangement.spacedBy(2.dp)) {
        chars.forEachIndexed { idx, ch ->
            val shape = when (idx) {
                0 -> RoundedCornerShape(topStart = 18.dp, bottomStart = 18.dp, topEnd = 6.dp, bottomEnd = 6.dp)
                chars.lastIndex -> RoundedCornerShape(topStart = 6.dp, bottomStart = 6.dp, topEnd = 18.dp, bottomEnd = 18.dp)
                else -> RoundedCornerShape(6.dp)
            }
            Surface(
                shape = shape,
                color = MaterialTheme.colorScheme.surfaceContainer,
                modifier = Modifier.size(width = 44.dp, height = 54.dp)
            ) {
                Box(contentAlignment = Alignment.Center) {
                    Text(
                        text = ch.toString(),
                        fontFamily = JetBrainsMonoFamily,
                        fontWeight = FontWeight.Bold,
                        fontSize = 23.sp,
                        color = MaterialTheme.colorScheme.primary
                    )
                }
            }
        }
    }
}

@Composable
fun SectionHeader(text: String) {
    Text(
        text = text,
        style = MaterialTheme.typography.labelLarge,
        color = MaterialTheme.colorScheme.primary,
        modifier = Modifier.padding(horizontal = 8.dp)
    )
}

fun DeviceFormFactor.toIcon(): ImageVector = when (this) {
    DeviceFormFactor.DESKTOP -> Icons.Rounded.Computer
    DeviceFormFactor.LAPTOP -> Icons.Rounded.LaptopMac
    DeviceFormFactor.PHONE -> Icons.Rounded.Smartphone
}
