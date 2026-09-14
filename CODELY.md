# CODELY.md

Instructional context for Codely CLI when working in the **Godogen source repo**.

## Project Overview

Godogen is **not a game and not an application** — it is the source repo for a generator that produces game repos: `godogen -> game repo -> game`. An autonomous agent (Claude Code or Codex) is given a short game brief, builds the actual game inside a published repo, generates visual assets via paid APIs, runs the engine, and proves the result from the running game (live URL or 15–20s recorded video) — never from a clean compile.

This repo holds only the *inputs* to that process: a runtime manifest, one-page per-engine guides, and a cross-engine asset-generation skill. There is no game scaffold, no planner, and no application code. The published repo is intentionally thin — the agent recreates project boilerplate from the engine guide.

Supported engine × host-agent targets (render-time choices over one source tree):

- **Godot 4** (.NET/C#, Jolt physics, build-time scene generation, `--write-movie` capture)
- **Bevy** (Rust, code-first ECS, offscreen `RenderTarget::Image` capture)
- **Babylon.js** (TypeScript/Vite, live URL, headless Chrome capture)
- **Cocos2d-x 4** (C++17/CMake, source-checkout SDK, DrawNode/Label procedural 2D)
- **Host agents:** Claude Code (`CLAUDE.md` + `.claude/skills/`) and Codex (`AGENTS.md` + `.agents/skills/`)

## Repository Structure (git-tracked source)

| Path | Purpose |
|------|---------|
| `prompts/runtime.md` | Engine-agnostic runtime manifest — the doc that becomes `CLAUDE.md`/`AGENTS.md` in a published repo. Fixes only *where durable state lives* (`README.md`) and *that results are proven from the running game*. |
| `engines/godot.md`, `engines/bevy.md`, `engines/babylon.md`, `engines/cocos2dx.md` | Per-engine guides: stack, project sketch, capture recipe, and silent-failure traps. Copied literally (no token substitution). |
| `asset-gen/` | The sole published skill: `SKILL.md` (frontmatter + usage), `rembg.md` (background removal), `tools/` (Python CLI: `asset_gen.py`, `tripo3d.py`, `grid_slice.py`, `find_loop_frame.py`, `rembg_matting.py`, `requirements.txt`). |
| `scripts/render_dir.py` | Renders `${KEY}=value` token substitution over every text file in a directory tree. |
| `scripts/generate_codex_metadata.py` | Generates Codex `agents/openai.yaml` from `asset-gen`'s `SKILL.md` frontmatter. |
| `publish.sh` | The publisher — renders a runtime repo for a chosen engine and host agent. |
| `docs/PROJECT.md` | Design document explaining the architecture and philosophy. `docs/demo_prompts.md`, `docs/gdscript-vs-csharp.md` are reference material. |
| `setup.md` | Workstation setup (.NET 9, Rust, Node 22.12+, Godot .NET edition, system packages, API keys). |
| `AGENTS.md` | Agent instructions for **this source repo** — editing rules (below). `CLAUDE.md` in this repo just points to it. |
| `CONTRIBUTING.md` | Issue-first contribution policy. |
| `CHANGELOG.md` | Dated design-level changes. Read top entry before making changes — the architecture was recently slimmed down. |

### Untracked local directories (NOT source — do not modify as part of repo work)

- `godot/` — local checkout of the Godot Engine source (reference material)
- `Godot_v4.7.1-stable_mono_win64/` — local Godot 4.7.1 .NET editor install
- `MiniGame/` — local published/test game repo (output of `publish.sh`)

## How Publishing Works

```bash
./publish.sh --engine godot|bevy|babylon|cocos2dx --agent claude|codex --out <dir> [--force]
```

Flow (see `publish.sh`):

1. Copies `asset-gen/` to `<out>/.claude/skills/asset-gen/` (Claude) or `<out>/.agents/skills/asset-gen/` (Codex), then renders tokens into it via `render_dir.py`.
2. Codex additionally gets `agents/openai.yaml` via `generate_codex_metadata.py`.
3. Copies `prompts/runtime.md` to `<out>/CLAUDE.md` or `<out>/AGENTS.md`, rendering engine/agent tokens.
4. Copies `engines/<engine>.md` literally to `<out>/<engine>.md`.
5. Writes a `<out>/.gitignore` (only if absent) covering the generated instruction files plus engine build outputs, and `git init`s the target.

Render tokens to keep consistent:

- `ENGINE_NAME`, `ENGINE_GUIDE_FILE` (manifest)
- `AGENT_NAME`, `ASSET_GEN_SKILL_DIR`, `ASSET_SKILL_COMMAND`, `RUNTIME_ASSET_DIR` (asset-gen skill; `RUNTIME_ASSET_DIR` is `src/assets` for Babylon, `assets` otherwise)

Prerequisites: bash, `rsync`, `python3`. The repo targets Ubuntu/Debian/macOS; `publish.sh` (rsync, mktemp) does not run natively on stock Windows — use WSL or Git Bash if needed.

## Editing Rules (from AGENTS.md — binding)

- **Do not create or maintain `.claude/skills/` or `.agents/skills/` in this source repo.** They are publish-time artifacts only.
- **Don't give obvious guidance.** The runtime consumer is a highly capable LLM whose deliverable (video/URL) surfaces its own mistakes. Guides should contain only what the model can't infer or discover fast: project sketch, capture recipe, silent-failure traps.
- **Describe the new state on its own terms.** When changing/removing a feature, name the new design as if it were always the design — don't write transition notes.
- Durable state in a *published* run lives in the game repo's `README.md` (survives context compaction) — that convention, not a multi-stage pipeline, is what the manifest fixes.

## Development Conventions

- **Philosophy: lean, autonomous-pipeline-first.** No features the pipeline doesn't need; no alternative approaches where the current one works; no configuration options where defaults suffice (see `CONTRIBUTING.md`).
- **Match existing style.** Docs are terse, information-dense English. Python tools print JSON to stdout (`{"ok": true, "path": ..., "cost_cents": ...}`) and progress to stderr; follow that contract.
- **Contribution process:** all changes start with an approved GitHub issue; PRs referencing an approved issue only; keep changes minimal and surgical. (Per user preference, this agent never commits — leave working-tree changes for the user to review, stage, and commit.)
- **Cost awareness:** every asset-gen tool invocation is a paid API call (Gemini, xAI Grok, Tripo3D). Never invoke the generation tools casually from this source repo.

## Verification

There is no test suite and no build step for this repo (docs + Python scripts + bash). Verify changes by:

1. Re-publishing to a scratch dir (`./publish.sh --engine <e> --agent <a> --out /tmp/check`) and inspecting the rendered output — tokens resolved, correct skill layout, correct `.gitignore`.
2. For `asset-gen` tool changes: `python3 -m py_compile asset-gen/tools/*.py`, then end-to-end pipeline testing per `CONTRIBUTING.md` (include output/summary in the PR).
3. For engine-guide changes: run a full generation in a published repo (e.g. the local `MiniGame/`) and confirm the guide's instructions still hold against the installed engine.

## Environment Notes

- API keys required by the published runtime: `GOOGLE_API_KEY`, `XAI_API_KEY`, `TRIPO3D_API_KEY` (Gemini images, Grok images/video, Tripo3D image-to-3D).
- Python 3.10+ with `asset-gen/tools/requirements.txt` + `google-genai` for asset tools.
- Published repos additionally need per-engine toolchains: Godot .NET edition + .NET 9 (godot), Rust/Cargo (bevy), Node 22.12+ (babylon), plus ffmpeg/ImageMagick/xvfb/vulkan-tools on Linux.
