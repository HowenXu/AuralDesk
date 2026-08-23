"""Pydantic Validator."""

from typing import Any

from pydantic import BeforeValidator


def _none_to_empty_list(value: list[Any] | None) -> list[Any]:
    """将 ``None`` 规整为空列表."""
    return [] if value is None else value


def _none_to_empty_dict(value: dict[str, Any] | None) -> dict[str, Any]:
    """将 ``None`` 规整为空字典."""
    return {} if value is None else value


def _none_or_zero_to_empty_str(value: str | int | None) -> str:
    """将 ``None`` 或 ``0`` 规整为空字符串."""
    return "" if value in (None, 0) else str(value)


#: 将 ``None`` 规整为空列表的 BeforeValidator.
NoneToEmptyList = BeforeValidator(_none_to_empty_list)

#: 将 ``None`` 规整为空字典的 BeforeValidator.
NoneToEmptyDict = BeforeValidator(_none_to_empty_dict)

#: 将 ``None`` 或 ``0`` 规整为空字符串的 BeforeValidator.
NoneOrZeroToEmptyStr = BeforeValidator(_none_or_zero_to_empty_str)
