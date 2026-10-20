#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""按 NPC 出「剧本需要什么表情 / 表里配了什么 / Spine 里实际有什么动作」对照表。

三份数据源：
  1. 剧本 `各事件对白文本.docx` 里每句台词末尾的 {表情} → 需求
  2. 《剧情节点配置表》animationAsset 列 → 实际写进表里的语义键
  3. `Assets/AssetArt/Actor/Spine/<角色>/<角色>.json` → 动作名、皮肤名、换图关键帧

用法：
    python3 GameConfig/Tools/story_import/report_expression_matrix.py \
        --src <各事件对白文本.docx> [--report docs/角色动作对照表.md]
"""
import argparse
import collections
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from fill_story_expressions import TABLE_NAME, align, load_rows, read_doc  # noqa: E402

import openpyxl  # noqa: E402

SPINE_ROOT = "GameUnity/Assets/AssetArt/Actor/Spine"

# 表里的 NpcId → Spine 角色目录（女孩与陈果共用一套）
NPC_SPINE = {
    "老周": "laozhou",
    "陈先生": "chenxiansheng",
    "陈果": "chenguo",
    "女孩": "chenguo",
}

EMOTIONS = ["think", "sad", "angry", "happy", "moved", "surprised"]
EMOTION_CN = {
    "think": "思考", "sad": "悲伤", "angry": "气愤",
    "happy": "喜悦", "moved": "感动", "surprised": "惊讶",
    None: "（无标记）",
}

# 与 BattleMainUI.MapNpcSpinePose 保持一致：语义键 → 该角色实际播的动作，None = 没有专用动作
POSE_MAP = {
    "laozhou": {
        "think": "sikao", "sad": "beishang", "angry": "shengqi",
        "happy": None, "moved": None, "surprised": None,
    },
    "chenxiansheng": {
        "think": "sikao", "sad": "shangxin", "angry": None,
        "happy": None, "moved": "shangxin", "surprised": "jingya",
    },
    "chenguo": {
        "think": "sikao", "sad": "beishang", "angry": None,
        "happy": "kaixin", "moved": "kaixin", "surprised": None,
    },
}

# 缺专用动作时的兜底（同样与 C# 对齐）
POSE_FALLBACK = {
    "laozhou": {"happy": "zhengchang", "moved": "zhengchang", "surprised": "zhengchang"},
    "chenxiansheng": {"angry": "jingya", "happy": "zhengchang"},
    "chenguo": {"angry": "beishang", "surprised": "zhengchang"},
}

# 缺口的人工说明（写得比「用 X 顶」具体一点）
GAP_NOTE = {
    ("laozhou", "happy"): "用 `zhengchang` 顶；该动作里有 1 帧 `zui  kx`（笑嘴），勉强能看",
    ("laozhou", "moved"): "用 `zhengchang` 顶，剧本里这个角色没有感动句，暂不影响",
    ("laozhou", "surprised"): "用 `zhengchang` 顶，剧本里没有惊讶句，暂不影响",
    ("chenxiansheng", "angry"): "用 `jingya`（惊讶）顶 —— 情绪方向不对，是全表最明显的缺口",
    ("chenxiansheng", "happy"): "用 `zhengchang` 顶，剧本里没有喜悦句，暂不影响",
    ("chenguo", "angry"): "用 `beishang` 顶，偏悲伤，剧本里没有气愤句，暂不影响",
    ("chenguo", "surprised"): "用 `zhengchang` 顶，剧本里没有惊讶句，暂不影响",
}


def load_spine(slug):
    """返回 (动作列表, 皮肤列表, {动作: 引用到的贴图名集合})。"""
    path = os.path.join(SPINE_ROOT, slug, slug + ".json")
    with open(path, encoding="utf-8") as f:
        data = json.load(f)

    skins_raw = data.get("skins") or []
    if isinstance(skins_raw, dict):
        skins_raw = [{"name": k, "attachments": v} for k, v in skins_raw.items()]
    skin_names = [s.get("name") for s in skins_raw]
    defined = set()
    for s in skins_raw:
        for atts in (s.get("attachments") or {}).values():
            defined |= set(atts.keys())

    anims = {}
    for name, anim in (data.get("animations") or {}).items():
        used = set()
        for track in (anim.get("slots") or {}).values():
            for frame in (track.get("attachment") or []):
                if frame.get("name"):
                    used.add(frame["name"])
        anims[name] = used

    return list(anims.keys()), skin_names, anims, defined


def resolve_pose(slug, emotion):
    """语义键 → (实际动作, 是否专用)。专用 = 该动作就是为这个表情做的。"""
    native = POSE_MAP[slug].get(emotion)
    if native:
        return native, True
    return POSE_FALLBACK[slug].get(emotion, "zhengchang"), False


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--src", required=True, help="各事件对白文本.docx")
    parser.add_argument("--datas", default="GameConfig/Datas")
    parser.add_argument("--report", default="", help="markdown 输出路径")
    args = parser.parse_args()

    # 1) 剧本需求
    doc_lines = read_doc(args.src)
    doc_stat = collections.defaultdict(collections.Counter)
    for speaker, _norm, key, _raw in doc_lines:
        doc_stat[speaker][key] += 1

    # 2) 表内实配（对齐后的语义键）
    wb = openpyxl.load_workbook(os.path.join(args.datas, TABLE_NAME))
    rows = load_rows(wb.active)
    assigned, _used = align(doc_lines, rows)
    table_stat = collections.defaultdict(collections.Counter)
    for i, row in enumerate(rows):
        table_stat[row["npc"]][assigned[i][0] if i in assigned else None] += 1

    # 3) Spine 素材
    spine = {slug: load_spine(slug) for slug in set(NPC_SPINE.values())}

    out = []
    out.append("# 角色动作对照表（剧本需求 / 表内实配 / Spine 实有）")
    out.append("")
    out.append("数据源：`各事件对白文本.docx` 的 `{表情}` 标记、"
               "《剧情节点配置表》`animationAsset` 列、"
               "`Assets/AssetArt/Actor/Spine/<角色>/<角色>.json`。")
    out.append("")

    # ---- 总览 ----
    out.append("## 总览")
    out.append("")
    out.append("| NPC | 剧本台词 | 需表情句 | 表内行数 | 配了表情 | Spine 动作数 | 覆盖情况 |")
    out.append("|---|---|---|---|---|---|---|")
    for npc in ["老周", "陈先生", "陈果", "陈父", "妻子", "AI"]:
        if npc not in doc_stat and npc not in table_stat:
            continue
        d, t = doc_stat[npc], table_stat[npc]
        need = sum(v for k, v in d.items() if k)
        filled = sum(v for k, v in t.items() if k)
        slug = NPC_SPINE.get(npc)
        if slug:
            anims, skins, _am, _df = spine[slug]
            need_kinds = sorted({k for k in d if k})
            native = [e for e in need_kinds if POSE_MAP[slug].get(e)]
            cover = "%d/%d 个表情有专用动作" % (len(native), len(need_kinds))
            out.append("| %s | %d | %d | %d | %d | %d | %s |"
                       % (npc, sum(d.values()), need, sum(t.values()), filled, len(anims), cover))
        else:
            out.append("| %s | %d | %d | %d | %d | — | 无 Spine，用立绘 `Image` |"
                       % (npc, sum(d.values()), need, sum(t.values()), filled))
    out.append("")

    # ---- 分角色 ----
    for npc in ["老周", "陈先生", "陈果", "陈父", "妻子", "AI"]:
        if npc not in doc_stat and npc not in table_stat:
            continue
        slug = NPC_SPINE.get(npc)
        out.append("## %s" % npc)
        out.append("")
        d, t = doc_stat[npc], table_stat[npc]

        if slug:
            anims, skins, anim_used, defined = spine[slug]
            out.append("**Spine 实有动作**（`%s`）：%s；皮肤：%s"
                       % (slug, " / ".join(anims), " / ".join(skins)))
            out.append("")
            out.append("| 剧本要的表情 | 剧本句数 | 表内行数 | 实际播的动作 | 是否专用 | 说明 |")
            out.append("|---|---|---|---|---|---|")
            for emo in EMOTIONS + [None]:
                dn, tn = d.get(emo, 0), t.get(emo, 0)
                if dn == 0 and tn == 0:
                    continue
                if emo is None:
                    out.append("| %s | %d | %d | — | — | 沿用上一句，不切动作 |"
                               % (EMOTION_CN[None], dn, tn))
                    continue
                pose, native = resolve_pose(slug, emo)
                note = ""
                if not native:
                    note = GAP_NOTE.get((slug, emo)) or ("用 `%s` 顶" % pose)
                else:
                    frames = len(anim_used.get(pose, ()))
                    note = "专用动作，含 %d 张换图帧" % frames
                    if emo == "moved" and slug == "chenxiansheng":
                        note += "（与「悲伤」共用 `shangxin`）"
                    if emo == "moved" and slug == "chenguo":
                        note += "（`kaixin` 内含 `yjL/gandong` 换图帧）"
                out.append("| %s | %d | %d | `%s` | %s | %s |"
                           % (EMOTION_CN[emo], dn, tn, pose, "是" if native else "**否**", note))
            out.append("")

            diff = [(EMOTION_CN[e], d.get(e, 0), t.get(e, 0))
                    for e in EMOTIONS + [None] if d.get(e, 0) != t.get(e, 0)]
            if diff:
                out.append("> 差额说明：" + "；".join(
                    "%s 剧本 %d 句 / 表 %d 行" % (name, dn, tn) for name, dn, tn in diff)
                    + "。一句台词被拆成多个节点（或反过来并成一行）时就会这样，不影响表情本身。")
                out.append("")

            out.append("| 动作名 | 换图关键帧 | 需要但未定义的贴图 | 用途 |")
            out.append("|---|---|---|---|")
            for a in anims:
                used = anim_used.get(a, set())
                missing = used - defined
                # 反查这个动作被哪些表情用到（专用 / 兜底分开说）
                native_for = [EMOTION_CN[e] for e in EMOTIONS if POSE_MAP[slug].get(e) == a]
                fallback_for = [EMOTION_CN[e] for e in EMOTIONS if POSE_FALLBACK[slug].get(e) == a]
                uses = []
                if a == "zhengchang":
                    uses.append("待机/平静")
                if native_for:
                    uses.append("专用：" + "、".join(native_for))
                if fallback_for:
                    uses.append("兜底：" + "、".join(fallback_for) + "（无专用动作）")
                out.append("| `%s` | %d | %s | %s |"
                           % (a, len(used), "、".join(sorted(missing)) if missing else "无",
                              "；".join(uses)))

            # 素材层面的两个硬约束，运行时靠它决定「先切皮肤还是先播动作」
            if "default" not in skins:
                out.append("")
                out.append("> ⚠ 这套素材**没有 `default` 皮肤**，必须先 `SwitchSkin` 再 `SetAnimation`，"
                           "否则人物一个格子都挂不上（表现为完全不显示）。")
            if slug == "chenguo":
                out.append("")
                out.append("> ⚠ 皮肤只有 `zhengchang`，所有表情都靠动作里的换图关键帧表达，"
                           "`kaixin` 同时覆盖「喜悦」（`zui/xiyue`）与「感动」（`yjL/gandong`）。")
            if slug == "laozhou":
                out.append("")
                out.append("> ℹ `zhengchang` 动作里有一帧 `zui  kx`（笑嘴），"
                           "所以「喜悦」用平静顶也不是完全没表情，只是没有独立的笑动作。")
            out.append("")
        else:
            out.append("没有绑定 Spine，走立绘 `Image` + 表情图（`MapNpcPortraitExpression`）。")
            out.append("")
            out.append("| 剧本要的表情 | 剧本句数 | 表内行数 |")
            out.append("|---|---|---|")
            for emo in EMOTIONS + [None]:
                dn, tn = d.get(emo, 0), t.get(emo, 0)
                if dn == 0 and tn == 0:
                    continue
                out.append("| %s | %d | %d |" % (EMOTION_CN[emo], dn, tn))
            out.append("")

    text = "\n".join(out) + "\n"
    if args.report:
        with open(args.report, "w", encoding="utf-8") as f:
            f.write(text)
        print("报告：%s" % args.report)
    else:
        print(text)


if __name__ == "__main__":
    main()
