"""歌曲模块 Web 路由适配."""

from typing import Annotated, Any, TypeAlias

from pydantic import BaseModel, BeforeValidator, Field, WithJsonSchema
from pydantic.json_schema import SkipJsonSchema

from qqmusic_api.modules.song import (
    BaseSongFileType,
    EncryptedSongFileType,
    RingSongFileType,
    SongFileInfo,
    SongFileType,
    SongQueryInfo,
    SpecialSongFileType,
)

from ..routing.adapter_registry import adapter
from ..routing.docstrings import get_enum_member_descriptions
from ..routing.params import enum_mapping_schema, enum_mapping_validator
from ..routing.route_types import EnumIntMapping, RouteContext

SONG_FILE_TYPES: tuple[BaseSongFileType, ...] = (
    SongFileType.DTS_X,
    SongFileType.MASTER,
    SongFileType.ATMOS_2,
    SongFileType.ATMOS_51,
    SongFileType.ATMOS_71,
    SongFileType.ATMOS_DB,
    SongFileType.NAC,
    SongFileType.FLAC,
    SongFileType.OGG_640,
    SongFileType.OGG_320,
    SongFileType.OGG_192,
    SongFileType.OGG_96,
    SongFileType.MP3_320,
    SongFileType.MP3_128,
    SongFileType.ACC_192,
    SongFileType.ACC_96,
    SongFileType.ACC_48,
    EncryptedSongFileType.DTS_X,
    EncryptedSongFileType.VINYL,
    EncryptedSongFileType.MASTER,
    EncryptedSongFileType.ATMOS_2,
    EncryptedSongFileType.ATMOS_51,
    EncryptedSongFileType.ATMOS_71,
    EncryptedSongFileType.ATMOS_DB,
    EncryptedSongFileType.NAC,
    EncryptedSongFileType.FLAC,
    EncryptedSongFileType.HIRES,
    EncryptedSongFileType.OGG_640,
    EncryptedSongFileType.OGG_320,
    EncryptedSongFileType.OGG_192,
    EncryptedSongFileType.OGG_96,
    SpecialSongFileType.TRY,
    SpecialSongFileType.ACCOM,
    SpecialSongFileType.MULTI,
    SpecialSongFileType.PIANO,
    SpecialSongFileType.BAYIN,
    SpecialSongFileType.GUZHENG,
    SpecialSongFileType.QUDI,
    SpecialSongFileType.HULUSI,
    SpecialSongFileType.SUONA,
    SpecialSongFileType.SHOUDIE,
    SpecialSongFileType.GUITAR,
    SpecialSongFileType.DRUMS,
    SpecialSongFileType.KAZOO,
    SpecialSongFileType.THERAPY,
    RingSongFileType.RING_128,
    RingSongFileType.RING_96,
    RingSongFileType.RING_48,
)
SONG_FILE_TYPE_LABELS = tuple(
    member.name
    if isinstance(member, SongFileType)
    else f"{type(member).__name__.removesuffix('SongFileType').upper()}_{member.name}"
    for member in SONG_FILE_TYPES
)

_song_file_type_descriptions_map: dict[str, str] = {}
for enum_type in {type(m) for m in SONG_FILE_TYPES}:
    _song_file_type_descriptions_map.update(get_enum_member_descriptions(enum_type))

SONG_FILE_TYPE_DESCRIPTIONS = tuple(_song_file_type_descriptions_map.get(member.name, "") for member in SONG_FILE_TYPES)

SONG_FILE_TYPE_MAPPING = EnumIntMapping(
    SONG_FILE_TYPES, labels=SONG_FILE_TYPE_LABELS, descriptions=SONG_FILE_TYPE_DESCRIPTIONS
)
SongFileTypeParam: TypeAlias = Annotated[
    Any,
    BeforeValidator(enum_mapping_validator(SONG_FILE_TYPE_MAPPING)),
    WithJsonSchema(enum_mapping_schema(SONG_FILE_TYPE_MAPPING)),
]
DEFAULT_SONG_FILE_TYPE = SONG_FILE_TYPE_MAPPING.values[SONG_FILE_TYPE_MAPPING.members.index(SongFileType.MP3_128)]
SONG_FILE_TYPE_SCHEMA = SONG_FILE_TYPE_MAPPING.schema()
SONG_FILE_TYPE_DESCRIPTION = "歌曲文件类型. 歌曲品质枚举值映射:\n" + SONG_FILE_TYPE_MAPPING.description()


class SongUrlItem(BaseModel):
    """单个歌曲文件链接请求项."""

    mid: str = Field(description="歌曲 MID.")
    file_type: SongFileTypeParam | SkipJsonSchema[None] = Field(
        default=None,
        description=SONG_FILE_TYPE_DESCRIPTION,
    )
    song_type: int | SkipJsonSchema[None] = Field(default=None, description="歌曲类型.")
    media_mid: str | SkipJsonSchema[None] = Field(default=None, description="媒体文件 MID.")


class SongUrlsRequest(BaseModel):
    """批量歌曲文件链接请求体."""

    file_info: list[SongUrlItem] = Field(description="歌曲文件信息列表.")
    file_type: SongFileTypeParam = Field(
        default=DEFAULT_SONG_FILE_TYPE,
        validate_default=True,
        description=SONG_FILE_TYPE_DESCRIPTION,
    )


class SongQueryItem(BaseModel):
    """单个歌曲查询项."""

    id: int | SkipJsonSchema[None] = Field(default=None, description="歌曲 ID.")
    mid: str | SkipJsonSchema[None] = Field(default=None, description="歌曲 MID.")
    song_type: int | SkipJsonSchema[None] = Field(default=None, description="歌曲类型.")


class QuerySongRequest(BaseModel):
    """批量歌曲查询请求体."""

    query_info: list[SongQueryItem] = Field(description="歌曲查询信息列表.")


@adapter("song", "get_song_urls")
async def get_song_urls_adapter(context: RouteContext):
    """批量获取歌曲文件链接."""
    body = context.params["body"]
    return await context.client.song.get_song_urls(
        [
            SongFileInfo(
                mid=item.mid,
                file_type=item.file_type,
                song_type=item.song_type,
                media_mid=item.media_mid,
            )
            for item in body.file_info
        ],
        file_type=body.file_type,
        credential=context.credential,
    )


@adapter("song", "get_fav_num_by_id")
async def get_fav_num_by_id_adapter(context: RouteContext):
    """根据单个歌曲 ID 获取收藏数量."""
    return await context.client.song.get_fav_num([context.params["id"]])

@adapter("song", "get_edown_url")
async def get_edown_url_adapter(context: RouteContext):
    """获取 Hi-Res 下载密钥（CgiGetEDownUrl），用于离线解密 MusicEx/旧版内嵌 ekey 文件."""
    return await _cgi_get_edown_url(context)

async def _cgi_get_edown_url(context) -> dict:
    """调用 music.vkey.GetEDownUrl/CgiGetEDownUrl 拿 ekey（Hi-Res 离线解密密钥）。"""
    mid = context.params["mid"]
    media_mid = context.params.get("media_mid") or mid
    song_type = context.params.get("song_type") or 1
    req = context.client.song._build_cgi(
        module="music.vkey.GetEDownUrl",
        method="CgiGetEDownUrl",
        param={
            "filename": [f"RSM1{media_mid}.mflac"],
            "musicfile": [f"RSM1{mid}.mflac"],
            "songmid": [mid],
            "songtype": [song_type],
            "uin": context.credential.str_musicid if context.credential else "",
            "guid": "37299DD8F8A540A1203F439C64954C2B",
            "checklimit": 1,
            "ctx": 1,
            "downloadfrom": 0,
            "nettype": "",
            "referer": "y.qq.com",
            "scene": 0,
        },
        credential=context.credential,
        require_login=True,
        disable_parse=True,
    )
    data = await req
    item = (data.get("midurlinfo") or [{}])[0]
    return {"ekey": item.get("ekey") or ""}


@adapter("song", "get_song_url")
async def get_song_url_adapter(context: RouteContext):
    """根据单个歌曲 MID 获取文件链接."""
    return await context.client.song.get_song_urls(
        [
            SongFileInfo(
                mid=context.params["mid"],
                song_type=context.params.get("song_type"),
                media_mid=context.params.get("media_mid"),
            )
        ],
        file_type=context.params["file_type"],
        credential=context.credential,
    )


@adapter("song", "query_song_get")
async def query_song_get_adapter(context: RouteContext):
    """获取单首歌曲信息."""
    value = context.params["value"]
    song_type = context.params.get("song_type")
    is_id = value.isdecimal()
    query_info = SongQueryInfo(
        id=int(value) if is_id else None,
        mid=None if is_id else value,
        song_type=song_type,
    )
    return await context.client.song.query_song([query_info])


@adapter("song", "query_song_post")
async def query_song_post_adapter(context: RouteContext):
    """批量查询歌曲."""
    body = context.params["body"]
    query_info = [SongQueryInfo(id=item.id, mid=item.mid, song_type=item.song_type) for item in body.query_info]
    return await context.client.song.query_song(query_info)
