#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
剧情支线可达性校验器

两种输入都支持：
    # A. 直接校验策划版源表（改完表先自查，不用先导入）
    python GameConfig/Tools/story_import/check_story_graph.py \
        --src ~/Desktop/副本剧情配表_V1.0(1).xlsx

    # B. 校验导入后的项目 Luban 表
    python GameConfig/Tools/story_import/check_story_graph.py \
        --datas GameConfig/Datas

检查分三层，逐层收紧：
    L1 结构可达   —— 图遍历，所有 Turn / Session 能不能从入口走到
    L2 条件可达   —— 考虑 requiredMemory 锁定，玩家真能选到的路径
    L3 支线覆盖   —— 每个节点是否至少有一条"实际可走"的路径到达

判定"可达"的定义（必须和运行时一致）：
    · Session 之间只能靠 nextSessionId 链式推进（NarrativeStoryScheduler）
    · Turn 出口优先级：reply.nextNodeId > extraNextNodeIds > branches > defaultNextNodeId
      （NarrativeDomainService.ResolveNextNode）
    · isSessionEnd=True 的节点是 Session 终点，不再有后继
    · Option 有 requiredMemory 时，只有记忆全部持有时才可选
"""

import argparse
import re
import sys
from collections import defaultdict, deque
from pathlib import Path

try:
    import openpyxl
except ImportError:
    sys.exit("缺少 openpyxl，请先执行：pip install openpyxl")

REPO_ROOT = Path(__file__).resolve().parents[3]
DATAS_DIR = REPO_ROOT / "GameConfig" / "Datas"


# ----------------------------------------------------------------------------
# 通用模型：一张有向图 + 可用条件
# ----------------------------------------------------------------------------

class StoryGraph:
    def __init__(self):
        self.sessions = {}       # sid -> {name, start, nexts[], day}
        self.nodes = {}          # nid -> {npc, text, replies[], next, extras[], branches[], end, minAff}
        self.options = {}        # oid -> {req[], reward[], next, end, text, set, emo[]}
        self.rules = {}          # rid -> {emotion, threshold, required[], reward[], priority}
        self.all_memories = set()
        self.source_label = ""

    def option_edges(self, nid):
        """该节点通过选项可以到达的节点（忽略锁定条件）"""
        out = []
        for oid in self.nodes[nid]["replies"]:
            o = self.options.get(oid)
            if o and o["next"]:
                out.append((o["next"], oid))
            elif o and o["end"]:
                pass  # 选项直接结束 Session
        return out

    def node_edges(self, nid, satisfied=None):
        """
        返回 [(nextNid, 走法说明)]。satisfied 为 None 时不考虑锁定条件。
        satisfied 是"当前已持有记忆集合"，用于 L2。
        """
        n = self.nodes[nid]
        out = []
        for oid in n["replies"]:
            o = self.options.get(oid)
            if not o:
                continue
            if satisfied is not None and not set(o["req"]) <= satisfied:
                continue
            if o["next"]:
                out.append((o["next"], f"reply:{oid}"))
        for x in n["extras"]:
            out.append((x, "extraNext"))
        for b in n["branches"]:
            if b.get("next"):
                out.append((b["next"], f"branch:{b.get('rule', '')}"))
        if n["next"]:
            out.append((n["next"], "defaultNext"))
        return [(t, why) for t, why in out if t]

    def reachable(self, satisfaction=None, stop_at_end=True):
        """
        从所有 Session 起点做 BFS。
        satisfaction=None -> L1 纯结构；传 set() -> L2 条件可达。
        返回 (visited, parents)
        """
        visited = set()
        parents = {}
        dq = deque()
        for sid, s in self.sessions.items():
            if s["start"]:
                dq.append(s["start"])
                parents.setdefault(s["start"], (None, f"session:{sid}"))
        while dq:
            nid = dq.popleft()
            if nid in visited or nid not in self.nodes:
                continue
            visited.add(nid)
            if stop_at_end and self.nodes[nid]["end"]:
                continue
            for nxt, why in self.node_edges(nid, satisfaction):
                if nxt not in visited and nxt not in parents:
                    parents[nxt] = (nid, why)
                    dq.append(nxt)
                elif nxt not in visited:
                    dq.append(nxt)
        return visited, parents

    def emotion_capacity(self, emotion_id, structural):
        """
        该情绪在本局内最多能被使用几次的乐观上限：
        统计可达节点里"允许该情绪或被空列表放行"的选项数量（每个 Turn 只能走一次）。
        只是上限估计，实际还受玩家选择与前置记忆影响。
        """
        count = 0
        for nid in structural:
            for oid in self.nodes[nid]["replies"]:
                o = self.options.get(oid)
                if not o:
                    continue
                allowed = o.get("emo") or []
                if not allowed or emotion_id in allowed:
                    count += 1
        return count

    def memory_closure(self):
        """
        计算"最终可能持有的记忆全集"。
        来源有两条：
          1. 选项 rewardMemory：所需记忆满足即可获得
          2. 情绪记忆规则：前置记忆满足 + 情绪使用次数上限达到阈值
        从空记忆开始反复扫描，直到不再变化。
        """
        satisfiable = set()
        structural, _ = self.reachable(None, stop_at_end=True)
        self._structural = structural
        changed = True
        guard = 0
        while changed and guard < 64:
            guard += 1
            changed = False
            for nid in structural:
                for oid in self.nodes[nid]["replies"]:
                    o = self.options.get(oid)
                    if not o:
                        continue
                    if set(o["req"]) <= satisfiable:
                        for m in o["reward"]:
                            if m not in satisfiable:
                                satisfiable.add(m)
                                changed = True
            for rid, rule in self.rules.items():
                if not set(rule["required"]) <= satisfiable:
                    continue
                if self.emotion_capacity(rule["emotion"], structural) < rule["threshold"]:
                    continue
                for m in rule["reward"]:
                    if m not in satisfiable:
                        satisfiable.add(m)
                        changed = True
        return satisfiable


# ----------------------------------------------------------------------------
# Loader A：策划版源表
# ----------------------------------------------------------------------------

def _cs(v):
    if v is None:
        return ""
    if isinstance(v, bool):
        return "True" if v else "False"
    return str(v).strip()


def _list(v):
    s = _cs(v).strip("[]")
    return [x.strip() for x in re.split(r"[,;、]", s) if x.strip()]


def _rows(wb, name):
    if name not in wb.sheetnames:
        return [], []
    ws = wb[name]
    hdr, out = [], []
    for i, row in enumerate(ws.iter_rows(values_only=True), 1):
        vals = list(row)
        if i == 1:
            hdr = [_cs(c) for c in vals]
            continue
        # 标记行（##var / ##type / ##group / ## 说明）整行跳过
        if vals and _cs(vals[0]).startswith("##"):
            continue
        first = _cs(vals[1]) if len(vals) > 1 else ""
        if not first or first.startswith(("#", "【")) or first in ("ItemPartID", "ItemPart", "TurnID", "ItemID"):
            continue
        out.append(vals)
    return hdr, out


def _idx(h, n):
    try:
        return h.index(n)
    except ValueError:
        return -1


def _g(vals, h, n, d=None):
    i = _idx(h, n)
    if i < 0 or i >= len(vals):
        return d
    return vals[i] if _cs(vals[i]) else d


def load_source(path):
    wb = openpyxl.load_workbook(path, data_only=True)
    g = StoryGraph()
    g.source_label = f"策划源表 {Path(path).name}"

    sh, sr = _rows(wb, "Session")
    th, tr = _rows(wb, "Turn")
    oh, orw = _rows(wb, "Option")

    for r in sr:
        sid = _cs(r[_idx(sh, "SessionID")])
        if not sid:
            continue
        nxt = _list(_g(r, sh, "NextSessionIDs"))
        g.sessions[sid] = {
            "name": _cs(_g(r, sh, "Name")),
            "start": _cs(_g(r, sh, "StartTurnID")),
            "nexts": nxt,
            "day": _g(r, sh, "SessionDate"),
        }

    for r in orw:
        oid = _cs(r[_idx(oh, "OptionID")])
        if not oid:
            continue
        g.options[oid] = {
            "req": _list(_g(r, oh, "ConditionGroupID")),
            "reward": _list(_g(r, oh, "RewardMemory")),   # 源表通常没有这列
            "next": _cs(_g(r, oh, "NextTurnID")),
            "end": _cs(_g(r, oh, "EndSession")).lower() == "true",
            "text": _cs(_g(r, oh, "ReplyText")),
            "set": _cs(_g(r, oh, "OptionSetID")),
            "emo": _list(_g(r, oh, "EmotionID")),
        }

    for r in tr:
        nid = _cs(r[_idx(th, "TurnID")])
        if not nid:
            continue
        set_id = _cs(_g(r, th, "OptionSetID"))
        replies = [o for o, v in g.options.items() if v["set"] == set_id] if set_id else []
        cond = _cs(_g(r, th, "ConditionGroupID"))
        nums = re.findall(r"\d+", cond)
        branches = []
        extras = _list(_g(r, th, "ExtraNextTurnIDs"))
        if nums and extras:
            branches = [{"rule": "ANY", "min": int(nums[0]), "max": 101, "next": extras[0]}]
        g.nodes[nid] = {
            "npc": _cs(_g(r, th, "SpeakerNPCID")),
            "text": _cs(_g(r, th, "Text")),
            "replies": replies,
            "next": _cs(_g(r, th, "DefaultNextTurnID")),
            "extras": extras if not branches else [],
            "branches": branches,
            "end": _cs(_g(r, th, "EndSession")).lower() == "true",
            "minAff": float(nums[0]) if nums and not branches else 0.0,
        }

    mh, mr = _rows(wb, "Memory")
    for r in mr:
        mid = _cs(r[_idx(mh, "MemoryID")])
        if mid:
            g.all_memories.add(mid)

    rh, rr = _rows(wb, "EmotionMemoryRule")
    for r in rr:
        rid = _cs(r[_idx(rh, "RuleID")])
        if not rid:
            continue
        g.rules[rid] = {
            "emotion": _cs(_g(r, rh, "EmotionID")),
            "threshold": int(_g(r, rh, "Threshold") or 0),
            "required": _list(_g(r, rh, "ConditionGroupID")),
            "reward": _list(_g(r, rh, "RewardMemoryIDs")),
        }
    return g


# ----------------------------------------------------------------------------
# Loader B：项目 Luban 表
# ----------------------------------------------------------------------------

def load_luban(datas_dir):
    g = StoryGraph()
    g.source_label = f"Luban 表 {datas_dir}"

    def sheet_rows(fname):
        p = datas_dir / fname
        if not p.exists():
            return [], []
        wb = openpyxl.load_workbook(p, data_only=True)
        ws = wb.worksheets[0]
        hdr = []
        out = []
        for i, row in enumerate(ws.iter_rows(values_only=True), 1):
            vals = list(row)
            if i == 1:
                hdr = [_cs(c) for c in vals]
                continue
            if vals and _cs(vals[0]).startswith("##"):
                continue
            if not _cs(vals[1] if len(vals) > 1 else ""):
                continue
            out.append(vals)
        return hdr, out

    sh, sr = sheet_rows("叙事Session配置表.xlsx")
    th, tr = sheet_rows("剧情节点配置表.xlsx")
    oh, orw = sheet_rows("回复选项配置表.xlsx")
    mh, mr = sheet_rows("记忆配置表.xlsx")

    for r in sr:
        sid = _cs(r[_idx(sh, "id")])
        if not sid:
            continue
        g.sessions[sid] = {
            "name": _cs(_g(r, sh, "name")),
            "start": _cs(_g(r, sh, "startNodeId")),
            "nexts": [x for x in [_cs(_g(r, sh, "nextSessionId"))] if x],
            "day": _g(r, sh, "dayIndex"),
        }

    for r in orw:
        oid = _cs(r[_idx(oh, "id")])
        if not oid:
            continue
        g.options[oid] = {
            "req": _list(_g(r, oh, "requiredMemory")),
            "reward": _list(_g(r, oh, "rewardMemory")),
            "next": _cs(_g(r, oh, "nextNodeId")),
            "end": _cs(_g(r, oh, "isSessionEnd")).lower() == "true",
            "text": _cs(_g(r, oh, "replyText")),
            "set": _cs(_g(r, oh, "optionSetId")),
            "emo": _list(_g(r, oh, "allowedEmotion")),
        }

    for r in tr:
        nid = _cs(r[_idx(th, "id")])
        if not nid:
            continue
        branches = []
        for item in _list(_g(r, th, "branches")):
            parts = item.split(",")
            if len(parts) >= 4:
                branches.append({"rule": parts[0], "min": parts[1], "max": parts[2], "next": parts[3]})
        g.nodes[nid] = {
            "npc": _cs(_g(r, th, "npcId")),
            "text": _cs(_g(r, th, "npcText")),
            "replies": _list(_g(r, th, "replies")),
            "next": _cs(_g(r, th, "defaultNextNodeId")),
            "extras": _list(_g(r, th, "extraNextNodeIds")),
            "branches": branches,
            "end": _cs(_g(r, th, "isSessionEnd")).lower() == "true",
            "minAff": float(_g(r, th, "minAffection") or 0),
        }

    for r in mr:
        mid = _cs(r[_idx(mh, "id")])
        if mid:
            g.all_memories.add(mid)

    rh, rr = sheet_rows("情绪记忆规则配置表.xlsx")
    for r in rr:
        rid = _cs(r[_idx(rh, "id")])
        if not rid:
            continue
        g.rules[rid] = {
            "emotion": _cs(_g(r, rh, "emotionId")),
            "threshold": int(_g(r, rh, "threshold") or 0),
            "required": _list(_g(r, rh, "requiredMemoryIds")),
            "reward": _list(_g(r, rh, "rewardMemoryIds")),
        }
    return g


# ----------------------------------------------------------------------------
# 分析
# ----------------------------------------------------------------------------

class Report:
    def __init__(self):
        self.buf = []
        self.fail = 0
        self.warn = 0

    def title(self, t):
        self.buf.append(f"\n## {t}\n")

    def ok(self, t):
        self.buf.append(f"- ✅ {t}")

    def bad(self, t):
        self.fail += 1
        self.buf.append(f"- ❌ {t}")

    def note(self, t):
        self.warn += 1
        self.buf.append(f"- ⚠️ {t}")

    def text(self, t=""):
        self.buf.append(t)


def analyze(g):
    r = Report()
    r.buf.append(f"# 剧情支线可达性报告\n")
    r.buf.append(f"- 数据源：{g.source_label}")
    r.buf.append(f"- Session {len(g.sessions)} / Turn {len(g.nodes)} / Option {len(g.options)} / Memory {len(g.all_memories)}\n")

    # ---------------- L1 结构 ----------------
    r.title("L1 结构可达（不考虑锁定条件）")

    # Session 链
    starts = [s for s in g.sessions.values() if s["start"]]
    if not starts:
        r.bad("没有任何 Session 配置了起始节点")
    chained = set()
    # 找出"没有前驱"的 Session = 入口
    has_prev = {n for s in g.sessions.values() for n in s["nexts"]}
    for sid, s in g.sessions.items():
        if sid not in has_prev:
            cur, guard = sid, 0
            while cur and guard < 200:
                chained.add(cur)
                nxt = g.sessions.get(cur, {}).get("nexts") or []
                cur = nxt[0] if nxt else None
                guard += 1
    orphan_ses = [s for s in g.sessions if s not in chained]
    if orphan_ses:
        for s in orphan_ses:
            r.bad(f"Session `{s}`（{g.sessions[s]['name']}）不在任何入口链上，玩家永远进不去")
    else:
        r.ok(f"Session 链完整：入口 {' -> '.join(sorted(chained))}")

    # 悬空引用
    dangling = []
    for nid, n in g.nodes.items():
        for t in [n["next"]] + n["extras"] + [b["next"] for b in n["branches"]]:
            if t and t not in g.nodes:
                dangling.append(f"Turn `{nid}` -> 不存在的节点 `{t}`")
        for oid in n["replies"]:
            if oid not in g.options:
                dangling.append(f"Turn `{nid}` -> 不存在的选项 `{oid}`")
    for oid, o in g.options.items():
        if o["next"] and o["next"] not in g.nodes:
            dangling.append(f"Option `{oid}` -> 不存在的节点 `{o['next']}`")
        for m in o["req"] + o["reward"]:
            if g.all_memories and m not in g.all_memories:
                dangling.append(f"Option `{oid}` -> 不存在的记忆 `{m}`")
    for sid, s in g.sessions.items():
        if s["start"] and s["start"] not in g.nodes:
            dangling.append(f"Session `{sid}` -> 不存在的起始节点 `{s['start']}`")
        for n in s["nexts"]:
            if n not in g.sessions:
                dangling.append(f"Session `{sid}` -> 不存在的后继 `{n}`")
    if dangling:
        for d in dangling[:40]:
            r.bad(d)
        if len(dangling) > 40:
            r.bad(f"…… 共 {len(dangling)} 条悬空引用")
    else:
        r.ok("无悬空引用（节点/选项/记忆/Session 全部指向存在的 ID）")

    visited, _ = g.reachable(None)
    unreach = sorted(set(g.nodes) - visited)
    if unreach:
        r.bad(f"{len(unreach)} 个 Turn 从任何 Session 起点都走不到")
        for nid in unreach[:30]:
            r.text(f"    - `{nid}` {g.nodes[nid]['npc']}：{(g.nodes[nid]['text'] or '')[:36]}")
    else:
        r.ok(f"全部 {len(g.nodes)} 个 Turn 结构可达")

    # 无出口节点
    dead = []
    for nid, n in g.nodes.items():
        if n["end"]:
            continue
        if not n["next"] and not n["extras"] and not n["branches"] and not n["replies"]:
            dead.append(nid)
    if dead:
        for nid in dead[:20]:
            r.bad(f"Turn `{nid}` 非结束节点，却没有任何出口（运行时会抛 No branch matched）")
        if len(dead) > 20:
            r.bad(f"…… 共 {len(dead)} 个断头节点")
    else:
        r.ok("所有非结束节点都有出口")

    # 每个 Session 的覆盖
    r.text("\n**各 Session 覆盖的 Turn 数（以 isSessionEnd 为边界）**\n")
    r.text("| Session | 起点 | 覆盖 Turn |")
    r.text("|---|---|---|")
    for sid in sorted(g.sessions):
        s = g.sessions[sid]
        if not s["start"] or s["start"] not in g.nodes:
            r.text(f"| {sid} | {s['start']} | 起点缺失 |")
            continue
        seen = set()
        dq = deque([s["start"]])
        while dq:
            nid = dq.popleft()
            if nid in seen or nid not in g.nodes:
                continue
            seen.add(nid)
            if g.nodes[nid]["end"]:
                continue
            for nxt, _ in g.node_edges(nid, None):
                if nxt not in seen:
                    dq.append(nxt)
        r.text(f"| {sid} | {s['start']} | {len(seen)} |")

    # 跨 Session 归属歧义
    overlap = defaultdict(set)
    for sid in sorted(g.sessions):
        s = g.sessions[sid]
        if not s["start"] or s["start"] not in g.nodes:
            continue
        seen = set()
        dq = deque([s["start"]])
        while dq:
            nid = dq.popleft()
            if nid in seen or nid not in g.nodes:
                continue
            seen.add(nid)
            if g.nodes[nid]["end"]:
                continue
            for nxt, _ in g.node_edges(nid, None):
                if nxt not in seen:
                    dq.append(nxt)
        for nid in seen:
            overlap[nid].add(sid)
    multi = {k: v for k, v in overlap.items() if len(v) > 1}
    if multi:
        r.note(f"{len(multi)} 个 Turn 可以从多个 Session 走到（可能是有意复用，也可能是漏配 EndSession）")
        for nid, sids in list(multi.items())[:10]:
            r.text(f"    - `{nid}` 可由 {sorted(sids)} 抵达")

    # ---------------- L2 条件可达 ----------------
    r.title("L2 条件可达（考虑 requiredMemory 锁定）")

    structural = g.reachable(None)[0]
    if g.rules:
        r.text(f"\n**情绪记忆规则 {len(g.rules)} 条**（情绪值达阈值 且 前置记忆齐备 -> 激活记忆）\n")
        r.text("| 规则 | 情绪 | 阈值 | 情绪使用上限 | 前置记忆 | 奖励记忆 | 判定 |")
        r.text("|---|---|---|---|---|---|---|")
        unreachable_threshold = 0
        for rid in sorted(g.rules):
            rule = g.rules[rid]
            cap = g.emotion_capacity(rule["emotion"], structural)
            enough = cap >= rule["threshold"]
            if not enough:
                unreachable_threshold += 1
            r.text(f"| {rid} | {rule['emotion']} | {rule['threshold']} | {cap} | "
                   f"{','.join(rule['required']) or '—'} | {','.join(rule['reward']) or '—'} | "
                   f"{'可达' if enough else '阈值够不到'} |")
        if unreachable_threshold:
            r.bad(f"{unreachable_threshold} 条规则的情绪阈值超过本局可选次数上限，奖励记忆永远拿不到")
        else:
            r.ok("所有规则的情绪阈值都能在本局内达到（上限为乐观估计）")
    else:
        r.note("没有配置任何情绪记忆规则，记忆只能靠选项 rewardMemory 获得")

    # 记忆来源盘点：每条被 require 的记忆，是否真的有地方产出
    required_by = defaultdict(list)     # mid -> [option id]
    for oid, o in g.options.items():
        for m in o["req"]:
            required_by[m].append(oid)
    source_desc = defaultdict(list)     # mid -> [来源说明]
    for oid, o in g.options.items():
        for m in o["reward"]:
            source_desc[m].append(f"选项 {oid} 奖励")
    for rid, rule in g.rules.items():
        for m in rule["reward"]:
            source_desc[m].append(f"规则 {rid} 激活")

    missing = {m: oids for m, oids in required_by.items() if not source_desc.get(m)}
    if missing:
        r.text(f"\n**记忆来源缺口 {len(missing)} 个**（这些记忆被选项需要，但没有任何产出途径）\n")
        r.text("| 记忆 | 需要它的选项 | 影响 |")
        r.text("|---|---|---|")
        for mid in sorted(missing):
            oids = missing[mid]
            r.text(f"| {mid} | {', '.join(oids[:4])}{' …' if len(oids) > 4 else ''} | "
                   f"{len(oids)} 个选项锁死 |")
        r.bad(f"{len(missing)} 个记忆没有任何产出途径，对应 {len({o for v in missing.values() for o in v})} "
              f"个选项永久锁死；需要在 Option 补 rewardMemory，或在【情绪记忆规则配置表】里补规则")
    elif required_by:
        r.ok("每个被需要的记忆都有产出途径")


    no_route_options = [oid for oid, o in g.options.items() if not o["next"] and not o["end"]]
    if no_route_options:
        for oid in no_route_options[:10]:
            holder = [nid for nid, n in g.nodes.items() if oid in n["replies"]]
            fallback = [g.nodes[n]["next"] for n in holder if n in g.nodes and g.nodes[n]["next"]]
            if fallback:
                r.note(f"Option `{oid}` 没配 nextNodeId 也不是结束，会静默走节点默认后继 `{fallback[0]}`"
                       f"（若本意是分支，该意图会丢失）")
            else:
                r.bad(f"Option `{oid}` 没配 nextNodeId 也不是结束，且所在节点没有默认后继，选中后运行时会抛错")

    satisfiable = g.memory_closure()
    locked = [oid for oid, o in g.options.items() if not set(o["req"]) <= satisfiable]
    if locked:
        r.bad(f"{len(locked)} 个选项在前置记忆里有缺口，无法解锁（明细见上方来源缺口表）")
    elif any(o["req"] for o in g.options.values()):
        r.ok(f"所有带记忆门槛的选项都可满足（可获记忆 {len(satisfiable)} 个）")
    else:
        r.note("整张表没有任何选项配置 requiredMemory，L2 与 L1 等价")

    # 自锁：需要 M，而 M 只能从需要 M 的选项获得
    rewarders = defaultdict(set)
    for oid, o in g.options.items():
        for m in o["reward"]:
            rewarders[m].add(oid)
    self_locked = []
    for oid, o in g.options.items():
        for m in o["req"]:
            srcs = rewarders.get(m, set())
            if srcs and srcs <= {oid}:
                self_locked.append((oid, m))
    if self_locked:
        for oid, m in self_locked[:15]:
            r.bad(f"自锁死：Option `{oid}` 需要记忆 `{m}`，而 `{m}` 只能由它自己产出")
    else:
        r.ok("无记忆自锁死（没有选项依赖只有自己能产出的记忆）")

    # 首节点可选项
    no_start_choice = []
    for sid, s in g.sessions.items():
        if not s["start"] or s["start"] not in g.nodes:
            continue
        n = g.nodes[s["start"]]
        if not n["replies"]:
            continue
        free = [oid for oid in n["replies"] if not set(g.options.get(oid, {}).get("req", []))]
        if not free:
            no_start_choice.append(f"Session `{sid}` 起点 `{s['start']}` 的所有选项都带记忆门槛，新档会卡死")
    if no_start_choice:
        for x in no_start_choice:
            r.bad(x)
    else:
        r.ok("每个 Session 起点都有不需要前置记忆的选项")

    # ---------------- L3 条件化支线覆盖 ----------------
    r.title("L3 支线覆盖（只走实际能选的选项）")
    cond_visited, _ = g.reachable(satisfiable)
    cond_unreach = sorted(set(g.nodes) - cond_visited)
    if cond_unreach:
        r.bad(f"{len(cond_unreach)} 个 Turn 结构可达但玩家实际走不到（被记忆门槛挡住）")
        for nid in cond_unreach[:30]:
            r.text(f"    - `{nid}` {g.nodes[nid]['npc']}：{(g.nodes[nid]['text'] or '')[:36]}")
        if len(cond_unreach) > 30:
            r.text(f"    - …… 共 {len(cond_unreach)} 个")
    else:
        r.ok(f"全部 {len(g.nodes)} 个 Turn 在记忆门槛下依然可达")

    # 未被任何选项引用的孤立选项
    used_options = {oid for n in g.nodes.values() for oid in n["replies"]}
    orphan_opts = sorted(set(g.options) - used_options)
    if orphan_opts:
        for oid in orphan_opts[:15]:
            r.note(f"Option `{oid}` 没有被任何 Turn 引用（孤儿选项）")
    else:
        r.ok("所有 Option 都被至少一个 Turn 引用")

    # ---------------- 结论 ----------------
    r.title("结论")
    if r.fail == 0:
        r.text(f"**通过**：0 个错误，{r.warn} 个提醒。")
    else:
        r.text(f"**不通过**：{r.fail} 个错误，{r.warn} 个提醒。上面标 ❌ 的都需要修。")
    return r


def main():
    ap = argparse.ArgumentParser(description="剧情支线可达性校验")
    grp = ap.add_mutually_exclusive_group(required=True)
    grp.add_argument("--src", help="策划版剧情配表 xlsx")
    grp.add_argument("--datas", help="Luban 表目录，如 GameConfig/Datas")
    ap.add_argument("--out", help="报告输出路径（.md），不填只打印")
    args = ap.parse_args()

    if args.src:
        p = Path(args.src).expanduser().resolve()
        if not p.exists():
            sys.exit(f"文件不存在：{p}")
        g = load_source(p)
    else:
        d = Path(args.datas).expanduser().resolve()
        if not d.exists():
            sys.exit(f"目录不存在：{d}")
        g = load_luban(d)

    rep = analyze(g)
    out = "\n".join(rep.buf)
    print(out)
    if args.out:
        Path(args.out).expanduser().write_text(out, encoding="utf-8")
        print(f"\n报告已写入：{args.out}")

    sys.exit(0 if rep.fail == 0 else 1)


if __name__ == "__main__":
    main()
