# European Portuguese (Português Europeu) Terminology & Style Glossary — RimWorld Access

This glossary steers the European Portuguese (pt-PT) translation wave. It is a **delta document**:
it inherits everything `src/docs/pt-br-glossary.md` establishes about mod-coined vocabulary,
style-rule scaffolding, and the general workflow, and states here ONLY what pt-PT does
differently from pt-BR, plus the facts unique to pt-PT's corpus. Read `pt-br-glossary.md` first if
you have not translated a Portuguese-dialect file before; this document assumes you have it open
as your working reference, since **pt-BR is your line-by-line source for this wave, not English**.

This is a **fresh translation with a different register**, not a find-and-replace pass over
Brazilian Portuguese. Section 1 below is the single most important thing in this document — read
it before touching any string.

Reference corpora, both parsed with `xml.etree.ElementTree` (never raw grep of the XML — every
value is interleaved with an `<!-- EN: ... -->` English source comment that would double-count):
- **European Portuguese**: `~/.cache/rimworld-access/corpus/Portuguese/` — **Core module ONLY**.
  The game ships no `Portuguese.tar` for Royalty, Ideology, Biotech, Anomaly, or Odyssey, so an
  EU-PT player hears **English** for every DLC-only vanilla string. This shapes Section 6.
- **Brazilian Portuguese**: `~/.cache/rimworld-access/corpus/PortugueseBrazilian/` — all six
  modules, complete. This is your primary reference for meaning and structure.

**Read this before you trust the EU-PT corpus for anything**: it is itself incomplete, even inside
Core. `Core/Keyed` has 1,919 parsed entries across 31 files versus Brazilian's 4,877 across 28
files — barely 39%. `Core/DefInjected` has 4,835 entries versus Brazilian's 11,493 — about 42%.
Some files are drastically truncated (`Keyed/GameplayCommands.xml` is 137 lines in EU-PT versus 767
in Brazilian). Worse, plenty of "resolved" values are English leaks the original translators never
got to: `MainTabDef` labels `Work.label`, `Restrict.label`, `Animals.label`, `Research.label`,
`Menu.label`, `Inspect.label`, `Social.label`, `World.label` are the literal lowercase English
words; `ThingDef` `Wall.label` is `wall`; `HediffDef` `Kidney.label` is `kidney`, `Lung.label` is
`lung`; `WorkTypeDef` `pawnLabel` for Cooking/Growing/Mining/Construction/Crafting/Research are
`Cook`/`Grower`/`Miner`/`Constructor`/`Craftsman`/`Researcher`. **None of this is our problem to
fix** (we never touch vanilla strings) but it means: when you resolve a vanilla key to sanity-check
a translation and get back an English word, that is not proof the term is untranslatable in
European Portuguese — it is proof the official translation abandoned that string. Translate our
own mod strings properly regardless; do not imitate a corpus leak. Section 6 lists which
quote-law entries this affects.

> How to use this file: for MOD vocabulary (menus, keybinding names, our own dialogs), read
> `pt-br-glossary.md` Section 2 and adapt only the register (Section 1). For GAME-anchored terms
> (zones, designators, work types, body parts...), check Section 5 here first — it already
> resolves the terms most likely to differ. If a term isn't in Section 5 and matters, resolve it
> yourself against both corpora the same way Section 5 was built, and add it.

---

## Section 0 — Folder name

Ship in `Languages/Portuguese/` — the ASCII prefix Core's own tar uses (`Portuguese.tar`,
`LegacyFolderName` = `Portuguese`), sibling to `Languages/PortugueseBrazilian/`. Same reasoning as
the Brazilian folder (see `pt-br-glossary.md` §0): ASCII-only survives NFC/NFD zip round-trips,
`LanguageDatabase.InitLanguageMetadataFrom` matches on it, no `LanguageInfo.xml` needed. Mirror
`Languages/English/Keyed/` exactly — we ship only `Keyed/`.

`Portuguese` and `PortugueseBrazilian` share `LanguageWorker_Portuguese` (decompiled, verified: it
overrides only `WithIndefiniteArticle`/`WithDefiniteArticle`, not `TotalNumCaseCount`) but are
**separate language entries with no fallback between them** — an EU-PT player who hits a key we
haven't translated falls back to English, never to our Brazilian string.

---

## Section 1 — Register verdict (READ FIRST)

**RULING: use informal singular** *tu* **throughout — second person singular verb forms ending in
`-s` (podes, tens, queres), tu-imperatives (Clica, Seleciona, Pressiona), and *teu/tua/teus/tuas*
for possessives.** Never *você*, never *vosso/vossa* (that one plural sighting is a scripted
address to the whole starting group, not the standing register). This is the opposite register
from Brazilian, which uses *você* throughout — do not carry Brazilian's pronoun choices over even
though you are translating from the Brazilian file.

Measured directly from `Portuguese/Core/Keyed/*.xml` (1,919 entries), contrasted with
`PortugueseBrazilian/Core/Keyed/*.xml`:

| Evidence | EU-PT | BR |
|---|---|---|
| `MainTabs.xml` `ClickToJumpTo` | **"Clica para ires para:"** | "Clique para ir para:" |
| `Alerts.xml` `ClickToJumpToProblem` | **"Clica para ir ao problema"** | "Clique para ir ao problema" |
| `Menu_KeyBindings.xml` `PressAnyKeyOrEsc` | **"Pressiona qualquer tecla ou Esc para cancelar..."** | (uses "Pressione") |
| `Menu_Options.xml` `ChangeStoryteller` | **"Escolhe o narrador"** | (uses "Escolha") |
| `ConceptDefs.xml` `ConceptText_EquippingWeapons` | **"Para que o **teu** colono equipe uma arma, **selecciona-o** e **clica**..."** | uses "seu"/"selecione"/"clique" |
| token counts, whole corpus | *tu*-family (teu/tua/teus/tuas): **40** hits; *você*: **0** hits | *você*: ~240 hits (lead's count, independently plausible against this corpus's 4,877-entry size) |

Second-person singular present-indicative forms confirm it outside imperatives too: "Se
**prenderes** alguém" (ConceptText_ArrestingCreatesEnemies), "Tu **podes** capturá-lo!"
(ConceptText_Capture1), "**Precisas** de definir uma ÁREA RESIDENCIAL" (NeedHomeAreaDesc).

**Imperative form**: bare tu-imperative, not an infinitive and not a "Please + verb" construction.
`-ar` verbs end in **-a** (Clica, Seleciona, Pressiona, Escolhe is the one `-er` exception
following the `-er` pattern **-e**). Designator/gizmo BUTTON labels stay infinitive exactly like
Brazilian (Cancelar, Minar, Colher) — the register split is about instructional sentences
("Click to...", "Press X to...", "Select a..."), not button captions. Confirmed against EU-PT's
own designator labels: `Designators.xml` `DesignatorMine` = "Minar" (infinitive), while the
concept-doc prose instructing the player to use it says "clica" (tu-imperative).

**Orthography**: the EU-PT corpus is written in **pre-1990-Acordo-Ortográfico spelling**
("selecciona" 19 hits vs. reformed "seleciona" 2; "directo" 4 vs. "direto" 0; "contacto" 2 vs.
"contato" 0). This is Portugal's traditional spelling and is what the shipped translation
overwhelmingly uses. **Follow it**: double consonants before another consonant that are silent in
speech but etymological (cc, ct) — "selecciona", "objectivo", "acção", "directo", "contacto",
"aspecto" — rather than the reformed "seleciona", "objetivo", "ação", "direto", "contato",
"aspeto". Brazilian Portuguese has used the reformed spelling since long before the AO90 accord and
never had these doubled consonants at all, so there is no equivalent decision to make when working
from the pt-BR file — this is purely a pt-PT-side choice, made from the pt-PT evidence above.

**Second-person possessive**: *teu/tua/teus/tuas* (40 hits) is the default. *seu/sua* also occurs
(25 hits) but only where it refers to a THIRD party ("a **sua** facção tornar-se-á tua inimiga" —
"their faction... your enemy", within the same sentence) or where the referent is ambiguous by
design — never as a second-person possessive competing with *teu*. When translating "your
colonists", "your storage", etc. addressed to the player, use *teu/tua/teus/tuas*.

---

## Section 2 — Worked examples

Fifteen representative pt-BR → pt-PT renderings, picked for variety (imperative hint, stat list,
confirmation, docs prose, plain label). Source: `Languages/PortugueseBrazilian/Keyed/`, cited by
file and key. These are worked TRANSLATIONS for this wave, not corpus quotes — the EU-PT corpus has
no equivalent mod-specific strings to cite (it is vanilla-only).

| # | Type | Key | pt-BR (reference) | pt-PT (this wave) |
|---|---|---|---|---|
| 1 | Imperative hint | `RimWorldAccess.Abilities.Item.DefaultInstruction` (`Abilities.xml`) | "Selecione um alvo" | "Selecciona um alvo" |
| 2 | Imperative + keys | `RimWorldAccess.Abilities.Start.HintSingle` (`Abilities.xml`) | ". Pressione R para verificar distância e linha de visão." | ". Pressiona R para verificares a distância e a linha de visão." |
| 3 | Multi-step instruction | `RimWorldAccess.Building.Shape.Desc.Manual` (`Building.xml`) | "Coloca uma célula por vez. Pressione Espaço para colocar, depois mova e coloque novamente." | "Coloca uma célula de cada vez. Pressiona Espaço para colocar, depois move-te e coloca outra vez." |
| 4 | Stat list (period-chopped) | `RimWorldAccess.Compat.CashRegister.RadiusStatus` (`Compat.xml`) | "Raio: {0}. Incluir cômodo: {1}." | "Raio: {0}. Incluir cómodo: {1}." |
| 5 | Stat list, 3-part | `RimWorldAccess.Compat.Rwom.LevelRow` (`Compat.xml`) | "Nível: {0}. {1} de {2} de experiência para o próximo nível." | "Nível: {0}. {1} de {2} de experiência para o próximo nível." *(identical — no dialect-sensitive words)* |
| 6 | Confirmation, imperative | `RimWorldAccess.Building.Place.WillReplaceConfirm` (`Building.xml`) | "Vai substituir {0}. Pressione Espaço novamente para colocar" | "Vai substituir {0}. Pressiona Espaço outra vez para colocar" |
| 7 | Confirmation, 2nd person + hotkey | `RimWorldAccess.Archonexus.Reform.CannotCancel` (`Archonexus.xml`) | "Você não pode cancelar a reforma da ideologia. Pressione Alt S para confirmar e continuar." | "Não podes cancelar a reforma da ideologia. Pressiona Alt S para confirmares e continuares." |
| 8 | Docs / natural prose | `RimWorldAccess.Learning.HelperHint` (`Learning.xml`) | "Pressione Shift Barra, o ponto de interrogação, para ler esta e outras lições." | "Pressiona Shift Barra, o ponto de interrogação, para leres esta e outras lições." |
| 9 | Plain label | `RimWorldAccess.Inspection.Category.WorkPriorities` (`Inspection.xml`) | "Prioridades de trabalho" | "Prioridades de trabalho" *(identical)* |
| 10 | Plain label, tab | `RimWorldAccess.Building.Shelf.StorageSettingsLabel` (`Building.xml`) | "Configurações de armazenamento" | "Definições de armazenamento" |
| 11 | Terse status line | `RimWorldAccess.Inspection.Pawn.MoodHeader` (`Inspection.xml`) | "Humor: {0}" | "Humor: {0}" *(identical)* |
| 12 | Error / fallback message | `RimWorldAccess.Inspection.Category.NotFound` (`Inspection.xml`) | "Categoria não encontrada." | "Categoria não encontrada." *(identical)* |
| 13 | Selection instruction, plural target | `RimWorldAccess.Building.Shelf.SelectAtLeastOneOther` (`Building.xml`) | "Selecione pelo menos outro armazenamento para ligar. Use as setas até o armazenamento, Espaço para selecionar." | "Selecciona pelo menos outro armazenamento para ligar. Usa as setas até ao armazenamento, Espaço para seleccionar." |
| 14 | Menu confirmation, category | `RimWorldAccess.Building.Architect.CategorySelected` (`Building.xml`) | "Categoria selecionada: {0}. Escolha uma ferramenta" | "Categoria seleccionada: {0}. Escolhe uma ferramenta" |
| 15 | Cancel/exit hint | `RimWorldAccess.Building.Shelf.LinkingCancelledStillSelecting` (`Building.xml`) | "Ligação cancelada. Ainda no modo de seleção. Pressione Esc para sair." | "Ligação cancelada. Ainda em modo de selecção. Pressiona Esc para saíres." |

Notes on the pattern: BR "Pressione X para Y" (formal imperative + infinitive complement) becomes
PT "Pressiona X para Y-eres" wherever the original infinitive clause has an understood "you" as its
subject — European Portuguese prefers the **inflected personal infinitive** (leres, confirmares,
continuares, seleccionares) over the bare infinitive in exactly this construction, and the EU-PT
corpus confirms it: "Para **salvares** um colono ferido, **selecciona**..."
(`ConceptText_RescueColonist`), "Para que o teu colono **equipe** uma arma, **selecciona-o**..."
(here the infinitive stays bare because it follows "para que" + subjunctive, a different
construction — check which pattern your sentence actually uses, don't apply personal-infinitive
mechanically). Rows 5, 9, 11, 12 are identical between dialects because they contain no pronoun,
possessive, or imperative — most of your file will look like this; the register only bites on the
subset of strings that address the player directly.

---

## Section 3 — Plural policy: `_numCase` is FORBIDDEN

Zero `_numCase` tags in the entire EU-PT Core corpus (grepped `Portuguese/Core/` recursively: 0
hits). This matches Brazilian and matches the shared `LanguageWorker_Portuguese`: the class
(decompiled, `decompiled/Verse/LanguageWorker_Portuguese.cs`) overrides only
`WithIndefiniteArticle`/`WithDefiniteArticle` — it does not override `TotalNumCaseCount`, so the
base `LanguageWorker.TotalNumCaseCount` returns 0 and any `_numCase` tag resolves to **empty
speech**. Never use `_numCase` in either Portuguese dialect's files.

---

## Section 4 — Typography policy (measured from EU-PT Core, 6,754 values)

| Character | Count | Verdict |
|---|---|---|
| ASCII double quote `"` | 2 (both inside English-leak strings, not real Portuguese usage) | **Do not use for quoting.** |
| ASCII apostrophe `'` | 242, but genuine Portuguese usage (not English-leak `'s`/`Lovin'`) is only the handful quoting a UI term: `'Prisioneiro'`, `'transportáveis'`, `'unidade'` | **This is the quote pair.** Unlike Brazilian (which pairs `"..."` and treats the apostrophe as unpaired elision), EU-PT has **zero elision** in this corpus (checked: no `d'água`-style pattern anywhere) so the apostrophe is unambiguous here — safe as a genuine open/close pair. |
| Curly quotes `‘’“”` | 1 stray `’` inside an English-leak string (`GotSomeLovin.stages.0.label`) | Zero genuine use. Forbidden. |
| Guillemets `«»` | 0 | Forbidden. |
| Ellipsis char `…` | 0 | Forbidden — never the single ellipsis codepoint. |
| Literal `...` (three ASCII periods) | 47 | **This is how EU-PT writes ellipsis** — "Gerir áreas...", "A carregar...", "Zzztt...". Use three ASCII periods, exactly like the corpus. |
| NBSP / NNBSP / thin space | 0 | Forbidden (matches the universal rule). |
| Zero-width space | 4, all inside an obvious copy-paste artifact (two adjacent zero-width spaces, in two DefInjected descriptions: `Tribal.description` and `Stonecutting.description`) | This is corpus noise from the original translators, not policy — **forbidden in our own strings** same as every other language. |
| Em/en dash | 0 | Forbidden. |

**Policy for `scripts/l10n/languages.json`**: `quote_pairs: [["'", "'"]]`, `punctuation_allowed:
["..."]` (the note field, not the character set — this is a 3-character literal, not a single
punctuation codepoint like the other languages' `"…"` entries; if the checker tooling assumes
single-character entries, flag this to the lead rather than silently coercing it to `"…"`).
`numcase_allowed: false`. One-line note: *"European Portuguese. Register is informal tu (see
src/docs/pt-pt-glossary.md), pre-AO90 orthography (selecciona, directo, contacto). Quotes with
ASCII apostrophes, not double quotes — the reverse of Brazilian. Ellipsis is three literal periods,
never the single-character ellipsis. _numCase is FORBIDDEN, same reason as Brazilian
(LanguageWorker_Portuguese doesn't override TotalNumCaseCount). Core-only corpus: DLC content has
no EU-PT reference and falls back to English in the live game."*

---

## Section 5 — Vocabulary delta (EU-PT vs. Brazilian)

### 5a. Game-anchored terms (resolved against both corpora)

| English | EU-PT | Brazilian | Source (EU-PT / BR) |
|---|---|---|---|
| stockpile zone | **zona de stock** | zona de estoque | `Core/Keyed/Gameplay.xml` `Stockpile` / `Core/Keyed/Misc_Gameplay.xml` `Stockpile` |
| storage (tab) | **Armazenamento** | Estoque | `Core/Keyed/Gameplay.xml` `TabStorage` / `Core/Keyed/ITabs.xml` `TabStorage` |
| storage priority: Preferred | **Alta** | preferida | `Core/Keyed/Gameplay.xml` `StoragePriorityPreferred` / `Core/Keyed/Enums.xml` |
| storage priority: Very cheap (price) | super barato | muito barato | `Core/Keyed/Enums.xml` `PriceTypeVeryCheap` (both files) |
| growing zone | zona de cultivo | zona de cultivo | **identical** — `Gameplay.xml` / `Misc_Gameplay.xml` `GrowingZone` |
| mine (designator) | **Minar** | Minerar | `Core/Keyed/Designators.xml` `DesignatorMine` |
| expand home area | **Expandir área residencial** | Expandir área da casa | `Core/Keyed/Designators.xml` `DesignatorAreaHomeExpand` |
| expand allowed area | Expandir área permitida | Expandir área permitida | **identical** — `Designators.xml` `DesignatorExpandAreaAllowed` |
| go here (float menu) | **Andar até aqui** | Ir aqui | `Core/Keyed/FloatMenu.xml` `GoHere` |
| add bill | **Adicionar tarefa** | Adicionar Tarefa | `Core/Keyed/Gameplay.xml` / `ITabs.xml` `AddBill` (BR capitalizes the noun; PT doesn't) |
| jump to location | Ir para o local | Ir para o local | **identical** — `Letters.xml` `JumpToLocation` |
| colony | **Colónia** (closed *ó*) | Colônia (closed *ô*) | `Core/Keyed/Misc.xml` `Colony` — same word, different accent per each dialect's own vowel system; always match the accent to the dialect |
| trade (verb) | **comercializar** | negociar | `Core/Keyed/FloatMenu.xml` `CannotTrade`/`TradeWith` vs. same keys in BR |
| trader | comerciante | comerciante | **identical** — both corpora, both `Incidents.xml` and `Dialogs_Various.xml` |
| silver (currency) | prata | prata | **identical** |
| tab (UI) | aba | aba | **identical** — `ConceptText_StorageTabCategories` "Na aba de armazenamento..." in BOTH corpora |
| work (main tab) | trabalho | Trabalho | **identical word**, EU-PT's own `MainTabDef` label is an English leak (`work`) so cite the Keyed occurrence `Menus_Overview.xml` `WorkTab` = "Trabalho", not the leaked DefInjected label |
| torso | Tronco | tronco | `Core/DefInjected/BodyPartDef/BodyParts_General.xml` `Torso.label` (both) |
| skull | Crânio | crânio | `BodyParts_General.xml` `Skull.label` (both) |
| sternum | Esterno | esterno | `BodyParts_General.xml` `Sternum.label` (both) |
| pelvis | **Bacia** | pelve | `BodyParts_General.xml` `Pelvis.label` |
| kidney | *(EN leak: "kidney")* — translate as **rim** | rim | `BodyParts_Organs.xml` `Kidney.label`; PT's shipped value is untranslated — use Brazilian's correct term, do not clone the leak |
| lung | *(EN leak: "lung")* — translate as **pulmão** | pulmão | `BodyParts_Organs.xml` `Lung.label`; same leak situation |
| shoulder | *(no EU-PT entry)* — use **ombro** | ombro | `BodyParts_General.xml`; missing from EU-PT entirely, BR value is standard in both dialects — use it |
| left/right leg, arm, eye, ear, hand, foot, clavicle | Perna/Braço/Olho/Orelha/Mão/Pé/Clavícula + esquerda/direita | *(mostly absent from BR's DefInjected — BR resolves generic `Leg.label`="perna" etc. instead of per-side)* | `BodyPartDef/BodyParts_Humanoid.xml` (PT ships per-side labels BR doesn't use); when translating our own body-part strings, follow English's structure (per-side or generic) rather than either corpus's def layout |
| work type: firefighting (short) | **Combater incêndios** | incêndios | `WorkTypeDef/BaseWorkTypes.xml` `Firefighter.labelShort` |
| work type: doctor (short) | **Medicar** | medicina | `WorkTypeDef/WorkTypes.xml` `Doctor.labelShort` |
| work type: doctor (pawn label) | Médico | Médico | **identical** |
| work type: warden (short) | **Proteger** | policiam. | `Warden.labelShort` |
| work type: handling (short/pawn) | **Coletar** / Coletor | domesticação / Domador | `Handling.labelShort`/`.pawnLabel` |
| work type: cooking (pawn) | *(EN leak: "Cook")* — translate as **Cozinheiro** | Cozinheiro | `WorkTypes.xml` `Cooking.pawnLabel`; PT's own value is untranslated |
| work type: growing (pawn) | *(EN leak: "Grower")* — translate as **Agricultor** or **Cultivador** | Agricultor | `Growing.pawnLabel`; leak — Brazilian's alternate file (`BaseWorkTypes.xml`) uses "Cultivador", pick one and stay consistent |
| work type: mining (pawn) | *(EN leak: "Miner")* — translate as **Mineiro** | Mineiro | `Mining.pawnLabel`; leak |
| work type: construction (pawn) | *(EN leak: "Constructor")* — translate as **Construtor** | Construtor | `Construction.pawnLabel`; leak |
| work type: crafting (pawn) | *(EN leak: "Craftsman")* — translate as **Artesão** | Artesão | `Crafting.pawnLabel`; leak |
| work type: research (pawn) | *(EN leak: "Researcher")* — translate as **Investigador** or **Pesquisador** | Pesquisador | `Research.pawnLabel`; leak — "Investigador" is the more standard EU-PT term for a research role if you want a dialect-flavored pick, but either reads fine |

### 5b. General software vocabulary (game corpus where it speaks, standard EU-PT convention where silent)

| English | EU-PT | Brazilian | Basis |
|---|---|---|---|
| screen | **ecrã** | tela | corpus: 15 EU-PT hits, 0 "tela"; 38 BR "tela" hits, 0 "ecrã" |
| mouse | **rato** | mouse | corpus: 17 EU-PT "rato", 0 "mouse"; BR corpus itself is mixed (21 "mouse", 9 "rato") but "mouse" dominates |
| file | **ficheiro** | arquivo | corpus: `Dialogs_Various.xml` `ProblemSavingFile` = "Ocorreu um problema a **guardar** o **ficheiro** {0}" (clean, unambiguous); BR uses "arquivo" 6x, 0 "ficheiro" |
| folder | **pasta** | pasta | **identical** — both corpora use "pasta"; do not invent "directório"/"diretório" (0 hits in either) |
| user | **utilizador** | usuário | corpus: 3 EU-PT "utilizador", 0 "usuário"; 40 BR "usuário", 0 "utilizador" |
| save (verb) | **guardar** | salvar | corpus: 15 EU-PT "guardar" vs. 1 "salvar"; 21 BR "salvar" vs. 2 "guardar" |
| delete / remove | **apagar** / **remover**, contextually — same split as Brazilian, not a dialect difference | apagar / remover | corpus: both dialects mix "apagar" (14 PT / 10 BR) and "remover" (14 PT / 42 BR) for delete-like actions in this specific game; there is no clean "eliminar (PT) / excluir (BR)" split in the game's own vocabulary — "eliminar" and "excluir" both have **zero** hits in either corpus. Use "apagar" or "remover" to match what the analogous vanilla string does; if you need a distinct word for our own UI outside any game-anchored context, "eliminar" is the standard European convention (**convention**, not corpus-attested) |
| settings | **definições** | configurações | corpus: 6 EU-PT "definições" vs. 0 "configurações"; 34 BR "configurações" vs. 3 "definições" |
| shortcut (keybinding) | atalho | atalho | **identical** — both corpora use "atalho" (2 PT, 14 BR; low PT count reflects corpus size, not a different word) |
| keyboard | teclado | teclado | **identical** |
| click (verb, infinitive/gizmo) | **clicar** | mixed, but "clique" as a noun/short-imperative dominates | corpus: PT favors the infinitive "clicar" (3 hits) over the noun/imperative "clique" (1 hit, itself borderline BR-influenced); BR is the reverse (62 "clique" vs. 10 "clicar"). In EU-PT instructional sentences, prefer the *tu*-imperative "Clica" (Section 1) over either noun form. |
| link / hyperlink | *(no corpus hits either side)* — use **hiperligação** if a distinct link concept ever appears; **link** is an accepted loanword in casual EU-PT tech writing if brevity matters | link | **convention** — zero corpus evidence in this game either way |

**Identical in both dialects — do not churn**: colonist→colono, faction→facção, biome→bioma,
terrain→terreno, priority→prioridade, home area→casa/área residencial (the noun "casa" itself is
identical, only the designator PHRASE differs, see 5a), pasta (folder), atalho (shortcut), teclado
(keyboard), trader→comerciante, silver→prata, tab→aba, torso, skull, sternum, growing zone, allowed
area, jump-to-location, storyteller→narrador, mod (loanword), goodwill→boa vontade.

---

## Section 6 — Quote-law resolution (22 manifest entries)

Resolved `scripts/l10n/quote_manifest.json` against `Portuguese/Core/` (Core-only, since that is
all that ships). Two distinct kinds of gap below — **read the "gap kind" column before treating
anything as DLC-only**:

| id | strategy → key | Resolved EU-PT value | Gap kind / action |
|---|---|---|---|
| `stockpiles.typeahead` | fragment → `Stockpile` | "Zona de stock" | resolved |
| `growingfood.typeahead` | fragment → `GrowingZone` | "Zona de cultivo" | resolved |
| `mining.typeahead` | fragment → `DesignatorMine` | "Minar" | resolved |
| `mining.minevein` | label → `DesignatorMineVein` | **none** | **Core gap** — key absent from EU-PT's `Designators.xml` entirely (not DLC; the whole gizmo label is simply untranslated in Core). Quote the ENGLISH label "Mine vein" — that's what an EU-PT player's game actually shows on this gizmo. |
| `storagetab.settings` | self → `RimWorldAccess.Building.Shelf.StorageSettingsLabel` | **n/a** | **our own key, not yet created** — `Languages/Portuguese/` doesn't exist yet. Resolves once this wave writes the file; translate it as "Definições de armazenamento" (see Section 2 row 10) and the quote in our own doc text must contain that literal string. |
| `homearea.typeahead` | fragment → `DesignatorAreaHomeExpand` | "Expandir área residencial" | resolved |
| `homearea.label` | label → `DesignatorAreaHomeExpand` | "Expandir área residencial" | resolved |
| `allowedareas.typeahead` | fragment → `DesignatorExpandAreaAllowed` | "Expandir área permitida" | resolved |
| `medops.addbill` | label → `AddBill` | "Adicionar tarefa" | resolved |
| `messages.jumploc` | label → `JumpToLocation` | "Ir para o local" | resolved |
| `formation.gohere` | label → `GoHere` | "Andar até aqui" | resolved |
| `formation.suffix` | self_suffix → `RimWorldAccess.Input.Order.FormationOption` | **n/a** | **our own key, not yet created** — same as `storagetab.settings`. Translate as "{0} (formação)" (mirrors Brazilian's structure exactly — "formação" is identical in both dialects). |
| `plantgizmo.prefix` | prefix → `CommandSelectPlantToGrow` | **none** | **Core gap** — `Keyed/GameplayCommands.xml` is truncated to 137 lines in EU-PT (767 in Brazilian) and this key isn't among them. Quote the ENGLISH literal prefix "Plant:" — the live EU-PT game shows the English gizmo here. |
| `plantgizmo.example` | parts → `CommandSelectPlantToGrow` + `Plant_Rice.label` | **none / none** | **Core gap**, same cause as above — neither part resolves. Quote the ENGLISH composed example "Plant: rice". |
| `psyfocus.anything` | label → `Anything.label` (definjected) | "anything" *(EN leak)* | **resolved, but it's an English leak** — the key exists in `TimeAssignmentDef/TimeAssignments.xml` but was never translated. Quote the leaked value "anything" verbatim (that IS what the live EU-PT game shows), do not "fix" it to a Portuguese word the game itself doesn't display. |
| `pollution.typeahead` | fragment → `DesignatorAreaPollutionClearExpand` | **none** | **true DLC gap** — Biotech-only key, EU-PT ships no Biotech tar at all. Quote the ENGLISH fragment "pollution". |
| `xenotype.composed` | parts → `Xenotype` + `Baseliner.label` | **none / none** | **true DLC gap** — Biotech-only. Quote the ENGLISH composed example "Xenotype: Baseliner". |
| `fishing.typeahead` | fragment → `Zone_Fishing` | **none** | **true DLC gap** — Odyssey-only. Quote the ENGLISH fragment "fishing". |
| `buildingbasics.wall` | fragment → `Wall.label` (definjected) | "wall" *(EN leak)* | **resolved, but it's an English leak** — `ThingDef/Buildings_Structure.xml` has `Wall.label` as the literal English "wall" in EU-PT (Brazilian correctly has "parede"). Quote the leaked value "wall" verbatim — that's what the live game shows. |
| `buildingbasics.stockpile` | fragment → `Stockpile` | "Zona de stock" | resolved |
| `inspect.leg` | fragment → `Leg.label` (definjected) | **none** | **Core gap** — no bare `Leg.label` tag exists in EU-PT's `BodyPartDef` files at all (checked all five: `BodyParts.xml`, `_General`, `_Humanoid`, `_Animal`, `_Mechanoid`, `_Organs`). Quote the ENGLISH fragment "leg". |
| `inspect.inf` | fragment → `WoundInfection.label` (definjected) | "infecção" | resolved |

**`quoteLawGaps` (no EU-PT corpus resolution at all, quote ENGLISH per the rule above)**:
`mining.minevein`, `plantgizmo.prefix`, `plantgizmo.example`, `pollution.typeahead`,
`xenotype.composed`, `fishing.typeahead`, `inspect.leg`. Of these, only `pollution.typeahead`,
`xenotype.composed`, and `fishing.typeahead` are DLC-gated in the strict sense (Biotech/Odyssey
have no EU-PT tar at all); `mining.minevein`, `plantgizmo.prefix`, `plantgizmo.example`, and
`inspect.leg` are Core-module keys the shipped EU-PT translation simply never covered — same
practical outcome (quote English), different root cause, worth keeping straight if anyone asks
why a Core key is "DLC-gated." `storagetab.settings` and `formation.suffix` are a third, unrelated
category — not corpus gaps at all, just our own not-yet-written keys — and resolve normally once
this wave creates `Languages/Portuguese/`.

Two entries resolve to **English-leak values that must be quoted as-is**: `psyfocus.anything`
("anything") and `buildingbasics.wall` ("wall"). These are correct citations of the live game, not
translation bugs to fix — the EU-PT corpus really does show English there.

## Mod-coined terms — watermill placement + read-only browsing (2026-08-13)

Thirteen new keys (`RimWorldAccess.Building.Place.Spot*`, `.Watermill*`, `RimWorldAccess.TextInput.BrowsingField/ReadOnlyField`)
had no reference translation to check: this local install only carries the English Core language pack,
so RimWorld's own EU-PT wording for the `WatermillGenerator` ThingDef could not be verified against the
game's corpus, and pt-BR (this wave's usual line-by-line source) needed its own fresh decision here too.
Decided from context per house rule (no native-review parking); record here so later waves stay
consistent. Delta from pt-BR: EU-PT keeps the unelided "de água" (pt-BR's glossary treats "roda d'água"
as a fixed idiom that this wave does not extend to "moinho"), and uses "partilhada"/formal-adjacent
"apenas" instead of BR's "compartilhada"/"somente".

| English | Português (EU) | Basis |
|---|---|---|
| watermill (whole building) | moinho de água | Standard EU-PT term for a water mill |
| waterwheel (the wheel part) | roda de água | EU-PT keeps "de água" unelided (delta from pt-BR's "roda d'água") |
| moving/running water | água corrente | Standard idiom for flowing water |
| water flow area | área de fluxo de água | Coined; parallels `Área necessária` (`OutlineOkAt`) |
| unshared / shared with another watermill | não partilhada / partilhada com outro moinho de água | Coined; "partilhar" is the EU-PT share verb (delta from pt-BR "compartilhar") |
| placement spot (scanner category/item) | Locais de colocação de {0} / Local | Mirrors `Abilities.Plant.ScannerCategory`/`ScannerItemLabel` ("Locais de plantação de {0}" / "Local plantável") |
| facing {0} (trailing fragment) | voltado para {0} | Mirrors `Building.ArchitectPlace.GravshipFacing` ("Gravinave voltada para {0}") |
| read only (BrowsingField) | apenas leitura | Reuses `Shell.State.ReadOnly`'s established EU-PT term (not Biotech's stray BR-ism "somente leitura") |

## Rulings from the legacy BR-leak sweep (2026-11-09)

Mod strings that had slipped through in Brazilian wording or untranslated English now follow these EU-PT terms; keep them in later waves.

| English | Português (EU) | Not |
|---|---|---|
| search (noun/verb) | pesquisa / pesquisar | busca, buscar |
| status | estado | status |
| delete (our own UI) | apagar (corpus: `CommandDeleteZoneLabel`) | excluir |
| room | divisão (corpus: `BuildARoom`) | cômodo |
| gender | género | gênero |
| default (setting) | predefinido / valor predefinido | padrão (keep "padrão" only for visual patterns) |
| land / landing spot | aterrar / local de aterragem | pousar, pouso |
| connected (physical link) | ligado | conectado |
| in progress | em curso | em andamento |
| download (noun) | transferência | download |
| target selection (targeting) | escolha de alvo / apontar | direcionamento / direcionar |
| staff (cash register shifts) | pessoal | equipe |
| percentage | percentagem | percentual |
| probability (hit chance) | probabilidade | chance |
| square bracket (key) | parêntese recto | colchete |
| record / log | registo | registro |
| sideburns / hair lock | patilhas / madeixa | costeletas / mecha |
| in a / in an | num / numa | em um / em uma |
| around | em redor de | ao redor de |

Pre-AO90 spellings applied across Keyed: activo, actual, acção, selecção, direcção, directo, afectar, eléctrico, objecto, protecção, correcção, óptimo, selector, leccionar, recto.

## Rulings from the StyleDescriptions pt-BR to pt-PT rewrite (2026-11-09)

Hair, beard and tattoo descriptions (`RimWorldAccess_StyleDescriptions.xml`); keep these in later waves.

| English | Português (EU) | Not |
|---|---|---|
| hair part / parting (noun) | risca ("repartido" stays fine as the participle) | divisão |
| shaved / shorn | rapado | raspado |
| scalp stubble | cabelo rapado / cabelo muito curto | barba rala (keep "barba rala" for facial stubble only) |
| handlebar (mustache) | bigode estilo guiador | guidão |
| fin (shape) | barbatana | nadadeira |
| look / appearance | aspecto (pre-AO90) | aspeto, visual |
| mop (of hair) | cabeleira | mecha, madeixa ("madeixa" is a single lock) |
| progressive participle clauses | a + infinitive ("a cair", "a emoldurar") or a relative clause; bare gerunds read Brazilian | caindo, emoldurando |
