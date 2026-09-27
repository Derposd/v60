@file:OptIn(ExperimentalMaterial3Api::class, ExperimentalLayoutApi::class)

package com.derposd.v60.ui.screens

import androidx.compose.animation.*
import androidx.compose.animation.core.*
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.rounded.*
import androidx.compose.material.icons.rounded.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.input.nestedscroll.nestedScroll
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.derposd.v60.data.Brew
import com.derposd.v60.data.formatClock
import com.derposd.v60.data.formatGrams
import com.derposd.v60.data.formatRatio
import com.derposd.v60.data.formatShortDate
import com.derposd.v60.ui.AppViewModel
import com.derposd.v60.ui.components.EmptyState
import com.derposd.v60.ui.components.RatingStars
import com.derposd.v60.ui.components.StatPill
import com.derposd.v60.ui.components.pressScale

@Composable
fun RecipesScreen(vm: AppViewModel, onOpen: (Long) -> Unit) {
    val brews by vm.brews.collectAsStateWithLifecycle()
    var query by rememberSaveable { mutableStateOf("") }
    var onlyFavorites by rememberSaveable { mutableStateOf(false) }
    val scrollBehavior = TopAppBarDefaults.exitUntilCollapsedScrollBehavior()

    val filtered = remember(brews, query, onlyFavorites) {
        val q = query.trim().lowercase()
        brews.filter { b ->
            (!onlyFavorites || b.favorite) && (q.isEmpty() || listOf(
                b.name, b.dripper, b.grind, b.comment,
                b.bean.name, b.bean.roaster, b.bean.origin, b.bean.process,
            ).any { it.lowercase().contains(q) })
        }
    }

    Scaffold(
        modifier = Modifier.nestedScroll(scrollBehavior.nestedScrollConnection),
        topBar = {
            LargeTopAppBar(
                title = { Text("Рецепты") },
                scrollBehavior = scrollBehavior,
                colors = TopAppBarDefaults.topAppBarColors(
                    containerColor = MaterialTheme.colorScheme.background,
                    scrolledContainerColor = MaterialTheme.colorScheme.surfaceContainer,
                ),
            )
        },
    ) { padding ->
        LazyColumn(
            contentPadding = PaddingValues(
                start = 16.dp,
                end = 16.dp,
                top = padding.calculateTopPadding(),
                bottom = padding.calculateBottomPadding() + 24.dp,
            ),
            verticalArrangement = Arrangement.spacedBy(10.dp),
            modifier = Modifier.fillMaxSize(),
        ) {
            if (brews.isNotEmpty()) {
                item(key = "search") {
                    Column {
                        TextField(
                            value = query,
                            onValueChange = { query = it },
                            placeholder = { Text("Поиск: зерно, обжарщик, заметки") },
                            leadingIcon = { Icon(Icons.Rounded.Search, null) },
                            trailingIcon = {
                                AnimatedVisibility(query.isNotEmpty(), enter = fadeIn() + scaleIn(), exit = fadeOut() + scaleOut()) {
                                    IconButton(onClick = { query = "" }) { Icon(Icons.Rounded.Close, "Очистить") }
                                }
                            },
                            singleLine = true,
                            shape = CircleShape,
                            colors = TextFieldDefaults.colors(
                                focusedContainerColor = MaterialTheme.colorScheme.surfaceContainerHigh,
                                unfocusedContainerColor = MaterialTheme.colorScheme.surfaceContainerHigh,
                                focusedIndicatorColor = androidx.compose.ui.graphics.Color.Transparent,
                                unfocusedIndicatorColor = androidx.compose.ui.graphics.Color.Transparent,
                            ),
                            modifier = Modifier.fillMaxWidth(),
                        )
                        Spacer(Modifier.height(10.dp))
                        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            FilterChip(
                                selected = !onlyFavorites,
                                onClick = { onlyFavorites = false },
                                label = { Text("Все · ${brews.size}") },
                            )
                            FilterChip(
                                selected = onlyFavorites,
                                onClick = { onlyFavorites = true },
                                label = { Text("Избранные") },
                                leadingIcon = { Icon(Icons.Rounded.Favorite, null, Modifier.size(FilterChipDefaults.IconSize)) },
                            )
                        }
                    }
                }
            }

            items(filtered, key = { it.id }) { brew ->
                BrewCard(
                    brew = brew,
                    onClick = { onOpen(brew.id) },
                    modifier = Modifier.animateItem(),
                )
            }

            if (filtered.isEmpty()) {
                item(key = "empty") {
                    EmptyState(
                        icon = if (brews.isEmpty()) Icons.AutoMirrored.Rounded.MenuBook else Icons.Rounded.SearchOff,
                        title = if (brews.isEmpty()) "Пока пусто" else "Ничего не найдено",
                        text = if (brews.isEmpty()) "Заварите кофе с таймером и опишите результат — рецепт появится здесь."
                        else "Попробуйте изменить запрос или фильтр.",
                        modifier = Modifier.animateItem().padding(top = 48.dp),
                    )
                }
            }
        }
    }
}

@Composable
fun BrewCard(brew: Brew, onClick: () -> Unit, modifier: Modifier = Modifier) {
    val interaction = remember { MutableInteractionSource() }
    Surface(
        onClick = onClick,
        interactionSource = interaction,
        shape = MaterialTheme.shapes.large,
        color = MaterialTheme.colorScheme.surfaceContainerLow,
        modifier = modifier.fillMaxWidth().pressScale(interaction, 0.98f),
    ) {
        Column(Modifier.padding(18.dp)) {
            Row(verticalAlignment = Alignment.Top) {
                Column(Modifier.weight(1f)) {
                    Text(brew.title, style = MaterialTheme.typography.titleMedium, maxLines = 1, overflow = TextOverflow.Ellipsis)
                    val sub = listOf(
                        brew.bean.name.takeIf { it != brew.title },
                        brew.bean.roaster,
                        brew.dripper,
                    ).filterNot { it.isNullOrBlank() }.joinToString(" · ")
                    if (sub.isNotEmpty()) {
                        Text(
                            sub,
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis,
                        )
                    }
                }
                if (brew.favorite) {
                    Icon(
                        Icons.Rounded.Favorite, "Избранное",
                        tint = MaterialTheme.colorScheme.secondary,
                        modifier = Modifier.size(20.dp),
                    )
                }
            }
            Spacer(Modifier.height(12.dp))
            FlowRow(horizontalArrangement = Arrangement.spacedBy(6.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                StatPill(Icons.Rounded.Timer, formatClock(brew.totalTimeMs))
                StatPill(Icons.Rounded.WaterDrop, "${brew.pours.size}×" + (brew.effectiveWaterG?.let { " · $it г" } ?: ""))
                brew.coffeeG?.let { StatPill(Icons.Rounded.Coffee, "${formatGrams(it)} г") }
                brew.ratio?.let { StatPill(Icons.Rounded.Balance, formatRatio(it)) }
                brew.waterTempC?.let { StatPill(Icons.Rounded.Thermostat, "$it°") }
            }
            Spacer(Modifier.height(12.dp))
            Row(verticalAlignment = Alignment.CenterVertically) {
                if (brew.rating > 0) RatingStars(brew.rating, size = 16.dp)
                Spacer(Modifier.weight(1f))
                Text(
                    formatShortDate(brew.brewDate),
                    style = MaterialTheme.typography.labelMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        }
    }
}
