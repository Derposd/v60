package com.derposd.v60.data

import kotlin.math.roundToInt

/** Подсказки по сохранённому рецепту во время заварки. */
class BrewGuide(val recipe: Brew, dose: Double?) {

    val scale: Double = run {
        val base = recipe.coffeeG
        if (dose != null && dose > 0 && base != null && base > 0) dose / base else 1.0
    }

    val coffeeG: Double? = recipe.coffeeG?.let { it * scale }
    val waterG: Int? = recipe.effectiveWaterG?.let { (it * scale).roundToInt() }
    val plannedPours: Int get() = recipe.pours.size
    val totalMs: Long get() = recipe.totalTimeMs

    fun pourWater(index: Int): Int? =
        recipe.pours.getOrNull(index)?.waterG?.let { (it * scale).roundToInt() }

    fun cumulativeWater(index: Int): Int? =
        recipe.cumulativeWater(index)?.let { (it * scale).roundToInt() }

    fun plannedStart(index: Int): Long? = recipe.pours.getOrNull(index)?.startMs

    fun hint(pourIndex: Int, currentPourStartMs: Long, elapsedMs: Long): GuideHint {
        val beyond = pourIndex >= plannedPours
        val nextAt = plannedStart(pourIndex + 1)
        val finishAt = if (!beyond && nextAt == null && totalMs > 0) totalMs else null
        val nextTarget = nextAt ?: finishAt
        val progress = if (nextTarget != null) {
            val span = (nextTarget - currentPourStartMs).coerceAtLeast(1)
            ((elapsedMs - currentPourStartMs).toFloat() / span).coerceIn(0f, 1f)
        } else null
        return GuideHint(
            pourWater = pourWater(pourIndex),
            cumulativeWater = cumulativeWater(pourIndex),
            nextPourAt = nextAt,
            finishAt = finishAt,
            msToTarget = nextTarget?.let { it - elapsedMs },
            startDeltaMs = plannedStart(pourIndex)?.let { currentPourStartMs - it },
            progress = progress,
            beyondPlan = beyond,
        )
    }
}

data class GuideHint(
    /** Сколько влить в текущем вливании. */
    val pourWater: Int?,
    /** Сколько должно быть на весах после текущего вливания. */
    val cumulativeWater: Int?,
    /** Рекомендуемое время следующего вливания. */
    val nextPourAt: Long?,
    /** Рекомендуемое время окончания, если текущее вливание последнее по рецепту. */
    val finishAt: Long?,
    /** Сколько осталось до следующего вливания или финиша (может быть < 0). */
    val msToTarget: Long?,
    /** Насколько текущее вливание началось позже (+) или раньше (−) плана. */
    val startDeltaMs: Long?,
    /** Прогресс текущего шага 0..1, если есть цель. */
    val progress: Float?,
    val beyondPlan: Boolean,
) {
    val isDue: Boolean get() = msToTarget != null && msToTarget <= 0
}
