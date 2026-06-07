#!/usr/bin/env python3
"""
Localization guardrail (roadmap D.8).

The compiler already blocks the most direct leak: TolkHelper.Speak(string) was
deleted in Phase F.4, so a raw Speak("english") is a hard build error. This lint
covers the SECOND class the compiler cannot see — player-facing English that is
returned from a method, assigned to a display field, interpolated, or
concatenated onto a .Translate() result, and only spoken/shown indirectly.

It scans src/**/*.cs for those patterns and fails (exit 1) on any finding that
is not explicitly accounted for, either by:
  * an inline  // l10n-exempt: <reason>  marker on the same line, or
  * an entry in scripts/localization_baseline.txt  (one "relpath\tstring" per
    line; the string is matched exactly, so the baseline survives line shifts).

Run:  python3 scripts/check_localization.py
      python3 scripts/check_localization.py --update-baseline   (re-bless current
                                                                 findings; use
                                                                 only after a
                                                                 deliberate review)
"""

import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC_DIR = os.path.join(REPO_ROOT, "src")
BASELINE_PATH = os.path.join(REPO_ROOT, "scripts", "localization_baseline.txt")

# Fields whose value is shown to the player. Assigning an English literal to one
# of these is a leak unless the value is a stable dispatch token (see SKIP).
DISPLAY_FIELDS = (
    "Label", "Tooltip", "Title", "Header", "Hint", "Message", "Announcement",
    "Reason", "DisabledReason", "CantEquipReason", "SuitabilityLine",
    "ExtraInfoLine", "defaultLabel", "defaultDesc", "Description",
)

ASSIGN_RE = re.compile(
    r"\b(?:" + "|".join(DISPLAY_FIELDS) + r")\s*=\s*(\$?@?\")((?:[^\"\\]|\\.)*)\""
)
RETURN_RE = re.compile(r"\breturn\s+(\$?@?\")((?:[^\"\\]|\\.)*)\"")
SPEAK_RE = re.compile(r"\bSpeak(?:Data)?\(\s*(\$?@?\")((?:[^\"\\]|\\.)*)\"")
# A string literal concatenated onto the same expression as a .Translate()/.Loc()
# result. Only a leak when the literal is English PROSE (raw words glued onto a
# localized fragment) — joining with punctuation/separators (". ", " - ", "(")
# is legitimate, so the prose test below filters those out.
HAS_LOC_CALL_RE = re.compile(r"\.(?:Translate|Loc)\s*\(")
LITERAL_RE = re.compile(r"\"((?:[^\"\\]|\\.)*)\"")

# A captured literal looks like player-facing prose when it has letters AND a
# space (multi-word). Single-word labels are usually either already localized or
# stable dispatch tokens; the baseline handles the rare real ones.
PROSE_RE = re.compile(r"[A-Za-z].*\s.*[A-Za-z]")


def looks_like_prose(text):
    if not text:
        return False
    if "RimWorldAccess." in text:          # a translation key, not prose
        return False
    if text.strip().startswith(("UI/", "Textures/", "Sounds/")):  # asset path
        return False
    # Strip interpolation holes so placeholder identifiers ({pawn.LabelCap},
    # {string.Join(...}) don't masquerade as English. Remove balanced {...}
    # first, then drop any trailing unbalanced hole left by a nested quote.
    bare = re.sub(r"\{[^{}]*\}", "", text)
    bare = re.sub(r"\{.*$", "", bare)
    # Code tokens left in the BARE text (after holes are gone) mean the capture
    # was a mis-sliced interpolation fragment, not a prose literal.
    if re.search(r"\.Translate|\.Loc\(|\(\)|=>|[a-z]\.[A-Za-z]", bare):
        return False
    # Real prose = two letter-runs separated by whitespace (a multi-word phrase).
    return bool(PROSE_RE.search(bare))


def load_baseline():
    pairs = set()
    if os.path.exists(BASELINE_PATH):
        with open(BASELINE_PATH, encoding="utf-8") as fh:
            for line in fh:
                line = line.rstrip("\n")
                if not line or line.startswith("#"):
                    continue
                if "\t" in line:
                    rel, text = line.split("\t", 1)
                    pairs.add((rel, text))
    return pairs


def scan():
    findings = []  # (relpath, lineno, kind, text)
    for root, _dirs, files in os.walk(SRC_DIR):
        for name in files:
            if not name.endswith(".cs"):
                continue
            path = os.path.join(root, name)
            rel = os.path.relpath(path, REPO_ROOT)
            with open(path, encoding="utf-8") as fh:
                for lineno, raw in enumerate(fh, 1):
                    stripped = raw.lstrip()
                    if stripped.startswith("//") or stripped.startswith("*"):
                        continue
                    if "l10n-exempt" in raw:          # inline, reviewed exemption
                        continue
                    for kind, regex in (("assign", ASSIGN_RE),
                                        ("return", RETURN_RE),
                                        ("speak", SPEAK_RE)):
                        for m in regex.finditer(raw):
                            text = m.group(2)
                            if looks_like_prose(text):
                                findings.append((rel, lineno, kind, text))
                    # Raw English words glued onto a localized fragment.
                    if "+" in raw and HAS_LOC_CALL_RE.search(raw):
                        for lm in LITERAL_RE.finditer(raw):
                            if looks_like_prose(lm.group(1)):
                                findings.append((rel, lineno, "concat", lm.group(1)))
                                break
    return findings


def main():
    update = "--update-baseline" in sys.argv
    baseline = load_baseline()
    findings = scan()

    if update:
        os.makedirs(os.path.dirname(BASELINE_PATH), exist_ok=True)
        lines = sorted({f"{rel}\t{text}" for rel, _ln, _kind, text in findings})
        with open(BASELINE_PATH, "w", encoding="utf-8") as fh:
            fh.write("# Localization guardrail baseline — reviewed, intentional "
                     "English.\n# Format: <relpath>\\t<exact string>. "
                     "Regenerate with --update-baseline only after review.\n")
            for line in lines:
                fh.write(line + "\n")
        print(f"Baseline updated: {len(lines)} entries.")
        return 0

    unexpected = [f for f in findings if (f[0], f[3]) not in baseline]

    if not unexpected:
        print("Localization guardrail: PASS — no un-accounted player-facing "
              "English found.")
        return 0

    print("Localization guardrail: FAIL\n")
    print("Player-facing English that is not localized and not in the baseline.")
    print("Fix it (author a translation key), or — if it is genuinely a stable")
    print("dispatch token / dev string — add `// l10n-exempt: <reason>` on the")
    print("line, or add it to scripts/localization_baseline.txt.\n")
    for rel, lineno, kind, text in sorted(unexpected):
        snippet = text if len(text) < 100 else text[:97] + "..."
        print(f"  {rel}:{lineno}  [{kind}]  {snippet}")
    print(f"\n{len(unexpected)} finding(s).")
    return 1


if __name__ == "__main__":
    sys.exit(main())
