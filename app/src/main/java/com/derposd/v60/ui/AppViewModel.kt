package com.derposd.v60.ui

import android.app.Application
import android.os.SystemClock
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.derposd.v60.data.ActiveSession
import com.derposd.v60.data.Bean
import com.derposd.v60.data.Brew
import com.derposd.v60.data.BrewGuide
import com.derposd.v60.data.BrewRepository
import com.derposd.v60.data.Pour
import com.derposd.v60.data.SessionStore
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch

class AppViewModel(app: Application) : AndroidViewModel(app) {

    private val repo = BrewRepository(app)
    private val store = SessionStore(app)

    val brews: StateFlow<List<Brew>> = repo.brews

    private val _session = MutableStateFlow(store.loadSession())
    val session: StateFlow<ActiveSession?> = _session.asStateFlow()

    private val _draft = MutableStateFlow(store.loadDraft())
    val draft: StateFlow<Brew?> = _draft.asStateFlow()

    /** Рецепт, выбранный как подсказка для следующей заварки. */
    private val _guideId = MutableStateFlow(store.guideId?.takeIf { repo.get(it) != null })
    val guideId: StateFlow<Long?> = _guideId.asStateFlow()

    private val _dose = MutableStateFlow(_guideId.value?.let { repo.get(it)?.coffeeG })
    val dose: StateFlow<Double?> = _dose.asStateFlow()

    /** Подсказка для экрана до старта (выбранный рецепт + доза). */
    val plannedGuide: StateFlow<BrewGuide?> =
        combine(brews, _guideId, _dose) { list, id, dose ->
            id?.let { gid -> list.firstOrNull { it.id == gid } }?.let { BrewGuide(it, dose) }
        }.stateIn(viewModelScope, SharingStarted.Eagerly, null)

    /** Подсказка для идущей заварки. */
    val activeGuide: StateFlow<BrewGuide?> =
        combine(brews, _session) { list, s ->
            s?.guideId?.let { gid -> list.firstOrNull { it.id == gid } }?.let { BrewGuide(it, s.guideDose) }
        }.stateIn(viewModelScope, SharingStarted.Eagerly, null)

    fun brew(id: Long): Brew? = repo.get(id)

    // ---------- Подготовка ----------

    fun selectGuide(id: Long?) {
        _guideId.value = id
        store.guideId = id
        _dose.value = id?.let { repo.get(it)?.coffeeG }
    }

    fun changeDose(delta: Double) {
        val current = _dose.value ?: return
        _dose.value = (current + delta).coerceIn(1.0, 100.0)
    }

    // ---------- Таймер ----------

    fun start() {
        if (_session.value != null) return
        val s = ActiveSession(
            startElapsed = SystemClock.elapsedRealtime(),
            startWall = System.currentTimeMillis(),
            pourStarts = listOf(0L),
            guideId = _guideId.value,
            guideDose = _dose.value,
        )
        setSession(s)
    }

    fun nextPour() {
        val s = _session.value ?: return
        val elapsed = SystemClock.elapsedRealtime() - s.startElapsed
        // Защита от случайного двойного нажатия.
        if (elapsed - s.pourStarts.last() < 600) return
        setSession(s.copy(pourStarts = s.pourStarts + elapsed))
    }

    fun undoPour() {
        val s = _session.value ?: return
        if (s.pourStarts.size > 1) setSession(s.copy(pourStarts = s.pourStarts.dropLast(1)))
    }

    fun cancelBrew() = setSession(null)

    /** Останавливает таймер и готовит черновик для описания. */
    fun finishBrew(): Boolean {
        val s = _session.value ?: return false
        if (!s.canFinish) return false
        val total = SystemClock.elapsedRealtime() - s.startElapsed
        val recipe = s.guideId?.let { repo.get(it) }
        val guide = recipe?.let { BrewGuide(it, s.guideDose) }
        val last = brews.value.firstOrNull()
        val draft = Brew(
            id = 0,
            createdAt = s.startWall,
            name = recipe?.name.orEmpty(),
            dripper = recipe?.dripper ?: last?.dripper ?: "V60 02",
            filter = recipe?.filter ?: last?.filter.orEmpty(),
            grind = recipe?.grind.orEmpty(),
            waterTempC = recipe?.waterTempC,
            coffeeG = s.guideDose ?: recipe?.coffeeG,
            waterG = null,
            totalTimeMs = total,
            pours = s.pourStarts.mapIndexed { i, start -> Pour(start, guide?.pourWater(i)) },
            bean = recipe?.bean ?: last?.bean ?: Bean(),
        )
        setDraft(draft)
        setSession(null)
        return true
    }

    // ---------- Черновик и рецепты ----------

    fun discardDraft() = setDraft(null)

    fun save(brew: Brew, fromDraft: Boolean, onSaved: (Brew) -> Unit) {
        viewModelScope.launch {
            val saved = repo.upsert(brew)
            if (fromDraft) setDraft(null)
            onSaved(saved)
        }
    }

    fun toggleFavorite(id: Long) {
        val b = repo.get(id) ?: return
        viewModelScope.launch { repo.upsert(b.copy(favorite = !b.favorite)) }
    }

    fun delete(id: Long) {
        viewModelScope.launch {
            repo.delete(id)
            if (_guideId.value == id) selectGuide(null)
        }
    }

    private fun setSession(s: ActiveSession?) {
        _session.value = s
        store.saveSession(s)
    }

    private fun setDraft(b: Brew?) {
        _draft.value = b
        store.saveDraft(b)
    }
}
