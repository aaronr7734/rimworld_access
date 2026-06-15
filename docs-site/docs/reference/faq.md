# Frequently asked questions

Common questions about what the mod does and doesn't do, and why.

## Can you add support for [some mod]?

Probably not, and almost certainly not any time soon. Here's the reasoning.

A lot of content mods, meaning mods that add new items, animals, events, or factions without building their own custom screens, are already largely accessible.

The trouble is mods that build their own UI. When a mod adds a custom screen that doesn't follow how vanilla RimWorld does things, the mod's screen gets no accessibility treatment from RimWorld Access. Making it accessible isn't a matter of flipping a switch. It means installing the mod, building a save that actually exercises that UI, writing and testing new Harmony patches for it, and then maintaining those patches whenever the mod updates. And that's for one mod.

Every time a new mod gets support, it opens the door to requests for the next one. The queue is effectively infinite. People in the Discord are finding new mods every week. I can't keep up with that, and trying to is how burnout happens.

There's also a localization problem most people don't think about. Many mods haven't been translated into the languages RimWorld Access supports. When a mod's text isn't localized, players hear the raw English strings, and the question comes back to me rather than to the mod author. That's not something I'm willing to volunteer for.

The codebase also isn't in a state right now where I can safely take outside pull requests for mod accessibility. That may change. Even if it does, though, I have no interest in becoming a code reviewer for mods I may not even have installed. Reviewing a contribution properly would mean installing whoever's mods to test the change, and I'm not going to approve work I can't check. So even down the road, this may simply not be something I take on. We'll see.

None of this is permanent. Once the core RimWorld experience is solid, I'll probably give some mods attention. The Vanilla Expanded series is a likely candidate, because those authors do careful work and their content fits naturally with what the mod does. I may also cover mods I personally find interesting. Eventually, once I'm more comfortable taking this on, I might set up community polls so players can weigh in on which mods matter most to them to see made accessible. I'd like people to have a say in what I work on, if I ever get there.

But the baseline answer is: if a mod adds its own custom screens, don't expect support for it.

## Why doesn't Alt+H read out every health condition?

**Alt+H** is a quick readout for what matters in a hurry: consciousness, movement, manipulation, and bleeding status. Those four things tell you most of what you need to know when something urgent is happening.

The request that comes up most often is adding infection status to that readout. The trouble is length. If a pawn has several infections or conditions, hearing them all in one announcement is slower than just searching for the one you care about. And searching is something the mod is already good at.

Type-ahead is, more or less, a search filter, and it's basically a mouse for blind people. Once it clicks, you'll use it everywhere. Here's how it works for health. Press **Enter** on the pawn under your cursor, or **Ctrl+Alt+Enter** on a pawn you've selected from the colonist bar, to open that pawn's inspect screen. Then just start typing. Type "inf" and you land on infections. Type "leg", "arm", or "head", or even just "hea", and you land on everything wrong with that part. The arrow keys step through the other matches. It's faster than any fixed readout could be, because you go straight to what you asked for.

That said, I'm still thinking about how best to surface every status condition through another shortcut, probably **Alt+Shift+H**. I'm not against the idea. It just feels redundant when type-ahead already gets you there so quickly. If you've leaned on type-ahead and still feel something is missing, that's worth telling me.

## Is there a guide to laying out a base?

Not in this documentation, and that's deliberate. Base layout is genuinely hard to describe in text. A kill box or a storage warehouse has a visual logic that's difficult to convey through written coordinates and dimensions, and advice that sounds simple on paper ("make your bedrooms seven by seven on the exterior, five by five inside") can be tricky to apply in practice.

What this documentation covers is the mechanics: how to place buildings, how rooms work, what the Architect menu offers, how to use the scanner to survey your map. The strategy of what to build and where is a harder problem.

The [colony building guide](https://rimworldwiki.com/wiki/Colony_Building_Guide) on the RimWorld wiki is a good place to start. It's written for players generally, and the advice translates.

For the "how does this actually look in practice" question, the goal is eventually to collect save files from community members that demonstrate well-built bases, so you can load them up and explore. That's not ready yet. The best current resource is the RimWorld Access Discord, where players share their approaches and can answer specific layout questions.

## Can I play with other mods?

You can, but it's unsupported. When something breaks in a modded game, it's very hard to tell whether the issue is with RimWorld Access, with one of the other mods, or with a combination. Isolating a bug officially means testing with only Harmony and RimWorld Access active, and if you're playing a modded save, that's not easy to do without starting over.

This doesn't mean you should avoid mods. Plenty of players run large mod lists without problems. But if you run into a bug and want to report it, you'll likely need to be able to reproduce it in a clean environment before it can be investigated. See [reporting bugs](reporting-bugs.md) for what that looks like.

The mod doesn't police what you install. It just can't take responsibility for what other mods do.

## Should I use permadeath (commitment mode)?

You can, but go in knowing the risks. In commitment mode, if your colony ends, the save is gone. Software crashes, screen reader hiccups, and bugs in any of your mods can all end a run without warning. The mod is stable enough that many players use commitment mode without incident, but "stable enough" and "completely safe" aren't the same thing.

If you're new to RimWorld, the bigger risk is just the game's difficulty. Losing a long permadeath run to a game mechanic you didn't know about yet is frustrating in a way that reloading from a normal save isn't. Most experienced players recommend playing without commitment mode until you understand how to handle raids, disease, and starvation.

If you want to use it, go for it. Just keep in mind that no software is crash-proof, and that mod updates, game updates, and screen reader updates can all introduce unexpected behavior.

## Some text isn't in my language. Why?

RimWorld Access supports several languages, and text that comes from the mod itself (menu labels, navigation announcements, and the descriptions I write) should appear in your language where it has been translated. Localization is still fairly new, though, so some of the mod's own text may not be translated yet, and there are still genuine localization bugs to work out. If you hear English where you expect your own language in a base-game or RimWorld Access screen, please report it. See [reporting bugs](reporting-bugs.md). That kind of report genuinely helps.

Some text can also come from another mod you have installed. Mods are responsible for their own translations, and when a mod author hasn't provided one for your language, that text falls back to English. That part isn't mine to fix, since the translation isn't mine to provide for content I didn't write. It isn't always obvious which mod a piece of text comes from, which is why these questions often land here, but the place to ask for a mod's translation is with that mod's author.
