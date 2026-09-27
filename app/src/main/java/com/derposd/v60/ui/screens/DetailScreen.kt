@file:OptIn(ExperimentalMaterial3Api::class)

package com.derposd.v60.ui.screens

import android.content.Intent
import androidx.compose.animation.*
import androidx.compose.animation.core.*
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.rounded.*
import androidx.compose.material.icons.rounded.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.input.nestedscroll.nestedScroll
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.derposd.v60.data.Brew
import com.derposd.v60.data.daysText
import com.derposd.v60.data.formatClock
import com.derposd.v60.data.formatDate
import com.derposd.v60.data.formatGrams
import com.derposd.v60.data.formatRatio
import com.derposd.v60.data.formatSeconds
import com.derposd.v60.data.pourName
import com.derposd.v60.data.poursText
import com.derposd.v60.data.shareText
import com.derposd.v60.ui.AppViewModel
import com.derposd.v60.ui.components.ConfirmDialog
import com.derposd.v60.ui.components.InfoRow
import com.derposd.v60.ui.components.RatingStars
import com.derposd.v60.ui.components.SectionCard
import com.derposd.v60.ui.components.StatTile
import com.derposd.v60.ui.theme.TabularNumbers
import java.time.LocalDate
import java.time.temporal.ChronoUnit

@Composable
fun DetailScreen(
    vm: AppViewModel,
    id: Long,
    onBack: () -> Unit,
    onEdit: () -> Unit,
    onBrewAgain: () -> Unit,
) {
    val brews by vm.brews.collectAsStateWithLifecycle()
    val live = brews.firstOrNull { it.id == id }
    // После удаления держим последнее состояние, пока экран уходит.
    val holder = remember { mutableStateOf(live) }
    if (live != null) holder.value = live
    val brew = holder.value

    val context = LocalContext.current
    var menu by remember { mutableStateOf(false) }
    var confirmDelete by remember { mutableStateOf(false) }
    val scrollBehavior = TopAppBarDefaults.pinnedScrollBehavior()

    Scaffold(
        modifier = Modifier.nestedScroll(scrollBehavior.nestedScrollConnection),
        topBar = {
            TopAppBar(
                title = {},
                scrollBehavior = scrollBehavior,
                navigationIcon = {
                    IconButton(onClick = onBack) { Icon(Icons.AutoMirrored.Rounded.ArrowBack, "Назад") }
                },
                actions = {
                    if (brew != null) {
                        val fav = brew.favorite
                        val scale by animateFloatAsState(
                            if (fav) 1.15f else 1f,
                            spring(dampingRatio = Spring.DampingRatioHighBouncy, stiffness = Spring.StiffnessMedium),
                            label = "fav",
                        )
                        IconButton(onClick = { vm.toggleFavorite(brew.id) }) {
                            Crossfade(fav, label = "fav-icon") { f ->
                                Icon(
                                    if (f) Icons.Rounded.Favorite else Icons.Rounded.FavoriteBorder,
                                    contentDescription = "Избранное",
                                    tint = if (f) MaterialTheme.colorScheme.secondary else MaterialTheme.colorScheme.onSurfaceVariant,
                                    modifier = Modifier.graphicsLayer { scaleX = scale; scaleY = scale },
                                )
                            }
                        }
                        IconButton(onClick = onEdit) { Icon(Icons.Rounded.Edit, "Изменить") }
                        Box {
                            IconButton(onClick = { menu = true }) { Icon(Icons.Rounded.MoreVert, "Ещё") }
                            DropdownMenu(expanded = menu, onDismissRequest = { menu = false }) {
                                DropdownMenuItem(
                                    text = { Text("Поделиться") },
                                    leadingIcon = { Icon(Icons.Rounded.Share, null) },
                                    onClick = {
                                        menu = false
                                        val send = Intent(Intent.ACTION_SEND).apply {
                                            type = "text/plain"
                                            putExtra(Intent.EXTRA_TEXT, shareText(brew))
                                        }
                                        context.startActivity(Intent.createChooser(send, "Поделиться рецептом"))
                                    },
                                )
                                DropdownMenuItem(
                                    text = { Text("Удалить") },
                                    leadingIcon = { Icon(Icons.Rounded.DeleteOutline, null) },
                                    onClick = { menu = false; confirmDelete = true },
                                )
                            }
                        }
                    }
                },
                colors = TopAppBarDefaults.topAppBarColors(
                    containerColor = MaterialTheme.colorScheme.background,
                    scrolledContainerColor = MaterialTheme.colorScheme.surfaceContainer,
                ),
            )
        },
        floatingActionButton = {
            if (brew != null) {
                ExtendedFloatingActionButton(
                    onClick = onBrewAgain,
                    icon = { Icon(Icons.Rounded.PlayArrow, null) },
                    text = { Text("Заварить по рецепту") },
                    containerColor = MaterialTheme.colorScheme.primary,
                    contentColor = MaterialTheme.colorScheme.onPrimary,
                )
            }
        },
    ) { padding ->
        if (brew == null) {
            Box(Modifier.fillMaxSize().padding(padding), contentAlignment = Alignment.Center) {
                Text("Рецепт не найден", color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
        } else LazyColumn(
            contentPadding = PaddingValues(
                start = 16.dp,
                end = 16.dp,
                top = padding.calculateTopPadding(),
                bottom = padding.calculateBottomPadding() + 96.dp,
            ),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            item { Header(brew) }
            item { Stats(brew) }
            item { PoursSection(brew) }
            if (brew.dripper.isNotBlank() || brew.filter.isNotBlank() || brew.grind.isNotBlank() || brew.waterTempC != null) {
                item { EquipmentSection(brew) }
            }
            if (!brew.bean.isEmpty) item { BeanSection(brew) }
            if (brew.comment.isNotBlank()) {
                item {
                    SectionCard("Комментарий", Icons.AutoMirrored.Rounded.Notes) {
                        Text(brew.comment, style = MaterialTheme.typography.bodyLarge)
                    }
                }
            }
        }
    }

    if (confirmDelete && brew != null) {
        ConfirmDialog(
            title = "Удалить рецепт?",
            text = "«${brew.title}» будет удалён без возможности восстановления.",
            confirm = "Удалить",
            icon = Icons.Rounded.DeleteOutline,
            onConfirm = {
                vm.delete(brew.id)
                onBack()
            },
            onDismiss = { confirmDelete = false },
        )
    }
}

@Composable
private fun Header(brew: Brew) {
    Column(Modifier.padding(horizontal = 4.dp, vertical = 4.dp)) {
        Text(brew.title, style = MaterialTheme.typography.headlineMedium)
        Spacer(Modifier.height(4.dp))
        Text(
            formatDate(brew.brewDate),
            style = MaterialTheme.typography.bodyMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
        if (brew.rating > 0) {
            Spacer(Modifier.height(10.dp))
            RatingStars(brew.rating, size = 24.dp)
        }
    }
}

@Composable
private fun Stats(brew: Brew) {
    Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            StatTile("Общее время", formatClock(brew.totalTimeMs), Icons.Rounded.Timer, Modifier.weight(1f))
            StatTile("Вливаний", "${brew.pours.size}", Icons.Rounded.WaterDrop, Modifier.weight(1f))
        }
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            StatTile("Кофе", brew.coffeeG?.let { "${formatGrams(it)} г" } ?: "—", Icons.Rounded.Coffee, Modifier.weight(1f))
            StatTile("Вода", brew.effectiveWaterG?.let { "$it г" } ?: "—", Icons.Rounded.Opacity, Modifier.weight(1f))
            StatTile("Пропорция", brew.ratio?.let { formatRatio(it) } ?: "—", Icons.Rounded.Balance, Modifier.weight(1f))
        }
    }
}

@Composable
private fun PoursSection(brew: Brew) {
    SectionCard("Вливания", Icons.Rounded.Timeline, trailing = {
        Text(
            poursText(brew.pours.size),
            style = MaterialTheme.typography.labelMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
    }) {
        brew.pours.forEachIndexed { i, p ->
            TimelineRow(
                first = i == 0,
                last = false,
                title = pourName(i),
                time = "${formatClock(p.startMs)} · ${formatSeconds(brew.pourDurationMs(i))}",
                amount = p.waterG?.let { "+$it г" },
                total = brew.cumulativeWater(i)?.let { "$it г" },
            )
        }
        TimelineRow(
            first = false,
            last = true,
            title = "Финиш",
            time = formatClock(brew.totalTimeMs),
            amount = null,
            total = null,
        )
    }
}

@Composable
private fun TimelineRow(first: Boolean, last: Boolean, title: String, time: String, amount: String?, total: String?) {
    val line = MaterialTheme.colorScheme.outlineVariant
    val dot = if (last) MaterialTheme.colorScheme.secondary else MaterialTheme.colorScheme.primary
    Row(Modifier.height(IntrinsicSize.Min)) {
        Box(Modifier.width(20.dp).fillMaxHeight(), contentAlignment = Alignment.TopCenter) {
            Column(Modifier.fillMaxHeight(), horizontalAlignment = Alignment.CenterHorizontally) {
                Box(Modifier.width(2.dp).height(10.dp).background(if (first) androidx.compose.ui.graphics.Color.Transparent else line))
                Box(Modifier.size(10.dp).clip(CircleShape).background(dot))
                Box(Modifier.width(2.dp).weight(1f).background(if (last) androidx.compose.ui.graphics.Color.Transparent else line))
            }
        }
        Spacer(Modifier.width(12.dp))
        Column(Modifier.weight(1f).padding(vertical = 4.dp)) {
            Text(title, style = MaterialTheme.typography.bodyLarge)
            Text(
                time,
                style = MaterialTheme.typography.bodySmall.merge(TabularNumbers),
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
            Spacer(Modifier.height(8.dp))
        }
        Column(horizontalAlignment = Alignment.End, modifier = Modifier.padding(top = 4.dp)) {
            if (amount != null) Text(amount, style = MaterialTheme.typography.bodyLarge.merge(TabularNumbers))
            if (total != null) {
                Text(
                    "всего $total",
                    style = MaterialTheme.typography.bodySmall.merge(TabularNumbers),
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        }
    }
}

@Composable
private fun EquipmentSection(brew: Brew) {
    SectionCard("Заварка", Icons.Rounded.CoffeeMaker) {
        if (brew.dripper.isNotBlank()) InfoRow(Icons.Rounded.FilterAlt, "Воронка", brew.dripper)
        if (brew.filter.isNotBlank()) InfoRow(Icons.Rounded.Layers, "Фильтр", brew.filter)
        if (brew.grind.isNotBlank()) InfoRow(Icons.Rounded.Grain, "Помол", brew.grind)
        brew.waterTempC?.let { InfoRow(Icons.Rounded.Thermostat, "Температура воды", "$it °C") }
    }
}

@Composable
private fun BeanSection(brew: Brew) {
    val b = brew.bean
    SectionCard("Зерно", Icons.Rounded.Eco) {
        if (b.name.isNotBlank()) InfoRow(Icons.Rounded.Coffee, "Название", b.name)
        if (b.roaster.isNotBlank()) InfoRow(Icons.Rounded.Storefront, "Обжарщик", b.roaster)
        if (b.origin.isNotBlank()) InfoRow(Icons.Rounded.Public, "Происхождение", b.origin)
        if (b.process.isNotBlank()) InfoRow(Icons.Rounded.WaterDrop, "Обработка", b.process)
        if (b.roastLevel.isNotBlank()) InfoRow(Icons.Rounded.LocalFireDepartment, "Обжарка", b.roastLevel)
        b.roastLocalDate()?.let { date ->
            InfoRow(Icons.Rounded.CalendarMonth, "Дата обжарки", formatDate(date))
            brew.beanAgeDays?.let { age ->
                InfoRow(Icons.Rounded.HourglassBottom, "На момент заварки", daysText(age))
            }
            val today = ChronoUnit.DAYS.between(date, LocalDate.now())
            if (today != brew.beanAgeDays) InfoRow(Icons.Rounded.Today, "Сегодня", daysText(today))
        }
    }
}
