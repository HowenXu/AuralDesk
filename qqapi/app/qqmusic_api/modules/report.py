"""听歌记录上报模块（对齐 QQ 音乐客户端的三段上报里的前两段）。

- ListeningMusicReport：影响推荐算法的听歌记录（musics.fcg，签名）
- ReportPlayRecentlyInfo：同步到 QQ 音乐的「最近播放」（musics.fcg，签名）

stat6 的 imusic_tj 播放流水需要完整设备指纹，暂未实现。
"""

from __future__ import annotations

import time
from typing import Any

from ..models.request import Credential
from ._base import ApiModule


class ReportApi(ApiModule):
    """听歌上报接口."""

    # 客户端「听歌上报」族的固定 comm；ct/cv 不对接口会只回空数据而不报错
    _REPORT_COMM: dict[str, Any] = {
        "tyt_exp_env": "0",
        "ct": "11",
        "cv": "20040008",
        "v": "20040008",
        "chid": "2005000982",
        "tmeAppID": "qqmusic",
    }

    def report_listening(
        self,
        songmid: str,
        play_ms: int,
        play_list: list[int] | None = None,
        credential: Credential | None = None,
    ):
        """上报听歌记录；play_ms 是已经播放的毫秒数."""
        return self._build_cgi(
            module="music.richFlag.listening",
            method="ListeningMusicReport",
            param={
                "pauseFlag": False,
                "playList": play_list or [],
                "remainingTime": int(play_ms),
                "songPlayTime": int(play_ms),
                "songid": 0,
                "songmid": songmid,
                "songtype": 1,
                "speed": 1,
            },
            sign=True,
            comm=self._REPORT_COMM,
            credential=credential,
        )

    def report_recently(
        self,
        song_id: int,
        album_id: str = "0",
        credential: Credential | None = None,
    ):
        """把歌曲写入账号的「最近播放」."""
        return self._build_cgi(
            module="music.musicasset.PlayRecentlyWrite",
            method="ReportPlayRecentlyInfo",
            param={
                "data": [
                    {
                        "id": str(song_id),
                        "type": 2,
                        "lastTime": int(time.time()),
                        "listenCnt": 1,
                        "auxillaryID": album_id or "0",
                        "auxillaryDict": {"vip": "1"},
                    }
                ]
            },
            sign=True,
            comm=self._REPORT_COMM,
            credential=credential,
        )
