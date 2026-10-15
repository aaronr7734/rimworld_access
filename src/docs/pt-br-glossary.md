# Brazilian Portuguese (Português Brasileiro) Terminology & Style Glossary — RimWorld Access

This glossary locks in consistent Brazilian Portuguese wording for translating the RimWorld Access
screen-reader mod. The goal is that Brazilian players feel the mod is a seamless extension of
RimWorld itself, so **every game-anchored term below uses the EXACT word RimWorld's own official
Brazilian Portuguese translation uses**, with a citation to the corpus file it was taken from.

The strings we translate are **spoken aloud by a TTS engine**, not displayed. Natural, terse,
unambiguous phrasing matters more than visual polish.

Reference corpus: RimWorld's official Brazilian Portuguese, extracted from
`PortugueseBrazilian (Português Brasileiro).tar` for Core plus the Royalty, Ideology, Biotech,
Anomaly and Odyssey equivalents — 1543 XML files, 8205 `Keyed/` entries and 24363 `DefInjected/`
entries per module tree. Paths below are relative to the extracted corpus root (`Core/`,
`Royalty/`, `Ideology/`, `Biotech/`, `Anomaly/`, `Odyssey/`). All counts in this document were
produced by parsing the XML with Python's `xml.etree.ElementTree` and skipping comment nodes —
never by grepping raw text, which double-counts the `<!-- EN: ... -->` English source comments
that are interleaved with every value in this corpus.

> How to use this file: grep for the English term. If a term you need is not here and RimWorld has
> it, grep the game corpus yourself (see Section 6) and add it — never invent a rendering for a
> concept the game already names.

---

## Section 0 — Language folder name (READ FIRST)

**Ship the mod's Brazilian Portuguese strings in `Languages/PortugueseBrazilian/`** — the pure-ASCII
legacy name, NOT `PortugueseBrazilian (Português Brasileiro)` and NOT `Portuguese (Brasil)`.

Core ships the language as `PortugueseBrazilian (Português Brasileiro).tar`, but a mod adding to an
existing language is merged by `LanguageDatabase.InitLanguageMetadataFrom`, which matches on
`folderName == langDir.Name || LegacyFolderName == langDir.Name`. `LegacyFolderName` is the part
before the `(` — i.e. `PortugueseBrazilian`. Naming our folder with the native parenthesized form
would embed non-ASCII characters (`ã`, `ê`) whose byte representation can change under Unicode
normalization (NFC vs NFD) when a zip crosses platforms. `PortugueseBrazilian` has no non-ASCII
bytes, so it always matches Core's `LegacyFolderName` and merges correctly on every platform. This
matches every language the mod already ships (`Languages/French/`, `Languages/Ukrainian/`,
`Languages/ChineseSimplified/`, `Languages/SpanishLatin/`, `Languages/Turkish/`). The menu still
displays "Português Brasileiro" — that comes from Core's entry, which our folder merges into. No
`LanguageInfo.xml` is needed.

Mirror `Languages/English/Keyed/` exactly: we ship **only** `Keyed/`.

---

## Section 1 — Core game vocabulary (game-anchored)

Every row is the term RimWorld itself ships in Brazilian Portuguese. Use it verbatim. These are the
words the player already hears from the game, so our mod must match them exactly — even in the rare
cases where the official translation itself is imperfect (see the `Apparel` → `Aparência` entry
below): the player already hears that word from vanilla, so consistency wins over "correctness".

### People, factions, world

| English | Português (BR) | Source file |
|---|---|---|
| colonist | colono | `Core/Keyed/Misc.xml` (`Colonist` **colono**) |
| colony | colônia | `Core/Keyed/Misc.xml` (`Colony` **Colônia**) |
| pawn / character (generic) | personagem | `Biotech/DefInjected/PawnKindDef` and similar use **personagem**; the plain-human sense also uses **pessoa**, e.g. `Biotech/Keyed/FloatMenu.xml` (`NoViablePawns` Nenhuma **pessoa** disponível) |
| faction | facção | `Core/Keyed/Misc_Gameplay.xml` (`Faction` **Facção**) |
| caravan | caravana | `Core/Keyed/Misc_Gameplay.xml` (`Caravan` **Caravana**) |
| raid | invasão | `Core/Keyed/Misc_Gameplay.xml` (`Raid` **Invasão**) |
| map | mapa | `Core/Keyed/Misc_Gameplay.xml` (`Map` **Mapa**) |
| map cell / square | célula (verify per-context) | no dedicated Keyed row found; use the game's own `case`-equivalent sparingly and prefer describing the position in words. See §3.8 false friends — do not confuse with the UI table "cell" |
| room | cômodo | `Core/Keyed/Misc_Gameplay.xml` (`Room` **Cômodo**) |
| biome | bioma | `Core/Keyed/Menus_Main.xml` (`Biome` **Bioma**) |
| terrain | terreno | `Core/Keyed/Menus_Main.xml` (`Terrain` **Terreno**) |
| elevation | elevação | `Core/Keyed/Menus_Main.xml` (`Elevation` **Elevação**) |
| rainfall | chuva | `Core/Keyed/Menus_Main.xml` (`Rainfall` **Chuva**) |
| forageability | foragibilidade | `Core/Keyed/Menus_Main.xml` (`Forageability` **Foragibilidade**) |
| goodwill | boa vontade | `Core/Keyed/Misc_Gameplay.xml` (`Goodwill` **Boa vontade**) |
| ally / hostile / neutral | aliado / hostil / neutro | `Core/Keyed/MainTabs.xml` (`Ally` **Aliado**, `Hostile` **Hostil**), `Core/Keyed/Letters.xml` (`Neutral` **Neutro**) |
| hilliness | plano / colinas pequenas / colinas grandes / montanhoso / intransitável | `Core/Keyed/Enums.xml` (`Hilliness_Flat`…`Hilliness_Impassable`) |
| gravship | gravinave | `Odyssey/Keyed/Designators.xml` (`DesignatorMoveGravshipDesc` local de pouso da **gravinave**), `Odyssey/Keyed/GameplayCommands.xml` (`CommandLaunchGravshipDesc`) |
| storyteller | narrador | `Core/Keyed/MainTabs.xml` (`Storyteller` **Narrador**) |
| mod | mod | `Core/Keyed/Menus_Main.xml` (widely, e.g. `ModOrderingWarning`) — a kept loanword |

### Designators / order verbs (INFINITIVE, exactly as the game writes buttons)

Brazilian Portuguese command labels are **infinitives**, not imperatives and not nouns — the same
convention French uses, and the corpus is uniform about it.

| English | Português (BR) | Source file |
|---|---|---|
| cancel | Cancelar | `Core/Keyed/Designators.xml` (`DesignatorCancel`) |
| mine | Minerar | `Core/Keyed/Designators.xml` (`DesignatorMine`) |
| mine vein | Veia de mina | `Core/Keyed/Designators.xml` (`DesignatorMineVein`) — a noun-phrase button, not the verb; note the mismatch with English's own gizmo naming |
| harvest | Colher | `Core/Keyed/Designators.xml` (`DesignatorHarvest`) |
| chop wood / harvest wood | Cortar árvores | `Core/Keyed/Designators.xml` (`DesignatorHarvestWood`) |
| cut plants | Cortar plantas | `Core/Keyed/Designators.xml` (`DesignatorCutPlants`) |
| deconstruct | Desconstruir | `Core/Keyed/Designators.xml` (`DesignatorDeconstruct`) |
| uninstall | Desinstalar | `Core/Keyed/Designators.xml` (`DesignatorUninstall`) |
| haul (things) | Transportar coisas | `Core/Keyed/Designators.xml` (`DesignatorHaulThings`) |
| hunt | Caçar | `Core/Keyed/Designators.xml` (`DesignatorHunt`) |
| tame | Domesticar | `Core/Keyed/Designators.xml` (`DesignatorTame`) |
| slaughter | Abater | `Core/Keyed/Designators.xml` (`DesignatorSlaughter`) |
| release to wild | Libertar | `Core/Keyed/Designators.xml` (`DesignatorReleaseAnimalToWild`) |
| forbid | Proibir | `Core/Keyed/Designators.xml` (`DesignatorForbid`) |
| unforbid / allow | Permitir | `Core/Keyed/Designators.xml` (`DesignatorUnforbid`) |
| claim | Reivindicar | `Core/Keyed/Designators.xml` (`DesignatorClaim`) |
| strip | Remover roupas | `Core/Keyed/Designators.xml` (`DesignatorStrip`) |
| open (container) | Abrir | `Core/Keyed/Designators.xml` (`DesignatorOpen`) |
| smooth (surface) | Alisar superfície | `Core/Keyed/Designators.xml` (`DesignatorSmoothSurface`) |
| plan (blueprint) | Planos | `Core/Keyed/Designators.xml` (`DesignatorPlan`) — a rare noun-labeled gizmo |
| study / inspect (designator) | Estudar | `Core/Keyed/Designators.xml` (`DesignatorStudy`) |
| extract tree | Extrair árvore | `Core/Keyed/Designators.xml` (`DesignatorExtractTree`) |
| remove floor | Remover piso | `Core/Keyed/Designators.xml` (`DesignatorRemoveFloor`) |
| expand zone | Expandir zona | `Core/Keyed/Designators.xml` (`DesignatorZoneExpand`) |
| delete/shrink zone (singular) | Apagar zona / Encolher plano | `Core/Keyed/Designators.xml` (`DesignatorZoneDeleteSingular`, `DesignatorPlanShrinkSingular`) |
| paint (building) | Pintar construções... | `Core/Keyed/Designators.xml` (`DesignatorPaintBuilding`) |
| designate (verb, in a description) | designar / marcar | `Core/Keyed/Designators.xml` (`DesignatorMineDesc` **Seleciona** áreas de rocha para serem **mineradas** — descriptions favor passive "para serem X-adas", see §3.1) |
| tend (a wound) | Tratar | `Core/Keyed/FloatMenu.xml` (`Tend` **Tratar** {0}) |
| rescue | Resgatar | `Core/Keyed/FloatMenu.xml` (`Rescue` **Resgatar** {1_labelShort}) |
| arrest | Prender | `Core/Keyed/FloatMenu.xml` (`Arrest` **Prender** {0}) |
| capture | Capturar | `Core/Keyed/FloatMenu.xml` (`Capture` **Capturar** {1_labelShort}) |
| equip | Equipar | `Core/Keyed/FloatMenu.xml` (`Equip` **Equipar** {0}) |
| attack | Atacar | `Core/Keyed/FloatMenu.xml` (`Attack` **Atacar** {1_labelShort}) |
| clean (a room) | Limpar | `Core/Keyed/FloatMenu.xml` (`CleanRoom` **Limpar** {0}) |
| consume | Consumir | `Core/Keyed/FloatMenu.xml` (`ConsumeThing` **Consumir** {1_labelShort}) |
| call (on radio) | Chamar | `Core/Keyed/FloatMenu.xml` (`CallOnRadio` **Chamar** {0}) |
| man (a turret) | Usar | `Core/Keyed/FloatMenu.xml` (`OrderManThing` **Usar** {1_labelShort}) |
| remove | Remover | `Core/Keyed/FloatMenu.xml` (`Remove`) |
| visit | Visitar | `Core/Keyed/FloatMenu.xml` (`VisitSettlement` **Visitar** {0}) |
| trade with | Negociar com | `Core/Keyed/FloatMenu.xml` (`TradeWith`, `TradeWithSettlement` **Negociar com** {0}) |
| cannot … | Impossível … | `Core/Keyed/FloatMenu.xml` (`CannotUseReason` **Impossível** usar: {0}; `CannotAttack` **não pode** atacar) — the corpus mixes the noun-phrase "Impossível X" and the finite "não pode X"; prefer "Impossível X" for short unavailability labels |
| draft | Alistar | `Core/Keyed/GameplayCommands.xml` (`CommandDraftLabel`) |
| undraft | Desalistar | `Core/Keyed/GameplayCommands.xml` (`CommandUndraftLabel`) |
| toggle (verb, generic) | Alternar | `Core/Keyed/GameplayCommands.xml` (`CommandToggleStudy` **Alternar** estudo), `Core/DefInjected/KeyBindingDef/KeyBindings.xml` (`Command_ItemForbid.label` **Alternar** Proibição) |

### Buildings, zones, areas, build categories

| English | Português (BR) | Source file |
|---|---|---|
| growing zone | Zona de cultivo | `Core/Keyed/Misc_Gameplay.xml` (`GrowingZone`) |
| stockpile / storage zone | Zona de estoque | `Core/Keyed/Misc_Gameplay.xml` (`Stockpile`) |
| home area | área da casa | `Core/Keyed/Designators.xml` (`DesignatorAreaHomeExpand` Expandir **área da casa**) |
| allowed area | Área permitida | `Core/Keyed/MainTabs.xml` (`AllowedArea`), `Core/Keyed/Designators.xml` (`DesignatorExpandAreaAllowed`) |
| area (generic) | área | `Core/Keyed/Misc_Gameplay.xml` (`AreaLower` **área**) |
| zone | zona | `Core/Keyed/Misc_Gameplay.xml` (`Zone` **Zona**). NOTE: `Core/DefInjected/DesignationCategoryDef/DesignationCategories.xml` (`Zone.label` **zona/Área**) contains a literal, untranslated `/` and stray capital — a corpus typo. Use plain **zonas** for the build-category tab; do not copy the slash |
| structure (build category) | estrutura | `Core/DefInjected/DesignationCategoryDef/DesignationCategories.xml` (`Structure.label`) |
| furniture | mobília | same file (`Furniture.label`) |
| floors | pisos | same file (`Floors.label`) |
| power | energia | same file (`Power.label`) |
| production | produção | same file (`Production.label`) |
| security | segurança | same file (`Security.label`) |
| temperature (category) | temperatura | same file (`Temperature.label`) |
| orders | ordens | same file (`Orders.label`) |
| recreation (build category) | lazer | same file (`Joy.label`) — note this differs from the *need* label, see below |
| misc | diversos | same file (`Misc.label`) |
| ship | nave | same file (`Ship.label`) |
| architect menu | Arquiteto | `Core/DefInjected/MainButtonDef/MainButtons.xml` (`Architect.label` **Arquiteto**) |
| storage priority (levels) | não estocado / baixa / normal / preferida / importante / crítica | `Core/Keyed/Enums.xml` (`StoragePriorityUnstored`…`StoragePriorityCritical`) |

### Main tabs (the game's own tab names)

`Core/DefInjected/MainButtonDef/MainButtons.xml` is the authority: `Animals.label` **Animais**,
`Architect.label` **Arquiteto**, `Assign.label` **Políticas** (NOT a literal "Atribuir" — the tab is
named for what it configures), `Factions.label` **Facções**, `History.label` **Histórico**,
`Inspect.label` **Inspecionar**, `Menu.label` **Menu**, `Quests.label` **Missões**,
`Research.label` **Pesquisa**, `Schedule.label` **Cronograma**, `Wildlife.label` **Vida Selvagem**,
`Work.label` **Trabalho**, `World.label` **Mundo**.

Inspect-tab names come from `Core/Keyed/ITabs.xml`: `TabHealth` **Saúde**, `TabGear` **Itens**,
`TabSocial` **Social**, `TabNeeds` **Situação**, `TabTraining` **Treino**,
`TabPrisoner` **Prisão**, `TabBills` **Tarefas**, `TabRecords` **Registros**,
`TabCharacter` **Perfil**, `TabStorage` **Estoque**.

### Work, priorities, skills, needs

| English | Português (BR) | Source file |
|---|---|---|
| work (tab / noun) | trabalho | `Core/DefInjected/MainButtonDef/MainButtons.xml` (`Work.label`) |
| bill (a work order) | tarefa | `Core/Keyed/ITabs.xml` (`TabBills` **Tarefas**, `AddBill` Adicionar **Tarefa**). **NOT "fatura"** |
| priority | Prioridade | `Core/Keyed/Misc_Gameplay.xml` (`Priority`) |
| manual priorities | Prioridade Manual | `Core/Keyed/MainTabs.xml` (`ManualPriorities`) |
| priority 0-4 | Não Fará / Prioridade Máxima / Prioridade Alta / Prioridade Normal / Prioridade Baixa | `Core/Keyed/MainTabs.xml` (`Priority0`…`Priority4`) |
| disabled by backstory / trait / quest | Desativado pela história {0} / pelo traço {0} / pela missão '{0}' | `Core/Keyed/MainTabs.xml` (`WorkDisabledByBackstory`, `WorkDisabledByTrait`, `WorkDisabledByQuest`) |
| skill level | Nível de habilidade | inferred from `Core/Keyed/ITabs.xml` skill vocabulary (`SkillLevel` not separately keyed; `Nível` confirmed below) |
| level | Nível | `Core/DefInjected/MainButtonDef` usage and `Royalty/Keyed/GameplayCommands.xml` (`CommandPsycastHigherLevelPsylinkRequired` psínculo de **nível** {0}) |
| trait | traço | `Core/Keyed/MainTabs.xml` (`WorkDisabledByTrait` Desativado pelo **traço** {0}) |
| backstory | história | `Core/Keyed/MainTabs.xml` (`WorkDisabledByBackstory` Desativado pela **história** {0}) |
| mood | humor | `Core/Keyed/Enums.xml` context and `Ideology/Keyed/Misc_Gameplay.xml` (`SelectedWorkTypeOpposedByIdeo` pode deixar essa pessoa infeliz); `Core/Keyed/MainTabs.xml`-adjacent need label below |
| food (need) | alimentação | `Core/DefInjected/NeedDef/Needs.xml` (`Food.label` **alimentação**) |
| recreation / joy (need) | diversão | same file (`Joy.label` **diversão**) — the *need* is "diversão", the build *category* is "lazer" (see above); keep them distinct |
| rest (need) | descanso | same file (`Rest.label`) |
| comfort | conforto | same file (`Comfort.label`) |
| beauty | beleza | same file (`Beauty.label`) |
| indoors / outdoors | interiores / exteriores | same file (`Indoors.label`, `Outdoors.label`) |
| room size (need) | tamanho do quarto | same file (`RoomSize.label`) |
| chemical (need) | química | same file (`DrugDesire.label`) |
| apparel | Aparência | `Core/Keyed/Misc_Gameplay.xml` (`Apparel` **Aparência**) — a genuine corpus oddity (a more literal word would be "roupas"/"vestimenta"), but this is what `ITab_Pawn_Gear` speaks via `"Apparel".Translate()`, so we must match it for consistency with vanilla speech |
| forced apparel | Vestuário Forçado | `Core/Keyed/MainTabs.xml` (`ForcedApparel`) — note "vestuário" appears too; the two words coexist in the corpus, see §7 false friends |
| policy (apparel/drug/food/reading) | políticas | `Core/Keyed/MainTabs.xml` (`ManageApparelPolicies` Gerenciar **Políticas** de Vestuário, `ManageDrugPolicies` Gerenciar **Políticas** de Droga, `ManageFoodPolicies` Gerenciar **Políticas** Alimentícias, `ManageReadingPolicies` Gerenciar **Políticas** de Leitura) |

**Skill names** — `Core/DefInjected/SkillDef/Skills.xml` gives both a lowercase `.label` (used
inline, e.g. "nível de habilidade em X") and a capitalized `.skillLabel` (used as a column/list
header): Animals `animal` / **Animal**, Artistic `artístico` / **Arte**, Construction `construção` /
**Construção**, Cooking `cozinha` / **Cozinha**, Crafting `fabricação` / **Fabricação**,
Intellectual `intelectual` / **Intelecto**, Medicine `medicina` / **Medicina**,
Melee `corpo a corpo` / **Corpo a Corpo**, Mining `mineração` / **Mineração**,
Plants `cultivo` / **Cultivo**, Shooting `tiro` / **Tiro**, Social `social` / **social** (note: the
corpus leaves `Social.skillLabel` lowercase — a rare inconsistency; Title-Case it if used as a
standalone header, to match its siblings).

**Work types** — `Core/DefInjected/WorkTypeDef/WorkTypes.xml` gives `labelShort` / `pawnLabel` /
`verb` for each; use the game's own value, never a re-translation: Art `arte` / Artista / Criar arte
em · BasicWorker `básico` / Trabalhador / Lidar com · Cleaning `faxina` / Faxineiro / Limpar ·
Construction `constr.` / Construtor / Construir · Cooking `cozinha` / Cozinheiro / Cozinhar ·
Crafting `fabricação` / Artesão / Fabricar em · Doctor `medicina` / Médico / Tratar de ·
Firefighter `incêndios` / Bombeiro / Apagar · Growing `agricultura` / Agricultor / Cultivar ·
Handling `domesticação` / Domador / Domesticar · **Hauling `auxílio` / Auxiliador / Auxiliar**
(hauling — NOT "transporte/transportador", even though `DesignatorHaulThings` uses "Transportar
coisas"; the two systems use different words for the same concept, keep them distinct per
context) · Hunting `caça` / Caçador / Caçar · Mining `mineração` / Mineiro / Minerar ·
Patient `tratam.` / Paciente / Ir para o tratamento · PatientBedRest `repouso` / Repouso / Repousar ·
PlantCutting `poda` / Podador / Cortar · Research `pesquisa` / Pesquisador / Pesquisar em ·
Smithing `forja` / Ferreiro / Forjar em · Tailoring `costura` / Alfaiate / Costurar em ·
**Warden `policiam.` / Guarda / Lidar** (warden — NOT "carcereiro/geôlier").

### Health

| English | Português (BR) | Source file |
|---|---|---|
| health | Saúde | `Core/Keyed/ITabs.xml` (`TabHealth`) |
| overview | Visão geral | `Core/Keyed/ITabs.xml` (`HealthOverview`) |
| bleeding (rate) | Sangramento | `Core/Keyed/ITabs.xml` (`BleedingRate`) |
| tend (verb) | tratar | `Core/Keyed/FloatMenu.xml` (`Tend` **Tratar** {0}) |
| self-tend | autoatendimento | `Core/Keyed/FloatMenu.xml` (`SelfTendDisabled` **autoatendimento** desabilitado) |
| medical care | cuidados médicos | `Core/Keyed/FloatMenu.xml` (`MedicalCareDisabled` **Cuidados médicos** desabilitados) |
| medical care levels | Sem Atendimento Médico / Atendimento sem Medicina / Medicina Natural ou Pior / Medicina Industrial ou Pior / Medicina de Melhor !ualidade | `Core/Keyed/Enums.xml` (`MedicalCareCategory_NoCare`…`MedicalCareCategory_Best`). NOTE: `MedicalCareCategory_Best` has a literal typo `!ualidade` for `Qualidade` in the shipped corpus — do not copy the typo if you ever quote this row; write **Qualidade** |
| operations (tab) | Operações | `Core/Keyed/ITabs.xml` (`MedicalOperationsShort`) |
| pain | Dor | `Core/Keyed/ITabs.xml` (`PainLevel`) |
| pain levels | leve / dolorido / doloroso | `Core/Keyed/Enums.xml` (`PainCategory_LowPain`…`PainCategory_HighPain`) |
| hunger levels | Faminto / Urgentemente com fome / Fome / Alimentado | `Core/Keyed/Enums.xml` (`HungerLevel_Starving`…`HungerLevel_Fed`) |
| rest levels | Exausto / Muito cansado / Cansado / Descansado | `Core/Keyed/Enums.xml` (same file, reused enum name `HungerLevel_Exhausted`…`HungerLevel_Rested`) |
| hediff / body part | use the specific `HediffDef` / `BodyPartDef` label | `*/DefInjected/HediffDef/*`, `Core/DefInjected/BodyPartDef/*` |
| efficiency | Eficiência | `Core/Keyed/ITabs.xml` (`Efficiency`) |
| missing / destroyed / removed (body part) | Removido / Destruído / Removido | `Core/Keyed/ITabs.xml` (`MissingBodyPart`, `DestroyedBodyPart`, `RemovedBodyPart`) |
| healthy | Saudável | `Core/Keyed/ITabs.xml` (`Healthy`) |

### Trade

| English | Português (BR) | Source file |
|---|---|---|
| trade (verb) | Negociar | `Core/Keyed/FloatMenu.xml` (`TradeWith`, `TradeWithSettlement` **Negociar com** {0}) |
| trader | comerciante | Corpus-wide count settles it: **comerciante** 86 occurrences vs **negociante** 7. The trade dialog's own file (`Core/Keyed/Dialogs_Various.xml` — `TraderWillNotTrade`, `YourTradeableSilver`) and the `TraderKindDef` labels all say comerciante; `FloatMenu.xml` `TraderDismissed` is one of the seven outliers. Use comerciante everywhere. |
| price — very cheap … exorbitant | muito barato / barato / normal / caro / exorbitante | `Core/Keyed/Enums.xml` (`PriceTypeVeryCheap`…`PriceTypeExorbitant`) |
| mass carried | Peso carregado | `Core/Keyed/ITabs.xml` (`MassCarried` **Peso carregado**: {0} / {1} kg) |
| market value | use the game's `StatDef` label | `Core/DefInjected/StatDef/*` |

### Research

| English | Português (BR) | Source file |
|---|---|---|
| research | Pesquisa | `Core/Keyed/MainTabs.xml` (`Research` — Research tab label. NOTE: the *button/verb* `Research` in `Core/Keyed/MainTabs.xml` renders **Pesquisar**; the DefInjected tab label renders **Pesquisa** — noun vs verb, keep them distinct) |
| techprint | projeto técnico | `Core/Keyed/Misc.xml` (`TechprintLabel` **projeto técnico** ({PROJECT_label})); plural **Projetos técnicos** in `Core/Keyed/MainTabs.xml` (`ResearchTechprintRequirement`) |
| tech level | nível de tecnologia | `Core/Keyed/MainTabs.xml` (`TechLevelTooLow` O **nível de tecnologia** da sua facção é {0}…) |
| tech levels | Animal / Neolítico / Medieval / Industrial / Espacial / Ultra / Arquotecnológico | `Core/Keyed/Enums.xml` (`TechLevel_Animal`…`TechLevel_Archotech`) |
| unresearched / finished / in progress / locked | Não Pesquisado / Finalizado / Em Progresso / Trancado | `Core/Keyed/MainTabs.xml` |
| prerequisites | Pré-Requisitos | `Core/Keyed/MainTabs.xml` (`Prerequisites`) |

### Prisoner, slave, ideology, ritual, abilities

| English | Português (BR) | Source file |
|---|---|---|
| prisoner | Prisioneiro / prisioneiro / prisioneiros | `Core/Keyed/Misc_Gameplay.xml` (`Prisoner`, `PrisonerLower`, `PrisonersLower`) |
| recruitment resistance | Resistência | `Core/Keyed/ITabs.xml` (`RecruitmentResistance`) |
| warden (work type) | Guarda | `Core/DefInjected/WorkTypeDef/WorkTypes.xml` (`Warden.pawnLabel`) |
| slave | Escravo | `Ideology/Keyed/ITabs.xml` (`TabSlave` **Escravo**) |
| suppression | repressão | `Ideology/Keyed/ITabs.xml` (`Suppression`) |
| will (a prisoner's resolve, Ideology) | Arbítrio | `Ideology/Keyed/ITabs.xml` (`WillLevel`) |
| ideoligion | ideologia | `Ideology/Keyed/Misc_Gameplay.xml` (`Ideo` **ideologia**) |
| precept | preceito / Preceitos | `Ideology/Keyed/Misc_Gameplay.xml` (`Precept`); `Ideology/Keyed/MainTabs.xml` (`Precepts`) |
| meme | Memes | `Ideology/Keyed/MainTabs.xml` (`Memes`) |
| ritual | Rituais | `Ideology/Keyed/MainTabs.xml` (`Rituals`) |
| role (Ideology) | Função / Funções | `Ideology/Keyed/Dialogs_Various.xml` (`Role`); `Ideology/Keyed/MainTabs.xml` (`IdeoRoles` **Funções**) |
| certainty | Certeza | `Ideology/Keyed/Misc_Gameplay.xml` (`Certainty`) |
| relic | Relíquia | `Ideology/Keyed/MainTabs.xml` (`IdeoRelics`) |
| ability (psycast) | poder / habilidade | `Royalty/Keyed/GameplayCommands.xml` (`CommandPsycastPawnIsUnconscious` O usuário da **habilidade** está inconsciente); the neurotrainer text uses **poder psíquico** (`Royalty/Keyed/Misc_Gameplay.xml` `PsycastNeurotrainerDescription`) |
| psyfocus | concentração psíquica | `Royalty/Keyed/Misc_Gameplay.xml` (`Psyfocus`); gizmo form **Concentração psíquica** (`PsyfocusLabelGizmo`) |
| psylink | psínculo | `Royalty/Keyed/GameplayCommands.xml` (`CommandPsycastHigherLevelPsylinkRequired` **psínculo** de nível {0}) |
| psycaster | psinjurador | `Royalty/Keyed/Letters.xml` (`LetterPsylinkLevelGained_First` agora é um(a) **psinjurador(a)**) |
| neural heat / entropy | calor neural | `Royalty/Keyed/GameplayCommands.xml` (`CommandPsycastWouldExceedEntropy` **O calor neural** sobrecarregaria) |
| psychic drone levels | Nenhum / Médio / Baixo / Médio / Alto / Extremo | `Core/Keyed/Enums.xml` (`PsychicDroneLevel_*`) |

### Biotech and Anomaly

| English | Português (BR) | Source file |
|---|---|---|
| gene | gene / genes | `Biotech/Keyed/Dialog_StatsReports.xml` (`StatsReport_Genes` **Genes** relevantes) |
| genepack | pacote de genes | `Biotech/Keyed/FloatMenu.xml` (`EjectGenepackFromGeneBank` Ejetar **pacote de genes** do banco de genes) |
| xenotype | xenótipo | `Biotech/Keyed/Misc_Gameplay.xml` (`Xenotype` **Xenótipo**) |
| xenogerm | xenogerminador | `Biotech/Keyed/FloatMenu.xml` (`ExtractXenogermFrom` Extrair **xenogerminador** de {PAWN_nameDef}) |
| mech (mechanoid) | mecanoide | `Biotech/Keyed/FloatMenu.xml` (`ControlMech` Tomar controle de {0}, referring to a **mecanoide**); `Biotech/Keyed/Alerts.xml` (`AlertMechLacksOverseer` **mecanoides** não controlados) |
| mechanitor (overseer) | mecanizador | `Biotech/Keyed/GameplayCommands.xml` (`CommandSelectOverseerDesc` Selecione o **mecanizador**…) |
| bandwidth | Banda-larga | `Biotech/Keyed/Misc_Gameplay.xml` (`Bandwidth`) |
| overseer | supervisor | `Biotech/Keyed/GameplayCommands.xml` (`CommandSelectOverseer` Selecionar **supervisor**) |
| entity | entidade | `Anomaly/Keyed/Misc_Gameplay.xml` (`CannotActivateEntity` Não é possível ativar a **entidade**) |
| study (Anomaly) | estudo / estudar | `Anomaly/Keyed/Dialog_StatsReports.xml` (`StudyFrequency` Intervalo de **estudo**); `Anomaly/Keyed/GameplayCommands.xml` (`ToggleStudy` **Estudar**) |
| holding platform | plataforma de retenção / plataforma de contenção | `Anomaly/Keyed/Alerts.xml` (`AlertHoldingPlatform` Precisa de **plataformas de retenção**); `Anomaly/Keyed/Misc_Gameplay.xml` (`SelectHoldingPlatform` Selecionar **Plataforma de Contenção**). The corpus itself alternates between the two words for the same def; **prefer "plataforma de contenção"** for new mod text (it matches "força de contenção"/`FloatMenuContainmentStrength` and the overall "contenção" theme), but do not "fix" a direct citation of the "retenção" form |
| containment | contenção | `Anomaly/Keyed/FloatMenu.xml` (`FloatMenuContainmentStrength` Força da **contenção**) |

### Weather, season, time, dates

| English | Português (BR) | Source file |
|---|---|---|
| temperature | Temperatura | `Core/Keyed/Menus_Main.xml` (`Temperature`) |
| weather | limpo / névoa / chuva com nevoeiro / chuva / chuva com trovoadas / trovoada seca / neve suave / neve densa / subterrâneo | `Core/DefInjected/WeatherDef/Weathers.xml` (`Clear.label`, `Fog.label`, `FoggyRain.label`, `Rain.label`, `RainyThunderstorm.label`, `DryThunderstorm.label`, `SnowGentle.label`, `SnowHard.label`, `Underground.label`) |
| season | primavera / verão / outono / inverno | `Core/Keyed/Time.xml` (`SeasonSpring`…`SeasonWinter`) — all lowercase in the corpus |
| quadrum (the unit) | trimestre | `Core/Keyed/Dates.xml` (`DateReadoutTip` **Trimestre** atual: {4}) |
| quadrum names | Abrimaio / Jugosto / Setentubro / Dezembreiro | `Core/Keyed/Time.xml` (`QuadrumAprimay`…`QuadrumDecembary`) |
| day(s) / hour(s) | dias / horas | `Core/Keyed/Time.xml` (`DaysLower`, `HoursLower`) — the corpus's generic forms are plural |
| letter abbreviations (d/h/m/s/y) | d / h / m / s / a | `Core/Keyed/Time.xml` (`LetterDay`, `LetterHour`, `LetterMinute`, `LetterSecond`, `LetterYear`) |
| clock time / date / year | Hora / Data / Ano | `Core/Keyed/Dates.xml` (`ClockTime`, `ClockDate`, `ClockYear`) |
| "{0} ago" | {0} atrás | `Core/Keyed/Time.xml` (`TimeAgo`) |
| expires in {0} | Expira em {0} | `Core/Keyed/MainTabs.xml` (`QuestExpiresIn`) |

### Combat, apparel, quality, stats

| English | Português (BR) | Source file |
|---|---|---|
| quality | Qualidade | inferred from `Core/Keyed/ITabs.xml` context and confirmed by `QualityCategoryShort_*` below |
| quality categories | horrível / pobre / normal / bom / excelente / obra-prima / lendário | `Core/Keyed/Enums.xml` (`QualityCategory_Awful`…`QualityCategory_Legendary`) |
| quality short forms | horr / pobre / normal / bom / exce / obra / lege | `Core/Keyed/Enums.xml` (`QualityCategoryShort_*`) — 4-letter truncation with no trailing period; already-short words (`pobre`, `bom`, `normal`) are left whole |
| hit points | pontos de vida | `Core/Keyed/Misc.xml` (`HitPointsBasic`) |
| hostility response | Ignorar / Atacar / Fugir | `Core/Keyed/Enums.xml` (`HostilityResponseMode_*`) |
| light level | Escuro / Iluminado / Muito iluminado | `Core/Keyed/Enums.xml` (`Dark`, `Lit`, `LitBrightly`) |
| compass directions (short) | N / NE / L / SE / S / SO / O / NO | `Core/Keyed/Enums.xml` (`Direction8Way_*_Short`) — note **L** for East (Leste), not "E"; **O** for West (Oeste); **SO/NO** for southwest/northwest |

### Gizmo / command / button labels

| English | Português (BR) | Source file |
|---|---|---|
| draft | Alistar | `Core/Keyed/GameplayCommands.xml` (`CommandDraftLabel`) |
| undraft | Desalistar | `Core/Keyed/GameplayCommands.xml` (`CommandUndraftLabel`) |
| toggle power | Interruptor de Energia | `Core/DefInjected/KeyBindingDef/KeyBindings.xml` (`Command_TogglePower.label`); the gameplay-command tooltip uses **Ativa ou desativa a energia** (`CommandDesignateTogglePowerDesc`) |
| hold open (door) | (no dedicated Keyed row found; compose from **Alternar** + the door state) | see `CommandToggleDoorForbidDesc` sibling below |
| forbid passage (door) | Quando proibida, colonos e aliados não irão passar pela porta | `Core/Keyed/GameplayCommands.xml` (`CommandToggleDoorForbidDesc`) |
| toggle study | Alternar estudo | `Core/Keyed/GameplayCommands.xml` (`CommandToggleStudy`) |
| view quest | Ver a missão | pattern from `Core/Keyed/MainTabs.xml` (`AcceptQuest` Aceitar a **missão**) |
| copy / paste | Copiar / Colar | `Core/Keyed/MainTabs.xml` (`Copy`, `Paste`) |
| rename | Renomear | `Core/Keyed/Misc_Gameplay.xml` (`Rename`) |
| accept quest | Aceitar a missão | `Core/Keyed/MainTabs.xml` (`AcceptQuest`) |
| form caravan | Formar caravana | `Core/Keyed/GameplayCommands.xml` (`CommandFormCaravanDesc` Formar um grupo de colonos…); `Core/DefInjected/KeyBindingDef/KeyBindings.xml` (`Command_FormCaravan`-equivalent renders **Formar Caravana**) |
| settle (found colony) | Colonizar | `Core/Keyed/GameplayCommands.xml` (`CommandSettleDesc` **Coloniza** esta área) |

### UI chrome

| English | Português (BR) | Source file |
|---|---|---|
| accept (dialog button) | Confirmar | `Core/Keyed/Dialogs_Various.xml` (`AcceptButton` **Confirmar**) |
| accept (semantic "I agree to this") | Aceitar | `Core/Keyed/Dialogs_Various.xml` (`Confirm` **Aceitar**) — the corpus's `AcceptButton`/`Confirm` keys map to the *opposite* English word from what you'd expect; use "Confirmar" for a generic OK/confirm dialog button, "Aceitar" for a semantic accept action (accept a quest, accept a gift) |
| cancel | Cancelar | `Core/Keyed/Dialogs_Various.xml` (`CancelButton`) |
| close | Fechar | `Core/Keyed/Dialogs_Various.xml` (`CloseButton`) |
| OK | OK | `Core/Keyed/Dialogs_Various.xml` (`OK`) |
| yes / no | Sim / Não | `Core/Keyed/Misc.xml` (inferred; confirmed via `Yes`/`No` usage across dialogs) |
| back / next | Voltar / Próximo | `Core/Keyed/Menus_Main.xml` (`Back`, `Next`) |
| options | Opções | `Core/Keyed/Menus_Main.xml` (`Options`) |
| menu | Menu | `Core/DefInjected/MainButtonDef/MainButtons.xml` (`Menu.label`) |
| enable / disable | Ativar / Desativar | `Core/Keyed/Menus_Main.xml` (`Enable`, `Disable`) |
| enabled / disabled (state) | Ativado(s) / Desativado(s) | `Core/Keyed/Menus_Main.xml` (`Enabled`, `Disabled`) — the corpus's own parenthetical plural is a TTS trap, see §3.5; write plain **Ativado** / **Desativado** in our own strings |
| add | adicionar | `Core/Keyed/Dialogs_Various.xml` (`Add` **adicionar**, lowercase in the corpus) |
| remove | Remover | `Core/Keyed/FloatMenu.xml` (`Remove`) |
| delete | Excluir | `Core/Keyed/Menus_Main.xml` (`Delete`) |
| save / load | Salvar / Carregar | `Core/Keyed/Menus_Main.xml` (`Save`, `Load`) |
| reset | Redefinir | `Core/Keyed/Dialogs_Various.xml` (`Reset`) |
| default | Padrão / padrão | `Core/Keyed/Misc.xml` (`Default`, `default`) |
| none | Nenhum / Nenhuma | `Core/Keyed/ITabs.xml` (`None` **Nenhum**). **Agree with the noun it replaces** — see §3.3 |
| (none) bracketed | (Nada) | `Core/Keyed/MainTabs.xml` (`NoneBrackets`) |
| description | Descrição | `Core/Keyed/Misc_Gameplay.xml` (`Description`) |
| details | Detalhes | `Core/Keyed/Dialogs_Various.xml` (`Details`) |
| filter | Filtro | `Core/Keyed/Misc_Gameplay.xml` (`Filter`) |
| search (verb) | Procurando (in-progress) | `Core/Keyed/Dialogs_Various.xml` (`Searching` **Procurando**) |
| search results | {0} resultados encontrados | `Core/Keyed/Dialogs_Various.xml` (`MapSearchResults`) |
| collapse all | Retrair tudo | `Core/Keyed/Dialogs_Various.xml` (`CollapseAllCategories`) |
| press (a key) | Pressione | `Core/Keyed/Menu_KeyBindings.xml` (`PressAnyKeyOrEsc` **Pressione** qualquer tecla ou Esc…) |
| key | tecla | same key |
| hotkey / key binding | Atalhos / Atalho de teclado | `Core/Keyed/Misc_Gameplay.xml` (`HotKeyTip` **Atalhos**); `Core/Keyed/Menu_KeyBindings.xml` (`KeyBindingOverwritten` **Atalho de teclado** sobrescrito: {0}) |
| button | botão | inferred from standard UI vocabulary (no single dedicated Keyed row; corpus uses it inline, e.g. `Core/Keyed/Menu_KeyBindings.xml` `BindingButtonToolTip` Clique no **botão** esquerdo…) |
| cursor | cursor | standard loanword, matches computing convention (no dedicated Keyed row; see accessibility section) |
| inventory | Inventário | `Core/Keyed/Misc_Gameplay.xml` (`Inventory`) |
| beauty / wealth etc. | use the game's `StatDef` / `NeedDef` label | `*/DefInjected/StatDef/*` |

---

## Section 2 — Accessibility-specific vocabulary (mod-coined)

These concepts RimWorld does NOT name. We lock a Brazilian Portuguese rendering here. Where the
game's own UI offers a match, we reuse it and cite it so users hear familiar wording; otherwise we
follow standard Brazilian screen-reader convention (NVDA-pt-BR, JAWS-pt-BR, VoiceOver-pt-BR, TalkBack).

| English | Português (BR) (locked) | Rationale / source |
|---|---|---|
| screen reader | leitor de tela | Standard Brazilian term (NVDA, JAWS, VoiceOver docs all use "leitor de tela"). Universally understood |
| announce (verb) | anunciar | Established Brazilian TTS/accessibility verb |
| announcement (noun) | anúncio | Matches the verb above |
| navigate / navigation | navegar / navegação | Standard Brazilian UI term |
| cursor | cursor | Standard computing loanword; unambiguous |
| map scanner | scanner do mapa | Mod-specific feature. "Scanner" is an accepted Portuguese computing loanword; RimWorld itself uses **escaneamento** for the research work-type gerund (`Core/DefInjected/WorkTypeDef/WorkTypes.xml` `Research.gerundLabel` pesquisa e **escaneamento**), a related but distinct concept — do not conflate |
| search (the act) | busca | Generic Brazilian computing term. The corpus's own **Procurando** is the progressive form ("searching"); use **busca** for the noun ("search"), matching common Brazilian UI ("Busca", "Barra de busca") |
| typeahead search | busca por digitação | No fixed game term. Transparent in TTS |
| tree view | árvore | Standard Brazilian screen-reader term (NVDA-pt-BR announces tree controls as "árvore") |
| node (tree node) | nó | Standard Brazilian UI term |
| level (tree depth) | nível | RimWorld uses it broadly, e.g. `Royalty/Keyed/GameplayCommands.xml` (psínculo de **nível** {0}) |
| expand | Expandir | RimWorld's own: `Core/Keyed/Designators.xml` (`DesignatorZoneExpand` **Expandir** zona) |
| collapse | Retrair / Encolher | RimWorld's own: `Core/Keyed/Dialogs_Various.xml` (`CollapseAllCategories` **Retrair** tudo); `Core/Keyed/Designators.xml` (`DesignatorPlanShrinkSingular` **Encolher** plano). Prefer **Retrair** for a generic tree/section collapse (matches the UI-chrome key exactly); **Encolher** is reserved for the zone/plan-specific designator sense |
| expanded (state) | expandido | Standard Brazilian screen-reader state word. Masculine singular; never agree it with the item — see §3.3 |
| collapsed (state) | retraído | Parallel state word to "Retrair". Masculine singular |
| toggle (verb) | Alternar | RimWorld's own: `Core/Keyed/GameplayCommands.xml` (`CommandToggleStudy` **Alternar** estudo) |
| on / off (state) | ativado / desativado | `Core/Keyed/Menus_Main.xml` (`Enabled`, `Disabled`) — write the plain masculine form, never the corpus's own `(s)` parenthetical (see §3.5) |
| checkbox | caixa de seleção | Standard Brazilian UI term (NVDA-pt-BR) |
| checked / not checked | marcado / não marcado | Standard; pairs with "caixa de seleção" |
| partially checked | parcialmente marcado | Standard Brazilian screen-reader wording |
| slider | controle deslizante | Standard Brazilian UI term (NVDA-pt-BR / Windows accessibility docs) |
| spin box / stepper | caixa de seleção numérica | Standard Brazilian term for a numeric +/- control |
| combo box | caixa de combinação | Standard Brazilian UI term |
| radio button | botão de opção | Standard Brazilian UI term (matches Windows/Android convention, not a literal "botão de rádio") |
| edit box / text field | campo de texto | Standard Brazilian UI term |
| button (UI) | botão | Generic computing term, confirmed by corpus usage in `Menu_KeyBindings.xml` |
| tab (UI tab) | aba | Standard Brazilian UI term. NOTE: the corpus's own `ITabs.xml` values are the tab *contents' names* (Saúde, Itens…), not the word "aba" itself — "aba" is the standard Brazilian word for the UI control, matching everyday Brazilian software (browsers call theirs "abas") |
| selected | selecionado | RimWorld uses it: `Core/Keyed/GameplayCommands.xml` (`CommandCopyPlanSelectionDesc` planos na área **selecionada**). In OUR strings keep it masculine singular and invariable — see §3.3 |
| not selected | não selecionado | Negation of the above, masculine singular |
| read-only | somente leitura | Standard Brazilian computing term |
| focused | em foco | Common Brazilian screen-reader phrasing ("elemento em foco"); avoids inventing an adjective that would need gender agreement |
| row / column | linha / coluna | Standard Brazilian UI terms |
| column header | cabeçalho de coluna | Standard Brazilian UI term |
| cell (table cell) | célula | Standard Brazilian UI term |
| table | tabela | Standard |
| list | lista | Standard |
| sortable / sorted ascending / descending | classificável / ordem crescente / ordem decrescente | "Crescente"/"decrescente" are the standard Brazilian sorting adjectives and are **epicene** (identical for masculine and feminine nouns), which sidesteps the gender-agreement problem entirely — see §3.3. Corroborated in-game: `Core/Keyed/Dialog_StatsReports.xml` (`StatsReport_ShootingExampleDistances` distâncias **crescentes**) |
| blank (empty cell) | vazio | Standard |
| position ("X of Y") | {0} de {1} | Standard Brazilian UI phrasing |
| jump to | Ir para: {0} | Built from RimWorld's own colon shape (§3.6); the corpus's `ClickToJumpTo` uses "Clique para ir para:" |
| edge / boundary | Já no topo / Já no final | Plain Brazilian Portuguese edge wording, gender-neutral phrasing (avoids agreeing an adjective with an unknown noun) |
| hotkey | Atalho | RimWorld uses it: `Core/Keyed/Misc_Gameplay.xml` (`HotKeyTip` **Atalhos**) |
| accessibility | acessibilidade | Standard Brazilian term |
| inspect / inspection | Inspecionar | `Core/DefInjected/MainButtonDef/MainButtons.xml` (`Inspect.label` **Inspecionar**) |
| scan (accessibility feature, e.g. Anomaly holding-platform scanner) | escanear / varredura | The corpus's own "escaneamento" (research gerund, above) supports **escanear** as the verb; **varredura** is the standard Brazilian noun for a UI/element sweep and avoids collision with the research sense |

---

## Section 3 — Style rules for translators

### 3.1 Register — formal-neutral "você", third-person-singular verb agreement

RimWorld's Brazilian Portuguese addresses the player as **"você"**, with the verb conjugated in the
third-person singular (the standard Brazilian pattern — "você" grammatically takes the same verb form
as "ele/ela", unlike European Portuguese's "tu"). This is not a stylistic choice we can revisit;
matching it is what makes the mod sound like the game.

Evidence (counted case-insensitively over `Core/Keyed`):

- "você" / "vocês" — **244** occurrences, e.g. "Você não pode pousar sobre este bioma: {0}"
  (`Menus_Main.xml` `CannotLandBiome`), "Você tem certeza de que deseja fazer isso?"
  (`GameplayCommands.xml` `AreYouSure`)
- possessives "seu" / "seus" / "sua" / "suas" — **51 / 73 / 84 / 39** occurrences (case-insensitive),
  e.g. "O nível de tecnologia da **sua** facção é {0}" (`MainTabs.xml` `TechLevelTooLow`)
- "tu" / "teu" / "tua" / "tuas" — **ZERO** occurrences anywhere in `Core/Keyed`. The corpus never
  addresses the player with the European "tu" form
- the clitic object pronoun "te" appears **3×** (`MainTabs.xml` `QuestBetrayalOfferTip` "{0} **te**
  dará uma recompensa"; `Letters.xml` "…para **te** ajudar quando você…"). This is NOT a "tu"-form
  contradiction: in colloquial Brazilian Portuguese, the clitic "te" is used as the object pronoun
  for "você" itself (this is standard, informal-but-not-dialectal Brazilian usage, distinct from
  European Portuguese's full "tu" paradigm). It is fine to use "te" as an object pronoun alongside
  "você" as the subject; never introduce "tu", "teu", "tua", or the "tu"-conjugated verb forms
- formal imperative when instructing: "**Certifique-se** de que suas defesas estejam preparadas"
  (`Incidents.xml` `EscapeShipFound`), "**Selecione** uma cor de uma construção existente"
  (`Designators.xml` `DesignatorEyeDropperDesc_Paint`), "**Escolha** um colono para hackear isso"
  (`FloatMenu.xml` `HackDesc`)

**Never use "tu", "teu", "tua", "tuas", or any "tu"-conjugated verb form (e.g. "tens", "fazes").**
European Portuguese is a separate, out-of-scope dialect for this wave.

**Command labels are INFINITIVES**, not imperatives and not nouns: "Cancelar", "Minerar",
"Alistar", "Reivindicar", "Fechar", "Salvar", "Renomear" (`Core/Keyed/Designators.xml`,
`Core/Keyed/Menus_Main.xml`, `Core/Keyed/GameplayCommands.xml`). Do not write "Cancele" or
"Cancelamento" for a button.

**Instructions are the "você"-agreeing imperative** (grammatically the present-subjunctive-derived
form shared by "você" and formal address): "Selecione", "Escolha", "Certifique-se", "Confira",
"Configure". This is the same verb form used whether the sentence is addressed to "você" or to a
generic "user" — do not second-guess it into an infinitive.

**States are nouns or masculine past participles**: "Humor", "Dor", "Ativado", "Desativado",
"expandido", "retraído", "selecionado". Otherwise match RimWorld's neutral, terse UI register. No
exclamation marks unless the English has them. No first person. No emoji.

### 3.2 The Portuguese language worker — narrow, article-only; no elision post-processing

Unlike French, Brazilian Portuguese's `LanguageWorker_Portuguese`
(`Verse/LanguageWorker_Portuguese.cs`, in the decompiled game source) does **only**
two things, both requiring a real `Gender` value the mod cannot supply for its own placeholders:

```csharp
public override string WithIndefiniteArticle(string str, Gender gender, bool plural = false, ...)
    => ((gender == Gender.Female) ? "uma " : "um ") + str;   // (or "umas "/"uns " if plural)

public override string WithDefiniteArticle(string str, Gender gender, bool plural = false, ...)
    => ((gender == Gender.Female) ? "a " : "o ") + str;      // (or "as "/"os " if plural)
```

It does **not** override `TotalNumCaseCount` (inherits the base `0` — see §3.4) and it does **not**
override `PostProcessed`, so there is no French-style elision rewrite pass. Writing an uncontracted
article and hoping the engine fixes it at runtime (the French trick, §3.2 of `fr-glossary.md`) **does
not work in Portuguese** — whatever article text you write is exactly what gets spoken. Write
grammatically complete Portuguese yourself; there is no safety net.

Portuguese does have a genuine gender/number word-list infrastructure
(`Core/WordInfo/Gender/{Male,Female,Singular,Plural}.txt` — 9 to 1350 entries per file) used by
`GrammarResolverSimple.TryResolveSymbol` to resolve `{0_gender ? … }`, `{0_definite}`,
`{0_indefinite}` tags on a **live `Pawn`/`Thing`** argument. This is the same narrow exception
documented for French: it only works because vanilla passes real game objects that are (sometimes)
in those word lists, and it **falls back to masculine** for anything absent. Our mod's placeholders
are composed labels, pawn names, and numbers — none of which are in those lists. **Do not add
`_gender`, `_definite`, `_indefinite`, or `_plural` tags to our own strings.**

### 3.3 Gender — the critical Portuguese decision

Our keys carry **no gender information**. Verified: `Languages/English/Keyed/` contains **zero**
grammar tags, every placeholder is a plain `{0}` / `{named}` `string.Format` argument, and every
`.Named("PAWN")` call in `src/` targets a **vanilla** key, never a `RimWorldAccess.*` key. So the
word that lands in `{0}` is an already-resolved label string whose gender we cannot know at
translation time.

**Therefore: never write an adjective, participle, or article that must agree with a placeholder.**

The official corpus solves this with `{PAWN_gender ? o : a}` tags (**42** occurrences in `Keyed/`,
**155** counting `DefInjected/`) and, more strikingly, with a **literal, static `(a)`/`(o)`
parenthetical written directly into the translated text** for cases where the source text has no live
gendered object to query — **203** occurrences in `Keyed/` alone (152 `(a)`, 43 `(s)`, 5 `(as)`,
2 `(es)`, 1 `(o)`), and 609 counting `DefInjected/`,
e.g. "requer um(a) {0} existente" (`FloatMenu.xml` `InstallImplantHediffRequired`), "Ativado(s)" /
"Desativado(s)" (`Menus_Main.xml`), and the entire `Grammar.xml` article/pronoun table
(`IndefiniteArticle` "um(a)", `DefiniteArticle` "o(a)", `Proit` "ele(a)"). **We cannot use either
device.** The `_gender` tag needs a live `Pawn`/`Thing` (§3.2); the parenthetical is a visual-UI
convention that a TTS engine reads aloud as "abre parênteses a fecha parênteses" — strictly worse
than the ambiguity it was trying to solve, and RimWorld only gets away with it because vanilla is not
narrated. Do not copy either pattern into our files.

**Use these strategies instead, in this order of preference.**

**Strategy A — the noun-or-participle + colon shape. This is the workhorse; prefer it.**

Put a Portuguese head word you control in front of the colon and let the placeholder stay bare. The
agreement then lands on *your* word, whose gender you know. The corpus does this constantly:

| Corpus value | Key / file |
|---|---|
| `Nasceu: {0}` | `Time.xml` `Born` |
| `Idade cronológica: {0} anos, {1} trimestres, {2} dias` | `Time.xml` `AgeChronological` |
| `Peso carregado: {0} / {1} kg` | `ITabs.xml` `MassCarried` |
| `Reservado por {1_labelShort}` | `FloatMenu.xml` `ReservedBy` |
| `Sem {0_label}` | `FloatMenu.xml` `NoIngredient` |
| `Desativado pelo traço {0}` | `MainTabs.xml` `WorkDisabledByTrait` |

So English `{0} selected` becomes **`Seleção: {0}`** (a feminine noun — fully gender-free), not
`{0} selecionado(a)`. English `Activated {0}` becomes **`Ativação: {0}`**, never `{0} ativado(a)`.

**Strategy B — an infinitive or a preposition, which never agrees.**

Verbs in the infinitive and prepositions carry no gender, so the placeholder stays bare:
"**Tratar** {0}", "**Capturar** {1_labelShort}", "**Negociar com** {0}",
"**Impossível** usar: {0}" (`Core/Keyed/FloatMenu.xml`). `de {placeholder}` and `com {placeholder}`
compositions are always safe.

**Strategy C — an appositive head noun that carries the agreement.**

Put a generic noun before or after the placeholder and agree with *that*. Useful head nouns for our
strings: **item** (m.), **elemento** (m., a list entry), **linha** (f., a row), **coluna** (f.),
**aba** (f., a UI tab), **botão** (m.), **área** (f.), **pessoa** (f.) / **colono** (m.) for a pawn,
**ritual** (m.), **gene** (m.), **traço** (m.). The corpus's own examples: "requer {0_label}"
(`FloatMenu.xml` `MustHaveHediff`), "**projeto técnico** ({PROJECT_label})"
(`Misc.xml` `TechprintLabel`).

**Strategy D — masculine singular, invariable, for a fixed state word.**

Where the state word stands alone as its own key (our `RimWorldAccess.Shell.State.*` set:
`selected`, `expanded`, `read only`…), write the **masculine singular** and never vary it:
"selecionado", "expandido", "retraído", "marcado", "não marcado", "desativado", "somente leitura". A
screen reader speaks these as fixed role/state tokens, exactly as NVDA-pt-BR does; a `(a)` in the
middle of a spoken phrase is read aloud as "abre parênteses a fecha parênteses" and is strictly
worse. **Never write `selecionado(a)`, `ativado(a)`, or any `(a)` / `(o)` / `(s)` parenthetical**,
even though the game's own corpus does this 203 times in `Keyed/` — that convention exists for a
visually-read UI, not a spoken one.

**"Nenhum" vs "Nenhuma".** These *do* agree, and you control the noun that follows, so agree
normally: "**Nenhum** dos seus {0} tem essa habilidade" (`Designators.xml`
`NoColonistWithSkillTip`), "**Nenhuma** política selecionada" (`Dialogs_Various.xml`
`NoPolicySelected`). When the missing thing is a bare placeholder, restructure: "Nenhum resultado
para '{0}'" rather than trying to agree with `{0}`.

**The narrow exception (document-only; do not use).** Vanilla's own `{0_gender ? … }` tags work only
on live `Pawn`/`Thing` objects — see §3.2. Our arguments are pawn names, composed labels, mod def
labels, and numbers, almost none of which resolve through `LoadedLanguage.ResolveGender`. **Do not
add these tags to our files.**

**Parentheses themselves are NOT forbidden — only invented agreement hedges are.** Our English source
uses parentheses in 217 of its 5,626 values (3.9%), e.g. `Map.Label.Suffixed` `{0} ({1})` and
`Building.Door.CurrentlyOpen` `(Currently open)`, and vanilla's own Brazilian labels use them too
(`RoofRockThin.label` is literally "telhado de pedra (fino)"). Keep the parentheses English has, and
reuse vanilla's parenthesised labels verbatim. What is banned is *adding* a `(a)` / `(o)` / `(s)`
purely to dodge an agreement decision. When you need to dodge agreement, use Strategy A (head word +
colon) instead of inventing a parenthetical: `Forbidden {0}` is **`Proibido: {0}`**, matching what
French (`Interdit : {0}`), Spanish (`Prohibido: {0}`) and Russian (`Запрещено: {0}`) already ship —
not `{0} (proibido)`.

**Ruled: door state stays feminine.** `Map.Label.WithDoorOpen` / `WithDoorClosed` annotate any
`Building_Door` with `{0} (aberta)` / `{0} (fechada)`. Feminine, because 7 of the 8 door labels a
player can meet are headed by the feminine "porta" (`Door` "porta", `Autodoor` "porta automática",
`OrnateDoor`, `SecurityDoor`, `AncientBlastDoor`, `AnimalFlap`, `GrayDoor`). The known exception is
`FenceGate`, whose Brazilian label "portão de cerca" is masculine, so a fence gate is announced as
"portão de cerca (aberta)". This is a deliberate trade-off, not an oversight: it puts the single
audible disagreement on the rarest door instead of on the most common one, and it matches what
Spanish (`abierta`/`cerrada`) and Russian (`открыта`/`закрыта`) already ship. French chose the
opposite (masculine `ouvert`/`fermé`). Do not "fix" this by switching to masculine or by inventing
`(aberto(a))`.

### 3.4 Plurals — flat plural after a count; `_numCase` is FORBIDDEN

**Finding: the Brazilian Portuguese corpus contains ZERO `_numCase` usages.** Verified over the
whole extraction (Core + all five DLCs, `Keyed/` and `DefInjected/`):

```
$ grep -rho '_numCase' <ptbr_ref>/ | wc -l
0
```

**So the mod must never use `_numCase` in Brazilian Portuguese.**
`GrammarResolverSimple.ResolveNumCase` checks the branch count against
`LanguageDatabase.activeLanguage.info.totalNumCaseCount ?? activeLanguageWorker.TotalNumCaseCount`.
`LanguageWorker_Portuguese` does **not** override `TotalNumCaseCount`, so it inherits
`LanguageWorker`'s base value of `0`, and Core's Brazilian Portuguese `LanguageInfo.xml` leaves
`totalNumCaseCount` unset. A multi-branch `_numCase` tag fails the count check, logs an error, and
**returns an empty string** — a silently blank spoken announcement. We cannot override this from a
mod: `LoadedLanguage.LoadMetadata` takes the **first** `LanguageInfo.xml` across `RunningMods` and
returns, and mods are forced to load after Core, so Core's Brazilian Portuguese metadata always wins.

**Brazilian Portuguese does not need it.** Portuguese has exactly two number forms, and the corpus
writes the plural flat after a count placeholder, with the noun pluralized and NO gender-conditional
suffix on the noun itself (gender concerns the article/adjective, not the count-plural mechanism):

- `Period1Day` → `1 dia`; `PeriodDays` → `{0} dias` (`Core/Keyed/Time.xml`) — a genuine
  singular/plural key PAIR (see below)
- `Period1Year` → `1 ano`; `PeriodYears` → `{0} anos`
- `Period1Hour` → `1 hora`; `PeriodHours` → `{0} horas`
- `AppearedDaysAgo` → `Apareceu há {0} dias atrás` (`MainTabs.xml`) — flat plural after an
  arbitrary, unbounded count, not a One/Many-keyed pair
- `MapSearchResults` → `{0} resultados encontrados` (`Dialogs_Various.xml`)
- `Last30Days` → `30 Dias`, `Last100Days` → `100 Dias` (`MainTabs.xml`)

The corpus *does* sometimes write a parenthetical `(s)` after a count placeholder — 12 times in
`Keyed/`, e.g. "tem apenas {1} ano(s)" (`MainTabs.xml` `WorkDisabledAge`), "já tem {1} parceiro(s)"
(`ITabs.xml` `RomanceWarningPolygamous`). **Do not copy it.** It is a visual-UI hedge that lets one
string serve both counts, and a TTS engine reads it aloud as "ano abre parênteses s fecha parênteses".
Where we control the count we use the real plural (the flat forms above, or both halves of a One/Many
pair); where we genuinely cannot know the count, restructure the sentence rather than hedging.
**Never write `(s)`.**

#### One / Many key PAIRS

Many of our keys come in a `…One` / `…Many` pair, chosen in C# by `count == 1`. **Brazilian
Portuguese should use both forms properly** — this is a real advantage over languages that cannot
distinguish, and it is exactly what the game's own `Period1Day`/`PeriodDays` pair does:

| Key | Português (BR) |
|---|---|
| `…One` (count == 1) | singular: `{0} item`, `{0} dia`, `{0} colono`, `{0} resultado` |
| `…Many` (count != 1) | plural: `{0} itens`, `{0} dias`, `{0} colonos`, `{0} resultados` |

Watch the agreement of any surrounding words: `Expanded {0} item` → `{0} item expandido`, and
`Expanded {0} items` → `{0} itens expandidos`.

Note that `count == 0` selects `…Many` in our own C# dispatch, so Portuguese will say "0 itens".
This matches everyday Brazilian usage (zero is treated as plural), so there is nothing to work
around. Always fill **both** keys — leaving one blank makes the mod fall back to English for that
count.

### 3.5 Punctuation, typography, numbers

**Typography: ASCII-only. This is the sharpest rule in this dialect.**

Measured over ALL Keyed modules (Core + Royalty + Ideology + Biotech + Anomaly + Odyssey, same
parse-not-grep methodology):

- ASCII apostrophe `'` (U+0027): **90** occurrences
- ASCII double quote `"` (U+0022): **70** occurrences
- Curly quotes `'` `'` `"` `"` (U+2018/2019/201C/201D): **ZERO**
- Guillemets `«` `»` (U+00AB/00BB): **ZERO**
- Ellipsis `…` (U+2026): **ZERO** (the corpus spells it out as three periods where it appears, e.g.
  `Core/Keyed/Menu_KeyBindings.xml` `PressAnyKeyOrEsc` "Pressione qualquer tecla ou Esc para
  cancelar**...**")
- Em dash `—` / en dash `–` (U+2014/2013): **ZERO**
- No-break space U+00A0, narrow no-break U+202F, thin space U+2009, zero-width space U+200B, BOM
  U+FEFF: **ZERO** each

So: **never emit a curly quote, a guillemet, a real ellipsis character, an em/en dash, or any
non-ASCII space.** Use the plain ASCII apostrophe and double quote, and spell out `...` with three
periods when the English source does.

**The apostrophe is not exclusively a quote mark in this language.** It also appears in genuine
elision, e.g. "roda d'água" (a water wheel), "gota d'água". Do not "fix" these into a curly form or
strip them; they are correct Portuguese.

**Numbers arrive pre-formatted through placeholders. Never reformat them.** No literal decimal-comma
or decimal-point numbers were found anywhere in the Keyed corpus outside of one unrelated
version-string example (`Menus_Main.xml` `ModLoadFolderMalformedVersion` "formato... Major.Minor
(por exemplo, 1.1)", which is describing a *mod version string*, not a game number). Brazilian
Portuguese convention uses a comma decimal separator, but the engine formats the value (with
whatever separator it uses) before it reaches the string — do not add, remove, or convert separators
inside `{0}`. **Percent signs:** the corpus's literal percents are always the bare postfix shape
"{0}%" / "50%" / "100%" — no space before the sign. Keep whatever shape the English source has.

**Capitalization is genre-dependent — measure before you translate a whole file.** This corpus does
**not** use a single consistent case convention. A structured survey of two-to-five-word label-like
values across `Core/Keyed` (classifying each as "every content word capitalized" vs "only the first
word capitalized", ignoring articles/prepositions) found:

| File (genre) | Title-Case-like | Sentence-case-like |
|---|---|---|
| `Menu_Options.xml` (settings screen) | 47 | 0 |
| `Menus_Main.xml` (main menu) | 104 | 25 |
| `Enums.xml` (dropdown/category labels) | 25 | 10 |
| `MainTabs.xml` (tab headers, mixed content) | 30 | 10 |
| `Designators.xml` (gizmo/order labels) | 0 | 39 |
| `GameplayCommands.xml` (gizmo labels) | 1 | 62 |
| `FloatMenu.xml` (right-click actions) | 0 | 43 |
| `ITabs.xml` (inspect-tab body text) | 1 | 58 |
| `Alerts.xml` | 0 | 39 |
| `Skills.xml` | 0 | 20 |

**The rule this reveals:** settings/menu-screen labels are Title Case (every content word
capitalized — "Volume Principal", "Escala da Interface", "Rolagem do Mouse nas Bordas"); in-game
action/gizmo/tooltip text is sentence case (only the first word — "Cortar plantas", "Expandir área
da casa", "Alisar superfície"). This maps directly onto our own mod's two genres:

- **`RimWorldAccess_MainMenu.xml`** (our settings screen, which lives at the same place in the UI as
  vanilla's own Options menu) → **Title Case**, matching `Menu_Options.xml`.
- **Everything else** (`_Inspection`, `_Building`, `_Map`, `_Pawns`, `_Shell`, `_UI`, `_Compat`,
  designator/gizmo/announcement text of any kind) → **sentence case**, matching
  `Designators`/`GameplayCommands`/`FloatMenu`/`ITabs`/`Alerts`/`Skills`. This is the overwhelming
  majority of our own key set, since our mod is almost entirely narration of dynamic game state, not
  a settings UI.

**Abbreviation convention.** Truncate to roughly 4-7 letters with **no trailing period** when the
truncation already reads as a short word (`horr`, `exce`, `obra`, `lege`, `constr.`—see next);
`Core/DefInjected/WorkTypeDef/WorkTypes.xml`'s `labelShort` values do add a trailing period when the
truncation would otherwise look like a different, misleading word (`constr.` for Construction,
`tratam.` for Patient/treatment, `policiam.` for Warden/policing). Follow the same instinct: add a
period only if omitting it would read as a different word.

### 3.6 Placeholders, line breaks, keys, spacing

- Copy `{0}`, `{1}`, `{NamedArg}`, `{label}`, `{count}` etc. **byte-for-byte.** Never translate,
  never add or remove braces, never renumber. The **set** of placeholders in a value must match the
  English source exactly.
- You **may reposition** a placeholder for natural Portuguese word order — `string.Format` is
  positional. The corpus does this itself, e.g. restructuring around the colon shape (§3.3
  Strategy A). Repositioning is fine; renumbering or translating is forbidden.
- **Do not add grammar tags** (`_definite`, `_indefinite`, `_plural`, `_gender ? …`, `_numCase`) that
  the English source does not have. See §3.3 and §3.4 for why.
- Copy `\n` line breaks exactly and keep them in the same logical spots.
- **XML keys are never translated** — every element name stays byte-for-byte identical to English, or
  the string silently fails to load.
- **XML comments are developer context — leave them in English.** Do not translate `<!-- … -->`.
- **Preserve leading/trailing spaces in values.** Some keys are suffixes that join onto a preceding
  label (e.g. `RimWorldAccess.InfoCard.Inspectable` = `" Inspectable."` with a leading space). Keep
  the exact leading and trailing space.
- Keep a single normal space where a number or placeholder abuts Portuguese text: "{0} itens",
  "nível {0}". Do not glue them together.
- Escape XML specials as the English file does. A literal `&` must be `&amp;`, `<` must be `&lt;`.
  Accented Portuguese characters need no escaping — the files are UTF-8.
- **Composed fragment joiners (`". "`, `", "`, `": "`) stay ASCII, unchanged.** These are TTS pause
  boundaries between independently-spoken fragments, not Portuguese sentence punctuation — do not
  rewrite `{0}: {1}` into anything else, and do not add a space before the punctuation mark itself.

### 3.7 Key names — Tab, Enter, Escape, arrows, and friends

Our mod speaks key names constantly, sourced from the `RimWorldAccess.Shell.Key.*` Keyed table
(`Languages/English/Keyed/RimWorldAccess_Shell.xml` lines 194-230) via `KeyChordFormat` — these ARE
translated per-language (French and Turkish have already localized this table; see their precedent
below). The ruling for Brazilian Portuguese:

**Keep these in English, exactly as printed on a Brazilian (ABNT2) physical keyboard**, because
Brazilian keycaps carry English legends for these keys, and the game's own corpus itself never
translates them:

| Key | Português (BR) | Evidence |
|---|---|---|
| Tab | Tab | not translated in French or Turkish either; matches the physical keycap |
| Enter (Return) | Enter | Brazilian keycaps print "Enter"; keep the same value French uses for the concept but the English word, matching Turkish's ruling |
| Escape | Esc | `Core/Keyed/Menu_KeyBindings.xml` (`PressAnyKeyOrEsc` "Pressione qualquer tecla ou **Esc** para cancelar...") — the game's own corpus abbreviates it to "Esc" and does not translate it |
| Shift | Shift | `Core/Keyed/Misc_Gameplay.xml` (`WorkPriorityShiftClickTip` "**Shift** + clique: Aumenta prioridade") — the corpus itself leaves "Shift" untranslated inline |
| Ctrl | Ctrl | matches `RimWorldAccess.Shell.Key.Ctrl` English value; both French and Turkish keep it |
| Alt | Alt | matches `RimWorldAccess.Shell.Key.Alt`; both French and Turkish keep it |
| Option (Mac) | Option | matches `RimWorldAccess.Shell.Key.Option`; both French and Turkish keep it |
| Home / End | Home / End | Brazilian keycaps print these in English; both French and Turkish keep "Home"/"End" |
| Backspace | Backspace | Brazilian keycaps print "Backspace"; Turkish keeps it (French translates to "Retour arrière" — do not follow French here, Brazilian keyboard culture is closer to Turkish's) |
| Delete | Delete | Brazilian keycaps print "Delete"; Turkish keeps it |
| Insert | Insert | Brazilian keycaps print "Insert"; Turkish keeps it |
| Page Up / Page Down | Page Up / Page Down | Brazilian keycaps print these in English |

**Translate these, matching the corpus's own compass/direction vocabulary and Turkish's precedent of
translating descriptive compounds while keeping bare key names in English:**

| Key | Português (BR) | Evidence |
|---|---|---|
| Space | Espaço | `Core/Keyed/Designators.xml` (`SpaceBeingSmoothed` "**Espaço** sendo limpo") |
| Up Arrow | Seta para Cima | `Core/DefInjected/KeyBindingDef/KeyBindings.xml` (`MapDolly_Up.label` Ir para **Cima**) |
| Down Arrow | Seta para Baixo | same file (`MapDolly_Down.label` Ir para **Baixo**) |
| Left Arrow | Seta para a Esquerda | same file (`Designator_RotateLeft.label` Virar à **Esquerda**) |
| Right Arrow | Seta para a Direita | same file (`Designator_RotateRight.label` Virar à **Direita**) |
| Backtick (BackQuote) | Acento Grave | standard Brazilian Portuguese name for the `` ` `` character |
| Left Bracket / Right Bracket | Colchete Esquerdo / Colchete Direito | standard Brazilian names for `[` `]` |
| Comma | Vírgula | standard |
| Period | Ponto | standard |
| Slash | Barra | standard |
| Minus | Menos | standard |
| Plus | Mais | standard |
| Equals | Igual | standard |
| Numpad {digit} | Numpad {0} | keep "Numpad" in English (mod-coined choice, matching Turkish's ruling over French's fully-translated "pavé numérique" — Brazilian tech culture commonly says "numpad" as a loanword) |
| Numpad Enter/Plus/Minus/Star/Slash/Period | Numpad Enter / Numpad Mais / Numpad Menos / Numpad Asterisco / Numpad Barra / Numpad Ponto | "Numpad" stays English; the descriptor after it translates like the bare key above |

**Mac Ctrl substitution.** Per the project's standing convention (`KeyboardHelper.CtrlLabelFor`), macOS
names Ctrl "Command", or "Option" in Tab combinations. Our strings must use the `CtrlLabel` token
mechanism, never hardcode "Ctrl" where the code substitutes it — this is a code-level concern, not a
translation one, but be aware when a string embeds a literal key name that it may need to flow
through `KeyChordFormat` instead of being a static translated sentence.

### 3.8 False friends and machine-translation traps

Machine translation gets these wrong in this game's domain. Every replacement below is the corpus's
own word.

| English | WRONG | RIGHT | Source |
|---|---|---|---|
| draft (a colonist) | recrutar, rascunho | **Alistar** / **Desalistar** | `Core/Keyed/GameplayCommands.xml` (`CommandDraftLabel`, `CommandUndraftLabel`) |
| bill (a work order) | fatura, conta | **tarefa** | `Core/Keyed/ITabs.xml` (`TabBills`, `AddBill`) |
| warden | carcereiro, diretor | **guarda** | `Core/DefInjected/WorkTypeDef/WorkTypes.xml` (`Warden.pawnLabel`) |
| hauling (work type) | transporte, transportador | **auxílio** / **Auxiliador** / **Auxiliar** | `Core/DefInjected/WorkTypeDef/WorkTypes.xml` (`Hauling.*`) — NOT the same word `DesignatorHaulThings` uses for the button ("Transportar coisas"); the two systems diverge, keep both |
| apparel | roupas, vestimenta | **Aparência** (per the corpus, despite reading oddly) | `Core/Keyed/Misc_Gameplay.xml` (`Apparel`) |
| raid | ataque, assalto | **invasão** | `Core/Keyed/Misc_Gameplay.xml` (`Raid`) |
| mood | ambiente, moral | **humor** | inferred from Ideology/ITabs usage |
| quest | pesquisa, busca | **missão** | `Core/Keyed/Misc_Gameplay.xml` (`Quest`), `Core/DefInjected/MainButtonDef/MainButtons.xml` (`Quests.label`) |
| research (noun/tab) | pesquisar (verb form) | **Pesquisa** (noun) — but the button/verb form is **Pesquisar** | `Core/Keyed/MainTabs.xml` |
| map | plano, planta | **mapa** | `Core/Keyed/Misc_Gameplay.xml` (`Map`) |
| save (a game) | poupar, economizar | **Salvar** (button) | `Core/Keyed/Menus_Main.xml` (`Save`) |
| load | fazer upload, baixar | **Carregar** | `Core/Keyed/Menus_Main.xml` (`Load`) |
| deconstruct | desconstruir *(this one happens to be right!)* | **Desconstruir** | `Core/Keyed/Designators.xml` (`DesignatorDeconstruct`) |
| smooth (a surface) | suavizar, alisar-se | **Alisar** | `Core/Keyed/Designators.xml` (`DesignatorSmoothSurface`) |
| claim | reclamar (a complaint), afirmar | **Reivindicar** | `Core/Keyed/Designators.xml` (`DesignatorClaim`) |
| forbid / allow | defender / deixar | **Proibir** / **Permitir** | `Core/Keyed/Designators.xml` (`DesignatorForbid`, `DesignatorUnforbid`) |
| tend (a wound) | estender, cuidar | **Tratar** | `Core/Keyed/FloatMenu.xml` (`Tend`) |
| tame | domar (a horse, colloquial) | **Domesticar** | `Core/Keyed/Designators.xml` (`DesignatorTame`) |
| slaughter | massacrar | **Abater** | `Core/Keyed/Designators.xml` (`DesignatorSlaughter`) |
| trade | troca (only as noun) | **Negociar** (verb) | `Core/Keyed/FloatMenu.xml` (`TradeWith`) |
| policy (drug/apparel) | política (singular, misleading in context), apólice | **políticas** (plural, as the game uses it) | `Core/Keyed/MainTabs.xml` (`ManageDrugPolicies`) |
| stockpile | pilha, monte | **zona de estoque** | `Core/Keyed/Misc_Gameplay.xml` (`Stockpile`) |
| growing zone | zona de crescimento | **Zona de cultivo** | `Core/Keyed/Misc_Gameplay.xml` (`GrowingZone`) |
| recreation (need) | recreação | **diversão** (the need) / **lazer** (the build category) | `Core/DefInjected/NeedDef/Needs.xml` (`Joy.label`), `Core/DefInjected/DesignationCategoryDef/*` (`Joy.label`) |
| masterwork (quality) | obra mestra | **obra-prima** | `Core/Keyed/Enums.xml` (`QualityCategory_Masterwork`) |
| techprint | impressão técnica | **projeto técnico** | `Core/Keyed/Misc.xml` (`TechprintLabel`) |
| mech | mecânico (the profession) | **mecanoide** | `Biotech/Keyed/Alerts.xml` (`AlertMechLacksOverseer`) |
| overseer (Biotech) | supervisor *(correct, but note the Royalty homonym)* | **supervisor** | `Biotech/Keyed/GameplayCommands.xml` (`CommandSelectOverseer`) — do not confuse with a "narrador"/Storyteller |
| psyfocus | psicofoco | **concentração psíquica** | `Royalty/Keyed/Misc_Gameplay.xml` (`Psyfocus`) |
| ideoligion | ideologia religiosa (redundant) | **ideologia** (the game coins no separate word) | `Ideology/Keyed/Misc_Gameplay.xml` (`Ideo`) |
| entity (Anomaly) | empresa, organismo | **entidade** | `Anomaly/Keyed/Misc_Gameplay.xml` |
| storyteller | contador de histórias | **narrador** | `Core/Keyed/MainTabs.xml` (`Storyteller`) |
| east / southwest (short) | E / SW | **L** / **SO** | `Core/Keyed/Enums.xml` (`Direction8Way_East_Short`, `Direction8Way_SouthWest_Short`) — Portuguese uses **L**este, not the English initial |
| toggle | trocar, mudar | **Alternar** | `Core/Keyed/GameplayCommands.xml` (`CommandToggleStudy`) |
| screen reader | leitor de telas (plural, non-standard) | **leitor de tela** (singular) | standard Brazilian accessibility term (§2) |

---

## Section 4 — Translate consistently / do not vary

These are the mod's highest-frequency terms. Twenty translator agents working in parallel must not
each invent a synonym. Use exactly the Portuguese given, every time this English word appears in a
similar role, unless a more specific game-vocabulary entry in Section 1 overrides it for a specific
domain (e.g. "selected" inside a health context still says "selecionado", but a *sort* state still
says "classificável"/"crescente"/"decrescente" per Section 2).

| English | Português (BR) — always use this |
|---|---|
| selected | selecionado (masc. sing., invariable — §3.3 Strategy D) |
| not selected | não selecionado |
| available | disponível |
| unavailable | indisponível |
| none | nenhum / nenhuma (agree with the noun — §3.3) |
| current | atual |
| default | padrão |
| toggle | alternar |
| row | linha |
| column | coluna |
| setting | configuração |
| enabled | ativado |
| disabled | desativado |
| unknown | desconhecido |
| empty | vazio |
| full | cheio |
| required | necessário / obrigatório (use "necessário" for a missing prerequisite, "obrigatório" for a hard validation requirement — do not mix within one screen) |
| optional | opcional |
| expanded | expandido |
| collapsed | retraído |
| locked | trancado |
| unlocked | destrancado |
| active | ativo |
| inactive | inativo |
| item | item |
| items | itens |
| result | resultado |
| results | resultados |
| no results | nenhum resultado |
| loading | carregando |
| error | erro |
| warning | aviso |
| confirm | confirmar (generic dialog button — §1 UI chrome) |
| cancel | cancelar |
| close | fechar |
| open | abrir |
| filter | filtro (noun) / filtrar (verb) |
| search | busca (noun) / buscar (verb) |
| sort ascending / descending | ordem crescente / ordem decrescente |
| out of range | ("{0} de {1}", never "fora de alcance" for a position readout — reserve "fora de alcance" for the combat sense, `Core/Keyed/FloatMenu.xml` `OutOfRange`) |
| page | página |
| tab (UI tab) | aba |
| description | descrição |
| details | detalhes |

---

## Section 5 — Ambiguous short strings, ruled

The following short English keys are grammatically ambiguous out of context. Each was resolved by
reading the C# call site (`grep -rn "<key>" src/`), not guessed from the key name alone.

- **`RimWorldAccess.Cmr.Active`** ("Active", `src/Compat/ColonyManagerRedux/CmrManagerScope.Game.cs`)
  — appended as a standalone status fragment describing whether a Colony Manager job is active.
  Ruling: **Ativo** (masculine singular, invariable state token — §3.3 Strategy D; do not try to
  agree it with "tarefa" or any other noun, since it is spoken as a bare status word).
- **`RimWorldAccess.Compat.RimTalk.Memory.LayerActive`** ("Active",
  `src/Compat/RimTalk/RimTalkMemoryCompat.Game.cs`) — a subsection header for a memory layer's
  active/inactive state. Ruling: **Ativo** (same invariable state token, consistent with the
  previous entry — do not write "Ativa" to agree with the feminine "camada"; a bare state word does
  not agree).
- **`RimWorldAccess.CharEd.XenoGenes.Clear`** ("Clear...",
  `src/Compat/CharacterEditor/CharEditorXenoGenesScope.Game.cs`) — a button action that clears the
  current xenogene selection. Ruling: **Limpar...** (infinitive verb, matches the corpus's own
  `Core/Keyed/FloatMenu.xml` `CleanRoom` → "Limpar {0}"; keep the ellipsis, since the English source
  has one and it signals "this opens a further step/confirmation").
- **`RimWorldAccess.History.Button.Open`** / **`RimWorldAccess.Shell.Glyph.Close`** /
  **`RimWorldAccess.WhatsNew.Button.Close`** ("Open"/"Close" as button labels) — imperative dialog
  buttons. Ruling: **Abrir** / **Fechar** (infinitive, matching `Core/Keyed/Designators.xml`
  `DesignatorOpen` → "Abrir" and `Core/Keyed/Dialogs_Various.xml` `CloseButton` → "Fechar").
- **`RimWorldAccess.Inspection.Tree.FilterPanelTitle`** ("Filter", a panel title) — Ruling:
  **Filtro** (noun, matches `Core/Keyed/Misc_Gameplay.xml` `Filter` → "Filtro"; a panel title names
  the panel's contents, it does not command an action).
- **`RimWorldAccess.Common.SortAscending` / `SortDescending`** ("ascending"/"descending", appended
  after a column/header name when announcing sort state, `src/Shell/Screens/ScreenScope.Game.cs`) —
  Ruling: **crescente** / **decrescente** (epicene adjectives, agree with any gender automatically —
  §2, corroborated by `Core/Keyed/Dialog_StatsReports.xml` "distâncias crescentes").
- **`RimWorldAccess.Building.Announcer.OrderFallback` / `RimWorldAccess.Building.Scanner.FallbackOrder`**
  ("Order", a fallback label when a gizmo/command has no more specific name) — Ruling: **Ordem**
  (matches `Core/Keyed/Menus_Main.xml` `ModOrderingWarning` and `Core/DefInjected/MainButtonDef/*`
  `Work.description` "...em que **ordem** de prioridade", both using "ordem" for a general
  command/sequence sense — Portuguese "ordem" covers both meanings the English word does).
- **`RimWorldAccess.Input.Order.*`** (a queued pawn command/order, `src/Input/`) — same word,
  different sense (an instruction given to a pawn rather than a sequence). Ruling: **Ordem** here
  too — Portuguese does not need two different words for these two English senses (standard
  vocabulary, "dar uma ordem" = "give an order/command" is basic Portuguese, not RimWorld jargon).

None of the requested short strings (`Set`, `None`, `Off`) exist as bare standalone keys in our own
`Languages/English/Keyed/` — every instance found is already a full phrase (e.g.
`RimWorldAccess.Building.PaintColor.Set` = `"Color: {0}"`, already unambiguous once translated per
§3.3 Strategy A). If a genuinely bare `Off`/`Set`/`None` key is added later: treat `Off` as the
`ativado/desativado` state pair (§2), `Set` contextually per its call site (usually resolves to a
"Label: {0}" shape already), and `None` per the "Nenhum/Nenhuma" agreement rule (§3.3).

---

## Section 6 — How to extend this glossary

To find how RimWorld translates a term you don't see above (paths relative to the extracted corpus
root):

```bash
# Always parse with ElementTree, never grep raw text — every file interleaves
# <!-- EN: ... --> English comments with the translated values.
python3 - <<'EOF'
import xml.etree.ElementTree as ET, sys
tree = ET.parse(sys.argv[1] if len(sys.argv) > 1 else "path/to/File.xml")
for el in tree.getroot():
    if isinstance(el.tag, str):
        print(el.tag, "\t", el.text)
EOF

# Or, once you trust the pattern, filter comment lines out of a grep:
grep -rh 'EnglishKeyName' <ptbr_ref>/*/Keyed/*.xml | grep -v '<!--'
```

Useful sub-paths: `Keyed/Designators.xml` (order verbs, infinitive), `Keyed/GameplayCommands.xml`
(gizmos and commands), `Keyed/FloatMenu.xml` (right-click actions — the richest source of the
infinitive+object shape), `Keyed/Misc.xml` + `Keyed/Misc_Gameplay.xml` (general UI),
`Keyed/Enums.xml` (every enum label: quality, price, storage priority, tech level, medical care,
hunger, compass directions), `Keyed/Dialogs_Various.xml` (dialog buttons), `Keyed/Menus_Main.xml`
(main menu, enable/disable — Title Case), `Keyed/Menu_Options.xml` (settings vocabulary — Title
Case), `Keyed/Menu_KeyBindings.xml` (keys and bindings), `Keyed/Time.xml` + `Keyed/Dates.xml`
(calendar), `Keyed/Grammar.xml` (the vanilla pronoun/article machinery — read-only reference, never
copy its `(a)`/`(o)` patterns into our files), `DefInjected/MainButtonDef` (main tab names),
`DefInjected/WorkTypeDef`, `DefInjected/SkillDef`, `DefInjected/NeedDef`,
`DefInjected/DesignationCategoryDef`, `DefInjected/WeatherDef`, `DefInjected/KeyBindingDef` (key
names).

Many corpus files carry `<!-- EN: … -->` comments giving the English source above each value. Use
them to confirm a key's meaning — and, for anything involving a placeholder, to see exactly how the
official translators restructured the English sentence. Always cite the file you took a term from
when you add a row, and always double-check a surprising result (e.g. `Apparel` → `Aparência`) by
reading the English comment rather than assuming a grep artifact.

## Mod-coined terms — watermill placement + read-only browsing (2026-08-13)

Thirteen new keys (`RimWorldAccess.Building.Place.Spot*`, `.Watermill*`, `RimWorldAccess.TextInput.BrowsingField/ReadOnlyField`)
had no reference translation to check: this local install only carries the English Core language pack,
so RimWorld's own Brazilian Portuguese wording for the `WatermillGenerator` ThingDef could not be
verified against the game's corpus. Decided from context per house rule (no native-review parking);
record here so later waves stay consistent instead of re-deriving these. The glossary's own example of
`roda d'água` (Section on elision) is used verbatim for the wheel.

| English | Português (BR) | Basis |
|---|---|---|
| watermill (whole building) | moinho de água | Standard Brazilian term for a water mill |
| waterwheel (the wheel part) | roda d'água | Already cited in this glossary's elision section as the established BR term |
| moving/running water | água corrente | Standard idiom for flowing water |
| water flow area | área de fluxo de água | Coined; parallels `Área necessária` (`OutlineOkAt`) |
| unshared / shared with another watermill | não compartilhada / compartilhada com outro moinho de água | Coined |
| placement spot (scanner category/item) | Locais de colocação de {0} / Local | Mirrors `Abilities.Plant.ScannerCategory`/`ScannerItemLabel` ("Locais de plantio de {0}" / "Local plantável") |
| facing {0} (trailing fragment) | voltado para {0} | Mirrors `Building.ArchitectPlace.GravshipFacing` ("Gravinave voltada para {0}") |
