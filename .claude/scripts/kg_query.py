#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
kg_query.py — DGame 知识图谱查询工具（供 graph-query-agent 调用）

知识图谱：.understand-anything/knowledge-graph.json（由 understand-anything 生成）
用途：把 1.6MB 的图谱 JSON 变成可精确检索的命令，避免把整个文件塞进上下文。

命令：
  search <关键词>         按 name / summary / tags 模糊查节点，返回 id 列表
  node   <id 或 名字关键词> 显示节点详情 + 它的全部关系边（出边/入边分组）
  layers                  列出所有架构分层及节点数
  layer  <层名关键词>      列出某一层包含的节点
  stats                   图谱总览（节点/边/层的数量统计）

约定：
  - 输出为纯文本，专供 AI 阅读，字段精炼。
  - node 命令的「出边」= 该节点依赖/调用/继承谁；「入边」= 谁依赖/调用/继承该节点。
"""
import sys
import io
import json
import os

# Windows 控制台强制 UTF-8，避免中文乱码
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

GRAPH_PATH = os.path.join(".understand-anything", "knowledge-graph.json")
MAX_HITS = 40  # search 结果上限，防止刷屏


def load_graph():
    if not os.path.exists(GRAPH_PATH):
        print(f"[错误] 未找到知识图谱：{GRAPH_PATH}")
        print("       请先运行 /understand 生成图谱。")
        sys.exit(1)
    with open(GRAPH_PATH, encoding="utf-8") as f:
        return json.load(f)


def short(text, n=100):
    if not text:
        return ""
    text = " ".join(text.split())
    return text if len(text) <= n else text[: n - 1] + "…"


def cmd_stats(g):
    from collections import Counter
    nt = Counter(n["type"] for n in g["nodes"])
    et = Counter(e["type"] for e in g["edges"])
    p = g.get("project", {})
    print(f"项目：{p.get('name')}  |  提交：{p.get('gitCommitHash', '')[:8]}")
    print(f"生成时间：{p.get('analyzedAt', '')}")
    print(f"\n节点总数：{len(g['nodes'])}")
    for t, c in nt.most_common():
        print(f"  - {t:10s} {c}")
    print(f"\n边总数：{len(g['edges'])}")
    for t, c in et.most_common():
        print(f"  - {t:12s} {c}")
    print(f"\n分层数：{len(g.get('layers', []))}（用 `layers` 命令查看）")


def cmd_search(g, kw):
    kw_l = kw.lower()
    hits = []
    for n in g["nodes"]:
        hay = (
            n.get("name", "")
            + " "
            + n.get("summary", "")
            + " "
            + " ".join(n.get("tags", []))
        ).lower()
        if kw_l in hay:
            # 命中优先级：name 命中 > tag 命中 > summary 命中
            score = 0
            if kw_l in n.get("name", "").lower():
                score = 2
            elif any(kw_l in t.lower() for t in n.get("tags", [])):
                score = 1
            hits.append((score, n))
    if not hits:
        print(f"未找到与「{kw}」相关的节点。")
        print("提示：可换更短的关键词，或用 `stats` 看有哪些节点类型。")
        return
    hits.sort(key=lambda x: -x[0])
    print(f"命中 {len(hits)} 个节点" + (f"（仅显示前 {MAX_HITS}）" if len(hits) > MAX_HITS else "") + "：\n")
    for _, n in hits[:MAX_HITS]:
        print(f"[{n['type']}] {n['name']}")
        print(f"    id: {n['id']}")
        if n.get("filePath"):
            print(f"    文件: {n['filePath']}")
        if n.get("summary"):
            print(f"    摘要: {short(n['summary'], 120)}")
        print()


def _find_node(g, key):
    """按精确 id 或名字关键词定位一个节点。返回 (node, 候选列表)。"""
    for n in g["nodes"]:
        if n["id"] == key:
            return n, []
    kw_l = key.lower()
    cands = [n for n in g["nodes"] if kw_l in n.get("name", "").lower()]
    if len(cands) == 1:
        return cands[0], []
    return None, cands


def cmd_node(g, key):
    node, cands = _find_node(g, key)
    if node is None:
        if cands:
            print(f"「{key}」匹配到多个节点，请用完整 id 指定其一：\n")
            for n in cands[:MAX_HITS]:
                print(f"  [{n['type']}] {n['name']}  ->  {n['id']}")
        else:
            print(f"未找到节点：{key}（先用 `search` 查关键词）")
        return

    nid = node["id"]
    print("=" * 60)
    print(f"[{node['type']}] {node['name']}")
    print(f"id      : {nid}")
    if node.get("filePath"):
        print(f"文件    : {node['filePath']}")
    print(f"复杂度  : {node.get('complexity', '-')}")
    if node.get("tags"):
        print(f"标签    : {', '.join(node['tags'])}")
    if node.get("summary"):
        print(f"摘要    : {node['summary']}")
    print("=" * 60)

    id2name = {n["id"]: n for n in g["nodes"]}

    def line(e, other_id):
        o = id2name.get(other_id)
        oname = f"[{o['type']}] {o['name']}" if o else other_id
        desc = f"  // {short(e.get('description',''), 60)}" if e.get("description") else ""
        return f"  ({e['type']}) {oname}{desc}\n      -> {other_id}"

    out = [e for e in g["edges"] if e["source"] == nid]
    inc = [e for e in g["edges"] if e["target"] == nid]

    print(f"\n▼ 出边 {len(out)}（本节点 依赖/调用/包含/继承 → 对方）")
    if out:
        for e in out:
            print(line(e, e["target"]))
    else:
        print("  （无）")

    print(f"\n▲ 入边 {len(inc)}（谁 依赖/调用/包含/继承 → 本节点）")
    if inc:
        for e in inc:
            print(line(e, e["source"]))
    else:
        print("  （无）")


def cmd_layers(g):
    layers = g.get("layers", [])
    if not layers:
        print("图谱无分层信息。")
        return
    print(f"共 {len(layers)} 层：\n")
    for L in layers:
        print(f"[{L['id']}] {L['name']}  —  {len(L.get('nodeIds', []))} 个节点")
        if L.get("description"):
            print(f"    {short(L['description'], 110)}")
        print()


def cmd_layer(g, kw):
    kw_l = kw.lower()
    layers = g.get("layers", [])
    match = [L for L in layers if kw_l in L["name"].lower() or kw_l in L["id"].lower()]
    if not match:
        print(f"未找到匹配「{kw}」的层。用 `layers` 查看全部层名。")
        return
    id2name = {n["id"]: n for n in g["nodes"]}
    for L in match:
        print(f"[{L['id']}] {L['name']}（{len(L.get('nodeIds', []))} 节点）")
        if L.get("description"):
            print(f"  {L['description']}\n")
        for nid in L.get("nodeIds", []):
            n = id2name.get(nid)
            if n:
                print(f"  [{n['type']}] {n['name']}  ->  {nid}")
        print()


USAGE = __doc__


def main():
    if len(sys.argv) < 2:
        print(USAGE)
        sys.exit(0)
    cmd = sys.argv[1]
    arg = " ".join(sys.argv[2:]).strip()
    g = load_graph()

    if cmd == "stats":
        cmd_stats(g)
    elif cmd == "search" and arg:
        cmd_search(g, arg)
    elif cmd == "node" and arg:
        cmd_node(g, arg)
    elif cmd == "layers":
        cmd_layers(g)
    elif cmd == "layer" and arg:
        cmd_layer(g, arg)
    else:
        print(USAGE)
        sys.exit(1)


if __name__ == "__main__":
    main()
