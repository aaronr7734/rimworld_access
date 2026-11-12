# German (Deutsch) Terminology & Style Glossary — RimWorld Access

This glossary locks in consistent German wording for translating the RimWorld Access screen-reader
mod. The goal is that German players feel the mod is a seamless extension of RimWorld itself, so
**every game-anchored term below uses the EXACT word RimWorld's own official German translation ships**,
with a citation to the reference file it was taken from.

The strings we translate are **spoken aloud by a TTS engine (screen reader / Sprachausgabe)**, not
displayed. Natural, terse, unambiguous phrasing matters more than visual polish. German runs long —
prefer the game's own word and two clear words over a Bandwurm-Kompositum.

RimWorld language folder name: `German` (native name `Deutsch`, `languageWorkerClass`
`LanguageWorker_German`, from `de_ref/Core/LanguageInfo.xml`). Reference corpus: RimWorld's official
German translation, extracted to `de_ref/` (Core + Royalty + Ideology + Biotech + Anomaly + Odyssey).
Paths below are relative to `de_ref/`.

> How to use this file: grep for the English term. If a term you need is not here and RimWorld has it,
> grep the game corpus yourself (see Section 4) and add it — never invent a rendering for a concept
> the game already names.

> **THE MOST IMPORTANT RULES (details in §3):**
> 1. **No `_numCase`.** Verified: **zero** `_numCase` usages in the whole German corpus. German uses
>    simple two-form plurals. For paired `…One`/`…Many` keys (C# pre-selects by `count == 1`): fill
>    `…One` = singular, `…Many` = plural. NEVER write `{N_numCase ? … : …}` — for German the grammar
>    engine resolves it to an **empty string**. (§3.2)
> 2. **Three-way gender tags ONLY on a real `_gender` arg.** German has three genera. Use
>    `{ARG_gender ? maskulin : feminin : neutrum}` (e.g. `{0_gender ? der : die : das}`) **only** when
>    the placeholder genuinely carries a `_gender` tag from the game. For a generic `{0}` item label
>    (gender unknown), recast to **label-colon-value** (`Auswahl: {0}`, `Ziel: {0}`) — never a
>    declined participle. This is the single most important rule for German. (§3.1, §3.4)
> 3. **Informal "du".** RimWorld's German addresses the player with **du** (`Klicke`, `Wähle`,
>    `Du kannst`, `deine`). Use du and du-form imperatives throughout. (§3.5)
> 4. **Capitalize nouns.** German capitalizes every noun (`Kolonist`, `Auswahl`, `Stimmung`). A common
>    LLM error is lower-casing them — do not. (§3.3)

---

## Section 1 — Core game vocabulary (game-anchored)

Every row is the term RimWorld itself ships in German. Use it verbatim. These are the words the player
already hears from the game, so our mod must match them exactly.

### People, factions, world

| English | Deutsch | Source file |
|---|---|---|
| colonist (colony member) | Kolonist | `Core/Keyed/Misc.xml` (`Colonist` Kolonist); `Core/Keyed/Alerts.xml` (`BreakRiskDescEnding` …die Stimmung der Kolonisten…) |
| colony (your base) | Kolonie | `Ideology/Keyed/Misc_Gameplay.xml` (`LetterLabelRoleActiveDesc` …in deiner Kolonie…). Note: a foreign base/settlement is a **Siedlung** |
| pawn / character (generic) | Spielfigur / Person | `Core/Keyed/Misc_Gameplay.xml` (`DefaultMedicineSettingsDesc` …Wenn die Spielfigur…); `Core/Keyed/Alerts.xml` (`ColonistNeedsTreatmentDesc` …Selektiere dann die verletzte Person…). Choose by who the pawn can be, see §3.11 |
| faction | Fraktion | `Core/Keyed/ITabs.xml` (`RecruitmentResistanceDesc` …deiner Fraktion beizutreten…); `Core/DefInjected/FactionDef/*` |
| caravan | Karawane | `Core/Keyed/Misc_Gameplay.xml` (`Caravan` Karawane); `Core/Keyed/GameplayCommands.xml` (`CommandFormCaravan` Karawane formieren). Used for both player expeditions and trader caravans |
| raid (event) | Überfall | `Core/Keyed/Misc_Gameplay.xml` (`Raid` Überfall) |
| map | Karte | `Core/Keyed/Misc_Gameplay.xml` (`Map` Karte) |
| tile / map cell | Feld | `Core/Keyed/Misc_Gameplay.xml` (`SelectNextInSquareTip` …das nächste Objekt im selben Feld…). Use **Feld** for a single map cell |
| zone | Zone | `Core/Keyed/Misc_Gameplay.xml` (`Zone` Zone); `Core/DefInjected/DesignationCategoryDef/DesignationCategories.xml` (`Zone.label` Zone) |
| area (map area) | Gebiet | `Core/Keyed/Misc_Gameplay.xml` (`AreaLower` Gebiet); `Core/Keyed/Designators.xml` (`DesignatorExpandAreaAllowed` Erlaubtes Gebiet erweitern) |
| world / planet | Welt / Weltkarte | `Core/DefInjected/MainButtonDef/MainButtons.xml` (`World.label` Weltkarte). The planet as a place = **Welt**; the world-map screen = **Weltkarte** |
| prisoner | Gefangener | `Core/Keyed/Misc_Gameplay.xml` (`Prisoner` Gefangener, `PrisonerLower` Gefangener) — noun is always capitalized |

### Designators / order verbs (imperative/infinitive form, as the game uses them on buttons)

| English | Deutsch | Source file |
|---|---|---|
| cancel | Abbrechen | `Core/Keyed/Designators.xml` (`DesignatorCancel`) |
| chop wood / harvest wood | Holz hacken | `Core/Keyed/Designators.xml` (`DesignatorHarvestWood`) |
| mine | Gestein abbauen | `Core/Keyed/Designators.xml` (`DesignatorMine`) |
| mine vein (mark) | Ader abbauen | `Core/Keyed/Designators.xml` (`DesignatorMineVein`) |
| harvest | Ernten | `Core/Keyed/Designators.xml` (`DesignatorHarvest`) |
| cut plants | Pflanzen schneiden | `Core/Keyed/Designators.xml` (`DesignatorCutPlants`) |
| deconstruct | Abreißen | `Core/Keyed/Designators.xml` (`DesignatorDeconstruct`) |
| uninstall | Demontieren | `Core/Keyed/Designators.xml` (`DesignatorUninstall`) |
| haul | Objekte wegtragen | `Core/Keyed/Designators.xml` (`DesignatorHaulThings`). For the bare verb use **wegtragen** / **tragen** |
| hunt | Jagen | `Core/Keyed/Designators.xml` (`DesignatorHunt`) |
| tame | Zähmen | `Core/Keyed/Designators.xml` (`DesignatorTame`) |
| slaughter | Schlachten | `Core/Keyed/Designators.xml` (`DesignatorSlaughter`) |
| forbid | Verbieten | `Core/Keyed/Designators.xml` (`DesignatorForbid`) |
| unforbid / allow | Erlauben | `Core/Keyed/Designators.xml` (`DesignatorUnforbid`) |
| claim | In Besitz nehmen | `Core/Keyed/Designators.xml` (`DesignatorClaim`) |
| strip | Entkleiden | `Core/Keyed/Designators.xml` (`DesignatorStrip`) |
| smooth (surface) | Fläche glätten | `Core/Keyed/Designators.xml` (`DesignatorSmoothSurface` Fläche glätten; also `DesignatorSmoothWalls` Wand glätten, `DesignatorSmoothFloors` Boden glätten). **Not "Oberfläche glätten"** — 0 corpus hits as a label, and `CommandViewSurface` "Oberfläche anzeigen" is a different command the same player hears |
| plan | Planen | `Core/Keyed/Designators.xml` (`DesignatorPlan`) |
| build | bauen | `Core/Keyed/GameplayCommands.xml` (`CommandBuildCopy` Kopie bauen); `Core/Keyed/Designators.xml` (`DesignatorAreaBuildRoofExpand` 'Dach bauen' erweitern) |
| prioritize | Priorisiere / priorisieren | `Core/Keyed/FloatMenu.xml` (`PrioritizeGenericSimple` Priorisiere {0}) |
| tend (a wound) | behandeln | `Core/Keyed/FloatMenu.xml` (`Tend` Behandle {0}); `Core/Keyed/Misc_Gameplay.xml` (`CanTendNow` Kann jetzt behandelt werden) |
| assign owner | Besitzer wählen | `Core/Keyed/GameplayCommands.xml` (`CommandThingSetOwnerLabel` Besitzer wählen) |
| draft / to combat mode | Kampfmodus | `Core/Keyed/GameplayCommands.xml` (`CommandDraftLabel` Kampfmodus). **WARNING: NOT "Einberufen".** The draft toggle is **Kampfmodus** |
| undraft | Kampfmodus beenden | `Core/Keyed/GameplayCommands.xml` (`CommandUndraftLabel`) |
| draft STATE (is not drafted) | ist nicht im Kampfmodus | `Core/Keyed/FloatMenu.xml` (`IsNotDraftedLower` {1_labelShort} ist nicht im Kampfmodus) |
| recruit (a prisoner) | rekrutieren | `Core/DefInjected/PrisonerInteractionModeDef/*` (`AttemptRecruit.label` rekrutieren); `Core/Keyed/Letters.xml` (`RecruitSuccess` {0} konnte {1} erfolgreich rekrutieren…). Distinct from draft (Kampfmodus) — no collision in German |

### Buildings, zones, areas, build categories

| English | Deutsch | Source file |
|---|---|---|
| growing zone | Anbauzone | `Core/Keyed/Misc_Gameplay.xml` (`GrowingZone`) |
| stockpile / storage zone | Lagerzone | `Core/Keyed/Misc_Gameplay.xml` (`Stockpile` Lagerzone) |
| stockpile group | Lager | `Core/Keyed/Misc_Gameplay.xml` (`StockpileGroup` Lager) |
| home area | Heimatgebiet | `Core/Keyed/Designators.xml` (`DesignatorAreaHomeExpand` Heimatgebiet erweitern) |
| allowed area | Erlaubtes Gebiet | `Core/Keyed/Designators.xml` (`DesignatorExpandAreaAllowed` Erlaubtes Gebiet erweitern) |
| structure (build category) | Strukturen | `Core/DefInjected/DesignationCategoryDef/DesignationCategories.xml` (`Structure.label`) |
| furniture | Möbel | same file (`Furniture.label`) |
| floor / flooring | Böden | same file (`Floors.label`) |
| power (category) | Strom | same file (`Power.label`) |
| production | Produktion | same file (`Production.label`) |
| security | Sicherheit | same file (`Security.label`) |
| temperature (category) | Temperatur | same file (`Temperature.label`) |
| orders | Befehle | same file (`Orders.label`) |
| recreation (category) | Entspannung | same file (`Joy.label`) |
| misc | Diverses | same file (`Misc.label`) |
| ship (category) | Raumschiff | same file (`Ship.label`) |
| blueprint | Blaupause | `Core/Keyed/GameplayCommands.xml` (`CommandPlaceBlueprints` Blaupausen platzieren) |
| rename | Umbenennen | `Core/Keyed/Misc_Gameplay.xml` (`Rename`) |

### Work, schedule, skills, needs

| English | Deutsch | Source file |
|---|---|---|
| work (tab / main button) | Arbeit | `Core/DefInjected/MainButtonDef/MainButtons.xml` (`Work.label` Arbeit) |
| schedule (tab) | Zeitplan | `Core/DefInjected/MainButtonDef/MainButtons.xml` (`Schedule.label` Zeitplan) |
| assign (tab) | Zuweisung | `Core/DefInjected/MainButtonDef/MainButtons.xml` (`Assign.label` Zuweisung) |
| work type / skill | use the specific game label (see below) | `Core/DefInjected/WorkTypeDef/*`, `Core/DefInjected/SkillDef/*` |
| skill labels | Fernkampf, Nahkampf, Konstruktion, Bergbau, Kochen, Pflanzen, Tiere, Handwerk, Kunst, Medizin, Sozial, Intellekt | `Core/DefInjected/SkillDef/Skills.xml` (`Shooting`/`Melee`/`Construction`/`Mining`/`Cooking`/`Plants`/`Animals`/`Crafting`/`Artistic`/`Medicine`/`Social`/`Intellectual`) |
| work-type labels (short) | Feuerwehr, Patient, Bettruhe, Arzt, Tierpflege, Bauen, Feldarbeit, Jagen, Grünschnitt, Bergbau, Reinigen, Forschen, Kochen, Schmieden, Schneidern, Handwerk, Kunst, Transport, Hilfsarbeit, Wärter | `Core/DefInjected/WorkTypeDef/WorkTypes.xml` (`*.labelShort`) |
| passion | Hingabe | `Core/Keyed/Skills.xml` (`Passion`) |
| passion — none | Keine Hingabe | `Core/Keyed/Skills.xml` (`PassionNone`) |
| passion — minor | Hingabe | `Core/Keyed/Skills.xml` (`PassionMinor` Hingabe). Note: `Passion` and `PassionMinor` both ship as "Hingabe"; **major** is the distinct one |
| passion — major | Brennende Hingabe | `Core/Keyed/Skills.xml` (`PassionMajor`) |
| level (skill) | Stufe | `Core/Keyed/Skills.xml` (`Level` Stufe). Distinct from **Ebene** (tree depth, §2) |
| mood | Stimmung | `Core/DefInjected/NeedDef/Needs.xml` (`Mood.label`) |
| need (tab) | Bedürfnis | `Core/Keyed/Alerts.xml` (`BreakRiskDescEnding` …den Bedürfnis-Tab dieser Kolonisten…) |
| food (need) | Ernährung | `Core/DefInjected/NeedDef/Needs.xml` (`Food.label`) |
| recreation / joy (need) | Entspannung | same file (`Joy.label`) |
| rest / sleep | Erholung | same file (`Rest.label`) |
| comfort | Komfort | same file (`Comfort.label`) |
| beauty | Attraktivität | same file (`Beauty.label`) |
| room size | Raumgröße | same file (`RoomSize.label`) |
| indoors / outdoors | drinnen / draußen | same file (`Indoors.label` drinnen, `Outdoors.label` draußen) |

### Health

| English | Deutsch | Source file |
|---|---|---|
| health (tab) | Körper | `Core/Keyed/ITabs.xml` (`TabHealth` Körper). **Note: the Health TAB is "Körper", not "Gesundheit".** |
| health (general noun) | Gesundheit | `Core/Keyed/Menus_Main.xml` (`Health` Gesundheit) |
| overview | Übersicht | `Core/Keyed/ITabs.xml` (`HealthOverview` Übersicht) |
| bleeding (rate) | Blutung | `Core/Keyed/ITabs.xml` (`BleedingRate`) |
| tend quality | Behandlungsqualität | `Core/Keyed/Misc_Gameplay.xml` (`TendQuality`) |
| tend (verb) | behandeln | `Core/Keyed/FloatMenu.xml` (`Tend` Behandle {0}) |
| self-tend | Selbstbehandlung | `Core/Keyed/FloatMenu.xml` (`SelfTendDisabled` Selbstbehandlung deaktiviert) |
| no medical care | keine Behandlung | `Core/Keyed/Enums.xml` (`MedicalCareCategory_NoCare`) |
| body capacities | Bewusstsein, Sehen, Hören, Bewegen, Arbeiten, Atmen, Blutfiltration, Blutproduktion, Verdauung, Essen, Sprechen | `Core/DefInjected/PawnCapacityDef/*` (`Consciousness`/`Sight`/`Hearing`/`Moving`/`Manipulation`/`Breathing`/…) |
| body part | use the specific game label | `Core/DefInjected/BodyPartDef/*` |

### Trade

| English | Deutsch | Source file |
|---|---|---|
| trade | Handeln | `Core/Keyed/GameplayCommands.xml` (`CommandTrade` Handeln); `Core/Keyed/Incidents.xml` (`CaravanMeeting_Trade` Handeln) |
| selling | verkaufen | `Core/Keyed/Dialogs_Various.xml` (`Selling`) |
| buying | kaufen | `Core/Keyed/Dialogs_Various.xml` (`Buying`) |
| nutrition | Nahrung | `Core/Keyed/Dialogs_Various.xml` (`Nutrition`) |
| silver | Silber | use the specific ThingDef label: `Core/DefInjected/ThingDef/*` (`Silver.label`). Verify before use if length matters |
| apparel / clothing | Kleidung | `Core/Keyed/Misc_Gameplay.xml` (`Apparel`) |

### Research

| English | Deutsch | Source file |
|---|---|---|
| research (main button / noun) | Forschung | `Core/DefInjected/MainButtonDef/MainButtons.xml` (`Research.label` Forschung) |
| research (verb) | Forschen | `Core/Keyed/MainTabs.xml` (`Research` Forschen); `Core/DefInjected/WorkTypeDef/WorkTypes.xml` (`Research.labelShort` Forschen) |
| stop research | Forschung stoppen | `Core/Keyed/MainTabs.xml` (`StopResearch`) |
| tech level — animal | Tier | `Core/Keyed/Enums.xml` (`TechLevel_Animal`) |
| tech level — neolithic | neolithisch | `Core/Keyed/Enums.xml` (`TechLevel_Neolithic`) |
| tech level — medieval | mittelalterlich | `Core/Keyed/Enums.xml` (`TechLevel_Medieval`) |
| tech level — industrial | industriell | `Core/Keyed/Enums.xml` (`TechLevel_Industrial`) |
| tech level — spacer | Raumfahrt | `Core/Keyed/Enums.xml` (`TechLevel_Spacer`) |
| tech level — ultra | Ultra | `Core/Keyed/Enums.xml` (`TechLevel_Ultra`) |
| tech level — archotech | Archotech | `Core/Keyed/Enums.xml` (`TechLevel_Archotech`) |

### Prisoner, ideology, ritual, abilities

| English | Deutsch | Source file |
|---|---|---|
| prisoner | Gefangener | `Core/Keyed/Misc_Gameplay.xml` (`Prisoner`) |
| for prisoners (bed) | Für Gefangene | `Core/Keyed/GameplayCommands.xml` (`CommandBedSetForPrisonersLabel`) |
| prisoner interaction modes | rekrutieren, hinrichten, freilassen, nur am Leben erhalten, nur Widerstand reduzieren | `Core/DefInjected/PrisonerInteractionModeDef/*` (`AttemptRecruit`/`Execution`/`Release`/`MaintainOnly`/`ReduceResistance`) |
| warden (work / role) | Wärter | `Core/DefInjected/WorkTypeDef/WorkTypes.xml` (`Warden.pawnLabel` Wärter, `Warden.labelShort` Wärter) |
| recruitment resistance | Widerstand | `Core/Keyed/ITabs.xml` (`RecruitmentResistanceDesc` …Wenn der Widerstand auf 0 ist, kann der Gefangene rekrutiert werden.) |
| ritual | Rituale / Ritual | `Ideology/Keyed/MainTabs.xml` (`Rituals` Rituale) — plural; singular = **Ritual** |
| belief in (ideoligion) | Glaube an | `Ideology/Keyed/Misc_Gameplay.xml` (`BeliefInIdeo` Glaube an) |
| ability (psycast etc.) | Fähigkeiten | `Core/Keyed/Menus_Main.xml` (`Abilities` Fähigkeiten). For a single ability use the specific AbilityDef label |
| psyfocus | Psyfokus | `Royalty/Keyed/Misc_Gameplay.xml` (`Psyfocus`) |

### Weather, season, temperature, dates

| English | Deutsch | Source file |
|---|---|---|
| temperature | Temperatur | `Core/Keyed/Misc_Gameplay.xml` (`Temperature`) |
| weather | use the specific WeatherDef label | `Core/DefInjected/WeatherDef/*` |
| season — spring/summer/fall/winter | Frühling / Sommer / Herbst / Winter | `Core/Keyed/Time.xml` (`SeasonSpring`…`SeasonWinter`) |
| quadrum | use the specific quadrum label (Aprai, Jugust, Septober, Dezebruar) | `Core/Keyed/Time.xml` (`QuadrumAprimay` Aprai, `QuadrumJugust` Jugust, `QuadrumSeptober` Septober, `QuadrumDecembary` Dezebruar) |
| day(s) (lower) | Tage | `Core/Keyed/Time.xml` (`DaysLower`) |
| hour(s) (lower) | Stunden | `Core/Keyed/Time.xml` (`HoursLower`) |
| period — days (counted) | {0} Tage | `Core/Keyed/Time.xml` (`PeriodDays`) — **plain plural, NO `_numCase`; canonical proof of §3.2** |
| period — hours (counted) | {0} Stunden | `Core/Keyed/Time.xml` (`PeriodHours`) |
| year (abbrev.) | y | `Core/Keyed/Time.xml` (`LetterYear`) |

### Gizmo / command / button labels

| English | Deutsch | Source file |
|---|---|---|
| range | Reichweite | `Core/Keyed/Dialogs_Various.xml` (`Range`) |
| enable / activate | Aktivieren | `Core/Keyed/Menus_Main.xml` (`Enable`); `Core/Keyed/Misc_Gameplay.xml` (`Activate`) |
| disable / deactivate | Deaktivieren | `Core/Keyed/Dialogs_Various.xml` (`Disable`); `Core/Keyed/Misc_Gameplay.xml` (`Deactivate`) |
| enabled (state) | Aktiviert | `Core/Keyed/Menus_Main.xml` (`Enabled`) — used as a control state, gender-neutral (§3.4) |
| disabled (state) | Deaktiviert | `Core/Keyed/Menus_Main.xml` (`Disabled`) |
| on | An | `Core/Keyed/Misc_Gameplay.xml` (`On`) |
| off | Aus | `Core/Keyed/Misc_Gameplay.xml` (`Off`) |
| close (button) | Schließen | `Core/Keyed/Dialogs_Various.xml` (`CloseButton`) |
| cancel (button) | Abbrechen | `Core/Keyed/Dialogs_Various.xml` (`CancelButton`) |
| accept (button) | Akzeptieren | `Core/Keyed/Dialogs_Various.xml` (`AcceptButton`) |
| confirm | Bestätigen | `Core/Keyed/Dialogs_Various.xml` (`Confirm`) |
| OK | OK | `Core/Keyed/Dialogs_Various.xml` (`OK`) |
| yes | Ja | `Core/Keyed/Misc.xml` (`Yes`) |
| no | Nein | `Core/Keyed/Misc.xml` (`No`) |
| filter | Filter | `Core/Keyed/Misc_Gameplay.xml` (`Filter`) |
| sort by | Sort. nach: | `Core/Keyed/Dialogs_Various.xml` (`SortBy`) |
| select | Wähle … aus / Selektiere / auswählen | `Core/Keyed/Misc_Gameplay.xml` (`SelectNextInSquareTip` Wähle das nächste Objekt … aus); `Core/Keyed/Alerts.xml` (`ColonistNeedsTreatmentDesc` Selektiere dann …). Imperative (du) = **Wähle … aus** or **Selektiere**; infinitive label = **auswählen** |
| button (label word) | Schaltfläche | `Core/Keyed/Alerts.xml` (`ColonistNeedsTreatmentDesc` …auf die Schaltfläche … drückst); `Core/Keyed/Menus_Main.xml` (`UIVolumeTooltip` …wie z.B. Schaltflächen…) |
| hotkey | Tastenkürzel | `Core/Keyed/Misc_Gameplay.xml` (`SelectNextInSquareTip` (\*SectionTitle)Tastenkürzel:(/SectionTitle) {0}) |
| tab (UI) | Tab | `Core/Keyed/Misc_Gameplay.xml` (`ClickToViewInQuestsTab` …im Quests-Tab…); the game builds compounds like Körper-Tab, Bedürfnis-Tab, WAREN-Tab. **Reiter** is an acceptable synonym; prefer **Tab** |

### Pawn tab / inspection-category names — use the vanilla ITab label (LOCKED)

When one of our navigation categories corresponds to a real RimWorld pawn tab, use the **exact vanilla
tab label**, not a clearer synonym. The blind user hears our mod and the game's own tutorial/UI text,
so a mismatch (we say "Gesundheit", the game says "Körper") is jarring. Game-consistency wins.

| English category | Deutsch (vanilla tab) | Source file |
|---|---|---|
| Health | Körper | `Core/Keyed/ITabs.xml` (`TabHealth`) — **not "Gesundheit"** (that is the general noun) |
| Character | Bio | `Core/Keyed/ITabs.xml` (`TabCharacter`) — **not "Charakter"** |
| Gear | Inventar | `Core/Keyed/ITabs.xml` (`TabGear`) — **not "Ausrüstung"** |
| Needs | Bedürfnis | `Core/Keyed/ITabs.xml` (`TabNeeds`) — singular, as the tab ships |
| Social | Sozial | `Core/Keyed/ITabs.xml` (`TabSocial`) |
| Training | Training | `Core/Keyed/ITabs.xml` (`TabTraining`) |
| Log | Logbuch | `Core/Keyed/ITabs.xml` (`TabLog`) |
| Records | Aufzeichnung | `Core/Keyed/ITabs.xml` (`TabRecords`) |
| Prisoner (tab) | Gefängnis | `Core/Keyed/ITabs.xml` (`TabPrisoner`) — this is the tab; the *person* is **Gefangener** |

Categories with **no** vanilla tab equivalent (Storage, Power, Bills→Auftrag, Door Controls, …) are
mod-coined — translate them naturally; there is no game label to match.

### Styling / appearance vocabulary

The mod ships written style descriptions for hair, beard, and tattoo styles, plus appearance
inspection. Use the game's own style labels and category words.

| English | Deutsch | Source file |
|---|---|---|
| hair (concept) | Frisur | `Ideology/Keyed/Dialogs_Various.xml` (`Hair` Frisur) |
| hair color | Haarfarbe | `Ideology/Keyed/Dialogs_Various.xml` (`HairColor`) |
| beard | Bart | `Ideology/Keyed/Dialogs_Various.xml` (`Beard`) |
| tattoo | Tattoos / Gesichtstattoo / Körpertattoo | `Ideology/Keyed/Dialogs_Various.xml` (`Tattoos`, `TattooFace` Gesichtstattoo, `TattooBody` Körpertattoo) |
| color | Farbe | `Core/Keyed/Designators.xml` (`Color`) |
| individual hair/beard/tattoo style | use the specific DefInjected label | `Core/DefInjected/HairDef/*` (`Bald.label` Glatze, `Bowlcut.label` Topfschnitt, `BraidedKnot.label` geflochtener Knoten), `Core/DefInjected/BeardDef/*` (`BeardCurly.label` lockig, `Braided.label` geflochten) |
| style category (Tribal, Urban, Royal…) | use the specific StyleItemCategoryDef label | `Core/DefInjected/StyleItemCategoryDef/*` (`Tribal.label` Indigen, `Urban.label` Städtisch, `Royal.label` Königlich, `Punk.label` Punk, `Soldier.label` Soldat, `Rural.label` Ländlich) |

### DLC & extra domains (Biotech, Anomaly, Odyssey, Mechs, rooms, factions)

| English | Deutsch | Source file |
|---|---|---|
| xenotype | Xenotyp | `Biotech/Keyed/Misc_Gameplay.xml` (`Xenotype`) |
| gene | Gen | `Biotech/Keyed/Misc_Gameplay.xml` (`Gene`) |
| xenotype labels | Normal (Baseliner), Neandertaler, Husar, Dschinn, Impid, Ödländer, … | `Biotech/DefInjected/XenotypeDef/*` |
| mechanoid | Mechanoid | `Biotech/Keyed/ScenParts.xml` (`Mechanoid`) |
| overseer (mech) | Aufseher | `Biotech/Keyed/Misc_Gameplay.xml` (`Overseer`) |
| gravship (Odyssey) | Gravschiff | `Odyssey/Keyed/Misc_Gameplay.xml` (`Gravship`) |
| room roles | Schlafraum, Schlafsaal, Speiseraum, Krankenstation, Küche, Labor, Scheune, Haftraum, Haftsaal | `Core/DefInjected/RoomRoleDef/*` (`Bedroom`/`Barracks`/`DiningRoom`/`Hospital`/`Kitchen`/`Laboratory`/`Barn`/`PrisonCell`/`PrisonBarracks`) |
| faction relation — hostile/neutral/ally | Feindlich / Neutral / Verbündet | `Core/Keyed/MainTabs.xml` (`Hostile` Feindlich, `Ally` Verbündet); `Core/Keyed/Letters.xml` (`Neutral`) |
| mental state labels | use the specific MentalStateDef label (Berserker, Wutanfall, Fressattacke, Feuerteufel, …) | `Core/DefInjected/MentalStateDef/*` |
| quest | Quest | `Core/Keyed/Misc_Gameplay.xml` (`Quest`); main button `Quests.label` Quests |
| fishing (verb/activity) | Angeln | `Odyssey/Keyed/*` (`CommandAllowFish` Angeln erlauben) — **not "Fischen"** (corpus: Angeln 40× vs Fischen 2×). Compound nouns for the *fish* stay: Fischart, Fischinfo, Fischbestand |
| fishing zone | Angelzone | `Odyssey/DefInjected/ZoneDef/*` (`Zone_Fishing` Angelzone) — **never "Fischereizone"** (0 corpus hits) |
| minimum population (fishing) | Mindestbestand | `Odyssey/Keyed/*` (`MinimumPopulation` Mindestbestand); fish population = **Fischbestand** (`FishPopulation`). Not "Population" |

---

## Section 2 — Accessibility-specific vocabulary (mod-coined)

These concepts RimWorld does NOT name. We lock a German rendering here. Where a German screen-reader
convention exists (NVDA-de, JAWS-de, Windows-Sprachausgabe), it is noted and preferred so blind German
users hear familiar wording. Where the game's own UI offers a near-match, we reuse it and cite it.
German gaming/screen-reader usage keeps established anglicisms (Screenreader, Cursor, Menü, Tab, Slot,
Scanner) — do NOT force-Germanize them (see §3.5).

| English | Deutsch (locked) | Rationale / source |
|---|---|---|
| screen reader | Screenreader | Established anglicism in the German blind community (NVDA-de docs, forums). **Sprachausgabe** is the TTS-only fallback term; prefer **Screenreader** for the program |
| announce / announcement | ansagen / Ansage | Standard German screen-reader verb. NVDA-de "ansagen". Avoid "verkünden" |
| navigate / navigation | navigieren / Navigation | Standard German UI term; matches NVDA-de "Navigation" |
| cursor | Cursor | Established anglicism; NVDA-de announces "Cursor". No native game citation needed |
| menu | Menü | RimWorld uses it: `Core/DefInjected/MainButtonDef/MainButtons.xml` (`Menu.label` Menü) |
| tab (UI tab) | Tab | RimWorld uses it: `Core/Keyed/Misc_Gameplay.xml` (`ClickToViewInQuestsTab` …im Quests-Tab…). **Reiter** acceptable; prefer **Tab** |
| search | Suche / suchen | RimWorld uses it: `Core/Keyed/Dialogs_Various.xml` (`SearchTheMap` Suche etwas auf der aktuellen Karte) |
| typeahead search | Tippsuche / Suche durch Tippen | No fixed game term. Use **Tippsuche** (type-to-find). For the bare act, **Suche** |
| expand (tree) | ausklappen | RimWorld uses it: `Core/Keyed/Dialogs_Various.xml` (`ExpandAllCategories` Alles ausklappen). Matches NVDA-de "ausklappen" |
| collapse (tree) | einklappen | RimWorld uses it: `Core/Keyed/Dialogs_Various.xml` (`CollapseAllCategories` Alles einklappen). Matches NVDA-de "einklappen" |
| expanded (state) | ausgeklappt | NVDA-de announces "ausgeklappt". Gender-neutral participle used as a control state (§3.4) |
| collapsed (state) | eingeklappt | NVDA-de announces "eingeklappt" (also "reduziert"). Prefer **eingeklappt** to match the game verb |
| tree view | Baumansicht | NVDA-de announces tree controls as "Baumansicht" |
| node (tree node) | Knoten | Standard German UI term; NVDA-de uses "Knoten" |
| level (tree depth) | Ebene | NVDA-de announces tree depth as "Ebene". **Keep distinct from Stufe** (skill level, §1). e.g. "Ebene {0}" |
| toggle (verb/control) | umschalten | Standard German UI verb. For bare states reuse An/Aus or Aktiviert/Deaktiviert |
| on (state) | An | matches game `On` An (`Core/Keyed/Misc_Gameplay.xml`) |
| off (state) | Aus | matches game `Off` Aus (`Core/Keyed/Misc_Gameplay.xml`) |
| selected | Auswahl: {0} | Prefer the gender-safe noun recast **Auswahl: {0}** for unknown-gender targets (§3.4). "Ausgewählt: {0}" with a colon is also acceptable (colon = fixed label). Never bare declined "Ausgewählter {0}" |
| position ("X of Y") | {0} von {1} | RimWorld uses **von** for this: `Core/Keyed/Misc_Gameplay.xml` (`Relationship`, `ThingOfSource` {0} von {1}). See §3.1 |
| scanner | Scanner | Mod-specific feature; established anglicism, unambiguous in TTS |
| jump to (no target arg) | Zum Ort springen | **CORRECTED 2026-07-29 — "Gehe zu" is NOT the game's jump wording** (0 corpus hits as an action; the only "Gehe zum Monolithen" is Anomaly letter prose). Vanilla splits by whether a target placeholder is present. Without one it uses the infinitive: `Core/Keyed/Letters.xml` (`JumpToLocation` Zum Ort springen), `Core/Keyed/Misc_Gameplay.xml` (`WorldJumpTo` Zum Ort springen). Analogously **Zur Person springen** |
| jump to (with target arg) | Springe zu: {0} | du-imperative when a placeholder is carried: `Core/Keyed/MainTabs.xml` (`JumpToTargetCustom` Springe zu {lookup: {0}; decline; 3}). Our colon replaces vanilla's `{lookup: … ; decline; 3}` for gender safety (§3.4). Related shipped forms: `ClickToJumpTo` Klicke zum Springen zu:, `ClickToJumpToProblem` Klicke zum Hinspringen |
| edge / boundary ("already at top") | Anfang / Ende / Rand | "Already at top/bottom" → "Bereits am Anfang" / "Bereits am Ende"; generic boundary = **Rand** |
| stepper (numeric +/- control) | Wertregler | No game term. **Wertregler** describes the control; for limits reuse **Minimum** / **Maximum** |
| accessibility | Barrierefreiheit | Standard German term (a11y = Barrierefreiheit) |
| inspect / inspection | untersuchen / Untersuchen | Reuse game **Untersuchen**: `Core/DefInjected/MainButtonDef/MainButtons.xml` (`Inspect.label` Untersuchen) |
| button | Schaltfläche | **Locked: Schaltfläche** (not "Button"). The game itself uses **Schaltfläche** (`ColonistNeedsTreatmentDesc`) and NVDA-de announces every real button as "Schaltfläche" — saying "Button" would clash with what the screen reader already speaks. Do not use "Button" |
| slot | Slot | Established anglicism in German gaming; keep **Slot** (e.g. inventory slot). Do not use "Steckplatz" for gameplay |

---

## Section 3 — Style rules for translators

### 3.1 Genus & case — the gender trap (CRITICAL, most important rule for German)

Our strings are mostly **label-value pairs** and **composed fragments** built with `string.Format`
placeholders (`"{0}: {1}"`, `"{0} of {1}"`, `"Range: {0}"`). The value substituted into a placeholder
almost always arrives from the game **already inflected, in the nominative case, with its own gender**
(an item label, a pawn name, a status word). We **cannot control its Genus (der/die/das) or its
Kasus**. German declines articles (der/die/das, ein/eine/ein, den/die/das) and adjectives/participles
(-er/-e/-es, -en) to agree with the noun's gender and case. So we must choose sentence shapes that read
correctly with **any** inserted value, of any gender.

**Rules:**

1. **Prefer Label-colon-value.** `"Range: {0}"` → `Reichweite: {0}`. The colon decouples the noun from
   agreement — `{0}` is just appended and reads fine regardless of its gender.
2. **Never decline an article or adjective to agree with an inserted `{0}`.** The game won't tell us
   whether `{0}` is der Kolonist, die Waffe, or das Gewehr.
   - **Don't:** `Ausgewählter {0}` (assumes masculine; wrong for "die Waffe", "das Gewehr").
   - **Do:** `Auswahl: {0}` (noun + colon) or `Ausgewählt: {0}` (treat the participle as a fixed label
     before the colon, not an agreeing adjective).
3. **For "verb + object" commands, prefer the bare infinitive/imperative** the way the game does.
   RimWorld writes order buttons as plain infinitives (`Abreißen`, `Zähmen`) or du-imperatives
   (`Priorisiere {0}`) without forcing agreement.

**Concrete rewrites:**

| English source | Bad (forces Genus/Kasus on `{0}`) | Good (gender-safe) |
|---|---|---|
| `Selected {0}` | `Ausgewählter {0}` (assumes masc.) | `Auswahl: {0}` (noun + colon is safest) |
| `Jumped to {0}` | `Gesprungen zum {0}` (forces masc. dative "zum") | `Springe zu: {0}` or `Ziel: {0}` |
| `No {0} available` | `Kein {0} verfügbar` (forces masc. "Kein") | `{0}: nicht verfügbar` |
| `{0} expanded` | `{0} ausgeklappter Knoten` (agrees with {0}) | `{0}. Ausgeklappt.` (decoupled) or `Status: ausgeklappt` |
| `Selected weapon: {0}` | `Ausgewählte Waffe: {0}` only if the fixed noun "Waffe" is yours, not `{0}` — fine | (the fixed noun before the colon may be declined; only never the `{0}`) |

When in doubt, **insert a colon or period before/around the placeholder** — it sidesteps the whole
agreement problem and matches the game's own label-value habit (`Tastenkürzel: {0}`).

**How the game itself handles gender (verified in the corpus):** RimWorld German uses the inline
**three-way** gender tag `{ARG_gender ? maskulin : feminin : neutrum}`, e.g. `{0_gender ? der : die :
das}`, `{1_gender ? einen : eine : ein}`, `{FUEL_gender ? keinen verfügbaren : keine verfügbare : kein
verfügbares}` (all attested in `de_ref/Core/Keyed/*.xml`). Two-branch forms also appear where the game
only distinguishes two (`{TITLE_gender ? kein : keine}`). You may use these tags **only when the
placeholder genuinely carries a `_gender` tag** (a pawn/thing arg the game exposes as gendered — e.g.
`{PAWN_gender ? … : … : …}`). For a generic `{0}` item label you do **not** get a `_gender` tag, so
fall back to the noun/colon recasts above. **Order of branches is always maskulin : feminin : neutrum
— never reorder.**

### 3.2 Plurals — simple two forms, NO `_numCase` (CRITICAL)

**German does NOT use the `_numCase` tag.** Verified: the entire official German corpus (Core + all
DLCs) contains **zero** `_numCase` usages, and `LanguageWorker_German` provides no multi-case override
(defaults to 2). The game writes counted nouns as a plain `{0}` + plural noun — e.g. `PeriodDays` is
simply `{0} Tage` (`Core/Keyed/Time.xml`), `PeriodHours` is `{0} Stunden`.

**Therefore:**

- **Never write** `{N_numCase ? … : …}` — for German the grammar engine resolves a multi-branch
  `_numCase` tag to an **empty string** (the branch count won't match the language's case count). It
  would silently blank the value.
- For a single counted value, just use the plural noun: `{0} Kolonisten`, `{0} Objekte`, `{0} Tage`.

#### One / Many key PAIRS (when C# pre-selects the key by `count == 1 ? One : Many`)

Many of our strings split into a `…One` / `…Many` key pair, chosen in C# by `count == 1`. The `One`
key is only ever shown for count 1; the `Many` key covers **0, 2, 3, …**.

- **`…One` key → singular.** `{0} Objekt` (or `Ausgeklappt: 1 Objekt`).
- **`…Many` key → plural.** `{0} Objekte`.

German has clean two-form plurals; the C# already split singular vs plural for you. Fill `…One` with
the singular noun and `…Many` with the plural. **Always fill both keys** — never leave one blank, or
the mod falls back to English for that count. Note the singular/plural often differ only by article or
suffix (`1 Ergebnis` / `{0} Ergebnisse`, `1 Kolonist` / `{0} Kolonisten`, `1 Tag` / `{0} Tage`).

| English pair | Deutsch `…One` | Deutsch `…Many` |
|---|---|---|
| `{0} item` / `{0} items` | `{0} Objekt` | `{0} Objekte` |
| `{0} colonist` / `{0} colonists` | `{0} Kolonist` | `{0} Kolonisten` |
| `{0} match` / `{0} matches` | `{0} Treffer` | `{0} Treffer` (plural identical) |
| `{0} result` / `{0} results` | `{0} Ergebnis` | `{0} Ergebnisse` |
| `Expanded {0} item` / `…items` | `{0} Objekt ausgeklappt` | `{0} Objekte ausgeklappt` |

### 3.3 Orthography, capitalization & punctuation (German-specific)

- **Capitalize every noun (Substantiv).** German capitalizes all nouns, always — `Kolonist`,
  `Auswahl`, `Stimmung`, `Gebäude`, `Fähigkeit`, `Schaltfläche`. A frequent LLM error is lower-casing
  German nouns as if they were English. **Do not.** Verbs, adjectives, articles stay lower-case unless
  sentence-initial. Nominalized adjectives/verbs are nouns and get capitalized (`das Ausklappen`).
- **Keep ß/ss and all umlauts.** Preserve `ä ö ü ß`. **Never** transliterate to `ae oe ue ss`. `ß`
  after a long vowel/diphthong (`Straße`, `schließen`, `groß`), `ss` after a short vowel (`Fluss`,
  `muss`). The corpus uses `schließen`, `Schließen`, `groß` — match it.
- **Spell out — do NOT invent abbreviations.** The output is read aloud. A screen reader speaks `TP`
  as "te-pe" and `EP` as "e-pe", which is noise. Write the full word: **Trefferpunkte** (not TP/HP),
  **Erfahrungspunkte** (not EP/XP), **Prozent** where spoken naturally. Only keep an abbreviation the
  game itself ships AND that reads correctly aloud. When in doubt, spell it out.
- **Formal du-imperative spelling.** du-imperatives drop the -st ending: `Wähle`, `Drücke`, `Klicke`,
  `Gehe`, `Öffne` (not `Wählst`). See §3.5.
- **Quotation marks.** The corpus mixes German „…“ (typographic) and straight `'…'`/`"…"` around
  interpolated labels (e.g. `'Karawane neu formieren'`, `"Starte Animabaumverbindung"`). For quoting an
  interpolated `{0}` label, straight `'{0}'` or `"{0}"` is safest for TTS. Do not obsess over „ “.
- **Full German punctuation INSIDE a translated prose value.** A tooltip that is a real sentence takes
  normal German punctuation and ends with `.` (`… kann der Gefangene rekrutiert werden.`). Questions
  take `?`, and the game does use `!` intentionally in alerts — mirror the English/game only.
- **Composed fragment joiners stay ASCII.** The mod glues TTS segments with `". "`, `", "`, `": "`
  (e.g. `RimWorldAccess.TwoLevel.ButtonAnnouncement` = `{0}. Button. {1}`). **Keep these joiners as
  ASCII period/comma/colon + a single space** — including the `": "` in every `Label: Wert` recast.
  TTS engines and screen readers reliably treat `. ` / `, ` / `: ` as segment/pause boundaries, and the
  trailing space safely separates an adjacent number or token. Do NOT replace them with dashes, drop
  the space, or convert to „ “.

> Quick rule: prose *inside one translated value* → full German punctuation, capitalized nouns, ß and
> umlauts. Glue *between composed fragments* and *Label: Wert* colons → keep ASCII `. ` `, ` `: ` with
> the trailing space, exactly as in the English source.

### 3.4 Genus — gender-safe recasts (CRITICAL)

German marks gender/case on articles (der/die/das, ein/eine/ein, den/dem), on adjectives and
participles (-er/-e/-es/-en), and on possessives (mein/meine). When our string describes an action by
or on a pawn (or object) of **unknown** gender, do NOT pick a masculine/feminine/neuter form to agree
with the inserted `{0}`.

- **Prefer noun + colon over a declined participle.** "Selected {0}" → `Auswahl: {0}` (noun) rather
  than `Ausgewählter {0}` (assumes masculine, mis-agrees with die/das).
- **Use Label: Wert with a colon to decouple agreement** — the value is just appended and never has to
  agree (`Ziel: {0}`, `Status: {0}`, `Reichweite: {0}`).
- **Use the bare infinitive/du-imperative for commands** as the game does (`Abreißen`, `Zähmen`,
  `Priorisiere {0}`, `Behandle {0}`) — infinitives carry no article/gender.
- **For control states**, gender-neutral participles work for inanimate UI controls
  (`Aktiviert`/`Deaktiviert`, `An`/`Aus`, `ausgeklappt`/`eingeklappt`) — these describe a control, not
  a pawn, and stand alone without an article, so they are safe.
- **When the value is a gendered game pawn arg** (the game exposes a `_gender` tag), you may use the
  three-way tag `{ARG_gender ? maskulin : feminin : neutrum}` exactly as the corpus does
  (`{0_gender ? der : die : das}`, `{1_gender ? einen : eine : ein}`). Otherwise fall back to recasts.

| Situation | Bad (declined to agree with `{0}`) | Good (gender-safe) |
|---|---|---|
| select an unknown-gender thing | `Ausgewählter {0}` | `Auswahl: {0}` |
| jump to a thing | `Gesprungen zum {0}` | `Springe zu: {0}` / `Ziel: {0}` |
| "no X available" | `Kein {0} verfügbar` | `{0}: nicht verfügbar` |
| forbidden state on a thing | `Verbotener {0}` | `Status: verboten` or keep the game's fixed label |
| the target is X | `Der {0} ist das Ziel` | `Ziel: {0}` |

### 3.5 Tone / register — informal "du" (VERIFIED)

RimWorld's official German addresses the player with the **informal second person "du"**, verified by
grepping the corpus:

- **Pronouns/possessives:** `deine` (121×), `deiner` (121×), `deinen` (40×), `Du kannst` (62×). The
  formal `Ihre` appears only 6× (legacy strings) — a vanishing minority.
- **Imperatives:** `Wähle` (64×), `Klicke` (32×), `Suche` (10×), `Gehe` (3×), `Drücke` (2×) — all
  du-forms. Cited examples:
  - `SelectNextInSquareTip` — "**Wähle** das nächste Objekt im selben Feld aus. … **Du** erreichst
    denselben Effekt, wenn **du** … klickst."
  - `ClickToViewFactions` — "**Klicke**, um alle Fraktionen zu sehen."
  - `BreakRiskDescEnding` — "**Kontrolliere** den Bedürfnis-Tab … **Du kannst** ihnen …"

**Translators MUST use informal "du"** and du-form imperatives (`Wähle`, `Drücke`, `Klicke`, `Springe zu`,
`Öffne`, `Selektiere`). Other register notes:

- Match RimWorld's neutral, terse UI register: **infinitives/du-imperatives for command labels**
  (`Abreißen`, `Abbrechen`, `Priorisiere {0}`), **nouns for states** (`Stimmung`, `Gesundheit`).
- **No exclamation marks** unless the English/game has them. **No first person. No emoji.**
- When a tooltip is a full sentence in English, translate it as a full sentence ending with `.`. When
  it is a short label, keep it short — German runs long, so cut ruthlessly for TTS; the game's own term
  always wins over a longer periphrase.

### 3.6 Placeholders, line breaks, keys, spacing

- Copy `{0}`, `{1}`, `{NamedArg}`, `{PAWN_label}`, `{1_labelShort}`, `{ARG_gender ? … : … : …}`, etc.
  **byte-for-byte.** Never translate a placeholder, never add/remove braces, never renumber. The
  **set** of placeholders in a value must be identical to the English source.
- You **may reposition** a placeholder within a sentence for natural German word order (`string.Format`
  is positional; German verb-final/verb-second word order often needs it). Repositioning is encouraged
  where it reads better; renumbering or translating is forbidden.
- **Do NOT add `_numCase` tags** — German doesn't support them (§3.2).
- Copy `\n` line breaks exactly and keep them in the same logical spots.
- **XML keys are never translated** — keep every element name byte-for-byte identical to English, or
  the string silently fails to load.
- **XML comments are developer context — leave them in English.** Do not translate `<!-- … -->`.
- **Preserve leading/trailing spaces in values.** Some keys are suffixes that join onto a preceding
  label (e.g. a leading-space `" Ebene {0}"` or a trailing-space fragment). Keep the exact
  leading/trailing space.
- Where a number/placeholder abuts German text, keep a single normal space (`{0} Objekte`,
  `Ebene {0}`). Do not glue them together.

### 3.7 Sentence shape for TTS — heard once, never re-read (CRITICAL)

Every value in these files is **spoken aloud**. The user cannot glance back, re-scan a line, or see
punctuation. A sentence that is comfortable to *read* can be impossible to *follow by ear*. These
rules apply to the text inside a translated value; they say nothing about this document's own prose.

- **No Schachtelsätze.** One statement per sentence. Main clause first, at most **one** subordinate
  clause, and never a subordinate clause nested inside another. German pushes the finite verb to the
  end of a subordinate clause — the listener then has to hold the whole clause in memory before the
  verb resolves it. Keep subject and verb close together.
  - Bad: `Wenn der Kolonist, der gerade ausgewählt ist, keine Waffe trägt, die er benutzen kann, wird
    die Aktion abgebrochen.`
  - Good: `Der ausgewählte Kolonist trägt keine benutzbare Waffe. Die Aktion wird abgebrochen.`
- **No semicolons.** A `;` in a spoken string is almost always a sentence that should have been two.
  **Split it into two sentences** joined with `". "` — do not merely downgrade it to a comma, which
  keeps the over-long sentence intact. Screen readers give `;` little or no audible pause, so the two
  halves run together.
- **No Gedankenstrich as punctuation.** The Halbgeviertstrich `–` and Geviertstrich `—` are forbidden
  inside a value. Depending on the engine and punctuation level they are read as "Bindestrich",
  "minus", or dropped entirely — the intended pause is lost either way. Recast as two sentences, a
  comma, or the `": "` label form.
  - **The Bindestrich `-` in a compound stays and is correct:** `Bedürfnis-Tab`, `Karawanen-Route`,
    `Anti-Getreide-Sporen`. This rule bans the dash *as a sentence-level punctuation mark*, not the
    hyphen inside a word. Likewise the ASCII joiners `. ` `, ` `: ` from §3.3 are unaffected.
- **No parenthetical insertions mid-sentence.** An aside in brackets or between commas splits the
  sentence in two for the listener. Put the qualifier in its own short sentence, or drop it. A short
  trailing parenthesis on a *label* (`Bett (belegt)`) is fine — a parenthesis interrupting *prose* is
  not.
- **Front-load the information.** The listener stops the moment they have what they need. Put the
  thing being announced first and the qualifier after: `Gebäude beschädigt. 42 von 100 Trefferpunkte.`
  not `Mit 42 von 100 Trefferpunkten ist das Gebäude beschädigt.`
- **Say it, don't administer it — no Beamtendeutsch.** The game talks to a player, not a citizen
  applying for a permit. Grammatically flawless officialese is a defect here, and it is the easiest
  defect to wave through **precisely because nothing is wrong with it**. Prefer the everyday verb:

  | Amtlich / hölzern | Natürlich |
  |---|---|
  | ins Inventar abgelegt | verstaut |
  | eine Aktion durchführen | machen / tun |
  | die Schaltfläche betätigen | die Schaltfläche drücken |
  | Selektiere das Objekt | Wähle das Objekt aus |
  | erfolgt eine Benachrichtigung | du wirst benachrichtigt |
  | zur Verfügung stehen | verfügbar sein / es gibt |
  | in Kenntnis setzen | Bescheid geben |
  | unter Verwendung von | mit |

  Nominalstil is the tell: a chain of `-ung` nouns strung together with prepositions
  (`bei Durchführung der Beladung`) instead of verbs (`beim Beladen`). Rewrite it with the verb.

- **Read it aloud in your head before you commit it.** If you run out of breath, lose the thread, or
  it sounds like a form letter, it fails — regardless of how correct it is.

---

## Section 3.8 — Binding rulings from review (2026-07-29)

Settled by an arbiter against the corpus during the German review pass. Each was a real dispute
between translator and reviewer. **Do not re-litigate these**; apply them everywhere, not only in the
file where they came up.

**Terminology**

- **Körperfunktionen** for body capacities (`PawnCapacityDef`), never *Fähigkeiten*. Source:
  `Core/WordInfo/capacity.txt`, which keys all 19 capacities as `Körperfunktion '<label>'`. This keeps
  the three-way split clean: **Fertigkeiten** = skills, **Fähigkeiten** = abilities/psycasts,
  **Körperfunktionen** = body capacities. Without it the Info-Card tree announces two different
  sections with the identical word.
  - *Method note:* a term absent from `Keyed/` and `DefInjected/` is **not** proof the game ships no
    German word. Grep `Core/WordInfo/*.txt` before concluding that.
- **Berührungsreichweite** for touch range, in the corpus construction *„in Berührungsreichweite sein"*
  (`Core/DefInjected/InstructionDef/Instructions.xml`, 4 of 4 uses). Never paraphrase as *„direkt
  daneben"* or *„daneben stehen"* — bare *daneben* is heard as *danebengegangen*. Use the copula
  **sein**, not *stehen*: a touch-range target may be a building or an item, not a person.
- **nächstgelegen**, never *nächste*, for "nearest". RimWorld reserves *der/die/das nächste* for
  *next in sequence* (`Core/Keyed/Misc_Gameplay.xml`, `SelectNextInSquareTip`), so in a list the user
  pages through, *„die nächste zuerst"* is heard as *the next one first*.
- **Der Eindruck ist …** for "The look is …", never *„Der Look"*. Corpus: *Look* has 0 German hits,
  *Eindruck* 29 including the shipped stat label `Impressiveness.label` = Eindruck. An anglicism is
  admissible only where the game or the screen reader already speaks it (Screenreader, Cursor, Tab,
  Slot), never in descriptive prose where German has a shipped word.
- **"Auto-X" toggles use the game's own verb phrase, never a coined noun.** The caravan auto-provision
  toggle is vanilla's `autoSelectTravelSupplies` checkbox, which ships as
  **„Reisevorräte automatisch auswählen"** (`Core/Keyed/Dialogs_Various.xml`, `AutomaticallySelectTravelSupplies`;
  the tab itself is `TravelSupplies` = Reisevorräte). Use that phrase plus `": an"` / `": aus"`.
  *Versorgung* is wrong: in the corpus it means medical care (*medizinische/ärztliche Versorgung*) or a
  supply drop (*Versorgungssatellit*), never travel supplies. *Automatische Reisevorräte* is wrong too —
  every corpus `automatisch + Nomen` takes a **Vorgangsnomen** (Nutzung, Unterdrückung, Speicherung,
  Reparatur, Markierung, Kleiderwahl, Fütterung, Altersumkehr), never a concrete object noun. Where a
  noun is grammatically unavoidable, nominalize the vanilla verb: **automatische Auswahl (der Reisevorräte)**.
- **Compass directions are spelled out** in a spoken value: `Nord`/`Süd`/`Ost`/`West` as a coordinate
  suffix after *Grad*, `Norden`/`Süden`/`Osten`/`Westen` as a standalone noun (`Core/Keyed/Misc.xml`).
  The game's single-letter forms `N/S/O/W/NO/SO/SW/NW` (`Core/Keyed/Enums.xml`,
  `Direction8Way_*_Short`) are **visual only** and must never appear in a value we speak.

**Phrasing and grammar**

- **Never decline an article or preposition-article at a placeholder** — not even when we control every
  value it can hold. If a `Label: Wert` frame reads redundantly because the label word already appears
  inside the value (*Modus gewechselt: Geschenkemodus*), drop the redundant label word
  (`Gewechselt: {0}`) rather than switching to an article-bearing frame.
- **A toggle announcing its new state front-loads the state name** and appends the corpus status word:
  `{0} aktiv` (corpus: *Nur im Entwicklermodus aktiv*, *Bereits aktiv*). Never open such a value with a
  bare past participle label (*Gewechselt: {0}*, *Aktiviert: {0}*). A participle may only follow the
  thing it describes (*Schicht gewechselt: {0}*).
- **Never join a placeholder holding a bare noun** (Kauf, Verkauf, Geschenk) to a following noun phrase
  with a comma. Bind it with a preposition (`{0} von {1} Stück`) or a colon (`{0}: {1}`) — a screen
  reader turns the comma into a pause and the listener gets two unlinked fragments.
- **Two or more keyboard hints in one value are separate statements.** Join them with `". "`, never a
  comma, and keep each hint's verb object: *Alt+A schließt den Handel ab*, not *Alt+A schließt ab*.
  Object-less transitive verbs are ambiguous heard once, and the hint should predict the wording of the
  confirmation it triggers.
- **A keyboard hint whose value has already named two or more nouns must spell its purpose out as a
  `, um … zu …` clause with the object named.** *Drücke Alt+A erneut, um die automatische Auswahl
  abzuschalten*, not *Drücke erneut Alt+A zum Abschalten*: the value just said *Reisevorräte automatisch
  auswählen* **and** *Tab Reisevorräte gesperrt*, so a bare *zum Abschalten* leaves the listener guessing
  which of the two the key switches off. This is the §3.8 "keep each hint's verb object" rule applied to
  the Nominalstil variant — dropping the object into a nominalization hides it just as an object-less
  verb does. The corpus writes purpose clauses this way throughout (`Core/Keyed/*`: *Klicke, um alle
  Fraktionen zu sehen*, *Klicke, um die Tierzahl zu limitieren*, *Klicke darauf, um den Planmodus zu
  aktivieren* — 167 `, um … zu …` hits). The short nominal form the game also ships (`ClickToAdd`
  *Klicke zum Hinzufügen*, `PressAnyKeyOrEsc` *… Esc zum Abbrechen*) stays allowed **only** where the
  value names exactly one candidate object, or none at all. Additionally: when two keys describe the
  **same** shortcut (the On-state hint and the locked-tab hint for the same Alt+A), they must use the
  **identical** verb phrase, so the hint predicts the confirmation.
- **Do not say a person *ist/war so eingestellt*** for a setting that already held its value — with a
  personal subject, a bare *so* reads as the person's disposition. Use *bereits {Wert}* when the value
  can be named, or the anaphoric *hatte das schon* when it cannot. Reserve *eingestellt* for cases with
  an explicit target (*auf X eingestellt*).
- **"X listet N Dinge" needs the separable prefix: `auflisten`, never bare transitive `listen`.** Both
  corpus uses of the verb are `listet … auf` (`Anomaly/Keyed/*`: *Diese Übersicht listet alle bekannten
  Entitäten auf, …*; `Core/Keyed/*`: *Version {0} listet denselben Ordner mehr als einmal auf: {1}*);
  bare `listet {N} X` has **zero** hits and is heard as a raw rendering of English *lists*. Where the
  subject is a container rather than a view, `enthält` is the alternative — a Liste *enthält* Einträge,
  eine Übersicht/ein Scanner *listet sie auf*.
- **Do not stretch the `Label: Wert` colon form onto a counted noun with a trailing qualifier.** The
  colon recast exists only to dodge Genus agreement on a placeholder; where the placeholder is a bare
  number, write a normal main clause instead.

---

## Section 3.9 — Binding rulings, second review pass (2026-07-30)

Same status as §3.8: settled against the corpus, **do not re-litigate**, apply everywhere.

### Key names (LOCKED — an earlier contradictory ruling was superseded)

- **Arrow keys are `Pfeil nach oben` / `Pfeil nach unten` / `Pfeil nach links` / `Pfeil nach rechts`.**
  Never *Pfeil hoch/runter*, never bare direction words (*Auf und Ab*, *Links und Rechts*), never NVDA's
  full *Pfeiltaste nach oben*. Two independent reasons: RimWorld's own up/down is *nach oben* / *nach
  unten* (`ReorderBillUpTip`, `ReorderBillDownTip`; *runter* appears once in 1537 files), and **`hoch`
  is the game's word for HIGH** (`PowerConsumptionHigh` = hoch) — so *Pfeil hoch* collides with stat
  values the same feature speaks aloud. The collective plural **Pfeiltasten** stays.
- **Every other key keeps its German key-cap legend, not NVDA's spoken label:** Enter (not *Eingabe*),
  Escape, Leertaste (not *Leerzeichen*), Pos1, Ende, Bild auf / Bild ab (not *Seite nach oben/unten*),
  Entf, Umschalt. Rationale: the user must find and press a physical key. Arrow keys are the one group
  with no legend text, only glyphs, which is why they alone get a spoken description.

### Keyboard hints

- **Every hint is a finite-verb clause whose object is a named noun:** *Pfeil nach rechts klappt den Tab
  aus*, *Escape bricht die Zielauswahl ab*. Never the nominal *X zum Ausklappen* form, never an
  object-less verb (*Escape bricht ab*), never a pronoun standing in for the object.
- **When a value has already named two or more candidate objects,** a hint states its purpose as a
  *", um … zu …"* clause naming the object (*Drücke Alt+A erneut, um die automatische Auswahl
  abzuschalten*), not a bare *zum* + nominalized verb (*zum Abschalten*), which hides the object.
- The hint must be **wordwise identical to the standalone announcement** the same control speaks
  elsewhere, so the hint predicts what the action will say.

### Sentence structure (extends §3.7)

- **A Komma-Splice is never a valid §3.7 fix.** Every clause with its own finite verb gets its own
  sentence ended by `". "`. A *die/der/das* directly after a comma is doubly forbidden: the listener
  parses it as a relative pronoun attached to the preceding noun and has to reparse.
- **Never carry a personal pronoun across a sentence boundary** when another noun of the same Genus
  stands between it and its antecedent. Repeat the noun, even at the cost of looking repetitive on the
  page — the listener cannot look back. To avoid flat repetition pick a *different corpus-shipped*
  noun (*Der Flächeneffekt* after *Diese Fähigkeit*), never a coined one.
- **A fixed sentence frame must never contain a word its own placeholder can take.**
- **A count-plus-state announcement takes vanilla's noun-less form `{0} + Partizip/Adjektiv`**
  (`CountToDesignate` "{0} betroffen", `CaravanPawnsDowned` "{0} bewegungsunfähig"). Do not insert a
  Bezugsnomen the game itself omits, and do not use *Ziel/Ziele* for bystanders an effect merely
  reaches — the corpus reserves *Ziele* for intended targets.

### Vocabulary

| Concept | German | Note |
|---|---|---|
| use X on Y (item, serum, ability) | `{X} auf {Y} angewendet` | Frame vanilla ships in `Core/Keyed/Messages.xml` (`GoodwillChangedReason_UsedItem/UsedAbility`). Never *einsetzen* (corpus = implant a body part, deploy weapons/people); *benutzen* only for the order to use a thing (`UseGizmo`) |
| a targeted map cell | Ort | Vanilla prompt *Wähle … einen Ort aus* (`Anomaly/Keyed`, `PsychicRitual_LocationTargetting`). Never *Platz* (corpus = space/room). Refer back with *dort*; a scanner-list entry is *die gewählte Fläche* |
| targeting mode (mod-coined) | Zielauswahl | No game noun exists; locked |
| missing prerequisite | `Am Cursor fehlt: {0}` | Corpus idiom (`MissingGenepackForXenogerm`, *Dach fehlt*). *gültiges Ziel* per `NoValidDestinationFound`, never *passendes Ziel* |
| X lists N things | `X listet N Dinge auf` | Separable prefix (2 corpus hits for *listet … auf*, 0 for bare transitive *listet*). *enthält* only when the subject is a container |
| auto-X toggle | game's verb phrase + `: an` / `: aus` | e.g. `AutomaticallySelectTravelSupplies` = *Reisevorräte automatisch auswählen*. Where a noun is unavoidable, nominalize the verb (*automatische Auswahl*). Never coin a noun label — RimWorld German writes automatisch/Auto- only with a Vorgangsnomen or a verb |
| top of the head / crown | **oben am Kopf** / **oben auf dem Kopf** (adverbial, NOT a coined noun) | **REVISED 2026-07-30, supersedes the earlier "Oberkopf" ruling.** English `crown` (49 uses) and `top of the head` (11) are both everyday English, not trade terms, and the source itself alternates between them. **Oberkopf** is a genuine German hairdressing term but a *specialist* one: a German native speaker on this project did not know it. Translating an everyday source word with a trade word shifts the register upward and is a defect, however correct the trade word is. Reword the sentence instead of hunting for a noun: *bedeckt den Oberkopf* becomes *liegt oben auf dem Kopf*, *der ganze Oberkopf bleibt kahl* becomes *oben bleibt der Kopf ganz kahl*, *am Oberkopf ist sie am vollsten* becomes *oben am Kopf ist sie am vollsten*. **Kopfoberseite** is a permitted fallback where a noun is truly unavoidable |
| back of the head | Hinterkopf | Stays. Ordinary German that needs no explanation, and the English is the equally plain *back of the head* — the register matches, which is exactly what *Oberkopf* failed to do |
| parting line in the hair | Scheitel | Reserved **exclusively** for the parting (Mittelscheitel, Seitenscheitel, gescheitelt, *ohne Scheitel und ohne Pony*). Never for the region on top, even though colloquial German allows it: the same value often also names the parting, and a listener who cannot look back has no way to tell the two apart |
| head region | **Wirbel** is banned | The official corpus uses *Wirbel* 70 times, without exception for a vertebra (Wirbelsäule, Wiedergängerwirbel, Wirbelsäulenprothese). In a game whose screen reader constantly reads out body parts, this would be an active misreading |
| bowl cut | Topfschnitt | `Core/DefInjected/HairDef/HairsGeneral.xml`, `Bowlcut.label`. *Schalenschnitt* / *Schalenform* have 0 corpus hits and are banned |
| bed use / owner type | Bettdefinition | Vanilla `ChangingOwnerTypeBedWarning` (`Core/Keyed/GameplayCommands.xml`) renders English *bed use* this way. Never the coined *Bettart* or *Bettnutzung*. Its three states take the vanilla gizmo labels **Für Kolonisten / Für Gefangene / Für Sklaven** (`CommandBedSetFor*Label`), not a bare declined role noun |
| unlocks (research) | Schaltet frei / Schaltet nichts frei | `Core/Keyed/Misc.xml`. **Where vanilla ships a verb phrase for a UI label, keep the verb and negate it for the empty case.** Do not nominalize it into a coined `-ung` noun (*Freischaltungen*, 0 corpus hits) merely to make sibling tree labels grammatically parallel. Vanilla's own asymmetry between *Voraussetzung(en)* (noun) and *Schaltet frei* (verb) is the pattern to match |

### Style-description prose

- **`Der Eindruck ist …` stays the default for "The look is …"** and the ban on *Der Look* is absolute
  (§3.8) — **but** where the predicate names a *physical property* rather than an evaluation (geteilt,
  kurz, lang), name the thing itself: *Die Frisur ist …*, *Der Bart ist …*, *Die Form ist …*. An
  Eindruck can be *überwältigend* but never *kurz*, and *der Eindruck ist geteilt* is misheard.
- **`wirken` takes a predicative adjective** (*wirkt massiv*) or *wie* + noun phrase (*wirkt wie ein
  Käfer*) — never a bare prepositional phrase (*wirkt stark in Bewegung*). Render "has a strong sense
  of X" as *steckt voller X* or *wirkt* + adjective.
- **Name a shape once with a corpus-attested noun** (Zacken, Büschel, Strähne) and **repeat that exact
  noun** for every later reference. Never switch synonyms mid-value. *Schopf* is banned for one of
  several clumps — 0 corpus hits, and it denotes the single head of hair a person has.
- **Never coin a German word for a style the game already labels.** Grep `DefInjected/` first and reuse
  the shipped label verbatim, then use that one word for that one shape across every key in the file.

---

## Section 3.10 — Binding rulings, first full-coverage pass (2026-07-30)

Same status as §3.8 and §3.9: settled against the corpus, **do not re-litigate**, apply everywhere.

### Vocabulary

| Concept | German | Note |
|---|---|---|
| apply (a brush, colour, setting to a target) | anwenden | Vanilla ships it as the button verb (`SaveAndApply` = *Speichern & Anwenden*) and as the action frame `{0} auf {1} angewendet`. So write *X auf Y anwenden*. Never *übertragen* (0 corpus hits, means transfer/copy and collides with Kopieren/Einfügen), never *auftragen* (0 hits, governs the medium rather than the tool) |
| fuel level / capacity / consumption of a machine | Treibstoff | `StoredChemfuel` = *Treibstoff getankt*, `ConfiguredTargetFuelLevel` = *Max. Treibstoff: {0}*. Takes **no** unit noun after its numbers: `Treibstoff: {0} von {1}`, never *… von {1} Sprit* |
| Chemfuel as a counted item | Sprit | `Chemfuel.label` = Sprit, `FuelPerTile` = *{0} Sprit*. **Treibstoff and Sprit are not interchangeable** — level versus goods |
| permanently hostile | Dauerhaft feindlich | `PermanentEnemy.label`. Take a multi-word game label **verbatim** even when the preceding placeholder repeats one of its words. The §3.9 ban on putting a placeholder's own word into the fixed frame covers single redundant label words carrying no information, never half of a shipped game term |
| prisoner / slave status of a named pawn | `{0}: nicht inhaftiert` | **Never** a declined noun phrase (*kein Gefangener*, *ein Sklave*). Vanilla gender-tags every such predicate (`{PAWN_gender ? ein Gefangener : eine Gefangene}`) and our placeholder carries only a label string, so use the corpus participles instead: **inhaftiert** (`RequiresImprisonment`, `Arrest`) |

### Announcement shapes

- **A confirmation of a chosen item, mode or option uses the label-colon form** `Ausgewählt: {0}` or
  `Auswahl: {0}`. Never a predicate like *{0} aktiv.* or *{0} ist gewählt.* — the placeholder usually
  holds a vanilla infinitive or negated label (*rekrutieren*, *nur Widerstand reduzieren*, *Keine
  Interaktion*) that cannot grammatically carry a predicate.
- **A standing marker on a one-of-N list row is `aktuell`**, not *ausgewählt*: it marks the single
  value a property currently has (the applied hairstyle, the paint colour, the ideo icon). Reserve
  **ausgewählt** for membership in a multi-select set (picked memes, selected colonists) or as
  confirmation of an act the user just performed (`{0} ausgewählt`).
- **A blocked action with exactly one nameable cause states the cause** using vanilla's `X hat kein/keine Y`
  frame (*Dieser Mech hat keine Auto-Reparatur*). Reserve `X nicht möglich` for blocks whose cause
  cannot be named, and never sacrifice the reason just to keep two sibling messages shaped alike.
- **A "press KEY to do X" prompt is a du-imperative with the key named up front** and the purpose as a
  *", um … zu …"* clause naming the object: *Drücke zuerst Enter, um die Nachricht zu öffnen*. Never
  the key trailing as *… mit Enter*, never the nominal *… zum Öffnen*. Binds all four `OpenFirst` keys
  (History, MainMenu, UI, Quests).
- **A settings checkbox label states the behaviour the way RimWorld German does:** object plus
  infinitive (*Echtzeituhr anzeigen*) or a finite clause (*Zoom wechselt Weltkartenschicht*). Never a
  coined adjective-plus-noun head word. Wrap-around navigation is therefore *Navigation springt vom
  Ende zum Anfang (und umgekehrt)*; both *umlaufend* (0 cyclic corpus hits, Umlaufbahn = orbit) and
  *endlos* (corpus = infinite, never cyclic) are banned.
- **A suffix fragment the code appends bare onto a preceding announcement opens with the ASCII joiner
  `". "`** whenever the fragment is its own statement. The preceding fragment often ends in a bare
  number (*Ebene {0}*, *{0} von {1}*), and a capitalized German noun running straight onto a number is
  heard as a quantity (*Ebene 2 Infokarte*). Keep the single space after the period per §3.6.

## Section 3.11 (2026-11-04): Binding rulings, catch-up pass

Same status as §3.8 to §3.10. They are settled. Do not re-litigate them and apply them everywhere.

- **Pawn: Spielfigur or Person, chosen by who can be meant.** Write **Spielfigur** whenever the pawn
  can also be an animal or a mechanoid. That covers all combat autopilot text, drafted pawns, pawn
  tables and generic pawn regions. Write **Person** only where the text can mean nothing but a human,
  such as the character editor or the work tab.
- **The off state of an option opens with *Wenn deaktiviert,*.** This is the opener RimWorld German
  ships for a disabled setting, with 14 corpus hits. Never *Ist diese Option aus* and never *Wenn du
  das ausschaltest* in an option description.
- **Schedule hours are slot numbers, not clock times.** One slot is *Stunde {0}* and a run is
  *Stunden {0} bis {1}*. Never write *Uhr* for them. A run ends at the end of its last slot, so *bis
  21 Uhr* would sound as if it ended an hour early.

## Section 3.12 (2026-11-05): Binding rulings, catch-up review

Same status as §3.8 to §3.11. They are settled. Do not re-litigate them and apply them everywhere.

### Values that stay identical to English

- A value made only of placeholders, the ASCII joiners `. ` `, ` `: `, symbols such as `{0}%` or
  `…`, axis letters or a key name whose German legend matches (Alt, Tab, Enter, Escape, Plus, Minus)
  stays byte-identical to English. A checker that flags it as untranslated is wrong, not the value.
- A word the corpus ships unchanged as German stays unchanged, for example *Details*.

### Counts without a One/Many key pair

- §3.8 bans the *Label: Wert* form for a bare count. One exception applies. If the code passes a
  count that is often 1 into a single key, write the count as a label after a participle or
  adjective plus plural noun: *Neu durchsuchte Karten: {0}*. This avoids *1 Karten*. Use it only
  where the plain form would produce that grammar error.
- A count of generic items with no key pair takes the invariant noun *Stück*: *{1} Stück*.
- A count after a placeholder that already holds parentheses gets its own fragment after `. `,
  never a second trailing parenthesis.

### Vocabulary

| Concept | German | Note |
|---|---|---|
| cell of a table or grid | Zelle | *Feld* stays reserved for a map cell or world tile |
| target fuel level | *die maximale Treibstoffmenge* in a sentence, *Max. Treibstoff: {0}* as a label | `CommandSetTargetFuelLevelDesc`, `ConfiguredTargetFuelLevel`. Never *das Maximum für Treibstoff* |
| reroll a pawn | zufällig neu generieren | `WarnChangingXenotypeWillRandomizePawn`. Never the menu label *Zufällig* as the object of a verb |
| gene that loses its conflict group | unterdrückt | Xenotype editor, as in `GeneLeftmostActive`. Keep *überschrieben* for a xenogene overriding a germline gene |
| catch-all heading (More, Other, Extras) | Sonstiges | `Miscellaneous.label`. *Weitere* only with its noun |
| additive stat modifier | Offset, paired with Faktor | Corpus *Qualitätsoffset*. *Versatz* has zero hits for this and is banned |
| severity of a health condition | Schwere | `ConfigurableSeverity`. Never *Schweregrad* |
| unarmed | unbewaffnet | Corpus *unbewaffneter Kampf*. Never *ohne Waffe* |
| while drafted | im Kampfmodus | Vanilla wording. Never *Kampfende* or *bis zum Ende des Kampfmodus* |
| a named pool of points | `<Bereich>punkte` | *Forschungspunkte*, *Entwicklungspunkte*. Never a bare *Punkte* where two pools exist |
| learning board (Progression: Educations) | Lerntafel | Never a bare *Tafel*, never *Schultafel* |
| a limit was reached | *das Maximum von {0} X* | Keep *die maximale Anzahl* for descriptions of a setting |
| a command that sets a quantity | `-menge` compound plus *festlegen* | *Nachlademenge festlegen*. Never *Menge zum automatischen Nachladen* |
| a row that represents a thing | steht für | *Jede Zeile steht für einen Teil des Aussehens*. Never *ist ein* |
| moving through list or scanner entries | navigieren, blättern | Never *auswählen*, which is a deliberate select act |
| a jump hint without a target placeholder | Zum Ort springen, Zur Person springen | Word for word the jump button label of the same feature. Never a bare *Springen* |
| choosing a multi-cell area | einen Bereich auswählen | Vanilla `CommandCopyPlanSelectionMouse`. Never the mouse-only *aufziehen* |
| a queued order | *{0}: Angriff auf {1} eingereiht.* | The order is the subject, with vanilla's *eingereiht* |
| removing a designation flag | abwählen | *Als Standard-Fernkampfwaffe abwählen*. Never *aufheben*, which means picking an item up |
| a leftover count after an action | *Noch {N} X.* | *Verbleibende X: {N}* stays for static labels |
| dev tool windows | an everyday German name | *Feinjustierung* for Tweak values. Every key naming the same window uses the same value |
| confirm | bestätigen takes the choice or target as object | Never the picked item. An item placed at the map cursor is placed *am Kartencursor* |

### Announcement shapes

- A settings label and the toggle announcement for the same setting use identical wording. The
  toggle only adds `: an` or `: aus`. A hotkey that toggles a setting announces that label too.
- An option and the state it produces use the same words: *Unbewaffneten Kampf erzwingen* leads to
  *Unbewaffnet erzwungen*. A value spoken in several places uses the same noun phrase everywhere.
- A preview of a change not yet applied says *bisher {0}, neu {1}*. Never *jetzt*, which marks a
  state already reached.
- A refusal to set a value reads *{Spalte} lässt sich nicht auf {Wert} setzen*.
- A short confirmation of the player's own action is object plus participle: *Log geleert*,
  *Logfenster verlassen*. Never a full du-sentence.
- Say *Pfeiltasten* only when all four arrows do the same thing. Otherwise name each pair with its
  locked key name and its own function.
- Hints in one value that trigger the same kind of action reuse one verb. A hint names its target
  with the exact noun the confirmation will speak.
- Never hardcode a label that a third-party mod supplies at runtime. Refer to it by role or
  position, for example *ab der nächsten Stufe*.
- Translate a third-party setting by what the mod's code and setting title say it does, never by a
  literal reading of its internal field name.

### Sentence structure

- Never open a sentence with *Ihre*. The listener hears the formal *Sie*. Keep the possessive
  inside the sentence that names the person.
- A settings label names the affected objects first and puts the trigger after them as a verb-based
  *beim* phrase: *beim Beenden des Kampfmodus*.
- A checkbox that switches between two targets names the checked one first: *Xenogene statt
  Keimbahngene bearbeiten*.
- A reason in a label's trailing parenthesis is a *da* clause with a subject and a finite verb:
  *(nicht bearbeitbar, da Änderungen beim Laden verloren gehen)*.
- Render "bypassing the normal X" as a trailing *ohne die üblichen X*. Never *entfällt*.
- An English gerund subject becomes a German main clause with a noun subject: *Deine Auswahl
  wechselt X*. Never a leading *Wenn du* clause.
- All location phrases go into one clause without a comma, the most specific place first.
- A negative message whose positive sibling starts with the same words puts the negation right after
  the verb, never at the end in a genitive.
- A list of three or more items never stands between a verb and its particle or its zu-infinitive.
  Settle the verb first with a summary word and give the list after `: `.
- Where a *welche X dürfen* clause would put two plural nouns of identical case side by side, use the
  passive: *welche Tiere gejagt werden dürfen*.
- A demonstrative such as *diese Einstellung* needs a noun the value has already spoken.
- Examples in a sentence of their own need a finite verb: *Solche X sind zum Beispiel A oder B*.
  Never a verbless *Zum Beispiel X.*

---

## Section 4 — How to extend this glossary

To find how RimWorld translates a term you don't see above:

```bash
# UI strings (button labels, menu text, gameplay commands):
grep -rhoE '<EnglishKeyName>[^<]*</EnglishKeyName>' de_ref/*/Keyed/*.xml

# Or search by a German word you suspect (find the key it lives under):
grep -rl 'deutsches_Wort' de_ref/*/Keyed/*.xml

# Concept labels (things, designators, factions, skills, needs, biomes…) use a .label suffix:
grep -rhoE '<KeyName\.label>[^<]*</KeyName\.label>' de_ref/*/DefInjected/**/*.xml

# Confirm German never uses _numCase (should return nothing):
grep -rl '_numCase' de_ref/*/Keyed/*.xml

# Confirm the du register on a verb you're unsure about:
grep -rhoE '\b(Wähle|Klicke|Drücke|Du kannst|deine)\b' de_ref/*/Keyed/*.xml | sort | uniq -c

# Inspect the game's three-way gender tags before reusing one:
grep -rhoE '\{[A-Za-z0-9_]+_gender \? [^}]*\}' de_ref/*/Keyed/*.xml | sort | uniq -c
```

**Note on DefInjected:** concept labels are stored as `<DefName.label>` (dotted), not `<DefName>`. A
plain `<Structure>` grep finds nothing; grep `<Structure.label>` or the whole file.

Useful sub-paths: `Keyed/Designators.xml` (order verbs), `Keyed/GameplayCommands.xml` +
`Keyed/FloatMenu.xml` + `Keyed/Misc_Gameplay.xml` (commands), `Keyed/Dialogs_Various.xml` +
`Keyed/Misc.xml` + `Keyed/Menus_Main.xml` (general UI/buttons), `Keyed/Enums.xml` (tech levels,
medical-care categories), `Keyed/Time.xml` (calendar), `Keyed/Skills.xml`, `Keyed/Alerts.xml`,
`Keyed/ITabs.xml`, `Keyed/MainTabs.xml`, `DefInjected/WorkTypeDef`, `DefInjected/SkillDef`,
`DefInjected/NeedDef`, `DefInjected/DesignationCategoryDef`, `DefInjected/MainButtonDef`,
`DefInjected/PawnCapacityDef`, `DefInjected/PrisonerInteractionModeDef`, `DefInjected/RoomRoleDef`,
`DefInjected/MentalStateDef`, `DefInjected/HairDef`, `DefInjected/BeardDef`, `DefInjected/TattooDef`,
`DefInjected/StyleItemCategoryDef`, `DefInjected/XenotypeDef`, `DefInjected/BiomeDef`,
`DefInjected/WeatherDef`, `DefInjected/FactionDef`.

Always cite the file you took a term from when you add a row. **Never add a `_numCase` tag (§3.2), keep
the informal du register (§3.5), capitalize nouns (§3.3), use three-way gender tags only on a real
`_gender` arg and otherwise recast to Label: Wert (§3.1/§3.4).**
