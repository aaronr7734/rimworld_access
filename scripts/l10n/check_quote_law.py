#!/usr/bin/env python3
"""Quote-law checker: our docs quote resolved game labels, so the quotes must
resolve in every language.

Our documentation tells a player which words to type and which labels to look
for. When a translated doc still quotes the English typeahead word, or quotes a
label the game does not ship in that language, the instruction is actively wrong
— the player types a word the architect menu never matches and concludes the
feature is broken. This has shipped: ChineseSimplified told players to type
'stock', 'grow' and 'wall'; Russian said to type a growing-zone word that is not
a prefix of the Russian label.

The check asserts CONTAINMENT of a resolved label in our translated value, not
extraction of quotes out of it. Quote characters in our translations are not a
reliable signal: French uses the ASCII apostrophe for both quoting ('agri') and
elision (l'onglet), and Turkish attaches case suffixes with one (Tab'a, Enter'a).
Quote extraction runs for exactly one strategy — truncated typeahead fragments,
where the doc deliberately quotes a substring of the label and only one valid
candidate is needed, so surrounding garbage is harmless.

scripts/l10n/quote_manifest.json maps each quoted span in the English docs to the
key that must resolve behind it. Extend it via --audit-english when adding a
documentation chapter that quotes a label.

Usage: check_quote_law.py [--lang <Folder>] [--all] [--manifest <path>]
                          [--languages-dir <dir>] [--audit-english] [--verbose]
Exit codes: 0 no failures, 1 at least one FAIL/UNRESOLVED/NOVALUE, 2 usage error
or an unparseable manifest.
"""
import argparse
import json
import os
import pathlib
import re
import sys
import unicodedata
import xml.etree.ElementTree as ET

REPO = pathlib.Path(__file__).resolve().parents[2]
MODULES = ("Core", "Royalty", "Ideology", "Biotech", "Anomaly", "Odyssey")
def _default_data_root():
    """Default game data directory per platform, mirroring rimworld_access.csproj.
    Override with the RIMWORLD_DATA environment variable."""
    home = pathlib.Path.home()
    if sys.platform == "darwin":
        base = home / "Library/Application Support/Steam/steamapps/common/RimWorld"
        if not (base / "RimWorld.app").is_dir():
            base = base / "RimWorldMac.app"
    elif sys.platform.startswith("linux"):
        base = home / ".steam/steam/steamapps/common/RimWorld"
    else:
        base = pathlib.Path(r"C:\Program Files (x86)\Steam\steamapps\common\RimWorld")
    for name in ("data", "Data"):
        if (base / name).is_dir():
            return str(base / name)
    return str(base / "data")


DEFAULT_DATA = _default_data_root()
DEFAULT_CORPUS_ROOT = pathlib.Path.home() / ".cache/rimworld-access/corpus"

FAILURE_KINDS = ("FAIL", "UNRESOLVED", "NOVALUE")

PAIRED = re.compile(r'[«“‹「『]([^«»“”‹›「」『』]{2,60}?)[»”›」』]')
# Latin + Latin Extended + Greek + Cyrillic. CJK is deliberately NOT a "letter"
# here so 「stock」 and "stock" both extract when flanked by Han characters.
LETTER = r'A-Za-zÀ-ɏͰ-ϿЀ-ӿ'
ASCIIQ = re.compile(rf'(?<![{LETTER}])["\']([^"\'\n]{{2,60}}?)["\'](?![{LETTER}])')


def quoted_spans(value):
    """Quoted spans in a value. Unambiguous paired quotes match unconditionally;
    ambiguous ASCII quotes require both edges to be non-letter-adjacent, which
    is what defeats French elision, Turkish suffixes and English possessives."""
    return ([m.group(1) for m in PAIRED.finditer(value)]
            + [m.group(1) for m in ASCIIQ.finditer(value)])


def fold(s):
    return unicodedata.normalize("NFC", s).casefold()


def contains(needle, value, case_sensitive=False):
    if case_sensitive:
        return needle in value
    return fold(needle) in fold(value)


def any_word(label, value):
    """Words of the label present in the value; CJK gets a shorter minimum.

    A composed example fills a placeholder with a short form of the label
    ("Sembrar: arroz" for "planta de arroz"), so demanding the whole label would
    fail every language.
    """
    minlen = 2 if any(ord(c) > 0x2E80 for c in label) else 3
    words = [w for w in label.split() if len(w) >= minlen] or [label.strip()]
    return [w for w in words if fold(w) in fold(value)]


def display(path):
    """Repo-relative when it is in the repo; fixture paths stay absolute rather
    than becoming a ../.. chain out of the worktree."""
    try:
        return str(pathlib.Path(path).resolve().relative_to(REPO))
    except ValueError:
        return str(path)


def parse_or_skip(path):
    try:
        return list(ET.parse(path).getroot())
    except ET.ParseError as e:
        print(f"  warning: unparseable XML skipped: {path}: {e}")
        return []


def load_corpus(root):
    """Vanilla Keyed and DefInjected values, first-wins in module order so Core
    is the authority when a key appears in more than one module."""
    keyed, definjected = {}, {}
    for module in MODULES:
        for f in sorted((root / module).glob("Keyed/*.xml")):
            for c in parse_or_skip(f):
                keyed.setdefault(c.tag, "".join(c.itertext()))
        for f in sorted((root / module).rglob("DefInjected/**/*.xml")):
            for c in parse_or_skip(f):
                definjected.setdefault(c.tag, "".join(c.itertext()))
    return keyed, definjected


def load_flat(paths):
    values = {}
    for f in paths:
        for c in parse_or_skip(f):
            values.setdefault(c.tag, "".join(c.itertext()))
    return values


class Resolver:
    def __init__(self, keyed, definjected, ours):
        self.keyed = keyed
        self.definjected = definjected
        self.ours = ours

    def vanilla(self, key, space):
        """Vanilla value for a key. Without an explicit space, Keyed wins over
        DefInjected — the manifest's bare keys are Keyed strings, and a def label
        of the same name would be a different string."""
        if space == "keyed":
            return self.keyed.get(key)
        if space == "definjected":
            return self.definjected.get(key)
        found = self.keyed.get(key)
        return found if found is not None else self.definjected.get(key)


def literal_prefix(value):
    """Text before the first placeholder. Empty when the label is a bare {0}."""
    return value.split("{")[0].strip()


def literal_suffix(value):
    return value.rsplit("}", 1)[-1].strip()


def check_entry(entry, lang, value, resolver):
    """One entry against one language's translated value.

    Returns (kind, [detail lines]). Kinds: PASS, SKIP, NOPREFIX (warning),
    FAIL / UNRESOLVED (failures).
    """
    over = (entry.get("overrides") or {}).get(lang, {})
    if "skip" in over:
        return "SKIP", [over["skip"]]

    strategy = entry["strategy"]
    exact = entry.get("case_sensitive", False)
    key = over.get("key", entry.get("key"))
    space = entry.get("space")

    if "expect" in over:
        expected = over["expect"]
        if contains(expected, value, exact):
            return "PASS", []
        return "FAIL", [f"expected override {expected!r} — not present in the "
                        f"translated value"]

    if strategy in ("label", "fragment", "prefix"):
        resolved = resolver.vanilla(key, space)
        if resolved is None:
            return "UNRESOLVED", [f"{key} resolves to nothing in the {lang} "
                                  f"corpus — manifest bug, or the key is "
                                  f"DLC-gated and needs an overrides entry"]
        if strategy == "label":
            target = resolved.strip()
            if contains(target, value, exact):
                return "PASS", []
            return "FAIL", [f"label {target!r} ({key}) — not present in the "
                            f"translated value"]
        if strategy == "fragment":
            spans = [s.strip() for s in quoted_spans(value)]
            spans = [s for s in spans if len(s) >= 2]
            if any(contains(s, resolved, exact) for s in spans):
                return "PASS", []
            return "FAIL", [f"label {resolved.strip()!r} ({key}) — none of the "
                            f"quoted spans {spans} is a substring"]
        prefix = literal_prefix(resolved)
        if not prefix:
            return "NOPREFIX", [f"{key}={resolved!r} — no literal prefix in "
                                f"{lang}; the doc must describe the gizmo "
                                f"rather than quote a prefix"]
        if contains(prefix, value, exact):
            return "PASS", []
        return "FAIL", [f"prefix {prefix!r} of {key}={resolved!r} — not present "
                        f"in the translated value"]

    if strategy in ("self", "self_suffix"):
        resolved = resolver.ours.get(key)
        if resolved is None:
            return "UNRESOLVED", [f"{key} is not in Languages/{lang}/Keyed — "
                                  f"our own key is missing in this language"]
        if strategy == "self":
            target = resolved.strip()
            if contains(target, value, exact):
                return "PASS", []
            return "FAIL", [f"our own {key}={target!r} — not present in the "
                            f"translated value (doc and key have drifted apart)"]
        suffix = literal_suffix(resolved)
        if not suffix:
            return "NOPREFIX", [f"{key}={resolved!r} — no literal text after "
                                f"the placeholder in {lang}; nothing to assert"]
        if contains(suffix, value, exact):
            return "PASS", []
        return "FAIL", [f"suffix {suffix!r} of our own {key}={resolved!r} — not "
                        f"present in the translated value"]

    if strategy == "parts":
        missing = []
        for part in entry["parts"]:
            if "literal" in part:
                if not contains(part["literal"], value, exact):
                    missing.append(f"literal {part['literal']!r} is absent")
                continue
            part_key = part.get("key") or part["prefix_of"]
            resolved = resolver.vanilla(part_key, part.get("space"))
            if resolved is None:
                return "UNRESOLVED", [f"{part_key} resolves to nothing in the "
                                      f"{lang} corpus — manifest bug"]
            if "prefix_of" in part:
                prefix = literal_prefix(resolved)
                # A language whose label is a bare {0} has no prefix to assert.
                if prefix and not contains(prefix, value, exact):
                    missing.append(f"prefix {prefix!r} of {part_key}="
                                   f"{resolved!r} is absent")
            elif not any_word(resolved, value):
                missing.append(f"no word of {part_key}={resolved.strip()!r} "
                               f"appears")
        if missing:
            return "FAIL", missing
        return "PASS", []

    return "UNRESOLVED", [f"unknown strategy {strategy!r}"]


def report(kind, entry, details):
    print(f"  {kind} {entry['id']}  {entry['field']}")
    for line in details:
        print(f"      {line}")


def check_language(lang, manifest, languages_dir, corpus_root, verbose):
    """Returns (passes, failures, warnings, skipped_no_corpus)."""
    print(f"=== {lang} ===")
    entries = manifest["entries"]
    sources = manifest["sources"]
    lang_dir = languages_dir / lang
    if not lang_dir.is_dir():
        # A misspelled language must fail, never read as "no findings".
        print(f"  FAIL {lang}: no such language under {languages_dir}")
        return 0, 1, 0, 0

    corpus_dir = corpus_root / lang
    has_corpus = (corpus_dir / ".complete").is_file()
    if not has_corpus:
        print(f"  SKIPPED {lang}: no corpus "
              f"(run scripts/l10n/extract_corpus.sh {lang})")
        keyed, definjected = {}, {}
    else:
        keyed, definjected = load_corpus(corpus_dir)

    ours = load_flat(sorted((lang_dir / "Keyed").glob("*.xml")))
    resolver = Resolver(keyed, definjected, ours)

    docs = {}
    for name, paths in sources.items():
        path = lang_dir / paths["translated"]
        if not path.is_file():
            print(f"  SKIPPED {name}: no translated file at {display(path)}")
            docs[name] = None
        else:
            docs[name] = load_flat([path])

    passes = failures = warnings = skipped_corpus = skipped_file = 0
    for entry in entries:
        doc = docs.get(entry["file"])
        if doc is None:
            skipped_file += 1
            continue
        if not has_corpus and entry["strategy"] not in ("self", "self_suffix"):
            skipped_corpus += 1
            continue
        value = doc.get(entry["field"])
        if value is None:
            report("NOVALUE", entry,
                   [f"the field is absent from the {lang} translated file"])
            failures += 1
            continue
        kind, details = check_entry(entry, lang, value, resolver)
        if kind == "PASS":
            passes += 1
            if verbose:
                report(kind, entry, details)
            continue
        report(kind, entry, details)
        if kind in FAILURE_KINDS:
            failures += 1
        elif kind == "NOPREFIX":
            warnings += 1

    parts = [f"{'FAIL' if failures else 'PASS'} {passes}/{len(entries)}"]
    if failures:
        parts.append(f"{failures} failure(s)")
    if warnings:
        parts.append(f"{warnings} warning" + ("s" if warnings != 1 else ""))
    if skipped_corpus:
        parts.append(f"{skipped_corpus} skipped (no corpus)")
    if skipped_file:
        parts.append(f"{skipped_file} skipped (no translated file)")
    print("  " + ", ".join(parts))
    print()
    return passes, failures, warnings, skipped_corpus


def audit_english(manifest, repo_root):
    """Seeding audit: diff the quoted spans in the English defs against the
    manifest. Reads no corpus. Always exits 0 — the audit is a report."""
    sources = manifest["sources"]
    literals = {}
    for item in manifest.get("literals", []):
        if isinstance(item, str):
            literals[item] = ""
        else:
            literals[item["span"]] = item.get("reason", "")

    by_field = {}
    for entry in manifest["entries"]:
        by_field.setdefault((entry["file"], entry["field"]), []).append(entry)

    found = {}
    for name, paths in sources.items():
        path = repo_root / paths["english"]
        if not path.is_file():
            print(f"SKIPPED {name}: no English def file at {path}")
            continue
        for d in parse_or_skip(path):
            def_name = d.findtext("defName")
            if not def_name:
                continue
            for field in ("label", "helpText"):
                text = d.findtext(field)
                if not text:
                    continue
                spans = quoted_spans(text)
                if spans:
                    found[(name, f"{def_name}.{field}")] = spans

    covered, uncovered = [], []
    for (name, field), spans in sorted(found.items()):
        entries = by_field.get((name, field), [])
        for span in spans:
            match = next((e for e in entries if e["en"] == span), None)
            if match:
                covered.append((field, span, match))
            elif span not in literals:
                uncovered.append((field, span))

    stale = []
    for (name, field), entries in sorted(by_field.items()):
        spans = found.get((name, field), [])
        for entry in entries:
            if entry["en"] not in spans:
                stale.append((field, entry))

    print(f"audit: {len(covered)} covered, {len(uncovered)} uncovered, "
          f"{len(stale)} stale")
    print()
    print("Covered:")
    for field, span, entry in covered:
        print(f"  {field}  {span!r} -> {entry['id']} ({entry['strategy']})")
    print()
    print("Uncovered:")
    for field, span in uncovered:
        print(f"  {field}  {span!r}")
        print("      classify by hand as one of:")
        print("      vanilla quote — grep the game's English Keyed and "
              "DefInjected for the exact span, then pick label / fragment / "
              "prefix / parts by whether the doc quotes the whole label, a "
              "truncation, a placeholder prefix, or a composed example")
        print("      self quote — grep Languages/English/Keyed/*.xml for the "
              "span; use self or self_suffix")
        print("      literal — not a label at all; add it to the manifest's "
              "\"literals\" list with a reason ('(formation)' looks literal "
              "and is not)")
    print()
    print("Stale:")
    for field, entry in stale:
        print(f"  {field}  {entry['en']!r} ({entry['id']}) — no longer quoted "
              f"in the English def; the entry needs review or deletion")
    return 0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--lang", action="append", default=None,
                    help="one language folder; repeatable")
    ap.add_argument("--all", action="store_true",
                    help="every shipped language except English (the default)")
    ap.add_argument("--manifest", default=str(REPO / "scripts/l10n/quote_manifest.json"))
    ap.add_argument("--languages-dir", default=str(REPO / "Languages"),
                    help="its parent also locates the English Defs/ that "
                         "--audit-english reads")
    ap.add_argument("--audit-english", action="store_true",
                    help="report manifest coverage of the English defs instead "
                         "of checking translations")
    ap.add_argument("--verbose", action="store_true", help="print PASS lines too")
    args = ap.parse_args()

    try:
        with open(args.manifest, encoding="utf-8") as f:
            manifest = json.load(f)
    except (OSError, ValueError) as e:
        print(f"check_quote_law: cannot read manifest {args.manifest}: {e}")
        return 2

    languages_dir = pathlib.Path(args.languages_dir)
    if args.audit_english:
        return audit_english(manifest, languages_dir.parent)

    if args.lang:
        langs = [l for l in args.lang if l != "English"]
        if len(langs) != len(args.lang):
            print("check_quote_law: skipping English — it has no DefInjected; "
                  "its quoted spans are the manifest's own 'en' values")
    else:
        if not languages_dir.is_dir():
            print(f"check_quote_law: no languages dir at {languages_dir}")
            return 2
        langs = sorted(d.name for d in languages_dir.iterdir()
                       if d.is_dir() and d.name != "English")
    if not langs:
        print("check_quote_law: no languages to check")
        return 2

    corpus_root = pathlib.Path(os.environ.get("RWA_CORPUS_ROOT",
                                              DEFAULT_CORPUS_ROOT))
    data_root = os.environ.get("RIMWORLD_DATA", DEFAULT_DATA)

    print(f"quote law: {len(langs)} language(s), "
          f"{len(manifest['entries'])} manifest entries")
    print()

    total_failures = total_warnings = total_skipped = 0
    failing = []
    for lang in langs:
        passes, failures, warnings, skipped = check_language(
            lang, manifest, languages_dir, corpus_root, args.verbose)
        total_failures += failures
        total_warnings += warnings
        total_skipped += skipped
        if failures:
            failing.append(lang)

    if total_failures:
        result = (f"Result: FAIL — {total_failures} failure(s) across "
                  f"{len(failing)} language(s) ({', '.join(failing)}).")
    else:
        result = f"Result: PASS — no failures across {len(langs)} language(s)."
    if total_warnings:
        plural = "s" if total_warnings != 1 else ""
        result += f" {total_warnings} warning{plural}."
    print(result)
    if total_skipped and not os.path.isdir(data_root):
        print(f"Note: vanilla quote law was NOT checked — no game data at "
              f"{data_root}. Only our own self-quotes were verified.")
    return 1 if total_failures else 0


if __name__ == "__main__":
    sys.exit(main())
