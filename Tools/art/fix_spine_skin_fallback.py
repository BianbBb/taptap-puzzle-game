#!/usr/bin/env python3
"""修掉 Spine 资产里「default 皮肤堆满表情变体槽位」的坑。

背景
----
Spine 运行时的皮肤查找是**两级**的：先用当前皮肤找，找不到就回退到 default 皮肤。
所以 default 皮肤里的任何附件都会**无条件参与渲染**，跟当前选的是哪个皮肤无关。

老周这版绑定把「表情变体」做成了**独立槽位**（falingwenR-beishang / zhezhouR-sikao /
yanqiuL-xiyue / yanzhuR-shengqi … 共 31 个），并且全塞进了 default 皮肤。
于是切到 zhengchang 皮肤后，这些槽位仍然靠 default 回退照常出图 ——
四套眉毛 / 眼睛 / 皱纹 / 发际纹 同时糊在脸上，观感就是「人脸破破烂烂、一道道竖向划痕」。

陈先生、陈果没有这个问题（default 皮肤是空的，表情靠动画里的换图关键帧）。

修法
----
把 default 皮肤里的附件清空（保留一个空的 default 皮肤条目，和陈先生一致）。
表情切换不受影响：真正的换表情是动画 `slots` 时间轴把 zui / meimao 槽替换成
zui bs / mmL sq 这类**同槽位里的不同附件名**，这些附件都在 zhengchang 皮肤里。

用法
----
    python Tools/art/fix_spine_skin_fallback.py            # 全部角色，直接改
    python Tools/art/fix_spine_skin_fallback.py --dry-run  # 只看会删什么
"""

from __future__ import annotations

import argparse
import json
import pathlib
import sys

SPINE_ROOT = pathlib.Path("GameUnity/Assets/AssetArt/Actor/Spine")


def fix_character(json_path: pathlib.Path, dry_run: bool) -> int:
    raw = json_path.read_text(encoding="utf-8")
    data = json.loads(raw)

    skins = data.get("skins")
    if not skins:
        print(f"  {json_path.stem}: 没有 skins 字段，跳过")
        return 0

    default_skin = None
    for skin in skins:
        if skin.get("name") == "default":
            default_skin = skin
            break
    if default_skin is None:
        print(f"  {json_path.stem}: 没有 default 皮肤，无需处理")
        return 0

    attachments = default_skin.get("attachments") or {}
    if not attachments:
        print(f"  {json_path.stem}: default 皮肤已经是空的，无需处理")
        return 0

    # default 里的槽位如果不是「被任一其它皮肤覆盖的正规槽位」，就是变体垃圾槽。
    covered_by_other = set()
    for skin in skins:
        if skin is default_skin:
            continue
        covered_by_other.update((skin.get("attachments") or {}).keys())

    removed = sorted(attachments.keys())
    keep = {slot: atts for slot, atts in attachments.items() if slot in covered_by_other}

    print(f"  {json_path.stem}: default 皮肤有 {len(attachments)} 个槽位，"
          f"其中 {len(removed)} 个是叠加在脸上的变体槽位")
    for slot in removed:
        print(f"      - {slot}")
    if keep:
        print(f"    保留（其它皮肤也定义了同名槽位）: {sorted(keep)}")

    if dry_run:
        return len(removed)

    default_skin["attachments"] = keep
    json_path.write_text(
        json.dumps(data, ensure_ascii=False, separators=(",", ":")),
        encoding="utf-8",
    )
    return len(removed)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--root", default=str(SPINE_ROOT))
    args = parser.parse_args()

    root = pathlib.Path(args.root)
    if not root.is_dir():
        print(f"找不到走素材目录: {root}", file=sys.stderr)
        return 1

    total = 0
    for json_path in sorted(root.glob("*/*.json")):
        total += fix_character(json_path, args.dry_run)

    verb = "将移除" if args.dry_run else "已移除"
    print(f"\n{verb} {total} 个 default 皮肤变体槽位。")
    if not args.dry_run and total:
        print("记得回 Unity 等 json 重新导入，然后重启 Play Mode。")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
