# Reporting bugs

Found something broken? A clear report gets it fixed faster, and it does not take much.

## First, isolate it

Test with only **Harmony** and **RimWorld Access** enabled, and no other mods. Other mods can change how the game behaves in ways that look like a RimWorld Access bug but aren't. If the problem still happens with just those two mods, it's a real report.

This is genuinely hard once you have other mods installed: stripping them out can break the save you were playing, so reproducing a problem in a clean setup takes real effort. RimWorld Access officially supports only Harmony plus itself, so a report from a clean save is the most useful kind, but do what you can.

## What to include

A good report answers three questions:

- **What you did.** The screen you were on and the keys you pressed, step by step. "On the Work tab, I pressed Alt+M, then..." is the right level of detail.
- **What was announced.** What your screen reader actually said.
- **What you expected instead.**

If you can, include your screen reader, your operating system, and which DLC was active. The more reliably you can reproduce it, the easier it is to fix.

## Attach your log file

RimWorld writes a log every time it runs, called `Player.log`. It records errors that never appear on screen, so attaching it often points straight at the cause. The file is replaced each time the game launches, so grab it after the problem happens and before starting the game again.

It is located here:

- **Windows:** `%localappdata%low\Ludeon Studios\RimWorld by Ludeon Studios\Player.log`
- **macOS:** `~/Library/Logs/Ludeon Studios/RimWorld by Ludeon Studios/Player.log`

You can paste either path as-is to reach the file. On Windows, paste it into the Run dialog (**Win+R**) or the File Explorer address bar. On macOS, paste it into Finder's Go to Folder box (**Cmd+Shift+G**).

## Where to file it

The easiest place is the **#bug-reports** channel on the Discord. <!-- TODO(Aaron): add the Discord invite link -->

If you know your way around GitHub, you can also open a GitHub issue instead. <!-- TODO(Aaron): add the GitHub issues link -->
