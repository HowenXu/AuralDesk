"""听歌上报 Web 路由适配."""

from ..routing.adapter_registry import adapter
from ..routing.route_types import RouteContext


def _ok(resp: object) -> bool:
    # SDK 已把非 0 的 code 转成异常；能返回 dict 就代表这一路成功。
    # 部分接口成功时 data 是空字典 {}，不能再按 code 字段二次判定。
    return isinstance(resp, dict)


@adapter("report", "play")
async def report_play_adapter(context: RouteContext):
    """按 QQ 客户端的三段语义上报：start / seek / end."""
    mid = str(context.params.get("mid") or "")
    song_id = int(context.params.get("song_id") or 0)
    album_id = str(context.params.get("album_id") or "0")
    play_ms = int(context.params.get("play_ms") or 0)
    play_seconds = int(context.params.get("play_seconds") or 0)
    duration_sec = int(context.params.get("duration_sec") or 0)
    completed = bool(context.params.get("completed"))
    kind = str(context.params.get("kind") or "start")
    playlist = context.params.get("playlist") or []

    result = {"recent": False, "listening": False, "stream": False}
    if kind in {"start", "seek"} and mid:
        try:
            resp = await context.client.report.report_listening(
                mid,
                max(0, play_ms),
                playlist,
                credential=context.credential,
            )
            result["listening"] = _ok(resp)
        except Exception as exc:  # noqa: BLE001
            result["listening_error"] = str(exc)

    if kind == "start" and song_id > 0:
        try:
            result["recent"] = await context.client.report.report_recently(
                song_id,
                album_id,
                credential=context.credential,
            )
        except Exception as exc:  # noqa: BLE001
            result["recent_error"] = str(exc)

    if kind == "end":
        if completed and mid:
            try:
                resp = await context.client.report.report_listening(
                    mid,
                    max(0, duration_sec * 1000),
                    playlist,
                    credential=context.credential,
                )
                result["listening"] = _ok(resp)
            except Exception as exc:  # noqa: BLE001
                result["listening_error"] = str(exc)
        try:
            result["stream"] = await context.client.report.report_stream(
                song_id,
                duration_sec,
                play_seconds,
                credential=context.credential,
            )
        except Exception as exc:  # noqa: BLE001
            result["stream_error"] = str(exc)
    return result
