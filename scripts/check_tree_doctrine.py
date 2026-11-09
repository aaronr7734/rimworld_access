#!/usr/bin/env python3
"""Hand-rolled-tree ratchet for RimWorld Access.

A class that writes `ElementDescription.Expanded` is presenting a tree. Trees
must ride the shared machinery — a `TreeRegionScope`/`FilterTreeScopeBase`
subclass, or a composed `TreeModel` (via `TreeNavigationHelper`, `ITreeShape`,
`GeneTreeRegion`, `IdeoDetailsTreeRegion`) — so shell-wide tree behavior
(submenu mode, sibling jumps, expansion carry, typeahead auto-expansion)
applies everywhere at once. The scenario editor shipped a private row list with
its own expand flags and silently ignored the submenu setting; this ratchet
exists so that cannot recur.

Detection is class-level across partials: every file whose top-level class
matches is unioned before the tree-host markers are checked, since a partial
may declare the base class in a sibling file.

TREE_EXEMPT lists classes that legitimately present Expanded WITHOUT owning a
tree: they mirror expansion state that lives in a foreign object — a vanilla
window's own flags, a mod's own selected-row state, or another window's
captured rows — where forking the state into our own TreeModel would break
visual parity, plus describe-helpers feeding a shared-tree host the per-class
union cannot see. Each entry carries the owning object as its reason; a new
entry must name one.

TREE_BASELINE grandfathers true offenders (our-side expansion state outside
the shared tree) and is shrink-only: an entry that no longer matches is itself
a failure. It is EMPTY — keep it that way; migrate instead of adding.

Usage: check_tree_doctrine.py
"""
import glob
import os
import re
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# Writing ElementDescription.Expanded (initializer or property form), never
# InspectionTreeItem.IsExpanded (tree builders legitimately set that).
EXPANDED_WRITE = re.compile(r"(?<!Is)\bExpanded\s*=(?!=)")

TREE_HOST_MARKERS = (
    ": TreeRegionScope",
    ": FilterTreeScopeBase",
    ": InspectionScope",
    "TreeModel<",
    "TreeNavigationHelper",
    "ITreeShape",
    "InspectionTreeItemShape",
    "GeneTreeRegion",
    "IdeoDetailsTreeRegion",
)

TOP_CLASS = re.compile(r"^\s{0,8}(?:\[[^\]]*\]\s*)*(?:public|internal|sealed|abstract|static|partial|\s)*class\s+(\w+)", re.MULTILINE)

# Legitimate Expanded presenters: the expansion state lives in a FOREIGN object
# they mirror, or they only describe rows for a shared-tree host.
TREE_EXEMPT = {
    "CmrLogsDetails": "mirrors Colony Manager's own SelectedLog per tab; SetSelectedLog is the toggle",
    "CmrManagerScope": "mirrors Colony Manager's own per-row expansion via live Func reads",
    "CmrProductionDetails": "mirrors Colony Manager's own _collapsedWorkbenchGroups (Production Available list)",
    "DevDebugScope": "drill-down pager over vanilla's own DebugActionNode location; no our-side state",
    "FishingZoneMenuState": "describe helper for FishingZoneScope, a TreeRegionScope",
    "GenericWindowScope": "mirrors captured foreign windows' collapsible rows; no stable model exists",
}

# True offenders, grandfathered shrink-only. EMPTY — keep it that way.
TREE_BASELINE = {}


def main():
    files = glob.glob(os.path.join(REPO, "src", "**", "*.cs"), recursive=True)
    class_text = {}
    class_files = {}
    for path in sorted(files):
        with open(path, encoding="utf-8") as f:
            text = f.read()
        m = TOP_CLASS.search(text)
        owner = m.group(1) if m else os.path.basename(path)
        class_text[owner] = class_text.get(owner, "") + "\n" + text
        class_files.setdefault(owner, []).append(os.path.relpath(path, REPO))

    offenders = {}
    for owner, text in class_text.items():
        if not EXPANDED_WRITE.search(text):
            continue
        if any(marker in text for marker in TREE_HOST_MARKERS):
            continue
        offenders[owner] = class_files[owner]

    errors = []
    for owner in sorted(offenders):
        if owner not in TREE_BASELINE and owner not in TREE_EXEMPT:
            errors.append(
                f"error RWA-TREE: {owner} ({', '.join(offenders[owner])}) presents "
                "Expanded state without the shared tree machinery. Host it on "
                "TreeRegionScope/FilterTreeScopeBase or a composed TreeModel — or, "
                "if the expansion state lives in a foreign object it mirrors, add a "
                "TREE_EXEMPT entry naming that object.")
    for owner in sorted((set(TREE_BASELINE) | set(TREE_EXEMPT)) - set(offenders)):
        errors.append(
            f"error RWA-TREE: entry '{owner}' no longer matches — delete its line "
            "so the lists stay exact.")

    if errors:
        print("\n".join(errors))
        return 1
    print(f"check_tree_doctrine: OK — every Expanded presenter rides the shared tree "
          f"or mirrors a named foreign owner ({len(TREE_EXEMPT)} exempt, "
          f"{len(TREE_BASELINE)} baselined)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
