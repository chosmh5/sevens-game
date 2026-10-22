package com.sevens.widget

import android.content.Context
import android.provider.Settings
import java.net.HttpURLConnection
import java.net.URL
import java.security.MessageDigest

const val SERVER_URL = "http://192.168.35.186:8000"

/**
 * 서버가 사람마다 대화·기억·관계를 따로 저장하도록 모든 요청에 붙이는 ID (X-User-Id 헤더).
 * Unity 게임의 SystemInfo.deviceUniqueIdentifier 와 같은 값(ANDROID_ID 의 MD5)이라
 * 같은 폰이면 위젯과 게임이 같은 기억을 쓴다.
 * 단, 안드로이드 8 이상에서 ANDROID_ID 는 앱 서명 키마다 달라지므로 두 앱을 같은 키로 서명해야 한다.
 * (게임 ⚙ 서버 창 아래의 '내 ID' 와 비교해 보면 된다)
 */
object UserId {
    @Volatile
    var value: String = ""
        private set

    fun init(context: Context) {
        if (value.isNotEmpty()) return
        val androidId = Settings.Secure.getString(context.contentResolver, Settings.Secure.ANDROID_ID) ?: ""
        if (androidId.isNotEmpty()) value = md5(androidId)
    }

    private fun md5(s: String): String =
        MessageDigest.getInstance("MD5").digest(s.toByteArray()).joinToString("") { "%02x".format(it) }
}

/** 서버 연결. X-User-Id 헤더를 붙인다. path 는 "/conversation/user" 처럼 / 로 시작. */
fun openServer(path: String): HttpURLConnection =
    (URL("$SERVER_URL$path").openConnection() as HttpURLConnection).apply {
        if (UserId.value.isNotEmpty()) setRequestProperty("X-User-Id", UserId.value)
    }

/** GET 요청 본문을 문자열로. */
fun serverGetText(path: String): String {
    val conn = openServer(path)
    try {
        return conn.inputStream.bufferedReader().use { it.readText() }
    } finally {
        conn.disconnect()
    }
}
