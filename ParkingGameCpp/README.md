# ParkingGameCpp — C++ GDExtension 版本

同一游戏核心循环的 **C++ GDExtension** 平行实现（参照版：`../ParkingGame/` C#）。
证明同一玩法可以完整以原生扩展方式落地，并与 C# 版共用同一套验证契约。

## 范围

第 1 关（侧方入库）+ 轿车 + 真实 VehicleBody3D 物理 + 「报告我停好了 (G)」判定
（四角入库 + 15° 角度 + 静止）+ HUD 简版（档位/时速/计时/toast/结算）+
`--demo` 自动驾驶（与 C# 参照版相同的运动学常数：两段 r=4.8m 满舵 S +
精确纯追踪 + 前拉一把 + 停稳后自动提交报告）。
天气/小怪/车库/编辑器等周边仍是 C# 版独有。

## 构建（一次性环境）

```bat
build_cpp.bat   # 首次自动构建 godot-cpp 绑定，再构建本扩展 DLL
run_cpp.bat     # 导入 + 启动游戏（可加 -- --demo）
```

要点：
- godot-cpp 绑定**精确对靶 Godot 4.7.1**：`Godot --headless --dump-extension-api`
  导出的 `extension_api.json` 通过 `custom_api_file=` 喂给 scons
  （godot-cpp 仓库已不再自带 api json）。
- 工具链：**MSVC**（本机 VS）。注意 **Strawberry 的 GCC 8.3 编不过 godot-cpp**
  （Array 构造重载歧义）——MinGW 路线需 GCC 9+。
- `.gdextension` 库键用 `windows.debug.x86_64`（引擎 feature 名是 `debug`，
  对应构建产物 `*.template_debug.*`）。
- `src/`、`bin/` 有 `.gdignore`——否则编辑器 importer 会把 MSVC 的 `.obj`
  当 Wavefront 模型导入并报错。
- MSVC 需 `/utf-8`（源码含中文 UI 字符串），SConstruct 已自动附加。

## 验证

`-- --demo`：自动驾驶倒车入库 → 遥测落 `demo_state.txt`（0.2s 一帧）→
`RESULT SUCCESS` → 截图 `screenshots/demo_success.png` → exit 0。
与 C# 版同一套证明契约（运行中的游戏 + 遥测日志 + 退出码，而非仅编译通过）。

## godot-cpp (4.7 master) 原生名与 C# 的差异（实测踩坑）

| C# | godot-cpp C++ |
|---|---|
| `AlbedoColor =` | `set_albedo(...)`（无 set_albedo_color） |
| `EmissionEnabled =` | `set_feature(BaseMaterial3D::FEATURE_EMISSION, ...)` |
| `GlowHdrThreshold` | `set_glow_hdr_bleed_threshold` |
| `BackgroundMode/AmbientSource/TonemapMode` | `set_background` / `set_ambient_source` / `set_tonemapper` |
| `SkyMaterial` | `Sky::set_material` |
| `ShadowEnabled/LightEnergy/LightColor/ShadowBlur` | `set_shadow` / `set_param(PARAM_ENERGY,..)` / `set_color` / `set_param(PARAM_SHADOW_BLUR,..)`（4.7 参数化） |
| `WheelRadius/WheelRestLength/WheelFrictionSlip/WheelRollInfluence` | `set_radius` / `set_suspension_rest_length` / `set_friction_slip` / `set_roll_influence` |
| `AudioStreamWAV`/`CPUParticles3D` 式大写缩写折叠 | 同 C#：小写缩写（`CpuParticles3D`）不适用——C++ 用原生名，无折叠问题 |
| `Mathf.Pi/TAU` | `Math::PI` / `Math::TAU`（math_defs.hpp，命名空间 Math 内） |
| 入口 `initialize()` | `init_obj.init()`（master 版样板） |
