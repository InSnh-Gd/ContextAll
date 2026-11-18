package app.contextall.ui.screens

import android.os.Build
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
import androidx.compose.material.icons.rounded.BugReport
import androidx.compose.material.icons.rounded.ContentCopy
import androidx.compose.material.icons.rounded.DarkMode
import androidx.compose.material.icons.rounded.Edit
import androidx.compose.material.icons.rounded.Fingerprint
import androidx.compose.material.icons.rounded.FolderZip
import androidx.compose.material.icons.rounded.Info
import androidx.compose.material.icons.rounded.KeyOff
import androidx.compose.material.icons.rounded.Memory
import androidx.compose.material.icons.rounded.NotificationsActive
import androidx.compose.material.icons.rounded.Palette
import androidx.compose.material.icons.rounded.Science
import androidx.compose.material.icons.rounded.Terminal
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.FilledTonalIconButton
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
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
import app.contextall.BuildConfig
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
    autoCaptureEnabled: Boolean,
    autoCaptureCapturing: Boolean,
    debugMode: Boolean = false,
    showDeveloperSection: Boolean = BuildConfig.DEBUG,
    debugFrameCount: Int = 0,
    headerActions: @Composable () -> Unit,
    onSaveDisplayName: (String) -> Unit,
    onCopyDeviceId: () -> Unit,
    onToggleDynamicColor: (Boolean) -> Unit,
    onToggleDarkTheme: (Boolean) -> Unit,
    onToggleLinkService: () -> Unit,
    onRequestPermissions: () -> Unit,
    onToggleAutoCapture: (Boolean) -> Unit,
    onOpenUserGuide: () -> Unit,
    onToggleDebugMode: (Boolean) -> Unit = {},
    onOpenScenarioLab: () -> Unit,
    onResetIdentity: () -> Unit,
    onOpenAboutPage: () -> Unit = {},
    modifier: Modifier = Modifier
) {
    var showEditDialog by remember { mutableStateOf(false) }
    var showResetDialog by remember { mutableStateOf(false) }
    var draftName by remember(localDisplayName) { mutableStateOf(localDisplayName) }
    val palette = LocalExpressivePalette.current
    val machineBrief = remember {
        val brand = Build.MANUFACTURER?.trim().orEmpty().replaceFirstChar {
            if (it.isLowerCase()) it.titlecase() else it.toString()
        }
        val model = Build.MODEL?.trim().orEmpty()
        listOf(brand, model).filter { it.isNotEmpty() }.distinct().joinToString(" ").ifEmpty { "Android" }
    }

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
                        count = 4,
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
                        count = 4,
                        onClick = { onToggleAutoCapture(!autoCaptureEnabled) },
                        modifier = Modifier.testTag("auto_capture_tile")
                    ) {
                        LocalSwitchRow(
                            icon = Icons.Rounded.ContentCopy,
                            badgeBg = palette.skyIconBg,
                            badgeFg = palette.skyIconFg,
                            title = stringResource(R.string.auto_capture_label),
                            subtitle = stringResource(
                                when {
                                    !autoCaptureEnabled -> R.string.auto_capture_sub_off
                                    autoCaptureCapturing -> R.string.auto_capture_sub_capturing
                                    else -> R.string.auto_capture_sub_inactive
                                }
                            ),
                            checked = autoCaptureEnabled,
                            onCheckedChange = { onToggleAutoCapture(it) },
                            testTag = "switch_auto_capture"
                        )
                    }

                    ExpressiveGroupTile(
                        index = 2,
                        count = 4,
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

                    ExpressiveGroupTile(index = 3, count = 4) {
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

            // Help & Onboarding Guide (Clean Consumer Section)
            item {
                SectionHeader(text = stringResource(R.string.local_help_section))
                Spacer(modifier = Modifier.height(10.dp))
                ExpressiveGroupTile(
                    index = 0,
                    count = 1,
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
            }

            // Developer Options Section (Debug Build / Unlocked Developer Mode)
            if (showDeveloperSection || debugMode) {
                val devTileCount = if (debugMode) 3 else 2
                item {
                    SectionHeader(text = stringResource(R.string.local_dev_section))
                    Spacer(modifier = Modifier.height(10.dp))
                    Column(verticalArrangement = Arrangement.spacedBy(2.dp)) {
                        ExpressiveGroupTile(
                            index = 0,
                            count = devTileCount,
                            onClick = { onToggleDebugMode(!debugMode) }
                        ) {
                            LocalSwitchRow(
                                icon = Icons.Rounded.BugReport,
                                badgeBg = palette.amberIconBg,
                                badgeFg = palette.amberIconFg,
                                title = stringResource(R.string.local_dev_mode_switch_title),
                                subtitle = stringResource(
                                    if (debugMode) R.string.local_dev_mode_switch_on_sub
                                    else R.string.local_dev_mode_switch_off_sub
                                ),
                                checked = debugMode,
                                onCheckedChange = onToggleDebugMode,
                                testTag = "switch_debug_mode"
                            )
                        }

                        ExpressiveGroupTile(
                            index = 1,
                            count = devTileCount,
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
                                        text = stringResource(R.string.local_dev_open_console_title),
                                        style = MaterialTheme.typography.titleSmall
                                    )
                                    Spacer(modifier = Modifier.height(2.dp))
                                    Text(
                                        text = stringResource(
                                            R.string.local_dev_open_console_sub,
                                            debugFrameCount
                                        ),
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

                        if (debugMode) {
                            ExpressiveGroupTile(index = 2, count = devTileCount) {
                                Row(
                                    modifier = Modifier
                                        .fillMaxWidth()
                                        .padding(horizontal = 18.dp, vertical = 16.dp),
                                    verticalAlignment = Alignment.CenterVertically
                                ) {
                                    ExpressiveIconBadge(
                                        icon = Icons.Rounded.Terminal,
                                        backgroundColor = palette.mintIconBg,
                                        iconTint = palette.mintIconFg
                                    )
                                    Spacer(modifier = Modifier.width(16.dp))
                                    Column(modifier = Modifier.weight(1f)) {
                                        Text(
                                            text = stringResource(R.string.local_dev_runtime_diag_label),
                                            style = MaterialTheme.typography.titleSmall
                                        )
                                        Spacer(modifier = Modifier.height(3.dp))
                                        Text(
                                            text = stringResource(R.string.local_dev_runtime_diag_value),
                                            fontFamily = JetBrainsMonoFamily,
                                            fontSize = 11.5.sp,
                                            color = MaterialTheme.colorScheme.onSurfaceVariant
                                        )
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // About Sub-page Entry Tile
            item {
                SectionHeader(text = stringResource(R.string.local_about_section))
                Spacer(modifier = Modifier.height(10.dp))
                ExpressiveGroupTile(
                    index = 0,
                    count = 1,
                    onClick = onOpenAboutPage,
                    modifier = Modifier.testTag("open_about_subpage_tile")
                ) {
                    Row(
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(horizontal = 18.dp, vertical = 16.dp),
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        ExpressiveIconBadge(
                            icon = Icons.Rounded.Info,
                            backgroundColor = palette.mintIconBg,
                            iconTint = palette.mintIconFg
                        )
                        Spacer(modifier = Modifier.width(16.dp))
                        Column(modifier = Modifier.weight(1f)) {
                            Text(
                                text = stringResource(R.string.local_about_entry_title),
                                style = MaterialTheme.typography.titleSmall
                            )
                            Spacer(modifier = Modifier.height(2.dp))
                            Text(
                                text = stringResource(
                                    R.string.local_about_entry_subtitle,
                                    BuildConfig.VERSION_NAME,
                                    machineBrief
                                ),
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
