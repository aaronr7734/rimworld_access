# Russian (Русский) Terminology & Style Glossary — RimWorld Access

This glossary locks in consistent Russian wording for translating the RimWorld Access
screen-reader mod. The goal is that Russian players feel the mod is a seamless extension of
RimWorld itself, so **every game-anchored term below uses the EXACT word RimWorld's own official
Russian translation uses**, with a citation to the reference file it was taken from.

The strings we translate are **spoken aloud by a TTS engine**, not displayed. Natural, terse,
unambiguous phrasing matters more than visual polish.

Reference corpus: RimWorld's official Russian, extracted to `/tmp/ru_ref/`
(Core + Royalty + Ideology + Biotech + Anomaly + Odyssey). Paths below are relative to `/tmp/ru_ref/`.

> How to use this file: grep for the English term. If a term you need is not here and RimWorld has
> it, grep the game corpus yourself (see Section 4) and add it — never invent a rendering for a
> concept the game already names.

> **RUSSIAN IS DIFFERENT FROM UKRAINIAN ON PLURALS.** Russian ships `LanguageWorker_Russian`
> (`TotalNumCaseCount => 3`) and the game's own Russian uses the `{N_numCase ? one : few : many}`
> tag ~148 times. For Russian you MUST use `_numCase` three-form tags for counted nouns. See §3.2 —
> this is the opposite of the Ukrainian rule.

---

## Section 1 — Core game vocabulary (game-anchored)

Every row is the term RimWorld itself ships in Russian. Use it verbatim. These are the words the
player already hears from the game, so our mod must match them exactly.

### People, factions, world

| English | Русский | Source file |
|---|---|---|
| colonist (colony member) | поселенец | `Core/Keyed/Misc_Gameplay.xml` (`ColonistsIdle` {0_numCase ? поселенец … : … : поселенцев …}); `Core/Keyed/Alerts.xml` (`LowFoodDesc` Поселенцы и пленники…). Note: the ThingDef `Colonist.label` is **житель** (`Core/DefInjected/ThingDef`) but the UI/alert word for a colony member is **поселенец** — use поселенец |
| colony / settlement (your base) | поселение | `Core/Keyed/Alerts.xml` (`LowFoodDesc` У поселения мало продовольствия); `Core/Keyed/Alerts.xml` (`NeedBatteriesDesc` …Ваше поселение…) |
| pawn / character (generic) | персонаж | `Core/Keyed/Misc_Gameplay.xml` (`ShowBeautyToggleButton` …персонаж, стоящий…) |
| faction | фракция | `Core/Keyed/Misc_Gameplay.xml` (`SettlementTrader` {0} из фракции {1}); `Core/Keyed/Incidents.xml` (`RefugeePodCrash_Hostile` …враждебной фракции…) |
| caravan | караван | `Core/Keyed/Misc_Gameplay.xml` (`Caravan` Караван; `CaravanColonistsCount`, `CaravanAnimalsCount`). Note: RimWorld uses **караван** for both player expeditions and trader caravans |
| raid / raider | налёт / налётчик | `Core/Keyed/Incidents.xml` (`Raid` Налёт; `AlertTimedRaidsArrivingIn` Налёты начнутся…); налётчик in `EscapeShipFound` (…банд отчаянных налётчиков…) |
| map | карта | `Core/Keyed/Misc.xml` (`Map` Карта) |
| tile / map cell | клетка | `Core/Keyed/Misc.xml` (`SelectNextInSquareTip` …на той же клетке). Use **клетка** for a single map cell |
| region / area (map area) | область | `Core/Keyed/Misc.xml` (`AreaLower` область); `Core/Keyed/Designators.xml` (`DesignatorExpandAreaAllowed` …доступную область) |
| zone | область | `Core/DefInjected/DesignationCategoryDef/DesignationCategories.xml` (`Zone.label` области); `Core/Keyed/Misc.xml` (`Zone` Область). Note: RimWorld Russian renders "zone" as **область** (same word as area) |
| world / planet | мир | `Core/Keyed/Misc_Gameplay.xml` (`GameOverPlanetkillerImpact` …этот мир расплавился…) |
| prisoner | пленник | `Core/Keyed/Misc_Gameplay.xml` (`Prisoner` Пленник, `PrisonerLower` пленник); `Core/DefInjected/HistoryEventDef/*` (`PrisonerDied.label` пленник погиб) |

### Designators / order verbs (imperative/infinitive form, as the game uses them on buttons)

| English | Русский | Source file |
|---|---|---|
| cancel | Отменить | `Core/Keyed/Designators.xml` (`DesignatorCancel`) |
| chop wood / harvest wood | Заготовить древесину | `Core/Keyed/Designators.xml` (`DesignatorHarvestWood`) |
| mine | Копать | `Core/Keyed/Designators.xml` (`DesignatorMine`) |
| mine vein (mark) | Копать залежи | `Core/Keyed/Designators.xml` (`DesignatorMineVein`) |
| harvest | Собрать | `Core/Keyed/Designators.xml` (`DesignatorHarvest`) |
| cut plants | Срубить | `Core/Keyed/Designators.xml` (`DesignatorCutPlants`) |
| deconstruct | Разобрать | `Core/Keyed/Designators.xml` (`DesignatorDeconstruct`) |
| uninstall | Убрать | `Core/Keyed/Designators.xml` (`DesignatorUninstall`) |
| haul | Перенести | `Core/Keyed/Designators.xml` (`DesignatorHaulThings`) |
| hunt | Охотиться | `Core/Keyed/Designators.xml` (`DesignatorHunt`) |
| tame | Приручить | `Core/Keyed/Designators.xml` (`DesignatorTame`) |
| slaughter | Забить | `Core/Keyed/Designators.xml` (`DesignatorSlaughter`) |
| forbid | Запретить | `Core/Keyed/Designators.xml` (`DesignatorForbid`) |
| unforbid / allow | Разрешить | `Core/Keyed/Designators.xml` (`DesignatorUnforbid`) |
| claim | Захватить | `Core/Keyed/Designators.xml` (`DesignatorClaim`) |
| strip | Раздеть | `Core/Keyed/Designators.xml` (`DesignatorStrip`) |
| smooth (surface) | Выровнять каменную поверхность | `Core/Keyed/Designators.xml` (`DesignatorSmoothSurface`) |
| plan | Планировка | `Core/Keyed/Designators.xml` (`DesignatorPlan`) |
| build | Построить / Строить | `Core/Keyed/Misc_Gameplay.xml` (`CommandBuildCopy` Построить копию); `Core/Keyed/Designators.xml` (`DesignatorAreaBuildRoofExpand` Строить крышу) |
| assign | Назначить | `Core/Keyed/Misc_Gameplay.xml` (`CommandThingSetOwnerLabel` Назначить владельца) |
| tend (a wound) | Лечить | `Core/Keyed/FloatMenu.xml` (`Tend` Лечить …); `Core/Keyed/Misc_Gameplay.xml` (`CanTendNow` Можно лечить) |
| draft (combat mode) | К бою! | `Core/Keyed/Misc_Gameplay.xml` (`CommandDraftLabel`) |
| undraft (free mode) | Роспуск | `Core/Keyed/Misc_Gameplay.xml` (`CommandUndraftLabel`) |
| recruit | вербовать / вербовка | `Core/Keyed/Incidents.xml` (`RefugeePodCrash_Factionless` …для последующей вербовки…) |

### Buildings, zones, areas, build categories

| English | Русский | Source file |
|---|---|---|
| growing zone | Посадки | `Core/Keyed/Misc_Gameplay.xml` (`GrowingZone` Посадки) |
| stockpile / storage zone | Склад / склад | `Core/Keyed/Misc_Gameplay.xml` (`Stockpile` Склад, `StockpileGroup` склад) |
| home area | домашняя область | `Core/Keyed/Designators.xml` (`DesignatorAreaHomeExpand` Расширить домашнюю область) |
| allowed area | доступная область | `Core/Keyed/Designators.xml` (`DesignatorExpandAreaAllowed` Расширить доступную область) |
| structure (build category) | постройка | `Core/DefInjected/DesignationCategoryDef/DesignationCategories.xml` (`Structure.label`) |
| furniture | мебель | same file (`Furniture.label`) |
| floor / flooring | покрытие | same file (`Floors.label`) |
| power | энергия | same file (`Power.label`) |
| production | производство | same file (`Production.label`) |
| security | защита | same file (`Security.label`) |
| temperature (category) | температура | same file (`Temperature.label`) |
| orders | приказы | same file (`Orders.label`) |
| recreation (category) | развлечения | same file (`Joy.label`) |
| misc | разное | same file (`Misc.label`) |
| blueprint | проект | `Core/Keyed/Misc_Gameplay.xml` (`CommandPlaceBlueprints` Разместить проект) |

### Work, schedule, skills, needs

| English | Русский | Source file |
|---|---|---|
| work (tab/menu) | Работа | `Core/Keyed/Alerts.xml` (`NeedDoctorDesc` …Во вкладке «Работа»…; `NeedMinerDesc` Во вкладке «Работа»…) |
| work type / skill | use the specific game label (Шахтёр, Врач, …) | `Core/DefInjected/WorkTypeDef/*`, `Core/DefInjected/SkillDef/*` |
| passion | Интерес | `Core/Keyed/Skills.xml` (`Passion`) |
| passion — none | Интерес отсутствует | `Core/Keyed/Skills.xml` (`PassionNone`) |
| passion — minor | Средний интерес | `Core/Keyed/Skills.xml` (`PassionMinor`) |
| passion — major | Горячий интерес | `Core/Keyed/Skills.xml` (`PassionMajor`) |
| mood | настроение | `Core/DefInjected/NeedDef/Needs.xml` (`Mood.label`) |
| need | нужда | `Core/Keyed/Alerts.xml` (`BreakRiskDescEnding` …вкладку «Нужды»…) |
| food (need) | сытость | `Core/DefInjected/NeedDef/Needs.xml` (`Food.label`) |
| recreation / joy (need) | удовлетворённость | same file (`Joy.label`) |
| rest / sleep | сон | same file (`Rest.label`) |
| comfort | комфорт | same file (`Comfort.label`) |
| beauty | красота окружения | same file (`Beauty.label`) |
| room size | площадь помещения | same file (`RoomSize.label`) |

### Health

| English | Русский | Source file |
|---|---|---|
| health | Здоровье | `Core/Keyed/Misc.xml` (`TabHealth` Здоровье) |
| overview | Обзор | `Core/Keyed/Misc.xml` (`HealthOverview` Обзор) |
| bleeding (rate) | Кровотечение | `Core/Keyed/ITabs.xml` (`BleedingRate`) |
| tend quality | Качество лечения | `Core/Keyed/ITabs.xml` (`TendQuality`) |
| self-tend | самолечение | `Core/Keyed/GameplayCommands.xml` (`SelfTendDisabled` самолечение недоступно) |
| body part | use the specific game label | `Core/DefInjected/BodyPartDef/*` |

### Trade

| English | Русский | Source file |
|---|---|---|
| trade | Торговать | `Core/Keyed/Misc_Gameplay.xml` (`CaravanMeeting_Trade`, `CommandTrade` Торговать) |
| selling | продажа | `Core/Keyed/Misc_Gameplay.xml` (`Selling`) |
| buying | покупка | `Core/Keyed/Misc_Gameplay.xml` (`Buying`) |
| silver | серебро | `Core/DefInjected/ThingDef/*` (`Silver.label`); `Core/Keyed/Misc_Gameplay.xml` (`NotEnoughSilver` недостаточно серебра) |
| transport / drop pod | капсула | `Core/Keyed/Misc_Gameplay.xml` (`PlayerPawnsArriveMethod_DropPods` В капсулах) |

### Research

| English | Русский | Source file |
|---|---|---|
| research (tab / main button) | технологии | `Core/DefInjected/MainButtonDef/MainButtons.xml` (`Research.label` технологии). The verb "Research" key is **Исследовать** (`Core/Keyed/Misc_Gameplay.xml` `Research`); the noun for a project / study is **исследование** (`StopResearch` Остановить исследование) |
| research project | исследование / проект | `Core/Keyed/Alerts.xml` (`NeedResearchProject` Не назначено исследование); `Core/Keyed/Misc_Gameplay.xml` (`ResearchCostComparison` …стоимость проекта…) |
| tech level — animal | животный | `Core/Keyed/Misc.xml` (`TechLevel_Animal`) |
| tech level — neolithic | неолитический | `Core/Keyed/Misc.xml` (`TechLevel_Neolithic`) |
| tech level — medieval | средневековый | `Core/Keyed/Misc.xml` (`TechLevel_Medieval`) |
| tech level — industrial | индустриальный | `Core/Keyed/Misc.xml` (`TechLevel_Industrial`) |
| tech level — spacer | космический | `Core/Keyed/Misc.xml` (`TechLevel_Spacer`) |
| tech level — ultra | ультратехнологичный | `Core/Keyed/Misc.xml` (`TechLevel_Ultra`) |
| tech level — archotech | архотехнологичный | `Core/Keyed/Misc.xml` (`TechLevel_Archotech`) |

### Prisoner, ideology, ritual, abilities

| English | Русский | Source file |
|---|---|---|
| prisoner | пленник | `Core/Keyed/Misc_Gameplay.xml` (`Prisoner` Пленник); `Core/DefInjected/HistoryEventDef/*` (`PrisonerDied.label`) |
| for prisoners (bed) | Для пленников | `Core/Keyed/Misc_Gameplay.xml` (`CommandBedSetForPrisonersLabel`) |
| warden (the role) | надзиратель | `Core/DefInjected/WorkTypeDef/WorkTypes.xml` (`Warden.pawnLabel` Надзиратель; `Warden.labelShort` надзор; `Warden.label` Надзор) |
| ritual | ритуал | `Ideology/Keyed/*` (`Rituals` Ритуалы) |
| belief / ideoligion | вера | `Ideology/Keyed/*` (`BeliefInIdeo` Вера в) |
| ability (psycast) | способность | `Royalty/Keyed/Misc_Gameplay.xml` (`CommandPsycastWouldExceedEntropy` …использовать способность); `Royalty/Keyed/*` (`Abilities` Способности) |
| psyfocus | пси-концентрация | `Royalty/Keyed/Misc_Gameplay.xml` (`Psyfocus` Пси-концентрация; `CommandPsycastLowPsyfocus` …ед. пси-концентрации…) |

### Weather, season, temperature, dates

| English | Русский | Source file |
|---|---|---|
| temperature | Температура | `Core/Keyed/Misc.xml` (`Temperature`) |
| weather | use the specific WeatherDef label | `Core/DefInjected/WeatherDef/*` |
| season — spring/summer/fall/winter | весна / лето / осень / зима | `Core/Keyed/Time.xml` (`SeasonSpring`…`SeasonWinter`) |
| quadrum | use the specific quadrum label (Мартомай, …) | `Core/Keyed/Time.xml` (`QuadrumAprimay` Мартомай) |
| day(s) (lower) | дней | `Core/Keyed/Time.xml` (`DaysLower`) — bare genitive-plural fallback; **but if a count `{0}` precedes it, use the `_numCase` tag — see §3.2** |
| hour(s) (lower) | часов | `Core/Keyed/Time.xml` (`HoursLower`) — same note as days |
| period — days (counted) | {0_numCase ? день : дня : дней} | `Core/Keyed/Time.xml` (`PeriodDays`) — **canonical `_numCase` model for a counted day count** |
| year (abbrev.) | г | `Core/Keyed/Time.xml` (`LetterYear`) |

### Gizmo / command / button labels

| English | Русский | Source file |
|---|---|---|
| range | Дальность | `Core/Keyed/Dialogs_Various.xml` (`Range` Дальность) |
| radius | Радиус | `Core/Keyed/Misc_Gameplay.xml` (`IngredientSearchRadius` Радиус поиска) |
| enable / activate | Включить / Активировать | `Core/Keyed/Misc.xml` (`Enable` Включить); `Core/Keyed/Misc_Gameplay.xml` (`Activate` Активировать) |
| disable / deactivate | Отключить | `Core/Keyed/Misc.xml` (`Disable` Отключить) |
| enabled (state) | Включено | `Core/Keyed/Misc.xml` (`Enabled`) |
| disabled (state) | Отключено | `Core/Keyed/Misc.xml` (`Disabled`) — neuter `-о` stative, gender-safe (see §3.4) |
| close (button) | Закрыть | `Core/Keyed/Misc.xml` (`CloseButton`) |
| cancel (button) | Отменить | `Core/Keyed/Misc.xml` (`CancelButton`) |
| accept (button) | Принять | `Core/Keyed/Misc.xml` (`AcceptButton`) |
| confirm | Подтвердить | `Core/Keyed/Misc.xml` (`Confirm`) |
| OK | OK | `Core/Keyed/Misc.xml` (`OK`) |
| yes | Да | `Core/Keyed/Misc.xml` (`Yes`) |
| no | Нет | `Core/Keyed/Misc.xml` (`No`) |
| select | Выбрать | `Core/Keyed/Misc.xml` (`SelectNextInSquareTip` Выбрать другой предмет…) |

---

## Section 2 — Accessibility-specific vocabulary (mod-coined)

These concepts RimWorld does NOT name. We lock a Russian rendering here. Where the game's own UI
offers a near-match, we reuse it and cite it so users hear familiar wording.

| English | Русский (locked) | Rationale / source |
|---|---|---|
| screen reader | программа экранного доступа | Standard Russian term (NVDA-ru, JAWS-ru, Windows «Экранный диктор» docs). «скринридер» is acceptable shorthand if length matters |
| announce / announcement | озвучить / озвучивание | Established screen-reader verb in Russian. Prefer over «объявить» |
| navigate / navigation | навигация / переходить | Standard Russian UI term |
| cursor | курсор | RimWorld uses it: `Core/Keyed/Misc_Gameplay.xml` (`ShowBeautyToggleButton` …на который указывает курсор) |
| menu | меню | RimWorld uses it: `Core/Keyed/Menus_Main.xml` (`QuitToMainMenu` Выйти в главное меню) |
| tab (UI tab) | вкладка | RimWorld uses it: `Core/Keyed/Alerts.xml` (`NeedDoctorDesc` …Во вкладке «Работа»…) |
| search | поиск / искать | RimWorld uses both: `Core/Keyed/Misc_Gameplay.xml` (`IngredientSearchRadius` Радиус поиска); `Core/Keyed/Misc_Gameplay.xml` (`SearchTheMap` Искать на текущей карте) |
| typeahead search | поиск по вводу | No fixed game term. Use «поиск по вводу» (type-to-find). For the bare act, «поиск» |
| expand (tree) | Развернуть | RimWorld uses it: `Core/Keyed/Misc_Gameplay.xml` (`ExpandAllCategories` Развернуть всё) |
| collapse (tree) | Свернуть | RimWorld uses it: `Core/Keyed/Misc_Gameplay.xml` (`CollapseAllCategories` Свернуть всё) |
| expanded (state) | развёрнуто | impersonal/neuter `-о` stative of Развернуть; gender-safe (see §3.4) |
| collapsed (state) | свёрнуто | impersonal/neuter `-о` stative of Свернуть |
| tree view | древовидный список | No game term. «древовидный список» (tree-like list) reads naturally in TTS |
| node (tree node) | узел | Standard Russian UI term; NVDA-ru uses «узел» |
| level (tree depth) | уровень | Standard. e.g. «уровень {0}» |
| toggle (verb/control) | переключить | Standard Russian UI verb. For the bare state words reuse Включено/Отключено or включено/выключено |
| on (state) | включено | Standard; impersonal/neuter, gender-safe; matches game `Enabled` Включено |
| off (state) | выключено | Standard; impersonal/neuter, gender-safe. (Game `Disabled` is «Отключено»; «выключено» is the natural on/off pair — both are gender-safe neuter) |
| selected | выбрано | impersonal/neuter `-о` stative of Выбрать. Gender-safe |
| position ("X of Y") | {0} из {1} | «{0} из {1}» (X of Y). Genitive «из» reads cleanly with numbers; see §3.1 |
| scanner | сканер | Mod-specific feature; «сканер» is the literal, unambiguous rendering (cf. game `CommandSelectMineralToScanForDesc` …для сканирования) |
| jump to | перейти к | Natural Russian («перейти к» = go/jump to). RimWorld uses it: `Core/Keyed/Misc_Gameplay.xml` (`ClickToViewInQuestsTab` …перейти к этому заданию) |
| edge / boundary | начало / конец / край | "Already at top/bottom" → «Уже в начале» / «Уже в конце»; generic boundary = «край» |
| stepper (numeric +/- control) | регулятор значения | No game term. «регулятор значения» describes the control; for minimum/maximum reuse «минимум» / «максимум» |
| hotkey | горячая клавиша | No single fixed game term; RimWorld labels the key as **Клавиша** (`Core/Keyed/Misc.xml` `SelectNextInSquareTip` «Клавиша: {0}») and a combo as **Комбинация** (`HotKeyTip`). For our standalone "hotkey" use the conventional Russian «горячая клавиша»; for "Key: {0}" prompts reuse the game's «Клавиша: {0}» |
| accessibility | доступность | Standard Russian term |
| inspect / inspection | обзор / осмотреть | Reuse game «Обзор» (`HealthOverview`); for the action «осмотреть» |
| button | кнопка | Standard Russian UI term; game uses it (`BindingButtonToolTip` …кнопку…) |

---

## Section 3 — Style rules for translators

### 3.1 Grammatical case & agreement (CRITICAL)

Our strings are mostly **label-value pairs** and **composed fragments** built with `string.Format`
placeholders (`"{0}: {1}"`, `"{0} of {1}"`, `"Range: {0}"`). The value substituted into a
placeholder almost always arrives from the game **already inflected, in the nominative case**
(an item label, a pawn name, a status word). **We cannot force a substituted value into genitive,
accusative, etc.** So we must choose sentence shapes that read correctly with a nominative insertion.

(The one exception is a **count** passed as a raw numeric — that drives `_numCase`; see §3.2.)

**Rules:**

1. **Prefer label-then-colon-then-value.** `"Range: {0}"` → `«Дальность: {0}»`. The colon decouples
   the noun from grammatical agreement — `{0}` stays nominative and it reads fine.
2. **Avoid constructions that grammatically demand a non-nominative inserted noun.** Do NOT write
   something that requires `{0}` to be genitive/accusative, because the game won't supply that form.
   - **Don't:** `«Нет {0}»` (genitive expected after «нет») when `{0}` is a nominative label.
   - **Do:** `«{0}: отсутствует»` or `«{0} — нет»` (keep `{0}` nominative, attach the state after it).
3. **For "verb + object" commands, prefer the bare infinitive + nominative-ish noun** the way the
   game does. RimWorld writes order buttons as plain infinitives (`Копать`, `Перенести`,
   `Назначить`) without forcing the object into a case. When an object label is interpolated, lead
   with the verb and let the label follow as a quoted nominative: e.g. the game's
   `CommandPlaceBlueprintsSpecific` is `«Разместить проект "{0}"»` — `{0}` stays nominative in quotes.

**Concrete rewrites:**

| English source | Bad (forces case on `{0}`) | Good (nominative-safe) |
|---|---|---|
| `Jumped to {0}` | `Перешёл к {0}` (gendered + demands case) | `Перейти к: {0}` or `Цель: {0}` |
| `No {0} available` | `Нет {0}` (demands genitive) | `{0}: недоступно` |
| `Selected {0}` | `Выбран {0}` (gendered + demands agreement) | `Выбрано: {0}` (colon is safest) |

When in doubt, **insert a colon or dash before the placeholder** — it sidesteps the whole agreement
problem and matches the game's own label-value habit (`«Дальность: {0}»`).

### 3.2 Plurals — USE the `_numCase` three-form tag (Russian DOES support it)

**This is the opposite of the Ukrainian rule.** Russian ships `LanguageWorker_Russian` whose
`TotalNumCaseCount => 3`, so RimWorld's grammar engine resolves the three-form inline tag correctly
for Russian. The game's own Russian uses it ~148 times. **For every counted noun, USE the tag:**

```
{N_numCase ? formOne : formFew : formMany}
```

where `N` is the numeric placeholder index (the same `{N}` that holds the count), and:

- **one** = the form for a last digit of **1 but not 11** → 1, 21, 31, 101 (`1 поселенец`)
- **few** = the form for last digits **2–4 but not 12–14** → 2, 3, 4, 22, 23 (`2 поселенца`)
- **many** = the form for **0, 5–9, and the teens 11–14** → 0, 5, 11, 12, 25 (`5 поселенцев`, `0 поселенцев`)

Standardize on the **`?` separator** form (space-separated, ` ? ` and ` : `), exactly like the
corpus: `{0_numCase ? день : дня : дней}`.

**Mechanism (verified against `.claude/skills/rimworld-access-localizer/references/engine-internals.md`
and the corpus):** `GrammarResolverSimple.ResolveNumCase` checks the branch count against
`activeLanguage.info.totalNumCaseCount ?? activeLanguageWorker.TotalNumCaseCount`. Russian's
`LanguageWorker_Russian` returns **3**, so a 3-branch tag passes. The form is chosen by
`GetFormForNumber` from the **raw numeric argument**, so the count MUST reach the value as a real
number — it does when the C# calls `"Key".Translate(count)` (or `count.ToString()` is *not* used; pass
the number itself). When `{0}` is the count, `"Key".Translate(count)` makes `{0}` the number and
`{0_numCase ? … : … : …}` inflects off it.

**Verbatim corpus examples (confirm by grepping `/tmp/ru_ref/`):**

- `<PeriodDays>{0_numCase ? день : дня : дней}</PeriodDays>` (`Core/Keyed/Time.xml`)
- `<NextTraderRestock>Список обновится через {0_numCase ? день : дня : дней}</NextTraderRestock>`
- `<ColonistsIdle>{0_numCase ? поселенец бездельничает : поселенца бездельничают : поселенцев бездельничают}</ColonistsIdle>`
- `<MapSearchResults>{0_numCase ? результат : результата : результатов}</MapSearchResults>`
- `<Stat_TrapDamageHitCount>{0_numCase ? удар : удара : ударов}</Stat_TrapDamageHitCount>`
- `{0_numCase ? год : года : лет}`, `{0_numCase ? навык : навыка : навыков}`,
  `{0_numCase ? механоид : механоида : механоидов}` (elsewhere in the corpus)

**Worked table — use a `_numCase` tag for every counted noun:**

| English | Русский (`_numCase`) |
|---|---|
| `{0} colonists` | `{0_numCase ? колонист : колониста : колонистов}` (or use поселенец: `{0_numCase ? поселенец : поселенца : поселенцев}`) |
| `{0} days` | `{0_numCase ? день : дня : дней}` |
| `{0} items` | `{0_numCase ? предмет : предмета : предметов}` |
| `{0} components` | `{0_numCase ? компонент : компонента : компонентов}` |
| `{0} characters` | `{0_numCase ? символ : символа : символов}` |
| `{0} hours` | `{0_numCase ? час : часа : часов}` |

Note: the tag only inflects the noun; if the verb/adjective also changes with number, you may inflect
it inside the same tag (as `ColonistsIdle` does: `… бездельничает : … бездельничают : … бездельничают`).

#### One / Many key PAIRS (when C# pre-selects the key by `count == 1 ? One : Many`)

Some of our strings are split into a `…One` / `…Many` key pair, chosen in C# by `count == 1`. The
`One` key is only ever shown for count 1; the `Many` key covers **0, 2, 3, 4, 5, …**.

- **`…One` key → nominative singular:** `Развёрнуто {0} элемент`.
- **`…Many` key:** the C# pre-select can't see *which* multi-count it is, so the value can't branch
  by itself — UNLESS the value carries the `{0}` count placeholder. Therefore:
  - **If the `…Many` value contains the count `{0}`**, put a `_numCase` tag *inside it* so it inflects
    correctly across 2–4 vs 5+: `Развёрнуто {0_numCase ? элемента : элемента : элементов}` (here `one`
    is never reached because the One key handles count 1, but fill all three branches anyway — keep
    `few` = `элемента`, `many` = `элементов`).
  - **If the `…Many` value has NO count placeholder**, use the plain genitive plural: `Развёрнуто
    элементов`.

Always fill **both** keys of a pair — never leave one blank, or the mod falls back to English for that
count.

### 3.3 Punctuation & typography

Russian uses **Latin-style punctuation marks** (`. , : ; ! ?`) — there is no full-width punctuation.

- **Quotation marks:** primary level is **«ёлочки» (guillemets)**, confirmed throughout the corpus
  (`«Работа»`, `«Нужды»`, `«{0}»`, `«{lookup: …}»` in `Core/Keyed/*`). Inner/nested level uses
  **„лапки“** (`„ “`). Newer DLC strings sometimes use straight `"..."`; prefer **«»** for consistency.
- **Dash:** the em dash **—** is used for in-sentence breaks (present throughout the corpus, e.g.
  `ResearchCostComparison` «…стоимость проекта — {0}…»). Use it for prose asides, not as a fragment
  joiner (see below).
- **Decimal separator:** Russian convention is the comma (`3,5`), but **leave any number that arrives
  via a placeholder exactly as the game formats it** — do not reformat `{0}`.
- **Composed fragment joiners stay ASCII.** The mod glues TTS segments with `". "`, `", "`, `": "`
  exactly like English (e.g. `RimWorldAccess.TwoLevel.ButtonAnnouncement` = `{0}. Button. {1}`).
  **Keep these joiners as ASCII period/comma/colon followed by a single space** even though Russian
  prose uses «». TTS engines and screen readers reliably treat `. ` / `, ` / `: ` as segment/pause
  boundaries; the trailing space also safely separates an adjacent number or Latin token. Do NOT
  replace them with em dashes or drop the space.

> Quick rule: prose *inside one translated phrase* → normal Russian punctuation incl. «» and —.
> Glue *between composed fragments* and *label: value* colons → keep ASCII `. ` `, ` `: ` with the
> trailing space, exactly as in the English source.

### 3.4 Gender

Grammatical gender bites in three places in Russian: **adjectives/participles agreeing with a noun**,
**past-tense verbs**, and **predicate adjectives/participles**. When our string describes an action by
or on a pawn (or an object) of unknown gender, do NOT pick a masculine/feminine form.

- **Prefer impersonal/neuter `-но/-то` stative forms** for "done"-style states: `выбрано`
  (selected), `развёрнуто` (expanded), `свёрнуто` (collapsed), `включено`/`выключено` (on/off),
  `запрещено` (forbidden), `назначено` (assigned). These agree with nothing and are gender-safe. The
  game uses this pattern (`Enabled` Включено, `Disabled` Отключено, `NeedResearchProject` Не назначено
  исследование).
- **Prefer the bare infinitive or imperative for commands**, exactly as the game does for order
  buttons: `Копать`, `Перенести`, `Назначить`, `Лечить` — none carry gender.
- **Avoid masculine past tense** like `Перешёл` / `Выбрал` (these assume a male subject) and gendered
  short participles like `Выбран`/`Выбрана`. Recast impersonally: `Перейдено к: {0}` → better
  `Переход к: {0}` / `Цель: {0}`; `Выбран {0}` → `Выбрано: {0}`.

Example from the corpus showing the neuter/impersonal habit: `Включено` / `Отключено` are used as state
words rather than gendered «включён»/«включена».

### 3.5 Tone / register — use formal «вы»

RimWorld's Russian addresses the player with the **formal second person «вы»** (lowercase, as is the
modern Russian UI convention), e.g. `BindingButtonToolTip` «…действие, на которое **вы** хотите
назначить…», `RefugeePodCrash_Hostile` «**Вы** можете захватить…», imperatives `Нажмите`, `Выберите`,
`Найдите`. The informal «ты» appears only rarely (mostly inside flavor text/letters). **Translators
MUST use formal «вы»** and the matching formal plural imperative (`Выберите`, `Нажмите`, `Найдите`).

- Match RimWorld's neutral, terse UI register: **infinitives/imperatives for commands** (`Построить`,
  `Назначить`, `Отменить`), **nouns for states** (`настроение`, `здоровье`).
- No exclamation marks unless the English/game has them (note the game's `CommandDraftLabel` «К бою!»
  is an intentional exception). **No first person. No emoji.**
- When a tooltip is a full sentence in English, translate it as a full sentence ending with `.`.
  When it is a short label, keep it short.

### 3.6 Placeholders, line breaks, keys, spacing

- Copy `{0}`, `{1}`, `{NamedArg}`, `{PAWN_label}`, `{lookup: …}`, `{0_numCase ? … : … : …}`, etc.
  **byte-for-byte.** Never translate a placeholder, never add/remove braces, never renumber. The
  **set** of placeholders in a value must be identical to the English source (the `_numCase` tag is an
  *addition around an existing* `{0}`, not a new placeholder — see §3.2).
- You **may reposition** a placeholder within a sentence for natural Russian word order
  (`string.Format` is positional). Repositioning is encouraged where it reads better; renumbering or
  translating is forbidden.
- Copy `\n` line breaks exactly and keep them in the same logical spots.
- **XML keys are never translated** — keep every element name byte-for-byte identical to English, or
  the string silently fails to load.
- **XML comments are developer context — leave them in English.** Do not translate `<!-- … -->`.
- **Preserve leading/trailing spaces in values.** Some keys are suffixes that join onto a preceding
  label (e.g. `RimWorldAccess.InfoCard.Inspectable` = `" Inspectable."` with a leading space;
  `RimWorldAccess.Menu.LevelSuffix` = `" level {0}"`). Keep the exact leading/trailing space.
- Where a number/placeholder abuts Russian text, keep a single normal space (`{0} элементов`,
  `уровень {0}`). Do not glue them together.

---

## Section 4 — How to extend this glossary

To find how RimWorld translates a term you don't see above:

```bash
# UI strings (button labels, menu text, gameplay commands):
grep -rh 'EnglishKeyName' /tmp/ru_ref/*/Keyed/*.xml

# Or search by a Russian word you suspect:
grep -rh 'русское_слово' /tmp/ru_ref/*/Keyed/*.xml

# Concept labels (things, designators, factions, skills, needs, biomes…):
grep -rh 'EnglishKeyName' /tmp/ru_ref/*/DefInjected/**/*.xml

# Find how the game handles a plural — copy its _numCase forms:
grep -rh '_numCase' /tmp/ru_ref/*/Keyed/*.xml
```

Useful sub-paths: `Keyed/Designators.xml` (order verbs), `Keyed/GameplayCommands.xml` +
`Keyed/Misc_Gameplay.xml` (commands), `Keyed/Misc.xml` (general UI), `Keyed/Time.xml` (calendar),
`Keyed/Skills.xml`, `Keyed/Alerts.xml`, `DefInjected/WorkTypeDef`, `DefInjected/SkillDef`,
`DefInjected/NeedDef`, `DefInjected/DesignationCategoryDef`, `DefInjected/MainButtonDef`,
`DefInjected/BiomeDef`, `DefInjected/WeatherDef`, `DefInjected/FactionDef`,
`DefInjected/HistoryEventDef`.

The English comments in the corpus (`<!-- EN: … -->`) let you confirm a key's meaning before trusting
its Russian value. Always cite the file you took a term from when you add a row. **For any counted
noun you add, prefer a `_numCase` three-form tag (§3.2) — Russian supports it.**
