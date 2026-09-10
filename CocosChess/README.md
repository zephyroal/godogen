# CocosChess — 2D 中国象棋（Cocos2d-x 4）

Cocos2d-x 引擎后端的首个游戏实现。全部程序化绘制（DrawNode 棋盘 + Label 中文棋子），
无外部资源依赖——克隆即玩。

## 玩法

- 红方先行，人类执红，AI 执黑
- 点击己方棋子 → 绿点（空位）/ 红圈（吃子）提示合法着法
- 点击目标格落子，AI 思考约 2 秒后回应
- 将军提示 / 绝杀 / 困毙判定齐全

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
build\bin\Release\CocosChess.exe
```

## 代码结构

| 文件 | 职责 |
|---|---|
| `Classes/ChessRules.h` | 完整规则引擎（90 格坐标、七种走法生成、马腿/象眼/炮翻山、飞将照面、将军/绝杀/困毙）——纯逻辑，不依赖引擎 |
| `Classes/AIPlayer.h` | negamax α-β 搜索（深度递增至 6、MVV-LVA 排序、2s 软时限、子力+过河兵评估） |
| `Classes/GameScene.h/cpp` | 棋盘 DrawNode 绘制、棋子 Label 渲染、触摸交互、AI 工作线程 + 主线程回投 |
| `Classes/AppDelegate.h/cpp` | 引擎入口：设计分辨率 1280×720、60 FPS 策略 |
| `Classes/main.h/cpp` | Windows WinMain 入口（官方模板模式） |
| `test/rules_test.py` | 规则引擎 Python 镜像测试（16/16 PASS） |

## 设计要点

- **无资源文件**——棋盘、棋子、河界文字全部用 `DrawNode` + `Label::createWithSystemFont` 绘制，
  系统字体（Microsoft YaHei）保证中文显示，无需打包任何 PNG/字体。
- **AI 不卡帧**——`std::async` 在工作线程搜索，主线程 `update()` 中非阻塞轮询 future，
  窗口 60 FPS 不受影响。
- **规则引擎可独立测试**——`ChessRules.h` 是 header-only 纯 C++，无 cocos2d include，
  `test/rules_test.py` 是其 Python 镜像的 16 项断言全过。
- **MSVC `/utf-8`**——源码含中文 UTF-8 字符串字面量（棋子汉字），CMakeLists 中对 MSVC
  强制传 `/utf-8`，否则默认 GBK 编译导致 C3688 "文本后缀无效"。

## 踩坑记录（写进引擎指南的核心条目）

| 坑 | 修复 |
|---|---|
| v4 预编译库仅 win32 | CMake 加 `-A Win32` |
| MSVC 编译 UTF-8 中文源码报 C3688 | `target_compile_options /utf-8` |
| v4 无 `Director::setGLContextAttrs` | 删除 `initGLContextAttrs` override |
| v4 `drawCircle` 无 7 参数（含线宽）重载 | 先 `setLineWidth` 再调 6 参数版 |
| 新 Windows SDK `GWL_WNDPROC` 可能未声明 | 引擎补 `#ifndef GWL_WNDPROC` 别名到 `GWLP_WNDPROC` |
| `MakeMove(m, int&)` 不能传临时字面量 | 用局部 `int captured = 0;` 传入 |
