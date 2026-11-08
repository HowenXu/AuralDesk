"""QQ 音乐听歌上报，对齐 Android 客户端的三段行为。

- ``ListeningMusicReport``：听歌记录，影响推荐算法
- ``ReportPlayRecentlyInfo``：写入「最近播放」
- ``stat6 / imusic_tj``：播放流水，记录本段真实播放秒数

三段都只在 QQ 曲目上触发，未登录或失败时静默返回，不影响本地播放。
"""

from __future__ import annotations

import gzip
import hashlib
import random
import time
from datetime import datetime
from typing import Any
from xml.sax.saxutils import escape, quoteattr

from ..core.versioning import Platform
from ..models.request import Credential
from ._base import ApiModule


class ReportApi(ApiModule):
    """听歌上报接口."""

    _REPORT_COMM: dict[str, Any] = {
        "tyt_exp_env": "0",
        "ct": "11",
        "cv": "20040008",
        "v": "20040008",
        "chid": "2005000982",
        "tmeAppID": "qqmusic",
    }
    _RECENTLY_URL = "https://u6.y.qq.com/cgi-bin/musicu.fcg?cgiKey=ReportPlayRecentlyInfo"
    _STAT_URL = "https://stat6.y.qq.com/android/fcgi-bin/imusic_tj"
    _TIMEKEY_SALT = "gk2$Lh-&l4#!4iow"

    async def _device_context(
        self,
        credential: Credential | None,
    ) -> tuple[Any, dict[str, str], Credential, dict[str, Any]]:
        """取当前设备、QIMEI、登录态，并构造上报 comm."""
        context = self._client._context
        target_credential = credential or context.credential
        device = await context._device_store.get_device()  # noqa: SLF001
        qimei = (
            await context._qimei_manager.get_cached()  # noqa: SLF001
            if context.platform == Platform.ANDROID
            else {"q16": "", "q36": ""}
        )
        comm = context.version_policy.build_comm(
            platform=context.platform,
            credential=target_credential,
            device=device,
            qimei=qimei if context.platform == Platform.ANDROID else None,
            guid=device.open_udid,
        )
        comm.update(self._REPORT_COMM)
        return device, qimei, target_credential, comm

    def report_listening(
        self,
        songmid: str,
        play_ms: int,
        play_list: list[int] | None = None,
        credential: Credential | None = None,
        *,
        pause_flag: bool = False,
    ):
        """上报听歌记录；play_ms 是当前播放位置（毫秒）."""
        return self._build_cgi(
            module="music.richFlag.listening",
            method="ListeningMusicReport",
            param={
                "pauseFlag": pause_flag,
                "playList": play_list or [],
                "remainingTime": int(play_ms),
                "songPlayTime": int(play_ms),
                "songid": 0,
                "songmid": songmid,
                "songtype": 1,
                "speed": 1,
            },
            sign=True,
            preserve_bool=True,
            comm=self._REPORT_COMM,
            credential=credential,
        )

    async def report_recently(
        self,
        song_id: int,
        album_id: str = "0",
        credential: Credential | None = None,
    ) -> bool:
        """写入「最近播放」；先走客户端无签名通道，失败再走签名通道."""
        if song_id <= 0:
            return False
        _, _, target_credential, comm = await self._device_context(credential)
        body = {
            "comm": comm,
            "music.musicasset.PlayRecentlyWrite.ReportPlayRecentlyInfo": {
                "module": "music.musicasset.PlayRecentlyWrite",
                "method": "ReportPlayRecentlyInfo",
                "param": {
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
            },
        }
        try:
            resp = await self._session.post(
                self._RECENTLY_URL,
                json=body,
                headers={
                    "User-Agent": "QQMusic/2104583050",
                    "Referer": "https://y.qq.com/",
                },
                proxies=self._client.proxies,
                cert=self._client.cert,
                verify=self._client.verify,
            )
            await self._session.gather(resp)
            if resp.status_code == 200 and int(resp.json().get("code", -1)) == 0:
                return True
        except Exception:
            pass

        try:
            resp = await self._build_cgi(
                module="music.musicasset.PlayRecentlyWrite",
                method="ReportPlayRecentlyInfo",
                param=body[
                    "music.musicasset.PlayRecentlyWrite.ReportPlayRecentlyInfo"
                ]["param"],
                sign=True,
                comm=self._REPORT_COMM,
                credential=target_credential,
            )
            return isinstance(resp, dict)
        except Exception:
            return False

    async def report_stream(
        self,
        song_id: int,
        duration_sec: int,
        play_seconds: int,
        credential: Credential | None = None,
    ) -> bool:
        """上报 stat6 播放流水；play_seconds 是本段真实播放墙钟秒数."""
        seconds = int(play_seconds)
        if song_id <= 0 or seconds <= 0:
            return False
        device, qimei, target_credential, _ = await self._device_context(credential)
        timestamp = int(time.time())
        time_text = str(seconds)
        uin = str(target_credential.musicid)
        timekey = hashlib.md5(
            f"{timestamp}{time_text}{uin}{self._TIMEKEY_SALT}".encode(),
        ).hexdigest().upper()
        uid = self._random_digits(10)
        xml = self._build_stat_xml(
            device=device,
            qimei=qimei,
            credential=target_credential,
            song_id=song_id,
            duration_sec=max(0, int(duration_sec)),
            seconds=seconds,
            timestamp=timestamp,
            timekey=timekey,
            uid=uid,
        )
        try:
            resp = await self._session.post(
                self._STAT_URL,
                data=gzip.compress(xml.encode("utf-8")),
                headers={
                    "User-Agent": "QQMusic 12030508(android 12)",
                    "Content-Type": "application/x-www-form-urlencoded",
                    "Content-Encoding": "gzip",
                },
                proxies=self._client.proxies,
                cert=self._client.cert,
                verify=self._client.verify,
            )
            await self._session.gather(resp)
            return 200 <= resp.status_code < 300
        except Exception:
            return False

    def _build_stat_xml(
        self,
        *,
        device: Any,
        qimei: dict[str, str],
        credential: Credential,
        song_id: int,
        duration_sec: int,
        seconds: int,
        timestamp: int,
        timekey: str,
        uid: str,
    ) -> str:
        now = datetime.now()
        sid = now.strftime("%Y%m%d%H%M%S") + uid
        taid = self._random_token(88)
        tid = self._random_digits(19)
        traceid = f"11_{self._random_digits(11)}_{timestamp}"
        vkey = self._random_hex(32)
        v4ip = ".".join(
            [
                str(random.randint(1, 223)),
                str(random.randint(0, 255)),
                str(random.randint(0, 255)),
                str(random.randint(1, 254)),
            ]
        )
        login_type = credential.login_type
        if not login_type:
            login_type = 1 if credential.musickey.startswith("W_X_") else 2
        open_udid = getattr(device, "open_udid", "") or ""
        os_ver = device.version.release
        phone_type = device.model
        device_level = device.version.sdk
        q36 = qimei.get("q36", "")
        play_duration_mi = seconds * 1000

        return (
            '<?xml version="1.0" encoding="UTF-8"?>'
            "<root>"
            f"<OpenUDID>{escape(open_udid)}</OpenUDID>"
            f"<udid>{escape(open_udid)}</udid>"
            "<ct>11</ct><cv>12030508</cv><v>12030508</v>"
            "<chid>74648</chid>"
            f"<os_ver>{escape(os_ver)}</os_ver>"
            f"<aid>{escape(device.android_id)}</aid>"
            f"<phonetype>{escape(phone_type)}</phonetype>"
            f"<devicelevel>{device_level}</devicelevel>"
            f"<newdevicelevel>{device_level}</newdevicelevel>"
            "<deviceScore>800.0</deviceScore>"
            f"<QIMEI36>{escape(q36)}</QIMEI36>"
            f"<taid>{taid}</taid>"
            "<tmeAppID>qqmusic</tmeAppID>"
            f"<tid>{tid}</tid>"
            "<modeSwitch>6</modeSwitch><teenMode>0</teenMode>"
            f"<uid>{uid}</uid><sid>{sid}</sid>"
            f"<OpenUDID2>{escape(open_udid)}</OpenUDID2>"
            "<ui_mode>1</ui_mode><nettype>1030</nettype>"
            f"<tmeLoginType>{login_type}</tmeLoginType>"
            "<tmeLoginMethod>2</tmeLoginMethod>"
            "<fPersonality>0</fPersonality>"
            f"<wid>{uid}</wid>"
            f"<v4ip>{v4ip}</v4ip>"
            f"<qq>{escape(str(credential.musicid))}</qq>"
            f"<authst>{escape(credential.musickey)}</authst>"
            f"<psrf_qqopenid>{escape(credential.openid)}</psrf_qqopenid>"
            f"<psrf_access_token_expiresAt>{credential.expired_at or 0}</psrf_access_token_expiresAt>"
            f"<psrf_qqaccess_token>{escape(credential.access_token)}</psrf_qqaccess_token>"
            "<hotfix>100000000</hotfix>"
            f"<traceid>{traceid}</traceid>"
            "<cid>228</cid>"
            f'<item cmd="1" optime="{timestamp}" nettype="1030" '
            f"QQ={quoteattr(str(credential.musicid))} uid={quoteattr(uid)} "
            f"os={quoteattr(os_ver)} model={quoteattr(phone_type)} "
            'version="12.3.5.8" songtype="1" playtype="4" '
            'from="1,132,151," dts="0" openstore="0" crytype="5" '
            'paytype="3" desktoplyric="0" playdevice="0" '
            'playlist_mode="0" outdev="0" url="26" playmode="1" '
            'repeat_times="0" cdn="" cdnip="" '
            'hasFirstBuffer="1" hijackflag="0" filetype="4" '
            f'err="0" size="0" time="{seconds}" retry="0" '
            'issoftdecode="1" component_type="2" '
            'bandwidth_policy="0" secondCacheCount="0" '
            'wait_time="0" player_retry="0" '
            f'audiotime="{duration_sec}" '
            f"timekey={quoteattr(timekey)} "
            f"vkey={quoteattr(vkey)} "
            f'play_duration_mi="{play_duration_mi}" '
            'play_speed="1.0" vip_level="65552" '
            'audio_effect="0:0" '
            f'songid="{song_id}" singerid="0" fversion="0"/>'
            "</root>"
        )

    @staticmethod
    def _random_digits(count: int) -> str:
        return "".join(str(random.randint(0, 9)) for _ in range(count))

    @staticmethod
    def _random_token(count: int) -> str:
        alphabet = "abcdefghijklmnopqrstuvwxyz0123456789"
        return "".join(random.choice(alphabet) for _ in range(count))

    @staticmethod
    def _random_hex(count: int) -> str:
        return "".join(random.choice("0123456789abcdef") for _ in range(count))
