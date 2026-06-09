# Ukrainian (Українська) Terminology & Style Glossary — RimWorld Access

This glossary locks in consistent Ukrainian wording for translating the RimWorld Access
screen-reader mod. The goal is that Ukrainian players feel the mod is a seamless extension of
RimWorld itself, so **every game-anchored term below uses the EXACT word RimWorld's own official
Ukrainian translation uses**, with a citation to the reference file it was taken from.

The strings we translate are **spoken aloud by a TTS engine**, not displayed. Natural, terse,
unambiguous phrasing matters more than visual polish.

Reference corpus: RimWorld's official Ukrainian, extracted to `/tmp/uk_ref/`
(Core + Royalty + Ideology + Biotech + Anomaly + Odyssey). Paths below are relative to `/tmp/uk_ref/`.

> How to use this file: grep for the English term. If a term you need is not here and RimWorld has
> it, grep the game corpus yourself (see Section 4) and add it — never invent a rendering for a
> concept the game already names.

---

## Section 1 — Core game vocabulary (game-anchored)

Every row is the term RimWorld itself ships in Ukrainian. Use it verbatim. These are the words the
player already hears from the game, so our mod must match them exactly.

### People, factions, world

| English | Українська | Source file |
|---|---|---|
| colonist | колоніст | `Core/Keyed/Designators.xml` (`DesignatorHaulThingsDesc` …колоністи переносять…); `Core/Keyed/Misc_Gameplay.xml` (`ColonistsIdle` {0} колоністів) |
| colony / settlement (your base) | поселення / колонія | `Core/Keyed/Alerts.xml` (`HostileVisitorsPresent` в колонії…); `Core/Keyed/Misc_Gameplay.xml` (…у їхньому поселенні) |
| pawn / character (generic) | персонаж | `Core/Keyed/Misc_Gameplay.xml` (`ShowBeautyToggleButton` …персонаж, який стоїть…) |
| faction | фракція | `Core/Keyed/Misc_Gameplay.xml` (`MilitaryAidConfirmMutualEnemy` Фракція {0}…) |
| caravan | караван | `Core/Keyed/Misc_Gameplay.xml` (`ImmobileCaravanDesc`, `CaravanIdleDesc` Ці каравани…). Note: RimWorld uses **караван** for both player expeditions and trader caravans |
| raid / raider | рейд / рейдер | `Core/Keyed/Incidents.xml` (`NeedDefensesDesc` …надсилатимуть рейди); `Core/Keyed/Incidents.xml` (`EscapeShipFound` …відчайдушних рейдерів) |
| map | мапа | `Core/Keyed/Misc.xml` (`Map` Мапа) |
| tile (world map tile) | плитка / клітинка | RimWorld surfaces world tiles via context; for a single map cell the game uses **квадрат** (`Core/Keyed/Misc_Gameplay.xml` `SelectNextInSquareTip` …у тому самому квадраті). Use **квадрат** for a map cell |
| region / area (map area) | область | `Core/Keyed/Misc.xml` (`AreaLower` область); `Core/Keyed/Designators.xml` (`DesignatorExpandAreaAllowed` …дозволену область) |
| zone | зона | `Core/DefInjected/DesignationCategoryDef/DesignationCategories.xml` (`Zone.label` зона); `Core/Keyed/Misc_Gameplay.xml` (`Zone` Зона) |
| world / planet | світ | `Core/Keyed/Misc_Gameplay.xml` (`GameOverPlanetkillerImpact` …цей світ розплавився…) |
| prisoner | в'язень / полонений | `Core/DefInjected/HistoryEventDef/*` (`PrisonerDied.label` в'язень помер); `Core/Keyed/Incidents.xml` (…для вербування — полонений) |

### Designators / order verbs (imperative form, as the game uses them on buttons)

| English | Українська | Source file |
|---|---|---|
| cancel | Скасувати | `Core/Keyed/Designators.xml` (`DesignatorCancel`) |
| chop wood / harvest wood | Зрубати | `Core/Keyed/Designators.xml` (`DesignatorHarvestWood`) |
| mine | Видобути | `Core/Keyed/Designators.xml` (`DesignatorMine`) |
| mine vein (mark) | Видобути жилу | `Core/Keyed/Designators.xml` (`DesignatorMineVein`) |
| harvest | Зібрати | `Core/Keyed/Designators.xml` (`DesignatorHarvest`) |
| cut plants | Зрізати | `Core/Keyed/Designators.xml` (`DesignatorCutPlants`) |
| deconstruct | Розібрати | `Core/Keyed/Designators.xml` (`DesignatorDeconstruct`) |
| uninstall | Демонтувати | `Core/Keyed/Designators.xml` (`DesignatorUninstall`) |
| haul | Перенести | `Core/Keyed/Designators.xml` (`DesignatorHaulThings`) |
| hunt | Полювати | `Core/Keyed/Designators.xml` (`DesignatorHunt`) |
| tame | Приручити | `Core/Keyed/Designators.xml` (`DesignatorTame`) |
| slaughter | Забити | `Core/Keyed/Designators.xml` (`DesignatorSlaughter`) |
| forbid | Заборонити | `Core/Keyed/Designators.xml` (`DesignatorForbid`) |
| unforbid / allow | Дозволити | `Core/Keyed/Designators.xml` (`DesignatorUnforbid`) |
| claim | Привласнити | `Core/Keyed/Designators.xml` (`DesignatorClaim`) |
| strip | Роздягнути | `Core/Keyed/Designators.xml` (`DesignatorStrip`) |
| smooth (surface) | Відполірувати | `Core/Keyed/Designators.xml` (`DesignatorSmoothSurface` Відполірувати поверхню) |
| plan | Планування | `Core/Keyed/Designators.xml` (`DesignatorPlan`) |
| build | Побудувати / Будувати | `Core/Keyed/Misc_Gameplay.xml` (`CommandBuildCopy` Побудувати копію); `Core/Keyed/Designators.xml` (`DesignatorAreaBuildRoofExpand` Будувати дах) |
| assign | Призначити | `Core/Keyed/Misc_Gameplay.xml` (`CommandThingSetOwnerLabel` Призначити власника) |
| tend (a wound) | Лікувати | `Core/Keyed/GameplayCommands.xml` (`Tend` Лікувати …) |
| draft (combat mode) | Бойовий режим | `Core/Keyed/Misc_Gameplay.xml` (`CommandDraftLabel`) |
| undraft (free mode) | Вільний режим | `Core/Keyed/Misc_Gameplay.xml` (`CommandUndraftLabel`) |
| recruit | вербувати / вербування | `Core/Keyed/Incidents.xml` (`RefugeePodCrash_Factionless` …для вербування…) |

### Buildings, zones, areas, build categories

| English | Українська | Source file |
|---|---|---|
| growing zone | Зона посіву | `Core/Keyed/Misc_Gameplay.xml` (`GrowingZone`) |
| stockpile / storage zone | Зона складу / склад | `Core/Keyed/Misc_Gameplay.xml` (`Stockpile` Зона складу, `StockpileGroup` склад) |
| home area | домашня область | `Core/Keyed/Designators.xml` (`DesignatorAreaHomeExpand` Розширити домашню область) |
| allowed area | дозволена область | `Core/Keyed/Designators.xml` (`DesignatorExpandAreaAllowed` …дозволену область) |
| structure (build category) | будівництво | `Core/DefInjected/DesignationCategoryDef/DesignationCategories.xml` (`Structure.label`) |
| furniture | меблі | same file (`Furniture.label`) |
| floor / flooring | покриття | same file (`Floors.label`) |
| power | енергія | same file (`Power.label`) |
| production | виробництво | same file (`Production.label`) |
| security | захист | same file (`Security.label`) |
| temperature (category) | температура | same file (`Temperature.label`) |
| orders | накази | same file (`Orders.label`) |
| recreation (category) | дозвілля | same file (`Joy.label`) |
| misc | різне | same file (`Misc.label`) |
| blueprint | проєкт | `Core/Keyed/Misc_Gameplay.xml` (`CommandPlaceBlueprints` Розмістити проєкт) |

### Work, schedule, skills, needs

| English | Українська | Source file |
|---|---|---|
| work (tab/menu) | Праця | `Core/Keyed/Alerts.xml` (`NeedDoctorDesc` …вкладку «Праця»; `NeedMinerDesc` …у вкладці «Праця») |
| work type / skill | use the specific game label (Гірник, Лікар, …) | `Core/DefInjected/WorkTypeDef/*`, `Core/DefInjected/SkillDef/*` |
| passion | Зацікавленість | `Core/Keyed/Skills.xml` (`Passion`) |
| passion — none | Відсутня | `Core/Keyed/Skills.xml` (`PassionNone`) |
| passion — minor | Цікаво | `Core/Keyed/Skills.xml` (`PassionMinor`) |
| passion — major | В захваті | `Core/Keyed/Skills.xml` (`PassionMajor`) |
| mood | настрій | `Core/DefInjected/NeedDef/Needs.xml` (`Mood.label`) |
| need | потреба | `Core/Keyed/Alerts.xml` (`BreakRiskDescEnding` …вкладку «Потреби»…) |
| food (need) | ситість | `Core/DefInjected/NeedDef/Needs.xml` (`Food.label`) |
| recreation / joy (need) | відпочинок | same file (`Joy.label`) |
| rest / sleep | сон | same file (`Rest.label`) |
| comfort | комфорт | same file (`Comfort.label`) |
| beauty | краса | same file (`Beauty.label`) |
| room size | розмір приміщення | same file (`RoomSize.label`) |

### Health

| English | Українська | Source file |
|---|---|---|
| health | Здоров'я | `Core/Keyed/Misc.xml` (`Health`, `TabHealth` Здоров'я) |
| overview | Огляд | `Core/Keyed/Misc.xml` (`HealthOverview`) |
| bleeding (rate) | Кровотеча | `Core/Keyed/Stats.xml` (`BleedingRate`) |
| tend quality | Якість лікування | `Core/Keyed/Stats.xml` (`TendQuality`) |
| self-tend | самолікування | `Core/Keyed/GameplayCommands.xml` (`SelfTendDisabled` самолікування недоступне) |
| body part | use the specific game label | `Core/DefInjected/BodyPartDef/*` |

### Trade

| English | Українська | Source file |
|---|---|---|
| trade | Торгувати | `Core/Keyed/Misc_Gameplay.xml` (`CaravanMeeting_Trade`) |
| selling | продаж | `Core/Keyed/Misc_Gameplay.xml` (`Selling`) |
| buying | купівля | `Core/Keyed/Misc_Gameplay.xml` (`Buying`) |
| silver | срібло | `Core/DefInjected/ThingDef/*` (`Silver.label`); `Core/Keyed/Misc_Gameplay.xml` (`NotEnoughSilver` не вистачає срібла) |
| transport / drop pod | капсула | `Core/Keyed/Misc_Gameplay.xml` (`PlayerPawnsArriveMethod_DropPods` У капсулах) |

### Research

| English | Українська | Source file |
|---|---|---|
| research | Дослідження | `Core/Keyed/Misc.xml` (`Research`) |
| research project | дослідницький проєкт | `Core/Keyed/Alerts.xml` (`NeedResearchProject` Потрібен дослідницький проєкт) |
| tech level — animal | тваринний | `Core/Keyed/Misc.xml` (`TechLevel_Animal`) |
| tech level — neolithic | неолітичний | `Core/Keyed/Misc.xml` (`TechLevel_Neolithic`) |
| tech level — medieval | середньовічний | `Core/Keyed/Misc.xml` (`TechLevel_Medieval`) |
| tech level — industrial | індустріальний | `Core/Keyed/Misc.xml` (`TechLevel_Industrial`) |
| tech level — spacer | космічний | `Core/Keyed/Misc.xml` (`TechLevel_Spacer`) |
| tech level — ultra | ультра-технологічний | `Core/Keyed/Misc.xml` (`TechLevel_Ultra`) |
| tech level — archotech | архо-технологічний | `Core/Keyed/Misc.xml` (`TechLevel_Archotech`) |

### Prisoner, ideology, ritual, abilities

| English | Українська | Source file |
|---|---|---|
| prisoner | в'язень / полонений | `Core/DefInjected/HistoryEventDef/*` (`PrisonerDied.label`) |
| for prisoners (bed) | Для в'язнів | `Core/Keyed/Misc_Gameplay.xml` (`CommandBedSetForPrisonersLabel`) |
| warden (the role) | наглядач | `Core/DefInjected/WorkTypeDef/WorkTypes.xml` (`Warden.pawnLabel` Наглядач, `Warden.labelShort` наглядач) |
| ritual | ритуал | `Ideology/Keyed/*` (`Rituals` Ритуали) |
| belief / ideoligion | віра | `Ideology/Keyed/*` (`BeliefInIdeo` Віра у) |
| ability (psycast) | здібність | `Royalty/Keyed/Misc_Gameplay.xml` (`CommandPsycastWouldExceedEntropy` …застосувати цю здібність) |
| psyfocus | пси-фокус | `Royalty/Keyed/Misc_Gameplay.xml` (`CommandPsycastLowPsyfocus` …{0} пси-фокусу…) |

### Weather, season, temperature, dates

| English | Українська | Source file |
|---|---|---|
| temperature | Температура | `Core/Keyed/Misc.xml` (`Temperature`) |
| weather | use the specific WeatherDef label | `Core/DefInjected/WeatherDef/*` |
| season — spring/summer/fall/winter | весна / літо / осінь / зима | `Core/Keyed/Time.xml` (`SeasonSpring`…`SeasonWinter`) |
| quadrum | use the specific quadrum label (беревень, …) | `Core/Keyed/Time.xml` (`QuadrumAprimay` беревень) |
| day(s) (lower) | днів | `Core/Keyed/Time.xml` (`DaysLower`) — note: this is already the genitive-plural form |
| hour(s) (lower) | годин | `Core/Keyed/Time.xml` (`HoursLower`) |
| year (abbrev.) | р | `Core/Keyed/Time.xml` (`LetterYear`) |

### Gizmo / command / button labels

| English | Українська | Source file |
|---|---|---|
| range | Дальність | `Core/Keyed/Stats.xml` (`Range`) |
| radius | Радіус | `Core/Keyed/Misc_Gameplay.xml` (`IngredientSearchRadius` Радіус пошуку) |
| enable / activate | Активувати | `Core/Keyed/Misc.xml` (`Enable`) |
| disable / deactivate | Деактивувати | `Core/Keyed/Misc.xml` (`Disable`) |
| enabled (state) | Активовано | `Core/Keyed/Misc.xml` (`Enabled`) |
| disabled (state) | Деактивовані | `Core/Keyed/Misc.xml` (`Disabled`) — adjective; for a neutral state prefer **вимкнено** (see Section 3 gender note) |
| close (button) | Закрити | `Core/Keyed/Misc.xml` (`CloseButton`) |
| cancel (button) | Скасувати | `Core/Keyed/Misc.xml` (`CancelButton`) |
| accept (button) | Прийняти | `Core/Keyed/Misc.xml` (`AcceptButton`) |
| confirm | Підтвердити | `Core/Keyed/Misc.xml` (`Confirm`) |
| OK | ОК | `Core/Keyed/Misc.xml` (`OK`) |
| yes | Так | `Core/Keyed/Misc.xml` (`Yes`) |
| no | Ні | `Core/Keyed/Misc.xml` (`No`) |
| select / selection | вибрати / виділення | `Core/Keyed/Misc_Gameplay.xml` (`CommandCopyPlanSelectionLabel` …виділення; `CommandSendCaravanDesc` …вибраного місця) |

---

## Section 2 — Accessibility-specific vocabulary (mod-coined)

These concepts RimWorld does NOT name. We lock a Ukrainian rendering here. Where the game's own UI
offers a near-match, we reuse it and cite it so users hear familiar wording.

| English | Українська (locked) | Rationale / source |
|---|---|---|
| screen reader | програма зчитування з екрана | Standard Ukrainian term (NVDA-uk, Windows «Екранний диктор» docs). «зчитувач екрана» is acceptable shorthand if length matters |
| announce / announcement | озвучити / озвучення | Established screen-reader verb in Ukrainian. Prefer over «оголосити» |
| navigate / navigation | навігація / переходити | Standard Ukrainian UI term |
| cursor | курсор | RimWorld uses it: `Core/Keyed/Misc_Gameplay.xml` (`ShowBeautyToggleButton` …на який вказує курсор) |
| menu | меню | RimWorld uses it: `Core/Keyed/Menus_Main.xml` (`QuitToMainMenu` До головного меню) |
| tab (UI tab) | вкладка | RimWorld uses it: `Core/Keyed/Alerts.xml` (`NeedDoctorDesc` …вкладку «Праця») |
| search | пошук / шукати | RimWorld uses both: `Core/Keyed/Misc_Gameplay.xml` (`SearchTheMap` Пошук…); `Core/Keyed/Misc.xml` (`CommandSelectMineralToScanFor` Шукати) |
| typeahead search | пошук за введенням | No fixed game term. Use «пошук за введенням» (type-to-find). For the bare act, «пошук» |
| expand (tree) | Розгорнути | RimWorld uses it: `Core/Keyed/Misc_Gameplay.xml` (`ExpandAllCategories` Розгорнути все) |
| collapse (tree) | Згорнути | RimWorld uses it: `Core/Keyed/Misc_Gameplay.xml` (`CollapseAllCategories` Згорнути все) |
| expanded (state) | розгорнуто | impersonal/neuter stative form of Розгорнути; gender-safe (see Section 3) |
| collapsed (state) | згорнуто | impersonal/neuter stative form of Згорнути |
| tree view | деревоподібний список | No game term. «деревоподібний список» (tree-like list) reads naturally in TTS |
| node (tree node) | вузол | Standard Ukrainian UI term; NVDA-uk uses «вузол» |
| level (tree depth) | рівень | Standard. e.g. «рівень {0}» |
| toggle (verb/control) | перемкнути | Standard Ukrainian UI verb. For the bare state words reuse Активовано/Деактивовано or увімкнено/вимкнено |
| on (state) | увімкнено | Standard; impersonal/neuter, gender-safe |
| off (state) | вимкнено | Standard; impersonal/neuter, gender-safe |
| selected | вибрано / виділено | impersonal/neuter stative; matches game «виділення». Gender-safe |
| position ("X of Y") | {0} з {1} | «{0} з {1}» (X of Y). Genitive «з» reads cleanly with numbers; see Section 3 |
| scanner | сканер | Mod-specific feature; «сканер» is the literal, unambiguous rendering |
| jump to | перейти до | Natural Ukrainian («перейти» = go/jump to). RimWorld has no direct equivalent |
| edge / boundary | край / початок / кінець | "Already at top/bottom" → «Вже на початку» / «Вже в кінці»; generic boundary = «край» |
| stepper (numeric +/- control) | регулятор значення | No game term. «регулятор значення» describes the control; for minimum/maximum reuse «мінімум» / «максимум» |
| hotkey | гаряча клавіша | RimWorld uses it: `Core/Keyed/Misc_Gameplay.xml` (`SelectNextInSquareTip` Гаряча клавіша: {0}) |
| accessibility | доступність | Standard Ukrainian term |
| inspect / inspection | огляд / оглянути | Reuse game «Огляд» (`HealthOverview`); for the action «оглянути» |
| button | кнопка | Standard Ukrainian UI term |

---

## Section 3 — Style rules for translators

### 3.1 Grammatical case & agreement (CRITICAL)

Our strings are mostly **label-value pairs** and **composed fragments** built with `string.Format`
placeholders (`"{0}: {1}"`, `"{0} of {1}"`, `"Range: {0}"`). The value substituted into a
placeholder almost always arrives from the game **already inflected, in the nominative case**
(an item label, a pawn name, a status word). **We cannot force a substituted value into genitive,
accusative, etc.** So we must choose sentence shapes that read correctly with a nominative insertion.

**Rules:**

1. **Prefer label-then-colon-then-value.** `"Range: {0}"` → `«Дальність: {0}»`. The colon decouples
   the noun from grammatical agreement — `{0}` stays nominative and it reads fine.
2. **Avoid constructions that grammatically demand a non-nominative inserted noun.** Do NOT write
   something that requires `{0}` to be genitive/accusative, because the game won't supply that form.
   - **Don't:** `«Немає {0}»` (genitive expected after «немає») when `{0}` is a nominative label.
   - **Do:** `«{0}: відсутній»` or `«{0} — немає»` (keep `{0}` nominative, attach the state after it).
3. **For "verb + object" commands, prefer the bare imperative + nominative-ish noun** the way the
   game does. RimWorld writes order buttons as plain imperatives (`Видобути`, `Перенести`,
   `Призначити`) without forcing the object into a case. When an object label is interpolated, lead
   with the verb and let the label follow as a quoted nominative: e.g. the game's
   `CommandPlaceBlueprintsSpecific` is `«Розмістити проєкт "{0}"»` — `{0}` stays nominative in quotes.

**Concrete rewrites:**

| English source | Bad (forces case on `{0}`) | Good (nominative-safe) |
|---|---|---|
| `Jumped to {0}` | `Перейшов до {0}` (demands genitive `{0}`) | `Перейти до: {0}` or `Ціль: {0}` |
| `No {0} available` | `Немає {0}` (demands genitive) | `{0}: недоступно` |
| `Selected {0}` | `Вибрано {0}` is fine ONLY if `{0}` reads as nominative-quoted | `Вибрано: {0}` (colon is safest) |

When in doubt, **insert a colon or dash before the placeholder** — it sidesteps the whole agreement
problem and matches the game's own label-value habit (`«Дальність: {0}»`).

### 3.2 Plurals — use a flat genitive plural (matches vanilla Ukrainian)

Ukrainian has a **three-form** count system: 1 → nominative singular; 2–4 → paucal; 0, 5–9, and the
teens 11–14 → genitive plural. RimWorld's grammar engine *can* in theory produce all three forms with
a `{N_numCase ? one : few : many}` tag — **but that mechanism is unavailable to us for Ukrainian, so
DO NOT use it.** Use a single flat **genitive-plural** noun for every plural, exactly as RimWorld's
own official Ukrainian does.

**Why `_numCase` is off the table (verified in decompiled source — do not re-introduce it):**

- `GrammarResolverSimple.ResolveNumCase` checks the branch count against
  `LanguageDatabase.activeLanguage.info.totalNumCaseCount ?? activeLanguageWorker.TotalNumCaseCount`.
  Ukrainian ships **no `LanguageWorker`** (falls back to `LanguageWorker_Default`, whose
  `TotalNumCaseCount` is **0**) and Core's `LanguageInfo.xml` leaves `totalNumCaseCount` unset. So the
  expected count is **0**, a 3-branch `_numCase` tag fails the check (`3 != 0`), logs an error, and
  **returns an empty string** — a silently blank spoken announcement.
- We **cannot** override this. `LoadedLanguage.LoadMetadata` takes the **first** `LanguageInfo.xml` it
  finds across `RunningMods` and returns immediately; mods are forced to load **after** Core
  (`ModsConfig` enforces `ModReorderConflict_MustLoadAfter` Core), so **Core's** Ukrainian
  `LanguageInfo.xml` always wins and our own would never be read. Shipping a `LanguageInfo.xml` with
  `<totalNumCaseCount>3</totalNumCaseCount>` therefore has no effect.
- This is exactly why RimWorld's own Ukrainian has **zero** `_numCase` usages and instead writes a
  flat genitive plural everywhere. We match that. (Russian works only because it ships
  `LanguageWorker_Russian` with `TotalNumCaseCount => 3` — Ukrainian has no equivalent.)

**THE RULE:** every plural noun (whether the value contains a `{0}` count or not) uses the
**genitive-plural** form — the same single form the game uses with arbitrary counts:

- `{0} колоністів байдикують` (`Core/Keyed/Misc_Gameplay.xml` `ColonistsIdle`)
- `{0} тварин` (`CaravanAnimalsCount`), `{1}, {0} людей` (`CaravanColonistsCount`)
- `Наступне оновлення через {0} днів` (`NextTraderRestock`); `DaysLower` ships as `днів`

Worked examples (use the genitive plural in both the `{0}`-bearing value AND in any bare suffix):

| English | Українська (genitive plural) |
|---|---|
| `{0} components connected` | `{0} компонентів під'єднано` |
| `{0} days` | `{0} днів` |
| `{0} items` | `{0} предметів` |
| `{0} characters` | `{0} символів` |
| `{0} colonists` | `{0} колоністів` |

**Tradeoff (accepted, matches vanilla):** counts ending in 2–4 read slightly off — `3 компонентів`
instead of the strictly-correct paucal `3 компоненти`. This is unavoidable without `_numCase` and is
precisely the game's own behavior, so it reads as native, not broken. Do **not** try to fix it
per-string.

#### One / Many key PAIRS (when C# pre-selects the key by `count == 1 ? One : Many`)

Some of our strings are split into a `…One` / `…Many` key pair, chosen in C# by `count == 1`. The
`One` key is only ever shown for count 1; the `Many` key covers **0, 2, 3, 4, 5, …**.

- **`…One` key → nominative singular:** `Розгорнуто {0} елемент`.
- **`…Many` key → genitive plural** (the safest single form across 2–4 and 5+): `Розгорнуто {0} елементів`.

Always fill **both** keys of a pair — never leave one blank, or the mod falls back to English for that
count.

### 3.3 Punctuation & typography

Ukrainian uses **Latin-style punctuation** (`. , : ; ! ?`) — there is no full-width punctuation.

- **Quotation marks:** primary level is **«guillemets»**, confirmed throughout the corpus
  (`«Гірник»`, `«Праця»`, `«{0}»` in `Core/Keyed/*`). Inner/nested level uses **„lapky“** (`„ “`).
  Newer DLC strings sometimes use straight `"..."`; prefer **«»** for consistency.
- **Dash:** the em dash **—** is used for in-sentence breaks (present in `Core/Keyed/Misc_Gameplay.xml`).
  Use it for prose asides, not as a fragment joiner (see below).
- **Decimal separator:** Ukrainian convention is the comma (`3,5`), but **leave any number that
  arrives via a placeholder exactly as the game formats it** — do not reformat `{0}`.
- **Composed fragment joiners stay ASCII.** The mod glues TTS segments with `". "`, `", "`, `": "`
  exactly like English (e.g. `RimWorldAccess.TwoLevel.ButtonAnnouncement` = `{0}. Button. {1}`).
  **Keep these joiners as ASCII period/comma/colon followed by a single space.** TTS engines and
  screen readers reliably treat `. ` / `, ` / `: ` as segment/pause boundaries; the trailing space
  also safely separates an adjacent number or Latin token. Do NOT replace them with em dashes or
  drop the space.

> Quick rule: prose *inside one translated phrase* → normal Ukrainian punctuation incl. «» and —.
> Glue *between composed fragments* and *label: value* colons → keep ASCII `. ` `, ` `: ` with the
> trailing space, exactly as in the English source.

### 3.4 Gender

Grammatical gender bites in three places: **adjectives/participles agreeing with a noun**,
**past-tense verbs**, and **predicate adjectives**. When our string describes an action by or on a
pawn (or an object) of unknown gender, do NOT pick a masculine/feminine form.

- **Prefer impersonal/neuter `-но/-то` stative forms** for "done"-style states: `вибрано`
  (selected), `розгорнуто` (expanded), `згорнуто` (collapsed), `увімкнено`/`вимкнено` (on/off),
  `заборонено` (forbidden). These agree with nothing and are gender-safe. The game uses this pattern
  (e.g. action-result tooltips).
- **Prefer the bare infinitive or imperative for commands**, exactly as the game does for order
  buttons: `Видобути`, `Перенести`, `Призначити`, `Лікувати` — none carry gender.
- **Avoid masculine past tense** like `Перейшов` / `Вибрав` (these assume a male subject). Recast as
  impersonal: `Перейдено до: {0}` / `Вибрано: {0}`.

Example from the corpus showing the neuter/impersonal habit: `Активовано` / `Деактивовано` are used
as state words rather than gendered «активний»/«активна».

### 3.5 Tone / register — use formal «ви»

RimWorld's Ukrainian addresses the player with the **formal second person «ви»** (e.g.
`«…вимагає, щоб ви завантажили…»`, `«Виберіть наступну річ…»` — formal imperative). The informal
«ти» appears only rarely (mostly inside flavor text/letters). **Translators MUST use formal «ви»**
and the matching formal imperative (`Виберіть`, `Натисніть`, `Оберіть`).

- Match RimWorld's neutral, terse UI register: **imperatives for commands** (`Збудувати`,
  `Призначити`, `Скасувати`), **nouns for states** (`настрій`, `здоров'я`).
- No exclamation marks unless the English/game has them. **No first person. No emoji.**
- When a tooltip is a full sentence in English, translate it as a full sentence ending with `.`.
  When it is a short label, keep it short.

### 3.6 Placeholders, line breaks, keys, spacing

- Copy `{0}`, `{1}`, `{NamedArg}`, `{PAWN_label}`, `{lookup: …}`, etc. **byte-for-byte.** Never
  translate, never add/remove braces, never renumber. The **set** of placeholders in a value must be
  identical to the English source.
- You **may reposition** a placeholder within a sentence for natural Ukrainian word order
  (`string.Format` is positional). Repositioning is encouraged where it reads better; renumbering or
  translating is forbidden.
- Copy `\n` line breaks exactly and keep them in the same logical spots.
- **XML keys are never translated** — keep every element name byte-for-byte identical to English, or
  the string silently fails to load.
- **XML comments are developer context — leave them in English.** Do not translate `<!-- … -->`.
- **Preserve leading/trailing spaces in values.** Some keys are suffixes that join onto a preceding
  label (e.g. `RimWorldAccess.InfoCard.Inspectable` = `" Inspectable."` with a leading space;
  `RimWorldAccess.Menu.LevelSuffix` = `" level {0}"`). Keep the exact leading/trailing space.
- Where a number/placeholder abuts Ukrainian text, keep a single normal space (`{0} елементів`,
  `рівень {0}`). Do not glue them together.

---

## Section 4 — How to extend this glossary

To find how RimWorld translates a term you don't see above:

```bash
# UI strings (button labels, menu text, gameplay commands):
grep -rh 'EnglishKeyName' /tmp/uk_ref/*/Keyed/*.xml

# Or search by a Ukrainian word you suspect:
grep -rh 'українське_слово' /tmp/uk_ref/*/Keyed/*.xml

# Concept labels (things, designators, factions, skills, needs, biomes…):
grep -rh 'EnglishKeyName' /tmp/uk_ref/*/DefInjected/**/*.xml
```

Useful sub-paths: `Keyed/Designators.xml` (order verbs), `Keyed/GameplayCommands.xml` (commands),
`Keyed/Misc.xml` + `Keyed/Misc_Gameplay.xml` (general UI), `Keyed/Time.xml` (calendar),
`Keyed/Skills.xml`, `Keyed/Stats.xml`, `DefInjected/WorkTypeDef`, `DefInjected/SkillDef`,
`DefInjected/NeedDef`, `DefInjected/DesignationCategoryDef`, `DefInjected/BiomeDef`,
`DefInjected/WeatherDef`, `DefInjected/FactionDef`, `DefInjected/HistoryEventDef`.

The English comments in the corpus (`<!-- EN: … -->`) let you confirm a key's meaning before trusting
its Ukrainian value. Always cite the file you took a term from when you add a row.
