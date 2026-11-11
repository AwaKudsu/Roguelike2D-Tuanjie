# -*- coding: utf-8 -*-
"""把 guaiwu/ 下的怪物帧抠干净，写进 Assets/_Project/Art/Enemies/。

为什么要有这个工具（而不是接着用 _Tuanjie/Editor/EnemyFrameImporter.cs）：
  那个导入器取**左上角一个颜色**、容差 90（曼哈顿）从四边泛洪。
  哥布林和骷髅弓箭手的截图底色是「透明棋盘格」——白 (255) 和灰 (192) 两种颜色交替，
  两者差 63×3 = 189，远超容差，于是**只抠掉了一半格子**，另一半留成了马赛克底。
  本工具改成「从边框聚类出全部底色（棋盘格 = 两种色），任一命中即算背景」，
  再泛洪，两种格子都能抠掉；暗绿底的另外四只也走同一条路，边缘更干净。

用法：
    python rekey_enemy_frames.py --dry-run            # 只算不写，打印报告
    python rekey_enemy_frames.py --out <临时目录>      # 写到别处，先看效果
    python rekey_enemy_frames.py                      # 直接覆盖工程里的帧
    python rekey_enemy_frames.py --sheet <目录>        # 额外出一张前后对比图

规则：
  · 输出尺寸必须和源图一致（PPU / 轴心 / 预制体尺寸都按高度算好了，尺寸一变全歪）；
  · 只把 alpha 清零，不缩放、不重采样、不抖动；
  · 泛洪从四边出发，所以人物内部与底色同色的像素不会被误伤。
"""
import argparse
import os
import sys
from collections import deque

import numpy as np
from PIL import Image

# 桌面文件夹名 → 工程里的资源名（和 EnemyFrameImporter / EnemyConfig 一致）
MONSTERS = [
    ("哥布林", "Goblin"),
    ("骷髅弓箭手", "SkeletonArcher"),
    ("史莱姆", "Slime"),
    ("蝙蝠", "Bat"),
    ("火焰法师", "FireMage"),
    ("持盾战士", "ShieldKnight"),
]

# 源目录的状态文件夹 → 工程里的状态名
STATES = [
    ("待机", "Idle"),
    ("移动", "Move"),
    ("攻击", "Attack"),
    ("受击", "Hurt"),
    ("死亡", "Death"),
]

DEFAULT_SRC = r"D:\zuoye\tuanjie\project\guaiwu"
DEFAULT_DST = r"D:\zuoye\tuanjie\project\TuanjieRoguelike2D-2\Assets\_Project\Art\Enemies"


def border_colors(rgb, border):
    """边框上出现过的颜色 → [(rgb, 像素数)]，按数量从多到少。"""
    flat = rgb[border]
    colors, counts = np.unique(flat.reshape(-1, 3), axis=0, return_counts=True)
    order = np.argsort(-counts)
    return [(colors[i].astype(int), int(counts[i])) for i in order]


def pick_backgrounds(rgb, tol=18, min_share=0.06, max_clusters=4):
    """从边框颜色里聚出「背景色」。

    棋盘格会聚出两种（白 / 灰），纯色底只会聚出一种。
    聚类用「和当前中心每通道差都 ≤ tol」的贪心吸收，避免 (255,255,255) 与 (192,192,192) 被并成一类。
    """
    h, w = rgb.shape[:2]
    border = np.zeros((h, w), bool)
    border[0, :] = border[-1, :] = True
    border[:, 0] = border[:, -1] = True

    items = border_colors(rgb, border)
    total = sum(n for _, n in items)
    clusters = []
    taken = [False] * len(items)

    for i, (c0, n0) in enumerate(items):
        if taken[i]:
            continue
        acc = n0
        members = [c0]
        taken[i] = True
        for j in range(i + 1, len(items)):
            if taken[j]:
                continue
            c1, n1 = items[j]
            if np.max(np.abs(c1 - c0)) <= tol:
                acc += n1
                members.append(c1)
                taken[j] = True
        if acc / total >= min_share:
            clusters.append(np.round(np.mean(members, axis=0)).astype(int))
        if len(clusters) >= max_clusters:
            break

    if not clusters:            # 理论上不会发生：至少留下最多那种
        clusters = [items[0][0]]
    return clusters


def background_mask(rgb, bg_colors, tol=18):
    mask = np.zeros(rgb.shape[:2], bool)
    for c in bg_colors:
        mask |= np.all(np.abs(rgb.astype(int) - c) <= tol, axis=2)
    return mask


def run_length(values):
    """[a,a,b,b,b] → [(a,2),(b,3)]"""
    out = []
    for v in values:
        if out and out[-1][0] == v:
            out[-1] = (v, out[-1][1] + 1)
        else:
            out.append((v, 1))
    return out


def checker_without_pattern(rgb, bg_colors, tol=18):
    """透明棋盘格底：返回 (mask, 说明) 或 (None, None)。

    棋盘格 = 中性灰 (192) / 中性白 (255) 交替，截图被二次采样过，
    两种格子之间还有一圈灰白渐变（这就是"马赛克感"的来源）。
    这里的判据是「中性色 + 足够亮」：
      · 白格子、灰格子、以及它们中间的过渡值都算背景；
      · 关键是**保留了暖色**——哥布林挥砍那道刀光是奶白偏黄（r-b 有 20 多），
        如果只按亮度判，它会被整片抠掉（第一版就掉了半个弧）。
    剩下的交给泛洪：人物内部的中性亮像素（牙齿、眼白）被轮廓包着，泛洪进不去。
    """
    if len(bg_colors) < 2:
        return None, None

    lum = [0.299 * c[0] + 0.587 * c[1] + 0.114 * c[2] for c in bg_colors]
    if max(lum) - min(lum) < 40:
        return None, None

    neutral_hi = [c for c in bg_colors if min(c) >= 235 and max(c) - min(c) <= 8]
    neutral_lo = [c for c in bg_colors if 165 <= min(c) and max(c) <= 215 and max(c) - min(c) <= 8]
    if not neutral_hi or not neutral_lo:
        return None, None

    low = int(min(neutral_lo[0]) - tol)
    ints = rgb.astype(int)
    neutral = (ints.max(axis=2) - ints.min(axis=2)) <= 6
    mask = neutral & (ints.mean(axis=2) >= low)
    return mask, f"棋盘格(中性亮色 ≥{low})"


def drop_border_fragments(alpha):
    """去掉贴着图边、但明显不是主体的碎块（源截图里切到一半的邻座怪）。

    判据：不连通主体 + 摸到了画布四边之一 + 面积远小于主体（< 主体的 25%）。
    最后那条是保险：万一背景没抠干净，背景会变成「贴着边的最大连通块」，
    没有这条就会把真正的人物当成碎块删掉。
    """
    h, w = alpha.shape
    seen = np.zeros((h, w), bool)
    comps = []
    for sy in range(h):
        for sx in range(w):
            if alpha[sy, sx] == 0 or seen[sy, sx]:
                continue
            q = deque([(sy, sx)])
            seen[sy, sx] = True
            cells = []
            touch = False
            while q:
                y, x = q.popleft()
                cells.append((y, x))
                if y == 0 or x == 0 or y == h - 1 or x == w - 1:
                    touch = True
                for ny, nx in ((y - 1, x), (y + 1, x), (y, x - 1), (y, x + 1)):
                    if 0 <= ny < h and 0 <= nx < w and alpha[ny, nx] and not seen[ny, nx]:
                        seen[ny, nx] = True
                        q.append((ny, nx))
            comps.append((cells, touch))

    if len(comps) <= 1:
        return 0
    comps.sort(key=lambda c: -len(c[0]))
    main = len(comps[0][0])
    removed = 0
    for cells, touch in comps[1:]:
        if touch and len(cells) < main * 0.25:
            for y, x in cells:
                alpha[y, x] = 0
            removed += len(cells)
    return removed


def flood_from_border(bg_like):
    """把「从四边连得上的背景像素」全标出来（四邻接）。"""
    h, w = bg_like.shape
    seen = np.zeros((h, w), bool)
    q = deque()

    def push(y, x):
        if 0 <= y < h and 0 <= x < w and bg_like[y, x] and not seen[y, x]:
            seen[y, x] = True
            q.append((y, x))

    for x in range(w):
        push(0, x)
        push(h - 1, x)
    for y in range(h):
        push(y, 0)
        push(y, w - 1)

    while q:
        y, x = q.popleft()
        push(y - 1, x)
        push(y + 1, x)
        push(y, x - 1)
        push(y, x + 1)
    return seen


def enclosed_small_blobs(bg_like, cleared, max_area):
    """被人物包住的小块背景（比如胳膊和身体之间露出的格子）。

    只在**像素颜色本来就是背景色**的前提下清掉，且连通块面积要小 ——
    大块的「浅色人物部分」不会被误伤。
    """
    h, w = bg_like.shape
    left = bg_like & ~cleared
    seen = np.zeros((h, w), bool)
    out = np.zeros((h, w), bool)
    for sy in range(h):
        for sx in range(w):
            if not left[sy, sx] or seen[sy, sx]:
                continue
            comp = []
            q = deque([(sy, sx)])
            seen[sy, sx] = True
            while q:
                y, x = q.popleft()
                comp.append((y, x))
                for ny, nx in ((y - 1, x), (y + 1, x), (y, x - 1), (y, x + 1)):
                    if 0 <= ny < h and 0 <= nx < w and left[ny, nx] and not seen[ny, nx]:
                        seen[ny, nx] = True
                        q.append((ny, nx))
                if len(comp) > max_area:
                    break
            if len(comp) <= max_area:
                for y, x in comp:
                    out[y, x] = True
    return out


def key_one(src_path, tol):
    im = Image.open(src_path).convert("RGBA")
    arr = np.array(im)
    rgb = arr[:, :, :3]
    bg = pick_backgrounds(rgb, tol=tol)

    checker_mask, checker_note = checker_without_pattern(rgb, bg, tol=tol)
    if checker_mask is not None:
        bg_like = checker_mask
    else:
        bg_like = background_mask(rgb, bg, tol=tol)

    cleared = flood_from_border(bg_like)

    # 被人物包住的小块背景（比如胳膊和身体之间露出的格子）
    small = enclosed_small_blobs(bg_like, cleared, max_area=max(24, int(arr.shape[0] * arr.shape[1] * 0.002)))
    cleared = cleared | small

    out = arr.copy()
    out[cleared, 3] = 0
    out[cleared, :3] = 0

    alpha = out[:, :, 3].copy()
    stray = drop_border_fragments(alpha)
    out[:, :, 3] = alpha
    out[alpha == 0, :3] = 0

    stats = dict(
        size=(im.width, im.height),
        bg=[tuple(int(v) for v in c) for c in bg],
        mode=checker_note or "纯色",
        cleared=int(cleared.sum()),
        total=int(cleared.size),
        leftover=int((bg_like & ~cleared).sum()),
        stray=stray,
    )
    return Image.fromarray(out, "RGBA"), stats


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--src", default=DEFAULT_SRC)
    ap.add_argument("--dst", default=DEFAULT_DST)
    ap.add_argument("--tol", type=int, default=18)
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--out", default="")
    ap.add_argument("--sheet", default="")
    args = ap.parse_args()

    dst_root = args.out or args.dst
    os.makedirs(dst_root, exist_ok=True)
    bad = 0
    made = 0
    sheets = []

    for folder, name in MONSTERS:
        mono_src = os.path.join(args.src, folder)
        if not os.path.isdir(mono_src):
            print("[SKIP] 找不到源目录 " + mono_src)
            bad += 1
            continue
        mono_dst = os.path.join(dst_root, name)
        os.makedirs(mono_dst, exist_ok=True)

        for state_cn, state_en in STATES:
            state_dir = os.path.join(mono_src, state_cn)
            if not os.path.isdir(state_dir):
                print("[SKIP] 缺状态目录 " + state_dir)
                continue
            files = sorted([f for f in os.listdir(state_dir) if f.lower().endswith(".png")])
            for i, frame in enumerate(files):
                src_path = os.path.join(state_dir, frame)
                dst_path = os.path.join(mono_dst, f"{name}_{state_en}_{i}.png")

                if os.path.exists(dst_path):
                    old = Image.open(dst_path).size
                    new = Image.open(src_path).size
                    if old != new:
                        print(f"[FAIL] 尺寸不一致 {name}_{state_en}_{i}: 工程 {old} vs 源 {new}")
                        bad += 1
                        continue

                out_img, st = key_one(src_path, args.tol)
                made += 1
                if st["leftover"] > 0:
                    print(f"[WARN] {name}_{state_en}_{i} 仍有 {st['leftover']} 个背景色像素没清（多半是人物内部同色）")
                if st["stray"] > 0:
                    print(f"[INFO] {name}_{state_en}_{i} 去掉贴边碎块 {st['stray']} 像素")
                if args.sheet:
                    sheets.append((f"{name}_{state_en}_{i}", src_path, out_img, st))
                if not args.dry_run:
                    out_img.save(dst_path)
            if files:
                last = key_one(os.path.join(state_dir, files[0]), args.tol)[1]
                print(f"       {state_en}: {len(files)} 帧，底色={last['bg']} 模式={last['mode']}")

        print(f"[OK] {folder} → {name}")

    print(f"\n处理 {made} 帧，失败 {bad} 帧")
    if args.sheet and sheets:
        write_sheet(sheets, args.sheet)
    return 0 if bad == 0 else 1


def write_sheet(sheets, out_dir):
    """把「原图 / 抠图后」摆在一起，方便肉眼过一遍。棋盘格底是画上去的，不是图里带的。"""
    os.makedirs(out_dir, exist_ok=True)
    cell = 200
    cols = 6
    rows = (len(sheets) + cols - 1) // cols
    sheet = Image.new("RGB", (cols * cell * 2, rows * cell), (24, 24, 28))
    for idx, (name, src_path, out_img, st) in enumerate(sheets):
        r, c = divmod(idx, cols)
        before = Image.open(src_path).convert("RGBA")
        before.thumbnail((cell, cell))
        after = out_img.copy()
        after.thumbnail((cell, cell))
        bg = checker(cell, cell)
        bg.paste(after, ((cell - after.width) // 2, (cell - after.height) // 2), after)
        base = Image.open(src_path).convert("RGBA")
        base.thumbnail((cell, cell))
        tile = Image.new("RGB", (cell, cell), (40, 40, 46))
        tile.paste(base, ((cell - base.width) // 2, (cell - base.height) // 2), base)
        sheet.paste(tile, (c * cell * 2, r * cell))
        sheet.paste(bg, (c * cell * 2 + cell, r * cell))
    path = os.path.join(out_dir, "sheet.png")
    sheet.save(path)
    print("[SHEET] " + path)


def checker(w, h, size=10):
    img = Image.new("RGB", (w, h), (200, 200, 200))
    px = img.load()
    for y in range(h):
        for x in range(w):
            if ((x // size) + (y // size)) % 2 == 0:
                px[x, y] = (255, 255, 255)
    return img


if __name__ == "__main__":
    sys.exit(main())
