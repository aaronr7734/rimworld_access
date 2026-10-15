# Castilian Spanish (Español Castellano) Delta Glossary — RimWorld Access

**This file INHERITS `src/docs/es-glossary.md` in full.** Every rule, every locked term, and every
style law in the Latin American Spanish glossary applies to Castilian unchanged **except** where this
file records a delta. Read `es-glossary.md` first; it is the parent document. This file is short on
purpose: it is a delta pass, not a second glossary.

Castilian Spanish is produced as a **derivation** of our existing `Languages/SpanishLatin/` files
(67 Keyed + 2 DefInjected). The files are cloned, then edited only where this document says to edit.
Anything not listed here stays byte-identical to the SpanishLatin value.

Reference corpus: RimWorld's official Castilian translation, extracted from the game's own tars
(Core + Royalty + Ideology + Biotech + Anomaly + Odyssey, 1,569 XML files). Citations below are
relative to that corpus root. Re-extract either dialect with:

```bash
D="$HOME/Library/Application Support/Steam/steamapps/common/RimWorld/Data"
# Castilian:
find "$D" -name 'Spanish (*Castellano*).tar'  # extract each into <DLC>/ subfolders
# Latin American (the parent glossary's /tmp/es_ref, which has since been emptied by temp cleanup):
find "$D" -name 'SpanishLatin*.tar'
```

> **Grep trap:** every corpus file interleaves English `<!-- EN: … -->` comment lines with the
> Spanish values. Any word-frequency grep MUST filter comment lines (`grep -v '<!--'`) or English
> words get counted as Spanish. This is how a first pass "found" 14 usted-form imperatives that were
> actually English source comments. Also anchor word searches on boundaries: an unanchored `coger`
> matches `Escoger`, and `pulsa` matches `expulsada`.

> **THE THREE MOST IMPORTANT CASTILIAN FACTS:**
> 1. **The register is IDENTICAL to SpanishLatin — informal tú.** This is not a delta. Do not
>    convert anything to usted. (§2)
> 2. **`_numCase` is forbidden here too**, for exactly the same reason: both dialects run
>    `LanguageWorker_Spanish`. (§3)
> 3. **~40 game-anchored terms differ.** The big ones by volume in our own files: map cell
>    `cuadro` → `casilla`, hair `cabello` → `pelo`, psyfocus `psicofoco` → `psifoco`. (§4)

---

## Section 1 — Folder name

Our folder is **`Languages/Spanish/`**.

Core ships the language as `Spanish (Español(Castellano)).tar`, and its `LanguageInfo.xml` declares
`friendlyNameNative` = `Español (Castellano)`, `friendlyNameEnglish` = `Spanish`. As with
`SpanishLatin`, `French`, and `Turkish`, our repo uses the **ASCII legacy prefix only** — the word
before the parenthesis. So: `Languages/Spanish/`, never `Languages/Español(Castellano)/`.

The two dialects are separate languages to RimWorld, and a player on Castilian never loads
`SpanishLatin/`. There is no fallback between them — every key must be present in `Spanish/`.

---

## Section 2 — Register and command style (VERDICT: no delta)

**Castilian vanilla addresses the player with informal tú, exactly like SpanishLatin.** This was the
one delta with the potential to touch every file, and it does not exist. Counts over all Keyed values
(comment lines excluded, word-boundary anchored):

- **tú imperatives, 138 total:** `Elige` 36, `Selecciona` 35, `Haz clic` 26, `Asegúrate` 12,
  `Escoge` 9, `Usa` 5, `Mantén` 4, `Haz` 4, `Arrastra` 4, `Presiona` 2, `Ve a` 1.
- **usted imperatives, 1 total:** a single `Use este comando…` in `CommandReplantDesc`
  (`Core/Keyed/GameplayCommands.xml`). `Haga clic`, `Pulse`, `Seleccione`, `Elija`, `Escoja`,
  `Vaya a`, `Mantenga` all return **zero**.
- **tú verb forms and possessives:** `tu` 153, `tus` 124, `quieres` 73, `puedes` 62, `tienes` 27,
  `deseas` 11, `necesitas` 8, `debes` 5, `verás` 1.
- **The pronoun `usted`/`ustedes` never appears** in the Castilian Keyed corpus.

Cited examples: `ClickToJumpToProblem` — "**Haz** clic para ir al problema"
(`Core/Keyed/Misc_Gameplay.xml`); `CreepJoinerTimeoutDescAppended` — "**Selecciona** un colono y
**haz** clic con el botón derecho del ratón sobre el visitante" (`Anomaly/Keyed/Alerts.xml`).

**Second-person plural:** Castilian uses **vosotros**, where Latin America uses ustedes. This shows
up in exactly two narrative keys — `GameStartDialog` "Los tres **os despertáis** en **vuestros**
sarcófagos" (`Core/Keyed/Misc.xml`) and `DateReadoutTip` "desde **vuestra** llegada"
(`Core/Keyed/Dates.xml`). Our mod never addresses a plural "you", so this has **no impact** on the
delta pass. If a future string ever needs plural address, use vosotros, not ustedes.

**Click-verb wording (small delta).** Both dialects say `clic` (CAST 73 / LAT 71) and prefer
`Haz clic` (CAST 45 / LAT 40). But:

- **`Cliquea` is Latin-American only** (LAT 7, CAST 0). Castilian writes `Haz clic`. Compare
  `ClickToViewInQuestsTab`: LAT "Cliquea para ver esta misión…" → CAST "**Haz clic** para ver esta
  misión…" (`Core/Keyed/Misc_Gameplay.xml`).
- Castilian sometimes clips the verb entirely: `ClickToLearnMore` LAT "Haz clic para aprender más."
  → CAST "**Clic** para saber más."; `ClickToViewFactions` LAT "Haz clic para ver las facciones." →
  CAST "**Clic** para ver las facciones." (`Core/Keyed/Dialogs_Various.xml`). This is a stylistic
  preference, not a rule — `Haz clic` is still the majority form. Prefer `Haz clic` for TTS clarity.
- **The mouse is `el ratón` in both dialects** (CAST 5 / LAT 9), never "mouse". Relevant to our
  mouse-play strings: `EdgeScreenScroll` "Desplazar pantalla con el ratón",
  `MapDragSensitivity` "Sensibilidad del ratón en el mapa" (`Core/Keyed/Menu_Options.xml`).
- `pulsar`/`presionar` behave the same in both (`presiona` 3/3, `pulsa` 7/5). Our own translation
  uses `presiona` throughout; leave it.

---

## Section 3 — Plurals: no `_numCase`, flat two forms (unchanged from parent)

**Verified for Castilian:** `grep -r '_numCase'` over all 1,569 corpus files returns **zero** hits.

**Verified in the decompiled game:** both dialects declare the same worker. `LanguageInfo.xml` inside
`Spanish (Español(Castellano)).tar` and inside `SpanishLatin (Español(Latinoamérica)).tar` both
contain `<languageWorkerClass>LanguageWorker_Spanish</languageWorkerClass>`. And
`decompiled/Verse/LanguageWorker_Spanish.cs` has **no `TotalNumCaseCount` override**, so it inherits
`LanguageWorker.TotalNumCaseCount => 0` (`decompiled/Verse/LanguageWorker.cs:13`).

**Therefore `{N_numCase ? … : …}` resolves to an EMPTY STRING for Castilian — silent blank speech.**
Never write one. Corpus proof of the plain-plural habit: `PeriodDays` is simply `{0} días` and
`ColonistsIdle` is `{0} colonos ociosos` — both byte-identical across the two dialects
(`Core/Keyed/Time.xml`, `Core/Keyed/Alerts.xml`).

**One/Many key pairs:** identical handling to the parent glossary. Fill `…One` with the singular and
`…Many` with the plural, and **always fill both** — a blank key falls back to English for that count.
Since the SpanishLatin values are already correct 2-form plurals, a delta agent normally changes
nothing here beyond vocabulary substitutions.

---

## Section 4 — Vocabulary delta table

Every row below is a term where the **Castilian corpus disagrees with our SpanishLatin rendering**.
Use the Castilian column. Terms not listed here are identical — see §4.3 before "fixing" anything.

### 4.1 Keyed deltas

| English | Our SpanishLatin | Castilian | Corpus citation |
|---|---|---|---|
| map cell / tile | cuadro | **casilla** | `SelectNextInSquareTip` "…el elemento siguiente en la misma **casilla**" (`Core/Keyed/Misc_Gameplay.xml`). Corpus-wide: casilla/casillas 58 vs cuadro 5 |
| deconstruct | Desarmar | **Deconstruir** | `DesignatorDeconstruct` (`Core/Keyed/Designators.xml`) |
| strip | Desvestir | **Desnudar** | `DesignatorStrip` (`Core/Keyed/Designators.xml`) |
| plan (designator) | Planificación | **Planificar** | `DesignatorPlan` (`Core/Keyed/Designators.xml`) |
| stockpile zone | Área del almacén | **Almacén** | `Stockpile` (`Core/Keyed/Misc_Gameplay.xml`). `StockpileGroup` = `almacén` in both |
| expand home area | Expandir área del hogar | **Expandir área de hogar** | `DesignatorAreaHomeExpand` (`Core/Keyed/Designators.xml`) — note "de hogar", no article |
| clear home area | Borrar área del hogar | **Reducir área de hogar** | `DesignatorAreaHomeClear` (same file) |
| allowed area (concept) | área asignable | **área permitida** | `AllowedArea` LAT "Permitir área" → CAST "**Área permitida**"; `ForbiddenOutsideAllowedAreaLower` "fuera del área permitida" (identical in both). `asignable` appears **0×** in the Castilian corpus |
| expand allowed area | Expandir área asignable | **Expandir área** | `DesignatorExpandAreaAllowed` (`Core/Keyed/Designators.xml`) |
| clear allowed area | Borrar área asignable | **Reducir área** | `DesignatorClearAreaAllowed` (same file) |
| no allowed area | Sin restricciones | **Sin restringir** | `NoAreaAllowed` |
| expand all (tree) | Ampliar todo | **Expandir todo** | `ExpandAllCategories` (`Core/Keyed/Dialogs_Various.xml`). `ampliar` = 0× in Castilian |
| collapse all (tree) | Reducir todo | **Colapsar todo** | `CollapseAllCategories` (same file) |
| passion — none | Sin pasión | **Desinteresado** | `PassionNone` (`Core/Keyed/Skills.xml`) — gendered `-o`, see §6 |
| passion — minor | Pasión | **Interesado** | `PassionMinor` (same file). `PassionMajor` = `Apasionado` in both |
| no medical care | sin atención médica | **sin tratamiento** | `MedicalCareCategory_NoCare` (`Core/Keyed/Enums.xml`). Castilian recasts the whole enum around *tratamiento*: `_Best` LAT "medicina de la mejor calidad" → CAST "el mejor tratamiento médico posible"; `_NoMeds` "tratamiento médico sin medicina". `_HerbalOrWorse` / `_NormalOrWorse` are identical. **`atención médica` is still good Castilian prose** (it appears ~10× in Castilian DefInjected backstories and research text) — this is an enum-wording delta, not a blanket word ban |
| tech level — spacer | espacial | **era espacial** | `TechLevel_Spacer` (`Core/Keyed/Enums.xml`) |
| tech level — archotech | arqueotéc | **arqueoteca** | `TechLevel_Archotech` (same file) |
| psyfocus | Psicofoco | **Psifoco** | `Psyfocus` (`Royalty/Keyed/Misc_Gameplay.xml`). Corpus-wide: psifoco 48 vs psicofoco 2 |
| quadrum — Aprimay | Abrimay | **Marzimayo** | `QuadrumAprimay` (`Core/Keyed/Time.xml`) |
| quadrum — Jugust | Jugosto | **Junligosto** | `QuadrumJugust` (same file) |
| quadrum — Septober | Septobre | **Septubre** | `QuadrumSeptober` (same file) |
| quadrum — Decembary | Dicinero | **Difebrero** | `QuadrumDecembary` (same file) |
| yes | Sí | **Sí** | `Yes` — vanilla LAT ships the unaccented "Si"; **Castilian ships the correct "Sí"** (`Core/Keyed/Misc.xml`). Our glossary already mandated `Sí`, so no edit; the anchor is now real |
| hair (concept) | cabello | **pelo** | `Hair`, `HairColor` "color de pelo" (`Ideology/Keyed/Dialogs_Various.xml`). Corpus-wide: pelo 56 vs cabello 3. **BUT `HairAndBeards` = "Cabellos y barbas" in BOTH** — keep that compound heading |
| tattered apparel (alert) | Vestimenta andrajosa | **Ropa andrajosa** | `AlertTatteredApparel` (`Core/Keyed/Alerts.xml`). This is the alert only — see §4.3 on `vestimenta` |
| tend (float menu) | Atender a {0} | **Atender {0}** | `Tend` (`Core/Keyed/FloatMenu.xml`) — the preposition is dropped |
| prioritized work | trabajo priorizado | **trabajo prioritario** | `CommandClearPrioritizedWorkDesc` (`Core/Keyed/GameplayCommands.xml`) |
| idle research (alert) | Investigación ociosa | **Ningún proyecto de investigación asignado** | `NeedResearchProject` (`Core/Keyed/Alerts.xml`) |
| selling / buying | vendiendo / comprando | **Vendiendo / Comprando** | `Selling`, `Buying` (`Core/Keyed/Dialogs_Various.xml`) — capitalized in Castilian |
| computer | computadora | **ordenador** | `ConfirmPermanentlyDisableDevMode` "…en este **ordenador**" (`Core/Keyed/Dialogs_Various.xml`). computadora = 0× in Castilian, ordenador = 0× in LAT |
| pollution clear area | Retiro de contaminación | **Área de retirada de contamin.** | `DesignatorAreaPollutionClearExpand` (`Biotech/Keyed/Designators.xml`) — note the **abbreviation with a period**, which breaks typeahead instructions (§5) |
| video (memory) | video | **vídeo** | `TextureCompression_Tooltip` "memoria de **vídeo**" (`Core/Keyed/Menu_Options.xml`) |
| cost | costo | **coste** | Corpus-wide: coste 27 vs costo 3 (e.g. `Core/Keyed/Misc_Gameplay.xml`). Pilot finding, lead-verified. 15 occurrences in our files: `RimWorldAccess_Map.xml` (path cost), `RimWorldAccess_Research.xml`, `RimWorldAccess_TransportPods.xml`, `Overrides_Vanilla.xml` |

### 4.2 DefInjected deltas

| English | Our SpanishLatin | Castilian | Corpus citation |
|---|---|---|---|
| warden (work type) | Vigilante / vigilante | **Guardia / guardia** | `Warden.label`, `.pawnLabel`, `.labelShort` (`Core/DefInjected/WorkTypeDef/WorkTypes.xml`). `vigilante` = 0× in Castilian |
| recreation / joy (need) | recreación | **diversión** | `Joy.label` (`Core/DefInjected/NeedDef/Needs.xml`) AND `Joy.label` (`Core/DefInjected/DesignationCategoryDef/DesignationCategories.xml`). `recreación` = 0× in Castilian |
| food (need) | alimentación | **comida** | `Food.label` (`Core/DefInjected/NeedDef/Needs.xml`) |
| rest / sleep (need) | sueño | **descanso** | `Rest.label` (same file) |
| hauling (work type) | Transporte | **Transportar cosas** | `Hauling.label` (`Core/DefInjected/WorkTypeDef/WorkTypes.xml`) |
| research (work type) | Investigación | **Investigador** | `Research.label` (same file). The MainButtonDef `Research.label` = `investigación` in both |
| shooting (skill) | tiro | **disparo** | `Shooting.label` (`Core/DefInjected/SkillDef/Skills.xml`) |
| intellectual (skill) | intelectual | **inteligencia** | `Intellectual.label` (same file) |
| assign (main button) | políticas | **asignar** | `Assign.label` (`Core/DefInjected/MainButtonDef/MainButtons.xml`) |
| shaved (hair) | rapado | **afeitado** | `Shaved.label` (`Core/DefInjected/HairDef/HairsGeneral.xml`) |
| curly (beard) | rizada | **rizado** | `BeardCurly.label` (`Core/DefInjected/BeardDef/BeardDefs.xml`) — masculine, agreeing with "pelo"/"bigote" rather than "barba" |
| royal (style category) | realeza | **regio** | `Royal.label` (`Core/DefInjected/StyleItemCategoryDef/StyleItemCategoryDefs.xml`) |
| misc (style category) | varios | **variado** | `Misc.label` (same file). **Only the style category** — `DesignationCategoryDef/Misc.label` = `varios` in both |
| baseliner (xenotype) | basinerte | **básico** | `Baseliner.label` (`Biotech/DefInjected/XenotypeDef/XenotypeDefs.xml`) |
| mechanical leg | pierna | **pata** | `MechanicalLeg.label` (`Core/DefInjected/BodyPartDef/BodyParts_Mechanoid.xml`), `FrontLeftLeg.labelShort` (`BodyPartGroupDef`). **`Leg.label` = `pierna` in both** — only mech/animal legs change |
| comfort (stat) | confort | **comodidad** | `Comfort.label` (`Core/DefInjected/StatDef/Stats_Basics_General.xml`). The **NeedDef** `Comfort.label` = `comodidad` in both |
| anything (time assignment) | cualq. cosa | **tiempo libre** | `Anything.label` (`Core/DefInjected/TimeAssignmentDef/TimeAssignments.xml`) |

### 4.3 Verified IDENTICAL — do not churn these

Confirmed byte-identical in both corpora. A delta agent that "corrects" one of these introduces a
regression.

**Designators and orders:** Cancelar, Talar, Minar, Minar veta, Cosechar, Cortar plantas,
Desinstalar, Transportar cosas, Cazar, Domesticar, Sacrificar, Prohibir, Permitir, Reclamar,
Alisar superficie, Designar para prisioneros, Sembrar, Recombinar, Expandir área (`DesignatorZoneExpand`).

**People and world:** colono, Caravana, Mapa, Área (`Zone`), área (`AreaLower`), áreas
(`Zone.label`), Prisionero/prisionero, Área de cultivo, almacén (`StockpileGroup`),
Asalto/Asedio (`RaidStrategyDef`), Área de pesca + `FishingGroup` = pesca.

**Build categories:** estructuras, mobiliario, suelos, electricidad, producción, seguridad,
temperatura, órdenes, varios (`DesignationCategoryDef/Misc`).

**Main buttons:** trabajo, investigación, arquitecto, horarios, animales, fauna, mundo, menú,
inspeccionar, historia, misiones, facciones.

**Needs:** humor, comodidad, belleza, tamaño de la habitación, aire libre.

**Work types:** Medicina, Construcción, Minería, Cocina, Agricultura, Limpieza, Bombero.

**Skills:** cuerpo a cuerpo, construcción, minería, cocina, agricultura, animales, fabricación,
arte, medicina, socialización.

**Materials:** plata, oro, acero, madera.

**UI and state words:** Rango, Activar, Desactivar, Activado, Desactivado, Encendido, Apagado,
Cerrar, Cancelar, Aceptar, Confirmar, OK, No, Nivel, Salud, Resumen, Temperatura, Investigación,
Detener investigación, Habilidades, Rituales, Información.

**Time:** primavera, verano, otoño, invierno, días, `{0} días`, horas.

**Tech levels:** animal, neolítico, medieval, industrial, ultra.

**Styling:** barba, Tatuajes, tatuaje facial, tatuaje corporal, Color, Cabellos y barbas,
afro, calvo, corto, trenzada, corazón, cruz, tribal, urbano, punk, soldado.

**Words a dialect stereotype would wrongly flag:**
- **`vestimenta` stays.** It is *more* frequent in Castilian (49) than in Latin American (29), and
  `Apparel` = "Vestimenta" in both. Only the `AlertTatteredApparel` string changes (§4.1).
- **`ratón` stays** (both dialects; never "mouse").
- **`archivo` stays** — `fichero` appears **0×** in the Castilian corpus.
- **`coger` is never introduced.** Despite being unremarkable in Spain, it appears 0× as a verb in
  the Castilian corpus (the only hits are inside `Escoger`). Keep `tomar`/`escoger`.
- **`presiona`, `clic`, `botón`, `cursor`, `pestaña`, `menú`, `tecla de atajo`** — all unchanged.

---

## Section 5 — Quote law: resolved vanilla labels quoted inside our strings

Our two DefInjected files quote **resolved vanilla UI labels** so a player can match what they hear
in the docs to what they hear in the game. If the quoted label does not match the running game, the
instruction is actively wrong. All 21 quoted spans in `Languages/SpanishLatin/` live in
`DefInjected/RimWorldAccess.ConceptHelpOverrideDef/Overrides_Vanilla.xml` and
`DefInjected/ConceptDef/Concepts_RimWorldAccess.xml`.

| Quoted in our SpanishLatin | Vanilla key | Castilian resolved value | Action |
|---|---|---|---|
| "Añadir proyecto" | `AddBill` (`Core/Keyed/ITabs.xml`) | Añadir proyecto | keep |
| "Ir al lugar" | `JumpToLocation` (`Core/Keyed/Letters.xml`) | Ir al lugar | keep |
| "Ir aquí" | `GoHere` (`Core/Keyed/FloatMenu.xml`) | Ir aquí | keep |
| "Expandir área del hogar" | `DesignatorAreaHomeExpand` | **Expandir área de hogar** | **change** |
| "asignable" (typeahead) | `DesignatorExpandAreaAllowed` = **Expandir área** | no "asignable" anywhere | **rewrite the instruction** — see below |
| "almacén" (typeahead) | `Stockpile` = **Almacén** | matches | keep the typeahead; fix the surrounding prose "el área del almacén" → "el almacén" |
| "contaminación" (typeahead) | `DesignatorAreaPollutionClearExpand` = **Área de retirada de contamin.** | truncated to "contamin." | **change typeahead to "contamin"** |
| "Planta:" / "Planta: arroz" | `CommandSelectPlantToGrow` = **Sembrar: {0}** in BOTH dialects | Sembrar: / Sembrar: arroz | **change — and flag: our SpanishLatin is already wrong** |
| "Cualquiera" (time block) | `Anything.label` (`TimeAssignmentDef`) — LAT `cualq. cosa` | **tiempo libre** | **change — and flag: our SpanishLatin is already wrong** |
| "Xenotipo: basinerte" | `Xenotype` = Xenotipo + `Baseliner.label` | **Xenotipo: básico** | **change** |
| "pared" (typeahead) | `Wall.label` = **muro** in BOTH dialects | muro | **change to "muro" — and flag: our SpanishLatin is already wrong** |
| "ampliar" (typeahead for the home-area designator) | designator label is `Expandir área de hogar` | no "ampliar" in Castilian at all | **change to "expandir" — and flag: already wrong in SpanishLatin** |
| "Ajustes de almacenamiento" | storage ITab; `TabStorage` = **Almacén** in both | verify against the live inspect tree | **flag for lead** — no vanilla key resolves to this exact string |
| "Meditar" (schedule brush) | `Meditate.label` = `meditar` (both) | meditar | keep (our string capitalizes the brush name) |
| "(formación)" | our own suffix, not a vanilla key | n/a | keep |
| "cultivo", "minar", "pesca", "pierna", "inf" (typeaheads) | `GrowingZone`, `DesignatorMine`, `FishingGroup`, `Leg.label` — all identical | match | keep |

**`CommandSelectPlantToGrow` answer to the standing question:** it is **not** bare `{0}`. Both
dialects ship `Sembrar: {0}` (`Core/Keyed/GameplayCommands.xml`). Our docs claiming the gizmo reads
"Planta:" are wrong in Spanish generally, not just in Castilian.

**Rewriting the allowed-area instruction.** In Castilian, both the create and delete designators are
called plain "Expandir área" / "Reducir área", and `DesignatorZoneExpand` is *also* "Expandir área" —
vanilla itself is ambiguous here. Do not promise a unique typeahead hit. Describe it as searching for
"área" and arrowing to the expand/reduce area designator, and use **área permitida** for the concept
noun (matching `AllowedArea`).

---

## Section 6 — Typography and TTS rules (re-verified for Castilian)

- **Inverted opening marks are required** and are used heavily: `¿` 110×, `¡` 147× in the Castilian
  Keyed corpus. Same law as the parent glossary — full marks inside a genuine prose sentence within
  one translated value, never around a composed fragment or a bare label.
- **Quotation marks are straight `"…"`.** Guillemets are **never** used: `«` and `»` both return
  **0** hits, as do the curly `“ ” ’`. 107 straight-quote pairs appear inside Castilian values, e.g.
  `UndiscoveredEntityDesc` "…el ritual psíquico "provocación del vacío""
  (`Anomaly/Keyed/Dialogs_Various.xml`). Do not
  introduce `«»` — a Spanish typographic instinct that would break our quote convention and add
  characters some TTS voices read aloud.
- **No U+00A0 (NBSP) and no U+202F.** Both return **0** hits in the Castilian corpus. Never emit
  them; a screen reader may voice them or swallow the word boundary.
- **Straight apostrophes only.** The typographic `’` returns 0 hits.
- **No em/en dashes.** `—` and `–` both return 0 hits in the corpus, and our house style forbids them
  anyway.
- **ASCII fragment joiners stay byte-identical to English.** The `". "`, `", "`, `": "` glue between
  composed TTS segments is unchanged from the parent glossary and from the SpanishLatin files. Keep
  the trailing space; never substitute punctuation.
- **Keep every diacritic:** `á é í ó ú ñ ü`, plus `¡ ¿`. `Sí` (affirmative) keeps its accent —
  Castilian vanilla gets this right where Latin American ships "Si".
- **Gender parentheticals `(a)` and `@` are FORBIDDEN in our strings** because TTS reads the
  punctuation aloud ("reclutado paréntesis a"). **Castilian's own strategy supports us:** the corpus
  overwhelmingly uses the inline gender tag — `{PAWN_gender ? o : a}` 55×, `{0_gender ? o : a}` 38×,
  `{PAWN_gender ? Un : Una}` 5× — and it *converted* at least one parenthetical that Latin American
  still ships: `IsNotDraftedLower` is LAT `no está reclutado(a)` but CAST
  `no está reclutad{1_gender ? o : a}` (`Core/Keyed/FloatMenu.xml`). About 25 `(a)` forms survive as
  stragglers (`hijo(a)`, `cansado(a)`, `dañado(a)`), but the direction of travel is clear.
  - Use `{ARG_gender ? … : …}` **only when the placeholder genuinely carries gender info** (a pawn
    arg the game tags). For a generic `{0}` item label there is no `_gender` tag.
  - Otherwise use the parent glossary's §3.4 recasts: noun forms and `label: value` colons
    (`Selección: {0}`, `Objetivo: {0}`, `Estado: {0}`), and bare infinitives for commands.
  - **Watch the new passion labels.** Castilian `Interesado`/`Desinteresado` (`PassionNone`,
    `PassionMinor`) are gendered adjectives describing a pawn, where LAT's `Sin pasión`/`Pasión` were
    genderless nouns. If our strings ever compose these with a pawn, recast around them rather than
    inheriting the agreement problem. Our current SpanishLatin files use neither string, so this is a
    forward-looking caution.
- **Placeholders, keys, `\n`, and leading/trailing spaces:** unchanged from the parent glossary §3.6.
  Copy byte-for-byte; repositioning within a sentence is allowed, renumbering and translating are not.

---

## Section 7 — Delta-pass checklist for file-level agents

For each cloned file in `Languages/Spanish/`, in this order. **Anything not on this list stays
byte-identical to the SpanishLatin source.**

1. **Do not touch the register.** No tú→usted conversion. No pronoun changes. §2 settles this.
2. **Do not touch plurals, placeholders, XML keys, `\n`, or leading/trailing spaces.** No `_numCase`,
   ever (§3).
3. **Apply the §4.1 and §4.2 vocabulary substitutions** where the term appears. Sized against our
   actual SpanishLatin files, highest-volume first:
   - `cuadro` → `casilla` — **88 occurrences**, spread across `Concepts_RimWorldAccess.xml`,
     `Overrides_Vanilla.xml`, and most `RimWorldAccess_*.xml` files. The single biggest edit.
   - `cabello` → `pelo` — **79 occurrences**, concentrated in `RimWorldAccess_CharEditor.xml`,
     `RimWorldAccess_StyleDescriptions.xml`, `RimWorldAccess_Styling.xml`. **Keep the compound
     "Cabellos y barbas"** where it renders that vanilla heading.
   - `asignable` / `área asignable` → `área permitida` — 9 occurrences in `Overrides_Vanilla.xml`,
     `RimWorldAccess_Animals.xml`, `RimWorldAccess_Mechs.xml`.
   - `ampliar` / `Ampliar` → `expandir` / `Expandir`, and tree-collapse `reducir` / `Reducir` →
     `colapsar` / `Colapsar` — 10 and 8 occurrences. **Judgement required:** only change `reducir`
     where it means *collapse a tree node*. Castilian uses `Reducir` for the shrink-an-area
     designators (`Reducir área de hogar`), so an area context keeps `Reducir`.
     **LEAD RULING (pilot round): verbs vs states differ.** Collapse/expand COMMANDS follow the
     vanilla verbs (`Expandir todo` / `Colapsar todo`). But the STATE adjectives spoken on every
     tree row are `expandido` / `contraído` — the NVDA-es screen-reader convention (parent glossary
     §3.4); `colapsado` is reserved by the game for roof collapse and mental breaks and sounds like
     breakage. Do not write `colapsado` as a node state. Where no natural adjective exists, recast
     with the verb (`Ningún elemento para expandir`, not the coinage `expandible`).
   - `psicofoco` / `Psicofoco` → `psifoco` / `Psifoco` — 11 occurrences.
   - `Planificación` → `Planificar` — 6 occurrences (`RimWorldAccess_Building.xml`,
     `RimWorldAccess_Map.xml`).
   - `varios` → `variado` — 6 occurrences, but **only where it renders the StyleItemCategoryDef
     label** (likely `RimWorldAccess_StyleDescriptions.xml`); the build-category `varios` is
     unchanged. Check each site.
   - `realeza` → `regio` — 5 occurrences; `rapado` → `afeitado` — 4; `Desvestir` → `Desnudar` — 3;
     `desarmar`/`Desarmar` → `Deconstruir` — 3; `recreación` → `diversión` — 2;
     `área del almacén` → `almacén` — 3; `alimentación` → `comida` — 1; `basinerte` → `básico` — 1;
     `tiro` → `disparo` — 1.
   - Terms with **zero occurrences** in our files — `vigilante`, `sueño`, the four quadrums,
     `computadora`, `cliquea`, `confort`, `intelectual`, `Sin pasión`, `espacial`, `arqueotéc` — need
     no edit. They are recorded in §4 as anchors for future strings only.
4. **Fix the quoted vanilla labels per §5.** These are the highest-severity edits: a wrong quoted
   label sends a blind player hunting for a string the game never says. Both DefInjected files only.
   While there, check the four medical-care strings in `RimWorldAccess_Animals.xml`
   (`Paint.Single.MedicalCareApplied` and siblings): they compose our label noun with a vanilla
   `MedicalCareCategory_*` value, and in Castilian that value is now *tratamiento*-based, so
   "atención médica {1}" reads as "atención médica sin tratamiento". Prefer "tratamiento médico {1}"
   there. (LAT has the same redundancy — "atención médica sin atención médica" — so this is a genuine
   improvement, not dialect drift.)
5. **Leave `vestimenta`, `ratón`, `archivo`, `presiona`, `clic`, `tomar` alone** (§4.3). Do not
   introduce `coger`, `fichero`, `ordenador` (outside the one computer-hardware sense), or guillemets.
6. **Typography check before finishing:** no `«»`, no NBSP/U+202F, no curly quotes or apostrophes, no
   em dashes, no new `(a)` or `@` gender parentheticals, ASCII joiners intact (§6).
7. **Validate XML well-formedness** after editing — a malformed file is discarded whole and silently
   by RimWorld's loader.

---

## Section 8 — Flags for lead review

1. **Three pre-existing SpanishLatin errors surfaced by this audit** (they are wrong in the shipped
   Latin American files too, not just Castilian):
   - `"Planta:"` / `"Planta: arroz"` — vanilla ships `Sembrar: {0}` in **both** dialects
     (`CommandSelectPlantToGrow`). 3 occurrences.
   - `"pared"` as the architect typeahead for a wall — `Wall.label` is **`muro`** in both dialects,
     so the documented keystrokes do not find it.
   - `"Cualquiera"` for the Anything schedule block — LAT vanilla says `cualq. cosa`.
   - Also suspicious: `"ampliar"` as the typeahead for a designator vanilla labels
     `Expandir área del hogar` in LAT. Typing "ampliar" would not match it.
2. **`"Ajustes de almacenamiento"`** (`RWA_Ovr_StorageTab.helpText`) does not resolve to any vanilla
   key in either corpus. The nearest matches are `TabStorage` = `Almacén` (both dialects) and
   `LinkedStorageSettings` = LAT "Configuración de almacenamiento vinculado" / CAST "Ajustes de
   almacenamiento vinculado". This string may be one of **our own** inspect-tree section labels, in
   which case it is not a quote at all and needs no change. Worth one live check.
3. **`DesignatorExpandAreaAllowed` collides in Castilian.** Both it and `DesignatorZoneExpand`
   resolve to "Expandir área". Vanilla's own ambiguity; our docs must not promise a unique typeahead
   hit (§5).
4. **`/tmp/es_ref` — the corpus path cited throughout `es-glossary.md` — is now an empty directory**
   (temp cleanup removed the XML while leaving the folders). Any future re-verification of a
   SpanishLatin citation needs a fresh extraction from the game tar; the command is at the top of
   this file. Consider updating `es-glossary.md`'s Section 4 to cite the tar instead of `/tmp`.
5. **Not verified:** whether any mod-compat file (`RimWorldAccess_Compat.xml`) quotes third-party mod
   labels that a Castilian player sees untranslated. Those mods ship their own translations (or none),
   so a quoted label there may be wrong in every language. Out of scope for this delta but worth a
   pass.

## Mod-coined terms — watermill placement + read-only browsing (2026-08-13)

Same 13 new keys as the SpanishLatin wave (see `es-glossary.md`'s matching section). No Castilian-specific
delta needed: every new value carries over unchanged from SpanishLatin — the only wording difference
in the whole set is the pre-existing "casillas" (Castilian) vs. "cuadros" (Latin American) tile word,
already governed by this file's earlier tile-word delta, not a new decision.
