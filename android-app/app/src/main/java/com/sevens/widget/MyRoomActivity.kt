package com.sevens.widget

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.sevens.widget.ui.theme.SevensWidgetTheme
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.withContext
import org.json.JSONObject
import java.net.URL

// ── 색상 ──────────────────────────────────────────────
private val BG         = Color(0xFF1A1A2E)
private val ACCENT     = Color(0xFFE94560)
private val CARD_BG    = Color(0xFF16213E)
private val GOLD       = Color(0xFFFFDD66)
private val BAR_INTIM  = Color(0xFF73BFFF)
private val BAR_TRUST  = Color(0xFF8CE68C)
private val TEXT_DIM   = Color(0xFF9999AA)

// ── 데이터 모델 ────────────────────────────────────────
data class CharRelation(
    val name: String,
    val intimacy: Int,
    val trust: Int,
    val emotion: String
)

data class EventSummary(
    val situation: String,
    val type: String
)

class MyRoomActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContent {
            SevensWidgetTheme {
                MyRoomScreen()
            }
        }
    }
}

@Composable
fun MyRoomScreen() {
    var selectedTab by remember { mutableIntStateOf(0) }
    val tabs = listOf("관계 현황", "채팅")

    Column(
        modifier = Modifier
            .fillMaxSize()
            .background(BG)
            .systemBarsPadding()
    ) {
        // 타이틀
        Box(
            modifier = Modifier
                .fillMaxWidth()
                .padding(16.dp),
            contentAlignment = Alignment.Center
        ) {
            Text("마이룸", color = GOLD, fontSize = 24.sp, fontWeight = FontWeight.Bold)
        }

        // 탭
        TabRow(
            selectedTabIndex = selectedTab,
            containerColor = CARD_BG,
            contentColor = ACCENT,
            indicator = { tabPositions ->
                TabRowDefaults.SecondaryIndicator(
                    Modifier.tabIndicatorOffset(tabPositions[selectedTab]),
                    color = ACCENT
                )
            }
        ) {
            tabs.forEachIndexed { index, title ->
                Tab(
                    selected = selectedTab == index,
                    onClick = { selectedTab = index },
                    text = {
                        Text(
                            title,
                            color = if (selectedTab == index) ACCENT else TEXT_DIM,
                            fontWeight = if (selectedTab == index) FontWeight.Bold else FontWeight.Normal
                        )
                    }
                )
            }
        }

        when (selectedTab) {
            0 -> RelationDashboard()
            1 -> ChatScreen()
        }
    }
}

@Composable
fun RelationDashboard() {
    var relations by remember { mutableStateOf(emptyList<CharRelation>()) }
    var events by remember { mutableStateOf(emptyList<EventSummary>()) }
    var isLoading by remember { mutableStateOf(true) }
    var errorMsg by remember { mutableStateOf<String?>(null) }

    // 자동 갱신
    LaunchedEffect(Unit) {
        while (true) {
            try {
                val (rels, evts) = fetchMyRoomStatus()
                relations = rels
                events = evts
                errorMsg = null
            } catch (e: Exception) {
                errorMsg = "서버 연결 실패"
            }
            isLoading = false
            delay(30_000)
        }
    }

    if (isLoading) {
        Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
            CircularProgressIndicator(color = ACCENT)
        }
        return
    }

    LazyColumn(
        modifier = Modifier
            .fillMaxSize()
            .padding(horizontal = 12.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
        contentPadding = PaddingValues(vertical = 16.dp)
    ) {
        if (errorMsg != null) {
            item {
                Text(
                    errorMsg!!,
                    color = ACCENT,
                    fontSize = 14.sp,
                    modifier = Modifier.padding(8.dp)
                )
            }
        }

        // 캐릭터 관계 카드
        items(relations) { rel ->
            RelationCard(rel)
        }

        // 최근 이벤트
        if (events.isNotEmpty()) {
            item {
                Spacer(modifier = Modifier.height(8.dp))
                Text("최근 소식", color = ACCENT, fontSize = 18.sp, fontWeight = FontWeight.Bold,
                    modifier = Modifier.padding(start = 4.dp))
            }
            items(events) { ev ->
                EventItem(ev)
            }
        }
    }
}

@Composable
fun RelationCard(rel: CharRelation) {
    val intimacyFraction = (rel.intimacy + 100f) / 200f  // -100~100 → 0~1
    val trustFraction = rel.trust / 100f

    val animIntimacy by animateFloatAsState(
        targetValue = intimacyFraction,
        animationSpec = tween(800), label = "intimacy"
    )
    val animTrust by animateFloatAsState(
        targetValue = trustFraction,
        animationSpec = tween(800), label = "trust"
    )

    val emotionText = when (rel.emotion) {
        "happy"     -> "😊 기쁨"
        "angry"     -> "😠 화남"
        "sad"       -> "😢 슬픔"
        "fearful"   -> "😰 불안"
        "surprised" -> "😲 놀람"
        "neutral"   -> "😐 평온"
        else        -> "😐 ${rel.emotion}"
    }

    Column(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(16.dp))
            .background(CARD_BG)
            .padding(16.dp)
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically
        ) {
            Text(rel.name, color = GOLD, fontSize = 20.sp, fontWeight = FontWeight.Bold)
            Text(emotionText, color = TEXT_DIM, fontSize = 14.sp)
        }

        Spacer(modifier = Modifier.height(12.dp))

        // 친밀도 바
        Text("친밀도: ${if (rel.intimacy >= 0) "+" else ""}${rel.intimacy}",
            color = BAR_INTIM, fontSize = 13.sp)
        Spacer(modifier = Modifier.height(4.dp))
        Box(
            modifier = Modifier
                .fillMaxWidth()
                .height(10.dp)
                .clip(RoundedCornerShape(5.dp))
                .background(Color(0xFF252540))
        ) {
            Box(
                modifier = Modifier
                    .fillMaxHeight()
                    .fillMaxWidth(animIntimacy)
                    .clip(RoundedCornerShape(5.dp))
                    .background(BAR_INTIM)
            )
        }

        Spacer(modifier = Modifier.height(10.dp))

        // 신뢰 바
        Text("신뢰: ${rel.trust}/100", color = BAR_TRUST, fontSize = 13.sp)
        Spacer(modifier = Modifier.height(4.dp))
        Box(
            modifier = Modifier
                .fillMaxWidth()
                .height(10.dp)
                .clip(RoundedCornerShape(5.dp))
                .background(Color(0xFF252540))
        ) {
            Box(
                modifier = Modifier
                    .fillMaxHeight()
                    .fillMaxWidth(animTrust)
                    .clip(RoundedCornerShape(5.dp))
                    .background(BAR_TRUST)
            )
        }
    }
}

@Composable
fun EventItem(ev: EventSummary) {
    val icon = if (ev.type == "card_game") "♠" else "💬"
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(10.dp))
            .background(CARD_BG.copy(alpha = 0.6f))
            .padding(12.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Text(icon, fontSize = 18.sp, modifier = Modifier.padding(end = 8.dp))
        Text(ev.situation, color = Color.White, fontSize = 14.sp)
    }
}

// ── 네트워크 ──────────────────────────────────────────

private suspend fun fetchMyRoomStatus(): Pair<List<CharRelation>, List<EventSummary>> =
    withContext(Dispatchers.IO) {
        val json = URL("$SERVER_URL/myroom/status").readText()
        val root = JSONObject(json)

        // player_relations 파싱
        val rels = mutableListOf<CharRelation>()
        val prObj = root.getJSONObject("player_relations")
        for (key in prObj.keys()) {
            val obj = prObj.getJSONObject(key)
            rels.add(
                CharRelation(
                    name = key,
                    intimacy = obj.getInt("intimacy"),
                    trust = obj.getInt("trust"),
                    emotion = obj.optString("emotion", "neutral")
                )
            )
        }

        // recent_events 파싱
        val evts = mutableListOf<EventSummary>()
        val evArr = root.getJSONArray("recent_events")
        for (i in 0 until evArr.length()) {
            val obj = evArr.getJSONObject(i)
            evts.add(
                EventSummary(
                    situation = obj.getString("situation"),
                    type = obj.optString("type", "conversation")
                )
            )
        }

        Pair(rels, evts)
    }
