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
import androidx.compose.material.icons.rounded.CheckCircle
import androidx.compose.material.icons.rounded.Close
import androidx.compose.material.icons.rounded.Download
import androidx.compose.material.icons.rounded.ErrorOutline
import androidx.compose.material.icons.rounded.MoveToInbox
import androidx.compose.material.icons.rounded.Refresh
import androidx.compose.material.icons.rounded.SwapVert
import androidx.compose.material.icons.rounded.Upload
import androidx.compose.material3.Button
import androidx.compose.material3.FilledTonalButton
import androidx.compose.material3.FilledTonalIconButton
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.minimumInteractiveComponentSize
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
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
import app.contextall.model.TransferDirection
import app.contextall.model.TransferSnapshot
import app.contextall.ui.components.ExpressiveGapProgressBar
import app.contextall.ui.components.ExpressiveGroupTile
import app.contextall.ui.components.ExpressiveIconBadge
import app.contextall.ui.components.ExpressiveScreenHeader
import app.contextall.ui.theme.JetBrainsMonoFamily
import app.contextall.ui.theme.LocalExpressivePalette

@Composable
fun TransfersScreen(
    filePrompts: List<FilePromptSnapshot>,
    transfers: List<TransferSnapshot>,
    headerActions: @Composable () -> Unit,
    onAcceptIncoming: (String) -> Unit,
    onDeclineIncoming: (String) -> Unit,
    onCancelTransfer: (String) -> Unit,
    onRetryBlockedCommit: (String) -> Unit,
    onClearCompleted: () -> Unit,
    modifier: Modifier = Modifier
) {
    val active = remember(transfers) { transfers.filterNot { it.completed } }
    val completed = remember(transfers) { transfers.filter { it.completed } }

    Box(modifier = modifier.fillMaxSize(), contentAlignment = Alignment.TopCenter) {
        LazyColumn(
            modifier = Modifier
                .fillMaxWidth()
                .widthIn(max = 640.dp)
                .testTag("transfers_list"),
            contentPadding = PaddingValues(start = 16.dp, end = 16.dp, top = 8.dp, bottom = 116.dp),
            verticalArrangement = Arrangement.spacedBy(20.dp)
        ) {
            item {
                ExpressiveScreenHeader(
                    title = stringResource(R.string.header_transfers_title),
                    subtitle = stringResource(R.string.header_transfers_subtitle),
                    trailingActions = headerActions
                )
            }

            if (filePrompts.isEmpty() && active.isEmpty() && completed.isEmpty()) {
                item {
                    EmptyTransfersStateCard()
                }
            }

            if (filePrompts.isNotEmpty()) {
                item {
                    Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
                        filePrompts.forEach { prompt ->
                            IncomingFilePromptCard(
                                prompt = prompt,
                                onAccept = { onAcceptIncoming(prompt.transferId) },
                                onDecline = { onDeclineIncoming(prompt.transferId) }
                            )
                        }
                    }
                }
            }

            if (active.isNotEmpty()) {
                item {
                    SectionHeader(
                        text = stringResource(R.string.transfers_active_section, active.size)
                    )
                    Spacer(modifier = Modifier.height(10.dp))
                    Column(verticalArrangement = Arrangement.spacedBy(2.dp)) {
                        active.forEachIndexed { index, item ->
                            ExpressiveGroupTile(
                                index = index,
                                count = active.size,
                                modifier = Modifier.testTag("transfer_card_${item.transferId}")
                            ) {
                                ActiveTransferTileContent(
                                    item = item,
                                    onCancel = { onCancelTransfer(item.transferId) },
                                    onRetryBlocked = { onRetryBlockedCommit(item.transferId) }
                                )
                            }
                        }
                    }
                }
            }

            if (completed.isNotEmpty()) {
                item {
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.SpaceBetween
                    ) {
                        SectionHeader(
                            text = stringResource(R.string.transfers_completed_section, completed.size)
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
                        completed.forEachIndexed { index, item ->
                            ExpressiveGroupTile(
                                index = index,
                                count = completed.size
                            ) {
                                CompletedTransferRow(item = item)
                            }
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun EmptyTransfersStateCard() {
    val palette = LocalExpressivePalette.current
    Surface(
        shape = RoundedCornerShape(28.dp),
        color = MaterialTheme.colorScheme.surfaceContainerLow,
        modifier = Modifier.fillMaxWidth()
    ) {
        Column(
            modifier = Modifier.padding(32.dp),
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
            ExpressiveIconBadge(
                icon = Icons.Rounded.SwapVert,
                backgroundColor = palette.skyIconBg,
                iconTint = palette.skyIconFg,
                size = 56.dp
            )
            Spacer(modifier = Modifier.height(14.dp))
            Text(
                text = stringResource(R.string.empty_transfers_title),
                style = MaterialTheme.typography.titleMedium
            )
            Spacer(modifier = Modifier.height(6.dp))
            Text(
                text = stringResource(R.string.empty_transfers_subtitle),
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
        item.waitingReconnect -> Triple(
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
                item.waitingReconnect -> palette.amberIconFg
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
private fun CompletedTransferRow(item: TransferSnapshot) {
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
                style = MaterialTheme.typography.titleSmall
            )
            Spacer(modifier = Modifier.height(2.dp))
            Text(
                text = if (item.direction == TransferDirection.SEND) {
                    stringResource(R.string.transfers_completed_send_sub, item.segmentCount)
                } else {
                    stringResource(R.string.transfers_completed_recv_sub, formatByteSize(item.totalSize))
                },
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
        }
    }
}
