# Cocos2d-x engine guide

Stack: **Cocos2d-x 4.0** (the C++/CMake line — not Cocos Creator), **C++17**, CMake ≥ 3.18.
The engine is a source checkout, not an installed SDK: clone
`https://github.com/cocos2d/cocos2d-x` (tag `v4.0` or `cocos2d-x-4.0`), point your
project's `COCOS2DX_ROOT` at it, and `add_subdirectory()` its `cocos/` folder.

## Project shape

```
GameName/
├── CMakeLists.txt       # add_subdirectory(<COCOS2DX_ROOT>/cocos) + your sources
├── Classes/             # all .h/.cpp — the engine only compiles what you list
│   ├── AppDelegate.h/.cpp
│   └── <Scene>.h/.cpp
├── Resources/           # runtime-loaded files (fonts, images, plists) — keep refs out
└── build/               # CMake build dir (gitignored)
```

- **CMakeLists.txt** — list every source file explicitly (no `GLOB`), link the `cocos`
  target from the engine's subdirectory, and set `COCOS2DX_ROOT` via `-D` or cache:
  `cmake -B build -DCOCOS2DX_ROOT=/path/to/cocos2d-x -A x64` (MSVC) or default generator.
- **`AppDelegate`** is mandatory — it initializes `Director`, sets the design
  resolution (`GLViewImpl::createWithRect` on desktop), sets the FPS policy
  (`Director::getInstance()->setAnimationInterval(1.0 / 60)`), and runs the first scene.
- Build gate: `cmake --build build --config Release` — MSVC needs `--config`
  because it's a multi-config generator. On Windows the exe lands in
  `build/bin/Release/GameName.exe` (the `Resources/` dir must sit next to the exe —
  either copy it in the CMake install step or add a post-build copy).

The user watches by running the exe (`build/bin/Release/GameName.exe`) — keep it
building cleanly so each run reflects current state.

## Everything is a Node (scene graph)

A `Scene` owns a tree of `Node` subclasses. `Layer` (or just `Node`) is the root
for in-scene groups; `DrawNode` draws primitives (lines/polygons) for board grids;
`Label` renders text (with system-font names like `"Microsoft YaHei"` on Windows
for CJK); `Sprite` shows images from `Resources/`. `Node::scheduleUpdate()` +
`update(float dt)` is the frame callback; UI/thinking work that would block the
frame goes through `Director::getInstance()->getScheduler()->schedule()` or
`std::thread` + `CustomEvent` back to the main thread (never touch the scene
graph off the main thread).

## Quirks worth knowing (silent-failure)

- **Cocos2d-x 4 uses a new renderer** — `GLProgram`/`setGLProgramState` are gone;
  custom materials are `backend::ProgramState` (rarely needed for 2D — the default
  pipeline suffices). `Director::getInstance()->setDisplayStats(true)` still works
  for an FPS readout in dev.
- **Ref-counting:** `create()` factories return autoreleased objects — storing one
  in a member variable needs `retain()` in the setter and `release()` in the
  destructor, or the object vanishes at frame end. Prefer `cocos2d::Vector<T*>` /
  `cocos2d::Map<K,V>` (they retain automatically) over raw `Ref*` members.
- **`Label` with system fonts:** `Label::createWithSystemFont("帅", "Microsoft YaHei", 48)` —
  the font name must match an installed system font; a wrong name silently falls back
  to a default that may not render CJK glyphs.
- **Touch events:** attach via `EventListenerTouchOneByOne::create()` +
  `_eventDispatcher->addEventListenerWithSceneGraphPriority()` in `onEnter` (and
  remove in `onExit`); return `true` from `onTouchBegan` to consume the touch.
  Converting to node space: `node->convertToNodeSpace(touch->getLocation())`.
- **`DrawNode` z-order:** draw order follows `setLocalZOrder()`; pieces above the
  board, markers above pieces. `DrawNode::drawDot`, `drawSegment`, `drawPoly` are
  your primitives for board grids, palace diagonals and star marks without any
  image assets.
- **Design resolution vs frame size:** `Director::getInstance()->getVisibleSize()`
  after `setDesignResolutionSize(w, h, ResolutionPolicy::FIXED_HEIGHT)` — compute
  layout from `getVisibleOrigin()` + `getVisibleSize()`, never hardcode pixel
  coordinates for a different window.
- **AI/threading:** a minimax search must not run in `update()` or the window
  freezes (on Windows the "not responding" overlay appears after ~5s). Run the
  search in a `std::thread`/`std::async` and post the result back with
  `Director::getInstance()->getScheduler()->performFunctionInCocosThread()`.
- **Windows console:** `#include "platform/win32/Win32Entry.h"`-style main is already
  provided by the engine — your entry is `AppDelegate::applicationDidFinishLaunching`.

## Capture (proof video)

There is no built-in movie writer — capture the window from outside with an
OS-level recorder, or drive the game deterministically and screenshot per frame:
schedule a `Director` end after N seconds (`scheduleOnce` with a counter),
auto-play moves from a fixed list, and use `utils::captureScreen(callback, path)`
(declared in `base/CCDirector.h` via `renderer/backend` utilities — check the
installed headers for the exact signature; it saves async on a worker thread).
Then assemble with ffmpeg:

```bash
ffmpeg -y -framerate 30 -i screenshots/frame%04d.png \
  -c:v libx264 -pix_fmt yuv420p -movflags +faststart screenshots/result/video.mp4
```

For deterministic capture, pre-plan the move list in the scene (drive input from
code, not live touches), fix the RNG seed, and advance the game from a timer
callback at a fixed cadence. The clip must show the behavior progressing across
the whole window — no dead time, no single looped frame.
