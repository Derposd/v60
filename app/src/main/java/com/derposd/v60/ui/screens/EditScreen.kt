@file:OptIn(ExperimentalMaterial3Api::class)

package com.derposd.v60.ui.screens

import androidx.compose.animation.*
import androidx.compose.animation.core.*
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.PressInteraction
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.rounded.*
import androidx.compose.material.icons.rounded.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.input.nestedscroll.nestedScroll
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.derposd.v60.data.Bean
import com.derposd.v60.data.Brew
import com.derposd.v60.data.Pour
import com.derposd.v60.data.daysText
import com.derposd.v60.data.formatClock
import com.derposd.v60.data.formatDate
import com.derposd.v60.data.formatGrams
import com.derposd.v60.data.formatRatio
import com.derposd.v60.data.formatSeconds
import com.derposd.v60.data.parseDecimal
import com.derposd.v60.data.parseInt
import com.derposd.v60.data.pourName
import com.derposd.v60.data.poursText
import com.derposd.v60.ui.AppViewModel
import com.derposd.v60.ui.Routes
import com.derposd.v60.ui.components.ConfirmDialog
import com.derposd.v60.ui.components.RatingStars
import com.derposd.v60.ui.components.SectionCard
import com.derposd.v60.ui.theme.TabularNumbers
import java.time.Instant
import java.time.LocalDate
import java.time.ZoneOffset
import java.time.temporal.ChronoUnit

/** Состояние формы — строки, чтобы поля можно было свободно редактировать. */
@Stable
private class BrewForm(b: Brew) {
    var name by mutableStateOf(b.name)
    var dripper by mutableStateOf(b.dripper)
    var filter by mutableStateOf(b.filter)
    var grind by mutableStateOf(b.grind)
    var temp by mutableStateOf(b.waterTempC?.toString().orEmpty())
    var coffee by mutableStateOf(b.coffeeG?.let(::formatGrams).orEmpty())
    var water by mutableStateOf(b.waterG?.toString().orEmpty())
    val pours = mutableStateListOf<String>().apply { addAll(b.pours.map { it.waterG?.toString().orEmpty() }) }

    var beanName by mutableStateOf(b.bean.name)
    var roaster by mutableStateOf(b.bean.roaster)
    var origin by mutableStateOf(b.bean.origin)
    var process by mutableStateOf(b.bean.process)
    var roastLevel by mutableStateOf(b.bean.roastLevel)
    var roastDate by mutableStateOf(b.bean.roastDate)

    var rating by mutableIntStateOf(b.rating)
    var comment by mutableStateOf(b.comment)

    val pouredSum: Int?
        get() = pours.mapNotNull { parseInt(it) }.takeIf { it.isNotEmpty() }?.sum()

    val totalWater: Int? get() = parseInt(water) ?: pouredSum

    val ratio: Double?
        get() {
            val c = parseDecimal(coffee) ?: return null
            val w = totalWater ?: return null
            return if (c > 0 && w > 0) w / c else null
        }

    fun applyBean(bean: Bean) {
        beanName = bean.name
        roaster = bean.roaster
        origin = bean.origin
        process = bean.process
        roastLevel = bean.roastLevel
        roastDate = bean.roastDate
    }

    fun toBrew(base: Brew): Brew = base.copy(
        name = name.trim(),
        dripper = dripper.trim(),
        filter = filter.trim(),
        grind = grind.trim(),
        waterTempC = parseInt(temp),
        coffeeG = parseDecimal(coffee)?.takeIf { it > 0 },
        waterG = parseInt(water)?.takeIf { it > 0 },
        pours = base.pours.mapIndexed { i, p -> Pour(p.startMs, pours.getOrNull(i)?.let { parseInt(it) }) },
        bean = Bean(
            name = beanName.trim(),
            roaster = roaster.trim(),
            origin = origin.trim(),
            process = process.trim(),
            roastLevel = roastLevel.trim(),
            roastDate = roastDate,
        ),
        rating = rating,
        comment = comment.trim(),
    )
}

@Composable
fun EditScreen(vm: AppViewModel, id: Long, onClose: () -> Unit, onSaved: (Brew) -> Unit) {
    val isDraft = id == Routes.DRAFT_ID
    val draft by vm.draft.collectAsStateWithLifecycle()
    val brews by vm.brews.collectAsStateWithLifecycle()
    val base = remember(id) { if (isDraft) draft else vm.brew(id) }

    if (base == null) {
        LaunchedEffect(Unit) { onClose() }
        return
    }

    val form = remember(id) { BrewForm(base) }
    var confirmDiscard by remember { mutableStateOf(false) }
    var saving by remember { mutableStateOf(false) }
    val scrollBehavior = TopAppBarDefaults.pinnedScrollBehavior()

    val save: () -> Unit = {
        if (!saving) {
            saving = true
            vm.save(form.toBrew(base), fromDraft = isDraft, onSaved = onSaved)
        }
    }

    Scaffold(
        modifier = Modifier.nestedScroll(scrollBehavior.nestedScrollConnection),
        topBar = {
            TopAppBar(
                title = { Text(if (isDraft) "Новая запись" else "Редактирование") },
                scrollBehavior = scrollBehavior,
                navigationIcon = {
                    IconButton(onClick = onClose) { Icon(Icons.Rounded.Close, "Закрыть") }
                },
                actions = {
                    if (isDraft) {
                        IconButton(onClick = { confirmDiscard = true }) {
                            Icon(Icons.Rounded.DeleteOutline, "Не сохранять")
                        }
                    }
                    TextButton(onClick = save) { Text("Сохранить") }
                },
                colors = TopAppBarDefaults.topAppBarColors(
                    containerColor = MaterialTheme.colorScheme.background,
                    scrolledContainerColor = MaterialTheme.colorScheme.surfaceContainer,
                ),
            )
        },
    ) { padding ->
        Column(
            Modifier
                .fillMaxSize()
                .padding(padding)
                .imePadding()
                .verticalScroll(rememberScrollState())
                .padding(horizontal = 16.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            if (isDraft) DoneHeader(base)

            PoursEditor(base, form)
            BrewEditor(form, brews)
            BeanEditor(form, brews, base)
            RatingEditor(form)

            Button(
                onClick = save,
                shape = RoundedCornerShape(20.dp),
                modifier = Modifier.fillMaxWidth().height(58.dp),
            ) {
                Icon(Icons.Rounded.Check, null)
                Spacer(Modifier.width(10.dp))
                Text("Сохранить рецепт", style = MaterialTheme.typography.titleSmall)
            }
            Spacer(Modifier.height(20.dp))
        }
    }

    if (confirmDiscard) {
        ConfirmDialog(
            title = "Не сохранять заварку?",
            text = "Время и вливания будут удалены.",
            confirm = "Удалить",
            icon = Icons.Rounded.DeleteOutline,
            onConfirm = {
                vm.discardDraft()
                onClose()
            },
            onDismiss = { confirmDiscard = false },
        )
    }
}

@Composable
private fun DoneHeader(brew: Brew) {
    var appeared by remember { mutableStateOf(false) }
    LaunchedEffect(Unit) { appeared = true }
    val scale by animateFloatAsState(
        if (appeared) 1f else 0.6f,
        spring(dampingRatio = Spring.DampingRatioMediumBouncy, stiffness = Spring.StiffnessLow),
        label = "done",
    )
    Surface(
        shape = MaterialTheme.shapes.large,
        color = MaterialTheme.colorScheme.primaryContainer,
        contentColor = MaterialTheme.colorScheme.onPrimaryContainer,
        modifier = Modifier.fillMaxWidth(),
    ) {
        Row(Modifier.padding(20.dp), verticalAlignment = Alignment.CenterVertically) {
            Icon(
                Icons.Rounded.CheckCircle, null,
                tint = MaterialTheme.colorScheme.primary,
                modifier = Modifier.size(44.dp).graphicsLayer { scaleX = scale; scaleY = scale },
            )
            Spacer(Modifier.width(16.dp))
            Column {
                Text("Готово!", style = MaterialTheme.typography.labelLarge)
                Text(
                    formatClock(brew.totalTimeMs),
                    style = MaterialTheme.typography.headlineMedium.merge(TabularNumbers),
                )
                Text(poursText(brew.pours.size), style = MaterialTheme.typography.bodyMedium)
            }
        }
    }
}

@Composable
private fun PoursEditor(base: Brew, form: BrewForm) {
    SectionCard("Вливания", Icons.Rounded.Timeline, trailing = {
        AnimatedContent(form.pouredSum, label = "sum") { sum ->
            Text(
                sum?.let { "всего $it г" } ?: "",
                style = MaterialTheme.typography.labelLarge.merge(TabularNumbers),
                color = MaterialTheme.colorScheme.secondary,
            )
        }
    }) {
        Text(
            "Время записано таймером. Укажите, сколько воды было в каждом вливании.",
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
        Spacer(Modifier.height(8.dp))
        base.pours.forEachIndexed { i, p ->
            Row(Modifier.fillMaxWidth().padding(vertical = 4.dp), verticalAlignment = Alignment.CenterVertically) {
                Column(Modifier.weight(1f)) {
                    Text(pourName(i), style = MaterialTheme.typography.bodyLarge)
                    Text(
                        "${formatClock(p.startMs)} · ${formatSeconds(base.pourDurationMs(i))}",
                        style = MaterialTheme.typography.bodySmall.merge(TabularNumbers),
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }
                OutlinedTextField(
                    value = form.pours.getOrElse(i) { "" },
                    onValueChange = { v -> if (i < form.pours.size) form.pours[i] = v.filter { it.isDigit() } },
                    placeholder = { Text("0") },
                    suffix = { Text("г") },
                    singleLine = true,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number, imeAction = ImeAction.Next),
                    shape = MaterialTheme.shapes.medium,
                    textStyle = MaterialTheme.typography.bodyLarge.merge(TabularNumbers),
                    modifier = Modifier.width(112.dp),
                )
            }
        }
        HorizontalDivider(Modifier.padding(vertical = 8.dp), color = MaterialTheme.colorScheme.outlineVariant)
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text("Общее время", style = MaterialTheme.typography.bodyLarge, modifier = Modifier.weight(1f))
            Text(formatClock(base.totalTimeMs), style = MaterialTheme.typography.titleMedium.merge(TabularNumbers))
        }
    }
}

@Composable
private fun BrewEditor(form: BrewForm, brews: List<Brew>) {
    val drippers = remember(brews) {
        (brews.map { it.dripper } + listOf("V60 01", "V60 02", "V60 03"))
            .filter { it.isNotBlank() }.distinctBy { it.lowercase() }.take(8)
    }
    val filters = remember(brews) {
        brews.map { it.filter }.filter { it.isNotBlank() }.distinctBy { it.lowercase() }.take(6)
    }
    SectionCard("Заварка", Icons.Rounded.CoffeeMaker, trailing = {
        AnimatedVisibility(form.ratio != null, enter = fadeIn() + scaleIn(), exit = fadeOut() + scaleOut()) {
            Text(
                form.ratio?.let { formatRatio(it) }.orEmpty(),
                style = MaterialTheme.typography.labelLarge.merge(TabularNumbers),
                color = MaterialTheme.colorScheme.secondary,
            )
        }
    }) {
        Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
            Field(form.name, { form.name = it }, "Название рецепта", placeholder = "Например, «Утренний V60»")
            Field(form.dripper, { form.dripper = it }, "Воронка", icon = Icons.Rounded.FilterAlt)
            Suggestions(drippers, form.dripper) { form.dripper = it }
            Field(form.filter, { form.filter = it }, "Фильтр", icon = Icons.Rounded.Layers)
            if (filters.isNotEmpty()) Suggestions(filters, form.filter) { form.filter = it }
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                Field(
                    form.coffee, { form.coffee = it }, "Кофе",
                    keyboard = KeyboardType.Decimal, suffix = "г", icon = Icons.Rounded.Coffee,
                    modifier = Modifier.weight(1f),
                )
                Field(
                    form.water, { v -> form.water = v.filter { it.isDigit() } }, "Вода",
                    keyboard = KeyboardType.Number, suffix = "г", icon = Icons.Rounded.Opacity,
                    placeholder = form.pouredSum?.toString(),
                    modifier = Modifier.weight(1f),
                )
            }
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                Field(
                    form.grind, { form.grind = it }, "Помол",
                    icon = Icons.Rounded.Grain, modifier = Modifier.weight(1f),
                )
                Field(
                    form.temp, { v -> form.temp = v.filter { it.isDigit() } }, "Температура",
                    keyboard = KeyboardType.Number, suffix = "°C", icon = Icons.Rounded.Thermostat,
                    modifier = Modifier.weight(1f),
                )
            }
        }
    }
}

@Composable
private fun BeanEditor(form: BrewForm, brews: List<Brew>, base: Brew) {
    val previous = remember(brews) {
        brews.map { it.bean }
            .filter { it.name.isNotBlank() }
            .distinctBy { it.name.lowercase() to it.roaster.lowercase() }
            .take(10)
    }
    var showDate by remember { mutableStateOf(false) }

    SectionCard("Зерно", Icons.Rounded.Eco) {
        if (previous.isNotEmpty()) {
            Text(
                "Из прошлых заварок",
                style = MaterialTheme.typography.labelMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
            Spacer(Modifier.height(6.dp))
            LazyRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                items(previous) { bean ->
                    val selected = bean.name == form.beanName && bean.roaster == form.roaster
                    FilterChip(
                        selected = selected,
                        onClick = { form.applyBean(bean) },
                        label = {
                            Text(
                                listOf(bean.name, bean.roaster).filter { it.isNotBlank() }.joinToString(" · "),
                                maxLines = 1,
                                overflow = TextOverflow.Ellipsis,
                            )
                        },
                        leadingIcon = { Icon(Icons.Rounded.Coffee, null, Modifier.size(FilterChipDefaults.IconSize)) },
                    )
                }
            }
            Spacer(Modifier.height(12.dp))
        }
        Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
            Field(form.beanName, { form.beanName = it }, "Название / сорт", icon = Icons.Rounded.Coffee)
            Field(form.roaster, { form.roaster = it }, "Обжарщик", icon = Icons.Rounded.Storefront)
            Field(form.origin, { form.origin = it }, "Страна, регион, ферма", icon = Icons.Rounded.Public)
            Field(form.process, { form.process = it }, "Обработка", icon = Icons.Rounded.WaterDrop)
            Suggestions(listOf("Мытая", "Натуральная", "Хани", "Анаэробная"), form.process) { form.process = it }

            Text(
                "Степень обжарки",
                style = MaterialTheme.typography.labelMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(top = 4.dp),
            )
            Suggestions(listOf("Светлая", "Средняя", "Тёмная", "Омни"), form.roastLevel) { form.roastLevel = it }

            val roast = form.roastDate.takeIf { it.isNotBlank() }?.let { runCatching { LocalDate.parse(it) }.getOrNull() }
            val src = remember { MutableInteractionSource() }
            LaunchedEffect(src) {
                src.interactions.collect { if (it is PressInteraction.Release) showDate = true }
            }
            OutlinedTextField(
                value = roast?.let { formatDate(it) }.orEmpty(),
                onValueChange = {},
                readOnly = true,
                label = { Text("Дата обжарки") },
                leadingIcon = { Icon(Icons.Rounded.CalendarMonth, null) },
                trailingIcon = roast?.let {
                    @Composable { IconButton(onClick = { form.roastDate = "" }) { Icon(Icons.Rounded.Close, "Очистить") } }
                },
                supportingText = roast?.let { date ->
                    @Composable {
                        val atBrew = ChronoUnit.DAYS.between(date, base.brewDate)
                        Text("${daysText(atBrew)} от обжарки на момент заварки")
                    }
                },
                interactionSource = src,
                shape = MaterialTheme.shapes.medium,
                modifier = Modifier.fillMaxWidth(),
            )
        }
    }

    if (showDate) {
        val initial = form.roastDate.takeIf { it.isNotBlank() }?.let { runCatching { LocalDate.parse(it) }.getOrNull() }
            ?: LocalDate.now()
        val state = rememberDatePickerState(
            initialSelectedDateMillis = initial.atStartOfDay(ZoneOffset.UTC).toInstant().toEpochMilli(),
        )
        DatePickerDialog(
            onDismissRequest = { showDate = false },
            confirmButton = {
                TextButton(onClick = {
                    state.selectedDateMillis?.let {
                        form.roastDate = Instant.ofEpochMilli(it).atZone(ZoneOffset.UTC).toLocalDate().toString()
                    }
                    showDate = false
                }) { Text("Готово") }
            },
            dismissButton = { TextButton(onClick = { showDate = false }) { Text("Отмена") } },
        ) {
            DatePicker(state = state, showModeToggle = false)
        }
    }
}

@Composable
private fun RatingEditor(form: BrewForm) {
    SectionCard("Оценка и комментарий", Icons.Rounded.Star) {
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.Center) {
            RatingStars(form.rating, size = 40.dp, onChange = { form.rating = it })
        }
        Spacer(Modifier.height(4.dp))
        AnimatedContent(form.rating, label = "rating-text", modifier = Modifier.fillMaxWidth()) { r ->
            Text(
                when (r) {
                    1 -> "Не получилось"
                    2 -> "Так себе"
                    3 -> "Нормально"
                    4 -> "Хорошо"
                    5 -> "Идеально"
                    else -> "Нажмите на звезду"
                },
                style = MaterialTheme.typography.labelLarge,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                textAlign = androidx.compose.ui.text.style.TextAlign.Center,
                modifier = Modifier.fillMaxWidth(),
            )
        }
        Spacer(Modifier.height(12.dp))
        OutlinedTextField(
            value = form.comment,
            onValueChange = { form.comment = it },
            label = { Text("Комментарий") },
            placeholder = { Text("Вкус, кислотность, сладость, что поменять в следующий раз…") },
            minLines = 4,
            keyboardOptions = KeyboardOptions(capitalization = KeyboardCapitalization.Sentences),
            shape = MaterialTheme.shapes.medium,
            modifier = Modifier.fillMaxWidth(),
        )
    }
}

@Composable
private fun Field(
    value: String,
    onChange: (String) -> Unit,
    label: String,
    modifier: Modifier = Modifier.fillMaxWidth(),
    keyboard: KeyboardType = KeyboardType.Text,
    suffix: String? = null,
    placeholder: String? = null,
    icon: ImageVector? = null,
) {
    OutlinedTextField(
        value = value,
        onValueChange = onChange,
        label = { Text(label, maxLines = 1, overflow = TextOverflow.Ellipsis) },
        placeholder = placeholder?.let { p -> @Composable { Text(p) } },
        suffix = suffix?.let { x -> @Composable { Text(x) } },
        leadingIcon = icon?.let { i -> @Composable { Icon(i, null) } },
        singleLine = true,
        keyboardOptions = KeyboardOptions(
            keyboardType = keyboard,
            capitalization = if (keyboard == KeyboardType.Text) KeyboardCapitalization.Sentences else KeyboardCapitalization.None,
            imeAction = ImeAction.Next,
        ),
        shape = MaterialTheme.shapes.medium,
        modifier = modifier,
    )
}

@Composable
private fun Suggestions(options: List<String>, current: String, onPick: (String) -> Unit) {
    LazyRow(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
        items(options) { o ->
            val selected = o.equals(current.trim(), ignoreCase = true)
            FilterChip(
                selected = selected,
                onClick = { onPick(if (selected) "" else o) },
                label = { Text(o) },
            )
        }
    }
}
