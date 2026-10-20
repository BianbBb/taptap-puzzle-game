#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
剧情配表 V1.0（策划版） -> DGame Luban 叙事表 转换器

用法：
    # 预览（默认）：输出到 GameConfig/Datas/_import_v1_preview/，不动生产表
    python GameConfig/Tools/story_import/convert_story_v1.py \
        --src ~/Desktop/副本剧情配表_V1.0(1).xlsx

    # 正式写入（会覆盖 GameConfig/Datas/ 下的 8 张叙事表）
    python GameConfig/Tools/story_import/convert_story_v1.py \
        --src ~/Desktop/副本剧情配表_V1.0(1).xlsx --apply

依赖：pip install openpyxl

设计原则：
  1. 默认不覆盖生产表，先产出预览 + 报告，人工确认后再 --apply。
  2. 所有"源表缺字段"的地方都记录到 import_report.md，不静默丢数据。
  3. 输出表结构完全对齐 GameConfig/Datas/ 现有表（##var/##type/##group/## 四行标记）。
"""

import argparse
import datetime
import re
import shutil
import sys
from collections import defaultdict, OrderedDict
from pathlib import Path

try:
    import openpyxl
    from openpyxl import Workbook
except ImportError:
    sys.exit("缺少 openpyxl，请先执行：pip install openpyxl")

REPO_ROOT = Path(__file__).resolve().parents[3]
DATAS_DIR = REPO_ROOT / "GameConfig" / "Datas"
DEFAULT_OUT = DATAS_DIR / "_import_v1_preview"

# 由剧情配表驱动的表：写生产时会覆盖。
# 其余表（记忆/情绪/物品/部件/动作）目前由策划在 Datas 里手工维护，
# 默认只生成预览、不覆盖，避免冲掉手工整理成果（含 imageId、重排后的记忆）。
CORE_FILES = {
    "剧情节点配置表.xlsx",
    "叙事Session配置表.xlsx",
    "回复选项配置表.xlsx",
    "情绪记忆规则配置表.xlsx",
}

# Excel 序列号基准（1900 日期系统）
EXCEL_EPOCH = datetime.date(1899, 12, 30)

# 策划表用 NPC_xxx 占位，项目 npcId 用中文名。新增 NPC 时在这里登记。
NPC_ID_MAP = {
    "NPC_001": "老周",
    "NPC_002": "陈先生",
    "NPC_003": "陈果",
}

# 策划表的场景名 -> 项目现有场景图层前缀。
# 新图补齐后，在这里加映射即可，代码侧 TryResolveBackgroundLocations 也要同步。
BACKGROUND_MAP = {
    "修理铺-收音机修复前": "scene_shop_old",
    "修理铺-收音机修复后": "scene_shop_new",
    "陈果卧室-修复前": "scene_chenguo",
    "陈果卧室-修复后": "scene_chenguo",
    "陈先生卧室-修复前": "scene_father",
    "陈先生卧室-修复后": "scene_father",
    "仓库": "scene_zhou",
}


# ----------------------------------------------------------------------------
# 基础解析工具
# ----------------------------------------------------------------------------

def cell_str(v):
    if v is None:
        return ""
    if isinstance(v, bool):
        return "True" if v else "False"
    if isinstance(v, float) and v.is_integer():
        return str(int(v))
    return str(v).strip()


def parse_list(v):
    """'[M01,M02]' / 'M01,M02' / 'M01;M02' -> ['M01','M02']"""
    s = cell_str(v)
    if not s:
        return []
    s = s.strip("[]")
    return [x.strip() for x in re.split(r"[,;、]", s) if x.strip()]


def parse_bool(v):
    s = cell_str(v).lower()
    return "True" if s in ("true", "1", "yes", "y", "是") else "False"


def parse_int(v, default=0):
    s = cell_str(v)
    if not s:
        return default
    try:
        return int(float(s))
    except ValueError:
        return default


def parse_number(v, default=0.0):
    s = cell_str(v)
    if not s:
        return default
    try:
        return float(s)
    except ValueError:
        return default


def excel_date_to_ordinal(v):
    """Session 的 SessionDate 存的是 Excel 日期序列号；返回 date 或 None"""
    if isinstance(v, datetime.datetime):
        return v.date()
    if isinstance(v, datetime.date):
        return v
    s = cell_str(v)
    if not s:
        return None
    try:
        serial = float(s)
    except ValueError:
        return None
    if serial < 1:
        return None
    return EXCEL_EPOCH + datetime.timedelta(days=int(serial))


def parse_affection_condition(text):
    """
    从策划自然语言条件里解析好感门槛，返回 (min, max) 或 None。
    支持：需要好感 5点 / 好感≥5 / 好感 5-10 / affection >= 5
    单值时 max 取 101（BranchRule 上界是开区间，101 作为无上界哨兵，与项目现有表一致）。
    """
    if not text:
        return None
    nums = re.findall(r"\d+(?:\.\d+)?", text)
    if not nums:
        return None
    lo = float(nums[0])
    hi = float(nums[1]) if len(nums) > 1 else 101.0
    if hi <= lo:
        hi = 101.0
    return lo, hi


def read_sheet_rows(wb, name, stop_marker=None):
    """
    读取一个 sheet 的数据行（跳过 ##var/##type/## 标记行）。
    stop_marker: 遇到该名字的子表表头就停止（用于 Item sheet 下半段嵌 ItemPart 子表的场景）。
    返回 (headers, rows)，headers 来自 ##var 行。
    """
    if name not in wb.sheetnames:
        return [], []
    ws = wb[name]
    headers = []
    rows = []
    for idx, row in enumerate(ws.iter_rows(values_only=True), start=1):
        values = list(row)
        if idx == 1:
            headers = [cell_str(c) for c in values]
            continue
        # 先判断是否撞到下半段子表表头（子表表头第一列也是 ##var，必须在跳过标记行之前判断）
        first = cell_str(values[1]) if len(values) > 1 else ""
        if stop_marker and first == stop_marker:
            break
        # 标记行（##var / ##type / ##group / ## 说明）整行跳过
        if values and cell_str(values[0]).startswith("##"):
            continue
        if not first:
            continue
        # 跳过夹在中间的子表表头（例如 Item sheet 下半段的 ItemPart 表头）
        if first.startswith("##") or first.startswith("【"):
            continue
        if first in ("ItemPartID", "ItemPart", "ItemID", "TurnID"):
            continue
        rows.append(values)
    return headers, rows


def col(headers, name):
    """按列名取索引，找不到返回 -1"""
    try:
        return headers.index(name)
    except ValueError:
        return -1


def get(values, headers, name, default=None):
    i = col(headers, name)
    if i < 0 or i >= len(values):
        return default
    v = values[i]
    return default if v is None or cell_str(v) == "" else v


# 动作里的 ADD_xxx 对应物品中文名，用于推断"这个节点在评估哪件物品"
ITEM_CN = {"GLASS": "眼镜", "CAMERA": "相机", "CLOCK": "座钟",
           "DOLL": "布偶", "RADIO": "收音机", "TAPE": "磁带"}


# ----------------------------------------------------------------------------
# 「物品-记忆-情绪对应表」用的是另一套编号（按物品紧凑排：相机3/眼镜3/座钟3/布偶3/
# 收音机4/磁带2/对话1），与剧情源表 Memory 的编号（相机1-2、7；眼镜3-5；钟表6、8-9；
# 娃娃10-12、17；收录机13-16；磁带18-19）不是同一套。
# 两张表的记忆内容一一对应（相似度 1.00），只是编号错位，因此靠这张固定翻译表搭桥。
# 左 = 对应表编号，右 = 剧情源表编号。
# ----------------------------------------------------------------------------
CORR_TO_SRC_MEMORY = {
    "M01": "M01",  # 无法使用      -> 相机记忆1
    "M02": "M02",  # 镜头见证      -> 相机记忆2
    "M20": "M07",  # 相机胶卷      -> 相机记忆3
    "M03": "M03",  # 旧镜架        -> 眼镜记忆1
    "M04": "M05",  # 蒙灰镜片      -> 眼镜记忆3
    "M05": "M04",  # 不同镜腿      -> 眼镜记忆2
    "M06": "M08",  # 报时鸟鸣      -> 钟表记忆1
    "M07": "M09",  # 月下钟声      -> 钟表记忆2
    "M08": "M06",  # 旧时停摆      -> 钟表记忆3
    "M09": "M10",  # 友情信物      -> 娃娃记忆1
    "M10": "M11",  # 修复尝试      -> 娃娃记忆2
    "M11": "M12",  # 未尽之言      -> 娃娃记忆3
    "M12": "M13",  # 卡带          -> 收录机记忆1
    "M13": "M14",  # 无缘重温      -> 收录机记忆2
    "M14": "M15",  # 遗忘许久      -> 收录机记忆3
    "M15": "M16",  # 尘封旧事      -> 收录机记忆4
    "M16": "M19",  # 旧事重提      -> 磁带记忆2
    "M17": "M18",  # 初见你        -> 磁带记忆1
    "M18": "M17",  # 起死回生(对话) -> 娃娃记忆4
}


def load_memory_item_map(path):
    """
    读「物品-记忆-情绪对应表」：记忆ID -> {name, item}，key 统一转换成剧情源表的编号。

    表结构：A 序号 / B 物品（合并单元格，只在首行填）/ C 记忆名 / D 记忆ID / E 描述；
    右侧 L~Q 另有一小块「对话记忆」区（出现章节 / 名字 / 对应文本 / 记忆：Mxx / 情绪）。
    """
    wb = openpyxl.load_workbook(path, data_only=True)
    ws = wb.worksheets[0]
    cur_item = ""
    out = {}
    unmapped = []
    for row in ws.iter_rows(min_row=2, values_only=True):
        if len(row) > 1 and cell_str(row[1]):
            cur_item = cell_str(row[1])
        if len(row) > 3 and cell_str(row[3]).startswith("M"):
            corr_id = cell_str(row[3])
            src_id = CORR_TO_SRC_MEMORY.get(corr_id)
            if src_id is None:
                unmapped.append(corr_id)
                src_id = corr_id
            out[src_id] = {"name": cell_str(row[2]), "item": cur_item}

    # 右侧「对话记忆」区：O 列写着 "记忆：Mxx"，这些是对话中习得的记忆，不属于任何物品
    for row in ws.iter_rows(min_row=3, values_only=True):
        if len(row) <= 14:
            continue
        tag = cell_str(row[14])
        if not tag or "记忆" not in tag:
            continue
        corr_id = tag.split("：")[-1].split(":")[-1].strip()
        if not corr_id:
            continue
        src_id = CORR_TO_SRC_MEMORY.get(corr_id, corr_id)
        out.setdefault(src_id, {"name": cell_str(row[12]), "item": "对话记忆"})

    return out, unmapped


def check_memory_alignment(mem_item_map, opt_rows, opt_h, turn_rows, turn_h, rep):
    """
    核对每个 requiredMemory 引用的记忆，在「物品-记忆-情绪对应表」里是否属于当前节点评估的物品。
    跨物品引用（例如"上次那副眼镜"的后续对话）是合法写法，只提示不报错。
    """
    t_item = {}
    set2turn = {}
    for r in turn_rows:
        tid = cell_str(r[col(turn_h, "TurnID")])
        if not tid:
            continue
        sid = cell_str(get(r, turn_h, "OptionSetID"))
        if sid:
            set2turn[sid] = tid
        m = re.match(r"ADD_(\w+)", cell_str(get(r, turn_h, "OnEnterActionSetID")))
        if m:
            t_item[tid] = ITEM_CN.get(m.group(1), m.group(1))

    rows = []
    miss = []
    cross = []
    for r in opt_rows:
        oid = cell_str(r[col(opt_h, "OptionID")])
        if not oid:
            continue
        tid = set2turn.get(cell_str(r[col(opt_h, "OptionSetID")]), "")
        item = t_item.get(tid, "")
        for mid in parse_list(get(r, opt_h, "ConditionGroupID")):
            info = mem_item_map.get(mid)
            if info is None:
                rows.append((oid, tid, item, mid, "—", "①"))
                miss.append((oid, mid))
            elif item and info["item"] != item and info["item"] != "对话记忆":
                rows.append((oid, tid, item, mid, f"{info['item']}/{info['name']}", "②"))
                cross.append((oid, mid, item, info["item"]))
            else:
                rows.append((oid, tid, item, mid,
                             f"{info['item']}/{info['name']}", "OK"))

    if not rows:
        return

    rep.h("记忆引用 vs 物品归属核对")
    rep.p("对应表用的是另一套记忆编号，已通过 `CORR_TO_SRC_MEMORY` 翻译成剧情源表编号后再核对。\n")
    rep.p("| 对应表编号 | → 源表编号 |")
    rep.p("|---|---|")
    for k, v in CORR_TO_SRC_MEMORY.items():
        rep.p(f"| {k} | {v} |")
    rep.p("")
    rep.p("图例：① 对应表里没有这条记忆；② 引用的记忆属于别的物品"
          "（可能是跨物品的剧情依赖，需人工确认）\n")
    rep.p("| Option | 节点 | 节点物品 | 引用的记忆 | 该记忆归属 | 判定 |")
    rep.p("|---|---|---|---|---|---|")
    for oid, tid, item, mid, belong, flag in rows:
        rep.p(f"| {oid} | {tid or '—'} | {item or '—'} | {mid} | {belong} | {flag} |")

    if cross:
        detail = "；".join(f"{o} 在「{item}」节点引用「{belong}」的记忆 {m}"
                           for o, m, item, belong in cross)
        rep.warn("MEMORY_CROSS_ITEM",
                 f"{len(cross)} 处跨物品引用，若剧情里确实回指前一件物品则属正常：{detail}")

    if miss:
        ids = sorted({m for _, m in miss}, key=lambda x: (len(x), x))
        rep.warn("MEMORY_IN_MAP_MISSING",
                 f"对应表缺少 {ids}，但剧情配表引用了它们（涉及 {len({o for o, _ in miss})} 个选项）。"
                 f"需要补进对应表，否则无法核对其物品归属")


# ----------------------------------------------------------------------------
# 输出：Luban 表写入
# ----------------------------------------------------------------------------

def write_luban_sheet(path, sheet_name, headers, comment_map, rows, groups="c"):
    """
    生成带 ##var/##type/##group/## 四行标记的 Luban 业务表。
    headers: [(字段名, 类型, 注释)]
    rows:    [[值...], ...]  值已按 luban 文本格式拼好
    """
    wb = Workbook()
    ws = wb.active
    ws.title = sheet_name

    ws.append(["##var"] + [h[0] for h in headers])
    ws.append(["##type"] + [h[1] for h in headers])
    ws.append(["##group"] + [groups for _ in headers])
    ws.append(["##"] + [h[2] for h in headers])

    for r in rows:
        # A 列留空：Luban 表的字段从 B 列开始（与现有业务表一致）
        ws.append([""] + list(r))

    # 列宽按内容自适应，方便策划直接打开检查
    for ci, h in enumerate(headers, start=1):
        width = max(len(str(h[0])), 10)
        for r in rows[:50]:
            if ci - 1 < len(r):
                width = max(width, min(len(str(r[ci - 1])), 60))
        ws.column_dimensions[ws.cell(row=1, column=ci).column_letter].width = width

    path.parent.mkdir(parents=True, exist_ok=True)
    wb.save(path)
    return path


# ----------------------------------------------------------------------------
# 转换主体
# ----------------------------------------------------------------------------

class Reporter:
    def __init__(self):
        self.lines = []
        self.warnings = []
        self.gaps = []

    def h(self, t):
        self.lines.append(f"\n## {t}\n")

    def p(self, t):
        self.lines.append(t)

    def warn(self, code, msg):
        self.warnings.append((code, msg))
        self.lines.append(f"- ⚠️ **[{code}]** {msg}")

    def note(self, code, msg):
        """普通提示：不需要处理，但值得让人知道。"""
        self.lines.append(f"- ℹ️ **[{code}]** {msg}")

    def gap(self, field, msg):
        self.gaps.append((field, msg))
        self.lines.append(f"- ❌ **[{field}]** {msg}")

    def dump(self, path):
        path.write_text("\n".join(self.lines), encoding="utf-8")


def build_option_index(opt_rows, opt_headers):
    """OptionSetID -> [OptionID...]（保持 displayOrder 顺序）"""
    by_set = defaultdict(list)
    by_id = {}
    oid_i = col(opt_headers, "OptionID")
    set_i = col(opt_headers, "OptionSetID")
    order_i = col(opt_headers, "DisplayOrder")
    for r in opt_rows:
        oid = cell_str(r[oid_i])
        sid = cell_str(r[set_i])
        order = parse_int(r[order_i], 0)
        by_set[sid].append((order, oid))
        by_id[oid] = r
    for sid in by_set:
        by_set[sid] = [oid for _, oid in sorted(by_set[sid], key=lambda x: (x[0], x[1]))]
    return by_set, by_id


def convert(src, out_dir, apply_mode, day_mode, mem_map=None):
    wb = openpyxl.load_workbook(src, data_only=True)
    rep = Reporter()
    rep.h("剧情配表 V1.0 导入报告")
    rep.p(f"- 源文件：`{src}`")
    rep.p(f"- 输出目录：`{out_dir}`")
    rep.p(f"- 模式：{'正式写入生产表' if apply_mode else '仅预览（未改动生产表）'}")

    # ---------- 读源表 ----------
    ses_h, ses_rows = read_sheet_rows(wb, "Session")
    turn_h, turn_rows = read_sheet_rows(wb, "Turn")
    opt_h, opt_rows = read_sheet_rows(wb, "Option")
    mem_h, mem_rows = read_sheet_rows(wb, "Memory")
    emo_h, emo_rows = read_sheet_rows(wb, "Emotion")
    act_h, act_rows = read_sheet_rows(wb, "Action")

    # Item sheet 上下两段：物品表 + 部件子表
    item_h, item_rows = read_sheet_rows(wb, "Item", stop_marker="ItemPartID")
    ws_item = wb["Item"]
    part_h = None
    part_rows = []
    for row in ws_item.iter_rows(values_only=True):
        first = cell_str(row[1]) if len(row) > 1 else ""
        if first == "ItemPartID":
            part_h = [cell_str(c) for c in row]
        elif part_h is not None and first and not first.startswith(("#", "【")):
            if first in ("ItemPart", "string"):
                continue
            part_rows.append(list(row))

    opt_by_set, opt_by_id = build_option_index(opt_rows, opt_h)

    rep.h("数据规模")
    rep.p(f"| 表 | 条数 |\n|---|---|")
    rep.p(f"| Session | {len(ses_rows)} |")
    rep.p(f"| Turn | {len(turn_rows)} |")
    rep.p(f"| Option | {len(opt_rows)} |")
    rep.p(f"| Memory | {len(mem_rows)} |")
    rep.p(f"| Emotion | {len(emo_rows)} |")
    rep.p(f"| Item | {len(item_rows)} |")
    rep.p(f"| ItemPart | {len(part_rows)} |")
    rep.p(f"| Action | {len(act_rows)} |")

    # ---------- 1. 情绪表 ----------
    rep.h("情绪配置表")
    # 项目表只有 id/emotionName/displayColor；源表的 Name 列放的是英文 key，中文在 Description 列。
    # **成员以源表为准**（源表删掉的情绪就是真删，不再被生产表兜回来）；名称/颜色仍优先沿用
    # 生产表里策划调过的值，否则会把 E02 显示成 "joyful"、把已有颜色冲成默认灰。
    fallback_colors = {
        "E00": "#E6E6E6", "E02": "#CDE7F0", "E03": "#D7D3E8",
        "E04": "#D5E8D4", "E06": "#FCE5CD", "E07": "#EDE0D4",
    }
    prod_emo = {}   # id -> (name, color)
    prod_emo_path = DATAS_DIR / "情绪配置表.xlsx"
    if prod_emo_path.exists():
        for pr in openpyxl.load_workbook(prod_emo_path, data_only=True).worksheets[0].iter_rows(
                min_row=5, values_only=True):
            if not pr or not cell_str(pr[1]):
                continue
            prod_emo[cell_str(pr[1])] = (cell_str(pr[2]), cell_str(pr[3]))
        rep.p(f"已读取生产「情绪配置表」{len(prod_emo)} 条，将以它为名称/颜色的基准。\n")

    emo_out = []
    for r in emo_rows:
        eid = cell_str(r[col(emo_h, "EmotionID")])
        key = cell_str(r[col(emo_h, "Name")])          # 英文 key，不进配置
        desc = cell_str(r[col(emo_h, "Description")])  # 中文名，才是要显示的文字
        if eid in prod_emo:
            name, color = prod_emo[eid]
            if desc and name and desc != name:
                rep.note("EMOTION_NAME_KEPT",
                         f"情绪 {eid}：生产表名「{name}」，源表中文「{desc}」—— 保留生产表名")
            emo_out.append([eid, name, color])
            continue
        color = fallback_colors.get(eid, "#E6E6E6")
        name = desc or key or eid
        if eid not in fallback_colors:
            rep.warn("EMOTION_COLOR_DEFAULT",
                     f"新增情绪 {eid}({name}) 源表无颜色，已填默认 {color}，需美术确认")
        emo_out.append([eid, name, color])
    extra = [e[0] for e in emo_out if e[0] not in prod_emo]
    if extra:
        rep.note("EMOTION_NEW", f"新增情绪 {extra}（源表有、生产表无），需补颜色与 UI 图标")
    dropped = [i for i in prod_emo if i not in {e[0] for e in emo_out}]
    if dropped:
        rep.warn("EMOTION_DROPPED",
                 f"生产表有、源表已删除的情绪 {dropped}（如 {[prod_emo[i][0] for i in dropped]}）——"
                 f"按源表为准**已从导出结果移除**；旧存档里已拥有的这些情绪不会报错，"
                 f"只是不再出现在图鉴里（图鉴按配置表遍历）")

    # ---------- 2. 记忆表 ----------
    rep.h("记忆配置表")
    mem_out = []
    for r in mem_rows:
        mid = cell_str(r[col(mem_h, "MemoryID")])
        name = cell_str(r[col(mem_h, "Name")])
        desc = cell_str(r[col(mem_h, "Description")])
        emos = parse_list(get(r, mem_h, "EmotionTagIDs"))
        # 生产表已有 imageId 列（策划手工加），源表 ImageID 目前都是 placeholder
        img = cell_str(get(r, mem_h, "ImageID"))
        mem_out.append([mid, name, desc, "" if img == "placeholder" else img, ",".join(emos)])
    # ---------- 记忆 ID 语义一致性检查（源表 vs 生产表）----------
    prod_mem = DATAS_DIR / "记忆配置表.xlsx"
    mem_conflicts = []
    if prod_mem.exists():
        pwb = openpyxl.load_workbook(prod_mem, data_only=True)
        pws = pwb.worksheets[0]
        ph = [cell_str(c.value) for c in pws[1]]
        if "id" in ph and "memoryName" in ph:
            ii, ni = ph.index("id"), ph.index("memoryName")
            prod_names = {}
            for row in pws.iter_rows(min_row=5, values_only=True):
                if ii < len(row) and row[ii]:
                    prod_names[cell_str(row[ii])] = cell_str(row[ni]) if ni < len(row) else ""
            src_names = {row[0]: row[1] for row in mem_out}
            for mid, sname in src_names.items():
                pname = prod_names.get(mid)
                if pname and pname != sname:
                    mem_conflicts.append((mid, sname, pname))
            if mem_conflicts:
                rep.h("⚠️ 记忆 ID 语义冲突（源表 vs 生产表）")
                rep.p("生产表 `记忆配置表.xlsx` 是**旧剧情**的记忆体系（M01–M31，一条记忆对应一个"
                      "剧情认知，例如「试着修理」「共同的期待」）；V1.0 源表是**新剧情**体系"
                      "（M01–M19，一条记忆对应一个物品部件观察点 + 少量对话记忆）。"
                      "两者 ID 空间重叠但含义不同，直接覆盖会让旧引用指错记忆：\n")
                rep.p("| 记忆ID | V1.0 剧情含义 | 生产表（旧剧情）含义 |")
                rep.p("|---|---|---|")
                for mid, sname, pname in mem_conflicts:
                    rep.p(f"| {mid} | {sname} | {pname} |")
                # 哪些选项会受影响
                affected = []
                for r in opt_rows:
                    oid = cell_str(r[col(opt_h, "OptionID")])
                    req = parse_list(get(r, opt_h, "ConditionGroupID"))
                    hit = [m for m in req if any(m == c[0] for c in mem_conflicts)]
                    if hit:
                        affected.append((oid, hit))
                if affected:
                    rep.p(f"\n受影响选项 {len(affected)} 个：")
                    for oid, hit in affected:
                        detail = "; ".join(
                            f"{m}（剧情={s} / 生产={p}）"
                            for m, s, p in mem_conflicts if m in hit)
                        rep.p(f"- `{oid}` 需要 {detail}")
                rep.p("\n**结论**：这不是「谁写错了」，而是两代剧情的记忆表并存。"
                      "V1.0 上线就必须整体替换 `记忆配置表.xlsx`（剧情节点表同时整体替换），"
                      "不能只换一部分。")
                prod_only = sorted(set(prod_names) - set(src_names), key=lambda x: (len(x), x))
                if prod_only:
                    rep.p(f"\n生产表有、V1.0 不用的记忆：`{prod_only}`。"
                          f"源表只用到 M01–M19 + M98/M99，与它们**ID 不重叠**，"
                          f"可以原样保留（旧存档兜底），也可以随旧节点表一起清掉。")

    # ---------- 记忆引用 vs 物品归属（以「物品-记忆-情绪对应表」为参照）----------
    if mem_map and Path(mem_map).expanduser().exists():
        mem_item_map, unmapped = load_memory_item_map(Path(mem_map).expanduser())
        if unmapped:
            rep.warn("MEMORY_MAP_UNMAPPED",
                     f"对应表里有 {sorted(unmapped)} 未登记在 CORR_TO_SRC_MEMORY 翻译表中，"
                     f"已按原编号处理，请补翻译表")
        check_memory_alignment(mem_item_map,
                               opt_rows, opt_h, turn_rows, turn_h, rep)

    # ---------- 3. 物品 / 部件 ----------
    rep.h("叙事物品配置表 / 叙事物品部件配置表")
    item_out = []
    for r in item_rows:
        iid = cell_str(r[col(item_h, "ItemID")])
        if not iid:
            continue
        item_out.append([
            iid,
            cell_str(r[col(item_h, "Name")]),
            cell_str(r[col(item_h, "Description")]),
            cell_str(r[col(item_h, "ImageID")]) or "placeholder",
            ",".join(parse_list(get(r, item_h, "ItemPartList"))),
        ])

    part_out = []
    if part_h is None:
        rep.gap("ItemPart", "源文件找不到 ItemPart 子表，项目部件表将无数据")
    else:
        for r in part_rows:
            pid = cell_str(r[col(part_h, "ItemPartID")])
            if not pid:
                continue
            part_out.append([
                pid,
                cell_str(r[col(part_h, "ItemPartName")]),
                "placeholder",                       # 部件图源表未给
                cell_str(r[col(part_h, "MemoryID")]),
                0.5, 0.5,                            # 源表无热点坐标，填画面中心
            ])
        rep.warn("ItemPart.HotspotPlaceholder",
                 "源表 ItemPart 无热点坐标，本版统一填 0.5/0.5（画面正中）占位；"
                 "热点位置后续在 prefab 里重标，本期不处理")

    # ---------- 4. 动作表 ----------
    rep.h("叙事动作配置表")
    act_out = []
    for r in act_rows:
        aid = cell_str(r[col(act_h, "ActionID")])
        if not aid:
            continue
        act_out.append([
            aid,
            cell_str(r[col(act_h, "ActionType")]),
            cell_str(r[col(act_h, "TargetID")]),
            cell_str(r[col(act_h, "InputValue")]),
        ])
    grant_emo = [(a[0], a[2]) for a in act_out if a[1].strip().lower() == "grantemotion"]
    if grant_emo:
        rep.note("ACTION_GRANT_EMOTION",
                 f"GrantEmotion 动作 {grant_emo}：TargetID 填的是**情绪 ID**（不是物品），"
                 f"运行时由 NarrativeActionService 白名单放行，并走 EmotionEmerged 演出链路")

    # ---------- 5. 回复选项表 ----------
    rep.h("回复选项配置表")
    rep_out = []
    npc_conflicts = []
    for r in opt_rows:
        oid = cell_str(r[col(opt_h, "OptionID")])
        if not oid:
            continue
        sid = cell_str(r[col(opt_h, "OptionSetID")])
        req = parse_list(get(r, opt_h, "ConditionGroupID"))
        emos = parse_list(get(r, opt_h, "EmotionID"))

        # AddAffection 形如 {NPC_001:2}，项目是 affectionNpcId + affectionDelta 单值字段
        aff_raw = cell_str(get(r, opt_h, "AddAffection"))
        aff_npc, aff_delta = "", 0
        pairs = re.findall(r"([A-Za-z0-9_\u4e00-\u9fa5]+)\s*:\s*(-?\d+(?:\.\d+)?)", aff_raw)
        if pairs:
            aff_npc, delta_s = pairs[0]
            aff_delta = int(float(delta_s))
            if len(pairs) > 1:
                npc_conflicts.append((oid, aff_raw))
        # 占位 ID -> 真实 npcId（NPC_001=老周 / NPC_002=陈先生 / NPC_003=陈果）
        if aff_npc in NPC_ID_MAP:
            aff_npc = NPC_ID_MAP[aff_npc]
        elif aff_npc.startswith("NPC_"):
            rep.warn("NPC_ID_UNMAPPED",
                     f"选项 {oid} 的好感对象 `{aff_npc}` 不在 NPC_ID_MAP 里，"
                     f"需在 convert_story_v1.py 补充映射，否则好感加不到实际 NPC 身上")

        rep_out.append([
            oid,
            cell_str(r[col(opt_h, "ReplyText")]),
            cell_str(r[col(opt_h, "LockedText")]),
            ",".join(req),
            ",".join(emos),
            "",                                       # rewardMemory：源表无此列
            "",                                       # affectionRules
            cell_str(r[col(opt_h, "NextTurnID")]),
            parse_bool(get(r, opt_h, "EndSession")),
            aff_npc,
            aff_delta,
            sid,
            parse_int(get(r, opt_h, "DisplayOrder"), 1),
            ",".join(parse_list(get(r, opt_h, "ActionSetID"))),
        ])

    if npc_conflicts:
        for oid, raw in npc_conflicts:
            rep.warn("AFFECTION_MULTI_NPC",
                     f"选项 {oid} 的 AddAffection=`{raw}` 含多个 NPC，项目字段只支持单个，仅保留第一个")

    rep.note("Option.RewardMemory",
             "源表「Option」没有「奖励记忆 / RewardMemory」列，转换后 rewardMemory 全空。"
             "这不影响主链路 —— 记忆主要靠「获得物品 -> 检查部件」产出（ItemPart.memoryId），"
             "这条路径在转换里是完整的。只有当某条记忆既没有部件承载、又需要作为选项条件时才要补。"
             "用 check_story_graph.py 可以查出到底是哪几条")

    # ---------- 6. Session ----------
    rep.h("叙事Session配置表")
    ses_out = []
    dates = []
    for r in ses_rows:
        dates.append(excel_date_to_ordinal(get(r, ses_h, "SessionDate")))

    day_values = []
    if day_mode == "date-offset":
        base = next((d for d in dates if d), None)
        last = None
        for d in dates:
            if d is None or base is None:
                day_values.append((day_values[-1] if day_values else 1))
                continue
            v = (d - base).days + 1
            if last is not None and v <= last:
                v = last + 1     # 同一天的两个 Session 顺延，保证严格递增
            day_values.append(v)
            last = v
    else:
        day_values = list(range(1, len(ses_rows) + 1))

    for idx, r in enumerate(ses_rows):
        sid = cell_str(r[col(ses_h, "SessionID")])
        if not sid:
            continue
        npc_raw = cell_str(get(r, ses_h, "NPCSet"))
        npcs = [x for x in re.split(r"[,，、\s]+", npc_raw) if x]
        nexts = parse_list(get(r, ses_h, "NextSessionIDs"))
        if len(nexts) > 1:
            rep.warn("SESSION_MULTI_NEXT",
                     f"Session {sid} 配置了多个后继 {nexts}，项目 nextSessionId 是单值字段，"
                     f"只保留第一个 `{nexts[0]}`，其余分支会丢失")
        bg_raw = cell_str(get(r, ses_h, "BackGroundID"))
        bg_id = BACKGROUND_MAP.get(bg_raw, "")
        if bg_raw and not bg_id:
            rep.warn("BACKGROUND_UNMAPPED",
                     f"Session {sid} 的场景名 `{bg_raw}` 不在 BACKGROUND_MAP 里，backgroundId 留空（会回退按 npcId 选图）")
        ses_out.append([
            sid,
            cell_str(r[col(ses_h, "Name")]),
            cell_str(r[col(ses_h, "StartTurnID")]),
            parse_int(get(r, ses_h, "Priority"), 10),
            day_values[idx],
            nexts[0] if nexts else "",
            cell_str(r[col(ses_h, "MonologText")]),
            "True" if (not nexts and idx == len(ses_rows) - 1) else "False",
            ",".join(npcs),
            bg_id,
        ])

    rep.p(f"- 场景背景：源表 `BackGroundID` 已映射到现有场景占位（{len(BACKGROUND_MAP)} 个映射），"
          f"美术补齐新图后在 BACKGROUND_MAP + BattleMainUI.TryResolveBackgroundLocations 同步加键")
    rep.gap("Session.ConditionGroupID", "项目 Session 表无执行条件字段，源表该列当前全空，暂不影响")
    rep.p(f"\n- dayIndex 换算方式 `{day_mode}`：{day_values}")

    # ---------- 7. Turn（剧情节点） ----------
    rep.h("剧情节点配置表")
    node_out = []
    aff_text_nodes = []
    aff_conversions = []
    for r in turn_rows:
        tid = cell_str(r[col(turn_h, "TurnID")])
        if not tid:
            continue
        set_id = cell_str(get(r, turn_h, "OptionSetID"))
        replies = opt_by_set.get(set_id, []) if set_id else []
        if set_id and not replies:
            rep.warn("OPTION_SET_EMPTY", f"节点 {tid} 引用了选项集 `{set_id}`，但 Option 表里没有对应选项")

        extra = parse_list(get(r, turn_h, "ExtraNextTurnIDs"))
        cond_raw = cell_str(get(r, turn_h, "ConditionGroupID"))
        min_aff = 0.0
        branches = ""

        if cond_raw:
            parsed = parse_affection_condition(cond_raw)
            if parsed:
                lo, hi = parsed
                if extra:
                    # 项目标准写法：好感门槛 + 额外分支 -> BranchRule `ANY,minAff,maxAff,nextNode`
                    branches = ",".join(f"ANY,{int(lo)},{int(hi)},{n}" for n in extra)
                    aff_conversions.append((tid, cond_raw, f"branches=ANY,{int(lo)},{int(hi)},{extra[0]}"))
                    extra = []
                else:
                    # 没有额外分支时是"节点级门槛"：好感不足会跳过本节点走 defaultNextNodeId
                    min_aff = lo
                    aff_conversions.append((tid, cond_raw, f"minAffection={lo:g}（节点级门槛，不足走默认后继）"))
            else:
                aff_text_nodes.append((tid, cond_raw))

        node_out.append([
            tid,
            cell_str(get(r, turn_h, "SpeakerNPCID")),
            cell_str(get(r, turn_h, "Text")),
            ",".join(replies),
            branches,
            parse_bool(get(r, turn_h, "EndSession")),
            cell_str(get(r, turn_h, "DefaultNextTurnID")),
            min_aff,
            cell_str(get(r, turn_h, "TurnType")),
            cell_str(get(r, turn_h, "AnimationAsset")),
            ",".join(parse_list(get(r, turn_h, "OnEnterActionSetID"))),
            ",".join(parse_list(get(r, turn_h, "OnExitActionSetID"))),
            ",".join(extra),
            parse_int(get(r, turn_h, "Priority"), 1),
        ])

    if aff_conversions:
        rep.p(f"\n- 好感条件改写（{len(aff_conversions)} 条，已转成项目支持的数值写法）：")
        for tid, raw, result in aff_conversions:
            rep.p(f"    - `{tid}`：`{raw}` -> `{result}`")

    if aff_text_nodes:
        for tid, raw in aff_text_nodes:
            rep.warn("CONDITION_UNPARSED",
                     f"节点 {tid} 的 ConditionGroupID=`{raw}` 里没有可解析的数值，已按无门槛处理")

    # ---------- 8. 情绪记忆规则 ----------
    # 语义：情绪累计使用次数 >= Threshold，且 RequiredMemoryIds 全部已持有 -> 激活 RewardMemoryIds。
    rep.h("情绪记忆规则配置表")
    rule_out = []
    if "EmotionMemoryRule" in wb.sheetnames:
        rule_h, rule_rows = read_sheet_rows(wb, "EmotionMemoryRule")
        for r in rule_rows:
            rid = cell_str(r[col(rule_h, "RuleID")])
            if not rid:
                continue
            req = parse_list(get(r, rule_h, "ConditionGroupID"))
            reward = parse_list(get(r, rule_h, "RewardMemoryIDs"))
            if not reward:
                rep.warn("RULE_NO_REWARD", f"规则 {rid} 没有配置奖励记忆，永远不会产生效果")
            rule_out.append([
                rid,
                cell_str(r[col(rule_h, "EmotionID")]),
                parse_int(get(r, rule_h, "Threshold"), 0),
                ",".join(req),
                ",".join(reward),
                parse_int(get(r, rule_h, "Priority"), 1),
                cell_str(get(r, rule_h, "RepeatPolicy")) or "OncePerRun",
                parse_int(get(r, rule_h, "MaxTriggerCount"), 0),
            ])
        rep.p(f"- 导入 {len(rule_out)} 条规则（情绪阈值 + 前置记忆 -> 激活记忆）")
        rep.p("- 前置记忆语义：`ConditionGroupID` 里的记忆必须**全部持有**才可能触发，"
              "与情绪阈值是 AND 关系")
    else:
        rep.warn("RULE_SHEET_MISSING", "源表没有 EmotionMemoryRule 页，项目规则表将为空")

    # ---------- 写文件 ----------
    out_dir.mkdir(parents=True, exist_ok=True)
    targets = [
        ("叙事Session配置表.xlsx", "叙事Session配置表",
         [("id", "string", "Session ID"), ("name", "string", "Session名称"),
          ("startNodeId", "string", "起始节点"), ("priority", "int", "优先级"),
          ("dayIndex", "int", "游戏内天数"), ("nextSessionId", "string", "后续Session"),
          ("dayMonologue", "string", "该Session结束后的AI日终独白"),
          ("isFormalEnding", "bool", "是否在日终后进入正式结局"),
          ("npcSet", "string", "按剧情配表 Session.NPCSet 的顺序填写 NPC 名，逗号分隔；用于 NPC 对话左右站位"),
          ("backgroundId", "string", "场景背景 ID；为空时回退到按 npcId 选择（旧逻辑）")],
         ses_out),
        ("剧情节点配置表.xlsx", "剧情节点配置表",
         [("id", "string", "节点ID"), ("npcId", "string", "NPC ID"), ("npcText", "string", "NPC文本"),
          ("replies", "(list#sep=,),string", "回复选项ID(逗号分隔)"),
          ("branches", "(list#sep=;),BranchRule", "分支规则"),
          ("isSessionEnd", "bool", "是否会话结束"), ("defaultNextNodeId", "string", "默认后继节点"),
          ("minAffection", "float", "最低说话人好感"), ("turnType", "string", "Turn类型"),
          ("animationAsset", "string", "演出资源"),
          ("onEnterActionIds", "(list#sep=,),string", "进入动作"),
          ("onExitActionIds", "(list#sep=,),string", "离开动作"),
          ("extraNextNodeIds", "(list#sep=,),string", "额外后继候选"),
          ("priority", "int", "节点优先级")],
         node_out),
        ("回复选项配置表.xlsx", "回复选项配置表",
         [("id", "string", "回复ID"), ("replyText", "string", "正常回复"),
          ("lockedText", "string", "锁定文本"),
          ("requiredMemory", "(list#sep=,),string", "需要记忆(逗号分隔)"),
          ("allowedEmotion", "(list#sep=,),string", "允许情绪(逗号分隔)"),
          ("rewardMemory", "(list#sep=,),string", "奖励记忆(逗号分隔)"),
          ("affectionRules", "(list#sep=;),EmotionAffectionRule", "情绪好感规则(元素;分隔,字段,分隔)"),
          ("nextNodeId", "string", "选项后继节点"), ("isSessionEnd", "bool", "选项结束Session"),
          ("affectionNpcId", "string", "好感变化对应NPC"), ("affectionDelta", "float", "NPC好感变化值"),
          ("optionSetId", "string", "回复所属选项集"), ("displayOrder", "int", "回复显示顺序"),
          ("actionIds", "(list#sep=,),string", "回复选择动作")],
         rep_out),
        ("记忆配置表.xlsx", "记忆配置表",
         [("id", "string", "记忆ID"), ("memoryName", "string", "记忆名"),
          ("description", "string", "描述"), ("imageId", "string", "图片资源(空=无图)"),
          ("emotionCardSet", "(list#sep=,),string", "关联情绪ID(逗号分隔)")],
         mem_out),
        ("情绪配置表.xlsx", "情绪配置表",
         [("id", "string", "情绪ID"), ("emotionName", "string", "情绪名"),
          ("displayColor", "string", "占位颜色 #RRGGBB")],
         emo_out),
        ("叙事物品配置表.xlsx", "Sheet1",
         [("id", "string", "唯一ID"), ("itemName", "string", "物品名称"),
          ("description", "string", "物品描述"), ("imageId", "string", "图片资源"),
          ("partIds", "(list#sep=,),string", "部件ID列表")],
         item_out),
        ("叙事物品部件配置表.xlsx", "Sheet1",
         [("id", "string", "唯一ID"), ("partName", "string", "部件名称"),
          ("imageId", "string", "图片资源"), ("memoryId", "string", "关联记忆"),
          ("hotspotX", "float", "图片热点横坐标，归一化0到1"),
          ("hotspotY", "float", "图片热点纵坐标，归一化0到1")],
         part_out),
        ("叙事动作配置表.xlsx", "Sheet1",
         [("id", "string", "唯一ID"), ("actionType", "string", "动作类型"),
          ("targetId", "string", "目标物品"), ("inputValue", "string", "输入参数")],
         act_out),
        ("情绪记忆规则配置表.xlsx", "情绪记忆规则配置表",
         [("id", "string", "规则ID"),
          ("emotionId", "string", "被检查的情绪ID"),
          ("threshold", "int", "情绪值阈值（该情绪被使用/表达的累计次数）"),
          ("requiredMemoryIds", "(list#sep=,),string", "前置记忆：必须已持有这些记忆才可能激活，逗号分隔"),
          ("rewardMemoryIds", "(list#sep=,),string", "激活奖励：条件满足时获得的记忆，逗号分隔"),
          ("priority", "int", "多条规则同时满足时的执行顺序，数值越大越优先"),
          ("repeatPolicy", "string", "OncePerRun / OncePerDay / Repeatable"),
          ("maxTriggerCount", "int", "整局最多触发次数，0 表示不限")],
         rule_out),
    ]

    written = []
    for filename, sheet_name, headers, rows in targets:
        p = write_luban_sheet(out_dir / filename, sheet_name, headers, None, rows)
        written.append((filename, len(rows)))

    rep.h("已生成文件")
    rep.p("| 文件 | 数据行 |\n|---|---|")
    for fn, n in written:
        rep.p(f"| `{fn}` | {n} |")

    rep.h("转换缺口汇总（必须人工决策）")
    if rep.gaps:
        for field, msg in rep.gaps:
            rep.p(f"- **{field}**：{msg}")
    else:
        rep.p("- 无")

    rep.h("警告汇总")
    if rep.warnings:
        for code, msg in rep.warnings:
            rep.p(f"- [{code}] {msg}")
    else:
        rep.p("- 无")

    report_path = out_dir / "import_report.md"
    rep.dump(report_path)
    return out_dir, report_path, written, rep


def main():
    ap = argparse.ArgumentParser(description="剧情配表 V1.0 -> DGame Luban 表")
    ap.add_argument("--src", required=True, help="策划版剧情配表 xlsx 路径")
    ap.add_argument("--out", default=str(DEFAULT_OUT), help="输出目录，默认 Datas/_import_v1_preview")
    ap.add_argument("--apply", action="store_true", help="直接覆盖 GameConfig/Datas 下的生产表")
    ap.add_argument("--with-static", action="store_true",
                    help="连同记忆/情绪/物品/部件/动作表一起覆盖（默认不覆盖，避免冲掉手工整理成果）")
    ap.add_argument("--day-mode", choices=["date-offset", "sequential"], default="date-offset",
                    help="dayIndex 换算：date-offset=按 SessionDate 相对天数；sequential=按顺序 1..N")
    ap.add_argument("--memory-map", default="",
                    help="物品-记忆-情绪对应表 xlsx，用于核对 requiredMemory 引用的记忆是否属于当前节点物品")
    args = ap.parse_args()

    src = Path(args.src).expanduser().resolve()
    if not src.exists():
        sys.exit(f"源文件不存在：{src}")

    out_dir = Path(args.out).expanduser().resolve()
    convert(src, out_dir, False, args.day_mode, args.memory_map)

    print(f"预览已生成：{out_dir}")
    print(f"报告：{out_dir / 'import_report.md'}")

    if args.apply:
        backup_dir = DATAS_DIR / "_backup_before_v1_import"
        backup_dir.mkdir(exist_ok=True)
        written_names = []
        for f in out_dir.glob("*.xlsx"):
            if not args.with_static and f.name not in CORE_FILES:
                continue
            target = DATAS_DIR / f.name
            if target.exists():
                shutil.copy2(target, backup_dir / f.name)
            shutil.copy2(f, target)
            written_names.append(f.name)
        print(f"已写入生产表 {len(written_names)} 张：{written_names}")
        if not args.with_static:
            skipped = [f.name for f in out_dir.glob("*.xlsx") if f.name not in CORE_FILES]
            print(f"未覆盖（手工维护表，需 --with-static 才写）：{skipped}")
        print(f"旧表备份在：{backup_dir}")
        print("下一步：bash GameConfig/GenerateTool_Binary/gen_bin_client_lazyload.sh")
    else:
        print("未改动生产表。确认无误后加 --apply 写入（会自动备份旧表）。")
        print("默认只覆盖 Session/剧情节点/回复选项/情绪记忆规则；"
              "记忆/情绪/物品等手工维护表需加 --with-static 才会写入。")


if __name__ == "__main__":
    main()
