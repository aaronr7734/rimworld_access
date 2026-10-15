#!/usr/bin/env python3
"""RimWorld 1.7 API check for RimWorld Access.

The shipped mod still compiles against 1.6 and runs on 1.7 through the game's
Unity compatibility layer, so a member that 1.7 renamed or removed would only
surface as a runtime failure. This check compiles src/ a second time, against
1.7's own assemblies (Krafs.Rimworld.Ref 1.7) and the Harmony build 1.7 loads,
and fails on any compile error. Nothing it builds is deployed.

Both configurations are compiled, since DEBUG-only and Release-only code call
different game members. The project lives in scripts/api_check_17/.

Blind spots: string-named lookups (`[HarmonyPatch(typeof(X), "Name")]`,
`AccessTools.Field(typeof(X), "name")`) compile regardless; Harmony's own
load-time errors and the flight recorder cover those.

A failed NuGet restore (no network, no dotnet) skips with an informational line
rather than failing.

Retire this check once the shipped build targets 1.7 itself.

Usage: check_api_17.py
"""
import os
import re
import shutil
import subprocess
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PROJECT = os.path.join(REPO, "scripts", "api_check_17", "ApiCheck17.csproj")
CONFIGURATIONS = ("Debug", "Release")

ERROR_RE = re.compile(r"^(?P<loc>.+?\(\d+,\d+\)): error (?P<code>CS\d+): (?P<msg>.*?)(?: \[[^\]]*\])?$")


def build(configuration):
    return subprocess.run(
        ["dotnet", "build", PROJECT, "-c", configuration, "-nologo", "-v", "q"],
        cwd=REPO, capture_output=True, text=True)


def main():
    if shutil.which("dotnet") is None:
        print("check_api_17: SKIPPED — dotnet not found")
        return 0

    errors = set()
    for configuration in CONFIGURATIONS:
        result = build(configuration)
        output = result.stdout + result.stderr
        if result.returncode != 0 and re.search(r"error NU\d+", output):
            print("check_api_17: SKIPPED — NuGet restore failed (offline?)")
            return 0
        for line in output.splitlines():
            match = ERROR_RE.match(line.strip())
            if match:
                loc = os.path.relpath(match.group("loc"), REPO) if os.path.isabs(match.group("loc")) else match.group("loc")
                errors.add((loc, match.group("code"), match.group("msg"), configuration))
        if result.returncode != 0 and not errors:
            print(output)
            print("error RWA-API17: the 1.7 check build failed without a compiler error; output above.")
            return 1

    if errors:
        for loc, code, msg, configuration in sorted(errors):
            print("error RWA-API17: " + loc + ": " + code + " (" + configuration + "): " + msg)
        print("check_api_17: " + str(len(errors)) + " error(s) compiling against RimWorld 1.7. "
              "The member above does not exist in 1.7 in that shape; read decompiled/1.7 "
              "and call a form both versions share.")
        return 1

    print("check_api_17: OK — src/ compiles against RimWorld 1.7 ("
          + " and ".join(CONFIGURATIONS) + ")")
    return 0


if __name__ == "__main__":
    sys.exit(main())
