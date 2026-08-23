"""私信模块返回模型定义."""

from typing import Annotated, Any

from pydantic import Field, model_validator

from ._validator import NoneToEmptyDict
from .request import Response


class PrivateMessageUser(Response):
    """私信用户信息.

    Attributes:
        avatar: 用户头像地址.
        encrypt_uin: 加密 UIN.
        uin: 用户 UIN.
        identity_pic: 身份图标地址.
        nick: 用户昵称.
        identity: 身份标识.
        type: 用户类型.
        is_concern: 关注状态.
    """

    avatar: str = ""
    encrypt_uin: str = ""
    uin: str = ""
    identity_pic: str = ""
    nick: str = ""
    identity: int = -1
    type: int = -1
    is_concern: int = 0


class PrivateMessageMetaData(Response):
    """私信消息元数据.

    Attributes:
        title: 卡片标题.
        content: 文本内容或卡片正文.
        pic: 图片地址.
        biz_id: 业务 ID.
        biz_type: 业务类型.
        url: 跳转链接.
        width: 媒体宽度.
        height: 媒体高度.
        duration: 媒体时长.
        size: 媒体大小.
    """

    title: str = ""
    content: str = ""
    pic: str = ""
    biz_id: str = ""
    biz_type: int = 0
    url: str = ""
    width: int = 0
    height: int = 0
    duration: int = Field(default=0, validation_alias="Duration")
    size: int = Field(default=0, validation_alias="Size")


class PrivateMessageInfo(Response):
    """私信消息项.

    Attributes:
        id: 消息 ID.
        meta_data: 消息元数据.
        client_key: 客户端生成的消息键.
        from_user: 发送方用户信息.
        time: 消息时间戳.
        state: 消息状态.
        result: 发送结果.
        tips: 提示文案.
        sequence: 消息序列号.
        show_type: 展示类型.
        msg_type: 消息类型.
        confirm: 确认状态.
        sort_time: 排序时间戳.
        complain_tip: 投诉提示文案.
        complain_url: 投诉跳转链接.
    """

    id: str = ""
    meta_data: PrivateMessageMetaData | None = None
    client_key: str = ""
    from_user: PrivateMessageUser | None = None
    time: int = 0
    state: int = 0
    result: int = 0
    tips: str = ""
    sequence: int = 0
    show_type: int = 0
    msg_type: int = 0
    confirm: int = 0
    sort_time: int = 0
    complain_tip: str = Field(default="", validation_alias="complainTip")
    complain_url: str = Field(default="", validation_alias="complainUrl")


class PrivateMessageTailTag(Response):
    """私信会话尾部标签.

    Attributes:
        data: 原始标签字段.
    """

    data: dict[str, Any] = Field(default_factory=dict)

    @model_validator(mode="before")
    @classmethod
    def _wrap_raw_tag(cls, data: Any) -> Any:
        """保留未建模的标签字段."""
        if isinstance(data, dict) and "data" not in data:
            return {"data": data}
        return data


class PrivateMessageSession(Response):
    """私信会话项.

    Attributes:
        session_id: 会话 ID.
        user: 对端用户信息.
        new_msg: 最新消息.
        new_msg_cnt: 未读消息数.
        sort_time: 排序时间戳.
        url: 会话跳转链接.
        create_time: 创建时间戳.
        from_: 会话来源.
        sm_star_virtual_uin: 超级私信虚拟 UIN.
        auth: 授权信息.
        ext: 扩展字段.
        tail_tags: 尾部标签列表.
    """

    session_id: str = ""
    user: PrivateMessageUser | None = None
    new_msg: PrivateMessageInfo | None = None
    new_msg_cnt: int = 0
    sort_time: int = 0
    url: str = ""
    create_time: int = Field(default=0, validation_alias="create_time")
    from_: int = Field(default=0, validation_alias="from")
    # 服务端字段名拼写如此, 保持 alias 与实际返回一致.
    sm_star_virtual_uin: str = Field(default="", validation_alias="SmStarVirtaulUin")
    auth: str = Field(default="", validation_alias="Auth")
    ext: dict[str, str] = Field(default_factory=dict, validation_alias="Ext")
    tail_tags: list[PrivateMessageTailTag] = Field(default_factory=list, validation_alias="TailTags")


class PrivateSessionListResponse(Response):
    """私信会话列表响应.

    Attributes:
        has_more: 是否还有更多会话.
        msg: 提示文案.
        new_msg_cnt: 未读消息数.
        sessions: 会话列表.
        subcode: 业务子码.
        setting_guide: 设置引导标记.
        state: 状态标记.
        extra: 扩展字段.
    """

    has_more: int = 0
    msg: str = ""
    new_msg_cnt: int = 0
    sessions: list[PrivateMessageSession] = Field(default_factory=list)
    subcode: int = 0
    setting_guide: int = 0
    state: int = 0
    extra: dict[str, str] = Field(default_factory=dict)


class PrivateMessagePatText(Response):
    """拍一拍文案信息.

    Attributes:
        nick: 用户昵称.
        pat_text: 拍一拍文案.
    """

    nick: str = Field(default="", validation_alias="Nick")
    pat_text: str = Field(default="", validation_alias="PatTxt")


class PrivateMessageListResponse(Response):
    """私信消息列表响应.

    Attributes:
        has_more: 是否还有更多消息.
        messages: 消息列表.
        msg: 提示文案.
        session: 会话信息.
        subcode: 业务子码.
        end_msg_seq: 结束消息序列号.
        attach: 附加字段.
        pat_interval: 拍一拍间隔.
        pat_map: 拍一拍文案映射.
        encrypt_star: 加密明星 UIN.
        location_tips: 定位提示文案.
        new_msg_cnt: 新消息数量.
    """

    has_more: int = 0
    messages: list[PrivateMessageInfo] = Field(default_factory=list)
    msg: str = ""
    session: PrivateMessageSession | None = None
    subcode: int = 0
    end_msg_seq: int = 0
    attach: Annotated[dict[str, Any], NoneToEmptyDict] = Field(default_factory=dict, validation_alias="Attach")
    pat_interval: int = Field(default=0, validation_alias="PatInterval")
    pat_map: Annotated[dict[str, PrivateMessagePatText], NoneToEmptyDict] = Field(
        default_factory=dict, validation_alias="PatMap"
    )
    encrypt_star: str = Field(default="", validation_alias="EncryptStar")
    location_tips: str = Field(default="", validation_alias="LocationTips")
    new_msg_cnt: int = Field(default=0, validation_alias="NewMsgCnt")


class PrivateSendMessageResponse(Response):
    """私信发送响应.

    Attributes:
        messages: 服务端返回的消息列表.
        session: 会话信息.
        tips: 提示文案.
        identify_url: 实名认证跳转链接.
        msg: 业务提示.
        reason: 失败原因码.
        end_msg_seq: 结束消息序列号.
        update_time: 更新时间戳.
    """

    messages: list[PrivateMessageInfo] = Field(default_factory=list)
    session: PrivateMessageSession | None = None
    tips: str = ""
    identify_url: str = ""
    msg: str = ""
    reason: int = 0
    end_msg_seq: int = 0
    update_time: int = 0


class PrivateOperationResponse(Response):
    """私信写操作响应.

    Attributes:
        msg: 提示文案.
        subcode: 业务子码.
        tips: 失败提示.
    """

    msg: str = ""
    subcode: int = 0
    tips: str = ""


class PrivateConfigResponse(Response):
    """私信配置读取响应.

    Attributes:
        config_value: 配置值.
        config_value_str: 配置字符串值.
        msg: 提示文案.
    """

    config_value: int = 0
    config_value_str: str = ""
    msg: str = ""


class PrivateMusicianCardResponse(Response):
    """音乐人卡片响应.

    Attributes:
        data: 未建模的卡片原始字段.
    """

    data: dict[str, Any] = Field(default_factory=dict)

    @model_validator(mode="before")
    @classmethod
    def _wrap_raw_card(cls, data: Any) -> Any:
        """保留未建模的卡片字段."""
        if isinstance(data, dict) and "data" not in data:
            return {"data": data}
        return data


class PrivateEntryItem(Response):
    """聊天页入口项.

    Attributes:
        entry_type: 入口类型.
        icon: 图标地址.
        title: 标题.
        skip_scheme: 跳转 Scheme.
        right_top_tag: 右上角标签.
        ext: 扩展字段.
    """

    entry_type: int = Field(default=-1, validation_alias="EntryType")
    icon: str = Field(default="", validation_alias="Icon")
    title: str = Field(default="", validation_alias="Title")
    skip_scheme: str = Field(default="", validation_alias="SkipScheme")
    right_top_tag: str = Field(default="", validation_alias="RightTopTag")
    ext: dict[str, str] = Field(default_factory=dict, validation_alias="Ext")


class PrivateChatEntriesResponse(Response):
    """聊天页入口响应.

    Attributes:
        ret_code: 返回码.
        ret_msg: 返回文案.
        entries: 按场景分组的入口列表.
        can_be_dazi: 是否可成为搭子.
        dz_data: 未建模的搭子入口字段.
    """

    ret_code: int = Field(default=0, validation_alias="RetCode")
    ret_msg: str = Field(default="", validation_alias="RetMsg")
    entries: dict[int, list[PrivateEntryItem]] = Field(default_factory=dict, validation_alias="Entries")
    can_be_dazi: bool | None = Field(default=None, validation_alias="CanBeDazi")
    dz_data: dict[str, Any] = Field(default_factory=dict, validation_alias="DzData")


class PrivateMediaMessageDetailsResponse(Response):
    """图片和视频消息详情响应.

    Attributes:
        msg_ids: 消息 ID 到消息详情的映射.
    """

    msg_ids: dict[str, PrivateMessageInfo] = Field(default_factory=dict, validation_alias="MsgIDs")


class PrivateSafetyHintResponse(Response):
    """私信安全提示响应.

    Attributes:
        hint: 安全提示文案.
    """

    hint: str = ""
