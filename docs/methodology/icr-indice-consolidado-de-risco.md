# ICR — Índice Consolidado de Risco: metodologia de análise e cálculo

> **Status:** Rascunho para revisão · 2026-10-05 · versão metodológica 1.0 (proposta, revisada após
> três revisões adversariais) · **S39**
>
> **Escopo:** como o NetRisk calcula um índice único de 0 a 100, com tendência, categorias
> contribuintes, sub-índices e drill-down por unidade, processo, atividade, aplicação e classe de
> ativo. O índice consolida o registro de riscos com suas revisões (revisões de gestão, aceitações,
> campanhas), vulnerabilidades, incidentes, avaliações, CMDB (Jira Assets), mapa de entidades e
> processos, Trend Vision One, SecurityScorecard e Tenable.
>
> **Base:** [migr-ti-ia.md](migr-ti-ia.md) (sumário) e [migr-ti-ia-guide.pdf](migr-ti-ia-guide.pdf)
> (autoritativo, citado como "PDF p.N"). A aderência atual está em
> [migr-ti-ia-coverage.md](migr-ti-ia-coverage.md). "Fase n" sempre significa a Fase n da MIGR-TI/IA;
> os passos de adoção do ICR chamam-se **Estágios A0–A4** (§15.2).
>
> **Derivados deste documento:** a especificação do painel "Visão Geral de Risco Cibernético", em
> [docs/features/risk-overview-dashboard.md](../features/risk-overview-dashboard.md) (**S40**), e o **Track 10** do [roadmap](../../ROADMAP.md) (marcos M52–M59, tarefas T236–T295).
>
> **Evidência de código:** as referências `arquivo:linha` foram conferidas contra a versão 2.24.0
> (`db_version` 87). Nada deste documento está implementado.

**Legenda de disponibilidade.** **[H]** disponível hoje · **[C]** requer correção de defeito (§14) ·
**[N]** requer dado ou integração nova.

---

## Emendas

| Data | O que mudou | Motivo |
|---|---|---|
| 2026-10-05 | **Escopo hierárquico:** uma claim numa entidade concede os seus descendentes na árvore (`entities.parent`), nunca os ascendentes nem os irmãos (§10.4, F-3, T292) | Decisão do product owner: quem tem acesso a uma unidade vê as subunidades, não o oposto |
| 2026-10-05 | **Aprovador do perfil:** papel Gerente de Riscos ou Administrador de Riscos, que representa o Comitê de Risco de TI no sistema (§13.1, T293) | Decisão do product owner |
| 2026-10-05 | **Tenable:** Vulnerability Management em nuvem com licença Tenable One; ACR, AES e CES ficam disponíveis com o conector (M56). Security Center vai para o backlog (T269) | Decisão do product owner |
| 2026-10-05 | **Vision One:** créditos CREM e permissões de API confirmados para `securityPosture`, `assetGroups`, objetos de superfície e Workbench (T271–T273) | Decisão do product owner |
| 2026-10-05 | **Master Dashboard** retirado depois da publicação do modo interino (§1.7, T294) | Decisão do product owner: dois painéis com números diferentes confundiriam |
| 2026-10-05 | **Público na publicação:** a equipe de riscos (analistas, gerentes e administradores de riscos); outros públicos depois, por concessão de `risk_index_view` (§15.2, T293) | Decisão do product owner |
| 2026-10-05 | **Rótulos das faixas em português e inglês**, aprovados juntos no perfil, como o resto do sistema (§4.5, §12.2, §12.4) | Decisão do product owner |
| 2026-10-05 | **Contexto de risco das entidades** (criticidade, internet-facing, classificação) editado só com escopo global; edição por gestores de unidade fica prevista para quando eles usarem o sistema (D-17, T295) | Decisão do product owner |

## Resumo executivo (leitura de 5 minutos)

**O que é.** O ICR é um número de 0 a 100 (**quanto maior, pior**) que resume a exposição a risco
cibernético de um escopo: a organização, uma unidade, um processo, uma atividade, uma aplicação ou uma
classe de ativo. Faixas padrão, iguais às do Vision One: **Baixo 0–30 · Médio 31–69 · Alto 70–100**.
O ICR serve para monitorar e comunicar. Ele **não aprova, não aceita e não prioriza** riscos; essas
decisões continuam nos Portões A→D da MIGR-TI/IA (§1.3).

**O que entra e quanto pesa.** Cada fonte vira um sinal de 0 a 1 por objeto (cenário, host, aplicação,
incidente). Os sinais formam cinco categorias, com pesos configuráveis (perfil "Equilibrado"):

| Categoria | O que mede | Fontes | Peso |
|---|---|---|---|
| REG | Residual dos cenários, com crédito só se revisados | Registro de riscos, revisões de gestão, campanhas | 30 % |
| EXP | Vulnerabilidades e postura dos ativos | Vision One, Tenable, SecurityScorecard, importadores, CMDB | 30 % |
| AME | Incidentes recentes | Incidentes | 15 % |
| CTL | Controles | Avaliações, planos de resposta, configuração do Vision One | 10 % |
| GOV | Disciplina do tratamento | Aceitações, apetite, campanhas, tarefas, SLA | 15 % |

A categoria Terceiros entra com 5 % quando existir o registro de fornecedores (M48). A criticidade
(CMDB e mapa de entidades) aumenta o peso de cada objeto. O mapa de entidades e processos diz a que
unidade, processo e atividade cada objeto pertence.

**Como as revisões contam** (§6). (1) Um residual só vale enquanto houver revisão de gestão qualificada
dentro da cadência. Sem ela, volta aos poucos ao risco inerente. Aceitar o risco ou decidir numa
campanha **não** renova esse crédito. (2) Aceitação vencida, aceite acima do apetite atual, item de
campanha pendente, tarefa ou SLA vencido e revisão solicitada sem resposta sobem GOV. (3) O ranking da
última campanha aumenta o peso, nunca o valor, dos cenários priorizados pelo negócio.

**Um problema grave não some na média.** A manchete nunca fica abaixo de 70 % da pior categoria, e uma
categoria nunca fica abaixo de 80 % do pior objeto: um servidor crítico não é diluído por vinte
máquinas de teste (§4.7). Dado ausente ou antigo nunca conta como seguro (§8).

**Quem muda os pesos** (§13). Os pesos ficam em perfis versionados. Uma mudança segue rascunho,
validação, prévia do efeito em 90 dias e aprovação por outra pessoa, citando a ata do Comitê de Risco
de TI. A troca fica marcada no gráfico de tendência.

**O que sai primeiro** (§15.2). Primeiro vêm as correções de dados (M52), depois o motor com snapshot
diário (M53), e então o painel e a configuração (M54–M55). Eles rodam 8–12 semanas em sombra, só para
o comitê, e só depois o modo **interino** chega aos gestores. No interino, processo e atividade mostram
registro, governança, incidentes e controles; a exposição técnica só fica completa com o M39 (no
processo, cobre apenas os achados sem host das suas aplicações; na atividade, ainda não existe).
Tenable, a ampliação de Vision One e SecurityScorecard e as correções de qualidade (M56–M58) vêm
depois, cada uma como nova versão de perfil. O modo **pleno** (M59) depende do Track 9.

**Painel:** ver o resumo executivo da [S40](../features/risk-overview-dashboard.md).

---

## 0. Síntese técnica

O **ICR** é um indicador **ordinal-heurístico de monitoramento**, de 0 a 100, em que **maior é pior**.
Ele resume a exposição de risco de um escopo e é sempre exibido junto com quatro informações:

- tendência;
- cinco categorias contribuintes;
- cobertura e qualidade dos dados;
- selos e flags da MIGR-TI/IA, que ficam fora do número.

O ICR **não decide nada**. Os Portões A→D e as quatro decisões continuam sendo o caminho de decisão
(regra R-1, §1.3). O ICR e seus sub-índices nunca são KRI com tolerância, nunca são comparados ao
apetite e nunca ordenam trabalho.

**As cinco categorias e seus pesos padrão (perfil Equilibrado):**

| Categoria | ω |
|---|---|
| REG — Cenários do registro | 0,30 |
| EXP — Exposição técnica | 0,30 |
| AME — Ameaça e eventos | 0,15 |
| CTL — Controles e conformidade | 0,10 |
| GOV — Governança e tratamento | 0,15 |
| TER — Terceiros | 0 até o M48 |

**O cálculo tem cinco passos, com o mesmo procedimento em todo nível do drill-down:**

1. **Normalização** de cada sinal para `[0,1]` por âncoras fixas por fonte. Nunca min-max nem
   percentil da população.
2. **Objeto.** Dentro de um fator, os sinais independentes combinam por "máximo + restante amortecido"
   (MRA, ρ = 0,30). Sinais sobrepostos combinam por **máximo**: o composto de um fornecedor e os
   achados que ele já contém, ou o mesmo CVE visto por duas ferramentas. Uma leitura antiga nunca
   some: ela mantém o valor ruim e só desliza para cima, em direção a um prior conservador.
3. **Categoria.** `C_k = 100 · max(M_4, 0,80 · max R_o)`. Os objetos são ponderados por criticidade,
   com cotas por população quando a categoria mistura cenários e ativos. A inclusão é conservadora e
   **por objeto**: cada objeto presumido entra se elevar o valor.
4. **Manchete.** `ICR* = max(Σ ω_k C_k / Σ ω_k, 0,70 · max C_k)`, sobre as categorias disponíveis.
   Com o padrão, `ICR* ≥ 56 · R` para qualquer objeto do escopo, e uma trava conjunta liga τ e τ_G às
   faixas: um objeto que sozinho está na faixa mais alta nunca deixa a manchete na faixa mais baixa.
5. **Piso de exibição.** Só o Portão A gera piso, e só a partir do predicado do M43 sobre um cenário.
   O valor sem piso é sempre guardado e mostrado. O teto ordinal de apetite entra como **selo**,
   nunca como piso.

**As revisões entram por três caminhos**, sempre pelos campos confiáveis hoje (§6):

- **Crédito de tratamento.** Sem revisão de gestão qualificada dentro da cadência, o residual regride
  ao inerente em REG. Aceitação e decisão de campanha **não** restauram crédito: aceitar não é
  revisar o residual.
- **Sinais de governança.** Aceitação vencida sem nova decisão, aceite anterior ao apetite vigente,
  deriva, teto de apetite excedido, escalonamento parado, item de campanha pendente, tarefa vencida,
  SLA vencido, revisão solicitada e não atendida, divergência entre evidência técnica e residual.
- **Rank de campanha.** Pondera o peso do cenário e nunca o seu valor.

**Configuração.** Pesos e parâmetros ficam em perfis versionados e imutáveis depois de aprovados. A
aprovação exige segregação de funções, referência à decisão do Comitê de Risco de TI e prévia de
sensibilidade, e a auditoria é obrigatória. Uma troca de versão é descontinuidade marcada na série.

**Dois modos de operação:**

- **Interino ("ICR-P"):** roda com os dados de hoje, depois do Estágio A0 (§15.2).
- **Pleno:** consome M39–M49 sem duplicá-los.

---

## 1. Posicionamento e princípios

### 1.1 O que o ICR é

- O **painel executivo** do roteiro da metodologia (dias 61–90) e um artefato da **Fase 7 —
  Monitoramento**.
- Uma medida **intensiva** no sentido de invariância por replicação: duplicar o inventário com a
  mesma distribuição não muda o valor, e o número de cenários registrados não o faz crescer. O piso
  de cauda é, por desenho, sensível ao tamanho: um escopo com mais objetos tem mais chance de conter
  um objeto ruim e tende a ler mais alto. É a escolha "cauda acima de comparabilidade" (§4.6); por
  isso a comparação entre unidades de tamanhos muito diferentes mostra a contagem de objetos e o chip
  "sustentado por", e deve ser feita também pelo ramo da média e pelos sub-índices.
- Um índice **composto**, no sentido do manual OECD/JRC de indicadores compostos:
  - a normalização é declarada;
  - a direção é única;
  - os pesos são juízos de valor aprovados, versionados e auditados.
- **Ordinal.** Diferenças entre dois períodos valem como *direção e causa* dentro da mesma versão do
  perfil. Não são magnitude de risco. "O dobro do risco" não tem significado. Por isso o painel exibe
  atribuições arredondadas a pontos inteiros ou em percentual, ordenadas (§9.4).

### 1.2 O que o ICR não é

1. **Não é um corte, um portão, um gatilho nem um KRI.** "O corte não deve ser um 'score mágico'"
   (PDF p.11). Nenhuma aceitação, arquivamento, escalonamento, SLA, notificação, alçada, campanha ou
   gatilho de reavaliação lê o ICR. O ICR e seus sub-índices **nunca** são registrados como KRI com
   tolerância no M46 nem comparados ao apetite ou à tolerância da Fase 0: o Portão B dispara quando
   "outro KRI" excede o limite aprovado (PDF p.11), e um ICR com tolerância viraria esse KRI.
2. **Não é uma probabilidade de violação nem uma perda.**
   - E[L], P95 e CVaR são grandezas monetárias da Fase 3 e do M45.
   - O painel mostra Σ E[L] **residual** ao lado do índice (a partir da T290; até lá, o inerente,
     rotulado como tal), porque médias somam, sempre com "n de N cenários quantificados" (§3.2,
     REG-ALE).
   - Percentis nunca são somados.
3. **Não é o CRI do Vision One, o rating da SecurityScorecard nem `entities.cyber_risk_index`.** Os dois
   primeiros são entradas: KRI de referência (quando o M46 existir) e sinal de exposição,
   respectivamente. A coluna legada **nunca é lida** (§1.7).
4. **Não é uma meta individual de desempenho.** Usado como meta, o índice convida a jogar com cobertura,
   criticidade e aceites (lei de Goodhart).
5. **Não substitui a lista "Top Risks" do M43 (T172).** O painel hospeda essa lista; não a reimplementa,
   nem a aproxima no interino.
6. **Não contém flags nem a confiança da evidência.** Ambas são "independente[s] da pontuação
   quantitativa" (PDF p.15) e ficam ao lado. A única interação permitida é o piso explícito do Portão A
   (§7), sempre acompanhado do valor sem piso.
7. **Não ordena tratamento.** A ordenação de tratamento é dos Portões C e D (redução de E[L] contra
   custo, preservando a cauda; PDF p.10–11). A "sensibilidade do indicador" (§4.8) diz quanto o ICR
   depende de cada objeto e não é lista de trabalho.

### 1.3 Regra R-1: linha de destaque ≠ linha de tratamento

O PDF (p.11) separa a "linha de destaque executivo" da "linha de tratamento". No NetRisk:

- **Nenhum serviço do caminho de decisão lê tabelas ou serviços do ICR.** Isso inclui
  `RiskWorkflowService`, `RiskAcceptancesService`, `RiskReviewCampaignsService`, `MgmtReviewsService`,
  `SlaService`, os jobs de notificação e de digest, o Portão A (M43), o Portão B (M45/M46), os KRIs e
  gatilhos do M46, o RiskPortal e os clientes.
- Um cenário pode aparecer em destaque no painel sem estar em tratamento, e o inverso também.
- O painel não oferece ações "tratar" ou "aceitar" derivadas do índice. O drill-down termina no
  cenário ou no objeto, que é onde vivem os portões e as decisões.
- **Nenhum backlog, fila de tratamento ou lista de trabalho ordena por Δ do ICR** nem pela
  sensibilidade do indicador.
- As **faixas** são categorias de exibição aprovadas "para comunicação", não tolerância.
- **Teste de arquitetura obrigatório:** `IcrIsNotADecisionInputTest`, em `ServerServices.Tests`.
  - É uma **allowlist**: por reflexão sobre todos os assemblies da solução (lidos do `netrisk.sln`),
    só podem referenciar o serviço do ICR ou as entidades `RiskIndex*`:
    - o próprio serviço e os seus jobs (`RiskIndexSnapshotJob`, retenção, prévia de sensibilidade);
    - os controllers do painel, de perfis, de exportação e de relatórios;
    - a camada REST do `ClientServices` — leitura do índice (`IRiskIndexService`) e ciclo de vida do
      perfil (`IRiskIndexConfigService`) — e as telas do painel e da configuração no `GUIClient`, que
      exibem o índice e editam perfis, mas não decidem nada;
    - as raízes de composição (`ServicesBootstrapper`, `ConfigurationManager`).
  - Qualquer outro tipo falha o teste, incluindo serviços de KRI, portões, portal, campanhas e
    qualquer cliente ou serviço do caminho de decisão. A página executiva do RiskPortal (T289,
    backlog), quando vier, entra como leitora nomeada. "Cliente", nesta regra, é quem decide com base
    no número; quem só o exibe está na allowlist.
- A função `Faixa` (§4.5) vive num módulo neutro (`Tools`), sem estado do ICR, porque também a usam
  serviços de decisão (alçada de aceitação, cadência); depender dela não é depender do ICR.

### 1.4 Lugar na MIGR-TI/IA

| Fase / elemento | Relação com o ICR |
|---|---|
| Fase 0 — Governança | O perfil de pesos e as faixas de comunicação são critérios aprovados (§13). O apetite (`risk_appetites`) é **insumo**; o ICR não o define |
| Fase 1 — Discovery | As frentes D (CMDB, varredura, EASM) e F (incidentes, KEV, EPSS) fornecem sinais. A cobertura do discovery vira a cobertura do ICR (§8) |
| Fase 2 — Cenários | Cada cenário aberto é um objeto da categoria REG. CVE, achado e alerta "não entram como risco autônomo": viram sinais dos objetos técnicos. A divergência entre evidência técnica e residual aparece em GOV.G; o **gatilho obrigatório** de reavaliação é do M46, nunca do ICR, e o residual nunca é reescrito |
| Fase 3 — Quantificação | A escala 0–10 do registro é triagem. Por isso o ICR se declara ordinal. E[L] e P95/CVaR ficam no painel monetário |
| Fase 4 — Portões | O ICR **não é entrada de nenhum portão**. O predicado do Portão A (M43) aparece como piso de exibição explícito. O teto ordinal de apetite do Track 8 é só uma **aproximação** do Portão B e entra como selo; o Portão B propriamente dito (P95 ou KRI acima da tolerância) só existe com o M45/M46 (§6.6) |
| Fase 5 — Tratamento | A execução do tratamento entra em GOV.T (tarefas vencidas, SLA vencido) |
| Fase 6 — IA | Nenhum componente é calculado por IA. Previsão de tendência fica bloqueada até o M50 |
| Fase 7 — Monitoramento | O ICR é o artefato desta fase: snapshot diário, leitura mensal pelo Comitê de Risco de TI, relatório trimestral ao Conselho/Reitoria, KRIs (M46) ao lado |

### 1.5 Invariantes

| # | Invariante | Onde é garantido |
|---|---|---|
| I1 | Escala 0–100, maior = pior, faixas configuráveis | §4.5, §12 |
| I2 | **Invariância por replicação.** Duplicar o inventário ou os cenários com a mesma distribuição não muda o índice. O piso de cauda é sensível ao tamanho do escopo, por desenho | §1.1, §4.6 |
| I3 | **Cauda preservada.** Um objeto com risco R sustenta a categoria em pelo menos `100·τ·R` e a manchete em pelo menos `100·τ_G·τ·R`. Em faixas: um objeto que sozinho está na faixa mais alta (`R ≥ L_topo/100`) nunca deixa a manchete na faixa mais baixa (trava conjunta da §12.4) | §4.3, §4.4, §12.4 |
| I4 | **Contagem não é risco.** Usa-se o composto do fornecedor **ou** os achados que ele já contém, nunca os dois | §3.4 |
| I5 | **Ausente ≠ zero.** Dentro de uma categoria, dado ausente ou obsoleto nunca baixa `C_k` e sempre reduz cobertura ou qualidade. Entre categorias, a renormalização pode mover a manchete para qualquer lado; por isso é divulgada pela COV e pelo intervalo de ignorância, sempre exibido quando falta categoria | §4.3, §8 |
| I6 | **Flags e confiança ao lado.** Só o Portão A gera piso explícito | §7 |
| I7 | **Mesmo cálculo em todo nível**, sobre o conjunto deduplicado de objetos | §10 |
| I8 | **Configuração controlada:** versionada, aprovada com segregação de funções, auditada, com descontinuidades marcadas. Os insumos de contexto que movem pesos (criticidade, internet-facing, classificação) exigem permissão própria e auditoria | §12, §13, §14.1 (D-17) |
| I9 | **O ICR não reescreve valores armazenados de avaliação humana:** residual, aceite, decisão. O residual efetivo `r_eff` é um ajuste de obsolescência do lado do indicador, exibido com o residual armazenado ao lado, nunca gravado de volta e nunca lido por outro serviço | §6.1, §6.3 (GOV.G) |
| I10 | **Aceite não é mitigação nem revisão.** Aceitar não reduz REG nem EXP e não restaura crédito de tratamento | §6.1, §6.2 |

### 1.6 As onze contradições que a metodologia veda

| Vedação | Como o ICR a cumpre |
|---|---|
| 1. Índice como corte ou decisão | R-1, a allowlist `IcrIsNotADecisionInputTest` e a proibição de registrar o ICR como KRI |
| 2. Média que dilui a cauda | Pisos `τ` e `τ_G` (§4), com trava conjunta ligada às faixas |
| 3. Somar P95/CVaR | Só Σ E[L]; percentis vêm do M45 |
| 4. Contagem como unidade de risco | MRA limitado, médias intensivas, regra composto **ou** achados |
| 5. Misturar escalas sem normalização, versão e declaração de ordinalidade | Âncoras fixas (§3.1), perfil versionado, I1. As comparações heurísticas entre escalas (GOV.G) são declaradas como tal |
| 6. CVSS como saída | CVSS é entrada de `s` (§3.1). No modo pleno vale a priorização T165 |
| 7. Ausente lido como seguro | I5 (§8) |
| 8. Somar ordinais por unidade, ou fazer roll-up de índices dos filhos | Cálculo por conjunto (§10) |
| 9. Mediana de perda, efetividade aditiva, escore derivado plotado nos eixos L×I | Nada disso é usado |
| 10. Dobrar flags ou confiança dentro do escore | I6. A confiança da evidência (M40) **não** entra na qualidade de dados |
| 11. Mudar pesos sem aprovação | §13 |

### 1.7 Nomes que não se confundem

| Nome | O que é | Papel no ICR |
|---|---|---|
| **ICR** | O índice desta metodologia | Saída |
| **ICR\*** | O ICR sem piso | Saída, sempre armazenada e exibida quando difere |
| `entities.cyber_risk_index` | Coluna legada. Contém a média ponderada Vision One (`TrendMicroService.cs:691-694`, gravada em `:700`) **ou** `100 − score` da SecurityScorecard (`SecurityScorecardService.cs:363`); vence a última sincronização | **Nunca lida.** Fica rebaixada a legado e é substituída pela tabela de escores por fonte (§15) |
| CRI do Vision One (`securityPosture.riskIndex`, `assetGroups`) | Índice do fornecedor, calculado sobre os mesmos dispositivos | Indicador de referência (KRI quando o M46 o registrar) e verificação de sanidade na calibração. Fora da soma, porque contaria os scores de dispositivo duas vezes |
| `PostureScore` do Master Dashboard | Contagem ponderada e saturada (`MasterDashboardService.cs:270-282`) | Não usado. O Master Dashboard é retirado depois da publicação do modo interino (T294) |
| `ContributingScore` / escore total | Heurísticas de triagem que crescem com a contagem (`RiskCalculationService.cs:106-120`) | Não usados |
| **Indicador** × **KRI** | Indicador é qualquer número exibido ao lado sem tolerância; KRI é o indicador registrado no M46 com tolerância e regra de obsolescência | No interino só há indicadores |

---

## 2. Modelo de objetos

Só objetos recebem valor. Escopos são **conjuntos de objetos**.

### 2.1 Objetos pontuáveis

| Objeto | Identidade | Categorias (população) | Criticidade usada | Disp. |
|---|---|---|---|---|
| **Cenário** (risco aberto: `risks.status <> 'Closed'`) | `risks.id` | REG; GOV e CTL.P (população "cenários") | Impacto do cenário (§5.4) só no **peso** | [H] |
| **Host** (servidor, estação, VM). Status ≠ Retired (27); exclui o pseudo-host da SSC | `hosts.id` | EXP; GOV (população "ativos", SLA); CTL.A (população "avaliações", se for alvo de avaliação) | `crit` do host (§5.1) | [H]; atribuição a entidade [C] D-20 |
| **Domínio externo** (SecurityScorecard) | `securityscorecard_connections.id`. O SSC-TOT entra por `security_scorecard_factors.connection_id`; o pseudo-host (`external_provider = 'SecurityScorecard'`) serve só para excluí-lo do inventário de dispositivos, porque ele só existe quando houve ingestão de issues ou CVEs (`SecurityScorecardService.cs:211-215`, `:395`) | EXP | `crit_ext` (padrão 4) | [H] |
| **Aplicação com achados sem host** (código, dependências) | `entities.id` do `vulnerabilities.entity_id` | EXP; GOV (população "ativos", SLA) | `crit` da entidade | [C] D-21: a importação carimba o escopo de quem importa, e um chamador irrestrito grava nulo (`VulnerabilitiesController.Aspm.cs:430-446`). No interino a atribuição só existe quando quem importou tinha escopo em exatamente essa entidade; o resto vai para "Não atribuído" (§3.4, regra 3) |
| **FQDN/IP internet-facing** do Vision One | id do fornecedor | EXP | Criticidade do fornecedor | [N] |
| **Identidade** (contas de domínio e de serviço do Vision One) | id do fornecedor | EXP | Criticidade do fornecedor | [N] |
| **Ativo de nuvem / app local** do Vision One | id do fornecedor | EXP | Criticidade do fornecedor | [N] |
| **Entidade-alvo de incidentes e avaliações** (organization, organizationUnit, subOrganizationUnit, businessProcess, application, e qualquer entidade impactada) | `entities.id` | AME; CTL.A (população "avaliações") | `crit` da entidade | [H] |
| **Tenant Vision One** (configuração dos agentes) | conexão | CTL.C (população "tenants") | `crit` da entidade da conexão (padrão 4) | [N] |
| **Fornecedor** | registro do M48 | TER | Criticidade do fornecedor | [N] |

### 2.2 Nós de agregação

Os nós **não têm sinal próprio**. O índice de um nó é a fórmula aplicada ao conjunto deduplicado de
objetos que o fecho do nó alcança (§10).

| Nó | Origem | Disp. |
|---|---|---|
| Organização → unidade → subunidade | `organization`, `organizationUnit`, `subOrganizationUnit`, pelo fecho de `entities.parent` mais os vínculos EAV (§10.1) | [C]: nenhum serviço percorre a árvore; os vínculos EAV não são relações tipadas (D-17) |
| Processo | `businessProcess` (`isRoot: True`; unidade pela propriedade EAV multivalorada `organizationUnit`, `src/API/EntitiesConfiguration.yaml:346-392`) | [H] para REG, GOV, AME e CTL; EXP parcial (aplicações) e hosts [N] (M39) |
| Atividade | Tipo `activity` filho de `businessProcess` pela árvore (`entities.parent`) | [N], barato: versão nova do YAML (T242). No interino recebe REG, GOV, AME e CTL como o processo (§10.2); EXP requer o M39 |
| Aplicação | `application`; ligada a processos pela propriedade EAV `applications` | [H] parcial |
| Serviço de TI | `itService` (M39, T145) | [N] |
| Dado | `organizationData` | [N] (M49) |
| Classe de ativo, fonte, ambiente, criticidade | Atributos do objeto | [H]/[C] |
| Terceiro | M48 | [N] |

Os nós processo, aplicação e serviço também **transmitem criticidade** aos objetos que servem (§5.2).

### 2.3 Categorias, fatores e pesos

Há três tipos de fator:

- **V (verossimilhança):** o valor é multiplicado pelo modulador de criticidade `m(crit)`.
- **C (composto):** o fornecedor já embute o impacto, então `m(crit)` não é aplicado de novo.
- **S (escalado pelo cenário):** o valor é multiplicado pela **severidade declarada** do cenário
  `σ_ref = (r_res ?? r_inh)/10`, que não sofre o decaimento do crédito (§6.1).

`u_f` é o **teto do fator**: o valor máximo que o fator pode atingir sozinho no objeto. A coluna
`m_net` diz onde o modulador de exposição à internet (§5.3) se aplica; nos demais fatores ele nunca se
aplica.

| Categoria (ω padrão) | Fator | Tipo | `u_f` | `m_net` | Operador entre fatores do objeto |
|---|---|---|---|---|---|
| **REG — Cenários do registro (0,30)** | REG.R Residual efetivo `σ_r = r_eff/10` (§6.1) | — | 1,0 | não | — |
| **EXP — Exposição técnica (0,30)** | EXP.D Composto de dispositivo: `max(V1, AES)` | C | 1,0 | não | **MAX**: as famílias se sobrepõem |
| | EXP.V Vulnerabilidades e achados (varredura e importadores; Vision One só sem EXP.D do V1) | V | 1,0 | **sim** | |
| | EXP.X Superfície externa: rating SSC (V); FQDN/IP do V1 (C) | V/C | 1,0 | não | |
| | EXP.I Identidades | C | 1,0 | não | |
| | EXP.N Nuvem e aplicações locais | C | 0,8 | não | |
| **AME — Ameaça e eventos (0,15)** | AME.I Incidentes | V | 1,0 | não | MRA, ρ = 0,30 |
| | AME.Q Quase-incidentes (M40) | V | 0,5 | não | |
| **CTL — Controles e conformidade (0,10)** | CTL.A Resultado de avaliações (experimental) | V | 0,8 | não | MRA, ρ = 0,30 |
| | CTL.P Prontidão de resposta (IRP) | S | 0,6 | não | |
| | CTL.C Configuração de segurança dos agentes (V1) | V | 0,8 | não | |
| **GOV — Governança e tratamento (0,15)** | GOV.A Apetite e aceitação | S | 1,0 | não | MRA, ρ = 0,30 |
| | GOV.C Decisão de campanha | S | 0,8 | não | |
| | GOV.T Execução do tratamento: tarefas (S); SLA no host (V) | S/V | 0,8 | não | |
| | GOV.R Integridade da revisão | S | 1,0 | não | |
| | GOV.G Divergência entre evidência técnica e residual (heurística) | S | 0,8 | não | |
| **TER — Terceiros (0; 0,05 a partir do M48)** | Postura externa do fornecedor · concentração por processo crítico · diligência (HECVAT/SBOM) | V | 1,0 | não | MRA |

**Populações.** GOV e CTL misturam objetos de naturezas diferentes. Para que o número de hosts não
afogue a governança dos cenários, nem o inverso, cada categoria mista divide o peso entre
**populações** por cotas fixas do perfil (§4.3): GOV = {cenários 0,5; ativos 0,5}; CTL = {avaliações
0,5; cenários 0,5}, e tenants em cota igual quando houver. REG, EXP e AME têm uma população só.

**Regras dos pesos de categoria:**

- Σω = 1.
- Cada ω é 0 (categoria desligada) ou fica em `[0,05; 0,60]`.
- `ω_REG ≥ 0,20`: o registro nunca é marginal.
- Pelo menos 3 categorias com ω > 0.
- A entrada do TER (M48) é uma nova versão do perfil com ω_TER = 0,05. As demais categorias são
  multiplicadas por 0,95.

### 2.4 Operadores: quando MAX, quando MRA, quando média

| Relação entre os itens | Exemplo | Operador | Por quê |
|---|---|---|---|
| **Sobrepostos:** medem o mesmo fato | Composto V1 do host × achados do mesmo host; mesmo CVE do Nessus e do Vision One; V1 × AES no mesmo host | **MAX** | Idempotente: nunca conta duas vezes |
| **Independentes** no mesmo objeto | CVEs distintos de um host; incidentes distintos de uma entidade; falhas de governança distintas de um cenário | **MRA** (§4.2) | O pior item define o patamar; os demais somam no máximo ρ do espaço restante |
| Objetos de um escopo numa categoria | Hosts de uma unidade | **Média de potência com piso de cauda e cotas por população** (§4.3) | Invariante por replicação, preserva a cauda e responde a toda correção |
| Categorias → manchete | REG, EXP, AME, CTL, GOV | **Média ponderada com piso de cauda** (§4.4) | Composição explicável, com garantia de cauda |

---

## 3. Catálogo de sinais

Todo sinal é normalizado para `s ∈ [0,1]`, com **1 = pior**, por **âncoras fixas**. Min-max e
percentil da população são proibidos, porque o valor de um objeto mudaria quando outros objetos
mudassem.

### 3.1 Âncoras de normalização

**Achado** (`vulnerabilities`):

```
b   = VprScore/10   se houver VPR
    › Cvss3BaseScore/10 › CvssBaseScore/10
    › âncora de severidade {1: 0,20 · 2: 0,50 · 3: 0,75 · 4: 0,95}   (severidade 0 é excluída)

sem evidência de exploração:   s = min( teto_sem_exploração , b · m_x · m_net )           teto 0,90
com exploração conhecida ou observada:
                               s = max( piso_exploração , min(1, b · m_net) )             piso 0,95
```

- **Exploração conhecida ou observada:** KEV (M42, T163); `ExploitedByScanner = true`;
  `exploitAttemptCount > 0` do Vision One, depois de persistido em separado [C] D-11. O piso vale
  **qualquer que seja a origem de `b`**, inclusive VPR.
- **Ordem pretendida, garantida pelas duas constantes:** todo achado com exploração (≥ 0,95) fica
  acima de todo achado sem exploração (≤ 0,90). A validação exige `teto_sem_exploração <
  piso_exploração`. A âncora 0,95 de severidade crítica só aparece integral com exploração e no SLA
  (`a_sev`, §6.3).
- **Severidade.** Lida pelo parser tolerante `SlaService.ParseSeverity`
  (`src/ServerServices/Findings/SlaService.cs:353`). Os parsers estritos do Master Dashboard e de
  `HostsService` não são usados.
- **`m_x` (exploit disponível).** Aplica-se **só quando `b` veio de CVSS ou severidade**, porque o VPR
  já incorpora ameaça.

  | Evidência | `m_x` |
  |---|---|
  | Exploit disponível (`ExploitAvaliable`), ou EPSS ≥ 0,10 (M42/T162) | 1,15 |
  | Demais casos | 1,00 |

  Hoje o flag do Vision One mistura "tentativas contra o dispositivo" com "atividade global alta"
  (`TrendMicroClient.cs:517-526`). Por isso, no modo interino, um achado `trendmicro-visionone` com o
  flag recebe `m_x` = 1,15, e não o piso de exploração.
- **`m_net` (exposição à internet).** Vale 1,2 **só nos sinais de achado (EXP.V)** de objeto
  internet-facing (§5.3). Não se aplica a incidentes, avaliações, SLA, compostos de fornecedor nem ao
  rating da SSC, que já mede exposição.
- **Modo pleno.** `b · m_x` é substituído pela priorização combinada do M42 (T165), que usa CVSS como
  entrada. O teto e o piso permanecem. O CVSS é **entrada, nunca saída**.

**Compostos de fornecedor (tipo C).** O impacto já está embutido, então não recebem `m(crit)`:

- Vision One `latestRiskScore/100`, persistido em `hosts.risk_score`. O próprio fornecedor o calcula
  como `√(verossimilhança × impacto)`.
- Tenable AES/1000 [N].
- `latestRiskScore/100` de FQDN/IP, contas, nuvem e apps do Vision One [N].

**SecurityScorecard**, só a linha `is_overall` de `security_scorecard_factors`. A escala da SSC é
maior = melhor, então a curva é invertida e linear por partes:

| Score SSC | 100 | 90 | 80 | 70 | 60 | 50 | 0 |
|---|---|---|---|---|---|---|---|
| `s` | 0,00 | 0,10 | 0,35 | 0,55 | 0,75 | 0,90 | 1,00 |

- A curva é **monótona e linear por partes**, coerente na ordem com as razões de probabilidade de
  violação publicadas no Scoring 3.0 (B 2,9×, C 5,4×, D 9,2×, F 13,8× em relação a A). A âncora em 50
  evita que a faixa F fique plana. Exemplos: 72 → 0,51; 59 → 0,765; 50 → 0,90.
- O `100 − score` linear de hoje subestima a SSC: um F com score 50 viraria 0,50.
- O valor de `s` é multiplicado por `m(crit_ext)`. `m_net` não se aplica.
- Fatores da SSC aparecem **só no detalhamento**. Fator ausente é NULL, nunca 0 (§14, D-12).

**Incidente** (`incidents`):

| Categoria | `s` | | Severidade (quando existir) [C] | `s` |
|---|---|---|---|---|
| `data_breach` | 0,90 | | crítica | 1,00 |
| `unauthorized_access`, `malware_infection`, `insider_threats` | 0,75 | | alta | 0,80 |
| `denial_of_service` | 0,60 | | média | 0,50 |
| `phishing`, `other_social_engineering`, `not_specified` | 0,50 | | baixa | 0,20 |
| `other` | 0,40 | | | |

- Quando o campo de severidade existir, ele **substitui** a âncora de categoria.
- `not_specified` vale 0,50: desconhecido não é leve.
- Decaimento: `d = 1` enquanto o incidente está aberto. Depois do encerramento,
  `d = 2^(−idade/30 d)`, com `idade = agora − data de encerramento` (§9.2), e o incidente sai da
  janela 90 dias após o encerramento, quando `d` já vale 0,125.
- Quase-incidente (M40, T153): o mesmo `s`, com `u_AME.Q = 0,5`.

**Avaliação** (`assessment_runs`):

```
s = Σ RiskScore da opção escolhida / Σ RiskScore máximo da questão, sobre as questões visíveis
```

- Usa só a última execução Submitted por (avaliação, alvo).
- É rotulada **experimental** enquanto a junção for por texto (§14).

**IRP**, por cenário aberto, com rampa contínua em `σ_ref` (sem degrau):

| Situação | `s_IRP` |
|---|---|
| Plano aprovado e testado há ≤ 365 dias | 0 |
| Plano aprovado sem teste recente | 0,5 |
| Sem plano vinculado (`Risk.IncidentResponsePlanId` ou `RelatedRisks`), ou não aprovado | 1,0 |

```
rampa(σ_ref) = clamp( (σ_ref − (θ − h)) / (2h) ; 0 ; 1 )        θ = 0,70 ; h = 0,10  (0 em 0,60, 1 em 0,80)
```

**Configuração do Vision One** [N]: `s = 1 − média(agentFeatureStatus[].adoptionRate)`.

**Cenário:** `σ_r = r_eff/10` em REG e `σ_ref = (r_res ?? r_inh)/10` em GOV e CTL.P (§6.1). O
impacto já está na matriz, então `m(crit)` não se aplica.

### 3.2 Catálogo por fonte

W é a janela de validade (§8.2). "Dedup" é a regra que garante que cada fato entra uma vez.

| ID | Fonte / campo | Cat. · fator | Normalização | W | Dedup / regra | Disp. |
|---|---|---|---|---|---|---|
| **Registro** | | | | | | |
| REG-RES | `risk_scoring.residual_risk ?? calculated_risk` + crédito de revisão | REG.R; `σ_ref` em GOV/CTL.P | `σ_r = r_eff/10` | — (crédito) | Um cenário conta uma vez por escopo, mesmo com vários vínculos | [C] D-01: o residual clássico só é gravado pela passada `ResidualRiskCalculation` (`RiskCalculationService.cs:181`), que hoje lê zero riscos; até a correção, `r_eff = r_inh` em todo cenário clássico. [C] D-03 para `scoring_method = 3` |
| REG-IMP | `ClassicImpact` (ou faixa de impacto M38 para quantitativos) | peso `w_r` | `m(impacto)` | — | — | [H] |
| REG-RANK | `risk_review_campaign_items.rank` da campanha concluída mais recente | peso `m_rank` | §6.5 | 2 ciclos | — | [H]; [C] depende de `risks.entity_id` |
| REG-ALE | E[L] **residual** por cenário; o inerente (`QuantAleMean`) é exibido à parte | Painel monetário Σ E[L] | Moeda; médias somam | — | Por cenário; sempre com "n de N cenários quantificados" e a fração do peso de REG quantificada. Abaixo de `cobertura_quantificacao_min` (padrão 0,80) o total é rotulado "parcial"; nunca há total nu | [N] para o residual: o simulador calcula a média residual (`QuantitativeRiskService.cs:122`) mas só persiste P10/P50/P90 (`RiskScoring.cs:66-70`); requer `quant_residual_ale_mean` (T290). [H] para o inerente, só `scoring_method = 3` |
| REG-P95 | P95/CVaR de portfólio | Painel e selo | Só M45 | — | Simulação conjunta | [N] M45 |
| REG-FLAG | Flags 1–11, predicado do Portão A | Piso PA-1 e colunas | — | — | — | [N] M43 |
| REG-CONF | Confiança da evidência | Exibida ao lado | — | — | — | [N] M40 |
| **Revisões, aceites, campanhas, tarefas, apetite** | ver §6.3 | GOV; crédito em REG | §6 | estado | §6.4 | [H]/[C] |
| **Vision One** | | | | | | |
| V1-DEV | `latestRiskScore` → `hosts.risk_score` | EXP.D | `s/100` (C) | 7 d (`risk_score_updated_at`) | Enquanto o host tiver leitura V1-DEV vigente, os CVEs Vision One do mesmo host não entram em EXP.V. MAX com TEN-AES | [H] só por sincronização manual de administrador; [C] D-01 na agendada (ressalvas D-10, D-11) |
| V1-CVE | CVEs de `vulnerableDevices` | EXP.V (leitura própria "Vision One") | Base de achado | 7 d (maior `last_detection` dos achados `trendmicro-visionone` do host) | Só entram quando o host não tem nenhuma leitura V1-DEV | [H] manual; [C] D-01 na agendada |
| V1-EXP | `exploitAttemptCount > 0` / `globalExploitActivityLevel = high` em separado | Piso de exploração / `m_x` 1,15 | — | — | Hoje viram o mesmo `ExploitAvaliable` | [C] D-11 |
| V1-EPSS | `epss` | `m_x` | ≥ 0,10 → 1,15 | — | Hoje descartado em `ToolFields` (`TrendMicroService.cs:772-783`) | [C] T162 |
| V1-IFA | `attackSurfaceGlobalFqdns` / `PublicIpAddresses` | EXP.X | `s/100` (C) | 7 d | Objeto distinto do domínio SSC (§3.4) | [N] |
| V1-ACC | Contas de domínio e de serviço, `accountCompromiseIndicators` | EXP.I | `s/100` (C) | 7 d | Por conta | [N] |
| V1-CLD / V1-APP | `attackSurfaceCloudAssets` / `attackSurfaceLocalApps` | EXP.N | `s/100` (C) | 7 d | Por ativo | [N] |
| V1-SCS | `securityPosture.securityConfigurationStatus` | CTL.C | `1 − adoptionRate` | 7 d | Objeto tenant | [N] |
| V1-WB | Alertas do Workbench, OAT | **Indicador** "intensidade de ataque" (KRI no M46) | Contagem / 30 d | 30 d | Alerta bruto não é risco (Fase 2) | [N] |
| V1-CRI | `riskIndex`, `assetGroups` | **Indicador de referência** (KRI no M46) | — | 2 d | Fora da soma (I4) | [N] |
| **SecurityScorecard** | | | | | | |
| SSC-TOT | Score geral (`is_overall = 1`, mais recente), por `connection_id` | EXP.X (objeto domínio = conexão) | Âncoras §3.1 × `m(crit_ext)` | 7 d (`captured_at`) | Issues SSC **nunca** pontuam. Fatores só no detalhe | [H] (a linha de postura não é escopada); [C] D-12. O pseudo-host e os achados da sincronização agendada ficam [C] D-01 |
| SSC-ISS | Achados com `import_source = 'securityscorecard'` | — | — | — | Excluídos de EXP.V e do SLA | [H] |
| SSC-HIST | `/history/score` | Série de EXP.X (backfill) | — | — | Recalibração mensal e penalidade de violação viram anotações | [N] |
| SSC-PORT | Portfólios de fornecedores | TER | Âncoras × criticidade do fornecedor | 7 d | — | [N] M48 |
| **Tenable** | | | | | | |
| TEN-NES | `.nessus` (VPR, CVSS, exploit) | EXP.V | Base de achado | 30 d | (host, CVE) | [H] |
| TEN-VM | Export `/vulns/export` (VPR v2, EPSS) | EXP.V | idem | 30 d | idem. VPR anterior a 2026-07-01 é anotado como modelo antigo | [N] |
| TEN-AES | `ratings.aes.score` (0–1000) | EXP.D | AES/1000 (C) | 30 d | Exclui achados do conector Tenable no mesmo ativo; MAX com V1-DEV | [N] M56; licença Tenable One confirmada (2026-10-05) |
| TEN-ACR | `ratings.acr.score` (1–10) | Criticidade | `⌈ACR/2⌉` | 30 d | Abaixo do CMDB na precedência | [N] M56; licença Tenable One confirmada |
| TEN-CES | CES / Exposure Score do Tenable One | **Indicador** (KRI no M46) | — | — | Fora da soma | [N] M56 |
| **Demais importadores** | sarif, semgrep, zap, trivy, openvas, burp, snyk, grype, dependabot | EXP.V | Base de achado | 30 d (`scan_imports` da entidade, para achados sem host) | Sem CVE: (host, ferramenta, `rule_id`). Sem host: §3.4, regra 3 | [H] com host; [C] D-21 sem host |
| **Achados (todos)** | Predicado "exposto" (§3.3); `sla_due_date` | EXP.V; GOV.T (SLA) | §3.1; §6.3 | estado | §3.4 | [C] D-04 (predicado interino na §3.3) |
| **Incidentes** | | | | | | |
| INC | Incidente aberto, ou fechado na janela | AME.I | §3.1 × `d` | evento (meia-vida 30 d) | Objeto = `impacted_entity_id ?? entity_id` | [H]; [C] D-15 |
| INC-NM | Quase-incidente | AME.Q | idem × 0,5 | idem | — | [N] M40 |
| IRP | `incident_response_plans` aprovado e testado | CTL.P | §3.1, com rampa | estado | Todo cenário aberto é membro; a rampa zera os de `σ_ref ≤ 0,60` | [H] |
| **Avaliações** | | | | | | |
| ASM | Última execução Submitted por (avaliação, alvo) | CTL.A | Razão 0–1 | 365 d | — | [C] D-16 (experimental) |
| **CMDB / Jira Assets** | | | | | | |
| CMDB-POP | Hosts e aplicações ativos | Inventário (denominador) | — | ciclo de vida | `DeactivateMissing` aposenta | [H] |
| CMDB-CRIT | Criticidade mapeada | `crit` | §5.1 | 30 d | Precedência §5.1 | [C] D-10 |
| CMDB-ENV | Ambiente | Padrão de criticidade não produtiva; dimensão | Vocabulário controlado | 30 d | — | [H] (texto livre normalizado pelo vocabulário `ambientes_nao_producao` do perfil) |
| CMDB-INET / CMDB-REL | Internet-facing; relação app↔servidor | `m_net`; vínculos | — | 30 d | — | [N] T291 |
| **Mapa de entidades** | | | | | | |
| ENT-TREE | `entities.parent` e vínculos EAV `organizationUnit` / `applications` | Fecho do drill-down | — | — | §10.1 | [C] D-17 |
| ENT-CRIT | Propriedade EAV `criticality` (1–5) em businessProcess, application, organizationUnit, subOrganizationUnit | `crit` e herança | §5.2 | — | Edição só com permissão própria e auditada | [N] (barato: versão do YAML) + [C] D-17; Estágio A0 |
| ENT-CLASS | `securityClassification` | +1 de criticidade | §5.2 | — | Edição com permissão própria (D-17) | [H] parcial |
| ENT-INET | Propriedade EAV `internetFacing` em application | `m_net` | — | — | Edição com permissão própria (D-17) | [N] (barato: mesma versão do YAML); Estágio A0 |
| ENT-ACT | Tipo `activity` | Dimensão | — | — | — | [N] |

### 3.3 Predicados

| Predicado | Definição | Observação |
|---|---|---|
| Achado **exposto** (entra em EXP.V) | `LifecycleStatus ∈ {Active, Verified, RiskAccepted}` **e** `import_source ∉ {'assessment', 'securityscorecard'}` **e**, enquanto D-04 não for corrigido, **não** (`Status` legado ∈ `ClosedStatuses` **e** nenhum `last_detection` posterior à última ação de fechamento registrada em `nr_actions`) | `RiskAccepted` permanece (I10). FalsePositive, OutOfScope, Duplicate e Mitigated saem. A cláusula interina existe porque o desktop fecha e rejeita achados gravando **só** o `Status` legado (D-04); uma redetecção posterior ao fechamento reabre o achado para o ICR, porque a reativação da ingestão não toca o `Status` legado (`FindingIngestionService.cs:787-792`). O número de achados em que as duas colunas discordam é publicado como métrica de qualidade |
| Achado **aberto para SLA** | `FindingStatus.IsOpen` (Active/Verified, `src/DAL/Enums/FindingStatus.cs:60-61`), a mesma cláusula interina de D-04 e `sla_due_date` não nulo | Achados aceitos não acumulam SLA. Achado aberto **sem** `sla_due_date` deixa o host "não avaliado" em GOV (§8.3) |
| Cenário **aberto** | `risks.status <> 'Closed'` (texto, não `status_id`) | Igual ao resto do produto; `status_id` não é mantido |
| Aceitação **válida** | `Status = Active ∧ ExpiresAt > agora` | Por data, igual a `RiskWorkflowService.cs:117-121`. Independe do job de expiração |
| Revisão **qualificada** | `MgmtReview` que (i) não foi gravada por uma aceitação nem por uma decisão de campanha e (ii) cujo `Reviewer ∉ {SubmittedBy, Owner, Manager}` do risco | §6.1. A regra (ii) é a segregação de funções do Track 8 (8.3.2), que o caminho desktop não aplica (D-07) |
| Host **ativo** | `hosts.status ≠ 27` (Retired) e `external_provider ≠ 'SecurityScorecard'` | O pseudo-host da SSC não é máquina |
| Incidente **aberto** | `status ∉ ClosedStatuses` | Status de incidente usa `IntStatus` |
| **Intake de incidentes ativo** | Algum incidente criado na organização nos últimos 90 dias, **ou** integração Workbench habilitada | Sem intake, AME fica "indisponível", não zero |

### 3.4 Deduplicação: cada fato entra uma vez

1. **Composto contra achados do mesmo fornecedor.**
   - Enquanto o host tiver **qualquer** leitura V1-DEV vigente (mesmo obsoleta, que permanece pelo
     valor ruim, §8.2), os CVEs `trendmicro-visionone` desse host não entram em EXP.V.
   - Enquanto houver leitura TEN-AES, os achados do conector Tenable VM no mesmo ativo não entram.
   - Os achados de outras ferramentas permanecem em EXP.V, e a combinação por **MAX** entre famílias
     impede a soma.
2. **Issues da SSC nunca pontuam.** O score já as contém. Também não entram no SLA.
3. **Chaves de deduplicação.**
   - Entre scanners: (host, CVE), mantendo o maior `s`. A chave de hoje inclui `tool`
     (`src/ServerServices/Importers/Dedup/DedupFieldSet.cs:20`), então o mesmo CVE do Nessus e do
     Vision One vira dois achados. O ICR deduplica no cálculo.
   - Sem CVE: (host, ferramenta, `rule_id`).
   - Achado sem host: (entidade, repositório ou projeto, `rule_id` ou CWE, `location`). Quando a
     entidade é nula (importação por chamador irrestrito, D-21), o objeto é agrupado sob
     "Não atribuído" pela chave (importador, repositório ou projeto quando o importador o informa,
     senão `scan_imports.id` da última importação), para que achados de repositórios distintos não se
     fundam. A fração desses objetos, por peso, é publicada.
4. **Dois compostos no mesmo host** (V1 e AES): máximo, nunca combinação.
5. **Mesmo perímetro externo** (domínio SSC × FQDN do Vision One): ficam como objetos distintos. Como
   a agregação entre objetos é intensiva, a sobreposição pesa na média mas não soma. Ela é declarada e
   medida na calibração.
6. **Exposição × execução do tratamento são fatos diferentes.** Um CVE vencido aparece em EXP (é
   exposição) e em GOV.T (o SLA foi descumprido), e isso não é dupla contagem. O **atraso de SLA entra
   uma vez**, em GOV.T, e nunca como multiplicador do sinal do achado.
7. **Revisão atrasada.** Num cenário com residual, entra **só** pelo crédito de tratamento (REG):
   GOV e CTL.P usam `σ_ref`, que não decai. Num cenário sem residual, entra **só** por REV-NOCREDIT
   (GOV.R).
8. **Aceitação vencida ou revogada.** Entra **só** por ACC-EXP (GOV.A). A solicitação de revisão que o
   código grava no mesmo ato (`RiskAcceptancesService.cs:253`, `:284`, `:431-435`) é ignorada por
   REV-REQ. §6.4 tem a tabela completa.

### 3.5 Sinais deliberadamente excluídos

| Excluído | Motivo |
|---|---|
| `ContributingScore` e o escore total `(calc + 2·contrib)/3` | Crescem com a contagem de vulnerabilidades, contam o pior achado duas vezes e quebram com nulos (`RiskCalculationService.cs:106-120`). O próprio produto os declara heurística de triagem |
| `PostureScore` do Master Dashboard | Contagem ponderada e saturada, com pesos fixos |
| `entities.cyber_risk_index` | Legado: vence a última escrita, sem histórico |
| CRI e `assetGroups` do Vision One; CES da Tenable | Indicadores de referência. Somá-los contaria os mesmos objetos duas vezes |
| Alertas do Workbench e OAT | Indicador até haver triagem: alerta bruto não é risco (Fase 2) |
| Contagens brutas (achados, incidentes, alertas, cenários) e conformidade de SLA em % | Exibidas como volume ou indicador |
| `MgmtReview.Review` e `MgmtReview.NextStep` | As constantes do código contradizem o seed (§6.7) |
| `MgmtReview.NextReview` | Calculado no cliente e ignorado por `GetOverdueReviewsAsync`. O vencimento sai da cadência aprovada no perfil |
| Revisões gravadas por aceitação ou campanha, e autoavaliações | Não restauram crédito (§6.1): aceitar não é revisar, e o dono não revisa o próprio residual |
| A flag `ReviewRequested` crua | O caminho desktop nunca a limpa. Usa-se a comparação de datas REV-REQ |
| Soma de P95/CVaR | Percentis não somam |
| Flags (M43) e confiança da evidência (M40) | Ficam ao lado; as flags só entram pelo piso do Portão A |
| Uso de override de segregação de funções e higiene de campanha | Indicadores exibidos ao lado |
| O próprio ICR como KRI | Viraria o "outro KRI" do Portão B (§1.2) |

---

## 4. Matemática de agregação

Esta seção é a especificação do motor. Os símbolos estão no glossário (§18).

### 4.1 Nível 1 — sinal

```
s ∈ [0,1]                         normalização por âncora (§3.1)
s_evento(t) = s · d(t)            d = 1 (aberto) ou 2^(−idade/h) após o encerramento; idade = agora − encerramento
```

### 4.2 Nível 2 — objeto

**Sinais de um mesmo fator** (sinais independentes, ordenados `x(1) ≥ x(2) ≥ …`, no máximo K):

```
MRA_ρ(x) = x(1) + ρ · (1 − x(1)) · [1 − Π_{k=2..K} (1 − x(k))]        ρ = 0,30 ; K = 10
x_{o,f}  = MRA_ρ(sinais de o em f)        (GOV.A e EXP.D usam MAX: os sinais descrevem o mesmo estado)
```

- O restante amortecido acrescenta no máximo `ρ · (1 − x(1))`.
- Exemplo: 50 achados médios (0,50) num host dão no máximo `0,50 + 0,30·0,50·(1 − 0,5^9) = 0,65`.
  É menos que um único crítico com exploração (0,95). O número de achados não substitui a gravidade.
- O operador generaliza o `ContributingScore` sem os seus defeitos: não cresce sem limite, não conta
  o pior achado duas vezes e não divide por zero.

**Fator efetivo** (aplica a criticidade, depois a obsolescência da §8.2):

```
F_{o,f} = m(crit_o) · x_{o,f}      fator tipo V
F_{o,f} = x_{o,f}                  fator tipo C
F_{o,f} = x_{o,f}                  fator tipo S (a escala σ_ref entra no objeto)
F_eff   = F + (1 − v_f) · max(0, π · m(crit_o) − F)       v_f = validade da leitura (§8.2); só sobe
```

**Objeto na categoria k**, sobre **todos os fatores com leitura vigente** (§8.2), inclusive os de
`v_f = 0`. Uma leitura antiga não sai do cálculo: ela entra por `F_eff`, que mantém o valor ruim e só
eleva o valor bom em direção a `π·m(crit)`.

```
REG:   R_{r,REG} = σ_r = r_eff / 10
EXP:   R_{o,EXP} = max_f ( u_f · F_eff,f )                          famílias sobrepostas
AME:   R_{e,AME} = MRA_ρ ( u_f · F_eff,f )
CTL:   R_{o,CTL} = MRA_ρ ( u_f · F_eff,f )                          entidade, host ou tenant
       R_{r,CTL} = σ_ref · u_P · s_IRP · rampa(σ_ref)               todo cenário aberto (§3.1)
GOV:   R_{r,GOV} = σ_ref · MRA_ρ ( u_f · x_{r,f} )                   cenário
       R_{h,GOV} = m(crit_h) · u_T · SLA_h                           host ou aplicação (§6.3)
```

- Em GOV e CTL.P a escala é `σ_ref`, a severidade **declarada**: uma falha de governança **nunca deixa
  um objeto maior do que o risco declarado a que se refere**. Como `σ_ref` não decai, a revisão
  atrasada entra só em REG (§3.4, regra 7).
- Um objeto está **avaliado** na categoria se algum fator tem `v_f > 0`. Os obsoletos (avaliados
  sem nenhum fator com `v = 1`) são um subconjunto deles, com valor elevado por `F_eff`.
- Um objeto do inventário cujas leituras têm todas `v_f = 0`, ou que nunca teve leitura, vai para o
  conjunto **presumido** (§8.3), mas só em EXP e CTL.A, e só quando alguma fonte da categoria está
  habilitada. Seu valor é o limite contínuo da mesma fórmula com `v = 0`:

  ```
  R_pres = max_f u_f · max(F_f, π · m(crit_o))      sobre as leituras vigentes
  R_pres = ū_k · π · m(crit_o)                       sem nenhuma leitura (ū_k = maior teto dos fatores do objeto em k)
  ```

  Assim, um objeto que passa de avaliado a presumido em `2W` não salta de valor.

**Pesos dos objetos:**

| Objeto | `w_o` |
|---|---|
| Técnico (host, domínio, aplicação, entidade, tenant) | `m(crit_o)` |
| Cenário | `w_r = m(impacto_r) · m_rank,r` (§5.4, §6.5) |

### 4.3 Nível 3 — categoria no escopo S

```
w'_o   = q̃_g(o) · w_o / Σ_{x∈X, g(x)=g(o)} w_x       q̃ = cotas do perfil renormalizadas sobre as populações presentes em X
M_p(X) = ( Σ_{o∈X} w'_o · R_o^p )^(1/p)                p = 4    (Σ w' = 1; M_p = 0 se X é vazio ou todo R é 0)
A(X)   = max( M_p(X) , τ · max_{o∈X} R_o )             τ = 0,80
C_k(S) = 100 · max_{j=0..n} A( Aval_k(S) ∪ Pres_k(S)_{(1..j)} )      Pres_k ordenado por R decrescente
```

- **Populações.** Em categorias de população única (REG, EXP, AME), `w'_o = w_o / Σ w`, e `M_p` é a
  média de potência ponderada usual. Em GOV e CTL, cada população recebe sua cota (§2.3), e dentro
  dela o peso é a criticidade. A cota depende só de quais populações existem em X, nunca do número de
  objetos, então hosts a mais não diluem cenários.
- `Aval_k(S)` são os objetos de S avaliados em k, **incluindo os avaliados limpos** (R = 0).
  `Pres_k(S)` são os presumidos (§8.3).
- **Inclusão conservadora, por objeto.** O máximo sobre prefixos de `Pres` ordenado por R decrescente
  inclui cada presumido que eleva o valor e nenhum que o dilua: acrescentar um objeto eleva a média
  da sua população (e, portanto, `M_p`) se e só se `R_o` é maior ou igual a essa média, e todos os
  presumidos de uma categoria pertencem à mesma população, então o melhor subconjunto é sempre um
  prefixo. Como todo prefixo com `j ≥ 1` contém o
  maior presumido, `C_k ≥ 100·τ·R_o` também para todo presumido: não medir nunca sai mais barato que
  medir.
- Os **pontos de presunção** `C_k − 100·A(Aval_k)` e o prefixo escolhido são publicados.
- Uma categoria só com presumidos (fonte habilitada, nada medido) entra com
  `C_k = 100 · max_j A(Pres_{(1..j)})`. Categoria sem inventário é "não aplicável"; categoria com
  inventário e sem fonte habilitada é "indisponível" (§8.1).

### 4.4 Nível 4 — manchete

```
K(S)    = { k : ω_k > 0, alguma fonte de k habilitada (AME: intake ativo) e Aval_k(S) ∪ Pres_k(S) ≠ ∅ }
ICR*(S) = max( Σ_{k∈K(S)} ω_k · C_k(S) / Σ_{k∈K(S)} ω_k ,  τ_G · max_{k∈K(S)} C_k(S) )      τ_G = 0,70
ICR(S)  = max( ICR*(S) , φ_A · 𝟙[predicado do Portão A verdadeiro para algum cenário de S] )   φ_A = 70 (§7)
```

ICR e ICR\* são ambos armazenados. A tendência plota ICR\*, e os períodos com piso aparecem sombreados.
Uma categoria "indisponível" sai de `K(S)` com renormalização; isso pode mover a manchete para
qualquer lado, e por isso o intervalo de ignorância é sempre exibido quando ela falta (§8.6).

### 4.5 Arredondamento e faixa

- **O valor exibido é inteiro**, com arredondamento comercial (meio para longe de zero; em C#,
  `MidpointRounding.AwayFromZero`, porque o padrão de `Math.Round` arredonda para o par), sempre a
  partir do valor em precisão total, nunca de um valor já arredondado no banco.
- **`Faixa(x, limites)`** é uma função pura, com limites inferiores inclusivos (`≥`), sem
  arredondamento embutido. Fica num módulo neutro (`Tools`) e não carrega estado do ICR (§1.3).
  - **ICR:** aplicada ao **inteiro exibido**, para que nunca apareça "70 Médio".
  - **Registro (cadência, §6.1):** aplicada ao score 0–10 **cru**, sem arredondar, com os limites
    `limites_registro` aprovados no perfil: Low 0 · Medium 4,0 · High 7,0 · Very High 9,0 (Very High
    alcançável, proposta da D-02 e decisão de Fase 0). Arredondar antes mudaria a cadência: 8,8
    arredondado a 9 viraria Very High.
- Faixas padrão do ICR, alinhadas ao Vision One: **Baixo 0–30 · Médio 31–69 · Alto 70–100**
  (em inglês, *Low · Medium · High*). A primeira faixa sempre começa em 0.
- Cada faixa tem rótulo em **português e em inglês**, como o resto do sistema; os dois são aprovados
  juntos no perfil, e a interface mostra o da cultura do usuário, com inglês como padrão.
- Preset opcional "Registro", alinhado a `risk_levels × 10` depois da correção da D-02: Baixo 0–39,
  Médio 40–69, Alto 70–89, Muito alto 90–100.
- As faixas são **categorias de comunicação**, aprovadas pelo Conselho/Reitoria "para comunicação".
  Não são tolerância nem apetite (§1.3).
- A faixa é sempre exibida como **texto e forma**, nunca só como cor ([ui-standard §2.6](../ui-standard.md)).

### 4.6 Propriedades

| Propriedade | Argumento |
|---|---|
| **Limitado** em [0,100] | `MRA ≤ x(1) + ρ(1 − x(1)) ≤ 1`; `m(crit) ≤ 1`; `u_f ≤ 1`; `σ ≤ 1`; `M_p ≤ max R`; médias ponderadas de valores em [0,100]; `φ_A ≤ 100` |
| **Monótono em R**: piorar o risco `R` de um objeto nunca baixa o índice | `∂MRA/∂x(1) = 1 − ρ·[1 − Π] ≥ 1 − ρ > 0` e `∂MRA/∂x(k) = ρ(1 − x(1))·Π_{j≠k}(1 − x(j)) ≥ 0`; MRA é contínua na troca de ordem. `M_p`, `max`, o máximo sobre prefixos e médias ponderadas são não decrescentes em cada R, e o máximo de funções não decrescentes também é. Logo, resolver um sinal nunca aumenta o ICR\*. **Exceções declaradas:** (a) mudança de perfil; (b) objeto recém-avaliado limpo baixa a média, porque é informação nova; (c) objeto presumido que passa a ser medido pode subir ou descer; (d) categoria que entra ou sai de `K(S)` renormaliza; (e) **mudança de contexto** (criticidade, impacto, rank de campanha): ela move o **peso**, e no ramo da média aumentar o peso de um objeto com `R_o < M_p` baixa `M_p`. Para compostos, a criticidade entra só no peso, então elevar a criticidade de um ativo limpo baixa a categoria. É a semântica de média ponderada por importância, mas é um vetor de Goodhart: por isso esses insumos exigem permissão própria e auditoria (D-17) e as exceções (c), (d) e (e) aparecem em "o que mudou" como efeito de cobertura ou de contexto (§9.4). "Piorar um sinal nunca baixa o índice" vale para R, não para contexto |
| **Preserva a cauda** | `C_k ≥ 100·τ·R_o` para todo objeto avaliado ou presumido de k (§4.3). `ICR* ≥ τ_G·max C_k`. Logo, `ICR* ≥ 100·τ_G·τ·R_o = 56·R_o` com o padrão. A trava conjunta `τ·τ_G·L_topo ≥ L_2` (§12.4) liga isso às faixas do perfil: com o padrão, `0,56 × 70 = 39,2 ≥ 31`, então um objeto com `R ≥ 0,70` nunca deixa a manchete em Baixo. Levar a manchete a Alto por um único objeto fica reservado ao Portão A |
| **Diluição limitada** | Acrescentar objetos limpos só reduz o ramo `M_p` da sua população, e nunca abaixo de `τ·max R`. Dentro da categoria, dado ausente nunca reduz (I5) |
| **Invariância por replicação** | Duplicar todos os objetos com a mesma distribuição não altera `M_p` nem o máximo (verificado na §4.7). O piso **não** é invariante ao tamanho: o máximo esperado de N objetos cresce com N (§1.1) |
| **Independe do inventário dentro do objeto** | O restante é limitado por ρ e por K |
| **Responsivo** | No ramo da média, `∂C/∂R_o = 100·w'_o·R_o^(p−1)·M_p^(1−p) > 0` para todo `R_o > 0`: toda correção move o índice, e mais quando o objeto é alto. No ramo do piso, só a melhora do objeto dominante move o índice. Isso é intencional, e o painel diz "sustentado por ‹objeto›" e mostra a sensibilidade do indicador (§4.8) |
| **Decomponível** | Alocação de Euler exata (§4.8). A variação no tempo é atribuída por caminho (Aumann–Shapley, §9.4) |
| **Reconciliável** | `R_o` não depende do escopo, porque a herança de criticidade usa todos os vínculos do objeto (§5.2). O mesmo objeto tem o mesmo valor em qualquer recorte; só o conjunto muda |

### 4.7 Exemplo canônico: o servidor crítico e as vinte máquinas de teste

Cenário: um servidor de criticidade 5 com score de dispositivo Vision One 90 e vinte VMs de teste de
criticidade 1 com score 10. São compostos, então não há modulador nos valores. Os pesos são
`m(5) = 1,00` e `m(1) = 0,40`.

| Método | Resultado |
|---|---|
| Média ponderada atual (`TrendMicroService.cs:691-694`, pesos 5 e 1) | **26,0**: faixa Baixo do próprio Vision One |
| Média aritmética | 13,8 |
| "Média pura" (p = 1, sem pisos; só na prévia) | 18,9 |
| `M_4` ponderada | 52,0. O servidor responde por 99,9% de Σ w·R⁴ |
| Piso `τ·max = 0,80 × 90` | 72,0 |
| **`C_EXP`** | **72 (Alto)** |
| 200 VMs de teste em vez de 20 | `M_4` cai para 30,1; `C_EXP` continua **72** |
| 2.000 VMs de teste | `M_4` 17,4; `C_EXP` **72** |
| Inventário duplicado (2 servidores e 40 VMs) | **72** |
| Servidor corrigido de 90 para 35 | `M_4` 20,5; piso 28,0; **`C_EXP` = 28** |
| Só as VMs corrigidas (10 → 0) | 72: o ramo do piso está ativo, "sustentado por SRV" |
| O mesmo servidor sozinho numa organização sem outro risco (demais categorias 0) | Manchete `max(0,30 × 72; 0,70 × 72) = 50,4` = `56 × 0,90`: Médio |

O comentário em `TrendMicroService.cs:636-638` afirma que a média ponderada evita esse caso. Não
evita, e precisa de teste de regressão (D-17).

### 4.8 Composição exata e sensibilidade do indicador

**Composição (alocação de Euler).** As contribuições somam exatamente o valor exibido sem piso.

| Nível | Ramo da média ativo | Ramo do piso ativo |
|---|---|---|
| Manchete | `g_k = ω_k / Σ_{K(S)} ω` | `g_k = τ_G` para a categoria de maior `C_k` (empate: dividido igualmente) e 0 para as demais; a média ponderada aparece como referência |
| Categoria | `parte_o = C_k · w'_o·R_o^p / Σ w'·R^p` (exata, porque `M_p` é homogênea de grau 1); todas as partes valem 0 quando `Σ w'·R^p = 0` | 100% de `C_k` para o objeto de maior `R_o` (empate: dividido igualmente), rotulado "sustentado por" |
| Objeto, MRA | O item principal recebe `x(1)`. O restante `ρ(1 − x(1))[1 − Π]` é repartido entre k ≥ 2 na proporção de `−ln(1 − x(k))` | — |

- **Na manchete**, a contribuição de um objeto é `contrib_o = g_k · parte_o`. Exibir `parte_o` sem o
  fator `g_k` soma `C_k`, não ICR\*.
- Objetos com `R_o = 0` têm parte 0.
- Quando a inclusão conservadora escolhe presumidos, a parte deles aparece como "presunção".

**Sensibilidade do indicador** (antes chamada "impacto de remediação"). Para os 50 objetos de maior
participação em cada nó, calculada todas as noites:

```
Δ_{o,k} = ICR*(S) − ICR*(S | R_{o,k} = 0)        o objeto passa a avaliado-limpo naquela categoria
```

- Mede **quanto o número depende do objeto**, não o valor de tratá-lo. Nenhuma lista de trabalho
  ordena por Δ (R-1). Ao lado aparecem o estado de decisão do cenário (portões, aceite, campanha) e,
  quando o M44 existir, o benefício líquido do Portão C/D, que é o critério de ordenação de tratamento.
- Para objetos de AME, que são eventos, o Δ exibido é o do encerramento hoje medido daqui a uma
  meia-vida: `s·d` com `d` reduzido à metade.
- Quando o ramo do piso está ativo, o painel mostra "retido por ‹objeto›" e indica o próximo objeto
  que passaria a sustentar o valor.

---

## 5. Criticidade e impacto

### 5.1 Criticidade de host e de objeto técnico

```
m(crit) = a + (1 − a) · (crit − 1)/4       a = 0,40  →  crit 1: 0,40 · 2: 0,55 · 3: 0,70 · 4: 0,85 · 5: 1,00
crit_ef(o) = min(5, max(crit_própria, crit_herdada) + Δ_dados)
```

A criticidade entra **no valor**, como modulador dos fatores V, e **no peso** (`w_o = m(crit)`). Por
isso o piso `a = 0,40` é deliberado. Uma escala 0,2–1,0 puniria em dobro o objeto de criticidade
padrão, que é comum. Por entrar no peso, uma mudança de criticidade é **mudança de contexto**
(§4.6, exceção e): pode baixar o ramo da média.

**Precedência da criticidade própria:**

1. Manual (edição no NetRisk; host auditado, porque `Host` está na allowlist do
   `GovernanceAuditInterceptor`; entidades só depois da D-17).
2. CMDB / Jira Assets.
3. Tenable ACR, por `⌈ACR/2⌉` [N].
4. Vision One (high 4, medium 3, low 2). O enum documentado da API não tem "critical", mas o código
   aceita a palavra "critical" e valores 0–100 e pode gravar 5 (`TrendMicroClient.cs:571-592`). **O
   ICR limita a 4 a criticidade de origem Vision One.** A criticidade 5 é decisão de negócio: CMDB ou
   manual.
5. Padrão: 3, ou 2 quando o ambiente está no vocabulário controlado "não produtivo".

**Situação atual e qualidade:**

- Hoje vale o último escritor, e o Vision One sobrescreve a cada sincronização (`TrendMicroService.cs:559,675`).
- **Pré-requisito:** coluna `hosts.criticality_source` (D-10).
- A origem e o autor da criticidade de cada objeto ficam na tabela de objetos (§12.5) e aparecem no
  drill-down. Toda mudança de criticidade aparece em "o que mudou" como efeito de contexto, com autor.
- A fração de objetos (por peso) com criticidade padrão é publicada como métrica de qualidade de
  contexto (§8.5).

### 5.2 Criticidade herdada, de processo e de dado

- **Herança (modo pleno, M39):**

  ```
  crit_herdada(o) = max crit(x), x ∈ processos, aplicações e serviços que o objeto serve
  ```

  - Usa **todos os vínculos** do objeto, independentemente do escopo do drill-down. Isso torna `R_o`
    independente do escopo.
  - É integral: um processo de criticidade 5 leva o host a `m = 1,00`. É também por isso que a
    criticidade de processo exige permissão e auditoria (D-17): uma edição move todos os hosts que o
    processo serve.
- **Criticidade de processo, aplicação e unidade:**
  - **Interino:** propriedade EAV `criticality` (1–5) em `businessProcess`, `application`,
    `organizationUnit` e `subOrganizationUnit`, com novo número de versão do
    `src/API/EntitiesConfiguration.yaml` (o tipo `businessProcess` está em `:346`). Não exige DDL, mas
    exige a permissão dedicada e a auditoria da D-17 (Estágio A0). É rotulada **"declarada, não BIA"**.
  - **Pleno (M41, T157):** o ICR **consome a criticidade de processo do M41 como entregue**. Não define
    mapeamento próprio de MTPD; se a especificação do M41 não trouxer um, a proposta (≤ 4 h → 5;
    ≤ 24 h → 4; ≤ 72 h → 3; ≤ 7 d → 2; acima → 1) vai para ela, não para o ICR.
  - BIA ausente lê "ausente": usa o padrão e reduz a qualidade de contexto. Nunca zero.
- **Classificação de dados:** `Δ_dados = +1` quando o objeto é uma aplicação (ou, no pleno, serve uma
  aplicação ou dado) cujo `securityClassification` aponta para um nível marcado como sensível na
  tabela de mapeamento do perfil. Teto 5. Pleno: sensibilidade e flags 2 e 5 do M49.

### 5.3 Exposição à internet

- É **verossimilhança, nunca impacto**. Entra como `m_net = 1,2` **só nos sinais de achado (EXP.V)**.
  Não tem papel em piso nem em Portão A.
- **Origem interina:**
  - aplicação com a propriedade EAV `internetFacing = true` [N, barato; Estágio A0, com a permissão
    da D-17];
  - host com IP público: heurística **opcional por perfil** (`heuristica_ip_publico`, desligada por
    padrão). Quando ligada, exclui as faixas de endereço público que a organização usa internamente
    (`faixas_publicas_internas`), considera fora de RFC 1918, RFC 6598, loopback e link-local, e é
    publicada como contexto de baixa confiança. Hoje `hosts.ip` guarda só o primeiro IPv4
    (`TrendMicroClient.cs:352`).
- **Origem plena [N]:** inventário internet-facing do Vision One, atributo do CMDB, tags do Tenable.
- Sem a informação, `m_net = 1,0` e o objeto conta como "contexto ausente".

### 5.4 Impacto do cenário e classe de ativo

- **Cenários.** O impacto já está no residual, então o impacto do cenário entra **só no peso**:
  `w_r = m(ClassicImpact) · m_rank`.
  - Para `ScoringMethod = 3`, o nível é a faixa de impacto ancorada do M38
    (`impact.impact_min/impact_max`) que contém a perda mais provável.
  - Sem impacto definido, usa 3 e conta como contexto ausente.
- **Entidade-alvo de incidente.** `crit` vem da propriedade EAV da entidade, ou do padrão 3.
- **Classe de ativo.** Enum normalizado: servidor, estação, rede, identidade, aplicação, internet-facing,
  nuvem, domínio externo, OT/IoT, desconhecida.
  - Interino: derivada por regras configuráveis sobre `hosts.source`, `external_provider`, `os` e o
    `ObjectTypeName` do Jira Assets.
  - Pleno: coluna normalizada.

---

## 6. Revisões e governança

As revisões e decisões da plataforma entram de três formas: crédito de tratamento (REG), sinais de
governança (GOV) e rank de campanha (peso). Todas usam **só campos confiáveis hoje**. Os códigos
`MgmtReview.Review` e `NextStep` não são lidos (§6.7).

### 6.1 Crédito de tratamento: a revisão como prazo de validade do residual

Um residual é uma **alegação** de que os controles reduzem o risco inerente. Uma alegação que ninguém
confirma dentro da cadência perde crédito aos poucos. É o princípio "ausente/obsoleto não lê como
seguro" aplicado ao registro.

```
d_ref  = max( data_efetiva(m) : m revisão qualificada do risco )
         sem revisão qualificada ⇒ d_ref = Risk.SubmissionDate
data_efetiva(m) = min( m.SubmissionDate , criação de m em audit_logs , agora )
T      = cadencia_revisao[ Faixa(score de cadência ; limites_registro) ]     cópias aprovadas no perfil (§12.2)
         score de cadência = base_cadencia (padrão InherentRisk, como Data/80.sql:46); residual nulo ⇒ inerente
a*     = última aceitação do risco sem sucessora (Status ≠ Renewed), válida ou não
fim    = RevokedAt de a* ?? ExpiresAt de a*
due    = min( d_ref + T , fim )      se a* existe e fim ≥ d_ref
due    = d_ref + T                   caso contrário
cred   = 1                                         se agora ≤ due
cred   = max( cred_min , 1 − λ·(agora − due)/T )   caso contrário          λ = 1,0 ; cred_min = 0
r_eff  = r_res + (1 − cred) · max(0, r_inh − r_res)
r_res nulo ⇒ r_eff = r_inh, rotulado "tratamento não avaliado"
σ_r    = r_eff / 10                 só em REG
σ_ref  = (r_res ?? r_inh) / 10      GOV e CTL.P: severidade declarada, sem decaimento
```

- **Revisão qualificada** (§3.3) é uma `MgmtReview` que:
  - **não foi gravada por uma aceitação nem por uma decisão de campanha.** O código grava uma
    `MgmtReview` em toda aceitação (`RiskAcceptancesService.cs:404-424`) e em toda decisão de
    campanha que não seja Accepted (`RiskReviewCampaignsService.cs:356-366`), no mesmo `SaveChanges`.
    Essas linhas são reconhecidas porque a criação delas em `audit_logs` compartilha o
    `CorrelationId` com a criação de uma `RiskAcceptance` ou com a decisão de um
    `RiskReviewCampaignItem`; na falta do registro, pelo mesmo risco e usuário a até
    `janela_revisao_derivada` (60 s) da criação da aceitação ou do `DecidedAt`;
  - **não é autoavaliação:** `Reviewer ∉ {SubmittedBy, Owner, Manager}` do risco, a regra do Track 8
    (8.3.2) que o caminho desktop não aplica (D-07). O servidor carimba `Reviewer` com o usuário da
    requisição (`MgmtReviewsController.cs:48`), então o campo é confiável.
- **Aceitação e decisão de campanha não restauram crédito.** Aceitar não é revisar o residual: quem
  aceita assume o valor alegado, não o confirma. Contar a aceitação como revisão faria "aceitar um
  cenário com revisão vencida" baixar REG, que é o caminho "aceitar para o painel ficar verde" que a
  I10 fecha. Escalated e MitigationRequested também não confirmam o residual: um adia a decisão, o
  outro diz que ele não é aceitável. As três decisões entram em GOV.C e no rank (§6.5).
- **Data efetiva.** `POST /MgmtReviews` grava o `SubmissionDate` que o cliente manda
  (`MgmtReviewsController.cs:35-52`, `MgmtReviewsService.cs:141-153`); a GUI desabilita o campo, a API
  não. Por isso a data é limitada pela criação da linha em `audit_logs` (`MgmtReview` é tipo auditado)
  e pelo instante do snapshot: uma revisão com data futura não mantém `cred = 1` indefinidamente. A
  correção de raiz é carimbar no servidor (D-07).
- **Vencimento pela aceitação.** O `ExpiresAt` (ou `RevokedAt`) da última aceitação **antecipa** o
  vencimento enquanto não houver revisão qualificada posterior a ele, inclusive depois que a aceitação
  expirou: uma aceitação vencida sem nova revisão começa a perder crédito na data em que venceu, e não
  ganha uma cadência inteira extra. Uma revisão qualificada posterior a `fim` tira a aceitação do
  cálculo.
- **Faixa e cadência.** `Faixa` é a função única da §4.5, sobre o score 0–10 cru, com `≥` e Very High
  alcançável (D-02). Os valores usados são **cópias aprovadas no perfil** (`cadencia_revisao`,
  `limites_registro`, `base_cadencia`), para que uma edição em `review_levels`, `risk_levels` ou
  `settings` não mova o índice sem versão nem marcador (§9.5). Quando a cópia diverge da tabela do
  produto, o painel mostra o chip "cadência do perfil ≠ cadência do produto".
  - Cadências do seed (`Data/1.sql:230-234`): Very High 30 d, High 90 d, Medium 120 d, Low 240 d.
  - "Insignificant" (360 d) não casa com nenhum `risk_levels` e não é usada.
  - A função única também resolve o score exatamente 0, que hoje fica sem faixa
    (`MgmtReviewsService.cs:416-434`, `>` estrito).
- **Residual acima do inerente.** O `max(0, ·)` impede que uma revisão vencida *baixe* `r_eff` quando
  `r_res > r_inh`, o que acontece porque o residual é gravado uma vez por dia e o inerente a cada 2 h,
  e pela D-03. O caso aparece como chip de qualidade "residual > inerente".
- **Leitura.** Com λ = 1, o crédito se perde linearmente ao longo de uma cadência de atraso. Um risco
  High (90 d) sem revisão volta ao inerente 180 dias depois da última revisão.
- **Antes da correção da D-01**, o residual clássico nunca é gravado (§3.2, REG-RES), então
  `r_eff = r_inh` em todo cenário clássico e o crédito não tem efeito. O modo interino só é publicado
  depois do Estágio A0, que corrige a D-01.

### 6.2 Aceitações

- **Válida** = `Status Active ∧ ExpiresAt > agora` (§3.3), calculada por data e independente do job
  de expiração.
- **A aceitação nunca reduz REG nem EXP e nunca restaura crédito.** Esta é uma invariante travada:
  não existe parâmetro de desconto, e a data da aceitação não entra em `d_ref`.
  - Achados `RiskAccepted` permanecem em EXP.
  - Aceitar muda a governança da exposição, não a verossimilhança nem o impacto. Descontar por aceite
    premiaria "aceitar para o painel ficar verde".
  - **Teste obrigatório:** aceitar um cenário com revisão vencida não altera `R_{r,REG}`.
- **O que a aceitação faz:**
  - a válida zera o sinal APT-BRK (§6.3);
  - a válida com `(r_res ?? r_inh) >` teto vigente acende ACC-APT, "**aceite anterior ao apetite
    vigente**". O Track 8 proíbe criar ou renovar aceitação acima do teto
    (`RiskAcceptancesService.cs:119-120`, `:189-190`), então esse estado só existe quando o aceite foi
    concedido antes de haver teto, sob um teto mais alto, ou quando o residual subiu depois (ACC-DRIFT);
  - o `ExpiresAt` (ou `RevokedAt`) da última aceitação antecipa o vencimento da revisão (§6.1);
  - vencida ou revogada sem nova decisão, acende ACC-EXP.

### 6.3 Sinais de governança (GOV)

Cada sinal tem valor em [0,1]. Dentro de GOV.A os sinais combinam por MAX, porque descrevem um único
estado (aceitação e apetite) do cenário. Em GOV.C vale só a última decisão de campanha, então os
sinais são mutuamente exclusivos. Em GOV.R os sinais combinam por MRA. Os fatores combinam entre si
por MRA (§4.2). "Teto" é `MaxAcceptableResidual` da linha da entidade (`risks.entity_id`), senão da
global, com `>` estrito, igual a `EvaluateAppetiteAsync`.

| ID | Fator | Condição (por cenário aberto, salvo indicação) | Valor padrão | Disp. |
|---|---|---|---|---|
| **APT-BRK** | GOV.A | `(r_res ?? r_inh) >` teto **e** sem aceitação válida | 1,0 sem tratamento em curso; 0,5 com tarefa Open/InProgress **ou** com tarefa concluída depois do último recálculo do residual (`CompletedAt > RiskScoring.ResidualUpdatedAt`), para que concluir o tratamento não dobre o sinal até a próxima passada | [H] se houver teto configurado |
| **ACC-APT** | GOV.A | Aceitação válida e `(r_res ?? r_inh) >` teto vigente ("aceite anterior ao apetite vigente") | 0,5 | [H] |
| **ACC-EXP** | GOV.A | Última aceitação sem sucessora expirada (`ExpiresAt ≤ agora`) ou revogada, depois de 7 d de carência, **sem** revisão qualificada nem decisão de campanha posterior a `fim`, **e** o cenário ainda precisa de aceite: não vale `(teto aplicável ∧ (r_res ?? r_inh) ≤ teto)` | 1,0 se expirou; 0,5 se foi revogada (a revogação é o controle funcionando, mas ainda exige nova decisão) | [H], por `ExpiresAt` e `RevokedAt`, não por `Status` |
| **ACC-DRIFT** | GOV.A | Aceitação válida e `Δ = (r_res ?? r_inh) − ResidualScoreSnapshot` | `clamp((Δ − 0,5)/1,5; 0; 1)` | [H] |
| **APT-DUAL** | GOV.A | Aceitação válida cuja revisão de aceitação exige contra-assinatura (`RequiresCountersignature`) sem `SecondReviewerId` há mais de 7 d | 0,5 | [C] parcial: só linhas de aceitação e do portal |
| **CMP-PEND** | GOV.C | Item `Pending` em campanha `Overdue` ou com `DueDate` vencido | 1,0 | [H]; [C] depende de `risks.entity_id` e do job de campanha |
| **CMP-ESC** | GOV.C | Última decisão `Escalated` sem revisão qualificada, aceitação ou tarefa posterior em 30 d | 0,7 | [H] |
| **CMP-MIT** | GOV.C | Última decisão `MitigationRequested` cujas tarefas criadas na decisão foram **todas canceladas**, sem decisão posterior. (A decisão sem tarefa é impossível: `RiskReviewCampaignsService.cs:308-323` exige ao menos uma e as cria na mesma chamada; tarefa vencida já está em TSK-OVD) | 0,5 | [H] |
| **TSK-OVD** | GOV.T | `max` sobre as tarefas Open/InProgress com `DueDate < agora` de `min(1, dias_vencidos / prazo_ref_tarefa)`, com `prazo_ref_tarefa` = 30 d. Sem denominador: concluir uma tarefa no prazo não sobe o sinal, e criar tarefas fictícias não o baixa | Contínuo | [H] |
| **SLA** | GOV.T (objeto host ou aplicação) | `SLA_h = max_f [ a_sev(f) · min(1, dias_vencidos(f) / prazo_remediação(f)) ]`, sobre achados abertos para SLA. `a_sev` são as âncoras de severidade da §3.1. Host sem nada vencido entra com 0 | Valor contínuo; `R_h = m(crit_h)·u_T·SLA_h` | [H]; host com achado aberto sem `sla_due_date` fica "não avaliado" (§8.3) |
| **REV-REQ** | GOV.R | `review_requested_at > d_ref` há mais de 14 d. A comparação de datas contorna o defeito de o desktop nunca limpar a flag. **Ignora** a solicitação gravada pela expiração ou revogação de aceitação (a escrita em `audit_logs` compartilha o `CorrelationId` com a alteração da `RiskAcceptance`; na falta, o texto de `ReviewRequestedReason` gravado por `FlagForReview`), porque ACC-EXP já cobre o fato | 1,0 | [H] |
| **REV-NOCREDIT** | GOV.R | Residual nulo e revisão vencida: não há crédito a decair, e sem esta regra o lapso seria invisível | `min(1, atraso/T)` | [H] |
| **DIV** | GOV.G | **Divergência entre evidência técnica e residual** (heurística ordinal). `X_r − σ_ref > 0,3`, com `X_r = max R_{o,EXP}` dos objetos ligados ao cenário calculado **só com fatores de `v = 1`** (sem prior de obsolescência nem presunção): hoje via `risks_to_vulnerabilities` → host; no pleno, pela cadeia do M39. Uma vez acesa, fica acesa até uma revisão qualificada (`d_ref` > data da primeira detecção) | 0,8 | [H] |
| *Só exibição* | — | Aceitação válida que vence em ≤ 30 d; uso de `SegregationOverrideReason`; conclusão de campanha, tempo médio até decidir e campanhas vencidas (indicadores) | — | [H] |

**Sobre a divergência (DIV):**

- É uma **heurística ordinal declarada**: compara a exposição do ativo, que inclui `m(crit)` e
  compostos de fornecedor, com o residual L×I do cenário dividido por 10. As grandezas não são
  comensuráveis (vedação 5); o limiar 0,3 só aponta onde olhar.
- Usa `σ_ref`, a alegação armazenada, e não `r_eff`: assim o crédito vencido não liga nem desliga o
  sinal. A trava até a revisão impede que deixar a revisão vencer apague a divergência.
- Usa só evidência fresca, para que dado **ausente** (prior e presunção) nunca dispare um pedido de
  "novos dados".
- O ICR **nunca reescreve o residual** (I9) e **não emite gatilho**. O gatilho obrigatório de
  reavaliação "novos dados" (gatilho 6 da Fase 7) é do M46: ele o dispara pelo mesmo predicado,
  calculado por um componente compartilhado de exposição de objeto que não depende de tabelas
  `RiskIndex*`, e o ICR apenas lê o estado.
- A data da primeira detecção é o primeiro dia da sequência contínua atual de dias com o sinal
  verdadeiro na tabela de objetos (§9.1).

### 6.4 Cada fato entra uma vez

| Fato | Entra no valor por | Também aparece em |
|---|---|---|
| Revisão atrasada, cenário com residual | REG (crédito); GOV e CTL.P usam `σ_ref` e não a veem | Lista de revisões vencidas; selo de revisão vencida (§6.6) |
| Revisão atrasada, cenário sem residual | GOV.R (REV-NOCREDIT) | Lista de revisões vencidas |
| Revisão solicitada e não atendida (manual) | GOV.R (REV-REQ) | — |
| Acima do teto sem aceitação válida | GOV.A (APT-BRK) | Selo "N cenários acima do teto de apetite" |
| Aceitação vencida ou revogada sem nova decisão | GOV.A (ACC-EXP); a flag de revisão gravada no mesmo ato é ignorada por REV-REQ | — |
| Aceitação válida acima do teto vigente, com deriva ou com contra-assinatura pendente | GOV.A (ACC-APT, ACC-DRIFT, APT-DUAL), por MAX | — |
| Decisão de campanha | GOV.C | Indicadores de campanha |
| Tarefa vencida | GOV.T | — |
| SLA vencido | GOV.T (no host) | Indicador de conformidade de SLA em % |
| Evidência técnica divergente do residual | GOV.G | Estado do gatilho do M46 no cenário, quando existir |
| Finding aceito | EXP (permanece) | — |
| Evidência fraca (M40), flags (M43) | — | Ao lado; flags só pelo piso do Portão A |

### 6.5 Prioridade de negócio (rank de campanha)

```
m_rank = 1 + β · (1 − (rank − 1)/(n − 1))        β = 0,30 ;  n = 1 ⇒ m_rank = 1 + β
sem rank válido ⇒ m_rank = 1 + β/2               (neutro: unidades sem campanha não são favorecidas)
```

- O rank só vale se vier da campanha `Completed` mais recente da entidade (`risks.entity_id`), dentro
  de duas cadências de campanha.
  - A cadência vem de `risk_review_campaign_cadence_months`, com padrão 3 (`Data/81.sql:29`), o que dá
    6 meses.
- O rank é ordinal *dentro* da campanha. Por isso modula **só o peso** do cenário, nunca o seu valor.
  Como todo peso, é contexto: subir um cenário de baixo risco no rank pode baixar o ramo da média
  (§4.6, exceção e).

### 6.6 Teto ordinal de apetite: selos, não piso

`risk_appetites.MaxAcceptableResidual` é um **teto ordinal de apetite** do Track 8, na escala 0–10 do
residual. É uma **aproximação** do Portão B, não o Portão B: a metodologia define o Portão B sobre E[L],
P95/CVaR, indisponibilidade, perda de dados, número de titulares ou outro KRI contra os limites da Fase
0 (PDF p.11), e isso só existe com o M45 e o M46. O teto atua de três formas, nenhuma como piso do
índice:

1. **Os sinais APT-BRK e ACC-APT** em GOV.A.
2. **Selos obrigatórios**, ao lado do ICR e da categoria REG:
   - "**N cenários acima do teto de apetite**": calculado pelo ICR **sobre o conjunto de objetos do
     nó** (atribuição da §10.2), com a mesma comparação de `EvaluateAppetiteAsync` (residual
     armazenado, senão inerente; linha da entidade, senão global; `>` estrito). A contagem oficial
     `CountRisksAboveAppetiteAsync` (`RiskWorkflowService.cs:245-306`) agrupa pelo `risks.entity_id`
     exato, sem fecho e sem `risk_to_entity`, e por isso não serve a um nó do drill-down. Teste
     obrigatório: na raiz, depois do backfill da D-05, as duas contagens coincidem. Mostra quantos têm
     aceite anterior ao apetite vigente.
   - "**M cenários com revisão vencida, dentro do teto pelo residual armazenado e com inerente acima
     dele**": um selo de **revisão**, não de apetite. O ICR nunca usa a palavra "apetite" para
     comparações sobre `r_eff`.
   - "**Teto de apetite não configurado**", quando não há linha. Nenhuma linha vem semeada, e
     `RiskAppetitesController.cs:55` devolve 204. Nunca é lido como "dentro do apetite".
3. **Linha do teto** (× 10) no gráfico de tendência de REG.

No modo interino, o painel do **Portão B** aparece como "não disponível (requer M45/M46)". No modo
pleno, "P95 acima da tolerância" (M45, T186) e "KRI acima da tolerância" (M46, T191) são os selos de
Portão B.

### 6.7 O que não é lido até a correção

- **`MgmtReview.Review` e `MgmtReview.NextStep`.** As constantes do Track 8 contradizem o seed:
  - `ReviewAcceptTheRisk = 2` (`RiskAcceptancesService.cs:42`, `RiskReviewCampaignsService.cs:43`) é
    "Reject Risk and Close" no seed (`Data/1.sql:226`);
  - `NextStepAcceptUntilNextReview = 3` (`RiskAcceptancesService.cs:39`) é "Submit as a Production
    Issue" (`Data/1.sql:166`);
  - `NextStepRequestRiskReview = 1` (`RiskWorkflowService.cs:47`) é "Accept until Next Review"
    (`Data/1.sql:164`).

  A correção é pré-requisito de qualquer uso futuro dos desfechos (D-06). Até lá, uma revisão
  qualificada conta como revisão mesmo que seu desfecho tenha sido "Request Risk review": essa é a
  limitação remanescente do crédito.
- **`MgmtReview.NextReview`.** É calculado no cliente (`EditMgmtReviewViewModel.cs:221-222`) e
  ignorado por `GetOverdueReviewsAsync` (`MgmtReviewsService.cs:342`).
- **A flag `ReviewRequested` crua.** Só é limpa por `CreateReviewAsync` (`MgmtReviewsService.cs:188`),
  que não tem chamador em produção.

---

## 7. Pisos de exibição

**Só o Portão A gera piso**, e só a partir do predicado do M43 sobre um **cenário**. O teto ordinal de
apetite e o Portão B são selos (§6.6).

| Código | Condição | Piso | Disp. |
|---|---|---|---|
| **PA-1** | Cenário aberto para o qual o predicado do Portão A do M43 (T169/T170) é verdadeiro: flag 1, flag 2, flag 3 com ataque ativo, ou "sem aceitação legítima". O ICR **lê** o predicado; não o rederiva | φ_A = 70 (configurável 70–100) | [N] M43 |

**Não existe piso derivado de achado.** Um achado com KEV ou exploração observada num objeto crítico
é **exposição**: entra em EXP pelo piso de exploração do sinal (§3.1) e aparece como chip
"**exploração ativa sem cenário vinculado → candidata a gatilho de reavaliação**", encaminhado ao M43
(julgamento da flag 3 e do Portão A sobre um cenário) e ao M46 (gatilho). CVE sem cenário não é risco
autônomo (PDF p.8–9), o M42 não produz predicado de Portão A, e exposição (interna/perimetral/externa)
está fora do escopo do M42 (nota da T166). Derivar um piso de achado + internet-facing + criticidade
seria o ICR inventar um resultado de portão.

**Regras:**

- O piso **não pode ser desligado**.
- Vale na manchete de **todo escopo que contém o cenário disparador**. A categoria de origem mostra o
  chip, mas seu valor não recebe piso.
- Aceitação nunca remove PA-1, porque não existe aceitação legítima de condição de Portão A.
- Exibição: "**70 · Alto · piso: Portão A — R1 (flag 2) · sem piso 64 (Médio)**", com um link para os
  disparadores (Top Risks, M43 T172).
- Os pisos **nunca** entram em contribuições, em cálculos de nós pais, na tendência suavizada nem na
  atribuição de variação. O efeito do piso é publicado à parte (§9.4).

**Modo interino: nenhum piso.** O painel mostra chips "**evidência para avaliação do Portão A**", com
link e sem alterar o número, para:

- incidente `data_breach` aberto no escopo;
- achado exposto com exploração conhecida ou observada (§3.1) em objeto com `crit_ef ≥ 4`, sem
  cenário vinculado.

O piso não é aproximado por categoria de incidente nem por achado: concluir que há condição de Portão
A é julgamento sobre o cenário, reservado ao M43. Os chips só encaminham a evidência a esse julgamento.

---

## 8. Dados ausentes, obsolescência, cobertura e qualidade

### 8.1 Estados de um objeto numa categoria

| Estado | Condição | Tratamento |
|---|---|---|
| Avaliado | Algum fator com `v > 0` | Valor pela §4.2 |
| Avaliado limpo | Avaliado e sem sinais | Entra com R = 0 |
| Obsoleto | Avaliado, sem nenhum fator com `v = 1` | Subconjunto dos avaliados. Valor desliza **para cima**, em direção ao prior (§8.2) |
| Presumido | Objeto do inventário de EXP ou CTL.A, com fonte da categoria habilitada, cujas leituras têm todas `v = 0`, ou que nunca teve leitura | `R_pres` (§4.2), só por inclusão conservadora |
| Não avaliado | GOV: host com achado aberto sem `sla_due_date`. REG: cenário sem tratamento avaliado (residual nulo, ou "valor não confiável" pela D-03), que entra no valor pelo inerente | Reduz a cobertura |
| Indisponível | Categoria com ω > 0 e inventário no escopo, mas sem fonte habilitada (AME: sem intake) ou sem nenhum objeto avaliado ou presumido; ou, no interino, EXP de atividade (inventário existe, vínculo não, §10.1) | Fora de `K(S)`, com renormalização; `cov_k = 0`; chip "Indisponível — requer ‹integração›"; intervalo sempre exibido |
| Não aplicável | Categoria sem inventário no escopo (por exemplo, REG e AME num nó de classe de ativo) | Fora de `K(S)` e fora do denominador da COV |
| Não configurado | Ex.: teto de apetite sem linha | Sinal inativo, selo próprio; a parte de apetite do cenário sai da cobertura de GOV (§8.4) |

### 8.2 Estados não envelhecem para o lado seguro; eventos decaem

- **Estados** não expiram por idade: achado aberto, score de fornecedor, rating, residual, resultado
  de avaliação. A **leitura vigente** é o último valor que a fonte gravou e permanece até evidência de
  ciclo de vida:
  - achado fechado;
  - host Retired;
  - Vision One `lastSeen` > 30 d, depois da correção D-11;
  - CMDB `DeactivateMissing`;
  - conexão removida.

  Se a fonte deixa de expor o valor (conexão desabilitada), a tabela de objetos fornece o último `R`
  por até 400 dias; depois disso, só o prior.
- **Eventos** decaem: incidentes com meia-vida de 30 d e janela de 90 d após o encerramento.
- **Validade de uma leitura**, por fonte:

  ```
  v = 1                      se idade ≤ W
  v = 1 − (idade − W)/W      se W < idade < 2W
  v = 0                      se idade ≥ 2W
  F_eff = F + (1 − v) · max(0, π · m(crit_o) − F)          π = 0,5  (só sobe)
  ```

  Um objeto bom desliza até o prior conservador; um objeto ruim mantém o valor ruim, **inclusive com
  `v = 0`**. Exemplo: host de criticidade 4 com score Vision One 90 lido há 15 dias (W = 7, logo
  `v = 0`) e uma varredura Nessus limpa hoje. EXP.D entra com `max(0,90; 0,5 × 0,85) = 0,90` e EXP.V
  com 0; `R_EXP = 0,90`, e o host está avaliado pela varredura fresca.

| Fator / leitura | Idade medida por | W |
|---|---|---|
| EXP.D Vision One | `hosts.risk_score_updated_at` | 7 d |
| EXP.D Tenable AES | `last_assessed` do export [N] | 30 d |
| EXP.V em host, achados de varredura e importadores | `hosts.last_assessed_at`, gravada só por ingestão de varredura e Tenable [C, coluna nova, D-18]. Interino: o maior `last_detection` entre os achados do host vindos de importadores de varredura e de código, **excluídos** `trendmicro-visionone` e `securityscorecard`; e `last_verification_date` **só** para host com `external_provider` nulo e sem vínculo CMDB, porque o inventário do Vision One, o pseudo-host da SSC, o Jira Assets e toda ingestão de achados também carimbam esse campo (`TrendMicroService.cs:561`, `SecurityScorecardService.cs:481`, `FindingIngestionService.cs:538,562`, `JiraIntegrationService.Assets.cs:361,583`) | 30 d |
| EXP.V em host, CVEs do Vision One (só sem leitura V1-DEV) | Maior `last_detection` dos achados `trendmicro-visionone` do host | 7 d |
| EXP.V em aplicação (sem host) | Último `scan_imports` de importador de código para a entidade | 30 d |
| EXP.X SSC | `captured_at` da linha `is_overall` da conexão | 7 d |
| CTL.A | Data de submissão. Interino: `assessment_runs.run_date`, que significa "último toque" (D-16) | 365 d |
| CTL.C | `createdDateTime` do `securityPosture` [N] | 7 d |
| CTL.P, REG, GOV | Lidos do banco a cada snapshot (a obsolescência do registro é o crédito, §6.1) | — |
| Criticidade de CMDB e fornecedor | Última leitura do objeto em `jira_asset_objects` / sincronização | 30 d. Após 2W o valor permanece, mas conta como contexto obsoleto |

### 8.3 Presunção e inventário

- **Presunção.** Um objeto do inventário de EXP ou CTL.A sem leitura com `v > 0` entra em `Pres_k`
  com `R_pres` (§4.2): o maior entre a leitura retida e `ū_k·π·m(crit)`.
  - Só entra pela inclusão conservadora por objeto (§4.3).
  - Os **pontos de presunção** (`C_k` − `C_k` só com avaliados) são publicados.
  - Só há presunção quando alguma fonte da categoria está habilitada; sem fonte, a categoria é
    "indisponível".
- **Inventário (denominador da cobertura).** A pertença ao inventário depende do ciclo de vida do
  objeto, **nunca do estado medido**.

  | Categoria | Inventário |
  |---|---|
  | EXP | Hosts ativos, até a saída por ciclo de vida (Retired; Vision One `lastSeen` > 30 d depois da D-11; CMDB `DeactivateMissing`). Host sem nenhuma fonte de ciclo de vida (fora do CMDB e do Vision One) sai depois de `janela_inventario_sinal` (400 d) sem nenhum sinal, validada ≥ 2W da fonte mais lenta do objeto; os que saem assim com última leitura ruim ficam listados no painel. Mais um objeto domínio por conexão SSC ativa; aplicações com achado exposto sem host, enquanto houver achado aberto; objetos Vision One [N]. Pseudo-hosts da SSC nunca contam como máquina |
  | REG | Cenários abertos **e** processos críticos do escopo (criticidade declarada ≥ 4 no interino; BIA do M41 no pleno) |
  | GOV | População **cenários**: cenários abertos. População **ativos**: o mesmo inventário de hosts e aplicações de EXP, exceto o domínio SSC. Host sem nada vencido entra com R = 0. Um host é avaliado quando todos os seus achados abertos têm `sla_due_date`; achados legados sem prazo o deixam "não avaliado" e reduzem a cobertura. Descobrir um achado novo ou corrigir o último não acrescenta nem remove objeto |
  | AME | Entidades-alvo do escopo, todas avaliadas se o intake estiver ativo. Sem intake, a categoria fica **indisponível**: zero incidentes sem processo de registro não é evidência de calma |
  | CTL | População **avaliações**: entidades do escopo dos tipos em `tipos_avaliaveis` (padrão: os tipos que têm ao menos uma execução Submitted em qualquer ponto da organização) e hosts com alguma execução, até a saída por ciclo de vida do alvo; uma unidade nunca avaliada de um tipo avaliado entra no denominador. População **cenários**: todos os cenários abertos (CTL.P, com rampa). Tenants Vision One [N] |

### 8.4 Cobertura

```
cov_k(S) = Σ_{o∈Aval_k(S)} w'_o · e_o / Σ_{o∈Inv_k(S)} w'_o      w' com as cotas de população calculadas sobre o inventário
COV(S)   = Σ_{k∈A(S)} ω_k · cov_k / Σ_{k∈A(S)} ω_k              A(S) = categorias com ω_k > 0 e inventário (as não aplicáveis saem)
```

- Como o peso é a criticidade, um ativo crítico faltando pesa mais.
- `e_o = 1`, exceto para cenário em GOV sem teto de apetite aplicável: a parte de apetite não é
  avaliável, e `e_o = 1 − u_A/Σ_f u_f` (0,77 com o padrão).
- **REG:** `cov_REG = cov_tratamento × cov_processos`, com
  - `cov_tratamento = Σ w_r dos cenários com tratamento avaliado / Σ w_r`;
  - `cov_processos = Σ m(crit) dos processos críticos com ao menos um cenário aberto / Σ m(crit) dos
    processos críticos`, igual a 1 com o chip "nenhum processo declarado crítico" quando não há
    nenhum. É a métrica "cobertura de processos críticos" da metodologia, na versão interina.
- Uma categoria **indisponível** sai de `K(S)` com renormalização, mas continua no denominador da
  cobertura com `cov_k = 0`. Uma categoria **não aplicável** sai dos dois.

### 8.5 Qualidade dos dados (não é confiança da evidência)

```
fresh_k = Σ_{Aval_k} w'_o · v_o / Σ_{Aval_k} w'_o
fresh   = Σ_{k∈K} ω_k · fresh_k / Σ_{k∈K} ω_k
Qualidade = COV · fresh          selo: Alta ≥ 0,75 · Média ≥ 0,50 · Baixa < 0,50
ctx_k   = fração (por peso) de objetos avaliados com criticidade de origem não padrão e atribuição não nula
```

- `ctx_k` é publicado ao lado e não entra na qualidade.
- A fração de hosts e aplicações "Não atribuído" (por peso) é publicada à parte. Acima de
  `limiar_nao_atribuido` (padrão 0,10), o drill-down por unidade mostra aviso de cobertura (D-20).
- **A confiança da evidência do M40** (confirmada / indicativa / hipótese) é exibida **à parte**, como
  distribuição. Nunca é misturada à qualidade, porque é uma propriedade do cenário e "independente da
  pontuação quantitativa".

### 8.6 Regras de exibição por cobertura

**Intervalo de ignorância** `[ICR_inf, ICR_sup]`: a fórmula da manchete aplicada com

- cada categoria indisponível com ω > 0 valendo 0 e valendo 100, **sem renormalizar**; e
- dentro de cada categoria disponível, os objetos presumidos e não avaliados do inventário valendo 0 e
  valendo `R_max,o`, o maior R que o objeto pode atingir na categoria (`max_f u_f`, com `m(crit)` nos
  fatores V). Cenários sem tratamento avaliado ficam no inerente nos dois limites: ausência de
  avaliação não autoriza supor redução.

O intervalo é sempre calculado e guardado. Pela I5, o valor pontual nunca fica abaixo de `ICR_inf`
por causa de objetos ausentes.

| Situação | O que se mostra |
|---|---|
| COV ≥ 0,75 e nenhuma categoria indisponível | Valor pontual e faixa; intervalo no detalhe |
| 0,40 ≤ COV < 0,75, **ou** qualquer categoria com ω > 0 indisponível | Valor, faixa e intervalo |
| COV < 0,40 | "**ICR indicativo**": sem faixa e sem cor; só valor e intervalo |
| `K(S)` vazio | "Sem dados" |

Comparações entre nós só valem entre nós com o mesmo conjunto de categorias aplicáveis (§10.1).

**Também ficam sempre visíveis:**

- registros com `entity_id` nulo ("Não atribuído") e sua fração;
- aplicações órfãs na raiz;
- objetos com criticidade padrão;
- pseudo-hosts SSC;
- fontes obsoletas com a data da última leitura;
- ativos críticos presumidos;
- hosts que saíram do inventário por inatividade com última leitura ruim.

---

## 9. Tempo: snapshots, tendência, "o que mudou", descontinuidades

### 9.1 Snapshot diário oficial

**Horário e job:**

- Diário às **05:00 UTC**: depois da passada de residual (02:20), do Vision One (03:00) e da SSC
  (04:00); antes dos digests (07:00) (`JobsManager.cs`).
- Job `RiskIndexSnapshotJob`, registrado com `services.AddScoped<RiskIndexSnapshotJob>()` em
  `src/BackgroundJobs/ConfigurationManager.cs`.
- **Escopo:** o principal de background é autenticado e não tem claim de escopo nem de entidade, então
  cai em `DenyAll` em todo job (D-01, confirmado). A correção da D-01 dá a ele `scope=global`; assim o
  snapshot e todo serviço reutilizado dentro dele (por exemplo, a comparação de apetite e o SLA) leem
  o banco inteiro, sem depender de `bypassEntityScope` em cada chamada.
- Acompanha um teste de que **todo job recorrente resolve** no contêiner de DI e um teste que roda o
  `DalService` real com o acessor de background e vê linhas semeadas.

**O que grava** (tabelas só de inserção, §12.5):

- **Por nó** (`risk_index_snapshots`):
  - ICR sem piso e com piso, em precisão total, e o inteiro exibido; `C_k`;
  - COV, `fresh`, qualidade e intervalo;
  - pisos ativos e chips;
  - id do perfil e o modo dele, e o hash dos insumos externos (§9.5);
  - contagem de objetos e pontos de presunção;
  - as 50 maiores contribuições, com a sensibilidade do indicador;
  - a atribuição de variação em relação ao dia anterior.
- **Por objeto e categoria** (`risk_index_object_days`):
  - `R_{o,k}`, `w_o`, população, `crit`, a origem e o autor da criticidade, o estado;
  - por fator: tipo, `x`, `v`, `u_f` e carimbo da leitura;
  - por cenário: nível de impacto, rank, `n` da campanha, `r_res`, `r_inh`, `T`, atraso, `cred` e
    `σ_ref`;
  - atribuição (ids de entidade);
  - até `k_sinais_max` = 50 sinais normalizados de cada fator, com os flags usados.

**Retenção:**

- Objetos: **400 dias**. Basta para a prévia de sensibilidade de 90 dias, a ponte de rebaseline e a
  comparação ano a ano.
- Nós: **1825 dias**, alinhado a `audit_logs`.
- O expurgo roda num job de retenção dedicado, que é o único caminho de exclusão.

**Nós pré-calculados e cálculo sob demanda:**

- Pré-calculados: raiz, cada entidade dos tipos organization, organizationUnit, subOrganizationUnit,
  businessProcess e application, cada classe de ativo na raiz e cada fonte na raiz.
- Combinações arbitrárias (unidade × classe, processo × fonte) são **recalculadas a partir das linhas
  de objeto do dia**. Usam as mesmas entradas, então valem como "oficial derivado".
- O recálculo com dados vivos é **prévia não oficial**, com cache de 15 min chaveado por (escopo, hash
  das claims, versão do perfil). Não grava nada.

### 9.2 Eventos e estados

Ver §8.2. A idade de um incidente fechado é `agora − data de encerramento`:

- a data de encerramento é `ResolvedAt`, quando ele existir (D-15);
- até lá, é a data do **primeiro snapshot que viu o incidente fechado**, congelada: edições posteriores
  em `LastUpdate` não reiniciam o decaimento;
- para incidentes já fechados antes do primeiro snapshot, `LastUpdate` é lido uma vez, no primeiro
  snapshot, e congelado, rotulado como proxy em hora local (D-15).

A validação exige `janela_incidente ≥ 3 × meia_vida_incidente`, para que `d` no corte valha no máximo
0,125 e não haja degrau grande.

### 9.3 Tendência

- **Valor do dia:** o snapshot cru, em inteiro.
- **Linha de tendência:** EWMA sobre o ICR\*: `E_t = α·ICR*_t + (1 − α)·E_{t−1}`, com
  `α = 2/(N+1) = 0,25` para N = 7.
  - `E_0` é o primeiro valor depois de cada descontinuidade: a EWMA **reinicia** em todo marcador da
    §9.5.
  - Dia sem snapshot: o último valor é repetido, marcado como lacuna.
- **Seta ↑/↓:** calculada sobre os **inteiros exibidos**: só quando
  `abs(round(ICR*_t) − round(ICR*_{t−7})) ≥ 3`. Se `t−7` faltar, usa o snapshot mais próximo até
  `t−10`; senão, não há seta. Nunca atravessa uma descontinuidade. O limiar é um **filtro de
  estabilidade**, não uma magnitude de risco.
- **Detalhe diário:** quando `abs(ΔICR*) ≥ 1`, como no Vision One.
- **Janelas:** 30, 90 e 365 dias.
- **Linhas de referência:** limites das faixas; teto de apetite na série de REG; nível-alvo (M44, T180)
  no modo pleno.
- **Resumo mensal por classe de ativo** (equivalente ao *Risk Summary* do Vision One): média mensal dos
  `C_k` diários restritos à classe.

### 9.4 "O que mudou": atribuição exata por caminho

A diferença simples das contribuições de Euler **não serve** para variações. Quando o objeto
dominante troca, ela culpa objetos que não mudaram (Exemplo C, §11.3). A atribuição usa **Aumann–Shapley**
(gradiente integrado) sobre as entradas dos objetos:

```
ΔICR* = A_cfg + A_cov + A_estr + Σ_{(o,k) ∉ E} A_{o,k} + A_na

A_cfg  = Δ_versão no dia da troca de perfil (ponte, §9.5), ou efeito de insumo externo alterado; 0 nos demais dias
A_cov  = [ICR*(x_t; K_t) − ICR*(x_t; K_∩)] + [ICR*(x_{t−1}; K_∩) − ICR*(x_{t−1}; K_{t−1})]
         K_∩ = K_t ∩ K_{t−1}            (efeito de categorias que entraram ou saíram)
A_{o,k} = ∫₀¹ ∇_{x_{o,k}} ICR*(x(θ); K_∩) · Δx_{o,k} dθ,     x(θ) = x_{t−1} + θ·(x_t − x_{t−1})
E      = pares (o,k) dos objetos que entraram no nó ou saíram dele só por reatribuição
A_estr = Σ_{(o,k) ∈ E} A_{o,k}   ("efeito de reestruturação"; esses pares não entram no Σ de risco)
A_na   = resíduo não resolvido ("efeito não atribuído")
```

- **Reatribuição** é a entrada ou saída de um objeto que existia nos dois dias e mudou de entidade:
  edição do mapa (`entities.parent`, tipo, propriedades `organizationUnit`/`applications`) ou regra de
  mapeamento de fornecedor que recarimbou o objeto. O caminho de integração é o mesmo dos demais
  objetos que entram e saem; só a parcela muda de rótulo. Assim uma mudança administrativa nunca
  aparece como mudança de risco.
- A reestruturação gera **marcador** no nó quando o conjunto do fecho muda ou quando uma regra de
  mapeamento recarimba objetos do nó no ciclo. A reatribuição manual de um objeto isolado gera só o
  evento, sem marcador (§9.5).
- **Troca de modo** (interino → pleno) só acontece junto com uma versão nova de perfil. O efeito vai a
  `A_cfg` pela ponte.

**Caminho:**

- `x` reúne `R_{o,k}` e `w_o`. As cotas de população são fixas dentro de uma versão.
- Objeto que **entra** percorre `R(θ) = θ·R_o` e `w(θ) = θ·w_o`; objeto que **sai**, o inverso. Assim
  um objeto novo que passa a sustentar o piso recebe o efeito do piso continuamente.
- Objeto que muda de conjunto (presumido ↔ avaliado, ou avaliado ↔ presumido em `2W`) é tratado como
  saída de um conjunto mais entrada no outro.
- O prefixo da inclusão conservadora, o argmax do piso e o ramo ativo são reavaliados em cada θ; nos
  empates, o gradiente é dividido igualmente.

**Integração:**

- Por **trechos** entre os pontos de troca (θ em que muda o argmax, o ramo ou o prefixo), localizados
  por bissecção. Dentro de cada trecho, regra do ponto médio com 64 passos. Isso torna exata a
  atribuição nos trechos lineares, que são a maioria.
- O resíduo `ΔICR* − Σ` é publicado; se passar de 0,05 ponto, os passos dobram (até 1024). O que
  sobrar depois do teto é publicado como "efeito não atribuído".

**Gradientes analíticos**, sobre `w'` (§4.3), com `W_g = Σ_{x∈g} w_x` e `M_g^p = Σ_{x∈g} w_x R_x^p / W_g`:

- ramo da média: `∂M_p/∂R_o = w'_o·R_o^(p−1)·M_p^(1−p)` e
  `∂M_p/∂w_o = q̃_g·(R_o^p − M_g^p) / (p·W_g·M_p^(p−1))`;
- quando `M_p = 0`, vale o limite: com um único objeto não nulo, `M_p = w'^(1/p)·R`; a parte de uma
  categoria toda limpa é 0;
- ramo do piso: `τ` para o argmax, dividido em caso de empate;
- manchete: `ω_k/Σω` (média) ou `τ_G` (piso, dividido em empate).

**Publicação:**

- Efeitos por objeto e por categoria, separados em **efeito de risco** (variação de R) e **efeito de
  contexto** (variação de peso: criticidade, impacto, rank, com o autor da mudança).
- Efeito de cobertura, efeito de configuração, efeito de reestruturação e efeito não atribuído.
- **Efeito de piso** = `ΔICR − ΔICR*`, sobre o valor exibido.
- Eventos: objetos novos e removidos, pisos ligados e desligados, aceites vencidos e renovados, fontes
  que ficaram obsoletas.
- **Exibição:** parcelas arredondadas a pontos inteiros, ou em percentual da variação, ordenadas. As
  duas casas decimais dos exemplos deste documento existem só para conferência e ficam no detalhe
  exportável.

### 9.5 Descontinuidades

**O que gera marcador vertical:**

- nova versão de perfil, incluindo troca de preset (que é sempre rebaseline, §12.4);
- fonte nova habilitada;
- troca de modo (interino → pleno), sempre com versão nova de perfil;
- mudança do teto de apetite (`RiskAppetite` é auditado);
- reestruturação do mapa de entidades, no nó cujo fecho mudou ou que recebeu ou perdeu objetos por
  regra de mapeamento (§9.4, `A_estr`);
- **mudança de insumo externo que o ICR não copia para o perfil**: `sla_configurations`,
  `quantitative_band_thresholds` e `risk_workflow_residual_strategy`. Cada snapshot guarda um hash
  desses insumos; quando ele muda, o dia ganha marcador e o efeito vai para `A_cfg` como "insumo
  externo alterado". Hoje essas tabelas estão fora da trilha de auditoria (D-22).

A cadência (`review_levels`), os limites do registro (`risk_levels`) e a base de cadência
(`next_review_date_uses`) **não** geram marcador, porque o ICR usa as cópias aprovadas no perfil (§6.1).

**No dia da troca de versão:**

- Calculam-se as duas versões (a **ponte**) e publica-se `Δ_versão`.
- Não há seta através da quebra, e a EWMA reinicia.
- Os últimos 90 dias são recalculados na versão nova a partir das linhas de objeto e aparecem como
  **sobreposição visível**.
- A série antiga **nunca é reescrita**.
- O replay a partir das linhas de objeto cobre: ω, p, τ, τ_G, ρ, K (até os 50 sinais guardados),
  `m(crit)` e `m_a` (a criticidade é guardada), `u_f` (o `x` de cada fator é guardado), π (o `v` de
  cada fator é guardado), β (rank e `n` são guardados), cotas de população (a população é guardada),
  λ e `cred_min` (atraso, T, `r_res` e `r_inh` são guardados) e os valores dos sinais de GOV.
  Mudanças de âncora ou de multiplicador de normalização exigem os dados brutos. Por isso só são
  recalculadas para o dia corrente, e a sobreposição fica rotulada "parcial".

**Mudanças de modelo dos fornecedores viram anotações, não quebras:**

- algoritmo do CRI do Vision One;
- VPR v2 da Tenable em 2026-07-01;
- recalibração mensal da SSC;
- penalidade de violação da SSC, de 10% com decaimento em 30 d.

---

## 10. Drill-down e roll-up

### 10.1 Semântica por conjunto

```
fecho(n) = n ∪ descendentes de n por entities.parent
         ∪ processos cuja propriedade EAV organizationUnit contém n ou um descendente de n
         ∪ descendentes desses processos por entities.parent (atividades)
         ∪ aplicações listadas na propriedade EAV applications desses processos
O(n)     = união deduplicada dos objetos atribuídos a algum nó de fecho(n)
ICR(n)   = fórmulas da §4 aplicadas a O(n)          mesmo perfil, mesmos parâmetros
```

- **Processos não pendem da árvore.** `businessProcess` é tipo raiz (`isRoot: True`, sem
  `allowedChildren`) e sua unidade é a propriedade EAV **multivalorada** `organizationUnit`; as
  aplicações do processo estão na propriedade `applications` (`src/API/EntitiesConfiguration.yaml:346-392`).
  Um processo pode pertencer a várias unidades e entra inteiro em cada uma. Aplicações criadas pelo
  Jira Assets ficam na raiz (`JiraIntegrationService.Assets.cs:480`) e só entram num fecho pelos
  processos que as listam.
- **Quando a árvore e a propriedade divergem** (um processo filho de uma unidade pela árvore e ligado
  a outra pela propriedade), vale a união, e o vínculo aparece como "vínculo divergente" na qualidade
  de dados.
- **Nunca** se calcula média ou soma de índices de filhos.
- Filtros combinados são interseções: Unidade U × Dispositivos = O(U) ∩ Dispositivos.
- O fecho é calculado pelo serviço do ICR, porque nenhum serviço percorre a árvore hoje. Uma tabela de
  fecho que materialize também os vínculos EAV como relações tipadas vem depois (D-17). Até lá, o
  fecho é [C].
- **Conjunto de categorias por tipo de nó.** Unidade, processo e aplicação: todas. Nós de classe de
  ativo e de fonte: EXP, GOV (população ativos) e CTL (avaliações de hosts); REG e AME são "não
  aplicáveis" ali, porque cenários e incidentes não são ativos. Nós de processo no interino: EXP é
  **parcial**, só com os objetos de achado sem host das suas aplicações; hosts e serviços "requerem
  M39". Nós de atividade no interino: EXP é "indisponível — requer M39" (não "não aplicável": a
  atividade tem ativos, só não há vínculo). Nos dois casos o intervalo aparece sempre. Por isso só se
  comparam nós do mesmo tipo.

### 10.2 Regras de atribuição

| Objeto | Atribuído a | Disp. |
|---|---|---|
| Host | `hosts.entity_id`; mais os vínculos M39 (aplicação, serviço, processo) | [C] D-20: `entity_id` só é gravado na criação, a partir da conexão ou de quem importa; hosts casados nunca são recarimbados; importação de administrador grava nulo; não há campo na GUI nem filtro (`FindingIngestionService.cs:562-567`, `TrendMicroService.cs:534`, `JiraIntegrationService.Assets.cs:293-299`). [N] M39 |
| Achado com host | A entidade **do host**, não `vulnerabilities.entity_id`, que pode divergir | [H] |
| Achado sem host | `vulnerabilities.entity_id` (objeto aplicação/entidade); nulo vai para "Não atribuído" (§3.4) | [C] D-21 |
| Cenário | `risk_to_entity ∪ {risks.entity_id}` (união deduplicada enquanto os vínculos não forem unificados; backfill de `risks.entity_id` é pré-requisito) | [C] D-05 |
| Incidente | `impacted_entity_id ?? entity_id` | [H] |
| Execução de avaliação | `assessment_runs.entity_id`; `host_id` aponta o objeto host | [H] |
| Domínio SSC; tenant Vision One | Entidade da conexão | [H] |
| Sem vínculo | Nó "**Não atribuído**", visível na raiz, com sua fração por peso | [H] |

**Processos, aplicações e atividades:**

- Processo P recebe:
  - cenários (`risk_to_entity` = P);
  - incidentes com entidade impactada P;
  - as aplicações da sua propriedade `applications` e os objetos de achado sem host delas;
  - no pleno, os objetos técnicos (hosts) das aplicações e serviços de P, pelo M39.
- No interino, o processo mostra REG, GOV, AME e CTL completos (CTL.A pelas avaliações cujo alvo é o
  processo; CTL.P pelos cenários vinculados) e EXP **parcial**: só os objetos de achado sem host das
  suas aplicações; hosts e serviços "requerem M39", com o intervalo de ignorância sempre exibido. A
  atividade, que não tem `applications`, mostra EXP "**indisponível — requer M39**".
- **Atividade** é o tipo novo `activity`, filho de `businessProcess` pela árvore
  (`businessProcess.allowedChildren: [activity]`, T242). Como `risk_to_entity` e
  `incidents.impacted_entity_id` já aceitam entidade de qualquer tipo, a atividade recebe no
  interino, sem DDL, o mesmo que o processo: cenários vinculados a ela, incidentes que a impactam e os
  sinais de GOV e CTL desses cenários e as avaliações cujo alvo é ela. O processo contém as suas atividades pelo fecho (§10.1), então um
  cenário vinculado à atividade conta uma vez no processo e na unidade. EXP por atividade é
  "indisponível — requer M39" até existirem vínculos tipados atividade↔ativo; o processo tem EXP
  parcial pelas suas aplicações.

### 10.3 Muitos-para-muitos

- **Pertencimento integral:** um host que serve dois processos entra **inteiro** em cada um e **uma
  vez** em qualquer nó que contenha os dois. O painel marca o objeto como "compartilhado com N".
- **Não há rateio fracionário.** O índice é intensivo, então não existe dupla contagem a dividir.
- **Os filhos não somam ao pai.** O pai pode ficar abaixo de todos os filhos, porque contém mais
  objetos limpos. Garantia formal: `C_k(pai) ≥ 100·τ·max R_o` de qualquer objeto de qualquer filho.
- **Não há piso hierárquico.** O valor da organização é único, independentemente da dimensão de
  drill-down.
- **Grandezas aditivas** (contagens, Σ E[L]) são recontadas no conjunto distinto de cada nível, com a
  nota "soma dos filhos ≠ pai: n objetos compartilhados".

### 10.4 Autorização

O escopo hoje é **plano**: uma claim numa unidade não concede as subunidades
(`src/DAL/Context/EntityScope.cs`; `DALService.cs:172-188`). Por decisão do product owner
(2026-10-05), passa a ser **hierárquico** (F-3, T292):

- uma claim numa entidade concede a entidade e **todos os seus descendentes** por `entities.parent`
  (subunidades, processos e aplicações filhos, atividades dos processos), e **nunca** os ascendentes nem
  os irmãos;
- a expansão vale para os filtros de leitura e para a guarda de escrita;
- vínculos que existem **só** em propriedades EAV (um processo que lista a unidade em
  `organizationUnit` sem ser filho dela na árvore; uma aplicação listada em `applications`) **não**
  concedem acesso. Para que os usuários de uma unidade vejam um processo, ele deve ser filho da unidade
  na árvore (`organizationUnit.allowedChildren` já admite `businessProcess`); um processo compartilhado
  fica sob a unidade dona e é concedido às outras por claim explícita.

Com o escopo hierárquico, o gestor de uma unidade recebe o snapshot oficial do nó sempre que o fecho do
nó (§10.1) está todo na sua subárvore. O "parcial" fica restrito aos nós cujo fecho inclui vínculos
só-EAV ou objetos de escopo fora da subárvore.

| Situação | O que o usuário recebe |
|---|---|
| Escopo global, ou claims expandidas (descendentes) ⊇ fecho(nó) | O snapshot pré-calculado do nó |
| Caso contrário | Cálculo ao vivo sobre os objetos cuja linha de origem passa no filtro de escopo do usuário, rotulado "**parcial — visibilidade restrita**", sem tendência e sem revelar contagens ocultas |
| Manchete da organização | Exige escopo global |
| Indicadores de tenant (CRI do Vision One, rating SSC) | Só para escopo irrestrito ou no nó da conexão |

- **Diferente do Vision One**, cujo CRI ignora a visibilidade do usuário, o ICR respeita o escopo.
- Todo cache é chaveado por (escopo, hash das claims, versão do perfil). O cache do Master Dashboard,
  que não é chaveado (`MasterDashboardService.cs:31`), é o anti-exemplo.
- **Permissões novas:** `risk_index_view`, `risk_index_configure` e `risk_index_approve`, mais a
  permissão de edição dos insumos de contexto de entidades da D-17.
  - São semeadas com `INSERT IGNORE` sem id explícito, no padrão de `Data/81.sql:21-23`.
  - O histórico SSC hoje está atrás de `configuration` (`SecurityScorecardController.cs:17`).
- **Terceira linha:** leitura de perfis, relatórios de sensibilidade, pontes e da trilha de auditoria
  do ICR. No pleno, pelo papel de assurance do M47 (T198); até lá, `risk_index_view` mais leitura de
  `audit_logs`. Relatórios de sensibilidade, pontes e histórico de perfil contêm valores de todos os
  nós e da organização, então exigem **escopo irrestrito** como a manchete; um usuário com escopo vê o
  perfil (parâmetros, estado, aprovadores) sem esses valores.

---

## 11. Exemplo completo: Unidade X (modo interino)

Data de referência 2026-10-05. Perfil Equilibrado. Teto de apetite global `MaxAcceptableResidual = 5,0`,
configurado em 2026-06-01. Cadência pela base padrão `InherentRisk`, com Very High ≥ 9,0 (proposta da
D-02). Modo interino depois do Estágio A0: D-01 corrigida, `criticality` e `internetFacing` entregues
com a permissão da D-17. Todos os números abaixo foram calculados por um motor de referência que
implementa as §§3–9.

O fecho da Unidade X (organizationUnit, criticidade declarada 3) contém:

- a subunidade **Lab L1** (crit 2), filha pela árvore;
- os processos **P-Matrícula** (crit 5) e **P-Pesquisa** (crit 3), que têm a Unidade X na propriedade
  `organizationUnit`;
- a aplicação **APP-03** (portal, crit 5, `internetFacing = true`), listada em `applications` de
  P-Matrícula. Seus achados SAST foram importados por uma conta de serviço com escopo só na entidade
  APP-03, o único caminho que hoje atribui achados sem host a uma aplicação (D-21).

### 11.1 Objetos e categorias

**EXP — exposição técnica** (população única)

| Objeto | crit (`m`) | Dados | Cálculo | R |
|---|---|---|---|---|
| SRV-01 | 5 (1,00) | V1 = 90, fresco | Composto | 0,900 |
| 20 × TST | 1 (0,40) | V1 = 10 | Composto | 0,100 cada |
| 10 × WS | 2 (0,55) | V1 = 25 | Composto | 0,250 cada |
| DB-02 | 4 (0,85) | Nessus: CVE com VPR 6,7; dois CVEs CVSS 5,3 com exploit disponível; nenhum com exploração | s = 0,670 (VPR, sem `m_x`); min(0,90; 0,53 × 1,15) = 0,6095 (×2). MRA = 0,670 + 0,3·0,33·(1 − 0,3905²) = 0,7539; × 0,85 | 0,641 |
| APP-03 | 5 (1,00) | SAST: 1 Alta e 3 Médias, sem CVSS nem exploração; internet-facing | s = min(0,90; 0,75 × 1,2) = 0,90; min(0,90; 0,50 × 1,2) = 0,60 (×3). MRA = 0,9281 | 0,928 |
| domínio (conexão SSC) | `crit_ext` 4 (0,85) | SSC 72 | s = 0,51; × 0,85 | 0,4335 |
| BKP-04 | 4 (0,85) | Última varredura limpa há 45 d (W = 30), datada pela regra interina: host sem `external_provider` e sem vínculo CMDB | v = 0,50; 0 + 0,5 × (0,5 × 0,85) | 0,2125 (obsoleto) |
| WS-ADM | 3 (0,70) | Nunca varrido | Presumido: 0,5 × 0,70 | 0,350 |
| LEG-05 | 5 (1,00) | Última varredura há 70 d (≥ 2W), leitura retida 0,30 | Presumido: max(0,30; 0,5 × 1,00) | 0,500 |

- `M_4(Aval)` = 54,53. Os presumidos, em ordem decrescente, são LEG-05 (0,50) e WS-ADM (0,35); os
  dois ficam abaixo de `M_4` (0,545), então nenhum prefixo eleva o valor (54,31 com LEG-05; 53,91 com
  os dois). A inclusão conservadora fica com os avaliados.
- O piso é `0,80 × 92,81` = **74,25** → **C_EXP = 74,25**, sustentado por APP-03.
- `cov_EXP` = 18,05 / 19,75 = 0,914; `fresh_EXP` = 0,976.

**AME — ameaça e eventos.** O intake está ativo. `m_net` não se aplica a incidentes, então o INC-2
vale 0,75 mesmo no portal internet-facing.

| Entidade | crit (`m`) | Eventos | R |
|---|---|---|---|
| Unidade X | 3 (0,70) | INC-1, phishing fechado há 20 d: 0,50 × 2^(−20/30) = 0,315; × 0,70 | 0,2205 |
| APP-03 | 5 (1,00) | INC-2, `unauthorized_access` aberto: 0,75 | 0,750 |
| P-Matrícula, P-Pesquisa, Lab L1 | 5, 3, 2 | — | 0 |

`M_4` = 53,27 e piso 0,80 × 75 = 60,0 → **C_AME = 60,0**.

**REG — cenários.** Ranks vindos da última campanha concluída (n = 6). `d_ref` é a última revisão
qualificada (§6.1).

| Cenário | Inerente | Residual | `d_ref` | T | `due` | Atraso | `cred` | `r_eff` | `σ_r` | `σ_ref` | Impacto | `m_rank` | `w_r` |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| R1 — vazamento de dados de alunos (SIS) | 8,8 | 8,4 | 2026-08-21 | 90 | **2026-09-20** (fim do aceite) | 15 | 0,833 | **8,47** | 0,847 | 0,840 | 5 | 1,30 | 1,300 |
| R2 | 8,0 | 4,0 | 2026-05-08 | 90 | 2026-08-06 | 60 | 0,333 | **6,67** | 0,667 | 0,400 | 4 | 1,24 | 1,054 |
| R3 | 6,0 | nulo | 2026-04-01 (submissão; nunca revisado) | 120 | 2026-07-30 | 67 | — | 6,00 (inerente) | 0,600 | 0,600 | 3 | 1,18 | 0,826 |
| R4 | 4,8 | 3,0 | 2026-09-01 | 120 | 2026-12-30 | 0 | 1,000 | 3,00 | 0,300 | 0,300 | 3 | 1,12 | 0,784 |
| R5 | 3,2 | 2,0 | 2026-01-18 | 240 | 2026-09-15 | 20 | 0,917 | 2,10 | 0,210 | 0,200 | 2 | 1,06 | 0,583 |
| R6 | 2,0 | 1,2 | 2026-09-10 | 240 | 2027-05-08 | 0 | 1,000 | 1,20 | 0,120 | 0,120 | 1 | 1,00 | 0,400 |

- **R1.** Aceite concedido em 2026-03-20, antes de existir o teto; expirou em 2026-09-20 sem
  sucessor, e a renovação agora é bloqueada pelo teto (`RiskAcceptancesService.cs:189-190`). A
  campanha concluída em 2026-08-25 decidiu Escalated, o que não restaura crédito. Sem revisão
  qualificada depois do fim do aceite, o vencimento é 2026-09-20, e não `d_ref + T` = 2026-11-19.
- **R2.** Há uma revisão de 2026-08-30 feita pelo próprio dono do risco: não qualifica
  (autoavaliação), e vale a de 2026-05-08. O residual armazenado 4,0 aparece ao lado do efetivo
  6,67 (I9).
- **R5.** Tem aceite válido até 2026-10-25, concedido em 2026-04-25. A data do aceite não entra em
  `d_ref`, e o `ExpiresAt` não antecipa nada porque é posterior a `d_ref + T`.
- `M_4` = 66,90 e piso 0,80 × 84,67 = 67,73 → **C_REG = 67,73**, sustentado por R1.
- Em Σ w·R⁴, R1 tem 67,4%, R2 21,0% e R3 10,8%.

**GOV — governança e tratamento.** `u`: A 1,0 · C 0,8 · T 0,8 · R 1,0 · G 0,8. Cotas: cenários 0,5,
ativos 0,5.

| Objeto (população) | Sinais | Cálculo | R |
|---|---|---|---|
| R1 (cenários) | GOV.A = max(APT-BRK 0,5 [tarefa em curso]; ACC-EXP 1,0 [aceite expirou em 2026-09-20, sem sucessor, sem revisão nem decisão desde então, e 8,4 > 5,0]) = 1,0. GOV.C = CMP-ESC 0,7 (Escalated em 2026-08-25, sem revisão, aceitação nem tarefa posterior em 30 d; as duas tarefas abertas são de julho). GOV.T = TSK-OVD 12/30 = 0,40 (uma delas vencida há 12 d) | 0,840 × MRA(1,0; 0,56; 0,32) = 0,840 × 1,0 | 0,840 |
| R2 | GOV.C = CMP-PEND 1,0 (pendente em campanha vencida) | `σ_ref` 0,400 × 0,8 | 0,320 |
| R3 | GOV.A = APT-BRK 1,0 (6,0 > 5,0; sem aceite; sem tarefa). GOV.R = REV-NOCREDIT min(1; 67/120) = 0,558 | 0,600 × MRA(1,0; 0,558) | 0,600 |
| R4 | GOV.G = DIV: `X_r` = 0,641 (DB-02, varredura fresca, via `risks_to_vulnerabilities`); 0,641 − `σ_ref` 0,300 = 0,341 > 0,3; acesa em 2026-09-15, depois da última revisão (2026-09-01) | 0,300 × 0,8 × 0,8 | 0,192 |
| R5, R6 | — (R5 tem aceite vencendo em 20 d: só exibido) | — | 0 |
| SRV-01 (ativos) | CVE crítico vencido há 6 d num prazo de 15: 0,95 × 6/15 = 0,38 | 1,00 × 0,8 × 0,38 | 0,304 |
| DB-02 | Alta vencida há 12 d num prazo de 30: 0,75 × 0,4 = 0,30 | 0,85 × 0,8 × 0,30 | 0,204 |
| APP-03, BKP-04, WS-ADM, 20 × TST, 10 × WS | Nada vencido | — | 0 |
| LEG-05 | Achados legados sem `sla_due_date` | Não avaliado | — |

- `M_4` = 52,80 e piso 0,80 × 84 = 67,2 → **C_GOV = 67,2**, sustentado por R1.
- Em Σ w'·R⁴, R1 tem 84,1% e R3 13,9%. Os 35 objetos avaliados da população ativos somam 0,4%, mas
  entram com a mesma cota de 0,5: a pertença deles não depende do estado, e eles não diluem os cenários.
- `cov_GOV` = 0,5 + 0,5 × 17,90/18,90 = 0,974 (LEG-05 não avaliado).
- Os CVEs Vision One do SRV-01 estão fora de EXP.V, porque o host tem leitura V1-DEV, mas contam no
  SLA (§3.4, regra 6).

**CTL — controles e conformidade.** `tipos_avaliaveis` = {organizationUnit, subOrganizationUnit}, os
tipos com execução Submitted na organização; os dois alvos do escopo estão avaliados. Cotas:
avaliações 0,5, cenários 0,5.

| Objeto (população) | Cálculo | R | w |
|---|---|---|---|
| Avaliação da Unidade X (avaliações; razão 0,35) | 0,70 × 0,8 × 0,35 | 0,196 | 0,70 |
| Avaliação do Lab L1 (razão 0,10) | 0,55 × 0,8 × 0,10 | 0,044 | 0,55 |
| IRP de R1 (cenários; `σ_ref` 0,84 → rampa 1; aprovado, último teste há 400 d → s = 0,5) | 0,840 × 0,6 × 0,5 × 1 | 0,252 | 1,30 |
| IRP de R2 a R6 | `σ_ref` ≤ 0,60 → rampa 0 | 0 | — |

`M_4` = 17,53 é menor que o piso 0,80 × 25,2 = 20,16 → **C_CTL = 20,16**, sustentado por R1. No ramo
da média, a composição de referência seria R1 56,1% e Unidade X 43,8%.

### 11.2 Manchete, composição e qualidade

| | REG | EXP | AME | CTL | GOV |
|---|---|---|---|---|---|
| `C_k` | 67,73 | 74,25 | 60,00 | 20,16 | 67,20 |
| Contribuição `ω_k·C_k` (pontos) | 20,32 | 22,27 | 9,00 | 2,02 | 10,08 |

- Média ponderada = **63,69**; piso `τ_G · max` = 0,70 × 74,25 = 51,97, inativo.
- **ICR\* = 63,69 → exibido 64 · Médio.**
- **Selos:**
  - "2 cenários acima do teto de apetite (R1, R3); nenhum com aceite anterior ao apetite vigente";
  - "1 cenário com revisão vencida, dentro do teto pelo residual armazenado e com inerente acima dele
    (R2)";
  - Portão B: "não disponível (requer M45/M46)";
  - Σ E[L]: "nenhum cenário quantificado (0 de 6)".
- **Chips de evidência para o Portão A:** nenhum. Não há `data_breach` aberto nem achado com
  exploração em objeto crítico.
- **Cobertura:** `cov_EXP` 0,914 (WS-ADM e LEG-05 presumidos); `cov_REG` 0,833 (R3 sem tratamento
  avaliado; P-Matrícula, único processo crítico, tem cenário); `cov_GOV` 0,974; AME e CTL 1,0 →
  **COV = 0,920**. `fresh` = 0,993. **Qualidade = 0,914 (Alta).** Intervalo de ignorância, no detalhe:
  [63,69; 65,42].
- **Peso efetivo do registro.** REG, GOV e CTL estão no ramo do piso, todos sustentados por R1: 50,9%
  da manchete depende de um único cenário. O número vai na prévia de sensibilidade (§13.2).
- **Sensibilidade do indicador** (Δ no ICR\*):

  | Objeto | Δ |
  |---|---|
  | R1 em REG (R → 0) | −4,32 |
  | R1 em GOV | −2,88 |
  | APP-03 em EXP | −0,67: o SRV-01 passaria a sustentar EXP em 72 |
  | IRP de R1 em CTL | −0,45 |
  | INC-2 | −4,50 uma meia-vida depois do encerramento |

  A tabela diz quanto o número depende de cada objeto. Não é ordem de tratamento, que vem dos Portões
  C e D (R-1).
- **Modo pleno, Portão A.** Se R1 portar a flag 2 (LGPD) e o predicado do M43 for verdadeiro, o valor
  exibido da Unidade X e da organização passa a "**70 · Alto · piso: Portão A — R1 (flag 2) · sem piso
  64 (Médio)**". A tendência continua plotando 63,7, com o período sombreado.

### 11.3 Exemplo C — "o que mudou" de 2026-10-05 para 2026-10-06

**Mudanças do dia:**

- a Alta do APP-03 foi corrigida (nova varredura SAST);
- R1 recebeu uma **revisão de gestão qualificada** (revisor que não é dono, gestor nem submissor);
- o INC-1 decaiu mais um dia; o BKP-04 envelheceu (`v` 0,467);
- os atrasos de SLA, da tarefa de R1 e das revisões de R2, R3 e R5 cresceram um dia.

**Resultado por categoria:**

| | Antes | Depois | Observação |
|---|---|---|---|
| EXP | 74,25 | 72,00 | O APP-03 cai para 0,701; o SRV-01 passa a sustentar |
| GOV | 67,20 | 48,00 | A revisão encerra ACC-EXP (há revisão depois do fim do aceite) e CMP-ESC (há seguimento do escalonamento). Restam APT-BRK 0,5 (ainda acima do teto, sem aceite, tarefa em curso) e TSK-OVD 13/30: R1 vai a 0,840 × MRA(0,50; 0,35) = 0,464. O R3 passa a sustentar (0,80 × 0,60) |
| REG | 67,73 | 67,20 | A revisão restaura o crédito de R1: `r_eff` 8,47 → 8,40. O R2 decai mais um dia (6,67 → 6,71), sem efeito: REG está no piso de R1 |
| AME, CTL | 60,00 / 20,16 | inalterados | — |

**ICR\*: 63,69 → 59,98 (Δ −3,71); exibido 64 → 60.**

A revisão baixou REG e GOV porque é o caminho legítimo: ela confirma o residual. Uma renovação do aceite
não restauraria o crédito (I10) e, aqui, nem seria possível, porque o teto a bloqueia.

**Atribuição Aumann–Shapley** (integração por trechos):

| Origem | Parcela |
|---|---|
| R1 em GOV (aceite vencido e escalonamento resolvidos pela revisão) | −2,88 |
| APP-03 em EXP (Alta corrigida) | −0,67 |
| R1 em REG (crédito restaurado) | −0,16 |
| INC-1, BKP-04, R2, R5, SLA do SRV-01 e do DB-02 | 0,00: as categorias estão no ramo do piso, e esses objetos não o sustentam |
| Efeito não atribuído | 0,00. Sem a divisão em trechos, 64 passos de ponto médio deixariam 0,02 |

A diferença simples de Euler dentro de EXP atribuiria +72,0 ao SRV-01, que não mudou, e −74,25 ao
APP-03. É o defeito que a atribuição por caminho evita. No painel, as parcelas aparecem em pontos
inteiros e ordenadas: GOV/R1 −3, EXP/APP-03 −1, REG/R1 0.

### 11.4 Exemplo D — drill-down muitos-para-muitos (modo pleno: vínculos do M39)

O APP-03 serve os dois processos.

| Nó | Objetos EXP | C_EXP | Ramo |
|---|---|---|---|
| P-Matrícula {APP-03, SRV-01, domínio, LEG-05 (pres.)} | 4 | **84,13** | Média. O presumido diluiria (78,88) e fica de fora |
| P-Pesquisa {APP-03, DB-02, BKP-04, WS-ADM (pres.)} | 4 | **75,71** | Média. Com o presumido seria 74,25 |
| Lab L1 (subunidade) {20 × TST, WS-ADM (pres.)} | 21 | **28,00** | 10,00 só com avaliados; o presumido eleva (0,35 > 0,10) e o piso vai a 0,80 × 0,35: +18,00 por presunção |
| Unidade X (união) | 37 | **74,25** | Piso, sustentado por APP-03, contado **uma vez** |

O pai está **abaixo dos dois filhos** e não é a média nem o máximo deles. Ele contém os 30 objetos
de baixo risco (R 0,10 e 0,25) das estações e VMs. Mesmo assim, a garantia vale: `74,25 ≥ 100 × 0,80 × 0,928`.

### 11.5 Exemplo E — cobertura parcial e intervalo de ignorância

Suponha a mesma Unidade X com o intake de incidentes inativo e sem nenhuma fonte de CTL habilitada no
perfil (avaliações e IRP). AME e CTL continuam com peso no perfil e têm inventário, mas ficam
indisponíveis.

- `K` = {REG, EXP, GOV}. Os pesos renormalizados ficam 0,40 / 0,40 / 0,20.
- **ICR\* = 70,23 → 70 · Alto.**
- COV = 0,670. Valor, faixa e intervalo de ignorância **[52,67; 79,40]**: AME e CTL valendo 0 e 100,
  sem renormalizar; WS-ADM e LEG-05 valendo 0 e 1,0 em EXP (o limite superior de EXP vai a 80); LEG-05
  valendo 0 e 0,8 em GOV, sem mover o piso de GOV.
- O chip informa "AME e CTL indisponíveis".

A ausência de duas categorias **mudou a faixa**: 64 Médio com elas, 70 Alto sem elas. É por isso que o
intervalo é obrigatório **sempre que falta categoria**, qualquer que seja a COV.

### 11.6 Sensibilidade

**Presets sobre a Unidade X e sobre o caso canônico:**

| Preset | ICR\* da Unidade X | `C_EXP` do caso canônico (§4.7) |
|---|---|---|
| **Equilibrado** | **63,7 (Médio)** | 72,0 |
| Centrado no registro | 65,8 (Médio) | 72,0 |
| Exposição técnica (CTEM) | 64,0 (Médio) | 72,0 |
| Conservador (p = 8, τ = 0,90, τ_G = 0,85) | 71,7 (Alto) | 81,0 |
| Média pura (p = 1, sem pisos; **só na prévia**) | 33,0 (Médio) | 18,9 |

Trocar de preset é rebaseline (§12.4).

**Tornado** (um parâmetro por vez, ±20% ou até o limite da faixa; quando um ω_k muda, os demais são
reescalados proporcionalmente para manter Σω = 1):

| Parâmetro | Variação no ICR\* (−20% / +20%) |
|---|---|
| τ (0,64 / 0,95) | −7,99 / +11,94 |
| σ do cenário dominante, R1 (±20% em `r_inh` e `r_res`) | −5,11 / +5,98 |
| ω_CTL | +0,97 / −0,97 |
| ω_EXP | −0,90 / +0,90 |
| ω_REG | −0,35 / +0,35 |
| p (3,2 / 4,8) | 0,00 / +0,25 |
| ρ (0,24 / 0,36) | −0,13 / +0,13 |
| ω_AME, ω_GOV | até ±0,13 |
| τ_G, π, β, cotas de população (0,3 / 0,7) | 0,00 |

Leitura: **τ, o cenário dominante e o preset dominam**. O comitê deve discutir o piso de cauda e o
peso efetivo do registro (51% aqui), não a segunda casa decimal de ω.

---

## 12. Modelo de configuração

### 12.1 Perfil

- **Um perfil vigente, global.** Não há pesos por entidade, porque isso quebraria a reconciliação
  (I7) e a comparabilidade entre unidades.
- Por entidade variam só os **insumos** que já existem: teto de apetite (`risk_appetites`) e SLA
  (`sla_configurations`).
- Os valores do registro que o ICR lê para a cadência (`review_levels`, os limites de `risk_levels` e
  `next_review_date_uses`) são **copiados para o perfil e aprovados com ele** (§6.1), para que uma
  edição nessas tabelas não mova o índice sem versão.
- Os parâmetros ficam num documento JSON (`parameters`), com as chaves abaixo. Um perfil aprovado é
  **imutável**; mudar significa criar uma nova versão.

### 12.2 Parâmetros

| Parâmetro | Padrão | Faixa permitida | Significado |
|---|---|---|---|
| `faixas` | `[{Baixo/Low, 0}, {Médio/Medium, 31}, {Alto/High, 70}]` | 2 a 5 faixas; o primeiro limite é sempre 0; os demais crescentes em [10, 95]; rótulo em pt-BR e en-US | Limite inferior inclusivo de cada faixa de comunicação, sobre o inteiro exibido. Mudança exige referência de aprovação do Conselho/Reitoria |
| `omega.REG` | 0,30 | [0,20; 0,60] | Peso de REG |
| `omega.EXP` | 0,30 | 0 ou [0,05; 0,60] | Peso de EXP |
| `omega.AME` | 0,15 | 0 ou [0,05; 0,60] | Peso de AME |
| `omega.CTL` | 0,10 | 0 ou [0,05; 0,60] | Peso de CTL |
| `omega.GOV` | 0,15 | 0 ou [0,05; 0,60] | Peso de GOV |
| `omega.TER` | 0 | 0 até o M48; depois [0,05; 0,60] | Peso de TER |
| `u.<fator>` | §2.3 | [0,5; 1,0] | Teto do fator |
| `cotas_populacao` | GOV {cenários 0,5; ativos 0,5}; CTL {avaliações 0,5; cenários 0,5}; tenants em cota igual quando houver | Cada cota em [0,2; 0,8]; soma 1 por categoria | Divisão do peso entre populações (§4.3) |
| `p` | 4 | [2; 16] | Expoente da média de potência entre objetos |
| `tau` | 0,80 | [0,60; 0,95] e trava conjunta | Piso de cauda da categoria |
| `tau_g` | 0,70 | [0,50; 1,00] e trava conjunta | Piso de cauda da manchete |
| `rho` | 0,30 | [0; 0,5] | Restante amortecido do MRA |
| `k_sinais` | 10 | [2; 50] (até 50 sinais são guardados) | Sinais considerados no MRA |
| `m_a` | 0,40 | [0,20; 0,60] | Piso do modulador `m(crit) = a + (1 − a)(crit − 1)/4` |
| `crit_padrao` / `crit_padrao_nao_producao` | 3 / 2 | [2; 4] / [1; 3] | Criticidade desconhecida |
| `crit_max_vision_one` | 4 | [3; 5] | Teto da criticidade de origem Vision One (§5.1) |
| `ambientes_nao_producao` | `["dev", "test", "homolog", "lab"]` | Lista | Vocabulário controlado de `hosts.environment` |
| `crit_ext` | 4 | [3; 5] | Criticidade de domínio externo e tenant |
| `passo_classificacao` / `niveis_sensiveis` | +1 / `[]` | [0; 2] / ids de `securityClassificationLevel` | Δ_dados |
| `ancoras_severidade` | 0,20 / 0,50 / 0,75 / 0,95 | Monótonas em (0, 1] | Base de achado sem VPR/CVSS |
| `m_x_disponivel` | 1,15 | [1; 1,5] | Exploit disponível ou EPSS (só sobre CVSS/severidade) |
| `teto_sem_exploracao` / `piso_exploracao` | 0,90 / 0,95 | [0,70; 0,95] / [0,80; 1,0]; teto < piso | Ordem entre achados com e sem exploração (§3.1) |
| `epss_limiar` | 0,10 | [0,01; 0,5] | Interino; T165 substitui |
| `m_net` | 1,2 | [1; 1,5] | Exposição à internet, só em EXP.V |
| `heuristica_ip_publico` / `faixas_publicas_internas` | desligada / `[]` | Booleano / lista CIDR | Internet-facing por IP de host (§5.3) |
| `ancoras_ssc` | {100: 0; 90: 0,10; 80: 0,35; 70: 0,55; 60: 0,75; 50: 0,90; 0: 1} | Monótonas, com s(100) = 0 e s(0) = 1 | Rating → `s` |
| `ancoras_incidente_categoria` / `_severidade` | §3.1 | [0; 1] | Incidente → `s` |
| `meia_vida_incidente` / `janela_incidente` | 30 d / 90 d | [7; 90] / [30; 365]; janela ≥ 3 × meia-vida | Decaimento |
| `fator_quase_incidente` | 0,5 | [0,25; 1] | `u_AME.Q` |
| `janela_intake_incidentes` | 90 d | [30; 365] | Intake ativo |
| `irp_limiar_sigma` / `irp_rampa` / `irp_teste_validade` | 0,70 / 0,10 / 365 d | [0,5; 0,9] / [0,05; 0,2] / [180; 730] | Centro e meia-largura da rampa de CTL.P; validade do teste |
| `lambda_credito` / `credito_minimo` | 1,0 / 0 | [0,25; 2] / [0; 0,5] | Crédito de tratamento |
| `cadencia_revisao` | Very High 30 · High 90 · Medium 120 · Low 240 (dias) | [7; 365], crescente de Very High para Low | Cópia aprovada de `review_levels` |
| `limites_registro` | Low 0 · Medium 4,0 · High 7,0 · Very High 9,0 | Crescentes em [0; 10], com Very High ≤ 10 | Cópia aprovada de `risk_levels`, com Very High alcançável (D-02) |
| `base_cadencia` | InherentRisk | InherentRisk / ResidualRisk | Cópia aprovada de `next_review_date_uses` |
| `janela_revisao_derivada` | 60 s | [10; 300] | Reconhecimento, na falta de `CorrelationId`, das revisões gravadas por aceitação ou campanha |
| `beta_rank` / `validade_rank_ciclos` | 0,30 / 2 | [0; 0,6] / [1; 4] | Rank de campanha |
| `s.APT_BRK` (sem / com tratamento) | 1,0 / 0,5 | [0; 1] | GOV.A |
| `s.ACC_APT` | 0,5 | [0; 1] | GOV.A, aceite anterior ao apetite vigente |
| `s.ACC_EXP` / `s.ACC_REV` / `carencia_aceite` | 1,0 / 0,5 / 7 d | [0; 1] / [0; 1] / [0; 30] | GOV.A, aceite expirado / revogado |
| `acc_drift_inicio` / `acc_drift_amplitude` | 0,5 / 1,5 | [0; 2] / [0,5; 5] | ACC-DRIFT |
| `s.APT_DUAL` / `prazo_contra_assinatura` | 0,5 / 7 d | [0; 1] / [1; 30] | GOV.A |
| `s.CMP_PEND` / `s.CMP_ESC` / `s.CMP_MIT` | 1,0 / 0,7 / 0,5 | [0; 1] | GOV.C |
| `prazo_seguimento_decisao` | 30 d | [7; 90] | CMP-ESC |
| `prazo_ref_tarefa` | 30 d | [7; 90] | TSK-OVD |
| `s.REV_REQ` / `prazo_revisao_solicitada` | 1,0 / 14 d | [0; 1] / [3; 60] | GOV.R |
| `s.DIV` / `delta_div` | 0,8 / 0,3 | [0; 1] / [0,1; 0,5] | GOV.G |
| `janela_aviso_expiracao` | 30 d | [7; 60] | Só exibição |
| `desconto_aceite` | 0 | **Travado em 0** | Aceite não é mitigação |
| `pi_prior` | 0,5 | [0,3; 1,0] | Prior conservador de obsolescência e presunção |
| `frescor_W` | V1 7 (dispositivo e CVEs); SSC 7; scanners 30; Tenable 30; CMDB 30; avaliações 365 (dias) | [1; 730] | Validade por fonte |
| `janela_inventario_sinal` | 400 d | [60; 730]; ≥ 2W da fonte mais lenta do objeto | Saída de host sem fonte de ciclo de vida |
| `tipos_avaliaveis` | Tipos com alguma execução Submitted na organização | Lista de tipos de entidade | Inventário de CTL.A |
| `cov_ponto` / `cov_indicativo` | 0,75 / 0,40 | [0,5; 0,95] / [0,2; 0,6] | Exibição por cobertura |
| `qualidade_alta` / `qualidade_media` | 0,75 / 0,50 | crescentes em (0, 1) | Selo de qualidade |
| `limiar_nao_atribuido` | 0,10 | [0,02; 0,5] | Aviso de "Não atribuído" no drill-down |
| `cobertura_quantificacao_min` | 0,80 | [0,5; 1] | Σ E[L] "parcial" abaixo disso |
| `piso_portao_a` | 70 | [70; 100]; **não desligável** | φ_A |
| `ewma_n` / `delta_seta` / `delta_detalhe` | 7 / 3 / 1 | [3; 30] / [1; 10] / [0,5; 5] | Tendência |
| `top_contribuicoes` / `passos_atribuicao` / `residuo_max` | 50 / 64 / 0,05 | [10; 200] / [16; 1024] / [0,01; 0,5] | "O que mudou" |
| `fontes_habilitadas` | Todas as fontes [H] da §3.2 | Subconjunto das fontes da §3.2 | Fontes que alimentam cada categoria. Habilitar uma fonte é nova versão de perfil, com marcador (§9.5) |

### 12.3 Presets

| Preset | Configuração | Uso |
|---|---|---|
| **Equilibrado** | Padrão | Ponto de partida |
| **Centrado no registro** | ω = REG 0,45 · EXP 0,20 · AME 0,10 · CTL 0,05 · GOV 0,20 | Organizações com registro maduro |
| **Exposição técnica (CTEM)** | ω = REG 0,20 · EXP 0,40 · AME 0,20 · CTL 0,10 · GOV 0,10 | Organizações com registro incipiente |
| **Conservador** | p = 8, τ = 0,90, τ_G = 0,85 (ω do Equilibrado) | "Quase o pior objeto" |
| **Média pura** | p = 1, τ = 0, τ_G = 0 | **Só na prévia**, para mostrar a diluição. Não pode ser ativado |

Um preset é escolhido no primeiro perfil (Estágio A1) ou num ciclo de rebaseline. Fora disso, trocar de
preset não é atalho: Centrado no registro move ω_REG em +0,15 e CTEM move ω_EXP em +0,10, acima do
limite de ±0,05 por trimestre.

### 12.4 Validação

Um perfil é recusado se violar qualquer uma destas regras:

- Σω = 1 (tolerância 10⁻⁶), cada ω em 0 ou na sua faixa, `ω_REG ≥ 0,20` e pelo menos 3 categorias
  com ω > 0.
- **Trava conjunta de cauda:** `τ · τ_G · L_topo ≥ L_2`, sobre as `faixas` do próprio perfil, com
  `L_topo` o limite inferior da faixa mais alta e `L_2` o da segunda faixa. Garante que um objeto que
  sozinho está na faixa mais alta nunca deixe a manchete na faixa mais baixa. Padrão:
  0,80 × 0,70 × 70 = 39,2 ≥ 31. As travas individuais antigas admitiam τ = 0,60 e τ_G = 0,50
  (0,30 × 70 = 21 < 31): com elas, um objeto em R = 1 leria 30, Baixo, a mesma falha do 26 do Vision
  One (§4.7). Com τ = 0,60, a trava exige τ_G ≥ 0,74. No preset "Registro" de faixas, exige
  `τ·τ_G ≥ 40/90 = 0,45`.
- Travas: `desconto_aceite = 0`, `piso_portao_a ≥ 70` e ligado.
- Multiplicadores ≥ 1; `teto_sem_exploracao < piso_exploracao`.
- Âncoras monótonas; âncoras SSC com s(100) = 0 e s(0) = 1.
- Faixas com o primeiro limite em 0, contíguas, crescentes e com rótulos em pt-BR e en-US
  preenchidos e únicos em cada idioma.
- W > 0; `janela_incidente ≥ 3 × meia_vida_incidente`; `janela_inventario_sinal ≥ 2W` da fonte mais
  lenta.
- Cotas de população somando 1 em cada categoria.
- Cópias do registro coerentes: `limites_registro` crescentes, `cadencia_revisao` crescente de Very
  High para Low.
- **Regras de ritmo**, salvo aprovação explícita de rebaseline:
  - **mudança de cada ω ≤ ±0,05 por trimestre**;
  - **no máximo dois parâmetros alterados por ciclo de calibração**;
  - **trocar de preset é sempre rebaseline**: exige aprovação explícita de rebaseline, ponte e
    relatório de sensibilidade;
  - **trocar de modo é sempre rebaseline**, como trocar de preset.
- **Como as regras de ritmo se contam:**
  - a **linha de base** é o perfil vigente no primeiro dia do trimestre civil corrente (UTC) ou, se
    nenhum perfil vigorava nesse dia, o primeiro perfil aprovado no trimestre, para que duas
    aprovações seguidas no mesmo trimestre não movam ω em ±0,10;
  - **um parâmetro** é uma chave da §12.2, exceto o vetor ω, que conta como **um** parâmetro porque
    Σω = 1 obriga a mover ao menos dois pesos; `frescor_W`, `fontes_habilitadas`, `faixas` e
    `cotas_populacao` contam como um cada;
  - a **aprovação de rebaseline libera as duas travas de ritmo** (±0,05 e dois parâmetros); sem isso um
    preset nunca poderia ser aprovado, porque o Conservador sozinho muda três parâmetros;
  - as regras de ritmo **não invalidam o rascunho**: marcam a submissão como "proposta de rebaseline",
    e só a aprovação exige a confirmação explícita;
  - só o primeiro perfil de todos (nenhum perfil aprovado antes) dispensa linha de base e regras de
    ritmo.
- Mudança de `faixas` exige `board_approval_ref`. O **primeiro perfil** também exige: suas faixas
  diferem do perfil vigente inexistente, e o Conselho/Reitoria as aprova "para comunicação" desde o
  início (§4.5).

### 12.5 Esquema (convenções do Track 6)

As tabelas abaixo vão no próximo par de scripts numerados (`Structure/88.sql` e `Data/88.sql`), com DDL
guardada e migração EF correspondente.

**`risk_index_profiles`**

| Coluna | Tipo | Observação |
|---|---|---|
| `id` | int PK | |
| `version` | int | `uq_risk_index_profiles_version` |
| `name` | varchar(120) | |
| `preset` | varchar(40) NULL | |
| `mode` | int | Interino 1 · Pleno 2 (enum com `HasConversion`). A troca de modo exige versão nova de perfil e é sempre rebaseline (§9.4, §12.4) |
| `status` | int | Draft 1 · PendingApproval 2 · Active 3 · Retired 4 · Rejected 5 (enum com `HasConversion`) |
| `parameters` | LONGTEXT | JSON validado |
| `justification` | TEXT | Obrigatório na submissão |
| `sensitivity_report` | LONGTEXT | Prévia anexada na submissão (§13.2), copiada da requisição de prévia; fora da auditoria campo a campo, porque é regenerável pelo hash dos parâmetros |
| `committee_decision_ref` | varchar(200) NULL | Obrigatório para ativar: ata ou data da decisão do Comitê de Risco de TI, com anexo |
| `board_approval_ref` | varchar(200) NULL | Obrigatório quando `faixas` difere do perfil vigente, inclusive no primeiro perfil |
| `is_rebaseline` | tinyint(1) | Aprovação explícita de rebaseline: troca de preset (ou de modo), ω acima de ±0,05 em relação à linha de base do trimestre, ou mais de dois parâmetros alterados no ciclo (§12.4) |
| `rejected_reason` | TEXT NULL | |
| `created_at`, `created_by_id` | datetime, int | FK `fk_risk_index_profiles_created_by_id` |
| `submitted_at` | datetime NULL | |
| `approved_at`, `approved_by_id` | datetime NULL, int NULL | `approved_by_id ≠ created_by_id`, sem bypass de administrador (§13.1) |
| `approval_grant_source` | varchar(32) NULL | Origem da permissão explícita do aprovador (`role:{id}` ou `direct`) |
| `effective_from` | datetime NULL | Primeiro snapshot que usa o perfil |
| `retired_at`, `retired_by_id` | datetime NULL, int NULL | FK `fk_risk_index_profiles_retired_by_id` |
| `retire_justification`, `retire_decision_ref` | TEXT NULL, varchar(200) NULL | Motivo e referência da aposentadoria (suspensão sem sucessor), sem sobrescrever a evidência da ativação |
| `revision` | int | Token de concorrência do rascunho, incrementado a cada alteração; fora da auditoria |
| `updated_at` | datetime NULL | Linhas Active e Retired são imutáveis, exceto a transição Active → Retired |

**`risk_index_snapshots`**

| Coluna | Tipo |
|---|---|
| `id` | bigint PK |
| `snapshot_date` | date |
| `profile_id` | FK `fk_risk_index_snapshots_profile_id` |
| `mode` | int (Interino 1, Pleno 2) |
| `scope_kind` | int (Organização 1, Entidade 2, ClasseDeAtivo 3, Fonte 4) |
| `scope_ref` | varchar(64) |
| `entity_id` | int NULL, filtro de escopo |
| `category` | int (0 = ICR, 1 REG, 2 EXP, 3 AME, 4 CTL, 5 GOV, 6 TER) |
| `value_unfloored` | double (precisão total; ICR\* ou `C_k`) |
| `value_floored` | double (precisão total; ICR com piso; igual ao anterior nas categorias) |
| `value_displayed` | int (arredondado de `value_floored` em precisão total, §4.5) |
| `floor_reasons` | TEXT, JSON |
| `coverage`, `freshness`, `quality` | decimal(4,3) |
| `interval_low`, `interval_high` | double NULL |
| `object_count` | int |
| `presumption_points` | double |
| `external_inputs_hash` | varchar(64) (§9.5) |
| `top_contributions`, `change_attribution` | LONGTEXT, JSON |
| `created_at` | datetime |

Chave única `uq_risk_index_snapshots_date_profile_scope_category`, sobre
(`snapshot_date`, `profile_id`, `scope_kind`, `scope_ref`, `category`).

**`risk_index_object_days`**

| Coluna | Tipo |
|---|---|
| `snapshot_date`, `profile_id` | date, FK |
| `object_kind` | int |
| `object_ref` | varchar(64) |
| `category` | int |
| `population` | int |
| `r_value` | double |
| `weight` | decimal(5,4) |
| `criticality` | tinyint |
| `criticality_source` | varchar(32) |
| `criticality_author_id` | int NULL |
| `state` | int |
| `entity_id` | int NULL, filtro de escopo |
| `attribution` | TEXT, JSON de ids de entidade |
| `factors` | TEXT, JSON: por fator, tipo, `x`, `v`, `u_f` e carimbo da leitura |
| `scenario_inputs` | TEXT NULL, JSON: nível de impacto, rank, `n`, `r_res`, `r_inh`, T, atraso, `cred`, `σ_ref` |
| `signals` | TEXT, JSON de até 50 sinais por fator |
| `created_at` | datetime |

Chave única `uq_risk_index_object_days_date_profile_object_category`.

**Também:**

- **Só `risk_index_profiles`** entra na allowlist do `GovernanceAuditInterceptor`
  (`src/DAL/Auditing/GovernanceAuditInterceptor.cs:48-61`), auditado campo a campo.
- `risk_index_snapshots` e `risk_index_object_days` são **só de inserção**: gravadas apenas pelo job,
  sem endpoint de alteração ou exclusão (um teste verifica que não existe), com expurgo só pelo job de
  retenção dedicado. Colocá-las na allowlist gravaria uma linha de auditoria para cada linha inserida
  ou expurgada (o interceptor registra todo `Added` e `Deleted` de tipo auditado,
  `GovernanceAuditInterceptor.cs:93-140`): dezenas de milhares por dia, por 1825 dias, soterrando a
  trilha de governança.
- Colunas novas em outras tabelas, conforme os pré-requisitos da §14: `hosts.criticality_source`,
  `hosts.last_assessed_at`, `incidents.severity`, `incidents.resolved_at` e
  `risk_scoring.quant_residual_ale_mean`.
- Propriedades EAV novas, sem DDL: `criticality` e `internetFacing`, editáveis só com a permissão da
  D-17.

---

## 13. Versionamento, aprovação, auditoria e calibração

### 13.1 Ciclo de vida e papéis

1. **Rascunho** (`risk_index_configure`): o autor, da **segunda linha**, com escopo irrestrito (a
   prévia traz valores de todos os nós, §10.4), edita parâmetros e roda a prévia.
2. **Em aprovação:** a submissão exige justificativa e prévia de sensibilidade anexada.
3. **Vigente** (`risk_index_approve`):
   - o aprovador precisa ser **diferente do autor**, **sem bypass de administrador**, como na
     segregação de funções do Track 8 (8.3.2);
   - o aprovador tem o papel **Gerente de Riscos** ou **Administrador de Riscos**, que representa o
     Comitê de Risco de TI no sistema (decisão do product owner, 2026-10-05; papéis semeados quando
     ausentes pela T293), e **não é dono de primeira linha de unidade pontuada**: o sistema recusa quem detém `business_risk_review` (revisor de negócio de campanha),
     para que ninguém aprove os pesos que medem a própria unidade;
   - **desde o primeiro perfil**, a ativação exige a referência da decisão do comitê (ata ou data, com
     anexo) em `committee_decision_ref`. Com o M47 (T197), a aprovação passa a ser colegiada dentro do
     sistema;
   - mudança de `faixas` exige `board_approval_ref`: o Conselho/Reitoria aprova as faixas "para
     comunicação";
   - o perfil vale a partir do **snapshot seguinte** e gera descontinuidade (§9.5).
4. **Aposentado** ou **Rejeitado.**

Toda transição e todo campo ficam em `audit_logs`. Cada snapshot carrega o id do perfil. A terceira
linha tem leitura de perfis e, com escopo irrestrito, de prévias, pontes e trilha de auditoria (§10.4;
M47, T198).

### 13.2 Prévia de sensibilidade (obrigatória para submeter)

O rascunho é reaplicado aos **últimos 90 dias** de linhas de objeto (§9.5) e ao dia corrente com
dados brutos. A prévia mostra:

- Δ por nó;
- nós que mudam de faixa;
- **τ de Kendall** do ranking das unidades entre o perfil vigente e o rascunho;
- objetos que mais se movem;
- **tornado** um parâmetro por vez, com ±20% em cada ω (com as demais reescaladas proporcionalmente
  para manter Σω = 1), p, τ, τ_G, ρ, β e nas cotas, mais cada preset (exemplo: §11.6);
- **peso efetivo do registro**: a fração da manchete sustentada pela severidade dos cenários somando
  REG, GOV (população cenários) e CTL.P, pela alocação de Euler, e a linha de tornado "σ do cenário
  dominante ±20%". GOV e CTL.P são escalados por `σ_ref`, então o mesmo cenário pode sustentar três
  categorias, e o comitê precisa ver isso para não subestimar o peso real do registro.

**Primeiro perfil (piloto).** Sem perfil vigente nem linhas de objeto, a prévia cobre só o dia
corrente com dados brutos: valores e faixas por nó, tornado, presets e peso efetivo do registro, sem Δ
contra o vigente, sem τ de Kendall e sem sobreposição de 90 dias.

### 13.3 Calibração

1. **Piloto em sombra de 8–12 semanas**, como na calibração da Fase 4 da MIGR-TI/IA.
   - Todo snapshot carrega o id de um perfil aprovado (§9.1), então o piloto começa com um **perfil de
     piloto** aprovado pelo fluxo da §13.1, com referência do comitê. Ao fim da calibração, o comitê
     aprova o **perfil de publicação**, que pode ser o mesmo ou uma versão nova (rebaseline, se a
     calibração pedir); só então o índice é publicado aos usuários com escopo (Estágio A2).
   - Durante a sombra, o painel e a API de leitura são visíveis só aos membros do comitê (detentores
     de `risk_index_approve`) e aos administradores. O **autor** (segunda linha, com escopo
     irrestrito) vê, além disso, a prévia e a ponte **do seu próprio rascunho** (§13.2), que é a
     ferramenta de calibração; não vê o painel.
   - O índice é confrontado semanalmente com o julgamento do comitê ("o que mudou" é plausível?).
2. **Backtesting.** Modo pleno: M47 T196. Interino: por entidade, porque incidentes não têm vínculo
   com ativo.
   - Para cada nó e mês, compara o índice em t com os incidentes em (t, t+90 d] cuja entidade
     impactada está no nó.
   - **Circularidade.** AME usa incidentes abertos e recentes, e incidentes se agrupam (mesma campanha,
     mesmo caso). Por isso o backtest usa **ICR\*₋AME**, a manchete recalculada sem AME e
     renormalizada, e exclui os incidentes ligados a incidentes já abertos em t (mesma entidade e
     categoria dentro da janela). As duas versões, com e sem AME, são publicadas; **as metas valem
     para a versão sem AME**.
   - **Metas de aceitação:**
     - correlação de Spearman ≥ 0,3;
     - taxa de incidentes na faixa Alto ≥ 2 × a da faixa Baixo;
     - falsos negativos ≤ 20% (incidente significativo em nó Baixo com qualidade ≥ 0,5 em t);
   - Com menos de 10 incidentes no período, a análise é **só qualitativa**.
   - Quase-incidentes (M40, T153) entram depois.
   - Um incidente registrado depois do cenário que ele "confirmaria" nunca conta como previsto.
3. **Estabilidade.** No máximo 10% dos escopos podem mudar de faixa por semana sem mudança material
   nos dados.
4. **Comparação com fornecedores.** Correlação com o CRI do Vision One, os `assetGroups` e a série da
   SSC. É verificação de sanidade, **não meta**.
5. **Governança da calibração:**
   - revisão **trimestral** do perfil, junto com o ciclo de portfólio e corte;
   - eliciação dos pesos de categoria por **alocação de orçamento** entre os membros do comitê
     (OECD/JRC);
   - no máximo **dois parâmetros por ciclo**;
   - assurance **anual** pela terceira linha (M47 T198).

---

## 14. Pré-requisitos e defeitos

### 14.1 Bloqueiam o modo interino (Estágio A0)

**Ordem obrigatória:** a D-03 entra **antes** da correção de escopo da D-01, ou na mesma mudança. Hoje
os dois passes que sobrescrevem riscos quantitativos leem zero riscos por causa do `DenyAll`; corrigir
a D-01 primeiro ativaria a sobrescrita no ciclo seguinte, sem histórico que permita rastreá-la.

| # | Defeito (evidência) | Efeito no ICR | Correção exigida |
|---|---|---|---|
| D-01 | **Confirmado:** todo job do Hangfire roda com escopo `DenyAll`. `ConfigurationManager.cs:37` registra o `BackgroundServiceHttpContextAccessor`, cujo principal é autenticado e só tem as claims `Sid` e `Name` (`src/ServerServices/Security/BackgroundServiceHttpContextAccessor.cs:27-37`); `DALService.GetCurrentEntityScope` (`src/ServerServices/Services/DALService.cs:172-188`) devolve então `ForEntities([])`, que é `DenyAll` (`src/DAL/Context/EntityScope.cs:31-35`), e a guarda de escrita lança `EntityScopeViolationException` (`src/DAL/Context/AuditableContext.cs:130-152`). Nenhum código de produção passa `bypassEntityScope: true`, exceto autenticação e SCIM. Além disso, cinco jobs do Track 8 estão agendados (`JobsManager.cs:45-63`) e não registrados em DI | Risco, host, incidente, vulnerabilidade, avaliação, apetite e campanha são escopados, então: as passadas de residual, expiração, cadência e campanha leem zero riscos mesmo depois de registradas; o residual clássico, gravado só por `CalculateResidualRiskAsync` (`RiskCalculationService.cs:181`, chamado só por `ResidualRiskCalculation.cs:31`), fica nulo para todo risco clássico; as sincronizações agendadas do Vision One e da SSC leem zero hosts e falham ao inserir; um snapshot que só abrisse *bypass* para si receberia `DenyAll` dentro de cada serviço reutilizado | Dar ao principal de background escopo irrestrito (claim `scope=global` no acessor, que `GetCurrentEntityScope` já reconhece), não só *bypass* no snapshot; registrar os cinco jobs; teste que roda o `DalService` real com esse acessor e exige que todo job registrado veja linhas semeadas; teste que resolve todos os jobs recorrentes. Até a correção: V1-DEV, V1-CVE e o pseudo-host SSC só por sincronização manual de administrador, e `r_eff = r_inh` em todo cenário clássico |
| D-02 | Faixas aplicadas de três formas: `>` (`MgmtReviewsService.cs:416-434`), `>=` (`RiskAcceptancesService.cs:386-393`), fixas (`StatisticsService.cs:146-148`, `MasterDashboardService.cs:187-189`). "Very High" = 10,1 é inalcançável (`src/ConsoleClient/DB/Data/1.sql:291`) | A cadência `T` diverge das notificações; o score 0 fica sem faixa | Uma função `Faixa` com `≥`, em módulo neutro (`Tools`), e Very High alcançável (proposta: 9,0, decisão de Fase 0) |
| D-03 | A passada de residual (`src/BackgroundJobs/Jobs/Governance/ResidualRiskCalculation.cs:29-31` → `RiskCalculationService.CalculateResidualRiskAsync`, `src/ServerServices/Services/RiskCalculationService.cs:151-200`) e o job de matriz a cada 2 h (`RiskCalculationService.cs:33-69`, `JobsManager.cs:192`) iteram `context.Risks` sem filtro de `ScoringMethod` | Hoje latente (D-01: os dois leem zero riscos). **Sobrescreverão** `CalculatedRisk` e `ResidualRisk` de todo risco `ScoringMethod = 3` assim que a D-01 for corrigida, sem gravar histórico | Pular os quantitativos nos dois passes, **antes ou junto da D-01**, com teste de que um risco `ScoringMethod = 3` mantém seus valores depois dos dois jobs rodarem com escopo irrestrito. Até lá, esses cenários aparecem como "valor não confiável" e contam como tratamento não avaliado |
| D-04 | Dois predicados de "aberto" em achados: `ClosedStatuses` (`src/Model/Status/ClosedStatuses.cs:18-30`) × `LifecycleStatus` (`src/DAL/Enums/FindingStatus.cs:60-61`), com **dois escritores**. O desktop fecha e rejeita achados gravando só o `Status` legado: `VulnerabilitiesViewModel.cs:1099` (Rejected) e `:1322` (status final do diálogo de fechamento) → `PUT /Vulnerabilities/{id}/WorkflowStatus` (`VulnerabilitiesRestService.cs:470`) → `VulnerabilitiesService.UpdateStatus` (`VulnerabilitiesService.cs:269-279`), que altera só `vulnerability.Status`; o controller declara que os dois endpoints gravam colunas diferentes (`VulnerabilitiesController.cs:455-475`). O backfill de `Structure/77.sql:178-182` não mapeou Retired 27, Deleted 37, Completed 47 e Cancelled 49, que ficaram Active. Avaliações gravam severidade 0–10 (`src/GUIClient/ViewModels/Assessments/AssessmentRunViewerViewModel.cs:434,440`) | Achados que analistas fecharam ou rejeitaram contariam em EXP.V e no SLA; severidades corrompidas | (a) Rotear as gravações de fechamento e rejeição do `WorkflowStatus` pelo `FindingLifecycleService` (Mitigated, FalsePositive, OutOfScope); (b) backfill único de `status_id` para linhas com `Status` legado em `ClosedStatuses` e `status_id = 1`; (c) teste de regressão de que um fechamento no desktop tira o achado de EXP e do SLA. Até lá, o predicado interino da §3.3 e a métrica de discordância entre as colunas. Exclusão de `import_source = 'assessment'` |
| D-05 | Dois vínculos risco↔entidade: `risks.entity_id` (`src/DAL/Entities/Risk.cs:74`, criado em `Structure/74.sql:11` sem backfill) × `risk_to_entity` (`src/DAL/Context/NRDbContext.cs:2852`) | Pertencimento de cenários; campanhas e apetite usam um, a GUI grava o outro | União deduplicada (§10.2) e backfill de `risks.entity_id` |
| D-17 | Mapa de entidades: CRUD só com `RequireValidUser` (`EntitiesController.cs:11,82-83`), delete em cascata, `Entity` e suas propriedades fora da auditoria (`GovernanceAuditInterceptor.cs:48-61`); nenhum serviço percorre a árvore; processos e aplicações só se ligam a unidades por propriedades EAV; comentário falso em `TrendMicroService.cs:636-638` | `criticality`, `internetFacing` e `securityClassification` movem pesos e valores, e a herança de criticidade é integral: qualquer usuário válido moveria o índice sem aprovação, sem trilha e sem marcador (contradiz I8). Fecho incompleto | **Permissão dedicada** para editar `criticality`, `internetFacing` e `securityClassification`; `Entity` e as propriedades na allowlist de auditoria; origem e autor da criticidade por objeto (§5.1); mudanças exibidas como efeito de contexto em "o que mudou". Fecho no serviço do ICR no interino; tabela de fecho com os vínculos EAV materializados como relações tipadas depois; teste de regressão do caso 26 |
| F-1 | Sem tabelas de perfil, snapshot e objeto; sem permissões; sem allowlist de auditoria do perfil | Sem tendência nem rastreabilidade | §12.5 e §10.4 |
| F-2 | Sem criticidade em processo, aplicação e unidade (`src/API/EntitiesConfiguration.yaml:346`), nem `internetFacing` em aplicação | Pesos, herança e `m_net` de aplicação impossíveis | Propriedades EAV `criticality` e `internetFacing` na mesma versão do YAML, sob a permissão da D-17 |
| F-3 | Escopo plano: uma claim numa unidade não concede as subunidades (`DALService.cs:172-188`; `EntityScope.cs:31-51`), e nenhum teste de escopo tem caso pai/filho | O gestor de uma unidade veria quase todo nó como "parcial" (§10.4) | Escopo hierárquico por descendentes na árvore, para leitura e escrita, com cache do fecho invalidado por mudança na árvore e testes de regressão por tipo escopado (T292; decisão do product owner, 2026-10-05) |

### 14.2 Degradam, com contorno documentado

| # | Defeito (evidência) | Contorno no ICR | Correção |
|---|---|---|---|
| D-06 | Constantes `Review`/`NextStep` contradizem o seed (`RiskAcceptancesService.cs:39,42`; `RiskReviewCampaignsService.cs:43-48`; `RiskWorkflowService.cs:47` × `Data/1.sql:164-167,225-227`) | Os códigos não são lidos (§6.7); uma revisão qualificada conta mesmo com desfecho desconhecido | Alinhar constantes e testes |
| D-07 | A revisão desktop contorna `CreateReviewAsync`: `EditMgmtReviewViewModel.cs:182-197` → `MgmtReviewsController.cs:52` → `MgmtReviewsService.Create`. **`POST /MgmtReviews` grava o `SubmissionDate` que o cliente envia**, sem carimbo no servidor (`MgmtReviewsController.cs:35-52`, `MgmtReviewsService.cs:141-153`); o desktop o envia em hora local (`EditMgmtReviewViewModel.cs:224`); a segregação de funções não é aplicada; `ReviewRequested` nunca é limpo | Data efetiva limitada pela criação em `audit_logs` e pelo snapshot; autoavaliações não restauram crédito (§6.1); REV-REQ por comparação de datas | Rotear por `CreateReviewAsync`; carimbar `SubmissionDate` em UTC no servidor, com teste de regressão de que uma data futura enviada pelo cliente é ignorada |
| D-08 | `GetOverdueReviewsAsync` ignora `NextReview` e não é exposto (`MgmtReviewsService.cs:342`) | O ICR calcula `due` por conta própria, honrando `ExpiresAt` e `RevokedAt` da última aceitação | Alinhar o serviço à §6.1 e expor |
| D-09 | `entities.cyber_risk_index` com última escrita vencendo (`TrendMicroService.cs:700`, `SecurityScorecardService.cs:363`) | Nunca lida | Tabela de escore por fonte com histórico; coluna rebaixada |
| D-10 | `hosts.criticality` com três escritores e sem origem; o Vision One sobrescreve (`TrendMicroService.cs:559,675`) e pode gravar 5 pela palavra "critical" ou por número acima de 80 (`TrendMicroClient.cs:571-592`) | Precedência indisponível; criticidade marcada "origem desconhecida"; origem Vision One limitada a 4 | Coluna `hosts.criticality_source` e precedência §5.1 |
| D-11 | Vision One: `lastSeen` descartado e lido de candidatos errados (`lastSeenDateTime`, `lastUsedIp`, `lastActivity`, `TrendMicroClient.cs:362`), que não existem no OAS de `attackSurfaceDevices`; exploit mistura tentativas e atividade global (`:517-526`); EPSS e virtual patch em `ToolFields` não persistidos (`TrendMicroService.cs:772-783`); `hosts.risk_score` nunca é limpo | Validade por `risk_score_updated_at`; `m_x` 1,15 para o flag do V1 | Persistir `lastDetectDateTime` e `firstSeenDateTime` (os campos do OAS), com teste de fixture montado no formato do OAS; persistir `exploitAttemptCount`, `globalExploitActivityLevel` e EPSS (T162) |
| D-12 | SSC: fator ausente gravado como 0 (`SecurityScorecardClient.cs:111`); endpoints de issues e `issue_count` não verificados contra payload real | Só a linha `is_overall` | Nulo em vez de 0; validar contra um tenant |
| D-13 | Sincronização efetiva em dias alternados: o vencimento conta do fim da execução (`TrendMicroService.cs:282-286`) | W = 7 d absorve; a série terá lacunas | Corrigir o cálculo de "devido" |
| D-14 | Host do achado resolvido por IP primeiro (`src/ServerServices/Importers/FindingIngestionService.cs:530-532`) | CVE pode cair no host errado | Id externo primeiro |
| D-15 | Incidentes sem severidade, sem `ResolvedAt`, em hora local (`IncidentsService.cs:64-66`), fora da auditoria por campo | Âncora por categoria; data de encerramento pelo primeiro snapshot que viu o incidente fechado, congelada (§9.2) | `incidents.severity`, `resolved_at` em UTC, auditoria |
| D-16 | Avaliações: junção resposta↔opção por texto; `RunDate` sobrescrito a cada atualização (`AssessmentsService.cs:164`); sem `SubmittedAt` | CTL.A "experimental" | FK de resposta, `SubmittedAt`, score no servidor |
| D-18 | `hosts.last_verification_date` é carimbado pelo Jira Assets (`JiraIntegrationService.Assets.cs:361,583`), pelo inventário do Vision One (`TrendMicroService.cs:561`), pelo pseudo-host da SSC (`SecurityScorecardService.cs:481`) e por toda ingestão de achados (`FindingIngestionService.cs:538,562`) | Regra interina da §8.2: `last_detection` de importadores de varredura e código; o campo só para host sem `external_provider` e sem vínculo CMDB | Coluna `hosts.last_assessed_at` gravada só por varredura |
| D-19 | Nenhuma linha de apetite semeada (`RiskAppetitesController.cs:55` devolve 204) | Selo "teto de apetite não configurado"; APT-BRK e ACC-APT inativos; a parte de apetite sai da cobertura de GOV | Configuração na Fase 0 |
| D-20 | Atribuição host→entidade: `hosts.entity_id` só é gravado na criação, a partir da conexão ou de quem importa (`FindingIngestionService.cs:562-567`, `TrendMicroService.cs:534`, `JiraIntegrationService.Assets.cs:293-299`); hosts casados nunca são recarimbados; importação de administrador grava nulo; não há campo no `EditHostDialogViewModel` nem filtro `entityId` (`src/API/ApplicationEntityFilterMapperProvider.cs:54-71`) | Fração "Não atribuído" por peso publicada em `ctx`; aviso no drill-down por unidade acima de `limiar_nao_atribuido`. A publicação por unidade no Estágio A2 exige o aviso visível | Caminho de atribuição (campo na API e na GUI, filtro), política explícita de recarimbo por conexão (Vision One e Jira Assets com unidade-alvo) e relatório de backfill |
| D-21 | Achados sem host: `vulnerabilities.entity_id` vem do escopo de quem importa (um escopo único dá a entidade; um chamador irrestrito ou com várias entidades dá nulo; `VulnerabilitiesController.Aspm.cs:430-446`, `FindingIngestionService.cs:866`); a chave de dedup não distingue repositórios | Agrupamento sob "Não atribuído" por (importador, repositório ou projeto, última importação), com fração publicada (§3.4) | Parâmetro de aplicação-alvo na importação, validado contra o escopo de quem importa e persistido em `scan_imports` e nos achados; discriminador de repositório ou projeto na chave de dedup |
| D-22 | Insumos do produto fora da auditoria: `review_levels`, `risk_levels`, `settings` (`next_review_date_uses`, `quantitative_band_thresholds`, `risk_workflow_residual_strategy`) e `sla_configurations` não estão na allowlist (`GovernanceAuditInterceptor.cs:48-61`) | Cópias aprovadas no perfil para cadência, limites e base de cadência; hash dos demais em cada snapshot, com marcador e efeito em `A_cfg` (§9.5) | Incluir esses tipos na allowlist |

---

## 15. Modo interino e modo pleno

### 15.1 O que cada modo calcula

| Categoria | Modo interino ("ICR-P") | Modo pleno |
|---|---|---|
| REG | Residual com crédito de revisão qualificada; rank de campanha | Idem, mais flags (PA-1) e cadeia do M39 |
| EXP | V1-DEV, SSC-TOT, achados de todos os importadores (VPR do `.nessus`), `m_x` por exploit/CVSS, piso de exploração por `ExploitedByScanner` | Mais Tenable VM (AES, ACR, VPR v2, EPSS), objetos Vision One (FQDN/IP, contas, nuvem, apps), priorização T165, KEV, herança de criticidade |
| AME | Incidentes por âncora de categoria | Severidade, `ResolvedAt`, quase-incidentes (M40) |
| CTL | Avaliações (experimental), IRP | Mais configuração dos agentes Vision One |
| GOV | Completo (§6.3) | Mais o estado do gatilho do M46 nos cenários |
| TER | — | Portfólios SSC, concentração uma vez por processo crítico, HECVAT (M48) |
| Pisos | Nenhum; chips "evidência para avaliação do Portão A" | PA-1 (M43) |
| Drill-down | Unidade (fecho com vínculos EAV), fonte, criticidade, ambiente, classe heurística; processo e atividade com REG, GOV, AME e CTL; EXP parcial no processo (achados sem host das aplicações) e indisponível na atividade | Processo, atividade, serviço, dado e ativo pela cadeia do M39 |
| Ao lado | Σ E[L] inerente dos quantificados, com "n de N" (residual requer coluna nova); contagens; indicadores de campanha e de SLA; "não disponível" para P95, Portão B, flags e confiança; **Top Risks: não disponível até o M43** (nunca aproximados) | P95/CVaR (M45), Portão B e KRIs com tolerância (M46), flags e Top Risks (M43), confiança da evidência (M40), nível-alvo (M44) |

### 15.2 Estágios de adoção

| Estágio | Conteúdo | Saída | Marcos (Track 10) |
|---|---|---|---|
| **A0 — Fundação** | D-03 antes ou junto da D-01; D-02, D-04, D-05, D-17, F-1, F-2 (com `internetFacing` e o tipo `activity`), F-3 (escopo hierárquico) | Dados e motor prontos para o primeiro snapshot (que só roda com o perfil de piloto aprovado, A1) | M52 (T236–T242); T243 |
| **A1 — Interino em sombra** (8–12 semanas) | Perfil de piloto aprovado antes do primeiro snapshot (escolha do preset); todas as fontes [H]; calibração §13.3 | Perfil de publicação aprovado ao fim da calibração, com referência da decisão do comitê | M53; T251–T253 (M54) e T259–T262 (M55); T258 |
| **A2 — Interino publicado** | Painel para a equipe de riscos (analistas, gerentes e administradores de riscos; outros públicos depois, por concessão), rótulos "parcial" e "interino"; aviso de "Não atribuído" no drill-down por unidade (D-20) | ICR-P | M54, M55 |
| **A3 — Enriquecimento de fontes**, cada uma como nova versão de perfil com marcador | Conector Tenable VM; Vision One `securityPosture`, `assetGroups` (indicador), contas, internet-facing, nuvem e apps; Workbench como indicador; backfill do histórico SSC; tabela de escore por fonte; `quant_residual_ale_mean`; atributo internet-facing e relação app↔servidor do Jira Assets (T291); D-06 a D-16, D-18, D-20, D-21, D-22 | Cobertura sobe; descontinuidades anotadas | M56, M57, M58 |
| **A4 — Pleno** | Consome M39 (vínculos, herança, `X_r`), M40, M41 (criticidade de processo), M42 (KEV/EPSS), M43 (PA-1, Top Risks), M44, M45, M46 (KRIs, gatilhos, Portão B), M47, M48, M49. Nenhuma previsão de tendência por IA antes do M50 | Modo pleno, com descontinuidade marcada | M59 |

---

## 16. Relação com o roadmap

| Track / marco | Papel no ICR |
|---|---|
| Track 2, M8 (T37/T38; S6 §2.3.3) | O ICR entrega a tendência e o drill-down pedidos e nunca entregues no Master Dashboard, que é retirado depois da publicação do modo interino (T294) |
| Track 3, M10–M13 | Fonte de EXP.V, do predicado "aberto", da chave de dedup e do SLA (GOV.T). D-04 e D-21 são correções desses marcos |
| Track 4, M18, M19, M20 | Vision One, SSC e Jira Assets já integrados. O "CRI de entidade" (T74) é redefinido como escore por fonte e indicador. O conector Tenable é o **M56** (hoje não há `IntegrationKind`) e a expansão do Vision One é o **M57**. D-20 é correção desses marcos |
| Track 6 | Convenções das tabelas novas e dos scripts numerados |
| Track 7 | Permissões novas, inventário de autorização (`ControllerAuthorizationInventoryTest`), escopo irrestrito do principal de background (D-01) e permissão dos insumos de contexto (D-17) |
| Track 8, M32–M38 | Aceitações, residual, apetite e segregação de funções, auditoria, cadência, campanhas, âncoras e FAIR-lite (Σ E[L]). D-01, D-02, D-03, D-06, D-07, D-08 e D-22 são correções desses marcos |
| Track 9, M39 | Cadeia de ligação: drill-down por processo, atividade, serviço, dado e ativo; herança de criticidade; `X_r` da divergência |
| M40 | Confiança da evidência ao lado; quase-incidentes |
| M41 | Criticidade de processo pelo BIA, consumida como entregue (substitui a interina). Se a especificação do M41 não trouxer mapeamento de MTPD, a proposta da §5.2 vai para ela |
| M42 | EPSS em coluna, KEV, priorização T165, piso de exploração do sinal. O ICR não faz sincronização própria de KEV/EPSS; consome a do M42. O M42 não produz predicado de Portão A |
| M43 | Predicado do Portão A (PA-1), flags ao lado, Top Risks (T172) hospedado. Recebe os chips "exploração ativa sem cenário vinculado" para julgamento |
| M44 | Nível-alvo (T180) como linha de referência; benefício líquido ao lado da sensibilidade do indicador |
| M45 | P95/CVaR de portfólio no painel monetário e como selo de Portão B. **Nenhum Monte Carlo local** |
| M46 | CRI do Vision One, CES, rating SSC, intensidade de ataque e higiene de campanha como KRIs com tolerância e obsolescência; gatilho "novos dados" pelo predicado compartilhado de divergência (§6.3). **O ICR nunca é registrado como KRI** |
| M47 | Backtesting (T196), comitê aprovador (T197), leitura da terceira linha (T198) |
| M48 | Categoria TER |
| M49 | Sensibilidade do dado no Δ_dados; flags 2 e 5 |
| M50 | Libera previsão de tendência assistida por IA |

**IDs no roadmap:** **S39** é esta metodologia; **S40** é a especificação do painel
([risk-overview-dashboard.md](../features/risk-overview-dashboard.md)). O trabalho é o **Track 10** do
[ROADMAP.md](../../ROADMAP.md), com oito marcos:

| Marco | Conteúdo | Estágio |
|---|---|---|
| M52 | Fundação: D-01, D-02, D-03, D-04, D-05, D-17, F-2 (T236–T242) e F-3, escopo hierárquico (T292) | A0 |
| M53 | Motor, perfis versionados e snapshots diários; API de leitura e de ciclo de vida do perfil (T243–T250); papéis aprovadores (T293) | A0/A1 |
| M54 | Painel "Visão Geral de Risco Cibernético" no desktop, piloto em sombra (T251–T258) e retirada do Master Dashboard (T294) | A1/A2 |
| M55 | Telas de configuração: perfis, prévia, aprovação, fontes, mapeamentos, contexto de entidades (T259–T264) | A1/A2 |
| M56 | Conector Tenable Vulnerability Management com Tenable One (T265–T268); Security Center no backlog (T269) | A3 |
| M57 | Expansão de postura Vision One e SecurityScorecard; D-09 a D-13 (T270–T276) | A3 |
| M58 | Qualidade de dados de governança e ativos: D-06 a D-08, D-14 a D-16, D-18, D-20 a D-22 (T277–T283), e a média residual de ALE `quant_residual_ale_mean` para o Σ E[L] residual (T290), e o atributo internet-facing e a relação app↔servidor do Jira Assets (T291) | A3 |
| M59 | Modo pleno: consumo do Track 9 (T284–T288) | A4 |

A página executiva somente leitura no RiskPortal (T289), o cliente Tenable Security Center (T269) e a
edição do contexto de risco por gestores de unidade (T295) ficam no backlog.

---

## 17. Decisões deliberadas e alternativas rejeitadas

| Decisão | Alternativa rejeitada | Por quê |
|---|---|---|
| Piso duro `max(M_p, τ·max)` na categoria e na manchete | Mistura convexa `τ·max + (1 − τ)·M` na categoria (τ = 0,80) e na manchete (τ_G = 0,30) | A garantia de cauda na manchete cairia para `τ·τ_G = 0,24·R`. O servidor canônico a 90 numa organização sem outro risco daria 42,0 (categoria 0,8 × 90 + 0,2 × 52,0 = 82,4; manchete 0,3 × 82,4 + 0,7 × 0,3 × 82,4), contra 50,4 aqui |
| Trava conjunta `τ·τ_G·L_topo ≥ L_2`, ligada às faixas do perfil | Travas independentes τ ≥ 0,60 e τ_G ≥ 0,50 | Admitiam uma garantia de 30·R: um objeto em R = 1 leria 30, Baixo |
| Duas camadas de piso (categoria, manchete), com os fatores combinados dentro do objeto | Três pisos aninhados (fator, categoria, manchete) | A garantia cairia para 0,448·R e a atribuição ficaria mais difícil |
| Teto de apetite como sinal de GOV e selo | Teto como piso de exibição | Um único cenário levaria a organização inteira a Alto, e o piso fica reservado ao Portão A |
| Teto de apetite fora da normalização | Teto como âncora da escala de REG | Aumentar o teto baixaria o índice (manipulável), e unidades com tetos diferentes ficariam incomparáveis |
| Teto ordinal chamado de aproximação do Portão B | Chamá-lo de Portão B | O Portão B é sobre E[L], P95/CVaR ou KRI (PDF p.11); o nome faria crer que ele já é avaliado no interino |
| Piso só do predicado do Portão A do M43 sobre um cenário; chips no interino | Aproximar o Portão A por categoria de incidente, ou por achado KEV/exploração + internet-facing + criticidade (o antigo PA-2) | Inventaria resultados de portão que a metodologia reserva ao julgamento do cenário; CVE sem cenário não é risco autônomo (PDF p.8–9) |
| Aceite nunca desconta e nunca restaura crédito (travado) | `desconto_aceite > 0`; mover achados aceitos para fora de EXP; contar o `StartDate` do aceite ou a decisão de campanha como revisão | Aceite não é mitigação nem revisão; contar o aceite como revisão faria aceitar um cenário vencido baixar REG, o "aceitar para ficar verde" |
| GOV e CTL.P escalados por `σ_ref` (residual declarado) | Escalar por `σ_r`, com crédito | A revisão atrasada entraria três vezes (REG, GOV, CTL), e a divergência ligaria e desligaria com o crédito |
| Pertença por ciclo de vida (GOV com todo o parque; CTL.P com todos os cenários e rampa) | Pertença dependente do estado (hosts com achado aberto; cenários com σ ≥ 0,70) | Corrigir o último achado de um host subiria GOV, e o índice teria degrau em σ = 0,70 |
| Cotas por população em GOV e CTL | Uma população só | Milhares de hosts afogariam a governança dos cenários no ramo da média |
| Alertas do Workbench como indicador (KRI no M46) | Fator pontuado | Alerta bruto sem triagem não é risco (Fase 2) |
| SLA uma vez, em GOV.T | Multiplicador de SLA no sinal do achado e também um sinal de SLA | Contaria o mesmo atraso duas vezes |
| Obsolescência desliza para cima até `π·m(crit)`, e a leitura ruim antiga permanece | Manter o objeto limpo e antigo em R = 0, ou descartar fatores com `v = 0` | Limpo e velho leria como seguro, e o ruim e velho sumiria (contradiz I5) |
| Inclusão conservadora por objeto (prefixos) | Presumidos direto na média; ou o conjunto inteiro, tudo ou nada | Presumidos a 0,5 podem diluir (54,53 → 53,91); o tudo ou nada descartaria um presumido alto junto com muitos baixos |
| Exploração conhecida ou observada com piso no sinal, acima de todo achado sem exploração | `m_x` multiplicativo para exploração ativa e piso só para KEV | CVSS alto com exploit disponível passaria do KEV, e a exploração observada pelo scanner valeria menos que o KEV |
| Confiança da evidência (M40) ao lado | Dentro da confiança ou da qualidade | "Independente da pontuação quantitativa" (PDF p.15) |
| Roll-up por conjunto, pertencimento integral | Média ou soma dos filhos; rateio fracionário | Dupla contagem e soma de ordinais (vedação 8). O índice é intensivo |
| Um perfil global | Pesos por entidade | Quebra a reconciliação e a comparabilidade |
| Cópias aprovadas da cadência e dos limites do registro no perfil | Ler `review_levels` e `risk_levels` vivos | Uma edição de cadência mudaria o índice da noite para o dia, sem versão nem marcador |
| Faixas 0–30 / 31–69 / 70–100, como categorias de comunicação | 40 / 70 / 90 como padrão; faixas como tolerância | Alinha ao Vision One e ao Defender; 40/70/90 fica como preset. Faixa como tolerância faria do ICR um KRI |
| O ICR nunca é KRI | Registrar o ICR com tolerância no M46 | Viraria o "outro KRI" do Portão B: o score mágico |
| Sensibilidade do indicador, sem ordenar trabalho | Ranking de "impacto de remediação" | Ordenar tratamento por índice ordinal contradiz os Portões C e D (PDF p.10–11) |
| `m(crit)` de 0,40 a 1,00 | 0,2 a 1,0 | A criticidade entra no valor e no peso; a escala estreita evita punir duas vezes o padrão 3 |
| CRI do fornecedor como indicador | CRI como entrada composta | Contaria os scores de dispositivo duas vezes |
| Âncoras fixas | Min-max ou percentil da população | O valor de um objeto mudaria quando outros mudassem |
| MRA (ρ = 0,30, K = 10) | Noisy-OR puro; `ContributingScore` | O noisy-OR satura com a contagem; o `ContributingScore` cresce com ela e conta o pior duas vezes |
| `ω_REG ≥ 0,20` | `ω_REG ≥ max ω_k` | Rígido demais para o preset CTEM; o piso basta para o registro nunca ser marginal |
| Aumann–Shapley por trechos para variações | Diferença de contribuições de Euler | A diferença culpa objetos inalterados quando o dominante troca (Exemplo C) |
| Backtest sem AME | Backtest do ICR completo | AME usa os próprios incidentes; a correlação viria de autocorrelação |
| Retenção de 400 d (objetos) e 1825 d (nós) | 35 dias, só na organização | Não permitiria replay de 90 dias, ponte de rebaseline nem comparação ano a ano |
| Snapshots e linhas de objeto só de inserção, fora da allowlist de auditoria | Auditar as três tabelas | Dezenas de milhares de linhas de auditoria por dia soterrariam a trilha de governança |
| Snapshot diário oficial | Recalcular ao vivo como oficial | Séries reprodutíveis e auditáveis; o ao vivo é prévia |
| Σ E[L] residual ao lado, com "n de N"; P95 só do M45 | Monte Carlo local no painel; total sem cobertura | Duplicaria o M45; percentis não somam; um total sem cobertura leria cenário não quantificado como zero |
| Não ler `MgmtReview.Review/NextStep` | Usar os desfechos | Contradizem o seed; usá-los mediria o defeito |
| Sem piso hierárquico | Pai ≥ maior filho | O valor da organização dependeria da dimensão de drill-down |
| Reatribuição como efeito de reestruturação (`A_estr`), com marcador no nó | Tratar a entrada ou saída por reatribuição como efeito de risco | Uma edição administrativa do mapa apareceria como piora ou melhora de risco |
| Ritmo contado contra a linha de base do trimestre; vetor ω conta como um parâmetro; rebaseline libera as duas travas | Comparar com o perfil imediatamente anterior | Duas aprovações seguidas no mesmo trimestre moveriam ω em ±0,10 sem rebaseline |
| Perfil de piloto aprovado antes do piloto em sombra; painel só para comitê e administradores; o autor vê só a prévia e a ponte do próprio rascunho | Piloto sem perfil aprovado, ou com o painel aberto | Todo snapshot carrega um perfil aprovado (§9.1); publicar antes da calibração exporia um número não calibrado |
| Troca de modo só com versão nova de perfil, sempre como rebaseline | Trocar o modo dentro da mesma versão | O salto entre modos precisa de ponte e marcador (§9.5) |
| Atividade como filha de processo pela árvore, com REG, GOV, AME e CTL no interino | Esperar o M39 | `risk_to_entity` e `impacted_entity_id` já aceitam qualquer tipo; não exige DDL |

---

## 18. Glossário

| Termo | Definição |
|---|---|
| **ICR / ICR\*** | Índice exibido / índice sem piso (§4.4) |
| **Objeto** | Menor coisa que recebe sinais e criticidade: cenário, host, domínio, aplicação, entidade-alvo, tenant, fornecedor (§2.1) |
| **Nó / escopo** | Conjunto de objetos definido pelo fecho de um nó ou por um filtro (§10) |
| **Sinal `s`** | Leitura normalizada em [0,1], maior = pior (§3.1) |
| **Fator** | Agrupamento de sinais de mesma natureza dentro de uma categoria (§2.3). Tipos V, C e S |
| **`u_f`** | Teto do fator |
| **`R_{o,k}`** | Risco do objeto o na categoria k, em [0,1] |
| **`w_o`, `w'_o`** | Peso do objeto (`m(crit)` para técnicos, `m(impacto)·m_rank` para cenários) e peso normalizado pela cota da população (§4.3) |
| **População, cota** | Grupo de objetos de mesma natureza numa categoria mista (cenários, ativos, avaliações, tenants) e a fração do peso que ele recebe |
| **`crit`, `m(crit)`** | Criticidade efetiva 1–5 e seu modulador `0,40 + 0,15·(crit − 1)` |
| **MRA** | Máximo com restante amortecido: `x(1) + ρ(1 − x(1))[1 − Π(1 − x(k))]` |
| **`M_p`** | Média de potência ponderada de ordem p |
| **τ, τ_G** | Pisos de cauda da categoria e da manchete, ligados às faixas pela trava conjunta |
| **Inclusão conservadora** | `max_j A(Aval ∪ Pres_(1..j))` sobre prefixos dos presumidos ordenados por R: cada presumido entra se elevar o valor |
| **`σ_r`, `σ_ref`** | Severidade do cenário: `r_eff/10` (REG, com crédito) e `(r_res ?? r_inh)/10` (GOV e CTL.P, declarada, sem decaimento) |
| **`r_eff`** | Residual efetivo, com crédito de tratamento aplicado (§6.1). Ajuste do indicador, nunca gravado de volta |
| **Revisão qualificada** | `MgmtReview` que não foi gravada por aceitação nem por campanha e não é autoavaliação (§6.1) |
| **`d_ref`, T, `due`, `cred`** | Data da última revisão qualificada, cadência, vencimento e crédito de tratamento |
| **Leitura vigente** | Último valor que a fonte gravou, mantido até evidência de ciclo de vida (§8.2) |
| **Validade `v`** | Frescor de uma leitura em [0,1] (§8.2) |
| **π** | Prior conservador usado na obsolescência e na presunção |
| **Presumido** | Objeto do inventário de EXP ou CTL.A sem leitura com `v > 0`, com valor `R_pres` (§4.2) |
| **Indisponível / não aplicável** | Categoria com inventário e sem fonte (sai de `K`, fica na COV com 0) / categoria sem inventário no escopo (sai das duas) |
| **Cobertura (COV)** | Fração ponderada por criticidade do inventário que foi avaliada |
| **Qualidade de dados** | `COV · fresh`. Não confundir com a confiança da evidência (M40) |
| **Intervalo de ignorância** | Manchete com as categorias indisponíveis em 0 e em 100, sem renormalizar, e os objetos ausentes em 0 e no máximo alcançável |
| **Piso de exibição** | Valor mínimo exibido por condição de Portão A (M43); o ICR\* é sempre mostrado |
| **Teto ordinal de apetite** | `MaxAcceptableResidual` do Track 8, na escala 0–10; aproximação do Portão B, não o Portão B |
| **Selo** | Informação obrigatória ao lado do número que não altera o valor (teto de apetite, P95, KRI) |
| **Chip** | Marcação de condição (evidência para o Portão A, indisponível, sustentado por) |
| **Indicador / KRI** | Número exibido ao lado, sem tolerância / indicador registrado no M46 com tolerância e obsolescência. Ambos ficam fora da soma; o ICR nunca é KRI |
| **Alocação de Euler** | Decomposição exata do valor em contribuições (§4.8) |
| **Sensibilidade do indicador** | Quanto o ICR\* cairia se o objeto ficasse limpo naquela categoria (§4.8). Não é ordem de tratamento |
| **Aumann–Shapley** | Atribuição exata da variação por integração ao longo do caminho (§9.4) |
| **Perfil** | Conjunto versionado e aprovado de parâmetros (§12) |
| **Ponte** | Cálculo nas duas versões no dia da troca de perfil (§9.5) |
| **Linha de base (do trimestre)** | Perfil vigente no primeiro dia do trimestre civil corrente ou, se nenhum vigorava, o primeiro aprovado no trimestre; é contra ele que se contam as regras de ritmo (§12.4) |
| **Rebaseline** | Aprovação explícita que libera as regras de ritmo (troca de preset ou de modo, ω acima de ±0,05 ou mais de dois parâmetros contra a linha de base do trimestre, §12.4) |
| **Efeito de reestruturação (`A_estr`)** | Parcela da variação causada por objetos que entraram no nó ou saíram dele só por reatribuição; não é efeito de risco (§9.4) |
| **Fecho** | O nó, seus descendentes por `entities.parent`, os processos ligados a ele pela propriedade `organizationUnit`, as atividades desses processos e as aplicações desses processos (§10.1) |
| **Não atribuído** | Nó que reúne objetos sem vínculo de entidade |
| **Modo interino / pleno** | Cálculo com os dados de hoje / com as saídas do Track 9 (§15) |
| **Estágio A0–A4** | Passos de adoção do ICR (§15.2). Não confundir com as Fases 0–7 da MIGR-TI/IA nem com as Etapas 9.x do Track 9 |

---

## Referências

- Metodologia: [migr-ti-ia.md](migr-ti-ia.md), [migr-ti-ia-guide.pdf](migr-ti-ia-guide.pdf) (p.8–11,
  p.14–15, p.17), [migr-ti-ia-coverage.md](migr-ti-ia-coverage.md).
- Plano de adequação: [Track 9](../roadmap/TRACK_9_MIGR_TI_IA.md) e [ROADMAP.md](../../ROADMAP.md)
  (M39–M50).
- Fornecedores:
  - Trend Micro, *More Than a Number: Your Cyber Risk Index Explained* (abr/2026) e a especificação
    OpenAPI v3.0 do Vision One;
  - SecurityScorecard, *Methodology Deep Dive 3.0*;
  - documentação Tenable de VPR, ACR, AES e CES, e changelog do VPR v2 (2026-07-01).
- Agregação: OECD/JRC, *Handbook on Constructing Composite Indicators* (2008); NIST SP 800-30r1;
  Cox (2008), *What's Wrong with Risk Matrices?*; documentação pública do Qualys TruRisk (modelo de
  plataforma "pior ativo + bandas amortecidas") e do Microsoft Defender Exposure Score.
