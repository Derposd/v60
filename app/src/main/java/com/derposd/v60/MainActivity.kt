package com.derposd.v60

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import com.derposd.v60.ui.V60App
import com.derposd.v60.ui.theme.V60Theme

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        setContent {
            V60Theme {
                V60App()
            }
        }
    }
}
