"""启动 Web API 服务."""

import inspect
import logging
import sys
from pathlib import Path

import uvicorn
from loguru import logger

project_root = Path(__file__).resolve().parent.parent
if str(project_root) not in sys.path:
    sys.path.insert(0, str(project_root))


class _LoguruInterceptHandler(logging.Handler):
    """将标准 logging 记录转发到 loguru."""

    def emit(self, record: logging.LogRecord) -> None:
        try:
            level = logger.level(record.levelname).name
        except ValueError:
            level = record.levelno

        frame, depth = inspect.currentframe(), 2
        while frame is not None and (
            frame.f_code.co_filename == logging.__file__ or frame.f_code.co_filename == inspect.__file__
        ):
            frame = frame.f_back
            depth += 1

        logger.bind(logger_name=record.name).opt(depth=depth, exception=record.exc_info).log(level, record.getMessage())


def _setup_logging(config) -> None:
    """配置 loguru 日志系统."""
    # 1. 移除所有默认 Handler
    logger.remove()

    # 2. 配置全局 Patch 以确保 logger_name 始终存在 (直接使用 logger.info 时回退到模块名)
    logger.configure(patcher=lambda r: r["extra"].setdefault("logger_name", r["name"]))

    # 3. 添加输出端
    if config.mode in ("console", "both"):
        logger.add(
            sys.stdout,
            level=config.level,
            format=config.console_format,
            colorize=True,
            backtrace=True,
            diagnose=False,
            enqueue=True,
            catch=True,
        )
    if config.mode in ("file", "both"):
        log_file = Path(config.file_path)
        log_file.parent.mkdir(parents=True, exist_ok=True)
        logger.add(
            str(log_file),
            level=config.level,
            format=config.file_format,
            rotation=config.max_bytes,
            retention=config.backup_count,
            compression="zip",
            backtrace=True,
            diagnose=False,
            enqueue=True,
            catch=True,
        )

    # 4. 拦截标准 logging
    intercept_handler = _LoguruInterceptHandler()
    # 将 root logger 设为 0 (NOTSET), 由 InterceptHandler 捕获后再由 Loguru 根据自身级别过滤
    logging.basicConfig(handlers=[intercept_handler], level=0, force=True)
    logging.captureWarnings(capture=True)

    # 5. 特殊处理 Uvicorn 等已有 Logger, 移除其 Handler 并统一转发
    business_prefixes = ("uvicorn", "web.src", "qqmusic_api", "fastapi")
    for name in logging.root.manager.loggerDict:
        if any(name.startswith(prefix) for prefix in business_prefixes):
            logging_logger = logging.getLogger(name)
            logging_logger.handlers = [intercept_handler]
            logging_logger.propagate = False
            # 确保这些 Logger 的级别不会拦截 INFO
            logging_logger.setLevel(0)

    logger.info("日志系统已初始化: 模式={}, 级别={}", config.mode, config.level)


if __name__ == "__main__":
    from web.src.core.config import settings

    _setup_logging(settings.logging)

    uvicorn.run(
        "web.src.app:create_app",
        factory=True,
        host=settings.server.host,
        port=settings.server.port,
        workers=settings.server.workers,
        limit_concurrency=settings.server.limit_concurrency,
        log_level=settings.logging.level.lower(),
        access_log=False,
        log_config=None,
    )
