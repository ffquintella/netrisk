# Aderência do NetRisk à MIGR-TI/IA

Análise de aderência do sistema à metodologia de referência
([migr-ti-ia.md](migr-ti-ia.md) · [PDF](migr-ti-ia-guide.pdf)), atividade por atividade.

**Data da análise:** 18/09/2026 · **Versão analisada:** 2.21.11 (`db_version` 82)

**Método.** Cada atividade da metodologia foi confrontada com o código e o esquema, não com a
documentação de produto. Toda afirmação de cobertura abaixo nomeia a entidade, o serviço, o endpoint
ou o teste que a sustenta — nenhuma linha diz "atendido" sem esse apontamento, e onde não há código
que nomear, a linha é **Ausente** mesmo que a intenção exista no roadmap.

**Legenda.** ✅ **Coberto** — existe instrumento dedicado. 🟡 **Parcial** — existe algo aproveitável,
mas falta o campo, o cálculo ou a obrigatoriedade que a metodologia pede. ❌ **Ausente** — não há
instrumento; o usuário só consegue registrar em texto livre.

---

## 1. Veredito

O NetRisk cobre com profundidade a **espinha dorsal de governança** da metodologia — Fases 0, 4
(parcialmente), 5 e 7 e o modelo de registro de risco — e cobre a Fase 3 na parte que a maioria das
ferramentas não cobre (FAIR/Monte Carlo com curva de excedência). Track 8 entregou justamente os
itens que a metodologia trata como não negociáveis: aceitação formal **com expiração**, inerente vs.
residual, segregação de funções, apetite, trilha de auditoria campo a campo e aceitação pelo dono do
negócio em portal próprio.

As lacunas concentram-se em três eixos, e são estruturais, não cosméticas:

1. **A cadeia de ligação `objetivo → processo → serviço → dado → ativo` não é navegável.** Existem os
   nós (há um tipo de entidade `businessProcess`, há `application`, há `organizationData`, há `Host`),
   mas o risco se liga a **uma** entidade genérica (`risks.entity_id`) e não a uma cadeia. O princípio
   nº 1 e o grupo "Identificação" do registro de risco não têm onde ser preenchidos de forma
   consultável.
2. **A priorização de vulnerabilidades não tem os sinais que a Fase 3 exige.** Não há EPSS, KEV nem
   ATT&CK como campos de primeira classe — só CVSS e severidade. O que existe é a triagem por
   severidade + SLA do Track 3.
3. **Não há Fase 6.** O produto não usa IA no fluxo de risco e, mais importante para a metodologia,
   não tem **inventário de modelos** nem as flags/controles de IA que a Fase 6 e a flag 11 pedem.

Um quarto eixo, menor mas com efeito desproporcional: **as 11 flags obrigatórias e o nível de
confiança da evidência não existem como campos**, e são exatamente o que faz os Portões A e B
funcionarem sem depender de score.

---

## 2. Fase 0 — Governança e contexto

| Atividade | Estado | Instrumento no NetRisk |
|---|---|---|
| Escopo por unidades/processos/tecnologias/dados | ✅ | `Entity` + `EntitiesConfiguration.yaml` (11 tipos, inclusive `businessProcess`, `application`, `organizationData`); escopo aplicado por `user_entity_roles` (Track 2.3) |
| Apetite e tolerância aprovados | ✅ | [`RiskAppetite`](../../src/DAL/Entities/RiskAppetite.cs) global ou por entidade, com limiar de dupla aprovação e teto de aceitação; **nenhuma linha é semeada**, por decisão deliberada |
| Critérios de impacto com definição escrita e faixa numérica | ✅ | Âncoras por nível nas escalas de probabilidade e impacto (Track 8 §8), exibidas no momento da avaliação |
| Taxonomia e categorias de risco | 🟡 | `Category`, `Source`, `RiskCatalog`, `RiskGrouping` e `ThreatCatalogMapping` existem, mas a taxonomia da Fase 2 (CIA, privacidade, segurança humana, continuidade, integridade acadêmica, sistêmico, IA) não está semeada nem estruturada como dimensões independentes — `Category` é um valor único |
| Cadência mensal/trimestral/anual | ✅ | `ReviewLevel` + `RiskReviewCadenceJob` (07:30 diário) + campanhas por período calendário (trimestral por padrão) |
| RACI e três linhas | 🟡 | Papéis por permissão (`Role`, `RoleResponsibility`, `business_risk_review` deliberadamente sem `riskmanagement`) e `EntityRiskReviewer` cobrem 1ª e 2ª linha; **não há papel de 3ª linha (auditoria) com acesso somente-leitura de assurance** — o auditor consome o pacote de evidências, não o sistema |
| Comitê de risco constituído | ❌ | Não há objeto "comitê"; a aprovação é por pessoa (`AuthorizingManagerId`) segundo faixa de severidade |
| Política de gestão de riscos versionada no sistema | ❌ | Não há repositório de políticas; anexos ficam em `NrFile` sem versionamento nem vigência |
| Distinção inerente / atual-residual / alvo | 🟡 | Inerente e residual são primeira classe (`risk_scoring.residual_risk` + `risk_scoring_history`, composição `1 − Π(1 − pᵢ)`). **Não há nível "alvo"** — o residual esperado após o tratamento planejado só existe no resultado quantitativo antes/depois, não como campo do registro |

## 3. Fase 1 — Discovery orientado a processos

| Frente | Estado | Instrumento |
|---|---|---|
| A — Organizacional (entrevistas, estratégia, contratos, auditorias) | 🟡 | `Assessment` + `AssessmentRun` são o instrumento natural (templates reusáveis, respostas por ativo/grupo, histórico longitudinal) e `PendingRisk` promove uma resposta em risco real. Não há template de entrevista de discovery semeado |
| B — Processos (BPMN, dependências, exceções, ponto único de falha) | 🟡 | `businessProcess` tem `name`, `description`, `objective` (texto livre), `applications`, `organizationUnit`. **Não tem** criticidade, MTPD/RTO/RPO, entradas/saídas, sazonalidade, controles manuais nem marcação de ponto único de falha. Não há importação de BPMN |
| C — Dados (catálogo, classificação, base legal, retenção, localização) | 🟡 | `organizationData` / `organizationDataGroup` com responsável e `securityClassificationLevel`. **Ausentes:** base legal/finalidade LGPD, retenção, localização, transferência internacional, marcação de dado pessoal sensível, linhagem |
| D — Técnico (CMDB, nuvem, ASM, varredura, IAM, SIEM, backups) | ✅ | `Host` + `HostsService` (porta/protocolo) + `Technology`; importadores de scanner extensíveis (`IVulnerabilityImporter`, SARIF, Nessus, ZAP, Trivy…) com deduplicação persistida (`dedup_key`); integrações de postura (Trend Micro Vision One, SecurityScorecard); Jira Assets como CMDB externa |
| E — Terceiros (contratos, HECVAT, SBOM, suboperadores, concentração, exit plan) | ❌ | **Não existe registro de fornecedor.** Um fornecedor só pode ser modelado como `organization`/`organizationUnit` genérico. Sem HECVAT, sem SBOM, sem SLA contratual, sem localização de dados, sem direito de auditoria, sem exit plan, sem medida de concentração |
| F — Ameaças (incidentes, near miss, TI, ATT&CK, KEV, EPSS) | 🟡 | `Incident` (numeração anual, categorias, anexos, vínculo a planos de resposta) e `ThreatCatalogMapping` no risco. **Sem** registro de *near miss* distinto do incidente, **sem** threat intelligence, **sem** ATT&CK/KEV/EPSS (ver §5) |
| IA no discovery (NLP/RAG, classificação, grafos, anomalias) | ❌ | Não há. O SDK de plugins tem `INetriskVulnerabilityClassificationPlugin` e `INetriskModelPlugin` como pontos de extensão, mas nenhuma implementação de IA acompanha o produto, e os requisitos da Fase 6 (fonte, confiança, revisão humana, log) não têm campos onde residir |

## 4. Fase 2 — Discriminação e formulação de cenários

| Atividade | Estado | Instrumento |
|---|---|---|
| Distinguir risco de vulnerabilidade, finding, incidente e issue | ✅ | São tabelas e ciclos de vida separados: `risks`, `vulnerabilities` com `FindingStatus` (`Active`/`Inactive`/`Duplicate`/`Mitigated`/`FalsePositive`/`OutOfScope`/`RiskAccepted`), `incidents`, e `FindingIssueLink` para o rastreador externo. Veredito de triagem é **aderente entre reimportações** |
| Distinguir ameaça | 🟡 | Só como `ThreatCatalogMapping` (texto) no risco |
| Distinguir não conformidade | 🟡 | Aproximada por `FrameworkControl` + `FrameworkControlTestResult` (teste de controle reprovado), não como registro próprio |
| Distinguir **hipótese** | 🟡 | `PendingRisk` é o mais próximo (vem de resposta de assessment, com `Pending`/`Promoted`/`Dismissed` e motivo), mas só nasce de um assessment — não se pode registrar uma hipótese avulsa |
| Cenário estruturado (causa · vulnerabilidade · evento central · consequência) | ❌ | `risks` tem `Subject`, `Assessment` e `Notes` como texto livre. **Não há campos separados** para causa/ameaça, vulnerabilidade/condição, evento central e consequências — logo as regras de qualidade do cenário não são verificáveis pelo sistema |
| Consolidar duplicatas e dividir cenários | 🟡 | Deduplicação existe e é forte **para findings** (`dedup_key`, estratégia registrada, painel de pré-visualização); para **riscos** não há detecção de duplicata |
| Dependências e correlações entre riscos | 🟡 | `ContributingRisk` (com peso) e `RiskGrouping` permitem compor; não há grafo de dependências nem correlação usada na agregação |
| Nível de confiança da evidência (confirmada / indicativa / hipótese) | ❌ | Não existe campo. Há `CvssReportConfidence` em `AssessmentScoring`, que é outra coisa |
| **As 11 flags obrigatórias** | ❌ | Nenhuma existe como campo booleano consultável. Flag 4 é inferível em parte por `SlaDueDate`, flag 2 por nada, flag 3 por nada (sem KEV), flag 11 por nada (sem IA) |
| Regra "CVE individual não é risco" | ✅ | Estruturalmente garantida: um finding é `vulnerabilities`, e virar risco exige associação explícita (`POST /vulnerabilities/{id}/Risks/{riskId}`) ou promoção de `PendingRisk` |
| Lista executiva "Top Risks" separada | 🟡 | `IStatisticsService.GetRisksTopAsync` e `risks.business_rank` (ranking do dono do negócio) dão a lista; **falta** tendência, confiança e "próxima decisão" na mesma visão |

## 5. Fase 3 — Análise, quantificação e priorização

| Atividade | Estado | Instrumento |
|---|---|---|
| Triagem qualitativa só para encaminhamento | ✅ | Documentado e assumido: `RiskCalculationTool.CalculateTotalRiskScore` é declarado heurística de fila e **não** medida — a mesma crítica de Cox que a metodologia faz está escrita em [risk-governance.md §8](../features/risk-governance.md#8-quantitative-scoring) |
| LEF como distribuição (mín / mais provável / máx) | ✅ | `quant_lef_min/most_likely/max` |
| LM como distribuição | ✅ | `quant_loss_min/most_likely/max`, amostrada como PERT |
| Monte Carlo com semente reproduzível | ✅ | [`MonteCarloRiskSimulator`](../../src/Tools/Risks/MonteCarloRiskSimulator.cs), 10 000 iterações, contagem de eventos por Poisson, `quant_seed` persistida |
| E[L] / ALE médio | ✅ | `quant_ale_mean` — e o mapeamento para faixas usa a **média**, não a mediana, precisamente pelo caso "uma vez por década, oito milhões" |
| Percentis | 🟡 | P10/P50/P90 (`quant_ale_p10/p50/p90`). A metodologia pede **P95**, que não é calculado nem armazenado |
| **CVaR** (medida de cauda) | ❌ | Não existe. A curva de excedência (`quant_loss_exceedance_curve`) é o material bruto para calculá-la, mas o valor não é produzido nem comparável ao apetite |
| Intervalos de confiança | ❌ | Não reportados |
| Residual quantitativo antes/depois | ✅ | `quant_residual_ale_p10/p50/p90` |
| **Agregação de portfólio e correlação** | ❌ | Cada risco é simulado isoladamente. Não há E[L] agregado da carteira, nem correlação entre cenários — logo a "exposição agregada acima do apetite" da Fase 7 não é calculável |
| Decomposição por tipo de perda (resposta, recuperação, produtividade, receita, multa, reputação) | ❌ | A magnitude é uma faixa única; as parcelas do exemplo da metodologia não têm campos |
| **EPSS** | 🟡 | Chega **apenas** pela Vision One, gravado em `ToolFields["epss"]` (saco de chaves livres). Não é coluna, não é filtrável, não entra em priorização |
| **CISA KEV** | ❌ | Ausente. O Track 3 cita o benchmark de 14 dias para itens KEV, mas nada consulta o catálogo |
| **MITRE ATT&CK** | ❌ | Ausente |
| Exposição (interna / perimetral / externa) | ❌ | Não há campo; inferível no máximo por porta/serviço em `HostsService` |
| Privilégios e blast radius | ❌ | Ausente |
| Controles compensatórios | 🟡 | Texto livre em `RiskAcceptance.CompensatingControls`; não afeta a priorização |
| CVSS como entrada | ✅ | `Vulnerability` guarda vetor/score e `RawSeverity` (valor cru da ferramenta, para a decisão de mapeamento permanecer auditável) |
| **BIA — MTPD/MAO, RTO, RPO, cascata** | ❌ | Nenhum desses campos existe em processo, serviço ou ativo. É a lacuna que mais bloqueia a metodologia: a flag 4, o Portão A e a métrica "restauração testada vs. RTO declarado" todos dependem dela |

## 6. Fase 4 — Os quatro portões

| Portão | Estado | Instrumento |
|---|---|---|
| **A — Não discricionário** | ❌ | Não implementável hoje: depende das flags 1–4, que não existem. Um risco de segurança humana ou com exploração ativa não se distingue de outro de score igual |
| **B — Apetite / tolerância** | ✅ | `RiskAppetite` com limiar de dupla aprovação e teto de aceitação, avaliado na criação da aceitação (`POST /Risks/{id}/Acceptance`) e no portal; a decisão de não semear linha nenhuma é deliberada e o painel administrativo diz que a checagem está inativa |
| **C — Economia marginal** | 🟡 | Os ingredientes existem: `quant_ale_mean` antes/depois e `IStatisticsService.GetRisksVsCosts`. **Falta o cálculo** — `MitigationCost` é uma escala ordinal (tabela de rótulos), não um valor monetário, então `E[L antes] − E[L depois] > custo` não é computável |
| **D — Capacidade / portfólio** | ❌ | Não há otimização nem orçamento; sem custo monetário e sem agregação, a seleção de carteira sob restrição não existe |
| Decisão **Agir imediatamente** | 🟡 | Existe severidade e SLA, não um estado de escalonamento imediato com notificação obrigatória |
| Decisão **Tratar no ciclo** | ✅ | `MitigationPlanned` exigido pela máquina de estados (recusa com 422 sem mitigação), `mitigation_tasks` com dono, prazo e status |
| Decisão **Monitorar / Aceitar** | ✅ | Este é o ponto mais forte: aceitação é um **registro**, não um status — gestor autorizador, justificativa, controles compensatórios, **snapshot do residual no momento da decisão** e **expiração obrigatória**; avisos em T-30 e T-7 (tomando o limiar mais restritivo) e reabertura automática ao vencer |
| Decisão **Arquivar** com gatilho de reabertura | 🟡 | `Closure` + `CloseReason` registram o fechamento; **não há gatilho de reabertura** nem revisão trimestral do arquivo |
| Gatilhos de reabertura explícitos | 🟡 | Só o temporal (expiração de aceitação). Gatilhos por condição (novo fornecedor, nova regulação, KRI estourado) não existem |
| Calibração por backtesting de incidentes e near misses | ❌ | Não há confronto entre incidentes ocorridos e riscos previstos; a métrica "incidentes não previstos / falsos negativos" não é calculável |

## 7. Fase 5 — Tratamento

| Atividade | Estado | Instrumento |
|---|---|---|
| Quatro opções (evitar / reduzir / transferir / aceitar) | 🟡 | "Reduzir" (`Mitigation` + `PlanningStrategy`) e "aceitar" (`RiskAcceptance`) são completas. **Evitar** e **transferir/compartilhar** não são tipos de tratamento — quem transfere por seguro ou SLA registra em texto |
| Vínculo explícito cenário → controle | ✅ | `MitigationToControl` liga a mitigação ao `FrameworkControl`, com `ValidationOwner`, `ValidationDetails` e `ValidationMitigationPercent` |
| Eficácia estimada por controle e composição | ✅ | `MitigationPercent` por mitigação, composto por [`MitigationPercentResidualStrategy`](../../src/ServerServices/Governance/MitigationPercentResidualStrategy.cs) como `1 − Π(1 − pᵢ)` — dois controles de 60 % dão 84 %, não 120 % |
| Custo do controle | ❌ | Só ordinal (`MitigationCost` é uma tabela de rótulos). Sem valor monetário não há Portão C |
| Plano de ação: ação, dono, prazo | ✅ | `MitigationTask` (título, descrição, `OwnerId`, `DueDate`, status, `CompletedAt`) com notificações de vencimento |
| **Evidência de conclusão e critério de aceite** | ❌ | `MitigationTask` não tem campo de evidência nem de critério de aceite; anexo é por risco, não por tarefa |
| Domínios de controle prioritários | ✅ | `Framework` / `FrameworkControl` / `Family` / `ControlClass` são genéricos e suportam qualquer catálogo; frameworks de mercado são importáveis (Track 2.2.3) |
| Teste de controle e maturidade atual vs. desejada | ✅ | `FrameworkControlTest` + `FrameworkControlTestResult`; `ControlMaturity` e `DesiredMaturity` no controle, e `LastAuditDate`/`NextAuditDate`/`DesiredFrequency` no framework — é o análogo de **perfil atual vs. alvo** do NIST CSF 2.0 |
| Playbooks por cenário e tabletop | ✅ | `IncidentResponsePlan` → `IncidentResponsePlanTask` → execuções, com gate de aprovação (só plano aprovado é usável), dependências entre tarefas, atribuição a entidades responsáveis e trilha de ativação |
| Backup imutável testado / restauração vs. RTO | ❌ | Não há registro de teste de restauração nem RTO declarado para comparar |

## 8. Fase 6 — Uso responsável de IA

| Atividade | Estado |
|---|---|
| Casos de uso de IA com humano no circuito | ❌ O produto não emprega IA no fluxo de risco. Os pontos de extensão (`INetriskVulnerabilityClassificationPlugin`, `INetriskModelPlugin`) existem sem implementação, e o FaceID é biometria de autenticação, não apoio à decisão de risco |
| Proibições (IA não aceita residual, não aprova exceção, não encerra finding, não é dona de risco) | ✅ **por construção** — todo ato de aprovação exige um `User`: `AuthorizingManagerId`, `EntityRiskReviewer`, revisor da `MgmtReview`. A segregação de funções recusa revisor que seja submetente, dono ou gestor, **inclusive administradores**, e o *break-glass* exige motivo escrito persistido em `mgmt_reviews.segregation_override_reason`. Trabalho de fundo grava como ator `system`, não como ninguém |
| **Inventário de modelos** (finalidade, dados, fornecedor, versão) | ❌ Não existe. `Technology` não tem os campos e nada obriga o registro |
| Testes, métricas, explicabilidade, viés, drift, red team de IA | ❌ Ausentes |
| RAG com fontes e nível de confiança | ❌ Ausente (e o campo "nível de confiança" também falta no registro de risco — §4) |
| Riscos de IA no mesmo registro, com a flag 11 | ❌ O registro é genérico e aceitaria o risco, mas sem a flag 11 e sem inventário de modelo ele é indistinguível de qualquer outro |
| Logs completos e rollback de componente de IA | 🟡 A trilha de auditoria e a assinatura/verificação de plugin cobrem a instalação; não há telemetria de modelo |

## 9. Fase 7 — Monitoramento, métricas e aprendizagem

| Atividade | Estado | Instrumento |
|---|---|---|
| Cadência mensal / trimestral / anual / pós-incidente | ✅ | `RiskReviewCadenceJob` (07:30, **após** as duas passagens de expiração, para que a aceitação vencida de madrugada entre na lista de hoje); campanhas trimestrais alinhadas ao calendário com índice único `(entidade, período)` — idempotentes por construção |
| Regra do primeiro vencimento | ✅ | Risco nunca revisado vence **um intervalo de cadência após a submissão**, não imediatamente — a alternativa faz a primeira notificação cobrir o registro inteiro, que é como um canal é silenciado |
| Notificação em canal | ✅ | `NotificationChannel` / `NotificationSubscription` com `MinSeverity`, escopo por entidade e janela de digest; 15+ tipos de evento |
| Gatilho: incidente ou near miss | 🟡 | Incidente existe; *near miss* não é distinguido; incidente **não dispara** reavaliação de risco automaticamente |
| Gatilho: mudança de arquitetura, novo fornecedor, nova regulação, novo modelo de IA | ❌ | Nenhum desses gatilhos existe |
| **Gatilho: KRI acima da tolerância** | ❌ | **Não há KRI no sistema.** É a lacuna de maior alcance da Fase 7: os gatilhos, o Portão B por KRI e metade das métricas dependem dela |
| Painel executivo | ✅ | `IStatisticsService` (números, top riscos, grupos, top entidades, risco no tempo, impacto × probabilidade, risco × custo); Master Dashboard multi-entidade (Track 2.3.3) |
| Métrica: % de riscos com dono e evidência | 🟡 | Dono é consultável (`owner`, `manager`); "com evidência documentada" não, porque não há campo de evidência |
| Métrica: tempo de discovery até decisão | ❌ | Não medida |
| Métrica: **exposição agregada acima do apetite (E[L] e P95)** | ❌ | Depende de agregação de portfólio e de P95 — nenhum dos dois existe (§5) |
| Métrica: eficácia de controle e residual por domínio | ✅ | Tabela por entidade pré/pós-tratamento no relatório Detailed Entities Risks, ordenada pela **menor redução primeiro** |
| Métrica: tempo de correção de itens KEV | ❌ | Sem KEV. O equivalente disponível é SLA por severidade (`SlaConfiguration` com `MaxTriageDays`/`MaxRemediationDays`, vigência temporal e escopo por entidade) e `SlaDueDate` por finding |
| Métrica: restauração testada vs. RTO/RPO | ❌ | Ausente |
| Métrica: concentração em terceiros | ❌ | Sem registro de fornecedor |
| Métrica: riscos reabertos, incidentes não previstos, falsos negativos | ❌ | Ausente |
| Métrica: IA (precisão, recall, calibração, drift, override humano) | ❌ | Ausente |
| Auditoria independente / assurance | ✅ | Trilha por campo via [`GovernanceAuditInterceptor`](../../src/DAL/Auditing/GovernanceAuditInterceptor.cs) (interceptor de `SaveChanges`, então nenhum serviço esquece de chamá-lo), id de correlação por gravação, allowlist de nove tipos, retenção de 1 825 dias **efetivamente aplicada** por job noturno; pacote de evidências CSV/PDF/JSON do mesmo objeto, com as três regras de seleção que importam (aceitação vigente concedida antes do período **entra**; item de campanha não decidido **entra**; lista truncada pelo limite **diz que foi truncada**) |

## 10. Modelo de registro de risco — campo a campo

| Grupo | Campo da metodologia | Estado | Onde |
|---|---|---|---|
| Identificação | ID único | ✅ | `risks.id` + `reference_id` |
| | Objetivo estratégico | ❌ | Inexistente (há `objective` como texto no `businessProcess`, não ligado ao risco) |
| | Processo/atividade | 🟡 | Só via `entity_id` genérico, que pode apontar um `businessProcess` — mas é **um** vínculo, disputado com unidade de negócio |
| | Serviço de TI | ❌ | Não há catálogo de serviços |
| Propriedade | Dono de negócio | ✅ | `owner` → `OwnerUser` |
| | Dono técnico | 🟡 | `manager` cumpre o papel na prática; não há campo distinto |
| | Aprovador do residual | ✅ | `risk_acceptances.authorizing_manager_id`, com faixa de autoridade por `risk_levels` |
| Cenário | Ativo/dado | 🟡 | `RisksToAsset` / `RisksToAssetGroup` e vulnerabilidades ligadas; dado não é ligável |
| | Causa/ameaça | ❌ | Texto livre |
| | Vulnerabilidade | ✅ | Relação N:N `risks ↔ vulnerabilities` |
| | Evento central | ❌ | Texto livre |
| | Consequências | ❌ | Texto livre |
| Requisitos | Legais, regulatórios, contratuais | 🟡 | `control_number` e mapeamento a `FrameworkControl`; a coluna `regulation` foi **removida** (Track 6 fase 6a/6b, órfã) |
| Controles | Existentes, eficácia, evidências | ✅ 🟡 | `MitigationToControl` + `MitigationPercent`; evidência/fonte sem campo |
| Quantificação | LEF, LM, E[L] | ✅ | `quant_*` |
| | P95 / CVaR | ❌ | P90 é o máximo |
| Níveis | Inerente, atual/residual | ✅ | `calculated_risk`, `residual_risk` + histórico |
| | Alvo | ❌ | Ausente |
| Classificação | Flags 1–11 | ❌ | Ausentes |
| | Nível de confiança da evidência | ❌ | Ausente |
| Decisão | Agir/Tratar/Monitorar/Arquivar | 🟡 | `RiskStatus` (`New`, `MitigationPlanned`, `ManagementReview`, `Closed`) + aceitação; não há "Arquivar" nem "Agir imediatamente" |
| | Plano e prazo | ✅ | `Mitigation` + `MitigationTask` |
| Monitoramento | KRI/gatilhos | ❌ | Ausente |
| | Dependências correlacionadas | 🟡 | `ContributingRisk`, `RiskGrouping` |
| | Última / próxima revisão | ✅ | `MgmtReview` + `ReviewLevel` + `next_review_date_uses` (escolhe se a cadência segue o inerente ou o residual; qualquer valor diferente de `ResidualRisk` significa inerente, deliberadamente, porque a configuração é editável pelo usuário) |
| Aceitação | Dono, data, **expiração**, gatilho de reabertura | ✅ 🟡 | Tudo em `RiskAcceptance`, com renovação encadeada por `renewed_from_id` e revogação com motivo exportado; o gatilho de reabertura é só o temporal |

## 11. Contexto universitário

A metodologia dedica uma seção às particularidades de IES. O que o NetRisk oferece hoje:

| Particularidade | Estado |
|---|---|
| Governança colegiada e descentralizada | ✅ Hierarquia de entidades + escopo por `user_entity_roles`; o portal de revisão delega a decisão à unidade, que é exatamente o modelo colegiado |
| Identidades transitórias (alunos, visitantes, pesquisadores) | 🟡 SCIM (provisionamento/desprovisionamento) e SSO federado (SAML/OIDC) existem para os **usuários do NetRisk**; não há gestão nem risco de identidade da instituição |
| Dados de estudante, saúde, menores; pesquisa sensível | 🟡 `securityClassificationLevel` classifica; sem base legal, sem DPIA, sem marcação de dado sensível |
| Integridade acadêmica/científica como categoria de risco | ❌ Não semeada na taxonomia |
| Liberdade acadêmica como restrição ao controle | ❌ Sem representação |
| Laboratórios IoT/OT, HPC, BYOD | 🟡 Modeláveis como `Host`/`application`; sem tipo próprio nem atributos de OT |
| Calendário acadêmico como RTO institucional | ❌ Depende de BIA, que não existe |
| Edtech, publishers, nuvem científica, HECVAT | ❌ Sem registro de fornecedor |

## 12. Lacunas priorizadas

Ordenadas por quanto destravam da metodologia, não por esforço.

> **Estas lacunas são o escopo do [Track 9](../roadmap/TRACK_9_MIGR_TI_IA.md)**, agrupadas por
> dependência em 12 etapas. Cada etapa é precedida por uma especificação completa e mesclada, e
> nenhuma é entregue sem os testes que a especificação planejou.

1. **Cadeia de ligação objetivo → processo → serviço → dado → ativo.** Um risco precisa poder apontar
   objetivo estratégico, processo, serviço de TI e dado — hoje aponta uma entidade. Destrava o
   princípio nº 1, o grupo Identificação do registro e a métrica de cobertura de processos críticos.
2. **Cenário estruturado em quatro campos** (causa · vulnerabilidade/condição · evento central ·
   consequências) mais o **nível de confiança da evidência**. Sem isso as regras de qualidade da
   Fase 2 não são verificáveis e o registro não se distingue de um campo de anotações.
3. **As 11 flags obrigatórias** como campos consultáveis. É o pré-requisito único do **Portão A**, que
   hoje não existe — e é o portão que a metodologia declara não discricionário.
4. **BIA: MTPD/MAO, RTO, RPO e dependências em cascata** no processo e no serviço. Destrava a flag 4,
   a decisão "agir imediatamente", e as métricas de continuidade.
5. **KRIs com limiar e gatilho.** Destrava o Portão B por indicador, os gatilhos obrigatórios de
   reavaliação e cinco das dez métricas da Fase 7.
6. **Sinais de exploração na priorização: KEV, EPSS de primeira classe e ATT&CK.** O EPSS já entra
   pela Vision One num saco de chaves livres — promovê-lo a coluna, somar o catálogo KEV e a técnica
   ATT&CK é o menor esforço com maior efeito na Fase 3. Também destrava a flag 3 e a métrica de tempo
   de correção de item KEV.
7. **Custo monetário do controle** em `Mitigation`, ao lado da escala ordinal, e o cálculo
   `E[L antes] − E[L depois] − custo`. Destrava o **Portão C**, que hoje tem todos os ingredientes e
   nenhuma conta.
8. **Registro de fornecedor/terceiro** com HECVAT, SBOM, suboperadores, localização de dados, SLA,
   direito de auditoria, exit plan e medida de concentração. É a única frente de discovery (E) sem
   nenhum instrumento, e a metodologia a trata como central tanto em empresa quanto em IES.
9. **P95, CVaR e agregação de portfólio com correlação.** O Monte Carlo já está lá e é bom; falta a
   estatística de cauda que o apetite usa e a soma da carteira que a Fase 7 pede.
10. **Nível de risco "alvo"** e **evidência de conclusão + critério de aceite** em `MitigationTask`.
11. **Catálogo de dados com base legal, finalidade, retenção, localização e transferência (LGPD)**,
    e DPIA como artefato. Hoje a classificação existe e a conformidade não é demonstrável.
12. **Inventário de modelos de IA + flag 11 + métricas de modelo.** A Fase 6 inteira. Vale notar que
    as **proibições** da Fase 6 já são satisfeitas por construção, porque toda aprovação exige um
    usuário — a lacuna é o inventário e o assurance, não o controle de autoridade.
13. **Tipos de tratamento "evitar" e "transferir/compartilhar"**, e o estado **"Arquivar"** com
    gatilho de reabertura e revisão trimestral.
14. **Near miss** como registro distinto do incidente, e **backtesting** de incidentes contra riscos
    previstos — a calibração do corte que a Fase 4 exige.
15. **Papel de 3ª linha (auditoria) somente-leitura** e objeto **comitê de risco** como aprovador
    colegiado, em lugar de um gestor autorizador individual.

## 13. O que a metodologia pede e o NetRisk faz melhor do que o pedido

Vale registrar, porque a análise acima é uma lista de lacunas e desequilibra a leitura:

- **A crítica de Cox está implementada, não apenas citada.** O score composto é declarado heurística
  de fila no código e na documentação, com a justificativa de não comensurabilidade, e o caminho
  quantitativo é oferecido como a alternativa para quando o número precisa significar algo.
- **A composição de eficácia de controles é a correta.** `1 − Π(1 − pᵢ)`; somar produz residual
  negativo.
- **O mapeamento de faixa quantitativa usa a média, não a mediana** — o caso de cauda longa que a
  metodologia usa como exemplo ("uma vez por década, oito milhões") tem mediana zero.
- **A aceitação é um registro com snapshot do residual**, e não um status. Um registro que relê o
  score de hoje não mostra o que foi de fato acordado.
- **A segregação de funções recusa administradores**, e o break-glass persiste o motivo em campo
  exportado no pacote de evidências. Um override que ninguém encontra depois não é override.
- **A retenção da trilha de auditoria é aplicada**, não apenas documentada.
- **O heatmap não move os pontos no modo residual, deliberadamente**, e omite o risco sem residual em
  vez de desenhá-lo na posição inerente. Plotar um número derivado em eixos de
  probabilidade × impacto colocaria o risco numa célula onde ninguém o avaliou — é o mesmo cuidado
  que a metodologia pede ao separar visualização executiva de mecanismo de decisão.
