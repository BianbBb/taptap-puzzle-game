#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
从角色立绘裁三个 NPC 的圆头像（气泡左侧用）。

为什么不用 Spine 图集：Spine 的头是拆开的图层（脸/头发/表情各一张），
按图集坐标拼头等于自己实现一遍骨骼合成；立绘本来就是一张完整图，
头部就是现成的。

输出到 `GameUnity/Assets/BundleAssets/UIRaw/Raw/dialogue_portrait_<key>.png`，
圆环/白边/内描边的比例是照 `dialogue_npc_01_portrait.png`（100px）量的：
    从外到内 = 1 柔边 + 1 环 + 1 浅环 + 4 白 + 1 淡粉 → 内容半径 42/50
尺寸统一 200px（气泡里的 Image 是 100 单位，2 倍图足够，也不吃 mipmap）。

用法：python Tools/art/make_npc_portrait.py
改构图只动 POTRAITS 里的 box（源图像素坐标），跑完重开 Unity 会自己刷 meta。
"""

from __future__ import annotations

import os

from PIL import Image, ImageDraw

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
RAW = os.path.join(REPO, "GameUnity", "Assets", "BundleAssets", "UIRaw", "Raw")

# 圆环四层，按 100px 头像量出来的颜色
RING = (175, 142, 127)
RING_LIGHT = (213, 195, 187)
RING_PINK = (255, 229, 222)

SIZE = 200
SS = 4  # 超采样倍数，圆边靠它抗锯齿

# key: (源图, 裁切框 left/top/right/bottom, 圆内底色)
POTRAITS = {
    # 老周：头发 y≈8 起、帘最宽 x≈315~865，脖子在 y≈510
    "laozhou": ("npc_lao_zhou_psd.png", (290, 0, 890, 600), (222, 228, 208)),
    # 陈先生：发顶 y≈8、脸最宽 x≈264~611，下巴 y≈430
    "chenxiansheng": ("npc_chen_xiansheng_psd.png", (187, 0, 687, 500), (226, 214, 200)),
    # 陈果：呆毛 y≈9、双马尾到 x≈995，下巴 y≈665
    "chenguo": ("npc_chen_guo_cutout.png", (170, 0, 930, 760), (214, 225, 230)),
}


def _ring(canvas: Image.Image, r_out: float, r_in: float, color: tuple[int, int, int]) -> None:
    """在 canvas 上画一个圆环（r_out → r_in 之间填色），用来叠边框。"""
    s = canvas.width
    mask = Image.new("L", (s, s), 0)
    draw = ImageDraw.Draw(mask)
    c = s / 2.0
    draw.ellipse([c - r_out, c - r_out, c + r_out, c + r_out], fill=255)
    draw.ellipse([c - r_in, c - r_in, c + r_in, c + r_in], fill=0)
    canvas.paste(Image.new("RGBA", (s, s), color + (255,)), (0, 0), mask)


def build(key: str, source: str, box: tuple[int, int, int, int],
          tint: tuple[int, int, int]) -> str:
    s = SIZE * SS
    art = Image.open(os.path.join(RAW, source)).convert("RGBA").crop(box)
    art = art.resize((s, s), Image.LANCZOS)

    canvas = Image.new("RGBA", (s, s), tint + (255,))
    canvas.alpha_composite(art)

    mask = Image.new("L", (s, s), 0)
    ImageDraw.Draw(mask).ellipse([0, 0, s - 1, s - 1], fill=255)
    canvas.putalpha(mask)

    k = s / 100.0  # 100px 参考坐标 → 当前超采样坐标
    half = s / 2.0
    _ring(canvas, half, half - k * 0.6, RING_LIGHT)        # 柔边
    _ring(canvas, half - k * 0.6, half - k * 1.6, RING)    # 主环
    _ring(canvas, half - k * 1.6, half - k * 2.6, RING_LIGHT)
    _ring(canvas, half - k * 2.6, half - k * 6.6, (255, 255, 255))
    _ring(canvas, half - k * 6.6, half - k * 7.6, RING_PINK)

    canvas = canvas.resize((SIZE, SIZE), Image.LANCZOS)
    out = os.path.join(RAW, f"dialogue_portrait_{key}.png")
    canvas.save(out)
    return out


def main() -> None:
    for key, (source, box, tint) in POTRAITS.items():
        print("wrote", os.path.relpath(build(key, source, box, tint), REPO))


if __name__ == "__main__":
    main()
