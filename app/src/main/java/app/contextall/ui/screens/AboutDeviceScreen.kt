package app.contextall.ui.screens

import android.app.ActivityManager
import android.content.Context
import android.os.Build
import androidx.activity.compose.BackHandler
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
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.rounded.ArrowBack
import androidx.compose.material.icons.rounded.Android
import androidx.compose.material.icons.rounded.Apps
import androidx.compose.material.icons.rounded.ContentCopy
import androidx.compose.material.icons.rounded.DeveloperBoard
import androidx.compose.material.icons.rounded.DisplaySettings
import androidx.compose.material.icons.rounded.Lan
import androidx.compose.material.icons.rounded.Memory
import androidx.compose.material.icons.rounded.Numbers
import androidx.compose.material.icons.rounded.Smartphone
import androidx.compose.material.icons.rounded.Storage
import androidx.compose.material.icons.rounded.Verified
import androidx.compose.material3.FilledTonalButton
import androidx.compose.material3.FilledTonalIconButton
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.minimumInteractiveComponentSize
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import app.contextall.BuildConfig
import app.contextall.R
import app.contextall.core.formatByteSize
import app.contextall.ui.components.ContextAllLogoMark
import app.contextall.ui.components.ExpressiveGroupTile
import app.contextall.ui.components.ExpressiveIconBadge
import app.contextall.ui.theme.JetBrainsMonoFamily
import app.contextall.ui.theme.LocalExpressivePalette

private data class MachineSpecsSnapshot(
    val brandAndModel: String,
    val boardAndHardware: String,
    val androidRelease: String,
    val sdkInt: Int,
    val securityPatchSuffix: String,
    val supportedAbis: String,
    val cpuCores: Int,
    val availMemoryBytes: Long,
    val totalMemoryBytes: Long,
    val screenWidthPx: Int,
    val screenHeightPx: Int,
    val densityDpi: Int,
    val buildDisplay: String
)

@Composable
fun AboutDeviceSubScreen(
    localDeviceId: String,
    debugMode: Boolean = false,
    headerActions: @Composable () -> Unit,
    onBack: () -> Unit,
    onCopyDiagnostics: (String) -> Unit,
    onUnlockDebugMode: () -> Unit = {},
    modifier: Modifier = Modifier
) {
    BackHandler(onBack = onBack)

    val context = LocalContext.current
    val palette = LocalExpressivePalette.current
    var versionTapCount by remember { mutableIntStateOf(0) }

    val specs = remember(context) {
        readRealMachineSpecs(context)
    }

    val buildChannel = if (BuildConfig.DEBUG) "Debug" else "Release"
    val versionPillText = stringResource(
        R.string.about_version_pill,
        BuildConfig.VERSION_NAME,
        BuildConfig.VERSION_CODE,
        buildChannel
    )
    val androidVersionValue = stringResource(
        R.string.about_item_android_value,
        specs.androidRelease,
        specs.sdkInt,
        specs.securityPatchSuffix
    )
    val cpuValue = stringResource(
        R.string.about_item_cpu_value,
        specs.supportedAbis,
        specs.cpuCores
    )
    val memoryValue = stringResource(
        R.string.about_item_memory_value,
        formatByteSize(specs.availMemoryBytes),
        formatByteSize(specs.totalMemoryBytes)
    )
    val displayValue = stringResource(
        R.string.about_item_display_value,
        specs.screenWidthPx,
        specs.screenHeightPx,
        specs.densityDpi
    )

    val diagnosticsReport = remember(specs, localDeviceId) {
        buildString {
            appendLine("ContextAll v${BuildConfig.VERSION_NAME} (${BuildConfig.VERSION_CODE}, $buildChannel)")
            appendLine("Package: ${BuildConfig.APPLICATION_ID}")
            appendLine("Node ID: ${localDeviceId.take(16)}…")
            appendLine("Device: ${specs.brandAndModel} (${specs.boardAndHardware})")
            appendLine("OS: Android ${specs.androidRelease} (API ${specs.sdkInt})${specs.securityPatchSuffix}")
            appendLine("CPU: ${specs.supportedAbis} (${specs.cpuCores} cores)")
            appendLine("RAM: ${formatByteSize(specs.availMemoryBytes)} / ${formatByteSize(specs.totalMemoryBytes)}")
            append("Display: ${specs.screenWidthPx}x${specs.screenHeightPx} @ ${specs.densityDpi}dpi")
        }
    }

    Box(modifier = modifier.fillMaxSize(), contentAlignment = Alignment.TopCenter) {
        LazyColumn(
            modifier = Modifier
                .fillMaxWidth()
                .widthIn(max = 640.dp)
                .testTag("about_subpage_screen"),
            contentPadding = PaddingValues(start = 16.dp, end = 16.dp, top = 8.dp, bottom = 128.dp),
            verticalArrangement = Arrangement.spacedBy(20.dp)
        ) {
            // Top Sub-page Navigation Bar
            item {
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.SpaceBetween
                ) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        FilledTonalIconButton(
                            onClick = onBack,
                            modifier = Modifier
                                .minimumInteractiveComponentSize()
                                .testTag("about_back_button")
                        ) {
                            Icon(
                                imageVector = Icons.AutoMirrored.Rounded.ArrowBack,
                                contentDescription = stringResource(R.string.about_back_desc)
                            )
                        }
                        Spacer(modifier = Modifier.width(12.dp))
                        Text(
                            text = stringResource(R.string.about_page_title),
                            style = MaterialTheme.typography.headlineMedium
                        )
                    }
                    headerActions()
                }
            }

            // Hero App Brand + Version Card
            item {
                ExpressiveGroupTile(index = 0, count = 1) {
                    Column(
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(horizontal = 24.dp, vertical = 24.dp),
                        horizontalAlignment = Alignment.CenterHorizontally
                    ) {
                        ContextAllLogoMark(size = 64.dp)
                        Spacer(modifier = Modifier.height(14.dp))
                        Text(
                            text = stringResource(R.string.app_name),
                            style = MaterialTheme.typography.headlineMedium
                        )
                        Spacer(modifier = Modifier.height(8.dp))
                        Surface(
                            onClick = {
                                if (!debugMode) {
                                    versionTapCount += 1
                                    val requiredTaps = if (BuildConfig.DEBUG) 1 else 5
                                    if (versionTapCount >= requiredTaps) {
                                        versionTapCount = 0
                                        onUnlockDebugMode()
                                    }
                                }
                            },
                            shape = CircleShape,
                            color = MaterialTheme.colorScheme.primaryContainer,
                            modifier = Modifier
                                .minimumInteractiveComponentSize()
                                .testTag("about_version_pill")
                        ) {
                            Text(
                                text = versionPillText,
                                fontFamily = JetBrainsMonoFamily,
                                fontSize = 12.sp,
                                color = MaterialTheme.colorScheme.onPrimaryContainer,
                                modifier = Modifier.padding(horizontal = 14.dp, vertical = 5.dp)
                            )
                        }
                        Spacer(modifier = Modifier.height(10.dp))
                        Text(
                            text = stringResource(R.string.about_hero_tagline),
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                            textAlign = TextAlign.Center
                        )
                        Spacer(modifier = Modifier.height(16.dp))
                        FilledTonalButton(
                            onClick = { onCopyDiagnostics(diagnosticsReport) },
                            shape = CircleShape,
                            modifier = Modifier
                                .height(48.dp)
                                .testTag("about_copy_diagnostics_button")
                        ) {
                            Icon(
                                imageVector = Icons.Rounded.ContentCopy,
                                contentDescription = null,
                                modifier = Modifier.size(18.dp)
                            )
                            Spacer(modifier = Modifier.width(8.dp))
                            Text(stringResource(R.string.about_copy_diagnostics_btn))
                        }
                    }
                }
            }

            // Version & Build Group
            item {
                SectionHeader(text = stringResource(R.string.about_section_version))
                Spacer(modifier = Modifier.height(10.dp))
                Column(verticalArrangement = Arrangement.spacedBy(2.dp)) {
                    AboutSpecRow(
                        index = 0,
                        count = 3,
                        icon = Icons.Rounded.Verified,
                        badgeBg = palette.mintIconBg,
                        badgeFg = palette.mintIconFg,
                        label = stringResource(R.string.about_item_version_label),
                        value = "v${BuildConfig.VERSION_NAME} (versionCode ${BuildConfig.VERSION_CODE} · $buildChannel)",
                        mono = true
                    )
                    AboutSpecRow(
                        index = 1,
                        count = 3,
                        icon = Icons.Rounded.Apps,
                        badgeBg = palette.skyIconBg,
                        badgeFg = palette.skyIconFg,
                        label = stringResource(R.string.about_item_package_label),
                        value = BuildConfig.APPLICATION_ID,
                        mono = true
                    )
                    AboutSpecRow(
                        index = 2,
                        count = 3,
                        icon = Icons.Rounded.Lan,
                        badgeBg = palette.violetIconBg,
                        badgeFg = palette.violetIconFg,
                        label = stringResource(R.string.about_item_protocol_label),
                        value = stringResource(R.string.about_item_protocol_value),
                        mono = true
                    )
                }
            }

            // Machine Hardware & OS Group
            item {
                SectionHeader(text = stringResource(R.string.about_section_machine))
                Spacer(modifier = Modifier.height(10.dp))
                Column(verticalArrangement = Arrangement.spacedBy(2.dp)) {
                    AboutSpecRow(
                        index = 0,
                        count = 7,
                        icon = Icons.Rounded.Smartphone,
                        badgeBg = palette.skyIconBg,
                        badgeFg = palette.skyIconFg,
                        label = stringResource(R.string.about_item_model_label),
                        value = specs.brandAndModel
                    )
                    AboutSpecRow(
                        index = 1,
                        count = 7,
                        icon = Icons.Rounded.DeveloperBoard,
                        badgeBg = palette.violetIconBg,
                        badgeFg = palette.violetIconFg,
                        label = stringResource(R.string.about_item_board_label),
                        value = specs.boardAndHardware,
                        mono = true
                    )
                    AboutSpecRow(
                        index = 2,
                        count = 7,
                        icon = Icons.Rounded.Android,
                        badgeBg = palette.mintIconBg,
                        badgeFg = palette.mintIconFg,
                        label = stringResource(R.string.about_item_android_label),
                        value = androidVersionValue
                    )
                    AboutSpecRow(
                        index = 3,
                        count = 7,
                        icon = Icons.Rounded.Memory,
                        badgeBg = palette.amberIconBg,
                        badgeFg = palette.amberIconFg,
                        label = stringResource(R.string.about_item_cpu_label),
                        value = cpuValue,
                        mono = true
                    )
                    AboutSpecRow(
                        index = 4,
                        count = 7,
                        icon = Icons.Rounded.Storage,
                        badgeBg = palette.skyIconBg,
                        badgeFg = palette.skyIconFg,
                        label = stringResource(R.string.about_item_memory_label),
                        value = memoryValue,
                        mono = true
                    )
                    AboutSpecRow(
                        index = 5,
                        count = 7,
                        icon = Icons.Rounded.DisplaySettings,
                        badgeBg = palette.violetIconBg,
                        badgeFg = palette.violetIconFg,
                        label = stringResource(R.string.about_item_display_label),
                        value = displayValue,
                        mono = true
                    )
                    AboutSpecRow(
                        index = 6,
                        count = 7,
                        icon = Icons.Rounded.Numbers,
                        badgeBg = palette.mintIconBg,
                        badgeFg = palette.mintIconFg,
                        label = stringResource(R.string.about_item_build_label),
                        value = specs.buildDisplay,
                        mono = true
                    )
                }
            }
        }
    }
}

@Composable
private fun AboutSpecRow(
    index: Int,
    count: Int,
    icon: ImageVector,
    badgeBg: Color,
    badgeFg: Color,
    label: String,
    value: String,
    mono: Boolean = false
) {
    ExpressiveGroupTile(index = index, count = count) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = 18.dp, vertical = 14.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            ExpressiveIconBadge(
                icon = icon,
                backgroundColor = badgeBg,
                iconTint = badgeFg
            )
            Spacer(modifier = Modifier.width(16.dp))
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    text = label,
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
                Spacer(modifier = Modifier.height(3.dp))
                Text(
                    text = value,
                    fontFamily = if (mono) JetBrainsMonoFamily else null,
                    fontSize = if (mono) 12.5.sp else 15.sp,
                    style = if (mono) MaterialTheme.typography.bodyMedium else MaterialTheme.typography.titleSmall,
                    color = MaterialTheme.colorScheme.onSurface
                )
            }
        }
    }
}

private fun readRealMachineSpecs(context: Context): MachineSpecsSnapshot {
    val manufacturer = Build.MANUFACTURER?.trim().orEmpty().replaceFirstChar {
        if (it.isLowerCase()) it.titlecase() else it.toString()
    }
    val model = Build.MODEL?.trim().orEmpty()
    val brandModel = listOf(manufacturer, model)
        .filter { it.isNotEmpty() }
        .distinct()
        .joinToString(" · ")
        .ifEmpty { "Android Device" }

    val board = Build.BOARD?.trim().orEmpty().ifEmpty { "unknown" }
    val hardware = Build.HARDWARE?.trim().orEmpty().ifEmpty { "unknown" }
    val deviceCode = Build.DEVICE?.trim().orEmpty().ifEmpty { "android" }

    val patch = Build.VERSION.SECURITY_PATCH?.trim().orEmpty()
    val patchSuffix = if (patch.isNotEmpty()) " · 安全补丁 $patch" else ""

    val abis = Build.SUPPORTED_ABIS?.takeIf { it.isNotEmpty() }?.joinToString(", ") ?: "arm64-v8a"
    val cores = Runtime.getRuntime().availableProcessors().coerceAtLeast(1)

    val am = context.getSystemService(ActivityManager::class.java)
    val memInfo = ActivityManager.MemoryInfo()
    am?.getMemoryInfo(memInfo)

    val dm = context.resources.displayMetrics

    return MachineSpecsSnapshot(
        brandAndModel = brandModel,
        boardAndHardware = "$board · $hardware ($deviceCode)",
        androidRelease = Build.VERSION.RELEASE ?: "16",
        sdkInt = Build.VERSION.SDK_INT,
        securityPatchSuffix = patchSuffix,
        supportedAbis = abis,
        cpuCores = cores,
        availMemoryBytes = memInfo.availMem.coerceAtLeast(0L),
        totalMemoryBytes = memInfo.totalMem.coerceAtLeast(0L),
        screenWidthPx = dm.widthPixels,
        screenHeightPx = dm.heightPixels,
        densityDpi = dm.densityDpi,
        buildDisplay = Build.DISPLAY?.trim().orEmpty().ifEmpty { Build.ID ?: "unknown" }
    )
}
