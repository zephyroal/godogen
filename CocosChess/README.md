# CocosChess — 3D 中国象棋（Cocos2d-x 4）

Cocos2d-x 引擎后端的首个游戏实现。**3D 版**：与 Godot 3DChess 参考渲染逐参数对齐
（见 `GODOT_COMPARE.md`）；**棋子为真实 3D 模型**（Tripo3D 生成的立体造型件，
经 `tools/glb_to_xmodel.py` 从参照版 GLB 离线转换），棋盘与标记全程序化绘制
（Sprite3D + 手写 Mesh），无外部模型查看器依赖——克隆即玩。

## 玩法

- **开局选择屏**：人机对弈（执红先行）/ 双人对弈（同屏轮流）/ 联机对战
- 红方先行；人机模式下 AI 执黑（negamax α-β + 静态搜索，900ms 预算，多样着法）
- 点击己方棋子 → 金色选中环 + 棋子悬浮自转 + 绿点（空位）/红环（吃子）提示合法着法
- 点击目标格落子（sine 弧线动画 + 落地尘土），AI 思考约 1 秒后回应
- 将军红环 + 「将军！」HUD 闪屏 / 绝杀 / 困毙判定齐全；被吃棋子飞入侧边木质托盘
- **悔棋（U 键或按钮）**：人机模式回 2 步、双人回 1 步、联机禁用；托盘棋子复活
- **菜单 / 再来一局（R 键）**：按钮 + 快捷键，任意时刻返回选择屏或同模式重开
- **联机对战**：主机监听 5005 端口、客户端直连 IP（按 I 键盘输入）；走子/重开同步，
  对手断线判胜；自测：双开 `CocosChess.exe --net-host` 与 `--net-join` 自动互连并自动走首着
- 吃子镜头拉近（1.2s 焦点）、鼠标悬停高亮（桌面）、终局金色庆祝粒子、结束遮罩按钮
- 单指拖拽旋转视角、双指捏合缩放；左上角 FPS 角标

## 构建

前提：CMake ≥ 3.18、MSVC 编译器（VS 2022/2026 的 "Desktop development with C++" 工作负载），
以及 cocos2d-x v4 源码：

```bash
git clone --depth 1 --branch v4 https://github.com/cocos2d/cocos2d-x.git /path/to/cocos2d-x
cd /path/to/cocos2d-x
python download-deps.py -r no    # 下载预编译第三方库（交互提示用 -r no 自动应答）
```

> **必须用 `-A Win32`**：cocos2d-x v4 的预编译第三方库（glfw/zlib/jpeg/png/openssl 等）
> 只有 32 位版本，没有 win64 变体。

Windows（一条命令）：

```bat
build.bat D:\path\to\cocos2d-x
```

或手动：

```bat
cmake -B build -DCOCOS2DX_ROOT=D:\path\to\cocos2d-x -A Win32
cmake --build build --config Release
build\bin\CocosChess\Release\CocosChess.exe
```

## 代码结构

| 文件 | 职责 |
|---|---|
| `Classes/ChessRules.h` | 完整规则引擎（90 格坐标、七种走法生成、马腿/象眼/炮翻山、飞将照面、将军/绝杀/困毙）——纯逻辑，不依赖引擎 |
| `Classes/AIPlayer.h` | negamax α-β + 静态搜索 + MVV-LVA + 迭代加深（超时弃层）+ 多样性——逐项对齐 Godot 3DChess AI.cs |
| `Classes/GameScene.h/cpp` | 3D 场景：手写 Mesh（圆柱/圆顶/盒体）棋盘与棋子、双相机（3D 透视 + 2D UI）、触摸拾取与轨道、模式选择/悔棋/重开/结束遮罩、动画/AI/音频全流程 |
| `Classes/NetPlayer.h/cpp` | 联机层：winsock2/BSD TCP、13 字节定长帧（Move/Restart/Leave）、接收线程 + 互斥消息队列，主机 5005 单客户端 |
| `tools/glb_to_xmodel.py` | 模型转换器：GLB（网格+baseColor JPEG）→ 自定义 .xmodel 二进制（按 Godot TargetHeight 公式烘焙缩放/对齐），可重复执行 |
| `Resources/models/*.xmodel` | 14 个真实棋子模型（7 型 × 红黑，含贴图，共约 16MB） |
| `Classes/AppDelegate.h/cpp` | 引擎入口：设计分辨率 1280×720、60 FPS 策略 |
| `Classes/main.h/cpp` | Windows WinMain 入口（官方 cpp-template 模式） |
| `test/rules_test.py` | 规则引擎 Python 镜像测试（16/16 PASS） |
| `test/verify_flow_check.py` | `--verify` 脚本对局的阶段断言器（46/46 PASS：双人/吃子/将军/悔棋复活/重开/菜单） |
| `Resources/audio/*.wav` | 音效（game_start/victory/defeat/checkmate/ai_thinking），复制自 Godot 3DChess |

## 设计要点

- **零模型资产的棋盘，真模型的棋子**——桌面/台面/边框/板条/网格线（3D 薄盒而非 DrawNode，保证深度正确）、
  托盘全部由 `CreateCylinder`/`CreateDome`/`CreateBox` 程序化生成；棋子加载 `Resources/models/`
  下的真实模型（lambert 光照 + 环境光/方向光），模型缺失时回退五段程序化几何。
  模型自带立体造型（帅=金铜小人、车=小推车）与贴图，**无刻字**——与 Godot 参照版一致。
- **汉字平躺朝上**——`Label` 旋 -90°X + scale 0.004 等效 Godot Label3D；红方正读，
  黑方 RotateY(π)（与参考渲染实测一致，勿照抄 Godot 源码里对 GLB 的红方旋转）。
- **AI 不卡帧**——`std::async` 工作线程搜索，主线程 `update()` 非阻塞轮询 future；
  音频循环 + 0.4s 延迟应用均对齐 Godot 节奏。
- **动画即 Godot 算法**——移动 0.45s sine 弧（高 0.55/吃子 0.9/入盘 2.2）、
  选中悬浮 0.32+0.04sin(4t) 自转 1.4rad/s、标记呼吸脉冲，全部逐帧实现（非 Action）。

## 踩坑记录（已写入 engines/cocos2dx.md 指南）

| 坑 | 修复 |
|---|---|
| v4 预编译库仅 win32 | CMake 加 `-A Win32` |
| MSVC 编译 UTF-8 中文源码报 C3688 | `target_compile_options /utf-8` |
| v4 无 `Director::setGLContextAttrs` | 删除 `initGLContextAttrs` override |
| 新 Windows SDK `GWL_WNDPROC` 可能未声明 | 引擎补 `#ifndef GWL_WNDPROC` 别名到 `GWLP_WNDPROC` |
| v4 `CameraFlag`：USER1=1<<1（非 v3 的 256） | 掩码用枚举常量，不硬编码 |
| 相机掩码按节点逐个判定，父节点不覆盖后加的子节点 | 每个动态子节点显式 `setCameraMask`；托盘/角框/提示点三类都栽过 |
| 2D UI 与 3D 相机 | 3D 相机 `setDepth(-1)` 先渲，UI 留 DEFAULT；背景用 `Director::setClearColor` |
| `Camera::unproject` 期望左上原点坐标 | `getLocation()`（左下原点）必须配 `unprojectGL`，否则拾取上下镜像 |
| 系统字体 Label 在 3D 相机下带深度测试 | 字模 quad 必须整体浮出圆顶（+0.32），嵌入即被裁剪成"无字" |
| 运行时新建 Sprite3D 不渲染 | 所有标记 init 时预建池（24 绿点/24 红环/80 碎屑），运行时只重定位 |
| `captureScreen` 截图看不到池化小标记 | 选中环/角框截图正常；提示点仅实机可见（截图路径怪癖） |
| **v4 无 glTF/GLB 加载器**（3D 模块只认 OBJ/c3t/c3b） | `tools/glb_to_xmodel.py` 离线展平 GLB 为自定义二进制（注意 `Mesh::IndexArray` 是 uint16），运行时 `Mesh::create` + `mesh->setTexture(diffuse)` |
| **场景有灯时 `Sprite3D::draw` 自动把 unlit 网格换成 lit 材质**（静默，颜色整套压暗一半） | 所有 unlit 网格 `setLightMask(0)` 免疫自动切换；lit 网格保持默认掩码 |
| **unlit 材质空纹理槽泄漏**（盲采样上一张绑定纹理）+ 引擎 dummy 纹理是 Release=透明黑 | 加载真白 PNG，`genMaterial` 前绑定（`Mesh::setMaterial` 回放进 pass） |
| lit 材质（genMaterial(true)）没有场景灯光时全黑 | 加 `AmbientLight` + `DirectionLight`（类名无 "al"，引擎上限 1 盏方向光），相机掩码与 3D 相机一致；unlit 材质不受灯光影响 |
| 32 个同类棋子重复解码同一张贴图，启动拖慢 | 模型按路径缓存（网格数组 + 纹理各解码一次），boot 从 >10s 降到 ~6s |
| `EventListenerTouchAllAtOnce` 不尊重 Menu 的吞触 | 点按钮同时会触发棋盘拾取——onTap 里先做按钮包围盒命中过滤 |
| 桌面鼠标无按键移动时 `EventMouse::getMouseButton()` 是 `BUTTON_UNSET(-1)` 枚举 | 与枚举比较，勿当 int 与 0 比较（编译错误 C2676） |
| 联机文本输入无 ui::TextField 依赖 | 键盘输入 IP（KEY_0..KEY_9/点/退格/回车），零 UI 模块依赖 |
| winsock 链接 | `#pragma comment(lib, "ws2_32.lib")` 免改 CMake；接收独立线程 + 主线程排水消息队列 |
