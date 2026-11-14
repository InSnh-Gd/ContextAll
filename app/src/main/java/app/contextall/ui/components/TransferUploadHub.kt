package app.contextall.ui.components

import android.content.ClipboardManager
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
import androidx.compose.material.icons.rounded.CloudUpload
import androidx.compose.material.icons.rounded.ContentPaste
import androidx.compose.material.icons.rounded.Description
import androidx.compose.material.icons.rounded.Devices
import androidx.compose.material.icons.rounded.FolderOpen
import androidx.compose.material.icons.rounded.NoteAdd
import androidx.compose.material.icons.rounded.PhotoLibrary
import androidx.compose.material.icons.rounded.Storage
import androidx.compose.material.icons.rounded.WarningAmber
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilledTonalButton
import androidx.compose.material3.FilterChip
import androidx.compose.material3.FilterChipDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
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
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import app.contextall.R
import app.contextall.core.computeSegmentCount
import app.contextall.core.formatByteSize
import app.contextall.model.PeerDeviceSnapshot
import app.contextall.model.PeerPresence
import app.contextall.model.SessionTelemetrySnapshot
import app.contextall.model.TransferDirection
import app.contextall.model.TransferSnapshot
import app.contextall.ui.screens.toIcon
import app.contextall.ui.theme.JetBrainsMonoFamily
import app.contextall.ui.theme.LocalExpressivePalette

@Composable
fun TransferUploadHubCard(
    trustedPeers: List<PeerDeviceSnapshot>,
    activeTransfers: List<TransferSnapshot>,
    debugMode: Boolean = false,
    onPickLocalFiles: (targetDeviceId: String) -> Unit,
    onPickMediaFiles: (targetDeviceId: String) -> Unit,
    onSendTextFile: (targetDeviceId: String, fileName: String, content: String) -> Unit,
    onSendPresetPayload: (targetDeviceId: String, fileName: String, totalBytes: Long) -> Unit,
    onNavigateToDevices: () -> Unit,
    modifier: Modifier = Modifier
) {
    val context = LocalContext.current
    val palette = LocalExpressivePalette.current
    var selectedPeerId by rememberSaveable { mutableStateOf<String?>(null) }
    var showTextUploadDialog by remember { mutableStateOf(false) }
    var initialDialogFileName by remember { mutableStateOf("备忘录.txt") }
    var initialDialogContent by remember { mutableStateOf("") }
    var showPresetSheet by remember { mutableStateOf(false) }

    LaunchedEffect(trustedPeers) {
        val currentValid = trustedPeers.any { it.deviceId == selectedPeerId }
        if (!currentValid) {
            selectedPeerId = trustedPeers.firstOrNull { it.presence == PeerPresence.CONNECTED }?.deviceId
                ?: trustedPeers.firstOrNull()?.deviceId
        }
    }

    val targetPeer = trustedPeers.firstOrNull { it.deviceId == selectedPeerId }
        ?: trustedPeers.firstOrNull()

    val busyOutboundPeerIds = remember(activeTransfers) {
        activeTransfers
            .filter { it.direction == TransferDirection.SEND && !it.completed }
            .map { it.deviceId }
            .toSet()
    }

    Column(
        modifier = modifier
            .fillMaxWidth()
            .testTag("transfers_upload_hub"),
        verticalArrangement = Arrangement.spacedBy(2.dp)
    ) {
        // Tile 0: Upload Hub Header + Target Peer Selector
        ExpressiveGroupTile(index = 0, count = if (targetPeer != null) 2 else 1) {
            Column(modifier = Modifier.padding(horizontal = 18.dp, vertical = 16.dp)) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    ExpressiveIconBadge(
                        icon = Icons.Rounded.CloudUpload,
                        backgroundColor = MaterialTheme.colorScheme.primaryContainer,
                        iconTint = MaterialTheme.colorScheme.onPrimaryContainer
                    )
                    Spacer(modifier = Modifier.width(14.dp))
                    Column(modifier = Modifier.weight(1f)) {
                        Text(
                            text = stringResource(R.string.transfers_upload_hub_title),
                            style = MaterialTheme.typography.titleMedium
                        )
                        Spacer(modifier = Modifier.height(2.dp))
                        Text(
                            text = stringResource(R.string.transfers_upload_hub_sub),
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
                    }
                }

                Spacer(modifier = Modifier.height(14.dp))

                if (trustedPeers.isEmpty()) {
                    Surface(
                        shape = RoundedCornerShape(18.dp),
                        color = MaterialTheme.colorScheme.surfaceContainer,
                        modifier = Modifier.fillMaxWidth()
                    ) {
                        Column(
                            modifier = Modifier.padding(16.dp),
                            horizontalAlignment = Alignment.Start
                        ) {
                            Text(
                                text = stringResource(R.string.transfers_no_trusted_title),
                                style = MaterialTheme.typography.titleSmall
                            )
                            Spacer(modifier = Modifier.height(4.dp))
                            Text(
                                text = stringResource(R.string.transfers_no_trusted_sub),
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant
                            )
                            Spacer(modifier = Modifier.height(12.dp))
                            FilledTonalButton(
                                onClick = onNavigateToDevices,
                                shape = CircleShape,
                                modifier = Modifier
                                    .height(48.dp)
                                    .testTag("transfers_go_pair_button")
                            ) {
                                Icon(
                                    imageVector = Icons.Rounded.Devices,
                                    contentDescription = null,
                                    modifier = Modifier.size(18.dp)
                                )
                                Spacer(modifier = Modifier.width(8.dp))
                                Text(stringResource(R.string.transfers_go_pair_button))
                            }
                        }
                    }
                } else {
                    Text(
                        text = stringResource(R.string.transfers_target_peer_label),
                        style = MaterialTheme.typography.labelMedium,
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                    Spacer(modifier = Modifier.height(6.dp))
                    LazyRow(
                        horizontalArrangement = Arrangement.spacedBy(8.dp),
                        contentPadding = PaddingValues(vertical = 2.dp)
                    ) {
                        items(trustedPeers, key = { it.deviceId }) { peer ->
                            val isSelected = peer.deviceId == targetPeer?.deviceId
                            val isOnline = peer.presence == PeerPresence.CONNECTED
                            val isSlotBusy = peer.deviceId in busyOutboundPeerIds
                            FilterChip(
                                selected = isSelected,
                                onClick = { selectedPeerId = peer.deviceId },
                                leadingIcon = {
                                    Box(contentAlignment = Alignment.BottomEnd) {
                                        Icon(
                                            imageVector = peer.formFactor.toIcon(),
                                            contentDescription = null,
                                            modifier = Modifier.size(18.dp)
                                        )
                                        Box(
                                            modifier = Modifier
                                                .size(7.dp)
                                                .clip(CircleShape)
                                                .background(
                                                    if (isOnline) palette.mintIconFg
                                                    else MaterialTheme.colorScheme.outline
                                                )
                                        )
                                    }
                                },
                                label = {
                                    Row(verticalAlignment = Alignment.CenterVertically) {
                                        Text(
                                            text = peer.displayName,
                                            maxLines = 1,
                                            overflow = TextOverflow.Ellipsis
                                        )
                                        if (isSlotBusy) {
                                            Spacer(modifier = Modifier.width(6.dp))
                                            Text(
                                                text = "· ${stringResource(R.string.transfers_peer_slot_busy)}",
                                                style = MaterialTheme.typography.labelMedium,
                                                color = MaterialTheme.colorScheme.primary
                                            )
                                        }
                                    }
                                },
                                shape = CircleShape,
                                colors = FilterChipDefaults.filterChipColors(
                                    selectedContainerColor = MaterialTheme.colorScheme.primaryContainer,
                                    selectedLabelColor = MaterialTheme.colorScheme.onPrimaryContainer
                                ),
                                modifier = Modifier
                                    .minimumInteractiveComponentSize()
                                    .testTag("upload_target_chip_${peer.deviceId}")
                            )
                        }
                    }
                }
            }
        }

        // Tile 1: Primary Upload Actions (Files + Photos/Videos + Text File + Presets)
        if (targetPeer != null) {
            ExpressiveGroupTile(index = 1, count = 2) {
                Column(modifier = Modifier.padding(horizontal = 18.dp, vertical = 14.dp)) {
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.spacedBy(10.dp)
                    ) {
                        UploadPrimaryActionCard(
                            icon = Icons.Rounded.FolderOpen,
                            title = stringResource(R.string.transfers_btn_pick_files),
                            subtitle = stringResource(R.string.transfers_btn_pick_files_sub),
                            containerColor = MaterialTheme.colorScheme.primaryContainer,
                            contentColor = MaterialTheme.colorScheme.onPrimaryContainer,
                            badgeBg = MaterialTheme.colorScheme.primary,
                            badgeFg = MaterialTheme.colorScheme.onPrimary,
                            onClick = { onPickLocalFiles(targetPeer.deviceId) },
                            testTag = "upload_pick_files_button",
                            modifier = Modifier.weight(1f)
                        )
                        UploadPrimaryActionCard(
                            icon = Icons.Rounded.PhotoLibrary,
                            title = stringResource(R.string.transfers_btn_pick_media),
                            subtitle = stringResource(R.string.transfers_btn_pick_media_sub),
                            containerColor = MaterialTheme.colorScheme.surfaceContainer,
                            contentColor = MaterialTheme.colorScheme.onSurface,
                            badgeBg = palette.skyIconBg,
                            badgeFg = palette.skyIconFg,
                            onClick = { onPickMediaFiles(targetPeer.deviceId) },
                            testTag = "upload_pick_media_button",
                            modifier = Modifier.weight(1f)
                        )
                    }

                    Spacer(modifier = Modifier.height(10.dp))

                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.spacedBy(10.dp)
                    ) {
                        FilledTonalButton(
                            onClick = {
                                initialDialogFileName = "备忘录.txt"
                                initialDialogContent = ""
                                showTextUploadDialog = true
                            },
                            shape = CircleShape,
                            modifier = Modifier
                                .weight(1f)
                                .height(48.dp)
                                .testTag("upload_text_file_button")
                        ) {
                            Icon(
                                imageVector = Icons.Rounded.Description,
                                contentDescription = null,
                                modifier = Modifier.size(18.dp)
                            )
                            Spacer(modifier = Modifier.width(6.dp))
                            Text(
                                text = stringResource(R.string.transfers_btn_text_file),
                                maxLines = 1,
                                overflow = TextOverflow.Ellipsis
                            )
                        }

                        OutlinedButton(
                            onClick = {
                                val cm = context.getSystemService(ClipboardManager::class.java)
                                val clipText = cm?.primaryClip
                                    ?.takeIf { it.itemCount > 0 }
                                    ?.getItemAt(0)
                                    ?.coerceToText(context)
                                    ?.toString()
                                    .orEmpty()
                                initialDialogFileName = "剪贴板文本.txt"
                                initialDialogContent = clipText
                                showTextUploadDialog = true
                            },
                            shape = RoundedCornerShape(16.dp),
                            modifier = Modifier
                                .weight(1f)
                                .height(48.dp)
                                .testTag("upload_clipboard_file_button")
                        ) {
                            Icon(
                                imageVector = Icons.Rounded.ContentPaste,
                                contentDescription = null,
                                modifier = Modifier.size(18.dp)
                            )
                            Spacer(modifier = Modifier.width(6.dp))
                            Text(
                                text = stringResource(R.string.transfers_btn_clipboard_file),
                                maxLines = 1,
                                overflow = TextOverflow.Ellipsis
                            )
                        }
                    }

                    if (debugMode) {
                        Spacer(modifier = Modifier.height(10.dp))
                        Surface(
                            onClick = { showPresetSheet = true },
                            shape = RoundedCornerShape(16.dp),
                            color = palette.amberIconBg,
                            modifier = Modifier
                                .fillMaxWidth()
                                .minimumInteractiveComponentSize()
                                .testTag("upload_debug_preset_button")
                        ) {
                            Row(
                                modifier = Modifier.padding(horizontal = 14.dp, vertical = 11.dp),
                                verticalAlignment = Alignment.CenterVertically,
                                horizontalArrangement = Arrangement.Center
                            ) {
                                Icon(
                                    imageVector = Icons.Rounded.BugReport,
                                    contentDescription = null,
                                    tint = palette.amberIconFg,
                                    modifier = Modifier.size(18.dp)
                                )
                                Spacer(modifier = Modifier.width(8.dp))
                                Text(
                                    text = stringResource(R.string.debug_transfers_inject_btn),
                                    fontFamily = JetBrainsMonoFamily,
                                    fontWeight = FontWeight.SemiBold,
                                    fontSize = 11.5.sp,
                                    color = palette.amberIconFg,
                                    maxLines = 1,
                                    overflow = TextOverflow.Ellipsis
                                )
                            }
                        }
                    }
                }
            }
        }
    }

    if (showTextUploadDialog && targetPeer != null) {
        ComposeTextFileUploadDialog(
            initialFileName = initialDialogFileName,
            initialContent = initialDialogContent,
            onDismiss = { showTextUploadDialog = false },
            onConfirmUpload = { fileName, content ->
                showTextUploadDialog = false
                onSendTextFile(targetPeer.deviceId, fileName, content)
            }
        )
    }

    if (showPresetSheet && targetPeer != null) {
        PresetUploadBottomSheet(
            peerName = targetPeer.displayName,
            onDismiss = { showPresetSheet = false },
            onSelectPreset = { name, bytes ->
                showPresetSheet = false
                onSendPresetPayload(targetPeer.deviceId, name, bytes)
            }
        )
    }
}

@Composable
private fun UploadPrimaryActionCard(
    icon: ImageVector,
    title: String,
    subtitle: String,
    containerColor: Color,
    contentColor: Color,
    badgeBg: Color,
    badgeFg: Color,
    onClick: () -> Unit,
    testTag: String,
    modifier: Modifier = Modifier
) {
    Surface(
        onClick = onClick,
        shape = RoundedCornerShape(20.dp),
        color = containerColor,
        modifier = modifier.testTag(testTag)
    ) {
        Column(modifier = Modifier.padding(horizontal = 14.dp, vertical = 14.dp)) {
            Surface(
                shape = RoundedCornerShape(13.dp),
                color = badgeBg,
                modifier = Modifier.size(38.dp)
            ) {
                Box(contentAlignment = Alignment.Center) {
                    Icon(
                        imageVector = icon,
                        contentDescription = null,
                        tint = badgeFg,
                        modifier = Modifier.size(20.dp)
                    )
                }
            }
            Spacer(modifier = Modifier.height(10.dp))
            Text(
                text = title,
                style = MaterialTheme.typography.titleSmall,
                color = contentColor,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis
            )
            Spacer(modifier = Modifier.height(2.dp))
            Text(
                text = subtitle,
                style = MaterialTheme.typography.bodySmall,
                fontSize = 11.5.sp,
                color = contentColor.copy(alpha = 0.78f),
                maxLines = 1,
                overflow = TextOverflow.Ellipsis
            )
        }
    }
}

@Composable
fun TransferTelemetrySummaryStrip(
    telemetry: SessionTelemetrySnapshot,
    modifier: Modifier = Modifier
) {
    val palette = LocalExpressivePalette.current
    Row(
        modifier = modifier
            .fillMaxWidth()
            .testTag("transfers_kpi_strip"),
        horizontalArrangement = Arrangement.spacedBy(8.dp)
    ) {
        CompactTransferStatPill(
            label = stringResource(R.string.transfers_kpi_uploaded),
            value = formatByteSize(telemetry.totalUploadBytes),
            accent = MaterialTheme.colorScheme.primary,
            bg = MaterialTheme.colorScheme.surfaceContainerLow,
            modifier = Modifier.weight(1f)
        )
        CompactTransferStatPill(
            label = stringResource(R.string.transfers_kpi_downloaded),
            value = formatByteSize(telemetry.totalDownloadBytes),
            accent = palette.skyIconFg,
            bg = MaterialTheme.colorScheme.surfaceContainerLow,
            modifier = Modifier.weight(1f)
        )
        CompactTransferStatPill(
            label = stringResource(R.string.transfers_kpi_segments),
            value = telemetry.verifiedSegmentsCount.toString(),
            accent = palette.mintIconFg,
            bg = MaterialTheme.colorScheme.surfaceContainerLow,
            modifier = Modifier.weight(1f)
        )
    }
}

@Composable
private fun CompactTransferStatPill(
    label: String,
    value: String,
    accent: Color,
    bg: Color,
    modifier: Modifier = Modifier
) {
    Surface(
        shape = RoundedCornerShape(20.dp),
        color = bg,
        modifier = modifier
    ) {
        Column(
            modifier = Modifier.padding(horizontal = 12.dp, vertical = 10.dp),
            horizontalAlignment = Alignment.Start
        ) {
            Text(
                text = label,
                style = MaterialTheme.typography.labelMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                maxLines = 1
            )
            Spacer(modifier = Modifier.height(2.dp))
            Text(
                text = value,
                fontFamily = JetBrainsMonoFamily,
                fontWeight = FontWeight.Bold,
                fontSize = 14.5.sp,
                color = accent,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis
            )
        }
    }
}

@Composable
private fun ComposeTextFileUploadDialog(
    initialFileName: String,
    initialContent: String,
    onDismiss: () -> Unit,
    onConfirmUpload: (fileName: String, content: String) -> Unit
) {
    var fileName by remember(initialFileName) { mutableStateOf(initialFileName) }
    var content by remember(initialContent) { mutableStateOf(initialContent) }

    val utf8Bytes = remember(content) { content.toByteArray(Charsets.UTF_8).size.toLong() }
    val segments = remember(utf8Bytes) { computeSegmentCount(utf8Bytes) }

    AlertDialog(
        onDismissRequest = onDismiss,
        shape = RoundedCornerShape(28.dp),
        title = { Text(stringResource(R.string.transfers_text_dialog_title)) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
                OutlinedTextField(
                    value = fileName,
                    onValueChange = { fileName = it },
                    singleLine = true,
                    label = { Text(stringResource(R.string.transfers_text_filename_label)) },
                    placeholder = { Text(stringResource(R.string.transfers_text_filename_placeholder)) },
                    shape = RoundedCornerShape(16.dp),
                    modifier = Modifier
                        .fillMaxWidth()
                        .testTag("text_upload_filename_input")
                )
                OutlinedTextField(
                    value = content,
                    onValueChange = { content = it },
                    minLines = 4,
                    maxLines = 7,
                    label = { Text(stringResource(R.string.transfers_text_content_label)) },
                    placeholder = { Text(stringResource(R.string.transfers_text_content_hint)) },
                    shape = RoundedCornerShape(16.dp),
                    modifier = Modifier
                        .fillMaxWidth()
                        .testTag("text_upload_content_input")
                )
                Text(
                    text = stringResource(
                        R.string.transfers_text_size_preview,
                        formatByteSize(utf8Bytes),
                        segments
                    ),
                    fontFamily = JetBrainsMonoFamily,
                    fontSize = 11.5.sp,
                    color = MaterialTheme.colorScheme.primary
                )
            }
        },
        confirmButton = {
            Button(
                onClick = {
                    val finalName = fileName.trim().ifEmpty { "备忘录.txt" }
                    onConfirmUpload(finalName, content)
                },
                shape = CircleShape,
                modifier = Modifier.testTag("text_upload_confirm_button")
            ) {
                Text(stringResource(R.string.transfers_text_submit))
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(stringResource(R.string.common_cancel_button))
            }
        }
    )
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun PresetUploadBottomSheet(
    peerName: String,
    onDismiss: () -> Unit,
    onSelectPreset: (fileName: String, totalBytes: Long) -> Unit
) {
    val palette = LocalExpressivePalette.current
    val sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true)
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
                .verticalScroll(rememberScrollState())
                .padding(horizontal = 20.dp)
                .padding(bottom = 20.dp)
                .testTag("transfers_preset_sheet")
        ) {
            Text(
                text = stringResource(R.string.debug_payload_section_header),
                fontFamily = JetBrainsMonoFamily,
                fontSize = 11.5.sp,
                color = palette.amberIconFg,
                modifier = Modifier.padding(horizontal = 6.dp)
            )
            Spacer(modifier = Modifier.height(4.dp))
            Text(
                text = stringResource(R.string.send_sheet_title, peerName),
                style = MaterialTheme.typography.titleLarge,
                modifier = Modifier.padding(horizontal = 6.dp)
            )
            Spacer(modifier = Modifier.height(14.dp))

            Column(verticalArrangement = Arrangement.spacedBy(2.dp)) {
                PresetTileRow(
                    index = 0,
                    count = 4,
                    icon = Icons.Rounded.Archive,
                    badgeBg = palette.skyIconBg,
                    badgeFg = palette.skyIconFg,
                    label = stringResource(R.string.send_sheet_preset_small),
                    subtitle = stringResource(R.string.debug_payload_small_sub),
                    testTag = "transfers_preset_small",
                    onClick = { onSelectPreset("diagnostics.tar.gz", 18_400_000L) }
                )
                PresetTileRow(
                    index = 1,
                    count = 4,
                    icon = Icons.Rounded.Storage,
                    badgeBg = palette.violetIconBg,
                    badgeFg = palette.violetIconFg,
                    label = stringResource(R.string.send_sheet_preset_large),
                    subtitle = stringResource(R.string.debug_payload_large_sub),
                    testTag = "transfers_preset_large",
                    onClick = { onSelectPreset("rootfs-arm64.img", 142_600_000L) }
                )
                PresetTileRow(
                    index = 2,
                    count = 4,
                    icon = Icons.Rounded.NoteAdd,
                    badgeBg = palette.mintIconBg,
                    badgeFg = palette.mintIconFg,
                    label = stringResource(R.string.send_sheet_preset_zero),
                    subtitle = stringResource(R.string.debug_payload_zero_sub),
                    testTag = "transfers_preset_zero",
                    onClick = { onSelectPreset(".ready", 0L) }
                )
                PresetTileRow(
                    index = 3,
                    count = 4,
                    icon = Icons.Rounded.WarningAmber,
                    badgeBg = MaterialTheme.colorScheme.errorContainer,
                    badgeFg = MaterialTheme.colorScheme.onErrorContainer,
                    label = stringResource(R.string.send_sheet_preset_oversize),
                    subtitle = stringResource(R.string.debug_payload_oversize_sub),
                    testTag = "transfers_preset_oversize",
                    onClick = { onSelectPreset("dataset-raw.bin", 1_120_000_000L) }
                )
            }
        }
    }
}

@Composable
private fun PresetTileRow(
    index: Int,
    count: Int,
    icon: ImageVector,
    badgeBg: Color,
    badgeFg: Color,
    label: String,
    subtitle: String,
    testTag: String,
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
            modifier = Modifier.padding(horizontal = 16.dp, vertical = 12.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            ExpressiveIconBadge(icon = icon, backgroundColor = badgeBg, iconTint = badgeFg, size = 38.dp)
            Spacer(modifier = Modifier.width(12.dp))
            Column(modifier = Modifier.weight(1f)) {
                Text(text = label, style = MaterialTheme.typography.titleSmall)
                Text(
                    text = subtitle,
                    fontFamily = JetBrainsMonoFamily,
                    fontSize = 11.sp,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
            }
        }
    }
}
