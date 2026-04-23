package com.sevens.widget

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import org.json.JSONObject
import java.io.OutputStreamWriter
import java.net.HttpURLConnection
import java.net.URL

private val BG = Color(0xFF1A1A2E)
private val ACCENT = Color(0xFFE94560)
private val ACCENT_DIM = Color(0x4DE94560)
private val BUBBLE_AI = Color(0xFF16213E)
private val BUBBLE_USER = Color(0xFF0F3460)

data class Message(val speaker: String, val text: String)

@Composable
fun ChatScreen() {
    val scope = rememberCoroutineScope()
    val messages = remember { mutableStateListOf<Message>() }
    var input by remember { mutableStateOf("") }
    var isLoading by remember { mutableStateOf(false) }
    val listState = rememberLazyListState()

    // 히스토리 로드만 담당 — 스크롤은 아래 LaunchedEffect에 위임
    LaunchedEffect(Unit) {
        messages.addAll(fetchHistory())
    }

    // 메시지 추가 시 항상 최신 메시지로 스크롤
    LaunchedEffect(messages.size) {
        if (messages.isNotEmpty()) listState.animateScrollToItem(messages.size - 1)
    }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .background(BG)
            .systemBarsPadding()
    ) {
        // 상단 타이틀
        Box(
            modifier = Modifier
                .fillMaxWidth()
                .padding(16.dp),
            contentAlignment = Alignment.Center
        ) {
            Text("세비-호", color = ACCENT, fontSize = 18.sp, fontWeight = FontWeight.Bold)
            TextButton(
                onClick = {
                    scope.launch {
                        clearConversation()
                        messages.clear()
                    }
                },
                modifier = Modifier.align(Alignment.CenterEnd)
            ) {
                Text("초기화", color = Color.Gray, fontSize = 12.sp)
            }
        }

        HorizontalDivider(color = ACCENT_DIM)

        // 메시지 목록
        LazyColumn(
            state = listState,
            modifier = Modifier
                .weight(1f)
                .padding(horizontal = 12.dp),
            verticalArrangement = Arrangement.spacedBy(8.dp),
            contentPadding = PaddingValues(vertical = 12.dp)
        ) {
            itemsIndexed(messages, key = { index, _ -> index }) { _, msg ->
                val isUser = msg.speaker == "플레이어"
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = if (isUser) Arrangement.End else Arrangement.Start
                ) {
                    Column(horizontalAlignment = if (isUser) Alignment.End else Alignment.Start) {
                        if (!isUser) {
                            Text(msg.speaker, color = ACCENT, fontSize = 11.sp, modifier = Modifier.padding(bottom = 2.dp))
                        }
                        Box(
                            modifier = Modifier
                                .background(
                                    if (isUser) BUBBLE_USER else BUBBLE_AI,
                                    RoundedCornerShape(12.dp)
                                )
                                .padding(horizontal = 12.dp, vertical = 8.dp)
                                .widthIn(max = 260.dp)
                        ) {
                            Text(msg.text, color = Color.White, fontSize = 14.sp)
                        }
                    }
                }
            }

            if (isLoading) {
                item {
                    Text(
                        text = "금방 대답할게요...",
                        color = Color.Gray,
                        fontSize = 11.sp,
                        modifier = Modifier.padding(start = 4.dp)
                    )
                }
            }
        }

        HorizontalDivider(color = ACCENT.copy(alpha = 0.3f))

        // 입력창
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(8.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            TextField(
                value = input,
                onValueChange = { input = it },
                modifier = Modifier.weight(1f),
                placeholder = { Text("메시지 입력...", color = Color.Gray) },
                colors = TextFieldDefaults.colors(
                    focusedContainerColor = BUBBLE_AI,
                    unfocusedContainerColor = BUBBLE_AI,
                    focusedTextColor = Color.White,
                    unfocusedTextColor = Color.White,
                    cursorColor = ACCENT,
                    focusedIndicatorColor = Color.Transparent,
                    unfocusedIndicatorColor = Color.Transparent
                ),
                shape = RoundedCornerShape(12.dp),
                singleLine = true
            )
            Spacer(modifier = Modifier.width(8.dp))
            Button(
                onClick = {
                    val text = input.trim()
                    if (text.isEmpty() || isLoading) return@Button
                    input = ""
                    messages.add(Message("플레이어", text))
                    isLoading = true
                    scope.launch {
                        val reply = sendMessage(text)
                        messages.add(reply)
                        isLoading = false
                    }
                },
                colors = ButtonDefaults.buttonColors(containerColor = ACCENT),
                shape = RoundedCornerShape(12.dp)
            ) {
                Text("전송", color = Color.White)
            }
        }
    }
}

private suspend fun fetchHistory(): List<Message> = withContext(Dispatchers.IO) {
    try {
        val json = URL("$SERVER_URL/conversation?limit=20").readText()
        val arr = JSONObject(json).getJSONArray("messages")
        (0 until arr.length()).map {
            val obj = arr.getJSONObject(it)
            Message(obj.getString("speaker"), obj.getString("message"))
        }
    } catch (e: Exception) {
        emptyList()
    }
}

private suspend fun clearConversation() = withContext(Dispatchers.IO) {
    try {
        val conn = URL("$SERVER_URL/conversation/clear").openConnection() as java.net.HttpURLConnection
        conn.requestMethod = "POST"
        conn.responseCode
    } catch (_: Exception) {}
}

private suspend fun sendMessage(text: String): Message = withContext(Dispatchers.IO) {
    try {
        val url = URL("$SERVER_URL/conversation/user")
        val conn = url.openConnection() as HttpURLConnection
        conn.requestMethod = "POST"
        conn.setRequestProperty("Content-Type", "application/json")
        conn.doOutput = true
        val body = JSONObject().put("message", text).toString()
        OutputStreamWriter(conn.outputStream).use { it.write(body) }
        val json = conn.inputStream.bufferedReader().readText()
        val obj = JSONObject(json)
        Message(obj.getString("speaker"), obj.getString("reply"))
    } catch (e: Exception) {
        Message("오류", "서버에 연결할 수 없습니다.")
    }
}
