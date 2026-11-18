package com.miying.player.msv

import android.content.Context
import android.content.SharedPreferences
import org.json.JSONArray
import org.json.JSONObject

data class RecentEntry(
    val uri: String,
    val name: String,
    val kind: String,
    val position: Long,
    val duration: Long,
    val timestamp: Long,
)

class RecentStore(context: Context) {
    private val preferences: SharedPreferences =
        context.getSharedPreferences("miying_recent", Context.MODE_PRIVATE)

    fun load(): List<RecentEntry> {
        val raw = preferences.getString("recent", null) ?: return emptyList()
        return try {
            val array = JSONArray(raw)
            (0 until array.length()).map { index ->
                val value = array.getJSONObject(index)
                RecentEntry(
                    value.optString("uri"),
                    value.optString("name"),
                    value.optString("kind", "video"),
                    value.optLong("position", 0L),
                    value.optLong("duration", 0L),
                    value.optLong("timestamp", 0L),
                )
            }.sortedByDescending { it.timestamp }
        } catch (_: Exception) {
            emptyList()
        }
    }

    fun save(uri: String, name: String, kind: String, position: Long = 0L, duration: Long = 0L) {
        val existing = load().filterNot { it.uri == uri }
        val updated = (listOf(RecentEntry(uri, name, kind, position, duration, System.currentTimeMillis())) + existing)
            .take(12)
        val array = JSONArray()
        updated.forEach { entry ->
            array.put(
                JSONObject()
                    .put("uri", entry.uri)
                    .put("name", entry.name)
                    .put("kind", entry.kind)
                    .put("position", entry.position)
                    .put("duration", entry.duration)
                    .put("timestamp", entry.timestamp),
            )
        }
        preferences.edit().putString("recent", array.toString()).apply()
    }

    fun remove(uri: String) {
        val remaining = load().filterNot { it.uri == uri }
        if (remaining.isEmpty()) {
            clear()
            return
        }

        val array = JSONArray()
        remaining.forEach { entry ->
            array.put(
                JSONObject()
                    .put("uri", entry.uri)
                    .put("name", entry.name)
                    .put("kind", entry.kind)
                    .put("position", entry.position)
                    .put("duration", entry.duration)
                    .put("timestamp", entry.timestamp),
            )
        }
        preferences.edit().putString("recent", array.toString()).apply()
    }

    fun clear() {
        preferences.edit().remove("recent").apply()
    }
}