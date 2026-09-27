package com.derposd.v60.data

import org.json.JSONArray
import org.json.JSONObject

internal object JsonMapper {

    fun brewsToJson(brews: List<Brew>): String =
        JSONObject()
            .put("version", 1)
            .put("brews", JSONArray().apply { brews.forEach { put(brewToJson(it)) } })
            .toString()

    fun brewsFromJson(text: String): List<Brew> {
        val arr = JSONObject(text).optJSONArray("brews") ?: return emptyList()
        return (0 until arr.length()).mapNotNull { i ->
            runCatching { brewFromJson(arr.getJSONObject(i)) }.getOrNull()
        }
    }

    fun brewToJson(b: Brew): JSONObject = JSONObject().apply {
        put("id", b.id)
        put("createdAt", b.createdAt)
        put("name", b.name)
        put("dripper", b.dripper)
        put("filter", b.filter)
        put("grind", b.grind)
        b.waterTempC?.let { put("waterTempC", it) }
        b.coffeeG?.let { put("coffeeG", it) }
        b.waterG?.let { put("waterG", it) }
        put("totalTimeMs", b.totalTimeMs)
        put("pours", JSONArray().apply {
            b.pours.forEach { p ->
                put(JSONObject().apply {
                    put("startMs", p.startMs)
                    p.waterG?.let { put("waterG", it) }
                })
            }
        })
        put("bean", JSONObject().apply {
            put("name", b.bean.name)
            put("roaster", b.bean.roaster)
            put("origin", b.bean.origin)
            put("process", b.bean.process)
            put("roastLevel", b.bean.roastLevel)
            put("roastDate", b.bean.roastDate)
        })
        put("rating", b.rating)
        put("comment", b.comment)
        put("favorite", b.favorite)
    }

    fun brewFromJson(o: JSONObject): Brew {
        val poursArr = o.optJSONArray("pours") ?: JSONArray()
        val pours = (0 until poursArr.length()).map { i ->
            val p = poursArr.getJSONObject(i)
            Pour(p.optLong("startMs"), p.optIntOrNull("waterG"))
        }
        val bo = o.optJSONObject("bean") ?: JSONObject()
        return Brew(
            id = o.optLong("id"),
            createdAt = o.optLong("createdAt", System.currentTimeMillis()),
            name = o.optString("name"),
            dripper = o.optString("dripper"),
            filter = o.optString("filter"),
            grind = o.optString("grind"),
            waterTempC = o.optIntOrNull("waterTempC"),
            coffeeG = if (o.has("coffeeG")) o.optDouble("coffeeG").takeIf { !it.isNaN() } else null,
            waterG = o.optIntOrNull("waterG"),
            totalTimeMs = o.optLong("totalTimeMs"),
            pours = pours,
            bean = Bean(
                name = bo.optString("name"),
                roaster = bo.optString("roaster"),
                origin = bo.optString("origin"),
                process = bo.optString("process"),
                roastLevel = bo.optString("roastLevel"),
                roastDate = bo.optString("roastDate"),
            ),
            rating = o.optInt("rating"),
            comment = o.optString("comment"),
            favorite = o.optBoolean("favorite"),
        )
    }

    fun sessionToJson(s: ActiveSession): String = JSONObject().apply {
        put("startElapsed", s.startElapsed)
        put("startWall", s.startWall)
        put("pourStarts", JSONArray().apply { s.pourStarts.forEach { put(it) } })
        s.guideId?.let { put("guideId", it) }
        s.guideDose?.let { put("guideDose", it) }
    }.toString()

    fun sessionFromJson(text: String): ActiveSession {
        val o = JSONObject(text)
        val arr = o.getJSONArray("pourStarts")
        return ActiveSession(
            startElapsed = o.getLong("startElapsed"),
            startWall = o.getLong("startWall"),
            pourStarts = (0 until arr.length()).map { arr.getLong(it) },
            guideId = if (o.has("guideId")) o.getLong("guideId") else null,
            guideDose = if (o.has("guideDose")) o.getDouble("guideDose") else null,
        )
    }

    private fun JSONObject.optIntOrNull(key: String): Int? =
        if (has(key) && !isNull(key)) optInt(key) else null
}
