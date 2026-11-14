package app.contextall.ui.components

import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.animateColorAsState
import androidx.compose.animation.core.Spring
import androidx.compose.animation.core.animateDpAsState
import androidx.compose.animation.core.spring
import androidx.compose.animation.core.tween
import androidx.compose.animation.expandVertically
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.shrinkVertically
import androidx.compose.animation.slideInVertically
import androidx.compose.animation.slideOutVertically
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsPressedAsState
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBars
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.Check
import androidx.compose.material.icons.rounded.Close
import androidx.compose.material3.Badge
import androidx.compose.material3.BadgedBox
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.material3.Text
import androidx.compose.material3.minimumInteractiveComponentSize
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.Shape
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import app.contextall.R
import app.contextall.ui.theme.JetBrainsMonoFamily
import app.contextall.ui.theme.LocalExpressivePalette

/**
 * Computes Android 16 Settings signature split-card geometry:
 * 28.dp outer group corners, 6.dp inner item corners separated by a 2.dp gap,
 * morphing into a 28.dp pill when pressed or active.
 */
@Composable
fun rememberExpressiveGroupShape(
    index: Int,
    count: Int,
    highlighted: Boolean = false,
    pressed: Boolean = false
): Shape {
    val outer = 28.dp
    val inner = 6.dp
    val morphed = highlighted || pressed

    val targetTop = when {
        morphed -> outer
        count <= 1 || index == 0 -> outer
        else -> inner
    }
    val targetBottom = when {
        morphed -> outer
        count <= 1 || index == count - 1 -> outer
        else -> inner
    }

    val spec = spring<Dp>(
        dampingRatio = Spring.DampingRatioMediumBouncy,
        stiffness = Spring.StiffnessMedium
    )
    val topRadius by animateDpAsState(targetValue = targetTop, animationSpec = spec, label = "topCorner")
    val bottomRadius by animateDpAsState(targetValue = targetBottom, animationSpec = spec, label = "bottomCorner")

    return RoundedCornerShape(
        topStart = topRadius,
        topEnd = topRadius,
        bottomStart = bottomRadius,
        bottomEnd = bottomRadius
    )
}

/**
 * Clickable Android 16 M3 Expressive grouped surface tile with spring corner morphing.
 */
@Composable
fun ExpressiveGroupTile(
    index: Int,
    count: Int,
    modifier: Modifier = Modifier,
    highlighted: Boolean = false,
    containerColor: Color = MaterialTheme.colorScheme.surfaceContainerLow,
    onClick: (() -> Unit)? = null,
    content: @Composable () -> Unit
) {
    val interactionSource = remember { MutableInteractionSource() }
    val isPressed by interactionSource.collectIsPressedAsState()
    val shape = rememberExpressiveGroupShape(
        index = index,
        count = count,
        highlighted = highlighted,
        pressed = isPressed
    )

    if (onClick != null) {
        Surface(
            onClick = onClick,
            interactionSource = interactionSource,
            shape = shape,
            color = containerColor,
            modifier = modifier.fillMaxWidth(),
            content = content
        )
    } else {
        Surface(
            shape = shape,
            color = containerColor,
            modifier = modifier.fillMaxWidth(),
            content = content
        )
    }
}

/**
 * Android 16 Settings multi-tonal squircle icon badge.
 */
@Composable
fun ExpressiveIconBadge(
    icon: ImageVector,
    backgroundColor: Color,
    iconTint: Color,
    contentDescription: String? = null,
    size: Dp = 44.dp
) {
    Surface(
        shape = RoundedCornerShape(15.dp),
        color = backgroundColor,
        modifier = Modifier.size(size)
    ) {
        Box(contentAlignment = Alignment.Center) {
            Icon(
                imageVector = icon,
                contentDescription = contentDescription,
                tint = iconTint,
                modifier = Modifier.size(22.dp)
            )
        }
    }
}

/**
 * Android 16 M3 Expressive Switch with Check / Close thumb glyphs.
 */
@Composable
fun ExpressiveSwitch(
    checked: Boolean,
    onCheckedChange: (Boolean) -> Unit,
    modifier: Modifier = Modifier
) {
    Switch(
        checked = checked,
        onCheckedChange = onCheckedChange,
        thumbContent = {
            Icon(
                imageVector = if (checked) Icons.Rounded.Check else Icons.Rounded.Close,
                contentDescription = null,
                modifier = Modifier.size(SwitchDefaults.IconSize)
            )
        },
        modifier = modifier.minimumInteractiveComponentSize()
    )
}

/**
 * Android 16 M3 Expressive segmented gap LinearProgressIndicator:
 * Thick pill active bar + physical gap + rounded remaining track + end-stop dot.
 */
@Composable
fun ExpressiveGapProgressBar(
    progress: Float,
    modifier: Modifier = Modifier,
    activeColor: Color = MaterialTheme.colorScheme.primary,
    trackColor: Color = MaterialTheme.colorScheme.secondaryContainer,
    height: Dp = 10.dp
) {
    val clamped = progress.coerceIn(0f, 1f)
    Canvas(
        modifier = modifier
            .fillMaxWidth()
            .height(height)
    ) {
        val barHeight = size.height
        val radius = CornerRadius(barHeight / 2f, barHeight / 2f)
        val gapPx = 6.dp.toPx()
        val totalWidth = size.width

        if (clamped <= 0.01f) {
            drawRoundRect(
                color = trackColor,
                topLeft = Offset.Zero,
                size = Size(totalWidth, barHeight),
                cornerRadius = radius
            )
        } else if (clamped >= 0.99f) {
            drawRoundRect(
                color = activeColor,
                topLeft = Offset.Zero,
                size = Size(totalWidth, barHeight),
                cornerRadius = radius
            )
        } else {
            val activeWidth = ((totalWidth - gapPx) * clamped).coerceAtLeast(barHeight)
            val trackStart = (activeWidth + gapPx).coerceAtMost(totalWidth - barHeight)
            val trackWidth = (totalWidth - trackStart).coerceAtLeast(barHeight)

            // Active pill
            drawRoundRect(
                color = activeColor,
                topLeft = Offset.Zero,
                size = Size(activeWidth, barHeight),
                cornerRadius = radius
            )
            // Remaining track pill
            drawRoundRect(
                color = trackColor,
                topLeft = Offset(trackStart, 0f),
                size = Size(trackWidth, barHeight),
                cornerRadius = radius
            )
            // Signature M3 Expressive stop dot at end of track
            val dotRadius = (barHeight * 0.24f).coerceAtLeast(2.dp.toPx())
            drawCircle(
                color = activeColor,
                radius = dotRadius,
                center = Offset(totalWidth - barHeight / 2f, barHeight / 2f)
            )
        }
    }
}

/**
 * Android 16 Large Expressive screen header with bold display title and concise subtitle.
 */
@Composable
fun ExpressiveScreenHeader(
    title: String,
    subtitle: String,
    modifier: Modifier = Modifier,
    trailingActions: (@Composable () -> Unit)? = null
) {
    Row(
        modifier = modifier
            .fillMaxWidth()
            .padding(horizontal = 6.dp, vertical = 4.dp),
        verticalAlignment = Alignment.Top,
        horizontalArrangement = Arrangement.SpaceBetween
    ) {
        Column(modifier = Modifier.weight(1f)) {
            Text(
                text = title,
                style = MaterialTheme.typography.displaySmall,
                color = MaterialTheme.colorScheme.onBackground
            )
            Spacer(modifier = Modifier.height(4.dp))
            Text(
                text = subtitle,
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
        }
        if (trailingActions != null) {
            Spacer(modifier = Modifier.width(12.dp))
            trailingActions()
        }
    }
}

data class FloatingDockItem<T>(
    val key: T,
    val label: String,
    val selectedIcon: ImageVector,
    val unselectedIcon: ImageVector,
    val badgeCount: Int = 0,
    val testTag: String
)

/**
 * Android 16 M3 Expressive Floating Toolbar / Pill Bottom Dock.
 * Respects WindowInsets.navigationBars to avoid gesture pill overlap.
 */
@Composable
fun <T> ExpressiveFloatingNavDock(
    items: List<FloatingDockItem<T>>,
    selectedKey: T,
    onSelect: (T) -> Unit,
    modifier: Modifier = Modifier
) {
    Box(
        modifier = modifier
            .fillMaxWidth()
            .windowInsetsPadding(WindowInsets.navigationBars)
            .padding(horizontal = 10.dp, vertical = 10.dp),
        contentAlignment = Alignment.Center
    ) {
        Surface(
            shape = CircleShape,
            color = MaterialTheme.colorScheme.surfaceContainerLowest,
            tonalElevation = 6.dp,
            shadowElevation = 10.dp
        ) {
            Row(
                modifier = Modifier.padding(horizontal = 5.dp, vertical = 6.dp),
                horizontalArrangement = Arrangement.spacedBy(2.dp),
                verticalAlignment = Alignment.CenterVertically
            ) {
                items.forEach { item ->
                    val isSelected = item.key == selectedKey
                    val pillColor by animateColorAsState(
                        targetValue = if (isSelected) {
                            MaterialTheme.colorScheme.primaryContainer
                        } else {
                            Color.Transparent
                        },
                        label = "dockPillColor"
                    )
                    val contentColor by animateColorAsState(
                        targetValue = if (isSelected) {
                            MaterialTheme.colorScheme.onPrimaryContainer
                        } else {
                            MaterialTheme.colorScheme.onSurfaceVariant
                        },
                        label = "dockContentColor"
                    )
                    val horizontalPad by animateDpAsState(
                        targetValue = if (isSelected) 13.dp else 9.dp,
                        animationSpec = spring(
                            dampingRatio = Spring.DampingRatioMediumBouncy,
                            stiffness = Spring.StiffnessMediumLow
                        ),
                        label = "dockPad"
                    )

                    Surface(
                        onClick = { onSelect(item.key) },
                        shape = CircleShape,
                        color = pillColor,
                        modifier = Modifier
                            .minimumInteractiveComponentSize()
                            .testTag(item.testTag)
                    ) {
                        Row(
                            modifier = Modifier.padding(horizontal = horizontalPad, vertical = 11.dp),
                            verticalAlignment = Alignment.CenterVertically,
                            horizontalArrangement = Arrangement.Center
                        ) {
                            BadgedBox(
                                badge = {
                                    if (item.badgeCount > 0) {
                                        Badge { Text(item.badgeCount.toString()) }
                                    }
                                }
                            ) {
                                Icon(
                                    imageVector = if (isSelected) item.selectedIcon else item.unselectedIcon,
                                    contentDescription = item.label,
                                    tint = contentColor,
                                    modifier = Modifier.size(20.dp)
                                )
                            }
                            Spacer(modifier = Modifier.width(6.dp))
                            Text(
                                text = item.label,
                                style = if (isSelected) {
                                    MaterialTheme.typography.labelLarge
                                } else {
                                    MaterialTheme.typography.labelMedium
                                },
                                color = contentColor,
                                maxLines = 1,
                                softWrap = false
                            )
                        }
                    }
                }
            }
        }
    }
}

/**
 * Collapsible Android 16 Material 3 Expressive Brand Top Bar showing Logo + ContextAll.
 * Smoothly collapses upward and fades out on downward scroll or when navigating away from Devices.
 */
@Composable
fun ContextAllCollapsibleBrandBar(
    visible: Boolean,
    centeredMaxWidth: Boolean,
    showDebugBadge: Boolean = false,
    onDebugBadgeClick: () -> Unit = {},
    modifier: Modifier = Modifier
) {
    val palette = LocalExpressivePalette.current
    AnimatedVisibility(
        visible = visible,
        enter = expandVertically(
            expandFrom = Alignment.Top,
            animationSpec = spring(
                dampingRatio = Spring.DampingRatioNoBouncy,
                stiffness = Spring.StiffnessMediumLow
            )
        ) + slideInVertically(
            initialOffsetY = { -it / 2 },
            animationSpec = spring(
                dampingRatio = Spring.DampingRatioNoBouncy,
                stiffness = Spring.StiffnessMediumLow
            )
        ) + fadeIn(animationSpec = tween(durationMillis = 220)),
        exit = shrinkVertically(
            shrinkTowards = Alignment.Top,
            animationSpec = spring(
                dampingRatio = Spring.DampingRatioNoBouncy,
                stiffness = Spring.StiffnessMedium
            )
        ) + slideOutVertically(
            targetOffsetY = { -it / 2 },
            animationSpec = spring(
                dampingRatio = Spring.DampingRatioNoBouncy,
                stiffness = Spring.StiffnessMedium
            )
        ) + fadeOut(animationSpec = tween(durationMillis = 160)),
        modifier = modifier.fillMaxWidth()
    ) {
        Box(
            modifier = Modifier
                .fillMaxWidth()
                .testTag("brand_top_bar"),
            contentAlignment = if (centeredMaxWidth) Alignment.Center else Alignment.CenterStart
        ) {
            Row(
                modifier = Modifier
                    .then(
                        if (centeredMaxWidth) {
                            Modifier
                                .fillMaxWidth()
                                .widthIn(max = 640.dp)
                        } else {
                            Modifier.fillMaxWidth()
                        }
                    )
                    .padding(horizontal = 22.dp, vertical = 8.dp),
                verticalAlignment = Alignment.CenterVertically
            ) {
                ContextAllLogoMark(size = 36.dp)
                Spacer(modifier = Modifier.width(12.dp))
                Text(
                    text = stringResource(R.string.app_name),
                    fontSize = 21.sp,
                    fontWeight = FontWeight.ExtraBold,
                    letterSpacing = (-0.4).sp,
                    color = MaterialTheme.colorScheme.onBackground
                )
                if (showDebugBadge) {
                    Spacer(modifier = Modifier.width(10.dp))
                    Surface(
                        onClick = onDebugBadgeClick,
                        shape = CircleShape,
                        color = palette.amberIconBg,
                        modifier = Modifier
                            .minimumInteractiveComponentSize()
                            .testTag("brand_debug_badge")
                    ) {
                        Text(
                            text = stringResource(R.string.debug_badge_label),
                            fontFamily = JetBrainsMonoFamily,
                            fontWeight = FontWeight.Bold,
                            fontSize = 11.sp,
                            letterSpacing = 0.6.sp,
                            color = palette.amberIconFg,
                            modifier = Modifier.padding(horizontal = 10.dp, vertical = 4.dp)
                        )
                    }
                }
            }
        }
    }
}

/**
 * Crisp vector squircle rendering of ContextAll's dual-device direct-link launcher emblem.
 */
@Composable
fun ContextAllLogoMark(
    size: Dp = 36.dp,
    modifier: Modifier = Modifier
) {
    val desc = stringResource(R.string.brand_logo_content_desc)
    Surface(
        shape = RoundedCornerShape(11.dp),
        shadowElevation = 2.dp,
        color = Color.Transparent,
        modifier = modifier
            .size(size)
            .semantics { contentDescription = desc }
    ) {
        Canvas(modifier = Modifier.fillMaxSize()) {
            val w = this.size.width
            val h = this.size.height

            // Launcher gradient backdrop (#003547 -> #006684)
            drawRect(
                brush = Brush.linearGradient(
                    colors = listOf(Color(0xFF003547), Color(0xFF006684)),
                    start = Offset.Zero,
                    end = Offset(w, h)
                )
            )

            val cardCorner = CornerRadius(w * 0.08f, h * 0.08f)

            // Left peer card silhouette (#E0F7FF)
            drawRoundRect(
                color = Color(0xFFE0F7FF),
                topLeft = Offset(w * 0.18f, h * 0.19f),
                size = Size(w * 0.36f, h * 0.48f),
                cornerRadius = cardCorner
            )

            // Right peer card silhouette (#70D2FF)
            drawRoundRect(
                color = Color(0xFF70D2FF),
                topLeft = Offset(w * 0.46f, h * 0.33f),
                size = Size(w * 0.36f, h * 0.48f),
                cornerRadius = cardCorner
            )

            // Central direct-link circle (#003547)
            val center = Offset(w * 0.5f, h * 0.5f)
            drawCircle(
                color = Color(0xFF003547),
                radius = w * 0.155f,
                center = center
            )

            // Crisp link arrow inside circle (#FFFFFF)
            drawRect(
                color = Color.White,
                topLeft = Offset(w * 0.415f, h * 0.472f),
                size = Size(w * 0.14f, h * 0.056f)
            )
            val arrowHead = Path().apply {
                moveTo(w * 0.495f, h * 0.41f)
                lineTo(w * 0.595f, h * 0.50f)
                lineTo(w * 0.495f, h * 0.59f)
                close()
            }
            drawPath(path = arrowHead, color = Color.White)
        }
    }
}
