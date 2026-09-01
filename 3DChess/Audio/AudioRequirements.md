# 音效需求清单（AudioRequirements）

《3D 中国象棋 Xiangqi3D》的音效需求与接入规范。
状态：**待生成与接入**。

---

## 一、音效清单

| # | 文件名 | 触发点 | 描述 | 优先级 |
|---|--------|--------|------|--------|
| 1 | `select.wav` | 点击选中己方棋子 | 清脆木牌轻击声（~0.2s） | P0 |
| 2 | `move.wav` | 棋子落盘 | 厚重木墩叩击声（~0.3s） | P0 |
| 3 | `capture.wav` | 吃子（棋子被移除） | 木块碎裂 + 碰撞（~0.4s） | P0 |
| 4 | `check.wav` | 被将军 | 低沉警示钟声（~0.6s） | P0 |
| 5 | `checkmate.wav` | 绝杀 | 沉重铜钟 + 余韵（~1.5s） | P1 |
| 6 | `stalemate.wav` | 困毙 | 沙哑木鱼单敲（~0.5s） | P1 |
| 7 | `game_start.wav` | 开局选择模式后 | 磨墨/铺卷轴声 → 轻磬（~1.0s） | P1 |
| 8 | `victory.wav` | 胜利结算 | 喜庆锣鼓短奏（~2.0s） | P1 |
| 9 | `defeat.wav` | 失败结算 | 低沉号角渐弱（~2.0s） | P1 |
| 10 | `ui_click.wav` | HUD 按钮点击 | 玉珠轻碰（~0.1s） | P2 |
| 11 | `ui_hover.wav` | HUD 按钮悬停 | 极轻丝竹拨弦（~0.1s） | P2 |
| 12 | `illegal.wav` | 落子非法（红色闪烁） | 干涩木梆拒击（~0.2s） | P2 |
| 13 | `ai_thinking.wav` | AI 思考中（循环） | 极轻翻书沙沙声（循环，~1.0s loop） | P2 |
| 14 | `undo.wav` | 悔棋 | 倒卷/退格声（~0.3s） | P2 |

### 触发点 → 代码对应

| 触发点 | 文件 | 函数 |
|--------|------|------|
| 选中棋子 | `Piece.cs` | `SetSelected(true)` |
| 落子 | `Game.cs` | `ExecuteMove()` → `mover.AnimateMove()` 完成时 |
| 吃子 | `Game.cs` | `ExecuteMove()` → `victim.AnimateCapture()` |
| 将军 | `Game.cs` | `FinishMove()` → `Hud.FlashCheck()` |
| 绝杀/困毙 | `Game.cs` | `FinishMove()` → `IsGameOver` |
| 开局 | `Game.cs` | `ChooseMode()` |
| 胜利/失败 | `Game.cs` | `EndGame()` → `Hud.ShowEndScreen()` |
| 按钮点击 | `HUD.cs` | `Button.Pressed` 回调 |
| 非法落子 | `Game.cs` | `HandleTapIndex()` → `Board.ShowIllegal()` |
| 悔棋 | `Game.cs` | `Undo()` |
| AI 思考 | `Game.cs` | `StartAI()` |

---

## 二、格式规范

| 项 | 要求 |
|----|------|
| 文件格式 | **.wav**（短音效）或 **.ogg**（循环/长音效） |
| 采样率 | 44100 Hz |
| 声道 | 单声道（音效）/ 立体声（BGM，如有） |
| 落盘位置 | `D:\godogen\3DChess\assets\audio\` |
| 命名 | 与上表文件名一致，全小写下划线 |

---

## 三、接入方式

```csharp
// 在 Game.cs 中添加 AudioStreamPlayer
private AudioStreamPlayer _sfx;

public override void _Ready()
{
    _sfx = new AudioStreamPlayer();
    AddChild(_sfx);
    // ...
}

private void PlaySfx(string name)
{
    var stream = GD.Load<AudioStream>($"res://assets/audio/{name}.wav");
    if (stream != null) { _sfx.Stream = stream; _sfx.Play(); }
}
```

在触发点调用 `PlaySfx("move")` 即可。AI 思考循环用 `AudioStreamPlayer` + `Stream.Loop = true`。

---

## 四、BGM（可选）

| 场景 | 风格 | 时长 |
|------|------|------|
| 对局中 | 古琴/箫循环，舒缓雅致，低音量 | ~60s loop |
| 终局结算 | 锣鼓或丝竹收尾 | ~5s |

> 当前项目无音频管线（README 已知取舍）。接入音频需在 `project.godot` 中确认 `audio/driver` 配置正常。
