# Track 9 — Adequação à MIGR-TI/IA: Plano e Especificações

> Status: **Planejado** · Roadmap: [ROADMAP.md → Track 9](../../ROADMAP.md#track-9-migr-tiia-methodology-alignment)
> Base: [docs/methodology/migr-ti-ia.md](../methodology/migr-ti-ia.md) ·
> Análise de aderência: [docs/methodology/migr-ti-ia-coverage.md](../methodology/migr-ti-ia-coverage.md) ·
> Fonte: [migr-ti-ia-guide.pdf](../methodology/migr-ti-ia-guide.pdf)
>
> Este documento está em português porque mapeia 1:1 as lacunas nomeadas na análise de aderência, que
> segue a terminologia do relatório original. A seção correspondente no [ROADMAP.md](../../ROADMAP.md)
> está em inglês, como o restante daquele arquivo.

Este track fecha as lacunas identificadas na análise de aderência do NetRisk 2.21.11 (`db_version`
82) à MIGR-TI/IA. **Não reabre o Track 8** — parte do que ele entregou (aceitação formal com
expiração, inerente vs. residual, segregação de funções, apetite, trilha de auditoria, FAIR/Monte
Carlo) e adiciona o que a metodologia exige e o sistema não instrumenta.

As 12 etapas abaixo são as 15 lacunas priorizadas da §12 da análise de aderência, agrupadas por
dependência. Cada etapa referencia a lacuna que fecha, para que a análise de aderência possa ser
reexecutada e usada como critério de conclusão.

---

## Regra do track: nada é implementado antes da especificação, nada é entregue sem teste

Duas travas se aplicam a **todas** as 12 etapas, sem exceção. Elas são o conteúdo principal deste
plano, não um preâmbulo: o histórico do repositório mostra três controles documentados como
funcionando que não funcionavam (`ApplyEntityScope`, o Master Dashboard, o `[Authorize]` do
`WebAuthnController`), e mostra defeitos — o portão de aviso T-7 que nunca disparava, o mapeamento
quantitativo pela mediana, o `next_review_date_uses` que havia sido apagado 50 versões antes — que só
apareceram porque alguém especificou o comportamento esperado e escreveu o teste.

### Trava 1 — Portão de especificação (obrigatório, precede qualquer código)

Cada etapa começa por uma **especificação completa e revisada**, entregue como um documento próprio
em `docs/roadmap/track9/9.N-<slug>.md` e **mesclada antes do primeiro commit de implementação**.
Nenhum item do ROADMAP pode ser marcado, e nenhum PR de implementação pode ser aberto, enquanto a
especificação da etapa não estiver mesclada.

A especificação de uma etapa só está completa quando contém **todas** as onze seções abaixo. Uma
seção que não se aplica é declarada "não se aplica" com o motivo — nunca omitida, porque uma seção
ausente é indistinguível de uma seção esquecida.

| # | Seção | Conteúdo obrigatório |
|---|---|---|
| 1 | **Lacuna e fase** | Qual lacuna da §12 da análise de aderência, qual fase e qual campo do modelo de registro de risco a etapa fecha. Se a etapa não fecha nada da metodologia, ela não pertence a este track |
| 2 | **Estado atual com evidência** | O que existe hoje, **nomeando a entidade, o serviço, o endpoint ou o teste**. Uma afirmação sem código apontado é tratada como lacuna, não como cobertura — mesma regra da [convenção de segurança](../security/README.md) |
| 3 | **Estado alvo e escopo negativo** | O comportamento pretendido e, explicitamente, **o que a etapa não fará**. O escopo negativo evita que a etapa cresça durante a implementação |
| 4 | **Modelo de dados** | Entidades e colunas novas, já conformes à convenção Track 6 (`snake_case`, `created_at`/`updated_at` UTC, `tinyint(1)` para booleano, `int` + enum com `HasConversion`, `varchar(n)` e **nunca `char(n)` para `string`**), FKs `fk_<tabela>_<coluna>`, índices `idx_`/`uq_`/`ftx_` |
| 5 | **Caminho de esquema** | O ritual de duas etapas: a migração EF (mantém modelo e snapshot em sincronia) **e** os arquivos numerados `DB/Structure/{n}.sql` + `DB/Data/{n}.sql`, com `targetVersion` em `DatabaseInformation.yaml`. Structure sem transação e com **todo statement guardado**; Data como DML puro em transação real, com o bump de `db_version` como ponto de commit. Se a etapa for uma fase Track 6, a entrada correspondente em `SchemaUpgradePhases.yaml` |
| 6 | **Contrato de API** | Rotas, verbos, DTOs, códigos de erro, e o atributo `[Authorize]`/`[PermissionAuthorize]` de **cada** ação, com a permissão nomeada. Um endpoint sem atributo é reprovado por `ControllerAuthorizationInventoryTest` — a especificação decide qual permissão, não a implementação |
| 7 | **Superfície de cliente e GUI** | Views Avalonia afetadas, classes de estilo da taxonomia §4.1, chaves de string nos **três** arquivos `Localization*.resx`, e a propriedade `Str*` de view-model que as expõe. Nada de cor inline, nada de literal em `Text`/`Content`/`Title` — `./build.sh LintUi` falha o build |
| 8 | **Plano de teste** | Por camada, **quais casos** serão escritos: caminho feliz, cada ramo de guarda/erro, e o caso-limite que a metodologia nomeia (ver Trava 2). O plano é revisado junto com a especificação, não escrito depois |
| 9 | **Critérios de aceitação verificáveis** | Cada critério é uma afirmação que um teste nomeado ou um comando nomeado decide. "Funciona" não é critério |
| 10 | **Efeito na análise de aderência** | Quais linhas de [migr-ti-ia-coverage.md](../methodology/migr-ti-ia-coverage.md) mudam de ❌/🟡 para ✅, e quais **permanecem** parciais e por quê |
| 11 | **Riscos, desvios e decisões deliberadas** | O que pode não funcionar como especificado e o que se decidiu **não** fazer. Um desvio descoberto na implementação volta para a especificação como emenda — não é anotado apenas no commit |

**Revisão da especificação.** Antes da mesclagem, a especificação passa por revisão que verifica as
onze seções, a conformidade das seções 4–7 com as convenções do repositório, e — em particular — se a
seção 2 aponta código real. Etapas que tocam autenticação, autorização, segredos, esquema de
configuração ou o composition root seguem a [norma de desenvolvimento seguro](../security/README.md)
e recebem revisão de segurança na especificação, não só no código.

**Emenda.** Uma especificação mesclada muda por emenda datada no próprio documento, com o motivo.
Especificação que a implementação contradiz silenciosamente é pior do que nenhuma, porque passa a
descrever um sistema que não existe.

### Trava 2 — Portão de testes (obrigatório, entrega junto com o código)

Testes fazem parte da mudança, não do follow-up — a regra já vale no repositório
([src/AI_TESTING_INSTRUCTIONS.md](../../src/AI_TESTING_INSTRUCTIONS.md)) e este track a aplica etapa
por etapa. Nenhum item é marcado sem os testes que a especificação planejou.

**O mínimo por camada**, conforme o que a etapa toca:

| Camada | Projeto | Exigência |
|---|---|---|
| Lógica de domínio / serviço | `ServerServices.Tests` | Caminho feliz **e cada ramo de guarda ou erro** introduzido, sobre `MockDalService`/`MockDbContext`; consultas paginadas asseveram lista **e** total (Sieve está ativo) |
| Endpoint | `API.Tests` | Herdar `BaseControllerTest`, dublês por teste via `ResolveController<T>(configure)`; asseverar o subtipo concreto de `ActionResult<T>` e depois o `.Value`. Controller novo é auto-registrado — não editar arquivo compartilhado |
| Autorização | `API.Tests/Security` | `ControllerAuthorizationInventoryTest` cobre a ausência de atributo; qualquer `[AllowAnonymous]` novo exige justificativa na allowlist |
| Cliente REST | `ClientServices.Tests` | Via `MockSetup.GetRestClient()` — nunca HTTP real |
| Cálculo puro | `Tools.Tests` | Determinístico e semeado. **Todo cálculo estatístico ou econômico desta etapa entra aqui**, não no serviço |
| Validação de GUI | `GUIClient.Tests` | Chave de localização que não resolve em nenhum `.resx`, e token `Classes="…"` que nenhum estilo define — o linter não vê nenhum dos dois |
| Esquema | `ConsoleClient.Tests` + `DAL.IntegrationTests` | Idempotência statement por statement; ordem de referência a tabelas; **e** a reaplicação com cada Structure rodado duas vezes resultando em esquema idêntico a uma passagem limpa |
| Job de fundo | `BackgroundJobs.Tests` | Idempotência e ordem relativa aos jobs existentes (06:15 expiração → 07:30 cadência → 08:00 campanhas) |
| Portal | `RiskPortal.Tests` | Fluxo do revisor, incluindo o caminho sem JavaScript quando houver progressive enhancement |

**Cinco regras que valem em todas as etapas:**

1. **Nenhum host, banco ou HTTP real** em teste unitário. O sujeito sai do contêiner de DI do projeto (`<Project>.Tests.DI.ServiceRegistration.GetServiceProvider()`).
2. **Correção de defeito entra com teste de regressão que falha no código pré-correção.** Sem isso, não se sabe se o teste testa algo.
3. **Nenhuma asserção é enfraquecida ou removida para obter execução verde.** Defeito encontrado e não corrigido é **reportado explicitamente** na etapa, não silenciado.
4. **`Include` em navegação obrigatória faz inner join.** Semear as linhas principais (`User`, `Entity`, `Role`), ou as linhas semeadas voltam como lista vazia e o teste passa medindo nada.
5. **Onde o comportamento é observável em execução, ele é observado.** Dois defeitos do Track 7 eram invisíveis a teste unitário: um cabeçalho que o middleware removia e o Kestrel readicionava abaixo dele, e um id de requisição SSO cuja entropia era irrelevante porque o atacante o escolhia. Etapas com efeito em runtime declaram na especificação **como** serão observadas.

**Além do mínimo, cada etapa escreve os casos-limite que a metodologia nomeia.** A metodologia é
específica sobre onde a implementação óbvia está errada, e cada um desses pontos é um teste:
composição de eficácia de controles como `1 − Π(1 − pᵢ)` e não soma; mapeamento de faixa pela média e
não pela mediana; escada de aviso pelo limiar **mais restritivo** e não pelo primeiro que casa;
primeiro vencimento de revisão **um intervalo após a submissão** e não imediatamente; aceitação
vigente concedida **antes** do período entrando no pacote de evidências; item de campanha não
decidido entrando como não decidido. As etapas abaixo nomeiam os seus.

---

## Fase I — Fundação do registro

Sem estas duas etapas nenhuma das outras tem onde se ancorar: os portões precisam de flags, as flags
precisam de cenário estruturado, e a cobertura de processos críticos precisa da cadeia.

### Etapa 9.1 — Cadeia de ligação `objetivo → processo → serviço → dado → ativo`

*Fecha a lacuna 1 · Princípio nº 1 · Grupo "Identificação" do registro de risco · Fase 1-B*

**Estado atual.** Existem os nós — `EntitiesConfiguration.yaml` define `businessProcess`,
`application`, `applicationModule`, `organizationData`, `organizationDataGroup`,
`securityClassificationLevel` — e existe `Host`/`HostsService`/`Technology` no lado técnico. O risco,
porém, liga-se a **uma** entidade genérica (`risks.entity_id`, endpoint singular
`GET/PUT/DELETE /risks/{id}/Entity`), disputada entre "unidade de negócio" e "processo".

**Estado alvo.** Objetivo estratégico como primeira classe; catálogo de serviços de TI; o risco
apontando a cadeia inteira, cada elo opcional mas consultável; cobertura de processos críticos
calculável.

**Escopo.** Objetivo estratégico como entidade própria (não como texto em `businessProcess.objective`);
tipo `itService` no esquema de entidades, com dono técnico e processos servidos; vínculos do risco à
cadeia; a métrica de cobertura de processos críticos da Fase 7.

**Fora de escopo.** Importação de BPMN e grafo de dependências visual — ficam para uma etapa própria
se a cadeia tabular se mostrar insuficiente. Atributos de continuidade do processo são a etapa 9.3.

**Casos-limite a testar.** Elo ausente no meio da cadeia (risco com processo e sem serviço) não deve
tornar o risco invisível em nenhuma consulta; a cobertura de processos críticos conta processos
**marcados como críticos** e não todos; o vínculo antigo `risks.entity_id` continua funcionando para
os riscos legados durante a coexistência (create-copy-coexist, como as fases Track 6).

### Etapa 9.2 — Cenário estruturado, discriminação de registros e confiança da evidência

*Fecha a lacuna 2 · Fase 2 · Grupos "Cenário" e "Classificação" do registro*

**Estado atual.** A discriminação entre risco, vulnerabilidade, finding e incidente é **estrutural e
sólida** — são tabelas e ciclos de vida separados, e o veredito de triagem de um finding é aderente
entre reimportações. O que falta é o cenário: `risks` tem `Subject`, `Assessment` e `Notes` como texto
livre, então nenhuma das regras de qualidade da Fase 2 é verificável pelo sistema. Não há nível de
confiança da evidência (`AssessmentScoring.CvssReportConfidence` é outra coisa). `PendingRisk` é a
hipótese, mas só nasce de resposta de assessment.

**Estado alvo.** Quatro campos separados — causa/ameaça, vulnerabilidade/condição, evento central,
consequências — mais nível de confiança da evidência (confirmada / indicativa / hipótese), hipótese
registrável de forma avulsa, *near miss* distinto de incidente, e detecção de risco duplicado pelo par
(evento central, consequência).

**Escopo.** Os quatro campos com o texto-modelo da metodologia como apoio de preenchimento; enum de
confiança; `PendingRisk` aceitando origem avulsa; *near miss* como categoria distinta em `Incident`;
detecção de duplicata de risco na criação, como **aviso**, não como bloqueio.

**Fora de escopo.** Preenchimento automático dos quatro campos a partir do texto livre existente — a
migração deixa os campos nulos nos riscos legados e a análise de aderência passa a medir quantos estão
preenchidos. Adivinhar a decomposição de um texto que ninguém escreveu com essa intenção produziria
cenários errados com aparência de corretos.

**Casos-limite a testar.** Risco legado com os quatro campos nulos continua editável, listável e
pontuável; a detecção de duplicata não impede o registro deliberado de dois cenários com o mesmo
evento e consequências distintas; a promoção de hipótese a risco preserva a origem e o autor.

---

## Fase II — Sinais de decisão

### Etapa 9.3 — BIA: MTPD/MAO, RTO, RPO e dependências em cascata

*Fecha a lacuna 4 · Fase 3 (BIA) · habilita a flag 4 e o Portão A*

**Estado atual.** Nenhum desses campos existe em processo, serviço ou ativo. `SlaConfiguration` tem
`MaxTriageDays`/`MaxRemediationDays` por severidade, com vigência temporal e escopo por entidade — é
SLA de correção de finding, não objetivo de recuperação.

**Estado alvo.** MTPD/MAO, RTO e RPO declarados no processo e no serviço; criticidade do processo;
dependências com efeito em cascata; registro de teste de restauração comparável ao RTO/RPO declarado.

**Casos-limite a testar.** RTO declarado sem teste de restauração aparece como **não verificado**, não
como atendido — é a diferença entre a métrica "restauração testada vs. RTO declarado" medir algo e
medir a intenção; cascata cíclica de dependências não causa recursão infinita no cálculo de impacto;
processo sem BIA não é tratado como RTO zero nem como infinito, mas como ausente.

### Etapa 9.4 — Sinais de exploração: CISA KEV, EPSS de primeira classe e MITRE ATT&CK

*Fecha a lacuna 6 · Fase 1-F e Fase 3 · habilita a flag 3 e a métrica de tempo de correção KEV*

**Estado atual.** Só CVSS (vetor e score, com `RawSeverity` preservando o valor cru da ferramenta) e
severidade normalizada. O EPSS chega **exclusivamente** pela Vision One e vai para
`ToolFields["epss"]` — um saco de chaves livres: não é coluna, não é filtrável, não entra em
priorização. KEV e ATT&CK não existem; o Track 3 cita o benchmark de 14 dias para itens KEV e nada
consulta o catálogo.

**Estado alvo.** EPSS promovido a coluna em `vulnerabilities`, alimentado por sincronização própria e
não só pela Vision One; catálogo CISA KEV sincronizado com data de inclusão e prazo; técnicas ATT&CK
associáveis ao finding e ao cenário de risco; priorização combinando os sinais que a Fase 3 lista, com
CVSS como **entrada**.

**Escopo.** Sincronização KEV e EPSS por job de fundo, através de `IOutboundHttpClient` (a política
SSRF se aplica); colunas, índices e filtros Sieve; exposição na priorização e nas métricas.

**Fora de escopo.** Exposição (interna/perimetral/externa), privilégios e blast radius — mesma fase da
metodologia, mas dependem de modelagem de topologia; ficam para uma etapa própria e permanecem ❌ na
análise de aderência até então. Declarar isso agora evita que a etapa 9.4 pareça fechar a Fase 3.

**Casos-limite a testar.** Catálogo indisponível ou resposta malformada **não** rebaixa silenciosamente
um item que já era KEV — o dado antigo permanece com sua data de sincronização visível; EPSS de fonte
própria e EPSS da Vision One para o mesmo CVE convergem por regra declarada, não por ordem de escrita;
a sincronização é idempotente e não reescreve linhas inalteradas (senão a trilha de auditoria e o
`updated_at` perdem sentido).

### Etapa 9.5 — As 11 flags obrigatórias e o Portão A

*Fecha a lacuna 3 · Fase 2 (flags) e Fase 4 (Portão A)*

**Estado atual.** Nenhuma flag existe como campo consultável. A flag 4 é parcialmente inferível por
`SlaDueDate`; as outras dez, por nada. Consequência direta: **o Portão A não é implementável** — um
risco de segurança humana ou com exploração ativa é indistinguível de outro com o mesmo score.

**Estado alvo.** As onze flags como campos consultáveis, algumas derivadas e outras declaradas; o
Portão A recusando o descarte de risco com flag não discricionária, com escalonamento imediato
notificado; a lista executiva "Top Risks" mostrando tendência, confiança e próxima decisão.

**Escopo.** As onze flags, com a origem de cada uma declarada na especificação (derivada de KEV/EPSS,
derivada do BIA, derivada da classificação do dado, ou declarada pelo avaliador); avaliação do Portão A
no fluxo de decisão e na aceitação; uma decisão "agir imediatamente" distinta de severidade alta.

**Dependências.** Etapas 9.3 (flag 4) e 9.4 (flag 3). A flag 11 fica declarável aqui e só passa a
derivável na etapa 9.12.

**Casos-limite a testar.** Flag derivada que perde sua base (item sai do KEV, BIA é apagado) volta a
falsa **com registro na trilha**, não silenciosamente; flag não discricionária verdadeira torna a
aceitação recusada mesmo quando o apetite permitiria — **o Portão A precede o Portão B**, e a ordem é
o teste; break-glass, se existir para o Portão A, exige motivo escrito persistido e exportado, como o
de segregação de funções.

---

## Fase III — Economia e cauda

### Etapa 9.6 — Economia do tratamento: custo monetário, Portões C e D, tratamento completo

*Fecha as lacunas 7 e 10 e parte da 13 · Fase 4 (Portões C e D) e Fase 5*

**Estado atual.** O vínculo cenário→controle é forte (`MitigationToControl` com `ValidationOwner`,
`ValidationDetails`, `ValidationMitigationPercent`) e a composição de eficácia é a correta
(`1 − Π(1 − pᵢ)`). Mas `MitigationCost` é uma **escala ordinal** — tabela de rótulos, não valor — de
modo que `E[L antes] − E[L depois] > custo` não é computável: o **Portão C tem todos os ingredientes e
nenhuma conta**. O Portão D não existe. "Evitar" e "transferir/compartilhar" não são tipos de
tratamento. `MitigationTask` tem dono, prazo e status, mas **não tem evidência de conclusão nem
critério de aceite**. Não há nível de risco "alvo".

**Estado alvo.** Custo monetário ao lado da escala ordinal (a escala continua, para quem não estima em
moeda); o cálculo de benefício marginal; seleção de carteira sob orçamento e prazo; as quatro opções de
tratamento; evidência e critério de aceite por tarefa; nível "alvo" no registro.

**Casos-limite a testar.** Mitigação sem custo monetário não entra no Portão C como custo zero —
entra como **não avaliável**, e isso aparece; benefício marginal usa o E[L] **médio** e não a mediana,
pelo mesmo motivo que o mapeamento de faixa (a metodologia cita Gordon–Loeb como referência
econômica, **não** como regra fixa de 37% — a especificação registra isso explicitamente); a seleção
de carteira do Portão D **preserva riscos de cauda e sistêmicos mesmo com E[L] moderado**, que é a
regra que um otimizador ingênuo viola primeiro.

### Etapa 9.7 — Estatística de cauda e portfólio: P95, CVaR, agregação e correlação

*Fecha a lacuna 9 · Fase 3 (saídas) e Fase 7 (exposição agregada)*

**Estado atual.** O Monte Carlo é bom e reprodutível (`MonteCarloRiskSimulator`: magnitude PERT,
contagem de eventos Poisson, `quant_seed` persistida, 10 000 iterações), e o mapeamento de faixa usa a
**média** justamente pelo caso de cauda longa. Faltam: **P95**, **CVaR**, intervalos de confiança, a
decomposição da magnitude por tipo de perda, e a **agregação de portfólio com correlação** — cada
risco é simulado isoladamente, então a métrica "exposição agregada acima do apetite (E[L] e P95)" da
Fase 7 não é calculável.

**Estado alvo.** P95 e CVaR calculados e armazenados; intervalos de confiança reportados; magnitude
decomponível em resposta, recuperação, produtividade, receita, responsabilidade, multa e reputação;
agregação de carteira com correlação declarada entre cenários; apetite comparável contra P95/CVaR,
como o Portão B da metodologia prevê.

**Casos-limite a testar.** CVaR de um cenário de baixa frequência não é zero por a maioria das
iterações ser zero — é a média da cauda além do percentil, e este é o teste que separa a implementação
certa da óbvia; a soma de carteira com correlação zero **não** é a soma dos P95 individuais, e a
especificação declara qual estatística é somável e qual não é; semente fixa reproduz o mesmo resultado
agregado, não só o individual.

---

## Fase IV — Monitoramento e ciclo

### Etapa 9.8 — KRIs, gatilhos obrigatórios de reavaliação e métricas da metodologia

*Fecha a lacuna 5 · Fase 7 inteira, e o Portão B por indicador*

**Estado atual.** A cadência é forte e correta: `RiskReviewCadenceJob` às 07:30 **após** as duas
passagens de expiração, campanhas trimestrais alinhadas ao calendário com índice único
`(entidade, período)` — idempotentes por construção — e a regra de que risco nunca revisado vence um
intervalo **após a submissão**. Notificação existe com 16 tipos de evento, `MinSeverity`, escopo por
entidade e janela de digest. O que **não existe é KRI**: nem indicador, nem limiar, nem gatilho. É a
lacuna de maior alcance da Fase 7 — dela dependem os gatilhos obrigatórios, o Portão B por indicador e
cinco das dez métricas.

**Estado alvo.** KRI como primeira classe (definição, fonte, limiar de tolerância, histórico); os seis
gatilhos obrigatórios de reavaliação da Fase 7; as métricas da metodologia que passam a ser calculáveis
depois das etapas anteriores.

**Escopo.** Entidade de KRI e série histórica; avaliação de limiar por job com notificação e gatilho de
reavaliação; gatilhos por evento (mudança de arquitetura, novo fornecedor, nova regulação, incidente ou
*near miss*, novo modelo de IA, KRI estourado); painel de métricas.

**Dependências.** 9.4 (tempo de correção KEV), 9.3 (restauração vs. RTO), 9.7 (exposição agregada),
9.10 (concentração em terceiros), 9.12 (métricas de IA). A etapa entrega o mecanismo e as métricas
cujas fontes já existirem; as demais entram com suas etapas, e a especificação lista quais serão quais.

**Casos-limite a testar.** KRI sem leitura recente aparece como **obsoleto**, não como dentro da
tolerância — falso conforto é o defeito característico de painel de indicador; o gatilho de reavaliação
é idempotente (um KRI que permanece estourado por trinta dias não abre trinta reavaliações); a ordem
relativa aos jobs existentes é preservada e testada, como a do `RiskReviewCadenceJob`.

### Etapa 9.9 — Arquivamento com gatilho, backtesting, comitê e terceira linha

*Fecha as lacunas 13 (parte), 14 e 15 · Fase 4 (calibração e decisões) e Fase 0 (papéis)*

**Estado atual.** `Closure` + `CloseReason` registram o fechamento, mas **não há gatilho de reabertura
por condição** — só o temporal da expiração de aceitação — nem revisão trimestral do arquivo. Não há
confronto entre incidentes ocorridos e riscos previstos, de modo que "incidentes não previstos" e
"falsos negativos" não são calculáveis. A aprovação é por pessoa (`AuthorizingManagerId`, com faixa de
autoridade resolvida das linhas semeadas de `risk_levels`), não por comitê. Papéis cobrem 1ª e 2ª
linha; **não há papel de 3ª linha somente-leitura** — o auditor consome o pacote de evidências, não o
sistema.

**Estado alvo.** Estado "arquivado" com justificativa, gatilho de reabertura por condição e revisão
trimestral; backtesting de incidentes e *near misses* contra o registro; comitê de risco como aprovador
colegiado; papel de auditoria com acesso de leitura de assurance.

**Casos-limite a testar.** Gatilho de reabertura por condição dispara uma vez e registra, em vez de
reabrir repetidamente; o backtesting classifica como "não previsto" o incidente sem cenário
correspondente e **não** conta como previsto o cenário registrado depois do incidente — a data é o
teste; o papel de 3ª linha lê e **não escreve**, inclusive não podendo revisar nem aceitar, e isso é
testado pelos casos negativos, como a segregação de funções do Track 8 (que recusa até
administradores).

---

## Fase V — Domínios ausentes

### Etapa 9.10 — Registro de terceiros: HECVAT, SBOM, concentração e exit plan

*Fecha a lacuna 8 · Fase 1-E, Fase 5 (fornecedores), Fase 7 (concentração)*

**Estado atual.** **Não existe registro de fornecedor.** Um fornecedor só pode ser modelado como
`organization`/`organizationUnit` genérico. Sem HECVAT, sem SBOM, sem SLA contratual, sem localização
de dados, sem direito de auditoria, sem exit plan, sem medida de concentração. É a única frente de
discovery sem nenhum instrumento — e a metodologia a trata como central tanto em empresa quanto em IES
(edtech, publishers, nuvem científica).

**Estado alvo.** Terceiro como primeira classe, com avaliação HECVAT, SBOM do componente fornecido,
suboperadores, localização dos dados, SLA e RTO/RPO contratados, direito de auditoria, exit plan e
portabilidade; concentração medida por fornecedor, nuvem e identidade; o terceiro ligável ao serviço de
TI da etapa 9.1 e ao dado da etapa 9.11.

**Casos-limite a testar.** Concentração conta o fornecedor **uma vez** por processo crítico dependente,
não uma vez por ativo (senão a métrica mede inventário, não concentração); HECVAT parcialmente
respondido pontua como incompleto e não como conforme; exclusão de fornecedor em uso é recusada — a
mesma regra que `SecretVaultService.CountReferencesAsync` já aplica a conexões de vault, e o registro
de referências tem de ser estendido, não duplicado.

### Etapa 9.11 — Catálogo de dados LGPD: base legal, finalidade, retenção, localização e DPIA

*Fecha a lacuna 11 · Fase 1-C, Fase 2 (flags 2 e 5), Fase 5 (dados)*

**Estado atual.** `organizationData` e `organizationDataGroup` existem com responsável e
`securityClassificationLevel` — a classificação existe. **Ausentes:** base legal e finalidade,
retenção, localização, transferência internacional, marcação de dado pessoal sensível, linhagem, DPIA.
Consequência: a conformidade não é demonstrável, e as flags 2 e 5 não têm base derivável. A coluna
`regulation` do risco foi removida no Track 6 fase 6b por estar órfã, então nem o vínculo textual
restou.

**Estado alvo.** Catálogo de dados com base legal, finalidade, retenção, localização, transferência
internacional e marcação de dado pessoal e sensível; DPIA como artefato ligado ao dado e ao processo;
requisitos legais e contratuais de volta ao registro de risco, agora ligados ao catálogo em vez de
texto livre.

**Casos-limite a testar.** Dado pessoal sensível sem base legal declarada é **um achado**, e o teste
verifica que ele aparece como tal e não como conforme por omissão; retenção vencida não apaga nada —
sinaliza, porque apagar dado do titular é decisão do controlador e não efeito colateral de um job;
a marcação de sensível deriva a flag 5 e a remoção da marcação remove a flag com registro na trilha.

### Etapa 9.12 — Governança de IA: inventário de modelos, flag 11 e métricas de modelo

*Fecha a lacuna 12 · Fase 6 inteira e a flag 11*

**Estado atual, com uma nuance que importa.** As **proibições** da Fase 6 já valem **por construção**:
todo ato de aprovação exige um `User` — `AuthorizingManagerId`, `EntityRiskReviewer`, o revisor da
`MgmtReview` — a segregação de funções recusa revisor que seja submetente, dono ou gestor, inclusive
administradores, e trabalho de fundo grava como ator `system` em vez de como ninguém. A IA não pode
aceitar risco residual, aprovar exceção ou ser dona de risco porque não há caminho no sistema para uma
não-pessoa fazer isso. **A lacuna é o inventário e o assurance, não o controle de autoridade.**

O que falta: inventário de modelos (finalidade, dados, fornecedor, versão); testes, métricas,
explicabilidade, viés, drift, red team; RAG com fontes e nível de confiança; proteção contra prompt
injection e DLP; telemetria e rollback de modelo. O produto não emprega IA no fluxo de risco; os pontos
de extensão existem (`INetriskVulnerabilityClassificationPlugin`, `INetriskModelPlugin`) sem
implementação, e o FaceID é biometria de autenticação, não apoio à decisão de risco.

**Estado alvo.** Inventário de modelos como primeira classe, com avaliação proporcional ao risco;
riscos do próprio componente de IA no **mesmo** registro, com a flag 11 derivada do inventário;
métricas de modelo (precisão, recall, calibração, drift, **taxa de override humano**); e, se e quando
uma funcionalidade de IA for adicionada ao fluxo de risco, os requisitos de fonte explicitada, nível de
confiança declarado e revisão humana obrigatória de todo resultado.

**Escopo desta etapa.** O inventário, o assurance e a flag 11. **Fora de escopo:** adicionar IA ao
fluxo de risco. A metodologia exige que o instrumento de governança exista *antes* do uso, e a ordem
inversa é como se acumula dívida de conformidade — esta etapa entrega a governança, e qualquer uso de
IA passa a ser um track próprio que a pressupõe.

**Casos-limite a testar.** As proibições continuam válidas **depois** de a etapa existir — testes
negativos explícitos de que nenhum caminho permite um não-usuário aceitar residual, aprovar exceção ou
encerrar finding material, para que a garantia por construção não se perca numa refatoração futura;
modelo sem avaliação registrada não é tratado como avaliado; a taxa de override humano exige que
override seja registrável, e o teste verifica que a decisão humana contrária ao modelo é gravada com
autor e motivo.

---

## Conclusão do track

O track está concluído quando a análise de aderência é **reexecutada** e as linhas que cada
especificação declarou na sua seção 10 estão em ✅ — e quando as que permanecem 🟡 ou ❌ estão nomeadas
com o motivo, como estão hoje. A análise de aderência é o critério de aceitação do track, e é por isso
que ela vive em [docs/methodology/](../methodology/) e não neste documento: ela é medida contra o
código, repetidamente, e não escrita uma vez.
