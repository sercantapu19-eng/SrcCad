import java.awt.RenderingHints
import java.awt.image.BufferedImage
import javax.imageio.ImageIO

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
        fun writeIcon(directory: String, name: String, size: Int) {
            val outputDirectory = File(outputRoot, directory)
            outputDirectory.mkdirs()
            val output = File(outputDirectory, name)
            val resized = BufferedImage(size, size, BufferedImage.TYPE_INT_RGB)
            val graphics = resized.createGraphics()
            graphics.setRenderingHint(RenderingHints.KEY_INTERPOLATION, RenderingHints.VALUE_INTERPOLATION_BICUBIC)
            graphics.setRenderingHint(RenderingHints.KEY_RENDERING, RenderingHints.VALUE_RENDER_QUALITY)
            graphics.drawImage(original, 0, 0, size, size, null)
            graphics.dispose()
            ImageIO.write(resized, "png", output)
        }

        // In-app brand mark.
        writeIcon("drawable-nodpi", "srccad_brand.png", 256)

        // Standard and round launcher icons for all Android density buckets.
        val densities = listOf(
            "mipmap-mdpi" to 48,
            "mipmap-hdpi" to 72,
            "mipmap-xhdpi" to 96,
            "mipmap-xxhdpi" to 144,
            "mipmap-xxxhdpi" to 192
        )
        densities.forEach { (directory, size) ->
            writeIcon(directory, "ic_launcher.png", size)
            writeIcon(directory, "ic_launcher_round.png", size)
        }
    }
}

plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
    id("org.jetbrains.kotlin.plugin.compose")
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
        versionCode = 3
        versionName = "0.3.0"
        ndk { abiFilters += listOf("arm64-v8a") }
        externalNativeBuild {
            cmake {
                cppFlags += listOf("-std=c++17", "-fexceptions", "-frtti")
                arguments += listOf(
                    "-DANDROID_STL=c++_shared",
                    "-DSRCCAD_ENABLE_OCCT=" + if (occtRoot.isBlank()) "OFF" else "ON",
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
