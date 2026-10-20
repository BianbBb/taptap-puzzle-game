#!/usr/bin/env python3
"""合成《记忆终会消散》的物品入库音 `sfx_item_gain`。

为什么不用 Freesound：这一声要的语义太具体 ——「东西落进包里的那一记叮」，
既不能像记忆落槽（sfx_memory_land 那种闷软落点），也不能像 UI 提示音。
干脆按项目风格（钢片琴 / 卡林巴，纯五度叠音，无电子感）自己合，参数可复现。

用法：
    python3 Tools/audio/synth_item_gain.py            # 写入 Audios/sfx/
    python3 Tools/audio/synth_item_gain.py --peak -2  # 改峰值电平

输出：GameUnity/Assets/BundleAssets/Audios/sfx/sfx_item_gain.wav
      44.1kHz / 16bit / 单声道（和项目其它 sfx 一致，.meta 是 forceToMono）
"""
import argparse
import math
import os
import struct
import wave

SR = 44100
DURATION = 0.52
PEAK_DB = -1.5

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT_DIR = os.path.join(ROOT, "GameUnity", "Assets", "BundleAssets", "Audios", "sfx")
OUT_NAME = "sfx_item_gain.wav"

# (频率倍数, 幅度, 衰减倍率)。非整数倍泛音（3.01 / 4.16）是钢片琴的特征，
# 用整数倍会变成电子风琴音色 —— sfx_manifest 的 _style 明确禁止 8bit。
PARTIALS = [
    (1.00, 1.00, 1.00),
    (2.00, 0.30, 1.55),
    (3.01, 0.13, 2.10),
    (4.16, 0.06, 2.70),
]

# G6 主音 + C6 垫音（纯四度下方），一起衰减。选 G6 是因为 1046~1600Hz 区间
# 在手机外放上也听得见，又不像 E7 那么刺。
BASE_HZ = 1567.98
UNDER_HZ = 1046.50


def envelope(t, attack, tau):
    """起音半余弦 + 指数衰减。attack 够短（4ms）才不会「噗」一声。"""
    if t < attack:
        return 0.5 - 0.5 * math.cos(math.pi * t / attack)
    return math.exp(-(t - attack) / tau)


def synth(peak_db=PEAK_DB):
    total = int(SR * DURATION)
    samples = [0.0] * total
    tau = 0.115
    attack = 0.004

    for i in range(total):
        t = i / SR
        value = 0.0
        for ratio, amp, decay in PARTIALS:
            freq = BASE_HZ * ratio
            if freq >= SR / 2:
                continue
            value += amp * math.sin(2 * math.pi * freq * t) * envelope(t, attack, tau / decay)
        # 垫音：低一个四度，弱且更快消，只负责「厚度」不负责「亮」。
        value += 0.26 * math.sin(2 * math.pi * UNDER_HZ * t) * envelope(t, attack * 2, tau * 0.62)
        # 极轻的木槌敲击：8ms 的高频噪声，让它有「被敲了一下」的物理感。
        if t < 0.008:
            value += 0.05 * math.sin(2 * math.pi * 5200 * t) * (1 - t / 0.008)
        samples[i] = value

    peak = max(abs(v) for v in samples) or 1.0
    target = 10 ** (peak_db / 20.0)
    scale = target / peak
    # 尾部 60ms 线性收掉，避免波形被硬切出咔哒声。
    tail = int(SR * 0.06)
    for i in range(total - tail, total):
        samples[i] *= (total - i) / tail
    return [max(-1.0, min(1.0, v * scale)) for v in samples]


def write_wav(path, samples):
    with wave.open(path, "wb") as fp:
        fp.setnchannels(1)
        fp.setsampwidth(2)
        fp.setframerate(SR)
        frames = b"".join(struct.pack("<h", int(round(v * 32767))) for v in samples)
        fp.writeframes(frames)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--peak", type=float, default=PEAK_DB, help="峰值电平 dBFS，默认 -1.5")
    args = parser.parse_args()

    os.makedirs(OUT_DIR, exist_ok=True)
    path = os.path.join(OUT_DIR, OUT_NAME)
    samples = synth(args.peak)
    write_wav(path, samples)
    print(f"wrote {path}  ({len(samples) / SR:.2f}s, peak {args.peak:g} dBFS)")


if __name__ == "__main__":
    main()
