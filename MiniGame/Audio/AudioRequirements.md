# 音效需求清单（AudioRequirements）

《堡垒冲刺 Fortress Rush》的音效需求与接入规范。
状态：**待生成与接入**。

---

## 一、音效清单

| # | 文件名 | 触发点 | 描述 | 优先级 |
|---|--------|--------|------|--------|
| 1 | `footstep.wav` | 角色奔跑（循环） | 方块鞋底叩地声（~0.3s loop） | P0 |
| 2 | `lane_switch.wav` | 变道（A/D） | 短促"嗖"横向滑动声（~0.15s） | P0 |
| 3 | `turn_back.wav` | 掉头折返（S） | 旋转换向声（~0.3s） | P0 |
| 4 | `blast.wav` | 爆破技能释放 | 爆炸冲击波（~0.6s） | P0 |
| 5 | `dash.wav` | 冲刺启动 | 嗖破风声（~0.4s） | P0 |
| 6 | `block_hit.wav` | 方块被攻击 | 石头碎裂声（~0.2s） | P0 |
| 7 | `block_destroyed.wav` | 方块被摧毁 | 碎块崩塌声（~0.4s） | P0 |
| 8 | `fortress_destroyed.wav` | 城池被摧毁 | 建筑倒塌 + 尘埃（~1.0s） | P1 |
| 9 | `runner_hit.wav` | 角色被击中 | 沉闷撞击 + 呃哼（~0.3s） | P1 |
| 10 | `runner_dead.wav` | 角色死亡 | 倒地闷响（~0.5s） | P1 |
| 11 | `respawn.wav` | 角色复活 | 能量汇聚"叮"（~0.4s） | P1 |
| 12 | `victory.wav` | 胜利结算 | 欢呼/号角（~2.0s） | P1 |
| 13 | `defeat.wav` | 失败结算 | 低沉号角渐弱（~2.0s） | P1 |
| 14 | `coin.wav` | 金币拾取 | 清脆金属"叮"（~0.15s） | P2 |
| 15 | `skill_ready.wav` | 技能冷却完成 | 极轻"叮"提示音（~0.1s） | P2 |
| 16 | `ui_click.wav` | HUD 按钮点击 | 玉珠轻碰（~0.1s） | P2 |
| 17 | `announce.wav` | 城池摧毁播报 | 战鼓单击（~0.4s） | P2 |
| 18 | `laser.wav` | 激光束发射 | 能量充能→释放（~0.3s） | P2 |

### 触发点 → 代码对应

| 触发点 | 文件 | 函数 |
|--------|------|------|
| 奔跑脚步 | `Runner.cs` | `Animate()` → `moving` 分支 |
| 变道 | `Runner.cs` | `ReadPlayerInput()` / `Think()` → `TargetLane` 变更 |
| 掉头 | `Runner.cs` | `ReadPlayerInput()` → `turn_back` |
| 爆破 | `Runner.cs` | `TryBlast()` |
| 冲刺 | `Runner.cs` | `TryDash()` |
| 方块受击 | `Block.cs` | `TakeDamage()` |
| 方块摧毁 | `Block.cs` | `DestroySelf()` |
| 城池摧毁 | `Fortress.cs` | `Collapse()` |
| 角色受击 | `Runner.cs` | `TakeDamage()` |
| 角色死亡 | `Runner.cs` | `Die()` |
| 复活 | `Runner.cs` | `Respawn()` |
| 胜利/失败 | `Game.cs` | `EndGame()` |
| 金币拾取 | — | （待接入：Game.cs 中金币系统） |
| 技能就绪 | `Runner.cs` | `BlastCdTimer`/`DashCdTimer` 归零 |
| HUD 按钮 | `HUD.cs` | `Button.Pressed` |
| 城池播报 | `Game.cs` | `OnFortressDestroyed()` → `Hud.Announce()` |
| 激光 | `FX.cs` | `LaserFx._Ready()` |

---

## 二、格式规范

| 项 | 要求 |
|----|------|
| 文件格式 | **.wav**（短音效）/ **.ogg**（循环脚步、BGM） |
| 采样率 | 44100 Hz |
| 声道 | 单声道（音效）/ 立体声（BGM） |
| 落盘位置 | `D:\godogen\MiniGame\assets\audio\` |
| 命名 | 全小写下划线，与上表一致 |

---

## 三、接入方式

```csharp
// 在 Game.cs 中添加全局音效播放器
private AudioStreamPlayer _sfx;
private AudioStreamPlayer _loop; // 脚步等循环音

public override void _Ready()
{
    _sfx = new AudioStreamPlayer();
    _loop = new AudioStreamPlayer();
    AddChild(_sfx);
    AddChild(_loop);
}

public void PlaySfx(string name)
{
    var stream = GD.Load<AudioStream>($"res://assets/audio/{name}.wav");
    if (stream != null) { _sfx.Stream = stream; _sfx.Play(); }
}

public void StartLoop(string name)
{
    var stream = GD.Load<AudioStream>($"res://assets/audio/{name}.ogg");
    if (stream is AudioStreamOggVorbis ogg) ogg.Loop = true;
    _loop.Stream = stream;
    _loop.Play();
}

public void StopLoop() => _loop.Stop();
```

在触发点调用 `Game.Instance.PlaySfx("blast")` 即可。脚步循环在 `Runner.Animate()` 的 `moving` 分支启停。

---

## 四、BGM（可选）

| 场景 | 风格 | 时长 |
|------|------|------|
| 对局中 | 紧张鼓点 + 弦乐循环（节奏感，配合跑酷速度） | ~60s loop |
| 终局结算 | 胜利号角 / 失败低沉 | ~5s |

> 当前项目无音频管线（README 已知取舍）。接入音频需在 `project.godot` 中确认 `audio/driver` 配置正常。
