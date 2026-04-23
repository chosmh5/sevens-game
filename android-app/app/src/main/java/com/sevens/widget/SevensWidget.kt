package com.sevens.widget

import android.content.Context
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.glance.GlanceId
import androidx.glance.GlanceModifier
import androidx.glance.action.ActionParameters
import androidx.glance.action.clickable
import androidx.glance.appwidget.GlanceAppWidget
import androidx.glance.appwidget.GlanceAppWidgetReceiver
import androidx.glance.appwidget.action.ActionCallback
import androidx.glance.appwidget.action.actionRunCallback
import androidx.glance.appwidget.cornerRadius
import androidx.glance.appwidget.provideContent
import androidx.glance.appwidget.state.updateAppWidgetState
import androidx.glance.background
import androidx.glance.currentState
import androidx.glance.layout.*
import androidx.glance.state.GlanceStateDefinition
import androidx.glance.state.PreferencesGlanceStateDefinition
import androidx.glance.text.FontWeight
import androidx.glance.text.Text
import androidx.glance.text.TextStyle
import androidx.glance.unit.ColorProvider
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONObject
import java.net.URL

val DIALOGUE_KEY = stringPreferencesKey("current_dialogue")

class SevensWidget : GlanceAppWidget() {

    override val stateDefinition: GlanceStateDefinition<*> = PreferencesGlanceStateDefinition

    override suspend fun provideGlance(context: Context, id: GlanceId) {
        provideContent {
            val prefs = currentState<Preferences>()
            val dialogue = prefs[DIALOGUE_KEY] ?: ""

            Box(
                modifier = GlanceModifier
                    .fillMaxSize()
                    .background(Color(0xFF1A1A2E))
                    .padding(8.dp),
                contentAlignment = Alignment.Center
            ) {
                Column(
                    horizontalAlignment = Alignment.CenterHorizontally,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    // 캐릭터 이미지 — 클릭하면 대사 요청
                    Box(
                        modifier = GlanceModifier
                            .size(64.dp)
                            .background(Color(0xFF16213E))
                            .cornerRadius(32)
                            .clickable(actionRunCallback<FetchDialogueAction>()),
                        contentAlignment = Alignment.Center
                    ) {
                        Text(
                            text = "세",
                            style = TextStyle(
                                color = ColorProvider(Color(0xFFE94560)),
                                fontSize = 28.sp,
                                fontWeight = FontWeight.Bold
                            )
                        )
                    }

                    if (dialogue.isNotEmpty()) {
                        Spacer(modifier = GlanceModifier.height(8.dp))
                        Box(
                            modifier = GlanceModifier
                                .background(Color(0xFF16213E))
                                .cornerRadius(8)
                                .padding(horizontal = 10.dp, vertical = 6.dp),
                            contentAlignment = Alignment.Center
                        ) {
                            Text(
                                text = dialogue,
                                style = TextStyle(
                                    color = ColorProvider(Color.White),
                                    fontSize = 11.sp
                                ),
                                maxLines = 2
                            )
                        }
                    }
                }
            }
        }
    }
}

class FetchDialogueAction : ActionCallback {
    override suspend fun onAction(
        context: Context,
        glanceId: GlanceId,
        parameters: ActionParameters
    ) {
        val line = withContext(Dispatchers.IO) {
            try {
                val json = URL("$SERVER_URL/character/line").readText()
                JSONObject(json).getString("line")
            } catch (e: Exception) {
                "..."
            }
        }
        updateAppWidgetState(context, glanceId) { prefs ->
            prefs[DIALOGUE_KEY] = line
        }
        SevensWidget().update(context, glanceId)
    }
}

class SevensWidgetReceiver : GlanceAppWidgetReceiver() {
    override val glanceAppWidget = SevensWidget()
}
