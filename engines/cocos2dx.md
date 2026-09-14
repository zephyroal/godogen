# Cocos2d-x engine guide

Stack: **Cocos2d-x 4.0** (the C++/CMake line — not Cocos Creator), **C++17**, CMake ≥ 3.18.
The engine is a source checkout, not an installed SDK: clone
`https://github.com/cocos2d/cocos2d-x` (tag `v4`), run `python download-deps.py -r no`
inside it (prebuilt third-party libs), then point your project's `COCOS2DX_ROOT` at it.

## Project shape

```
GameName/
├── CMakeLists.txt       # add_subdirectory(${COCOS2DX_ROOT}/cocos) + your sources
├── Classes/             # all .h/.cpp — the engine only compiles what you list
│   ├── AppDelegate.h/.cpp
│   ├── main.h/main.cpp  # Windows entry (_tWinMain) — required on desktop
│   └── <Scene>.h/.cpp
├── Resources/           # runtime-loaded files (fonts, images, audio)
└── build/               # CMake build dir (gitignored)
```

- **CMakeLists.txt** — list every source file explicitly (no `GLOB`). Use the official
  template pattern:
  `set(CMAKE_MODULE_PATH ${COCOS2DX_ROOT}/cmake/Modules/)`, `include(CocosBuildSet)`,
  `add_subdirectory(${COCOS2DX_ROOT}/cocos ${ENGINE_BINARY_PATH}/cocos/core)`,
  `target_link_libraries(<game> cocos2d)` (the link target is `cocos2d`, not `cocos`).
  Engine helpers `setup_cocos_app_config(<game>)` + `cocos_copy_target_dll` +
  `cocos_copy_target_res` copy the runtime DLLs and `Resources/` next to the exe —
  no manual post-build step needed.
- **Configure with `-A Win32` on Windows**: `cmake -B build -DCOCOS2DX_ROOT=<path> -A Win32`.
  The v4 prebuilt third-party libs (glfw/zlib/jpeg/png/openssl/…) are 32-bit only —
  `-A x64` fails at link with unresolved externals.
- **Build gate**: `cmake --build build --config Release` (MSVC is multi-config; `--config`
  is mandatory). The exe lands in `build/bin/<ProjectName>/Release/<ProjectName>.exe`
  (note the project-name subfolder) with `Resources/` copied beside it. A locked exe
  (game still running) fails with LNK1104 — stop the game before rebuilding.
- **`AppDelegate`** is mandatory — it initializes `Director`, sets the design resolution
  (`GLViewImpl::createWithRect` on desktop, `setDesignResolutionSize` +
  `ResolutionPolicy::FIXED_HEIGHT`), sets the FPS policy
  (`Director::getInstance()->setAnimationInterval(1.0 / 60)`), and runs the first scene.
- **Windows entry**: you also need `Classes/main.h` (`WIN32_LEAN_AND_MEAN` + `windows.h`
  + `platform/CCStdC.h`) and `Classes/main.cpp` with `_tWinMain` that calls
  `Application::getInstance()->run()` — matching the official cpp-template.
- **MSVC `/utf-8`**: source with CJK string literals needs
  `target_compile_options(<game> PRIVATE /utf-8)` — the default codepage mangles
  UTF-8 literals into hard errors like C3688.

The user watches by running the exe (`build/bin/<ProjectName>/Release/<ProjectName>.exe`)
— keep it building cleanly so each run reflects current state.

## Scene graph, 2D and 3D in one scene

A `Scene` owns a tree of `Node` subclasses. `Layer`/`Node` for groups; `DrawNode` for 2D
primitives; `Label` for text (system fonts like `"Microsoft YaHei"` render CJK);
`Sprite` shows images from `Resources/`; `Sprite3D` + hand-built `Mesh` (vertex/normal/UV
buffers via `Mesh::create`) covers procedural 3D without any model assets.
`Node::scheduleUpdate()` + `update(float dt)` is the frame callback; long work (AI search)
goes in `std::async`, polled non-blockingly in `update()` — never touch the scene graph
from a worker thread.

Mixing 3D and 2D UI in one scene is camera-mask territory — the classic silent-failure:

- **`CameraFlag` values**: v4 has `DEFAULT = 1`, `USER1 = 1<<1`, `USER2 = 1<<2`, … — NOT
  the v3 numbering. Assign 3D content (and its camera) to `USER1` via
  `setCameraMask((unsigned short)CameraFlag::USER1)` and leave 2D UI at `DEFAULT`.
  A wrong mask makes nodes invisible with no error.
- **Camera order**: give the perspective camera `setDepth(-1)` so it renders *before*
  the scene's default 2D camera and the UI stays on top; clear the window background
  with `Director::getInstance()->setClearColor(...)`, not a `LayerColor` under the 3D
  camera.
- **Picking**: `Camera::unproject()` expects screen coords with a *top-left* origin,
  `unprojectGL()` the *bottom-left* GL/design coords that `Touch::getLocation()`
  returns. Pair them correctly or picks are mirrored vertically with zero errors.

## DCC assets: GLB has no loader — flatten offline

The 3D module loads only OBJ/c3t/c3b; glTF/GLB is not supported. The proven pattern
for generated models with baked textures: a small Python script flattens each GLB
offline into a raw bundle (positions/normals/uvs + indices + the embedded
base-color JPEG), applying the target scaling there so the runtime stays dumb.
Runtime side: build the mesh with `Mesh::create(pos, nrm, uv, idx)` — note
`Mesh::IndexArray` is `std::vector<unsigned short>`, so re-emit uint32 indices as
uint16 (split or clamp if a mesh exceeds 65k verts). Textures come from memory:
`Image::initWithImageData(bytes, len)` → `Texture2D::initWithImage(image)` →
`mesh->setTexture(tex)` (diffuse slot, retains it).

`Sprite3D::genMaterial(useLight)` picks the program from the vertex attributes:
UVs present → the unlit *textured* program (`POSITION_TEXTURE_3D`) — an unbound
diffuse texture renders as the node color, and binding one switches the same
mesh to textured. With normals and `useLight=true` you get the lit lambert
program — which renders **black without scene lights**: add `AmbientLight` and
`DirectionLight` (the class has no "-al") with camera masks matching your 3D
camera. Unlit materials ignore lights, so one scene can mix lit models with
unlit procedural geometry. Decode each texture once and cache per source file —
duplicate pieces re-decoding the same ~1 MB JPEG is seconds of startup.

## Quirks worth knowing (silent-failure)

- **Cocos2d-x 4 uses a new renderer** — `GLProgram`/`setGLProgramState` are gone; custom
  materials are `backend::ProgramState` (rarely needed for 2D — `Sprite3D::genMaterial(false)`
  gives an unlit material that suffices for flat-color meshes). v3 code that overrides
  `initGLContextAttrs` no longer compiles — `Director::setGLContextAttrs` is gone in v4.
- **Ref-counting:** `create()` factories return autoreleased objects — storing one in a
  member needs `retain()` in the setter and `release()` in the destructor, or the object
  vanishes at frame end. Prefer `cocos2d::Vector<T*>` / `cocos2d::Map<K,V>`.
- **Moving a node between layers:** `getChildByTag()` + `removeChild()` drops the
  parent's reference — `retain()` before, `addChild()` then `release()` after, or the
  node is destroyed mid-transfer.
- **`Label` with system fonts:** the font name must match an installed font; a wrong
  name silently falls back to one that may not render CJK. A 2D `Label` under a 3D
  camera renders at *world-unit* scale — set an explicit `setScale(0.004f)`-style factor
  or a 44pt label is 44 world units tall.
- **`DrawNode` is 2D:** its segments have no depth and can draw over 3D geometry. For
  board lines that pieces must occlude, build thin 3D boxes (`Sprite3D` + `Mesh`) instead.
- **Touch events:** attach via `EventListenerTouchOneByOne` (tap semantics, swallow
  touches) or `EventListenerTouchAllAtOnce` (multi-touch: pinch/drag) +
  `addEventListenerWithSceneGraphPriority()` in `onEnter` (remove in `onExit`); return
  `true` from `onTouchBegan` to consume the touch. Tap-vs-drag is your call: treat
  sub-10px total movement as a tap.
- **v4 API thinnings:** `DrawNode::drawCircle` has no line-width overload — call
  `setLineWidth` first. Check against the installed headers before assuming v3-era
  signatures.
- **Newer Windows SDKs may not declare `GWL_WNDPROC`** — if the engine's
  `ui/UIEditBox/UIEditBoxImpl-win32.cpp` fails there, patch the checkout with
  `#ifndef GWL_WNDPROC #define GWL_WNDPROC GWLP_WNDPROC #endif`.
- **Design resolution vs frame size:** compute layout from
  `Director::getInstance()->getVisibleOrigin()` + `getVisibleSize()` after
  `setDesignResolutionSize`, never hardcode window pixels.
- **Audio:** `#include "AudioEngine.h"` (`cocos2d::AudioEngine` in v4 — no
  `experimental::` namespace), files resolve under `Resources/`
  (`AudioEngine::play2d("audio/move.wav")`, loop flag for ambience). Guard with
  `FileUtils::isFileExist()` so missing files skip silently.
- **AI/threading:** a minimax search must not run in `update()` or the window freezes
  ("not responding" after ~5s). Run it in `std::async` on a `Position` snapshot, poll
  the future in `update()`, and post results back as moves to execute on the main
  thread.
- **Menu taps reach AllAtOnce listeners:** a `Menu` claims its touches, but
  `EventListenerTouchAllAtOnce` callbacks still fire for the same tap — if the scene
  also picks the board from a gesture listener, hit-test the menu items' bounding
  boxes first and skip the pick.
- **`captureScreen` misses pooled markers:** it writes the GL backbuffer
  asynchronously; small pooled markers repositioned at runtime may be absent from
  the capture even though they render on screen — verify such markers live, not
  from screenshots.
- **`Sprite3D::draw` auto-switches materials when the scene has lights:** on the
  first draw, any Sprite3D whose light mask matches an enabled light silently
  gets `genMaterial(true)` — an unlit mesh turns lit (ambient-only, no
  directional contribution in some paths) and every color dims by half, with
  zero errors logged. Give unlit meshes `setLightMask(0)` to pin them.
- **Unlit meshes sample `u_texture` blindly:** `3D_colorTexture.frag` multiplies
  the texture by `u_color` with no default — an empty slot leaks whatever
  texture the pipeline bound last (dark map textures turn the whole unlit
  scene dark). The engine dummy texture is transparent-black in release, not
  white. Load a real 1×1 white PNG and bind it **before** `genMaterial`:
  `Mesh::setMaterial` replays stored textures into the material's passes —
  the same path file-loaded textured models rely on.

## UI layer: overlays, menus, keyboard and mouse

2D UI stays at the `DEFAULT` camera mask and draws after the 3D pass. Verified pieces:

- **Buttons without textures**: `MenuItemLabel::create(label, callback)` inside a
  `Menu` gives text-only buttons that fit a zero-asset game. Position items relative
  to the `Menu` node, or let `alignItemsVerticallyWithPadding` /
  `alignItemsHorizontallyWithPadding` lay them out.
- **Full-screen overlays**: `LayerColor::create(Color4B(0,0,0,alpha), w, h)` plus
  label/menu children. `FadeIn`/`FadeOut` actions on the `LayerColor` cascade to its
  children — after hiding, reset `setOpacity(255)` so the next show is not stuck
  transparent.
- **Keyboard**: `EventListenerKeyboard` (scene-graph priority); keys are
  `EventKeyboard::KeyCode::KEY_U`-style enums. `KEY_0..KEY_9` are contiguous, which is
  enough to type simple input (an IP address) without the `ui::TextField`/IME stack.
- **Desktop mouse**: `EventListenerMouse::onMouseMove` plus the same ray picking gives
  hover hints. A move with no button held leaves the default
  `EventMouse::MouseButton::BUTTON_UNSET` (-1) — compare against the enum; treating it
  as `int 0` does not even compile (C2676).

## LAN networking (optional)

v4 ships no multiplayer layer. For a two-player LAN game, raw TCP over winsock2 works
in-process: `#pragma comment(lib, "ws2_32.lib")` in the .cpp avoids CMake changes. One
rule keeps it stable: **the socket thread never touches the scene graph** — it pushes
small fixed-size frames into a `std::deque` under a mutex, and the game drains the
queue in `update()`. Host: blocking `accept()` in the worker. Guest: non-blocking
`connect()` + `select()` with a timeout so a bad IP fails in seconds instead of the
OS default. Shutdown from the main thread: send a leave frame, `shutdown()` the socket
(unblocks `recv`), close the listener (unblocks `accept`), then `join()` the worker.

## Capture (proof video)

There is no built-in movie writer — capture the window from outside with an OS-level
recorder, or drive the game deterministically and screenshot per frame: auto-play moves
from a fixed list, schedule a `Director` end after N seconds (`scheduleOnce` with a
counter), and use `utils::captureScreen(callback, path)` (check the installed headers
for the exact signature; it saves async on a worker thread). Then assemble with ffmpeg:

```bash
ffmpeg -y -framerate 30 -i screenshots/frame%04d.png \
  -c:v libx264 -pix_fmt yuv420p -movflags +faststart screenshots/result/video.mp4
```

For deterministic capture, pre-plan the move list in the scene (drive input from code,
not live touches), fix the RNG seed, and advance the game from a timer callback at a
fixed cadence. The clip must show the behavior progressing across the whole window — no
dead time, no single looped frame.
