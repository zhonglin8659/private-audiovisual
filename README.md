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
cd D:\PrivateAudiovisual\MiYing.Android
.\gradlew.bat assembleDebug
```

生成 APK：`app\build\outputs\apk\debug\app-debug.apk`

### Windows

```powershell
cd D:\PrivateAudiovisual\MiYing.Desktop
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

建议将本目录作为单仓库，或分别把 `MiYing.Android` 和 `MiYing.Desktop` 建为两个独立仓库。
当前尚未初始化 Git。`MiYing.Android` 与 `MiYing.Desktop` 均已验证可独立构建。