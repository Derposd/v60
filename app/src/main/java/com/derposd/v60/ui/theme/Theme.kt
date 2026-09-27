package com.derposd.v60.ui.theme

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Shapes
import androidx.compose.material3.Typography
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

private val LightColors = lightColorScheme(
    primary = Color(0xFF6B4431),
    onPrimary = Color(0xFFFFFFFF),
    primaryContainer = Color(0xFFF3DED0),
    onPrimaryContainer = Color(0xFF2B160B),
    secondary = Color(0xFFA86F45),
    onSecondary = Color(0xFFFFFFFF),
    secondaryContainer = Color(0xFFF6E5D6),
    onSecondaryContainer = Color(0xFF3A2212),
    tertiary = Color(0xFF55704E),
    onTertiary = Color(0xFFFFFFFF),
    tertiaryContainer = Color(0xFFDCEBD2),
    onTertiaryContainer = Color(0xFF162713),
    background = Color(0xFFFBF7F3),
    onBackground = Color(0xFF221A15),
    surface = Color(0xFFFBF7F3),
    onSurface = Color(0xFF221A15),
    surfaceVariant = Color(0xFFEFE4DB),
    onSurfaceVariant = Color(0xFF6E5F55),
    surfaceContainerLowest = Color(0xFFFFFFFF),
    surfaceContainerLow = Color(0xFFF6F0EA),
    surfaceContainer = Color(0xFFF2EAE3),
    surfaceContainerHigh = Color(0xFFECE3DB),
    surfaceContainerHighest = Color(0xFFE6DCD3),
    outline = Color(0xFFB5A497),
    outlineVariant = Color(0xFFE4D8CE),
    error = Color(0xFFB3261E),
    onError = Color(0xFFFFFFFF),
    errorContainer = Color(0xFFF9DEDC),
    onErrorContainer = Color(0xFF410E0B),
)

private val DarkColors = darkColorScheme(
    primary = Color(0xFFE8BC93),
    onPrimary = Color(0xFF3F2414),
    primaryContainer = Color(0xFF55382A),
    onPrimaryContainer = Color(0xFFFBE2D0),
    secondary = Color(0xFFD9A77F),
    onSecondary = Color(0xFF3A2212),
    secondaryContainer = Color(0xFF45301F),
    onSecondaryContainer = Color(0xFFF6E5D6),
    tertiary = Color(0xFFB5D0A9),
    onTertiary = Color(0xFF213620),
    tertiaryContainer = Color(0xFF344731),
    onTertiaryContainer = Color(0xFFDCEBD2),
    background = Color(0xFF15110E),
    onBackground = Color(0xFFEDE0D6),
    surface = Color(0xFF15110E),
    onSurface = Color(0xFFEDE0D6),
    surfaceVariant = Color(0xFF3A302A),
    onSurfaceVariant = Color(0xFFC8B8AC),
    surfaceContainerLowest = Color(0xFF100C0A),
    surfaceContainerLow = Color(0xFF1D1814),
    surfaceContainer = Color(0xFF221C18),
    surfaceContainerHigh = Color(0xFF2C251F),
    surfaceContainerHighest = Color(0xFF372F29),
    outline = Color(0xFF8E7F74),
    outlineVariant = Color(0xFF3F352E),
    error = Color(0xFFF2B8B5),
    onError = Color(0xFF601410),
    errorContainer = Color(0xFF8C1D18),
    onErrorContainer = Color(0xFFF9DEDC),
)

private val AppShapes = Shapes(
    extraSmall = RoundedCornerShape(8.dp),
    small = RoundedCornerShape(12.dp),
    medium = RoundedCornerShape(18.dp),
    large = RoundedCornerShape(24.dp),
    extraLarge = RoundedCornerShape(32.dp),
)

private val Base = Typography()

private val AppTypography = Base.copy(
    displayLarge = Base.displayLarge.copy(fontWeight = FontWeight.Light),
    headlineLarge = Base.headlineLarge.copy(fontWeight = FontWeight.SemiBold, letterSpacing = (-0.5).sp),
    headlineMedium = Base.headlineMedium.copy(fontWeight = FontWeight.SemiBold, letterSpacing = (-0.25).sp),
    titleLarge = Base.titleLarge.copy(fontWeight = FontWeight.SemiBold),
    titleMedium = Base.titleMedium.copy(fontWeight = FontWeight.SemiBold),
    labelLarge = Base.labelLarge.copy(fontWeight = FontWeight.SemiBold),
)

/** Крупные цифры таймера с моноширинными цифрами, чтобы не «прыгали». */
val TimerTextStyle = TextStyle(
    fontSize = 76.sp,
    fontWeight = FontWeight.Light,
    letterSpacing = (-2).sp,
    fontFeatureSettings = "tnum",
)

val TabularNumbers = TextStyle(fontFeatureSettings = "tnum")

@Composable
fun V60Theme(darkTheme: Boolean = isSystemInDarkTheme(), content: @Composable () -> Unit) {
    MaterialTheme(
        colorScheme = if (darkTheme) DarkColors else LightColors,
        typography = AppTypography,
        shapes = AppShapes,
        content = content,
    )
}
