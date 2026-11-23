import java.awt.RenderingHints
import java.awt.image.BufferedImage
import javax.imageio.ImageIO

plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
    id("org.jetbrains.kotlin.plugin.compose")
}

val generatedSrcCadResources = layout.buildDirectory.dir("generated/srccad-branding/res")
val generateSrcCadBranding by tasks.registering {
    val sourceImage = rootProject.file("branding/SrcCadWorks.jpg")
    inputs.file(sourceImage)
    outputs.dir(generatedSrcCadResources)

    doLast {
        if (!sourceImage.exists()) {
            throw GradleException("SrcCad brand image is missing: ${sourceImage.absolutePath}")
        }
        val original = ImageIO.read(sourceImage)
            ?: throw GradleException("SrcCad brand image could not be decoded.")
        val outputRoot = generatedSrcCadResources.get().asFile

        fun renderImage(image: BufferedImage, directory: String, name: String, width: Int, height: Int) {
            val outputDirectory = File(outputRoot, directory)
            outputDirectory.mkdirs()
            val resized = BufferedImage(width, height, BufferedImage.TYPE_INT_RGB)
            val graphics = resized.createGraphics()
            graphics.setRenderingHint(RenderingHints.KEY_INTERPOLATION, RenderingHints.VALUE_INTERPOLATION_BICUBIC)
            graphics.setRenderingHint(RenderingHints.KEY_RENDERING, RenderingHints.VALUE_RENDER_QUALITY)
            graphics.drawImage(image, 0, 0, width, height, null)
            graphics.dispose()
            ImageIO.write(resized, "png", File(outputDirectory, name))
        }

        // Crop the white padding for a legible horizontal brand mark inside the app.
        var minX = original.width
        var minY = original.height
        var maxX = -1
        var maxY = -1
        for (y in 0 until original.height) {
            for (x in 0 until original.width) {
                val rgb = original.getRGB(x, y)
                val red = (rgb shr 16) and 0xFF
                val green = (rgb shr 8) and 0xFF
                val blue = rgb and 0xFF
                if (red < 247 || green < 247 || blue < 247) {
                    minX = minOf(minX, x)
                    minY = minOf(minY, y)
                    maxX = maxOf(maxX, x)
                    maxY = maxOf(maxY, y)
                }
            }
        }
        if (maxX < minX || maxY < minY) {
            throw GradleException("SrcCad logo artwork was not found in the source image.")
        }
        val croppedLogo = original.getSubimage(minX, minY, maxX - minX + 1, maxY - minY + 1)
        renderImage(croppedLogo, "drawable-nodpi", "srccad_brand.png", 480, 148)

        // Keep the square master image for launcher icons.
        val densities = listOf(
            "mipmap-mdpi" to 48,
            "mipmap-hdpi" to 72,
            "mipmap-xhdpi" to 96,
            "mipmap-xxhdpi" to 144,
            "mipmap-xxxhdpi" to 192
        )
        densities.forEach { (directory, size) ->
            renderImage(original, directory, "ic_launcher.png", size, size)
            renderImage(original, directory, "ic_launcher_round.png", size, size)
        }
    }
}

val defaultOcctRoot = rootProject.file("third_party/occt-android/arm64-v8a")
val occtRoot = providers.gradleProperty("srccadOcctRoot").orNull
    ?: System.getenv("SRCCAD_OCCT_ROOT")
    ?: if (defaultOcctRoot.exists()) defaultOcctRoot.absolutePath else ""

android {
    namespace = "com.srccad.mobile"
    compileSdk = 36
    ndkVersion = "28.2.13676358"
    sourceSets.getByName("main").res.srcDir(generatedSrcCadResources)

    // OCCT shared libraries are imported and linked by CMake below.
    // Android Gradle Plugin packages CMake-linked imported shared libraries automatically.

    defaultConfig {
        applicationId = "com.srccad.mobile"
        minSdk = 26
        targetSdk = 36
        versionCode = 4
        versionName = "0.4.0"
        ndk { abiFilters += listOf("arm64-v8a") }
        externalNativeBuild {
            cmake {
                cppFlags += listOf("-std=c++20", "-fexceptions", "-frtti")
                arguments += listOf(
                    "-DANDROID_STL=c++_shared",
                    "-DSRCCAD_ENABLE_OCCT=" + if (occtRoot.isBlank()) "OFF" else "ON",
                    "-DSRCCAD_ENABLE_OPENSWX=ON",
                    "-DSRCCAD_OCCT_ROOT=" + occtRoot
                )
            }
        }
    }

    buildFeatures { compose = true }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    externalNativeBuild {
        cmake { path = file("src/main/cpp/CMakeLists.txt") }
    }
}

kotlin { jvmToolchain(17) }

dependencies {
    val composeBom = platform("androidx.compose:compose-bom:2025.10.00")
    implementation(composeBom)
    androidTestImplementation(composeBom)
    implementation("androidx.activity:activity-compose:1.11.0")
    implementation("androidx.compose.ui:ui")
    implementation("androidx.compose.ui:ui-tooling-preview")
    implementation("androidx.compose.material3:material3")
    implementation("androidx.lifecycle:lifecycle-runtime-ktx:2.9.4")
    debugImplementation("androidx.compose.ui:ui-tooling")
}

tasks.named("preBuild").configure {
    dependsOn(generateSrcCadBranding)
}
