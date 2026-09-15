# ParkingGame — 3D 俯视角倒车入库

Godot 4.7.1 (.NET/C#) 停车游戏：真实刚体车辆物理（`VehicleBody3D` + 四轮悬挂/牵引/转向）、
手动挡 R/N/D、**9 个关卡**（晴/雨/雪/烈日四种天气）、路上有走动的行人与巡场车、
金币与涂装车库、**内置关卡编辑器**。入库不由系统自动判定——停好后玩家点「报告我停好了」
按钮（或按 G），游戏检测并给出结果：成功弹出结算（用时/碰撞/角度误差/居中偏差/金币），
失败弹 toast 说明具体原因，可继续调整后再次报告。设计文档见 `GDD.md`。

## 运行

```bat
build_scene.bat   # dotnet build + headless 导入 + 生成 scenes/Main.tscn（首次或场景变更后）
run_game.bat      # 编译并启动游戏
run_demo.bat      # 第 1 关自动驾驶演示（无需输入注入的端到端验证）
record_demo.bat   # 录制证明视频 → screenshots/movie/parking_demo.mp4
```

或直接：`"Godot_4.7.1_mono_console.exe" --path <本目录> [-- --demo | --verify-report | --verify-hazards | --verify-features | --editor] [-- --level=N]`
（注意：`--` 之后的参数用 `OS.GetCmdlineUserArgs()` 读，`GetCmdlineArgs()` 只有引擎参数）。

## 操作

| 按键 | 功能 |
|---|---|
| ↑ / ↓ | 油门 / 刹车 |
| ← / → | 方向盘（速度敏感：低速满舵 34°，随车速衰减） |
| R / N / D | 挂倒挡 / 空挡 / 前进挡 |
| 空格 | 手刹 |
| **G / 底部按钮** | **报告我停好了——触发停车检测，给出结果** |
| B | 车库（涂装购买/选择；开局界面或结算后） |
| E（开局界面） | 关卡编辑器 |
| 0（开局界面） | 游玩编辑器保存的自定义关卡 |
| 回车 / 退格 | 重开 / 结算后下一关、重试 |
| PgUp / PgDn | 切换关卡 |
| P | 截图到 `screenshots/` |

## 关卡（9 内置 + 自定义）

| # | 名称 | 机制焦点 | 天气 |
|---|---|---|---|
| 1 | 侧方入库 | 教学：标准 S 弧 | 晴 |
| 2 | 窄位极限 | 前后余量 0.35m；行人横穿进车路线 | 晴 |
| 3 | 直角巷道 | 倒出巷道再摆尾；巷道内外行人 | 晴 |
| 4 | 斜列式车位 | 45° 斜位；行人 + 巡场车环路 | 晴 |
| 5 | 障碍绕行 | 锥桶 + 横停车辆 + 双行人 | 晴 |
| 6 | 墙缝极限 | 库宽 2.15m、容差 10° | 晴 |
| 7 | 暴雨侧位 | 雨丝粒子 + 雾 + 抓地 0.78（刹车变长） | 雨 |
| 8 | 风雪窄巷 | 雪花粒子 + 抓地 0.55（操控减半） | 雪 |
| 9 | 烈日广场 | 斜位 + 双行人 + 巡场车，烈日强光 | 烈日 |
| 0 | 自定义关卡 | 编辑器保存的 levels/custom.json | 编辑器设定 |

## 移动小怪

- **行人**（蓝衣胶囊）：直线路径往返，行走摆动动画；**巡场车**（琥珀色）：矩形环路，车灯常亮。
- **阻挡**：前方短射线探测——被玩家车挡路会停下等待（不物理推车）。
- **碰撞**：撞上小怪 = 普通碰撞，计入成绩。计数机制：车上 `Area3D` 触发器
  （**RigidBody.BodyEntered 对 kinematic 不触发**——行人能物理推动车辆但信号静默；
  `AnimatableBody3D.SyncToPhysics` 必须保持默认 true，否则碰撞器不随节点移动）。

## 天气系统（WeatherFx）

四种预设改写天空/环境光/太阳/雾/Glow/粒子，并缩放轮胎抓地力：
晴 1.0 / 雨 0.78 / 雪 0.55 / 烈日 1.0。雨为快速雨丝（20 m/s 高位薄带），
雪为慢速雪花（低位宽发射带 y 2–14——**慢速粒子若从高位窄带发射会悬在俯视相机视野锥之外**）。

## 判定与经济

- **通过条件**：四角入库（余量 0.04m）+ 角度容差内 + 静止（<0.12 m/s）。
- **金币**：成功 +150 / 「★ 完美入库」（0 碰撞且 ≤5° 且 ≤15cm）+300；
  车库（B）购买涂装：经典红免费、曜石黑 200、海洋蓝 300、珍珠白 400、荧光绿 600、鎏金 1000；
  存档 `user://parking_save.json`（coins/owned/selected）。
- **失败** → toast 逐条说明原因（4s 消失），计时不停（1.5s 冷却防连点）。
- 经验：**真瓶颈是四角在库内**——4.6m 车身在 2.5m 宽库位偏航 >~8° 时对角线超宽。

## 视效

- 暖色太阳 + ShadowBlur 软阴影 + SSAO 接地暗部 + Glow 0.65（拾取车灯/自发光库位线）
  + Filmic + 8x MSAA + FXAA。
- **刹车灯**：刹车/手刹时尾灯发光能量 1.6→4.5（加大尺寸保证俯视可辨）。
- **刹车轨迹**：重刹/手刹滑行时后轮落点生成暗色四边形（池 360、10s 淡出）。
- **爆炸**：碰撞时一次性粒子爆发（橙）+ OmniLight 闪光 + 镜头震动 0.4s（0.5s 冷却）。

## 关卡编辑器（E / --editor）

工具：1 墙块（R 横竖）· 2 停放车（R 45°步进、颜色自动轮换）· 3 锥桶 · 4 行人（两次点击定路径）
· 5 库位（R 旋转 / [ ] 长度 / - = 宽度）· 6 出生点 · 7 删除 · W 天气循环 · S 保存 · T 测试试玩
（Esc 回编辑器）· Esc 退出。鼠标左键放置、虚影跟随、实时重建预览（含运动中的小怪）。
保存 `levels/custom.json`；开局界面按 0 游玩。

## 引擎陷阱（实测）

- 车体级 `EngineForce` 正值推向 +Z（车尾方向），与 Bullet 文档直觉相反。
- C# 枚举名：`Viewport.Msaa.Msaa8X`（大写 X）、`ScreenSpaceAAEnum.Fxaa`、粒子类是
  **`CpuParticles3D`**（不是 CPUParticles3D）、`Key.Bracketleft/Bracketright`（单词小写）。
- 射线 `HitFromInside` 默认 false——从自身形状内部发出的射线不自检命中。
- **bat 内 `chcp 65001` + 非 ASCII 注释会让 cmd.exe 逐行读取脱锚**——Godot 被裸启动成
  项目管理器（子进程无参数即此症状）。本项目所有 .bat 纯 ASCII。

## 脚本化验证（无需输入注入）

| 模式 | 断言 |
|---|---|
| `--demo` | L1 自动驾驶停稳→报告→SUCCESS（exit 0），遥测逐帧落盘 demo_state.txt |
| `--verify-report` | 判定两分支：歪姿势拒停+toast 截图；完美姿势过审+结算截图 |
| `--verify-hazards` | 全 9 关出生零碰撞 + 小怪移动 + 行人阻挡 + kinematic/static 碰撞计数 |
| `--verify-features` | 三天气截图、爆炸触发、刹车轨迹、金币发放/购买/涂装、编辑器存取回路、
  自定义关卡可玩；对玩家存档与 custom.json 做备份/恢复，测试幂等 |
| `record_demo.bat` | --write-movie 30fps 确定性录屏 → ffmpeg 合成证明视频 |

## 工程结构

```
project.godot            # 1280x720、physics 60Hz、SystemFont
scenes/BuildMain.cs      # 打包期生成 Main.tscn（极简根节点，nodes=1 校验）
scenes/Main.tscn         # 生成物（勿手编）
scripts/Car.cs           # 车辆物理 + 程序化车壳 + 换肤/刹车灯/抓地力 + Area 碰撞触发器
scripts/Hazard.cs        # 行人/巡场车：AnimatableBody3D 往返/环路 + 射线阻挡
scripts/Level.cs         # 9+自定义 关卡定义 + 程序化构建
scripts/WeatherFx.cs     # 天气预设：天空/光/雾/Glow/粒子 + 抓地缩放
scripts/Effects.cs        # 碰撞爆炸：一次性粒子 + OmniLight 闪光
scripts/SkidMarks.cs      # 后轮刹车痕迹对象池 + 淡出
scripts/Garage.cs         # 金币/涂装/存档 + 车库 UI
scripts/Editor.cs         # 关卡编辑器：工具/JSON 存取/测试试玩
scripts/HUD.cs            # 档位/时速/金币/计时/报告按钮/失败 toast/开局与结算遮罩
scripts/Game.cs           # 环境/相机/输入/关卡流转/ReportParked/--demo/--verify-*
GDD.md                    # 游戏设计文档（含打包与 C# 代码保护结论）
```

场景运行时全程序化构建；`Main.tscn` 只是挂 `Game` 的极简壳。
