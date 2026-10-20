#!/usr/bin/env python3
"""Freesound CC0 音效抓取器（《记忆终会消散》专用）。

只抓 CC0（零署名义务），按 sfx_manifest.json 的清单批量下载并归一化。

用法：
    # 按清单批量抓（跳过已存在的）
    python3 Tools/audio/fetch_cc0_sfx.py --manifest Tools/audio/sfx_manifest.json

    # 只抓某几条
    python3 Tools/audio/fetch_cc0_sfx.py --manifest Tools/audio/sfx_manifest.json --only sfx_ui_click sfx_ui_hover

    # 单条临时抓
    python3 Tools/audio/fetch_cc0_sfx.py --query "soft ui click" --name sfx_ui_click

授权红线：只接受 CC0。CC-BY / CC-BY-SA / Sampling+ / 任何 NC 一律跳过。
"""

import argparse
import json
import os
import re
import shutil
import subprocess
import sys
import urllib.parse
import urllib.request

API_BASE = "https://freesound.org/apiv2"
PROJECT_ROOT = os.environ.get("PROJECT_ROOT") or os.path.abspath(
    os.path.join(os.path.dirname(__file__), "..", "..")
)
AUDIO_ROOT = os.path.join(PROJECT_ROOT, "GameUnity", "Assets", "BundleAssets", "Audios")
DEFAULT_TOKEN_FILE = os.path.expanduser("~/.workbuddy/freesound_key.txt")

# 只接受这个许可
CC0_MARKERS = ("publicdomain/zero",)


# ---------------------------------------------------------------- ffmpeg 定位
def find_ffmpeg(explicit=None):
    if explicit:
        return explicit
    for cand in ("ffmpeg", "/opt/anaconda3/bin/ffmpeg", "/usr/local/bin/ffmpeg", "/opt/homebrew/bin/ffmpeg"):
        p = shutil.which(cand) if not cand.startswith("/") else (cand if os.path.exists(cand) else None)
        if p:
            return p
    raise SystemExit("找不到 ffmpeg，请用 --ffmpeg 指定路径")


# ---------------------------------------------------------------- Freesound
def load_token(path):
    with open(path, "r", encoding="utf-8") as f:
        token = f.read().strip()
    if not token:
        raise SystemExit("Freesound token 为空：%s" % path)
    return token


def build_opener(no_proxy):
    if no_proxy:
        return urllib.request.build_opener(urllib.request.ProxyHandler({}))
    return urllib.request.build_opener()


def api_get(opener, url, token):
    req = urllib.request.Request(url, headers={"Authorization": "Token " + token})
    with opener.open(req, timeout=30) as r:
        return json.loads(r.read().decode("utf-8"))


def is_cc0(uri):
    u = (uri or "").lower()
    return any(m in u for m in CC0_MARKERS)


def search(opener, token, query, min_dur, max_dur, page_size=40):
    flt = ['license:"Creative Commons 0"', "duration:[%s TO %s]" % (min_dur, max_dur)]
    params = {
        "query": query,
        "filter": " ".join(flt),
        "fields": "id,name,license,duration,previews,url,username,avg_rating,num_ratings",
        "page_size": str(page_size),
        "sort": "rating_desc",
    }
    url = API_BASE + "/search/text/?" + urllib.parse.urlencode(params)
    data = api_get(opener, url, token)
    out = []
    for r in data.get("results", []):
        if not is_cc0(r.get("license")):
            continue
        pre = (r.get("previews") or {}).get("preview-hq-mp3") or (r.get("previews") or {}).get("preview-lq-mp3")
        if not pre:
            continue
        r["_preview"] = pre
        out.append(r)
    return out


def sound_by_id(opener, token, sound_id):
    """直接按 id 取详情（查询词命中不了时的兜底），同一套字段。"""
    url = API_BASE + "/sounds/%s/?fields=id,name,license,duration,previews,url,username" % sound_id
    try:
        r = api_get(opener, url, token)
    except Exception:
        return None
    if not is_cc0(r.get("license")):
        return None
    pre = (r.get("previews") or {}).get("preview-hq-mp3") or (r.get("previews") or {}).get("preview-lq-mp3")
    if not pre:
        return None
    r["_preview"] = pre
    return r


def pick(cands, prefer_longest):
    if not cands:
        return None
    cands = sorted(cands, key=lambda r: (-(r.get("avg_rating") or 0), -int(r.get("num_ratings") or 0)))
    # 评分最高的前若干条里，按时长偏好挑
    top = cands[: max(1, min(6, len(cands)))]
    if prefer_longest:
        return max(top, key=lambda r: r.get("duration") or 0)
    return min(top, key=lambda r: r.get("duration") or 0)


def download(opener, url, out_path, retries=8):
    """带长度校验 + 断点续传的分块下载。

    Freesound CDN 实测只有 ~20KB/s，单次 read() 读满 3.4MB 会撞上 180s 超时，
    而超时前一字节都没落盘 → 每次重试都从头开始，永远下不完。
    所以必须**流式落盘**：每读完一块就写，超时也能靠 Range 续传。
    """
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    if os.path.exists(out_path):
        os.remove(out_path)

    expect = None
    for attempt in range(retries):
        have = os.path.getsize(out_path) if os.path.exists(out_path) else 0
        headers = {"User-Agent": "Mozilla/5.0"}
        if have:
            headers["Range"] = "bytes=%d-" % have
        req = urllib.request.Request(url, headers=headers)
        try:
            with opener.open(req, timeout=120) as r:
                if expect is None:
                    cl = r.headers.get("Content-Length")
                    if cl and r.status == 200:
                        expect = int(cl)
                    elif cl and r.status == 206:
                        expect = have + int(cl)
                with open(out_path, "ab" if have else "wb") as f:
                    while True:
                        chunk = r.read(64 * 1024)
                        if not chunk:
                            break
                        f.write(chunk)
                        f.flush()
        except Exception as e:
            size = os.path.getsize(out_path) if os.path.exists(out_path) else 0
            print("# 下载中断 %d/%s (%s)，续传（第 %d 次）"
                  % (size, expect or "?", type(e).__name__, attempt + 1), file=sys.stderr)

        size = os.path.getsize(out_path) if os.path.exists(out_path) else 0
        if expect is None or size >= expect:
            return size
        if size == have:
            print("# 本轮零进展，重试（第 %d 次）" % (attempt + 1), file=sys.stderr)

    raise IOError("下载始终不完整：%s" % url)


# ---------------------------------------------------------------- 音频处理
def probe_peak(ffmpeg, path):
    """返回 max_volume(dB)，失败返回 None。"""
    proc = subprocess.run(
        [ffmpeg, "-hide_banner", "-i", path, "-af", "volumedetect", "-f", "null", "-"],
        capture_output=True, text=True,
    )
    m = re.search(r"max_volume:\s*(-?\d+(?:\.\d+)?) dB", proc.stderr or "")
    return float(m.group(1)) if m else None


def convert(ffmpeg, src, dst, fmt, rate=44100, peak_db=-1.5, crop=None,
            channels=1, extra_af=None, fade_out=0.0):
    """统一声道 / 采样率，峰值归一到 peak_db，再编码。

    channels:   1=单声道（sfx 默认），2=保留立体声（BGM 需要）
    extra_af:   额外的 -af 片段，如 "lowpass=f=6000"，用于削掉硬质毛刺
    fade_out:   裁剪后的淡出秒数，避免硬切爆音
    """
    gain = 0.0
    cur = probe_peak(ffmpeg, src)
    if cur is not None:
        gain = peak_db - cur
        gain = max(-24.0, min(18.0, gain))

    chain = []
    if extra_af:
        chain.append(extra_af)
    if abs(gain) > 0.05:
        chain.append("volume=%.2fdB" % gain)
    if fade_out and fade_out > 0:
        total = crop or duration_of(ffmpeg, src) or 0
        if total > 0:
            chain.append("afade=t=out:st=%.3f:d=%.3f" % (max(0.0, total - fade_out), fade_out))
    filters = ",".join(chain) if chain else None

    cmd = [ffmpeg, "-y", "-hide_banner", "-loglevel", "error", "-i", src]
    if crop:
        cmd += ["-t", "%.3f" % crop]
    cmd += ["-ac", str(channels), "-ar", str(rate)]
    if filters:
        cmd += ["-af", filters]
    if fmt == "ogg":
        cmd += ["-c:a", "libvorbis", "-q:a", "4"]
    elif fmt == "mp3":
        # 本机 ffmpeg 无 libvorbis，BGM 用 mp3（Unity 同样支持，循环接缝略差于 ogg）
        cmd += ["-c:a", "libmp3lame", "-b:a", "128k"]
    else:
        cmd += ["-c:a", "pcm_s16le"]
    cmd.append(dst)
    subprocess.run(cmd, check=True)


def loop_crossfade(ffmpeg, path, seconds, channels=2, rate=44100, bitrate="128k", peak_db=None):
    """把结尾的淡出尾巴与开头交叉淡化，做成真正能无缝循环的版本。

    原理：out = x[0 : n-D]；把 out 的开头 D 秒替换为
    「x[0:D] 淡入 + x[n-D:n] 淡出」的等功率混合。
    这样播完 out 的最后一个样本（≈ x[n-D]）接回 out[0]（同样从 x[n-D] 起）
    是同一点，接缝连续；代价是开头 D 秒由原 intro 变成「尾巴→intro」过渡。
    成品长度缩短 D 秒。
    """
    try:
        import numpy as np
    except ImportError:
        print("# 缺 numpy，跳过 loop_crossfade", file=sys.stderr)
        return None

    raw = subprocess.run(
        [ffmpeg, "-v", "error", "-i", path, "-f", "f32le",
         "-ac", str(channels), "-ar", str(rate), "-"],
        capture_output=True, check=True).stdout
    x = np.frombuffer(raw, dtype="<f4")
    if x.size % channels:
        x = x[: x.size - x.size % channels]
    x = x.reshape(-1, channels).astype("float32")

    n = len(x)
    D = int(seconds * rate)
    if n <= 3 * D:
        print("# 音频太短，跳过 loop_crossfade", file=sys.stderr)
        return None
    L = n - D

    ramp = np.sin(np.linspace(0.0, np.pi / 2, D, dtype="float32"))[:, None] ** 2  # 0→1
    out = x[:L].copy()
    out[:D] = x[:D] * ramp + x[L:L + D] * (1.0 - ramp)

    # 交叉淡化会让两个不同相位的信号叠加，峰值可能抬升几个 dB，这里重新归一
    if peak_db is not None:
        cur = float(np.max(np.abs(out)) + 1e-12)
        out *= (10.0 ** (peak_db / 20.0)) / cur

    subprocess.run(
        [ffmpeg, "-y", "-hide_banner", "-loglevel", "error",
         "-f", "f32le", "-ar", str(rate), "-ac", str(channels), "-i", "-",
         "-c:a", "libmp3lame", "-b:a", bitrate, path],
        input=out.tobytes(), check=True)
    return round(L / rate, 3)


def duration_of(ffmpeg, path):
    proc = subprocess.run(
        [ffmpeg.replace("ffmpeg", "ffprobe") if False else shutil.which("ffprobe") or "ffprobe",
         "-v", "error", "-show_entries", "format=duration",
         "-of", "default=noprint_wrappers=1:nokey=1", path],
        capture_output=True, text=True,
    )
    try:
        return round(float((proc.stdout or "").strip()), 3)
    except Exception:
        return None


# ---------------------------------------------------------------- 主流程
def fetch_one(opener, token, ffmpeg, spec, force=False, no_proxy=False, dry_run=False):
    name = spec["name"]
    fmt = spec.get("format", "wav")
    subdir = spec.get("dir") or ("bgm" if name.startswith("bgm_") else "amb" if name.startswith("amb_") else "sfx")
    dst = os.path.join(AUDIO_ROOT, subdir, "%s.%s" % (name, fmt))

    if os.path.exists(dst) and not force:
        return {"name": name, "status": "skipped", "path": dst}

    query = spec["query"]
    min_dur = spec.get("min_duration", 0.05)
    max_dur = spec.get("max_duration", 1.2)

    cands = search(opener, token, query, min_dur, max_dur)

    # 清单里写死了 freesound_id 就按 id 取，保证可复现
    forced_id = spec.get("freesound_id")
    if forced_id:
        chosen = next((c for c in cands if c["id"] == forced_id), None)
        if chosen is None:  # 查询词没命中就直接按 id 取详情
            chosen = sound_by_id(opener, token, forced_id)
        if chosen is None:
            return {"name": name, "status": "NO_RESULT", "query": query,
                    "error": "清单指定的 freesound_id=%s 查不到（可能已下架）" % forced_id}
    else:
        chosen = pick(cands, spec.get("prefer_longest", False))

    if not chosen:
        return {"name": name, "status": "NO_RESULT", "query": query,
                "range": [min_dur, max_dur], "note": spec.get("note", "")}

    if dry_run:
        return {"name": name, "status": "dry-run", "pick": chosen["name"],
                "id": chosen["id"], "duration": round(chosen.get("duration") or 0, 3)}

    tmp = os.path.join("/tmp/sfx_cc0", "%s.mp3" % name)
    os.makedirs(os.path.dirname(tmp), exist_ok=True)
    download(opener, chosen["_preview"], tmp)
    convert(ffmpeg, tmp, dst, fmt,
            peak_db=spec.get("peak_db", -1.5),
            crop=spec.get("crop"),
            channels=spec.get("channels", 1),
            extra_af=spec.get("af"),
            fade_out=spec.get("fade_out", 0.0))
    os.remove(tmp)

    if spec.get("loop_crossfade") and fmt == "mp3":
        loop_crossfade(ffmpeg, dst, spec["loop_crossfade"],
                       channels=spec.get("channels", 1),
                       peak_db=spec.get("peak_db", -1.5))

    return {
        "name": name,
        "status": "ok",
        "path": os.path.relpath(dst, PROJECT_ROOT),
        "out_duration": duration_of(ffmpeg, dst),
        "source": {
            "freesound_id": chosen["id"],
            "title": chosen["name"],
            "author": chosen.get("username"),
            "url": chosen.get("url"),
            "license": "CC0",
            "src_duration": round(chosen.get("duration") or 0, 3),
        },
    }


def list_one(opener, token, spec, limit=8):
    """列出候选，供人工挑选（挑好后把 id 写进 manifest 的 freesound_id）。"""
    cands = search(opener, token, spec["query"],
                   spec.get("min_duration", 0.05), spec.get("max_duration", 1.2))
    cands = sorted(cands, key=lambda r: (-(r.get("avg_rating") or 0), -int(r.get("num_ratings") or 0)))
    if spec.get("prefer_longest"):
        cands = sorted(cands, key=lambda r: -(r.get("duration") or 0))
    print("\n## %s  (%s | %s-%ss)" % (
        spec["name"], spec["query"], spec.get("min_duration", 0.05), spec.get("max_duration", 1.2)))
    if not cands:
        print("   (无 CC0 结果)")
    for c in cands[:limit]:
        print("   %-9s %6.2fs  ★%-4s %s" % (
            c["id"], c.get("duration") or 0, round(c.get("avg_rating") or 0, 1), c["name"][:62]))


def scan_disk():
    """遍历音频根目录，返回 [(subdir, filename)]。以磁盘为真源，避免清单与文件漂移。"""
    out = []
    for sub in ("sfx", "bgm", "amb"):
        d = os.path.join(AUDIO_ROOT, sub)
        if not os.path.isdir(d):
            continue
        for fn in sorted(os.listdir(d)):
            if fn.endswith(".meta") or fn.startswith("."):
                continue
            if not fn.lower().endswith((".wav", ".mp3", ".ogg")):
                continue
            out.append((sub, fn))
    return out


def _mod_desc(spec, fmt, channels, peak_db):
    if not spec:
        return "项目原有素材，未改造"
    if spec.get("via") == "workshop":
        # 工坊直选的素材是 Freesound 原始下载档（wav/flac/ogg），不是预览 mp3
        parts = (["Freesound 原档转 wav 44.1kHz/16bit"] if fmt == "wav"
                 else ["Freesound 原档重编码 mp3 128kbps"])
    elif fmt == "wav":
        parts = ["Freesound 预览 mp3 转 wav 44.1kHz/16bit"]
    else:
        parts = ["Freesound 预览 mp3 重编码 128kbps"]
    parts.append("保留立体声" if channels == 2 else "下混单声道")
    parts.append("峰值归一 %+.1fdB" % peak_db)
    if spec.get("crop"):
        parts.append("裁剪至 %.2fs" % spec["crop"])
    if spec.get("af"):
        parts.append("滤波 %s" % spec["af"])
    if spec.get("fade_out"):
        parts.append("结尾淡出 %.2fs" % spec["fade_out"])
    if spec.get("loop_crossfade"):
        parts.append("首尾交叉淡化 %.1fs 做无缝循环（总长缩短同等秒数）" % spec["loop_crossfade"])
    return "；".join(parts)


def write_credits(opener, token, ffmpeg, specs, out_dir):
    """生成 CC0 授权登记表（CSV + Markdown），覆盖磁盘上全部音频文件。"""
    import csv

    by_name = {s["name"]: s for s in specs}
    type_label = {"sfx": "音效 SFX", "bgm": "音乐 BGM", "amb": "环境音 AMB"}
    rows = []

    for sub, fn in scan_disk():
        stem, ext = os.path.splitext(fn)
        path = os.path.join(AUDIO_ROOT, sub, fn)
        spec = by_name.get(stem)
        fid = (spec or {}).get("freesound_id")
        fmt = ext.lstrip(".").lower()
        channels = (spec or {}).get("channels", 1)
        peak_db = (spec or {}).get("peak_db", -1.5)

        meta = sound_by_id(opener, token, fid) if fid else None
        out_dur = duration_of(ffmpeg, path)
        rows.append({
            "文件名": fn,
            "类型": type_label.get(sub, sub),
            "项目内用途": (spec or {}).get("note") or "全局 UI 按钮点击音",
            "Freesound ID": fid or "",
            "原始标题": (meta or {}).get("name", ""),
            "作者": (meta or {}).get("username", ""),
            "作品页": (meta or {}).get("url", ""),
            "授权": "CC0 1.0 Universal (Public Domain)" if fid else "项目自有素材（非外部来源）",
            "原始时长(s)": ("%.2f" % meta["duration"]) if meta else "",
            "使用时长(s)": ("%.2f" % out_dur) if out_dur else "",
            "最终格式": ("wav 44.1kHz/16bit 单声道" if fmt == "wav" and channels == 1
                          else "mp3 128kbps 立体声" if fmt == "mp3" and channels == 2
                          else "mp3 128kbps 单声道" if fmt == "mp3"
                          else "%s %d声道" % (fmt, channels)),
            "是否修改": "否" if not spec else "是",
            "修改说明": _mod_desc(spec, fmt, channels, peak_db),
        })

    os.makedirs(out_dir, exist_ok=True)
    csv_path = os.path.join(out_dir, "CC0_Audio_Credits.csv")
    md_path = os.path.join(out_dir, "CC0_Audio_Credits.md")

    fields = ["文件名", "类型", "项目内用途", "Freesound ID", "原始标题", "作者", "作品页",
              "授权", "原始时长(s)", "使用时长(s)", "最终格式", "是否修改", "修改说明"]
    with open(csv_path, "w", encoding="utf-8-sig", newline="") as f:
        w = csv.DictWriter(f, fieldnames=fields)
        w.writeheader()
        w.writerows(rows)

    total = len(rows)
    cc0 = sum(1 for r in rows if r["Freesound ID"])
    with open(md_path, "w", encoding="utf-8") as f:
        f.write("# 《记忆终会消散》音频素材授权登记表\n\n")
        f.write("> 本项目全部音频素材均为 **CC0 1.0 Universal（公有领域贡献）**，"
                "可商用、无署名义务。本表仅为溯源与合规审核留档。\n>\n")
        f.write("> 共 %d 条：CC0 外部素材 %d 条，项目自有素材 %d 条。\n>\n"
                % (total, cc0, total - cc0))
        f.write("> 授权文本：https://creativecommons.org/publicdomain/zero/1.0/deed.zh\n\n")
        f.write("生成方式：`python3 Tools/audio/fetch_cc0_sfx.py "
                "--manifest Tools/audio/sfx_manifest.json --credits ThirdParty`\n"
                "（数据来自 Freesound API + 磁盘实际文件，可复现）\n\n")

        for sub, label in (("sfx", "一、音效 SFX"), ("bgm", "二、音乐 BGM"), ("amb", "三、环境音 AMB")):
            group = [r for r in rows if r["类型"] == type_label[sub]]
            if not group:
                continue
            f.write("## %s（%d 条）\n\n" % (label, len(group)))
            f.write("| 文件名 | 用途 | Freesound ID | 原始标题 | 作者 | 原始时长 | 使用时长 | 修改说明 |\n")
            f.write("|---|---|---|---|---|---|---|---|\n")
            for r in group:
                fid = ("[%s](%s)" % (r["Freesound ID"], r["作品页"])) if r["Freesound ID"] else "—"
                f.write("| `%s` | %s | %s | %s | %s | %s | %s | %s |\n" % (
                    r["文件名"], r["项目内用途"], fid,
                    r["原始标题"] or "—", r["作者"] or "—",
                    r["原始时长(s)"] or "—", r["使用时长(s)"], r["修改说明"]))
            f.write("\n")

    print(json.dumps({"status": "credits", "csv": csv_path, "md": md_path,
                      "rows": total, "cc0": cc0}, ensure_ascii=False))
    return rows


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--manifest")
    ap.add_argument("--query")
    ap.add_argument("--name")
    ap.add_argument("--only", nargs="*")
    ap.add_argument("--format", default=None, choices=["wav", "ogg"])
    ap.add_argument("--max-duration", type=float)
    ap.add_argument("--min-duration", type=float)
    ap.add_argument("--force", action="store_true", help="已存在也重新抓")
    ap.add_argument("--dry-run", action="store_true", help="只搜索不下载")
    ap.add_argument("--list", type=int, nargs="?", const=8, default=None,
                    help="只列候选不下载（可跟条数，默认 8）")
    ap.add_argument("--no-proxy", action="store_true", help="忽略系统代理直连")
    ap.add_argument("--ffmpeg", default=None)
    ap.add_argument("--token-file", default=DEFAULT_TOKEN_FILE)
    ap.add_argument("--report", default=None, help="把结果写成 JSON 报告")
    ap.add_argument("--credits", default=None, metavar="OUT_DIR",
                    help="生成 CC0 授权登记表（CSV + Markdown）到指定目录，不下载任何文件")
    args = ap.parse_args()

    if not args.manifest and not (args.query and args.name):
        raise SystemExit("要么给 --manifest，要么同时给 --query 和 --name")

    ffmpeg = find_ffmpeg(args.ffmpeg)
    token = load_token(args.token_file)
    opener = build_opener(args.no_proxy)

    if args.manifest:
        with open(args.manifest, "r", encoding="utf-8") as f:
            specs = json.load(f)["sounds"]
        if args.only:
            want = set(args.only)
            specs = [s for s in specs if s["name"] in want]
            missing = want - {s["name"] for s in specs}
            if missing:
                raise SystemExit("清单里没有：%s" % ", ".join(sorted(missing)))
    else:
        specs = [{
            "name": args.name,
            "query": args.query,
            "format": args.format or "wav",
            "min_duration": args.min_duration if args.min_duration is not None else 0.05,
            "max_duration": args.max_duration if args.max_duration is not None else 1.2,
        }]

    results = []
    if args.list is not None:
        for spec in specs:
            list_one(opener, token, spec, limit=args.list)
        return

    if args.credits:
        write_credits(opener, token, ffmpeg, specs, args.credits)
        return

    for spec in specs:
        if args.format and not args.manifest:
            spec["format"] = args.format
        try:
            r = fetch_one(opener, token, ffmpeg, spec, force=args.force,
                          no_proxy=args.no_proxy, dry_run=args.dry_run)
        except Exception as e:  # 单条失败不影响整批
            r = {"name": spec["name"], "status": "ERROR", "error": "%s: %s" % (type(e).__name__, e)}
        results.append(r)
        print(json.dumps(r, ensure_ascii=False))

    if args.report:
        with open(args.report, "w", encoding="utf-8") as f:
            json.dump(results, f, ensure_ascii=False, indent=2)

    bad = [r for r in results if r["status"] in ("NO_RESULT", "ERROR")]
    print("\n# 汇总: ok=%d skipped=%d 失败=%d" % (
        sum(1 for r in results if r["status"] == "ok"),
        sum(1 for r in results if r["status"] == "skipped"),
        len(bad)), file=sys.stderr)
    for r in bad:
        print("# 失败 %s <- %s" % (r["name"], r.get("query") or r.get("error")), file=sys.stderr)


if __name__ == "__main__":
    main()
