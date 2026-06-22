# Latin American Spanish (Español Latinoamérica) Terminology & Style Glossary — RimWorld Access

This glossary locks in consistent Latin American Spanish wording for translating the RimWorld Access
screen-reader mod. The goal is that Spanish players feel the mod is a seamless extension of
RimWorld itself, so **every game-anchored term below uses the EXACT word RimWorld's own official
Latin American Spanish translation uses**, with a citation to the reference file it was taken from.

The strings we translate are **spoken aloud by a TTS engine**, not displayed. Natural, terse,
unambiguous phrasing matters more than visual polish.

RimWorld language folder name: `SpanishLatin` (native name `Español(Latinoamérica)`). Reference
corpus: RimWorld's official Latin American Spanish, extracted to `/tmp/es_ref/`
(Core + Royalty + Ideology + Biotech + Anomaly + Odyssey). Paths below are relative to `/tmp/es_ref/`.

> How to use this file: grep for the English term. If a term you need is not here and RimWorld has
> it, grep the game corpus yourself (see Section 4) and add it — never invent a rendering for a
> concept the game already names.

> **THE THREE MOST IMPORTANT RULES (details in §3):**
> 1. **No `_numCase`.** Spanish uses simple 2-form plurals. Our paired `…One`/`…Many` keys: fill
>    `…One` = singular, `…Many` = plural. NEVER use `{N_numCase ? … : …}` — it resolves to empty
>    string for Spanish. (§3.2)
> 2. **Gender-safe recasts.** For unknown-gender pawns/objects, use noun/`label: value` shapes
>    (`Selección: {0}`), not gendered participles (`Seleccionado {0}`). (§3.4)
> 3. **Informal "tú".** RimWorld's Latin American Spanish addresses the player with informal **tú**
>    (`Haz`, `Selecciona`, `puedes`, `tu base`). Use tú throughout. (§3.5)

---

## Section 1 — Core game vocabulary (game-anchored)

Every row is the term RimWorld itself ships in Latin American Spanish. Use it verbatim. These are
the words the player already hears from the game, so our mod must match them exactly.

### People, factions, world

| English | Español | Source file |
|---|---|---|
| colonist (colony member) | colono | `Core/Keyed/Misc.xml` (`Colonist` colono); `Core/Keyed/Alerts.xml` (`ColonistNeedsRescue` ¡Un colono necesita rescate!; `ColonistsIdle` {0} colonos ociosos) |
| colony (your base) | colonia | `Core/Keyed/Misc_Gameplay.xml` (10× colonia). Note: a foreign base/settlement is **asentamiento** (`GiveGiftViaTransportPodsTradeRequestWarning` …el asentamiento los aceptará…) |
| pawn / character (generic) | personaje | `Core/Keyed/Misc_Gameplay.xml` (`ShowBeautyToggleButton` …un personaje parado…); `Core/Keyed/GameplayCommands.xml` (`CommandClearPrioritizedWorkDesc` El personaje regresará…) |
| faction | facción | `Core/Keyed/Misc_Gameplay.xml` (`DropThingLodger` …facciones temporales…; `RenounceTitleWillLosePermitPoints` …con la facción {FACTION_name}) |
| caravan | caravana | `Core/Keyed/Misc.xml` (`Caravan` Caravana); `Core/Keyed/Misc_Gameplay.xml` (`CaravanMeeting_TradeIncapable` …miembro de la caravana…). Used for both player expeditions and trader caravans |
| raid (event) | Asalto | `Core/Keyed/Incidents.xml` (`Raid` Asalto); `Core/DefInjected/RaidStrategyDef/*` (`ImmediateAttack.letterLabelEnemy` Asalto, `Siege.letterLabelEnemy` Asedio) |
| raider | asaltante | `Core/Keyed/Incidents.xml` (`EscapeShipFound`, `HibernateWarning` …bandas de asaltantes desesperados…) |
| map | mapa | `Core/Keyed/Misc.xml` (`Map` Mapa) |
| tile / map cell | cuadro | `Core/Keyed/Misc_Gameplay.xml` (`SelectNextInSquareTip` …la siguiente cosa en el mismo cuadro). For a single map cell use **cuadro** |
| region (map region) | región | `Core/Keyed/Incidents.xml` (`BeaversArrived` …acaba de llegar a tu región…). RimWorld doesn't surface "region" as a distinct UI label; use **región** |
| zone | Área | `Core/Keyed/Misc.xml` (`Zone` Área); `Core/DefInjected/DesignationCategoryDef/*` (`Zone.label` áreas). Note: RimWorld Spanish renders both "zone" and "area" as **área** |
| area (map area) | área | `Core/Keyed/Misc_Gameplay.xml` (`AreaLower` área) |
| world / planet | mundo | `Core/Keyed/Misc_Gameplay.xml` (`ShowBeautyToggleButton` …la belleza del mundo…) |
| prisoner | prisionero | `Core/Keyed/Misc_Gameplay.xml` (`Prisoner` Prisionero, `PrisonerLower` prisionero) |

### Designators / order verbs (infinitive form, as the game uses them on buttons)

| English | Español | Source file |
|---|---|---|
| cancel | Cancelar | `Core/Keyed/Designators.xml` (`DesignatorCancel`) |
| chop wood / harvest wood | Talar | `Core/Keyed/Designators.xml` (`DesignatorHarvestWood`) |
| mine | Minar | `Core/Keyed/Designators.xml` (`DesignatorMine`) |
| mine vein (mark) | Minar veta | `Core/Keyed/Designators.xml` (`DesignatorMineVein`) |
| harvest | Cosechar | `Core/Keyed/Designators.xml` (`DesignatorHarvest`) |
| cut plants | Cortar plantas | `Core/Keyed/Designators.xml` (`DesignatorCutPlants`) |
| deconstruct | Desarmar | `Core/Keyed/Designators.xml` (`DesignatorDeconstruct`) |
| uninstall | Desinstalar | `Core/Keyed/Designators.xml` (`DesignatorUninstall`) |
| haul | Transportar cosas | `Core/Keyed/Designators.xml` (`DesignatorHaulThings`). For the bare verb use **Transportar** |
| hunt | Cazar | `Core/Keyed/Designators.xml` (`DesignatorHunt`) |
| tame | Domesticar | `Core/Keyed/Designators.xml` (`DesignatorTame`) |
| slaughter | Sacrificar | `Core/Keyed/Designators.xml` (`DesignatorSlaughter`) |
| forbid | Prohibir | `Core/Keyed/Designators.xml` (`DesignatorForbid`) |
| unforbid / allow | Permitir | `Core/Keyed/Designators.xml` (`DesignatorUnforbid`) |
| claim | Reclamar | `Core/Keyed/Designators.xml` (`DesignatorClaim`) |
| strip | Desvestir | `Core/Keyed/Designators.xml` (`DesignatorStrip`) |
| smooth (surface) | Alisar superficie | `Core/Keyed/Designators.xml` (`DesignatorSmoothSurface`) |
| plan | Planificación | `Core/Keyed/Designators.xml` (`DesignatorPlan`). The plan area as a concept = **planificación** |
| build | Construir | `Core/Keyed/Misc_Gameplay.xml` (`CommandBuildCopy` Construir copia; `BuildRoof` Construir techo) |
| prioritize | Priorizar | `Core/Keyed/GameplayCommands.xml` (`PrioritizeGeneric` Priorizar {0} {1}; `PrioritizeGenericSimple` Priorizar {0}) |
| tend (a wound) | Atender | `Core/Keyed/GameplayCommands.xml` (`Tend` Atender a {0}; `CanTendNow` Puede atenderse ahora) |
| draft / undraft (combat mode) | Reclutar (toggle) | `Core/Keyed/Misc_Gameplay.xml` (`CommandDraftLabel` Reclutar; `CommandToggleDraftDesc` Alterna el reclutamiento). **WARNING: the game uses "Reclutar" for the DRAFT toggle, which collides with "recruit". For draft STATE use está/no está reclutado(a)** (`IsNotDraftedLower` …no está reclutado(a)). Disambiguate by context: combat draft vs prisoner recruiting |
| recruit (a prisoner) | reclutar | `Core/Keyed/Misc_Gameplay.xml` (`RecruitSuccess` {0} ha reclutado con éxito a {1}…; `RecruitmentResistanceDesc` …puede ser reclutado). Same verb as draft; use the prisoner context to keep them distinct |
| claim/assign owner | Designar / Asignar | `Core/Keyed/Misc_Gameplay.xml` (`CommandBedSetForPrisonersLabel` Designar para prisioneros) |

### Buildings, zones, areas, build categories

| English | Español | Source file |
|---|---|---|
| growing zone | Área de cultivo | `Core/Keyed/Misc_Gameplay.xml` (`GrowingZone`) |
| stockpile / storage zone | Área del almacén / almacén | `Core/Keyed/Misc_Gameplay.xml` (`Stockpile` Área del almacén, `StockpileGroup` almacén) |
| home area | área del hogar | `Core/Keyed/Designators.xml` (`DesignatorAreaHomeExpand` Expandir área del hogar) |
| allowed area | área asignable | `Core/Keyed/Designators.xml` (`DesignatorExpandAreaAllowed` Expandir área asignable) |
| structure (build category) | estructuras | `Core/DefInjected/DesignationCategoryDef/*` (`Structure.label`) |
| furniture | mobiliario | same file (`Furniture.label`) |
| floor / flooring | suelos | same file (`Floors.label`) |
| power (category) | electricidad | same file (`Power.label`) |
| production | producción | same file (`Production.label`) |
| security | seguridad | same file (`Security.label`) |
| temperature (category) | temperatura | same file (`Temperature.label`) |
| orders | órdenes | same file (`Orders.label`) |
| recreation (category) | recreación | same file (`Joy.label`) |
| misc | varios | same file (`Misc.label`) |
| blueprint | anteproyecto | `Core/Keyed/Misc_Gameplay.xml` (`CommandPlaceBlueprints` Colocar anteproyectos) |

### Work, schedule, skills, needs

| English | Español | Source file |
|---|---|---|
| work (tab / main button) | trabajo | `Core/DefInjected/MainButtonDef/MainButtons.xml` (`Work.label` trabajo; `Work.description` Escoge los trabajos que realizará cada colono…) |
| work type / skill | use the specific game label (Cantería, Médico…) | `Core/DefInjected/WorkTypeDef/*`, `Core/DefInjected/SkillDef/*` |
| passion | Pasión | `Core/Keyed/Skills.xml` (`Passion`) |
| passion — none | Sin pasión | `Core/Keyed/Skills.xml` (`PassionNone`) |
| passion — minor | Pasión | `Core/Keyed/Skills.xml` (`PassionMinor` Pasión). Note: `Passion` and `PassionMinor` both ship as "Pasión"; **major** is the distinct one |
| passion — major | Apasionado | `Core/Keyed/Skills.xml` (`PassionMajor`) — gendered `-o`; for a pawn of unknown gender prefer a noun recast (see §3.4) |
| mood | humor | `Core/DefInjected/NeedDef/Needs.xml` (`Mood.label`) |
| need | necesidad | `Core/Keyed/Alerts.xml` (`BreakRiskDescEnding` …la pestaña de Necesidad…) |
| food (need) | alimentación | `Core/DefInjected/NeedDef/Needs.xml` (`Food.label`) |
| recreation / joy (need) | recreación | same file (`Joy.label`) |
| rest / sleep | sueño | same file (`Rest.label`) |
| comfort | comodidad | same file (`Comfort.label`) |
| beauty | belleza | same file (`Beauty.label`) |
| room size | tamaño de la habitación | same file (`RoomSize.label`) |

### Health

| English | Español | Source file |
|---|---|---|
| health | Salud | `Core/Keyed/Misc.xml` (`TabHealth` Salud, `Health` Salud) |
| overview | Resumen | `Core/Keyed/Misc.xml` (`HealthOverview` Resumen) |
| bleeding (rate) | Hemorragia | `Core/Keyed/ITabs.xml` (`BleedingRate`) |
| tend quality | use the specific game label | `Core/Keyed/ITabs.xml`; the verb "tend" is **Atender** (`Tend`). For "medical care" the game uses **atención médica** (`MedicalCareCategory_NoCare` sin atención médica) |
| self-tend | autoatenderse | `Core/Keyed/GameplayCommands.xml` (`SelfTendDisabled` autoatenderse desactivado) |
| body part | use the specific game label | `Core/DefInjected/BodyPartDef/*` |

### Trade

| English | Español | Source file |
|---|---|---|
| trade | Comerciar | `Core/Keyed/Misc_Gameplay.xml` (`CaravanMeeting_Trade` Comerciar) |
| selling | vendiendo | `Core/Keyed/Misc_Gameplay.xml` (`Selling`) |
| buying | comprando | `Core/Keyed/Misc_Gameplay.xml` (`Buying`) |
| silver | plata | `Core/DefInjected/ThingDef/*` (`Silver.label` plata); `Core/Keyed/Misc_Gameplay.xml` (`NeedSilverLaunchable` …{0} de plata…) |
| transport / drop pod | cápsula de transporte | `Core/Keyed/Misc_Gameplay.xml` (`GiveGiftViaTransportPodsTradeRequestWarning` …cápsulas de transporte; `Reward_Pawn_DropPod` …llegará en una cápsula de transporte) |

### Research

| English | Español | Source file |
|---|---|---|
| research (tab / noun) | investigación | `Core/DefInjected/MainButtonDef/MainButtons.xml` (`Research.label` investigación); `Core/Keyed/Misc_Gameplay.xml` (`Research` Investigación, `StopResearch` Detener investigación) |
| research project | proyecto / investigación | `Core/Keyed/Misc_Gameplay.xml` (`ResearchCostComparison` El costo base del proyecto {0}…); `Core/Keyed/Alerts.xml` (`NeedResearchProject` Investigación ociosa) |
| tech level — animal | animal | `Core/Keyed/Misc.xml` (`TechLevel_Animal`) |
| tech level — neolithic | neolítico | `Core/Keyed/Misc.xml` (`TechLevel_Neolithic`) |
| tech level — medieval | medieval | `Core/Keyed/Misc.xml` (`TechLevel_Medieval`) |
| tech level — industrial | industrial | `Core/Keyed/Misc.xml` (`TechLevel_Industrial`) |
| tech level — spacer | espacial | `Core/Keyed/Misc.xml` (`TechLevel_Spacer`) |
| tech level — ultra | ultra | `Core/Keyed/Misc.xml` (`TechLevel_Ultra`) |
| tech level — archotech | arqueotéc | `Core/Keyed/Misc.xml` (`TechLevel_Archotech`) |

### Prisoner, ideology, ritual, abilities

| English | Español | Source file |
|---|---|---|
| prisoner | prisionero | `Core/Keyed/Misc_Gameplay.xml` (`Prisoner` Prisionero) |
| for prisoners (bed) | Designar para prisioneros | `Core/Keyed/Misc_Gameplay.xml` (`CommandBedSetForPrisonersLabel`) |
| warden (work / the role) | Vigilante / vigilante | `Core/DefInjected/WorkTypeDef/WorkTypes.xml` (`Warden.label` Vigilante, `Warden.pawnLabel` Vigilante, `Warden.labelShort` vigilante) |
| ritual | Rituales | `Ideology/Keyed/*` (`Rituals` Rituales) — plural form; singular = **ritual** |
| belief in (ideoligion) | Cree en | `Ideology/Keyed/*` (`BeliefInIdeo` Cree en) |
| ideoligion / ideology | ideoligión | `Ideology/Keyed/*` (`Stat_Thing_RelicOf_Desc` La ideoligión a la que pertenece…). Plural **ideoligiones** |
| ability | Habilidades | `Royalty/Keyed/*` (`Abilities` Habilidades). For a single ability use the specific AbilityDef label |
| psycast / psychic power | poder psíquico | `Royalty/Keyed/Misc_Gameplay.xml` (`LetterPsylinkLevelGained_PsycastLearned` …aprendió automáticamente este poder psíquico) |
| psyfocus | Psicofoco | `Royalty/Keyed/Misc_Gameplay.xml` (`Psyfocus` Psicofoco; `CommandPsycastLowPsyfocus` …al menos {0} de psicofoco…) |

### Weather, season, temperature, dates

| English | Español | Source file |
|---|---|---|
| temperature | Temperatura | `Core/Keyed/Misc.xml` (`Temperature`) |
| weather | use the specific WeatherDef label | `Core/DefInjected/WeatherDef/*` |
| season — spring/summer/fall/winter | primavera / verano / otoño / invierno | `Core/Keyed/Time.xml` (`SeasonSpring`…`SeasonWinter`) |
| quadrum | use the specific quadrum label (Abrimay, Jugosto, Septobre, …) | `Core/Keyed/Time.xml` (`QuadrumAprimay` Abrimay, `QuadrumJugust` Jugosto, `QuadrumSeptober` Septobre) |
| day(s) (lower) | días | `Core/Keyed/Time.xml` (`DaysLower`) |
| hour(s) (lower) | horas | `Core/Keyed/Time.xml` (`HoursLower`) |
| period — days (counted) | {0} días | `Core/Keyed/Time.xml` (`PeriodDays`) — **note: plain plural, NO `_numCase`; canonical proof of §3.2** |
| year (abbrev.) | a | `Core/Keyed/Time.xml` (`LetterYear`) |

### Gizmo / command / button labels

| English | Español | Source file |
|---|---|---|
| range | Rango | `Core/Keyed/Misc.xml` (`Range` Rango) |
| radius | Radio | `Core/Keyed/Misc_Gameplay.xml` (`FoliageKillRadius` Radio de matanza…, `WeaponMissRadius` Radio de fallo…) |
| enable / activate | Activar | `Core/Keyed/Misc.xml` (`Enable` Activar; `Activate` Activar) |
| disable / deactivate | Desactivar | `Core/Keyed/Misc.xml` (`Disable` Desactivar) |
| enabled (state) | Activado | `Core/Keyed/Misc.xml` (`Enabled`) — gendered `-o`; for an unknown-gender subject prefer §3.4 recasts |
| disabled (state) | Desactivado | `Core/Keyed/Misc.xml` (`Disabled`) — gendered `-o`; see §3.4 |
| on | Encendido | `Core/Keyed/Misc.xml` (`On`) |
| off | Apagado | `Core/Keyed/Misc.xml` (`Off`) |
| close (button) | Cerrar | `Core/Keyed/Misc.xml` (`CloseButton`) |
| cancel (button) | Cancelar | `Core/Keyed/Misc.xml` (`CancelButton`) |
| accept (button) | Aceptar | `Core/Keyed/Misc.xml` (`AcceptButton`) |
| confirm | Confirmar | `Core/Keyed/Misc.xml` (`Confirm`) |
| OK | OK | `Core/Keyed/Misc.xml` (`OK`) |
| yes | Sí | `Core/Keyed/Misc.xml` (`Yes` ships as "Si" without the accent in the corpus, but correct Spanish is **Sí** with the accent for the affirmative — use **Sí**) |
| no | No | `Core/Keyed/Misc.xml` (`No`) |
| select | Selecciona / Seleccionar | `Core/Keyed/Misc_Gameplay.xml` (`SelectNextInSquareTip` Selecciona la siguiente cosa…). Imperative form (tú) = **Selecciona**; infinitive label = **Seleccionar** |

### Styling / appearance vocabulary

The mod ships written style descriptions for hair, beard, and tattoo styles, plus appearance
inspection. Use the game's own style labels and category words.

| English | Español | Source file |
|---|---|---|
| hair (concept) | cabello | `Ideology/Keyed/*` (`Hair` cabello; `HairColor` color de cabello; `HairAndBeards` Cabellos y barbas) |
| beard | barba | `Ideology/Keyed/*` (`Beard` barba; `HairAndBeards` Cabellos y barbas) |
| tattoo | tatuaje | `Ideology/Keyed/*` (`Tattoos` Tatuajes; `TattooFace` tatuaje facial; `TattooBody` tatuaje corporal) |
| individual hair/beard/tattoo style | use the specific DefInjected label | `Core/DefInjected/HairDef/*` (`Afro.label` afro, `Bald.label` calvo, `Bob.label` corto), `Core/DefInjected/BeardDef/*` (`BeardCurly.label` rizada, `Braided.label` trenzada), `Ideology/DefInjected/TattooDef/*` or `Core/DefInjected/TattooDef/*` (`Body_Heart.label` corazón, `Body_Cross.label` cruz) |
| style category (Tribal, Urban, Royal…) | use the specific StyleItemCategoryDef label | `Core/DefInjected/StyleItemCategoryDef/*` (`Tribal.label` tribal, `Urban.label` urbano, `Royal.label` realeza, `Punk.label` punk, `Soldier.label` soldado) |
| apparel / clothing | vestimenta / ropa | `Core/Keyed/Alerts.xml` (`AlertTatteredApparel` Vestimenta andrajosa; `AlertUnhappyNudityDesc` …fabricar vestimenta…); ropa is the everyday synonym |
| color | Color | `Ideology/Keyed/*` (`Color` Color; `RecolorApparel` ropa recoloreada) |

---

## Section 2 — Accessibility-specific vocabulary (mod-coined)

These concepts RimWorld does NOT name. We lock a Spanish rendering here. Where a Spanish
screen-reader community convention exists (NVDA-es, JAWS-es, Windows "Narrador"), it is noted and
preferred so blind Spanish users hear familiar wording. Where the game's own UI offers a near-match,
we reuse it and cite it.

| English | Español (locked) | Rationale / source |
|---|---|---|
| screen reader | lector de pantalla | Standard term across NVDA-es, JAWS-es, Windows "Narrador" docs. Universally understood |
| announce / announcement | anunciar / anuncio | Established screen-reader verb in Spanish. (NVDA-es / Narrador "anunciar") |
| navigate / navigation | navegar / navegación | Standard Spanish UI term; matches NVDA-es "navegación" |
| cursor | cursor | RimWorld uses it: `Core/Keyed/Misc_Gameplay.xml` (`ShowBeautyToggleButton` …donde apunta el cursor) |
| menu | menú | RimWorld uses it: `Core/Keyed/Menus_Main.xml` (`QuitToMainMenu` Volver al menú principal) |
| tab (UI tab) | pestaña | RimWorld uses it: `Core/Keyed/Alerts.xml` (`BreakRiskDescEnding` …la pestaña de Necesidad…); `Core/Keyed/Misc_Gameplay.xml` (`ClickToViewInQuestsTab` …la pestaña de misiones) |
| search | buscar / búsqueda | NVDA-es / standard UI term. RimWorld near-match: `Core/Keyed/Alerts.xml` instructions use **buscar** widely |
| typeahead search | búsqueda por escritura | No fixed game term. Use "búsqueda por escritura" (type-to-find). For the bare act, **buscar** |
| expand (tree) | Ampliar | RimWorld uses it: `Core/Keyed/Misc_Gameplay.xml` (`ExpandAllCategories` Ampliar todo). NVDA-es also announces **expandir**; either reads naturally, prefer game's **Ampliar** |
| collapse (tree) | Reducir | RimWorld uses it: `Core/Keyed/Misc_Gameplay.xml` (`CollapseAllCategories` Reducir todo). NVDA-es announces **contraer**; prefer game's **Reducir** |
| expanded (state) | expandido / ampliado | NVDA-es announces "expandido". Gender-safe alternative for our bare-state strings: "Ampliado" (matches game verb) — pick one and stay consistent. These are control states, conventionally masculine |
| collapsed (state) | contraído / reducido | NVDA-es announces "contraído". Or "Reducido" (matches game verb) |
| tree view | vista de árbol | NVDA-es announces tree controls as "árbol" / "vista de árbol" |
| node (tree node) | nodo | Standard Spanish UI term; NVDA-es uses "nodo" |
| level (tree depth) | nivel | Standard. e.g. "nivel {0}" |
| toggle (verb/control) | alternar | RimWorld uses it: `Core/Keyed/Misc_Gameplay.xml` (`CommandToggleDraftDesc` Alterna el reclutamiento). For bare state words reuse Encendido/Apagado or Activado/Desactivado |
| on (state) | Encendido | matches game `On` Encendido (`Core/Keyed/Misc.xml`) |
| off (state) | Apagado | matches game `Off` Apagado (`Core/Keyed/Misc.xml`) |
| selected | Selección: {0} / seleccionado | Prefer the gender-safe noun recast **Selección: {0}** for unknown-gender targets (see §3.4); the participle "seleccionado" is gendered |
| position ("X of Y") | {0} de {1} | "{0} de {1}" (X of Y). RimWorld uses **de** for this: `Core/Keyed/Misc_Gameplay.xml` (`SettlementTrader` {0} de {1}). See §3.1 |
| scanner | escáner | Mod-specific feature; "escáner" is the literal, unambiguous Spanish rendering |
| jump to | ir a / saltar a | Natural Spanish ("ir a" = go/jump to). Prefer **ir a** for brevity in TTS |
| edge / boundary ("already at top") | borde / principio / final | "Already at top/bottom" → "Ya en el principio" / "Ya en el final"; generic boundary = **borde** |
| stepper (numeric +/- control) | control de valor | No game term. "control de valor" describes the control; for limits reuse **mínimo** / **máximo** |
| hotkey | tecla de atajo | RimWorld uses it: `Core/Keyed/Misc_Gameplay.xml` (`SelectNextInSquareTip` (\*SectionTitle)Tecla de atajo:(/SectionTitle) {0}). Use the game's **tecla de atajo** |
| accessibility | accesibilidad | Standard Spanish term |
| inspect / inspection | inspeccionar / ver | Reuse game **ver** for "view" (`ClickToViewFactions` ver las facciones); for the explicit inspect action **inspeccionar** |
| button | botón | Standard Spanish UI term; the game uses **botón** (e.g. `ColonistNeedsTreatmentDescController` …pulsa el botón…) |

---

## Section 3 — Style rules for translators

### 3.1 Grammatical agreement & gender (CRITICAL)

Our strings are mostly **label-value pairs** and **composed fragments** built with `string.Format`
placeholders (`"{0}: {1}"`, `"{0} of {1}"`, `"Range: {0}"`). The value substituted into a
placeholder almost always arrives from the game **already inflected** (an item label, a pawn name, a
status word) and we **cannot control its gender or number**. So we must choose sentence shapes that
read correctly with any inserted value.

**Rules:**

1. **Prefer label-then-colon-then-value.** `"Range: {0}"` → `Rango: {0}`. The colon decouples the
   noun from agreement — `{0}` is just appended and reads fine.
2. **Avoid adjective/participle agreement with an inserted `{0}`.** Don't write something whose
   ending must match the gender of `{0}`, because the game won't tell us its gender.
   - **Don't:** `Seleccionado {0}` (assumes masculine; wrong for "una construcción").
   - **Do:** `Selección: {0}` or `Seleccionado: {0}` with a colon (treat the participle as a fixed
     label, not an agreeing adjective).
3. **For "verb + object" commands, prefer the bare infinitive** the way the game does. RimWorld
   writes order buttons as plain infinitives (`Minar`, `Transportar`, `Priorizar`) without forcing
   agreement. When an object label is interpolated, lead with the verb: `Priorizar {0}`
   (`PrioritizeGenericSimple`).

**Concrete rewrites:**

| English source | Bad (forces gender on `{0}`) | Good (gender-safe) |
|---|---|---|
| `Jumped to {0}` | `Saltado a {0}` (gendered participle) | `Ir a: {0}` or `Objetivo: {0}` |
| `Selected {0}` | `Seleccionado {0}` (assumes masc.) | `Selección: {0}` (noun + colon is safest) |
| `No {0} available` | `Ningún {0}` (forces masc. determiner) | `{0}: no disponible` |
| `{0} expanded` | `{0} expandido` (agrees with {0}) | `{0}. Expandido.` (decoupled) or `Estado: expandido` |

When in doubt, **insert a colon or period before/around the placeholder** — it sidesteps the whole
agreement problem and matches the game's own label-value habit (`Tecla de atajo: {0}`).

**How the game itself sidesteps gender (verified in the corpus):** RimWorld Spanish uses the inline
gender tag `{PAWN_gender ? o : a}` / `{PAWN_gender ? Un : Una}` (e.g. `Reward_Pawn_DropPod`
`{0_gender ? un : una}`) and parenthetical agreement like `reclutado(a)` (`IsNotDraftedLower`). You
may use the `{ARG_gender ? … : …}` tag **only when the placeholder genuinely carries gender info**
(a pawn arg the game tags as gendered). For generic `{0}` item labels you do NOT get a `_gender`
tag, so fall back to the noun/colon recasts above. The parenthetical `(a)` form
(`reclutado(a)`) is also acceptable when a recast would be awkward.

### 3.2 Plurals — simple two forms, NO `_numCase` (CRITICAL)

**Spanish does NOT support the `_numCase` tag.** Verified: `LanguageWorker_Spanish` has no
`TotalNumCaseCount` override (defaults to 2), and the official Spanish corpus contains **zero**
`_numCase` usages. The game writes counted nouns as a plain `{0}` + plural noun — e.g.
`PeriodDays` is simply `{0} días` (`Core/Keyed/Time.xml`), `ColonistsIdle` is `{0} colonos ociosos`.

**Therefore:**

- **Never write** `{N_numCase ? … : …}` — for Spanish the grammar engine resolves a multi-branch
  `_numCase` tag to an **empty string** (the branch count won't match `TotalNumCaseCount`=2). It
  would silently blank the value.
- For a single counted value, just use the plural noun: `{0} colonos`, `{0} elementos`, `{0} días`.
  Spanish plural is regular (`-s` after a vowel, `-es` after a consonant: `colono→colonos`,
  `nivel→niveles`, `botón→botones`).

#### One / Many key PAIRS (when C# pre-selects the key by `count == 1 ? One : Many`)

Many of our strings split into a `…One` / `…Many` key pair, chosen in C# by `count == 1`. The
`One` key is only ever shown for count 1; the `Many` key covers **0, 2, 3, …**.

- **`…One` key → singular.** `Ampliado {0} elemento` (or `1 elemento`).
- **`…Many` key → plural.** `Ampliado {0} elementos` / `Ampliados {0} elementos`.

Because Spanish has clean 2-form plurals and the C# already split singular vs plural for you, this is
straightforward: fill `…One` with the singular noun and `…Many` with the plural noun. **Always fill
both keys** — never leave one blank, or the mod falls back to English for that count.

| English pair | Español `…One` | Español `…Many` |
|---|---|---|
| `{0} item` / `{0} items` | `{0} elemento` | `{0} elementos` |
| `{0} colonist` / `{0} colonists` | `{0} colono` | `{0} colonos` |
| `{0} match` / `{0} matches` | `{0} coincidencia` | `{0} coincidencias` |
| `Expanded {0} item` / `…items` | `Ampliado {0} elemento` | `Ampliado {0} elementos` |

### 3.3 Punctuation & typography

Spanish uses Latin punctuation (`. , : ; ! ?`), no full-width marks.

- **Inverted opening marks.** Full exclamations/questions take an opening `¡`/`¿` AND a closing
  `!`/`?` — both are required: `¡Un colono necesita rescate!` (`ColonistNeedsRescue`),
  `¿Estás seguro…?` (`GiveGiftViaTransportPodsTradeRequestWarning`). Use them **only inside a genuine
  prose sentence within one translated value** — never around a composed fragment or a bare label.
- **Accents.** Keep all diacritics: `á é í ó ú ñ ü`, and the inverted marks `¡ ¿`. `Sí` (affirmative)
  carries an accent to distinguish it from `si` (if).
- **Quotation marks.** The corpus uses straight `"..."` (e.g. `CommandPlaceBlueprintsSpecific`
  patterns). Use straight `"..."` for quoting an interpolated label; do not introduce guillemets.
- **Decimal separator.** Latin American Spanish convention is the period for decimals, but **leave any
  number that arrives via a placeholder exactly as the game formats it** — never reformat `{0}`.
- **Composed fragment joiners stay ASCII.** The mod glues TTS segments with `". "`, `", "`, `": "`
  (e.g. `RimWorldAccess.TwoLevel.ButtonAnnouncement` = `{0}. Button. {1}`). **Keep these joiners as
  ASCII period/comma/colon + a single space.** TTS engines and screen readers reliably treat
  `. ` / `, ` / `: ` as segment/pause boundaries; the trailing space safely separates an adjacent
  number or Latin token. Do NOT replace them with `¡¿`, em dashes, or drop the space.

> Quick rule: prose *inside one translated value* → full Spanish punctuation incl. `¡ ¿ … ! ?` and
> accents. Glue *between composed fragments* and *label: value* colons → keep ASCII `. ` `, ` `: `
> with the trailing space, exactly as in the English source.

### 3.4 Gender — gender-safe recasts (CRITICAL)

Spanish marks gender on articles (el/la, un/una) and on adjectives/participles (-o/-a). When our
string describes an action by or on a pawn (or object) of **unknown** gender, do NOT pick a
masculine or feminine form.

- **Prefer noun forms over participles.** "Selected {0}" → `Selección: {0}` (noun) rather than
  `Seleccionado {0}` (which assumes masculine and mis-agrees with a feminine label).
- **Use label: value with a colon to decouple agreement** — the value is just appended and never has
  to agree (`Objetivo: {0}`, `Estado: {0}`, `Rango: {0}`).
- **Use the bare infinitive for commands** as the game does (`Minar`, `Transportar`, `Priorizar`,
  `Atender`) — infinitives carry no gender.
- **For control states**, the conventional masculine works for inanimate UI controls
  (`Activado`/`Desactivado`, `Encendido`/`Apagado`, `expandido`/`contraído`) — these describe a
  control, not a pawn, so the game's masculine `-o` is fine.
- **When the value is a gendered game pawn arg** (the game exposes a `_gender` tag), you may use
  `{ARG_gender ? o : a}` exactly as the corpus does (`{PAWN_gender ? o : a}`,
  `{0_gender ? un : una}`). Otherwise fall back to recasts. The parenthetical `(a)` style
  (`reclutado(a)`, as in `IsNotDraftedLower`) is an acceptable last resort.

| Situation | Bad (gendered) | Good (gender-safe) |
|---|---|---|
| select an unknown-gender thing | `Seleccionado {0}` | `Selección: {0}` |
| jump to a thing | `Saltado a {0}` | `Ir a: {0}` |
| "no X available" | `Ningún {0} disponible` | `{0}: no disponible` |
| forbidden state on a thing | `Prohibido` (if it must agree) | `Estado: prohibido` or keep as the game's fixed label |

### 3.5 Tone / register — informal "tú" (VERIFIED)

RimWorld's official Latin American Spanish addresses the player with the **informal second person
"tú"**, verified by grepping imperative and verb forms in the corpus:

- **Imperatives:** `Haz clic` (24×), `Selecciona` (20×), `Elige` (7×), `Escoge` (3×) — the tú forms
  dominate. The few usted forms (`Seleccione` 3×, `Elija` 1×) are a small minority.
- **Verb forms / pronouns:** `puedes` (32×), `tienes` (15×), `quieres` (33×), `debes` (3×), plus
  possessive `tu`/`tus` everywhere (`tu base`, `tus colonos`). Cited examples:
  - `DesignatorPlanDesc` — "…ayudar**te** visualmente a planificar… **Haz** clic con el botón
    derecho…"
  - `ClickToLearnMore` — "**Haz** clic para aprender más."
  - `NeedResearchProjectDesc` — "**Tienes** el equipo necesario… **Haz** clic para abrir el menú…"

**Translators MUST use informal "tú"** and tú-form imperatives (`Selecciona`, `Presiona`/`Pulsa`,
`Elige`, `Ve a`). Latin American Spanish also:

- uses **"ustedes"** for the second-person plural, **never "vosotros"**;
- uses **neutral Latin American vocabulary** (e.g. the corpus writes **computadora**, not
  "ordenador" — see `GameUpdatedToNewVersionSteam`); prefer **carro/auto** habits etc. only if a
  game term doesn't already exist (always reuse the game term first);
- requires the **inverted opening marks** `¡ … !` and `¿ … ?` for full exclamations/questions (§3.3).

Other register notes:
- Match RimWorld's neutral, terse UI register: **infinitives for command labels**
  (`Construir`, `Cancelar`, `Priorizar`), **nouns for states** (`humor`, `salud`).
- No exclamation marks unless the English/game has them (the game's alerts like `¡Incendio!` are
  intentional). **No first person. No emoji.**
- When a tooltip is a full sentence in English, translate it as a full sentence ending with `.`.
  When it is a short label, keep it short.

### 3.6 Placeholders, line breaks, keys, spacing

- Copy `{0}`, `{1}`, `{NamedArg}`, `{PAWN_label}`, `{PAWN_gender ? o : a}`, etc. **byte-for-byte.**
  Never translate a placeholder, never add/remove braces, never renumber. The **set** of placeholders
  in a value must be identical to the English source.
- You **may reposition** a placeholder within a sentence for natural Spanish word order
  (`string.Format` is positional). Repositioning is encouraged where it reads better; renumbering or
  translating is forbidden.
- **Do NOT add `_numCase` tags** — Spanish doesn't support them (§3.2).
- Copy `\n` line breaks exactly and keep them in the same logical spots.
- **XML keys are never translated** — keep every element name byte-for-byte identical to English, or
  the string silently fails to load.
- **XML comments are developer context — leave them in English.** Do not translate `<!-- … -->`.
- **Preserve leading/trailing spaces in values.** Some keys are suffixes that join onto a preceding
  label (e.g. a leading-space `" Inspectable."` or `" nivel {0}"`). Keep the exact leading/trailing
  space.
- Where a number/placeholder abuts Spanish text, keep a single normal space (`{0} elementos`,
  `nivel {0}`). Do not glue them together.

---

## Section 4 — How to extend this glossary

To find how RimWorld translates a term you don't see above:

```bash
# UI strings (button labels, menu text, gameplay commands):
grep -rh 'EnglishKeyName' /tmp/es_ref/*/Keyed/*.xml

# Or search by a Spanish word you suspect:
grep -rh 'palabra_española' /tmp/es_ref/*/Keyed/*.xml

# Concept labels (things, designators, factions, skills, needs, biomes…):
grep -rh 'EnglishKeyName' /tmp/es_ref/*/DefInjected/**/*.xml

# Confirm Spanish never uses _numCase (should return nothing):
grep -rh '_numCase' /tmp/es_ref/*/Keyed/*.xml

# Confirm the tú register on a verb you're unsure about:
grep -rhoE 'Selecciona|Seleccione|Pulsa|Pulse|Haz |Haga ' /tmp/es_ref/*/Keyed/*.xml | sort | uniq -c
```

Useful sub-paths: `Keyed/Designators.xml` (order verbs), `Keyed/GameplayCommands.xml` +
`Keyed/Misc_Gameplay.xml` (commands), `Keyed/Misc.xml` (general UI), `Keyed/Time.xml` (calendar),
`Keyed/Skills.xml`, `Keyed/Alerts.xml`, `Keyed/ITabs.xml`, `DefInjected/WorkTypeDef`,
`DefInjected/SkillDef`, `DefInjected/NeedDef`, `DefInjected/DesignationCategoryDef`,
`DefInjected/MainButtonDef`, `DefInjected/HairDef`, `DefInjected/BeardDef`, `DefInjected/TattooDef`,
`DefInjected/StyleItemCategoryDef`, `DefInjected/BiomeDef`, `DefInjected/WeatherDef`,
`DefInjected/FactionDef`.

The English comments in the corpus (`<!-- EN: … -->`) let you confirm a key's meaning before trusting
its Spanish value. Always cite the file you took a term from when you add a row. **Never add a
`_numCase` tag (§3.2), keep the informal tú register (§3.5), and prefer gender-safe recasts (§3.4).**
