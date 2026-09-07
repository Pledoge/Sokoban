# 03 · 美术资源文档（ArtBible · AI 生图清单）

> 美术风格统一关键词：**奶蛙 · Q 弹 · 圆润 · 暖色 + 烫金点缀**
> 所有素材优先用 AI 生图（`ImageGen`），不合格的进二次 inpaint，未通过的用色块占位。
>
> **版本**：v4（2026-09-07）—— **局内素材已落地**（8 张，见 §3.1）；**UI 部分走 UI Builder 产出**（§4）。
>
> **v4 交付状态**：局内 8 张素材已生成并处理完毕，落 `Assets/Resources/Art/`，由 `BoardRenderer` 运行时加载；
> 角色**多方向动作帧（§2.1 / §2.2）尚未产出**，当前表现为单帧静态（不影响玩法，列为后续项）。

---

## 1. 风格锚定（Style Anchor）

### 1.1 通用 prompt 前缀

复制下面这段到任何 prompt 前面，保证风格统一：

```
A cute round "milk-frog" style character / asset,
2D game art, soft volumetric shading,
warm cream palette #FFF6E0 with gold accents #E8B931,
thick 2px clean outline, no texture noise,
chibi proportions (2-head tall), flat background,
studio ghibli friendly vibe, family-friendly,
game asset, no text, no watermark.
```

### 1.2 配色卡

| 用途 | 色值 | 备注 |
|---|---|---|
| 主背景 / 地板 | `#FFF6E0` | 奶油白 |
| 点缀金 | `#E8B931` | 烫金，箱子 / 终点 / Logo |
| 主角皮肤 | `#A5D86E` | 鲜绿带奶白肚皮 |
| 反派披风 | `#A24B6E` | 紫红 |
| 墙 | `#9E8C7A` | 灰咖石砖 |
| 障碍 | `#5C4033` | 深咖荆棘 |
| UI 主按钮 | `#FFC857` | 圆角矩形 + 烫金描边 |
| UI 危险按钮 | `#E76F51` | 撤销 / 重置 |

---

## 2. 角色动作清单

> 主角 = "奶蛙推箱手"，反派 = "邪恶推箱人"
> 每个动作需要 1 张主图 + （可选）1 张附加表情包落 PNG，64×64 的 4×1 sprite sheet 提供。

### 2.1 主角 · 奶蛙推箱手

| # | 文件名 | 动作描述 | Prompt 模板（套 §1.1 前缀） |
|---|---|---|---|
| 1 | `hero_idle.png` | 站立，左右微晃，嘴叼羽毛笔 | `idle pose, frog pushing a wooden box, smiling, eye forward, pen in mouth` |
| 2 | `hero_push_up.png` | 推箱子朝上看 | `pushing a heavy box upward, eyes wide, sweat drop, leaning forward` |
| 3 | `hero_push_down.png` | 推箱子朝下看 | `pushing a box downward, leaning forward, both hands on box` |
| 4 | `hero_push_left.png` | 推箱子朝左 | `pushing a box to the left, side view, leaning left foot forward` |
| 5 | `hero_push_right.png` | 推箱子朝右 | `pushing a box to the right, side view, leaning right foot forward` |
| 6 | `hero_walk.png` | 空手走路 4 方向 | 共用 4 张（`hero_walk_up/down/left/right.png`），无箱子版本 |
| 7 | `hero_win.png` | 胜利欢呼 | `jumping with arms up, confetti around, big smile` |
| 8 | `hero_bump.png` | **撞墙 / 撞敌人顶一下**（v2：撞墙是合法战术操作，表情要中性，**不要**迷惑/失败感） | `frog bracing shoulder against a wall, leaning in, one fist up, determined neutral face, small impact star` |
| 9 | `hero_bump_idle.png` | 顶住不动的持续帧（挤压 1px） | 同上，身体轻微压缩，无位移 |

### 2.2 反派 · 邪恶推箱人

> 同身型，紫红披风 + 单眼护目镜 + 紫光尾迹。配色不同，但动作姿态与主角严格 1:1 对齐，方便动画对照。

| # | 文件名 | 动作描述 | Prompt |
|---|---|---|---|
| 1 | `villain_idle.png` | 站立挑眉 | `evil twin of the frog, purple cape, single goggle, smirk, cross arms` |
| 2 | `villain_push_up/down/left/right.png` | 推箱子 4 方向 | `evil twin frog pushing a box <direction>, purple cape flutters, goggle glints` |
| 3 | `villain_walk.png` | 空手走路 4 方向 | 紫色版本 |
| 4 | `villain_fail.png` | 撞玩家取消（动不了） | `evil twin frog tripped, dust cloud, frustrated face` |
| 5 | `villain_bump.png` | **撞墙顶一下**（v2：敌人与玩家解耦后会频繁单独撞墙，需要独立反馈） | `evil twin frog bracing against a wall, purple cape pressed flat, annoyed but focused, small impact star` |

### 2.3 生图 → 可用素材流程（v4 实际管线）

```text
① ImageGen 生 1024×1024（transparent 背景 + 风格前缀）
② 批处理脚本 .workbuddy-img/process.py：
   a. 边缘泛白 BFS 去底（只移除与画布边缘连通的近白像素，不伤轮廓内部）
   b. 预裁四边 ~6% —— 去 AI 生图右下角水印
   c. 按 alpha bbox 裁切 → 补方形留白（物件 10%，贴图 2%）→ 缩放 256×256
③ 落盘 Assets/Resources/Art/<名字>.png
④ BoardRenderer 运行时 Resources.Load<Texture2D> + Sprite.Create（PPU = 图宽 → 1 格 = 1 单位）
⑤ 缺图自动回退程序化色块 —— 美术永远不阻塞逻辑
```

> ⚠️ **踩坑记录**：① 带内部空格的行别用「去空格后的字符集」判断是否地图行（会整行丢弃）；
> ② AI 图角落几乎必有水印，整图素材（墙 / 地板）裁 6% 再补边不伤观感；③ 首版不满意的直接重生成
> （墙首版画成了"石子托盘"、障碍颜色过浅，均已重出）。

---

## 3. 场景物件清单

| 物件 | 尺寸 / 设计 | Prompt 关键词 |
|---|---|---|
| 木箱 | 单格 64×64，木纹 + 4 颗金钉 | `wooden crate with gold corners, square flat, top-down slight angle` |
| 金箱 | 单格 64×24，纯金 + 高光 | `golden treasure chest, square top-down view, glowing` |
| 炸弹箱 | 拓展模式可选，黑铁 + 红色引信 | `iron bomb box with red fuse, square top-down` |
| 终点 | 单格 64×64，金色光圈 + ✦ | `glowing gold ring target marker, circular halo, top-down` |
| 终点激活态 | 箱子已上 + 同色淡金光 | 同上但饱和度 +30%，加星星 |
| 外墙 | 64×64，石砖 + 金描边 | `stone brick wall, rounded top-down block, gold edge highlight` |
| 障碍（荆棘） | 64×64，深咖 + 紫尖刺 | `dark thorn bush obstacle, spiky top-down, deep purple accents` |
| 地砖（默认） | 单格，奶油白 + 浅金格子 | `soft cream ivory floor tile, checkerboard, top-down` |
| 背景 | 1920×1080 远景 | `warm cozy fantasy library back hall, blurred, bokeh` |

### 3.1 已交付素材清单（v4 · `Assets/Resources/Art/`）

| 文件 | 对应物件 | 备注 |
|---|---|---|
| `player.png` | 奶蛙推箱手（单帧） | 奶油肚 + 奶绿背，大眼 |
| `enemy.png` | 邪恶推箱人（单帧） | 紫红 + 怒眉獠牙 + 小角 |
| `box.png` | 木箱 | 木纹 + 金色角件与绑带 |
| `box_on_goal.png` | 金色箱子 | 箱子推上终点自动切换（`BoardRenderer` 按地形判断） |
| `goal.png` | 终点标记 | 金环 + ✦ + 奶油底垫 |
| `wall.png` | 外墙 | 砖纹满格贴图（裁 6% 去水印后重新补边） |
| `obstacle.png` | 障碍岩石 | 深巧克力棕，与木箱拉开色差（首版过浅已重出） |
| `floor.png` | 地板砖 | 奶油棋盘格，非墙格铺满（v4 新增表现） |

> 渲染分层：地板(0) → 墙/障碍/终点(1) → 箱子/玩家/敌人(2)。UI 侧已有素材在 `Assets/Resources/UI/`（`bg_main` / `btn_primary` / `hud_panel` / `logo_frog` / `Round` 九宫格）。

---

## 4. UI —— 走 UI Builder 产出（v3 变更）

> **UI 不再逐张出图，改为用 UI Builder 网页排版后一键导入 Unity。**
> 这样多比例适配（9:16 / 9:19.5 / 9:21 / 16:9）在工具里切下拉就能预览，不用反复进 Unity 试锚点。

### 4.0 工具位置与工作流

| 项 | 路径 |
|---|---|
| 网页工具 | `Sokoban/UITools/UIBuilder/index.html`（双击打开，离线可用） |
| Unity 导入器 | `Assets/Editor/UIBuilder/UIBuilderImporter.cs` |
| AI 排版代理 | `Sokoban/UITools/UIBuilder/start-proxy.bat`（被 CSP 拦截时启动，页面勾「本地代理模式」） |
| Unity 菜单 | `UI Builder / Import from JSON` · `Import from Package` · `Export to JSON (回写网页)` |
| 产出 | `Assets/UIBuilder/<docName>.prefab` |

**流程**：

```text
① 打开 index.html，顶部「屏幕比例」选 9:16（竖屏主设计比例）
② 策划模式排版 → 「✨ AI 排版」一句话出初版 → 微调锚点/位置
③ 切「🎨 美术」模式上色、贴背景素材（本节的 ui_*.png 在这里用上）
④ 「📤 导出 → 完整包」得到 .json
⑤ Unity: UI Builder / Import from JSON → 生成 prefab
⑥ 程序绑 onClick（写在独立脚本里，重导入不会丢）
```

> ⚠️ **踩坑预防**：按钮的 `onClick` **不要直接挂在 prefab 上**，写在独立脚本里按名字查找绑定。
> 否则重新导入 UI 时绑定会丢。工具虽然做了增量保护，但自己再稳一层更保险。

### 4.1 需要做的 UI 套数

> 每套 = UI Builder 里的一个文档标签（对应一个 Unity 根 prefab）。

| # | docName | 内容 | 主设计比例 |
|---|---|---|---|
| 1 | `MainMenu` | Logo + [经典模式] [拓展模式] [关卡编辑器] [退出] | 9:16（横屏另存一套） |
| 2 | `LevelSelect` | 关卡列表 + 模式切换 + 返回 | 9:16 |
| 3 | `GameHUD` | 步数 / 撤销数 / 用时 + [暂停][撤销][重做][重置][菜单] | 9:16 |
| 4 | `GameHUD_Land` | 同上，横屏布局（HUD 分列左右） | 16:9 |
| 5 | `WinPanel` | 胜利框 + 步数 / 撤销 / 用时 + [下一关][重玩][回菜单] | 9:16 |
| 6 | `PausePanel` | [继续][重玩][回菜单][音效开关] | 9:16 |
| 7 | `EditorPanel` | 工具栏 7 格 + [保存][加载][测试][检测有解] + 网格区 | 16:9（编辑器 PC 为主） |

### 4.2 UI 用图清单（给 UI Builder「背景素材」字段用）

> 这些仍是 AI 生图，但**只是贴图**，布局交给工具。

**主菜单**

| 元素 | 描述 |
|---|---|
| `ui_logo.png` | 大 Logo：「推箱子黄金无敌至尊版」+ 烫金花纹（**不带文字**，文字用 UI Builder 的 Text 节点） |
| `ui_bg_main.png` | 主菜单背景（暖色书房远景虚化） |
| `ui_btn_primary.png` | 通用按钮底（烫金圆角）；按下态 `ui_btn_primary_press.png` |
| `ui_btn_danger.png` | 危险按钮底（撤销 / 重置）；按下态同名 `_press` |

**HUD / 结算 / 编辑器**

| 元素 | 描述 |
|---|---|
| `ui_hud_panel.png` | 半透明金底信息面板 |
| `ui_icon_undo.png` / `ui_icon_redo.png` / `ui_icon_reset.png` / `ui_icon_pause.png` / `ui_icon_menu.png` | 5 个图标，线性风格，金色描边 |
| `ui_win_panel.png` | 胜利大金框 |
| `ed_icon_wall/player/enemy/box/goal/obstacle/erase.png` | 编辑器工具栏 7 个图标 |

### 4.3 比例适配要求

| 比例 | 对应设备 | 检查点 |
|---|---|---|
| 9:16 · 720×1280 | 常规竖屏 | 棋盘完整可见，下方操作区拇指可达 |
| 9:19.5 · 720×1560 | 全面屏 | 同上，HUD 不被拉长 |
| 9:21 · 720×1680 | 超长屏 | 上下留白均匀，不挤压棋盘 |
| 16:9 · 1280×720 | 横屏 / PC | HUD 分列左右，棋盘居中占主视觉 |

> 每套 UI 在工具里**逐个比例切一遍**确认无遮挡再导出。这是工具带来的最大红利 —— 手写得改五遍锚点。

---

## 5. 关卡默认美术覆盖

- 经典模式用奶油地砖 + 金色终点。
- 拓展模式整体调暗 15%（背景 overlay 用纯黑 alpha 0.15）。
- 反派只出在拓展模式关卡。

---

## 6. 临时占位策略（v4：程序化兜底）

> 占位不再走 `_Placeholder/` 目录，改为 **`BoardRenderer` 程序化兜底**：
> 素材缺失时按 `FallbackColors` 画纯色 / 圆角色块（64px 程序纹理）。
> 好处：删掉任何一张 `Resources/Art/*.png`，游戏照跑，只是变回色块 —— 美术与逻辑彻底解耦。

## 7. 验收 Checklist

**游戏素材（AI 生图）· v4 状态**

- [x] 场景物件 8 类齐（玩家 / 敌人 / 木箱 / 金箱 / 终点 / 墙 / 障碍 / 地板）
- [x] 所有 PNG 为透明背景、256×256、中心 pivot，`BoardRenderer` 可直接加载
- [x] 美术配色与 §1.2 一致（暖金系）
- [x] 程序化占位兜底可用（删图即回退）
- [ ] 主角 / 反派多方向动作帧（§2.1 / §2.2 的 4 方向 + bump 表情）—— **后续项**，当前单帧
- [ ] 背景远景图

**UI（UI Builder 产出）**

- [x] 6 套 UI 排版完成并导入 `Assets/UIBuilder/`（主菜单 / 选关 / HUD / 暂停 / 结算 / 编辑器）
- [x] 1080×1920（用户手调稿）与 16:9 下无遮挡无溢出（编辑态预览与 Play 共用同一套缩放引擎）
- [x] `onClick` 全部写在独立脚本里（不在 prefab 上直接挂），重导入不丢
- [ ] HUD 图标化（当前为文字按钮）
- [x] UI 配色与游戏素材一致（共用 §1.2 色卡）
