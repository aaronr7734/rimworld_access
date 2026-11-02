#!/usr/bin/env python3
"""Inspection/gizmo rework doctrine ratchet: gizmo announcement/execution and
inspect-tree content dispatch must stay registry/datum-driven, never matched
on type names or translated labels.

Locks that doctrine in so it can't silently regress:

1. Language-fragile predicate guard: gizmo detection must never match on
   translated display strings. The label matches were replaced
   in ShouldSkipGizmo (transporter/launch-group nav -> action-method
   identity), the merge-caravan dedup (-> icon reference identity), and
   GetDisabledGizmoContext (-> CompTransporter.OverMassCapacity). None of the
   retired patterns may reappear in the inspection/gizmo sources.

2. Hardcoded announcement guard: the localized English literals
   ("Not available", "Disabled:", "Bound buildings:", the inline typeahead
   "of N matches for" suffix) must not reappear as string literals in the
   inspection sources — announcements go through Keyed translation strings.

3. Name-tier registration guard: GizmoHandlerRegistry.RegisterByTypeName with
   a string literal is forbidden in our source. The byType chain resolves
   before byTypeName, so a name-tier entry for a PUBLIC type is silently
   shadowed the moment a base type gains a registration (the
   registration-order trap). Public types register via Register(typeof(...));
   the name tier exists only for genuinely non-public types, which our own
   source has none of. NAME_TIER_ALLOWLIST is the one frozen exception: VEF
   compat's insectoid auto-cast handler ADDS a facet to vanilla's public
   Command_Ability without replacing AbilityGizmoHandler's existing byType
   registration (Register(Type, handler) REPLACES rather than merges — see
   TypeChainResolver.Register — so Register(typeof(Command_Ability), ...)
   would silently drop AbilityGizmoHandler's cost/range/cooldown facets). The
   handler's own exact-type + IsPlayerDraftedInsectoid gates keep it inert
   everywhere else. New entries need the same "additive, can't use Register"
   justification, not just a build-passes excuse.

4. Type-name guard: `GetType().Name ==` / `typeName ==` / `switch (typeName)`
   dispatch is forbidden in the inspection tree sources (TabRegistry,
   InspectionTreeBuilder) AND in GizmoNavigationState (its 49 type-name
   sites are gone: announcement, execution, slider adjustment and hints all
   resolve through the handler registry now).
   Resolve by `is` / typeof / a registered handler instead.

5. Category localizer completeness: every `OriginalCategoryName = "..."`
   synthetic-category identifier assigned anywhere in src/ must have an entry
   in InspectionCategoryLocalizer's key table, so no category can silently
   speak raw English in a localized game.

6. Reflection guard: GizmoNavigationState.cs holds ZERO reflection call sites,
   down from 78. Per-gizmo-type
   reflection lives inside the type's handler, cached once against the
   declaring type; the navigation state machine itself never reflects.

7. Tab-registry collapse guard: TabRegistry.cs holds no
   string-keyed dictionaries and no BaseType walks. Tab resolution goes
   through InspectNodeRegistry's TypeChainResolver, keyed by typeof. The
   retired tabTypeToCategory / tabTypeToHandler / categoryNameToHandler
   dictionaries and the GetOriginalCategoryName type-name switch must not
   reappear.

8. Category-dispatch guard: `category == "..."` /
   `categoryKey == "..."` string comparisons are forbidden in
   InspectionTreeBuilder.cs and everywhere under src/Inspection/Adapters/.
   The retired IsSingleItemCategory / IsExpandableCategory /
   GetSimplifiedCategoryContent / GetCategoryLabel predicates and the
   ExecuteCategoryAction switch resolved behavior per category name; all of
   it now dispatches through InspectNodeRegistry.TryResolveCategory to the
   adapter facets (CategoryLabel / IsInline / CanExpand / NoExpandLabel /
   ExecuteAction). InspectionInfoHelper's fallback content helpers are
   deliberately NOT in scope: the capture backend supersedes them.

Usage: check_inspection_doctrine.py
"""
import glob
import os
import re
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

GIZMO_NAV = "src/Inspection/GizmoNavigationState.cs"
GIZMO_NAV_FILES = (
    "src/Inspection/GizmoNavigationState.cs",
    "src/Inspection/GizmoNavigationState.Input.cs",
    "src/Inspection/GizmoNavigationState.Describe.cs",
    "src/Inspection/GizmoNavigationState.Actions.cs",
)
TREE_FILES = (
    "src/Inspection/TabRegistry.cs",
    "src/Inspection/InspectionTreeBuilder.cs",
    "src/Inspection/GizmoNavigationState.cs",
)
LOCALIZER = "src/Inspection/InspectionCategoryLocalizer.cs"

# Retired language-fragile predicates (check 1) — patterns that matched
# translated display strings, scoped to the gizmo navigation source.
FRAGILE_PREDICATE_RES = [
    re.compile(r'Contains\(\s*"transporter"'),
    re.compile(r'Contains\(\s*"launch group"'),
    re.compile(r'Contains\(\s*"merge"'),
    re.compile(r'Contains\(\s*"mass'),
]

# Localized-in-Phase-0 English literals (check 2), scoped to src/Inspection.
HARDCODED_ANNOUNCEMENT_RES = [
    re.compile(r'"Not available"'),
    re.compile(r'"\s*Disabled:'),
    re.compile(r'"Bound buildings:'),
    re.compile(r'of \{[\w.]+\} matches for'),
]

NAME_TIER_RE = re.compile(r'RegisterByTypeName\s*\(\s*"([^"]+)"')

# Frozen allowlist for check_name_tier_literals: (repo-relative file, type
# name literal). Every entry must be additive-only (see rule 3 above) — never
# add one just to make the build pass.
NAME_TIER_ALLOWLIST = {
    ("src/Compat/VEF/VefGizmoCompat.Game.cs", "Command_Ability"),
    # Drafted Auto-Combat's auto-cast facet: same additive shape, gated on exact type + IsDraftedPlayerPawn.
    ("src/Compat/AutoCombat/AutoCombatModule.Game.cs", "Command_Ability"),
}
TYPE_NAME_DISPATCH_RE = re.compile(
    r'GetType\(\)\s*\.\s*Name\s*==|\btypeName\s*==\s*"|switch\s*\(\s*typeName\s*\)')

REFLECTION_RE = re.compile(
    r'\.GetField\s*\(|\.GetProperty\s*\(|\.GetMethod\s*\(|BindingFlags|'
    r'AccessTools|Activator\s*\.\s*CreateInstance')

TAB_REGISTRY = "src/Inspection/TabRegistry.cs"
TAB_REGISTRY_RETIRED_RE = re.compile(
    r'Dictionary\s*<\s*string|tabTypeToCategory|tabTypeToHandler|'
    r'categoryNameToHandler|\.BaseType\b')

TREE_BUILDER = "src/Inspection/InspectionTreeBuilder.cs"
CATEGORY_DISPATCH_RE = re.compile(
    r'\bcategory(?:Key)?\s*==\s*"|"\s*==\s*category(?:Key)?\b|'
    r'switch\s*\(\s*category(?:Key)?\s*\)')

ORIGINAL_CATEGORY_RE = re.compile(r'OriginalCategoryName\s*=\s*"([^"]+)"')
LOCALIZER_KEY_RE = re.compile(r'\{\s*"([^"]+)"\s*,\s*"RimWorldAccess\.')


def strip_comments(text):
    """Line-based comment stripper: drops `//...` tails (same tradeoffs as
    check_shell_demolition.strip_comments)."""
    out_lines = []
    for line in text.splitlines():
        idx = line.find("//")
        out_lines.append(line[:idx] if idx != -1 else line)
    return "\n".join(out_lines)


def read_stripped(rel):
    with open(os.path.join(REPO, rel), encoding="utf-8") as f:
        return strip_comments(f.read())


def all_src_files():
    return glob.glob(os.path.join(REPO, "src/**/*.cs"), recursive=True)


def inspection_files():
    return glob.glob(os.path.join(REPO, "src/Inspection/**/*.cs"), recursive=True)


def check_fragile_predicates():
    failures = []
    for rel in GIZMO_NAV_FILES:
        stripped = read_stripped(rel)
        for pattern in FRAGILE_PREDICATE_RES:
            if pattern.search(stripped):
                failures.append(
                    f"error RWA-INSPECT: language-fragile predicate "
                    f"({pattern.pattern}) in {rel} — detect gizmos by game "
                    f"identity (action-method, icon reference, typed property), "
                    f"never by translated label/reason text.")
    return failures


def check_hardcoded_announcements():
    failures = []
    for f in inspection_files():
        rel = os.path.relpath(f, REPO).replace(os.sep, "/")
        stripped = strip_comments(open(f, encoding="utf-8").read())
        for pattern in HARDCODED_ANNOUNCEMENT_RES:
            if pattern.search(stripped):
                failures.append(
                    f"error RWA-INSPECT: hardcoded English announcement "
                    f"({pattern.pattern}) in {rel} — speak through a Keyed "
                    f"translation string instead.")
    return failures


def check_name_tier_literals():
    failures = []
    for f in all_src_files():
        rel = os.path.relpath(f, REPO).replace(os.sep, "/")
        stripped = strip_comments(open(f, encoding="utf-8").read())
        for m in NAME_TIER_RE.finditer(stripped):
            type_name = m.group(1)
            if (rel, type_name) in NAME_TIER_ALLOWLIST:
                continue
            failures.append(
                f"error RWA-INSPECT: RegisterByTypeName(\"{type_name}\") in "
                f"{rel} — the byType chain shadows name-tier entries once a "
                f"base type registers; use Register(typeof(...)) for public "
                f"types, or add a justified entry to NAME_TIER_ALLOWLIST.")
    return failures


def check_tree_type_name_dispatch():
    failures = []
    for rel in TREE_FILES:
        stripped = read_stripped(rel)
        if TYPE_NAME_DISPATCH_RE.search(stripped):
            failures.append(
                f"error RWA-INSPECT: GetType().Name == dispatch in {rel} — "
                f"resolve by `is`/typeof (all vanilla tab and zone types are "
                f"public in Assembly-CSharp).")
    return failures


def check_gizmo_nav_reflection():
    failures = []
    for rel in GIZMO_NAV_FILES:
        stripped = read_stripped(rel)
        if REFLECTION_RE.search(stripped):
            failures.append(
                f"error RWA-INSPECT: reflection call site in {rel} — the "
                f"navigation state machine holds zero reflection; put "
                f"per-type reflection inside that type's gizmo handler, cached "
                f"once against the declaring type.")
    return failures


def check_tab_registry_collapse():
    stripped = read_stripped(TAB_REGISTRY)
    match = TAB_REGISTRY_RETIRED_RE.search(stripped)
    if match:
        return [
            f"error RWA-INSPECT: retired pattern ({match.group(0)}) in "
            f"{TAB_REGISTRY} — tab resolution is typeof-keyed through "
            f"InspectNodeRegistry; no string dictionaries or "
            f"BaseType walks."]
    return []


def check_category_dispatch():
    failures = []
    targets = [os.path.join(REPO, TREE_BUILDER)]
    targets += glob.glob(os.path.join(REPO, "src/Inspection/Adapters/**/*.cs"),
                         recursive=True)
    for f in targets:
        rel = os.path.relpath(f, REPO).replace(os.sep, "/")
        stripped = strip_comments(open(f, encoding="utf-8").read())
        if CATEGORY_DISPATCH_RE.search(stripped):
            failures.append(
                f"error RWA-INSPECT: category-name dispatch (category == \"...\") "
                f"in {rel} — resolve through InspectNodeRegistry.TryResolveCategory "
                f"and the adapter facets; category keys are registry "
                f"identity, not branch conditions.")
    return failures


def check_localizer_completeness():
    localizer_text = read_stripped(LOCALIZER)
    known = set(LOCALIZER_KEY_RE.findall(localizer_text))
    failures = []
    if not known:
        return [f"error RWA-INSPECT: could not parse any category keys out of "
                f"{LOCALIZER} — the localizer table moved; update this script."]
    for f in all_src_files():
        rel = os.path.relpath(f, REPO).replace(os.sep, "/")
        stripped = strip_comments(open(f, encoding="utf-8").read())
        for name in ORIGINAL_CATEGORY_RE.findall(stripped):
            if name not in known:
                failures.append(
                    f"error RWA-INSPECT: synthetic category \"{name}\" ({rel}) "
                    f"has no entry in InspectionCategoryLocalizer — it would "
                    f"speak raw English in a localized game.")
    return failures


def main():
    failed = False
    for check in (check_fragile_predicates,
                  check_hardcoded_announcements,
                  check_name_tier_literals,
                  check_tree_type_name_dispatch,
                  check_gizmo_nav_reflection,
                  check_tab_registry_collapse,
                  check_category_dispatch,
                  check_localizer_completeness):
        for msg in check():
            print(msg)
            failed = True
    if failed:
        return 1
    print("check_inspection_doctrine: OK — no language-fragile predicates, no "
          "hardcoded announcements, no literal name-tier registrations, no "
          "tree type-name dispatch, no reflection in GizmoNavigationState, "
          "TabRegistry collapse holds, no category-name dispatch, category "
          "localizer complete")
    return 0


if __name__ == "__main__":
    sys.exit(main())
