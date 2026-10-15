#!/usr/bin/env bash
# Extract a language's reference corpus out of the game's module tars into a
# stable cache, sentinel-gated so a deleted/half-written cache is detectable
# rather than silently read as empty (a scratchpad path being cleaned out
# mid-session is what produced phantom "no results" greps in past waves).
#
# Usage: extract_corpus.sh <LangFolder> [<LangFolder> ...]
#        extract_corpus.sh --all
#        extract_corpus.sh --check <LangFolder>
#        extract_corpus.sh --path <LangFolder>
set -u

MODULES=(Core Royalty Ideology Biotech Anomaly Odyssey)
# Default game data directory per platform, mirroring rimworld_access.csproj.
# Override with RIMWORLD_DATA.
if [[ -z "${RIMWORLD_DATA:-}" ]]; then
    case "$(uname -s)" in
        Darwin) GAME_DIR="$HOME/Library/Application Support/Steam/steamapps/common/RimWorld"
                [[ -d "$GAME_DIR/RimWorld.app" ]] || GAME_DIR="$GAME_DIR/RimWorldMac.app" ;;
        Linux)  GAME_DIR="$HOME/.steam/steam/steamapps/common/RimWorld" ;;
        *)      GAME_DIR="/c/Program Files (x86)/Steam/steamapps/common/RimWorld" ;;
    esac
    if [[ -d "$GAME_DIR/data" ]]; then DATA="$GAME_DIR/data"; else DATA="$GAME_DIR/Data"; fi
else
    DATA="$RIMWORLD_DATA"
fi
CORPUS_ROOT="${RWA_CORPUS_ROOT:-$HOME/.cache/rimworld-access/corpus}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

usage() {
    cat >&2 <<EOF
Usage: extract_corpus.sh <LangFolder> [<LangFolder> ...]
       extract_corpus.sh --all
       extract_corpus.sh --check <LangFolder>
       extract_corpus.sh --path <LangFolder>
EOF
}

check_one() {
    local lang="$1"
    if [[ -f "$CORPUS_ROOT/$lang/.complete" ]]; then
        return 0
    fi
    echo "extract_corpus: no corpus for $lang; run: scripts/l10n/extract_corpus.sh $lang" >&2
    return 1
}

path_one() {
    echo "$CORPUS_ROOT/$1"
}

# Extracts one language. Returns 1 (and leaves no dir/sentinel behind) if no
# module shipped a tar for it, or a tar failed to extract.
extract_one() {
    local lang="$1"
    # An empty or path-bearing name would aim the rm -rf below at the corpus
    # root itself (or outside it).
    if [[ -z "$lang" || "$lang" == */* || "$lang" == .* ]]; then
        echo "extract_corpus: invalid language name '$lang'" >&2
        return 1
    fi
    local dest="$CORPUS_ROOT/$lang"

    # Sentinel removed before any content is touched: a crash mid-extraction
    # must never leave a stale sentinel over a half-written corpus.
    rm -f "$dest/.complete"
    rm -rf "$dest"
    mkdir -p "$dest"

    local extracted_modules=()
    local module tar_path tars
    for module in "${MODULES[@]}"; do
        shopt -s nullglob
        tars=("$DATA/$module/Languages/$lang ("*").tar")
        shopt -u nullglob
        if [[ ${#tars[@]} -eq 0 ]]; then
            echo "  skip $module: no $lang tar"
            continue
        fi
        tar_path="${tars[0]}"
        mkdir -p "$dest/$module"
        if ! tar -xf "$tar_path" -C "$dest/$module"; then
            echo "extract_corpus: failed to extract $tar_path" >&2
            rm -rf "$dest"
            return 1
        fi
        extracted_modules+=("$module")
    done

    local xml_count
    xml_count=$(find "$dest" -name '*.xml' | wc -l | tr -d ' ')
    if [[ "$xml_count" -eq 0 ]]; then
        echo "extract_corpus: no $lang tar found in any module (${MODULES[*]})" >&2
        rm -rf "$dest"
        return 1
    fi

    {
        echo "lang=$lang"
        echo "extracted=$(date -u +%Y-%m-%dT%H:%M:%SZ)"
        echo "modules=${extracted_modules[*]}"
        echo "xml_files=$xml_count"
        echo "data_root=$DATA"
    } > "$dest/.complete"

    echo "$lang: $xml_count xml files from ${#extracted_modules[@]} modules -> $dest"
    return 0
}

if [[ $# -eq 0 ]]; then
    usage
    exit 2
fi

case "$1" in
    --check)
        [[ $# -eq 2 ]] || { usage; exit 2; }
        check_one "$2"
        exit $?
        ;;
    --path)
        [[ $# -eq 2 ]] || { usage; exit 2; }
        path_one "$2"
        exit 0
        ;;
    --all)
        [[ $# -eq 1 ]] || { usage; exit 2; }
        langs_dir="$REPO_ROOT/Languages"
        langs=()
        for d in "$langs_dir"/*/; do
            name="$(basename "$d")"
            [[ "$name" == "English" ]] && continue
            langs+=("$name")
        done
        ;;
    --*)
        usage
        exit 2
        ;;
    *)
        langs=("$@")
        ;;
esac

# The data root is checked once, before any language's cache is touched, so a
# bad RIMWORLD_DATA override cannot invalidate an already-good cached corpus.
if [[ ! -d "$DATA" ]]; then
    echo "extract_corpus: game data not found at $DATA" >&2
    exit 1
fi

status=0
for lang in "${langs[@]}"; do
    extract_one "$lang" || status=1
done
exit $status
