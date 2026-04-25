package com.sevens.widget

import android.content.Intent
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Text
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.sevens.widget.ui.theme.SevensWidgetTheme

private val BG_MAIN = Color(0xFF1A1A2E)
private val ACCENT_MAIN = Color(0xFFE94560)
private val CARD_MAIN = Color(0xFF16213E)

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        setContent {
            SevensWidgetTheme {
                val context = LocalContext.current
                Box(
                    modifier = Modifier
                        .fillMaxSize()
                        .background(BG_MAIN)
                        .systemBarsPadding(),
                    contentAlignment = Alignment.Center
                ) {
                    Column(
                        horizontalAlignment = Alignment.CenterHorizontally,
                        verticalArrangement = Arrangement.spacedBy(20.dp)
                    ) {
                        Text(
                            text = "세비스",
                            color = ACCENT_MAIN,
                            fontSize = 36.sp,
                            fontWeight = FontWeight.Bold
                        )

                        Spacer(modifier = Modifier.height(8.dp))

                        Button(
                            onClick = {
                                context.startActivity(
                                    Intent(context, MyRoomActivity::class.java)
                                )
                            },
                            colors = ButtonDefaults.buttonColors(containerColor = CARD_MAIN),
                            shape = RoundedCornerShape(16.dp),
                            modifier = Modifier
                                .width(220.dp)
                                .height(60.dp)
                        ) {
                            Text("마이룸", color = Color.White, fontSize = 20.sp)
                        }

                        Button(
                            onClick = {
                                val intent = context.packageManager
                                    .getLaunchIntentForPackage("com.sevens.game")
                                if (intent != null) {
                                    context.startActivity(intent)
                                } else {
                                    android.widget.Toast.makeText(
                                        context,
                                        "게임 앱이 설치되지 않았습니다.",
                                        android.widget.Toast.LENGTH_SHORT
                                    ).show()
                                }
                            },
                            colors = ButtonDefaults.buttonColors(containerColor = ACCENT_MAIN),
                            shape = RoundedCornerShape(16.dp),
                            modifier = Modifier
                                .width(220.dp)
                                .height(60.dp)
                        ) {
                            Text("게임 시작", color = Color.White, fontSize = 20.sp)
                        }
                    }
                }
            }
        }
    }
}
