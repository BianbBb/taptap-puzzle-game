#!/usr/bin/env python3
"""离线自查叙事配表：不进 Unity 就能跑一遍 NarrativeConfigValidator 的硬错误规则。

用法：
    python Tools/check_narrative_config.py            # 有 Error 时退出码 1
    python Tools/check_narrative_config.py --quiet    # 只打印计数

为什么要有这个：校验器只在 BattleMainUI.StartStoryAsync 里跑，而且一条一条往 Console 刷。
改完配表开一局才知道哪条错了，来回成本太高 —— 这里读 GameUnity/Configs/Json/ 的导出结果，
把「引用不存在 / 坐标越界 / 节点没有出口」这类结构错误一次性列出来。

白的放过、灰的照旧报（与 C# 版一致的取舍）：
- 部件热点 (-1,-1) = 这个部件在图上没有位置，走详情页按钮列表，不算错（2026-10-21 起）。
- NODE_ALL_REPLIES_LOCKED / NODE_NO_MEMORYLESS_REPLY 依赖存档状态，这里不查，留给运行时。
"""
import argparse
import json
import os
import sys

JSON_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                        '..', 'GameUnity', 'Configs', 'Json')


def load(name):
    with open(os.path.join(JSON_DIR, name), encoding='utf-8') as f:
        return json.load(f)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--quiet', action='store_true')
    args = ap.parse_args()

    emotions = {r['id'] for r in load('tbemotionconfig.json')}
    memory_rows = load('tbmemoryconfig.json')
    memories = {r['id'] for r in memory_rows}
    items = {r['id']: r for r in load('tbnarrativeitemconfig.json')}
    parts = {r['id']: r for r in load('tbnarrativeitempartconfig.json')}
    actions = {r['id']: r for r in load('tbnarrativeactionconfig.json')}
    replies = {r['id']: r for r in load('tbreplyoptionconfig.json')}
    nodes = {r['id']: r for r in load('tbstorynodeconfig.json')}
    sessions = {r['id']: r for r in load('tbnarrativesessionconfig.json')}
    hotspots = load('tbscenehotspotconfig.json')

    errors, warns = [], []

    def E(rule, cid, msg):
        errors.append((rule, cid, msg))

    def W(rule, cid, msg):
        warns.append((rule, cid, msg))

    # ---- 物品 / 部件 ----
    for iid, item in items.items():
        for pid in item.get('partIds') or []:
            if pid not in parts:
                E('ITEM_PART_MISSING', iid, f'references unknown item part {pid}')

    for pid, part in parts.items():
        x, y = part['hotspotX'], part['hotspotY']
        off_image = x < 0 and y < 0          # (-1,-1) = 图上没位置，走列表按钮
        if not off_image and not (0 <= x <= 1 and 0 <= y <= 1):
            E('ITEM_PART_HOTSPOT_OUT_OF_RANGE', pid, f'({x}, {y})')
        mid = part.get('memoryId')
        if mid and mid not in memories:
            E('ITEM_PART_MEMORY_MISSING', pid, f'references unknown memory {mid}')

    # ---- 场景交互点 ----
    for hot in hotspots:
        x, y = hot['hotspotX'], hot['hotspotY']
        w, h = hot['width'], hot['height']
        if not (0 <= x <= 1 and 0 <= y <= 1):
            E('HOTSPOT_POSITION_OUT_OF_RANGE', hot['id'], f'({x}, {y})')
        if not (0 < w <= 1 and 0 < h <= 1):
            E('HOTSPOT_SIZE_INVALID', hot['id'], f'({w}, {h})')
        if hot.get('itemId') and hot['itemId'] not in items:
            E('HOTSPOT_ITEM_MISSING', hot['id'], hot['itemId'])
        if hot.get('memoryId') and hot['memoryId'] not in memories:
            E('HOTSPOT_MEMORY_MISSING', hot['id'], hot['memoryId'])
        if hot.get('actionId') and hot['actionId'] not in actions:
            E('HOTSPOT_ACTION_MISSING', hot['id'], hot['actionId'])

    # ---- 动作白名单 ----
    granted, removed = set(), set()
    for aid, action in actions.items():
        kind = (action.get('actionType') or '').lower()
        if kind not in ('grantitem', 'removeitem', 'grantemotion', 'grantmemory'):
            E('ACTION_TYPE_UNSUPPORTED', aid, action.get('actionType'))
            continue
        target = action.get('targetId')
        if kind == 'grantmemory' and target not in memories:
            E('ACTION_MEMORY_MISSING', aid, target)
        if kind in ('grantitem', 'removeitem'):
            if target not in items:
                E('ACTION_ITEM_MISSING', aid, target)
            (granted if kind == 'grantitem' else removed).add(target)
        if kind == 'grantemotion' and target not in emotions:
            E('ACTION_EMOTION_MISSING', aid, target)

    for gid in sorted(granted - removed):
        W('ITEM_GRANT_WITHOUT_REMOVE', gid, 'granted but never removed (bag capacity = 1)')

    # ---- Session ----
    for sid, session in sessions.items():
        if session['startNodeId'] not in nodes:
            E('SESSION_START_MISSING', sid, session['startNodeId'])
        nxt = session.get('nextSessionId')
        if nxt and nxt not in sessions:
            E('SESSION_NEXT_MISSING', sid, nxt)

    # ---- 记忆 -> 情绪 ----
    for memory in memory_rows:
        for eid in memory.get('emotionCardSet') or []:
            if eid not in emotions:
                E('MEMORY_EMOTION_MISSING', memory['id'], eid)

    # ---- 回复选项 ----
    def check_actions(cid, ids):
        for aid in ids or []:
            if aid not in actions:
                E('ACTION_REF_MISSING', cid, aid)

    for rid, reply in replies.items():
        for key in ('requiredMemory', 'rewardMemory'):
            for mid in reply.get(key) or []:
                if mid not in memories:
                    E('REPLY_MEMORY_MISSING', rid, f'{key}={mid}')
        for eid in reply.get('allowedEmotion') or []:
            if eid not in emotions:
                E('REPLY_EMOTION_MISSING', rid, eid)
        for rule in reply.get('affectionRules') or []:
            eid = (rule or {}).get('emotion')
            if eid and eid not in ('E00', 'NONE') and eid not in emotions:
                E('AFFECTION_EMOTION_MISSING', rid, eid)
        check_actions(rid, reply.get('actionIds'))
        nxt = reply.get('nextNodeId')
        if nxt and nxt not in nodes:
            E('REPLY_NEXT_NODE_MISSING', rid, nxt)

    # ---- 节点 ----
    for nid, node in nodes.items():
        check_actions(nid, node.get('onEnterActionIds'))
        check_actions(nid, node.get('onExitActionIds'))
        for extra in node.get('extraNextNodeIds') or []:
            if extra not in nodes:
                E('NODE_EXTRA_NEXT_MISSING', nid, extra)
        for rid in node.get('replies') or []:
            if rid not in replies:
                E('NODE_REPLY_MISSING', nid, rid)
        default_next = node.get('defaultNextNodeId')
        if default_next and default_next not in nodes:
            E('NODE_DEFAULT_NEXT_MISSING', nid, default_next)
        if (not node.get('isSessionEnd') and not default_next
                and not (node.get('branches') or []) and not (node.get('replies') or [])):
            E('NODE_NO_ROUTE', nid, 'no reply / next / branch')
        for branch in node.get('branches') or []:
            if branch.get('reply') not in ('*', 'ANY', None) and branch['reply'] not in replies:
                E('BRANCH_REPLY_MISSING', nid, branch.get('reply'))
            if branch.get('nextNode') not in nodes:
                E('BRANCH_NODE_MISSING', nid, branch.get('nextNode'))
        if (node.get('turnType') or '').lower() == 'scenesearch':
            if not default_next:
                E('SCENE_SEARCH_NO_NEXT', nid, 'needs defaultNextNodeId')
            background = node.get('backgroundId')
            if background and not [h for h in hotspots if h['backgroundId'] == background]:
                W('SCENE_SEARCH_NO_HOTSPOT', nid, background)

    # ---- 有没有人用 ----
    used_memories, used_emotions = set(), set()
    for reply in replies.values():
        used_memories.update(reply.get('requiredMemory') or [])
        used_memories.update(reply.get('rewardMemory') or [])
        used_emotions.update(reply.get('allowedEmotion') or [])
    for part in parts.values():
        if part.get('memoryId'):
            used_memories.add(part['memoryId'])
    for hot in hotspots:
        if hot.get('memoryId'):
            used_memories.add(hot['memoryId'])
    for memory in memory_rows:
        used_emotions.update(memory.get('emotionCardSet') or [])
    for action in actions.values():
        kind = (action.get('actionType') or '').lower()
        if kind == 'grantmemory':
            used_memories.add(action['targetId'])
        if kind == 'grantemotion':
            used_emotions.add(action['targetId'])

    for eid in sorted(emotions - used_emotions - {'E00'}):
        W('EMOTION_UNUSED', eid, 'not referenced anywhere')
    for mid in sorted(memories - used_memories):
        W('MEMORY_UNUSED', mid, 'not required / rewarded / inspected anywhere')

    if args.quiet:
        print(f'errors={len(errors)} warnings={len(warns)}')
    else:
        print(f'ERRORS {len(errors)}   WARNINGS {len(warns)}\n')
        for rule, cid, msg in errors:
            print(f'[E] {rule:32} {cid:16} {msg}')
        if errors and warns:
            print()
        for rule, cid, msg in warns:
            print(f'[W] {rule:32} {cid:16} {msg}')
    return 1 if errors else 0


if __name__ == '__main__':
    sys.exit(main())
