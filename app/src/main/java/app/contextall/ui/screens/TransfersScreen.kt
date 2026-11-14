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
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.CheckCircle
import androidx.compose.material.icons.rounded.Close
import androidx.compose.material.icons.rounded.Download
import androidx.compose.material.icons.rounded.ErrorOutline
import androidx.compose.material.icons.rounded.MoveToInbox
import androidx.compose.material.icons.rounded.Pause
import androidx.compose.material.icons.rounded.PlayArrow
import androidx.compose.material.icons.rounded.Refresh
import androidx.compose.material.icons.rounded.Replay
import androidx.compose.material.icons.rounded.ScheduleSend
import androidx.compose.material.icons.rounded.SwapVert
import androidx.compose.material.icons.rounded.Upload
import androidx.compose.material3.Button
import androidx.compose.material3.FilledTonalButton
import androidx.compose.material3.FilledTonalIconButton
import androidx.compose.material3.FilterChip
import androidx.compose.material3.FilterChipDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
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
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import app.contextall.R
import app.contextall.core.ProtocolLimits
import app.contextall.core.formatByteSize
import app.contextall.model.FilePromptSnapshot
import app.contextall.model.PeerDeviceSnapshot
import app.contextall.model.QueuedUploadSnapshot
import app.contextall.model.SessionTelemetrySnapshot
import app.contextall.model.TransferDirection
import app.contextall.model.TransferSnapshot
import app.contextall.ui.components.ExpressiveGapProgressBar
import app.contextall.ui.components.ExpressiveGroupTile
import app.contextall.ui.components.ExpressiveIconBadge
import app.contextall.ui.components.ExpressiveScreenHeader
import app.contextall.ui.components.TransferTelemetrySummaryStrip
import app.contextall.ui.components.TransferUploadHubCard
import app.contextall.ui.theme.JetBrainsMonoFamily
import app.contextall.ui.theme.LocalExpressivePalette

private enum class TransferListFilter {
    ALL,
    SEND,
    RECEIVE,
    COMPLETED
}

@Composable
fun TransfersScreen(
    trustedPeers: List<PeerDeviceSnapshot>,
    filePrompts: List<FilePromptSnapshot>,
    transfers: List<TransferSnapshot>,
    queuedUploads: List<QueuedUploadSnapshot>,
    telemetry: SessionTelemetrySnapshot,
    debugMode: Boolean = false,
    headerActions: @Composable () -> Unit,
    onPickLocalFiles: (targetDeviceId: String) -> Unit,
    onPickMediaFiles: (targetDeviceId: String) -> Unit,
    onSendTextFile: (targetDeviceId: String, fileName: String, content: String) -> Unit,
    onSendPresetPayload: (targetDeviceId: String, fileName: String, totalBytes: Long) -> Unit,
    onNavigateToDevices: () -> Unit,
    onAcceptIncoming: (String) -> Unit,
    onDeclineIncoming: (String) -> Unit,
    onTogglePauseTransfer: (String) -> Unit,
    onCancelTransfer: (String) -> Unit,
    onRetryBlockedCommit: (String) -> Unit,
    onRemoveQueuedUpload: (String) -> Unit,
    onClearQueuedUploads: () -> Unit,
    onResendCompleted: (String) -> Unit,
    onClearCompleted: () -> Unit,
    modifier: Modifier = Modifier
) {
    var selectedFilter by remember { mutableStateOf(TransferListFilter.ALL) }

    val allSendCount = remember(transfers, queuedUploads) {
        transfers.count { it.direction == TransferDirection.SEND } + queuedUploads.size
    }
    val allReceiveCount = remember(transfers, filePrompts) {
        transfers.count { it.direction == TransferDirection.RECEIVE } + filePrompts.size
    }
    val allCompletedCount = remember(transfers) {
        transfers.count { it.completed }
    }
    val totalItemCount = remember(transfers, filePrompts, queuedUploads) {
        transfers.size + filePrompts.size + queuedUploads.size
    }

    val visiblePrompts = remember(filePrompts, selectedFilter) {
        when (selectedFilter) {
            TransferListFilter.ALL, TransferListFilter.RECEIVE -> filePrompts
            TransferListFilter.SEND, TransferListFilter.COMPLETED -> emptyList()
        }
    }

    val visibleActive = remember(transfers, selectedFilter) {
        val activeOnly = transfers.filterNot { it.completed }
        when (selectedFilter) {
            TransferListFilter.ALL -> activeOnly
            TransferListFilter.SEND -> activeOnly.filter { it.direction == TransferDirection.SEND }
            TransferListFilter.RECEIVE -> activeOnly.filter { it.direction == TransferDirection.RECEIVE }
            TransferListFilter.COMPLETED -> emptyList()
        }
    }

    val visibleQueued = remember(queuedUploads, selectedFilter) {
        when (selectedFilter) {
            TransferListFilter.ALL, TransferListFilter.SEND -> queuedUploads
            TransferListFilter.RECEIVE, TransferListFilter.COMPLETED -> emptyList()
        }
    }

    val visibleCompleted = remember(transfers, selectedFilter) {
        val completedOnly = transfers.filter { it.completed }
        when (selectedFilter) {
            TransferListFilter.ALL, TransferListFilter.COMPLETED -> completedOnly
            TransferListFilter.SEND -> completedOnly.filter { it.direction == TransferDirection.SEND }
            TransferListFilter.RECEIVE -> completedOnly.filter { it.direction == TransferDirection.RECEIVE }
        }
    }

    Box(modifier = modifier.fillMaxSize(), contentAlignment = Alignment.TopCenter) {
        LazyColumn(
            modifier = Modifier
                .fillMaxWidth()
                .widthIn(max = 640.dp)
                .testTag("transfers_list"),
            contentPadding = PaddingValues(start = 16.dp, end = 16.dp, top = 8.dp, bottom = 128.dp),
            verticalArrangement = Arrangement.spacedBy(18.dp)
        ) {
            item {
                ExpressiveScreenHeader(
                    title = stringResource(R.string.header_transfers_title),
                    subtitle = stringResource(R.string.header_transfers_subtitle),
                    trailingActions = headerActions
                )
            }

            if (visiblePrompts.isNotEmpty()) {
                item {
                    Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
                        visiblePrompts.forEach { prompt ->
                            IncomingFilePromptCard(
                                prompt = prompt,
                                onAccept = { onAcceptIncoming(prompt.transferId) },
                                onDecline = { onDeclineIncoming(prompt.transferId) }
                            )
                        }
                    }
                }
            }

            item {
                TransferTelemetrySummaryStrip(telemetry = telemetry)
            }

            item {
                TransferUploadHubCard(
                    trustedPeers = trustedPeers,
                    activeTransfers = transfers.filterNot { it.completed },
                    debugMode = debugMode,
                    onPickLocalFiles = onPickLocalFiles,
                    onPickMediaFiles = onPickMediaFiles,
                    onSendTextFile = onSendTextFile,
                    onSendPresetPayload = onSendPresetPayload,
                    onNavigateToDevices = onNavigateToDevices
                )
            }

            item {
                LazyRow(
                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                    modifier = Modifier.fillMaxWidth()
                ) {
                    item {
                        TransferFilterChip(
                            label = stringResource(R.string.transfers_filter_all, totalItemCount),
                            selected = selectedFilter == TransferListFilter.ALL,
                            onClick = { selectedFilter = TransferListFilter.ALL },
                            testTag = "transfers_filter_all"
                        )
                    }
                    item {
                        TransferFilterChip(
                            label = stringResource(R.string.transfers_filter_send, allSendCount),
                            selected = selectedFilter == TransferListFilter.SEND,
                            onClick = { selectedFilter = TransferListFilter.SEND },
                            testTag = "transfers_filter_send"
                        )
                    }
                    item {
                        TransferFilterChip(
                            label = stringResource(R.string.transfers_filter_receive, allReceiveCount),
                            selected = selectedFilter == TransferListFilter.RECEIVE,
                            onClick = { selectedFilter = TransferListFilter.RECEIVE },
                            testTag = "transfers_filter_receive"
                        )
                    }
                    item {
                        TransferFilterChip(
                            label = stringResource(R.string.transfers_filter_done, allCompletedCount),
                            selected = selectedFilter == TransferListFilter.COMPLETED,
                            onClick = { selectedFilter = TransferListFilter.COMPLETED },
                            testTag = "transfers_filter_done"
                        )
                    }
                }
            }

            if (visiblePrompts.isEmpty() &&
                visibleActive.isEmpty() &&
                visibleQueued.isEmpty() &&
                visibleCompleted.isEmpty()
            ) {
                item {
                    EmptyTransfersStateCard(isFiltered = totalItemCount > 0)
                }
            }

            if (visibleActive.isNotEmpty()) {
                item {
                    SectionHeader(
                        text = stringResource(R.string.transfers_active_section, visibleActive.size)
                    )
                    Spacer(modifier = Modifier.height(10.dp))
                    Column(verticalArrangement = Arrangement.spacedBy(2.dp)) {
                        visibleActive.forEachIndexed { index, item ->
                            ExpressiveGroupTile(
                                index = index,
                                count = visibleActive.size,
                                modifier = Modifier.testTag("transfer_card_${item.transferId}")
                            ) {
                                ActiveTransferTileContent(
                                    item = item,
                                    onTogglePause = { onTogglePauseTransfer(item.transferId) },
                                    onCancel = { onCancelTransfer(item.transferId) },
                                    onRetryBlocked = { onRetryBlockedCommit(item.transferId) }
                                )
                            }
                        }
                    }
                }
            }

            if (visibleQueued.isNotEmpty()) {
                item {
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.SpaceBetween
                    ) {
                        SectionHeader(
                            text = stringResource(R.string.transfers_queued_section, visibleQueued.size)
                        )
                        TextButton(
                            onClick = onClearQueuedUploads,
                            modifier = Modifier.testTag("clear_queued_uploads_button")
                        ) {
                            Text(stringResource(R.string.transfers_clear_queued))
                        }
                    }
                    Spacer(modifier = Modifier.height(4.dp))
                    Column(verticalArrangement = Arrangement.spacedBy(2.dp)) {
                        visibleQueued.forEachIndexed { index, queued ->
                            ExpressiveGroupTile(
                                index = index,
                                count = visibleQueued.size,
                                modifier = Modifier.testTag("queued_upload_${queued.queueId}")
                            ) {
                                QueuedUploadRowContent(
                                    item = queued,
                                    onRemove = { onRemoveQueuedUpload(queued.queueId) }
                                )
                            }
                        }
                    }
                }
            }

            if (visibleCompleted.isNotEmpty()) {
                item {
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.SpaceBetween
                    ) {
                        SectionHeader(
                            text = stringResource(R.string.transfers_completed_section, visibleCompleted.size)
                        )
                        TextButton(
                            onClick = onClearCompleted,
                            modifier = Modifier.testTag("clear_completed_transfers_button")
                        ) {
                            Text(stringResource(R.string.transfers_clear_completed))
                        }
                    }
                    Spacer(modifier = Modifier.height(4.dp))
                    Column(verticalArrangement = Arrangement.spacedBy(2.dp)) {
                        visibleCompleted.forEachIndexed { index, item ->
                            ExpressiveGroupTile(
                                index = index,
                                count = visibleCompleted.size
                            ) {
                                CompletedTransferRow(
                                    item = item,
                                    canResend = item.direction == TransferDirection.SEND &&
                                        trustedPeers.any { it.deviceId == item.deviceId },
                                    onResend = { onResendCompleted(item.transferId) }
                                )
                            }
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun TransferFilterChip(
    label: String,
    selected: Boolean,
    onClick: () -> Unit,
    testTag: String
) {
    FilterChip(
        selected = selected,
        onClick = onClick,
        label = {
            Text(
                text = label,
                style = MaterialTheme.typography.labelMedium
            )
        },
        shape = CircleShape,
        colors = FilterChipDefaults.filterChipColors(
            selectedContainerColor = MaterialTheme.colorScheme.secondaryContainer,
            selectedLabelColor = MaterialTheme.colorScheme.onSecondaryContainer
        ),
        modifier = Modifier
            .minimumInteractiveComponentSize()
            .testTag(testTag)
    )
}

@Composable
private fun EmptyTransfersStateCard(isFiltered: Boolean) {
    val palette = LocalExpressivePalette.current
    Surface(
        shape = RoundedCornerShape(28.dp),
        color = MaterialTheme.colorScheme.surfaceContainerLow,
        modifier = Modifier.fillMaxWidth()
    ) {
        Column(
            modifier = Modifier.padding(28.dp),
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
            ExpressiveIconBadge(
                icon = Icons.Rounded.SwapVert,
                backgroundColor = palette.skyIconBg,
                iconTint = palette.skyIconFg,
                size = 54.dp
            )
            Spacer(modifier = Modifier.height(12.dp))
            Text(
                text = stringResource(
                    if (isFiltered) R.string.transfers_filtered_empty_title
                    else R.string.empty_transfers_title
                ),
                style = MaterialTheme.typography.titleMedium
            )
            Spacer(modifier = Modifier.height(6.dp))
            Text(
                text = stringResource(
                    if (isFiltered) R.string.transfers_filtered_empty_sub
                    else R.string.empty_transfers_subtitle
                ),
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                textAlign = TextAlign.Center
            )
        }
    }
}

@Composable
private fun IncomingFilePromptCard(
    prompt: FilePromptSnapshot,
    onAccept: () -> Unit,
    onDecline: () -> Unit
) {
    val timeProgress = (prompt.remainingMilliseconds.toFloat() / ProtocolLimits.PROMPT_TIMEOUT_MS.toFloat())
        .coerceIn(0f, 1f)

    Surface(
        shape = RoundedCornerShape(28.dp),
        color = MaterialTheme.colorScheme.primaryContainer,
        modifier = Modifier
            .fillMaxWidth()
            .testTag("incoming_file_prompt_${prompt.transferId}")
    ) {
        Column(modifier = Modifier.padding(20.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Surface(
                    shape = RoundedCornerShape(16.dp),
                    color = MaterialTheme.colorScheme.primary,
                    modifier = Modifier.size(46.dp)
                ) {
                    Box(contentAlignment = Alignment.Center) {
                        Icon(
                            imageVector = Icons.Rounded.MoveToInbox,
                            contentDescription = null,
                            tint = MaterialTheme.colorScheme.onPrimary
                        )
                    }
                }
                Spacer(modifier = Modifier.width(14.dp))
                Column(modifier = Modifier.weight(1f)) {
                    Text(
                        text = stringResource(R.string.transfers_incoming_header),
                        style = MaterialTheme.typography.labelLarge,
                        color = MaterialTheme.colorScheme.primary
                    )
                    Text(
                        text = prompt.fileName,
                        style = MaterialTheme.typography.titleMedium,
                        color = MaterialTheme.colorScheme.onPrimaryContainer,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis
                    )
                    Text(
                        text = stringResource(
                            R.string.transfers_incoming_from,
                            prompt.peerDisplayName,
                            formatByteSize(prompt.totalSize),
                            prompt.segmentCount
                        ),
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onPrimaryContainer.copy(alpha = 0.8f)
                    )
                }
            }
            Spacer(modifier = Modifier.height(14.dp))
            ExpressiveGapProgressBar(
                progress = timeProgress,
                activeColor = MaterialTheme.colorScheme.primary,
                trackColor = MaterialTheme.colorScheme.surfaceContainerLowest.copy(alpha = 0.65f)
            )
            Spacer(modifier = Modifier.height(6.dp))
            Text(
                text = stringResource(R.string.transfers_incoming_countdown, prompt.remainingSeconds),
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onPrimaryContainer.copy(alpha = 0.8f)
            )
            Spacer(modifier = Modifier.height(16.dp))
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(10.dp)
            ) {
                OutlinedButton(
                    onClick = onDecline,
                    shape = RoundedCornerShape(18.dp),
                    modifier = Modifier
                        .weight(0.4f)
                        .height(50.dp)
                        .testTag("incoming_decline_${prompt.transferId}")
                ) {
                    Text(text = stringResource(R.string.transfers_decline_button))
                }
                Button(
                    onClick = onAccept,
                    shape = CircleShape,
                    modifier = Modifier
                        .weight(0.6f)
                        .height(50.dp)
                        .testTag("incoming_accept_${prompt.transferId}")
                ) {
                    Text(text = stringResource(R.string.transfers_accept_button))
                }
            }
        }
    }
}

@Composable
private fun ActiveTransferTileContent(
    item: TransferSnapshot,
    onTogglePause: () -> Unit,
    onCancel: () -> Unit,
    onRetryBlocked: () -> Unit
) {
    val palette = LocalExpressivePalette.current
    val pct = (item.progressFraction * 100).toInt()
    val (badgeBg, badgeFg, icon) = when {
        item.moveBlocked -> Triple(
            MaterialTheme.colorScheme.errorContainer,
            MaterialTheme.colorScheme.onErrorContainer,
            Icons.Rounded.ErrorOutline
        )
        item.pausedByOperator || item.waitingReconnect -> Triple(
            palette.amberIconBg,
            palette.amberIconFg,
            if (item.direction == TransferDirection.SEND) Icons.Rounded.Upload else Icons.Rounded.Download
        )
        else -> Triple(
            palette.skyIconBg,
            palette.skyIconFg,
            if (item.direction == TransferDirection.SEND) Icons.Rounded.Upload else Icons.Rounded.Download
        )
    }

    Column(modifier = Modifier.padding(horizontal = 18.dp, vertical = 16.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            ExpressiveIconBadge(
                icon = icon,
                backgroundColor = badgeBg,
                iconTint = badgeFg
            )
            Spacer(modifier = Modifier.width(14.dp))
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    text = item.fileName,
                    style = MaterialTheme.typography.titleSmall,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis
                )
                Spacer(modifier = Modifier.height(2.dp))
                Text(
                    text = stringResource(
                        if (item.direction == TransferDirection.SEND) {
                            R.string.transfers_direction_send
                        } else {
                            R.string.transfers_direction_receive
                        },
                        item.peerDisplayName,
                        formatByteSize(item.totalSize)
                    ),
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
            }

            if (!item.moveBlocked) {
                FilledTonalIconButton(
                    onClick = onTogglePause,
                    modifier = Modifier
                        .minimumInteractiveComponentSize()
                        .testTag("pause_resume_transfer_${item.transferId}")
                ) {
                    Icon(
                        imageVector = if (item.pausedByOperator) Icons.Rounded.PlayArrow else Icons.Rounded.Pause,
                        contentDescription = stringResource(
                            if (item.pausedByOperator) R.string.transfers_resume_desc
                            else R.string.transfers_pause_desc
                        ),
                        modifier = Modifier.size(18.dp)
                    )
                }
            }

            FilledTonalIconButton(
                onClick = onCancel,
                modifier = Modifier
                    .minimumInteractiveComponentSize()
                    .testTag("cancel_transfer_${item.transferId}")
            ) {
                Icon(
                    imageVector = Icons.Rounded.Close,
                    contentDescription = stringResource(R.string.transfers_cancel_desc),
                    modifier = Modifier.size(18.dp)
                )
            }
        }
        Spacer(modifier = Modifier.height(14.dp))
        ExpressiveGapProgressBar(
            progress = item.progressFraction,
            activeColor = when {
                item.moveBlocked -> MaterialTheme.colorScheme.error
                item.pausedByOperator || item.waitingReconnect -> palette.amberIconFg
                else -> MaterialTheme.colorScheme.primary
            }
        )
        Spacer(modifier = Modifier.height(8.dp))
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically
        ) {
            Text(
                text = stringResource(
                    when {
                        item.moveBlocked -> R.string.transfers_move_blocked
                        item.pausedByOperator -> R.string.transfers_paused_status
                        item.waitingReconnect -> R.string.transfers_waiting_reconnect
                        else -> R.string.transfers_segment_progress
                    },
                    item.completedSegments,
                    item.segmentCount
                ),
                fontFamily = JetBrainsMonoFamily,
                fontSize = 11.sp,
                color = if (item.moveBlocked) {
                    MaterialTheme.colorScheme.error
                } else {
                    MaterialTheme.colorScheme.onSurfaceVariant
                },
                modifier = Modifier.weight(1f)
            )
            Spacer(modifier = Modifier.width(8.dp))
            Text(
                text = "$pct%",
                fontFamily = JetBrainsMonoFamily,
                fontSize = 12.sp,
                color = MaterialTheme.colorScheme.primary
            )
        }

        if (item.moveBlocked) {
            Spacer(modifier = Modifier.height(12.dp))
            FilledTonalButton(
                onClick = onRetryBlocked,
                shape = CircleShape,
                modifier = Modifier
                    .fillMaxWidth()
                    .height(46.dp)
                    .testTag("retry_blocked_${item.transferId}")
            ) {
                Icon(imageVector = Icons.Rounded.Refresh, contentDescription = null)
                Spacer(modifier = Modifier.width(8.dp))
                Text(stringResource(R.string.transfers_retry_commit))
            }
        }
    }
}

@Composable
private fun QueuedUploadRowContent(
    item: QueuedUploadSnapshot,
    onRemove: () -> Unit
) {
    val palette = LocalExpressivePalette.current
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = 18.dp, vertical = 14.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        ExpressiveIconBadge(
            icon = Icons.Rounded.ScheduleSend,
            backgroundColor = palette.violetIconBg,
            iconTint = palette.violetIconFg
        )
        Spacer(modifier = Modifier.width(14.dp))
        Column(modifier = Modifier.weight(1f)) {
            Text(
                text = item.fileName,
                style = MaterialTheme.typography.titleSmall,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis
            )
            Spacer(modifier = Modifier.height(2.dp))
            Text(
                text = stringResource(
                    R.string.transfers_queued_item_sub,
                    item.peerDisplayName,
                    formatByteSize(item.totalSize),
                    item.segmentCount
                ),
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
        }
        FilledTonalIconButton(
            onClick = onRemove,
            modifier = Modifier
                .minimumInteractiveComponentSize()
                .testTag("remove_queued_${item.queueId}")
        ) {
            Icon(
                imageVector = Icons.Rounded.Close,
                contentDescription = stringResource(R.string.transfers_remove_queued_desc),
                modifier = Modifier.size(18.dp)
            )
        }
    }
}

@Composable
private fun CompletedTransferRow(
    item: TransferSnapshot,
    canResend: Boolean,
    onResend: () -> Unit
) {
    val palette = LocalExpressivePalette.current
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = 18.dp, vertical = 16.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        ExpressiveIconBadge(
            icon = Icons.Rounded.CheckCircle,
            backgroundColor = palette.mintIconBg,
            iconTint = palette.mintIconFg
        )
        Spacer(modifier = Modifier.width(16.dp))
        Column(modifier = Modifier.weight(1f)) {
            Text(
                text = item.savedFileName,
                style = MaterialTheme.typography.titleSmall,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis
            )
            Spacer(modifier = Modifier.height(2.dp))
            Text(
                text = if (item.direction == TransferDirection.SEND) {
                    stringResource(
                        R.string.transfers_completed_peer_send,
                        item.peerDisplayName,
                        formatByteSize(item.totalSize),
                        item.segmentCount
                    )
                } else {
                    stringResource(
                        R.string.transfers_completed_peer_recv,
                        item.peerDisplayName,
                        formatByteSize(item.totalSize)
                    )
                },
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
        }
        if (canResend) {
            Spacer(modifier = Modifier.width(8.dp))
            FilledTonalButton(
                onClick = onResend,
                shape = CircleShape,
                contentPadding = PaddingValues(horizontal = 12.dp, vertical = 6.dp),
                modifier = Modifier
                    .minimumInteractiveComponentSize()
                    .testTag("resend_completed_${item.transferId}")
            ) {
                Icon(
                    imageVector = Icons.Rounded.Replay,
                    contentDescription = null,
                    modifier = Modifier.size(16.dp)
                )
                Spacer(modifier = Modifier.width(4.dp))
                Text(
                    text = stringResource(R.string.transfers_resend_button),
                    style = MaterialTheme.typography.labelMedium
                )
            }
        }
    }
}
