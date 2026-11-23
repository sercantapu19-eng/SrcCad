package com.srccad.mobile

import android.app.AlertDialog
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
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.layout.navigationBarsPadding
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
    private var dxfDrawing by mutableStateOf<DxfDrawing?>(null)
    private var dxfFitToken by mutableStateOf(0)
    private var measurementText by mutableStateOf("İki nokta arası ölçüm için önce bir STEP modeli açın.")
    private var infoExpanded by mutableStateOf(false)
    private var firstMeasurePick: DoubleArray? = null
    private var hasFirstMeasurePick by mutableStateOf(false)
    private var measurementHistory by mutableStateOf(emptyList<String>())

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
            if (ray == null) {
                if (!measurementMode) {
                    viewer.highlightFaces(intArrayOf())
                    viewer.highlightEdges()
                }
                statusText = "Bu konumda model yüzeyi bulunamadı. Yüzeye dokunup tekrar deneyin."
            } else if (measurementMode) {
                handleMeasurementRay(ray)
            } else {
                handleModelSelectionRay(ray)
            }
        }

        setContent {
            MaterialTheme {
                Surface(Modifier.fillMaxSize(), color = Color(0xFF171B22)) {
                    BoxWithConstraints(Modifier.fillMaxSize().safeDrawingPadding()) {
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
                                        onFit = { if (dxfDrawing != null) dxfFitToken++ else viewer.fitModel() }
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
                                    onFit = { if (dxfDrawing != null) dxfFitToken++ else viewer.fitModel() }
                                )
                            }

                            Box(
                                Modifier.weight(1f).fillMaxWidth().background(Color(0xFF202833))
                            ) {
                                AndroidView(factory = { viewer }, modifier = Modifier.fillMaxSize())
                                val dxf = dxfDrawing
                                if (dxf != null) {
                                    DxfCanvasView(dxf, dxfFitToken, Modifier.fillMaxSize())
                                }
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
                                val measurementPreview = measurementText.substringBefore("\n").trim()
                                if (
                                    measurementPreview.startsWith("Doğru kenar") ||
                                    measurementPreview.startsWith("Dairesel kenar") ||
                                    measurementPreview.startsWith("Eğri kenar") ||
                                    measurementPreview.startsWith("Düzlemsel yüzey") ||
                                    measurementPreview.startsWith("Silindirik yüzey") ||
                                    measurementPreview.startsWith("Yüzey") ||
                                    measurementPreview.startsWith("1)")
                                ) {
                                    Text(
                                        measurementPreview,
                                        color = Color(0xFFB8D7F2),
                                        style = MaterialTheme.typography.labelMedium,
                                        maxLines = 1,
                                        overflow = TextOverflow.Ellipsis
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
                                        if (measurementHistory.isNotEmpty()) {
                                            Text("ÖLÇÜ GEÇMİŞİ", color = Color(0xFF9EABB9), style = MaterialTheme.typography.labelSmall)
                                            measurementHistory.take(5).forEachIndexed { index, item ->
                                                Text("${index + 1}. $item", color = Color(0xFFD5DEE8), style = MaterialTheme.typography.labelSmall)
                                            }
                                            TextButton(onClick = { measurementHistory = emptyList() }) {
                                                Text("Ölçü Geçmişini Temizle")
                                            }
                                        }
                                    }
                                }
                            }

                            Row(
                                modifier = Modifier
                                    .fillMaxWidth(),
                                horizontalArrangement = Arrangement.spacedBy(8.dp),
                                verticalAlignment = Alignment.CenterVertically
                            ) {
                                Button(
                                    onClick = { viewer.fitModel() },
                                    enabled = hasModel,
                                    modifier = if (compactLayout) Modifier.weight(1f) else Modifier
                                ) {
                                    Text(if (compactLayout) "Sığdır" else "İzometrik / Sığdır")
                                }
                                Button(
                                    onClick = {
                                        measurementMode = !measurementMode
                                        viewer.measurementMode = measurementMode
                                        firstMeasurePick = null
                                        hasFirstMeasurePick = false
                                        viewer.setMeasurementDimension(null, null)
                                        measurementText = if (measurementMode) {
                                            "Otomatik ölçüm açık: kenara/yüzeye dokunun; sürükleyerek modeli döndürün."
                                        } else {
                                            "Ölçüm kapatıldı."
                                        }
                                    },
                                    enabled = hasModel && measurementAvailable && !isBusy,
                                    modifier = if (compactLayout) Modifier.weight(1f) else Modifier
                                ) {
                                    Text(if (measurementMode) "Ölçümü Kapat" else if (compactLayout) "Ölç" else "Otomatik Ölçüm")
                                }
                                if (measurementMode) {
                                    TextButton(
                                        onClick = {
                                            firstMeasurePick = null
                                            hasFirstMeasurePick = false
                                            viewer.highlightFaces(intArrayOf())
                                            viewer.highlightEdges()
                                            measurementText = "İlk seçim temizlendi. Yeni ölçüm için bir kenara veya yüzeye dokunun."
                                            statusText = "Ölçüm seçimi temizlendi."
                                        },
                                        enabled = hasFirstMeasurePick
                                    ) {
                                        Text("İlk Seçimi Temizle")
                                    }
                                }
                                if (!compactLayout) {
                                    Spacer(Modifier.weight(1f))
                                    Text(
                                        "Sürükle: döndür  •  İki parmak: kaydır / yakınlaştır",
                                        color = Color(0xFF7F8C9B),
                                        style = MaterialTheme.typography.labelSmall,
                                        maxLines = 1
                                    )
                                }
                            }
                            Row(
                                modifier = Modifier.fillMaxWidth(),
                                horizontalArrangement = Arrangement.spacedBy(4.dp),
                                verticalAlignment = Alignment.CenterVertically
                            ) {
                                TextButton(onClick = { viewer.setStandardView("front") }, enabled = hasModel) { Text("Ön") }
                                TextButton(onClick = { viewer.setStandardView("top") }, enabled = hasModel) { Text("Üst") }
                                TextButton(onClick = { viewer.setStandardView("right") }, enabled = hasModel) { Text("Sağ") }
                                TextButton(onClick = { viewer.setStandardView("left") }, enabled = hasModel) { Text("Sol") }
                                TextButton(onClick = { viewer.setStandardView("isometric") }, enabled = hasModel) { Text("İzometrik") }
                            }
                        }
                    }
                }
            }
        }
    }

    private fun openStepPicker() {
        if (!isBusy) {
            openDocument.launch(arrayOf("*/*"))
        } else {
            statusText = "Devam eden bir içe aktarma var, lütfen bekleyin."
        }
    }

    private fun loadDemoBox() {
        if (isBusy || nativeHandle == 0L) return
        isBusy = true
        hasModel = false
        measurementAvailable = false
        measurementMode = false
        viewer.measurementMode = false
        firstMeasurePick = null
        measurementText = "Demo geometri: 100 × 50 × 20 mm."
        modelSummary = "Demo model hazırlanıyor…"
        viewer.clearMesh()
        dxfDrawing = null

        lifecycleScope.launch {
            try {
                val result = withContext(Dispatchers.IO) {
                    val response = JSONObject(NativeCadEngine.nativeLoadDemoBox(nativeHandle))
                    if (!response.optBoolean("ok")) {
                        throw IllegalStateException(response.optString("message", "Demo model oluşturulamadı."))
                    }
                    val vertices = NativeCadEngine.nativeGetVertices(nativeHandle)
                    val indices = NativeCadEngine.nativeGetIndices(nativeHandle)
                    val faceIds = NativeCadEngine.nativeGetTriangleFaceIndices(nativeHandle)
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
                        bounds,
                        faceIds
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
        dxfDrawing = null
        currentImport?.delete()
        currentImport = result.file
        viewer.submitMesh(result.vertices, result.indices, result.bounds, result.triangleFaceIds)
        fileName = result.displayName
        modelSummary = if (result.faces > 0) result.faces.toString() + " yüzey • " + result.triangles + " üçgen"
            else "Ağ (mesh) modeli • " + result.triangles + " üçgen"
        measurementAvailable = enableMeasurement
        measurementMode = false
        viewer.measurementMode = false
        firstMeasurePick = null
        hasModel = true
        measurementText = if (enableMeasurement) {
            "İki yüzey noktasını seçerek mesafe ölçebilirsiniz."
        } else {
            if (result.faces == 0) "Ağ (mesh) modeli: yüzey seçimi ve hassas ölçüm yalnızca STEP/IGES/BREP B-Rep modellerinde çalışır."
            else "Demo geometri: 100 × 50 × 20 mm • Ölçüm için STEP dosyası açın."
        }
    }

    private fun unsupportedFormatMessage(name: String, ext: String): String = when (ext) {
        "sldprt", "sldasm", "slddrw", "easm", "eprt", "edrw" ->
            "$name açılamadı: SolidWorks/eDrawings biçimleri kapalıdır ve ücretsiz bir okuyucusu yoktur. Ticari SDK gerekir veya dosyayı kaynak programdan STEP olarak dışa aktarın."
        "ipt", "iam", "idw" ->
            "$name açılamadı: Inventor biçimleri kapalıdır. Ticari SDK gerekir veya STEP olarak dışa aktarın."
        "x_t", "x_b" ->
            "$name açılamadı: Parasolid ücretsiz okunamaz. Ticari SDK gerekir veya STEP olarak dışa aktarın."
        "sat", "sab" ->
            "$name açılamadı: ACIS ücretsiz okunamaz. Ticari SDK gerekir veya STEP olarak dışa aktarın."
        "dwg" ->
            "$name açılamadı: DWG kapalı bir formattır. Dosyayı DXF'e dönüştürüp (ör. ODA File Converter) açın."
        else ->
            "$name açılamadı. Desteklenenler: STEP/STP, IGES/IGS, BREP, STL, OBJ, 3MF (3B) ve DXF (2B)."
    }

    private fun presentDxf(name: String, drawing: DxfDrawing) {
        currentImport?.delete()
        currentImport = null
        viewer.clearMesh()
        dxfDrawing = drawing
        dxfFitToken++
        fileName = name
        modelSummary = "2B çizim • " + drawing.entityCount + " nesne • " + drawing.layerCount + " katman"
        measurementAvailable = false
        measurementMode = false
        viewer.measurementMode = false
        firstMeasurePick = null
        hasModel = true
        measurementText = "2B çizim görüntüleniyor (kaydır / iki parmakla yakınlaştır). Ölçüm yalnızca 3B modellerde çalışır." +
            if (drawing.skipped.isEmpty()) "" else "\nÇizilmeyen nesneler: " + drawing.skippedSummary()
    }

    private fun importSolidWorks(uri: Uri, displayName: String) {
        if (!stepReaderAvailable) { statusText = "SolidWorks aktarımı için OCCT STEP okuyucusu gerekli."; return }
        isBusy = true; fileName = displayName; statusText = "SolidWorks dosyası inceleniyor…"; hasModel = false
        lifecycleScope.launch {
            var source: File? = null
            try {
                val json = withContext(Dispatchers.IO) {
                    val dir = File(cacheDir, "solidworks").apply { mkdirs() }
                    source = File(dir, UUID.randomUUID().toString() + ".sldprt")
                    contentResolver.openInputStream(uri)?.use { input -> source!!.outputStream().use { input.copyTo(it) } }
                        ?: throw IllegalStateException("Dosya okunamadı.")
                    JSONObject(NativeCadEngine.nativeInspectSolidWorks(source!!.absolutePath))
                }
                if (!json.optBoolean("ok")) throw IllegalStateException(json.optString("message", "SolidWorks incelenemedi."))
                val configs = json.optJSONArray("configurations") ?: throw IllegalStateException("Konfigürasyon bulunamadı.")
                isBusy = false
                val names = Array(configs.length()) { i -> configs.optJSONObject(i)?.optString("name").orEmpty().ifBlank { "Configuration " + (i+1) } }
                AlertDialog.Builder(this@MainActivity).setTitle("Konfigürasyon seç").setItems(names) { _, i ->
                    val cfg = configs.optJSONObject(i) ?: return@setItems
                    val all = cfg.optJSONArray("candidates") ?: org.json.JSONArray()
                    val valid = mutableListOf<JSONObject>()
                    val labels = mutableListOf<String>()
                    for (j in 0 until all.length()) {
                        val item = all.optJSONObject(j) ?: continue
                        if (!item.optBoolean("selectedPartition") || !item.optBoolean("decodedComplete") || item.optString("role") != "PARTITION") continue
                        val bodies = item.optJSONArray("bodies") ?: continue
                        if ((0 until bodies.length()).none { bodies.optJSONObject(it)?.optString("kind") == "SOLID" }) continue
                        valid.add(item); labels.add("Payload " + item.optInt("payloadIndex"))
                    }
                    if (valid.isEmpty()) { source?.delete(); statusText = "Bu konfigürasyonda güvenli aktarım için uygun katı gövde bulunamadı."; return@setItems }
                    AlertDialog.Builder(this@MainActivity).setTitle("Geometri kaynağı").setItems(labels.toTypedArray()) { _, k ->
                        val item = valid[k]; val bodies = item.optJSONArray("bodies") ?: org.json.JSONArray()
                        val ids = mutableListOf<Int>(); val bodyLabels = mutableListOf<String>()
                        for (n in 0 until bodies.length()) { val b = bodies.optJSONObject(n) ?: continue; if (b.optString("kind") == "SOLID") { ids.add(b.optInt("id")); bodyLabels.add("Katı BODY #" + b.optInt("id")) } }
                        AlertDialog.Builder(this@MainActivity).setTitle("Katı gövde seç").setItems(bodyLabels.toTypedArray()) { _, n ->
                            convertSolidWorksBody(source!!, displayName, cfg.optString("name"), item.optInt("payloadIndex"), ids[n])
                        }.setOnCancelListener { source?.delete() }.show()
                    }.setOnCancelListener { source?.delete() }.show()
                }.setOnCancelListener { source?.delete() }.show()
            } catch (e: Exception) { source?.delete(); isBusy = false; statusText = e.message ?: "SolidWorks açılamadı." }
        }
    }

    private fun convertSolidWorksBody(source: File, displayName: String, config: String, payload: Int, body: Int) {
        isBusy = true; statusText = "SolidWorks gövdesi STEP formatına dönüştürülüyor…"
        lifecycleScope.launch {
            var step: File? = null
            try {
                val model = withContext(Dispatchers.IO) {
                    val dir = File(cacheDir, "solidworks-step").apply { mkdirs() }
                    step = File(dir, UUID.randomUUID().toString() + ".step")
                    val export = JSONObject(NativeCadEngine.nativeSolidWorksToStepSelected(source.absolutePath, config, payload, body, step!!.absolutePath))
                    if (!export.optBoolean("ok")) throw IllegalStateException(export.optString("message", "STEP dönüşümü başarısız."))
                    val loaded = JSONObject(NativeCadEngine.nativeLoadCad(nativeHandle, step!!.absolutePath, "step"))
                    if (!loaded.optBoolean("ok")) throw IllegalStateException(loaded.optString("message", "STEP açılamadı."))
                    val v = NativeCadEngine.nativeGetVertices(nativeHandle); val ix = NativeCadEngine.nativeGetIndices(nativeHandle)
                    val f = NativeCadEngine.nativeGetTriangleFaceIndices(nativeHandle); val bounds = NativeCadEngine.nativeGetBounds(nativeHandle)
                    if (v.isEmpty() || ix.isEmpty() || bounds.size != 6) throw IllegalStateException("Görüntülenebilir geometri üretilemedi.")
                    ImportResult(displayName, step, loaded.optInt("faces"), loaded.optInt("triangles"), v, ix, bounds, f)
                }
                source.delete(); presentModel(model, enableMeasurement = true); statusText = displayName + " • " + config + " • BODY #" + body + " açıldı."
            } catch (e: Exception) { source.delete(); step?.delete(); hasModel = false; statusText = e.message ?: "SolidWorks aktarımı başarısız." }
            finally { isBusy = false }
        }
    }
    private fun importStep(uri: Uri) {
        if (isBusy) return
        val candidateName = queryDisplayName(uri) ?: uri.lastPathSegment?.substringAfterLast('/') ?: "model.step"
        if (candidateName.substringAfterLast('.', "").lowercase(Locale.ROOT) == "sldprt") {
            importSolidWorks(uri, candidateName)
            return
        }
        // Update the selected filename immediately so a failed import cannot leave
        // the previous model name beside the new file's error message.
        val selectedName = queryDisplayName(uri)
            ?: uri.lastPathSegment?.substringAfterLast('/')?.takeIf { it.isNotBlank() }
            ?: "model.step"
        fileName = selectedName
        statusText = "$selectedName seçildi. Dosya biçimi doğrulanıyor…"
        isBusy = true
        hasModel = false
        measurementAvailable = false
        measurementMode = false
        viewer.measurementMode = false
        firstMeasurePick = null
        measurementText = "Model içe aktarılıyor…"
        modelSummary = "Dosya hazırlanıyor…"
        viewer.clearMesh()
        dxfDrawing = null

        lifecycleScope.launch {
            try {
                val loaded = withContext(Dispatchers.IO) {
                    val extension = selectedName.substringAfterLast('.', "").lowercase(Locale.ROOT)
                    val kind = when (extension) {
                        "step", "stp" -> "step"
                        "iges", "igs" -> "iges"
                        "brep", "brp" -> "brep"
                        "stl", "obj", "3mf" -> "mesh"
                        "dxf" -> "dxf"
                        else -> throw IllegalArgumentException(unsupportedFormatMessage(selectedName, extension))
                    }

                    val directory = File(cacheDir, "step-imports").apply { mkdirs() }
                    val destination = File(directory, UUID.randomUUID().toString() + "." + extension)
                    val input = contentResolver.openInputStream(uri)
                        ?: throw IllegalStateException("Seçilen dosya okunamadı.")
                    input.use { source -> destination.outputStream().use { target -> source.copyTo(target) } }

                    try {
                        when (kind) {
                            "dxf" -> {
                                val drawing = DxfReader.read(destination)
                                destination.delete()
                                Loaded(null, drawing, false)
                            }
                            "mesh" -> {
                                val mesh = when (extension) {
                                    "stl" -> MeshFileReaders.readStl(destination)
                                    "obj" -> MeshFileReaders.readObj(destination)
                                    else -> MeshFileReaders.read3mf(destination)
                                }
                                destination.delete()
                                Loaded(
                                    ImportResult(
                                        selectedName, null, 0, mesh.triangles,
                                        mesh.vertices, mesh.indices, mesh.bounds, IntArray(mesh.triangles) { -1 }
                                    ),
                                    null, false
                                )
                            }
                            else -> {
                                if (!stepReaderAvailable) {
                                    throw IllegalStateException("Bu APK'da OCCT B-Rep okuyucusu yok; $selectedName açılamaz.")
                                }
                                val response = JSONObject(
                                    NativeCadEngine.nativeLoadCad(nativeHandle, destination.absolutePath, kind)
                                )
                                if (!response.optBoolean("ok")) {
                                    throw IllegalStateException(response.optString("message", "İçe aktarma başarısız."))
                                }
                                val vertices = NativeCadEngine.nativeGetVertices(nativeHandle)
                                val indices = NativeCadEngine.nativeGetIndices(nativeHandle)
                                val faceIds = NativeCadEngine.nativeGetTriangleFaceIndices(nativeHandle)
                                val bounds = NativeCadEngine.nativeGetBounds(nativeHandle)
                                if (vertices.isEmpty() || indices.isEmpty() || bounds.size != 6) {
                                    throw IllegalStateException("Geometri okundu ancak görüntülenebilir üçgen ağı üretilemedi.")
                                }
                                Loaded(
                                    ImportResult(
                                        selectedName, destination,
                                        response.optInt("faces"), response.optInt("triangles"),
                                        vertices, indices, bounds, faceIds
                                    ),
                                    null, true
                                )
                            }
                        }
                    } catch (oom: OutOfMemoryError) {
                        destination.delete()
                        throw IllegalStateException("Bellek yetersiz: $selectedName bu cihaz için çok büyük.")
                    } catch (error: Exception) {
                        destination.delete()
                        throw error
                    }
                }

                val dxf = loaded.dxf
                if (dxf != null) {
                    presentDxf(selectedName, dxf)
                    statusText = "$selectedName 2B çizim olarak açıldı. Kaydırın ve iki parmakla yakınlaştırın."
                } else {
                    presentModel(loaded.model!!, enableMeasurement = loaded.measurable)
                    statusText = if (loaded.measurable) {
                        "$selectedName başarıyla açıldı. Yüzeye dokunarak seçebilirsiniz."
                    } else {
                        "$selectedName açıldı (ağ modeli). Yüzey seçimi ve ölçüm bu formatta kullanılamaz."
                    }
                }
            } catch (error: Exception) {
                statusText = error.message ?: "$selectedName içe aktarılamadı."
                modelSummary = "$selectedName yüklenemedi"
                hasModel = false
                measurementAvailable = false
            } finally {
                isBusy = false
            }
        }
    }

    /** Normal CAD selection: a tap picks the B-rep face and paints its full tessellated face. */
    private fun handleModelSelectionRay(ray: FloatArray) {
        if (!hasModel || !measurementAvailable || ray.size < 7 || nativeHandle == 0L) return
        val origin = doubleArrayOf(ray[0].toDouble(), ray[1].toDouble(), ray[2].toDouble())
        val direction = doubleArrayOf(ray[3].toDouble(), ray[4].toDouble(), ray[5].toDouble())
        val tolerance = ray[6].toDouble()
        lifecycleScope.launch {
            try {
                val picked = withContext(Dispatchers.Default) {
                    NativeCadEngine.nativePickGeometry(nativeHandle, origin, direction, tolerance)
                }
                val faceId = picked.getOrElse(10) { -1.0 }.toInt()
                if (picked.size >= 10 && faceId >= 0) {
                    viewer.highlightEdges()
                    viewer.highlightFaces(intArrayOf(faceId))
                    statusText = "Yüzey seçildi. Seçili yüzey turuncu renkle vurgulandı."
                } else {
                    viewer.highlightFaces(intArrayOf())
                    viewer.highlightEdges()
                    statusText = "Bu noktada seçilebilir yüzey bulunamadı."
                }
            } catch (error: Exception) {
                statusText = error.message ?: "Yüzey seçimi başarısız."
            }
        }
    }

    private fun handleMeasurementRay(ray: FloatArray) {
        if (!measurementMode || !measurementAvailable || ray.size < 7 || nativeHandle == 0L) return

        val origin = doubleArrayOf(ray[0].toDouble(), ray[1].toDouble(), ray[2].toDouble())
        val direction = doubleArrayOf(ray[3].toDouble(), ray[4].toDouble(), ray[5].toDouble())
        val edgeTolerance = ray[6].toDouble()

        lifecycleScope.launch {
            try {
                val picked = withContext(Dispatchers.Default) {
                    NativeCadEngine.nativePickGeometry(nativeHandle, origin, direction, edgeTolerance)
                }
                if (picked.size < 10) {
                    statusText = "Geometri seçilemedi. Kenara veya yüzeyin içine kısa dokunun."
                    return@launch
                }

                val first = firstMeasurePick
                if (first == null) {
                    firstMeasurePick = picked
                    hasFirstMeasurePick = true
                    viewer.setMeasurementDimension(null, null)
                    viewer.highlightFaces(intArrayOf(picked.getOrElse(10) { -1.0 }.toInt()))
                    viewer.highlightEdges(picked)
                    measurementText = describePick(picked) +
                        "\nİkinci kenar/yüzeyi seçin; modeli sürükleyerek döndürebilirsiniz."
                    statusText = "Geometri seçildi. Ölçü otomatik hesaplandı."
                } else {
                    val pointA = first.copyOfRange(1, 4)
                    val pointB = picked.copyOfRange(1, 4)
                    val values = withContext(Dispatchers.Default) {
                        NativeCadEngine.nativeMeasurePoints(nativeHandle, pointA, pointB)
                    }
                    firstMeasurePick = null
                    hasFirstMeasurePick = false
                    viewer.highlightFaces(intArrayOf(first.getOrElse(10) { -1.0 }.toInt(), picked.getOrElse(10) { -1.0 }.toInt()))
                    viewer.highlightEdges(first, picked)
                    viewer.setMeasurementDimension(first.copyOfRange(1, 4), picked.copyOfRange(1, 4))

                    val output = StringBuilder()
                    output.append("1) ").append(describePick(first))
                    output.append("\n2) ").append(describePick(picked))
                    if (values.size >= 4) {
                        output.append("\nNoktalar arası uzaklık: ").append(formatMm(values[0])).append(" mm")
                        output.append("\nΔX: ").append(formatMm(values[1]))
                            .append(" mm   ΔY: ").append(formatMm(values[2]))
                            .append(" mm   ΔZ: ").append(formatMm(values[3])).append(" mm")
                    }

                    val firstKind = first[0].toInt()
                    val secondKind = picked[0].toInt()
                    val comparableDirections =
                        (firstKind == 1 && secondKind == 1) ||
                        (firstKind == 4 && secondKind == 4) ||
                        (firstKind == 5 && secondKind == 5)
                    if (comparableDirections && first[9] > 0.5 && picked[9] > 0.5) {
                        val dot = first[6] * picked[6] + first[7] * picked[7] + first[8] * picked[8]
                        val angle = Math.toDegrees(kotlin.math.acos(kotlin.math.abs(dot).coerceIn(0.0, 1.0)))
                        output.append("\nEn küçük açı: ").append(String.format(Locale.US, "%.3f", angle)).append("°")
                    }

                    measurementText = output.toString()
                    measurementHistory = (listOf(
                        fileName + " • " + output.toString().replace("\n", " | ")
                    ) + measurementHistory).take(20)
                    statusText = "Ölçü tamamlandı. Yeni ölçüm için başka bir kenar/yüzeye dokunun."
                }
            } catch (error: Exception) {
                statusText = error.message ?: "Geometri ölçümü sırasında hata oluştu."
            }
        }
    }

    private fun describePick(pick: DoubleArray): String {
        if (pick.size < 10) return "Geometri ölçümü yok."
        return when (pick[0].toInt()) {
            1 -> "Doğru kenar • Uzunluk: " + formatMm(pick[4]) + " mm"
            2 -> "Dairesel kenar • Çap: " + formatMm(pick[4]) + " mm • Yarıçap: " + formatMm(pick[5]) + " mm"
            3 -> "Eğri kenar • Uzunluk: " + formatMm(pick[4]) + " mm"
            4 -> "Düzlemsel yüzey • Alan: " + formatMm(pick[4]) + " mm²"
            5 -> "Silindirik yüzey • Çap: " + formatMm(pick[4]) + " mm • Derinlik: " + formatMm(pick[5]) + " mm"
            6 -> "Yüzey • Alan: " + formatMm(pick[4]) + " mm²"
            else -> "Geometri seçildi."
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
    private class Loaded(val model: ImportResult?, val dxf: DxfDrawing?, val measurable: Boolean)


    private data class ImportResult(
        val displayName: String,
        val file: File?,
        val faces: Int,
        val triangles: Int,
        val vertices: FloatArray,
        val indices: IntArray,
        val bounds: DoubleArray,
        val triangleFaceIds: IntArray
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
        Button(onClick = onOpen, enabled = !isBusy) {
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
