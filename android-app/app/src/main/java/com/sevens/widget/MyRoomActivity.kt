package com.sevens.widget

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import com.sevens.widget.ui.theme.SevensWidgetTheme

class MyRoomActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContent {
            SevensWidgetTheme {
                ChatScreen()
            }
        }
    }
}
