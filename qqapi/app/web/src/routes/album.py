"""专辑 Web 路由契约."""

from qqmusic_api.models.album import AlbumFavWriteResponse, GetAlbumDetailResponse, GetAlbumSongResponse

from ..routing.route_types import AuthPolicy, HttpMethod, PUBLIC_300, WebRoute
from ._helpers import B, VALUE, R

ROUTES: tuple[WebRoute, ...] = (
    R("album", "get_detail", "/album/{value}/detail", GetAlbumDetailResponse, params=VALUE, cache=PUBLIC_300),
    R(
        "album",
        "get_song",
        "/album/{value}/songs",
        GetAlbumSongResponse,
        params=VALUE,
        cache=PUBLIC_300,
    ),
    R(
        "album",
        "fav_album",
        "/album/fav",
        AlbumFavWriteResponse,
        methods=(HttpMethod.POST,),
        params=(B("album_id", list[int], description="专辑 ID 列表."),),
        auth=AuthPolicy.COOKIE_OR_DEFAULT,
    ),
    R(
        "album",
        "del_fav_album",
        "/album/unfav",
        AlbumFavWriteResponse,
        methods=(HttpMethod.POST,),
        params=(B("album_id", list[int], description="专辑 ID 列表."),),
        auth=AuthPolicy.COOKIE_OR_DEFAULT,
    ),
)
