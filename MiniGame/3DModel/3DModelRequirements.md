# 3D 模型需求清单（3DModelRequirements）

《堡垒冲刺》 Fortress Rush 的 3D 模型生成需求与格式规范。
更新：2026-08-31 · 状态：**已生成并接入**（1-3 号模型经 Rodin 生成 GLB 并接入游戏，见 README M8；#4 方块小人未生成——人形骨骼动画需专用角色生成，保持程序化方块人）。

---

## 一、模型需求清单

> 游戏为体素 MC 风格。所有提示词遵循统一风格锚点：`Minecraft voxel style, blocky, low-poly game asset, centered`。
> 推荐流程：先文生图出概念参考图 → 再图生 3D（比纯文生 3D 可控）。

| # | 模型 | 数量 | 优先级 | 游戏内用途 |
|---|------|------|--------|-----------|
| 1 | 中世纪主城堡 | 2（蓝旗/红旗各一） | P0 | 双方 10 号主城的核心装饰（城池中央，替代现有金冠方块） |
| 2 | 瞭望塔 | 2（蓝/红） | P1 | 各城池门前装饰塔（替换现有柱帽方块塔） |
| 3 | 体素松树 | 1 | P1 | 替换程序化方块树（路侧 44 棵实例化） |
| 4 | 方块小人 | 1 | P2（可选） | 玩家/AI 角色外观；⚠️ 人形带骨骼需专用的带动画角色生成，不可用时保持程序化方块人 |

### 提示词（可直接复制）

**1. 主城堡（蓝方）** — 面数预算 ≤10k，PBR 纹理
```
A grand medieval stone castle with four cone-roofed corner towers and thick fortress walls,
flying blue banners with a white shield emblem, glowing blue gate arch in the front wall,
Minecraft voxel style, blocky cubes, low-poly game asset, centered, full building visible
```
**1b. 主城堡（红方）**：同上，`blue banners` → `red banners`，`blue gate` → `red gate`。

**2. 瞭望塔（蓝方）** — 面数预算 ≤5k
```
A medieval stone watchtower with crenellated battlements on top and a small blue flag,
round tapered tower body, Minecraft voxel style, blocky, low-poly game asset, centered
```
**2b. 瞭望塔（红方）**：`blue flag` → `red flag`。

**3. 体素松树** — 面数预算 ≤2k
```
A single voxel pine tree with layered dark-green blocky foliage and a brown blocky trunk,
Minecraft style, low-poly game asset, centered, no ground, no background objects
```

**4. 方块小人（可选）** — 需带骨骼动画的角色生成
```
A blocky Minecraft-style humanoid character, simple shirt and pants, standing straight,
voxel game asset, T-pose, no weapons
```

**不需要生成**：地形（程序化地面已有）、天空盒（ProceduralSky 已验收）、城墙方块（玩法核心，保持可破坏程序化方块）。

---

## 二、格式需求（引擎接入规范）

| 项 | 要求 |
|----|------|
| 文件格式 | **GLB**（引擎原生支持） |
| 落盘位置 | `D:\godogen\MiniGame\assets\glb\` |
| 命名 | `castle_blue.glb` / `castle_red.glb` / `tower_blue.glb` / `tower_red.glb` / `tree.glb` |
| 面数预算 | 单模型 ≤10k 面；全场景（2 城堡+多塔+44 树实例化）合计 ≤60k 面 |
| 纹理 | 内嵌 GLB，PBR on（引擎自动导入为材质） |
| 原点/朝向 | 期望底部中心、前向 -Z；不满足时接入代码量 AABB 修正 |
| 游戏内目标尺寸 | 主城堡高 ~12m（地块 3m）；瞭望塔高 ~6m；树高 ~5m —— 接入时按 AABB 实测缩放 |
| 碰撞 | 无需（本项目玩法不依赖物理碰撞，装饰模型） |

### 接入流程（工具可用后）

1. GLB 放入 `MiniGame/assets/glb/` → 运行同目录 `scrub_glb.py` 清洗产物元数据 → 按 README 命令重导入资源
2. 代码实例化 `PackedScene`，量 `MeshInstance3D` AABB → 计算缩放 → 脚部对齐 y=0（按引擎指南 GLB 规范）
3. 替换对应程序化模型（城堡→10 号城中央、塔→城门旁、树→路侧）；**缺文件自动回退程序化方块**，游戏不依赖模型可运行
4. 重录证明视频回看验收（`test/Presentation.cs` 流程不变）
