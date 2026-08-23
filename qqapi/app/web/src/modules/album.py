"""专辑模块 Web 路由适配."""

from ..routing.adapter_registry import adapter
from ..routing.route_types import RouteContext


@adapter("album", "fav_album")
async def fav_album_adapter(context: RouteContext):
    """收藏专辑."""
    return await context.client.album.fav_album(
        context.params["album_id"],
        credential=context.credential,
    )


@adapter("album", "del_fav_album")
async def del_fav_album_adapter(context: RouteContext):
    """取消收藏专辑."""
    return await context.client.album.del_fav_album(
        context.params["album_id"],
        credential=context.credential,
    )
