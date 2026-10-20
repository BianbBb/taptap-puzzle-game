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
        self.options = {}        # oid -> {req[], reward[], next, end, text, set, emo[], aff}
        self.rules = {}          # rid -> {emotion, threshold, required[], reward[], priority}
        self.items = {}          # itemId -> {name, parts[]}
        self.parts = {}          # partId -> {name, memory, item}
        self.node_items = {}     # nid -> [itemId]  进入/离开该节点时发放的物品
        self.mem_emotions = {}   # mid -> [emotionId]   记忆标注的情绪（涌现的来源）
        self.emotions = {}       # eid -> name
        self.all_memories = set()
        self.source_label = ""

    # ---- 记忆 -> 情绪涌现 ----

    def emergent_emotions(self, owned):
        """
        复刻运行时 NarrativeDomainService.FindEmergentEmotions 的口径：
        **两段及以上**已拥有记忆的 EmotionCardSet 含同一情绪 -> 该情绪涌现。
        （一段记忆对同一情绪只算一次。）

        返回 (emotions, detail)：emotions 是能涌现的情绪集合，detail 是 eid -> [记忆...]
        """
        sources = defaultdict(list)
        for mid in owned:
            for eid in dict.fromkeys(self.mem_emotions.get(mid, [])):
                if eid:
                    sources[eid].append(mid)
        emergent = {eid: mems for eid, mems in sources.items() if len(mems) >= 2}
        return set(emergent), emergent

    def emotion_refs(self):
        """哪些选项限制了可表达的情绪：eid -> [(oid, oidset)]"""
        out = defaultdict(list)
        for oid, o in self.options.items():
            for eid in o.get("emo") or []:
                out[eid].append(oid)
        return out

    # ---- 好感 ----

    def max_affection_before(self, target_nid, npc):
        """
        走到 target_nid 之前，对某个 NPC 最多能攒到多少好感（乐观上界）。
        同一个 OptionSet 只能选一个选项，所以按 Set 取最大增量再求和。
        只统计"能从 session 起点走到 target 之前"的选项。
        """
        idx = self._collect_option_preconditions(target_nid)
        total = 0.0
        for set_id, oids in idx.items():
            best = 0.0
            for oid in oids:
                o = self.options.get(oid)
                if not o:
                    continue
                aff = o.get("aff") or {}
                d = float(aff.get(npc, 0) or 0)
                if d > best:
                    best = d
            total += best
        return total

    def _collect_option_preconditions(self, target_nid):
        """找出所有"在 target 之前就可能被选中"的选项，按 OptionSet 归类"""
        # 反向：从 target 出发用"能到达 target 的节点集合"近似"target 之前"
        preds = self._predecessors(target_nid)
        idx = defaultdict(list)
        for nid in preds:
            for oid in self.nodes[nid]["replies"]:
                o = self.options.get(oid)
                if o and o["set"]:
                    idx[o["set"]].append(oid)
        return idx

    def _predecessors(self, target_nid):
        """能走到 target 的节点集合（跨 Session），用于"之前"的近似"""
        if getattr(self, "_pred_cache", None) is None:
            self._pred_cache = {}
        if target_nid in self._pred_cache:
            return self._pred_cache[target_nid]
        preds = set()
        for sid, s in self.sessions.items():
            if s["start"]:
                for nid in self.forward_reach(s["start"]):
                    if target_nid in self.forward_reach(nid):
                        preds.add(nid)
        self._pred_cache[target_nid] = preds
        return preds

    # ---- 物品 -> 记忆 ----

    def memory_via_items(self, structural):
        """
        「获得物品 -> 检查部件 -> 得到记忆」是一条独立的记忆来源，
        和选项奖励、情绪规则并列。

        返回 (memories, detail)：
          memories: 结构可达节点上发到手的物品，其全部部件记忆
          detail:   mid -> [说明]
        """
        granted = []
        for nid in structural:
            for iid in self.node_items.get(nid, []):
                if iid not in granted:
                    granted.append(iid)
        memories, detail = set(), defaultdict(list)
        for iid in granted:
            item = self.items.get(iid)
            if not item:
                continue
            for pid in item["parts"]:
                part = self.parts.get(pid)
                if not part or not part["memory"]:
                    continue
                memories.add(part["memory"])
                detail[part["memory"]].append(
                    f"检查「{item['name']}」的{part['name']}（{pid}）")
        return memories, detail

    def item_grant_nodes(self, item_id):
        """哪些节点会发放这个物品"""
        return [nid for nid, items in self.node_items.items() if item_id in items]

    def _ensure_session_index(self):
        """
        建立 nid -> {sid} 的反查。注意一个节点可能被多个 Session 走到（复用节点），
        所以存集合而不是单值。
        """
        if getattr(self, "_node_sessions", None) is not None:
            return
        idx = defaultdict(set)
        for sid, s in self.sessions.items():
            if not s["start"]:
                continue
            seen, dq = {s["start"]}, deque([s["start"]])
            while dq:
                nid = dq.popleft()
                if nid not in self.nodes:
                    continue
                idx[nid].add(sid)
                if self.nodes[nid]["end"]:
                    continue
                for nxt, _ in self.node_edges(nid, None):
                    if nxt not in seen:
                        seen.add(nxt)
                        dq.append(nxt)
        self._node_sessions = idx

    def forward_reach(self, start):
        """
        从 start 往后能走到哪些节点。
        必须能**跨 Session**：一个 Session 结束时会跳到 nextSessionId 的起点，
        否则「上一章的物品记忆能不能撑到下一章」这类问题全都会被误判成时序倒置。
        """
        if getattr(self, "_fwd_cache", None) is None:
            self._fwd_cache = {}
        if start in self._fwd_cache:
            return self._fwd_cache[start]
        self._ensure_session_index()
        seen, dq = {start}, deque([start])
        while dq:
            nid = dq.popleft()
            if nid not in self.nodes:
                continue
            nxts = [t for t, _ in self.node_edges(nid, None)]
            if self.nodes[nid]["end"] or not nxts:
                for sid in self._node_sessions.get(nid, ()):
                    for ns in self.sessions.get(sid, {}).get("nexts", []):
                        st = self.sessions.get(ns, {}).get("start")
                        if st:
                            nxts.append(st)
            for nxt in nxts:
                if nxt not in seen:
                    seen.add(nxt)
                    dq.append(nxt)
        self._fwd_cache[start] = seen
        return seen

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
        计算"最终可能持有的记忆全集"。来源有三条：
          1. 物品部件检查：走到发放物品的节点 -> 检查部件 -> 得到该部件的记忆
             （这是主路径，游戏里记忆主要靠点物品的各个部位拿到）
          2. 选项 rewardMemory：所需记忆满足即可获得
          3. 情绪记忆规则：前置记忆满足 + 情绪使用次数上限达到阈值

        做法是从空记忆起步，每轮用"当前已知记忆"重算一遍**条件可达**的节点集合，
        再看这轮新开了哪些节点、能发哪些物品、能拿哪些记忆；不再变化就收敛。
        这样「必须走到某个受记忆门槛保护的节点才能拿到物品」也能被正确判死，
        不会像直接拿结构可达集合那样乐观放过。
        """
        satisfiable = set()
        structural, _ = self.reachable(None, stop_at_end=True)
        self._structural = structural
        changed = True
        guard = 0
        while changed and guard < 64:
            guard += 1
            changed = False

            # 先按"当前已知记忆"重算可达集合，L3 也复用这一步的结果
            live, _ = self.reachable(frozenset(satisfiable), stop_at_end=True)
            self._live = live

            # 来源 1：走到哪、发到哪，就能检查哪件物品的哪个部件
            item_mems, _ = self.memory_via_items(live)
            for m in item_mems:
                if m not in satisfiable:
                    satisfiable.add(m)
                    changed = True

            # 来源 2：选项奖励（只在可达节点上算）
            for nid in live:
                for oid in self.nodes[nid]["replies"]:
                    o = self.options.get(oid)
                    if not o:
                        continue
                    if set(o["req"]) <= satisfiable:
                        for m in o["reward"]:
                            if m not in satisfiable:
                                satisfiable.add(m)
                                changed = True

            # 来源 3：情绪记忆规则
            for rid, rule in self.rules.items():
                if not set(rule["required"]) <= satisfiable:
                    continue
                if self.emotion_capacity(rule["emotion"], structural) < rule["threshold"]:
                    continue
                for m in rule["reward"]:
                    if m not in satisfiable:
                        satisfiable.add(m)
                        changed = True
        if not hasattr(self, "_live"):
            self._live = self.reachable(frozenset(satisfiable), stop_at_end=True)[0]
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
    if isinstance(v, (list, tuple)):
        return [_cs(x) for x in v if _cs(x)]
    s = _cs(v).strip("[]")
    return [x.strip().strip("'\"").strip() for x in re.split(r"[,;、]", s) if x.strip(" '\"")]


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


def _parse_aff_map(v):
    """`{NPC_001:2}` / `{NPC_001:2,NPC_002:1}` -> {老周: 2.0, ...}"""
    s = _cs(v).strip("{}")
    out = {}
    for part in s.split(","):
        part = part.strip()
        if not part or ":" not in part:
            continue
        k, _, val = part.partition(":")
        try:
            out[NPC_ID_MAP.get(k.strip(), k.strip())] = float(val.strip() or 0)
        except ValueError:
            continue
    return out


# 源表的 NPC 占位 ID -> 项目中实际使用的 NPC 名（与 convert_story_v1.py 的 NPC_ID_MAP 保持一致）
NPC_ID_MAP = {"NPC_001": "老周", "NPC_002": "陈先生", "NPC_003": "陈果"}


def _aff_from_flat(npc, delta):
    """Luban 表是扁平的 affectionNpcId + affectionDelta"""
    if not npc or delta is None:
        return {}
    try:
        return {_cs(npc): float(delta)}
    except (TypeError, ValueError):
        return {}


def _load_items_source(wb, g):
    """
    读策划源表的「Item」sheet。它是一张表里叠了两个子表：
      上半：ItemID / Name / Description / ImageID / ItemPartList
      下半（换一个 ##var）：ItemPartID / ItemPartName / ImageID / MemoryID
    以第二段表头出现的位置为分界。
    """
    if "Item" not in wb.sheetnames:
        return
    ws = wb["Item"]
    mode = None           # "item" / "part"
    ih = ph = None
    for row in ws.iter_rows(values_only=True):
        vals = list(row)
        if not vals or not _cs(vals[0]).startswith("##"):
            pass
        first_b = _cs(vals[1]) if len(vals) > 1 else ""
        if _cs(vals[0]) == "##var":
            if first_b == "ItemPartID":
                mode = "part"
                ph = [_cs(c) for c in vals]
            else:
                mode = "item"
                ih = [_cs(c) for c in vals]
            continue
        if _cs(vals[0]).startswith("##") or not first_b:
            continue
        if mode == "item":
            iid = first_b
            g.items[iid] = {
                "name": _cs(_g(vals, ih, "Name")),
                "parts": _list(_g(vals, ih, "ItemPartList")),
            }
        elif mode == "part":
            pid = first_b
            g.parts[pid] = {
                "name": _cs(_g(vals, ph, "ItemPartName")),
                "memory": _cs(_g(vals, ph, "MemoryID")),
                "item": "",
            }
    # 反向补上部件所属物品
    for iid, item in g.items.items():
        for pid in item["parts"]:
            if pid in g.parts:
                g.parts[pid]["item"] = iid


def _load_items_luban(datas_dir, g, sheet_rows):
    """读 Luban 三张表：叙事物品配置表 / 叙事物品部件配置表 / 叙事动作配置表"""
    ih, ir = sheet_rows("叙事物品配置表.xlsx")
    for r in ir:
        iid = _cs(r[_idx(ih, "id")]) if _idx(ih, "id") >= 0 else ""
        if not iid:
            continue
        g.items[iid] = {
            "name": _cs(_g(r, ih, "itemName")),
            "parts": _list(_g(r, ih, "partIds")),
        }
    ph, pr = sheet_rows("叙事物品部件配置表.xlsx")
    for r in pr:
        pid = _cs(r[_idx(ph, "id")]) if _idx(ph, "id") >= 0 else ""
        if not pid:
            continue
        g.parts[pid] = {
            "name": _cs(_g(r, ph, "partName")),
            "memory": _cs(_g(r, ph, "memoryId")),
            "item": "",
        }
    for iid, item in g.items.items():
        for pid in item["parts"]:
            if pid in g.parts:
                g.parts[pid]["item"] = iid

    ah, ar = sheet_rows("叙事动作配置表.xlsx")
    action_item = {}
    for r in ar:
        aid = _cs(r[_idx(ah, "id")]) if _idx(ah, "id") >= 0 else ""
        if not aid:
            continue
        if _cs(_g(r, ah, "actionType")).lower() == "grantitem":
            action_item[aid] = _cs(_g(r, ah, "targetId"))
    for nid, n in g.nodes.items():
        ids = _list(n.get("_enterActions")) + _list(n.get("_exitActions"))
        items = [action_item[a] for a in ids if a in action_item]
        if items:
            g.node_items[nid] = items


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
            "aff": _parse_aff_map(_g(r, oh, "AddAffection")),
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
            "_enterActions": _list(_g(r, th, "OnEnterActionSetID")),
            "_exitActions": _list(_g(r, th, "OnExitActionSetID")),
        }

    mh, mr = _rows(wb, "Memory")
    for r in mr:
        mid = _cs(r[_idx(mh, "MemoryID")])
        if mid:
            g.all_memories.add(mid)
            g.mem_emotions[mid] = _list(_g(r, mh, "EmotionTagIDs"))

    eh, er = _rows(wb, "Emotion")
    for r in er:
        eid = _cs(r[_idx(eh, "EmotionID")])
        if eid:
            # 源表 Name 列是英文 key，中文在 Description 列
            g.emotions[eid] = _cs(_g(r, eh, "Description")) or _cs(_g(r, eh, "Name")) or eid

    # ---- 物品 / 部件 / 动作（记忆的主要来源路径）----
    _load_items_source(wb, g)
    action_item = {}
    if "Action" in wb.sheetnames:
        ah, ar = _rows(wb, "Action")
        for r in ar:
            aid = _cs(r[_idx(ah, "ActionID")])
            if not aid:
                continue
            if _cs(_g(r, ah, "ActionType")).lower() == "grantitem":
                action_item[aid] = _cs(_g(r, ah, "TargetID"))
    for nid, n in g.nodes.items():
        ids = _list(n.get("_enterActions")) + _list(n.get("_exitActions"))
        items = [action_item[a] for a in ids if a in action_item]
        if items:
            g.node_items[nid] = items

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
            "aff": _aff_from_flat(_g(r, oh, "affectionNpcId"), _g(r, oh, "affectionDelta")),
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
            "_enterActions": _list(_g(r, th, "onEnterActionIds")),
            "_exitActions": _list(_g(r, th, "onExitActionIds")),
        }

    for r in mr:
        mid = _cs(r[_idx(mh, "id")])
        if mid:
            g.all_memories.add(mid)
            g.mem_emotions[mid] = _list(_g(r, mh, "emotionCardSet"))

    eh, er = sheet_rows("情绪配置表.xlsx")
    for r in er:
        eid = _cs(r[_idx(eh, "id")]) if _idx(eh, "id") >= 0 else ""
        if eid:
            g.emotions[eid] = _cs(_g(r, eh, "emotionName")) or eid

    _load_items_luban(datas_dir, g, sheet_rows)

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
        r.note("没有配置情绪记忆规则（情绪值达阈值 -> 激活记忆）。"
               "V1.0 目前只靠「物品部件检查」产出记忆；如果设计上有「用够次数才解锁」的记忆，需要补规则")

    # 记忆来源盘点：每条被 require 的记忆，是否真的有地方产出
    required_by = defaultdict(list)     # mid -> [option id]
    for oid, o in g.options.items():
        for m in o["req"]:
            required_by[m].append(oid)
    source_desc = defaultdict(list)     # mid -> [来源说明]

    # 来源 1：物品部件检查（主路径）
    live = getattr(g, "_live", structural)
    item_mems, item_detail = g.memory_via_items(live)
    for m, descs in item_detail.items():
        source_desc[m].extend(descs)
    # 来源 2：选项奖励
    for oid, o in g.options.items():
        for m in o["reward"]:
            source_desc[m].append(f"选项 {oid} 奖励")
    # 来源 3：情绪记忆规则
    for rid, rule in g.rules.items():
        for m in rule["reward"]:
            source_desc[m].append(f"规则 {rid} 激活")

    # ---- 物品 -> 记忆 全链路 ---- #
    if g.items or g.parts:
        r.title("物品 -> 部件 -> 记忆 链路")
        ungranted = [iid for iid in g.items if not g.item_grant_nodes(iid)]
        blocked = [iid for iid in g.items
                   if g.item_grant_nodes(iid) and not set(g.item_grant_nodes(iid)) & live]
        r.text(f"\n共 {len(g.items)} 件物品、{len(g.parts)} 个部件，"
               f"其中 {len(item_mems)} 个部件记忆可以由「记忆门槛下仍走得到」的节点拿到。\n")
        r.text("| 物品 | 部件 | 部件记忆 | 物品获取节点 | 状态 |")
        r.text("|---|---|---|---|---|")
        for iid in sorted(g.items, key=lambda x: (g.items[x]["name"] or x, x)):
            item = g.items[iid]
            nodes = g.item_grant_nodes(iid)
            reach_nodes = [n for n in nodes if n in live]
            for pid in item["parts"]:
                part = g.parts.get(pid)
                if not part:
                    r.text(f"| {item['name'] or iid} | {pid}（表里查不到） | — | — | ❌ 部件缺失 |")
                    continue
                if not nodes:
                    state = "❌ 剧情里拿不到这件物品"
                elif not reach_nodes:
                    state = "❌ 发放节点被记忆门槛挡住"
                elif not part["memory"]:
                    state = "⚠️ 部件未绑记忆"
                else:
                    state = "✅"
                mark = ",".join(reach_nodes) if reach_nodes else (",".join(nodes) or "—")
                r.text(f"| {item['name'] or iid} | {part['name'] or pid} | "
                       f"{part['memory'] or '—'} | {mark} | {state} |")
        if ungranted:
            r.bad(f"{len(ungranted)} 件物品在剧情里没有发放节点：{ungranted}")
        elif blocked:
            r.bad(f"{len(blocked)} 件物品的发放节点被记忆门槛挡住，部件记忆拿不到：{blocked}")
        else:
            r.ok("所有物品都能在剧情里拿到，部件记忆链路完整")

        # 部件绑了记忆但记忆表里没有
        dangling = sorted({p["memory"] for p in g.parts.values()
                           if p["memory"] and p["memory"] not in g.all_memories})
        if dangling:
            r.bad(f"部件引用了记忆表里不存在的记忆：{dangling}")

        # 有记忆却没有任何部件承载（只能靠选项奖励/规则拿）
        part_mems = {p["memory"] for p in g.parts.values() if p["memory"]}
        no_part = sorted(m for m in g.all_memories if m not in part_mems)
        if no_part:
            granted_anywhere = set()
            for nid in live:
                for oid in g.nodes[nid]["replies"]:
                    o = g.options.get(oid)
                    if o:
                        granted_anywhere |= set(o["reward"])
            for rule in g.rules.values():
                granted_anywhere |= set(rule["reward"])
            orphan = [m for m in no_part if m not in granted_anywhere]
            if orphan:
                r.note(f"{len(orphan)} 条记忆既没有部件承载、也没有任何奖励来源"
                       f"（如果是开局自带，需要在代码里显式发放）：{orphan}")
            else:
                r.text(f"\n{len(no_part)} 条记忆没有部件承载，但都能通过奖励拿到：{no_part}")

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
        r.text("\n各记忆的产出方式：\n")
        r.text("| 记忆 | 被哪些选项需要 | 产出方式 |")
        r.text("|---|---|---|")
        for mid in sorted(required_by):
            r.text(f"| {mid} | {', '.join(required_by[mid][:4])} | "
                   f"{'；'.join(source_desc[mid][:2])} |")

    # 情绪记忆规则的前置记忆也必须有来源，否则规则永远不触发
    prereq_missing = {}
    for rid, rule in g.rules.items():
        for m in rule["required"]:
            if not source_desc.get(m):
                prereq_missing.setdefault(m, []).append(rid)
    if prereq_missing:
        r.text(f"\n**规则前置记忆无来源 {len(prereq_missing)} 个**"
               f"（这些记忆没有产出途径，依赖它们的规则永远不会触发）\n")
        r.text("| 记忆 | 被哪些规则当作前置 |")
        r.text("|---|---|")
        for mid in sorted(prereq_missing):
            r.text(f"| {mid} | {', '.join(prereq_missing[mid])} |")
        r.bad(f"{len(prereq_missing)} 个记忆被规则当作前置，但没有任何产出途径")

    # ---------------- 记忆时序：物品得先拿到，才能检查出记忆 ----------------
    # 物品是在某个节点的「进入动作」发放、「离开动作」移除的，
    # 所以玩家必须在那个节点的界面里点检查，才能拿到该物品的部件记忆。
    # 若需要这条记忆的选项出现在物品节点的**前面**（或另一条互不可达的支线上），
    # 那这个选项在实际游玩里永远开不了 —— 结构可达看不出这个问题。
    mem_item_node = {}          # mid -> [(itemId, [发放节点])]
    for iid, item in g.items.items():
        for pid in item["parts"]:
            part = g.parts.get(pid)
            if part and part["memory"]:
                mem_item_node.setdefault(part["memory"], []).append(
                    (iid, g.item_grant_nodes(iid)))
    timing_bad = []
    reach_cache = {}
    for oid, o in g.options.items():
        for m in o["req"]:
            if m not in mem_item_node:
                continue
            holders = [nid for nid, n in g.nodes.items() if oid in n["replies"]]
            ok = False
            for iid, gnodes in mem_item_node[m]:
                for gn in gnodes:
                    if gn not in reach_cache:
                        reach_cache[gn] = g.forward_reach(gn)
                    if any(h in reach_cache[gn] for h in holders) or gn in holders:
                        ok = True
            if not ok and holders:
                timing_bad.append((oid, m, holders[0]))
    if timing_bad:
        r.text(f"\n**记忆时序问题 {len(timing_bad)} 处**"
               f"（选项所在节点无法从「发该记忆的物品节点」走到，说明检查来不及做）\n")
        r.text("| Option | 需要记忆 | 选项所在节点 | 问题 |")
        r.text("|---|---|---|---|")
        for oid, m, nid in timing_bad:
            item, gnodes = next((i, ns) for i, ns in mem_item_node[m] if ns)
            r.text(f"| {oid} | {m} | {nid} | 发 {item} 的节点 {','.join(gnodes)} 走不到这里 |")
        r.bad(f"{len(timing_bad)} 处「先要记忆、后发物品」的时序倒置，实际游玩会锁死")
    elif mem_item_node:
        r.ok("所有物品记忆都在需要它的选项之前就能拿到（时序无倒置）")


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

    # ---------------- 情绪涌现 ----------------
    # 运行时口径（NarrativeDomainService.FindEmergentEmotions）：
    #   ≥2 段已拥有记忆的 EmotionCardSet 含同一情绪 -> 该情绪涌现（解锁）
    ownable = satisfiable | item_mems
    emergent, emergent_detail = g.emergent_emotions(sorted(ownable))
    emo_refs = g.emotion_refs()
    if g.mem_emotions and g.emotions:
        r.title("情绪涌现（≥2 段记忆标注同一情绪即解锁）")
        r.text("\n| 情绪 | 名称 | 标注它的可得记忆 | 能否涌现 | 允许表达它的选项 |")
        r.text("|---|---|---|---|---|")
        dead_emo_ref = []
        for eid in sorted(g.emotions):
            if eid == "E00":
                continue
            mems = [m for m in g.all_memories
                    if eid in (g.mem_emotions.get(m) or []) and m in ownable]
            can = len(mems) >= 2
            refs = emo_refs.get(eid, [])
            r.text(f"| {eid} | {g.emotions.get(eid, '')} | {len(mems)} 条"
                   f"{'（' + ','.join(sorted(mems)) + '）' if mems else ''} | "
                   f"{'✅ 可涌现' if can else '❌ 永不涌现'} | {','.join(refs) or '—'} |")
            if refs and not can:
                dead_emo_ref.append((eid, refs, len(mems)))
        if dead_emo_ref:
            for eid, refs, cnt in dead_emo_ref:
                r.bad(f"情绪 {eid}({g.emotions.get(eid, '')}) 被 {','.join(refs)} 允许表达，"
                      f"但只有 {cnt} 段记忆标注它，永远达不到 ≥2 的涌现条件 —— "
                      f"这些选项的「{g.emotions.get(eid, '')}」表达分支永远走不到")
        else:
            r.ok("所有被选项引用为可表达情绪的情绪都能涌现")

        # 允许表达的情绪里，实际能拿到几个
        if emo_refs:
            r.text("\n**选项 × 可表达情绪**（allowedEmotion 只是「允许」，"
                   "实际能选到的只有已涌现的那几个）\n")
            r.text("| Option | 允许表达 | 实际可表达 | 备注 |")
            r.text("|---|---|---|---|")
            for oid, o in sorted(g.options.items()):
                allowed = o.get("emo") or []
                if not allowed:
                    continue
                usable = [e for e in allowed if e in emergent]
                missing = [e for e in allowed if e not in emergent]
                note = f"缺 {','.join(missing)}（不涌现）" if missing else "全部可用"
                r.text(f"| {oid} | {','.join(allowed)} | "
                       f"{','.join(usable) or '—'} | {note} |")

    # ---------------- 好感门槛 ----------------
    # 运行时有两套形态，都要看：
    #   ① extras 候选级（NarrativeDomainService.ResolveNextNode）：affection >= 候选节点 MinAffection 才会被选中
    #   ② 节点级跳过（BattleMainUI.PresentStoryNodeAsync）：MinAffection 不够就不播该节点，改走它的 DefaultNextNodeId
    aff_branches = []
    for nid, n in g.nodes.items():
        for cand in n.get("extras") or []:
            cn = g.nodes.get(cand)
            if cn and cn.get("minAff", 0) > 0:
                aff_branches.append(("extras", nid, cand, cn["minAff"], n.get("npc", "")))
        if n.get("branches"):
            for b in n["branches"]:
                try:
                    need = float(b.get("min") or 0)
                except (TypeError, ValueError):
                    need = 0
                if need > 0:
                    aff_branches.append(("branch", nid, b.get("next"), need, n.get("npc", "")))
        if n.get("minAff", 0) > 0:
            aff_branches.append(("node", nid, n.get("next"), n["minAff"], n.get("npc", "")))

    if aff_branches:
        r.title("好感门槛的分支")
        r.text("\n| 形态 | 分支点 | 目标节点 | 需要好感 | NPC | 最多能攒到 | 判定 |")
        r.text("|---|---|---|---|---|---|---|")
        kind_cn = {"extras": "候选筛选", "branch": "分支规则", "node": "节点级跳过"}
        fail = 0
        for kind, nid, tgt, need, npc in aff_branches:
            got = g.max_affection_before(nid, npc)
            ok = got >= need
            if not ok:
                fail += 1
            r.text(f"| {kind_cn.get(kind, kind)} | {nid} | {tgt or '—'} | {need:g} | {npc or '—'} | "
                   f"{got:g} | {'✅ 可达' if ok else '❌ 攒不够'} |")
            if not ok:
                r.bad(f"{nid} -> {tgt} 需要 {npc} 好感 {need:g}，"
                      f"但走到这里最多只能攒到 {got:g}，这条分支永远进不去")
        # 节点级门槛必须有兜底，否则运行时直接 return
        no_fallback = [nid for nid, n in g.nodes.items()
                       if n.get("minAff", 0) > 0 and not n.get("next")]
        if no_fallback:
            r.bad(f"节点级好感门槛没有默认后继，运行时好感不够会直接中断：{no_fallback}")
        if fail == 0:
            r.ok("所有好感门槛分支都能达到")

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
