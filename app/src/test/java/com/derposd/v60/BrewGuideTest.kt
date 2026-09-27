package com.derposd.v60

import com.derposd.v60.data.Brew
import com.derposd.v60.data.BrewGuide
import com.derposd.v60.data.Pour
import com.derposd.v60.data.formatClock
import com.derposd.v60.data.pourName
import com.derposd.v60.data.ruPlural
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class BrewGuideTest {

    private val recipe = Brew(
        id = 1,
        coffeeG = 15.0,
        totalTimeMs = 180_000,
        pours = listOf(
            Pour(0, 45),
            Pour(45_000, 105),
            Pour(90_000, 100),
        ),
    )

    @Test
    fun names() {
        assertEquals("Блуминг", pourName(0))
        assertEquals("Вливание 2", pourName(1))
        assertEquals("1:05", formatClock(65_400))
        assertEquals("дня", ruPlural(3, "день", "дня", "дней"))
        assertEquals("дней", ruPlural(12, "день", "дня", "дней"))
        assertEquals("день", ruPlural(21, "день", "дня", "дней"))
    }

    @Test
    fun recipeTotals() {
        assertEquals(250, recipe.effectiveWaterG)
        assertEquals(150, recipe.cumulativeWater(1))
        assertEquals(250.0 / 15.0, recipe.ratio!!, 0.0001)
        assertEquals(90_000, recipe.pourDurationMs(2))
    }

    @Test
    fun scalesWaterWithDose() {
        val g = BrewGuide(recipe, 18.0)
        assertEquals(54, g.pourWater(0))
        assertEquals(300, g.waterG)
        assertEquals(180, g.cumulativeWater(1))
    }

    @Test
    fun hintCountsDownToNextPour() {
        val g = BrewGuide(recipe, null)
        val h = g.hint(pourIndex = 0, currentPourStartMs = 0, elapsedMs = 30_000)
        assertEquals(45, h.pourWater)
        assertEquals(45_000L, h.nextPourAt)
        assertEquals(15_000L, h.msToTarget)
        assertFalse(h.isDue)
        assertTrue(g.hint(0, 0, 46_000).isDue)
    }

    @Test
    fun lastPlannedPourPointsToFinish() {
        val g = BrewGuide(recipe, null)
        val h = g.hint(pourIndex = 2, currentPourStartMs = 93_000, elapsedMs = 100_000)
        assertNull(h.nextPourAt)
        assertEquals(180_000L, h.finishAt)
        assertEquals(3_000L, h.startDeltaMs)
        assertTrue(g.hint(3, 150_000, 151_000).beyondPlan)
    }
}
