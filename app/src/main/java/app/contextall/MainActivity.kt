package app.contextall

import android.Manifest
import android.content.pm.PackageManager
import android.os.Build
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
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
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.rounded.HelpOutline
import androidx.compose.material.icons.outlined.Devices
import androidx.compose.material.icons.outlined.Smartphone
import androidx.compose.material.icons.outlined.SwapVert
import androidx.compose.material.icons.rounded.DarkMode
import androidx.compose.material.icons.rounded.Devices
import androidx.compose.material.icons.rounded.LightMode
import androidx.compose.material.icons.rounded.Science
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
import app.contextall.ui.screens.DevicesListScreen
import app.contextall.ui.screens.EmptyDetailPlaceholder
import app.contextall.ui.screens.ManualConnectBottomSheet
import app.contextall.ui.screens.OnboardingGuideOverlay
import app.contextall.ui.screens.PairingVerificationBottomSheet
import app.contextall.ui.screens.PeerDetailScreen
import app.contextall.ui.screens.ScenarioLabBottomSheet
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
    var pendingFileTargetPeerId by remember { mutableStateOf<String?>(null) }

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

    // Zero-permission SAF system document picker
    val documentPickerLauncher = rememberLauncherForActivityResult(
        contract = ActivityResultContracts.OpenDocument()
    ) { uri ->
        val targetId = pendingFileTargetPeerId
        if (uri != null && targetId != null) {
            viewModel.sendPickedDocument(deviceId = targetId, uri = uri)
        }
        pendingFileTargetPeerId = null
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
            }
            snackbarHostState.showSnackbar(msg)
        }
    }

    val activeTransferCount = remember(state.transfers, state.filePrompts) {
        state.transfers.count { !it.completed } + state.filePrompts.size
    }
    val selectedPeer = remember(state.devices, state.selectedDeviceId) {
        state.devices.firstOrNull { it.deviceId == state.selectedDeviceId && it.trusted }
    }

    val headerActionButtons: @Composable () -> Unit = {
        Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
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
            FilledTonalIconButton(
                onClick = { showScenarioLab = true },
                modifier = Modifier
                    .minimumInteractiveComponentSize()
                    .testTag("top_action_scenario_lab")
            ) {
                Icon(
                    imageVector = Icons.Rounded.Science,
                    contentDescription = stringResource(R.string.action_open_scenario_lab)
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
                                onClick = { viewModel.selectTab(TopTab.THIS_DEVICE) },
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
                            centeredMaxWidth = !isTwoColumn
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
                                                headerActions = headerActionButtons,
                                                onBannerClick = {
                                                    if (!state.linkServiceRunning) {
                                                        viewModel.toggleLinkService()
                                                    } else {
                                                        viewModel.toggleLanWifiState()
                                                    }
                                                },
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
                                            onBack = {},
                                            onLaunchSystemFilePicker = {
                                                pendingFileTargetPeerId = selectedPeer.deviceId
                                                documentPickerLauncher.launch(arrayOf("*/*"))
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
                                    onBack = { viewModel.selectTrustedDevice(null) },
                                    onLaunchSystemFilePicker = {
                                        pendingFileTargetPeerId = selectedPeer.deviceId
                                        documentPickerLauncher.launch(arrayOf("*/*"))
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
                                        headerActions = headerActionButtons,
                                        onBannerClick = {
                                            if (!state.linkServiceRunning) {
                                                viewModel.toggleLinkService()
                                            } else {
                                                viewModel.toggleLanWifiState()
                                            }
                                        },
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

                            TopTab.TRANSFERS -> {
                                TransfersScreen(
                                    filePrompts = state.filePrompts,
                                    transfers = state.transfers,
                                    headerActions = headerActionButtons,
                                    onAcceptIncoming = { viewModel.acceptIncomingFile(it) },
                                    onDeclineIncoming = { viewModel.rejectIncomingFile(it) },
                                    onCancelTransfer = { viewModel.cancelTransfer(it) },
                                    onRetryBlockedCommit = { viewModel.retryBlockedCommit(it) },
                                    onClearCompleted = { viewModel.clearCompletedTransfers() }
                                )
                            }

                            TopTab.THIS_DEVICE -> {
                                ThisDeviceScreen(
                                    localDisplayName = state.localDisplayName,
                                    localDeviceId = state.localDeviceId,
                                    darkTheme = state.darkTheme,
                                    dynamicColor = state.dynamicColor,
                                    linkServiceRunning = state.linkServiceRunning,
                                    permissionsGranted = state.runtimePermissionsGranted,
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
                                    onRequestPermissions = {
                                        if (requiredRuntimePermissions.isNotEmpty()) {
                                            permissionsLauncher.launch(requiredRuntimePermissions)
                                        } else {
                                            viewModel.onPermissionsResult(true)
                                        }
                                    },
                                    onOpenUserGuide = { viewModel.openOnboardingGuide() },
                                    onOpenScenarioLab = { showScenarioLab = true },
                                    onResetIdentity = { viewModel.resetIdentity() }
                                )
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
        ScenarioLabBottomSheet(
            onDismiss = { showScenarioLab = false },
            onDiscoverPeer = { viewModel.labDiscoverPeer() },
            onInboundPairing = { viewModel.labInboundPairing() },
            onInboundFile = { viewModel.labInboundFile(moveBlocked = false) },
            onInboundBlockedFile = { viewModel.labInboundFile(moveBlocked = true) },
            onRemoteClipboard = { viewModel.labRemoteClipboard() },
            onFlapConnection = { viewModel.labTogglePeerConnection() }
        )
    }

    OnboardingGuideOverlay(
        visible = showOnboardingGuide,
        onDismiss = { viewModel.dismissOnboardingGuide() },
        onDismissAndOpenLab = {
            viewModel.dismissOnboardingGuide()
            showScenarioLab = true
        }
    )
}
