# AuralDesk

> 中文版 README：[README.zh-CN.md](README.zh-CN.md)

> A streaming-music upsampling player for Hi-Fi setups: QQ Music Hi-Res download & decrypt → HQPlayer upsampling → NAA / USB-exclusive output.

**Status:** early-stage, testing. Expect rough edges.

[Getting Started](#getting-started) · [Features](#features) · [Why not bundle HQPlayer](#why-hqplayer-is-not-bundled-inside-auraldesk) · [Third-party & Licenses](#third-party--licenses)

---

## Introduction

AuralDesk is a **streaming upsampling player** for Windows. It is not a general-purpose Hi-Fi player — it is a streaming front-end **deeply tied to HQPlayer**: sign in to your QQ Music account, browse playlists/albums/artists/collections, download songs (**Hi-Res included**), decrypt them locally into full lossless files, and hand them to HQPlayer for real-time upsampling before delivery to your DAC over NAA or USB-exclusive output.

The whole point of the app is to make "streaming upsampling" smooth and pleasant. Anything outside HQPlayer (like plain system-output listening) is just a convenience for quick auditions.

> If you actually want a USB-exclusive *non-upsampling* path built in, file an issue — I don't need it myself. 😋

**How is this different from "QQ Music UPnP → foobar2000 → HQPlayer"?**

- QQ Music's UPnP path does **not** support Hi-Res (usually capped at 48 kHz). AuralDesk does: Hi-Res sources are downloaded and decrypted locally (verified 96 kHz end-to-end) and handed to HQPlayer unshortened in sample rate and bit depth.
- No foobar2000 in the middle: download → decrypt → cache → upsample in one flow, with full playlists, automatic next/previous caching, and buffer cleanup.

## Why HQPlayer is not bundled inside AuralDesk

A common question — "can you just ship HQPlayer inside the app?" — and unfortunately the honest answer is *no*:

- **HQPlayer is closed-source commercial software** (Signalyst). Its engine cannot be redistributed or embedded without a licensing agreement from the author.
- There is **no Windows-native "Embedded" build**. HQPlayer Embedded only exists as a Linux image / Linux packages, so there is no engine we could wrap in an `.exe` anyway.
- DSP changes (filters/modulators/rates) rebuild the audio pipeline, so they inherently interrupt the current track — this is a HQPlayer engine behavior we can't change from outside.
- What we *can* do is integrate through its public control interfaces, which is exactly what AuralDesk does (remote control over HQPlayer's control API; playback, queueing, status, auto-advance).

So AuralDesk is the *player + streaming front-end*, and HQPlayer remains the *upsampling engine you run separately* — on the same PC or on a dedicated machine. We think of that as a feature: you keep upgrading HQPlayer on your own schedule.

## Features

- **Full QQ Music browsing**: favorites, playlists, albums, artists, search, daily picks, endless radar feed.
- **Three quality tiers**: Hi-Res / Lossless / Normal, classified by the *actual* sample rate of the downloaded file (Hi-Res is mostly 96 kHz; falls back to lossless when no Hi-Res master exists — never to the marketing upsample tiers).
- **Built-in decryption**: QMC files are decrypted offline by our own [qmc-decrypt](https://github.com/HowenXu/qmc-decrypt) (pure Python, zero deps, ships with the app).
- **HQPlayer integration**: PCM/DSD upsampling via NAA or USB-exclusive; can auto-detect and launch HQPlayer.
- **Output**: HQPlayer or system output, switch on the fly.
- **Lyrics & covers**: scrolling synced lyrics with translations; album and artist pages with artwork.
- **Play queue**: full-playlist loading, shuffle, reorder/multi-delete, automatic next/previous caching with configurable buffer size & folder.
- **Android remote**: scan-and-connect over your LAN; control playback/queue/search/favorites, media-session notifications.
- **Remembers everything**: settings, quality, output; optionally resume last song & position.

## Getting Started

> We deliberately don't teach HQPlayer itself. Filter/modulator/NAA configuration is covered by the official HQPlayer documentation.

### 1. Install

1. Download `AuralDesk-Setup-*.exe` and run it. The installer checks for the .NET 8 runtime and points you to Microsoft's download if it is missing.
2. On first launch, if no HQPlayer is detected you'll see a one-time notice that the app is designed to be used with HQPlayer upsampling (system output still works without it).
3. Install `AuralDesk-Android-v*.apk` on your phone (same Wi-Fi as the PC).

### 2. Sign in to QQ Music

Open the app → Stream → Log in, scan the QR with QQ. The login stays on your machine and is not sent anywhere.

### 3. Pick a quality

Settings → Quality: Hi-Res / Lossless / Normal.

- Hi-Res silently falls back to Lossless, then Normal, when a source doesn't have the tier (never the marketing tiers).
- Quality is classified by actual sample rate of the download: 44.1/48 kHz FLAC = Lossless, above 48 kHz (typically 96 kHz) = Hi-Res, MP3 = Normal.

### 4. Pick an output

- **System output**: quick local audition.
- **HQPlayer**: configure upsampling/output (NAA or USB-exclusive) in HQPlayer itself; AuralDesk feeds it the full lossless file for real-time upsampling. An "auto-start HQPlayer" option is available under settings.

### 5. Daily use

- **Play**: single-click a song anywhere (playlist/album/artist/search) — the app queues the current page and plays; a "back to current song" button helps after scrolling.
- **Caching**: the next songs (configurable count) are pre-cached automatically; cached items are flagged in the queue.
- **Lyrics**: full-screen mode with fade in/out; scroll back to earlier lines anytime.

### 6. FAQ

- **Hi-Res download slow?** The app bypasses the system proxy for QQ CDN. If you run a proxy client, make sure it isn't force-routing the CDN.
- **Can't reach HQPlayer?** Make sure it is running and listening; select "HQPlayer" as output; configure your NAA device under HQPlayer's Outputs.
- **Phone can't find the PC?** Same Wi-Fi, and enable "LAN remote" in the PC settings.

## Repository layout

```
AuralDesk/
├── *.cs / *.xaml           Windows desktop app (WPF / .NET 8)
├── qqapi/app/              QQ Music API backend (Python; decryption & sidecar)
│   ├── qqmusic_api/        Upstream QQMusicApi library (GPL-3.0)
│   ├── web/                Local sidecar (127.0.0.1:8123)
│   ├── qmc_decrypt.py      Offline decryptor (same source as the qmc-decrypt repo)
│   └── ogg2flac.py         OGG→FLAC helper
├── android/                Android remote app (Kotlin)
└── installer.iss           Inno Setup script
```

## Third-party & Licenses

- [QQMusicApi](https://github.com/L-1124/QQMusicApi) (GPL-3.0) — QQ Music API wrapper, in `qqapi/app/qqmusic_api/`.
- [unlock-music](https://github.com/rong6/unlock-music) — reference for the QMC decryption algorithms (`QmcDecoder.cs` and `qmc_decrypt.py`).
- [NAudio](https://github.com/naudio/NAudio) (MIT) — audio output.
- [Microsoft.Web.WebView2](https://www.nuget.org/packages/Microsoft.Web.WebView2) — embedded web & login UI.
- [qmc-decrypt](https://github.com/HowenXu/qmc-decrypt) (AGPL-3.0) — our standalone QQ Music QMC decryptor.

This project is [AGPL-3.0](LICENSE); the `qqapi/app/qqmusic_api/` portion keeps upstream [QQMusicApi](https://github.com/L-1124/QQMusicApi)'s GPL-3.0 terms.

Copyright © 2026 [Howen_Xu](https://github.com/HowenXu). All rights reserved.

For learning purposes only — delete downloaded content within 24 hours.

## Roadmap

- [x] QQ Music: download / decrypt / playback / lyrics / favorites / playlists / albums / artists / search
- [x] HQPlayer upsampling (PCM / DSD, NAA / USB-exclusive)
- [x] Android phone remote
- [ ] Apple Music: just a new folder. I don't use it; if you want it, open an issue and I might get to it.
- [ ] NetEase Cloud Music: foobar2000 UPnP already covers this use case, I don't use it, and the quality isn't better than QQ Music. Not doing it. 😋
