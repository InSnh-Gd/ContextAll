package app.contextall.ui.screens

import androidx.activity.compose.BackHandler
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.animateColorAsState
import androidx.compose.animation.core.FastOutSlowInEasing
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.Spring
import androidx.compose.animation.core.animateDpAsState
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.spring
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.foundation.background
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
import androidx.compose.foundation.layout.statusBars
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.foundation.pager.HorizontalPager
import androidx.compose.foundation.pager.rememberPagerState
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.rounded.ArrowBack
import androidx.compose.material.icons.automirrored.rounded.ArrowForward
import androidx.compose.material.icons.rounded.Check
import androidx.compose.material.icons.rounded.ContentPaste
import androidx.compose.material.icons.rounded.DesktopWindows
import androidx.compose.material.icons.rounded.DownloadDone
import androidx.compose.material.icons.rounded.FolderZip
import androidx.compose.material.icons.rounded.Password
import androidx.compose.material.icons.rounded.Science
import androidx.compose.material.icons.rounded.Smartphone
import androidx.compose.material.icons.rounded.SyncAlt
import androidx.compose.material.icons.rounded.Timer
import androidx.compose.material.icons.rounded.WifiTethering
import androidx.compose.material3.Button
import androidx.compose.material3.FilledTonalButton
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
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import app.contextall.R
import app.contextall.ui.components.ExpressiveGapProgressBar
import app.contextall.ui.components.ExpressiveGroupTile
import app.contextall.ui.components.ExpressiveIconBadge
import app.contextall.ui.components.ExpressiveSwitch
import app.contextall.ui.theme.JetBrainsMonoFamily
import app.contextall.ui.theme.LocalExpressivePalette
import kotlinx.coroutines.launch

/**
 * Android 16 (Material 3 Expressive) interactive first-launch and manual-reopen User Guide.
 */
@Composable
fun OnboardingGuideOverlay(
    visible: Boolean,
    onDismiss: () -> Unit,
    onDismissAndOpenLab: () -> Unit
) {
    AnimatedVisibility(
        visible = visible,
        enter = fadeIn(animationSpec = tween(220)),
        exit = fadeOut(animationSpec = tween(180))
    ) {
        val totalSteps = 4
        val pagerState = rememberPagerState(pageCount = { totalSteps })
        val scope = rememberCoroutineScope()
        val colorScheme = MaterialTheme.colorScheme

        BackHandler {
            if (pagerState.currentPage > 0) {
                scope.launch {
                    pagerState.animateScrollToPage(pagerState.currentPage - 1)
                }
            } else {
                onDismiss()
            }
        }

        Surface(
            color = colorScheme.background,
            modifier = Modifier
                .fillMaxSize()
                .drawBehind {
                    drawCircle(
                        brush = Brush.radialGradient(
                            colors = listOf(
                                colorScheme.primaryContainer.copy(alpha = 0.42f),
                                Color.Transparent
                            ),
                            center = Offset(size.width * 0.85f, size.height * 0.14f),
                            radius = size.minDimension * 0.72f
                        ),
                        radius = size.minDimension * 0.72f,
                        center = Offset(size.width * 0.85f, size.height * 0.14f)
                    )
                    drawCircle(
                        brush = Brush.radialGradient(
                            colors = listOf(
                                colorScheme.tertiaryContainer.copy(alpha = 0.30f),
                                Color.Transparent
                            ),
                            center = Offset(size.width * 0.12f, size.height * 0.82f),
                            radius = size.minDimension * 0.65f
                        ),
                        radius = size.minDimension * 0.65f,
                        center = Offset(size.width * 0.12f, size.height * 0.82f)
                    )
                }
                .testTag("onboarding_guide_root")
        ) {
            Box(
                modifier = Modifier
                    .fillMaxSize()
                    .windowInsetsPadding(WindowInsets.statusBars)
                    .windowInsetsPadding(WindowInsets.navigationBars),
                contentAlignment = Alignment.Center
            ) {
                Column(
                    modifier = Modifier
                        .fillMaxSize()
                        .widthIn(max = 580.dp)
                        .padding(horizontal = 20.dp, vertical = 12.dp),
                    verticalArrangement = Arrangement.SpaceBetween
                ) {
                    // Top Bar: Step counter pill + Skip button
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.SpaceBetween,
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Surface(
                            shape = CircleShape,
                            color = colorScheme.surfaceContainerHigh
                        ) {
                            Text(
                                text = stringResource(
                                    R.string.guide_badge_step,
                                    pagerState.currentPage + 1,
                                    totalSteps
                                ),
                                style = MaterialTheme.typography.labelLarge,
                                color = colorScheme.onSurfaceVariant,
                                modifier = Modifier.padding(horizontal = 14.dp, vertical = 8.dp)
                            )
                        }

                        TextButton(
                            onClick = onDismiss,
                            modifier = Modifier
                                .minimumInteractiveComponentSize()
                                .testTag("onboarding_skip_button")
                        ) {
                            Text(
                                text = stringResource(R.string.guide_skip_button),
                                style = MaterialTheme.typography.labelLarge
                            )
                        }
                    }

                    // Pager Body
                    HorizontalPager(
                        state = pagerState,
                        modifier = Modifier
                            .weight(1f)
                            .fillMaxWidth()
                    ) { page ->
                        Column(
                            modifier = Modifier
                                .fillMaxSize()
                                .verticalScroll(rememberScrollState())
                                .padding(vertical = 12.dp),
                            verticalArrangement = Arrangement.Center
                        ) {
                            when (page) {
                                0 -> GuideStepLanQuic()
                                1 -> GuideStepPairingCode()
                                2 -> GuideStepPoliciesInteractive()
                                else -> GuideStepSegmentedTransfer()
                            }
                        }
                    }

                    // Bottom Indicator + Navigation Controls
                    Column(
                        modifier = Modifier.fillMaxWidth(),
                        verticalArrangement = Arrangement.spacedBy(14.dp)
                    ) {
                        ExpressiveStepDots(
                            currentStep = pagerState.currentPage,
                            totalSteps = totalSteps,
                            modifier = Modifier.align(Alignment.CenterHorizontally)
                        )

                        val isLastPage = pagerState.currentPage == totalSteps - 1
                        if (isLastPage) {
                            Button(
                                onClick = onDismiss,
                                shape = CircleShape,
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .height(54.dp)
                                    .testTag("onboarding_finish_button")
                            ) {
                                Icon(
                                    imageVector = Icons.Rounded.Check,
                                    contentDescription = null,
                                    modifier = Modifier.size(20.dp)
                                )
                                Spacer(modifier = Modifier.width(8.dp))
                                Text(
                                    text = stringResource(R.string.guide_finish_button),
                                    style = MaterialTheme.typography.titleSmall
                                )
                            }
                        } else {
                            Row(
                                modifier = Modifier.fillMaxWidth(),
                                horizontalArrangement = Arrangement.spacedBy(12.dp),
                                verticalAlignment = Alignment.CenterVertically
                            ) {
                                if (pagerState.currentPage > 0) {
                                    OutlinedButton(
                                        onClick = {
                                            scope.launch {
                                                pagerState.animateScrollToPage(pagerState.currentPage - 1)
                                            }
                                        },
                                        shape = CircleShape,
                                        modifier = Modifier
                                            .height(52.dp)
                                            .minimumInteractiveComponentSize()
                                            .testTag("onboarding_prev_button")
                                    ) {
                                        Icon(
                                            imageVector = Icons.AutoMirrored.Rounded.ArrowBack,
                                            contentDescription = stringResource(R.string.guide_prev_button),
                                            modifier = Modifier.size(18.dp)
                                        )
                                        Spacer(modifier = Modifier.width(6.dp))
                                        Text(text = stringResource(R.string.guide_prev_button))
                                    }
                                }

                                Button(
                                    onClick = {
                                        scope.launch {
                                            pagerState.animateScrollToPage(pagerState.currentPage + 1)
                                        }
                                    },
                                    shape = CircleShape,
                                    modifier = Modifier
                                        .weight(1f)
                                        .height(52.dp)
                                        .testTag("onboarding_next_button")
                                ) {
                                    Text(
                                        text = stringResource(R.string.guide_next_button),
                                        style = MaterialTheme.typography.titleSmall
                                    )
                                    Spacer(modifier = Modifier.width(8.dp))
                                    Icon(
                                        imageVector = Icons.AutoMirrored.Rounded.ArrowForward,
                                        contentDescription = null,
                                        modifier = Modifier.size(18.dp)
                                    )
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun ExpressiveStepDots(
    currentStep: Int,
    totalSteps: Int,
    modifier: Modifier = Modifier
) {
    Row(
        modifier = modifier,
        horizontalArrangement = Arrangement.spacedBy(8.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        repeat(totalSteps) { index ->
            val selected = index == currentStep
            val dotWidth by animateDpAsState(
                targetValue = if (selected) 32.dp else 10.dp,
                animationSpec = spring(
                    dampingRatio = Spring.DampingRatioMediumBouncy,
                    stiffness = Spring.StiffnessMedium
                ),
                label = "stepDotWidth"
            )
            val dotColor by animateColorAsState(
                targetValue = if (selected) {
                    MaterialTheme.colorScheme.primary
                } else {
                    MaterialTheme.colorScheme.surfaceContainerHighest
                },
                label = "stepDotColor"
            )
            Box(
                modifier = Modifier
                    .height(10.dp)
                    .width(dotWidth)
                    .background(color = dotColor, shape = CircleShape)
            )
        }
    }
}

@Composable
private fun StepHeaderBlock(
    tagText: String,
    titleText: String,
    descText: String,
    accentBg: Color,
    accentFg: Color
) {
    Surface(
        shape = CircleShape,
        color = accentBg
    ) {
        Text(
            text = tagText,
            style = MaterialTheme.typography.labelLarge,
            color = accentFg,
            modifier = Modifier.padding(horizontal = 14.dp, vertical = 6.dp)
        )
    }
    Spacer(modifier = Modifier.height(14.dp))
    Text(
        text = titleText,
        style = MaterialTheme.typography.headlineLarge,
        color = MaterialTheme.colorScheme.onBackground
    )
    Spacer(modifier = Modifier.height(10.dp))
    Text(
        text = descText,
        style = MaterialTheme.typography.bodyLarge,
        color = MaterialTheme.colorScheme.onSurfaceVariant,
        lineHeight = 24.sp
    )
}

@Composable
private fun GuideStepLanQuic() {
    val palette = LocalExpressivePalette.current
    Column(modifier = Modifier.fillMaxWidth()) {
        // Android 16 Expressive Hero Illustration Stage
        ExpressiveGroupTile(
            index = 0,
            count = 1,
            containerColor = MaterialTheme.colorScheme.surfaceContainerLow
        ) {
            Column(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(22.dp),
                horizontalAlignment = Alignment.CenterHorizontally
            ) {
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceEvenly,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Column(horizontalAlignment = Alignment.CenterHorizontally) {
                        ExpressiveIconBadge(
                            icon = Icons.Rounded.DesktopWindows,
                            backgroundColor = palette.skyIconBg,
                            iconTint = palette.skyIconFg,
                            size = 58.dp
                        )
                        Spacer(modifier = Modifier.height(8.dp))
                        Text(
                            text = stringResource(R.string.guide_step1_node_desktop),
                            style = MaterialTheme.typography.labelMedium
                        )
                    }

                    Surface(
                        shape = CircleShape,
                        color = palette.mintIconBg
                    ) {
                        Row(
                            modifier = Modifier.padding(horizontal = 14.dp, vertical = 8.dp),
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            Icon(
                                imageVector = Icons.Rounded.WifiTethering,
                                contentDescription = null,
                                tint = palette.mintIconFg,
                                modifier = Modifier.size(18.dp)
                            )
                            Spacer(modifier = Modifier.width(6.dp))
                            Icon(
                                imageVector = Icons.Rounded.SyncAlt,
                                contentDescription = null,
                                tint = palette.mintIconFg,
                                modifier = Modifier.size(18.dp)
                            )
                        }
                    }

                    Column(horizontalAlignment = Alignment.CenterHorizontally) {
                        ExpressiveIconBadge(
                            icon = Icons.Rounded.Smartphone,
                            backgroundColor = palette.violetIconBg,
                            iconTint = palette.violetIconFg,
                            size = 58.dp
                        )
                        Spacer(modifier = Modifier.height(8.dp))
                        Text(
                            text = stringResource(R.string.guide_step1_node_android),
                            style = MaterialTheme.typography.labelMedium
                        )
                    }
                }

                Spacer(modifier = Modifier.height(18.dp))
                Surface(
                    shape = CircleShape,
                    color = MaterialTheme.colorScheme.surfaceContainerHigh
                ) {
                    Text(
                        text = stringResource(R.string.guide_step1_pill),
                        fontFamily = JetBrainsMonoFamily,
                        fontSize = 12.sp,
                        color = MaterialTheme.colorScheme.onSurface,
                        modifier = Modifier.padding(horizontal = 14.dp, vertical = 6.dp)
                    )
                }
            }
        }

        Spacer(modifier = Modifier.height(24.dp))
        StepHeaderBlock(
            tagText = stringResource(R.string.guide_step1_tag),
            titleText = stringResource(R.string.guide_step1_title),
            descText = stringResource(R.string.guide_step1_desc),
            accentBg = palette.skyIconBg,
            accentFg = palette.skyIconFg
        )
    }
}

@Composable
private fun GuideStepPairingCode() {
    val palette = LocalExpressivePalette.current
    val sampleChars = listOf('K', '7', 'M', '4', 'Q', '9')

    Column(modifier = Modifier.fillMaxWidth()) {
        ExpressiveGroupTile(
            index = 0,
            count = 1,
            containerColor = MaterialTheme.colorScheme.surfaceContainerLow
        ) {
            Column(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(20.dp),
                horizontalAlignment = Alignment.CenterHorizontally
            ) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    ExpressiveIconBadge(
                        icon = Icons.Rounded.Password,
                        backgroundColor = palette.violetIconBg,
                        iconTint = palette.violetIconFg,
                        size = 38.dp
                    )
                    Spacer(modifier = Modifier.width(10.dp))
                    Text(
                        text = stringResource(R.string.guide_step2_demo_peer),
                        style = MaterialTheme.typography.titleSmall
                    )
                }

                Spacer(modifier = Modifier.height(16.dp))
                Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                    sampleChars.forEachIndexed { idx, ch ->
                        Surface(
                            shape = RoundedCornerShape(14.dp),
                            color = if (idx < 3) palette.violetIconBg else palette.skyIconBg
                        ) {
                            Box(
                                modifier = Modifier.size(width = 40.dp, height = 48.dp),
                                contentAlignment = Alignment.Center
                            ) {
                                Text(
                                    text = ch.toString(),
                                    fontFamily = JetBrainsMonoFamily,
                                    fontWeight = FontWeight.Bold,
                                    fontSize = 20.sp,
                                    color = if (idx < 3) palette.violetIconFg else palette.skyIconFg
                                )
                            }
                        }
                    }
                }

                Spacer(modifier = Modifier.height(14.dp))
                Surface(
                    shape = CircleShape,
                    color = palette.amberIconBg
                ) {
                    Row(
                        modifier = Modifier.padding(horizontal = 12.dp, vertical = 6.dp),
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Icon(
                            imageVector = Icons.Rounded.Timer,
                            contentDescription = null,
                            tint = palette.amberIconFg,
                            modifier = Modifier.size(16.dp)
                        )
                        Spacer(modifier = Modifier.width(6.dp))
                        Text(
                            text = stringResource(R.string.guide_step2_demo_timer),
                            style = MaterialTheme.typography.labelMedium,
                            color = palette.amberIconFg
                        )
                    }
                }
            }
        }

        Spacer(modifier = Modifier.height(24.dp))
        StepHeaderBlock(
            tagText = stringResource(R.string.guide_step2_tag),
            titleText = stringResource(R.string.guide_step2_title),
            descText = stringResource(R.string.guide_step2_desc),
            accentBg = palette.violetIconBg,
            accentFg = palette.violetIconFg
        )
    }
}

@Composable
private fun GuideStepPoliciesInteractive() {
    val palette = LocalExpressivePalette.current
    var demoClipboard by remember { mutableStateOf(false) }
    var demoAskBeforeSave by remember { mutableStateOf(true) }

    Column(modifier = Modifier.fillMaxWidth()) {
        // Live interactive Android 16 split-card group so users can feel spring morphing
        Column(verticalArrangement = Arrangement.spacedBy(2.dp)) {
            ExpressiveGroupTile(
                index = 0,
                count = 2,
                highlighted = demoClipboard,
                onClick = { demoClipboard = !demoClipboard }
            ) {
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(horizontal = 18.dp, vertical = 15.dp),
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    ExpressiveIconBadge(
                        icon = Icons.Rounded.ContentPaste,
                        backgroundColor = palette.mintIconBg,
                        iconTint = palette.mintIconFg
                    )
                    Spacer(modifier = Modifier.width(14.dp))
                    Column(modifier = Modifier.weight(1f)) {
                        Text(
                            text = stringResource(R.string.guide_step3_demo_clip_title),
                            style = MaterialTheme.typography.titleSmall
                        )
                        Text(
                            text = stringResource(R.string.guide_step3_demo_clip_sub),
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
                    }
                    Spacer(modifier = Modifier.width(10.dp))
                    ExpressiveSwitch(
                        checked = demoClipboard,
                        onCheckedChange = { demoClipboard = it }
                    )
                }
            }

            ExpressiveGroupTile(
                index = 1,
                count = 2,
                onClick = { demoAskBeforeSave = !demoAskBeforeSave }
            ) {
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(horizontal = 18.dp, vertical = 15.dp),
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    ExpressiveIconBadge(
                        icon = Icons.Rounded.DownloadDone,
                        backgroundColor = palette.amberIconBg,
                        iconTint = palette.amberIconFg
                    )
                    Spacer(modifier = Modifier.width(14.dp))
                    Column(modifier = Modifier.weight(1f)) {
                        Text(
                            text = stringResource(R.string.guide_step3_demo_ask_title),
                            style = MaterialTheme.typography.titleSmall
                        )
                        Text(
                            text = stringResource(R.string.guide_step3_demo_ask_sub),
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
                    }
                    Spacer(modifier = Modifier.width(10.dp))
                    ExpressiveSwitch(
                        checked = demoAskBeforeSave,
                        onCheckedChange = { demoAskBeforeSave = it }
                    )
                }
            }
        }

        Spacer(modifier = Modifier.height(24.dp))
        StepHeaderBlock(
            tagText = stringResource(R.string.guide_step3_tag),
            titleText = stringResource(R.string.guide_step3_title),
            descText = stringResource(R.string.guide_step3_desc),
            accentBg = palette.mintIconBg,
            accentFg = palette.mintIconFg
        )
    }
}

@Composable
private fun GuideStepSegmentedTransfer() {
    val palette = LocalExpressivePalette.current
    val infiniteTransition = rememberInfiniteTransition(label = "guideTransferProgress")
    val animatedProgress by infiniteTransition.animateFloat(
        initialValue = 0.28f,
        targetValue = 0.88f,
        animationSpec = infiniteRepeatable(
            animation = tween(durationMillis = 2400, easing = FastOutSlowInEasing),
            repeatMode = RepeatMode.Reverse
        ),
        label = "demoGapBar"
    )

    Column(modifier = Modifier.fillMaxWidth()) {
        ExpressiveGroupTile(
            index = 0,
            count = 1,
            containerColor = MaterialTheme.colorScheme.surfaceContainerLow
        ) {
            Column(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(20.dp)
            ) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    ExpressiveIconBadge(
                        icon = Icons.Rounded.FolderZip,
                        backgroundColor = palette.amberIconBg,
                        iconTint = palette.amberIconFg
                    )
                    Spacer(modifier = Modifier.width(14.dp))
                    Column(modifier = Modifier.weight(1f)) {
                        Text(
                            text = stringResource(R.string.guide_step4_demo_file),
                            style = MaterialTheme.typography.titleSmall
                        )
                        Spacer(modifier = Modifier.height(2.dp))
                        Text(
                            text = stringResource(R.string.guide_step4_demo_meta),
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
                    }
                    Text(
                        text = "${(animatedProgress * 100).toInt()}%",
                        fontFamily = JetBrainsMonoFamily,
                        fontWeight = FontWeight.SemiBold,
                        fontSize = 13.sp,
                        color = MaterialTheme.colorScheme.primary
                    )
                }

                Spacer(modifier = Modifier.height(16.dp))
                ExpressiveGapProgressBar(
                    progress = animatedProgress,
                    height = 12.dp
                )
            }
        }

        Spacer(modifier = Modifier.height(24.dp))
        StepHeaderBlock(
            tagText = stringResource(R.string.guide_step4_tag),
            titleText = stringResource(R.string.guide_step4_title),
            descText = stringResource(R.string.guide_step4_desc),
            accentBg = palette.amberIconBg,
            accentFg = palette.amberIconFg
        )
    }
}
