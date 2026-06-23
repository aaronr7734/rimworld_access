#!/usr/bin/env bash
#
# Publish RimWorld Access to the Steam Workshop via steamcmd.
#
# steamcmd uploads the Release content folder directly to Steam; the game does
# not need to be running. RimWorld's Steam App ID is 294100.
#
# Usage:
#   STEAM_USER=<your_steam_account> scripts/publish-workshop.sh [--dry-run] [--changenote "text"]
#
# First run (About/PublishedFileId.txt is 0 / missing): performs a TWO-PHASE
# create (see below) and writes the assigned id into About/PublishedFileId.txt
# for you to commit. Subsequent runs: a single update of that same item.
#
# Why two phases on a fresh publish:
#   RimWorld's own uploader calls CreateItem, writes the assigned id into
#   About/PublishedFileId.txt INSIDE the mod folder, then uploads the folder --
#   all in one submit, so the published content carries the correct id. A
#   subscriber's ModMetaData reads the published id ONLY from that file
#   (ModLister builds workshop mods via `new ModMetaData(workshopItem)`, which
#   does not take the id from the Steam folder), and the in-game Unsubscribe
#   button / "Open Workshop Page" use it (Page_ModsConfig). steamcmd assigns the
#   id only AFTER the content is frozen, so a one-shot create would ship
#   PublishedFileId.txt = 0 and break those for subscribers. To match the game,
#   a fresh publish here: (1) creates to reserve the id, (2) writes it into the
#   package and rebuilds, (3) finalizes with the corrected content. This costs
#   two Workshop change notes on the very first publish only.
#
# Authentication: steamcmd prompts for your password and Steam Guard code on the
# first login and caches the session, so later runs are non-interactive. Run
#   steamcmd +login <your_steam_account> +quit
# once by hand first if you want to get the Steam Guard prompt out of the way.
#
# Notes / known limitations:
#   * Title + description are set on creation only (matching RimWorld's own
#     uploader). Edit the description later on the Workshop page; re-running this
#     script will NOT overwrite it (unless you pass --with-description).
#   * Workshop tags ("Mod" + one "Major.Minor" per supported version, e.g.
#     "1.6") are set automatically from About.xml, mirroring RimWorld's own
#     uploader (Verse.Steam.Workshop -> SteamUGC.SetItemTags). These app-defined
#     tags are NOT editable on the Workshop web page; they can only be set at
#     upload time, which is why the script sends them on every submit.
#   * No preview image is uploaded (intentional — the audience is blind users).

set -euo pipefail

# --- Resolve paths -----------------------------------------------------------
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
APPID=294100
CSPROJ="$REPO_ROOT/rimworld_access.csproj"
CONTENT_DIR="$REPO_ROOT/release/RimWorldAccess"
ABOUT_XML="$REPO_ROOT/About/About.xml"
# Optional long-form Workshop listing. When present, used as the item description
# (newlines/BBCode preserved) instead of the short in-game About.xml description.
WORKSHOP_DESC="$REPO_ROOT/About/WorkshopDescription.txt"
PFID_FILE="$REPO_ROOT/About/PublishedFileId.txt"

# --- Args --------------------------------------------------------------------
DRY_RUN=0
CHANGENOTE=""
WITH_DESC=0
# Visibility for newly-created items: 0 public, 1 friends-only, 2 private, 3 unlisted.
# Default to unlisted so a first publish can be verified before it is public; flip
# to public on the Workshop page (or pass --visibility 0) once it looks right.
VISIBILITY=3
while [[ $# -gt 0 ]]; do
  case "$1" in
    --dry-run) DRY_RUN=1; shift ;;
    --changenote) CHANGENOTE="${2:-}"; shift 2 ;;
    --visibility) VISIBILITY="${2:-}"; shift 2 ;;
    # Re-send title + description on an update (normally only sent on create).
    # Use to correct the listing; otherwise manual Workshop-page edits are kept.
    --with-description) WITH_DESC=1; shift ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
done

: "${STEAM_USER:?Set STEAM_USER to your Steam account name}"

command -v steamcmd >/dev/null || { echo "steamcmd not found on PATH" >&2; exit 1; }
command -v dotnet   >/dev/null || { echo "dotnet not found on PATH" >&2; exit 1; }

# --- Helpers -----------------------------------------------------------------

# Build a clean Release package. CI=true builds only the release/ artifact and
# does NOT clobber the local Mods/ dev deploy. The Release target wipes the
# package folder first, so no stale files can be published. The whole About/
# folder is copied, so About/PublishedFileId.txt is embedded as it stands now.
build_package() {
  echo ">> Building clean Release package..."
  dotnet build -c Release -p:CI=true "$CSPROJ" >/dev/null
  [[ -d "$CONTENT_DIR" ]] || { echo "Release package missing: $CONTENT_DIR" >&2; exit 1; }
  echo "   content folder: $CONTENT_DIR"
}

# Write the VDF manifest for one submit. $1 = publishedfileid ("0" to create),
# $2 = change note. Title/description are included on create, or on update with
# --with-description; tags are always included; visibility only on create.
gen_vdf() {
  local pfid="$1" note="$2"
  CONTENT_DIR="$CONTENT_DIR" PFID="$pfid" APPID="$APPID" NOTE="$note" \
  ABOUT_XML="$ABOUT_XML" WORKSHOP_DESC="$WORKSHOP_DESC" VDF_FILE="$VDF_FILE" \
  VISIBILITY="$VISIBILITY" WITH_DESC="$WITH_DESC" python3 - <<'PY'
import os, xml.etree.ElementTree as ET

def vdf_escape(s, keep_newlines=False):
    s = s.replace('\\', '\\\\').replace('"', '\\"')
    # Steam stores the description verbatim and renders real newlines as line
    # breaks; it does NOT interpret a literal "\n", so keep newline characters.
    s = s.replace('\r\n', '\n') if keep_newlines else s.replace('\r\n', ' ').replace('\n', ' ')
    return s.strip()

pfid = os.environ["PFID"]
about_root = ET.parse(os.environ["ABOUT_XML"]).getroot()
lines = ['"workshopitem"', "{",
         f'\t"appid"           "{os.environ["APPID"]}"',
         f'\t"publishedfileid" "{pfid}"',
         f'\t"contentfolder"   "{os.environ["CONTENT_DIR"]}"',
         f'\t"changenote"      "{vdf_escape(os.environ["NOTE"])}"']

# Workshop tags. RimWorld's own uploader (Verse.Steam.Workshop) sets "Mod" (or
# "Translation" for a translation mod) plus one "Major.Minor" tag per supported
# version via SteamUGC.SetItemTags. Mirror that so steamcmd produces the same
# tags the in-game uploader would. Sent on every submit because these app-defined
# tags are not editable on the Workshop web page.
is_translation = (about_root.findtext("translationMod") or "").strip().lower() == "true"
tags = ["Translation" if is_translation else "Mod"]
for li in about_root.findall("./supportedVersions/li"):
    v = (li.text or "").strip()
    if v:
        tags.append(".".join(v.split(".")[:2]))
if tags:
    lines.append('\t"tags"')
    lines.append("\t{")
    for i, t in enumerate(tags):
        lines.append(f'\t\t"{i}"\t"{vdf_escape(t)}"')
    lines.append("\t}")

# Visibility is set only when creating, so updates never change it.
if pfid == "0":
    lines.append(f'\t"visibility"      "{os.environ.get("VISIBILITY", "3")}"')

# Title + description are sent on creation, or on an update when --with-description
# is passed (to correct the listing). Otherwise an update omits them so a manual
# edit made on the Workshop page is preserved.
if pfid == "0" or os.environ.get("WITH_DESC") == "1":
    root = about_root
    name = (root.findtext("name") or "RimWorld Access").strip()
    desc_file = os.environ.get("WORKSHOP_DESC", "")
    if desc_file and os.path.isfile(desc_file):
        with open(desc_file, encoding="utf-8") as f:
            desc = vdf_escape(f.read(), keep_newlines=True)
    else:
        desc = vdf_escape((root.findtext("description") or "").strip())
    lines.append(f'\t"title"           "{vdf_escape(name)}"')
    lines.append(f'\t"description"     "{desc}"')

lines.append("}")
with open(os.environ["VDF_FILE"], "w") as f:
    f.write("\n".join(lines) + "\n")
print("\n".join(lines))
PY
}

# Upload the current VDF via steamcmd, teeing output to $LOG_FILE.
do_upload() {
  echo ">> Uploading via steamcmd (you may be prompted for Steam Guard)..."
  set +e
  steamcmd +login "$STEAM_USER" +workshop_build_item "$VDF_FILE" +quit | tee "$LOG_FILE"
  local rc=${PIPESTATUS[0]}
  set -e
  if [[ $rc -ne 0 ]]; then
    echo "!! steamcmd exited $rc — upload may have failed. See output above." >&2
    exit $rc
  fi
}

# --- 1. Build ----------------------------------------------------------------
build_package

# --- 2. Create vs update -----------------------------------------------------
if [[ -f "$PFID_FILE" ]]; then
  PFID="$(tr -dc '0-9' < "$PFID_FILE")"
fi
PFID="${PFID:-0}"

if [[ "$PFID" == "0" ]]; then
  echo ">> No PublishedFileId found -> CREATING a new Workshop item (two-phase)."
else
  echo ">> PublishedFileId $PFID found -> UPDATING the existing Workshop item."
fi

# --- 3. Change notes ---------------------------------------------------------
# On a fresh publish the user's --changenote (or "Initial release") applies to
# the FINAL, content-correct submit so it reads as the real release note; the
# reservation submit gets a minor fixed note.
RESERVE_NOTE="Reserve published file id"
if [[ "$PFID" == "0" ]]; then
  FINAL_NOTE="${CHANGENOTE:-Initial release}"
else
  FINAL_NOTE="${CHANGENOTE:-Update $(date -u +%Y-%m-%dT%H:%M:%SZ)}"
fi

VDF_FILE="$(mktemp "${TMPDIR:-/tmp}/rwa-workshop.XXXXXX")"
trap 'rm -f "$VDF_FILE"' EXIT

# --- 4. Dry run --------------------------------------------------------------
if [[ "$DRY_RUN" == "1" ]]; then
  if [[ "$PFID" == "0" ]]; then
    echo ">> [dry-run] phase 1 (reserve) manifest:"; gen_vdf 0 "$RESERVE_NOTE"
    echo ">> [dry-run] phase 2 would re-run with the assigned id and note: $FINAL_NOTE"
  else
    gen_vdf "$PFID" "$FINAL_NOTE"
  fi
  trap - EXIT   # keep the file so it can be inspected
  echo ">> --dry-run: VDF written to $VDF_FILE (not uploading). Remove --dry-run to publish."
  echo "   inspect: cat $VDF_FILE"
  exit 0
fi

LOG_FILE="$(mktemp "${TMPDIR:-/tmp}/rwa-steamcmd.XXXXXX")"

# --- 5. Publish --------------------------------------------------------------
if [[ "$PFID" == "0" ]]; then
  # Phase 1: create to reserve the published file id (content still has id 0).
  echo ">> Phase 1/2: creating item to reserve a published file id..."
  gen_vdf 0 "$RESERVE_NOTE"
  do_upload

  # steamcmd prints "Create new workshop item ( PublishFileID 1234567890)." — note
  # it is "PublishFileID" (no "ed"). The trailing `|| true` keeps a no-match grep
  # from aborting the script under `set -e` + `pipefail`.
  NEW_ID="$(grep -oiE 'Publish(ed)?FileID[^0-9]*[0-9]+' "$LOG_FILE" | grep -oE '[0-9]+' | tail -1 || true)"
  if [[ -z "${NEW_ID:-}" ]]; then
    echo "!! Could not parse the new PublishedFileId from steamcmd output." >&2
    echo "   Find it on your Workshop page, write it to $PFID_FILE, and re-run to finalize." >&2
    exit 1
  fi
  echo "$NEW_ID" > "$PFID_FILE"
  echo ">> Reserved id $NEW_ID, written to $PFID_FILE"

  # Phase 2: rebuild so the package embeds the real id, then finalize the upload
  # so the published content carries About/PublishedFileId.txt = NEW_ID.
  echo ">> Phase 2/2: rebuilding with the id embedded, then finalizing..."
  build_package
  EMBEDDED="$(tr -dc '0-9' < "$CONTENT_DIR/About/PublishedFileId.txt" 2>/dev/null || true)"
  if [[ "$EMBEDDED" != "$NEW_ID" ]]; then
    echo "!! Package About/PublishedFileId.txt is '$EMBEDDED', expected '$NEW_ID'." >&2
    echo "   The finalize upload would ship the wrong id; aborting." >&2
    exit 1
  fi
  gen_vdf "$NEW_ID" "$FINAL_NOTE"
  do_upload

  echo ">> Published. New Workshop item id $NEW_ID (content carries the correct id)."
  echo "   COMMIT $PFID_FILE so future runs update the same item."
  echo "   Then on the Workshop page set Harmony as a Required Item and flip to public."
else
  # Existing item: a single update submit.
  gen_vdf "$PFID" "$FINAL_NOTE"
  do_upload
  echo ">> Updated Workshop item $PFID."
fi

echo ">> Done."
