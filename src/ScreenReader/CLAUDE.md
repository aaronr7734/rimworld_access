# ScreenReader Module

## Purpose
Provides cross-platform screen reader integration via the Prism library and custom audio feedback for navigation cues.

## Files in This Module

### Screen Reader Bridge (3 files)
- **TolkHelper.cs** - High-level speech API (`Speak()`, `Initialize()`, `Shutdown()`, `IsActive()`) used by all modules
- **PrismNative.cs** - Low-level Prism C API bindings: delegates, enums, structs, UTF-8 marshaling helpers
- **NativeLibraryLoader.cs** - Cross-platform native library loader (LoadLibraryW on Windows, dlopen on Unix)

### Speech Sanitization (1 file)
- **SpeechSanitizer.cs** - Centralized text cleanup pipeline (tag stripping, punctuation fixes, whitespace normalization). Runs automatically in TolkHelper.Speak() before text reaches the screen reader.

### Audio Feedback (2 files)
- **EmbeddedAudioHelper.cs** - Loads and plays custom audio files embedded in DLL
- **TerrainAudioHelper.cs** - Per-cell audio cues for map navigation. `PlayCellAudio(cell, map, volume)` plays a distinct wall sound (`wall.wav`) for any full-fill edifice - built walls, natural rock/mountain, and closed doors - taking precedence over the terrain beneath; otherwise it plays the terrain's own sound. Open doors fall through to terrain. Wall detection uses `GetEdifice` + `Fillage == Full` (excluding open `Building_Door`), never label matching.

## Key Architecture

### Prism Integration Stack
```
All modules → TolkHelper.Speak()
                ↓
              SpeechSanitizer.Sanitize()
                ↓
              PrismNative.MarshalUtf8() → prism_backend_output()
                ↓
              NativeLibraryLoader → prism.dll / libprism.dylib / libprism.so
                ↓
              Prism auto-selects backend: NVDA, JAWS, VoiceOver, Orca, SAPI, etc.
```

### State Management
No state - this module provides services to other modules.

### Input Handling
No input handling - this module only provides output (speech and audio).

### Dependencies
**Requires:** Core/ (for initialization)
**Used by:** All modules (every State announces via TolkHelper.Speak())

## Cross-Platform Support

- **Windows:** prism.dll (x64) — backends: NVDA, JAWS, SAPI, OneCore, UIA, ZDSR, ZoomText
- **macOS:** libprism.dylib (universal binary) — backends: VoiceOver, AVSpeech
- **Linux:** libprism.so (x64) — backends: Orca, Speech Dispatcher

Platform detection uses `Environment.OSVersion.Platform` with macOS/Linux disambiguation via filesystem checks.

## Speech Priority Levels

- **SpeechPriority.Low** - Don't interrupt current speech (navigation, browsing)
- **SpeechPriority.Normal** - Default priority (actions, selections)
- **SpeechPriority.High** - Interrupt everything (errors, warnings, critical info)

Priority mapping: High → `interrupt=true`, Low/Normal → `interrupt=false`

## Screen Reader Detection

Prism automatically selects the best available backend via `prism_registry_acquire_best()`. Screen readers are prioritized over standalone TTS engines. No manual fallback logic needed.

## Testing Checklist
- [ ] TolkHelper initializes without errors on Windows
- [ ] Screen reader announces text (test with NVDA or JAWS on Windows)
- [ ] VoiceOver works on macOS
- [ ] Orca/Speech Dispatcher works on Linux
- [ ] SAPI/OneCore fallback works when no screen reader is running
- [ ] Speech priorities work correctly (High interrupts Low)
- [ ] Custom audio files play correctly
- [ ] No audio crackling or distortion
- [ ] Graceful error message when native library is missing
