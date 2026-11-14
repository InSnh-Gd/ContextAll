package app.contextall.ui.screens

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.Archive
import androidx.compose.material.icons.rounded.BugReport
import androidx.compose.material.icons.rounded.ContentCopy
import androidx.compose.material.icons.rounded.ContentPasteGo
import androidx.compose.material.icons.rounded.DeleteSweep
import androidx.compose.material.icons.rounded.MoveToInbox
import androidx.compose.material.icons.rounded.NoteAdd
import androidx.compose.material.icons.rounded.Password
import androidx.compose.material.icons.rounded.Radar
import androidx.compose.material.icons.rounded.Speed
import androidx.compose.material.icons.rounded.Storage
import androidx.compose.material.icons.rounded.SyncProblem
import androidx.compose.material.icons.rounded.Visibility
import androidx.compose.material.icons.rounded.WarningAmber
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilledTonalButton
import androidx.compose.material3.FilterChip
import androidx.compose.material3.FilterChipDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.minimumInteractiveComponentSize
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import app.contextall.BuildConfig
import app.contextall.R
import app.contextall.model.DebugFrameLogEntry
import app.contextall.model.EngineDebugState
import app.contextall.model.PeerDeviceSnapshot
import app.contextall.model.PeerPresence
import app.contextall.model.SeedDataProfile
import app.contextall.model.TransferDirection
import app.contextall.model.TransferSnapshot
import app.contextall.ui.components.ExpressiveGroupTile
import app.contextall.ui.components.ExpressiveIconBadge
import app.contextall.ui.theme.JetBrainsMonoFamily
import app.contextall.ui.theme.LocalExpressivePalette

private enum class DebugConsoleTab {
    FAULTS,
    PAYLOADS,
    FRAMES
}

/**
 * Full-featured Developer Debug Workbench for ContextAll's debug edition.
 * Consolidates Seed Profile switching, SessionHost fault injection, boundary payload stress tests,
 * live runtime invariants, and wire-frame / command tracing.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun DeveloperDebugConsoleSheet(
    debugState: EngineDebugState,
    trustedPeers: List<PeerDeviceSnapshot>,
    activeTransfers: List<TransferSnapshot>,
    pendingPromptCount: Int,
    onDismiss: () -> Unit,
    onPreviewReleaseUi: () -> Unit,
    onSwitchSeedProfile: (SeedDataProfile) -> Unit,
    onDiscoverPeer: () -> Unit,
    onInboundPairing: () -> Unit,
    onInboundFile: () -> Unit,
    onInboundBlockedFile: () -> Unit,
    onRemoteClipboard: () -> Unit,
    onFlapConnection: () -> Unit,
    onTelemetryBurst: () -> Unit,
    onInjectBoundaryPayload: (deviceId: String, fileName: String, sizeBytes: Long) -> Unit,
    onCopyFrameLogs: () -> Unit,
    onClearFrameLogs: () -> Unit
) {
    val palette = LocalExpressivePalette.current
    val sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true)
    var activeTab by rememberSaveable { mutableStateOf(DebugConsoleTab.FAULTS) }
    var selectedTargetPeerId by rememberSaveable { mutableStateOf<String?>(null) }

    LaunchedEffect(trustedPeers) {
        if (trustedPeers.none { it.deviceId == selectedTargetPeerId }) {
            selectedTargetPeerId = trustedPeers.firstOrNull { it.presence == PeerPresence.CONNECTED }?.deviceId
                ?: trustedPeers.firstOrNull()?.deviceId
        }
    }

    val targetPeer = trustedPeers.firstOrNull { it.deviceId == selectedTargetPeerId }
        ?: trustedPeers.firstOrNull()

    val sendSlotCount = remember(activeTransfers) {
        activeTransfers.count { !it.completed && it.direction == TransferDirection.SEND }
    }
    val recvSlotCount = remember(activeTransfers, pendingPromptCount) {
        activeTransfers.count { !it.completed && it.direction == TransferDirection.RECEIVE } + pendingPromptCount
    }

    ModalBottomSheet(
        onDismissRequest = onDismiss,
        sheetState = sheetState,
        shape = RoundedCornerShape(topStart = 32.dp, topEnd = 32.dp),
        containerColor = MaterialTheme.colorScheme.surfaceContainerLow
    ) {
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .navigationBarsPadding()
                .padding(horizontal = 20.dp)
                .padding(bottom = 18.dp)
                .testTag("scenario_lab_sheet")
        ) {
            // Header Row + Build Version + Quick Preview Release UI Button
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(horizontal = 4.dp),
                verticalAlignment = Alignment.CenterVertically
            ) {
                ExpressiveIconBadge(
                    icon = Icons.Rounded.BugReport,
                    backgroundColor = palette.amberIconBg,
                    iconTint = palette.amberIconFg,
                    size = 44.dp
                )
                Spacer(modifier = Modifier.width(12.dp))
                Column(modifier = Modifier.weight(1f)) {
                    Row(
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(8.dp)
                    ) {
                        Text(
                            text = stringResource(R.string.debug_sheet_title),
                            style = MaterialTheme.typography.titleLarge
                        )
                        Surface(
                            shape = CircleShape,
                            color = palette.amberIconBg
                        ) {
                            Text(
                                text = "v${BuildConfig.VERSION_NAME}",
                                fontFamily = JetBrainsMonoFamily,
                                fontSize = 10.5.sp,
                                fontWeight = FontWeight.Bold,
                                color = palette.amberIconFg,
                                modifier = Modifier.padding(horizontal = 8.dp, vertical = 2.dp)
                            )
                        }
                    }
                    Spacer(modifier = Modifier.height(2.dp))
                    Text(
                        text = stringResource(R.string.debug_sheet_subtitle),
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                }
            }

            Spacer(modifier = Modifier.height(12.dp))

            // Segmented Console Tabs + Quick Switch to Formal Release View
            LazyRow(
                horizontalArrangement = Arrangement.spacedBy(8.dp),
                modifier = Modifier.fillMaxWidth()
            ) {
                item {
                    FilterChip(
                        selected = activeTab == DebugConsoleTab.FAULTS,
                        onClick = { activeTab = DebugConsoleTab.FAULTS },
                        label = { Text(stringResource(R.string.debug_tab_faults)) },
                        shape = CircleShape,
                        modifier = Modifier
                            .minimumInteractiveComponentSize()
                            .testTag("debug_tab_faults")
                    )
                }
                item {
                    FilterChip(
                        selected = activeTab == DebugConsoleTab.PAYLOADS,
                        onClick = { activeTab = DebugConsoleTab.PAYLOADS },
                        label = { Text(stringResource(R.string.debug_tab_payloads)) },
                        shape = CircleShape,
                        modifier = Modifier
                            .minimumInteractiveComponentSize()
                            .testTag("debug_tab_payloads")
                    )
                }
                item {
                    FilterChip(
                        selected = activeTab == DebugConsoleTab.FRAMES,
                        onClick = { activeTab = DebugConsoleTab.FRAMES },
                        label = { Text(stringResource(R.string.debug_tab_frames, debugState.frameLogs.size)) },
                        shape = CircleShape,
                        modifier = Modifier
                            .minimumInteractiveComponentSize()
                            .testTag("debug_tab_frames")
                    )
                }
                item {
                    OutlinedButton(
                        onClick = {
                            onDismiss()
                            onPreviewReleaseUi()
                        },
                        shape = CircleShape,
                        contentPadding = PaddingValues(horizontal = 12.dp, vertical = 4.dp),
                        modifier = Modifier
                            .minimumInteractiveComponentSize()
                            .testTag("debug_preview_release_button")
                    ) {
                        Icon(
                            imageVector = Icons.Rounded.Visibility,
                            contentDescription = null,
                            modifier = Modifier.size(16.dp)
                        )
                        Spacer(modifier = Modifier.width(6.dp))
                        Text(
                            text = stringResource(R.string.debug_preview_release_btn),
                            style = MaterialTheme.typography.labelMedium
                        )
                    }
                }
            }

            Spacer(modifier = Modifier.height(12.dp))

            Column(
                modifier = Modifier
                    .fillMaxWidth()
                    .weight(1f, fill = false)
                    .verticalScroll(rememberScrollState())
            ) {
                when (activeTab) {
                    DebugConsoleTab.FAULTS -> {
                        // Seed Profile Switcher Card
                        Surface(
                            shape = RoundedCornerShape(22.dp),
                            color = MaterialTheme.colorScheme.surfaceContainer,
                            modifier = Modifier.fillMaxWidth()
                        ) {
                            Column(modifier = Modifier.padding(horizontal = 16.dp, vertical = 12.dp)) {
                                Text(
                                    text = stringResource(R.string.debug_seed_section_label),
                                    style = MaterialTheme.typography.labelMedium,
                                    color = MaterialTheme.colorScheme.primary
                                )
                                Spacer(modifier = Modifier.height(8.dp))
                                Row(
                                    modifier = Modifier.fillMaxWidth(),
                                    horizontalArrangement = Arrangement.spacedBy(8.dp)
                                ) {
                                    FilterChip(
                                        selected = debugState.seedProfile == SeedDataProfile.CONSUMER,
                                        onClick = { onSwitchSeedProfile(SeedDataProfile.CONSUMER) },
                                        label = {
                                            Text(
                                                text = stringResource(R.string.debug_seed_consumer),
                                                maxLines = 1,
                                                overflow = TextOverflow.Ellipsis
                                            )
                                        },
                                        shape = CircleShape,
                                        modifier = Modifier
                                            .weight(1f)
                                            .minimumInteractiveComponentSize()
                                            .testTag("debug_seed_consumer_chip")
                                    )
                                    FilterChip(
                                        selected = debugState.seedProfile == SeedDataProfile.DEVELOPER_LAB,
                                        onClick = { onSwitchSeedProfile(SeedDataProfile.DEVELOPER_LAB) },
                                        label = {
                                            Text(
                                                text = stringResource(R.string.debug_seed_developer),
                                                maxLines = 1,
                                                overflow = TextOverflow.Ellipsis
                                            )
                                        },
                                        shape = CircleShape,
                                        modifier = Modifier
                                            .weight(1f)
                                            .minimumInteractiveComponentSize()
                                            .testTag("debug_seed_developer_chip")
                                    )
                                }
                            }
                        }

                        Spacer(modifier = Modifier.height(10.dp))

                        Column(verticalArrangement = Arrangement.spacedBy(2.dp)) {
                            DebugActionRow(
                                index = 0,
                                count = 7,
                                icon = Icons.Rounded.Radar,
                                badgeBg = palette.skyIconBg,
                                badgeFg = palette.skyIconFg,
                                title = stringResource(R.string.lab_action_discover_peer),
                                subtitle = stringResource(R.string.lab_action_discover_peer_sub),
                                testTag = "lab_discover_peer",
                                onClick = {
                                    onDiscoverPeer()
                                    onDismiss()
                                }
                            )
                            DebugActionRow(
                                index = 1,
                                count = 7,
                                icon = Icons.Rounded.Password,
                                badgeBg = palette.violetIconBg,
                                badgeFg = palette.violetIconFg,
                                title = stringResource(R.string.lab_action_inbound_pair),
                                subtitle = stringResource(R.string.lab_action_inbound_pair_sub),
                                testTag = "lab_inbound_pairing",
                                onClick = {
                                    onDismiss()
                                    onInboundPairing()
                                }
                            )
                            DebugActionRow(
                                index = 2,
                                count = 7,
                                icon = Icons.Rounded.MoveToInbox,
                                badgeBg = palette.mintIconBg,
                                badgeFg = palette.mintIconFg,
                                title = stringResource(R.string.lab_action_inbound_file),
                                subtitle = stringResource(R.string.lab_action_inbound_file_sub),
                                testTag = "lab_inbound_file",
                                onClick = {
                                    onDismiss()
                                    onInboundFile()
                                }
                            )
                            DebugActionRow(
                                index = 3,
                                count = 7,
                                icon = Icons.Rounded.WarningAmber,
                                badgeBg = MaterialTheme.colorScheme.errorContainer,
                                badgeFg = MaterialTheme.colorScheme.onErrorContainer,
                                title = stringResource(R.string.lab_action_inbound_blocked),
                                subtitle = stringResource(R.string.lab_action_inbound_blocked_sub),
                                testTag = "lab_inbound_blocked",
                                onClick = {
                                    onDismiss()
                                    onInboundBlockedFile()
                                }
                            )
                            DebugActionRow(
                                index = 4,
                                count = 7,
                                icon = Icons.Rounded.ContentPasteGo,
                                badgeBg = palette.amberIconBg,
                                badgeFg = palette.amberIconFg,
                                title = stringResource(R.string.lab_action_remote_clipboard),
                                subtitle = stringResource(R.string.lab_action_remote_clipboard_sub),
                                testTag = "lab_remote_clipboard",
                                onClick = {
                                    onDismiss()
                                    onRemoteClipboard()
                                }
                            )
                            DebugActionRow(
                                index = 5,
                                count = 7,
                                icon = Icons.Rounded.SyncProblem,
                                badgeBg = palette.skyIconBg,
                                badgeFg = palette.skyIconFg,
                                title = stringResource(R.string.lab_action_flap_connection),
                                subtitle = stringResource(R.string.lab_action_flap_connection_sub),
                                testTag = "lab_flap_connection",
                                onClick = {
                                    onDismiss()
                                    onFlapConnection()
                                }
                            )
                            DebugActionRow(
                                index = 6,
                                count = 7,
                                icon = Icons.Rounded.Speed,
                                badgeBg = palette.mintIconBg,
                                badgeFg = palette.mintIconFg,
                                title = stringResource(R.string.debug_action_jitter_title),
                                subtitle = stringResource(R.string.debug_action_jitter_sub),
                                testTag = "lab_telemetry_burst",
                                onClick = {
                                    onTelemetryBurst()
                                    onDismiss()
                                }
                            )
                        }
                    }

                    DebugConsoleTab.PAYLOADS -> {
                        if (targetPeer == null) {
                            Surface(
                                shape = RoundedCornerShape(22.dp),
                                color = MaterialTheme.colorScheme.surfaceContainer,
                                modifier = Modifier.fillMaxWidth()
                            ) {
                                Text(
                                    text = stringResource(R.string.transfers_no_trusted_sub),
                                    style = MaterialTheme.typography.bodyMedium,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                                    modifier = Modifier.padding(18.dp)
                                )
                            }
                        } else {
                            Text(
                                text = stringResource(R.string.debug_payload_target_label),
                                style = MaterialTheme.typography.labelMedium,
                                color = MaterialTheme.colorScheme.primary,
                                modifier = Modifier.padding(horizontal = 6.dp)
                            )
                            Spacer(modifier = Modifier.height(6.dp))
                            LazyRow(
                                horizontalArrangement = Arrangement.spacedBy(8.dp),
                                modifier = Modifier.fillMaxWidth()
                            ) {
                                items(trustedPeers, key = { it.deviceId }) { peer ->
                                    FilterChip(
                                        selected = peer.deviceId == targetPeer.deviceId,
                                        onClick = { selectedTargetPeerId = peer.deviceId },
                                        label = { Text(peer.displayName) },
                                        colors = FilterChipDefaults.filterChipColors(
                                            selectedContainerColor = MaterialTheme.colorScheme.primaryContainer,
                                            selectedLabelColor = MaterialTheme.colorScheme.onPrimaryContainer
                                        ),
                                        shape = CircleShape,
                                        modifier = Modifier.minimumInteractiveComponentSize()
                                    )
                                }
                            }
                            Spacer(modifier = Modifier.height(12.dp))
                            Column(verticalArrangement = Arrangement.spacedBy(2.dp)) {
                                DebugActionRow(
                                    index = 0,
                                    count = 4,
                                    icon = Icons.Rounded.Archive,
                                    badgeBg = palette.skyIconBg,
                                    badgeFg = palette.skyIconFg,
                                    title = "diagnostics.tar.gz",
                                    subtitle = stringResource(R.string.debug_payload_small_sub),
                                    monoTitle = true,
                                    testTag = "debug_payload_small",
                                    onClick = {
                                        onDismiss()
                                        onInjectBoundaryPayload(targetPeer.deviceId, "diagnostics.tar.gz", 18_400_000L)
                                    }
                                )
                                DebugActionRow(
                                    index = 1,
                                    count = 4,
                                    icon = Icons.Rounded.Storage,
                                    badgeBg = palette.violetIconBg,
                                    badgeFg = palette.violetIconFg,
                                    title = "rootfs-arm64.img",
                                    subtitle = stringResource(R.string.debug_payload_large_sub),
                                    monoTitle = true,
                                    testTag = "debug_payload_large",
                                    onClick = {
                                        onDismiss()
                                        onInjectBoundaryPayload(targetPeer.deviceId, "rootfs-arm64.img", 142_600_000L)
                                    }
                                )
                                DebugActionRow(
                                    index = 2,
                                    count = 4,
                                    icon = Icons.Rounded.NoteAdd,
                                    badgeBg = palette.mintIconBg,
                                    badgeFg = palette.mintIconFg,
                                    title = ".ready",
                                    subtitle = stringResource(R.string.debug_payload_zero_sub),
                                    monoTitle = true,
                                    testTag = "debug_payload_zero",
                                    onClick = {
                                        onDismiss()
                                        onInjectBoundaryPayload(targetPeer.deviceId, ".ready", 0L)
                                    }
                                )
                                DebugActionRow(
                                    index = 3,
                                    count = 4,
                                    icon = Icons.Rounded.WarningAmber,
                                    badgeBg = MaterialTheme.colorScheme.errorContainer,
                                    badgeFg = MaterialTheme.colorScheme.onErrorContainer,
                                    title = "dataset-raw.bin",
                                    subtitle = stringResource(R.string.debug_payload_oversize_sub),
                                    monoTitle = true,
                                    testTag = "debug_payload_oversize",
                                    onClick = {
                                        onDismiss()
                                        onInjectBoundaryPayload(targetPeer.deviceId, "dataset-raw.bin", 1_120_000_000L)
                                    }
                                )
                            }
                        }
                    }

                    DebugConsoleTab.FRAMES -> {
                        // Invariants Snapshot Card
                        Surface(
                            shape = RoundedCornerShape(22.dp),
                            color = MaterialTheme.colorScheme.surfaceContainer,
                            modifier = Modifier.fillMaxWidth()
                        ) {
                            Column(modifier = Modifier.padding(horizontal = 16.dp, vertical = 14.dp)) {
                                Text(
                                    text = stringResource(R.string.debug_invariants_title),
                                    style = MaterialTheme.typography.labelLarge,
                                    color = MaterialTheme.colorScheme.primary
                                )
                                Spacer(modifier = Modifier.height(8.dp))
                                Row(
                                    modifier = Modifier.fillMaxWidth(),
                                    horizontalArrangement = Arrangement.spacedBy(8.dp)
                                ) {
                                    InvariantBadge(
                                        text = stringResource(R.string.debug_invariant_window),
                                        modifier = Modifier.weight(1f)
                                    )
                                    InvariantBadge(
                                        text = stringResource(
                                            R.string.debug_invariant_slots,
                                            sendSlotCount,
                                            recvSlotCount
                                        ),
                                        modifier = Modifier.weight(1f)
                                    )
                                }
                                Spacer(modifier = Modifier.height(6.dp))
                                Row(
                                    modifier = Modifier.fillMaxWidth(),
                                    horizontalArrangement = Arrangement.spacedBy(8.dp)
                                ) {
                                    InvariantBadge(
                                        text = stringResource(
                                            R.string.debug_invariant_dedupe,
                                            debugState.dedupedRequestCount
                                        ),
                                        modifier = Modifier.weight(1f)
                                    )
                                    InvariantBadge(
                                        text = stringResource(
                                            R.string.debug_invariant_downloads,
                                            debugState.committedDownloadCount
                                        ),
                                        modifier = Modifier.weight(1f)
                                    )
                                }
                            }
                        }

                        Spacer(modifier = Modifier.height(12.dp))

                        Row(
                            modifier = Modifier.fillMaxWidth(),
                            verticalAlignment = Alignment.CenterVertically,
                            horizontalArrangement = Arrangement.SpaceBetween
                        ) {
                            Text(
                                text = stringResource(R.string.debug_frames_header),
                                style = MaterialTheme.typography.labelLarge,
                                color = MaterialTheme.colorScheme.primary,
                                modifier = Modifier.padding(start = 4.dp)
                            )
                            Row(horizontalArrangement = Arrangement.spacedBy(4.dp)) {
                                FilledTonalButton(
                                    onClick = onCopyFrameLogs,
                                    enabled = debugState.frameLogs.isNotEmpty(),
                                    shape = CircleShape,
                                    contentPadding = PaddingValues(horizontal = 12.dp, vertical = 4.dp),
                                    modifier = Modifier
                                        .minimumInteractiveComponentSize()
                                        .testTag("debug_copy_logs_button")
                                ) {
                                    Icon(
                                        imageVector = Icons.Rounded.ContentCopy,
                                        contentDescription = null,
                                        modifier = Modifier.size(15.dp)
                                    )
                                    Spacer(modifier = Modifier.width(4.dp))
                                    Text(
                                        text = stringResource(R.string.debug_frames_copy),
                                        style = MaterialTheme.typography.labelMedium
                                    )
                                }
                                TextButton(
                                    onClick = onClearFrameLogs,
                                    enabled = debugState.frameLogs.isNotEmpty(),
                                    modifier = Modifier
                                        .minimumInteractiveComponentSize()
                                        .testTag("debug_clear_logs_button")
                                ) {
                                    Icon(
                                        imageVector = Icons.Rounded.DeleteSweep,
                                        contentDescription = null,
                                        modifier = Modifier.size(16.dp)
                                    )
                                    Spacer(modifier = Modifier.width(4.dp))
                                    Text(stringResource(R.string.debug_frames_clear))
                                }
                            }
                        }

                        Spacer(modifier = Modifier.height(6.dp))

                        Surface(
                            shape = RoundedCornerShape(22.dp),
                            color = MaterialTheme.colorScheme.surfaceContainerHighest.copy(alpha = 0.65f),
                            modifier = Modifier.fillMaxWidth()
                        ) {
                            if (debugState.frameLogs.isEmpty()) {
                                Text(
                                    text = stringResource(R.string.debug_frames_empty),
                                    style = MaterialTheme.typography.bodySmall,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                                    modifier = Modifier.padding(18.dp)
                                )
                            } else {
                                Column(
                                    modifier = Modifier.padding(horizontal = 12.dp, vertical = 10.dp),
                                    verticalArrangement = Arrangement.spacedBy(6.dp)
                                ) {
                                    debugState.frameLogs.forEach { entry ->
                                        FrameTraceRow(entry = entry)
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
private fun InvariantBadge(
    text: String,
    modifier: Modifier = Modifier
) {
    Box(
        modifier = modifier
            .clip(RoundedCornerShape(12.dp))
            .background(MaterialTheme.colorScheme.surfaceContainerLow)
            .padding(horizontal = 10.dp, vertical = 7.dp)
    ) {
        Text(
            text = text,
            fontFamily = JetBrainsMonoFamily,
            fontSize = 11.sp,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
            color = MaterialTheme.colorScheme.onSurface
        )
    }
}

@Composable
private fun FrameTraceRow(entry: DebugFrameLogEntry) {
    val palette = LocalExpressivePalette.current
    val (tagBg, tagFg) = when {
        !entry.ok || entry.category == "ERR" ->
            MaterialTheme.colorScheme.errorContainer to MaterialTheme.colorScheme.onErrorContainer
        entry.category == "RX" ->
            palette.mintIconBg to palette.mintIconFg
        entry.category == "CMD" ->
            palette.skyIconBg to palette.skyIconFg
        else ->
            palette.violetIconBg to palette.violetIconFg
    }

    Column(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(12.dp))
            .background(MaterialTheme.colorScheme.surfaceContainerLow)
            .padding(horizontal = 10.dp, vertical = 8.dp)
    ) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Surface(shape = RoundedCornerShape(6.dp), color = tagBg) {
                Text(
                    text = entry.category,
                    fontFamily = JetBrainsMonoFamily,
                    fontSize = 10.sp,
                    fontWeight = FontWeight.Bold,
                    color = tagFg,
                    modifier = Modifier.padding(horizontal = 6.dp, vertical = 1.dp)
                )
            }
            Spacer(modifier = Modifier.width(6.dp))
            Text(
                text = "#%03d · %d %s".format(entry.seq, entry.frameCode, entry.frameName),
                fontFamily = JetBrainsMonoFamily,
                fontSize = 11.5.sp,
                fontWeight = FontWeight.SemiBold,
                color = MaterialTheme.colorScheme.onSurface,
                modifier = Modifier.weight(1f),
                maxLines = 1,
                overflow = TextOverflow.Ellipsis
            )
            Text(
                text = entry.timestampText,
                fontFamily = JetBrainsMonoFamily,
                fontSize = 10.sp,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
        }
        Spacer(modifier = Modifier.height(3.dp))
        Text(
            text = entry.summary,
            fontFamily = JetBrainsMonoFamily,
            fontSize = 11.sp,
            color = if (entry.ok) {
                MaterialTheme.colorScheme.onSurfaceVariant
            } else {
                MaterialTheme.colorScheme.error
            }
        )
    }
}

@Composable
private fun DebugActionRow(
    index: Int,
    count: Int,
    icon: ImageVector,
    badgeBg: Color,
    badgeFg: Color,
    title: String,
    subtitle: String,
    testTag: String,
    monoTitle: Boolean = false,
    onClick: () -> Unit
) {
    ExpressiveGroupTile(
        index = index,
        count = count,
        containerColor = MaterialTheme.colorScheme.surfaceContainer,
        onClick = onClick,
        modifier = Modifier.testTag(testTag)
    ) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = 16.dp, vertical = 13.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            ExpressiveIconBadge(
                icon = icon,
                backgroundColor = badgeBg,
                iconTint = badgeFg,
                size = 40.dp
            )
            Spacer(modifier = Modifier.width(14.dp))
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    text = title,
                    fontFamily = if (monoTitle) JetBrainsMonoFamily else null,
                    style = MaterialTheme.typography.titleSmall
                )
                Spacer(modifier = Modifier.height(2.dp))
                Text(
                    text = subtitle,
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
            }
        }
    }
}
