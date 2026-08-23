"""Lyric API 返回模型定义."""

import contextlib

from pydantic import Field, model_validator

from ..algorithms import qrc_decrypt
from .request import Response


class GetLyricResponse(Response):
    """歌词接口返回响应.

    Attributes:
        songid: 歌曲 ID.
        lyric: 原始歌词内容.
        trans: 翻译歌词内容.
        roma: 罗马音歌词内容.
        singing_annotations: 助唱标注歌词内容.
        lrc_t: LRC 歌词更新时间戳.
        qrc_t: QRC 歌词更新时间戳.
        trans_t: 翻译歌词更新时间戳.
        roma_t: 罗马音歌词更新时间戳.
        singing_annotations_ts: 助唱标注歌词时间戳.
        has_contributor: 是否有歌词贡献者.
        has_trans_contributor: 是否有翻译贡献者.
        has_multi_trans: 是否有多风格翻译歌词.
    """

    songid: int = Field(validation_alias="songID")
    lyric: str
    trans: str = ""
    roma: str = ""
    singing_annotations_lyric: str = Field(default="", validation_alias="singingAnnotationsLyric")
    lrc_t: int = 0
    qrc_t: int = 0
    trans_t: int = 0
    roma_t: int = 0
    singing_annotations_ts: int = Field(default=0, validation_alias="singingAnnotationsTs")
    has_contributor: bool = Field(default=False, validation_alias="hasContributor")
    has_trans_contributor: bool = Field(default=False, validation_alias="hasTransContributor")
    has_multi_trans: bool = Field(default=False, validation_alias="hasMultiTrans")

    @model_validator(mode="before")
    @classmethod
    def _decrypt_lyrics(cls, data: dict) -> dict:
        target_fields = ("lyric", "trans", "roma", "singingAnnotationsLyric")
        for field in target_fields:
            value = data.get(field)
            if value and isinstance(value, str):
                with contextlib.suppress(ValueError, TypeError):
                    data[field] = qrc_decrypt(value)
        return data


class GetSingingAnnotationsInfoResponse(Response):
    """获取助唱标注歌词信息响应模型.

    Attributes:
        has_singing_annotations_lyric: 是否包含助唱标注歌词.
    """

    has_singing_annotations_lyric: bool = Field(default=False, validation_alias="hasSingingAnnotationsLyric")


class MultiStyleLyricItem(Response):
    """多风格翻译歌词项模型.

    Attributes:
        style: 风格 ID.
        style_name: 风格名称.
        lyric: 翻译歌词内容.
        timestamp: 时间戳.
    """

    style: int
    style_name: str = Field(validation_alias="styleName")
    lyric: str
    timestamp: int = 0

    @model_validator(mode="before")
    @classmethod
    def _decrypt_lyric(cls, data: dict) -> dict:
        value = data.get("lyric")
        if value and isinstance(value, str):
            with contextlib.suppress(ValueError, TypeError):
                data["lyric"] = qrc_decrypt(value)
        return data


class BatchGetMultiStyleTransLyricResponse(Response):
    """多风格翻译歌词接口响应.

    Attributes:
        lyrics: 多风格翻译歌词列表.
    """

    lyrics: list[MultiStyleLyricItem] = Field(default_factory=list)


class IsAIDictExistsResponse(Response):
    """检查是否存在 AI 歌词词典响应模型.

    Attributes:
        exists: 是否存在 AI 歌词词典.
    """

    exists: bool = False


class AIDictItem(Response):
    """AI 歌词词典项模型.

    Attributes:
        phrase: 划词 / 难词短语.
        explain: AI 详细释义与语境解析.
        lyric_text: 词语所在的歌词原文行.
        trans_lyric_text: 歌词原文行的中文翻译.
        lyric_timestamp: 歌词时间戳.
    """

    phrase: str = ""
    explain: str = ""
    lyric_text: str = ""
    trans_lyric_text: str = ""
    lyric_timestamp: str = ""


class GetAIDictResponse(Response):
    """获取 AI 歌词词典响应模型.

    Attributes:
        dict_list: AI 歌词词典列表.
    """

    dict_list: list[AIDictItem] = Field(default_factory=list, validation_alias="dictList")
