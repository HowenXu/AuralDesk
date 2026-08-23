"""Top API 返回模型定义."""

from typing import Annotated

from pydantic import Field

from ._validator import NoneToEmptyList
from .base import Song
from .request import Response


class TopPreviewSong(Response):
    """排行榜预览歌曲条目.

    Attributes:
        rank: 排名位置.
        rank_type: 排名变化类型.
        rank_value: 排名变化值文本.
        id: 歌曲数字 ID.
        name: 歌曲标题.
        singer_name: 歌手名称文本.
        singer_mid: 主歌手 MID.
        album_mid: 专辑 MID.
        cover: 封面地址.
        mv_id: MV 数字 ID.
    """

    rank: int
    rank_type: int = Field(validation_alias="rankType")
    rank_value: str = Field(validation_alias="rankValue")
    id: int = Field(validation_alias="songId")
    name: str = Field(validation_alias="title")
    singer_name: str = Field(validation_alias="singerName")
    singer_mid: str = Field(validation_alias="singerMid")
    album_mid: str = Field(validation_alias="albumMid")
    cover: str = ""
    mv_id: int = Field(default=0, validation_alias="mvid")


class TopSummary(Response):
    """排行榜摘要信息.

    Attributes:
        id: 排行榜 ID.
        name: 榜单标题.
        title_detail: 榜单完整标题.
        title_sub: 榜单副标题.
        intro: 榜单简介.
        period: 榜单期数.
        update_time: 更新时间.
        listen_num: 播放量.
        total_num: 榜单总曲数.
        songs: 榜单预览歌曲.
        front_pic_url: 榜单封面.
        head_pic_url: 榜单头图.
        h5_jump_url: H5 跳转地址.
        special_scheme: 客户端跳转 Scheme.
    """

    id: int = Field(validation_alias="topId")
    name: str = Field(validation_alias="title")
    title_detail: str = Field(validation_alias="titleDetail")
    title_sub: str = Field(default="", validation_alias="titleSub")
    intro: str = ""
    period: str = ""
    update_time: str = Field(default="", validation_alias="updateTime")
    listen_num: int = Field(default=0, validation_alias="listenNum")
    total_num: int = Field(default=0, validation_alias="totalNum")
    songs: list[TopPreviewSong] = Field(default_factory=list, validation_alias="song")
    front_pic_url: str = Field(default="", validation_alias="frontPicUrl")
    head_pic_url: str = Field(default="", validation_alias="headPicUrl")
    h5_jump_url: str = Field(default="", validation_alias="h5JumpUrl")
    special_scheme: str = Field(default="", validation_alias="specialScheme")


class TopCategory(Response):
    """排行榜分类.

    Attributes:
        id: 分类 ID.
        name: 分类名称.
        toplist: 分类下的排行榜摘要列表.
    """

    id: int = Field(validation_alias="groupId")
    name: str = Field(validation_alias="groupName")
    toplist: list[TopSummary]


class TopCategoryResponse(Response):
    """排行榜分类响应.

    Attributes:
        group: 排行榜分类列表.
    """

    group: list[TopCategory]


class TopDetailResponse(Response):
    """排行榜详情响应.

    Attributes:
        info: 排行榜基础信息.
        songs: 排行榜歌曲列表.
        song_tags: 歌曲标签列表.
        ext_info_list: 附加信息列表.
        index_info_list: 榜单索引信息列表.
    """

    info: TopSummary = Field(validation_alias="data")
    songs: list[Song] = Field(validation_alias="songInfoList")
    song_tags: Annotated[list[dict], NoneToEmptyList] = Field(default_factory=list, validation_alias="songTagInfoList")
    ext_info_list: Annotated[list[dict], NoneToEmptyList] = Field(default_factory=list, validation_alias="extInfoList")
    index_info_list: Annotated[list[dict], NoneToEmptyList] = Field(
        default_factory=list, validation_alias="indexInfoList"
    )
