# AuralDesk

> 给 Hi-Fi 场景而生的流媒体播放器：QQ 音乐 Hi-Res 下载解密 → HQPlayer 升频 → NAA / USB 独占输出。

[使用教程](#使用教程) · [特性](#特性) · [引用的开源库](#引用的开源库) · [开源协议](#开源协议)

---

## 简介

AuralDesk 是一台跑在 Windows 上的「流媒体转盘」：登录 QQ 音乐账号，浏览歌单、专辑、歌手与收藏，把歌曲（**含 Hi-Res**）下载并解密成完整无损文件，交给 HQPlayer 实时升频，再通过 NAA 投送到解码器，或直接走 USB 独占输出；不想开 HQPlayer 时也可以切回系统输出随便听。

**和「QQ 音乐直接 UPnP 投送 foobar2000 → HQPlayer 升频」有什么区别？**

核心区别是 Hi-Res 的支持：

- QQ 音乐的 UPnP 投送**不支持 Hi-Res**，投送链路通常只到 48kHz；AuralDesk 支持 Hi-Res 音源（实测 96kHz 全链路可用），下载后本地解密成完整无损文件，再交给 HQPlayer 升频，采样率、码率都不打折。
- 不依赖 foobar2000 中转，下载 → 解密 → 缓存 → 升频一体完成，播放队列按歌单完整加载，自动缓存下一首/上一首，听完即清，省心省空间。

---

## 特性

- **QQ 音乐全功能浏览**：我的收藏、歌单、专辑、歌手、搜索、每日推荐、沉浸刷歌（无限推荐）。
- **音质三档**：Hi-Res / 无损 / 普通，按音源实际采样率自动归类（Hi-Res 以 96kHz 为主；若无 Hi-Res 档自动降无损，绝不选臻品母带这类升频营销档）。
- **内置解密**：QMC 文件离线解密（独立库见 [qmc-decrypt](https://github.com/HowenXu/qmc-decrypt)），纯 Python 零依赖，随包分发。
- **HQPlayer 集成**：支持 PCM / DSD 升频，经 NAA 投送或 USB 独占到解码器；可自动检测并拉起 HQPlayer。
- **输出方式**：HQPlayer / 系统输出，切换即用。
- **歌词与封面**：滚动歌词、逐行高亮、翻译；专辑与歌手页带封面。
- **播放队列**：歌单完整加载、随机播放、上下移/多选删除、缓存上一首/下一首、缓冲空间与路径可配。
- **手机遥控**：Android 端在同一 WiFi 下自动扫描并连接，可控制播放、队列、搜索、收藏，通知栏显示播放状态。
- **记忆与恢复**：设置、音质、输出方式全部记住；可选恢复上次歌曲与进度。

---

## 使用教程

### 一、安装

1. 下载 `AuralDesk-Setup-*.exe`，双击安装。安装器会检测 .NET 8 运行时，缺失时提示前往微软官方下载。
2. 安装完成后首次启动，若检测不到 HQPlayer 会弹窗提示：本软件建议搭配 HQPlayer 升频使用（仅提示一次，没有 HQPlayer 也能用系统输出直听）。
3. Android 手机端安装 `AuralDesk-Android-v*.apk`（与电脑同一 WiFi 即可）。

### 二、登录 QQ 音乐

打开软件 → 流媒体 → 登录，用 QQ 扫码登录。登录态保存在本机（仅本机可用，不向任何第三方发送）。

### 三、选择音质

设置 → 音质：Hi-Res / 无损 / 普通。

- 选 Hi-Res 时，若歌曲没有 Hi-Res 档会自动降无损，再降普通，绝不会选臻品母带。
- 音质按下载文件的**实际采样率**归类：44.1/48kHz FLAC = 无损，高于 48kHz（通常 96kHz）= Hi-Res，MP3 = 普通。

### 四、选择输出

- **系统输出**：电脑直接出声，方便快速试听。
- **HQPlayer**：在 HQPlayer 里配置好升频与输出（NAA 或 USB 独占），软件把完整无损文件交给 HQPlayer 实时升频。升频设置、NAA 地址等请到 HQPlayer 界面调整，软件内另有「自动启动 HQPlayer」选项（设置里可指定 HQPlayer 路径）。

### 五、使用

- **播放**：单击歌单/专辑/歌手/搜索页里的歌曲即播放，并自动把当前页面全部歌曲加入播放队列；播放中可返回当前播放歌曲。
- **缓存**：播放时自动缓存下一首（以及上一首），已缓存歌曲在队列里标注；缓冲空间与目录可在设置里调整。
- **歌词**：歌词页支持点击进度条跳转；全屏模式下歌词淡入淡出，已播放的歌词可上滑回看。
- **手机遥控**：手机端打开后自动扫描电脑，或手动输入电脑地址；可浏览流媒体、控制播放、管理队列、搜索歌曲。

### 六、常见问题

- **Hi-Res 下载很慢？** 软件已禁用系统代理直连 QQ CDN；若你开了代理软件，请确认没有强行走代理。
- **HQPlayer 连不上？** 确认 HQPlayer 已启动并在监听，软件里输出选「HQPlayer」，NAA 设备在 HQPlayer 的 Outputs 里配置。
- **手机端扫不到电脑？** 确认在同一 WiFi，电脑端设置里开启了「局域网遥控」。

---

## 目录结构

```
AuralDesk/
├── *.cs / *.xaml           桌面端源码（WPF / .NET 8）
├── qqapi/app/              QQ 音乐 API 后端（Python，含解锁与 sidecar）
│   ├── qqmusic_api/        上游 QQMusicApi 库（GPL-3.0）
│   ├── web/                本地 sidecar（127.0.0.1:8123）
│   ├── qmc_decrypt.py      离线解密（与独立库 qmc-decrypt 同源）
│   └── ogg2flac.py         OGG 转 FLAC 辅助
├── android/                Android 遥控端源码（Kotlin）
└── installer.iss           Inno Setup 安装脚本
```

## 引用的开源库

- [QQMusicApi](https://github.com/L-1124/QQMusicApi)（GPL-3.0）—— QQ 音乐接口封装，位于 `qqapi/app/qqmusic_api/`。
- [unlock-music](https://github.com/rong6/unlock-music) —— QMC 解密算法参考（`QmcDecoder.cs` 与 `qmc_decrypt.py` 均以其 Rust 实现为参照逐一对拍）。
- [NAudio](https://github.com/naudio/NAudio)（MIT）—— 音频输出。
- [Microsoft.Web.WebView2](https://www.nuget.org/packages/Microsoft.Web.WebView2) —— 内嵌网页与登录。
- [qmc-decrypt](https://github.com/HowenXu/qmc-decrypt)（AGPL-3.0）—— 本项目维护的 QQ 音乐 QMC 离线解密工具。

## 开源协议

本项目采用 [AGPL-3.0](LICENSE)。其中 `qqapi/app/qqmusic_api/` 部分沿用上游 [QQMusicApi](https://github.com/L-1124/QQMusicApi) 的 GPL-3.0 许可。

仅用于解密你自己合法下载、有权使用的音频文件。音乐平台不易，请尊重版权，支持正版。

---

## 路线图

- [x] QQ 音乐：下载 / 解密 / 播放 / 歌词 / 收藏 / 歌单 / 专辑 / 歌手 / 搜索
- [x] HQPlayer 升频（PCM / DSD，NAA / USB 独占）
- [x] Android 手机遥控
- [ ] 苹果音乐：新建文件夹（是的，就是新建文件夹，还没开始写）
- [ ] 网易云音乐：规划中
