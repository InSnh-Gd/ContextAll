package app.contextall.ui.components

import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.SizeTransform
import androidx.compose.animation.animateColorAsState
import androidx.compose.animation.core.FastOutSlowInEasing
import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.Spring
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.spring
import androidx.compose.animation.core.tween
import androidx.compose.animation.expandHorizontally
import androidx.compose.animation.expandVertically
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.scaleIn
import androidx.compose.animation.scaleOut
import androidx.compose.animation.shrinkHorizontally
import androidx.compose.animation.slideInVertically
import androidx.compose.animation.slideOutVertically
import androidx.compose.animation.togetherWith
import androidx.compose.foundation.Canvas
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
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.CheckCircle
import androidx.compose.material.icons.rounded.Radar
import androidx.compose.material.icons.rounded.Refresh
import androidx.compose.material.icons.rounded.WifiOff
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.FilledTonalButton
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.minimumInteractiveComponentSize
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.scale
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import app.contextall.R
import app.contextall.ui.theme.LocalExpressivePalette

enum class DiscoveryVisualMode {
    SCANNING,
    DISCOVERED,
    IDLE_EMPTY,
    PAUSED
}

/**
 * Material 3 `CircularProgressIndicator` combined with multi-wave concentric expanding ripples
 * and smooth icon morphing when transitioning from active scanning to discovered devices.
 */
@Composable
fun DiscoveryRippleOrb(
    mode: DiscoveryVisualMode,
    modifier: Modifier = Modifier,
    orbSize: Dp = 60.dp
) {
    val palette = LocalExpressivePalette.current
    val primaryColor = MaterialTheme.colorScheme.primary
    val isScanning = mode == DiscoveryVisualMode.SCANNING

    // Smoothly ramp ripple intensity up/down so ripples subside organically when a device is found
    val rippleStrength by animateFloatAsState(
        targetValue = if (isScanning) 1f else 0f,
        animationSpec = tween(durationMillis = 450, easing = FastOutSlowInEasing),
        label = "rippleStrength"
    )

    val infiniteTransition = rememberInfiniteTransition(label = "lanDiscoveryRipples")
    val waveProgress by infiniteTransition.animateFloat(
        initialValue = 0f,
        targetValue = 1f,
        animationSpec = infiniteRepeatable(
            animation = tween(durationMillis = 2100, easing = LinearEasing),
            repeatMode = RepeatMode.Restart
        ),
        label = "waveProgress"
    )
    val corePulse by infiniteTransition.animateFloat(
        initialValue = 0.94f,
        targetValue = 1.05f,
        animationSpec = infiniteRepeatable(
            animation = tween(durationMillis = 850, easing = FastOutSlowInEasing),
            repeatMode = RepeatMode.Reverse
        ),
        label = "corePulse"
    )

    val indicatorDesc = stringResource(R.string.scan_indicator_desc)
    val coreScale = if (isScanning) corePulse else 1f

    val (badgeBgTarget, badgeFgTarget) = when (mode) {
        DiscoveryVisualMode.SCANNING -> palette.skyIconBg to palette.skyIconFg
        DiscoveryVisualMode.DISCOVERED -> palette.mintIconBg to palette.mintIconFg
        DiscoveryVisualMode.IDLE_EMPTY -> MaterialTheme.colorScheme.surfaceContainerHigh to MaterialTheme.colorScheme.onSurfaceVariant
        DiscoveryVisualMode.PAUSED -> MaterialTheme.colorScheme.errorContainer to MaterialTheme.colorScheme.onErrorContainer
    }
    val badgeBg by animateColorAsState(targetValue = badgeBgTarget, label = "scannerBadgeBg")
    val badgeFg by animateColorAsState(targetValue = badgeFgTarget, label = "scannerBadgeFg")

    Box(
        modifier = modifier
            .size(orbSize)
            .semantics { contentDescription = indicatorDesc }
            .testTag("discovery_ripple_orb"),
        contentAlignment = Alignment.Center
    ) {
        // 3-layer concentric expanding sonar ripples
        if (rippleStrength > 0.01f) {
            Canvas(modifier = Modifier.fillMaxSize()) {
                val maxRadius = size.minDimension / 2f
                val minRadius = maxRadius * 0.42f
                val waveCount = 3

                for (i in 0 until waveCount) {
                    val phase = (waveProgress + i.toFloat() / waveCount.toFloat()) % 1f
                    // Ease-out radius expansion and quadratic alpha fade
                    val easedPhase = 1f - (1f - phase) * (1f - phase)
                    val radius = minRadius + (maxRadius - minRadius) * easedPhase
                    val fade = (1f - phase) * (1f - phase) * rippleStrength

                    drawCircle(
                        color = primaryColor.copy(alpha = (0.16f * fade).coerceIn(0f, 1f)),
                        radius = radius
                    )
                    drawCircle(
                        color = primaryColor.copy(alpha = (0.52f * fade).coerceIn(0f, 1f)),
                        radius = radius,
                        style = Stroke(width = 1.6.dp.toPx())
                    )
                }
            }
        }

        // Material 3 CircularProgressIndicator ring during active scan
        AnimatedVisibility(
            visible = isScanning,
            enter = scaleIn(
                initialScale = 0.78f,
                animationSpec = spring(
                    dampingRatio = Spring.DampingRatioMediumBouncy,
                    stiffness = Spring.StiffnessMedium
                )
            ) + fadeIn(animationSpec = tween(220)),
            exit = scaleOut(
                targetScale = 0.84f,
                animationSpec = tween(220)
            ) + fadeOut(animationSpec = tween(200))
        ) {
            CircularProgressIndicator(
                modifier = Modifier
                    .size(orbSize * 0.74f)
                    .testTag("discovery_circular_progress"),
                color = primaryColor,
                trackColor = MaterialTheme.colorScheme.primaryContainer.copy(alpha = 0.45f),
                strokeWidth = 3.dp,
                strokeCap = StrokeCap.Round
            )
        }

        // Central tactile badge with smooth icon morph
        Surface(
            shape = CircleShape,
            color = badgeBg,
            modifier = Modifier
                .size(orbSize * 0.54f)
                .scale(coreScale)
        ) {
            Box(contentAlignment = Alignment.Center) {
                AnimatedContent(
                    targetState = mode,
                    transitionSpec = {
                        (scaleIn(
                            initialScale = 0.65f,
                            animationSpec = spring(
                                dampingRatio = Spring.DampingRatioMediumBouncy,
                                stiffness = Spring.StiffnessMedium
                            )
                        ) + fadeIn(tween(180))) togetherWith
                            (scaleOut(targetScale = 0.7f, animationSpec = tween(140)) + fadeOut(tween(140)))
                    },
                    label = "scannerCenterIcon"
                ) { targetMode ->
                    val icon = when (targetMode) {
                        DiscoveryVisualMode.SCANNING -> Icons.Rounded.Radar
                        DiscoveryVisualMode.DISCOVERED -> Icons.Rounded.CheckCircle
                        DiscoveryVisualMode.IDLE_EMPTY -> Icons.Rounded.Radar
                        DiscoveryVisualMode.PAUSED -> Icons.Rounded.WifiOff
                    }
                    Icon(
                        imageVector = icon,
                        contentDescription = null,
                        tint = badgeFg,
                        modifier = Modifier.size(orbSize * 0.30f)
                    )
                }
            }
        }
    }
}

/**
 * Android 16 Material 3 Expressive Nearby Discovery Scanner Card.
 * Showcases `CircularProgressIndicator` + concentric ripples during scanning and smoothly
 * crossfades status text & count when devices are discovered.
 */
@Composable
fun NearbyDiscoveryScannerCard(
    isScanning: Boolean,
    lanReady: Boolean,
    nearbyCount: Int,
    onTriggerScan: () -> Unit,
    modifier: Modifier = Modifier
) {
    val mode = when {
        !lanReady -> DiscoveryVisualMode.PAUSED
        isScanning -> DiscoveryVisualMode.SCANNING
        nearbyCount > 0 -> DiscoveryVisualMode.DISCOVERED
        else -> DiscoveryVisualMode.IDLE_EMPTY
    }

    val cardColor by animateColorAsState(
        targetValue = if (mode == DiscoveryVisualMode.SCANNING) {
            MaterialTheme.colorScheme.surfaceContainer
        } else {
            MaterialTheme.colorScheme.surfaceContainerLow
        },
        animationSpec = tween(300),
        label = "scannerCardBg"
    )

    Surface(
        onClick = {
            if (lanReady && !isScanning) {
                onTriggerScan()
            }
        },
        shape = RoundedCornerShape(26.dp),
        color = cardColor,
        modifier = modifier
            .fillMaxWidth()
            .testTag("nearby_scanner_card")
    ) {
        Row(
            modifier = Modifier.padding(horizontal = 14.dp, vertical = 12.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            DiscoveryRippleOrb(
                mode = mode,
                orbSize = 58.dp
            )

            Spacer(modifier = Modifier.width(12.dp))

            AnimatedContent(
                targetState = Pair(mode, nearbyCount),
                transitionSpec = {
                    (slideInVertically(
                        initialOffsetY = { it / 3 },
                        animationSpec = spring(
                            dampingRatio = Spring.DampingRatioNoBouncy,
                            stiffness = Spring.StiffnessMediumLow
                        )
                    ) + fadeIn(tween(220))) togetherWith
                        (slideOutVertically(
                            targetOffsetY = { -it / 3 },
                            animationSpec = tween(160)
                        ) + fadeOut(tween(150))) using SizeTransform(clip = false)
                },
                modifier = Modifier.weight(1f),
                label = "scannerTextTransition"
            ) { (currentMode, count) ->
                val (title, subtitle) = when (currentMode) {
                    DiscoveryVisualMode.SCANNING ->
                        stringResource(R.string.scan_status_scanning_title) to
                            stringResource(R.string.scan_status_scanning_sub)
                    DiscoveryVisualMode.DISCOVERED ->
                        stringResource(R.string.scan_status_found_title, count) to
                            stringResource(R.string.scan_status_found_sub)
                    DiscoveryVisualMode.IDLE_EMPTY ->
                        stringResource(R.string.scan_status_empty_title) to
                            stringResource(R.string.scan_status_empty_sub)
                    DiscoveryVisualMode.PAUSED ->
                        stringResource(R.string.scan_status_paused_title) to
                            stringResource(R.string.scan_status_paused_sub)
                }
                Column {
                    Text(
                        text = title,
                        style = MaterialTheme.typography.titleSmall,
                        color = MaterialTheme.colorScheme.onSurface
                    )
                    Spacer(modifier = Modifier.height(2.dp))
                    Text(
                        text = subtitle,
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                }
            }

            Spacer(modifier = Modifier.width(10.dp))

            FilledTonalButton(
                onClick = onTriggerScan,
                enabled = lanReady && !isScanning,
                shape = CircleShape,
                contentPadding = PaddingValues(horizontal = 14.dp, vertical = 10.dp),
                modifier = Modifier
                    .minimumInteractiveComponentSize()
                    .testTag("scan_nearby_button")
            ) {
                Icon(
                    imageVector = Icons.Rounded.Refresh,
                    contentDescription = null,
                    modifier = Modifier.size(16.dp)
                )
                Spacer(modifier = Modifier.width(6.dp))
                Text(
                    text = stringResource(
                        if (isScanning) R.string.scan_action_scanning
                        else R.string.scan_action_rescan
                    ),
                    style = MaterialTheme.typography.labelLarge
                )
            }
        }
    }
}

/**
 * Smooth spring entrance wrapper for newly discovered devices so arriving peers
 * expand, slide, scale, and fade into the grouped stack without jarring jumps.
 */
@Composable
fun DiscoveredDeviceEntranceItem(
    animateEntrance: Boolean,
    onEntered: () -> Unit,
    content: @Composable () -> Unit
) {
    var visible by remember { mutableStateOf(!animateEntrance) }

    LaunchedEffect(animateEntrance) {
        if (animateEntrance && !visible) {
            visible = true
            onEntered()
        }
    }

    AnimatedVisibility(
        visible = visible,
        enter = expandVertically(
            expandFrom = Alignment.Top,
            animationSpec = spring(
                dampingRatio = Spring.DampingRatioNoBouncy,
                stiffness = Spring.StiffnessMediumLow
            )
        ) + slideInVertically(
            initialOffsetY = { it / 3 },
            animationSpec = spring(
                dampingRatio = Spring.DampingRatioLowBouncy,
                stiffness = Spring.StiffnessMediumLow
            )
        ) + scaleIn(
            initialScale = 0.95f,
            animationSpec = spring(
                dampingRatio = Spring.DampingRatioMediumBouncy,
                stiffness = Spring.StiffnessMediumLow
            )
        ) + fadeIn(animationSpec = tween(durationMillis = 260))
    ) {
        content()
    }
}

/**
 * Animated pill badge marking a freshly discovered peer during its entrance window.
 */
@Composable
fun NewlyDiscoveredPill(visible: Boolean) {
    val palette = LocalExpressivePalette.current
    AnimatedVisibility(
        visible = visible,
        enter = expandHorizontally(expandFrom = Alignment.Start) + fadeIn(tween(200)),
        exit = shrinkHorizontally(shrinkTowards = Alignment.Start) + fadeOut(tween(200))
    ) {
        Surface(
            shape = CircleShape,
            color = palette.skyIconBg
        ) {
            Text(
                text = stringResource(R.string.device_newly_discovered_badge),
                style = MaterialTheme.typography.labelMedium,
                color = palette.skyIconFg,
                modifier = Modifier.padding(horizontal = 9.dp, vertical = 3.dp)
            )
        }
    }
}
