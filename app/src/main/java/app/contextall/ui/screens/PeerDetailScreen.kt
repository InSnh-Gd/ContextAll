package app.contextall.ui.screens

import androidx.activity.compose.BackHandler
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
import androidx.compose.material.icons.automirrored.rounded.ArrowBack
import androidx.compose.material.icons.rounded.Archive
import androidx.compose.material.icons.rounded.Computer
import androidx.compose.material.icons.rounded.ContentCopy
import androidx.compose.material.icons.rounded.ContentPaste
import androidx.compose.material.icons.rounded.DownloadForOffline
import androidx.compose.material.icons.rounded.FolderOpen
import androidx.compose.material.icons.rounded.LinkOff
import androidx.compose.material.icons.rounded.NoteAdd
import androidx.compose.material.icons.rounded.Storage
import androidx.compose.material.icons.rounded.Sync
import androidx.compose.material.icons.rounded.UploadFile
import androidx.compose.material.icons.rounded.WarningAmber
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilledTonalButton
import androidx.compose.material3.FilledTonalIconButton
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.minimumInteractiveComponentSize
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import app.contextall.R
import app.contextall.core.formatByteSize
import app.contextall.model.PeerDeviceSnapshot
import app.contextall.model.PeerPresence
import app.contextall.model.TransferSnapshot
import app.contextall.ui.components.ExpressiveGapProgressBar
import app.contextall.ui.components.ExpressiveGroupTile
import app.contextall.ui.components.ExpressiveIconBadge
import app.contextall.ui.components.ExpressiveSwitch
import app.contextall.ui.theme.JetBrainsMonoFamily
import app.contextall.ui.theme.LocalExpressivePalette

@Composable
fun PeerDetailScreen(
    peer: PeerDeviceSnapshot,
    peerActiveTransfers: List<TransferSnapshot>,
    showBackButton: Boolean,
    onBack: () -> Unit,
    onLaunchSystemFilePicker: () -> Unit,
    onSendPresetPayload: (fileName: String, sizeBytes: Long) -> Unit,
    onTogglePeerConnection: () -> Unit,
    onCopyPeerId: () -> Unit,
    onClipboardToggle: (Boolean) -> Unit,
    onAskBeforeSaveToggle: (Boolean) -> Unit,
    onUnpairConfirm: () -> Unit,
    modifier: Modifier = Modifier
) {
    if (showBackButton) {
        BackHandler(onBack = onBack)
    }

    var showUnpairDialog by remember { mutableStateOf(false) }
    var showSendSheet by remember { mutableStateOf(false) }
    val palette = LocalExpressivePalette.current

    Box(modifier = modifier.fillMaxSize(), contentAlignment = Alignment.TopCenter) {
        LazyColumn(
            modifier = Modifier
                .fillMaxWidth()
                .widthIn(max = 640.dp)
                .testTag("peer_detail_screen"),
            contentPadding = PaddingValues(start = 16.dp, end = 16.dp, top = 8.dp, bottom = 116.dp),
            verticalArrangement = Arrangement.spacedBy(20.dp)
        ) {
            if (showBackButton) {
                item {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        FilledTonalIconButton(
                            onClick = onBack,
                            modifier = Modifier
                                .minimumInteractiveComponentSize()
                                .testTag("detail_back_button")
                        ) {
                            Icon(
                                imageVector = Icons.AutoMirrored.Rounded.ArrowBack,
                                contentDescription = stringResource(R.string.detail_back_content_desc)
                            )
                        }
                        Spacer(modifier = Modifier.width(12.dp))
                        Text(
                            text = peer.displayName,
                            style = MaterialTheme.typography.headlineMedium
                        )
                    }
                }
            }

            // Hero + Public Key Fingerprint 2-tile group
            item {
                Column(verticalArrangement = Arrangement.spacedBy(2.dp)) {
                    ExpressiveGroupTile(index = 0, count = 2) {
                        Column(
                            modifier = Modifier
                                .fillMaxWidth()
                                .padding(horizontal = 22.dp, vertical = 22.dp),
                            horizontalAlignment = Alignment.CenterHorizontally
                        ) {
                            Surface(
                                shape = RoundedCornerShape(22.dp),
                                color = palette.skyIconBg,
                                modifier = Modifier.size(72.dp)
                            ) {
                                Box(contentAlignment = Alignment.Center) {
                                    Icon(
                                        imageVector = peer.formFactor.toIcon(),
                                        contentDescription = peer.displayName,
                                        tint = palette.skyIconFg,
                                        modifier = Modifier.size(36.dp)
                                    )
                                }
                            }
                            Spacer(modifier = Modifier.height(14.dp))
                            if (!showBackButton) {
                                Text(
                                    text = peer.displayName,
                                    style = MaterialTheme.typography.headlineMedium
                                )
                                Spacer(modifier = Modifier.height(8.dp))
                            }
                            PresenceBadge(presence = peer.presence)
                            if (peer.endpoint != null) {
                                Spacer(modifier = Modifier.height(6.dp))
                                Surface(
                                    shape = CircleShape,
                                    color = MaterialTheme.colorScheme.surfaceContainerHigh
                                ) {
                                    Text(
                                        text = stringResource(R.string.device_endpoint_format, peer.endpoint),
                                        fontFamily = JetBrainsMonoFamily,
                                        fontSize = 11.5.sp,
                                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                                        modifier = Modifier.padding(horizontal = 12.dp, vertical = 4.dp)
                                    )
                                }
                            }
                            Spacer(modifier = Modifier.height(8.dp))
                            Text(
                                text = stringResource(
                                    when (peer.presence) {
                                        PeerPresence.CONNECTED -> R.string.detail_quic_session_badge
                                        PeerPresence.REACHABLE -> R.string.detail_reachable_session_badge
                                        PeerPresence.OFFLINE -> R.string.detail_offline_session_badge
                                    }
                                ),
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                                textAlign = TextAlign.Center
                            )
                            Spacer(modifier = Modifier.height(18.dp))
                            Row(
                                modifier = Modifier.fillMaxWidth(),
                                horizontalArrangement = Arrangement.spacedBy(10.dp)
                            ) {
                                Button(
                                    onClick = { showSendSheet = true },
                                    shape = CircleShape,
                                    modifier = Modifier
                                        .weight(1f)
                                        .height(54.dp)
                                        .testTag("send_file_button")
                                ) {
                                    Icon(
                                        imageVector = Icons.Rounded.UploadFile,
                                        contentDescription = null
                                    )
                                    Spacer(modifier = Modifier.width(8.dp))
                                    Text(
                                        text = stringResource(R.string.detail_send_file_button),
                                        style = MaterialTheme.typography.titleSmall
                                    )
                                }
                                if (peer.presence != PeerPresence.CONNECTED) {
                                    FilledTonalButton(
                                        onClick = onTogglePeerConnection,
                                        shape = RoundedCornerShape(18.dp),
                                        modifier = Modifier
                                            .height(54.dp)
                                            .testTag("reconnect_peer_button")
                                    ) {
                                        Icon(
                                            imageVector = Icons.Rounded.Sync,
                                            contentDescription = null
                                        )
                                        Spacer(modifier = Modifier.width(6.dp))
                                        Text(stringResource(R.string.detail_reconnect_button))
                                    }
                                }
                            }
                        }
                    }

                    ExpressiveGroupTile(
                        index = 1,
                        count = 2,
                        onClick = onCopyPeerId
                    ) {
                        Row(
                            modifier = Modifier
                                .fillMaxWidth()
                                .padding(horizontal = 20.dp, vertical = 14.dp),
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            Column(modifier = Modifier.weight(1f)) {
                                Text(
                                    text = stringResource(R.string.detail_device_id_card_label),
                                    style = MaterialTheme.typography.labelMedium,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant
                                )
                                Spacer(modifier = Modifier.height(4.dp))
                                Text(
                                    text = peer.deviceId,
                                    fontFamily = JetBrainsMonoFamily,
                                    fontSize = 12.sp,
                                    color = MaterialTheme.colorScheme.onSurface
                                )
                            }
                            FilledTonalIconButton(
                                onClick = onCopyPeerId,
                                modifier = Modifier
                                    .minimumInteractiveComponentSize()
                                    .testTag("copy_peer_id_button")
                            ) {
                                Icon(
                                    imageVector = Icons.Rounded.ContentCopy,
                                    contentDescription = stringResource(R.string.detail_copy_peer_id_desc),
                                    modifier = Modifier.size(18.dp)
                                )
                            }
                        }
                    }
                }
            }

            // Active transfers for this peer
            if (peerActiveTransfers.isNotEmpty()) {
                item {
                    SectionHeader(text = stringResource(R.string.detail_section_active_transfers))
                    Spacer(modifier = Modifier.height(10.dp))
                    Column(verticalArrangement = Arrangement.spacedBy(2.dp)) {
                        peerActiveTransfers.forEachIndexed { idx, tx ->
                            ExpressiveGroupTile(index = idx, count = peerActiveTransfers.size) {
                                Column(modifier = Modifier.padding(horizontal = 18.dp, vertical = 14.dp)) {
                                    Row(
                                        modifier = Modifier.fillMaxWidth(),
                                        horizontalArrangement = Arrangement.SpaceBetween
                                    ) {
                                        Text(
                                            text = tx.fileName,
                                            style = MaterialTheme.typography.titleSmall
                                        )
                                        Text(
                                            text = formatByteSize(tx.totalSize),
                                            style = MaterialTheme.typography.bodySmall,
                                            color = MaterialTheme.colorScheme.onSurfaceVariant
                                        )
                                    }
                                    Spacer(modifier = Modifier.height(8.dp))
                                    ExpressiveGapProgressBar(progress = tx.progressFraction)
                                }
                            }
                        }
                    }
                }
            }

            // Segmented 2-switch Policy Group
            item {
                SectionHeader(text = stringResource(R.string.detail_section_policies))
                Spacer(modifier = Modifier.height(10.dp))
                Column(verticalArrangement = Arrangement.spacedBy(2.dp)) {
                    ExpressiveGroupTile(
                        index = 0,
                        count = 2,
                        onClick = { onClipboardToggle(!peer.clipboardSync) }
                    ) {
                        PolicySwitchRowContent(
                            icon = Icons.Rounded.ContentPaste,
                            badgeBg = palette.mintIconBg,
                            badgeFg = palette.mintIconFg,
                            title = stringResource(R.string.detail_clipboard_sync_title),
                            subtitle = stringResource(R.string.detail_clipboard_sync_subtitle),
                            checked = peer.clipboardSync,
                            onCheckedChange = onClipboardToggle,
                            testTag = "switch_clipboard_sync"
                        )
                    }
                    ExpressiveGroupTile(
                        index = 1,
                        count = 2,
                        onClick = { onAskBeforeSaveToggle(!peer.askBeforeSave) }
                    ) {
                        PolicySwitchRowContent(
                            icon = Icons.Rounded.DownloadForOffline,
                            badgeBg = palette.amberIconBg,
                            badgeFg = palette.amberIconFg,
                            title = stringResource(R.string.detail_ask_before_save_title),
                            subtitle = stringResource(R.string.detail_ask_before_save_subtitle),
                            checked = peer.askBeforeSave,
                            onCheckedChange = onAskBeforeSaveToggle,
                            testTag = "switch_ask_before_save"
                        )
                    }
                }
            }

            // Danger Unpair Tile
            item {
                SectionHeader(text = stringResource(R.string.detail_section_danger))
                Spacer(modifier = Modifier.height(10.dp))
                ExpressiveGroupTile(
                    index = 0,
                    count = 1,
                    onClick = { showUnpairDialog = true },
                    modifier = Modifier.testTag("unpair_device_card")
                ) {
                    Row(
                        modifier = Modifier.padding(horizontal = 18.dp, vertical = 16.dp),
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        ExpressiveIconBadge(
                            icon = Icons.Rounded.LinkOff,
                            backgroundColor = MaterialTheme.colorScheme.errorContainer,
                            iconTint = MaterialTheme.colorScheme.onErrorContainer
                        )
                        Spacer(modifier = Modifier.width(16.dp))
                        Column(modifier = Modifier.weight(1f)) {
                            Text(
                                text = stringResource(R.string.detail_unpair_title),
                                style = MaterialTheme.typography.titleSmall,
                                color = MaterialTheme.colorScheme.error
                            )
                            Spacer(modifier = Modifier.height(2.dp))
                            Text(
                                text = stringResource(R.string.detail_unpair_subtitle),
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant
                            )
                        }
                    }
                }
            }
        }
    }

    if (showSendSheet) {
        SendFileOptionsSheet(
            peerName = peer.displayName,
            onDismiss = { showSendSheet = false },
            onPickSystemDocument = {
                showSendSheet = false
                onLaunchSystemFilePicker()
            },
            onSendPreset = { name, bytes ->
                showSendSheet = false
                onSendPresetPayload(name, bytes)
            }
        )
    }

    if (showUnpairDialog) {
        AlertDialog(
            onDismissRequest = { showUnpairDialog = false },
            shape = RoundedCornerShape(28.dp),
            title = { Text(stringResource(R.string.unpair_dialog_title, peer.displayName)) },
            text = { Text(stringResource(R.string.unpair_dialog_body)) },
            confirmButton = {
                TextButton(
                    onClick = {
                        showUnpairDialog = false
                        onUnpairConfirm()
                    },
                    modifier = Modifier.testTag("confirm_unpair_button")
                ) {
                    Text(
                        text = stringResource(R.string.unpair_confirm_button),
                        color = MaterialTheme.colorScheme.error
                    )
                }
            },
            dismissButton = {
                TextButton(onClick = { showUnpairDialog = false }) {
                    Text(stringResource(R.string.common_cancel_button))
                }
            }
        )
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun SendFileOptionsSheet(
    peerName: String,
    onDismiss: () -> Unit,
    onPickSystemDocument: () -> Unit,
    onSendPreset: (String, Long) -> Unit
) {
    val palette = LocalExpressivePalette.current
    ModalBottomSheet(
        onDismissRequest = onDismiss,
        shape = RoundedCornerShape(topStart = 32.dp, topEnd = 32.dp),
        containerColor = MaterialTheme.colorScheme.surfaceContainerLow
    ) {
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = 20.dp)
                .padding(bottom = 28.dp)
                .testTag("send_file_sheet")
        ) {
            Text(
                text = stringResource(R.string.send_sheet_title, peerName),
                style = MaterialTheme.typography.titleLarge,
                modifier = Modifier.padding(horizontal = 6.dp)
            )
            Spacer(modifier = Modifier.height(4.dp))
            Text(
                text = stringResource(R.string.send_sheet_subtitle),
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(horizontal = 6.dp)
            )
            Spacer(modifier = Modifier.height(16.dp))

            Column(verticalArrangement = Arrangement.spacedBy(2.dp)) {
                ExpressiveGroupTile(
                    index = 0,
                    count = 5,
                    containerColor = MaterialTheme.colorScheme.primaryContainer,
                    onClick = onPickSystemDocument,
                    modifier = Modifier.testTag("pick_system_document_tile")
                ) {
                    Row(
                        modifier = Modifier.padding(horizontal = 18.dp, vertical = 16.dp),
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        ExpressiveIconBadge(
                            icon = Icons.Rounded.FolderOpen,
                            backgroundColor = MaterialTheme.colorScheme.primary,
                            iconTint = MaterialTheme.colorScheme.onPrimary
                        )
                        Spacer(modifier = Modifier.width(14.dp))
                        Column {
                            Text(
                                text = stringResource(R.string.send_sheet_pick_system),
                                style = MaterialTheme.typography.titleSmall,
                                color = MaterialTheme.colorScheme.onPrimaryContainer
                            )
                            Text(
                                text = stringResource(R.string.send_sheet_pick_system_sub),
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onPrimaryContainer.copy(alpha = 0.8f)
                            )
                        }
                    }
                }

                SendPresetRow(
                    index = 1,
                    count = 5,
                    icon = Icons.Rounded.Archive,
                    badgeBg = palette.skyIconBg,
                    badgeFg = palette.skyIconFg,
                    label = stringResource(R.string.send_sheet_preset_small),
                    testTag = "send_preset_small",
                    onClick = { onSendPreset("diagnostics.tar.gz", 18_400_000L) }
                )
                SendPresetRow(
                    index = 2,
                    count = 5,
                    icon = Icons.Rounded.Storage,
                    badgeBg = palette.violetIconBg,
                    badgeFg = palette.violetIconFg,
                    label = stringResource(R.string.send_sheet_preset_large),
                    testTag = "send_preset_large",
                    onClick = { onSendPreset("rootfs-arm64.img", 142_600_000L) }
                )
                SendPresetRow(
                    index = 3,
                    count = 5,
                    icon = Icons.Rounded.NoteAdd,
                    badgeBg = palette.mintIconBg,
                    badgeFg = palette.mintIconFg,
                    label = stringResource(R.string.send_sheet_preset_zero),
                    testTag = "send_preset_zero",
                    onClick = { onSendPreset(".ready", 0L) }
                )
                SendPresetRow(
                    index = 4,
                    count = 5,
                    icon = Icons.Rounded.WarningAmber,
                    badgeBg = MaterialTheme.colorScheme.errorContainer,
                    badgeFg = MaterialTheme.colorScheme.onErrorContainer,
                    label = stringResource(R.string.send_sheet_preset_oversize),
                    testTag = "send_preset_oversize",
                    onClick = { onSendPreset("dataset-raw.bin", 1_120_000_000L) }
                )
            }
        }
    }
}

@Composable
private fun SendPresetRow(
    index: Int,
    count: Int,
    icon: ImageVector,
    badgeBg: Color,
    badgeFg: Color,
    label: String,
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
            modifier = Modifier.padding(horizontal = 18.dp, vertical = 14.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            ExpressiveIconBadge(icon = icon, backgroundColor = badgeBg, iconTint = badgeFg, size = 40.dp)
            Spacer(modifier = Modifier.width(14.dp))
            Text(text = label, style = MaterialTheme.typography.titleSmall)
        }
    }
}

@Composable
private fun PolicySwitchRowContent(
    icon: ImageVector,
    badgeBg: Color,
    badgeFg: Color,
    title: String,
    subtitle: String,
    checked: Boolean,
    onCheckedChange: (Boolean) -> Unit,
    testTag: String
) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = 18.dp, vertical = 16.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        ExpressiveIconBadge(icon = icon, backgroundColor = badgeBg, iconTint = badgeFg)
        Spacer(modifier = Modifier.width(16.dp))
        Column(modifier = Modifier.weight(1f)) {
            Text(text = title, style = MaterialTheme.typography.titleSmall)
            Spacer(modifier = Modifier.height(2.dp))
            Text(
                text = subtitle,
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
        }
        Spacer(modifier = Modifier.width(12.dp))
        ExpressiveSwitch(
            checked = checked,
            onCheckedChange = onCheckedChange,
            modifier = Modifier.testTag(testTag)
        )
    }
}

@Composable
fun EmptyDetailPlaceholder(modifier: Modifier = Modifier) {
    val palette = LocalExpressivePalette.current
    Box(
        modifier = modifier
            .fillMaxSize()
            .padding(24.dp),
        contentAlignment = Alignment.Center
    ) {
        Surface(
            shape = RoundedCornerShape(28.dp),
            color = MaterialTheme.colorScheme.surfaceContainerLow,
            modifier = Modifier.widthIn(max = 420.dp)
        ) {
            Column(
                modifier = Modifier.padding(32.dp),
                horizontalAlignment = Alignment.CenterHorizontally
            ) {
                ExpressiveIconBadge(
                    icon = Icons.Rounded.Computer,
                    backgroundColor = palette.skyIconBg,
                    iconTint = palette.skyIconFg,
                    size = 56.dp
                )
                Spacer(modifier = Modifier.height(16.dp))
                Text(
                    text = stringResource(R.string.empty_detail_pane_title),
                    style = MaterialTheme.typography.titleLarge
                )
                Spacer(modifier = Modifier.height(6.dp))
                Text(
                    text = stringResource(R.string.empty_detail_pane_subtitle),
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    textAlign = TextAlign.Center
                )
            }
        }
    }
}
