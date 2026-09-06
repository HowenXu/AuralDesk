#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
QQMusic QMC encrypted-file decryptor
====================================

Pure-Python implementation with zero third-party dependencies (standard
library only); runs on Windows / macOS / Linux as-is.

Supported formats
-----------------
* v1 static key (.tkm / .bkc* / hex extensions, legacy)
* v2 embedded EKey (.mflac / .mgg / .mgg0 / .mgg1 / .mflac0 / .mmp4 /
  .qmcflac / .qmcogg / .qmc0 / .qmc2 / .qmc3 / .qmc4 / .qmc6 / .qmc8)
  - including both the "QQMusic EncV2,Key:" two-layer TEA and the
    single-layer V1 EKey forms, and both Map (short-key) and RC4
    (long-key) stream ciphers
* Android QTag (embedded EKey) decrypts offline;
  Android STag / PC MusicEx (no embedded EKey) need a key fetched from the
  server - the tool tells you and supports --ekey for manual keys

Algorithms are ported 1:1 from the official unlock-music Rust
implementation (lib_um_crypto_rust); unit-test vectors are included - check
with --self-test.

For learning purposes only - delete downloaded content within 24 hours.
"""

import argparse
import base64
import math
import os
import sqlite3
import struct
import sys
from pathlib import Path

# ---------------------------------------------------------------------------
# 通用常量
# ---------------------------------------------------------------------------

V1_KEY_SIZE = 128
V1_OFFSET_BOUNDARY = 0x7FFF  # 32767

# 对应 Rust: um_crypto/qmc/src/v1/mod.rs 中的 V1_STATIC_KEY
V1_STATIC_KEY = bytes([
    0xc3, 0x4a, 0xd6, 0xca, 0x90, 0x67, 0xf7, 0x52, 0xd8, 0xa1, 0x66, 0x62, 0x9f, 0x5b, 0x09, 0x00,
    0xc3, 0x5e, 0x95, 0x23, 0x9f, 0x13, 0x11, 0x7e, 0xd8, 0x92, 0x3f, 0xbc, 0x90, 0xbb, 0x74, 0x0e,
    0xc3, 0x47, 0x74, 0x3d, 0x90, 0xaa, 0x3f, 0x51, 0xd8, 0xf4, 0x11, 0x84, 0x9f, 0xde, 0x95, 0x1d,
    0xc3, 0xc6, 0x09, 0xd5, 0x9f, 0xfa, 0x66, 0xf9, 0xd8, 0xf0, 0xf7, 0xa0, 0x90, 0xa1, 0xd6, 0xf3,
    0xc3, 0xf3, 0xd6, 0xa1, 0x90, 0xa0, 0xf7, 0xf0, 0xd8, 0xf9, 0x66, 0xfa, 0x9f, 0xd5, 0x09, 0xc6,
    0xc3, 0x1d, 0x95, 0xde, 0x9f, 0x84, 0x11, 0xf4, 0xd8, 0x51, 0x3f, 0xaa, 0x90, 0x3d, 0x74, 0x47,
    0xc3, 0x0e, 0x74, 0xbb, 0x90, 0xbc, 0x3f, 0x92, 0xd8, 0x7e, 0x11, 0x13, 0x9f, 0x23, 0x95, 0x5e,
    0xc3, 0x00, 0x09, 0x5b, 0x9f, 0x62, 0x66, 0xa1, 0xd8, 0x52, 0xf7, 0x67, 0x90, 0xca, 0xd6, 0x4a,
])

# 扩展名 -> 解码版本/默认输出格式（对应 web 前端 HandlerMap）
V2_EXTENSIONS = {
    'mgg', 'mgg0', 'mggl', 'mgg1', 'mflac', 'mflac0', 'mmp4',
    'qmcflac', 'qmcogg', 'qmc0', 'qmc2', 'qmc3', 'qmc4', 'qmc6', 'qmc8',
}
V1_EXTENSIONS = {
    'bkcmp3', 'bkcm4a', 'bkcflac', 'bkcwav', 'bkcape', 'bkcogg', 'bkcwma',
    'tkm', '666c6163', '6d7033', '6f6767', '6d3461', '776176',
}
DEFAULT_EXT_MAP = {
    'mgg': 'ogg', 'mgg0': 'ogg', 'mggl': 'ogg', 'mgg1': 'ogg',
    'mflac': 'flac', 'mflac0': 'flac', 'mmp4': 'mp4',
    'qmcflac': 'flac', 'qmcogg': 'ogg', 'qmc0': 'mp3', 'qmc2': 'ogg',
    'qmc3': 'mp3', 'qmc4': 'ogg', 'qmc6': 'ogg', 'qmc8': 'ogg',
    'bkcmp3': 'mp3', 'bkcm4a': 'm4a', 'bkcflac': 'flac', 'bkcwav': 'wav',
    'bkcape': 'ape', 'bkcogg': 'ogg', 'bkcwma': 'wma', 'tkm': 'm4a',
    '666c6163': 'flac', '6d7033': 'mp3', '6f6767': 'ogg',
    '6d3461': 'm4a', '776176': 'wav',
}
ALL_EXTENSIONS = V2_EXTENSIONS | V1_EXTENSIONS

# ---------------------------------------------------------------------------
# QMC V1 静态密钥变换
# ---------------------------------------------------------------------------

def qmc1_transform(key, value, offset):
    """对应 Rust qmc1_transform：offset 超过 0x7FFF 时先对 0x7FFF 取模。"""
    if offset > V1_OFFSET_BOUNDARY:
        offset %= V1_OFFSET_BOUNDARY
    return value ^ key[offset % V1_KEY_SIZE]


def _xor_stream(chunk, key_stream):
    """用 int 大整数异或实现快速 bulk XOR（二者等长）。"""
    a = int.from_bytes(chunk, 'little')
    b = int.from_bytes(key_stream, 'little')
    return (a ^ b).to_bytes(len(chunk), 'little')


def _transform(data, offset_start, key):
    """按 qmc1_transform 对整段数据做变换（原地）。offset_start 为绝对偏移。

    注意 Rust 的边界语义：offset <= 0x7FFF 时保留原值，offset > 0x7FFF 时先
    对 0x7FFF 取模。因此字节 0x7FFF 的 key 索引是 127，而字节 0x8000 是 1。
    """
    out = bytearray(data)
    pos = offset_start
    i = 0
    total = len(out)
    while i < total:
        if pos <= V1_OFFSET_BOUNDARY:
            # [0, 0x7FFF]：key 索引 = pos % 128（含 0x7FFF -> 127）
            take = min(0x8000 - pos, total - i)
            phase = pos % V1_KEY_SIZE
        else:
            # (0x7FFF, ...)：key 索引 = (pos % 0x7FFF) % 128，按 0x7FFF 周期连续递增
            r = pos % V1_OFFSET_BOUNDARY
            take = min(V1_OFFSET_BOUNDARY - r, total - i)
            phase = r % V1_KEY_SIZE
        if phase == 0:
            base = key
        else:
            base = key[phase:] + key[:phase]
        key_stream = (base * ((take + V1_KEY_SIZE - 1) // V1_KEY_SIZE))[:take]
        out[i:i + take] = _xor_stream(bytes(out[i:i + take]), key_stream)
        i += take
        pos += take
    return bytes(out)


def v1_decrypt(data):
    """整文件静态密钥解密（offset 从 0 开始）。"""
    return _transform(data, 0, V1_STATIC_KEY)


# ---------------------------------------------------------------------------
# Tencent TEA (tc_tea) —— 对应 Rust tc_tea crate
# ---------------------------------------------------------------------------

TEA_DELTA = 0x9E3779B9
TEA_ROUNDS = 16
TEA_SALT_LEN = 2
TEA_ZERO_LEN = 7


def _tea_ecb_decrypt(block, k):
    """16 轮标准 TEA 解密一个 64 位块。k 为 4 个大端 u32。"""
    y = (block >> 32) & 0xFFFFFFFF
    z = block & 0xFFFFFFFF
    s = (TEA_DELTA * TEA_ROUNDS) & 0xFFFFFFFF
    for _ in range(TEA_ROUNDS):
        z = (z - _tea_single_round(y, s, k[2], k[3])) & 0xFFFFFFFF
        y = (y - _tea_single_round(z, s, k[0], k[1])) & 0xFFFFFFFF
        s = (s - TEA_DELTA) & 0xFFFFFFFF
    return (y << 32) | z


def _tea_single_round(value, s, key1, key2):
    left = ((value << 4) & 0xFFFFFFFF) + key1
    right = (value >> 5) + key2
    mid = (s + value) & 0xFFFFFFFF
    return (left ^ mid ^ right) & 0xFFFFFFFF


def tea_cbc_decrypt(ciphertext, key16):
    """tc_tea 的“tweaked CBC”解密。返回去除 padding 后的明文。"""
    k = struct.unpack('>IIII', key16)
    if len(ciphertext) % 8 != 0 or len(ciphertext) < 10:
        raise ValueError('TEA: invalid cipher length %d' % len(ciphertext))
    iv1 = 0
    iv2 = 0
    out = bytearray()
    for i in range(0, len(ciphertext), 8):
        block = int.from_bytes(ciphertext[i:i + 8], 'big')
        result = (block ^ iv2) & 0xFFFFFFFFFFFFFFFF
        next_iv2 = _tea_ecb_decrypt(result, k)
        p = (next_iv2 ^ iv1) & 0xFFFFFFFFFFFFFFFF
        out += p.to_bytes(8, 'big')
        iv1 = block
        iv2 = next_iv2
    pad_size = out[0] & 0b111
    start = 1 + pad_size + TEA_SALT_LEN
    end = len(ciphertext) - TEA_ZERO_LEN
    if any(out[end:]):
        raise ValueError('TEA: invalid padding')
    return bytes(out[start:end])


def tea_cbc_encrypt(plaintext, key16, salt):
    """tc_tea 的加密（对应 cbc.rs encrypt；构造测试 EKey 用）。salt 为 10 字节。"""
    k = struct.unpack('>IIII', key16)
    out_len = 10 + len(plaintext)
    pad_len = (8 - (out_len & 7)) & 7
    header_len = 1 + pad_len + TEA_SALT_LEN
    out_len += pad_len

    header = bytearray(16)
    header[:header_len] = salt[:header_len]
    header[0] = (header[0] & ~7) | pad_len
    copy_len = min(16 - header_len, len(plaintext))
    header[header_len:header_len + copy_len] = plaintext[:copy_len]
    rest = plaintext[copy_len:]

    iv1 = 0
    iv2 = 0
    out = bytearray(out_len)

    def round_enc(block):
        nonlocal iv1, iv2
        b = int.from_bytes(block, 'big')
        iv2_next = (b ^ iv1) & 0xFFFFFFFFFFFFFFFF
        c = (_tea_ecb_encrypt(iv2_next, k) ^ iv2) & 0xFFFFFFFFFFFFFFFF
        iv1 = c
        iv2 = iv2_next
        return c.to_bytes(8, 'big')

    out[0:8] = round_enc(header[0:8])
    out[8:16] = round_enc(header[8:16])
    pos = 16
    while len(rest) >= 8:
        out[pos:pos + 8] = round_enc(rest[:8])
        rest = rest[8:]
        pos += 8
    if rest:
        out[pos:pos + 8] = round_enc(rest + b'\x00' * (8 - len(rest)))
    return bytes(out[:out_len])


def _tea_ecb_encrypt(block, k):
    y = (block >> 32) & 0xFFFFFFFF
    z = block & 0xFFFFFFFF
    s = 0
    for _ in range(TEA_ROUNDS):
        s = (s + TEA_DELTA) & 0xFFFFFFFF
        y = (y + _tea_single_round(z, s, k[0], k[1])) & 0xFFFFFFFF
        z = (z + _tea_single_round(y, s, k[2], k[3])) & 0xFFFFFFFF
    return (y << 32) | z


# ---------------------------------------------------------------------------
# EKey 解密（对应 Rust ekey.rs）
# ---------------------------------------------------------------------------

# base64("QQMusic EncV2,Key:")
EKEY_V2_PREFIX = b'UVFNdXNpYyBFbmNWMixLZXk6'
EKEY_V2_KEY1 = bytes([0x33, 0x38, 0x36, 0x5A, 0x4A, 0x59, 0x21, 0x40,
                      0x23, 0x2A, 0x24, 0x25, 0x5E, 0x26, 0x29, 0x28])
EKEY_V2_KEY2 = bytes([0x2A, 0x2A, 0x23, 0x21, 0x28, 0x23, 0x24, 0x25,
                      0x26, 0x5E, 0x61, 0x31, 0x63, 0x5A, 0x2C, 0x54])


def _f32(x):
    return struct.unpack('f', struct.pack('f', x))[0]


def make_simple_key():
    """对应 Rust ekey.rs 中 make_simple_key::<8>()，严格 f32 语义。"""
    f01 = _f32(0.1)
    result = bytearray()
    for i in range(8):
        v = _f32(106.0 + _f32(i * f01))
        t = abs(math.tan(v))
        v = _f32(t)
        v = _f32(v * 100.0)
        # Rust: f32 `as u8` 为饱和转换
        result.append(max(0, min(int(v), 255)))
    return bytes(result)


EKEY_SIMPLE_KEY = make_simple_key()


def _is_b64_chr(c):
    return (c in b'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/=')


def _ekey_decrypt_v1(ekey):
    """V1 EKey：base64 -> header(8) + cipher，TEA 密钥 = 交错(simple_key, header)。"""
    ekey = ekey.encode('latin-1') if isinstance(ekey, str) else bytes(ekey)
    if len(ekey) < 12:
        raise ValueError('EKey 太短，无法解密')
    decoded = base64.b64decode(ekey)
    if len(decoded) < 8:
        raise ValueError('EKey base64 解码后不足 8 字节')
    header, cipher = decoded[:8], decoded[8:]
    tea_key = bytearray()
    for sk, hk in zip(EKEY_SIMPLE_KEY, header):
        tea_key.append(sk)
        tea_key.append(hk)
    plain = tea_cbc_decrypt(cipher, bytes(tea_key))
    return header + plain


def _ekey_decrypt_v2(ekey):
    """V2 EKey：双层 TEA(KEY1, KEY2) 解密后去 0，再走 V1。"""
    ekey = ekey.encode('latin-1') if isinstance(ekey, str) else bytes(ekey)
    payload = base64.b64decode(ekey)
    payload = tea_cbc_decrypt(payload, EKEY_V2_KEY1)
    payload = tea_cbc_decrypt(payload, EKEY_V2_KEY2)
    # 从头开始直到第一个 0 字节
    first_zero = payload.find(b'\x00')
    if first_zero != -1:
        payload = payload[:first_zero]
    return _ekey_decrypt_v1(payload)


def ekey_decrypt(ekey):
    """解密 EKey 得到主密钥（master key）。ekey 为文件中读出的字符串。"""
    ekey = ekey.encode('latin-1') if isinstance(ekey, str) else bytes(ekey)
    if ekey.startswith(EKEY_V2_PREFIX):
        return _ekey_decrypt_v2(ekey[len(EKEY_V2_PREFIX):])
    return _ekey_decrypt_v1(ekey)


# ---------------------------------------------------------------------------
# QMC V2 流密码
# ---------------------------------------------------------------------------

def key_compress(long_key):
    """对应 Rust v2_map/key.rs 的 key_compress：128 字节长 -> 压缩到 128 字节。"""
    long_key = bytes(long_key)
    n = len(long_key)
    if n == 0:
        raise ValueError('Map 密钥为空')
    result = bytearray()
    for i in range(V1_KEY_SIZE):
        idx = (i * i + 71214) % n
        key = long_key[idx]
        shift = (idx + 4) % 8
        result.append(((key << shift) | (key >> shift)) & 0xFF)
    return bytes(result)


class QMC2Map:
    """短密钥（1..300 字节）的 Map 密码：压缩后按 V1 变换异或。"""

    def __init__(self, key):
        self.key = key_compress(key)

    def decrypt(self, data, offset=0):
        return _transform(data, offset, self.key)


def qmc2_hash(key):
    """对应 Rust v2_rc4/hash.rs 的 hash()。"""
    h = 1
    for v in key:
        if v == 0:
            continue
        nxt = (h * v) & 0xFFFFFFFF
        if nxt == 0 or nxt <= h:
            break
        h = nxt
    return float(h)


def get_segment_key(id_, seed, h):
    """对应 Rust v2_rc4/segment_key.rs 的 get_segment_key。"""
    if seed == 0:
        return 0
    denom = ((id_ + 1) * seed) & 0xFFFFFFFFFFFFFFFF
    return int(h / float(denom) * 100.0)


class _RC4:
    """Modified RC4：状态长度 = 密钥长度（非 256），且状态为 u8（i as u8 会模 256）。"""

    def __init__(self, key):
        n = len(key)
        state = [i & 0xFF for i in range(n)]
        j = 0
        for i in range(n):
            j = (j + state[i] + key[i % n]) % n
            state[i], state[j] = state[j], state[i]
        self.state = state
        self.i = 0
        self.j = 0
        self.n = n

    def generate(self):
        n = self.n
        self.i = (self.i + 1) % n
        self.j = (self.j + self.state[self.i]) % n
        self.state[self.i], self.state[self.j] = self.state[self.j], self.state[self.i]
        idx = (self.state[self.i] + self.state[self.j]) % n
        return self.state[idx]


RC4_FIRST_SEGMENT_SIZE = 0x0080
RC4_OTHER_SEGMENT_SIZE = 0x1400
RC4_STREAM_CACHE_SIZE = RC4_OTHER_SEGMENT_SIZE + 512  # 0x1600


class QMC2RC4:
    """长密钥（>300 字节）的 RC4 密码。"""

    def __init__(self, key):
        key = bytes(key)
        rc4 = _RC4(key)
        self.hash = qmc2_hash(key)
        self.key = key
        self.key_stream = bytes(rc4.generate() for _ in range(RC4_STREAM_CACHE_SIZE))

    def _process_first_segment(self, buf, start, length, offset):
        n = len(self.key)
        for j in range(length):
            o = offset + j
            idx = get_segment_key(o, self.key[o % n], self.hash) % n
            buf[start + j] ^= self.key[idx]

    def _process_other_segment(self, buf, start, length, offset):
        n = len(self.key)
        seg_id = offset // RC4_OTHER_SEGMENT_SIZE
        block_off = offset % RC4_OTHER_SEGMENT_SIZE
        seed = self.key[seg_id % n]
        skip = get_segment_key(seg_id, seed, self.hash) & 0x1FF
        # key_stream 中该段对应的 key 是连续切片，用批量 int-XOR 加速
        ks = self.key_stream
        chunk = bytes(buf[start:start + length])
        buf[start:start + length] = _xor_stream(chunk, ks[skip + block_off:skip + block_off + length])

    def decrypt(self, data, offset=0):
        out = bytearray(data)
        n = len(out)
        pos = offset
        start = 0
        if pos < RC4_FIRST_SEGMENT_SIZE:
            take = min(RC4_FIRST_SEGMENT_SIZE - pos, n - start)
            self._process_first_segment(out, start, take, pos)
            start += take
            pos += take
        if pos % RC4_OTHER_SEGMENT_SIZE != 0:
            take = min(RC4_OTHER_SEGMENT_SIZE - (pos % RC4_OTHER_SEGMENT_SIZE), n - start)
            self._process_other_segment(out, start, take, pos)
            start += take
            pos += take
        while start < n:
            take = min(RC4_OTHER_SEGMENT_SIZE, n - start)
            self._process_other_segment(out, start, take, pos)
            start += take
            pos += take
        return bytes(out)


def make_qmc2_cipher(master_key):
    """按主密钥长度选择密码。对应 Rust QMCv2Cipher::new。"""
    master_key = bytes(master_key)
    if len(master_key) == 0:
        raise ValueError('主密钥为空')
    if 1 <= len(master_key) <= 300:
        return QMC2Map(master_key)
    return QMC2RC4(master_key)


# ---------------------------------------------------------------------------
# Footer 解析（对应 Rust qmc/src/footer/）
# ---------------------------------------------------------------------------

class FooterMetadata:
    def __init__(self, size, ekey, ftype, **extra):
        self.size = size       # 应从文件末尾裁剪的字节数
        self.ekey = ekey       # 内嵌 EKey 字符串（可能为 None）
        self.ftype = ftype     # STag / QTag / PcV2MusicEx / PcV1Legacy
        self.extra = extra     # media_mid / media_filename 等附加信息

    def __repr__(self):
        return 'FooterMetadata(size=%d, type=%s, ekey=%r, extra=%r)' % (
            self.size, self.ftype, self.ekey, self.extra)


def _is_base64_text(s):
    return all(c in b'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/=' for c in s)


class FooterParseError(Exception):
    pass


def parse_footer(tail):
    """解析文件末尾片段（建议取最后 1024 字节）。返回 FooterMetadata 或 None。

    按 Rust from_byte_slice 的顺序：STag -> QTag -> PcV2MusicEx -> PcV1Legacy。
    """
    if len(tail) < 8:
        return None

    # ---- 1. Android STag（无 EKey，仅元数据）----
    if tail.endswith(b'STag'):
        footer = tail[:-4]
        payload, size_bytes = footer[:-4], footer[-4:]
        payload_len = int.from_bytes(size_bytes, 'big')
        if len(payload) < payload_len:
            raise FooterParseError('STag 长度不一致')
        csv = payload[len(payload) - payload_len:].decode('utf-8', 'replace')
        parts = csv.split(',')
        if len(parts) != 3:
            raise FooterParseError('STag CSV 格式错误')
        rid, ver, media_mid = parts
        if ver != '2':
            raise FooterParseError('STag 版本不支持: %s' % ver)
        if not rid.isdigit():
            raise FooterParseError('STag ID 非法: %s' % rid)
        return FooterMetadata(payload_len + 8, None, 'STag',
                              resource_id=int(rid), media_mid=media_mid)

    # ---- 2. Android QTag（含 EKey）----
    if tail.endswith(b'QTag'):
        footer = tail[:-4]
        payload, size_bytes = footer[:-4], footer[-4:]
        payload_len = int.from_bytes(size_bytes, 'big')
        if len(payload) < payload_len:
            raise FooterParseError('QTag 长度不一致')
        csv = payload[len(payload) - payload_len:].decode('utf-8', 'replace')
        parts = csv.split(',')
        if len(parts) != 3:
            raise FooterParseError('QTag CSV 格式错误')
        ekey, rid, ver = parts
        if ver != '2':
            raise FooterParseError('QTag 版本不支持: %s' % ver)
        if not rid.isdigit():
            raise FooterParseError('QTag ID 非法: %s' % rid)
        if not _is_base64_text(ekey.encode('latin-1')):
            raise FooterParseError('QTag EKey 非法')
        return FooterMetadata(payload_len + 8, ekey, 'QTag',
                              resource_id=int(rid))

    # ---- 3. PC MusicEx（无 EKey，元数据 + 在线取 key 依据）----
    if tail.endswith(b'musicex\x00'):
        payload = tail[:-8]
        if len(payload) < 4:
            raise FooterParseError('MusicEx 过短')
        data, version_bytes = payload[:-4], payload[-4:]
        version = int.from_bytes(version_bytes, 'little')
        if version != 1:
            raise FooterParseError('MusicEx 版本不支持: %d' % version)
        if len(data) < 4:
            raise FooterParseError('MusicEx 过短')
        payload2, payload_len_bytes = data[:-4], data[-4:]
        payload_len = int.from_bytes(payload_len_bytes, 'little')
        if payload_len != 0xC0:
            raise FooterParseError('MusicEx 长度非法: 0x%X' % payload_len)
        inner = payload2[len(payload2) - (payload_len - 0x10):]
        # 结构：3*u32 未知，mid[60]，media_filename[100]，u32 未知
        # 均为 UTF-16LE（ASCII 范围内）
        mid = _read_utf16le(inner[12:12 + 60])
        media_filename = _read_utf16le(inner[12 + 60:12 + 60 + 100])
        return FooterMetadata(payload_len, None, 'PcV2MusicEx',
                              mid=mid, media_filename=media_filename)

    # ---- 4. PC V1 Legacy（含 EKey，经典 .mflac/.mgg）----
    payload, size_bytes = tail[:-4], tail[-4:]
    payload_len = int.from_bytes(size_bytes, 'little')
    if payload_len > 0x500:  # MAX_ALLOWED_EKEY_LEN
        return None  # 可能是非 QMC 文件
    if len(payload) < payload_len:
        raise FooterParseError('PCv1 长度不一致')
    ekey_bytes = payload[len(payload) - payload_len:]
    zero = ekey_bytes.find(b'\x00')
    if zero != -1:
        ekey_bytes = ekey_bytes[:zero]
    ekey = ekey_bytes.decode('latin-1')
    if not _is_base64_text(ekey_bytes):
        raise FooterParseError('PCv1 EKey 非法')
    return FooterMetadata(payload_len + 4, ekey, 'PcV1Legacy')


def _read_utf16le(data):
    out = []
    for i in range(0, len(data) - 1, 2):
        if data[i] == 0 and data[i + 1] == 0:
            break
        if data[i + 1] == 0 and 0 < data[i] < 128:
            out.append(chr(data[i]))
        else:
            break
    return ''.join(out)


# ---------------------------------------------------------------------------
# 本地密钥数据库查询（QQ 音乐安卓端 player_process_db）
# ---------------------------------------------------------------------------

def _db_candidates(*names):
    """把可能的文件名/媒体名整理成匹配候选（含去除目录后的文件名）。"""
    out = []
    for n in names:
        if not n:
            continue
        n = str(n).strip()
        base = Path(n).name
        if n not in out:
            out.append(n)
        if base not in out:
            out.append(base)
    return out


def _all_ekeys_from_db(db_path):
    """读取密钥库中全部 (文件名, ekey) 条目。

    支持三种表结构：
      * audio_file_ekey_table(file_path, ekey)  —— 安卓下载文件
      * EKeyFileInfo(filePath, eKey)            —— 旧版安卓
      * p2p_cache_info_table(file_id, ekey)     —— PC 媒体文件名(AIM...)的 P2P 缓存密钥
    """
    conn = sqlite3.connect('file:%s?mode=ro' % db_path, uri=True)
    try:
        cur = conn.cursor()
        tables = [r[0] for r in cur.execute(
            "SELECT name FROM sqlite_master WHERE type='table'")]
        out = []
        for table, path_col, key_col in (
            ('audio_file_ekey_table', 'file_path', 'ekey'),
            ('EKeyFileInfo', 'filePath', 'eKey'),
            ('p2p_cache_info_table', 'file_id', 'ekey'),
        ):
            if table not in tables:
                continue
            rows = cur.execute('SELECT %s, %s FROM %s' % (path_col, key_col, table)).fetchall()
            for path, ekey in rows:
                if path is None or ekey is None:
                    continue
                out.append((str(Path(str(path)).name), str(ekey).strip()))
        return out
    finally:
        conn.close()


def lookup_ekey_from_db(db_path, *names):
    """在 QQ 音乐安卓端密钥数据库（SQLite）中按文件名查找 EKey。

    支持官方实现中的两种表结构：
      * audio_file_ekey_table(file_path, ekey)
      * EKeyFileInfo(filePath, eKey)
    返回匹配到的 ekey 字符串；未找到返回 None。
    """
    candidates = _db_candidates(*names)
    if not candidates:
        return None
    try:
        conn = sqlite3.connect('file:%s?mode=ro' % db_path, uri=True)
    except (sqlite3.Error, OSError):
        return None
    try:
        cur = conn.cursor()
        tables = [r[0] for r in cur.execute(
            "SELECT name FROM sqlite_master WHERE type='table'")]
        for table, path_col, key_col in (
            ('audio_file_ekey_table', 'file_path', 'ekey'),
            ('EKeyFileInfo', 'filePath', 'eKey'),
            ('p2p_cache_info_table', 'file_id', 'ekey'),
        ):
            if table not in tables:
                continue
            rows = cur.execute('SELECT %s, %s FROM %s' % (path_col, key_col, table)).fetchall()
            for path, ekey in rows:
                if path is None or ekey is None:
                    continue
                path_s = str(path)
                base = Path(path_s).name
                if base in candidates:
                    return str(ekey).strip()
                # 子串匹配：media_mid 等标识出现在 file_id/file_path 中（如 AIM0000<mid>.mflac）
                for cand in candidates:
                    if len(cand) >= 8 and cand in path_s:
                        return str(ekey).strip()
    finally:
        conn.close()
    return None


# ---------------------------------------------------------------------------
# 音频类型嗅探（对应 um_audio）
# ---------------------------------------------------------------------------

MAGIC_MAP = {
    b'fLaC': 'flac',
    b'OggS': 'ogg',
    b'FRM8': 'dff',
    b'\x30\x26\xB2\x75': 'wma',
    b'RIFF': 'wav',
    b'MAC ': 'ape',
    b'\x1A\x45\xDF\xA3': 'mka',
}


class NeedMoreHeader(Exception):
    def __init__(self, needed):
        super().__init__('需要更多头部字节: %d' % needed)
        self.needed = needed


def _syncsafe_int(b4):
    if any(c & 0x80 for c in b4):
        return 0
    return (b4[0] << 21) | (b4[1] << 14) | (b4[2] << 7) | b4[3]


def _id3_or_ape_size(buf, offset):
    if len(buf) < offset + 10:
        raise NeedMoreHeader(offset + 10)
    b = buf[offset:]
    if b.startswith(b'TAG'):
        return 128
    if b.startswith(b'ID3'):
        return 10 + _syncsafe_int(b[6:10])
    if b.startswith(b'APETAGEX'):
        if len(b) < 32:
            raise NeedMoreHeader(offset + 32)
        extra = int.from_bytes(b[0x0C:0x10], 'little')
        return 32 + extra
    return 0


def get_header_metadata_size(buf, offset=0):
    for _ in range(5):
        ln = _id3_or_ape_size(buf, offset)
        if ln == 0:
            break
        offset += ln
    return offset


def _is_aac(magic):
    return (magic & 0xFFF60000) == 0xFFF00000


def _mp3_bitrate_kbps(version, layer, idx):
    if idx in (0, 15) or layer == 0:
        return None
    if version == 0b11:
        table = {
            0b11: [32, 64, 96, 128, 160, 192, 224, 256, 288, 320, 352, 384, 416, 448],
            0b10: [32, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384],
            0b01: [32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320],
        }.get(layer)
    else:
        table = {
            0b11: [32, 48, 56, 64, 80, 96, 112, 128, 144, 160, 176, 192, 224, 256],
        }.get(layer) or [8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160]
    if table is None:
        return None
    return table[idx - 1]


def _mp3_sample_rate_hz(version, idx):
    table = {
        0b11: [44100, 48000, 32000],
        0b10: [22050, 24000, 16000],
        0b00: [11025, 12000, 8000],
    }
    return table.get(version, [None, None, None])[idx]


def _parse_mp3_header(h):
    sync = (h >> 21) & 0x7FF
    if sync != 0x7FF:
        return None
    version = (h >> 19) & 0b11
    if version == 0b01:
        return None
    layer = (h >> 17) & 0b11
    if layer == 0b00:
        return None
    bitrate_idx = (h >> 12) & 0b1111
    if bitrate_idx in (0, 15):
        return None
    sampling_idx = (h >> 10) & 0b11
    if sampling_idx == 0b11:
        return None
    padding = (h >> 9) & 1
    bitrate_kbps = _mp3_bitrate_kbps(version, layer, bitrate_idx)
    sample_rate = _mp3_sample_rate_hz(version, sampling_idx)
    if bitrate_kbps is None or sample_rate is None:
        return None
    bitrate = bitrate_kbps * 1000  # 帧长公式用 bps（Rust: bitrate_kbps(...) * 1000）
    if layer == 0b11:
        return ((12 * bitrate // sample_rate) + padding) * 4
    if version == 0b11:
        return (144 * bitrate // sample_rate) + padding
    return (72 * bitrate // sample_rate) + padding


def _scan_for_mp3(buf):
    n = len(buf)
    if n < 4:
        return 0
    cache = [0] * n
    for i in range(n - 4):
        h = int.from_bytes(buf[i:i + 4], 'big')
        fs = _parse_mp3_header(h)
        if fs is not None:
            cache[i] = i + fs
    result = 0
    for i in range(n - 4):
        cnt = 0
        j = i
        while j < n and cache[j] != 0:
            cnt += 1
            j = cache[j]
        result = max(result, cnt)
    return result


def detect_audio_type(data):
    """返回音频扩展名（'bin' 表示未知）。需要足够数据；不足抛 NeedMoreHeader。"""
    offset = get_header_metadata_size(data, 0)
    if len(data) < offset + 0x10:
        raise NeedMoreHeader(offset + 0x10)
    buf = data[offset:]
    magic4 = buf[:4]
    if magic4 in MAGIC_MAP:
        return MAGIC_MAP[magic4]
    magic = int.from_bytes(magic4, 'big')
    if _is_aac(magic):
        return 'aac'
    if _scan_for_mp3(buf) >= 3:
        return 'mp3'
    if len(buf) >= 8 and buf[4:8] == b'ftyp':
        major = buf[8:12]
        if major in (b'isom', b'iso2', b'MSNV'):
            return 'mp4'
        if major == b'NDAS':
            return 'm4a'
        if len(buf) >= 11:
            major3 = buf[8:11]
            if major3 == b'M4A':
                return 'm4a'
            if major3 == b'M4B':
                return 'm4b'
            if major3 == b'mp4':
                return 'mp4'
    if len(data) < 4096:
        raise NeedMoreHeader(4096)
    return 'bin'


def detect_audio_extension(data):
    """带重试的检测：按需增长头部长度，直到得到结论。"""
    needed = 0x100
    ext = 'bin'
    while needed != 0:
        try:
            ext = detect_audio_type(data[:needed])
            needed = 0
        except NeedMoreHeader as e:
            if len(data) >= e.needed:
                needed = e.needed
            else:
                needed = 0
                ext = 'bin'
    return ext


def looks_like_audio(data):
    if len(data) < 0x20:
        return False
    try:
        res = detect_audio_type(data[:0x20])
        return res != 'bin'
    except NeedMoreHeader:
        # ID3 等有效头会请求更多数据，视为音频
        return True


# ---------------------------------------------------------------------------
# 解密主流程
# ---------------------------------------------------------------------------

class DecryptError(Exception):
    pass


def _try_all_ekeys(db_path, data_region):
    """用密钥库中全部 EKey 依次试解数据区头部，返回 (key名称, ekey) 或 None。

    密钥按歌曲 MID 绑定、与文件名无关，因此不同客户端的同名歌曲密钥可通用。
    """
    sample = data_region[:0x4000]
    for name, ekey in _all_ekeys_from_db(db_path):
        try:
            master = ekey_decrypt(ekey)
            cipher = make_qmc2_cipher(master)
            out = cipher.decrypt(sample, 0)
            if detect_audio_extension(out) != 'bin':
                return name, ekey
        except Exception:
            continue
    return None


def _frida_fallback(input_path, ext_hint, log, work_dir):
    """Frida 借壳解密整文件（可选兜底，思路来源见 frida_mode/hook_qq_music.js 署名）。

    返回 (明文bytes, 扩展名)。任何前置条件不满足或解密失败都抛 DecryptError。
    """
    try:
        import frida_mode
    except ImportError:
        raise DecryptError('Frida 兜底不可用：未找到 frida_mode 模块（仓库结构不完整）')
    if not frida_mode.available():
        raise DecryptError('Frida 兜底不可用：未安装 frida（pip install frida）')
    if not frida_mode.client_running():
        raise DecryptError('Frida 兜底不可用：未检测到 QQMusic.exe 进程（请先启动 QQ 音乐客户端并登录）')
    os.makedirs(work_dir, exist_ok=True)
    tmp = str(Path(work_dir) / ('_frida_tmp_%d' % os.getpid()))
    try:
        frida_mode.decrypt_file(str(input_path), tmp)
        with open(tmp, 'rb') as tf:
            out = tf.read()
        ext = detect_audio_extension(out)
        if ext == 'bin':
            ext = _fallback_ext(ext_hint)
        log.append('Frida 借壳解密成功（%d 字节）' % len(out))
        return out, ext
    except DecryptError:
        raise
    except Exception as e:
        raise DecryptError('Frida 借壳解密失败: %s' % e)
    finally:
        try:
            os.remove(tmp)
        except OSError:
            pass


def decrypt_qmc_file(data, ekey_override=None, ext_hint=None,
                     key_db=None, input_name=None, try_all_keys=False,
                     frida_fallback=False, input_path=None):
    """解密单个文件数据。返回 (输出字节, 输出扩展名, 日志列表)。

    - 先尝试 v2 footer；有内嵌 EKey 或提供了 --ekey 则走 v2。
    - 无内嵌密钥时，若给定 --ekey-db（QQ 音乐安卓端密钥库），自动按文件名查 EKey；
      --try-all-keys 时还会拿库中全部密钥逐一试解。
    - 无 footer / 无法确定时回退 v1 静态密钥。
    - 仍无 EKey 且无法离线解密的（STag/MusicEx）抛出 DecryptError 并说明。
    """
    log = []
    tail = data[-1024:]
    footer = None
    try:
        footer = parse_footer(tail)
    except FooterParseError as e:
        log.append('footer 解析失败: %s' % e)
        footer = None

    if footer is not None:
        log.append('检测到 footer: %s (裁剪 %d 字节)' % (footer.ftype, footer.size))

    # --- v2 路径 ---
    if footer is not None:
        ekey = footer.ekey or ekey_override
        if ekey is None and key_db is not None:
            found = lookup_ekey_from_db(
                key_db, input_name,
                footer.extra.get('media_filename'),
                footer.extra.get('media_mid'))
            if found:
                log.append('已从密钥库按文件名找到 EKey')
                ekey = found
            elif try_all_keys:
                hit = _try_all_ekeys(key_db, data[:len(data) - footer.size])
                if hit:
                    log.append('逐一试解命中密钥: %s' % hit[0])
                    ekey = hit[1]
        if ekey is not None:
            master_key = ekey_decrypt(ekey)
            log.append('主密钥长度: %d 字节' % len(master_key))
            cipher = make_qmc2_cipher(master_key)
            audio = data[:len(data) - footer.size]
            out = cipher.decrypt(audio, 0)
            ext = detect_audio_extension(out)
            if ext == 'bin':
                ext = _fallback_ext(ext_hint)
                log.append('注意: 解密结果未能识别为音频格式（可能是旧 key 或文件已损坏）')
            return out, ext, log
        # footer 存在但无 EKey
        if ekey_override is not None:
            master_key = ekey_decrypt(ekey_override)
            cipher = make_qmc2_cipher(master_key)
            audio = data[:len(data) - footer.size]
            out = cipher.decrypt(audio, 0)
            ext = detect_audio_extension(out)
            if ext == 'bin':
                ext = _fallback_ext(ext_hint)
            return out, ext, log
        # 需要联网取 key
        info = _footer_key_hint(footer)
        # 回退尝试 v1（几乎必然失败，但保持与官方前端一致的行为）
        try:
            v1 = v1_decrypt(data)
            ext1 = detect_audio_extension(v1)
            if ext1 != 'bin':
                log.append('按 v1 静态密钥解密成功')
                return v1, ext1, log
        except Exception:
            pass
        # Frida 借壳兜底（--frida-fallback）：让 QQ 音乐客户端进程亲手解密
        if frida_fallback and input_path is not None:
            log.append('密钥不可用，尝试 Frida 借壳解密 ...')
            try:
                out, ext = _frida_fallback(
                    input_path, ext_hint, log,
                    os.path.dirname(os.path.abspath(str(input_path))))
                return out, ext, log
            except DecryptError as e:
                log.append(str(e))
        raise DecryptError(
            '该文件未内嵌解密密钥，需要在线向服务器获取。%s\n'
            '若你已单独获得密钥，可用 --ekey <key> 指定。\n'
            '提示：Windows 下可用 --frida-fallback 重试'
            '（需保持 QQ 音乐客户端运行，无需密钥库）。' % info)

    # --- v1 静态密钥路径 ---
    out = v1_decrypt(data)
    ext = detect_audio_extension(out)
    if ext == 'bin':
        raise DecryptError(
            '未能识别为受支持的 QMC 文件（静态密钥解密后不是可识别的音频数据）。')
    return out, ext, log


def decrypt_file_streaming(input_path, output_dir, key_db=None,
                           ekey_override=None, try_all_keys=False,
                           progress_cb=None, cancel=None,
                           chunk_size=4 * 1024 * 1024,
                           frida_fallback=False):
    """流式解密单个文件并写入 output_dir，供 GUI 使用。

    参数：
      input_path   : 加密文件路径
      output_dir   : 输出目录（已创建）
      key_db       : QQ 音乐密钥库（player_process_db）
      ekey_override: 手动指定 EKey
      try_all_keys : 文件名匹配不到时用库中全部密钥试解
      progress_cb  : 回调(done_bytes, total_bytes)，在解密块之间调用
      cancel       : threading.Event，置位则中止
      chunk_size   : 每块读取字节数
      frida_fallback: 无密钥时用 Frida 借壳解密兜底（Windows + 客户端运行）

    返回 (输出文件路径, 扩展名, 日志列表)。失败抛 DecryptError。
    """
    from pathlib import Path as _P
    log = []
    in_path = _P(input_path)
    total = in_path.stat().st_size

    with open(in_path, 'rb') as f:
        # ---- 读取尾部 footer ----
        tail_size = min(1024, total)
        f.seek(-tail_size, 2)
        tail = f.read(tail_size)
        footer = None
        try:
            footer = parse_footer(tail)
        except FooterParseError as e:
            log.append('footer 解析失败: %s' % e)
        if footer is not None:
            log.append('检测到 footer: %s (裁剪 %d 字节)' % (footer.ftype, footer.size))

        data_size = total - (footer.size if footer else 0)
        if data_size < 0:
            data_size = 0

        # ---- 读取头部样本 ----
        f.seek(0)
        head = f.read(0x4000)[:data_size]

        # ---- 确定解密方式 ----
        cipher = None
        v1_mode = False
        ext_hint = in_path.suffix.lstrip('.').lower()

        if footer is not None:
            ekey = footer.ekey or ekey_override
            if ekey is None and key_db is not None:
                found = lookup_ekey_from_db(
                    key_db, in_path.name,
                    footer.extra.get('media_filename'),
                    footer.extra.get('media_mid'))
                if found:
                    log.append('已从密钥库按文件名/MID 找到 EKey')
                    ekey = found
                elif try_all_keys:
                    hit = _try_all_ekeys(key_db, head)
                    if hit:
                        log.append('逐一试解命中密钥: %s' % hit[0])
                        ekey = hit[1]
            if ekey is not None:
                master = ekey_decrypt(ekey)
                log.append('主密钥长度: %d 字节' % len(master))
                cipher = make_qmc2_cipher(master)
            else:
                # 无密钥：尝试 v1 静态密钥
                v1_head = _transform(head, 0, V1_STATIC_KEY)
                if detect_audio_extension(v1_head) != 'bin':
                    log.append('按 v1 静态密钥解密')
                    v1_mode = True
                else:
                    hint = _footer_key_hint(footer)
                    if frida_fallback:
                        log.append('密钥不可用，尝试 Frida 借壳解密 ...')
                        try:
                            out, ext = _frida_fallback(
                                str(in_path), ext_hint, log, str(output_dir))
                            output_path = _P(output_dir) / _output_name(in_path.stem, ext)
                            n = 1
                            while output_path.exists():
                                output_path = output_path.with_name(
                                    '%s (%d)%s' % (output_path.stem, n, output_path.suffix))
                                n += 1
                            output_path.write_bytes(out)
                            log.append('输出: %s' % output_path.name)
                            return output_path, ext, log
                        except DecryptError as e:
                            log.append(str(e))
                    raise DecryptError(
                        '该文件未内嵌解密密钥，需要在线获取密钥。%s\n'
                        '请选择密钥库(含 p2p_cache_info_table 的 player_process_db)，'
                        '或勾选"试解全部密钥"，或用 --ekey 指定。\n'
                        '提示：Windows 下可勾选"Frida 借壳兜底"重试'
                        '（需保持 QQ 音乐客户端运行，无需密钥库）。' % hint)
        else:
            v1_head = _transform(head, 0, V1_STATIC_KEY)
            if detect_audio_extension(v1_head) == 'bin':
                raise DecryptError(
                    '未能识别为受支持的 QMC 文件（静态密钥解密后不是可识别的音频数据）。')
            log.append('v1 静态密钥模式')
            v1_mode = True

        # ---- 嗅探输出扩展名 ----
        probe_len = min(data_size, 0x10000)
        if v1_mode:
            ext = detect_audio_extension(_transform(head[:probe_len], 0, V1_STATIC_KEY))
        else:
            ext = detect_audio_extension(cipher.decrypt(head[:probe_len], 0))
        if ext == 'bin':
            ext = _fallback_ext(ext_hint)

        # ---- 流式解密写入（冲突时自动加序号，避免覆盖）----
        output_path = _P(output_dir) / _output_name(in_path.stem, ext)
        n = 1
        while output_path.exists():
            output_path = output_path.with_name(
                '%s (%d)%s' % (output_path.stem, n, output_path.suffix))
            n += 1
        if n > 1:
            log.append('输出文件已存在，改用: %s' % output_path.name)

        with open(output_path, 'wb') as out_f:
            offset = 0
            remaining = data_size
            while remaining > 0:
                if cancel is not None and cancel.is_set():
                    raise DecryptError('已取消')
                f.seek(offset)
                chunk = f.read(min(chunk_size, remaining))
                if not chunk:
                    break
                if v1_mode:
                    dec = _transform(chunk, offset, V1_STATIC_KEY)
                else:
                    dec = cipher.decrypt(chunk, offset)
                out_f.write(dec)
                offset += len(chunk)
                remaining -= len(chunk)
                if progress_cb:
                    progress_cb(offset, data_size)

        if progress_cb:
            progress_cb(data_size, data_size)
        return output_path, ext, log


def _fallback_ext(ext_hint):
    if ext_hint and ext_hint in DEFAULT_EXT_MAP:
        return DEFAULT_EXT_MAP[ext_hint]
    return 'bin'


def _footer_key_hint(footer):
    if footer.ftype == 'STag':
        return '媒体标识(media_mid): %s，资源 ID: %d' % (
            footer.extra.get('media_mid'), footer.extra.get('resource_id'))
    if footer.ftype == 'PcV2MusicEx':
        return '媒体文件名: %s，MID: %s' % (
            footer.extra.get('media_filename'), footer.extra.get('mid'))
    return ''


# ---------------------------------------------------------------------------
# 命令行
# ---------------------------------------------------------------------------

def _output_name(stem, ext):
    if ext == 'bin':
        return stem + '.bin'
    return stem + '.' + ext


def _process_one(input_path, output_dir, ekey_override, key_db, force, try_all_keys,
                 frida_fallback=False):
    data = input_path.read_bytes()
    ext_hint = input_path.suffix.lstrip('.').lower()
    out, ext, log = decrypt_qmc_file(data, ekey_override, ext_hint,
                                     key_db=key_db, input_name=input_path.name,
                                     try_all_keys=try_all_keys,
                                     frida_fallback=frida_fallback,
                                     input_path=input_path)
    output_path = output_dir / _output_name(input_path.stem, ext)
    if output_path.exists() and not force:
        raise DecryptError('输出文件已存在: %s（使用 --force 覆盖）' % output_path)
    output_path.write_bytes(out)
    return input_path, output_path, ext, log


def _list_ekey_db(db_path, filter_name=None):
    """列出密钥库中的文件名 -> EKey 条目（验证库是否有效 / 查找目标密钥）。"""
    fkey = filter_name.lower() if filter_name else None
    conn = sqlite3.connect('file:%s?mode=ro' % db_path, uri=True)
    try:
        cur = conn.cursor()
        tables = [r[0] for r in cur.execute(
            "SELECT name FROM sqlite_master WHERE type='table'")]
        found_any = False
        for table, path_col, key_col in (
            ('audio_file_ekey_table', 'file_path', 'ekey'),
            ('EKeyFileInfo', 'filePath', 'eKey'),
        ):
            if table not in tables:
                continue
            found_any = True
            rows = cur.execute('SELECT %s, %s FROM %s' % (path_col, key_col, table)).fetchall()
            if not fkey:
                print('[表] %s（%d 条）' % (table, len(rows)))
            for path, ekey in rows:
                if path is None or ekey is None:
                    continue
                name = Path(str(path)).name
                if fkey and fkey not in name.lower() and fkey not in str(path).lower():
                    continue
                ekey_s = str(ekey).strip()
                marker = '  <== 匹配' if fkey else ''
                print('  %s -> %s%s' % (name, ekey_s[:40] + ('…' if len(ekey_s) > 40 else ''), marker))
        if not found_any:
            print('[-] 未找到已知的密钥表（audio_file_ekey_table / EKeyFileInfo），'
                  '可能不是 QQ 音乐密钥库')
    finally:
        conn.close()


def main(argv=None):
    parser = argparse.ArgumentParser(
        description='QQMusic QMC 加密文件解密工具（纯 Python，无第三方依赖）')
    parser.add_argument('input', nargs='*', help='QMC 加密文件或目录')
    parser.add_argument('-o', '--output-dir', default=None,
                        help='输出目录（默认与输入同目录，目录输入时默认 ./decrypted）')
    parser.add_argument('-e', '--ekey', default=None,
                        help='手动指定 EKey 字符串（用于无内嵌密钥的文件）')
    parser.add_argument('--ekey-db', default=None, metavar='DB',
                        help='QQ 音乐安卓端密钥数据库 player_process_db，'
                             '自动按文件名查找 EKey')
    parser.add_argument('--try-all-keys', action='store_true',
                        help='配合 --ekey-db：按文件名找不到时，'
                             '用库中全部密钥逐一试解')
    parser.add_argument('--list-ekey-db', default=None, metavar='DB',
                        help='列出密钥库中的 文件名->EKey 条目后退出'
                             '（可配合 --find <名字> 过滤）')
    parser.add_argument('--find', default=None, metavar='NAME',
                        help='与 --list-ekey-db 配合，只显示含指定名字的条目')
    parser.add_argument('-f', '--force', action='store_true',
                        help='覆盖已存在的输出文件')
    parser.add_argument('--frida-fallback', action='store_true',
                        help='无密钥时用 Frida 借壳解密兜底'
                             '（Windows 需 QQ 音乐客户端运行中，思路来源'
                             ' decrypt-mflac-frida / music-decryptor，见 README 致谢）')
    parser.add_argument('-v', '--verbose', action='store_true',
                        help='输出详细日志')
    parser.add_argument('--self-test', action='store_true',
                        help='运行内置自检（Rust 单元测试向量）后退出')
    args = parser.parse_args(argv)

    if args.self_test:
        ok = run_self_test()
        sys.exit(0 if ok else 1)

    if args.list_ekey_db:
        _list_ekey_db(args.list_ekey_db, args.find)
        return 0

    inputs = []
    for p in args.input:
        path = Path(p)
        if path.is_dir():
            for f in sorted(path.rglob('*')):
                if f.is_file() and f.suffix.lstrip('.').lower() in ALL_EXTENSIONS:
                    inputs.append(f)
            if not inputs:
                print('[!] 目录中未找到任何 QMC 文件', file=sys.stderr)
        else:
            inputs.append(path)

    if not inputs:
        print('[-] 没有可处理的文件', file=sys.stderr)
        return 1

    output_dir = None
    if args.output_dir:
        output_dir = Path(args.output_dir)
        output_dir.mkdir(parents=True, exist_ok=True)

    ok_count = 0
    fail_count = 0
    for input_path in inputs:
        try:
            if output_dir is None:
                out_dir = input_path.parent / 'decrypted'
            else:
                out_dir = output_dir
            out_dir.mkdir(parents=True, exist_ok=True)
            in_p, out_p, ext, log = _process_one(
                input_path, out_dir, args.ekey, args.ekey_db, args.force,
                args.try_all_keys, args.frida_fallback)
            ok_count += 1
            print('[OK] %s -> %s  (%s)' % (in_p.name, out_p, ext))
            if args.verbose:
                for line in log:
                    print('     | ' + line)
        except DecryptError as e:
            fail_count += 1
            print('[!!] %s: %s' % (input_path.name, e))
        except Exception as e:
            fail_count += 1
            print('[!!] %s: 异常: %s' % (input_path.name, e))

    print('----')
    print('完成：成功 %d，失败 %d' % (ok_count, fail_count))
    return 0 if fail_count == 0 else 1


# ---------------------------------------------------------------------------
# 自检：内建 Rust 单元测试向量
# ---------------------------------------------------------------------------

def run_self_test():
    ok = True

    def check(name, got, expected):
        nonlocal ok
        status = 'PASS' if got == expected else 'FAIL'
        if status == 'FAIL':
            ok = False
            print('  [FAIL] %s\n    got:      %r\n    expected: %r' % (name, got, expected))
        else:
            print('  [PASS] %s' % name)

    # ---- v1 transform（Rust 测试使用 generate_key_128 = [1,2,...,128]）----
    gen_key = bytes(range(1, 129))
    d = bytearray(b'igohj&pg{fo')
    d = bytearray(qmc1_transform(gen_key, b, i) for i, b in enumerate(d))
    check('v1 transform start', bytes(d), b'hello world')

    d2 = bytearray([0x13, 0x19, 0x11, 0x12, 0x10, 0xa0, 0x75, 0x6c, 0x76, 0x69, 0x62])
    for i in range(len(d2)):
        d2[i] = qmc1_transform(gen_key, d2[i], 0x7FFA + i)
    check('v1 transform boundary', bytes(d2), b'hello world')

    v1 = v1_decrypt(bytes([0xab, 0x2f, 0xba, 0xa6, 0xff, 0x47, 0x80, 0x3d, 0xaa, 0xcd, 0x02]))
    check('v1 whole-file decrypt', v1, b'hello world')

    # ---- key_compress ----
    test_key = (b'abcdefghijklmnopqrstuvwxyz'
                b'ABCDEFGHIJKLMNOPQRSTUVWXYZ'
                b'0123456789') * 6
    test_key = (test_key * 10)[:325]
    expected_compress = bytes([
        0x79, 0xf4, 0x00, 0x75, 0x9e, 0x36, 0x00, 0x14, 0x8a, 0x63, 0x00, 0xb4, 0xbe, 0x77,
        0x00, 0x17, 0xba, 0x00, 0x37, 0x00, 0x00, 0x00, 0xbf, 0x80, 0x41, 0xbf, 0x83, 0xdd,
        0xbc, 0x5c, 0x02, 0x43, 0x14, 0x82, 0x49, 0x02, 0x00, 0x55, 0xbe, 0x6d, 0xbf, 0x49,
        0x80, 0x8e, 0x43, 0x00, 0xfa, 0x41, 0x67, 0xa8, 0x17, 0xf4, 0xae, 0x16, 0x15, 0x00,
        0xc1, 0x37, 0x82, 0xdd, 0x36, 0x21, 0x38, 0x55, 0x00, 0x79, 0x41, 0x9e, 0x42, 0xc1,
        0x36, 0xfa, 0xcf, 0x35, 0x00, 0x00, 0x41, 0xdd, 0x43, 0x42, 0x17, 0x4d, 0x8e, 0x8a,
        0xdd, 0x00, 0xbe, 0xf5, 0x38, 0xb4, 0xbf, 0x00, 0x7a, 0xcc, 0x4d, 0x02, 0x00, 0xcf,
        0xc1, 0xc1, 0x02, 0xa8, 0x00, 0x16, 0xc1, 0xbf, 0xc2, 0x42, 0x00, 0x49, 0x00, 0xc1,
        0xc2, 0xf5, 0x00, 0x17, 0x41, 0xdc, 0x83, 0xc2, 0x00, 0x9e, 0x41, 0xc1, 0x71, 0x36,
        0x00, 0x80,
    ])
    check('key_compress', key_compress(test_key), expected_compress)

    # ---- QMC2Map ----
    cipher = QMC2Map(test_key)
    ct = bytes([0x00, 0x9e, 0x41, 0xc1, 0x71, 0x36, 0x00, 0x80, 0xf4, 0x00, 0x75, 0x9e, 0x36, 0x00,
                0x14, 0x8a])
    check('QMC2Map decrypt @32760', cipher.decrypt(ct, 32760), b'\x00' * 16)

    # ---- hash ----
    check('qmc2_hash', qmc2_hash(b'hello world'), 4045008896.0)

    # ---- get_segment_key ----
    check('segment_key 0', get_segment_key(1, 0, 12345.0), 0)
    check('segment_key 123', get_segment_key(1, 123, 12345.0), 5018)
    check('segment_key large1', get_segment_key(51, 35, 516402887.0), 28373784)
    check('segment_key large2', get_segment_key(0, 66, 3908240000.0), 5921575757)

    # ---- RC4 ----
    rc4 = _RC4(b'this is a test key')
    out = bytes(v ^ rc4.generate() for v in b'hello world')
    check('RC4 derive', out, bytes([0x68, 0x75, 0x6b, 0x64, 0x64, 0x24, 0x7f, 0x60, 0x7c, 0x7d, 0x60]))

    # ---- QMC2RC4（首段 + 其他段）----
    rc4_key = (b'abcdefghijklmnopqrstuvwxyz'
               b'ABCDEFGHIJKLMNOPQRSTUVWXYZ'
               b'0123456789') * 9
    rc4_key = (rc4_key * 10)[:512]
    rc4_ct = bytes([
        0x39, 0x5a, 0x4f, 0x75, 0x38, 0x71, 0x37, 0x6b, 0x36, 0x51, 0x53, 0x6d, 0x7a, 0x66,
        0x53, 0x4b, 0x66, 0x50, 0x69, 0x34, 0x67, 0x6c, 0x33, 0x7a, 0x55, 0x62, 0x35, 0x5a,
        0x32, 0x75, 0x4f, 0x68, 0x44, 0x52, 0x6d, 0x65, 0x75, 0x6e, 0x39, 0x52, 0x30, 0x7a,
        0x68, 0x62, 0x73, 0x59, 0x39, 0x48, 0x55, 0x57, 0x73, 0x32, 0x5a, 0x70, 0x64, 0x50,
        0x4e, 0x52, 0x6a, 0x63, 0x4d, 0x39, 0x37, 0x76, 0x72, 0x47, 0x64, 0x4d, 0x62, 0x6d,
        0x58, 0x68, 0x75, 0x47, 0x37, 0x56, 0x69, 0x6b, 0x4a, 0x79, 0x66, 0x63, 0x70, 0x39,
        0x59, 0x34, 0x43, 0x6b, 0x45, 0x32, 0x5a, 0x31, 0x38, 0x77, 0x70, 0x43, 0x51, 0x79,
        0x6a, 0x62, 0x32, 0x33, 0x65, 0x58, 0x4a, 0x4d, 0x33, 0x4e, 0x70, 0x62, 0x62, 0x67,
        0x4c, 0x54, 0x78, 0x64, 0x64, 0x77, 0x6e, 0x72, 0x37, 0x41, 0x54, 0x39, 0x42, 0x52,
        0x47, 0x32, 0x1a, 0xe4, 0x1b, 0x71, 0x68, 0x29, 0xb3, 0x6e, 0xad, 0xc5, 0x28, 0x12,
        0xd6, 0xa4, 0x4b, 0x06, 0x7a, 0xdc, 0x90, 0x15, 0x99, 0xd6, 0xbf, 0x72, 0xa2, 0x30,
        0x37, 0x6b, 0x5c, 0xd6, 0x2f, 0x35, 0x14, 0x8a, 0xd6, 0xfb, 0x9f, 0xee, 0x7d, 0x2d,
        0xb7, 0x37, 0xf2, 0x0b, 0x6e, 0x00, 0xfb, 0xa0, 0x3c, 0x40, 0xf3, 0x36, 0xb2, 0x76,
        0x20, 0x0f, 0x9e, 0xa5, 0xa3, 0x15, 0x60, 0x23, 0x15, 0x29, 0xa1, 0x91, 0xbf, 0xfb,
        0x12, 0x95, 0xaa, 0x8d, 0x92, 0xc6, 0x0b, 0x8d, 0x49, 0x99, 0xa5, 0xe0, 0x05, 0xcf,
        0xb6, 0xac, 0x07, 0x54, 0x58, 0x28, 0xf9, 0x96, 0xd1, 0x9a, 0xfe, 0x0b, 0x3c, 0xfb,
        0x0b, 0x25, 0x7a, 0x43, 0x5a, 0x33, 0xc3, 0x7a, 0xfc, 0x33, 0xa3, 0xc2, 0x65, 0x48,
        0x29, 0x8d, 0x2c, 0x8f, 0x4e, 0x88, 0xfd, 0x44, 0xfd, 0xd5, 0xca, 0xb9, 0x8d, 0x62,
        0x4a, 0x48, 0x20,
    ]) + bytes([0x1d])  # 原文第 256 字节为 0x1d（Rust 测试数据末字节）
    # 注：Rust 测试数据共 256 字节，最后一个字节为 0x1d（已含在上面加号之后）
    rc4_cipher = QMC2RC4(rc4_key)
    check('QMC2RC4 decrypt', rc4_cipher.decrypt(rc4_ct, 0), b'\x00' * 256)

    # ---- tc_tea CBC 解密 ----
    good_ct = bytes([
        0x91, 0x09, 0x51, 0x62, 0xe3, 0xf5, 0xb6, 0xdc,
        0x6b, 0x41, 0x4b, 0x50, 0xd1, 0xa5, 0xb8, 0x4e,
        0xc5, 0x0d, 0x0c, 0x1b, 0x11, 0x96, 0xfd, 0x3c,
    ])
    tea_key16 = bytes([0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38,
                       0x41, 0x42, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48])
    check('tc_tea decrypt', tea_cbc_decrypt(good_ct, tea_key16), bytes([1, 2, 3, 4, 5, 6, 7, 8]))

    # ---- tc_tea 加密（自洽往返）----
    enc = tea_cbc_encrypt(b'this is a test message.', b'43218765dcbahgfe',
                          bytes([0xA5, 0x6E, 0x35, 0xBC, 0x7C, 0x31, 0x04, 0x55, 0xA0, 0xBF]))
    check('tc_tea encrypt roundtrip', tea_cbc_decrypt(enc, b'43218765dcbahgfe'),
          b'this is a test message.')

    # ---- 音频嗅探 ----
    flac = b'fLaC' + b'\x00' * 100
    check('sniff flac', detect_audio_extension(flac), 'flac')
    ogg = b'OggS' + b'\x00' * 100
    check('sniff ogg', detect_audio_extension(ogg), 'ogg')
    # ID3v2 头（size=11）+ 11 字节帧数据，音频从第 21 字节开始
    id3 = b'ID3\x04\x00\x00\x00\x00\x00\x0b' + b'\x00' * 11 + b'fLaC' + b'\x00' * 100
    check('sniff id3+flac', detect_audio_extension(id3), 'flac')
    garbage = bytes(range(256)) * 8
    check('sniff garbage=bin', detect_audio_extension(garbage), 'bin')

    print('----')
    print('自检%s' % ('全部通过' if ok else '存在失败项'))
    return ok


if __name__ == '__main__':
    sys.exit(main())
