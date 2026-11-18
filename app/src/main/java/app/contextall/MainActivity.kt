package app.contextall

import android.Manifest
import android.content.pm.PackageManager
import android.os.Build
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.result.PickVisualMediaRequest
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.statusBars
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.slideInHorizontally
import androidx.compose.animation.slideOutHorizontally
import androidx.compose.animation.togetherWith
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.rounded.HelpOutline
import androidx.compose.material.icons.outlined.Devices
import androidx.compose.material.icons.outlined.Hub
import androidx.compose.material.icons.outlined.Smartphone
import androidx.compose.material.icons.outlined.SwapVert
import androidx.compose.material.icons.rounded.BugReport
import androidx.compose.material.icons.rounded.DarkMode
import androidx.compose.material.icons.rounded.Devices
import androidx.compose.material.icons.rounded.Hub
import androidx.compose.material.icons.rounded.LightMode
import androidx.compose.material.icons.rounded.Smartphone
import androidx.compose.material.icons.rounded.SwapVert
import androidx.compose.material3.Badge
import androidx.compose.material3.BadgedBox
import androidx.compose.material3.FilledTonalIconButton
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.NavigationRail
import androidx.compose.material3.NavigationRailItem
import androidx.compose.material3.Scaffold
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Text
import androidx.compose.material3.VerticalDivider
import androidx.compose.material3.minimumInteractiveComponentSize
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.derivedStateOf
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.input.nestedscroll.NestedScrollConnection
import androidx.compose.ui.input.nestedscroll.NestedScrollSource
import androidx.compose.ui.input.nestedscroll.nestedScroll
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import androidx.core.content.ContextCompat
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewmodel.compose.viewModel
import app.contextall.model.TopTab
import app.contextall.model.UiEventNotice
import app.contextall.ui.components.ContextAllCollapsibleBrandBar
import app.contextall.ui.components.ExpressiveFloatingNavDock
import app.contextall.ui.components.FloatingDockItem
import app.contextall.ui.screens.AboutDeviceSubScreen
import app.contextall.ui.screens.ConnectionScreen
import app.contextall.ui.screens.DeveloperDebugConsoleSheet
import app.contextall.ui.screens.DevicesListScreen
import app.contextall.ui.screens.EmptyDetailPlaceholder
import app.contextall.ui.screens.ManualConnectBottomSheet
import app.contextall.ui.screens.OnboardingGuideOverlay
import app.contextall.ui.screens.PairingVerificationBottomSheet
import app.contextall.ui.screens.PeerDetailScreen
import app.contextall.ui.screens.ThisDeviceScreen
import app.contextall.ui.screens.TransfersScreen
import app.contextall.ui.theme.ContextAllTheme
import app.contextall.viewmodel.ContextAllViewModel
import kotlinx.coroutines.launch

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        setContent {
            val vm: ContextAllViewModel = viewModel()
            val state by vm.state.collectAsStateWithLifecycle()

            ContextAllTheme(
                darkTheme = state.darkTheme,
                dynamicColor = state.dynamicColor
            ) {
                ContextAllAppShell(viewModel = vm)
            }
        }
    }
}

@Composable
fun ContextAllAppShell(
    viewModel: ContextAllViewModel
) {
    val state by viewModel.state.collectAsStateWithLifecycle()
    val showOnboardingGuide by viewModel.showOnboardingGuide.collectAsStateWithLifecycle()
    val snackbarHostState = remember { SnackbarHostState() }
    val scope = rememberCoroutineScope()
    val context = LocalContext.current
    var showScenarioLab by remember { mutableStateOf(false) }
    var showManualConnectSheet by remember { mutableStateOf(false) }
    var showAboutSubPage by remember { mutableStateOf(false) }
    var pendingFileTargetPeerId by remember { mutableStateOf<String?>(null) }

    LaunchedEffect(state.currentTab) {
        if (state.currentTab != TopTab.THIS_DEVICE) {
            showAboutSubPage = false
        }
    }

    // Hoisted scroll state & direction tracker for collapsible Brand Top Bar
    val devicesListState = rememberLazyListState()
    var isDevicesScrolledDown by remember { mutableStateOf(false) }

    val brandScrollConnection = remember(devicesListState) {
        object : NestedScrollConnection {
            override fun onPostScroll(
                consumed: Offset,
                available: Offset,
                source: NestedScrollSource
            ): Offset {
                if (consumed.y < -6f) {
                    isDevicesScrolledDown = true
                } else if (
                    consumed.y > 6f &&
                    devicesListState.firstVisibleItemIndex == 0 &&
                    devicesListState.firstVisibleItemScrollOffset <= 24
                ) {
                    isDevicesScrolledDown = false
                }
                return Offset.Zero
            }
        }
    }

    val isAtDevicesTop by remember {
        derivedStateOf {
            devicesListState.firstVisibleItemIndex == 0 &&
                devicesListState.firstVisibleItemScrollOffset <= 6
        }
    }

    LaunchedEffect(isAtDevicesTop) {
        if (isAtDevicesTop) {
            isDevicesScrolledDown = false
        }
    }

    // Zero-permission SAF system single document picker (Peer Detail)
    val documentPickerLauncher = rememberLauncherForActivityResult(
        contract = ActivityResultContracts.OpenDocument()
    ) { uri ->
        val targetId = pendingFileTargetPeerId
        if (uri != null && targetId != null) {
            viewModel.sendPickedDocument(deviceId = targetId, uri = uri)
        }
        pendingFileTargetPeerId = null
    }

    // Zero-permission SAF multi-document upload picker (Transfers Upload Hub)
    var pendingMultiUploadPeerId by remember { mutableStateOf<String?>(null) }
    val multiDocumentPickerLauncher = rememberLauncherForActivityResult(
        contract = ActivityResultContracts.OpenMultipleDocuments()
    ) { uris ->
        val targetId = pendingMultiUploadPeerId
        if (uris.isNotEmpty() && targetId != null) {
            viewModel.uploadPickedUris(deviceId = targetId, uris = uris)
        }
        pendingMultiUploadPeerId = null
    }

    // Zero-permission Android Photo Picker for images & videos (Transfers Upload Hub)
    val mediaPickerLauncher = rememberLauncherForActivityResult(
        contract = ActivityResultContracts.PickMultipleVisualMedia()
    ) { uris ->
        val targetId = pendingMultiUploadPeerId
        if (uris.isNotEmpty() && targetId != null) {
            viewModel.uploadPickedUris(deviceId = targetId, uris = uris)
        }
        pendingMultiUploadPeerId = null
    }

    // Runtime dangerous permissions (POST_NOTIFICATIONS & NEARBY_WIFI_DEVICES on Android 13+)
    val requiredRuntimePermissions = remember {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            arrayOf(
                Manifest.permission.POST_NOTIFICATIONS,
                Manifest.permission.NEARBY_WIFI_DEVICES
            )
        } else {
            emptyArray()
        }
    }

    val permissionsLauncher = rememberLauncherForActivityResult(
        contract = ActivityResultContracts.RequestMultiplePermissions()
    ) { grants ->
        val allGranted = grants.values.all { it }
        viewModel.onPermissionsResult(allGranted)
    }

    LaunchedEffect(Unit) {
        if (requiredRuntimePermissions.isEmpty()) {
            viewModel.onPermissionsResult(true)
        } else {
            val alreadyGranted = requiredRuntimePermissions.all { perm ->
                ContextCompat.checkSelfPermission(context, perm) == PackageManager.PERMISSION_GRANTED
            }
            viewModel.onPermissionsResult(alreadyGranted)
        }
    }

    LaunchedEffect(viewModel) {
        viewModel.notices.collect { notice ->
            val msg = when (notice) {
                is UiEventNotice.Paired ->
                    context.getString(R.string.notice_paired_success, notice.peerName)
                is UiEventNotice.PairingTimeout ->
                    context.getString(R.string.notice_pairing_timeout, notice.peerName)
                UiEventNotice.PairingRejected ->
                    context.getString(R.string.notice_pairing_rejected)
                UiEventNotice.PairingCancelled ->
                    context.getString(R.string.notice_pairing_cancelled)
                is UiEventNotice.Unpaired ->
                    context.getString(R.string.notice_unpaired, notice.peerName)
                is UiEventNotice.SendBusy ->
                    context.getString(R.string.notice_send_busy, notice.peerName)
                UiEventNotice.SendTooLarge ->
                    context.getString(R.string.notice_send_too_large)
                is UiEventNotice.SendStarted ->
                    context.getString(
                        R.string.notice_send_started,
                        notice.peerName,
                        notice.fileName,
                        notice.segments
                    )
                UiEventNotice.SendHeldOffline ->
                    context.getString(R.string.notice_send_held)
                is UiEventNotice.FilesQueued ->
                    context.getString(R.string.notice_files_queued, notice.count, notice.peerName)
                is UiEventNotice.TransferPaused ->
                    context.getString(R.string.notice_transfer_paused, notice.fileName)
                is UiEventNotice.TransferResumed ->
                    context.getString(R.string.notice_transfer_resumed, notice.fileName)
                is UiEventNotice.InboundAutoAccepted ->
                    context.getString(
                        R.string.notice_inbound_auto_accepted,
                        notice.peerName,
                        notice.fileName
                    )
                is UiEventNotice.InboundTimeout ->
                    context.getString(R.string.notice_inbound_timeout, notice.peerName)
                is UiEventNotice.TransferCompleted ->
                    context.getString(R.string.notice_transfer_completed, notice.fileName)
                is UiEventNotice.ClipboardPushed ->
                    context.getString(
                        R.string.notice_clipboard_pushed,
                        notice.peerCount,
                        notice.byteCount
                    )
                UiEventNotice.ClipboardTooLarge ->
                    context.getString(R.string.notice_clipboard_too_large)
                UiEventNotice.ClipboardEmpty ->
                    context.getString(R.string.notice_clipboard_empty)
                UiEventNotice.ClipboardNoMutualPeers ->
                    context.getString(R.string.notice_clipboard_no_mutual)
                is UiEventNotice.ClipboardReceived ->
                    context.getString(R.string.notice_clipboard_received, notice.peerName)
                UiEventNotice.CopiedId ->
                    context.getString(R.string.notice_copied_id)
                UiEventNotice.IdentityResetDone ->
                    context.getString(R.string.notice_identity_reset)
                is UiEventNotice.DisplayNameSaved ->
                    context.getString(R.string.notice_display_name_saved, notice.newName)
                UiEventNotice.InvalidDisplayName ->
                    context.getString(R.string.notice_invalid_name)
                is UiEventNotice.ManualEndpointReconnected ->
                    context.getString(
                        R.string.notice_manual_reconnected,
                        notice.peerName,
                        notice.endpoint
                    )
                UiEventNotice.ManualConnectUnavailable ->
                    context.getString(R.string.notice_manual_unavailable)
                UiEventNotice.ManualInvalidEndpoint ->
                    context.getString(R.string.notice_manual_invalid)
                is UiEventNotice.DebugSeedSwitched ->
                    context.getString(
                        if (notice.isDeveloperProfile) R.string.notice_debug_seed_dev
                        else R.string.notice_debug_seed_consumer
                    )
                is UiEventNotice.CopiedDebugLogs ->
                    context.getString(R.string.notice_debug_logs_copied, notice.count)
                UiEventNotice.DebugLogsCleared ->
                    context.getString(R.string.notice_debug_logs_cleared)
                UiEventNotice.DebugModeUnlocked ->
                    context.getString(R.string.notice_debug_unlocked)
                UiEventNotice.DebugPreviewingRelease ->
                    context.getString(R.string.notice_debug_preview_release)
            }
            snackbarHostState.showSnackbar(msg)
        }
    }

    val activeTransferCount = remember(state.transfers, state.filePrompts, state.queuedUploads) {
        state.transfers.count { !it.completed } + state.filePrompts.size + state.queuedUploads.size
    }
    val trustedPeers = remember(state.devices) {
        state.devices.filter { it.trusted }
    }
    val selectedPeer = remember(state.devices, state.selectedDeviceId) {
        state.devices.firstOrNull { it.deviceId == state.selectedDeviceId && it.trusted }
    }

    val isDebugUi = state.debugState.developerUiEnabled

    val headerActionButtons: @Composable () -> Unit = {
        Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
            if (isDebugUi) {
                FilledTonalIconButton(
                    onClick = { showScenarioLab = true },
                    modifier = Modifier
                        .minimumInteractiveComponentSize()
                        .testTag("top_action_scenario_lab")
                ) {
                    Icon(
                        imageVector = Icons.Rounded.BugReport,
                        contentDescription = stringResource(R.string.action_open_debug_console)
                    )
                }
            }
            FilledTonalIconButton(
                onClick = { viewModel.openOnboardingGuide() },
                modifier = Modifier
                    .minimumInteractiveComponentSize()
                    .testTag("top_action_user_guide")
            ) {
                Icon(
                    imageVector = Icons.AutoMirrored.Rounded.HelpOutline,
                    contentDescription = stringResource(R.string.action_open_user_guide)
                )
            }
            FilledTonalIconButton(
                onClick = { viewModel.setDarkTheme(!state.darkTheme) },
                modifier = Modifier
                    .minimumInteractiveComponentSize()
                    .testTag("top_action_theme_toggle")
            ) {
                Icon(
                    imageVector = if (state.darkTheme) Icons.Rounded.LightMode else Icons.Rounded.DarkMode,
                    contentDescription = stringResource(R.string.action_toggle_appearance)
                )
            }
        }
    }

    val dockItems = listOf(
        FloatingDockItem(
            key = TopTab.DEVICES,
            label = stringResource(R.string.nav_devices),
            selectedIcon = Icons.Rounded.Devices,
            unselectedIcon = Icons.Outlined.Devices,
            testTag = "nav_item_devices"
        ),
        FloatingDockItem(
            key = TopTab.CONNECTION,
            label = stringResource(R.string.nav_connection),
            selectedIcon = Icons.Rounded.Hub,
            unselectedIcon = Icons.Outlined.Hub,
            testTag = "nav_item_connection"
        ),
        FloatingDockItem(
            key = TopTab.TRANSFERS,
            label = stringResource(R.string.nav_transfers),
            selectedIcon = Icons.Rounded.SwapVert,
            unselectedIcon = Icons.Outlined.SwapVert,
            badgeCount = activeTransferCount,
            testTag = "nav_item_transfers"
        ),
        FloatingDockItem(
            key = TopTab.THIS_DEVICE,
            label = stringResource(R.string.nav_this_device),
            selectedIcon = Icons.Rounded.Smartphone,
            unselectedIcon = Icons.Outlined.Smartphone,
            testTag = "nav_item_this_device"
        )
    )

    BoxWithConstraints(modifier = Modifier.fillMaxSize()) {
        // Matches ContextAll.App.ShellLayout.TwoColumnWidth == 840
        val isTwoColumn = maxWidth >= 840.dp
        val showBrandTopBar = state.currentTab == TopTab.DEVICES &&
            (isTwoColumn || selectedPeer == null) &&
            !isDevicesScrolledDown

        Scaffold(
            contentWindowInsets = WindowInsets(0, 0, 0, 0),
            containerColor = MaterialTheme.colorScheme.background,
            snackbarHost = {
                SnackbarHost(
                    hostState = snackbarHostState,
                    modifier = Modifier.padding(bottom = if (isTwoColumn) 16.dp else 92.dp)
                )
            }
        ) { innerPadding ->
            Box(
                modifier = Modifier
                    .fillMaxSize()
                    .padding(innerPadding)
                    .windowInsetsPadding(WindowInsets.statusBars)
            ) {
                Row(modifier = Modifier.fillMaxSize()) {
                    if (isTwoColumn) {
                        NavigationRail(
                            containerColor = MaterialTheme.colorScheme.background,
                            modifier = Modifier.testTag("side_nav_rail")
                        ) {
                            NavigationRailItem(
                                selected = state.currentTab == TopTab.DEVICES,
                                onClick = {
                                    if (state.currentTab == TopTab.DEVICES) {
                                        scope.launch { devicesListState.animateScrollToItem(0) }
                                    } else {
                                        viewModel.selectTab(TopTab.DEVICES)
                                    }
                                },
                                icon = {
                                    Icon(
                                        imageVector = Icons.Rounded.Devices,
                                        contentDescription = stringResource(R.string.nav_devices)
                                    )
                                },
                                label = { Text(stringResource(R.string.nav_devices)) }
                            )
                            NavigationRailItem(
                                selected = state.currentTab == TopTab.CONNECTION,
                                onClick = { viewModel.selectTab(TopTab.CONNECTION) },
                                icon = {
                                    Icon(
                                        imageVector = Icons.Rounded.Hub,
                                        contentDescription = stringResource(R.string.nav_connection)
                                    )
                                },
                                label = { Text(stringResource(R.string.nav_connection)) }
                            )
                            NavigationRailItem(
                                selected = state.currentTab == TopTab.TRANSFERS,
                                onClick = { viewModel.selectTab(TopTab.TRANSFERS) },
                                icon = {
                                    BadgedBox(
                                        badge = {
                                            if (activeTransferCount > 0) {
                                                Badge { Text(activeTransferCount.toString()) }
                                            }
                                        }
                                    ) {
                                        Icon(
                                            imageVector = Icons.Rounded.SwapVert,
                                            contentDescription = stringResource(R.string.nav_transfers)
                                        )
                                    }
                                },
                                label = { Text(stringResource(R.string.nav_transfers)) }
                            )
                            NavigationRailItem(
                                selected = state.currentTab == TopTab.THIS_DEVICE,
                                onClick = {
                                    if (state.currentTab == TopTab.THIS_DEVICE && showAboutSubPage) {
                                        showAboutSubPage = false
                                    } else {
                                        viewModel.selectTab(TopTab.THIS_DEVICE)
                                    }
                                },
                                icon = {
                                    Icon(
                                        imageVector = Icons.Rounded.Smartphone,
                                        contentDescription = stringResource(R.string.nav_this_device)
                                    )
                                },
                                label = { Text(stringResource(R.string.nav_this_device)) }
                            )
                        }
                    }

                    Column(
                        modifier = Modifier
                            .weight(1f)
                            .fillMaxHeight()
                    ) {
                        ContextAllCollapsibleBrandBar(
                            visible = showBrandTopBar,
                            centeredMaxWidth = !isTwoColumn,
                            showDebugBadge = isDebugUi,
                            onDebugBadgeClick = { showScenarioLab = true }
                        )

                        Box(
                            modifier = Modifier
                                .weight(1f)
                                .fillMaxWidth()
                        ) {
                            when (state.currentTab) {
                                TopTab.DEVICES -> {
                                    if (isTwoColumn) {
                                        Row(modifier = Modifier.fillMaxSize()) {
                                            DevicesListScreen(
                                                wifiLanReady = state.wifiLanReady,
                                                linkServiceRunning = state.linkServiceRunning,
                                                devices = state.devices,
                                                clipboardState = state.clipboardState,
                                                selectedDeviceId = selectedPeer?.deviceId,
                                                telemetry = state.telemetry,
                                                isScanningLan = state.isScanningLan,
                                                lastDiscoveredDeviceId = state.lastDiscoveredDeviceId,
                                                headerActions = headerActionButtons,
                                                onBannerClick = { viewModel.selectTab(TopTab.CONNECTION) },
                                                onTriggerScan = { viewModel.triggerLanScan() },
                                                onPushSystemClipboard = { viewModel.pushSystemClipboard() },
                                                onPushCustomClipboard = { viewModel.pushCustomClipboardText(it) },
                                                onSelectTrustedDevice = { viewModel.selectTrustedDevice(it.deviceId) },
                                                onStartPairing = { viewModel.startPairing(it.deviceId) },
                                                onOpenManualConnect = { showManualConnectSheet = true },
                                                listState = devicesListState,
                                                modifier = Modifier
                                                    .weight(0.46f)
                                                    .nestedScroll(brandScrollConnection)
                                            )
                                    VerticalDivider(
                                        modifier = Modifier.fillMaxHeight(),
                                        color = MaterialTheme.colorScheme.outlineVariant.copy(alpha = 0.35f)
                                    )
                                    if (selectedPeer != null) {
                                        val peerActive = state.transfers.filter {
                                            it.deviceId == selectedPeer.deviceId && !it.completed
                                        }
                                        PeerDetailScreen(
                                            peer = selectedPeer,
                                            peerActiveTransfers = peerActive,
                                            showBackButton = false,
                                            debugMode = isDebugUi,
                                            onBack = {},
                                            onLaunchSystemFilePicker = {
                                                pendingFileTargetPeerId = selectedPeer.deviceId
                                                documentPickerLauncher.launch(arrayOf("*/*"))
                                            },
                                            onLaunchMediaPicker = {
                                                pendingMultiUploadPeerId = selectedPeer.deviceId
                                                mediaPickerLauncher.launch(
                                                    PickVisualMediaRequest(ActivityResultContracts.PickVisualMedia.ImageAndVideo)
                                                )
                                            },
                                            onSendPresetPayload = { name, size ->
                                                viewModel.sendFilePayload(selectedPeer.deviceId, name, size)
                                            },
                                            onTogglePeerConnection = {
                                                viewModel.labTogglePeerConnection(selectedPeer.deviceId)
                                            },
                                            onCopyPeerId = {
                                                viewModel.copyTextToSystemClipboard(
                                                    "Peer ID",
                                                    selectedPeer.deviceId
                                                )
                                                scope.launch {
                                                    snackbarHostState.showSnackbar(
                                                        context.getString(R.string.notice_copied_id)
                                                    )
                                                }
                                            },
                                            onClipboardToggle = {
                                                viewModel.setClipboardSync(selectedPeer.deviceId, it)
                                            },
                                            onAskBeforeSaveToggle = {
                                                viewModel.setAskBeforeSave(selectedPeer.deviceId, it)
                                            },
                                            onUnpairConfirm = { viewModel.unpairDevice(selectedPeer.deviceId) },
                                            modifier = Modifier.weight(0.54f)
                                        )
                                    } else {
                                        EmptyDetailPlaceholder(modifier = Modifier.weight(0.54f))
                                    }
                                }
                            } else if (selectedPeer != null) {
                                val peerActive = state.transfers.filter {
                                    it.deviceId == selectedPeer.deviceId && !it.completed
                                }
                                PeerDetailScreen(
                                    peer = selectedPeer,
                                    peerActiveTransfers = peerActive,
                                    showBackButton = true,
                                    debugMode = isDebugUi,
                                    onBack = { viewModel.selectTrustedDevice(null) },
                                    onLaunchSystemFilePicker = {
                                        pendingFileTargetPeerId = selectedPeer.deviceId
                                        documentPickerLauncher.launch(arrayOf("*/*"))
                                    },
                                    onLaunchMediaPicker = {
                                        pendingMultiUploadPeerId = selectedPeer.deviceId
                                        mediaPickerLauncher.launch(
                                            PickVisualMediaRequest(ActivityResultContracts.PickVisualMedia.ImageAndVideo)
                                        )
                                    },
                                    onSendPresetPayload = { name, size ->
                                        viewModel.sendFilePayload(selectedPeer.deviceId, name, size)
                                    },
                                    onTogglePeerConnection = {
                                        viewModel.labTogglePeerConnection(selectedPeer.deviceId)
                                    },
                                    onCopyPeerId = {
                                        viewModel.copyTextToSystemClipboard("Peer ID", selectedPeer.deviceId)
                                        scope.launch {
                                            snackbarHostState.showSnackbar(
                                                context.getString(R.string.notice_copied_id)
                                            )
                                        }
                                    },
                                    onClipboardToggle = {
                                        viewModel.setClipboardSync(selectedPeer.deviceId, it)
                                    },
                                    onAskBeforeSaveToggle = {
                                        viewModel.setAskBeforeSave(selectedPeer.deviceId, it)
                                    },
                                    onUnpairConfirm = { viewModel.unpairDevice(selectedPeer.deviceId) }
                                )
                            } else {
                                    DevicesListScreen(
                                        wifiLanReady = state.wifiLanReady,
                                        linkServiceRunning = state.linkServiceRunning,
                                        devices = state.devices,
                                        clipboardState = state.clipboardState,
                                        selectedDeviceId = null,
                                        telemetry = state.telemetry,
                                        isScanningLan = state.isScanningLan,
                                        lastDiscoveredDeviceId = state.lastDiscoveredDeviceId,
                                        headerActions = headerActionButtons,
                                        onBannerClick = { viewModel.selectTab(TopTab.CONNECTION) },
                                        onTriggerScan = { viewModel.triggerLanScan() },
                                        onPushSystemClipboard = { viewModel.pushSystemClipboard() },
                                        onPushCustomClipboard = { viewModel.pushCustomClipboardText(it) },
                                        onSelectTrustedDevice = { viewModel.selectTrustedDevice(it.deviceId) },
                                        onStartPairing = { viewModel.startPairing(it.deviceId) },
                                        onOpenManualConnect = { showManualConnectSheet = true },
                                        listState = devicesListState,
                                        modifier = Modifier.nestedScroll(brandScrollConnection)
                                    )
                                }
                            }

                            TopTab.CONNECTION -> {
                                ConnectionScreen(
                                    wifiLanReady = state.wifiLanReady,
                                    linkServiceRunning = state.linkServiceRunning,
                                    devices = state.devices,
                                    telemetry = state.telemetry,
                                    headerActions = headerActionButtons,
                                    onToggleLanWifi = { viewModel.toggleLanWifiState() },
                                    onToggleLinkService = { viewModel.toggleLinkService() },
                                    onOpenManualConnect = { showManualConnectSheet = true },
                                    onTogglePeerConnection = { viewModel.labTogglePeerConnection(it) },
                                    onPairPeer = { viewModel.startPairing(it.deviceId) }
                                )
                            }

                            TopTab.TRANSFERS -> {
                                TransfersScreen(
                                    trustedPeers = trustedPeers,
                                    filePrompts = state.filePrompts,
                                    transfers = state.transfers,
                                    queuedUploads = state.queuedUploads,
                                    telemetry = state.telemetry,
                                    debugMode = isDebugUi,
                                    headerActions = headerActionButtons,
                                    onPickLocalFiles = { targetId ->
                                        pendingMultiUploadPeerId = targetId
                                        multiDocumentPickerLauncher.launch(arrayOf("*/*"))
                                    },
                                    onPickMediaFiles = { targetId ->
                                        pendingMultiUploadPeerId = targetId
                                        mediaPickerLauncher.launch(
                                            PickVisualMediaRequest(ActivityResultContracts.PickVisualMedia.ImageAndVideo)
                                        )
                                    },
                                    onSendTextFile = { targetId, fileName, content ->
                                        viewModel.uploadTextAsFile(targetId, fileName, content)
                                    },
                                    onSendPresetPayload = { targetId, fileName, bytes ->
                                        viewModel.uploadPresetFromTransfers(targetId, fileName, bytes)
                                    },
                                    onNavigateToDevices = { viewModel.selectTab(TopTab.DEVICES) },
                                    onAcceptIncoming = { viewModel.acceptIncomingFile(it) },
                                    onDeclineIncoming = { viewModel.rejectIncomingFile(it) },
                                    onTogglePauseTransfer = { viewModel.togglePauseTransfer(it) },
                                    onCancelTransfer = { viewModel.cancelTransfer(it) },
                                    onRetryBlockedCommit = { viewModel.retryBlockedCommit(it) },
                                    onRemoveQueuedUpload = { viewModel.removeQueuedUpload(it) },
                                    onClearQueuedUploads = { viewModel.clearQueuedUploads() },
                                    onResendCompleted = { viewModel.resendCompletedTransfer(it) },
                                    onClearCompleted = { viewModel.clearCompletedTransfers() }
                                )
                            }

                            TopTab.THIS_DEVICE -> {
                                AnimatedContent(
                                    targetState = showAboutSubPage,
                                    transitionSpec = {
                                        if (targetState) {
                                            (slideInHorizontally(
                                                initialOffsetX = { it / 4 },
                                                animationSpec = tween(240)
                                            ) + fadeIn(tween(220))).togetherWith(
                                                slideOutHorizontally(
                                                    targetOffsetX = { -it / 6 },
                                                    animationSpec = tween(200)
                                                ) + fadeOut(tween(160))
                                            )
                                        } else {
                                            (slideInHorizontally(
                                                initialOffsetX = { -it / 6 },
                                                animationSpec = tween(240)
                                            ) + fadeIn(tween(220))).togetherWith(
                                                slideOutHorizontally(
                                                    targetOffsetX = { it / 4 },
                                                    animationSpec = tween(200)
                                                ) + fadeOut(tween(160))
                                            )
                                        }
                                    },
                                    label = "thisDeviceAboutTransition"
                                ) { isAboutOpen ->
                                    if (isAboutOpen) {
                                        AboutDeviceSubScreen(
                                            localDeviceId = state.localDeviceId,
                                            debugMode = isDebugUi,
                                            headerActions = headerActionButtons,
                                            onBack = { showAboutSubPage = false },
                                            onCopyDiagnostics = { report ->
                                                viewModel.copyTextToSystemClipboard("ContextAll Diagnostics", report)
                                                scope.launch {
                                                    snackbarHostState.showSnackbar(
                                                        context.getString(R.string.notice_copied_diagnostics)
                                                    )
                                                }
                                            },
                                            onUnlockDebugMode = { viewModel.setDeveloperUiEnabled(true) }
                                        )
                                    } else {
                                        ThisDeviceScreen(
                                            localDisplayName = state.localDisplayName,
                                            localDeviceId = state.localDeviceId,
                                            darkTheme = state.darkTheme,
                                            dynamicColor = state.dynamicColor,
                                            linkServiceRunning = state.linkServiceRunning,
                                            permissionsGranted = state.runtimePermissionsGranted,
                                            autoCaptureEnabled = state.autoCaptureEnabled,
                                            autoCaptureCapturing = state.autoCaptureCapturing,
                                            debugMode = isDebugUi,
                                            showDeveloperSection = BuildConfig.DEBUG || isDebugUi,
                                            debugFrameCount = state.debugState.frameLogs.size,
                                            headerActions = headerActionButtons,
                                            onSaveDisplayName = { viewModel.updateLocalDisplayName(it) },
                                            onCopyDeviceId = {
                                                viewModel.copyTextToSystemClipboard("ContextAll ID", state.localDeviceId)
                                                scope.launch {
                                                    snackbarHostState.showSnackbar(
                                                        context.getString(R.string.notice_copied_id)
                                                    )
                                                }
                                            },
                                            onToggleDynamicColor = { viewModel.setDynamicColor(it) },
                                            onToggleDarkTheme = { viewModel.setDarkTheme(it) },
                                            onToggleLinkService = { viewModel.toggleLinkService() },
                                            onToggleAutoCapture = { viewModel.setAutoCaptureEnabled(it) },
                                            onRequestPermissions = {
                                                if (requiredRuntimePermissions.isNotEmpty()) {
                                                    permissionsLauncher.launch(requiredRuntimePermissions)
                                                } else {
                                                    viewModel.onPermissionsResult(true)
                                                }
                                            },
                                            onOpenUserGuide = { viewModel.openOnboardingGuide() },
                                            onToggleDebugMode = { viewModel.setDeveloperUiEnabled(it) },
                                            onOpenScenarioLab = { showScenarioLab = true },
                                            onResetIdentity = { viewModel.resetIdentity() },
                                            onOpenAboutPage = { showAboutSubPage = true }
                                        )
                                    }
                                }
                            }
                            }
                        }
                    }
                }

                if (!isTwoColumn) {
                    ExpressiveFloatingNavDock(
                        items = dockItems,
                        selectedKey = state.currentTab,
                        onSelect = { tab ->
                            if (tab == TopTab.DEVICES && state.currentTab == TopTab.DEVICES) {
                                scope.launch { devicesListState.animateScrollToItem(0) }
                            } else if (tab == TopTab.THIS_DEVICE && state.currentTab == TopTab.THIS_DEVICE && showAboutSubPage) {
                                showAboutSubPage = false
                            } else {
                                viewModel.selectTab(tab)
                            }
                        },
                        modifier = Modifier
                            .align(Alignment.BottomCenter)
                            .testTag("bottom_nav_bar")
                    )
                }
            }
        }
    }

    state.pairing?.let { prompt ->
        PairingVerificationBottomSheet(
            prompt = prompt,
            onReject = { viewModel.rejectPairing() },
            onAccept = { viewModel.acceptPairing() }
        )
    }

    if (showManualConnectSheet) {
        ManualConnectBottomSheet(
            onDismiss = { showManualConnectSheet = false },
            onConnectAndPair = { host, portText, alias ->
                val ok = viewModel.connectManualPeer(host = host, portText = portText, alias = alias)
                if (ok) {
                    showManualConnectSheet = false
                }
            }
        )
    }

    if (showScenarioLab) {
        DeveloperDebugConsoleSheet(
            debugState = state.debugState,
            trustedPeers = trustedPeers,
            activeTransfers = state.transfers,
            pendingPromptCount = state.filePrompts.size,
            onDismiss = { showScenarioLab = false },
            onPreviewReleaseUi = { viewModel.setDeveloperUiEnabled(false) },
            onSwitchSeedProfile = { viewModel.switchSeedProfile(it) },
            onDiscoverPeer = { viewModel.labDiscoverPeer() },
            onInboundPairing = { viewModel.labInboundPairing() },
            onInboundFile = { viewModel.labInboundFile(moveBlocked = false) },
            onInboundBlockedFile = { viewModel.labInboundFile(moveBlocked = true) },
            onRemoteClipboard = { viewModel.labRemoteClipboard() },
            onFlapConnection = { viewModel.labTogglePeerConnection() },
            onTelemetryBurst = { viewModel.labTelemetryBurst() },
            onInjectBoundaryPayload = { devId, name, bytes ->
                viewModel.sendFilePayload(devId, name, bytes)
            },
            onCopyFrameLogs = {
                val count = state.debugState.frameLogs.size
                viewModel.copyDebugFrameLogs()
                scope.launch {
                    snackbarHostState.showSnackbar(
                        context.getString(R.string.notice_debug_logs_copied, count)
                    )
                }
            },
            onClearFrameLogs = { viewModel.clearDebugFrameLogs() }
        )
    }

    OnboardingGuideOverlay(
        visible = showOnboardingGuide,
        onDismiss = { viewModel.dismissOnboardingGuide() },
        onDismissAndOpenLab = {
            viewModel.dismissOnboardingGuide()
            if (isDebugUi) {
                showScenarioLab = true
            }
        }
    )
}
