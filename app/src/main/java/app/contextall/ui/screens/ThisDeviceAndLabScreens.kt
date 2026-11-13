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
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.rounded.HelpOutline
import androidx.compose.material.icons.automirrored.rounded.KeyboardArrowRight
import androidx.compose.material.icons.rounded.Badge
import androidx.compose.material.icons.rounded.ContentCopy
import androidx.compose.material.icons.rounded.ContentPasteGo
import androidx.compose.material.icons.rounded.DarkMode
import androidx.compose.material.icons.rounded.Edit
import androidx.compose.material.icons.rounded.Fingerprint
import androidx.compose.material.icons.rounded.FolderZip
import androidx.compose.material.icons.rounded.KeyOff
import androidx.compose.material.icons.rounded.Memory
import androidx.compose.material.icons.rounded.MoveToInbox
import androidx.compose.material.icons.rounded.NotificationsActive
import androidx.compose.material.icons.rounded.Palette
import androidx.compose.material.icons.rounded.Password
import androidx.compose.material.icons.rounded.Radar
import androidx.compose.material.icons.rounded.Science
import androidx.compose.material.icons.rounded.SyncProblem
import androidx.compose.material.icons.rounded.WarningAmber
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilledTonalIconButton
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.OutlinedTextField
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
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import app.contextall.R
import app.contextall.core.DisplayNames
import app.contextall.ui.components.ExpressiveGroupTile
import app.contextall.ui.components.ExpressiveIconBadge
import app.contextall.ui.components.ExpressiveScreenHeader
import app.contextall.ui.components.ExpressiveSwitch
import app.contextall.ui.theme.JetBrainsMonoFamily
import app.contextall.ui.theme.LocalExpressivePalette

@Composable
fun ThisDeviceScreen(
    localDisplayName: String,
    localDeviceId: String,
    darkTheme: Boolean,
    dynamicColor: Boolean,
    linkServiceRunning: Boolean,
    permissionsGranted: Boolean,
    headerActions: @Composable () -> Unit,
    onSaveDisplayName: (String) -> Unit,
    onCopyDeviceId: () -> Unit,
    onToggleDynamicColor: (Boolean) -> Unit,
    onToggleDarkTheme: (Boolean) -> Unit,
    onToggleLinkService: () -> Unit,
    onRequestPermissions: () -> Unit,
    onOpenUserGuide: () -> Unit,
    onOpenScenarioLab: () -> Unit,
    onResetIdentity: () -> Unit,
    modifier: Modifier = Modifier
) {
    var showEditDialog by remember { mutableStateOf(false) }
    var showResetDialog by remember { mutableStateOf(false) }
    var draftName by remember(localDisplayName) { mutableStateOf(localDisplayName) }
    val palette = LocalExpressivePalette.current

    Box(modifier = modifier.fillMaxSize(), contentAlignment = Alignment.TopCenter) {
        LazyColumn(
            modifier = Modifier
                .fillMaxWidth()
                .widthIn(max = 640.dp)
                .testTag("this_device_screen"),
            contentPadding = PaddingValues(start = 16.dp, end = 16.dp, top = 8.dp, bottom = 116.dp),
            verticalArrangement = Arrangement.spacedBy(20.dp)
        ) {
            item {
                ExpressiveScreenHeader(
                    title = stringResource(R.string.header_local_title),
                    subtitle = stringResource(R.string.header_local_subtitle),
                    trailingActions = headerActions
                )
            }

            // Segmented 2-tile Identity Group
            item {
                SectionHeader(text = stringResource(R.string.local_identity_section))
                Spacer(modifier = Modifier.height(10.dp))
                Column(verticalArrangement = Arrangement.spacedBy(2.dp)) {
                    ExpressiveGroupTile(
                        index = 0,
                        count = 2,
                        onClick = {
                            draftName = localDisplayName
                            showEditDialog = true
                        }
                    ) {
                        Row(
                            modifier = Modifier
                                .fillMaxWidth()
                                .padding(horizontal = 18.dp, vertical = 16.dp),
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            ExpressiveIconBadge(
                                icon = Icons.Rounded.Badge,
                                backgroundColor = palette.skyIconBg,
                                iconTint = palette.skyIconFg
                            )
                            Spacer(modifier = Modifier.width(16.dp))
                            Column(modifier = Modifier.weight(1f)) {
                                Text(
                                    text = stringResource(R.string.local_display_name_label),
                                    style = MaterialTheme.typography.bodySmall,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant
                                )
                                Spacer(modifier = Modifier.height(2.dp))
                                Text(
                                    text = localDisplayName,
                                    style = MaterialTheme.typography.titleMedium
                                )
                            }
                            FilledTonalIconButton(
                                onClick = {
                                    draftName = localDisplayName
                                    showEditDialog = true
                                },
                                modifier = Modifier
                                    .minimumInteractiveComponentSize()
                                    .testTag("edit_display_name_button")
                            ) {
                                Icon(
                                    imageVector = Icons.Rounded.Edit,
                                    contentDescription = stringResource(R.string.local_edit_name_desc),
                                    modifier = Modifier.size(18.dp)
                                )
                            }
                        }
                    }

                    ExpressiveGroupTile(
                        index = 1,
                        count = 2,
                        onClick = onCopyDeviceId
                    ) {
                        Row(
                            modifier = Modifier
                                .fillMaxWidth()
                                .padding(horizontal = 18.dp, vertical = 16.dp),
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            ExpressiveIconBadge(
                                icon = Icons.Rounded.Fingerprint,
                                backgroundColor = palette.mintIconBg,
                                iconTint = palette.mintIconFg
                            )
                            Spacer(modifier = Modifier.width(16.dp))
                            Column(modifier = Modifier.weight(1f)) {
                                Text(
                                    text = stringResource(R.string.local_device_id_label),
                                    style = MaterialTheme.typography.bodySmall,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant
                                )
                                Spacer(modifier = Modifier.height(4.dp))
                                Text(
                                    text = localDeviceId,
                                    fontFamily = JetBrainsMonoFamily,
                                    fontSize = 12.sp,
                                    color = MaterialTheme.colorScheme.onSurface
                                )
                            }
                            FilledTonalIconButton(
                                onClick = onCopyDeviceId,
                                modifier = Modifier
                                    .minimumInteractiveComponentSize()
                                    .testTag("copy_device_id_button")
                            ) {
                                Icon(
                                    imageVector = Icons.Rounded.ContentCopy,
                                    contentDescription = stringResource(R.string.local_copy_id_desc),
                                    modifier = Modifier.size(18.dp)
                                )
                            }
                        }
                    }
                }
            }

            // Segmented 2-tile Android 16 Appearance Group
            item {
                SectionHeader(text = stringResource(R.string.local_appearance_section))
                Spacer(modifier = Modifier.height(10.dp))
                Column(verticalArrangement = Arrangement.spacedBy(2.dp)) {
                    ExpressiveGroupTile(
                        index = 0,
                        count = 2,
                        onClick = { onToggleDynamicColor(!dynamicColor) }
                    ) {
                        LocalSwitchRow(
                            icon = Icons.Rounded.Palette,
                            badgeBg = palette.violetIconBg,
                            badgeFg = palette.violetIconFg,
                            title = stringResource(R.string.local_dynamic_color_title),
                            subtitle = stringResource(R.string.local_dynamic_color_subtitle),
                            checked = dynamicColor,
                            onCheckedChange = onToggleDynamicColor,
                            testTag = "switch_dynamic_color"
                        )
                    }
                    ExpressiveGroupTile(
                        index = 1,
                        count = 2,
                        onClick = { onToggleDarkTheme(!darkTheme) }
                    ) {
                        LocalSwitchRow(
                            icon = Icons.Rounded.DarkMode,
                            badgeBg = palette.skyIconBg,
                            badgeFg = palette.skyIconFg,
                            title = stringResource(R.string.local_dark_mode_title),
                            subtitle = stringResource(R.string.local_dark_mode_subtitle),
                            checked = darkTheme,
                            onCheckedChange = onToggleDarkTheme,
                            testTag = "switch_dark_theme"
                        )
                    }
                }
            }

            // Segmented 3-tile Runtime, Permissions & Storage Group
            item {
                SectionHeader(text = stringResource(R.string.local_runtime_section))
                Spacer(modifier = Modifier.height(10.dp))
                Column(verticalArrangement = Arrangement.spacedBy(2.dp)) {
                    ExpressiveGroupTile(
                        index = 0,
                        count = 3,
                        onClick = onToggleLinkService
                    ) {
                        LocalSwitchRow(
                            icon = Icons.Rounded.Memory,
                            badgeBg = palette.mintIconBg,
                            badgeFg = palette.mintIconFg,
                            title = stringResource(R.string.local_service_label),
                            subtitle = stringResource(
                                if (linkServiceRunning) R.string.local_service_running_sub
                                else R.string.local_service_stopped_sub
                            ),
                            checked = linkServiceRunning,
                            onCheckedChange = { onToggleLinkService() },
                            testTag = "switch_link_service"
                        )
                    }

                    ExpressiveGroupTile(
                        index = 1,
                        count = 3,
                        onClick = onRequestPermissions,
                        modifier = Modifier.testTag("request_permissions_tile")
                    ) {
                        Row(
                            modifier = Modifier.padding(horizontal = 18.dp, vertical = 16.dp),
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            ExpressiveIconBadge(
                                icon = Icons.Rounded.NotificationsActive,
                                backgroundColor = palette.amberIconBg,
                                iconTint = palette.amberIconFg
                            )
                            Spacer(modifier = Modifier.width(16.dp))
                            Column(modifier = Modifier.weight(1f)) {
                                Text(
                                    text = stringResource(R.string.local_permissions_label),
                                    style = MaterialTheme.typography.titleSmall
                                )
                                Spacer(modifier = Modifier.height(2.dp))
                                Text(
                                    text = stringResource(
                                        if (permissionsGranted) R.string.local_permissions_granted_sub
                                        else R.string.local_permissions_request_sub
                                    ),
                                    style = MaterialTheme.typography.bodySmall,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant
                                )
                            }
                        }
                    }

                    ExpressiveGroupTile(index = 2, count = 3) {
                        Row(
                            modifier = Modifier.padding(horizontal = 18.dp, vertical = 16.dp),
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            ExpressiveIconBadge(
                                icon = Icons.Rounded.FolderZip,
                                backgroundColor = palette.violetIconBg,
                                iconTint = palette.violetIconFg
                            )
                            Spacer(modifier = Modifier.width(16.dp))
                            Column {
                                Text(
                                    text = stringResource(R.string.local_storage_label),
                                    style = MaterialTheme.typography.titleSmall
                                )
                                Spacer(modifier = Modifier.height(2.dp))
                                Text(
                                    text = stringResource(R.string.local_storage_value),
                                    style = MaterialTheme.typography.bodySmall,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant
                                )
                            }
                        }
                    }
                }
            }

            // Help & Onboarding Guide + Scenario Lab Group
            item {
                SectionHeader(text = stringResource(R.string.local_help_section))
                Spacer(modifier = Modifier.height(10.dp))
                Column(verticalArrangement = Arrangement.spacedBy(2.dp)) {
                    ExpressiveGroupTile(
                        index = 0,
                        count = 2,
                        onClick = onOpenUserGuide,
                        modifier = Modifier.testTag("open_user_guide_tile")
                    ) {
                        Row(
                            modifier = Modifier
                                .fillMaxWidth()
                                .padding(horizontal = 18.dp, vertical = 16.dp),
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            ExpressiveIconBadge(
                                icon = Icons.AutoMirrored.Rounded.HelpOutline,
                                backgroundColor = palette.skyIconBg,
                                iconTint = palette.skyIconFg
                            )
                            Spacer(modifier = Modifier.width(16.dp))
                            Column(modifier = Modifier.weight(1f)) {
                                Text(
                                    text = stringResource(R.string.local_reopen_guide_title),
                                    style = MaterialTheme.typography.titleSmall
                                )
                                Spacer(modifier = Modifier.height(2.dp))
                                Text(
                                    text = stringResource(R.string.local_reopen_guide_subtitle),
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

                    ExpressiveGroupTile(
                        index = 1,
                        count = 2,
                        onClick = onOpenScenarioLab,
                        modifier = Modifier.testTag("open_scenario_lab_tile")
                    ) {
                        Row(
                            modifier = Modifier
                                .fillMaxWidth()
                                .padding(horizontal = 18.dp, vertical = 16.dp),
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            ExpressiveIconBadge(
                                icon = Icons.Rounded.Science,
                                backgroundColor = palette.violetIconBg,
                                iconTint = palette.violetIconFg
                            )
                            Spacer(modifier = Modifier.width(16.dp))
                            Column(modifier = Modifier.weight(1f)) {
                                Text(
                                    text = stringResource(R.string.local_open_lab_title),
                                    style = MaterialTheme.typography.titleSmall
                                )
                                Spacer(modifier = Modifier.height(2.dp))
                                Text(
                                    text = stringResource(R.string.local_open_lab_subtitle),
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

            // Security Identity Reset Tile
            item {
                SectionHeader(text = stringResource(R.string.local_security_section))
                Spacer(modifier = Modifier.height(10.dp))
                ExpressiveGroupTile(
                    index = 0,
                    count = 1,
                    onClick = { showResetDialog = true },
                    modifier = Modifier.testTag("reset_identity_card")
                ) {
                    Row(
                        modifier = Modifier.padding(horizontal = 18.dp, vertical = 16.dp),
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        ExpressiveIconBadge(
                            icon = Icons.Rounded.KeyOff,
                            backgroundColor = MaterialTheme.colorScheme.errorContainer,
                            iconTint = MaterialTheme.colorScheme.onErrorContainer
                        )
                        Spacer(modifier = Modifier.width(16.dp))
                        Column(modifier = Modifier.weight(1f)) {
                            Text(
                                text = stringResource(R.string.local_reset_identity_title),
                                style = MaterialTheme.typography.titleSmall,
                                color = MaterialTheme.colorScheme.error
                            )
                            Spacer(modifier = Modifier.height(2.dp))
                            Text(
                                text = stringResource(R.string.local_reset_identity_subtitle),
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant
                            )
                        }
                    }
                }
            }
        }
    }

    if (showEditDialog) {
        val isValid = DisplayNames.isLegal(draftName)
        val cpCount = DisplayNames.codePointCount(draftName)
        AlertDialog(
            onDismissRequest = { showEditDialog = false },
            shape = RoundedCornerShape(28.dp),
            title = { Text(text = stringResource(R.string.edit_name_dialog_title)) },
            text = {
                OutlinedTextField(
                    value = draftName,
                    onValueChange = { draftName = it },
                    singleLine = true,
                    isError = !isValid,
                    supportingText = {
                        Text(
                            text = if (isValid) {
                                stringResource(R.string.edit_name_dialog_supporting, cpCount)
                            } else {
                                stringResource(R.string.edit_name_dialog_error)
                            }
                        )
                    },
                    shape = RoundedCornerShape(16.dp),
                    modifier = Modifier
                        .fillMaxWidth()
                        .testTag("display_name_input")
                )
            },
            confirmButton = {
                TextButton(
                    onClick = {
                        onSaveDisplayName(draftName)
                        showEditDialog = false
                    },
                    enabled = isValid,
                    modifier = Modifier.testTag("save_display_name_button")
                ) {
                    Text(text = stringResource(R.string.edit_name_save_button))
                }
            },
            dismissButton = {
                TextButton(onClick = { showEditDialog = false }) {
                    Text(text = stringResource(R.string.common_cancel_button))
                }
            }
        )
    }

    if (showResetDialog) {
        AlertDialog(
            onDismissRequest = { showResetDialog = false },
            shape = RoundedCornerShape(28.dp),
            title = { Text(text = stringResource(R.string.reset_dialog_title)) },
            text = { Text(text = stringResource(R.string.reset_dialog_body)) },
            confirmButton = {
                TextButton(
                    onClick = {
                        showResetDialog = false
                        onResetIdentity()
                    },
                    modifier = Modifier.testTag("confirm_reset_identity_button")
                ) {
                    Text(
                        text = stringResource(R.string.reset_confirm_button),
                        color = MaterialTheme.colorScheme.error
                    )
                }
            },
            dismissButton = {
                TextButton(onClick = { showResetDialog = false }) {
                    Text(text = stringResource(R.string.common_cancel_button))
                }
            }
        )
    }
}

@Composable
private fun LocalSwitchRow(
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

/**
 * Non-intrusive Android 16 M3 Expressive Scenario Lab Sheet for interactive protocol exercises.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ScenarioLabBottomSheet(
    onDismiss: () -> Unit,
    onDiscoverPeer: () -> Unit,
    onInboundPairing: () -> Unit,
    onInboundFile: () -> Unit,
    onInboundBlockedFile: () -> Unit,
    onRemoteClipboard: () -> Unit,
    onFlapConnection: () -> Unit
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
                .testTag("scenario_lab_sheet")
        ) {
            Text(
                text = stringResource(R.string.lab_sheet_title),
                style = MaterialTheme.typography.headlineMedium,
                modifier = Modifier.padding(horizontal = 6.dp)
            )
            Spacer(modifier = Modifier.height(4.dp))
            Text(
                text = stringResource(R.string.lab_sheet_subtitle),
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(horizontal = 6.dp)
            )
            Spacer(modifier = Modifier.height(16.dp))

            Column(verticalArrangement = Arrangement.spacedBy(2.dp)) {
                LabActionTile(
                    index = 0,
                    count = 6,
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
                LabActionTile(
                    index = 1,
                    count = 6,
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
                LabActionTile(
                    index = 2,
                    count = 6,
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
                LabActionTile(
                    index = 3,
                    count = 6,
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
                LabActionTile(
                    index = 4,
                    count = 6,
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
                LabActionTile(
                    index = 5,
                    count = 6,
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
            }
        }
    }
}

@Composable
private fun LabActionTile(
    index: Int,
    count: Int,
    icon: ImageVector,
    badgeBg: Color,
    badgeFg: Color,
    title: String,
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
            modifier = Modifier.padding(horizontal = 16.dp, vertical = 13.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            ExpressiveIconBadge(icon = icon, backgroundColor = badgeBg, iconTint = badgeFg, size = 40.dp)
            Spacer(modifier = Modifier.width(14.dp))
            Column(modifier = Modifier.weight(1f)) {
                Text(text = title, style = MaterialTheme.typography.titleSmall)
                Text(
                    text = subtitle,
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
            }
        }
    }
}
