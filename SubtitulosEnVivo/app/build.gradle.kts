plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.kotlin.android)
}

android {
    namespace = "com.gabriel.subtitulosenvivo"
    compileSdk = 35
    // NDK por defecto de AGP 8.13 (instalado con sdkmanager "ndk;27.0.12077973" "cmake;3.22.1").
    ndkVersion = "27.0.12077973"

    defaultConfig {
        applicationId = "com.gabriel.subtitulosenvivo"
        // La captura del audio de otras apps (AudioPlaybackCapture) existe desde Android 10.
        minSdk = 29
        targetSdk = 34
        versionCode = 1
        versionName = "0.1"
        ndk { abiFilters += listOf("arm64-v8a") }
        externalNativeBuild {
            cmake {
                // whisper.cpp sin optimizar es ~10 veces más lento: se compila optimizado también en debug.
                arguments += listOf("-DCMAKE_BUILD_TYPE=Release")
            }
        }
    }

    externalNativeBuild {
        cmake {
            path = file("src/main/cpp/CMakeLists.txt")
            version = "3.22.1"
        }
    }

    buildTypes {
        release {
            isMinifyEnabled = true
            isShrinkResources = true
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"), "proguard-rules.pro")
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    androidResources {
        localeFilters += listOf("es")
    }

    buildFeatures {
        viewBinding = true
    }
}

kotlin {
    compilerOptions {
        jvmTarget.set(org.jetbrains.kotlin.gradle.dsl.JvmTarget.JVM_17)
    }
}

dependencies {
    implementation(libs.androidx.core.ktx)
    implementation(libs.androidx.appcompat)
    implementation(libs.material)
    implementation(libs.androidx.activity)
    implementation(libs.androidx.lifecycle.service)
    implementation(libs.kotlinx.coroutines.android)
    implementation(libs.mlkit.translate)

    testImplementation(libs.junit)
}
