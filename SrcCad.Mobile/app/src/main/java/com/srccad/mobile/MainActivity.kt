package com.srccad.mobile

import android.net.Uri
import android.os.Bundle
import android.provider.OpenableColumns
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.lifecycle.lifecycleScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import org.json.JSONObject
import java.io.File
import java.util.Locale
import java.util.UUID

class MainActivity : ComponentActivity() {
    private lateinit var viewer: CadGlSurfaceView
    private var nativeHandle = 0L
    private var stepReaderAvailable = false
    private var currentImport: File? = null

    private var fileName by mutableStateOf("Henüz dosya açılmadı")
    private var statusText by mutableStateOf("STEP dosyası seçerek başlayın.")
    private var modelSummary by mutableStateOf("Model yüklenmedi")
    private var isBusy by mutableStateOf(false)
    private var hasModel by mutableStateOf(false)
    private var measurementMode by mutableStateOf(false)
    private var measurementAvailable by mutableStateOf(false)
    private var measurementText by mutableStateOf("İki nokta arası ölçüm için önce bir STEP modeli açın.")
    private var infoExpanded by mutableStateOf(false)
    private var firstMeasurePoint: DoubleArray? = null

    private val openDocument = registerForActivityResult(ActivityResultContracts.OpenDocument()) { uri ->
        if (uri != null) importStep(uri)
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        nativeHandle = NativeCadEngine.nativeCreate()
        viewer = CadGlSurfaceView(this)

        if (nativeHandle == 0L) {
            statusText = "Native CAD motoru için bellek ayrılamadı."
        } else {
            val engineStatus = NativeCadEngine.nativeEngineStatus()
            stepReaderAvailable = engineStatus.contains("OCCT STEP çekirdeği bağlı")
            statusText = if (stepReaderAvailable) {
                "STEP okuyucu hazır. Yerel STEP dosyası açabilirsiniz."
            } else {
                "Bu APK demo sürümüdür; STEP motoru henüz bağlanmadığı için STEP açma kapalı."
            }
        }

        viewer.onRayRequested = { ray ->
            if (ray != null) handleMeasurementRay(ray)
            else statusText = "Bu konumda model yüzeyi bulunamadı. Yüzeye dokunup tekrar deneyin."
        }

        setContent {
            MaterialTheme {
                Surface(Modifier.fillMaxSize(), color = Color(0xFF171B22)) {
                    BoxWithConstraints(Modifier.fillMaxSize()) {
                        val compactLayout = maxWidth < 620.dp
                        Column(
                            modifier = Modifier.fillMaxSize().padding(if (compactLayout) 8.dp else 14.dp),
                            verticalArrangement = Arrangement.spacedBy(if (compactLayout) 6.dp else 10.dp)
                        ) {
                            Row(
                                modifier = Modifier.fillMaxWidth(),
                                horizontalArrangement = Arrangement.SpaceBetween,
                                verticalAlignment = Alignment.CenterVertically
                            ) {
                                Row(
                                    verticalAlignment = Alignment.CenterVertically,
                                    horizontalArrangement = Arrangement.spacedBy(8.dp)
                                ) {
                                    Image(
                                        painter = painterResource(R.drawable.srccad_brand),
                                        contentDescription = "SrcCad logo",
                                        modifier = Modifier
                                            .width(if (compactLayout) 104.dp else 120.dp)
                                            .padding(vertical = 4.dp),
                                        contentScale = ContentScale.Fit
                                    )
                                    Column {
                                        Text("SrcCad Mobile", color = Color.White, style = MaterialTheme.typography.titleLarge)
                                        Text("CAD Viewer • Android", color = Color(0xFF9EABB9), style = MaterialTheme.typography.labelMedium)
                                    }
                                }
                                if (!compactLayout) {
                                    FileActionButtons(
                                        compact = false,
                                        isBusy = isBusy,
                                        stepReaderAvailable = stepReaderAvailable,
                                        hasModel = hasModel,
                                        onOpen = { openStepPicker() },
                                        onDemo = { loadDemoBox() },
                                        onFit = { viewer.fitModel() }
                                    )
                                }
                            }

                            if (compactLayout) {
                                FileActionButtons(
                                    compact = true,
                                    isBusy = isBusy,
                                    stepReaderAvailable = stepReaderAvailable,
                                    hasModel = hasModel,
                                    onOpen = { openStepPicker() },
                                    onDemo = { loadDemoBox() },
                                    onFit = { viewer.fitModel() }
                                )
                            }

                            Box(
                                Modifier.weight(1f).fillMaxWidth().background(Color(0xFF202833))
                            ) {
                                AndroidView(factory = { viewer }, modifier = Modifier.fillMaxSize())
                                if (!hasModel) {
                                    Column(
                                        modifier = Modifier.align(Alignment.Center).padding(20.dp),
                                        horizontalAlignment = Alignment.CenterHorizontally,
                                        verticalArrangement = Arrangement.spacedBy(8.dp)
                                    ) {
                                        Text("3D GÖRÜNTÜLEME ALANI", color = Color(0xFFD5DEE8), style = MaterialTheme.typography.titleMedium)
                                        Text(
                                            "Bir CAD dosyası açın veya görüntüleyiciyi Demo Kutu ile deneyin.",
                                            color = Color(0xFF9EABB9),
                                            style = MaterialTheme.typography.bodySmall
                                        )
                                    }
                                }
                                if (isBusy) {
                                    Surface(
                                        modifier = Modifier.align(Alignment.Center),
                                        color = Color(0xDD171B22),
                                        shape = MaterialTheme.shapes.large
                                    ) {
                                        Row(
                                            modifier = Modifier.padding(16.dp),
                                            verticalAlignment = Alignment.CenterVertically,
                                            horizontalArrangement = Arrangement.spacedBy(12.dp)
                                        ) {
                                            CircularProgressIndicator()
                                            Text("Model hazırlanıyor…", color = Color.White)
                                        }
                                    }
                                }
                            }

                            // Compact information strip: the full diagnostics panel stays hidden by default.
                            Row(
                                modifier = Modifier.fillMaxWidth(),
                                verticalAlignment = Alignment.CenterVertically,
                                horizontalArrangement = Arrangement.spacedBy(8.dp)
                            ) {
                                Text(
                                    fileName,
                                    modifier = Modifier.weight(1f),
                                    color = Color.White,
                                    style = MaterialTheme.typography.bodySmall,
                                    maxLines = 1,
                                    overflow = TextOverflow.Ellipsis
                                )
                                if (measurementText.startsWith("Mesafe:")) {
                                    Text(
                                        measurementText.substringBefore("|").trim(),
                                        color = Color(0xFFB8D7F2),
                                        style = MaterialTheme.typography.labelMedium,
                                        maxLines = 1
                                    )
                                }
                                TextButton(onClick = { infoExpanded = !infoExpanded }) {
                                    Text(if (infoExpanded) "Gizle" else "Bilgi")
                                }
                            }

                            if (infoExpanded) {
                                Surface(
                                    modifier = Modifier.fillMaxWidth(),
                                    color = Color(0xFF222B35),
                                    shape = MaterialTheme.shapes.medium
                                ) {
                                    Column(
                                        modifier = Modifier.padding(horizontal = 10.dp, vertical = 7.dp),
                                        verticalArrangement = Arrangement.spacedBy(2.dp)
                                    ) {
                                        Text(modelSummary, color = Color(0xFF9EABB9), style = MaterialTheme.typography.labelSmall)
                                        Text(statusText, color = Color(0xFFB9C5D2), style = MaterialTheme.typography.labelSmall)
                                        Text(measurementText, color = Color(0xFFB8D7F2), style = MaterialTheme.typography.labelSmall)
                                    }
                                }
                            }

                            Row(
                                modifier = Modifier.fillMaxWidth(),
                                horizontalArrangement = Arrangement.spacedBy(8.dp),
                                verticalAlignment = Alignment.CenterVertically
                            ) {
                                Button(onClick = { viewer.fitModel() }, enabled = hasModel) {
                                    Text(if (compactLayout) "Sığdır" else "İzometrik / Sığdır")
                                }
                                Button(
                                    onClick = {
                                        measurementMode = !measurementMode
                                        viewer.measurementMode = measurementMode
                                        firstMeasurePoint = null
                                        measurementText = if (measurementMode) {
                                            "Ölçüm açık: kısa dokunuşla nokta seçin; sürükleyerek modeli döndürün."
                                        } else {
                                            "Ölçüm kapatıldı."
                                        }
                                    },
                                    enabled = hasModel && measurementAvailable && !isBusy
                                ) {
                                    Text(if (measurementMode) "Ölçümü Kapat" else if (compactLayout) "Ölç" else "Mesafe Ölç")
                                }
                                Spacer(Modifier.weight(1f))
                                Text(
                                    if (compactLayout) "Sürükle: döndür • 2 parmak: zoom"
                                    else "Sürükle: döndür  •  İki parmak: kaydır / yakınlaştır",
                                    color = Color(0xFF7F8C9B),
                                    style = MaterialTheme.typography.labelSmall,
                                    maxLines = 1
                                )
                            }
                        }
                    }
                }
            }
        }
    }

    private fun openStepPicker() {
        if (stepReaderAvailable && !isBusy) {
            openDocument.launch(arrayOf("*/*"))
        } else {
            statusText = "STEP okuyucu bu APK'da bulunmuyor."
        }
    }

    private fun loadDemoBox() {
        if (isBusy || nativeHandle == 0L) return
        isBusy = true
        hasModel = false
        measurementAvailable = false
        measurementMode = false
        viewer.measurementMode = false
        firstMeasurePoint = null
        measurementText = "Demo geometri: 100 × 50 × 20 mm."
        modelSummary = "Demo model hazırlanıyor…"
        viewer.clearMesh()

        lifecycleScope.launch {
            try {
                val result = withContext(Dispatchers.IO) {
                    val response = JSONObject(NativeCadEngine.nativeLoadDemoBox(nativeHandle))
                    if (!response.optBoolean("ok")) {
                        throw IllegalStateException(response.optString("message", "Demo model oluşturulamadı."))
                    }
                    val vertices = NativeCadEngine.nativeGetVertices(nativeHandle)
                    val indices = NativeCadEngine.nativeGetIndices(nativeHandle)
                    val bounds = NativeCadEngine.nativeGetBounds(nativeHandle)
                    if (vertices.isEmpty() || indices.isEmpty() || bounds.size != 6) {
                        throw IllegalStateException("Demo model görüntüleme ağı oluşturulamadı.")
                    }
                    ImportResult(
                        "Demo Kutu • 100 × 50 × 20 mm",
                        null,
                        response.optInt("faces"),
                        response.optInt("triangles"),
                        vertices,
                        indices,
                        bounds
                    )
                }
                presentModel(result, enableMeasurement = false)
                statusText = "Demo model yüklendi. Döndürme, kaydırma ve yakınlaştırmayı deneyebilirsiniz."
            } catch (error: Exception) {
                statusText = error.message ?: "Demo model oluşturulurken hata oluştu."
                modelSummary = "Demo model yüklenemedi"
                hasModel = false
                measurementAvailable = false
            } finally {
                isBusy = false
            }
        }
    }

    private fun presentModel(result: ImportResult, enableMeasurement: Boolean) {
        currentImport?.delete()
        currentImport = result.file
        viewer.submitMesh(result.vertices, result.indices, result.bounds)
        fileName = result.displayName
        modelSummary = result.faces.toString() + " yüzey • " + result.triangles + " üçgen"
        measurementAvailable = enableMeasurement
        measurementMode = false
        viewer.measurementMode = false
        firstMeasurePoint = null
        hasModel = true
        measurementText = if (enableMeasurement) {
            "İki yüzey noktasını seçerek mesafe ölçebilirsiniz."
        } else {
            "Demo geometri: 100 × 50 × 20 mm • Ölçüm için STEP dosyası açın."
        }
    }

    private fun importStep(uri: Uri) {
        if (isBusy) return
        isBusy = true
        hasModel = false
        measurementAvailable = false
        measurementMode = false
        viewer.measurementMode = false
        firstMeasurePoint = null
        measurementText = "Model içe aktarılıyor…"
        modelSummary = "Dosya hazırlanıyor…"
        viewer.clearMesh()

        lifecycleScope.launch {
            try {
                val result = withContext(Dispatchers.IO) {
                    val displayName = queryDisplayName(uri) ?: "model.step"
                    val extension = displayName.substringAfterLast('.', "").lowercase()
                    if (extension != "step" && extension != "stp") {
                        throw IllegalArgumentException("Bu sürüm STEP/STP dosyalarını kabul ediyor. Seçilen: " + displayName)
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
                        displayName,
                        destination,
                        response.optInt("faces"),
                        response.optInt("triangles"),
                        vertices,
                        indices,
                        bounds
                    )
                }

                presentModel(result, enableMeasurement = true)
                statusText = "STEP modeli yüklendi. Modeli sürükleyerek döndürebilirsiniz."
            } catch (error: Exception) {
                statusText = error.message ?: "Beklenmeyen içe aktarma hatası."
                modelSummary = "Model yüklenemedi"
                hasModel = false
                measurementAvailable = false
            } finally {
                isBusy = false
            }
        }
    }

    private fun handleMeasurementRay(ray: FloatArray) {
        if (!measurementMode || !measurementAvailable || ray.size < 6 || nativeHandle == 0L) return
        val origin = doubleArrayOf(ray[0].toDouble(), ray[1].toDouble(), ray[2].toDouble())
        val direction = doubleArrayOf(ray[3].toDouble(), ray[4].toDouble(), ray[5].toDouble())

        lifecycleScope.launch {
            try {
                val point = withContext(Dispatchers.Default) {
                    NativeCadEngine.nativePickRay(nativeHandle, origin, direction)
                }
                if (point.size < 3) {
                    statusText = "Yüzey yakalanamadı. Modelin görünen bir yüzeyine dokunun."
                    return@launch
                }

                val first = firstMeasurePoint
                if (first == null) {
                    firstMeasurePoint = point
                    measurementText = "İlk nokta: X " + formatMm(point[0]) + ", Y " +
                        formatMm(point[1]) + ", Z " + formatMm(point[2]) + " mm. İkinci noktayı seçin."
                    statusText = "İlk ölçüm noktası alındı."
                } else {
                    val values = withContext(Dispatchers.Default) {
                        NativeCadEngine.nativeMeasurePoints(nativeHandle, first, point)
                    }
                    firstMeasurePoint = null
                    if (values.size >= 4) {
                        measurementText = "Mesafe: " + formatMm(values[0]) + " mm | ΔX " +
                            formatMm(values[1]) + " ΔY " + formatMm(values[2]) +
                            " ΔZ " + formatMm(values[3]) + " mm"
                        statusText = "Ölçüm tamamlandı."
                    } else {
                        measurementText = "Ölçüm hesaplanamadı."
                        statusText = "Native ölçüm motoru geçerli sonuç döndürmedi."
                    }
                }
            } catch (error: Exception) {
                statusText = error.message ?: "Nokta seçimi sırasında hata oluştu."
            }
        }
    }

    private fun formatMm(value: Double): String = String.format(Locale.US, "%.3f", value)

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
        val displayName: String,
        val file: File?,
        val faces: Int,
        val triangles: Int,
        val vertices: FloatArray,
        val indices: IntArray,
        val bounds: DoubleArray
    )
}

@Composable
private fun FileActionButtons(
    compact: Boolean,
    isBusy: Boolean,
    stepReaderAvailable: Boolean,
    hasModel: Boolean,
    onOpen: () -> Unit,
    onDemo: () -> Unit,
    onFit: () -> Unit
) {
    Row(
        horizontalArrangement = Arrangement.spacedBy(if (compact) 6.dp else 8.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Button(onClick = onOpen, enabled = !isBusy && stepReaderAvailable) {
            Text("Aç")
        }
        Button(onClick = onDemo, enabled = !isBusy) {
            Text(if (compact) "Demo" else "Demo Kutu")
        }
        Button(onClick = onFit, enabled = hasModel && !isBusy) {
            Text(if (compact) "Sığdır" else "Ekrana Sığdır")
        }
    }
}
