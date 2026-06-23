# Installation

This page walks through what you need to do before the mod will work, including turning off the Steam overlay and installing the mod files.

## Turn off the Steam in-game overlay

Do this before anything else, for your sanity. This will benefit all your Steam games, not just RimWorld.

Steam's in-game overlay grabs certain key combinations like **Shift+Tab** before they reach RimWorld. The overlay itself is not screen-reader accessible, so when it is on, some of the mod's keys will silently do nothing and you will have no way to tell why.

Turn it off:

1. Open Steam.
2. Go to **Steam menu > Settings**.
3. Select the **In-Game** tab.
4. Uncheck **"Enable the Steam Overlay while in-game."**
5. Close the Settings window.

If a key like **Shift+Tab** ever stops working, come back and check this first.

## Install the mod

There are two ways to install RimWorld Access: through the Steam Workshop, or by hand. The Workshop is the easier path and keeps the mod up to date for you. Pick whichever fits, then finish with the shared step at the end that switches the mod on.

### Steam Workshop (recommended)

1. Log into Steam, open the [RimWorld Access Workshop page](https://steamcommunity.com/sharedfiles/filedetails/?id=3750094441), and click **Subscribe**.
2. Steam will ask whether you also want to subscribe to Harmony, the other mod RimWorld Access depends on. Choose **Subscribe to All**.
3. Both will automatically download the next time RimWorld launches.

That puts the files in place. Now read [Turn the mod on](#turn-the-mod-on).

### Manual install

Use this if you do not own RimWorld on Steam, or you would rather manage the files yourself.

[Download the latest release](https://github.com/aaronr7734/rimworld_access/releases/download/dev/RimWorldAccess-dev.zip) and unzip it into RimWorld's `Mods` folder, so you end up with a `RimWorldAccess` folder inside `Mods`. With a default Steam install, the `Mods` folder can be found at the following locations:

- **Windows:** `C:\Program Files (x86)\Steam\steamapps\common\RimWorld\Mods`
- **macOS:** `~/Library/Application Support/Steam/steamapps/common/RimWorld/RimWorldMac.app/Mods`

If you installed RimWorld to a different Steam library, the path up to `steamapps` changes, but everything after it stays the same.

A manual install does not include Harmony. You will need to download it separately:

- **On Steam:** Log into your Steam account in a web browser, open the [Harmony Workshop page](https://steamcommunity.com/workshop/filedetails/?id=2009463077), and click Subscribe. Steam downloads it the next time the game launches.
- **Without Steam:** Install Harmony from its [GitHub releases page](https://github.com/pardeike/HarmonyRimWorld/releases/latest) and place it in the same `Mods` folder.

## Turn the mod on

Regardless of how you installed it, the final step is the same. RimWorld's own mod list is not screen-reader accessible until RimWorld Access is already running, so instead of switching the mod on inside the game, you drop in a settings file that has it switched on already.

Download [`ModsConfig.xml`](../files/ModsConfig.xml){ download="ModsConfig.xml" } and place it in RimWorld's Config folder. It comes ready-made with Harmony and RimWorld Access already enabled. The Config folder can be found at the following locations:

- **Windows:** `%localappdata%low\Ludeon Studios\RimWorld by Ludeon Studios\Config`
- **macOS:** `~/Library/Application Support/RimWorld/Config`

You can paste either path as-is and it will open the correct folder. On Windows, paste it into the Run dialog (**Win+R**) or the File Explorer address bar. On macOS, paste it into Finder's Go to Folder box (**Cmd+Shift+G**).

### Already installed it by hand before?

If you installed an earlier version of RimWorld Access by hand, you may still have a `RimWorldAccess` folder in your `Mods` folder. After subscribing on the Workshop, it is worth deleting that folder, for two reasons:

- A copy sitting in your `Mods` folder always loads instead of the Workshop copy. You could keep running an old version without realizing it, and it would not update automatically.
- Older builds used a different mod identifier than the Workshop release. The two will not clash on their own, but if both end up enabled in your mods list, RimWorld might load the mod twice, which would probably break things.

The settings file above enables only a single copy, so as long as you use it and do not turn on a second one by hand, you are safe either way. Deleting the old folder just removes any chance of mixing them up.

## Launch the game

Once the files are in place, launch RimWorld. With your screen reader running, the main menu should start talking immediately.

## Next steps

Head to [first launch](first-launch.md) to learn more about getting started with the mod!
