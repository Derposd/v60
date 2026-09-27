package com.derposd.v60.data

import java.time.Instant
import java.time.LocalDate
import java.time.ZoneId
import java.time.temporal.ChronoUnit

/** Одно вливание: момент начала от старта таймера и (необязательно) сколько воды влито. */
data class Pour(
    val startMs: Long,
    val waterG: Int? = null,
)

data class Bean(
    val name: String = "",
    val roaster: String = "",
    val origin: String = "",
    val process: String = "",
    val roastLevel: String = "",
    /** ISO-дата yyyy-MM-dd или пустая строка. */
    val roastDate: String = "",
) {
    val isEmpty: Boolean
        get() = listOf(name, roaster, origin, process, roastLevel, roastDate).all { it.isBlank() }

    fun roastLocalDate(): LocalDate? =
        if (roastDate.isBlank()) null else runCatching { LocalDate.parse(roastDate) }.getOrNull()
}

data class Brew(
    val id: Long = 0,
    val createdAt: Long = System.currentTimeMillis(),
    val name: String = "",
    val dripper: String = "",
    val filter: String = "",
    val grind: String = "",
    val waterTempC: Int? = null,
    val coffeeG: Double? = null,
    /** Общее количество воды, если указано вручную. Иначе считается по вливаниям. */
    val waterG: Int? = null,
    val totalTimeMs: Long = 0,
    val pours: List<Pour> = emptyList(),
    val bean: Bean = Bean(),
    val rating: Int = 0,
    val comment: String = "",
    val favorite: Boolean = false,
) {
    val pouredWaterG: Int?
        get() = if (pours.any { it.waterG != null }) pours.sumOf { it.waterG ?: 0 } else null

    val effectiveWaterG: Int?
        get() = waterG ?: pouredWaterG

    /** Соотношение кофе к воде (сколько грамм воды на 1 г кофе). */
    val ratio: Double?
        get() {
            val c = coffeeG ?: return null
            val w = effectiveWaterG ?: return null
            return if (c > 0 && w > 0) w / c else null
        }

    val title: String
        get() = name.ifBlank { bean.name.ifBlank { "Заварка" } }

    val brewDate: LocalDate
        get() = Instant.ofEpochMilli(createdAt).atZone(ZoneId.systemDefault()).toLocalDate()

    /** Сколько дней прошло от обжарки до этой заварки. */
    val beanAgeDays: Long?
        get() = bean.roastLocalDate()?.let { ChronoUnit.DAYS.between(it, brewDate) }

    fun pourDurationMs(index: Int): Long {
        val start = pours.getOrNull(index)?.startMs ?: return 0
        val end = pours.getOrNull(index + 1)?.startMs ?: totalTimeMs
        return (end - start).coerceAtLeast(0)
    }

    /** Нарастающий итог воды к концу вливания [index] (показания весов). */
    fun cumulativeWater(index: Int): Int? {
        var sum = 0
        for (i in 0..index) sum += pours.getOrNull(i)?.waterG ?: return null
        return sum
    }
}

/** Идущая заварка — хранится, чтобы таймер пережил закрытие приложения. */
data class ActiveSession(
    /** SystemClock.elapsedRealtime() в момент старта. */
    val startElapsed: Long,
    /** Настенное время старта — запасной вариант после перезагрузки телефона. */
    val startWall: Long,
    /** Начало каждого вливания в мс от старта; первое всегда 0 (блуминг). */
    val pourStarts: List<Long>,
    val guideId: Long? = null,
    val guideDose: Double? = null,
) {
    val pourCount: Int get() = pourStarts.size
    val canFinish: Boolean get() = pourCount >= MIN_POURS

    companion object {
        const val MIN_POURS = 3
    }
}
