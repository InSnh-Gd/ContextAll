package app.contextall.ui.components

import androidx.compose.animation.core.FastOutSlowInEasing
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectHorizontalDragGestures
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.Analytics
import androidx.compose.material.icons.rounded.CheckCircle
import androidx.compose.material.icons.rounded.SwapVert
import androidx.compose.material3.FilterChip
import androidx.compose.material3.FilterChipDefaults
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.minimumInteractiveComponentSize
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.PathEffect
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.StrokeJoin
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import app.contextall.R
import app.contextall.core.formatByteSize
import app.contextall.model.SessionTelemetrySnapshot
import app.contextall.model.TelemetryBucketSnapshot
import app.contextall.ui.theme.JetBrainsMonoFamily
import app.contextall.ui.theme.LocalExpressivePalette
import java.util.Locale
import kotlin.math.roundToInt

enum class TelemetryChartMode {
    COMBINED,
    SUCCESS_RATE,
    THROUGHPUT
}

@Composable
fun TelemetryAnalyticsCard(
    telemetry: SessionTelemetrySnapshot,
    modifier: Modifier = Modifier
) {
    val palette = LocalExpressivePalette.current
    val buckets = telemetry.buckets
    if (buckets.isEmpty()) return

    var chartMode by remember { mutableStateOf(TelemetryChartMode.COMBINED) }
    var selectedIndex by remember { mutableIntStateOf(buckets.lastIndex) }

    LaunchedEffect(buckets.size) {
        if (selectedIndex !in buckets.indices) {
            selectedIndex = buckets.lastIndex
        }
    }

    val safeSelectedIndex = selectedIndex.coerceIn(0, buckets.lastIndex)
    val activeBucket = buckets[safeSelectedIndex]

    val maxBucketBytes = remember(buckets) {
        (buckets.maxOfOrNull { it.totalBytes } ?: 1_000_000L).coerceAtLeast(1_000_000L)
    }

    val successLineColor = palette.mintIconFg
    val uploadBarColor = MaterialTheme.colorScheme.primary
    val downloadBarColor = palette.skyIconFg

    Column(
        modifier = modifier
            .fillMaxWidth()
            .testTag("telemetry_analytics_card"),
        verticalArrangement = Arrangement.spacedBy(2.dp)
    ) {
        // Tile 0: Header + Dual KPI Summary
        ExpressiveGroupTile(index = 0, count = 3) {
            Column(modifier = Modifier.padding(horizontal = 18.dp, vertical = 16.dp)) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    ExpressiveIconBadge(
                        icon = Icons.Rounded.Analytics,
                        backgroundColor = palette.violetIconBg,
                        iconTint = palette.violetIconFg
                    )
                    Spacer(modifier = Modifier.width(14.dp))
                    Column(modifier = Modifier.weight(1f)) {
                        Text(
                            text = stringResource(R.string.telemetry_card_title),
                            style = MaterialTheme.typography.titleMedium
                        )
                        Spacer(modifier = Modifier.height(2.dp))
                        Text(
                            text = stringResource(R.string.telemetry_card_subtitle),
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
                    }
                }

                Spacer(modifier = Modifier.height(14.dp))

                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.spacedBy(10.dp)
                ) {
                    TelemetryKpiBox(
                        label = stringResource(R.string.telemetry_kpi_success_label),
                        value = String.format(Locale.US, "%.1f%%", telemetry.overallSuccessRate),
                        subtitle = stringResource(
                            R.string.telemetry_kpi_success_sub,
                            telemetry.totalSucceeded,
                            telemetry.totalAttempts
                        ),
                        accentColor = successLineColor,
                        badgeBg = palette.mintIconBg.copy(alpha = 0.45f),
                        icon = Icons.Rounded.CheckCircle,
                        modifier = Modifier.weight(1f)
                    )
                    TelemetryKpiBox(
                        label = stringResource(R.string.telemetry_kpi_throughput_label),
                        value = formatByteSize(telemetry.totalThroughputBytes),
                        subtitle = stringResource(
                            R.string.telemetry_kpi_throughput_sub,
                            formatByteSize(telemetry.totalUploadBytes),
                            formatByteSize(telemetry.totalDownloadBytes)
                        ),
                        accentColor = uploadBarColor,
                        badgeBg = palette.skyIconBg.copy(alpha = 0.45f),
                        icon = Icons.Rounded.SwapVert,
                        modifier = Modifier.weight(1f)
                    )
                }
            }
        }

        // Tile 1: Mode Selector + Interactive Multi-Series Visualization Canvas
        ExpressiveGroupTile(index = 1, count = 3) {
            Column(modifier = Modifier.padding(horizontal = 18.dp, vertical = 14.dp)) {
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    TelemetryModeChip(
                        label = stringResource(R.string.telemetry_mode_combined),
                        selected = chartMode == TelemetryChartMode.COMBINED,
                        onClick = { chartMode = TelemetryChartMode.COMBINED },
                        testTag = "telemetry_mode_combined"
                    )
                    TelemetryModeChip(
                        label = stringResource(R.string.telemetry_mode_success),
                        selected = chartMode == TelemetryChartMode.SUCCESS_RATE,
                        onClick = { chartMode = TelemetryChartMode.SUCCESS_RATE },
                        testTag = "telemetry_mode_success"
                    )
                    TelemetryModeChip(
                        label = stringResource(R.string.telemetry_mode_throughput),
                        selected = chartMode == TelemetryChartMode.THROUGHPUT,
                        onClick = { chartMode = TelemetryChartMode.THROUGHPUT },
                        testTag = "telemetry_mode_throughput"
                    )
                }

                Spacer(modifier = Modifier.height(10.dp))

                // Series Legend + Axis Scale Indicators
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Row(
                        horizontalArrangement = Arrangement.spacedBy(12.dp),
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        if (chartMode != TelemetryChartMode.THROUGHPUT) {
                            LegendDotItem(
                                color = successLineColor,
                                label = stringResource(R.string.telemetry_legend_success)
                            )
                        }
                        if (chartMode != TelemetryChartMode.SUCCESS_RATE) {
                            LegendDotItem(
                                color = uploadBarColor,
                                label = stringResource(R.string.telemetry_legend_upload)
                            )
                            LegendDotItem(
                                color = downloadBarColor.copy(alpha = 0.68f),
                                label = stringResource(R.string.telemetry_legend_download)
                            )
                        }
                    }

                    Text(
                        text = when (chartMode) {
                            TelemetryChartMode.SUCCESS_RATE -> "0–100%"
                            TelemetryChartMode.THROUGHPUT -> "峰值 ${formatByteSize(maxBucketBytes)}"
                            TelemetryChartMode.COMBINED -> "100% · ${formatByteSize(maxBucketBytes)}"
                        },
                        fontFamily = JetBrainsMonoFamily,
                        fontSize = 10.5.sp,
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                }

                Spacer(modifier = Modifier.height(10.dp))

                InteractiveTelemetryPlot(
                    buckets = buckets,
                    maxBucketBytes = maxBucketBytes,
                    mode = chartMode,
                    selectedIndex = safeSelectedIndex,
                    onSelectIndex = { selectedIndex = it },
                    successLineColor = successLineColor,
                    uploadBarColor = uploadBarColor,
                    downloadBarColor = downloadBarColor
                )

                Spacer(modifier = Modifier.height(8.dp))

                // X-axis Bucket Labels
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween
                ) {
                    buckets.forEachIndexed { idx, bucket ->
                        val isSelected = idx == safeSelectedIndex
                        Box(
                            contentAlignment = Alignment.Center,
                            modifier = Modifier
                                .weight(1f)
                                .minimumInteractiveComponentSize()
                                .clip(CircleShape)
                                .background(
                                    if (isSelected) {
                                        MaterialTheme.colorScheme.secondaryContainer
                                    } else {
                                        Color.Transparent
                                    }
                                )
                                .clickable { selectedIndex = idx }
                                .padding(vertical = 4.dp)
                                .testTag("telemetry_bucket_label_$idx")
                        ) {
                            Text(
                                text = bucket.label,
                                fontFamily = JetBrainsMonoFamily,
                                fontSize = 11.sp,
                                fontWeight = if (isSelected) FontWeight.Bold else FontWeight.Normal,
                                color = if (isSelected) {
                                    MaterialTheme.colorScheme.onSecondaryContainer
                                } else {
                                    MaterialTheme.colorScheme.onSurfaceVariant
                                },
                                textAlign = TextAlign.Center,
                                maxLines = 1
                            )
                        }
                    }
                }
            }
        }

        // Tile 2: Interactive Scrubber Tooltip Readout
        ExpressiveGroupTile(index = 2, count = 3) {
            Column(modifier = Modifier.padding(horizontal = 18.dp, vertical = 14.dp)) {
                Surface(
                    shape = RoundedCornerShape(16.dp),
                    color = MaterialTheme.colorScheme.surfaceContainer,
                    modifier = Modifier
                        .fillMaxWidth()
                        .testTag("telemetry_selected_tooltip")
                ) {
                    Column(modifier = Modifier.padding(horizontal = 14.dp, vertical = 12.dp)) {
                        Row(
                            modifier = Modifier.fillMaxWidth(),
                            horizontalArrangement = Arrangement.SpaceBetween,
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            Surface(
                                shape = CircleShape,
                                color = MaterialTheme.colorScheme.primaryContainer
                            ) {
                                Text(
                                    text = stringResource(R.string.telemetry_tooltip_slot, activeBucket.label),
                                    style = MaterialTheme.typography.labelMedium,
                                    color = MaterialTheme.colorScheme.onPrimaryContainer,
                                    modifier = Modifier.padding(horizontal = 10.dp, vertical = 3.dp)
                                )
                            }
                            Text(
                                text = stringResource(
                                    R.string.telemetry_tooltip_rate,
                                    String.format(Locale.US, "%.1f%%", activeBucket.connectionSuccessRate),
                                    activeBucket.connectionSucceeded,
                                    activeBucket.connectionAttempts
                                ),
                                fontFamily = JetBrainsMonoFamily,
                                fontSize = 12.sp,
                                fontWeight = FontWeight.SemiBold,
                                color = successLineColor
                            )
                        }
                        Spacer(modifier = Modifier.height(6.dp))
                        Text(
                            text = stringResource(
                                R.string.telemetry_tooltip_volume,
                                formatByteSize(activeBucket.totalBytes),
                                formatByteSize(activeBucket.uploadBytes),
                                formatByteSize(activeBucket.downloadBytes)
                            ),
                            fontFamily = JetBrainsMonoFamily,
                            fontSize = 11.5.sp,
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
                    }
                }
                Spacer(modifier = Modifier.height(8.dp))
                Text(
                    text = stringResource(
                        R.string.telemetry_hint_interactive,
                        telemetry.verifiedSegmentsCount
                    ),
                    style = MaterialTheme.typography.bodySmall,
                    fontSize = 11.5.sp,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
            }
        }
    }
}

@Composable
private fun InteractiveTelemetryPlot(
    buckets: List<TelemetryBucketSnapshot>,
    maxBucketBytes: Long,
    mode: TelemetryChartMode,
    selectedIndex: Int,
    onSelectIndex: (Int) -> Unit,
    successLineColor: Color,
    uploadBarColor: Color,
    downloadBarColor: Color
) {
    val gridColor = MaterialTheme.colorScheme.outlineVariant.copy(alpha = 0.45f)
    val highlightBandColor = MaterialTheme.colorScheme.secondaryContainer.copy(alpha = 0.42f)
    val nodeCenterColor = MaterialTheme.colorScheme.surfaceContainerLow

    // Smoothly animate each bucket's metrics when live transfers or connection states update
    val animatedRates = buckets.map { bucket ->
        animateFloatAsState(
            targetValue = (bucket.connectionSuccessRate / 100f).coerceIn(0f, 1f),
            animationSpec = tween(durationMillis = 420, easing = FastOutSlowInEasing),
            label = "rate_${bucket.label}"
        ).value
    }
    val animatedUploadFracs = buckets.map { bucket ->
        animateFloatAsState(
            targetValue = (bucket.uploadBytes.toFloat() / maxBucketBytes.toFloat()).coerceIn(0f, 1f),
            animationSpec = tween(durationMillis = 420, easing = FastOutSlowInEasing),
            label = "up_${bucket.label}"
        ).value
    }
    val animatedDownloadFracs = buckets.map { bucket ->
        animateFloatAsState(
            targetValue = (bucket.downloadBytes.toFloat() / maxBucketBytes.toFloat()).coerceIn(0f, 1f),
            animationSpec = tween(durationMillis = 420, easing = FastOutSlowInEasing),
            label = "down_${bucket.label}"
        ).value
    }

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .height(196.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        // Left Y-axis ticks
        Column(
            modifier = Modifier
                .fillMaxHeight()
                .padding(vertical = 6.dp),
            verticalArrangement = Arrangement.SpaceBetween,
            horizontalAlignment = Alignment.End
        ) {
            val topLabel = if (mode == TelemetryChartMode.THROUGHPUT) {
                formatCompactBytes(maxBucketBytes)
            } else {
                "100%"
            }
            val midLabel = if (mode == TelemetryChartMode.THROUGHPUT) {
                formatCompactBytes(maxBucketBytes / 2L)
            } else {
                "50%"
            }
            val botLabel = if (mode == TelemetryChartMode.THROUGHPUT) "0 B" else "0%"

            AxisTickText(topLabel)
            AxisTickText(midLabel)
            AxisTickText(botLabel)
        }

        Spacer(modifier = Modifier.width(8.dp))

        Canvas(
            modifier = Modifier
                .weight(1f)
                .fillMaxHeight()
                .clip(RoundedCornerShape(16.dp))
                .background(MaterialTheme.colorScheme.surfaceContainerLowest.copy(alpha = 0.55f))
                .pointerInput(buckets.size) {
                    detectTapGestures { pos ->
                        if (size.width > 0 && buckets.isNotEmpty()) {
                            val slotWidth = size.width.toFloat() / buckets.size.toFloat()
                            val idx = (pos.x / slotWidth).toInt().coerceIn(0, buckets.lastIndex)
                            onSelectIndex(idx)
                        }
                    }
                }
                .pointerInput(buckets.size) {
                    detectHorizontalDragGestures { change, _ ->
                        change.consume()
                        if (size.width > 0 && buckets.isNotEmpty()) {
                            val slotWidth = size.width.toFloat() / buckets.size.toFloat()
                            val idx = (change.position.x / slotWidth).toInt().coerceIn(0, buckets.lastIndex)
                            onSelectIndex(idx)
                        }
                    }
                }
                .testTag("telemetry_chart_canvas")
        ) {
            val w = size.width
            val h = size.height
            val padTop = 16.dp.toPx()
            val padBottom = 14.dp.toPx()
            val plotH = (h - padTop - padBottom).coerceAtLeast(1f)
            val count = buckets.size
            val slotW = w / count.toFloat()
            val dashEffect = PathEffect.dashPathEffect(floatArrayOf(8f, 8f), 0f)

            // 1. Horizontal reference gridlines (0%, 25%, 50%, 75%, 100%)
            for (step in 0..4) {
                val y = padTop + plotH * (step / 4f)
                drawLine(
                    color = gridColor,
                    start = Offset(0f, y),
                    end = Offset(w, y),
                    strokeWidth = 1.dp.toPx(),
                    pathEffect = if (step in 1..3) dashEffect else null
                )
            }

            // 2. Active selected bucket highlight band & vertical crosshair
            if (selectedIndex in 0 until count) {
                val bandLeft = selectedIndex * slotW + slotW * 0.08f
                val bandWidth = slotW * 0.84f
                val centerX = selectedIndex * slotW + slotW * 0.5f
                drawRoundRect(
                    color = highlightBandColor,
                    topLeft = Offset(bandLeft, 4.dp.toPx()),
                    size = Size(bandWidth, h - 8.dp.toPx()),
                    cornerRadius = CornerRadius(12.dp.toPx(), 12.dp.toPx())
                )
                drawLine(
                    color = gridColor.copy(alpha = 0.85f),
                    start = Offset(centerX, padTop),
                    end = Offset(centerX, padTop + plotH),
                    strokeWidth = 1.2.dp.toPx(),
                    pathEffect = dashEffect
                )
            }

            // 3. Throughput Stacked Bars (Upload + Download)
            if (mode == TelemetryChartMode.COMBINED || mode == TelemetryChartMode.THROUGHPUT) {
                val barMaxScale = if (mode == TelemetryChartMode.COMBINED) 0.78f else 0.94f
                val barW = (slotW * 0.44f).coerceIn(10.dp.toPx(), 28.dp.toPx())
                val corner = CornerRadius(6.dp.toPx(), 6.dp.toPx())
                val gapPx = 2.dp.toPx()

                for (i in 0 until count) {
                    val cx = i * slotW + slotW * 0.5f
                    val upFrac = animatedUploadFracs[i]
                    val downFrac = animatedDownloadFracs[i]
                    val totalH = ((upFrac + downFrac).coerceAtMost(1f)) * plotH * barMaxScale
                    if (totalH <= 1f) continue

                    val sumRaw = (upFrac + downFrac).coerceAtLeast(0.0001f)
                    val downH = totalH * (downFrac / sumRaw)
                    val upH = totalH * (upFrac / sumRaw)
                    val barAlpha = if (i == selectedIndex) 0.96f else 0.76f
                    val bottomY = padTop + plotH

                    if (downH > 1f && upH > 1f) {
                        val splitGap = gapPx.coerceAtMost(totalH * 0.15f)
                        // Bottom segment: Download
                        drawRoundRect(
                            color = downloadBarColor.copy(alpha = barAlpha * 0.72f),
                            topLeft = Offset(cx - barW / 2f, bottomY - downH),
                            size = Size(barW, downH),
                            cornerRadius = corner
                        )
                        // Top segment: Upload
                        val topSegH = (upH - splitGap).coerceAtLeast(2f)
                        drawRoundRect(
                            color = uploadBarColor.copy(alpha = barAlpha),
                            topLeft = Offset(cx - barW / 2f, bottomY - downH - splitGap - topSegH),
                            size = Size(barW, topSegH),
                            cornerRadius = corner
                        )
                    } else if (upH >= downH) {
                        drawRoundRect(
                            color = uploadBarColor.copy(alpha = barAlpha),
                            topLeft = Offset(cx - barW / 2f, bottomY - totalH),
                            size = Size(barW, totalH),
                            cornerRadius = corner
                        )
                    } else {
                        drawRoundRect(
                            color = downloadBarColor.copy(alpha = barAlpha * 0.78f),
                            topLeft = Offset(cx - barW / 2f, bottomY - totalH),
                            size = Size(barW, totalH),
                            cornerRadius = corner
                        )
                    }
                }
            }

            // 4. Smooth Cubic Bézier Spline + Gradient Area for Connection Success Rate (%)
            if (mode == TelemetryChartMode.COMBINED || mode == TelemetryChartMode.SUCCESS_RATE) {
                val points = List(count) { i ->
                    val cx = i * slotW + slotW * 0.5f
                    val cy = padTop + plotH * (1f - animatedRates[i])
                    Offset(cx, cy)
                }

                if (points.size >= 2) {
                    val linePath = Path().apply {
                        moveTo(points.first().x, points.first().y)
                        for (i in 0 until points.lastIndex) {
                            val p0 = points[i]
                            val p1 = points[i + 1]
                            val controlX = (p0.x + p1.x) * 0.5f
                            cubicTo(controlX, p0.y, controlX, p1.y, p1.x, p1.y)
                        }
                    }

                    val fillPath = Path().apply {
                        addPath(linePath)
                        lineTo(points.last().x, padTop + plotH)
                        lineTo(points.first().x, padTop + plotH)
                        close()
                    }

                    val areaTopAlpha = if (mode == TelemetryChartMode.SUCCESS_RATE) 0.30f else 0.18f
                    drawPath(
                        path = fillPath,
                        brush = Brush.verticalGradient(
                            colors = listOf(
                                successLineColor.copy(alpha = areaTopAlpha),
                                successLineColor.copy(alpha = 0.01f)
                            ),
                            startY = padTop,
                            endY = padTop + plotH
                        )
                    )

                    drawPath(
                        path = linePath,
                        color = successLineColor,
                        style = Stroke(
                            width = 2.8.dp.toPx(),
                            cap = StrokeCap.Round,
                            join = StrokeJoin.Round
                        )
                    )
                }

                // Data point nodes
                points.forEachIndexed { idx, pt ->
                    val isSelected = idx == selectedIndex
                    if (isSelected) {
                        drawCircle(
                            color = successLineColor.copy(alpha = 0.25f),
                            radius = 9.dp.toPx(),
                            center = pt
                        )
                    }
                    drawCircle(
                        color = successLineColor,
                        radius = if (isSelected) 5.2.dp.toPx() else 3.8.dp.toPx(),
                        center = pt
                    )
                    drawCircle(
                        color = nodeCenterColor,
                        radius = if (isSelected) 2.4.dp.toPx() else 1.8.dp.toPx(),
                        center = pt
                    )
                }
            }
        }
    }
}

@Composable
private fun TelemetryKpiBox(
    label: String,
    value: String,
    subtitle: String,
    accentColor: Color,
    badgeBg: Color,
    icon: androidx.compose.ui.graphics.vector.ImageVector,
    modifier: Modifier = Modifier
) {
    Surface(
        shape = RoundedCornerShape(18.dp),
        color = badgeBg,
        modifier = modifier
    ) {
        Column(modifier = Modifier.padding(horizontal = 12.dp, vertical = 11.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                androidx.compose.material3.Icon(
                    imageVector = icon,
                    contentDescription = null,
                    tint = accentColor,
                    modifier = Modifier.size(15.dp)
                )
                Spacer(modifier = Modifier.width(5.dp))
                Text(
                    text = label,
                    style = MaterialTheme.typography.labelMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis
                )
            }
            Spacer(modifier = Modifier.height(4.dp))
            Text(
                text = value,
                fontFamily = JetBrainsMonoFamily,
                fontWeight = FontWeight.Bold,
                fontSize = 18.sp,
                color = accentColor,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis
            )
            Spacer(modifier = Modifier.height(2.dp))
            Text(
                text = subtitle,
                style = MaterialTheme.typography.bodySmall,
                fontSize = 11.sp,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis
            )
        }
    }
}

@Composable
private fun TelemetryModeChip(
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
            selectedContainerColor = MaterialTheme.colorScheme.primaryContainer,
            selectedLabelColor = MaterialTheme.colorScheme.onPrimaryContainer
        ),
        modifier = Modifier
            .minimumInteractiveComponentSize()
            .testTag(testTag)
    )
}

@Composable
private fun LegendDotItem(
    color: Color,
    label: String
) {
    Row(verticalAlignment = Alignment.CenterVertically) {
        Box(
            modifier = Modifier
                .size(8.dp)
                .clip(CircleShape)
                .background(color)
        )
        Spacer(modifier = Modifier.width(5.dp))
        Text(
            text = label,
            style = MaterialTheme.typography.bodySmall,
            fontSize = 11.5.sp,
            color = MaterialTheme.colorScheme.onSurfaceVariant
        )
    }
}

@Composable
private fun AxisTickText(text: String) {
    Text(
        text = text,
        fontFamily = JetBrainsMonoFamily,
        fontSize = 10.sp,
        color = MaterialTheme.colorScheme.onSurfaceVariant
    )
}

private fun formatCompactBytes(bytes: Long): String {
    val mb = bytes / (1024.0 * 1024.0)
    return if (mb >= 1.0) {
        String.format(Locale.US, "%.0fM", mb.roundToInt().coerceAtLeast(1).toDouble())
    } else {
        val kb = (bytes / 1024.0).roundToInt().coerceAtLeast(0)
        "${kb}K"
    }
}
