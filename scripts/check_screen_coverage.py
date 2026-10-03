#!/usr/bin/env python3
"""Window-coverage ratchet for RimWorld Access.

Every concrete `Verse.Window` subclass in the game's own assemblies must be
accounted for by exactly one of three things:

1. A `ScopeForWindow.Register` / `RegisterHierarchy` / `RegisterGenericHierarchy`
   call anywhere in src/ (compat included — mod scopes register the same way).
   Hierarchy registrations are resolved through the decompiled inheritance graph,
   not by name prefix; that resolution is load-bearing (it is the difference
   between 56 and 43 uncovered types today).
2. A `[HarmonyPatch(typeof(X)...)]` in src/ naming the window type. This is a
   deliberate heuristic: a Harmony patch is not proof of a scope, but it is proof
   we looked at that window on purpose, which is what this ratchet measures.
3. An entry in COVERAGE_BASELINE stating in one line why the window is not
   covered. Seeded honestly — "gap, no bespoke scope yet" is an acceptable reason
   and several entries say exactly that.

A new vanilla window arriving in a RimWorld update, or one of ours losing its
registration, therefore fails the ratchet. So does a stale baseline entry (the
type gained coverage or no longer exists), which keeps the set exact.

The census walks the decompiled tree by text — it is not a compiler. Top-level
directories skipped: `bin`, `obj`, `mods` and `AnimalTraits` (decompiled MOD
assemblies, not the game), plus `NVorbis`, `Ionic*`, `ISharpZipLib`, `KTrie`,
`DelaunatorSharp`, `Gilzoide` and anything starting with `UnityEngine`, `Unity.`,
`com.` or `System.` (engine and vendor code, no `Verse.Window` in any of it).
Abstract window types are excluded: an abstract window is never opened, so
covering it is meaningless — `RegisterHierarchy` on one is how we cover its
concrete children.

Mod windows cannot be enumerated statically; this ratchet sees only the game's
own assemblies. The runtime complement is the QA flight recorder's
`attach generic-window for <Type>` line, which names any window — vanilla or
modded — that fell through to the generic reader in a real session.

The decompiled tree is gitignored, so a checkout without it skips this ratchet
with an informational line rather than failing.

Usage: check_screen_coverage.py
"""
import collections
import glob
import os
import re
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DECOMPILED_CANDIDATES = (
    os.path.join(REPO, "decompiled"),
    os.path.join(os.path.dirname(REPO), "decompiled"),
)

SKIP_TOP_DIRS = frozenset({
    "bin", "obj", "mods", "AnimalTraits", "NVorbis", "Ionic.Crc", "Ionic.Zlib",
    "ISharpZipLib", "KTrie", "DelaunatorSharp",
})
SKIP_TOP_PREFIXES = ("UnityEngine", "Unity.", "com.", "System.", "Ionic",
                     "Gilzoide")

# Line-anchored class declaration: modifier soup, name, optional generic
# parameter list, optional base list. Group 3 is the whole base list; the first
# entry of it is the base class (C# requires the base class first).
CLASS_DECL_RE = re.compile(
    r'^\s*(?:\[[^\]]*\]\s*)*'
    r'(?:(?:public|internal|protected|private|sealed|abstract|static|partial'
    r'|new|unsafe|readonly|ref)\s+)*'
    r'class\s+(\w+)\s*(?:<[^>{]*>)?\s*(?::\s*([^{]+))?',
    re.M)

REGISTRATION_RE = re.compile(
    r'ScopeForWindow\.(Register|RegisterHierarchy|RegisterGenericHierarchy)'
    r'\s*\(\s*typeof\(\s*([A-Za-z0-9_.]+)\s*(?:<>)?\s*\)')
HARMONY_TYPE_RE = re.compile(r'\[HarmonyPatch\(\s*typeof\(\s*([A-Za-z0-9_.]+)\s*\)')

# Concrete game windows with neither a registration nor a Harmony patch naming
# them, each with the reason it is not covered. Verified by grepping src/ for
# every type name and reading the covering scope (or confirming there is none).
COVERAGE_BASELINE = {
    # Windowless scopes and states that replace or mirror the vanilla dialog:
    # the surface is ours, so nothing ever registers the vanilla type.

    # Vehicle Framework dialogs surfaced by the decompiled reference dump; the ones
    # players hit have scopes via VfDialogCompat, these six ride the generic reader.
    "Dialog_AssignSeats": "generic reader; Vehicle Framework caravan seat assignment, no bespoke scope yet",
    "Dialog_StripMineConfiguration": "generic reader via ScopeDelegateGuard in AllowToolRectDesignationHandler",
    "Dialog_ColorWheel": "visual color wheel; cosmetic and mouse-driven, no keyboard surface planned",
    "Dialog_ConfigureTurret": "generic reader; Vehicle Framework turret loadout dialog, no bespoke scope yet",
    "Dialog_GiveVehicleName": "generic reader; single text field rename, the text session handles it",
    "Dialog_LoadCargo": "generic reader; Vehicle Framework cargo-loading dialog, no bespoke scope yet",
    "Dialog_NodeSettings": "generic reader; Vehicle Framework dev/config popup",
    "Dialog_StashVehicle": "generic reader; Vehicle Framework world stash confirmation",
    "Dialog_StatSettings": "generic reader; Vehicle Framework dev/config popup",
    "Dialog_VehiclePainter": "visual color picker; painting is cosmetic and mouse-driven, no keyboard surface planned",
    "Dialog_VehicleSelector": "generic reader; Vehicle Framework world vehicle selector, no bespoke scope yet",

    # Served adequately by the generic reader; no bespoke work planned.
    "Dialog_MapSettings": "generic reader; Hospitality's checkbox-only per-map settings dialog",
    "Dialog_KeyBindings": "generic reader; scopeless dialog above Options, per OptionsScope's foreign-window guard",
    "Dialog_DefineBinding": "generic reader; scopeless key-rebind prompt above Options",
    "Dialog_AddPreferredName": "generic reader; scopeless name-picker above Options",
    "Dialog_ResolutionConfirm": "generic reader; revert-countdown confirm raised by ResolutionUtility",
    "Dialog_XenogermList_Load": "generic reader; file picker inside the xenogerm assembler",

    # Dev-mode and debug-only, outside the player-facing mission.
    "Dialog_DevCelestial": "LudeonTK dev tool",
    "Dialog_DevInfectionPathways": "LudeonTK dev tool",
    "Dialog_DevMusic": "LudeonTK dev tool",
    "Dialog_DevNoiseMap": "LudeonTK dev tool",
    "Dialog_DevNoiseWorld": "LudeonTK dev tool",
    "Dialog_DevPalette": "LudeonTK dev tool",
    "EditWindow_DefEditor": "LudeonTK dev def editor",
    "Dialog_DebugRenderTree": "dev-only, opened from DebugActionsIdeo",
    "Dialog_DebugSetHediffRemaining": "dev-only debug setter",
    "Dialog_DebugSetSeverity": "dev-only debug setter",
    "Dialog_PawnTableTest": "dev-only pawn-table harness",
    "Dialog_CameraConfig": "dev-only, opened from DebugActionsMisc",
    "Dialog_CameraConfigList_Load": "dev-only camera-config file picker",
    "Dialog_CameraConfigList_Save": "dev-only camera-config file picker",

    # Not an interactive window at all.
    "ImmediateWindow": "per-frame IMGUI drawing host (tooltips, chrome); ScopeForWindow skips it explicitly",
    "Screen_ArchonexusSettlementCinematics": "cinematic; ArchonexusCinematicPatch handles it from a base-type Window patch guarded by `__instance is`",
    "WorldInspectPane": "a pane, not a WindowStack window; StartingSiteScreenScope reads it directly",
    "Dialog_WorkshopOperationInProgress": "progress readout for a Steam upload, no controls to operate",
    "FloatMenuGrid": "visual swatch grid; PaintColorHelper/PlanColorHelper route the same picks through an accessible FloatMenu",
    "Dialog_Ideo": "dead vanilla type, never instantiated anywhere in the decompiled tree",
}


def find_decompiled():
    for path in DECOMPILED_CANDIDATES:
        if os.path.isdir(path):
            return path
    return None


def decompiled_files(root):
    for entry in sorted(os.listdir(root)):
        path = os.path.join(root, entry)
        if os.path.isfile(path):
            if entry.endswith(".cs"):
                yield path
            continue
        if entry in SKIP_TOP_DIRS or entry.startswith(SKIP_TOP_PREFIXES):
            continue
        for dirpath, dirnames, filenames in os.walk(path):
            dirnames[:] = [d for d in dirnames if d not in ("bin", "obj")]
            for name in filenames:
                if name.endswith(".cs"):
                    yield os.path.join(dirpath, name)


def first_base(base_list):
    """First entry of a C# base list, stripped of generic arguments and
    namespace qualification. Commas inside `<...>` do not separate entries."""
    depth = 0
    head = []
    for ch in base_list:
        if ch == "<":
            depth += 1
        elif ch == ">":
            depth -= 1
        elif ch == "," and depth == 0:
            break
        head.append(ch)
    return re.sub(r'<.*$', '', "".join(head)).strip().split(".")[-1]


def build_type_graph(root):
    """Maps simple class name -> (base name or None, is_abstract, relative path).
    First declaration of a given simple name wins; the decompiled tree has no
    duplicate window names."""
    types = {}
    for path in decompiled_files(root):
        with open(path, encoding="utf-8", errors="replace") as handle:
            text = handle.read()
        rel = os.path.relpath(path, root)
        for match in CLASS_DECL_RE.finditer(text):
            name = match.group(1)
            if name in types:
                continue
            base = first_base(match.group(2)) if match.group(2) else None
            is_abstract = " abstract " in " " + match.group(0)
            types[name] = (base, is_abstract, rel)
    return types


def concrete_windows(types):
    def is_window(name):
        seen = set()
        cur = name
        while cur and cur not in seen:
            if cur == "Window":
                return True
            seen.add(cur)
            entry = types.get(cur)
            if entry is None:
                return False
            cur = entry[0]
        return False

    return {name: entry[2] for name, entry in types.items()
            if name != "Window" and not entry[1] and is_window(name)}


def descendants_of(children, root):
    found = set()
    stack = [root]
    while stack:
        for child in children.get(stack.pop(), ()):
            if child not in found:
                found.add(child)
                stack.append(child)
    return found


def scan_source(types):
    children = collections.defaultdict(list)
    for name, entry in types.items():
        if entry[0]:
            children[entry[0]].append(name)

    covered = set()
    patched = set()
    for path in glob.glob(os.path.join(REPO, "src/**/*.cs"), recursive=True):
        with open(path, encoding="utf-8") as handle:
            text = handle.read()
        for kind, qualified in REGISTRATION_RE.findall(text):
            simple = qualified.split(".")[-1]
            covered.add(simple)
            if kind != "Register":
                covered |= descendants_of(children, simple)
        for qualified in HARMONY_TYPE_RE.findall(text):
            patched.add(qualified.split(".")[-1])
    return covered, patched


def main():
    root = find_decompiled()
    if root is None:
        print("check_screen_coverage: SKIPPED — no decompiled/ tree next to the "
              "repo (it is gitignored); the window census needs it.")
        return 0

    types = build_type_graph(root)
    windows = concrete_windows(types)
    covered, patched = scan_source(types)

    failures = []
    matched_baseline = set()
    for name in sorted(windows):
        accounted = name in covered or name in patched
        in_baseline = name in COVERAGE_BASELINE
        if in_baseline:
            matched_baseline.add(name)
        if accounted:
            if in_baseline:
                failures.append(
                    f"error RWA-COVERAGE: '{name}' is now covered by a "
                    f"ScopeForWindow registration or a Harmony patch — delete "
                    f"its COVERAGE_BASELINE entry in check_screen_coverage.py, "
                    f"the set must stay exact.")
            continue
        if not in_baseline:
            failures.append(
                f"error RWA-COVERAGE: concrete window '{name}' "
                f"({windows[name]}) has no ScopeForWindow registration, no "
                f"Harmony patch naming it, and no COVERAGE_BASELINE entry. "
                f"Register a scope in ShellBootstrap.Game.cs, or add a "
                f"COVERAGE_BASELINE entry in check_screen_coverage.py stating "
                f"in one line why it is not covered.")

    for name in sorted(set(COVERAGE_BASELINE) - matched_baseline):
        failures.append(
            f"error RWA-COVERAGE: COVERAGE_BASELINE entry '{name}' no longer "
            f"names a concrete game window (renamed, removed, or now abstract) "
            f"— delete it from check_screen_coverage.py.")

    for message in failures:
        print(message)
    if failures:
        return 1

    print(f"check_screen_coverage: OK — {len(windows)} concrete game windows, "
          f"all accounted for ({len(COVERAGE_BASELINE)} baselined with reasons)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
