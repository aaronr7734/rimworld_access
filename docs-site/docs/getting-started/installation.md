# Installation

This page walks through what you need to do before the mod will work: turn off the Steam overlay, then install the mod files.

## Turn off the Steam in-game overlay

Do this before anything else.

Steam's in-game overlay grabs certain key combinations (Shift+Tab is the main one) before they reach RimWorld. The overlay itself is not screen-reader accessible, so when it is on, some of the mod's keys will silently do nothing and you will have no way to tell why.

Turn it off:

1. Open Steam.
2. Go to **Steam menu > Settings**.
3. Select the **In-Game** tab.
4. Uncheck **"Enable the Steam Overlay while in-game."**
5. Close the Settings window. There is no save button; closing is enough, and nothing gets disabled.

If a key like **Shift+Tab** ever seems to do nothing, come back and check this first.

## Install the mod

### Steam Workshop (coming soon)

The plan is to put RimWorld Access on the Steam Workshop so it installs and updates automatically. That is not ready yet.

### Manual install

Manual install has two parts: putting the mod files in the right place, and installing Harmony (a required dependency).

#### Place the mod files

[Download the latest release](https://github.com/aaronr7734/rimworld_access/releases/download/dev/RimWorldAccess-dev.zip) and unzip it into RimWorld's `Mods` folder, so you end up with a `RimWorldAccess` folder inside `Mods`. With a default Steam install, the `Mods` folder is here:

- **Windows:** `C:\Program Files (x86)\Steam\steamapps\common\RimWorld\Mods`
- **macOS:** `~/Library/Application Support/Steam/steamapps/common/RimWorld/RimWorldMac.app/Mods`

If you installed RimWorld to a different Steam library, the path up to `steamapps` changes, but everything after it stays the same.

Then download [`ModsConfig.xml`](../files/ModsConfig.xml){ download="ModsConfig.xml" } and place it in RimWorld's Config folder. This file comes ready-made with Harmony and RimWorld Access already enabled, so you do not need to turn anything on inside the game afterward. The Config folder is here:

- **Windows:** `%localappdata%low\Ludeon Studios\RimWorld by Ludeon Studios\Config`
- **macOS:** `~/Library/Application Support/RimWorld/Config`

You can paste either path as-is and it will resolve to the right folder. On Windows, paste it into the Run dialog (**Win+R**) or the File Explorer address bar. On macOS, paste it into Finder's Go to Folder box (**Cmd+Shift+G**). You do not need to substitute your username or hunt for the folder by hand.

#### Install Harmony

Harmony is a library the mod depends on. You need it separately.

**On Steam:** Log into your Steam account in a web browser, open the [Harmony Workshop page](https://steamcommunity.com/workshop/filedetails/?id=2009463077), and click Subscribe. Steam will download it automatically the next time the game launches.

**Without Steam:** Install Harmony manually from its GitHub releases page and place it in the same `Mods` folder. Legitimate copies of RimWorld exist outside Steam, bought from a reputable shop or from Ludeon Studios directly, and those are fine. But if you are playing a copy you did not get from Steam, another reputable shop, or Ludeon themselves, most of the mod will probably not work, and that is not something that can be supported.

## Launch the game

Once the files are in place, launch RimWorld. With your screen reader running, the main menu should start talking immediately. If it does not, the most common cause is the Steam in-game overlay, covered at the top of this page.

## Next steps

Head to [first launch](first-launch.md) to go through the initial settings and find the RimWorld Access options.
