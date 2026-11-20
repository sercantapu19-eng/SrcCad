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
