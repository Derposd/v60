package com.derposd.v60.data

import android.content.Context
import android.os.SystemClock
import org.json.JSONObject

/** Идущая заварка и незаписанный черновик — в SharedPreferences. */
class SessionStore(context: Context) {

    private val prefs = context.getSharedPreferences("session", Context.MODE_PRIVATE)

    fun loadSession(): ActiveSession? {
        val text = prefs.getString(KEY_SESSION, null) ?: return null
        val s = runCatching { JsonMapper.sessionFromJson(text) }.getOrNull() ?: return null
        val now = SystemClock.elapsedRealtime()
        // После перезагрузки elapsedRealtime начинается заново — восстанавливаем по настенным часам.
        val fixed = if (s.startElapsed > now) {
            s.copy(startElapsed = now - (System.currentTimeMillis() - s.startWall).coerceAtLeast(0))
        } else s
        // Слишком старые заварки (забытые на сутки) не восстанавливаем.
        return if (now - fixed.startElapsed > 24 * 60 * 60 * 1000L) null else fixed
    }

    fun saveSession(s: ActiveSession?) {
        prefs.edit().apply {
            if (s == null) remove(KEY_SESSION) else putString(KEY_SESSION, JsonMapper.sessionToJson(s))
        }.apply()
    }

    fun loadDraft(): Brew? {
        val text = prefs.getString(KEY_DRAFT, null) ?: return null
        return runCatching { JsonMapper.brewFromJson(JSONObject(text)) }.getOrNull()
    }

    fun saveDraft(b: Brew?) {
        prefs.edit().apply {
            if (b == null) remove(KEY_DRAFT) else putString(KEY_DRAFT, JsonMapper.brewToJson(b).toString())
        }.apply()
    }

    var guideId: Long?
        get() = if (prefs.contains(KEY_GUIDE)) prefs.getLong(KEY_GUIDE, 0) else null
        set(value) {
            prefs.edit().apply { if (value == null) remove(KEY_GUIDE) else putLong(KEY_GUIDE, value) }.apply()
        }

    private companion object {
        const val KEY_SESSION = "active"
        const val KEY_DRAFT = "draft"
        const val KEY_GUIDE = "guide"
    }
}
