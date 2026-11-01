#!/usr/bin/env python3
"""Write-side mutation doctrine ratchet (the mutation doctrine in CLAUDE.md).

Game-state mutations must ride a vanilla vehicle: invoke the vanilla
widget/delegate itself (Category A), or a vanilla method that carries the
gate (Category B). Hand-copied gates (Category C) need a MUTATION-C marker;
raw ungated mutation (Category D) is forbidden. This script cannot judge
semantics, so it enforces the doctrine mechanically, four ways:

1. Reflection-write baseline: the per-file count of `.SetValue(` /
   `Traverse` sites (comment-stripped, MUTATION-C-marked lines exempt) is
   frozen. A new site fails the build until it is either rewritten onto a
   vanilla vehicle or justified with a MUTATION-C marker; a removed site
   fails too, demanding the baseline be tightened (that is the ratchet).
   Run with --emit-baseline to print the current counts.

2. Named forbidden mutations: signatures of known cheat-class calls
   (e.g. `Precept_Role.Assign` — vanilla only assigns roles through the
   RoleChange ritual flow) are forbidden outside their frozen allowlist.
   The allowlist shrinks to zero as the repairs land, then stays zero.

3. Cached-but-never-invoked gate handles: a file that caches a `CanAccept`
   MethodInfo must also invoke it. Calling a dialog's `Accept` without the
   `CanAccept` the vanilla button pairs it with is how the xenotype editor
   shipped an illegal-metabolism hole. Known offenders ride a PENDING list
   that must be emptied by the repairs; a fixed file left on the list fails.

4. Hand-picked text-field constraints: `maxLength: <numeric literal>` in a
   TextFieldSpec outside src/Input/TextInput/ is frozen per file. Field
   limits are harvested from the game (call-site args, dialog introspection,
   the game's own static fields), never chosen by hand.

5. Direct (non-reflection) game-state writes: an assignment straight into a
   live Verse/RimWorld object, with no `.SetValue`/`Traverse` indirection to
   trip check #1. This was the ratchet's blind spot -- the audit's worst
   escapes (quest accept, caravan gear) were exactly this shape. Python
   cannot resolve C# types, so detection is two curated, low-false-positive
   patterns rather than type inference:
     a. Any assignment reached through two-or-more dotted hops off a
        `Find.` singleton (`Find.GameInitData.startingTile = ...`). `Find.*`
        singletons are always live game managers, so this needs no
        per-member curation and has near-zero false positives.
     b. A hand-curated list of leaf field/property names (SENSITIVE_MEMBER_NAMES)
        harvested from known Verse/RimWorld types this codebase mutates
        directly through a local variable rather than a `Find.` chain
        (Faction's reward-preference bools, DrugPolicyEntry, AutoSlaughterConfig,
        Difficulty's Anomaly overrides, ...). Grow this list as new areas are
        swept for MUTATION-C coverage.
   A site is exempt if a MUTATION-C marker appears anywhere from its
   enclosing method's header down to the write itself (the doctrine allows
   the marker on "the mutating line or its enclosing member" -- a single
   marker routinely sits above a multi-case switch whose branches write ten
   or more lines below it). The unmarked-site baseline is exact (file +
   member name), not a count, so a rename or move surfaces as both a growth
   and a shrink. Run with --emit-baseline to print candidate baselines.

Usage: check_mutation_doctrine.py [--emit-baseline | --self-test]

--self-test plants a scratch file with two deliberately unmarked sensitive
writes, proves check #5 catches both, then deletes the scratch file.
"""
import glob
import os
import re
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

MARKER = "MUTATION-C"

BLOCK_COMMENT_RE = re.compile(r"/\*.*?\*/", re.DOTALL)
LINE_COMMENT_RE = re.compile(r"//(?!.*" + MARKER + r").*$", re.MULTILINE)
STRING_RE = re.compile(r'"(?:\\.|[^"\\])*"')

SETVALUE_RE = re.compile(r"\.SetValue\(")
TRAVERSE_RE = re.compile(r"\bTraverse\b")
MAXLENGTH_LITERAL_RE = re.compile(r"maxLength:\s*\d")

ASSIGN_OP = r"(?:=(?!=)|\+\+|--|\+=|-=|\|=|&=)"

# Any assignment reached through 2+ dotted hops off a `Find.` singleton.
# Find.* always resolves to a live game manager (GameInitData, GameEnder,
# WorldSelector, TickManager, PlaySettings, ...), so this pattern needs no
# per-member curation.
FIND_CHAIN_WRITE_RE = re.compile(
    r"(Find\.[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)+)\s*" + ASSIGN_OP)

# Leaf field/property names on known Verse/RimWorld types that this codebase
# writes directly through a local variable (not a `Find.` chain), harvested
# by hand from swept areas. Add to this list -- with a citing comment -- as
# new areas are audited; do not remove an entry just because its current
# site(s) are now marked (that keeps the pattern alive for future sites).
SENSITIVE_MEMBER_NAMES = [
    # Faction reward preferences (Dialog_RewardPrefsConfig checkbox refs)
    "allowRoyalFavorRewards",
    "allowGoodwillRewards",
    # BillUtility's copy/paste clipboard (Bill.DoInterface copy button)
    "Clipboard",
    # DrugPolicyEntry (Dialog_ManageDrugPolicies bare-field/slider writes)
    "allowedForAddiction",
    "allowedForJoy",
    "allowScheduled",
    "daysFrequency",
    "onlyIfMoodBelow",
    "onlyIfJoyBelow",
    "takeToInventory",
    # AutoSlaughterConfig (Dialog_AutoSlaughter bare-field/checkbox writes)
    "maxTotal",
    "maxMales",
    "maxMalesYoung",
    "maxFemales",
    "maxFemalesYoung",
    "allowSlaughterPregnant",
    "allowSlaughterBonded",
    # Difficulty's Anomaly overrides (Dialog_AnomalySettings Accept branch)
    "overrideAnomalyThreatsFraction",
    "anomalyThreatsInactiveFraction",
    "anomalyThreatsActiveFraction",
    "studyEfficiencyFactor",
    "AnomalyPlaystyleDef",
    # Quest (MainTabWindow_Quests.DoDismissButton's Historical/dismissed branches)
    "hiddenInUI",
    "dismissed",
    # Pawn_PlayerSettings (PawnColumnWorker_AllowedArea's own bare-field gate)
    "AreaRestrictionInPawnCurrentMap",
    # Bill_Production (Dialog_BillConfig's targetCount IntEntry + its
    # unconditional unpauseWhenYouHave delta-shift; ITab card +/- stepper)
    "targetCount",
    "unpauseWhenYouHave",
    # Pawn_OutfitTracker / Pawn_FoodRestrictionTracker (Assign tab's own
    # per-pawn dropdown assignment, Dialog-free bare-field writes)
    "CurrentApparelPolicy",
    "CurrentFoodPolicy",
    # PawnGenerationRequest (CharacterCardUtility.LifestageAndXenotypeOptions's
    # xenotype branches; no gated setter on the request struct's fields)
    "ForcedXenotype",
    "ForcedCustomXenotype",
    "AllowedXenotypes",
]

SENSITIVE_MEMBER_RE = re.compile(
    r"(?<=[\w\]\)])\.(" +
    "|".join(sorted(SENSITIVE_MEMBER_NAMES, key=len, reverse=True)) +
    r")\b\s*" + ASSIGN_OP)

# A write is exempt if some MUTATION-C marker's justified statement/block is
# still open when the write is reached (see statement_stays_open below). A
# fixed line-count window can't tell "marker sits 25 lines above the write,
# same still-open switch" (legitimate -- this codebase's dominant
# MUTATION-C shape is a comment block above a multi-case switch or above a
# method header, justifying every branch/write below it) apart from "marker
# sits 25 lines above the write, but its OWN statement already closed and
# the write is an unrelated later statement" (not covered -- this is exactly
# how the Difficulty anomalyThreatsActiveFraction drift hid from an earlier,
# window-based version of this check: a marker for a sibling statement 27
# lines above happened to fall inside a 40-line fallback window). Brace
# depth resolves the ambiguity a line count cannot.
MARKER_BACKSCAN_LIMIT = 80

# Named forbidden mutation signatures. Each entry: (name, regex, allowlist)
# where allowlist maps repo-relative path -> exact expected count. Counts
# drop to zero as the doctrine repairs land; entries then stay as tripwires.
FORBIDDEN = [
    ("Precept_Role.Assign outside the ritual flow (vanilla assigns roles "
     "only via Dialog_BeginRitual / RitualOutcomeEffectWorker_RoleChange; "
     "use SocialTabHelper.OpenRoleChangeRitual)",
     re.compile(r"\.Assign\([^)]*addThoughts"),
     {}),
    ("Precept_Role.Unassign outside the ritual flow "
     "(use SocialTabHelper.OpenRoleChangeRitual with a null role)",
     re.compile(r"\.Unassign\([^)]*generateThoughts"),
     {}),
]

# Files that cache a CanAccept MethodInfo without ever invoking it — known
# Category-D holes awaiting repair. Empty since the doctrine repairs landed;
# any new entry means a new hole of the xenotype-metabolism shape.
CANACCEPT_PENDING = set()

CANACCEPT_CACHE_RE = re.compile(r'"CanAccept"')
CANACCEPT_INVOKE_RE = re.compile(r"mi_canAccept\s*\.\s*Invoke")

# Frozen per-file counts. Regenerate a candidate with --emit-baseline, but
# every delta must be reviewed against the mutation doctrine in CLAUDE.md first.
REFLECTION_WRITE_BASELINE = {
    "src/Archonexus/ArchonexusColonyState.cs": 1,
    "src/Archonexus/ArchonexusReformIdeoState.cs": 1,
    "src/Biotech/XenogermState.cs": 8,
    # Tightened 12 -> 11 by the xenotype load-path reconciliation (slice 6):
    # ApplyCustomXenotype retired for vanilla's own Dialog_XenotypeList_Load
    # callback, and the premade callback dropped the name-lock write vanilla
    # never performs.
    "src/Biotech/XenotypeEditorState.cs": 11,
    # XenotypeTreeBuilder's 7 writes moved to XenotypeEditorState (all
    # MUTATION-C-marked there) in the ScreenScope migration
    # migration; the builder is pure tree construction now.
    "src/Biotech/XenotypeTreeBuilder.cs": 0,
    "src/Building/PaintColorHelper.cs": 2,
    "src/Building/PlanColorHelper.cs": 1,
    # Character Editor browser dialogs: 4 selection writes mirroring
    # SZWidgets.ListView's ref-bound row click (AddTrait, ChangeBackstory,
    # AddAbility, ChangeRace kind), the no-blocking-skills checkbox pair
    # mirroring DialogChangeBackstory's inline Widgets.Checkbox + Old-twin
    # diff, and ChangeFaction's inline RadioButton selection. All
    # MUTATION-C-marked; reviewed (S2 review converted the
    # race-specific-dress write to the dialog's own ARedress handler).
    "src/Compat/CharacterEditor/CharEditorBrowserCompat.Game.cs": 7,
    # DialogChangeBirthday: one shared SetInt helper writing the dialog's
    # private pending-value int fields, mirroring the mod's own by-ref
    # threading through Listing_X.AddIntSection (no setter methods exist);
    # values reach the pawn only via the dialog's own DoAndClose.
    # MUTATION-C-marked; reviewed.
    "src/Compat/CharacterEditor/CharEditorBirthdayCompat.Game.cs": 1,
    # DialogObjects (apparel/weapon browser): three filter pairs mirroring the
    # dialog's own inline float-menu delegates (each writes the search field
    # AND rebuilds the def list, 2 sites each), the ListView ref-bound
    # selection, and the quality/stack inline widget writes. All
    # MUTATION-C-marked at their wrappers; reviewed.
    "src/Compat/CharacterEditor/CharEditorObjectsCompat.Game.cs": 9,
    # DialogColorPicker (LIVE-apply, no gated setters anywhere): selectedColor
    # + the dialog's own TextValuesFromSelectedColor apply path, min/max
    # random-brightness floats (max reproduces the draw loop's inline
    # offsetCX/lcolors palette retint -- 2 writes -- since bypassing the draw
    # means that inline code never runs), and the channel radio flag; plus the
    # shared SetBool/SetFloat primitives the wrappers ride. All
    # MUTATION-C-marked at their wrappers; reviewed.
    "src/Compat/CharacterEditor/CharEditorCompat.ColorPicker.Game.cs": 6,
    # DialogViewXenoGenes: the endo/xeno toggle's own inline-delegate flip
    # (ToggleTarget, mirrors the mod's `delegate { bIsXeno = !bIsXeno; }`
    # button body -- no named toggle method exists). MUTATION-C-marked;
    # Reviewed.
    "src/Compat/CharacterEditor/CharEditorXenoGenesCompat.Game.cs": 1,
    # DialogGenery: the base DialogTemplate<T>'s own ListView ref-bound
    # selection write (SetSelected), matching ObjectsAdapter's identical
    # precedent for the same shared base class. MUTATION-C-marked;
    # Reviewed.
    "src/Compat/CharacterEditor/CharEditorGeneryCompat.Game.cs": 1,
    # DialogXenoType's OWN inheritable checkbox (PostXenotypeOnGUI's inline
    # `Widgets.CheckboxLabeled(rect, taggedString, ref inheritable)`) and its
    # own ignoreRestrictionsConfirmationSent one-time-ever marker (a SEPARATE
    # static field from vanilla Dialog_CreateXenotype's copy of the same
    # idiom, since DialogXenoType is a sibling subclass, not a child, of that
    # dialog). Both MUTATION-C-marked; reviewed.
    "src/Compat/CharacterEditor/CharEditorXenoTypeCompat.Game.cs": 2,
    # DialogXenoType's own state facade, mirroring XenotypeEditorState.cs's
    # shape and its own (larger, 12-write) baseline member-for-member: the
    # gene-toggle name-regeneration write, the rename-confirm name+lock pair,
    # the icon-selector write, and the name-lock toggle. Every ignore-
    # restrictions branch is already MUTATION-C-marked inline exactly as the
    # vanilla sibling's own is; these three remaining sites are not (their
    # marker sits at the top of each short helper method, more than the
    # 4-line adjacency window this ratchet's baseline count requires),
    # matching the SAME shape XenotypeEditorState.cs's own 12-count baseline
    # already accepts for its own unmarked-within-4-lines sites. Slice S8,
    # Reviewed.
    "src/Compat/CharacterEditor/CharEditorXenoTypeState.Game.cs": 3,
    # RwomClassCardAdapter part D (write path): learn-ability effect (learned
    # flag + child-abilities loop, magic; the three SuperSoldier sub-skill
    # bool flags, might), ability level-up + global/per-ability skill-spend
    # point deductions, autocast toggle, and the two god-mode LevelUp/
    # ResetSkills invokes. Every site is either a vehicle-B MethodInfo.Invoke
    # (LevelUpPower/LevelUp/ResetSkills/AddPawnAbility) or a MUTATION-C-marked
    # bare field/property mutation mirroring the card's own inline effect
    # (MagicCardUtility/MightCardUtility CustomPowersHandler/CustomSkillHandler/
    # DrawLevelBar — see each handler method's own remarks for its exact
    # source citation). Reviewed at slice D's landing.
    # RimTalkNarrativeCompat.TrySetOverlayEnabled (Dialogue Log's "Toggle chat
    # overlay" button, speech mods campaign S4): one field write mirroring
    # TogglePatch.cs's own plain-click branch verbatim (rimTalkSettings.OverlayEnabled
    # = value; ((ModSettings)rimTalkSettings).Write();) -- RimTalkSettings.OverlayEnabled
    # is a bare public field with no gated setter method, so no A/B vehicle exists
    # beyond reproducing the exact field-then-Write() pair the mod's own gear-dropdown
    # checkbox and PlaySettings toggle both use. MUTATION-C-marked at the write site.
    "src/Compat/RimTalk/RimTalkNarrativeCompat.Game.cs": 1,
    "src/Compat/RWoM/RwomClassCardAdapter.Game.cs": 14,
    # RwomGizmoCompat (autocast/Incite Passion gizmo extension): the
    # MagicPower.AutoCast/MightPower.AutoCast property write (vehicle B, the
    # mod's own debounced setter) and the MUTATION-C-marked incitePassionSkill
    # field write mirroring TM_Action.DrawAutoCastForGizmo's in-draw picker
    # closure (TM_Action.cs:2984-3008, unreachable outside the IMGUI pass).
    # Reviewed at slice D's landing.
    "src/Compat/RWoM/RwomGizmoCompat.Game.cs": 2,
    # RwomGolemCompat (golem overview table columns): no baseline entry needed
    # -- its three writes (the shared slider-column field write serving all
    # three slider columns, and the master-column's two pawnMaster writes)
    # each carry a MUTATION-C marker within the tight 4-line backscan window
    # this ratchet checks, so scan_counts() reports 0 for this file. See each
    # write site's own citation of PawnColumnWorker_GolemThreatRange/
    # GolemRestPercent/GolemAwakenPercent.DoCell or GolemUtility.MasterButton.
    # RwomGolemTabAdapter (golem tab: name/master/toggles/sliders): two vehicle-A
    # window-field seeds mirroring GolemNameWindow/GolemAbilitiesWindow's own
    # construction sites verbatim (ITab_GolemPawn.cs:112-115/163-164 — the
    # window's OWN fields, set before Find.WindowStack.Add, not persistent game
    # state), plus three MUTATION-C-marked bare writes (CompGolem.pawnMaster
    # mirroring GolemUtility.MasterButton's option delegate, the shared
    # checkbox-toggle field flip mirroring FillTab's six CheckboxLabeled rows,
    # and the shared slider-confirm field write mirroring FillTab's four
    # HorizontalSlider rows) — CompGolem/TMPawnGolem expose no gated setter for
    # any of pawnMaster/the six bools/the four floats. Reviewed at slice F's
    # landing.
    "src/Compat/RWoM/RwomGolemTabAdapter.Game.cs": 6,
    "src/Gravships/GravshipPatch.cs": 1,
    "src/History/HistoryHelper.cs": 3,
    "src/IdeoBuilder/IdeoReformState.cs": 1,
    "src/Inspection/Capture/InspectTabCaptureHarness.Game.cs": 3,
    "src/Inspection/GizmoHandlers/MechCarrierGizmoHandler.Game.cs": 1,
    "src/Inspection/GizmoHandlers/PsychicEntropyGizmoHandler.Game.cs": 1,
    # Grown 1 -> 2: an adjusted target must land in Gizmo_Slider's private
    # targetValuePct as well as Target, because GizmoOnGUI copies that field
    # into Target on every rendered frame and would otherwise revert the write
    # within a frame. Vehicle A both times -- targetValuePct is the exact store
    # vanilla's own Widgets.DraggableBar writes, and the Target setter it then
    # runs is vanilla's gated mutator. Same shape as
    # PsychicEntropyGizmoHandler's cached-field write above.
    "src/Inspection/GizmoHandlers/SliderGizmoHandler.Game.cs": 2,
    "src/MainMenu/IdeologySelectionPatch.cs": 1,
    "src/MainMenu/ModListVanillaBridge.cs": 2,
    "src/MainMenu/StartingPawnPatch.cs": 1,
    "src/MainMenu/WandererPatch.cs": 2,
    "src/MainMenu/WorldParamsPageBridge.cs": 7,
    "src/Map/MapNavigationPatch.cs": 10,
    "src/Pawns/LineFormationState.cs": 0,
    "src/Portals/EnterPortalAdapter.cs": 1,
    "src/Rituals/DryadCasteState.cs": 1,
    "src/Rituals/GravshipLaunchAdapter.cs": 1,
    "src/Shell/Screens/CaravanFormationScope.Game.cs": 4,
    "src/Shell/Screens/RenameScope.Game.cs": 2,
    "src/Shell/Screens/ScheduleScope.Game.cs": 1,
    "src/Shell/Screens/SliderDialogState.cs": 1,
    "src/Shell/Screens/SplitCaravanScope.Game.cs": 1,
    "src/Styling/StylingStationHelper.cs": 2,
    "src/TransportPods/LoadTransportersAdapter.cs": 1,
    "src/TransportPods/TransportPodHelper.cs": 1,
}

TEXTFIELD_LITERAL_BASELINE = {
    "src/MainMenu/PawnFilterPresetSaveState.cs": 1,
    "src/Shell/Screens/FileListScope.Game.cs": 1,
    # RimworldTogetherChatScope's message field: 512 mirrors DLG_Chat.DrawInput's own
    # `text.Length <= 512` guard exactly (decompiled-verified), not a hand-picked number.
    "src/Compat/RimworldTogether/RimworldTogetherChatScope.Game.cs": 1,
}

# Frozen per-file lists of unmarked direct game-state writes (file + exact
# member name, not a count -- a rename must surface as both a growth and a
# shrink). These are real gaps awaiting MUTATION-C review, not sanctioned
# exceptions. New unmarked sites (in any file) fail the build; every entry
# here disappearing (because it got marked or rewritten onto a vanilla
# vehicle) must be deleted from this dict in the same change. Emptied by the
# Marker/baseline reconciliation pass -- every site the re-audit
# found FAITHFUL now carries a MUTATION-C marker citing its vanilla vehicle;
# every site it found DRIFTED was fixed in 0376d63/9a3557f/bfe27e1 first.
DIRECT_WRITE_BASELINE = {}


def all_src_files():
    return sorted(glob.glob(os.path.join(REPO, "src/**/*.cs"), recursive=True))


def strip_for_scan(text):
    """Remove block comments, strings, and line comments — but keep line
    comments that carry the MUTATION-C marker so marked sites stay visible
    to the exemption logic."""
    text = BLOCK_COMMENT_RE.sub("", text)
    text = STRING_RE.sub('""', text)
    text = LINE_COMMENT_RE.sub("", text)
    return text


def counted_sites(lines, pattern):
    """Count pattern hits, exempting lines covered by a MUTATION-C marker on
    the same line or within the four preceding lines (multi-line
    justification comments put the marker a few lines above the write)."""
    count = 0
    for i, line in enumerate(lines):
        if not pattern.search(line):
            continue
        window = lines[max(0, i - 4):i + 1]
        if any(MARKER in w for w in window):
            continue
        count += 1
    return count


def scan_counts():
    reflection = {}
    textfield = {}
    for f in all_src_files():
        rel = os.path.relpath(f, REPO).replace(os.sep, "/")
        lines = strip_for_scan(open(f, encoding="utf-8").read()).splitlines()
        n = counted_sites(lines, SETVALUE_RE) + counted_sites(lines, TRAVERSE_RE)
        if n:
            reflection[rel] = n
        if not rel.startswith("src/Input/TextInput/"):
            m = counted_sites(lines, MAXLENGTH_LITERAL_RE)
            if m:
                textfield[rel] = m
    return reflection, textfield


def find_markers(lines):
    """Line indices carrying the literal MUTATION-C token. strip_for_scan
    blanks a marker's continuation comment lines (they don't repeat the
    token), so a multi-line justification comment surfaces as exactly one
    marker index here -- its first line."""
    return [i for i, line in enumerate(lines) if MARKER in line]


def statement_stays_open(lines, attach_idx, write_idx):
    """True if the code starting at attach_idx (a marker's first following
    code line) still justifies write_idx. Two independent closure signals,
    checked together because they catch two different marker shapes:

    1. Brace-only nesting: a marker's coverage ends when a brace block it
       opened (switch/if/method body) closes back to its starting depth.
       Parens are deliberately excluded from THIS signal -- a multi-line
       method header's parameter list closes its own parens on the header
       line itself, which is not a statement boundary, and would falsely
       end coverage for a marker placed above the header (this codebase's
       dominant shape, e.g. AutoSlaughterState.SetLimitForColumn).

    2. Combined (brace+paren+bracket) depth: a top-level ',' -- one at
       exactly the combined depth the attach line started at -- ends
       coverage. This is what a marker placed above ONE named argument in
       an already-open multi-line call looks like from the inside (e.g.
       DifficultySettingsHelper's per-slider markers): the attach line
       itself contributes no brackets, so signal 1 never fires, but the
       trailing ',' closing that argument is a real boundary vanilla
       parity doesn't extend past. This signal is silent for a flat
       ';'-terminated statement sequence with no argument-list commas at
       all (e.g. AnomalySettingsDialogState.Accept), which is exactly the
       other marker shape signal 1 alone must keep open."""
    if attach_idx >= write_idx:
        return True
    brace_level = 0
    entered_brace = False
    combined_level = 0
    for i in range(attach_idx, write_idx):
        for ch in lines[i]:
            if ch == "{":
                brace_level += 1
                entered_brace = True
                combined_level += 1
            elif ch == "}":
                brace_level -= 1
                combined_level -= 1
                if entered_brace and brace_level <= 0:
                    return False
            elif ch in "([":
                combined_level += 1
            elif ch in ")]":
                combined_level -= 1
            elif ch == "," and combined_level <= 0:
                return False
    return True


def marker_covers_write(lines, markers, idx):
    """A write at lines[idx] is covered if some MUTATION-C marker at or
    before idx (within MARKER_BACKSCAN_LIMIT lines) has its justified
    statement/block still open at idx."""
    for m in reversed(markers):
        if m > idx:
            continue
        if idx - m > MARKER_BACKSCAN_LIMIT:
            break
        attach = m
        while attach < len(lines) and (
                lines[attach].strip() == "" or
                MARKER in lines[attach] or
                lines[attach].strip().startswith("//")):
            attach += 1
        if attach > idx:
            continue
        if statement_stays_open(lines, attach, idx):
            return True
    return False


def scan_direct_writes():
    """Per-file sorted list of unmarked direct game-state write sites,
    identified by member name (see check #5 in the module docstring)."""
    result = {}
    for f in all_src_files():
        rel = os.path.relpath(f, REPO).replace(os.sep, "/")
        lines = strip_for_scan(open(f, encoding="utf-8").read()).splitlines()
        markers = find_markers(lines)
        unmarked = set()
        for i, line in enumerate(lines):
            for m in FIND_CHAIN_WRITE_RE.finditer(line):
                if not marker_covers_write(lines, markers, i):
                    unmarked.add(m.group(1))
            for m in SENSITIVE_MEMBER_RE.finditer(line):
                if not marker_covers_write(lines, markers, i):
                    unmarked.add(m.group(1))
        if unmarked:
            result[rel] = sorted(unmarked)
    return result


def diff_member_baseline(kind, actual, baseline, grow_hint, shrink_hint):
    failures = []
    for rel in sorted(set(actual) | set(baseline)):
        a, b = set(actual.get(rel, [])), set(baseline.get(rel, []))
        for member in sorted(a - b):
            failures.append(
                f"error RWA-MUTATION: new unmarked {kind} in {rel}: {member}. "
                f"{grow_hint}")
        for member in sorted(b - a):
            failures.append(
                f"error RWA-MUTATION: {rel} no longer has the baselined "
                f"unmarked {kind} {member} — excellent, now tighten the "
                f"ratchet: {shrink_hint}")
    return failures


def check_direct_write_baseline(actual):
    return diff_member_baseline(
        "direct game-state write", actual, DIRECT_WRITE_BASELINE,
        "Ride a vanilla vehicle (the mutation doctrine in CLAUDE.md) or add a "
        "MUTATION-C marker stating the mirrored vanilla path.",
        "remove it from DIRECT_WRITE_BASELINE in scripts/check_mutation_doctrine.py.")


def diff_baseline(kind, actual, baseline, grow_hint, shrink_hint):
    failures = []
    for rel in sorted(set(actual) | set(baseline)):
        a, b = actual.get(rel, 0), baseline.get(rel, 0)
        if a > b:
            failures.append(
                f"error RWA-MUTATION: {kind} count in {rel} grew {b} -> {a}. "
                f"{grow_hint}")
        elif a < b:
            failures.append(
                f"error RWA-MUTATION: {kind} count in {rel} shrank {b} -> {a} "
                f"— excellent, now tighten the ratchet: {shrink_hint}")
    return failures


def check_reflection_baseline(actual):
    return diff_baseline(
        "reflection-write", actual, REFLECTION_WRITE_BASELINE,
        "Ride a vanilla vehicle (the mutation doctrine in CLAUDE.md) or add a "
        "MUTATION-C marker stating the mirrored vanilla path.",
        "update REFLECTION_WRITE_BASELINE in scripts/check_mutation_doctrine.py.")


def check_textfield_baseline(actual):
    return diff_baseline(
        "hand-picked maxLength literal", actual, TEXTFIELD_LITERAL_BASELINE,
        "Harvest the limit from the game (TextField call-site args, "
        "TextFieldSpec.ForRimWorldDialog, or the dialog's own static fields).",
        "update TEXTFIELD_LITERAL_BASELINE in scripts/check_mutation_doctrine.py.")


def check_forbidden():
    failures = []
    for name, pattern, allow in FORBIDDEN:
        for f in all_src_files():
            rel = os.path.relpath(f, REPO).replace(os.sep, "/")
            stripped = strip_for_scan(open(f, encoding="utf-8").read())
            n = len(pattern.findall(stripped))
            expected = allow.get(rel, 0)
            if n > expected:
                failures.append(
                    f"error RWA-MUTATION: forbidden mutation in {rel} "
                    f"({n} > {expected} allowed): {name}.")
            elif n < expected:
                failures.append(
                    f"error RWA-MUTATION: {rel} no longer contains the "
                    f"allowlisted call ({name}) — remove/shrink its entry in "
                    f"FORBIDDEN so it can never come back.")
    return failures


def check_canaccept_pairing():
    failures = []
    for f in all_src_files():
        rel = os.path.relpath(f, REPO).replace(os.sep, "/")
        raw = open(f, encoding="utf-8").read()
        text = BLOCK_COMMENT_RE.sub("", raw)
        caches = CANACCEPT_CACHE_RE.search(text) is not None
        invokes = CANACCEPT_INVOKE_RE.search(text) is not None
        if caches and not invokes:
            if rel in CANACCEPT_PENDING:
                continue
            failures.append(
                f"error RWA-MUTATION: {rel} caches a CanAccept handle but "
                f"never invokes it — calling Accept without the CanAccept the "
                f"vanilla button pairs with it is the xenotype-metabolism "
                f"hole. Gate the commit on CanAccept.")
        elif rel in CANACCEPT_PENDING and (not caches or invokes):
            failures.append(
                f"error RWA-MUTATION: {rel} is fixed (or no longer caches "
                f"CanAccept) — remove it from CANACCEPT_PENDING so the hole "
                f"can never reopen.")
    return failures


SELF_TEST_PATH = os.path.join(REPO, "src", "_SelfTestMutationRatchet.cs")

SELF_TEST_SOURCE = """namespace RimWorldAccess
{
    public static class _SelfTestMutationRatchet
    {
        // Deliberately unmarked (no doctrine marker of any kind nearby) --
        // this file exists only for check_mutation_doctrine.py --self-test
        // and is deleted immediately after the run.
        public static void UnmarkedWrite(RimWorld.Faction faction)
        {
            faction.allowRoyalFavorRewards = true;
            Verse.Find.GameEnder.gameEnding = true;
        }
    }
}
"""


def self_test():
    """Proves check #5 actually catches a violation: plant an unmarked write
    to an already-curated sensitive member (allowRoyalFavorRewards) and to a
    fresh Find.* chain (Find.GameEnder.gameEnding, already in the real
    baseline for WandererPatch.cs but not for this scratch file) in a scratch
    file inside src/, confirm both are reported as new (not in
    DIRECT_WRITE_BASELINE, since this file was never baselined), then delete
    the scratch file and confirm the tree is clean again."""
    if os.path.exists(SELF_TEST_PATH):
        os.remove(SELF_TEST_PATH)
    try:
        with open(SELF_TEST_PATH, "w", encoding="utf-8") as fh:
            fh.write(SELF_TEST_SOURCE)
        _, _ = scan_counts()
        direct_writes = scan_direct_writes()
        failures = check_direct_write_baseline(direct_writes)
        rel = "src/_SelfTestMutationRatchet.cs"
        caught = [m for m in failures if rel in m]
        print(f"--self-test: planted {rel} with two unmarked writes "
              f"(allowRoyalFavorRewards, Find.GameEnder.gameEnding).")
        if len(caught) == 2:
            print("--self-test: PASS -- ratchet caught both as new "
                  "unmarked direct game-state writes:")
            for m in caught:
                print(f"  {m}")
            result = 0
        else:
            print(f"--self-test: FAIL -- expected 2 catches, got "
                  f"{len(caught)}. Full failure list:")
            for m in failures:
                print(f"  {m}")
            result = 1
    finally:
        if os.path.exists(SELF_TEST_PATH):
            os.remove(SELF_TEST_PATH)
        still_there = os.path.exists(SELF_TEST_PATH)
        print(f"--self-test: scratch file removed, tree clean "
              f"(exists={still_there}).")
    return result


def main():
    argv = sys.argv[1:]
    if "--self-test" in argv:
        return self_test()
    reflection, textfield = scan_counts()
    direct_writes = scan_direct_writes()
    if "--emit-baseline" in argv:
        print("REFLECTION_WRITE_BASELINE = {")
        for rel in sorted(reflection):
            print(f'    "{rel}": {reflection[rel]},')
        print("}\n")
        print("TEXTFIELD_LITERAL_BASELINE = {")
        for rel in sorted(textfield):
            print(f'    "{rel}": {textfield[rel]},')
        print("}\n")
        print("DIRECT_WRITE_BASELINE = {")
        for rel in sorted(direct_writes):
            members = ", ".join(f'"{m}"' for m in direct_writes[rel])
            print(f'    "{rel}": [{members}],')
        print("}")
        return 0

    failed = False
    for msgs in (check_reflection_baseline(reflection),
                 check_textfield_baseline(textfield),
                 check_forbidden(),
                 check_canaccept_pairing(),
                 check_direct_write_baseline(direct_writes)):
        for msg in msgs:
            print(msg)
            failed = True
    if failed:
        return 1
    print("check_mutation_doctrine: OK — reflection-write baseline holds, no "
          "hand-picked text-field limits beyond baseline, no forbidden "
          "mutation signatures beyond allowlist, every cached CanAccept is "
          "invoked (or tracked pending repair), no unmarked direct "
          "game-state writes beyond baseline")
    return 0


if __name__ == "__main__":
    sys.exit(main())
