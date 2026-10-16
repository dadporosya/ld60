# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

**LD60** is a game project built with **Unity 6 (6000.3.2f1)** using the **Universal Render Pipeline (URP)** and the new **Input System**. All core architecture and gameplay code lives under `Assets/_ARCHITECTURE/Scripts/` (with custom editor tooling in `Assets/_ARCHITECTURE/Editor/` and dialogue/audio editor scripts in respective subsystem Editor subfolders). The main sample scene is `Assets/Scenes/SampleScene.unity`.

Game scripts compile into `Assembly-CSharp` along with modular packages/asmdefs:
- `VolFx.Runtime`, `VolFx.Editor`, `ScreenFx`, `ScreenFx.Editor`, `Tools.Runtime`, `Tools.Editor` (under `Assets/_ARCHITECTURE/VolFx/`)
- `PrimeTween.Runtime` (under `Packages/com.kyrylokuzyk.primetween/Runtime/`)
- `PrimeTween.Installer` (under `Assets/_ARCHITECTURE/Plugins/PrimeTween/internal/`)

## Building / Running / Testing

- **In Editor**: Open in Unity Editor 6000.3.2f1 and press Play, or build via **File > Build Settings**.
- **CLI Compilation**: The solution builds cleanly from the terminal with:
  ```powershell
  dotnet build ld60.sln
  ```
  or individual projects:
  ```powershell
  dotnet build Assembly-CSharp.csproj
  dotnet build Assembly-CSharp-Editor.csproj
  ```
- **Tests**: Unity Test Framework (`com.unity.test-framework`) can be run from **Window > General > Test Runner** or headlessly:
  ```powershell
  Unity.exe -runTests -projectPath . -testPlatform EditMode -testResults results.xml -batchmode
  ```

## Packages & Plugins

- **PrimeTween 1.5.0** (`com.kyrylokuzyk.primetween`) — High-performance, zero-allocation tweening and animation library used extensively throughout animations, camera controls, volume fading, and UI. Installed as a local package at `Packages/com.kyrylokuzyk.primetween` (source tarball at `Packages/com.kyrylokuzyk.primetween.tgz` referenced in `Packages/manifest.json`), with runtime project `PrimeTween.Runtime.csproj`. Prefer PrimeTween over coroutines for tween animations.
- **VolFx & ScreenFx** (`Assets/_ARCHITECTURE/VolFx/`) — Modular URP post-processing framework with passes for Watercolor, Dither, VHS, Glitch, Scanlines, and screen-space distortion effects.
- **EZ Camera Shake** (`Assets/_ARCHITECTURE/EZ-Camera-Shake-Unity-master`) — Camera shake management; integrated into `h` (Helpers) and camera components.
- **UI Particle** (`com.coffee.ui-particle`, mob-sakai's ParticleEffectForUGUI, namespace `Coffee.UIExtensions`) — Renders `ParticleSystem`s within Canvas UI via the `UIParticle` component.
- **rsg** (`com.xk.rsg`) — Promises and reactive utilities.

## Architecture & Subsystems

All code is organized modularly under `Assets/_ARCHITECTURE/Scripts/`:

### Global Static Helpers & Preferences

- **`h`** (`Helpers/Helpers.cs`) — Core static utility class: logging via `h.Out(...)`, random utilities (`h.RandChoice`), math helpers, and the singleton initialization pattern `h.CreateStaticInstance(this, ref Instance)`.
- **`P`** (`Helpers/Preferences.cs`) — Global static lookup tables, constants, and enums (`P.Team`, `P.EquipmentType`, `P.Stat`, string stat keys `P.StatK`, `P.rarities`).
- **`PlayHistory`** (`Helpers/PlayHistory.cs`) — PlayerPrefs persistent progress tracker across game sessions: tracks played dialogues (`HasPlayedDialogue`, `MarkDialoguePlayed`), entered scenes (`HasPlayed`, `MarkPlayed`), and provides `ResetToFirstEnter()` for first-run testing.

### Managers (`Managers/`)

- **`GameManager`** (`Managers/GameManager.cs`) — Core gameplay manager singleton: provides access to the primary drawing canvas (`MainBoard`), dispatches `onDamaged` (`UnityEvent<float>`), and manages gameplay flags (`IsOver`, `IsShaking`, `DrawingThroughDialogue`) and rule suspensions (`SuspendRules`, `ResumeRules`).
- **`GameFlowManager`** (`Managers/GameFlowManager.cs`) — State machine managing overall lifecycle (`Intro`, `Game`, `Pause`, `Loss`, `Finale`) with pause stack memory (`Resume()` restores prior non-pause state).
- **`CameraManager`** / **`ScreenManager`** / **`DifficultyManager`** / **`GlobalEventManager`** / **`ShutdownManager`**.

### Drawing & Pencil (`DrawingAndPencil/`)

Interactive 2D drawing mechanics operating directly on dynamic textures with pixel saturation and zone tracking:
- **`PixelGrid`** (`PixelGrid.cs`) — Drawing canvas component. Manages a grid of `PixelCell`s, renders to a runtime `Texture2D` using multiply or alpha blend modes, detects stroke crossings/overlaps, triggers damage, and reports zone changes (`OnCellZoneChanged`, `OnOverlap`, `OnStrokeEnded`).
- **`PixelCell`** (`PixelCell.cs`) — Per-cell data structure tracking saturation (0..1), rework layer count, and last-touched timestamp.
- **`SaturationSettings`** (`SaturationSettings.cs`) — Tuning curves and thresholds for paint saturation: base rates, exponential rework multipliers, overlap detection spacing, color gradients, and zone boundaries (`Empty`, `NotEnough`, `Perfect`, `TooMuch`).
- **`BoardDrawSfx`** (`BoardDrawSfx.cs`) — Audio controller attached to `PixelGrid`: loops drawing SFX during active strokes, plays dedicated overdose loops when painting over already-saturated cells, and smoothly modulates volume against the saturation curve.
- **`Pencil`** (`Pencil.cs`) — Drawing controller with lagging tip physics and smoothing. Dispatches drawing commands to the current `PixelGrid`, supports hardware mouse input and virtual pointer scripting (`SetVirtualPointer`), and controls drawing permissions via `CanDraw` / `CanDrawOn`.
- **`PencilMarker`** (`PencilMarker.cs`) — Visual cursor/indicator tracking the pencil tip position.
- **`PencilParticles`** & **`OverdoseParticles`** — Spawns UI particle effects during active drawing and when cells enter the `TooMuch` zone.
- **`PencilRecorder`** & **`PencilRecording`** (`PencilRecorder.cs`, `PencilRecording.cs`) — Records mouse strokes into reusable ScriptableObject assets.
- **`PencilPlayback`** (`PencilPlayback.cs`) — Replays stored `PencilRecording` data through virtual pointer simulation on a target board.
- **`HandAnimation`** (`HandAnimation.cs`) — UI Image sprite sequencer for the player's hand: plays frame-based damage reactions while damage shake lasts and schedules weighted-random idle spasms during calm periods.

### Rendering & Post-Processing Volumes (`Rendering/`)

Dynamic runtime control over URP post-processing volumes without modifying on-disk profile assets:
- **`VolumeManager`** (`VolumeManager.cs`) — Base class managing a single URP `Volume`. Accesses `volume.profile` to safely mutate a runtime copy, and provides `TweenWeight` to smoothly transition volume blend weights with PrimeTween.
- **Typed Volume Managers**:
  - `BloomVolumeManager` — Controls URP Bloom (intensity, threshold, scatter, tint).
  - `BlurVolumeManager` — Controls blur radius and strength.
  - `GlitchVolumeManager` — Controls scanline and noise glitch effects.
  - `JpegVolumeManager` — Controls compression and artifact simulation.
  - `VhsVolumeManager` — Controls VHS tracking and tape distortion.
  - `VignetteVolumeManager` — Controls URP Vignette intensity and smoothness.
  - `VolFxSourceManager` — Connects custom VolFx post-process passes to the Volume system.

### Dialogues & Visual Novel (`Dialogues/`)

- **Core Dialogue System** — `DialogueManager`, `DialogueContainer`, `Talkable`. Supports typing effects, hold-to-skip (Esc/Enter radial indicator), per-node auto-skip timers, and dialogue queuing.
- **Dialogue UI Enhancements** (`DialogueHelpers/`):
  - `DialogueWindowAnimations` — Window open/close transitions, text typing bounce, and interruption dips.
  - `DialogueWindowCollapse` — Minimizable tab button sliding the dialogue window off-screen to reveal underlying gameplay while holding progression.
  - `TalkableWithModels` & `TalkableWModelsController` — Multi-character model and avatar management per dialogue node.
- **Visual Novel Stage** (`VisualNovel/`):
  - `VisualNovelTalkable` & `VisualNovelDialogueContainer` — Full visual novel presentation controlling foreground/background sprites, transitions, audio volumes, camera framing, and post-process blur.
  - `VisualNovelEffect` — ScriptableObject effect presets for dialogue events (`Play`, `Sustain`, `Release`).

### Cutscenes (`CutScenesScripts/`)

- **`CutSceneBase`** — Base class for procedural and scripted cutscene controllers (clones at runtime and cleans up upon completion).
- **`TutorialCutscene`** (`TutorialCutscene.cs`) — Controls tutorial sequences, manages player drawing permissions (`PlayerMayDraw`), and coordinates with `GameManager` rule suspensions.
- **`CutsceneHolder`** & **`ButtonCutscene`**.

### Transitions (`Transitions/`)

- **`TransitionManager`** (`TransitionManager.cs`) — Coordinates smooth multi-stage camera transitions: dives camera with PrimeTween curves, dynamically generates and tunes cloud particle systems, coordinates screen fade covers via `FadeImageController`, and swaps scenes/levels behind the transition cover.

### Camera Systems (`Camera/`)

- **`RTSCameraController`** (`RTSCameraController.cs`) — Main RTS gameplay camera with keyboard/edge panning, zoom, bounds clamping, and movement tilt lean on rig parent (execution order `-200`).
- **`CameraController`** (`CameraController.cs`) — Tween-based camera for cutscenes, targeting, and focused transitions.

### Drag & Drop (`DnD/`)

- **`CursorDnDManager`** — Central singleton orchestrating UI drag-and-drop operations across canvases.
- **`DragableObject`** — Draggable base handling canvas reparenting to `DragLayer` and return animations.
- **`SlotBase`** & **`GameObjectDNDObject`** — Receptive drop target slots and drag wrappers for game objects with tooltip delays.

### Units, Equipment & Spawning (`Units/`, `Equipment/`, `Directors/`)

- **`Unit`** (`Units/UnitMonoB.cs`) — Core gameplay entity implementing `IDamageable` and `StatUIShownBase`, managing equipment slots and stat containers.
- **`ObjectsCountContainer`** (`_Scriptable objects/_Src/`) — ScriptableObject storing string-keyed stat dictionaries.
- **`DirectorBase<T>`** (`Directors/Directors/`) — Token-budget economy spending procedural tokens on weighted prefab lists.

## Coding Conventions

- **Logging**: Always use `h.Out(...)` rather than `Debug.Log`.
- **Singletons**: Use `h.CreateStaticInstance(this, ref Instance)` in `Awake()`.
- **Tweens**: Use `PrimeTween` (`Tween.Custom`, `Tween.Position`, `Tween.Alpha`, etc.) instead of coroutines for interpolations.
- **Stat References**: Always look up stats, rarity, and equipment types through `P`, never with magic strings.
- **File Organization**: Keep reusable architecture and gameplay code under `Assets/_ARCHITECTURE/Scripts/<subsystem>/`.
