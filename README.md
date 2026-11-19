# PrivateAudiovisual

密影（MiYing）三条产品线的源码副本。

## 目录

| 目录 | 对应程序 | 说明 |
|---|---|---|
| `MiYing.Android` | `app-debug.apk` | Android 加密视频/照片播放器，Media3 + MSV/MSP 解密 |
| `MiYing.Desktop/src/MiYing.Encryptor` | `MiYing.Encryptor.exe` | Windows 加密工具 |
| `MiYing.Desktop/src/MiYing.Player` | `MiYing.Player.exe` | Windows 加密媒体播放器 |

> 桌面端两个程序共用 `MiYing.Desktop/src/MiYing.Core` 和 `MiYing.Desktop/src/MiYing.UI`，因此放在同一个桌面解决方案中维护，避免共享加解密代码重复。

## 构建

### Android

```powershell
cd D:\MyProject\private-audiovisual\MiYing.Android
.\gradlew.bat assembleDebug
```

生成 APK：`app\build\outputs\apk\debug\app-debug.apk`

### Windows

```powershell
cd D:\MyProject\private-audiovisual\MiYing.Desktop
dotnet build .\MiYingVideo.sln -c Debug -m:1 -nr:false
```

发布：

```powershell
.\publish.ps1
```

## 已排除的生成物

复制时已排除：

- Android：`.gradle`、`.kotlin`、`app/build`
- Windows：`bin`、`obj`、`dist`、`.vs`
- 本地依赖缓存：`.nuget-local` 只保留占位说明，不复制大体积 nupkg；`.dotnet-home` 不复制
- 临时测试素材：`test_assets`

本仓库托管在 GitHub（https://github.com/zhonglin8659/private-audiovisual），统一维护三条产品线的源码。
已验证：`MiYing.Android` 可通过 `gradlew.bat assembleDebug` 构建；`MiYing.Desktop` 解决方案构建成功，并通过 75 个单元测试。