plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.kotlin.compose)
}

android {
    namespace = "app.contextall"
    compileSdk = 36

    defaultConfig {
        applicationId = "app.contextall"
        minSdk = 26
        targetSdk = 36
        versionCode = 2
        versionName = "0.1.0"
    }

    val localDebugKeystore = file("${rootDir}/debug.keystore")
    if (localDebugKeystore.exists()) {
        signingConfigs {
            create("debugConfig") {
                storeFile = localDebugKeystore
                storePassword = "android"
                keyAlias = "androiddebugkey"
                keyPassword = "android"
            }
        }
    }

    buildTypes {
        debug {
            versionNameSuffix = "-debug"
            signingConfigs.findByName("debugConfig")?.let { signingConfig = it }
        }
        release {
            isMinifyEnabled = false
            signingConfigs.findByName("debugConfig")?.let { signingConfig = it }
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_21
        targetCompatibility = JavaVersion.VERSION_21
    }

    buildFeatures {
        compose = true
        buildConfig = true
    }

    sourceSets {
        getByName("main") {
            jniLibs.directories.add(layout.buildDirectory.dir("nativeJniLibs").get().asFile.absolutePath)
        }
    }
}

val skipNativeAot = providers.gradleProperty("skipNativeAot").map { it.toBoolean() }.getOrElse(false)
val nativeAbis = providers.gradleProperty("nativeAbis").getOrElse("x86_64,arm64-v8a")

if (!skipNativeAot) {
    val buildNativeAndroidLibs = tasks.register<Exec>("buildNativeAndroidLibs") {
        group = "build"
        description = "Builds NativeAOT libcontextall_native.so and extracts libmsquic.so for Android ABIs"
        workingDir = rootDir
        commandLine("bash", "${rootDir}/scripts/build-android-native.sh", nativeAbis)
        inputs.file(rootProject.file("scripts/build-android-native.sh"))
        inputs.file(rootProject.file("src/ContextAll.Native/ContextAll.Native.csproj"))
        inputs.dir(rootProject.file("src/ContextAll.Native"))
        inputs.dir(rootProject.file("src/ContextAll.Session"))
        inputs.dir(rootProject.file("src/ContextAll.Core"))
        inputs.dir(rootProject.file("src/ContextAll.Effects"))
        inputs.dir(rootProject.file("src/ContextAll.Protocol"))
        outputs.dir(layout.buildDirectory.dir("nativeJniLibs"))
    }

    tasks.configureEach {
        if (name == "mergeDebugJniLibFolders" || name == "mergeReleaseJniLibFolders") {
            dependsOn(buildNativeAndroidLibs)
        }
    }
}

dependencies {
    implementation(libs.androidx.core.ktx)
    implementation(libs.androidx.lifecycle.runtime.ktx)
    implementation(libs.androidx.lifecycle.runtime.compose)
    implementation(libs.androidx.lifecycle.viewmodel.compose)
    implementation(libs.androidx.activity.compose)
    implementation(platform(libs.androidx.compose.bom))
    implementation(libs.androidx.ui)
    implementation(libs.androidx.ui.graphics)
    implementation(libs.androidx.ui.tooling.preview)
    implementation(libs.androidx.material3)
    implementation(libs.androidx.material.icons.extended)
    testImplementation(libs.junit)
}
