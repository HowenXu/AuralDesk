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
            B("kind", str, "start", "start / seek / end."),
            B("mid", str, "", "歌曲 mid."),
            B("song_id", int, 0, "歌曲数字 ID."),
            B("album_id", str, "0", "专辑 ID."),
            B("play_ms", int, 0, "当前播放位置（毫秒）."),
            B("play_seconds", int, 0, "本段真实播放秒数."),
            B("duration_sec", int, 0, "歌曲总时长（秒）."),
            B("completed", bool, False, "是否自然播完."),
            B("playlist", list[int], [], "当前队列歌曲 ID."),
        ),
        auth=AuthPolicy.COOKIE_OR_DEFAULT,
        summary="上报播放记录",
        description="按 QQ 客户端语义写入听歌记录、最近播放和 stat6 播放流水.",
    ),
)
