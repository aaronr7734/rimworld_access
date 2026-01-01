# ScreenReader Module

## Purpose
Provides direct screen reader integration via Tolk library and custom audio feedback for navigation cues.

## Files in This Module

### Screen Reader Bridge (1 file)
- **TolkHelper.cs** - P/Invoke wrapper for Tolk.dll and nvdaControllerClient64.dll, provides Speak() method

### Audio Feedback (2 files)
- **EmbeddedAudioHelper.cs** - Loads and plays custom audio files embedded in DLL
- **TerrainAudioHelper.cs** - Provides terrain-based audio cues for map navigation

## Key Architecture

### State Management
No state - this module provides services to other modules.

### Input Handling
No input handling - this module only provides output (speech and audio).

### Dependencies
**Requires:** Core/ (for initialization)
**Used by:** All modules (every State announces via TolkHelper.Speak())

## Keyboard Shortcuts
None - this module is output-only

## Integration with Core Systems

### UnifiedKeyboardPatch
Not applicable - no input handling

### TolkHelper (Screen Reader)
This IS the TolkHelper implementation

### MapNavigationState
TerrainAudioHelper is used by MapNavigationState to play audio cues based on terrain type

## Common Patterns

### Announcing to Screen Reader
```csharp
TolkHelper.Speak("Selected item", SpeechPriority.Low);
TolkHelper.Speak("Error occurred", SpeechPriority.High);
```

### Playing Custom Audio
```csharp
EmbeddedAudioHelper.PlayEmbeddedSound("navigate.wav", 0.5f);
```

## RimWorld Integration

### Harmony Patches
None - this module is utility-only

### Reflection Usage
- Uses `Assembly.GetManifestResourceStream()` to load embedded audio files

### Game Systems Used
- Unity's AudioSource for audio playback

## Speech Priority Levels

- **SpeechPriority.Low** - Don't interrupt current speech (navigation, browsing)
- **SpeechPriority.Normal** - Default priority (actions, selections)
- **SpeechPriority.High** - Interrupt everything (errors, warnings, critical info)

## Screen Reader Detection

TolkHelper supports multiple fallback methods:
1. **Detected Screen Reader** - Via Tolk.dll detection
2. **Direct NVDA** - Via nvdaControllerClient64.dll if Tolk fails
3. **SAPI Fallback** - Windows built-in TTS if no screen reader detected

## Testing Checklist
- [ ] TolkHelper initializes without errors
- [ ] Screen reader announces text (test with NVDA or JAWS)
- [ ] SAPI fallback works when no screen reader is running
- [ ] Speech priorities work correctly (High interrupts Low)
- [ ] Custom audio files play correctly
- [ ] No audio crackling or distortion
