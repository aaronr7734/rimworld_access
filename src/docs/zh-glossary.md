# Simplified Chinese (简体中文) Terminology Glossary — RimWorld Access

This glossary locks in consistent Simplified Chinese wording for translating the RimWorld Access
screen-reader mod. The goal is that Chinese players feel the mod is a seamless extension of RimWorld
itself, so **every game-anchored term below uses the EXACT word RimWorld's own official zh-Hans
translation uses**, with a citation to the reference file it was taken from.

Reference corpus: RimWorld's official Simplified Chinese, extracted to `/tmp/zh_ref/`
(Core + Royalty + Ideology + Biotech + Anomaly + Odyssey). Paths below are relative to `/tmp/zh_ref/`.

> How to use this file: grep for the English term. If a term you need is not here and RimWorld has it,
> grep the game corpus yourself (see Section 4) and add it — never invent a rendering for a concept
> the game already names.

---

## Section 1 — Core game vocabulary (game-anchored)

Every row is the term RimWorld itself ships. Use it verbatim.

### People, factions, world

| English | 简体中文 | Source file |
|---|---|---|
| colonist | 殖民者 | `Core/Keyed/Alerts.xml` (`ColonistNeedsRescue` 殖民者需要救援) |
| colony / settlement (your base) | 定居点 | `Core/Keyed/Misc_Gameplay.xml` (`PlayerFactionBaseGainsName`) |
| pawn / character (generic) | 角色 | `Core/Keyed/GameplayCommands.xml` (该角色…); RimWorld uses 角色 for the generic person concept |
| settler | 定居者 | not a distinct UI term in zh-Hans; treat as colonist 殖民者 or 定居者. **See ambiguities.** |
| faction | 派系 | `Core/DefInjected/FactionDef/*` + `Core/Keyed/Misc_Gameplay.xml` (PlayerFaction…) |
| faction leader | 领袖 | `Core/DefInjected/FactionDef/Factions_Misc.xml` (`Ancients.leaderTitle` 领袖) |
| caravan | 远行队 | `Core/Keyed/Alerts.xml` (`CaravanIdle` 远行队闲置). **Use 远行队, NOT 商队** — 远行队 appears 141× vs 商队 13× and 商队 means a trader caravan specifically |
| raid / raider | 袭击 / 袭击者 | `Core/DefInjected/RaidStrategyDef/*` (`*.letterLabelEnemy` 袭击); `Core/DefInjected/IncidentDef` (袭击者) |
| map | 地图 | `Core/Keyed/Misc.xml` (`Map` 地图) |
| tile (world map tile) | 格 / 地块 | `Core/Keyed/Misc_Gameplay.xml` (`TilesPerDay` 格/天). Use 格 in counts ("{0} 格"); 地块 for the noun |
| region (map region) | 区域 | `Core/Keyed/Misc_Gameplay.xml` (`AreaLower` 区域); RimWorld does not surface "region" as distinct UI — use 区域 |
| biome | 生物群系 — but use the specific biome's own label (寒带森林, 沙漠, 热带雨林…) | `Core/DefInjected/BiomeDef/Biomes_Vanilla.xml` |
| settlement / base (other factions) | 定居点 / 前哨站 | `Core/Keyed/Misc_Gameplay.xml` (定居点或前哨站) |
| world | 世界 | `Core/Keyed/Misc_Gameplay.xml` (`WorldFileVersionMismatch` 世界…) |

### Designators / order verbs

| English | 简体中文 | Source file |
|---|---|---|
| cancel | 取消 | `Core/Keyed/Designators.xml` (`DesignatorCancel`) |
| chop wood / harvest wood | 伐木 | `Core/Keyed/Designators.xml` (`DesignatorHarvestWood`) |
| mine | 开采 | `Core/Keyed/Designators.xml` (`DesignatorMine`) |
| mine vein (mark) | 标记开采 | `Core/Keyed/Designators.xml` (`DesignatorMineVein`) |
| harvest | 收获 | `Core/Keyed/Designators.xml` (`DesignatorHarvest`) |
| cut plants | 割除 | `Core/Keyed/Designators.xml` (`DesignatorCutPlants`) |
| sow / plant | 种植 | `Core/Keyed/Misc_Gameplay.xml` (`PlantThing` 种植{0_label}) |
| deconstruct | 拆除 | `Core/Keyed/Designators.xml` (`DesignatorDeconstruct`) |
| uninstall | 卸载 | `Core/Keyed/Designators.xml` (`DesignatorUninstall`) |
| haul | 搬运 | `Core/Keyed/Designators.xml` (`DesignatorHaulThings`) |
| prioritize | 优先 | `Core/Keyed/GameplayCommands.xml` (`PrioritizeGenericSimple` 优先{0}) |
| hunt | 狩猎 | `Core/Keyed/Designators.xml` (`DesignatorHunt`) |
| tame | 驯服 | `Core/Keyed/Designators.xml` (`DesignatorTame`) |
| slaughter | 宰杀 | `Core/Keyed/Designators.xml` (`DesignatorSlaughter`) |
| forbid | 禁用 | `Core/Keyed/Designators.xml` (`DesignatorForbid`) |
| unforbid | 解禁 | `Core/Keyed/Designators.xml` (`DesignatorUnforbid`) |
| claim | 占有 | `Core/Keyed/Designators.xml` (`DesignatorClaim`) |
| strip | 剥光 | `Core/Keyed/Designators.xml` (`DesignatorStrip`) |
| smooth (surface/floor) | 打磨 | `Core/Keyed/Designators.xml` (`DesignatorSmoothSurface` 打磨表面) |
| plan | 计划 | `Core/Keyed/Designators.xml` (`DesignatorPlan`) |
| tend (wound) | 照料 | `Core/Keyed/GameplayCommands.xml` (`Tend` 照料{0}) |
| rescue | 救援 | `Core/Keyed/Alerts.xml` (派遣另一名殖民者去救援) |

### Buildings, zones, areas

| English | 简体中文 | Source file |
|---|---|---|
| zone | 区域 | `Core/DefInjected/DesignationCategoryDef` (`Zone.label` 区域) |
| growing zone | 种植区 | `Core/Keyed/Misc_Gameplay.xml` (`GrowingZone` 种植区) |
| stockpile / storage zone | 储存区 | `Core/Keyed/Misc_Gameplay.xml` (创建一片…储存区) |
| home area | 居住区 | `Core/Keyed/Designators.xml` (`DesignatorAreaHomeExpand` 添加居住区) |
| allowed area | 活动区 | `Core/Keyed/Designators.xml` (`DesignatorExpandAreaAllowed` 添加活动区) |
| structure | 结构 | `Core/DefInjected/DesignationCategoryDef` (`Structure.label`) |
| furniture | 家具 | `Core/DefInjected/DesignationCategoryDef` (`Furniture.label`) |
| floor | 地板 | `Core/DefInjected/DesignationCategoryDef` (`Floors.label`) |
| blueprint | 蓝图 | `Core/Keyed/Misc_Gameplay.xml` (`CommandPlaceBlueprints` 放置蓝图) |
| frame (construction) | 框架 / 施工框架 | `Core/Keyed/*` — RimWorld uses 框架; confirm against `ThingDef` if used as a label |

### Work, schedule, skills, needs

| English | 简体中文 | Source file |
|---|---|---|
| work type (e.g. Doctor, Hauling) | use the specific label (医生, 搬运, 建造…) | `Core/DefInjected/WorkTypeDef/WorkTypes.xml` |
| priority (work priority) | 优先级 | `Core/Keyed/GameplayCommands.xml` (`ManualPriorities` 自定义优先级) |
| skill (e.g. Construction) | use specific label (建造, 烹饪, 射击…) | `Core/DefInjected/SkillDef/Skills.xml` |
| passion | 兴趣度 | `Core/Keyed/Skills.xml` (`Passion` 兴趣度) |
| passion — none | 无 | `Core/Keyed/Skills.xml` (`PassionNone`) |
| passion — minor (interested) | 好奇 | `Core/Keyed/Skills.xml` (`PassionMinor`) |
| passion — major (burning) | 狂热 | `Core/Keyed/Skills.xml` (`PassionMajor`) |
| mood | 心情 | `Core/DefInjected/NeedDef/Needs.xml` (`Mood.label`) |
| need | 需求 | `Core/Keyed/Alerts.xml` (「需求」选项卡) |
| beauty (need) | 美观 | `Core/DefInjected/NeedDef/Needs.xml` (`Beauty.label`) |
| comfort | 舒适 | `Core/DefInjected/NeedDef/Needs.xml` (`Comfort.label`) |
| recreation / joy | 娱乐 | `Core/DefInjected/NeedDef/Needs.xml` (`Joy.label`) |
| food (need) | 饮食 | `Core/DefInjected/NeedDef/Needs.xml` (`Food.label`) |

### Health

| English | 简体中文 | Source file |
|---|---|---|
| health | 健康 | `Core/Keyed/*` (`Health` 健康; `TabHealth` 健康) |
| hediff / health condition | 健康状况 / 病症 | RimWorld surfaces these via specific HediffDef labels; use the specific label. 健康状况 for the generic word |
| body part | 身体部位 | `Core/Keyed/Stats.xml` (…对于给定的身体部位…); also 身体部件 in some stat reports |
| bleeding | 流血 | `Core/Keyed/*` (Stat_Hediff_BleedingRate 血液流失); use 流血 for the status word |
| tend (verb) | 照料 | `Core/Keyed/GameplayCommands.xml` (`Tend`) |
| self-tend | 自我治疗 | `Core/Keyed/GameplayCommands.xml` (`SelfTendDisabled` 自我治疗已禁用) |

### Trade

| English | 简体中文 | Source file |
|---|---|---|
| trade | 交易 | `Core/Keyed/Misc_Gameplay.xml` (`CaravanMeeting_Trade` 交易) |
| sell / selling | 出售 | `Core/Keyed/Misc_Gameplay.xml` (`Selling` 出售) |
| buy / buying | 购买 | `Core/Keyed/Misc_Gameplay.xml` (`Buying` 购买) |
| silver | 白银 | `Core/Keyed/Misc_Gameplay.xml` (`NotEnoughSilver` 白银不足) |
| trade beacon | 轨道交易信标 | `Core/Keyed/Misc_Gameplay.xml` (`YourTradeableSilverTip`) |
| transport pod / drop pod | 运输舱 | `Core/Keyed/Misc_Gameplay.xml` (`PlayerPawnsArriveMethod_DropPods` 运输舱) |

### Research

| English | 简体中文 | Source file |
|---|---|---|
| research project | 研究项目 | `Core/Keyed/Alerts.xml` (`NeedResearchProject` 缺少研究项目) |
| research (menu) | 研究 | `Core/Keyed/Alerts.xml` (打开「研究」菜单) |
| tech level — neolithic | 石器时代 | `Core/Keyed/Misc.xml` (`TechLevel_Neolithic`) |
| tech level — medieval | 中世纪 | `Core/Keyed/Misc.xml` (`TechLevel_Medieval`) |
| tech level — industrial | 工业时代 | `Core/Keyed/Misc.xml` (`TechLevel_Industrial`) |
| tech level — animal | 茹毛饮血 | `Core/Keyed/Misc.xml` (`TechLevel_Animal`) |

### Prisoner, ideology, ritual, abilities

| English | 简体中文 | Source file |
|---|---|---|
| prisoner | 囚犯 | `Core/Keyed/*` (`CommandBedSetForPrisonersLabel` 囚犯专用) |
| recruit (verb) | 招募 | `Core/Keyed/Incidents.xml` (俘虏…来招募或奴役) |
| warden (work) | 监管 | `Core/DefInjected/WorkTypeDef/WorkTypes.xml` (`Warden.label` 监管) |
| warden (the pawn doing it) | 狱卒 | `Core/DefInjected/WorkTypeDef/WorkTypes.xml` (`Warden.pawnLabel` 狱卒) |
| ideoligion / ideology | 信仰 (belief) / 教义 (precepts) | `Ideology/Keyed/*` (`BeliefInIdeo` 信仰). Use 信仰 for the ideoligion concept |
| ritual | 仪式 | `Ideology/Keyed/*` (`Rituals` 仪式) |
| ability / psycast | 心灵能力 (psycast specifically) | `Royalty/Keyed/*` (心灵能力, 5×). For generic non-psychic abilities use the specific AbilityDef label |
| psyfocus | 精神力 | `Royalty/Keyed/Misc_Gameplay.xml` (`PsyfocusDesc`) |
| psycast level | 灵能水平 / 灵能等级 | `Royalty/Keyed/Misc_Gameplay.xml` (`PsyfocusLevelInfoPsycasts` 灵能水平) |

### Weather, season, temperature, dates

| English | 简体中文 | Source file |
|---|---|---|
| weather | use specific label (晴, 雨, 雾, 暴风雨, 小雪, 大雪…) | `Core/DefInjected/WeatherDef/Weathers.xml` |
| temperature | 温度 | `Core/Keyed/*` (`Temperature` 温度) |
| season — spring/summer/fall/winter | 春季 / 夏季 / 秋季 / 冬季 | `Core/Keyed/Time.xml` (`SeasonSpring`…) |
| quadrum | 象 (e.g. 翠象, 赫象, 荼象, 素象) | `Core/Keyed/Time.xml` (`QuadrumAprimay` 翠象). The word for "quadrum" itself is 象 |
| day / year / hour | 天 / 年 / 小时 | `Core/Keyed/Time.xml` (`DaysLower` 天, `LetterYear` 年, `HoursLower` 小时) |
| date — "{0} of {1}, {2}" | {2}年{1}第{0}天 | `Core/Keyed/Dates.xml` (`FullDate`). Note placeholder reorder — see Section 3 |
| time (clock) | 时间 | `Core/Keyed/Dates.xml` (`ClockTime` 时间) |

### Gizmo / command / button labels

| English | 简体中文 | Source file |
|---|---|---|
| command (gizmo) | 命令 | `Core/DefInjected/DesignationCategoryDef` (`Orders.label` 命令); `Core/Keyed/Misc_Gameplay.xml` (`MessageFormedCaravan_Orders` 命令) |
| enable | 启用 | `Core/Keyed/Misc.xml` (`Enable` 启用) |
| disable | 禁用 | `Core/Keyed/Misc.xml` (`Disable` 禁用) |
| enabled / disabled (state) | 启用 / 禁用 | `Core/Keyed/Misc.xml` (`Enabled`, `Disabled`) |
| on | 开 | `Core/Keyed/Misc.xml` (`On` 开) |
| off | 关 | `Core/Keyed/Misc.xml` (`Off` 关) |
| yes | 是 | `Core/Keyed/Misc.xml` (`Yes` 是) |
| no | 否 | `Core/Keyed/Misc.xml` (`No` 否) |
| close | 关闭 | `Core/Keyed/Misc.xml` (`Close` 关闭) |
| toggle | 切换 | RimWorld does not ship a standalone "Toggle" Keyed label; use 切换 (standard zh UI). See Section 2 |

---

## Section 2 — Accessibility-specific vocabulary (mod-coined)

These concepts RimWorld does NOT name. We choose and lock a Chinese rendering. Where a Chinese
screen-reader community convention exists (NVDA-zh, JAWS-zh, Windows 讲述人/Narrator), it is noted
and preferred so blind Chinese users hear familiar wording.

| English | 简体中文 (locked) | Rationale / convention |
|---|---|---|
| screen reader | 屏幕阅读器 | Standard term across NVDA-zh, JAWS-zh, Windows 讲述人 docs. Universally understood |
| cursor | 光标 | Standard. (Map cursor = 光标; if context needs "selection cursor" still 光标) |
| navigate / navigation | 导航 | Standard zh UI term; matches NVDA-zh "导航" |
| menu | 菜单 | RimWorld uses 菜单 (打开「研究」菜单). Reuse it |
| tree view | 树形视图 / 树状视图 | NVDA-zh announces tree controls as 树 / 树视图. Use 树形视图 |
| node (tree node) | 节点 | Standard; NVDA-zh uses 节点 / 项 |
| expand | 展开 | NVDA-zh announces 展开 for expandable controls |
| collapse | 折叠 | NVDA-zh announces 折叠 (state: 已折叠) |
| expanded (state) | 已展开 | Past/stative form matches NVDA-zh "已展开". For our bare-state strings, 展开 / 折叠 also acceptable — pick one and stay consistent |
| collapsed (state) | 已折叠 | matches NVDA-zh "已折叠" |
| level (tree depth) | 层级 | NVDA-zh announces tree depth as 层级 (e.g. "层级 2"). Prefer 层级 over 级别 |
| typeahead search | 输入搜索 / 即输即搜 | No fixed zh convention. Use 输入搜索 (concise, "type to search"). For the act of searching, 搜索 |
| position ("X of Y") | 第 X 项，共 Y 项 | Standard zh list-position phrasing; NVDA-zh uses 第…项，共…项. **See Section 3 placeholder note** |
| announce / announcement | 播报 | 播报 is the established screen-reader verb in zh (NVDA/讲述人 "播报"). Prefer over 朗读/读出 |
| jump to | 跳转到 | Standard zh UI ("跳转"). RimWorld has no equivalent; 跳转到 is natural |
| scanner | 扫描器 | Mod-specific feature. 扫描器 is the literal, unambiguous rendering |
| edge / boundary ("already at top") | 边缘 / 顶部 / 底部 | For "already at top/bottom" use 已到顶部 / 已到底部; generic boundary = 边缘 |
| stepper (numeric +/- control) | 数值调节器 / 步进器 | No game term. 步进器 is the literal control name; 数值调节器 is clearer for users. Prefer 步进器 if space-neutral, else 数值调节 |
| inspect / inspection | 查看 / 检查 | 查看 reads more naturally for "view details". Use 查看 for the action, 检查 only if distinguishing from "view" |
| tab (UI tab) | 选项卡 | RimWorld uses 选项卡 (「需求」选项卡, `Core/Keyed/Alerts.xml`). MUST use 选项卡, not 标签页 |
| hotkey | 快捷键 | Standard zh UI. RimWorld key-binding UI uses 快捷键 |
| toggle (verb/control) | 切换 | Standard zh UI verb |

---

## Section 3 — Style rules for translators

### 3.1 Punctuation — half-width vs full-width (IMPORTANT, evidence-based)

RimWorld's own zh-Hans corpus was measured (Core/Keyed, all files):

- Colon: **half-width `:` 5343× vs full-width `：` 199×.** The game writes label-value pairs as
  `储存的食物数量: {0}`, `报酬: {1}` — half-width colon **followed by a space**.
- Comma: full-width `，` (697×) is used for in-sentence pauses (`由于温度危险，…准备离开`); the
  half-width `,` count (617×) is mostly inside placeholders/numbers/English, not sentence pauses.

**Recommendation — follow the game exactly:**

1. **Label-value separator: use half-width colon + space `: `** (e.g. `兴趣度: 狂热`). This matches
   RimWorld and reads cleanly in TTS. Our mod's English already uses `{0}: {1}` — keep the colon
   half-width in zh.
2. **In-sentence pause inside a single translated clause: use full-width `，`** (e.g. `无法在此种植，请选择其他地块`).
3. **Sentence terminator: use full-width `。`** for full sentences (descriptions, tooltips).
4. **Fragment separators that the MOD composes from multiple strings** (e.g. English `"{0}. {1}"`,
   `"{0}, {1}, {2}"`): these are screen-reader segment joiners, not prose. Keep them **half-width
   with a trailing space** (`. ` and `, `). Reason: TTS engines and screen readers reliably treat
   `. ` / `, ` as pause/segment boundaries; a bare full-width `。`/`，` with no space can run
   segments together or be read as a literal character by some zh TTS voices. The space also
   safely separates any adjacent Latin/number placeholder. **Do not "upgrade" these joiner
   periods/commas to full-width.**

> Quick rule: prose *inside one phrase* → full-width 。，：；！？. Glue *between composed
> fragments* and *label: value* colons → half-width `. ` `, ` `: `.

### 3.2 Spacing

- **No spaces between Chinese characters.** Never insert a space to separate two Chinese words.
- **Keep a single space where a placeholder, number, or Latin/abbreviation abuts Chinese** only if
  readability needs it (e.g. `{0} 格`, `NVDA 已启动`, `第 {0} 项`). When the placeholder is itself a
  translated Chinese label, no space is needed (`优先{0}` like the game).
- Follow the game's own habit: `优先{0}` (no space), but `{0}: {1}` (space after colon).

### 3.3 Placeholders and line breaks

- Copy `{0}`, `{1}`, `{NamedArg}`, `{PAWN_label}`, etc. **byte-for-byte**. Never translate, never
  add/remove braces, never change the index.
- Chinese word order may move a placeholder within the sentence — that is fine and expected.
  `string.Format` is positional, so `{2}年{1}第{0}天` (game's `FullDate`, reordered from English
  `{0} of {1}, {2}`) works correctly.
- Copy `\n` line breaks exactly and keep them in the same logical spots.
- Do not translate placeholder content or invent new placeholders.

### 3.4 XML keys

- Keep every element name (the XML key) **byte-for-byte identical to English.** Only the inner text
  is translated. A renamed key silently fails to load.

### 3.5 Tone / register

- Match RimWorld's neutral, terse UI register. Imperatives for commands (取消, 拆除, 优先), nouns for
  states. No exclamation unless the English/game has it. No first person. No emoji.
- When a tooltip is a full sentence in English, translate as a full sentence with `。`. When it is a
  short label, keep it short.

### 3.6 Plurals (One / Many variant keys)

Chinese has **no plural inflection.** Our mod has paired keys like `…CountOne` / `…CountMany`,
`MapClosed.One` / `…Many`, `ExpansionSuffixWithCountOne` / `…Many`.

**Recommended approach: give both variants identical Chinese text.** Example — English
`"Expanded {0} item"` / `"Expanded {0} items"` → both become `已展开 {0} 项`. The measure word 项
covers singular and plural, so no distinction is needed. Always fill BOTH keys (do not leave one
blank) so the mod's fallback never surfaces English.

### 3.7 Measure words (量词)

For counts like "{0} items", insert the appropriate measure word; do not omit it.

| Counted noun | 量词 | Example |
|---|---|---|
| generic item / list entry | 项 | `{0} 项` |
| colonist / person | 名 | `{0} 名殖民者` |
| building / structure | 座 / 个 | `{0} 座建筑` |
| tile (map) | 格 | `{0} 格` (matches game `TilesPerDay` 格/天) |
| match (search result) | 项 / 个 | `{0} 个匹配` |
| menu item / option | 项 | `第 {0} 项，共 {1} 项` |

When the noun is itself a placeholder ("{0} of {1} {2}") and you cannot know its measure word, use
the neutral 项 or 个, e.g. `第 {0} 项，共 {1} 项`.

---

## Section 4 — How to extend this glossary

To find how RimWorld translates a term you don't see above:

```bash
# UI strings (button labels, menu text, gameplay commands):
grep -rh '关键中文或英文键名' /tmp/zh_ref/*/Keyed/*.xml

# Concept labels (things, designators, factions, skills, needs, biomes…):
grep -rh 'EnglishKeyName' /tmp/zh_ref/*/DefInjected/**/*.xml
```

Useful sub-paths: `Keyed/Designators.xml` (order verbs), `Keyed/GameplayCommands.xml` (commands),
`Keyed/Time.xml` + `Keyed/Dates.xml` (calendar), `DefInjected/SkillDef`, `DefInjected/WorkTypeDef`,
`DefInjected/NeedDef`, `DefInjected/BiomeDef`, `DefInjected/WeatherDef`, `DefInjected/FactionDef`.
Always cite the file you took a term from when you add a row.
