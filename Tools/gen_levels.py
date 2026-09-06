#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""内置关卡生成器：ASCII -> JSON（落 Assets/Resources/Levels/）。

图例:
  #  墙(1)   .  空(0)   O  障碍(2)   G  终点(3)
  B  箱(4)   *  箱在终点(5)
  P  玩家(6) E  敌人(8)

坐标约定: y 向上 —— rows 列表的**最后一行**是 y=0（即写关卡时从上往下画）。
用法: python gen_levels.py
"""
import json, os

LEVELS = [
    {
        "id": "tut_01", "name": "第一步", "mode": 0, "author": "built-in",
        "rows": [
            "#######",
            "#.....#",
            "#.PBG.#",
            "#.....#",
            "#######",
        ],
    },
    {
        "id": "cls_01", "name": "黄金屋", "mode": 0, "author": "built-in",
        "rows": [
            "#########",
            "#.......#",
            "#.B.B.B.#",
            "#.......#",
            "#P......#",
            "#.G.G.G.#",
            "#.......#",
            "#########",
        ],
    },
    {
        "id": "cls_02", "name": "回廊", "mode": 0, "author": "built-in",
        "rows": [
            "#########",
            "#.......#",
            "#.P.B.B.#",
            "#.G...G.#",
            "#.......#",
            "#########",
        ],
    },
    {
        "id": "ext_01", "name": "镜像对决", "mode": 1, "author": "built-in",
        "rows": [
            "########",
            "#......#",
            "#.P.B..#",
            "#......#",
            "#..G...#",
            "#E.....#",
            "########",
        ],
    },
    {
        "id": "ext_02", "name": "双箱对峙", "mode": 1, "author": "built-in",
        "rows": [
            "#########",
            "#.......#",
            "#.P.B...#",
            "#.......#",
            "#.B.....#",
            "#..G.G..#",
            "#E......#",
            "#########",
        ],
    },
]

CODE = {'#': 1, '.': 0, 'O': 2, 'G': 3, 'B': 4, '*': 5, 'P': 6, 'E': 8}

def convert(spec):
    rows = spec["rows"]                       # 从上往下画
    h = len(rows)
    w = max(len(r) for r in rows)
    rows = [r.ljust(w, '.') for r in rows]
    cells = []
    player, enemy = None, None
    for y in range(h):                        # y=0 是最后一行
        row = rows[h - 1 - y]
        for x, ch in enumerate(row):
            cells.append(CODE.get(ch, 0))
            if ch == 'P': player = {"x": x, "y": y}
            if ch == 'E': enemy = {"x": x, "y": y}
    return {
        "id": spec["id"], "name": spec["name"], "mode": spec["mode"],
        "width": w, "height": h, "author": spec.get("author", ""),
        "cells": cells,
        "playerSpawn": player or {"x": 1, "y": 1},
        "enemySpawn": enemy or {"x": -99, "y": -99},
    }

def main():
    out_dir = os.path.join(os.path.dirname(__file__), "..", "Assets", "Resources", "Levels")
    os.makedirs(out_dir, exist_ok=True)
    for spec in LEVELS:
        data = convert(spec)
        path = os.path.join(out_dir, spec["id"] + ".json")
        with open(path, "w", encoding="utf-8") as f:
            json.dump(data, f, ensure_ascii=False, indent=2)
        print(f"OK  {path}  ({data['width']}x{data['height']}, mode={data['mode']})")

if __name__ == "__main__":
    main()
