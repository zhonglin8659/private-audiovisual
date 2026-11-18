# MiYing.Android

密影 Android 播放器源码。

- 解密协议：MSV v1（AES-256-GCM + PBKDF2-HMAC-SHA256）
- 播放内核：AndroidX Media3 / ExoPlayer
- 最低系统：Android 8.0（API 26）

构建：

```powershell
.\gradlew.bat assembleDebug
```

APK 输出：`app\build\outputs\apk\debug\app-debug.apk`