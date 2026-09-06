plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}

import java.util.Properties

android {
    namespace = "com.auraldesk.remote"
    compileSdk = 36

    defaultConfig {
        applicationId = "com.auraldesk.remote"
        minSdk = 26
        targetSdk = 36
        versionCode = 23
        versionName = "0.1.37"
    }

    signingConfigs {
        create("release") {
            val props = Properties()
            val propsFile = rootProject.file("keystore.properties")
            if (propsFile.exists()) {
                propsFile.inputStream().use { props.load(it) }
            }
            val storePath = props.getProperty("storeFile")
            if (storePath != null) {
                storeFile = rootProject.file(storePath)
                storePassword = props.getProperty("storePassword")
                keyAlias = props.getProperty("keyAlias")
                keyPassword = props.getProperty("keyPassword")
            }
        }
    }
    buildTypes {
        release {
            isMinifyEnabled = false
            // 本地有 keystore.properties 时用正式签名；否则回退 debug key，干净检出仍可构建
            signingConfig = if (signingConfigs.getByName("release").storeFile != null)
                signingConfigs.getByName("release") else signingConfigs.getByName("debug")
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    kotlinOptions {
        jvmTarget = "17"
    }
}

dependencies {
    implementation("androidx.core:core-ktx:1.13.1")
    implementation("androidx.appcompat:appcompat:1.7.0")
    implementation("androidx.media:media:1.7.0")
}
