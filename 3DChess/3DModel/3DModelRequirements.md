# 3D 模型需求清单（3DModelRequirements）

《3D 中国象棋 Xiangqi3D》棋子模型生成需求与格式规范。
更新：2026-08-31 · 状态：**待生成**（工具接入后按本文执行；程序化车削棋子为回退方案）

---

## 一、模型需求清单

> 风格目标：**拟真卡通化**（stylized realistic cartoon）——圆润雕塑感 + 柔和 PBR 材质 + 手绘光泽，
> 参考 Clash Royale / Royal Match 的棋子质感；不是体素、不是写实文物。
> 形制：立体象棋——7 类棋子各为独立小雕塑，立于车削木圆座之上，阵营以配色区分
> （红方=朱漆+黄铜点缀 / 黑方=墨黑+鎏金点缀），底座前缘保留角色刻字以便读盘。

| # | 棋子 | 模型数 | 优先级 | 造型设定 | 高度（相对帅=1.0） |
|---|------|--------|--------|----------|------------------|
| 1 | 帅 / 将 | 2 | P0 | 披甲端立的将军俑，盔缛高耸，威严呆萌 | 1.0 |
| 2 | 车 | 2 | P0 | 双轮木质战车，带顶棚与车辕 | 0.85 |
| 3 | 马 | 2 | P0 | 微抬前蹄的战马，配辔头与鞍毯 | 0.85 |
| 4 | 炮 / 砲 | 2 | P0 | 架在双轮木车上的古代火炮，炮口上扬 | 0.85 |
| 5 | 兵 / 卒 | 2 | P1 | 持矛戴小斗笠的士兵俑，最矮最萌 | 0.65 |
| 6 | 仕 / 士 | 2 | P1 | 长袍持笏的文官俑 | 0.75 |
| 7 | 相 / 象 | 2 | P1 | 背披华丽鞍布的战象 | 0.80 |

共 14 个模型（7 类 × 红黑双方）。红黑只差配色与纹样，几何可共享思路但需分别生成（贴图烘焙进模型）。

### 风格锚点（所有提示词共用后缀）

```
stylized cartoon 3D game asset, smooth rounded sculpt, soft PBR materials, glossy hand-painted finish,
mobile strategy game style, centered, single object, solid light background, no ground plane, no text
```

### 提示词（可直接复制，替换【】内阵营色）

**1. 帅 / 将**
```
A cute stylized Chinese general figurine standing proud in【vermillion red lacquered armor with golden
helmet crest】/【ink-black lacquered armor with bronze trim】, on a round turned wooden base with an
engraved Chinese character plaque on the front rim, 【warm maple base with red accent ring】/【dark
walnut base with gold accent ring】, <风格锚点>
```

**2. 车**
```
A cute stylized ancient Chinese war chariot with two large wooden spoked wheels and a small canopy,
pulled pole forward, painted in【vermillion red and brass】/【black and gold】livery, standing on a
round turned wooden base with character plaque, <风格锚点>
```

**3. 马**
```
A cute stylized Chinese war horse figurine with one front hoof raised, ornate bridle and saddle
cloth in【vermillion red with brass bells】/【black and gold】, on a round turned wooden base with
character plaque, <风格锚点>
```

**4. 炮 / 砲**
```
A cute stylized ancient Chinese bronze cannon mounted on a wooden carriage with two wheels, barrel
tilted slightly up,【red lacquered carriage with brass fittings】/【black lacquered carriage with
gold fittings】, on a round turned wooden base with character plaque, <风格锚点>
```

**5. 兵 / 卒**
```
A cute stylized Chinese foot soldier figurine holding a short spear and wearing a small conical
hat, simple cloth armor in【vermillion red】/【ink black】, on a round turned wooden base with
character plaque, <风格锚点>
```

**6. 仕 / 士**
```
A cute stylized Chinese royal advisor figurine in flowing court robes holding a jade tablet,
robe sash in【vermillion red】/【black with gold embroidery】, on a round turned wooden base with
character plaque, <风格锚点>
```

**7. 相 / 象**
```
A cute stylized war elephant figurine with ornate cloth draped over its back, friendly cartoon
eyes, drape in【vermillion red with golden trim】/【black with gold trim】, on a round turned
wooden base with character plaque, <风格锚点>
```

**不需要生成**：棋盘/桌面（程序化木纹棋盘已验收）、特效（代码粒子已有）。

---

## 二、格式需求

| 项 | 要求 |
|----|------|
| 文件格式 | **GLB**，内嵌 PBR 贴图 |
| 落盘位置 | `D:\godogen\3DChess\3DModel\glb\` |
| 命名 | `<type>_<side>.glb`，type ∈ {king, chariot, horse, cannon, soldier, advisor, elephant}，side ∈ {red, black}，共 14 个 |
| 面数预算 | 帅/将/车/马/炮 ≤8000；仕/相/兵 ≤5000；全 32 枚上场合计 ≤120k 面 |
| 原点 / 朝向 | 底座底面中心为原点，前向 +Z（底座刻字面朝镜头） |
| 游戏内目标尺寸 | 底座直径 0.86（格距 1.0），高度按上表比例（帅≈0.95）——接入时按包围盒实测缩放 |
| 命中/碰撞 | 无需碰撞体（拾取走格点判定，模型纯表现） |
| 贴图 | texture_mode=high；底座木色保留自然纹理，避免纯白过曝 |

### 接入流程（工具可用后）

1. 生成产物 GLB（Rodin 输出 URL `rodin_base_basic_pbr.glb`）下载至 `3DModel\glb\` 并按命名表重命名
2. 触发项目资源重导入
3. 逐个实例化，按包围盒实测缩放：底面贴地对齐格点、高度按上表比例归一
4. 替换程序化车削棋子；**缺文件自动回退程序化棋子**，游戏不依赖模型可运行
5. 重录对局验证视频回看（选中抬升、抛物线走子、吃子飞入收纳盘动画保持不变）

---

## 三、生成工具实操备忘（Rodin MCP，2026-08-31 实测）

- `provider` / `model_version` 参数会被后端忽略，实际全部路由 **Rodin**（type=rodin_generation）
- `face_limit` 映射为 qualityOverride（面数预算生效）
- 真正的质量杠杆：`tier`（Extreme-Low ~40s 快但糙 / **Gen-2.5-High 3–10 分钟，正式产物用这个**）+ `texture_mode=high`
- 建议批次策略：先用 Extreme-Low 快速出 14 个草稿全览造型 → 挑选/调整提示词后用 Gen-2.5-High 生成正式版
- 产物为 GLB 的下载 URL，需自行下载落盘
- 工具返回的 `poll_command_powershell` 内含 `function Try`（PowerShell 保留字），直接执行必报解析错——派发轮询任务时将函数改名（如 `Poll1`）
- 提示词遵循"文生图出概念图 → 图生 3D 更可控"：可先用文生图（豆包等）出单棋子概念图，再以参考图生成 3D
