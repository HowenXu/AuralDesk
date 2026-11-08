"""听歌上报 Web 路由契约."""

from ..routing.route_types import AuthPolicy, HttpMethod, WebRoute
from ._helpers import B, R

ROUTES: tuple[WebRoute, ...] = (
    R(
        "report",
        "play",
        "/report/play",
        dict,
        methods=(HttpMethod.POST,),
        params=(
            B("mid", str, "", "歌曲 mid."),
            B("song_id", int, 0, "歌曲数字 ID."),
            B("album_id", str, "0", "专辑 ID."),
            B("play_ms", int, 0, "已播放毫秒数."),
            B("playlist", list[int], [], "当前队列歌曲 ID."),
        ),
        auth=AuthPolicy.COOKIE_OR_DEFAULT,
        summary="上报播放记录",
        description="把播放写入 QQ 音乐的听歌记录和最近播放.",
    ),
)
