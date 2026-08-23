"""辅助功能相关数据模型."""

from typing import TypedDict

from pydantic import Field

from .request import BaseModel


class InitUploadFileDict(TypedDict):
    """InitUpload 的单文件参数字典.

    Attributes:
        FileSha1: 待上传文件的 SHA1 哈希值.
        FileName: 待上传文件的名称.
        FileSize: 待上传文件的大小(字节).
    """

    FileSha1: str
    FileName: str
    FileSize: int


class FinishUploadBucketDict(TypedDict):
    """FinishUpload 的 Bucket 字典.

    Attributes:
        Name: 实际上传使用的存储桶名称.
        Region: 实际上传使用的存储桶地域.
    """

    Name: str
    Region: str


class FinishUploadStorageDict(TypedDict):
    """FinishUpload 的 Storage 字典.

    Attributes:
        Bucket: 存储桶信息字典.
        ObjectKey: COS 对象存储路径.
    """

    Bucket: FinishUploadBucketDict
    ObjectKey: str


class FinishUploadResultDict(TypedDict):
    """FinishUpload 的单文件结果字典.

    Attributes:
        Storage: 存储信息字典.
        UploadResult: 上传状态码, 通常为 0 表示成功.
    """

    Storage: FinishUploadStorageDict
    UploadResult: int


class UploadBucketInfo(BaseModel):
    """COS 存储桶基本信息.

    Attributes:
        name: 存储桶名称.
        region: 存储桶地域.
    """

    name: str = Field(validation_alias="Name")
    region: str = Field(validation_alias="Region")


class UploadBucketStatus(BaseModel):
    """COS 存储桶上传状态.

    Attributes:
        bucket: 存储桶基本信息.
        upload_status: 上传状态码, 0 表示正常.
    """

    bucket: UploadBucketInfo = Field(validation_alias="Bucket")
    upload_status: int = Field(validation_alias="UploadStatus")


class UploadFileInfo(BaseModel):
    """COS 上传文件元信息.

    Attributes:
        file_sha1: 文件 SHA1 哈希值.
        object_key: COS 对象存储路径 (即文件路径).
        buckets: 目标存储桶列表.
    """

    file_sha1: str = Field(validation_alias="FileSha1")
    object_key: str = Field(validation_alias="ObjectKey")
    buckets: list[UploadBucketStatus] = Field(validation_alias="Buckets")


class UploadAuthInfo(BaseModel):
    """COS 上传鉴权信息.

    Attributes:
        secret_id: COS 临时密钥 ID.
        secret_key: COS 临时密钥 Key.
        token: COS 临时 Token.
        start_time: 临时密钥生效时间戳.
        expired_time: 临时密钥过期时间戳.
    """

    secret_id: str = Field(validation_alias="SecretID")
    secret_key: str = Field(validation_alias="SecretKey")
    token: str = Field(validation_alias="Token")
    start_time: int = Field(validation_alias="StartTime")
    expired_time: int = Field(validation_alias="ExpiredTime")


class InitUploadResponse(BaseModel):
    """InitUpload 接口返回数据.

    Attributes:
        auth_info: COS 上传鉴权凭证.
        files: 文件存储路径与存储桶信息列表.
    """

    auth_info: UploadAuthInfo = Field(validation_alias="AuthInfo")
    files: list[UploadFileInfo] = Field(validation_alias="Files")


class UploadStorage(BaseModel):
    """存储信息.

    Attributes:
        bucket: 存储桶信息.
        object_key: 文件在存储桶中的对象路径.
    """

    bucket: UploadBucketInfo = Field(validation_alias="Bucket")
    object_key: str = Field(validation_alias="ObjectKey")


class UploadUrlInfo(BaseModel):
    """COS 上传完成后的 URL 详情.

    Attributes:
        file_id: 文件唯一 ID (部分场景返回).
        url: 基础访问 URL.
        cdn_url: CDN 加速访问 URL.
        presigned_url: 预签名带鉴权访问 URL.
        internal_url: 内部网络访问 URL.
    """

    file_id: str = Field(validation_alias="FileId", default="")
    url: str = Field(validation_alias="URL")
    cdn_url: str = Field(validation_alias="CDNURL")
    presigned_url: str = Field(validation_alias="PresignedURL", default="")
    internal_url: str = Field(validation_alias="InternalURL", default="")


class UploadObjectInfo(BaseModel):
    """COS 上传完成后的文件对象.

    Attributes:
        storage: 文件所在的存储系统信息.
        url: 文件上传成功后分配的各路访问 URL.
    """

    storage: UploadStorage = Field(validation_alias="Storage")
    url: UploadUrlInfo = Field(validation_alias="Url")


class FinishUploadResponse(BaseModel):
    """FinishUpload 接口返回数据.

    Attributes:
        objects: 上传成功的文件对象列表.
    """

    objects: list[UploadObjectInfo] | None = Field(validation_alias="Objects", default=None)
