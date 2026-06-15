# Playing on Mac

This page covers the keyboard differences between Windows and macOS. All other documentation uses Windows key names. Substitute as described here when reading any other page.

## General modifier substitutions

| Windows | macOS |
|---------|-------|
| **Ctrl** | **Cmd** |
| **Alt** | **Option** |

These substitutions apply throughout the mod. For example, where the docs say **Ctrl+Alt+Enter**, on Mac you press **Cmd+Option+Enter**.

## The Ctrl+Tab exception

One shortcut does not follow the Ctrl-to-Cmd pattern: **Ctrl+Tab** (used to switch views in the Work menu and similar panels) maps to **Option+Tab** on macOS, not Cmd+Tab.

This is because Cmd+Tab is reserved by the operating system as the app switcher and cannot reach the game, and physical Ctrl+Tab is also not reliably deliverable on Mac through Unity. The mod handles this automatically: when the current key is Tab, pressing Option acts as the Ctrl substitute. No configuration is needed.

So on Mac:

| Windows | macOS |
|---------|-------|
| **Ctrl+Tab** | **Option+Tab** |
| **Ctrl+Shift+Tab** | **Option+Shift+Tab** |

All other Ctrl shortcuts use **Cmd** as normal.
