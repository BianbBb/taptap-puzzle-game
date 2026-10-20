#!/usr/bin/env python3
"""按音效工坊导出的 JSON 替换项目音频。

读 `~/Desktop/记忆音效工坊` 导出的 `音效配置-*.json`，把每个 slot 的目标文件
换成 JSON 里指定的音效，并同步更新 `Tools/audio/sfx_manifest.json` 的 freesound_id
（授权登记表靠它，不能漏）。

用法：
    python3 Tools/audio/apply_sfx_config.py ~/Desktop/音效配置-20261020-2055.json
    python3 Tools/audio/apply_sfx_config.py <json> --dry-run     # 只看要做什么，不落盘
    python3 Tools/audio/apply_sfx_config.py <json> --workshop ~/Desktop/记忆音效工坊

规则：
- **目标文件名不变**（= SfxNames 常量 = YooAsset Address），只换内容。
- `.meta` 不动，导入设置（Streaming / 单声道 / 2D）全部保留。
- sfx：若源文件本来就是 44.1k/16bit/单声道的 wav（工坊产物就是），**直接拷贝字节**，
  零重编码；否则 ffmpeg 转 wav 44.1k/单声道 + 峰值归一 -1.5dB。
- bgm/amb：一律重编码成 mp3，并把电平归一**回该 slot 原本的目标电平**
  （bgm_story 是 -6dB，不能让换曲顺带把伴奏抬高 3dB）。
"""
import argparse
import json
import os
import shutil
import subprocess
import sys
import time

HOME = os.path.expanduser("~")
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
AUDIO_ROOT = os.path.join(ROOT, "GameUnity", "Assets", "BundleAssets", "Audios")
MANIFEST = os.path.join(ROOT, "Tools", "audio", "sfx_manifest.json")
FF = "/opt/anaconda3/bin/ffmpeg"
FP = "/opt/anaconda3/bin/ffprobe"
SR = 44100


def probe(path):
    out = subprocess.run(
        [FP, "-v", "error", "-show_entries", "stream=codec_name,channels,sample_rate",
         "-show_entries", "format=duration", "-of", "json", path],
        capture_output=True, text=True)
    try:
        d = json.loads(out.stdout)
        st = (d.get("streams") or [{}])[0]
        return {
            "codec": st.get("codec_name"),
            "channels": st.get("channels"),
            "rate": int(st.get("sample_rate") or 0),
            "duration": round(float(d["format"]["duration"]), 3),
        }
    except Exception:
        return None


def peak_db(path):
    proc = subprocess.run([FF, "-hide_banner", "-i", path, "-af", "volumedetect", "-f", "null", "-"],
                          capture_output=True, text=True)
    import re
    m = re.search(r"max_volume:\s*(-?\d+(?:\.\d+)?) dB", proc.stderr or "")
    return float(m.group(1)) if m else None


def slot_dir_ext(slot):
    sub = "bgm" if slot.startswith("bgm_") else "amb" if slot.startswith("amb_") else "sfx"
    d = os.path.join(AUDIO_ROOT, sub)
    if os.path.isdir(d):
        for fn in os.listdir(d):
            stem, ext = os.path.splitext(fn)
            if stem == slot and ext.lower() in (".wav", ".mp3", ".ogg"):
                return sub, d, os.path.join(d, fn), ext.lower()
    return sub, d, None, ".wav" if sub == "sfx" else ".mp3"


def apply_sfx(src, dst, dry, target_peak=-1.5, tol=0.5):
    """sfx：电平已经对就直拷（工坊产物多为 44.1k/16bit/单声道），否则转码。

    工坊里的候选库是**原始下载、没做电平归一**，同一条可能 -1.5dB、也可能 -10.7dB。
    项目里其他音效统一峰值 -1.5dB，不补齐的话有的音进游戏几乎听不见，
    所以这里按峰值归一（容差 0.5dB 内不动，避免无谓重编码）。
    """
    s = probe(src)
    spec_ok = (s and s["codec"] == "pcm_s16le" and s["channels"] == 1
               and s["rate"] == SR and dst.lower().endswith(".wav"))
    cur = peak_db(src) if spec_ok else None
    gain = 0.0 if cur is None else max(-24.0, min(24.0, target_peak - cur))
    if abs(gain) <= tol and spec_ok:
        if dry:
            return "copy", (s or {}).get("duration")
        shutil.copyfile(src, dst)
        return "copy", (probe(dst) or {}).get("duration")

    if dry:
        return "wav %+.1fdB" % gain, (s or {}).get("duration")
    af = ["volume=%.2fdB" % gain] if abs(gain) > 0.01 else []
    cmd = [FF, "-y", "-hide_banner", "-loglevel", "error", "-i", src]
    if af:
        cmd += ["-af", af[0]]
    cmd += ["-ac", "1", "-ar", str(SR), "-c:a", "pcm_s16le", dst]
    subprocess.run(cmd, check=True)
    return "wav %+.1fdB" % gain, (probe(dst) or {}).get("duration")


def apply_music(src, dst, channels, target_db, dry):
    """bgm/amb：重编码 mp3 并归一回该 slot 的目标电平。"""
    cur = peak_db(src)
    gain = 0.0 if cur is None else max(-24.0, min(18.0, target_db - cur))
    if dry:
        return "mp3 %.2fdB" % gain, (probe(src) or {}).get("duration")
    subprocess.run([FF, "-y", "-hide_banner", "-loglevel", "error", "-i", src,
                    "-ac", str(channels), "-ar", str(SR),
                    "-af", "volume=%.2fdB" % gain,
                    "-c:a", "libmp3lame", "-b:a", "128k", dst], check=True)
    return "mp3 %.2fdB" % gain, (probe(dst) or {}).get("duration")


def dump_manifest(man, path):
    """按项目原有风格写回 manifest：一行一条、按前缀分组、列对齐、组间空行。

    直接 json.dump 会把 30 多条压成 290 行，人工 diff 时全成噪音。
    """
    KEY_ORDER = ["name", "query", "freesound_id", "format", "channels", "loop_crossfade",
                 "min_duration", "max_duration", "prefer_longest", "peak_db",
                 "crop", "af", "fade_out", "via", "note"]

    def group_of(n):
        if n.startswith("sfx_"):
            return n.split("_")[1]          # ui / story / memory / item
        return "music"                      # bgm_* / amb_*

    groups, order = {}, []
    for s in man["sounds"]:
        g = group_of(s["name"])
        if g not in groups:
            groups[g] = []
            order.append(g)
        groups[g].append(s)

    def num(v):
        if isinstance(v, bool):
            return "true" if v else "false"
        if isinstance(v, int):
            return str(v)
        # 至少保留一位小数：-3.0 不写成 -3.00，0.3 不写成 0.3
        return "%.1f" % v if float(v) == int(v) else "%.2f" % v

    def field(k, v):
        val = v if isinstance(v, str) else num(v)
        if not isinstance(v, str):
            return '"%s": %s' % (k, val)
        return '"%s": %s' % (k, json.dumps(v, ensure_ascii=False))

    out = ["{",
           "  %s: %s," % (json.dumps("_comment"), json.dumps(man.get("_comment", ""), ensure_ascii=False)),
           "  %s: %s," % (json.dumps("_style"), json.dumps(man.get("_style", ""), ensure_ascii=False)),
           '  "sounds": [']
    for gi, g in enumerate(order):
        items = groups[g]
        nw = max(len(field("name", s["name"])) + 1 for s in items)
        qw = max(len(field("query", s.get("query", ""))) + 1 for s in items)
        for s in items:
            keys = [k for k in KEY_ORDER if k in s] + [k for k in s if k not in KEY_ORDER]
            parts = []
            for idx, k in enumerate(keys):
                last = (idx == len(keys) - 1)
                f = field(k, s[k])
                if k == "name":
                    f = (f + ("" if last else ",")).ljust(nw)
                elif k == "query":
                    f = (f + ("" if last else ",")).ljust(qw)
                elif not last:
                    f = f + ","
                parts.append(f)
            eol = "," if (s is not items[-1] or gi != len(order) - 1) else ""
            out.append("    { " + " ".join(parts) + " }" + eol)
        if gi != len(order) - 1:
            out.append("")
    out += ["  ]", "}", ""]
    with open(path, "w", encoding="utf-8") as f:
        f.write("\n".join(out))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("config")
    ap.add_argument("--workshop", default=os.path.join(HOME, "Desktop", "记忆音效工坊"))
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    cfg = json.load(open(os.path.expanduser(args.config), "r", encoding="utf-8"))
    with open(MANIFEST, "r", encoding="utf-8") as f:
        man = json.load(f)
    by_name = {s["name"]: s for s in man["sounds"]}

    rows, errors = [], []
    backup_dir = None
    for slot, a in (cfg.get("assignments") or {}).items():
        src = os.path.join(args.workshop, a["file"])
        if not os.path.isfile(src):
            errors.append("%s: 源文件不存在 %s" % (slot, src))
            continue

        sub, d, existing, ext = slot_dir_ext(slot)
        if existing is None:
            errors.append("%s: 项目里找不到目标文件（%s）" % (slot, d))
            continue

        spec = by_name.get(slot, {})
        dst_info = probe(existing) or {}

        # 覆盖前先备份一份，方便整批回滚（git 也能回，但 yml/新文件不进 git 时靠它）
        if not args.dry_run:
            if backup_dir is None:
                backup_dir = os.path.join(os.path.dirname(__file__),
                                          "_backup", time.strftime("%Y%m%d-%H%M%S"))
                os.makedirs(backup_dir, exist_ok=True)
            shutil.copy2(existing, os.path.join(backup_dir, os.path.basename(existing)))

        if sub == "sfx":
            how, dur = apply_sfx(src, existing, args.dry_run)
        else:
            # channels: 表里没写就沿用目标文件现有的声道数（mp3 槽多为 None）
            ch = spec.get("channels") or dst_info.get("channels") or 2
            how, dur = apply_music(src, existing, int(ch),
                                   float(spec.get("peak_db", -3.0)), args.dry_run)

        # manifest 同步：换 id，并清掉描述"旧处理"的参数（裁剪/滤波/淡出/循环淡化）
        if not args.dry_run and slot in by_name:
            fsid = a.get("freesoundId")
            note_extra = "｜工坊指定：" + (a.get("title") or "")
            # 工坊里"复用现用音乐"导出的是 "current-bgm_title" 这种占位，不是数字 id。
            # 解析回被复用那个 slot 的真实 Freesound id，否则授权登记表会取不到来源。
            if isinstance(fsid, str) and fsid.startswith("current-"):
                src_slot = fsid[len("current-"):]
                fsid = (by_name.get(src_slot) or {}).get("freesound_id")
                note_extra = "｜工坊指定：复用 %s（%s）" % (src_slot, a.get("title") or "")
            by_name[slot]["freesound_id"] = fsid
            by_name[slot]["via"] = "workshop"   # 授权登记表据此写准确的"修改说明"
            for k in ("crop", "af", "fade_out", "loop_crossfade"):
                by_name[slot].pop(k, None)
            base = (by_name[slot].get("note") or "").split("｜工坊指定")[0]
            by_name[slot]["note"] = base + note_extra

        rows.append((slot, os.path.basename(a["file"]), a.get("title"), how, dur,
                     a.get("roughness"), a.get("currentRoughness"),
                     os.path.relpath(existing, AUDIO_ROOT)))

    if rows:
        print("%-24s %-26s %-9s %-9s %-13s %s" % ("slot", "来源", "方式", "时长", "rough 旧→新", "→ 目标"))
        print("-" * 118)
        for slot, fn, title, how, dur, rn, ro, rel in rows:
            print("%-24s %-26s %-9s %-9s %-13s %s" % (
                slot, (title or fn)[:26], how, str(dur), "%s→%s" % (ro, rn), rel))

    if errors:
        print("\n!! 失败 %d 条：" % len(errors))
        for e in errors:
            print("   " + e)

    if not args.dry_run:
        dump_manifest(man, MANIFEST)
        print("\n已替换 %d 条，manifest 已同步。%s" %
              (len(rows), ""))
    else:
        print("\n[dry-run] 以上 %d 条是预览，未落盘。" % len(rows))

    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
