@file:OptIn(ExperimentalMaterial3Api::class, ExperimentalLayoutApi::class)

package com.derposd.v60.ui.screens

import android.os.SystemClock
import androidx.compose.animation.*
import androidx.compose.animation.core.*
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.rounded.*
import androidx.compose.material.icons.rounded.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.derposd.v60.data.ActiveSession
import com.derposd.v60.data.Brew
import com.derposd.v60.data.BrewGuide
import com.derposd.v60.data.GuideHint
import com.derposd.v60.data.formatClock
import com.derposd.v60.data.formatGrams
import com.derposd.v60.data.formatRatio
import com.derposd.v60.data.pourName
import com.derposd.v60.data.poursText
import com.derposd.v60.ui.AppViewModel
import com.derposd.v60.ui.components.ConfirmDialog
import com.derposd.v60.ui.components.IconBadge
import com.derposd.v60.ui.components.RatingStars
import com.derposd.v60.ui.components.StatPill
import com.derposd.v60.ui.components.pressScale
import com.derposd.v60.ui.components.vibrate
import com.derposd.v60.ui.theme.TabularNumbers
import com.derposd.v60.ui.theme.TimerTextStyle
import java.time.LocalTime
import kotlin.math.cos
import kotlin.math.min
import kotlin.math.sin

@Composable
fun BrewScreen(vm: AppViewModel, onOpenDraft: () -> Unit) {
    val session by vm.session.collectAsStateWithLifecycle()
    AnimatedContent(
        targetState = session != null,
        transitionSpec = {
            (fadeIn(tween(450, delayMillis = 60)) + scaleIn(tween(450, easing = FastOutSlowInEasing), initialScale = 0.92f))
                .togetherWith(fadeOut(tween(220)) + scaleOut(tween(300), targetScale = 1.05f))
        },
        label = "brew-state",
    ) { running ->
        if (running) RunningBrew(vm, onFinished = onOpenDraft) else IdleBrew(vm, onOpenDraft)
    }
}

// ============================================================================
// До старта
// ============================================================================

@Composable
private fun IdleBrew(vm: AppViewModel, onOpenDraft: () -> Unit) {
    val brews by vm.brews.collectAsStateWithLifecycle()
    val draft by vm.draft.collectAsStateWithLifecycle()
    val guide by vm.plannedGuide.collectAsStateWithLifecycle()
    val guideId by vm.guideId.collectAsStateWithLifecycle()
    var showPicker by rememberSaveable { mutableStateOf(false) }
    var confirmDiscard by remember { mutableStateOf(false) }
    val haptic = LocalHapticFeedback.current

    Column(Modifier.fillMaxSize().statusBarsPadding()) {
        Column(
            Modifier
                .weight(1f)
                .verticalScroll(rememberScrollState())
                .padding(horizontal = 20.dp),
        ) {
            Spacer(Modifier.height(20.dp))
            Text(
                greeting(),
                style = MaterialTheme.typography.bodyLarge,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
            Text("Время для V60", style = MaterialTheme.typography.headlineLarge)
            Spacer(Modifier.height(20.dp))

            AnimatedVisibility(
                visible = draft != null,
                enter = fadeIn() + expandVertically(),
                exit = fadeOut() + shrinkVertically(),
            ) {
                draft?.let { d ->
                    DraftBanner(d, onOpen = onOpenDraft, onDiscard = { confirmDiscard = true })
                }
            }

            GuideCard(
                guide = guide,
                hasRecipes = brews.isNotEmpty(),
                onPick = { showPicker = true },
                onClear = { vm.selectGuide(null) },
                onDose = vm::changeDose,
            )

            AnimatedVisibility(visible = brews.isEmpty() && draft == null) {
                Column {
                    Spacer(Modifier.height(12.dp))
                    HowItWorks()
                }
            }
            Spacer(Modifier.height(12.dp))
        }

        Box(
            Modifier
                .fillMaxWidth()
                .padding(top = 4.dp, bottom = 16.dp),
            contentAlignment = Alignment.Center,
        ) {
            StartButton(onClick = {
                haptic.performHapticFeedback(HapticFeedbackType.LongPress)
                vm.start()
            })
        }
    }

    if (showPicker) {
        RecipePickerSheet(
            brews = brews,
            selectedId = guideId,
            onSelect = { vm.selectGuide(it); showPicker = false },
            onDismiss = { showPicker = false },
        )
    }
    if (confirmDiscard) {
        ConfirmDialog(
            title = "Удалить запись?",
            text = "Время и вливания этой заварки не сохранятся.",
            confirm = "Удалить",
            icon = Icons.Rounded.DeleteOutline,
            onConfirm = vm::discardDraft,
            onDismiss = { confirmDiscard = false },
        )
    }
}

private fun greeting(): String = when (LocalTime.now().hour) {
    in 5..11 -> "Доброе утро"
    in 12..17 -> "Добрый день"
    in 18..22 -> "Добрый вечер"
    else -> "Доброй ночи"
}

@Composable
private fun StartButton(onClick: () -> Unit) {
    val transition = rememberInfiniteTransition(label = "halo")
    val halo by transition.animateFloat(
        initialValue = 0f,
        targetValue = 1f,
        animationSpec = infiniteRepeatable(tween(2400, easing = LinearOutSlowInEasing), RepeatMode.Restart),
        label = "halo-progress",
    )
    val interaction = remember { MutableInteractionSource() }
    val primary = MaterialTheme.colorScheme.primary

    Box(Modifier.size(196.dp), contentAlignment = Alignment.Center) {
        Box(
            Modifier
                .size(148.dp)
                .graphicsLayer {
                    val s = 1f + halo * 0.3f
                    scaleX = s
                    scaleY = s
                    alpha = (1f - halo) * 0.45f
                }
                .clip(CircleShape)
                .background(primary),
        )
        Surface(
            onClick = onClick,
            interactionSource = interaction,
            shape = CircleShape,
            color = primary,
            contentColor = MaterialTheme.colorScheme.onPrimary,
            shadowElevation = 8.dp,
            modifier = Modifier.size(148.dp).pressScale(interaction, 0.92f),
        ) {
            Column(
                horizontalAlignment = Alignment.CenterHorizontally,
                verticalArrangement = Arrangement.Center,
            ) {
                Icon(Icons.Rounded.PlayArrow, contentDescription = null, modifier = Modifier.size(56.dp))
                Text("Старт", style = MaterialTheme.typography.titleMedium)
            }
        }
    }
}

@Composable
private fun DraftBanner(draft: Brew, onOpen: () -> Unit, onDiscard: () -> Unit) {
    Surface(
        onClick = onOpen,
        shape = MaterialTheme.shapes.large,
        color = MaterialTheme.colorScheme.tertiaryContainer,
        contentColor = MaterialTheme.colorScheme.onTertiaryContainer,
        modifier = Modifier.fillMaxWidth().padding(bottom = 12.dp),
    ) {
        Row(Modifier.padding(start = 16.dp, top = 14.dp, bottom = 14.dp, end = 6.dp), verticalAlignment = Alignment.CenterVertically) {
            IconBadge(
                Icons.Rounded.EditNote,
                container = MaterialTheme.colorScheme.tertiary,
                tint = MaterialTheme.colorScheme.onTertiary,
            )
            Spacer(Modifier.width(12.dp))
            Column(Modifier.weight(1f)) {
                Text("Заварка не описана", style = MaterialTheme.typography.titleSmall)
                Text(
                    "${formatClock(draft.totalTimeMs)} · ${poursText(draft.pours.size)} — нажмите, чтобы сохранить",
                    style = MaterialTheme.typography.bodySmall,
                )
            }
            IconButton(onClick = onDiscard) { Icon(Icons.Rounded.Close, contentDescription = "Удалить") }
        }
    }
}

@Composable
private fun GuideCard(
    guide: BrewGuide?,
    hasRecipes: Boolean,
    onPick: () -> Unit,
    onClear: () -> Unit,
    onDose: (Double) -> Unit,
) {
    Surface(
        onClick = onPick,
        enabled = hasRecipes,
        shape = MaterialTheme.shapes.large,
        color = MaterialTheme.colorScheme.surfaceContainerLow,
        modifier = Modifier.fillMaxWidth(),
    ) {
        Column(Modifier.padding(18.dp).animateContentSize(spring(stiffness = Spring.StiffnessMediumLow))) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                IconBadge(Icons.Rounded.AutoAwesome)
                Spacer(Modifier.width(12.dp))
                Column(Modifier.weight(1f)) {
                    Text(
                        "Подсказки по рецепту",
                        style = MaterialTheme.typography.labelMedium,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                    AnimatedContent(
                        targetState = guide?.recipe?.title ?: "Свободная заварка",
                        transitionSpec = { fadeIn(tween(220)) togetherWith fadeOut(tween(120)) },
                        label = "guide-title",
                    ) { title ->
                        Text(title, style = MaterialTheme.typography.titleMedium, maxLines = 1, overflow = TextOverflow.Ellipsis)
                    }
                }
                if (guide != null) {
                    IconButton(onClick = onClear) { Icon(Icons.Rounded.Close, contentDescription = "Без рецепта") }
                } else if (hasRecipes) {
                    Icon(Icons.Rounded.UnfoldMore, null, tint = MaterialTheme.colorScheme.onSurfaceVariant)
                }
            }

            if (guide == null) {
                Spacer(Modifier.height(10.dp))
                Text(
                    if (hasRecipes) "Выберите сохранённый рецепт — таймер подскажет, сколько лить и когда следующее вливание."
                    else "После первой сохранённой заварки её можно будет выбрать здесь как подсказку.",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            } else {
                GuidePlan(guide, onDose)
            }
        }
    }
}

@Composable
private fun GuidePlan(guide: BrewGuide, onDose: (Double) -> Unit) {
    val r = guide.recipe
    Spacer(Modifier.height(16.dp))
    if (guide.coffeeG != null) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Column(Modifier.weight(1f)) {
                Text("Порция кофе", style = MaterialTheme.typography.labelMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
                Text("вода пересчитается", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.outline)
            }
            FilledTonalIconButton(onClick = { onDose(-0.5) }) { Icon(Icons.Rounded.Remove, "Меньше") }
            AnimatedContent(
                targetState = guide.coffeeG,
                transitionSpec = {
                    val up = targetState > initialState
                    (slideInVertically { if (up) it else -it } + fadeIn())
                        .togetherWith(slideOutVertically { if (up) -it else it } + fadeOut())
                },
                label = "dose",
            ) { dose ->
                Text(
                    "${formatGrams(dose)} г",
                    style = MaterialTheme.typography.titleLarge.merge(TabularNumbers),
                    textAlign = TextAlign.Center,
                    modifier = Modifier.widthIn(min = 76.dp),
                )
            }
            FilledTonalIconButton(onClick = { onDose(0.5) }) { Icon(Icons.Rounded.Add, "Больше") }
        }
        Spacer(Modifier.height(14.dp))
    }

    FlowRow(horizontalArrangement = Arrangement.spacedBy(6.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
        guide.waterG?.let { StatPill(Icons.Rounded.WaterDrop, "$it г") }
        r.ratio?.let { StatPill(Icons.Rounded.Balance, formatRatio(it)) }
        r.waterTempC?.let { StatPill(Icons.Rounded.Thermostat, "$it °C") }
        if (r.grind.isNotBlank()) StatPill(Icons.Rounded.Grain, r.grind)
        StatPill(Icons.Rounded.Timer, formatClock(r.totalTimeMs))
    }
    Spacer(Modifier.height(14.dp))
    HorizontalDivider(color = MaterialTheme.colorScheme.outlineVariant)
    Spacer(Modifier.height(8.dp))
    r.pours.forEachIndexed { i, p ->
        Row(Modifier.fillMaxWidth().padding(vertical = 5.dp), verticalAlignment = Alignment.CenterVertically) {
            Text(
                formatClock(p.startMs),
                style = MaterialTheme.typography.bodyMedium.merge(TabularNumbers),
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.width(48.dp),
            )
            Text(pourName(i), style = MaterialTheme.typography.bodyMedium, modifier = Modifier.weight(1f))
            guide.pourWater(i)?.let {
                Text("+$it г", style = MaterialTheme.typography.bodyMedium.merge(TabularNumbers))
            }
            guide.cumulativeWater(i)?.let {
                Text(
                    "  → $it",
                    style = MaterialTheme.typography.bodyMedium.merge(TabularNumbers),
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        }
    }
    Row(Modifier.fillMaxWidth().padding(vertical = 5.dp)) {
        Text(
            formatClock(r.totalTimeMs),
            style = MaterialTheme.typography.bodyMedium.merge(TabularNumbers),
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            modifier = Modifier.width(48.dp),
        )
        Text("Финиш", style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.secondary)
    }
}

@Composable
private fun HowItWorks() {
    Surface(
        shape = MaterialTheme.shapes.large,
        color = MaterialTheme.colorScheme.surfaceContainerLow,
        modifier = Modifier.fillMaxWidth(),
    ) {
        Column(Modifier.padding(18.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            Text("Как это работает", style = MaterialTheme.typography.titleMedium)
            Step(1, "«Старт» запускает таймер — первое вливание, блуминг.")
            Step(2, "«Следующее вливание» отмечает время каждого нового вливания.")
            Step(3, "После 3-го вливания появится «Закончить». Вливаний может быть сколько угодно.")
            Step(4, "Опишите заварку, зерно и дату обжарки — рецепт сохранится для подсказок.")
        }
    }
}

@Composable
private fun Step(n: Int, text: String) {
    Row(verticalAlignment = Alignment.Top) {
        Box(
            Modifier.size(24.dp).clip(CircleShape).background(MaterialTheme.colorScheme.secondaryContainer),
            contentAlignment = Alignment.Center,
        ) {
            Text("$n", style = MaterialTheme.typography.labelMedium, color = MaterialTheme.colorScheme.onSecondaryContainer)
        }
        Spacer(Modifier.width(12.dp))
        Text(text, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
    }
}

@Composable
private fun RecipePickerSheet(
    brews: List<Brew>,
    selectedId: Long?,
    onSelect: (Long?) -> Unit,
    onDismiss: () -> Unit,
) {
    val sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true)
    val sorted = remember(brews) {
        brews.sortedWith(compareByDescending<Brew> { it.favorite }.thenByDescending { it.rating }.thenByDescending { it.createdAt })
    }
    ModalBottomSheet(
        onDismissRequest = onDismiss,
        sheetState = sheetState,
        containerColor = MaterialTheme.colorScheme.surfaceContainerLow,
    ) {
        Text(
            "Выберите рецепт",
            style = MaterialTheme.typography.titleLarge,
            modifier = Modifier.padding(horizontal = 24.dp, vertical = 4.dp),
        )
        Spacer(Modifier.height(8.dp))
        LazyColumn(
            contentPadding = PaddingValues(start = 12.dp, end = 12.dp, bottom = 24.dp),
            verticalArrangement = Arrangement.spacedBy(4.dp),
        ) {
            item {
                PickerRow(
                    title = "Свободная заварка",
                    subtitle = "Только таймер, без подсказок",
                    selected = selectedId == null,
                    icon = Icons.Rounded.Timer,
                    onClick = { onSelect(null) },
                )
            }
            items(sorted, key = { it.id }) { b ->
                PickerRow(
                    title = b.title,
                    subtitle = listOfNotNull(
                        b.bean.name.takeIf { it.isNotBlank() && it != b.title },
                        b.coffeeG?.let { "${formatGrams(it)} г" },
                        b.effectiveWaterG?.let { "$it мл" },
                        formatClock(b.totalTimeMs),
                        poursText(b.pours.size),
                    ).joinToString(" · "),
                    selected = selectedId == b.id,
                    icon = if (b.favorite) Icons.Rounded.Favorite else Icons.Rounded.Coffee,
                    rating = b.rating,
                    onClick = { onSelect(b.id) },
                )
            }
        }
    }
}

@Composable
private fun PickerRow(
    title: String,
    subtitle: String,
    selected: Boolean,
    icon: androidx.compose.ui.graphics.vector.ImageVector,
    onClick: () -> Unit,
    rating: Int = 0,
) {
    val bg by animateColorAsState(
        if (selected) MaterialTheme.colorScheme.primaryContainer else Color.Transparent,
        label = "picker-bg",
    )
    Row(
        Modifier
            .fillMaxWidth()
            .clip(MaterialTheme.shapes.medium)
            .background(bg)
            .clickable(onClick = onClick)
            .padding(horizontal = 12.dp, vertical = 12.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        IconBadge(
            icon,
            container = if (selected) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.surfaceContainerHighest,
            tint = if (selected) MaterialTheme.colorScheme.onPrimary else MaterialTheme.colorScheme.secondary,
        )
        Spacer(Modifier.width(14.dp))
        Column(Modifier.weight(1f)) {
            Text(title, style = MaterialTheme.typography.titleSmall, maxLines = 1, overflow = TextOverflow.Ellipsis)
            Text(
                subtitle,
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }
        if (rating > 0) {
            Spacer(Modifier.width(8.dp))
            RatingStars(rating, size = 12.dp)
        }
    }
}

// ============================================================================
// Идёт заварка
// ============================================================================

@Composable
private fun rememberElapsed(startElapsed: Long): State<Long> {
    val state = remember(startElapsed) { mutableLongStateOf(SystemClock.elapsedRealtime() - startElapsed) }
    LaunchedEffect(startElapsed) {
        while (true) {
            withFrameMillis { }
            state.longValue = SystemClock.elapsedRealtime() - startElapsed
        }
    }
    return state
}

@Composable
private fun RunningBrew(vm: AppViewModel, onFinished: () -> Unit) {
    val live by vm.session.collectAsStateWithLifecycle()
    // Держим последнее состояние, чтобы экран красиво «уехал» после остановки.
    val holder = remember { mutableStateOf(live) }
    if (live != null) holder.value = live
    val s: ActiveSession = holder.value ?: return
    val guide by vm.activeGuide.collectAsStateWithLifecycle()

    val context = LocalContext.current
    val haptic = LocalHapticFeedback.current
    val view = LocalView.current
    DisposableEffect(Unit) {
        view.keepScreenOn = true
        onDispose { view.keepScreenOn = false }
    }

    val elapsed = rememberElapsed(s.startElapsed)
    val pourIndex = s.pourCount - 1
    val pourStart = s.pourStarts.last()
    val g = guide

    val hint by remember(g, pourIndex, pourStart) {
        derivedStateOf { g?.hint(pourIndex, pourStart, elapsed.value / 100 * 100) }
    }
    val due = hint?.isDue == true
    val soon = hint?.msToTarget?.let { it in 1..3000 } == true

    // Вибро-подсказки: за 3 секунды и в момент следующего вливания/финиша.
    LaunchedEffect(pourIndex, soon) { if (soon) vibrate(context, 0, 40) }
    LaunchedEffect(pourIndex, due) { if (due) vibrate(context, 0, 220, 120, 220) }

    var confirmCancel by remember { mutableStateOf(false) }

    Column(
        Modifier
            .fillMaxSize()
            .systemBarsPadding()
            .padding(horizontal = 20.dp),
    ) {
        Row(Modifier.fillMaxWidth().padding(top = 8.dp), verticalAlignment = Alignment.CenterVertically) {
            Column(Modifier.weight(1f)) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    LiveDot()
                    Spacer(Modifier.width(8.dp))
                    Text("Заварка идёт", style = MaterialTheme.typography.titleMedium)
                }
                if (g != null) {
                    Text(
                        "по рецепту «${g.recipe.title}»",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                }
            }
            FilledTonalIconButton(onClick = { confirmCancel = true }) {
                Icon(Icons.Rounded.Close, contentDescription = "Сбросить")
            }
        }

        Spacer(Modifier.weight(1f))

        TimerDial(
            elapsed = elapsed,
            pourIndex = pourIndex,
            pourStart = pourStart,
            due = due,
            progress = {
                val e = elapsed.value
                g?.hint(pourIndex, pourStart, e)?.progress ?: ((e % 60_000) / 60_000f)
            },
            totalProgress = g?.totalMs?.takeIf { it > 0 }?.let { total -> { elapsed.value / total.toFloat() } },
            modifier = Modifier
                .align(Alignment.CenterHorizontally)
                .fillMaxWidth(0.82f)
                .widthIn(max = 320.dp),
        )

        Spacer(Modifier.height(20.dp))

        AnimatedVisibility(visible = g != null && hint != null) {
            hint?.let { GuidePanel(it, g?.plannedPours ?: 0) }
        }

        Spacer(Modifier.height(12.dp))
        PourTimeline(
            starts = s.pourStarts,
            elapsed = elapsed,
            onUndo = {
                haptic.performHapticFeedback(HapticFeedbackType.LongPress)
                vm.undoPour()
            },
        )

        Spacer(Modifier.weight(1f))

        AnimatedVisibility(
            visible = !s.canFinish,
            enter = fadeIn() + expandVertically(),
            exit = fadeOut() + shrinkVertically(),
        ) {
            MinPoursHint(s.pourCount)
        }

        Controls(
            canFinish = s.canFinish,
            onNext = {
                haptic.performHapticFeedback(HapticFeedbackType.LongPress)
                vm.nextPour()
            },
            onFinish = {
                haptic.performHapticFeedback(HapticFeedbackType.LongPress)
                if (vm.finishBrew()) onFinished()
            },
        )
        Spacer(Modifier.height(16.dp))
    }

    if (confirmCancel) {
        ConfirmDialog(
            title = "Сбросить заварку?",
            text = "Таймер остановится, отметки вливаний не сохранятся.",
            confirm = "Сбросить",
            icon = Icons.Rounded.RestartAlt,
            onConfirm = vm::cancelBrew,
            onDismiss = { confirmCancel = false },
        )
    }
}

@Composable
private fun LiveDot() {
    val t = rememberInfiniteTransition(label = "live")
    val a by t.animateFloat(1f, 0.25f, infiniteRepeatable(tween(900), RepeatMode.Reverse), label = "live-alpha")
    Box(
        Modifier
            .size(8.dp)
            .graphicsLayer { alpha = a }
            .clip(CircleShape)
            .background(MaterialTheme.colorScheme.tertiary),
    )
}

@Composable
private fun TimerDial(
    elapsed: State<Long>,
    pourIndex: Int,
    pourStart: Long,
    due: Boolean,
    progress: () -> Float,
    totalProgress: (() -> Float)?,
    modifier: Modifier = Modifier,
) {
    val track = MaterialTheme.colorScheme.surfaceContainerHigh
    val arcColor by animateColorAsState(
        if (due) MaterialTheme.colorScheme.tertiary else MaterialTheme.colorScheme.primary,
        tween(500),
        label = "arc",
    )
    val innerColor = MaterialTheme.colorScheme.secondary
    val tickColor = MaterialTheme.colorScheme.outlineVariant

    val pulse = rememberInfiniteTransition(label = "pulse")
    val pulseScale by pulse.animateFloat(
        1f, 1.025f,
        infiniteRepeatable(tween(650, easing = FastOutSlowInEasing), RepeatMode.Reverse),
        label = "pulse-scale",
    )

    // Плавно «доезжающая» дуга при смене вливания.
    val pourAnim = remember { Animatable(0f) }
    LaunchedEffect(pourIndex) {
        pourAnim.snapTo(0f)
        pourAnim.animateTo(1f, tween(600, easing = FastOutSlowInEasing))
    }

    Box(
        modifier
            .aspectRatio(1f)
            .graphicsLayer {
                val sc = if (due) pulseScale else 1f
                scaleX = sc
                scaleY = sc
            },
        contentAlignment = Alignment.Center,
    ) {
        Canvas(Modifier.fillMaxSize()) {
            val stroke = 14.dp.toPx()
            val inset = stroke / 2 + 4.dp.toPx()
            val arcSize = Size(size.width - inset * 2, size.height - inset * 2)
            val topLeft = Offset(inset, inset)
            val radius = arcSize.width / 2
            val c = center

            // Деления, как на циферблате.
            for (i in 0 until 60) {
                val angle = Math.toRadians(i * 6.0 - 90)
                val long = i % 5 == 0
                val r1 = radius - stroke - (if (long) 10.dp.toPx() else 6.dp.toPx())
                val r2 = radius - stroke - 2.dp.toPx()
                drawLine(
                    color = tickColor,
                    start = Offset(c.x + (r1 * cos(angle)).toFloat(), c.y + (r1 * sin(angle)).toFloat()),
                    end = Offset(c.x + (r2 * cos(angle)).toFloat(), c.y + (r2 * sin(angle)).toFloat()),
                    strokeWidth = if (long) 2.dp.toPx() else 1.dp.toPx(),
                    cap = StrokeCap.Round,
                )
            }

            drawArc(track, 0f, 360f, false, topLeft, arcSize, style = Stroke(stroke))

            val p = min(progress(), 1f) * pourAnim.value
            val sweep = 360f * p
            if (sweep > 0.5f) {
                drawArc(arcColor, -90f, sweep, false, topLeft, arcSize, style = Stroke(stroke, cap = StrokeCap.Round))
            }
            // Светящаяся точка на конце дуги.
            val endAngle = Math.toRadians(-90.0 + sweep)
            val dot = Offset(c.x + (radius * cos(endAngle)).toFloat(), c.y + (radius * sin(endAngle)).toFloat())
            drawCircle(arcColor.copy(alpha = 0.25f), radius = stroke * 1.05f, center = dot)
            drawCircle(Color.White.copy(alpha = 0.9f), radius = stroke * 0.28f, center = dot)

            // Тонкое внутреннее кольцо — общий прогресс по рецепту.
            if (totalProgress != null) {
                val innerInset = inset + stroke + 18.dp.toPx()
                val innerSize = Size(size.width - innerInset * 2, size.height - innerInset * 2)
                val tp = totalProgress().coerceIn(0f, 1f)
                drawArc(
                    innerColor.copy(alpha = 0.18f), 0f, 360f, false,
                    Offset(innerInset, innerInset), innerSize, style = Stroke(3.dp.toPx()),
                )
                drawArc(
                    innerColor, -90f, 360f * tp, false,
                    Offset(innerInset, innerInset), innerSize,
                    style = Stroke(3.dp.toPx(), cap = StrokeCap.Round),
                )
            }
        }

        Column(horizontalAlignment = Alignment.CenterHorizontally) {
            AnimatedContent(
                targetState = pourIndex,
                transitionSpec = {
                    (slideInVertically(spring(stiffness = Spring.StiffnessMediumLow)) { it } + fadeIn())
                        .togetherWith(slideOutVertically { -it } + fadeOut())
                },
                label = "pour-name",
            ) { idx ->
                Surface(
                    shape = CircleShape,
                    color = if (idx == 0) MaterialTheme.colorScheme.secondaryContainer else MaterialTheme.colorScheme.primaryContainer,
                ) {
                    Row(Modifier.padding(horizontal = 12.dp, vertical = 5.dp), verticalAlignment = Alignment.CenterVertically) {
                        Icon(
                            if (idx == 0) Icons.Rounded.Spa else Icons.Rounded.WaterDrop,
                            contentDescription = null,
                            modifier = Modifier.size(15.dp),
                        )
                        Spacer(Modifier.width(6.dp))
                        Text(pourName(idx), style = MaterialTheme.typography.labelLarge)
                    }
                }
            }
            TimerText(elapsed)
            val pourSec by remember(pourStart) { derivedStateOf { (elapsed.value - pourStart).coerceAtLeast(0) / 1000 } }
            Text(
                "в этом вливании ${formatClock(pourSec * 1000)}",
                style = MaterialTheme.typography.bodyMedium.merge(TabularNumbers),
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
    }
}

@Composable
private fun TimerText(elapsed: State<Long>) {
    val tenths by remember { derivedStateOf { elapsed.value / 100 } }
    Row {
        Text(
            formatClock(tenths * 100),
            style = TimerTextStyle,
            color = MaterialTheme.colorScheme.onSurface,
            modifier = Modifier.alignByBaseline(),
        )
        Text(
            ".${tenths % 10}",
            style = TimerTextStyle.copy(fontSize = 30.sp, letterSpacing = 0.sp),
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            modifier = Modifier.alignByBaseline(),
        )
    }
}

@Composable
private fun GuidePanel(hint: GuideHint, plannedPours: Int) {
    Surface(
        shape = MaterialTheme.shapes.large,
        color = MaterialTheme.colorScheme.surfaceContainerLow,
        modifier = Modifier.fillMaxWidth(),
    ) {
        if (hint.beyondPlan) {
            Row(Modifier.padding(16.dp), verticalAlignment = Alignment.CenterVertically) {
                Icon(Icons.Rounded.Info, null, tint = MaterialTheme.colorScheme.secondary)
                Spacer(Modifier.width(12.dp))
                Text(
                    "В рецепте ${poursText(plannedPours)} — это вливание сверх плана.",
                    style = MaterialTheme.typography.bodyMedium,
                )
            }
        } else Row(Modifier.padding(vertical = 14.dp).height(IntrinsicSize.Min)) {
            GuideStat(
                label = "Влейте",
                value = hint.pourWater?.let { "+$it г" } ?: "—",
                sub = hint.cumulativeWater?.let { "до $it г на весах" },
                highlight = false,
                modifier = Modifier.weight(1f),
            )
            VerticalDivider(color = MaterialTheme.colorScheme.outlineVariant)
            val label = when {
                hint.nextPourAt != null -> "Следующее"
                hint.finishAt != null -> "Финиш"
                else -> "План"
            }
            val target = hint.nextPourAt ?: hint.finishAt
            val ms = hint.msToTarget
            GuideStat(
                label = label,
                value = when {
                    ms == null -> "—"
                    ms > 0 -> "через ${formatClock(ms + 999)}"
                    else -> "сейчас"
                },
                sub = target?.let { "в ${formatClock(it)}" + (hint.startDeltaMs?.takeIf { d -> kotlin.math.abs(d) >= 3000 }?.let { d -> " · ${if (d > 0) "позже" else "раньше"} на ${formatClock(kotlin.math.abs(d))}" } ?: "") },
                highlight = hint.isDue,
                modifier = Modifier.weight(1f),
            )
        }
    }
}

@Composable
private fun GuideStat(label: String, value: String, sub: String?, highlight: Boolean, modifier: Modifier = Modifier) {
    val color by animateColorAsState(
        if (highlight) MaterialTheme.colorScheme.tertiary else MaterialTheme.colorScheme.onSurface,
        label = "guide-color",
    )
    Column(modifier.padding(horizontal = 16.dp)) {
        Text(label, style = MaterialTheme.typography.labelMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
        Text(value, style = MaterialTheme.typography.titleLarge.merge(TabularNumbers), color = color, maxLines = 1)
        if (sub != null) {
            Text(
                sub,
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                maxLines = 2,
            )
        }
    }
}

@Composable
private fun PourTimeline(starts: List<Long>, elapsed: State<Long>, onUndo: () -> Unit) {
    val listState = rememberLazyListState()
    LaunchedEffect(starts.size) { listState.animateScrollToItem(starts.lastIndex) }

    Column {
        Row(Modifier.fillMaxWidth().height(36.dp), verticalAlignment = Alignment.CenterVertically) {
            Text(
                "Вливания · ${starts.size}",
                style = MaterialTheme.typography.labelLarge,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.weight(1f),
            )
            AnimatedVisibility(visible = starts.size > 1, enter = fadeIn(), exit = fadeOut()) {
                TextButton(onClick = onUndo, contentPadding = PaddingValues(horizontal = 10.dp)) {
                    Icon(Icons.AutoMirrored.Rounded.Undo, null, modifier = Modifier.size(18.dp))
                    Spacer(Modifier.width(6.dp))
                    Text("Отменить последнее")
                }
            }
        }
        LazyRow(state = listState, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            itemsIndexed(starts, key = { i, _ -> i }) { i, start ->
                val current = i == starts.lastIndex
                val bg by animateColorAsState(
                    if (current) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.surfaceContainerHigh,
                    tween(400),
                    label = "chip-bg",
                )
                val fg by animateColorAsState(
                    if (current) MaterialTheme.colorScheme.onPrimary else MaterialTheme.colorScheme.onSurface,
                    tween(400),
                    label = "chip-fg",
                )
                Column(
                    Modifier
                        .animateItem()
                        .clip(RoundedCornerShape(16.dp))
                        .background(bg)
                        .padding(horizontal = 14.dp, vertical = 8.dp),
                ) {
                    Text(pourName(i), style = MaterialTheme.typography.labelLarge, color = fg)
                    val durationText = if (current) {
                        val sec by remember(start) { derivedStateOf { (elapsed.value - start).coerceAtLeast(0) / 1000 } }
                        "${formatClock(start)} · ${sec} с"
                    } else {
                        "${formatClock(start)} · ${(starts[i + 1] - start) / 1000} с"
                    }
                    Text(
                        durationText,
                        style = MaterialTheme.typography.bodySmall.merge(TabularNumbers),
                        color = fg.copy(alpha = 0.75f),
                    )
                }
            }
        }
    }
}

@Composable
private fun MinPoursHint(count: Int) {
    Row(
        Modifier.fillMaxWidth().padding(bottom = 12.dp),
        horizontalArrangement = Arrangement.Center,
        verticalAlignment = Alignment.CenterVertically,
    ) {
        for (i in 1..ActiveSession.MIN_POURS) {
            val filled = i <= count
            val w by animateDpAsState(if (filled) 18.dp else 8.dp, spring(dampingRatio = 0.6f), label = "dot")
            val color by animateColorAsState(
                if (filled) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.outlineVariant,
                label = "dot-color",
            )
            Box(Modifier.padding(horizontal = 3.dp).height(8.dp).width(w).clip(CircleShape).background(color))
        }
        Spacer(Modifier.width(10.dp))
        Text(
            "«Закончить» — после 3-го вливания",
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
    }
}

@Composable
private fun Controls(canFinish: Boolean, onNext: () -> Unit, onFinish: () -> Unit) {
    val nextInteraction = remember { MutableInteractionSource() }
    val finishInteraction = remember { MutableInteractionSource() }
    Row(Modifier.fillMaxWidth().height(72.dp)) {
        Button(
            onClick = onNext,
            interactionSource = nextInteraction,
            shape = RoundedCornerShape(24.dp),
            contentPadding = PaddingValues(horizontal = 16.dp),
            modifier = Modifier
                .weight(1f)
                .fillMaxHeight()
                .pressScale(nextInteraction),
        ) {
            Icon(Icons.Rounded.WaterDrop, contentDescription = null)
            Spacer(Modifier.width(10.dp))
            Text(
                "Следующее вливание",
                style = MaterialTheme.typography.titleSmall,
                textAlign = TextAlign.Center,
                maxLines = 2,
            )
        }
        AnimatedVisibility(
            visible = canFinish,
            enter = expandHorizontally(spring(dampingRatio = 0.75f, stiffness = Spring.StiffnessLow)) + fadeIn(tween(300)),
            exit = shrinkHorizontally() + fadeOut(),
            modifier = Modifier.fillMaxHeight(),
        ) {
            Row(Modifier.fillMaxHeight()) {
                Spacer(Modifier.width(10.dp))
                FilledTonalButton(
                    onClick = onFinish,
                    interactionSource = finishInteraction,
                    shape = RoundedCornerShape(24.dp),
                    contentPadding = PaddingValues(horizontal = 18.dp),
                    colors = ButtonDefaults.filledTonalButtonColors(
                        containerColor = MaterialTheme.colorScheme.secondaryContainer,
                        contentColor = MaterialTheme.colorScheme.onSecondaryContainer,
                    ),
                    modifier = Modifier.fillMaxHeight().pressScale(finishInteraction),
                ) {
                    Icon(Icons.Rounded.Stop, contentDescription = null)
                    Spacer(Modifier.width(6.dp))
                    Text("Закончить", style = MaterialTheme.typography.titleSmall)
                }
            }
        }
    }
}
