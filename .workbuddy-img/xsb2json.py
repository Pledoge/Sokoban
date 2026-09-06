# -*- coding: utf-8 -*-
"""Microban XSB -> Sokoban LevelData JSON 转换器
- XSB 文本首行是顶部 → 项目 y-up：文本第 r 行写入 y = height-1-r
- 外部空地（从边界泛洪不可达内部的外圈空格）填为 Wall，关卡呈完整砖墙矩形
- 标记 source="imported"、author="David W. Skinner"
"""
import json, os
from collections import deque

SRC = r"G:/unity/Thebeastadventure/Sokoban/.workbuddy-img/microban.txt"
DST = r"G:/unity/Thebeastadventure/Sokoban/Assets/Resources/Levels"

MAP = set("#$*@+.-")
FLOOR = MAP | {" "}

def is_map_line(s):
    """地图行：至少含一个非空格字符，且全部字符都属于地图符号或空格。"""
    return bool(s.strip()) and set(s.rstrip()) <= FLOOR

def parse_blocks(text):
    blocks, title, rows = [], None, []
    for line in text.splitlines():
        s = line.rstrip("\r\n")
        if s.strip().startswith(";"):
            if rows:
                blocks.append((title, rows)); rows = []
            title = s.strip().lstrip(";").strip()
            continue
        if is_map_line(s):
            rows.append(s)
        elif rows and not s.strip():
            blocks.append((title, rows)); rows = []
    if rows:
        blocks.append((title, rows))
    return blocks

def convert(title, rows):
    rows = [r for r in rows]
    w = max(len(r) for r in rows)
    h = len(rows)
    grid = [[0] * w for _ in range(h)]        # y=0 是文本最后一行（y-up）
    spawn = None
    goals = boxes = 0
    for r, line in enumerate(rows):
        y = h - 1 - r                          # 文本第 r 行（顶）→ y = h-1-r
        for x in range(w):
            c = line[x] if x < len(line) else " "
            if c == "#": grid[y][x] = 1
            elif c == "$": grid[y][x] = 4; boxes += 1
            elif c == "*": grid[y][x] = 5; boxes += 1; goals += 1
            elif c == ".": grid[y][x] = 3; goals += 1
            elif c == "@": spawn = (x, y)
            elif c == "+": spawn = (x, y); grid[y][x] = 3; goals += 1
            # 空格 → 0
    if spawn is None:
        return None, "无玩家出生点"
    if boxes != goals or boxes == 0:
        return None, f"箱子{boxes}/终点{goals} 不匹配"

    # 外部泛洪：从边界上所有 Empty 出发，凡连通的 Empty 都视为墙外 → Wall
    seen = [[False] * w for _ in range(h)]
    q = deque()
    for x in range(w):
        for y in (0, h - 1):
            if grid[y][x] == 0 and not seen[y][x]: seen[y][x] = True; q.append((x, y))
    for y in range(h):
        for x in (0, w - 1):
            if grid[y][x] == 0 and not seen[y][x]: seen[y][x] = True; q.append((x, y))
    while q:
        x, y = q.popleft()
        grid[y][x] = 1                          # 墙外 → Wall
        for nx, ny in ((x+1,y),(x-1,y),(x,y+1),(x,y-1)):
            if 0 <= nx < w and 0 <= ny < h and not seen[ny][nx] and grid[ny][nx] == 0:
                seen[ny][nx] = True; q.append((nx, ny))

    cells = [grid[y][x] for y in range(h) for x in range(w)]
    num = title.split()[0] if title else "?"          # 标题可能是 "44 'Duh!'"，只取编号
    lv = {
        "id": f"microban_{int(num):03d}",
        "name": f"Microban #{title}",
        "mode": 0,
        "width": w, "height": h,
        "author": "David W. Skinner",
        "source": "imported",
        "cells": cells,
        "playerSpawn": {"x": spawn[0], "y": spawn[1]},
        "enemySpawn": {"x": -99, "y": -99},
    }
    return lv, None

blocks = parse_blocks(open(SRC, encoding="utf-8").read())
os.makedirs(DST, exist_ok=True)
ok = fail = 0
for title, rows in blocks:
    lv, err = convert(title, rows)
    if lv is None:
        print(f"[跳过] {title}: {err}"); fail += 1; continue
    path = os.path.join(DST, lv["id"] + ".json")
    with open(path, "w", encoding="utf-8") as f:
        json.dump(lv, f, ensure_ascii=False)
    ok += 1
print(f"完成：成功 {ok}，失败 {fail}")
