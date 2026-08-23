"""Comment API 返回模型定义."""

from enum import IntEnum
from typing import Any

from pydantic import Field

from .request import Response


class CommentBizType(IntEnum):
    """评论业务分类.

    + SONG: 单曲评论.
    + ALBUM: 专辑评论.
    + PLAYLIST: 歌单评论.
    + MV: 音乐视频(MV)评论.
    + SPECIAL_AUDIO: 特殊音频/长音频等评论.
    """

    SONG = 1
    ALBUM = 2
    PLAYLIST = 3
    MV = 4
    SPECIAL_AUDIO = 15


class IconTextInfo(Response):
    """评论数量接口附带的角标文案与展示标识.

    Attributes:
        txt: 角标展示文案.
        unique_id: 角标唯一标识.
        type: 角标类型.
        cmid: 关联评论 ID.
        is_dynamic: 是否为动态角标.
    """

    txt: str
    unique_id: str
    type: int
    cmid: str
    is_dynamic: bool


class CommentCountResponse(Response):
    """评论数量接口整理后的统计结果.

    Attributes:
        biz_type: 业务类型.
        biz_id: 业务对象 ID.
        biz_sub_type: 业务子类型.
        count: 评论总数.
        count_ver: 计数字段版本.
        count_view: 面向展示的计数文案.
        related_id: 关联对象 ID.
        tip: 附加提示文案.
        icon_list: 角标信息列表.
        cm_tab_type: 评论标签页类型.
    """

    biz_type: int = Field(json_schema_extra={"jsonpath": "$.response.biz_type"})
    biz_id: str = Field(json_schema_extra={"jsonpath": "$.response.biz_id"})
    biz_sub_type: int = Field(json_schema_extra={"jsonpath": "$.response.biz_sub_type"})
    count: int = Field(json_schema_extra={"jsonpath": "$.response.count"})
    count_ver: str = Field(json_schema_extra={"jsonpath": "$.response.count_ver"})
    count_view: str = Field(json_schema_extra={"jsonpath": "$.response.count_view"})
    related_id: str = Field(json_schema_extra={"jsonpath": "$.response.related_id"})
    tip: str = Field(json_schema_extra={"jsonpath": "$.response.tip"})
    icon_list: list[IconTextInfo] = Field(
        default_factory=list,
        json_schema_extra={"jsonpath": "$.response.icon_list[*]"},
    )
    cm_tab_type: int = Field(json_schema_extra={"jsonpath": "$.cmTabType"})


class CommentItem(Response):
    """标准评论列表中的单条评论记录.

    Attributes:
        cmid: 评论 ID.
        seq_no: 评论序号.
        nick: 昵称.
        avatar: 头像地址.
        encrypt_uin: 加密 UIN.
        content: 评论正文.
        pub_time: 发布时间戳.
        praise_num: 点赞数.
        reply_cnt: 回复数.
        is_praised: 当前账号是否已点赞.
        is_self: 是否为本人评论.
        state: 评论状态.
        hot_score: 热度分数文案.
        rec_score: 推荐分数文案.
        song_id: 关联歌曲 ID.
        song_name: 关联歌曲名.
        singer_names: 关联歌手名串.
        song_ts_elems: 歌曲时间轴元素.
        hash_tag_list: 话题标签列表.
        little_tails: 尾巴挂件列表.
        icon_list: 图标列表.
        vip_ui: VIP 展示信息.
        sub_comments: 子评论列表.
    """

    cmid: str = Field(validation_alias="CmId")
    seq_no: str = Field(validation_alias="SeqNo")
    nick: str = Field(validation_alias="Nick")
    avatar: str = Field(validation_alias="Avatar")
    encrypt_uin: str = Field(validation_alias="EncryptUin")
    content: str = Field(validation_alias="Content")
    pub_time: int = Field(validation_alias="PubTime")
    praise_num: int = Field(validation_alias="PraiseNum")
    reply_cnt: int = Field(validation_alias="ReplyCnt")
    is_praised: int = Field(validation_alias="IsPraised")
    is_self: int = Field(validation_alias="IsSelf")
    state: int = Field(validation_alias="State")
    hot_score: str = Field(validation_alias="HotScore")
    rec_score: str = Field(validation_alias="RecScore")
    song_id: int = Field(validation_alias="SongId")
    song_name: str = Field(validation_alias="SongName")
    singer_names: str = Field(validation_alias="SingerNames")
    song_ts_elems: list[dict[str, Any]] = Field(default_factory=list, validation_alias="SongTsElems")
    hash_tag_list: list[dict[str, Any]] = Field(default_factory=list, validation_alias="HashTagList")
    little_tails: list[dict[str, Any]] = Field(default_factory=list, validation_alias="LittleTails")
    icon_list: list[dict[str, Any]] = Field(default_factory=list, validation_alias="IconList")
    vip_ui: dict[str, Any] = Field(default_factory=dict, validation_alias="VipUI")
    sub_comments: list[dict[str, Any]] = Field(default_factory=list, validation_alias="SubComments")


class CommentListResponse(Response):
    """歌曲等常规内容评论列表接口的响应体.

    Attributes:
        comments: 当前页评论列表.
        comment_ids: 当前页评论 ID 列表.
        has_more: 是否还有更多结果.
        next_offset: 下一页偏移量.
        total: 评论总数.
        total_cm_num: 评论总量字段.
        comment_tip: 评论提示文案.
        comment_h5_page: H5 页面地址.
        has_ts_cm: 是否包含时间轴评论.
        share_cnt: 分享数.
        msg: 附加消息.
        sub_code: 子返回码.
    """

    comments: list[CommentItem] = Field(
        default_factory=list,
        json_schema_extra={"jsonpath": "$.CommentList.Comments[*]"},
    )
    comment_ids: list[str] = Field(default_factory=list, json_schema_extra={"jsonpath": "$.CommentList.CommentIds[*]"})
    has_more: int = Field(json_schema_extra={"jsonpath": "$.CommentList.HasMore"})
    next_offset: int = Field(json_schema_extra={"jsonpath": "$.CommentList.NextOffset"})
    total: int = Field(json_schema_extra={"jsonpath": "$.CommentList.Total"})
    total_cm_num: int = Field(validation_alias="TotalCmNum")
    comment_tip: str = Field(validation_alias="CommentTip")
    comment_h5_page: str = Field(validation_alias="CommentH5Page")
    has_ts_cm: int = Field(validation_alias="HasTsCm")
    share_cnt: int = Field(validation_alias="ShareCnt")
    msg: str = Field(validation_alias="Msg")
    sub_code: int = Field(validation_alias="SubCode")


class MomentCommentItem(Response):
    """时刻动态评论流中的单条评论记录.

    Attributes:
        cmid: 评论 ID.
        seq_no: 评论序号.
        content: 评论正文.
        encrypt_uin: 加密 UIN.
        pub_time: 发布时间戳.
        praise_num: 点赞数.
        reply_cnt: 回复数.
        state: 评论状态.
        is_self: 是否为本人评论.
        location: 位置文案.
        phone_type: 设备类型文案.
        pic: 配图地址.
        pic_size: 配图尺寸信息.
        song_ts_elems: 歌曲时间轴元素.
        hash_tag_list: 话题标签列表.
        little_tails: 尾巴挂件列表.
    """

    cmid: str = Field(validation_alias="CmId")
    seq_no: str = Field(validation_alias="SeqNo")
    content: str = Field(validation_alias="Content")
    encrypt_uin: str = Field(validation_alias="EncryptUin")
    pub_time: int = Field(validation_alias="PubTime")
    praise_num: int = Field(validation_alias="PraiseNum")
    reply_cnt: int = Field(validation_alias="ReplyCnt")
    state: int = Field(validation_alias="State")
    is_self: int = Field(validation_alias="IsSelf")
    location: str = Field(validation_alias="Location")
    phone_type: str = Field(validation_alias="PhoneType")
    pic: str = Field(validation_alias="Pic")
    pic_size: str = Field(validation_alias="PicSize")
    song_ts_elems: list[dict[str, Any]] = Field(default_factory=list, validation_alias="SongTsElems")
    hash_tag_list: list[dict[str, Any]] = Field(default_factory=list, validation_alias="HashTagList")
    little_tails: list[dict[str, Any]] = Field(default_factory=list, validation_alias="LittleTails")


class MomentCommentResponse(Response):
    """时刻评论列表接口的响应体.

    Attributes:
        comments: 当前页时刻评论列表.
        has_more: 是否还有更多结果.
        next_pos: 下一页游标.
        hint: 提示文案.
        prev_list_loaded: 前序列表是否已加载.
        map_cm_ext: 评论扩展信息映射.
    """

    comments: list[MomentCommentItem] = Field(default_factory=list, json_schema_extra={"jsonpath": "$.CmList[*]"})
    has_more: int = Field(validation_alias="HasMore")
    next_pos: str = Field(default="", validation_alias="NextPos")
    hint: str = Field(validation_alias="Hint")
    prev_list_loaded: int = Field(validation_alias="PrevListLoaded")
    map_cm_ext: dict[str, dict[str, Any]] = Field(default_factory=dict, validation_alias="MapCmExt")


class AddCommentResponse(Response):
    """添加评论接口的响应体.

    Attributes:
        subcode: 响应子代码.
        msg: 响应消息.
        id: 新增评论 ID.
        parent: 父评论 ID.
        floor: 楼层号.
        verify_url: 验证码 URL (如果需要).
    """

    subcode: int = Field(validation_alias="SubCode")
    msg: str = Field(validation_alias="Msg")
    id: str = Field(validation_alias="AddedCmId")
    parent: str = Field(validation_alias="ParentCmId")
    floor: int = Field(json_schema_extra={"jsonpath": "$.Floor.Num"})
    verify_url: str = Field(validation_alias="VerifyUrl")
