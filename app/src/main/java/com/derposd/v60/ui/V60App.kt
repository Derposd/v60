package com.derposd.v60.ui

import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.core.FastOutSlowInEasing
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.scaleIn
import androidx.compose.animation.scaleOut
import androidx.compose.animation.slideInHorizontally
import androidx.compose.animation.slideInVertically
import androidx.compose.animation.slideOutHorizontally
import androidx.compose.animation.slideOutVertically
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.consumeWindowInsets
import androidx.compose.foundation.layout.padding
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.outlined.MenuBook
import androidx.compose.material.icons.automirrored.rounded.MenuBook
import androidx.compose.material.icons.outlined.Coffee
import androidx.compose.material.icons.rounded.Coffee
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.NavigationBarItemDefaults
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewmodel.compose.viewModel
import androidx.navigation.NavGraph.Companion.findStartDestination
import androidx.navigation.NavHostController
import androidx.navigation.NavType
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.currentBackStackEntryAsState
import androidx.navigation.compose.rememberNavController
import androidx.navigation.navArgument
import com.derposd.v60.ui.screens.BrewScreen
import com.derposd.v60.ui.screens.DetailScreen
import com.derposd.v60.ui.screens.EditScreen
import com.derposd.v60.ui.screens.RecipesScreen

object Routes {
    const val BREW = "brew"
    const val RECIPES = "recipes"
    const val DETAIL = "detail/{id}"
    const val EDIT = "edit/{id}"
    const val DRAFT_ID = -1L
    fun detail(id: Long) = "detail/$id"
    fun edit(id: Long) = "edit/$id"
}

private data class Tab(val route: String, val label: String, val icon: ImageVector, val selectedIcon: ImageVector)

private val tabs = listOf(
    Tab(Routes.BREW, "Заварка", Icons.Outlined.Coffee, Icons.Rounded.Coffee),
    Tab(Routes.RECIPES, "Рецепты", Icons.AutoMirrored.Outlined.MenuBook, Icons.AutoMirrored.Rounded.MenuBook),
)

private const val NAV_ANIM = 320

@Composable
fun V60App(vm: AppViewModel = viewModel()) {
    val nav = rememberNavController()
    val session by vm.session.collectAsStateWithLifecycle()
    val entry by nav.currentBackStackEntryAsState()
    val route = entry?.destination?.route
    val showBar = (route == Routes.RECIPES) || (route == Routes.BREW && session == null)

    Scaffold(
        contentWindowInsets = WindowInsets(0),
        bottomBar = {
            AnimatedVisibility(
                visible = showBar,
                enter = slideInVertically(tween(NAV_ANIM)) { it } + fadeIn(tween(NAV_ANIM)),
                exit = slideOutVertically(tween(NAV_ANIM)) { it } + fadeOut(tween(NAV_ANIM)),
            ) {
                NavigationBar(containerColor = MaterialTheme.colorScheme.surfaceContainer) {
                    tabs.forEach { tab ->
                        val selected = route == tab.route
                        NavigationBarItem(
                            selected = selected,
                            onClick = { nav.switchTab(tab.route) },
                            icon = { Icon(if (selected) tab.selectedIcon else tab.icon, contentDescription = tab.label) },
                            label = { Text(tab.label) },
                            colors = NavigationBarItemDefaults.colors(
                                indicatorColor = MaterialTheme.colorScheme.primaryContainer,
                            ),
                        )
                    }
                }
            }
        },
    ) { padding ->
        NavHost(
            navController = nav,
            startDestination = Routes.BREW,
            modifier = Modifier.padding(padding).consumeWindowInsets(padding),
            enterTransition = {
                fadeIn(tween(NAV_ANIM)) + slideInHorizontally(tween(NAV_ANIM, easing = FastOutSlowInEasing)) { it / 10 }
            },
            exitTransition = { fadeOut(tween(NAV_ANIM / 2)) },
            popEnterTransition = { fadeIn(tween(NAV_ANIM)) + scaleIn(tween(NAV_ANIM), initialScale = 0.97f) },
            popExitTransition = {
                fadeOut(tween(NAV_ANIM / 2)) + slideOutHorizontally(tween(NAV_ANIM, easing = FastOutSlowInEasing)) { it / 10 }
            },
        ) {
            composable(
                Routes.BREW,
                enterTransition = { fadeIn(tween(NAV_ANIM)) },
                exitTransition = { fadeOut(tween(NAV_ANIM / 2)) },
            ) {
                BrewScreen(
                    vm = vm,
                    onOpenDraft = { nav.navigate(Routes.edit(Routes.DRAFT_ID)) },
                )
            }
            composable(
                Routes.RECIPES,
                enterTransition = { fadeIn(tween(NAV_ANIM)) },
                exitTransition = { fadeOut(tween(NAV_ANIM / 2)) },
                popEnterTransition = { fadeIn(tween(NAV_ANIM)) + scaleIn(tween(NAV_ANIM), initialScale = 0.97f) },
                popExitTransition = { fadeOut(tween(NAV_ANIM / 2)) + scaleOut(tween(NAV_ANIM), targetScale = 0.97f) },
            ) {
                RecipesScreen(vm = vm, onOpen = { nav.navigate(Routes.detail(it)) })
            }
            composable(Routes.DETAIL, arguments = listOf(navArgument("id") { type = NavType.LongType })) { e ->
                val id = e.arguments?.getLong("id") ?: 0L
                DetailScreen(
                    vm = vm,
                    id = id,
                    onBack = { nav.popBackStack() },
                    onEdit = { nav.navigate(Routes.edit(id)) },
                    onBrewAgain = {
                        vm.selectGuide(id)
                        nav.switchTab(Routes.BREW)
                    },
                )
            }
            composable(Routes.EDIT, arguments = listOf(navArgument("id") { type = NavType.LongType })) { e ->
                val id = e.arguments?.getLong("id") ?: Routes.DRAFT_ID
                EditScreen(
                    vm = vm,
                    id = id,
                    onClose = { nav.popBackStack() },
                    onSaved = { saved ->
                        if (id == Routes.DRAFT_ID) {
                            nav.navigate(Routes.detail(saved.id)) {
                                popUpTo(Routes.BREW)
                            }
                        } else nav.popBackStack()
                    },
                )
            }
        }
    }
}

private fun NavHostController.switchTab(route: String) {
    navigate(route) {
        popUpTo(graph.findStartDestination().id) { saveState = true }
        launchSingleTop = true
        restoreState = true
    }
}
