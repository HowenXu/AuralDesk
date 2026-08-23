"""签名算法实现."""

import re
from base64 import b64encode
from hashlib import sha1

PART_1_INDEXES = [23, 14, 6, 36, 16, 7, 19]
PART_2_INDEXES = [16, 1, 32, 12, 19, 27, 8, 5]
SCRAMBLE_VALUES = [89, 39, 179, 150, 218, 82, 58, 252, 177, 52, 186, 123, 120, 64, 242, 133, 143, 161, 121, 179]


def zzc_sign(payload: str | bytes | bytearray) -> str:
    """计算 QQ 音乐客户端请求的 zzc 签名.

    Args:
        payload: 待签名的明文 (字符串或二进制数据).

    Returns:
        str: 计算出的签名值.
    """
    payload_bytes = payload.encode("utf-8") if isinstance(payload, str) else bytes(payload)
    hash_hex = sha1(payload_bytes).hexdigest().upper()

    part1 = "".join(hash_hex[i] for i in PART_1_INDEXES)
    part2 = "".join(hash_hex[i] for i in PART_2_INDEXES)

    part3 = bytearray(20)
    for i, v in enumerate(SCRAMBLE_VALUES):
        value = v ^ int(hash_hex[i * 2 : i * 2 + 2], 16)
        part3[i] = value
    b64_part = re.sub(rb"[\\/+=]", b"", b64encode(part3)).decode("utf-8")
    return f"zzc{part1}{b64_part}{part2}".lower()
