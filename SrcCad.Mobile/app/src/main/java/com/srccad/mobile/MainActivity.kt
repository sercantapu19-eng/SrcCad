package com.srccad.mobile

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Button
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp

class MainActivity : ComponentActivity() {
    private val openStep = registerForActivityResult(ActivityResultContracts.OpenDocument()) { uri ->
        uri?.let {
            contentResolver.takePersistableUriPermission(
                it,
                android.content.Intent.FLAG_GRANT_READ_URI_PERMISSION
            )
            selectedFileName = it.lastPathSegment ?: "STEP file"
            statusText = "Dosya seçildi. OCCT STEP motoru henüz bağlanmadı."
        }
    }

    private var selectedFileName by mutableStateOf("Henüz dosya açılmadı")
    private var statusText by mutableStateOf("STEP dosyası seçerek başlayın.")

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContent {
            MaterialTheme {
                Surface(Modifier.fillMaxSize()) {
                    Column(
                        Modifier.fillMaxSize().background(Color(0xFF171B22)).padding(16.dp),
                        verticalArrangement = Arrangement.spacedBy(12.dp)
                    ) {
                        Row(
                            Modifier.fillMaxWidth(),
                            horizontalArrangement = Arrangement.SpaceBetween,
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            Text("SrcCad Mobile", color = Color.White, style = MaterialTheme.typography.titleLarge)
                            Button(onClick = { openStep.launch(arrayOf("*/*")) }) {
                                Text("STEP Aç")
                            }
                        }

                        Box(
                            Modifier.weight(1f).fillMaxWidth().background(Color(0xFF252C36)),
                            contentAlignment = Alignment.Center
                        ) {
                            Column(horizontalAlignment = Alignment.CenterHorizontally) {
                                Text("3D GÖRÜNTÜLEME ALANI", color = Color(0xFF9EABB9))
                                Text("OCCT Android renderer bekleniyor", color = Color(0xFF657383))
                            }
                        }

                        Text(selectedFileName, color = Color.White)
                        Text(statusText, color = Color(0xFFB9C5D2))
                        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            Button(onClick = { statusText = "Döndürme aracı: renderer bağlandıktan sonra etkinleşecek." }) { Text("Döndür") }
                            Button(onClick = { statusText = "Ölçüm: geometrik seçim motoru henüz bağlanmadı." }) { Text("Ölç") }
                            Button(onClick = { statusText = "Kesit: sonraki geliştirme aşaması." }) { Text("Kesit") }
                        }
                    }
                }
            }
        }
    }
}
