package com.srccad.mobile

import android.net.Uri
import android.os.Bundle
import android.provider.OpenableColumns
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.lifecycle.lifecycleScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import org.json.JSONObject
import java.io.File
import java.util.UUID

class MainActivity : ComponentActivity() {
    private lateinit var viewer: CadGlSurfaceView
    private var nativeHandle = 0L
    private var currentImport: File? = null

    private var fileName by mutableStateOf("Henüz dosya açılmadı")
    private var statusText by mutableStateOf("STEP dosyası seçerek başlayın.")
    private var modelSummary by mutableStateOf("Model yüklenmedi")
    private var isBusy by mutableStateOf(false)
    private var hasModel by mutableStateOf(false)

    private val openDocument = registerForActivityResult(ActivityResultContracts.OpenDocument()) { uri ->
        if (uri != null) importStep(uri)
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        nativeHandle = NativeCadEngine.nativeCreate()
        viewer = CadGlSurfaceView(this)

        setContent {
            MaterialTheme {
                Surface(Modifier.fillMaxSize(), color = Color(0xFF171B22)) {
                    Column(
                        modifier = Modifier.fillMaxSize().padding(14.dp),
                        verticalArrangement = Arrangement.spacedBy(10.dp)
                    ) {
                        Row(
                            modifier = Modifier.fillMaxWidth(),
                            horizontalArrangement = Arrangement.SpaceBetween,
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            Column {
                                Text("SrcCad Mobile", color = Color.White, style = MaterialTheme.typography.titleLarge)
                                Text("CAD Viewer • Android", color = Color(0xFF9EABB9), style = MaterialTheme.typography.labelMedium)
                            }
                            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                                Button(onClick = { openDocument.launch(arrayOf("*/*")) }, enabled = !isBusy) {
                                    Text("STEP Aç")
                                }
                                Button(onClick = { viewer.fitModel() }, enabled = hasModel && !isBusy) {
                                    Text("Ekrana Sığdır")
                                }
                            }
                        }

                        Box(Modifier.weight(1f).fillMaxWidth().background(Color(0xFF202833))) {
                            AndroidView(factory = { viewer }, modifier = Modifier.fillMaxSize())
                            if (!hasModel) {
                                Column(
                                    modifier = Modifier.align(Alignment.Center).padding(20.dp),
                                    horizontalAlignment = Alignment.CenterHorizontally,
                                    verticalArrangement = Arrangement.spacedBy(8.dp)
                                ) {
                                    Text("3D GÖRÜNTÜLEME ALANI", color = Color(0xFFD5DEE8), style = MaterialTheme.typography.titleMedium)
                                    Text(
                                        "STEP dosyası açıldığında model burada görüntülenecek.",
                                        color = Color(0xFF9EABB9), style = MaterialTheme.typography.bodySmall
                                    )
                                }
                            }
                            if (isBusy) {
                                Surface(
                                    modifier = Modifier.align(Alignment.Center),
                                    color = Color(0xDD171B22), shape = MaterialTheme.shapes.large
                                ) {
                                    Row(
                                        modifier = Modifier.padding(16.dp),
                                        verticalAlignment = Alignment.CenterVertically,
                                        horizontalArrangement = Arrangement.spacedBy(12.dp)
                                    ) {
                                        CircularProgressIndicator()
                                        Text("STEP içe aktarılıyor…", color = Color.White)
                                    }
                                }
                            }
                        }

                        Column(verticalArrangement = Arrangement.spacedBy(3.dp)) {
                            Text(fileName, color = Color.White, style = MaterialTheme.typography.bodyMedium)
                            Text(modelSummary, color = Color(0xFF9EABB9), style = MaterialTheme.typography.bodySmall)
                            Text(statusText, color = Color(0xFFB9C5D2), style = MaterialTheme.typography.bodySmall)
                        }

                        Row(
                            modifier = Modifier.fillMaxWidth(),
                            horizontalArrangement = Arrangement.spacedBy(8.dp),
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            Button(onClick = { viewer.fitModel() }, enabled = hasModel) { Text("İzometrik / Sığdır") }
                            Button(onClick = { }, enabled = false) { Text("Ölçüm (yakında)") }
                            Spacer(Modifier.weight(1f))
                            Text(
                                "Tek parmak: döndür  •  İki parmak: kaydır / yakınlaştır",
                                color = Color(0xFF7F8C9B), style = MaterialTheme.typography.labelSmall
                            )
                        }
                    }
                }
            }
        }
        statusText = NativeCadEngine.nativeEngineStatus()
    }

    private fun importStep(uri: Uri) {
        if (isBusy) return
        isBusy = true
        hasModel = false
        modelSummary = "Dosya hazırlanıyor…"
        viewer.clearMesh()

        lifecycleScope.launch {
            try {
                val result = withContext(Dispatchers.IO) {
                    val displayName = queryDisplayName(uri) ?: "model.step"
                    val extension = displayName.substringAfterLast('.', "").lowercase()
                    if (extension != "step" && extension != "stp") {
                        throw IllegalArgumentException("İlk sürüm STEP/STP dosyalarını kabul ediyor. Seçilen: " + displayName)
                    }

                    val directory = File(cacheDir, "step-imports").apply { mkdirs() }
                    val destination = File(directory, UUID.randomUUID().toString() + "." + extension)
                    val input = contentResolver.openInputStream(uri)
                        ?: throw IllegalStateException("Seçilen dosya okunamadı.")
                    input.use { source -> destination.outputStream().use { target -> source.copyTo(target) } }

                    val response = JSONObject(NativeCadEngine.nativeLoadStep(nativeHandle, destination.absolutePath))
                    if (!response.optBoolean("ok")) {
                        destination.delete()
                        throw IllegalStateException(response.optString("message", "STEP içe aktarma başarısız."))
                    }

                    val vertices = NativeCadEngine.nativeGetVertices(nativeHandle)
                    val indices = NativeCadEngine.nativeGetIndices(nativeHandle)
                    val bounds = NativeCadEngine.nativeGetBounds(nativeHandle)
                    if (vertices.isEmpty() || indices.isEmpty() || bounds.size != 6) {
                        destination.delete()
                        throw IllegalStateException("Geometri okundu ancak görüntülenebilir üçgen ağı üretilemedi.")
                    }
                    ImportResult(
                        displayName, destination, response.optInt("faces"), response.optInt("triangles"),
                        vertices, indices, bounds
                    )
                }

                currentImport?.delete()
                currentImport = result.file
                viewer.submitMesh(result.vertices, result.indices, result.bounds)
                fileName = result.displayName
                modelSummary = result.faces.toString() + " yüzey • " + result.triangles + " üçgen"
                statusText = "STEP modeli yüklendi. Geometrik ölçüm henüz etkin değil."
                hasModel = true
            } catch (error: Exception) {
                statusText = error.message ?: "Beklenmeyen içe aktarma hatası."
                modelSummary = "Model yüklenemedi"
                hasModel = false
            } finally {
                isBusy = false
            }
        }
    }

    private fun queryDisplayName(uri: Uri): String? {
        contentResolver.query(uri, arrayOf(OpenableColumns.DISPLAY_NAME), null, null, null)?.use { cursor ->
            if (cursor.moveToFirst()) {
                val index = cursor.getColumnIndex(OpenableColumns.DISPLAY_NAME)
                if (index >= 0) return cursor.getString(index)
            }
        }
        return null
    }

    override fun onResume() {
        super.onResume()
        if (::viewer.isInitialized) viewer.onResume()
    }

    override fun onPause() {
        if (::viewer.isInitialized) viewer.onPause()
        super.onPause()
    }

    override fun onDestroy() {
        if (nativeHandle != 0L) {
            NativeCadEngine.nativeDestroy(nativeHandle)
            nativeHandle = 0L
        }
        currentImport?.delete()
        super.onDestroy()
    }

    private data class ImportResult(
        val displayName: String, val file: File, val faces: Int, val triangles: Int,
        val vertices: FloatArray, val indices: IntArray, val bounds: DoubleArray
    )
}
