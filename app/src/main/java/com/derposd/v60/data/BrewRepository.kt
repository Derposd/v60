package com.derposd.v60.data

import android.content.Context
import android.util.AtomicFile
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import java.io.File

/** Хранилище рецептов в JSON-файле во внутренней памяти приложения. */
class BrewRepository(context: Context) {

    private val file = AtomicFile(File(context.filesDir, "brews.json"))
    private val mutex = Mutex()
    private val _brews = MutableStateFlow(load())
    val brews: StateFlow<List<Brew>> = _brews.asStateFlow()

    fun get(id: Long): Brew? = _brews.value.firstOrNull { it.id == id }

    suspend fun upsert(brew: Brew): Brew = mutex.withLock {
        val current = _brews.value
        val saved = if (brew.id == 0L) {
            brew.copy(id = maxOf(System.currentTimeMillis(), (current.maxOfOrNull { it.id } ?: 0) + 1))
        } else brew
        val updated = if (current.any { it.id == saved.id }) {
            current.map { if (it.id == saved.id) saved else it }
        } else current + saved
        commit(updated)
        saved
    }

    suspend fun delete(id: Long) = mutex.withLock {
        commit(_brews.value.filterNot { it.id == id })
    }

    private suspend fun commit(list: List<Brew>) {
        val sorted = list.sortedByDescending { it.createdAt }
        _brews.value = sorted
        withContext(Dispatchers.IO) {
            val out = file.startWrite()
            try {
                out.write(JsonMapper.brewsToJson(sorted).toByteArray())
                file.finishWrite(out)
            } catch (e: Exception) {
                file.failWrite(out)
            }
        }
    }

    private fun load(): List<Brew> = runCatching {
        if (!file.baseFile.exists()) emptyList()
        else JsonMapper.brewsFromJson(String(file.readFully())).sortedByDescending { it.createdAt }
    }.getOrDefault(emptyList())
}
