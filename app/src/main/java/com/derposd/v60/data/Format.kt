package com.derposd.v60.data

import java.time.LocalDate
import java.time.format.DateTimeFormatter
import java.util.Locale
import kotlin.math.abs
import kotlin.math.roundToLong

val RU: Locale = Locale.forLanguageTag("ru")

fun pourName(index: Int): String = if (index == 0) "Блуминг" else "Вливание ${index + 1}"

/** 83_400 → "1:23" */
fun formatClock(ms: Long): String {
    val totalSec = ms.coerceAtLeast(0) / 1000
    return "%d:%02d".format(totalSec / 60, totalSec % 60)
}

/** Разница со знаком: +0:05 / −0:12 */
fun formatDelta(ms: Long): String {
    val sign = if (ms < 0) "−" else "+"
    return sign + formatClock(abs(ms))
}

fun formatSeconds(ms: Long): String = "${(ms.coerceAtLeast(0) / 1000.0).roundToLong()} с"

fun formatGrams(value: Double): String =
    if (value % 1.0 == 0.0) value.toLong().toString() else "%.1f".format(RU, value)

fun formatRatio(ratio: Double): String = "1:" + "%.1f".format(RU, ratio).removeSuffix(",0")

fun parseDecimal(text: String): Double? = text.trim().replace(',', '.').toDoubleOrNull()

fun parseInt(text: String): Int? = parseDecimal(text)?.let { Math.round(it).toInt() }

fun ruPlural(n: Long, one: String, few: String, many: String): String {
    val n10 = abs(n) % 10
    val n100 = abs(n) % 100
    return when {
        n10 == 1L && n100 != 11L -> one
        n10 in 2..4 && n100 !in 12..14 -> few
        else -> many
    }
}

fun daysText(days: Long): String = "$days ${ruPlural(days, "день", "дня", "дней")}"

fun poursText(n: Int): String = "$n ${ruPlural(n.toLong(), "вливание", "вливания", "вливаний")}"

private val dateFormatter = DateTimeFormatter.ofPattern("d MMMM yyyy", RU)
private val shortDateFormatter = DateTimeFormatter.ofPattern("d MMM", RU)

fun formatDate(date: LocalDate): String = dateFormatter.format(date)
fun formatShortDate(date: LocalDate): String = shortDateFormatter.format(date).removeSuffix(".")

fun shareText(brew: Brew): String = buildString {
    appendLine("☕ ${brew.title}")
    appendLine(formatDate(brew.brewDate))
    if (brew.dripper.isNotBlank()) appendLine("Воронка: ${brew.dripper}")
    if (brew.filter.isNotBlank()) appendLine("Фильтр: ${brew.filter}")
    brew.coffeeG?.let { appendLine("Кофе: ${formatGrams(it)} г") }
    brew.effectiveWaterG?.let { appendLine("Вода: $it г") }
    brew.ratio?.let { appendLine("Соотношение: ${formatRatio(it)}") }
    if (brew.grind.isNotBlank()) appendLine("Помол: ${brew.grind}")
    brew.waterTempC?.let { appendLine("Температура: $it °C") }
    appendLine("Общее время: ${formatClock(brew.totalTimeMs)}")
    appendLine()
    brew.pours.forEachIndexed { i, p ->
        append("${formatClock(p.startMs)} — ${pourName(i)}")
        p.waterG?.let { append(": $it г") }
        brew.cumulativeWater(i)?.let { append(" (всего $it г)") }
        appendLine()
    }
    val b = brew.bean
    if (!b.isEmpty) {
        appendLine()
        if (b.name.isNotBlank()) appendLine("Зерно: ${b.name}")
        if (b.roaster.isNotBlank()) appendLine("Обжарщик: ${b.roaster}")
        if (b.origin.isNotBlank()) appendLine("Происхождение: ${b.origin}")
        if (b.process.isNotBlank()) appendLine("Обработка: ${b.process}")
        if (b.roastLevel.isNotBlank()) appendLine("Обжарка: ${b.roastLevel}")
        b.roastLocalDate()?.let { appendLine("Дата обжарки: ${formatDate(it)}") }
    }
    if (brew.rating > 0) appendLine("Оценка: " + "★".repeat(brew.rating) + "☆".repeat(5 - brew.rating))
    if (brew.comment.isNotBlank()) {
        appendLine()
        appendLine(brew.comment)
    }
}.trim()
