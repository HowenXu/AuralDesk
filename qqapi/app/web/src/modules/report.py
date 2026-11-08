"""听歌上报 Web 路由适配."""

from ..routing.adapter_registry import adapter
from ..routing.route_types import RouteContext


def _ok(resp: object) -> bool:
    return isinstance(resp, dict) and resp.get("code") == 0


@adapter("report", "play")
async def report_play_adapter(context: RouteContext):
    """同时写「听歌记录」和「最近播放」；两者互不影响，失败单独返回."""
    mid = str(context.params.get("mid") or "")
    song_id = int(context.params.get("song_id") or 0)
    album_id = str(context.params.get("album_id") or "0")
    play_ms = int(context.params.get("play_ms") or 0)
    playlist = context.params.get("playlist") or []

    result = {"recent": False, "listening": False}
    if song_id > 0:
        try:
            resp = await context.client.report.report_recently(
                song_id, album_id, credential=context.credential)
            result["recent"] = _ok(resp)
        except Exception as exc:  # noqa: BLE001
            result["recent_error"] = str(exc)
    if mid:
        try:
            resp = await context.client.report.report_listening(
                mid, play_ms, playlist, credential=context.credential)
            result["listening"] = _ok(resp)
        except Exception as exc:  # noqa: BLE001
            result["listening_error"] = str(exc)
    return result
