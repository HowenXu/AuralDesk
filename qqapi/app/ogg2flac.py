"""AuralDesk：把 OGG/Vorbis 转成 FLAC（libsndfile，支持多声道）。"""
import sys

import soundfile as sf

try:
    data, sr = sf.read(sys.argv[1])
    sf.write(sys.argv[2], data, sr, format="FLAC")
except Exception as e:  # noqa: BLE001
    print("ogg2flac error:", e)
    sys.exit(1)
