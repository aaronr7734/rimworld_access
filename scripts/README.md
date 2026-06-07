# scripts/

## `check_localization.py` — localization guardrail (roadmap D.8)

Fails when player-facing English is added that the C# compiler cannot catch.

### Why a lint when the compiler already blocks raw English?

Phase F.4 deleted the `TolkHelper.Speak(string)` overload, so a raw
`Speak("english")` is now a hard build error. That covers the *direct* case.
It does **not** cover English that is only spoken/shown *indirectly*:

- returned from a method (`return "No save files available";`)
- assigned to a display field (`Label = "Storage settings"`, `defaultLabel = ...`)
- built by interpolation (`return $"{n} days, {h} hours";`)
- raw words concatenated onto a localized fragment (`"X" + key.Translate()`)

This lint scans `src/**/*.cs` for those forms and reports any that aren't
accounted for.

### Run it

```bash
python3 scripts/check_localization.py        # exits 1 on any finding
```

It also runs automatically in CI on every push / pull request
(`.github/workflows/localization-guard.yml`).

### When it flags a line

1. **It's a real leak** — author a translation key in the relevant
   `Languages/English/Keyed/RimWorldAccess_*.xml` and call `.Translate()` /
   `.Loc()`. Prefer reusing an existing key or a vanilla game key.
2. **It's genuinely intentional** — a stable dispatch token, a dev-only
   diagnostic, or a string localized elsewhere via a lookup table — add an
   inline marker on the same line:

   ```csharp
   return "Pen Animals"; // l10n-exempt: dispatch token, localized via InspectionCategoryLocalizer
   ```

   Always include a reason. The marker is per-line and self-documenting.

A baseline file (`scripts/localization_baseline.txt`) is also supported for
bulk-blessing reviewed exceptions, but inline markers are preferred. Regenerate
the baseline only after a deliberate review:

```bash
python3 scripts/check_localization.py --update-baseline
```

### Scope / limitations

It is a heuristic regex lint, not a full C# parser. It targets multi-word
English prose and the concatenation trap; single-word labels and
preposition+data fragments can slip through. The goal is to catch the common
regressions cheaply, not to prove the absence of every possible leak.
