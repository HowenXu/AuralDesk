"""歌手模块 Web 路由适配."""

from ..routing.adapter_registry import adapter
from ..routing.route_types import RouteContext


@adapter("singer", "get_desc_by_mid")
async def get_desc_by_mid_adapter(context: RouteContext):
    """根据单个歌手 MID 获取描述信息."""
    kwargs = {k: v for k, v in context.params.items() if k != "mid"}
    return await context.client.singer.get_desc([context.params["mid"]], **kwargs)
