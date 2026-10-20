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


def download(opener, url, out_path, retries=5):
    """带长度校验 + 断点续传的下载。CDN 偶尔会截断大文件，直接写会产出坏音频。"""
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
        with opener.open(req, timeout=180) as r:
            if expect is None:
                cl = r.headers.get("Content-Length")
                if cl and r.status == 200:
                    expect = int(cl)
                elif cl and r.status == 206:
                    expect = have + int(cl)
            chunk = r.read()
        with open(out_path, "ab" if have else "wb") as f:
            f.write(chunk)

        size = os.path.getsize(out_path)
        if expect is None or size >= expect:
            return size
        print("# 下载不完整 %d/%d，续传（第 %d 次）" % (size, expect, attempt + 1), file=sys.stderr)

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


def convert(ffmpeg, src, dst, fmt, rate=44100, peak_db=-1.5, crop=None):
    """统一转 mono / rate，峰值归一到 peak_db，再编码。"""
    gain = 0.0
    cur = probe_peak(ffmpeg, src)
    if cur is not None:
        gain = peak_db - cur
        gain = max(-24.0, min(18.0, gain))
    filters = "volume=%.2fdB" % gain if abs(gain) > 0.05 else None

    cmd = [ffmpeg, "-y", "-hide_banner", "-loglevel", "error", "-i", src]
    if crop:
        cmd += ["-t", "%.3f" % crop]
    cmd += ["-ac", "1", "-ar", str(rate)]
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
    convert(ffmpeg, tmp, dst, fmt, peak_db=spec.get("peak_db", -1.5), crop=spec.get("crop"))
    os.remove(tmp)

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
