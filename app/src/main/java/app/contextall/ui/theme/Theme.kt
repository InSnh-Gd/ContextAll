package app.contextall.ui.theme

import android.os.Build
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Shapes
import androidx.compose.material3.Typography
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.dynamicDarkColorScheme
import androidx.compose.material3.dynamicLightColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.Immutable
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.Font
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import app.contextall.R

val JetBrainsMonoFamily = FontFamily(
    Font(R.font.jetbrains_mono, FontWeight.Normal),
    Font(R.font.jetbrains_mono, FontWeight.Medium),
    Font(R.font.jetbrains_mono, FontWeight.Bold)
)

// Android 16 Material 3 Expressive Breeze-Cobalt fallback palette
private val ExpressiveLightColors = lightColorScheme(
    primary = Color(0xFF1B5E91),
    onPrimary = Color(0xFFFFFFFF),
    primaryContainer = Color(0xFFD5E3FF),
    onPrimaryContainer = Color(0xFF001C38),
    secondary = Color(0xFF4F5F76),
    onSecondary = Color(0xFFFFFFFF),
    secondaryContainer = Color(0xFFD3E4FF),
    onSecondaryContainer = Color(0xFF0B1C30),
    tertiary = Color(0xFF146B54),
    onTertiary = Color(0xFFFFFFFF),
    tertiaryContainer = Color(0xFFA4F2D5),
    onTertiaryContainer = Color(0xFF002117),
    error = Color(0xFFBA1A1A),
    onError = Color(0xFFFFFFFF),
    errorContainer = Color(0xFFFFDAD6),
    onErrorContainer = Color(0xFF410002),
    background = Color(0xFFF0F4FA),
    onBackground = Color(0xFF161C22),
    surface = Color(0xFFF0F4FA),
    onSurface = Color(0xFF161C22),
    surfaceVariant = Color(0xFFDDE3EC),
    onSurfaceVariant = Color(0xFF414750),
    surfaceContainerLowest = Color(0xFFFFFFFF),
    surfaceContainerLow = Color(0xFFFAFBFD),
    surfaceContainer = Color(0xFFE7EDF6),
    surfaceContainerHigh = Color(0xFFDFE6F0),
    surfaceContainerHighest = Color(0xFFD7DFEA),
    outline = Color(0xFF717881),
    outlineVariant = Color(0xFFC1C7D1)
)

private val ExpressiveDarkColors = darkColorScheme(
    primary = Color(0xFFA6C8FF),
    onPrimary = Color(0xFF00315B),
    primaryContainer = Color(0xFF004780),
    onPrimaryContainer = Color(0xFFD5E3FF),
    secondary = Color(0xFFB7C8E2),
    onSecondary = Color(0xFF213146),
    secondaryContainer = Color(0xFF38485E),
    onSecondaryContainer = Color(0xFFD3E4FF),
    tertiary = Color(0xFF89D6B9),
    onTertiary = Color(0xFF00382A),
    tertiaryContainer = Color(0xFF00513E),
    onTertiaryContainer = Color(0xFFA4F2D5),
    error = Color(0xFFFFB4AB),
    onError = Color(0xFF690005),
    errorContainer = Color(0xFF93000A),
    onErrorContainer = Color(0xFFFFDAD6),
    background = Color(0xFF0E1319),
    onBackground = Color(0xFFDEE3EB),
    surface = Color(0xFF0E1319),
    onSurface = Color(0xFFDEE3EB),
    surfaceVariant = Color(0xFF414750),
    onSurfaceVariant = Color(0xFFC1C7D1),
    surfaceContainerLowest = Color(0xFF090E13),
    surfaceContainerLow = Color(0xFF171D25),
    surfaceContainer = Color(0xFF1D242D),
    surfaceContainerHigh = Color(0xFF262E38),
    surfaceContainerHighest = Color(0xFF313944),
    outline = Color(0xFF8B919B),
    outlineVariant = Color(0xFF414750)
)

/**
 * Android 16 Settings multi-tonal category icon containers (M3 Expressive).
 */
@Immutable
data class ExpressivePalette(
    val connectedContainer: Color,
    val onConnectedContainer: Color,
    val reachableContainer: Color,
    val onReachableContainer: Color,
    val skyIconBg: Color,
    val skyIconFg: Color,
    val mintIconBg: Color,
    val mintIconFg: Color,
    val amberIconBg: Color,
    val amberIconFg: Color,
    val violetIconBg: Color,
    val violetIconFg: Color
)

val LocalExpressivePalette = staticCompositionLocalOf {
    ExpressivePalette(
        connectedContainer = Color(0xFFC8F7DF),
        onConnectedContainer = Color(0xFF074E31),
        reachableContainer = Color(0xFFFFECBE),
        onReachableContainer = Color(0xFF5A3F00),
        skyIconBg = Color(0xFFD6E6FF),
        skyIconFg = Color(0xFF0E4A86),
        mintIconBg = Color(0xFFC8F7DF),
        mintIconFg = Color(0xFF075436),
        amberIconBg = Color(0xFFFFECBE),
        amberIconFg = Color(0xFF694900),
        violetIconBg = Color(0xFFEADDFF),
        violetIconFg = Color(0xFF4F378B)
    )
}

// Use system variable sans-serif so both CJK and Latin render with genuine ExtraBold / Bold / Medium weights like Android 16 Settings
val ContextAllTypography = Typography(
    displaySmall = TextStyle(
        fontFamily = FontFamily.SansSerif,
        fontWeight = FontWeight.ExtraBold,
        fontSize = 32.sp,
        lineHeight = 38.sp,
        letterSpacing = (-0.5).sp
    ),
    headlineMedium = TextStyle(
        fontFamily = FontFamily.SansSerif,
        fontWeight = FontWeight.Bold,
        fontSize = 26.sp,
        lineHeight = 32.sp,
        letterSpacing = (-0.3).sp
    ),
    titleLarge = TextStyle(
        fontFamily = FontFamily.SansSerif,
        fontWeight = FontWeight.Bold,
        fontSize = 21.sp,
        lineHeight = 28.sp
    ),
    titleMedium = TextStyle(
        fontFamily = FontFamily.SansSerif,
        fontWeight = FontWeight.SemiBold,
        fontSize = 16.5.sp,
        lineHeight = 23.sp
    ),
    titleSmall = TextStyle(
        fontFamily = FontFamily.SansSerif,
        fontWeight = FontWeight.SemiBold,
        fontSize = 15.sp,
        lineHeight = 21.sp
    ),
    bodyLarge = TextStyle(
        fontFamily = FontFamily.SansSerif,
        fontWeight = FontWeight.Normal,
        fontSize = 16.sp,
        lineHeight = 24.sp
    ),
    bodyMedium = TextStyle(
        fontFamily = FontFamily.SansSerif,
        fontWeight = FontWeight.Normal,
        fontSize = 14.sp,
        lineHeight = 20.sp
    ),
    bodySmall = TextStyle(
        fontFamily = FontFamily.SansSerif,
        fontWeight = FontWeight.Normal,
        fontSize = 12.5.sp,
        lineHeight = 18.sp
    ),
    labelLarge = TextStyle(
        fontFamily = FontFamily.SansSerif,
        fontWeight = FontWeight.Bold,
        fontSize = 13.5.sp,
        lineHeight = 18.sp,
        letterSpacing = 0.2.sp
    ),
    labelMedium = TextStyle(
        fontFamily = FontFamily.SansSerif,
        fontWeight = FontWeight.SemiBold,
        fontSize = 12.sp,
        lineHeight = 16.sp
    )
)

val ContextAllShapes = Shapes(
    extraSmall = RoundedCornerShape(6.dp),
    small = RoundedCornerShape(14.dp),
    medium = RoundedCornerShape(20.dp),
    large = RoundedCornerShape(28.dp),
    extraLarge = RoundedCornerShape(32.dp)
)

@Composable
fun ContextAllTheme(
    darkTheme: Boolean = isSystemInDarkTheme(),
    dynamicColor: Boolean = true,
    content: @Composable () -> Unit
) {
    val context = LocalContext.current
    val baseScheme = when {
        dynamicColor && Build.VERSION.SDK_INT >= Build.VERSION_CODES.S -> {
            if (darkTheme) dynamicDarkColorScheme(context) else dynamicLightColorScheme(context)
        }
        darkTheme -> ExpressiveDarkColors
        else -> ExpressiveLightColors
    }

    // Ensure high-contrast Android 16 surface-to-card separation even on dynamic palettes
    val colorScheme = if (darkTheme) {
        baseScheme.copy(
            background = Color(0xFF0E1319),
            surface = Color(0xFF0E1319),
            surfaceContainerLow = Color(0xFF181F27),
            surfaceContainer = Color(0xFF202833),
            surfaceContainerHigh = Color(0xFF293240)
        )
    } else {
        baseScheme.copy(
            background = Color(0xFFEEF3FA),
            surface = Color(0xFFEEF3FA),
            surfaceContainerLow = Color(0xFFFFFFFF),
            surfaceContainer = Color(0xFFE3EAF4),
            surfaceContainerHigh = Color(0xFFD9E2EE)
        )
    }

    val expressivePalette = if (darkTheme) {
        ExpressivePalette(
            connectedContainer = Color(0xFF10422B),
            onConnectedContainer = Color(0xFF9DF4C8),
            reachableContainer = Color(0xFF4C3800),
            onReachableContainer = Color(0xFFFFDF94),
            skyIconBg = Color(0xFF163A5F),
            skyIconFg = Color(0xFFAED2FF),
            mintIconBg = Color(0xFF10422B),
            mintIconFg = Color(0xFF9DF4C8),
            amberIconBg = Color(0xFF4C3800),
            amberIconFg = Color(0xFFFFDF94),
            violetIconBg = Color(0xFF392668),
            violetIconFg = Color(0xFFDCC9FF)
        )
    } else {
        ExpressivePalette(
            connectedContainer = Color(0xFFC8F7DF),
            onConnectedContainer = Color(0xFF074E31),
            reachableContainer = Color(0xFFFFECBE),
            onReachableContainer = Color(0xFF5A3F00),
            skyIconBg = Color(0xFFD8E8FF),
            skyIconFg = Color(0xFF0E4A86),
            mintIconBg = Color(0xFFC8F7DF),
            mintIconFg = Color(0xFF075436),
            amberIconBg = Color(0xFFFFECBE),
            amberIconFg = Color(0xFF694900),
            violetIconBg = Color(0xFFEBDDFF),
            violetIconFg = Color(0xFF4F378B)
        )
    }

    CompositionLocalProvider(LocalExpressivePalette provides expressivePalette) {
        MaterialTheme(
            colorScheme = colorScheme,
            typography = ContextAllTypography,
            shapes = ContextAllShapes,
            content = content
        )
    }
}
