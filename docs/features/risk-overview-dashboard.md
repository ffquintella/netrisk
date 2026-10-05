# Visão Geral de Risco Cibernético — painel do ICR e telas de configuração (S40)

> **Status:** Rascunho para revisão · 2026-10-05 · **S40**
>
> **Metodologia (autoritativa):** [S39 — ICR, Índice Consolidado de Risco](../methodology/icr-indice-consolidado-de-risco.md).
> Esta especificação **implementa** a S39 e não redefine fórmula, parâmetro, estado, faixa ou regra
> dela. Onde este texto e a S39 divergirem, vale a S39 e este texto está errado. As citações têm a
> forma "S39 §n".
>
> **Roadmap:** [ROADMAP.md — Track 10](../../ROADMAP.md) (marcos M52–M59, tarefas T236–T295, com a T269, a T289 e a T295 no
> backlog). Toda tarefa que cita S40 está especificada aqui; o mapa tarefa → seção está na §14.
>
> **Referência visual:** captura do *Cyber Risk Overview* do Trend Vision One enviada pelo product
> owner em 2026-10-05 (não incorporada a este repositório). Ela mostra, de cima para baixo: abas
> *Risk / Exposure / Attack / Security Configuration Overview*; o cartão **CYBER RISK INDEX 46.1/100
> "Medium risk"** com o botão *Cyber Risk Subindexes*; *Contributing categories* (Exposure: Medium,
> Attack: High, Security Configuration: Medium) com links; *Risk Event Overview*; um gráfico de
> tendência de cinco meses sobre áreas de faixa (verde 0–30, amarelo 31–69, vermelho 70–100), com um
> marcador vertical tracejado e a bolha do valor atual; a alternância *Compare with other orgs*; as
> abas de classe de ativo *Devices, Internet-Facing Assets, Accounts, Applications, Cloud Assets*, cada
> uma com "Risk level"; o *Risk Summary* mensal em barras agrupadas (Exposure, Attack, Security
> Configuration); a lista *Risk Factors* ("2.227 Detected vulnerabilities", "34 Cyber threats found on
> devices"); e a *Attack Surface* com "Discovered Devices 16.831", barra de visibilidade *Full /
> Partial / Limited* (7.241 / 2.931 / 6.659) e "Devices Can Be Assessed for Risk 7.361" com barra
> Low / Medium / High (508 / 6.841 / 12).
>
> **Evidência de código:** as referências `arquivo:linha` foram conferidas contra a versão 2.24.0
> (`db_version` 87, `src/ConsoleClient/DB/DatabaseInformation.yaml`). Nada deste documento está
> implementado.

**Legenda de disponibilidade** (a mesma da S39): **[H]** disponível hoje · **[C]** requer correção de
defeito (S39 §14) · **[N]** requer dado ou integração nova.

---

## Emendas

| Data | O que mudou | Seções |
|---|---|---|
| 2026-10-05 | Respostas do product owner às perguntas P-1, P-2, P-3, P-4 e P-6: **escopo hierárquico** (claim na unidade concede as subunidades, nunca o oposto; T292); **aprovação** pelos papéis Gerente de Riscos ou Administrador de Riscos (T293); **Tenable** Vulnerability Management em nuvem com Tenable One (Security Center para o backlog, T269); **Vision One** com créditos CREM e permissões de API; **Master Dashboard** retirado depois da publicação do interino (T294) | §4.2, §5.6.5, §6.8, §9.4, §11.1, §11.3, §12, §13, §14, §15 |
| 2026-10-05 | Respostas a P-7, P-8 e P-11: na publicação, só a **equipe de riscos** (analistas, gerentes e administradores de riscos) recebe `risk_index_view`; **rótulos das faixas em português e inglês**, como o resto do sistema; escrita do **contexto de risco só com escopo global**, com a regra por subárvore prevista para o futuro (T295, backlog) | §6.2, §6.7, §8.3, §10.5, §11.1, §14.3, §15.4 |

## Resumo executivo (leitura de 5 minutos)

**O que é.** Um módulo novo do cliente desktop, "Visão Geral de Risco Cibernético", no formato do
*Cyber Risk Overview* do Vision One. Ele mostra o ICR (S39) da organização, ou da unidade, do processo
ou da atividade escolhidos, e explica de onde vem cada ponto. O servidor calcula os números uma vez
por dia (05:00 UTC); o painel não recalcula nada.

**O que a tela mostra, de cima para baixo** (§5.0):
- **Manchete:** o número inteiro de 0 a 100, a faixa em texto e forma (Baixo 0–30, Médio 31–69, Alto
  70–100), a variação em 7 dias, a cobertura e a qualidade dos dados, e selos como "N cenários acima do apetite".
- **Tendência** de 30, 90 ou 365 dias sobre as faixas, com um marcador onde a configuração mudou.
- **Faixas sempre visíveis** de qualidade dos dados e de governança e revisões: revisões vencidas,
  aceitações a vencer, campanhas e SLA.
- **Categorias contribuintes** (REG, EXP, AME, CTL, GOV), cada uma com nível e acesso ao detalhe.
- **Explorar (drill-down):** o usuário escolhe a dimensão (unidade, processo, atividade, aplicação,
  classe de ativo, fonte, criticidade ou ambiente) e navega numa árvore com o ICR de cada nó. Ao abrir
  um nó, o painel inteiro passa a falar dele.
- **Abas por classe de ativo** (dispositivos, internet-facing, identidades, aplicações, nuvem), com
  resumo mensal, principais fatores de risco e visibilidade do inventário.
- **"O que mudou"**, as maiores contribuições, a lista de objetos com sinais e frescor, a perda
  esperada dos cenários quantificados e, no modo pleno, os Top Risks.

**Quem vê.** Só quem tem `risk_index_view`, e apenas as entidades do seu escopo. Na publicação, isso
é a equipe de riscos: analistas, gerentes e administradores de riscos. Um nó visto em parte
aparece como "parcial". O conteúdo de um risco, host ou incidente exige a permissão do módulo dono (§11.3).

**Telas de configuração** (Administração, §6):
- **Perfis:** versões com comparação entre elas e um **editor** de rascunho para pesos, parâmetros,
  presets e faixas, com validação imediata.
- **Prévia e submissão:** o efeito do rascunho nos últimos 90 dias (que nós mudam de faixa) antes de
  submeter com justificativa.
- **Aprovação:** feita por outra pessoa com `risk_index_approve`, citando a ata do Comitê de Risco de TI.
  O sistema recusa a autoaprovação.
- **Fontes e frescor**, **mapeamentos** (grupos e tags do Vision One, conexões da SecurityScorecard e
  tags do Tenable para entidades), **contexto de risco das entidades** (criticidade, exposição à
  internet, classificação, atividades) e a **conexão Tenable**.

**O que sai primeiro** (§14.3). M52 e M53 não mudam nada visível: corrigem dados e entregam o motor e a
API. M54 e M55 entregam o painel e a configuração ao comitê, em sombra. Depois do piloto de 8–12
semanas (T258), o painel chega aos gestores como "interino". Em seguida vêm Tenable (M56), Vision One
e SecurityScorecard ampliados (M57) e as correções de qualidade (M58); o modo pleno (M59) depende do
Track 9. As decisões pendentes do product owner estão na §15.4.

---

## 1. Problema e objetivo

### 1.1 O pedido

O product owner pediu um painel geral de risco no formato do *Cyber Risk Overview* do Vision One que:

- consolide Vision One, SecurityScorecard, Tenable e os dados internos: incidentes, avaliações, CMDB
  (Jira Assets), lista de vulnerabilidades e o mapa de entidades e processos;
- siga uma metodologia escrita, com pesos configuráveis, que também considere as revisões da
  plataforma (revisões de gestão, aceitações, campanhas);
- mostre **um número principal** e permita drill-down por unidade, processo, atividade e demais
  recortes;
- venha com as telas de configuração que o alimentam.

A metodologia é a S39. Este documento especifica o painel, as telas de configuração, a API, os jobs, o
conector Tenable e a expansão de Vision One e SecurityScorecard que a S39 pressupõe.

### 1.2 O que existe hoje

| Tema | Hoje | Consequência |
|---|---|---|
| Painel consolidado | Só o Master Dashboard, **restrito a administrador** (`DashboardController.cs:29-31`), com um `PostureScore` de contagem ponderada e saturada (`MasterDashboardService.cs:270-282`) que o próprio modelo declara "heurística de triagem" (`EntityPostureSummary.cs:37-41`) | Usuário com escopo não tem painel; o número não é metodologia |
| Índice por entidade | `entities.cyber_risk_index` (`Entity.Posture.cs:13-18`) é gravado pela média ponderada do Vision One (`TrendMicroService.cs:691-694`, escrita em `:700`) **ou** por `100 − score` da SecurityScorecard (`SecurityScorecardService.cs:363`, escrita em `:373`); vence a última sincronização | Sem histórico, sem atribuição de fonte; a S39 proíbe lê-lo (S39 §1.7) |
| Tendência | Não há série persistida. A "tendência de riscos" da tela inicial é reconstruída de datas (`StatisticsService.cs:502`) | Não existe tendência de índice |
| Drill-down | Nenhum. O Master Dashboard agrupa por `entity_id` exato, sem árvore (`MasterDashboardService.cs:52-53`) | Não há roll-up por unidade, processo ou atividade |
| Configuração | Pesos do `PostureScore` são constantes no código (`MasterDashboardService.cs:272-276`) | Nada versionado, aprovado ou auditado |
| Tenable | Só importação de `.nessus`; não há `IntegrationKind` de Tenable (`IntegrationKind.cs:11-30`) | VPR v2, EPSS, ACR e AES não chegam |

### 1.3 Objetivo

1. Um módulo desktop **"Visão Geral de Risco Cibernético"** que exibe o ICR (S39) de um escopo, com a
   faixa em texto e forma, tendência, categorias contribuintes, selos, cobertura e qualidade, e
   drill-down por conjunto (S39 §10) em todas as dimensões pedidas.
2. Telas de configuração na janela de Administração para o ciclo de vida do perfil (S39 §12–§13), as
   fontes e o frescor, os mapeamentos fornecedor → entidade, o contexto de risco das entidades e o
   conector Tenable.
3. Uma API de leitura que respeita o escopo do usuário (S39 §10.4) e uma API de perfis com segregação
   de funções.
4. Os jobs que gravam o snapshot diário oficial (S39 §9.1) e as sincronizações novas.
5. Nada disso entra no caminho de decisão (S39 §1.3, regra R-1).

---

## 2. Referência e pesquisa

### 2.1 O que vem do *Cyber Risk Overview* do Vision One

| Elemento da captura | Elemento no NetRisk | Seção |
|---|---|---|
| Título *Cyber Risk Overview* e data/hora no topo | Título do módulo e **data do snapshot oficial** (05:00 UTC), não a hora da consulta | §5.1 |
| Seletor de organização (tenant) | **Breadcrumb de escopo** (Organização › Unidade › …), limitado ao escopo do usuário | §5.1, §5.6 |
| *CYBER RISK INDEX 46.1/100 "Medium risk"* | **ICR inteiro**/100 com faixa em texto **e forma** (S39 §4.5) | §5.2 |
| *Cyber Risk Subindexes* | **Árvore de nós** do drill-down por conjunto (unidade, processo, atividade, aplicação, classe, fonte, criticidade). O subíndice de grupo de ativos do Vision One aparece como indicador de referência no nó mapeado | §5.6, §5.2.4 |
| *Contributing categories* com nível e link | Painel **Categorias contribuintes** com as cinco categorias da S39 (REG, EXP, AME, CTL, GOV; TER a partir do M48), valor, faixa, peso, pontos e "sustentado por" | §5.4 |
| *Risk Event Overview* (radar por fator) | **"O que mudou"** (atribuição exata, S39 §9.4) e **Contribuições e sensibilidade** (S39 §4.8). Sem radar | §5.7, §5.8 |
| Tendência sobre áreas de faixa, marcador vertical, bolha do valor | Tendência de **ICR\*** sobre áreas de faixa, **EWMA**, sombreamento de piso, **marcadores de descontinuidade** com tooltip, lacunas; a bolha marca o último valor | §5.3 |
| Linha vertical do dia corrente | Não existe: a série termina no snapshot escolhido, e a bolha marca esse dia | §5.3 |
| Abas de topo *Risk / Exposure / Attack / Security Configuration Overview* | **Detalhe da categoria**, pelo link › de cada linha de categorias contribuintes: o mesmo nó com a tendência na série da categoria. Não há abas de topo porque, no ICR, uma categoria não é outra página, é um recorte do mesmo conjunto de objetos | §5.4 |
| Abas *Devices / Internet-Facing / Accounts / Applications / Cloud* com "Risk level" | Abas de classe **Dispositivos · Internet-facing · Identidades · Aplicações · Nuvem**, cada uma com a faixa do nó corrente ∩ classe. Unidades, processos, atividades e os demais recortes ficam no painel **Explorar**, sempre visível | §5.5, §5.6 |
| *Risk Summary* mensal em barras | **Resumo mensal por categoria**: média mensal dos `C_k` diários restritos à classe (S39 §9.3) | §5.5 |
| *Risk Factors* com contagem e chevron | **Fatores de risco**: volume e pontos por fator, com link para a lista de objetos filtrada | §5.5 |
| *Attack Surface* — visibilidade *Full / Partial / Limited* e "can be assessed" Low/Medium/High | **Visibilidade do inventário por fonte**: avaliado, obsoleto, presumido, não avaliado, fora do alcance (S39 §8.1, §8.3), e distribuição de `R` por faixa | §5.5 |
| Seletor de período *Current* da *Attack Surface* | A **data do snapshot** global da barra superior; nenhum bloco tem data própria | §5.1 |
| Link *What is the Attack Surface?* | Ícone ⓘ no título de cada bloco, com dica `Str*` que explica o bloco e cita a seção da S39 | §5.5 |
| *Extend Visibility* | Link "Fontes e frescor" no bloco de visibilidade, só para `risk_index_configure` (abre a Administração na aba, §10.2) | §5.5, §6.5 |
| *Data sources* | Chip de fontes → **Fontes e frescor** (configuração) | §6.5 |
| *Manage Reports* | **Exportar** CSV/PDF da visão corrente | §5.14 |

### 2.2 O que é deliberadamente diferente

| Vision One | NetRisk | Por quê |
|---|---|---|
| O CRI usa todos os dados do tenant, independentemente da visibilidade de quem consulta | O ICR **respeita o escopo**: a manchete da organização exige escopo global; nós parcialmente visíveis são recalculados só com o visível e rotulados "parcial — visibilidade restrita" | S39 §10.4 |
| Valor com uma casa decimal | **Inteiro**, arredondado meio-para-longe-de-zero a partir da precisão total | S39 §4.5 |
| A cor da área diz a faixa | Faixa sempre como **texto e forma** (glifo), nunca só cor; nenhum medidor colorido sem rótulo | S39 §4.5; ui-standard §2.6 |
| Só o número | Número **sem piso** sempre guardado e exibido quando difere; **cobertura**, **qualidade**, **intervalo de ignorância**, **selos** e **chips** ao lado | S39 §4.4, §6.6, §7, §8 |
| "Dismiss/accept" de eventos baixa o CRI | Aceitar **nunca** baixa REG nem EXP (I10). O painel não tem ações de tratar ou aceitar | S39 §1.3, §6.2 |
| *Risk reduction goals* com projeção e "eventos a corrigir" | Não existe. Projeção ou meta seria fila de trabalho ordenada pelo índice | S39 §1.2 (7), §1.3 |
| *Compare with other orgs* | Não existe. Não há conjunto de pares; e percentil de população é proibido | S39 §3 |
| *Manage usage* | Não existe. É gestão de licença e créditos do fornecedor; o NetRisk só informa, no teste de conexão, o crédito ou a permissão que falta (§6.9) | — |
| *Estimate Devices* digitado pelo operador | O denominador é o inventário por ciclo de vida (CMDB, fontes) | S39 §8.3 |
| Ataque como índice de 14 dias | AME por incidentes registrados com decaimento; alertas do Workbench só como **indicador** | S39 §3.2 (V1-WB), §3.5 |

### 2.3 Outros padrões usados

- **Tenable One — Exposure View:** cartões de exposição por domínio (equivalem às abas de classe),
  tendência do CES com **marcadores de evento** (equivalem às anotações de modelo de fornecedor da
  §5.3) e *Tag Performance* (equivale ao drill-down por tag mapeada para entidade, §6.6).
- **Microsoft Defender — Exposure score:** faixas 0–29 / 30–69 / 70–100 próximas das do ICR; a
  orientação da Microsoft de tratar mudança de modelo como **nova linha de base** é a descontinuidade
  marcada da S39 §9.5. O *remediation impact* por item vira a **sensibilidade do indicador**, com o
  aviso R-1 de que não é ordem de trabalho (§5.8).
- **Qualys / ServiceNow:** reforçam a escolha de operadores com cauda; não alteram a interface.

---

## 3. Estado atual com evidência

Só o que este painel e as telas tocam. Os defeitos de dados que bloqueiam o índice estão na S39 §14 e
não se repetem aqui.

### 3.1 Painéis e gráficos

| Fato | Evidência |
|---|---|
| Master Dashboard: serviço singleton, cache de processo de 2 min **não chaveado por escopo** | `src/API/ServicesBootstrapper.cs:130`; `MasterDashboardService.cs:31` |
| O cálculo abre `GetContext()` sem *bypass*; um usuário com papel `Administrator` sem a flag `Admin` passa em `RequireAdminOnly` e preenche o cache com o próprio escopo (PLAUSÍVEL, não observado) | `MasterDashboardService.cs:100`; `DALService.cs:180` |
| Endpoint admin-only | `src/API/Controllers/DashboardController.cs:29-31` |
| Tela inicial usa LiveCharts com cores `SKColor` literais e nome de série em inglês fixo | `src/GUIClient/ViewModels/DashboardViewModel.cs:143`, `:152`, `:193` |
| LiveCharts 2 é um **build de desenvolvimento** | `src/Directory.Packages.props:51-53` (`2.1.0-dev-798`) |
| Já existe uso de `RectangularSection` (áreas de fundo), também com cores literais | `src/GUIClient/ViewModels/Reports/RisksVsCostsViewModel.cs:49-80`; `Views/Reports/RisksVsCosts.axaml:45` |
| Não há gráfico de ponteiro (*gauge*) nem tratador de clique em ponto de gráfico | busca por `DataPointerDown`/`ChartPointPointerDown` sem resultado em `src/GUIClient` |
| `TreeDataGrid` virtualizado com colunas montadas no code-behind | `src/GUIClient/Views/VulnerabilitiesView.axaml:454-455` |
| Rampa de cores "quão grave" já existe como token | `src/GUIClient/Styles/Tokens.axaml:122-127` (`NrCriticality1..5`, `NrCriticalityUnset`) |
| Cartões e métricas do Master Dashboard | `src/GUIClient/Styles/WindowStyles.axaml:293-326` (`Border.card`, `TextBlock.metric*`) |

### 3.2 Escopo e autorização

| Fato | Evidência |
|---|---|
| Escopo plano: `ForEntities([])` é `DenyAll`; claim numa unidade não concede subunidades (a T292 torna o escopo hierárquico) | `src/DAL/Context/EntityScope.cs:31-35`; `src/ServerServices/Services/DALService.cs:172-188` |
| Filtros globais por `entity_id` em riscos, achados, hosts, incidentes e avaliações | `src/DAL/Context/NRDbContext.EntityScope.cs:91-103` |
| O papel `Admin` satisfaz qualquer `[PermissionAuthorize]` | `src/API/Security/PermissionAuthorizationHandler.cs:26-34` |
| O principal de background não tem escopo (T237 corrige) | `src/ServerServices/Security/BackgroundServiceHttpContextAccessor.cs:27-37`; `src/BackgroundJobs/ConfigurationManager.cs:37` |
| Padrão de semeadura idempotente de permissão (`INSERT IGNORE`, sem id) | `src/ConsoleClient/DB/Data/81.sql:21-23` |
| CRUD do mapa de entidades só exige `RequireValidUser` (T241 corrige) | `src/API/Controllers/EntitiesController.cs:11`, `:82-84`, `:137-138` |
| Allowlist da auditoria por campo; `Entity` fora dela | `src/DAL/Auditing/GovernanceAuditInterceptor.cs:48-61`; `CorrelationId` por `SaveChanges` em `:104` |
| Histórico por registro já exposto para hosts | `src/API/Controllers/HostsController.cs:194-204` → `IAuditTrailService.GetForRecordAsync` (`src/ServerServices/Interfaces/IAuditTrailService.cs:15`) |

### 3.3 Shell, administração e integrações (GUI)

| Fato | Evidência |
|---|---|
| Lista de módulos do shell | `src/GUIClient/Models/AvaliableViews.cs:3-14` |
| Troca de módulo por `IsVisible` | `src/GUIClient/ViewModels/MainWindowViewModel.cs:288-323` |
| A navegação não sabe abrir um registro específico | `src/GUIClient/Navigation/INavigationService.cs:14-31` |
| Botão do Master Dashboard habilitado por `IsAdmin`, com dica de permissão | `src/GUIClient/Views/NavigationBar.axaml:45-57` |
| Flags de permissão da barra de navegação | `src/GUIClient/ViewModels/NavigationBarViewModel.cs:272-277` |
| A janela de Administração só abre para `IsAdmin` | `src/GUIClient/Views/NavigationBar.axaml:158-160`; aberta em `NavigationBarViewModel.cs:322` |
| Administração: barra de ícones com dica e painéis por `IsVisible` | `src/GUIClient/Views/AdminWindow.axaml:22-94` (navegação), `:96-125` (conteúdo) |
| `AdminViewModel` inicializa **todos** os painéis no construtor | `src/GUIClient/ViewModels/AdminViewModel.cs:152-180` |
| Aba de postura (Vision One e SecurityScorecard) no vocabulário S37 com cofre de segredos, Salvar/Testar/Sincronizar/Excluir e log | `src/GUIClient/Views/Admin/IntegrationsView.axaml:858-1150` |
| A aba de postura **não tem seletor de entidade**; o `EntityId` da conexão só é preservado | `IntegrationsView.axaml:858-1110` (sem campo); `src/GUIClient/ViewModels/Admin/IntegrationsViewModel.cs:1983`, `:2019` |
| Utilitários da base de view-model: `Toasts`, `WithBusyAsync`, `RunAsync`, `ExplainError`, legendas de formCard | `src/GUIClient/ViewModels/ViewModelBase.cs:41`, `:60`, `:84`, `:190`, `:206-209` |

### 3.4 Integrações, jobs e persistência

| Fato | Evidência |
|---|---|
| Conexão Vision One: opções e `EntityId` | `src/DAL/Entities/TrendMicroConnection.cs:13-69` |
| Vencimento de sincronização contado do fim da execução (T276) | `TrendMicroService.cs:282-286` |
| Fator SSC ausente gravado como 0 (T274) | `src/ServerServices/Integrations/SecurityScorecard/SecurityScorecardClient.cs:111` |
| Histórico SSC atrás de `configuration` | `src/API/Controllers/SecurityScorecardController.cs:17`, `:138-146` |
| Controladores de postura: `[PermissionAuthorize("configuration")]`, rotas CRUD/test/sync/log | `src/API/Controllers/TrendMicroController.cs:17`, `:31-131` |
| Registro de referências de cofre | `src/ServerServices/Secrets/SecretVaultService.cs:772-786` |
| Saída HTTP única com política de SSRF; redes privadas permitidas por padrão e controláveis por `Integrations:BlockPrivateNetworks`/`AllowedPrivateHosts` | `src/ServerServices/Interfaces/IOutboundHttpClient.cs:12`; `src/ServerServices/Http/OutboundUrlPolicy.cs:28-46` |
| Log de sincronização único com `IntegrationKind` | `src/DAL/Entities/IntegrationSyncLog.cs:11-56`; `src/DAL/Enums/IntegrationKind.cs:11-30` |
| Horários: residual 02:20, retenção de governança 02:30, Vision One 03:00, SSC 04:00, expiração 06:15, cadência 07:30, campanhas 08:00 | `src/BackgroundJobs/JobsManager.cs:46-63`, `:90-95` |
| Hangfire com `InMemoryStorage` e 2 *workers*: estado de job não sobrevive a reinício | `src/BackgroundJobs/ConfigurationManager.cs:131-140` |
| Listas paginadas por `ListQuery` (sintaxe Sieve traduzida para Gridify), `X-Total-Count`, 409 para mapeador e 400 para filtro | `src/API/Controllers/HostsController.cs:60-90` |
| Exportação de evidência: CSV direto e PDF pelo motor de relatórios (armazenado como `NrFile`) | `src/API/Controllers/AuditTrailController.cs:129-191`; `src/Model/Reports/ReportParameters.cs:24` |
| A listagem de relatórios não filtra por escopo nem por autor | `src/API/Controllers/ReportsController.cs:25-33` |
| Anexos de evidência como `nr_files` com FK e filtro de escopo | `src/DAL/Context/NRDbContext.Aspm.cs:55-59`; `src/DAL/Context/NRDbContext.Security.cs:35-36` |
| Tipo `businessProcess` é raiz, sem filhos; versão do YAML 2.4 | `src/API/EntitiesConfiguration.yaml:2`, `:346-392` |

---

## 4. Estado alvo e escopo negativo

### 4.1 Estado alvo

- Módulo **Visão Geral de Risco Cibernético** no shell, para quem tem `risk_index_view`, lendo só
  snapshots oficiais ou cálculos derivados das linhas de objeto do dia (S39 §9.1).
- Painel **Índice de risco** na janela de Administração, para `risk_index_configure`,
  `risk_index_approve` e `entity_risk_context` (T241).
- Aba **Tenable** e aba **Mapeamento de entidades** em Integrações; seletor de entidade nas conexões
  Vision One e SecurityScorecard.
- Tabelas da S39 §12.5 mais as adições da §7 deste documento.
- `IcrIsNotADecisionInputTest` verde e verificações de fonte na GUI que impedem ações de decisão no
  painel.

### 4.2 O que esta especificação não faz

1. **O ICR nunca é entrada de decisão.** O painel não oferece "tratar", "aceitar", "escalar",
   "criar mitigação" nem qualquer ação de escrita sobre o objeto. O drill-down termina no objeto, com
   **navegação** para a tela onde vivem os portões e as decisões (S39 §1.3).
2. **Nenhuma lista ordena por Δ** do ICR nem pela sensibilidade do indicador. Colunas de Δ não são
   ordenáveis (§5.6.1, §5.8; desvio declarado na §15.2). Não há fila de trabalho derivada do índice.
3. **Sem previsão de tendência**, por IA ou estatística, antes do M50 (S39 §1.4).
4. **Sem lista Top Risks aproximada** antes do M43: o espaço mostra "não disponível até o M43"
   (S39 §15.1).
5. **Sem Monte Carlo** no painel. P95/CVaR só do M45 (S39 §16).
6. **Sem piso no modo interino.** Só chips de evidência para o Portão A (S39 §7).
7. **Sem comparação com outras organizações** nem percentil de população.
8. **Sem pesos por entidade:** um perfil global vigente (S39 §12.1).
9. **Sem registro do ICR como KRI** nem comparação com apetite ou tolerância (S39 §1.2).
10. **Sem página no RiskPortal:** fica no backlog (T289). Quando vier, consome a mesma API de leitura e
    entra na allowlist do `IcrIsNotADecisionInputTest` como leitor.
11. **Sem leitura de `entities.cyber_risk_index`**, `PostureScore` ou `ContributingScore` (S39 §3.5).
12. **Escopo hierárquico só pela árvore.** Por decisão do product owner, uma claim numa entidade
    concede os descendentes por `entities.parent`, nunca os ascendentes nem os irmãos (T292, S39 §10.4).
    Vínculos só-EAV (`organizationUnit` de um processo, `applications`) não concedem acesso; esta
    especificação não cria outra regra de visibilidade.
13. **O Master Dashboard não convive com o painel.** Ele é retirado pela T294 depois que o modo
    interino é publicado aos usuários com escopo (decisão do product owner, 2026-10-05); até lá fica
    como está, só para administradores.

---

## 5. O painel "Visão Geral de Risco Cibernético"

### 5.0 Princípios de leitura e layout geral

**De onde vêm os números.** O painel nunca calcula o ICR no cliente nem lê tabelas de origem. Toda
tela mostra um destes quatro tipos de valor, sempre rotulado no chip de procedência (§5.1):

| Procedência | O que é | Quando | Tendência |
|---|---|---|---|
| **Oficial** | Linha de `risk_index_snapshots` do nó pré-calculado (S39 §9.1) | Raiz, entidades dos tipos organization, organizationUnit, subOrganizationUnit, businessProcess e application; classes e grupos de classe na raiz; fontes na raiz | 30/90/365 d |
| **Oficial derivado** | Fórmulas da S39 §4 aplicadas às linhas de `risk_index_object_days` do dia (S39 §9.1) | Interseções (unidade × classe × fonte × criticidade), atividades, "Não atribuído" | 30/90 d (365 d desabilitado com motivo) |
| **Parcial — visibilidade restrita** | Mesmas fórmulas sobre as linhas de objeto do dia que passam no filtro de escopo do usuário (S39 §10.4) | Usuário cujas claims não cobrem o fecho do nó | Nenhuma |
| **Prévia não oficial** | Recálculo com dados vivos, cache de 15 min (S39 §9.1) | Só sob comando explícito (§5.1) | Nenhuma |

**Layout.** Módulo do shell (IX-1), rolagem vertical única, largura mínima 1280 px. A faixa de
qualidade e a faixa de indicadores de governança são **sempre visíveis** e não recolhem (S39 §8.6,
§6.3). Os painéis inferiores são `Expander`s, abertos por padrão os três primeiros.

```
┌────────────────────────────────────────────────────────────────────────────────────────────────┐
│ BARRA SUPERIOR (§5.1): título · breadcrumb · data do snapshot · chips · ações                   │
├──────────────────────────────────┬─────────────────────────────────────────────────────────────┤
│ CARTÃO DA MANCHETE (§5.2)         │ TENDÊNCIA (§5.3)                                            │
│ valor · faixa · Δ7d · qualidade   │ áreas de faixa · ICR* · EWMA · marcadores · piso            │
│ selos · chips · indicadores ref.  │                                                             │
├──────────────────────────────────┴─────────────────────────────────────────────────────────────┤
│ FAIXA DE QUALIDADE (§5.12.1) — sempre visível, sem controle de recolher                         │
│ GOVERNANÇA E REVISÕES — indicadores fora do ICR (§5.2.5) — sempre visível                       │
├────────────────────────────────────────────────────────────────────────────────────────────────┤
│ CATEGORIAS CONTRIBUINTES (§5.4)                                                                 │
├────────────────────────────────────────────────────────────────────────────────────────────────┤
│ EXPLORAR (§5.6): dimensão · filtros · árvore de nós (60 %)   │ resumo mensal do nó corrente      │
├────────────────────────────────────────────────────────────────────────────────────────────────┤
│ ABAS DE CLASSE (§5.5): Dispositivos │ Internet-facing │ Identidades │ Aplicações │ Nuvem         │
│  resumo mensal · fatores de risco · visibilidade do inventário                                  │
├────────────────────────────────────────────────────────────────────────────────────────────────┤
│ ▾ O que mudou (§5.7)                                                                            │
│ ▾ Contribuições e sensibilidade do indicador (§5.8)                                             │
│ ▾ Objetos (§5.9)                                                                                │
│ ▸ Qualidade dos dados — detalhe (§5.12.2)  ▸ Painel monetário (§5.10)  ▸ Top Risks (§5.11)      │
└────────────────────────────────────────────────────────────────────────────────────────────────┘
```

Cada painel carrega em paralelo, com o seu próprio `ProgressRing` (IX-4), e falha sozinho: o erro de
um painel não esconde os outros (§5.13). O indicador de ocupado por painel é desvio declarado da
ui-standard §4.7 (§15.2).

**Objetos de outros módulos.** O ICR fala de cenários, hosts, achados, incidentes e avaliações, cujas
telas têm permissão própria. `risk_index_view` dá o **valor** (R, pontos, categoria, estado), não o
**conteúdo** do registro: rótulos, entradas do cenário, estado de decisão, sinais e destinos de
navegação de um objeto só vêm para quem tem a permissão do módulo dono, e senão aparecem como
"‹Tipo› #id" com a dica "Requer a permissão ‹módulo›" (regra de redação da §11.3). Isso vale em toda
tela deste módulo que nomeia objeto e na exportação.

### 5.1 Barra superior

```
┌────────────────────────────────────────────────────────────────────────────────────────────────┐
│ Visão Geral de Risco Cibernético                                                                │
│ ⌂ Organização › Unidade X › Lab L1                                Snapshot [05/10/2026 📅] 05:00 UTC │
│ [▣ Perfil v3 · Equilibrado] [◐ Interino] [✓ Oficial]           [⟳ Atualizar] [◎ Prévia ao vivo] [⇩ Exportar ▾] │
└────────────────────────────────────────────────────────────────────────────────────────────────┘
```

| Elemento | Conteúdo e regra | Controle / comando |
|---|---|---|
| Título | `StrRiskOverviewTitle` | `TextBlock.header` |
| Breadcrumb de escopo | Caminho do nó corrente (§5.6): segmentos de entidade e, depois deles, um segmento por termo de filtro do nó (classe, fonte, criticidade, ambiente). Para escopo global, a raiz é "Organização". Para escopo restrito com **uma** entidade, a raiz é essa entidade; com **várias**, a raiz é "Meu escopo", que lista as entidades e **não tem manchete** (S39 §10.4: a manchete da organização exige escopo global) | Segmentos são `Button.link` → `BtBreadcrumbClicked(node)`; o último é texto |
| Data do snapshot | Último snapshot por padrão. Escolher uma data anterior mostra o painel **como estava** naquele dia (a janela da tendência termina nela) e acende o chip "Histórico: dd/mm" com o `Button.link` "Voltar ao último" → `BtBackToLatestClicked` | `CalendarDatePicker` com `DisplayDateStart`/`DisplayDateEnd` = primeiro e último snapshot e `BlackoutDates` preenchido com os dias sem snapshot (o complemento, no intervalo, dos dias que `GET /RiskIndex/Snapshots/Dates` devolve) |
| Chip de perfil | "Perfil v{n} · {nome ou preset}". Abre o diálogo somente leitura de perfis: o vigente (parâmetros, aprovador, referência do comitê, vigência) e as demais versões com diferença de parâmetros. **Relatório de sensibilidade, ponte e histórico** só aparecem com escopo irrestrito (`IsGlobalScope`), porque trazem valores da organização e de outras unidades (S39 §10.4); para os demais, a seção fica visível e desabilitada com `StrProfileValuesRequireGlobalScopeReason` ("Requer escopo global: o relatório mostra valores de toda a organização"). Com `risk_index_configure` ou `risk_index_approve`, oferece "Abrir na configuração" (`Button.type3`), que chama `INavigationService.ShowAdministration(AdminTarget.RiskIndex(RiskIndexAdminTab.Profiles, profileId))` (§10.2) | `Border.riskChip` + `Button.link` → `BtProfileInfoClicked` |
| Chip de modo | "Interino" ou "Pleno", com dica que resume a S39 §15.1 | `Border.riskChip.info` |
| Chip de procedência | Oficial · Oficial derivado · Parcial — visibilidade restrita · Prévia não oficial (§5.0) | `Border.riskChip` (+ `.warning` para parcial e prévia) |
| Chip de piloto | "Piloto em sombra — visível só ao comitê (risk_index_approve) e aos administradores" enquanto a publicação estiver em `shadow` (§6.4, §8.2) | `Border.riskChip.warning` |
| Atualizar | Relê o snapshot (não recalcula). `IsRefreshEnabled = !IsBusy`; desabilitado com "Carregando…" | `Button.type2`, ícone `Reload` → `BtRefreshClicked` |
| Prévia ao vivo | Recalcula com dados vivos (S39 §9.1), cache de 15 min. `IsLivePreviewEnabled` = escopo global **e** (`risk_index_configure` **ou** `risk_index_approve`); senão desabilitado com o motivo (`StrLivePreviewDisabledReason`) | `Button.type3`, ícone `EyeRefreshOutline` → `BtLivePreviewClicked` |
| Exportar | Menu: "CSV desta visão" e "Relatório PDF" (§5.14). Desabilitado em prévia não oficial | `Button.type2`, ícone `Download` + `Flyout` → `BtExportCsvClicked`, `BtExportPdfClicked` |

### 5.2 Cartão da manchete

#### 5.2.1 Estado normal (modo interino)

```
┌ ÍNDICE CONSOLIDADO DE RISCO ⓘ ───────────────────────────────┐
│                                                               │
│   64 /100    [▲ Médio]                                        │
│   ↑ +4 (pior) em 7 dias                                       │
│   Qualidade [◆ Alta 0,91] · Cobertura 92% · Frescor 99%       │
│   Intervalo de ignorância: no detalhe ⓘ                       │
│   Manchete pela média ponderada (piso τ_G inativo)            │
├ Selos ────────────────────────────────────────────────────────┤
│ ◆ 2 cenários acima do teto de apetite (R1, R3);               │
│   nenhum com aceite anterior ao apetite vigente ›             │
│ ◆ 1 cenário com revisão vencida, dentro do teto pelo residual │
│   armazenado e com inerente acima dele (R2) ›                 │
│ ◆ Portão B: não disponível (requer M45/M46)                   │
│ ◆ Σ E[L]: nenhum cenário quantificado (0 de 6) ›              │
├ Chips ────────────────────────────────────────────────────────┤
│ —  (nenhum chip de evidência para o Portão A)                 │
├ Indicadores de referência — fora do ICR ──────────────────────┤
│ CRI Vision One 46,1 (Médio) · 05/10   SSC 72 (C) · 04/10       │
│ CES Tenable —  ·  Intensidade de ataque 34 alertas/30 d       │
└───────────────────────────────────────────────────────────────┘
```

O valor, a faixa, a qualidade, a cobertura, os selos e a ausência de chips de Portão A são os da
Unidade X (S39 §11.2: "Chips de evidência para o Portão A: nenhum"). O Δ de 7 dias e os indicadores
de referência são ilustrativos: a S39 não os dá para a Unidade X.

#### 5.2.2 Regras de cada elemento

| Elemento | Regra | Origem |
|---|---|---|
| Valor | `value_displayed` da linha `category = 0`: inteiro arredondado da precisão total, meio para longe de zero | S39 §4.5 |
| Faixa | `Faixa(valor exibido, faixas do perfil do snapshot)`, mostrada como **texto + glifo** na pílula `Border.riskBand.b{n}`, onde `n = ChartPaletteMap.BandStep(posição, quantidade de faixas)` é o **passo da rampa**, não a posição (3 faixas → `b1`, `b3`, `b5`; §10.6). Glifos por posição (§10.7); a cor é reforço | S39 §4.5; ui-standard §2.6 |
| Δ 7 dias | Seta só quando `abs(round(ICR*_t) − round(ICR*_{t−7})) ≥ delta_seta` (padrão 3); sem `t−7`, o snapshot mais próximo até `t−10`; nunca através de descontinuidade. A seta vem **com texto**: "↑ +4 (pior) em 7 dias", "↓ −5 (melhor) em 7 dias". Sem seta, o texto diz por quê: "estável em 7 dias (variação menor que 3)", "sem comparação: descontinuidade em dd/mm", "sem comparação: sem snapshot entre t−10 e t−7" | S39 §9.3 |
| Valor sem piso | Só no modo pleno e só quando o piso está ativo: "70 · Alto · piso: Portão A — R1 (flag 2) · sem piso 64 (Médio)", com link para os disparadores (Top Risks, T172) | S39 §7 |
| Qualidade | Selo Alta / Média / Baixa por `qualidade_alta`/`qualidade_media` e o valor `COV · fresh`, mais COV e `fresh` em percentual | S39 §8.5 |
| Intervalo de ignorância | No cartão quando `COV < cov_ponto` **ou** há categoria com ω > 0 indisponível; senão "no detalhe" (dica e exportação) | S39 §8.6 |
| Ramo da manchete | "Manchete pela média ponderada" ou "Manchete sustentada por ‹categoria› (piso τ_G × C_k)" | S39 §4.4, §4.8 |
| Selos | Os da S39 §6.6, calculados sobre o conjunto do nó; cada um com `Button.detailButton` (ícone `ChevronRight`) para a lista de objetos filtrada. "Teto de apetite não configurado" quando não há linha. Os mesmos selos de apetite e de revisão aparecem também na linha REG das categorias e no detalhe da categoria REG (§5.4), como a S39 §6.6 exige ("ao lado do ICR e da categoria REG") | S39 §6.6 |
| Chips | Evidência para o Portão A (interino); exploração ativa sem cenário vinculado; categoria indisponível; cadência do perfil ≠ do produto; residual > inerente; nenhum processo declarado crítico; Não atribuído acima do limiar; parcial | S39 §6.1, §7, §8.4–§8.6 |

#### 5.2.3 Estados do cartão

| Estado | Condição (S39 §8.6) | O que se mostra |
|---|---|---|
| Valor pontual | `COV ≥ cov_ponto` e nenhuma categoria indisponível | Valor, faixa; intervalo no detalhe |
| Valor com intervalo | `cov_indicativo ≤ COV < cov_ponto`, ou alguma categoria com ω > 0 indisponível | Valor, faixa e "Intervalo [53–79]" (limites arredondados para inteiros) no cartão, com o chip "AME e CTL indisponíveis" |
| **ICR indicativo** | `COV < cov_indicativo` | "ICR indicativo: 47 · intervalo [20–81]". **Sem faixa, sem pílula colorida, sem seta.** Texto: "cobertura de 31%, abaixo de 40%: o valor não é classificado" |
| **Sem dados** | `K(S)` vazio | Ícone `DatabaseOffOutline` e "Sem dados: nenhuma categoria tem fonte habilitada e inventário neste escopo". Selos e chips continuam (por exemplo, "Teto de apetite não configurado") |
| Com piso (pleno) | Predicado do Portão A verdadeiro para algum cenário do nó | Valor com piso, faixa do valor com piso, linha "sem piso NN (faixa)" e link para os disparadores |
| Parcial | Ver §5.6.5 | Faixa `Border.riskPartialBanner` acima do cartão; sem seta de 7 dias |

#### 5.2.4 Indicadores de referência (fora da soma)

Faixa separada, título "Indicadores de referência — fora do ICR", com: CRI do Vision One e nível
(`securityPosture.riskIndex`, T271), subíndice do grupo de ativos mapeado para o nó (T271, §6.6),
rating SSC com nota, CES da Tenable (quando licenciado) e intensidade de ataque do Workbench (T273),
cada um com a data da leitura e o selo "obsoleto" após `2W`.

- Visíveis **só** com escopo irrestrito ou no nó da entidade da conexão (S39 §10.4).
- Nunca há subtração, razão ou gráfico comparando um indicador ao ICR. Eles são entradas de
  calibração (S39 §13.3) e KRIs de referência no M46, não "a mesma coisa medida de outro jeito".
- Indicador sem fonte configurada não aparece; fonte configurada sem leitura mostra "—".

#### 5.2.5 Governança e revisões — indicadores fora do ICR

Faixa sempre visível abaixo da faixa de qualidade (§5.0), título "Governança e revisões — indicadores
(fora do ICR)", com a nota "contagens são volume, não risco". É o "só exibição" da S39 §6.3 e o "ao
lado" da S39 §15.1: as revisões da plataforma aparecem aqui como volume, além de entrarem no número por
REG e GOV.

```
Governança e revisões — indicadores (fora do ICR)                                  contagens são volume, não risco
Aceitações válidas que vencem em ≤ 30 d: 3 ›    Uso de justificativa de exceção à segregação: 1 em 90 d ›
Campanhas: conclusão 82% · tempo médio até decidir 9 d · vencidas 1 ›    Conformidade de SLA: 71% ›
Flags (M43): não disponível (requer M43)    Portão B: não disponível (requer M45/M46)
```

| Indicador | Regra | Origem |
|---|---|---|
| Aceitações a vencer | Aceitações válidas do conjunto do nó com `ExpiresAt ≤ hoje + janela_aviso_expiracao` (padrão 30 d) | S39 §6.3 (só exibição), §12.2 |
| Exceção à segregação | Revisões de gestão (`mgmt_reviews`) dos cenários do conjunto com `SegregationOverrideReason` preenchido nos últimos 90 dias | S39 §6.3 (só exibição) |
| Campanhas | Conclusão (itens decididos / itens) da campanha mais recente das entidades do fecho, tempo médio até decidir e campanhas `Overdue` | S39 §6.3 (só exibição), §6.4 |
| Conformidade de SLA | % dos achados abertos para SLA do conjunto dentro do prazo | S39 §3.5, §6.4 |
| Flags, Portão B | "não disponível (requer M43)" / "(requer M45/M46)" no interino | S39 §15.1 |

- Calculados sobre o conjunto do nó, com o mesmo filtro de escopo das linhas de objeto: em visão parcial
  contam só o visível (S39 §10.4).
- Cada contagem com `Button.detailButton` leva ao módulo dono, filtrado (Riscos, Campanhas,
  Vulnerabilidades), e fica desabilitada com o motivo quando o usuário não tem a permissão do módulo
  (`ModuleAccess`, §10.3).
- Nunca entram no número nem em ordenação; não são KRI (S39 §1.2).
- API: `GET /RiskIndex/GovernanceIndicators` (§8.2). Tarefa: T256.

### 5.3 Tendência

```
Tendência   Janela [90 dias ▾]   Série [ICR ▾]   [☰ Ver como tabela]   ⓘ
100 ┤ Alto ≥ 70 ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░╎░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░
 70 ┤╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╎╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌
    │        ╭──╮              ╭─○─╮              ▼ v3 ╎ ┈┈┈┈┈ v3 recalculado (90 d)
    │ ───────╯  ╰──────────────╯   ╰─────────╮         ╎  ╭──╮         ╭───────● 64
 31 ┤╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╰─────────╎──╯  ╰─────────╯ Médio 31–69
    │ ┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄╎┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄
  0 ┤ Baixo 0–30                    ◇ VPR v2 (Tenable)  ╎
      07-08     07-22     08-05     08-19     09-02     09-16     09-30  10-05
   ── ICR* diário   ┄┄ Tendência (EWMA 7)   ▼ descontinuidade   ◇ anotação de fornecedor   ○ lacuna   □ indicativo
```

| Elemento | Regra | Origem |
|---|---|---|
| Janelas | 30, 90 e 365 dias. Nós derivados: 30 e 90 (365 desabilitado: "disponível só para nós pré-calculados"). Parcial: sem tendência | S39 §9.3 |
| Séries | ICR (padrão), REG, EXP, AME, CTL, GOV; TER quando ω_TER > 0. Categoria indisponível em um dia aparece como lacuna nesse dia | S39 §9.3 |
| Linha diária | **ICR\*** (sem piso), valor do dia em inteiro | S39 §4.4 |
| Dia **indicativo** (`COV < cov_indicativo` no dia) | O ponto é desenhado como quadrado vazado (distinto do círculo vazio da lacuna e do losango da anotação) com "bigode" vertical do intervalo de ignorância do dia; a dica e a tabela dizem "indicativo, sem faixa · intervalo [20–81]" e **nunca** nomeiam a faixa da área em que o ponto cai. A linha não é interrompida (o valor existe), mas o trecho entre dois dias indicativos é tracejado | S39 §8.6 |
| Dia com categoria indisponível (ω > 0) | Faixa vertical do intervalo de ignorância do dia (sombreado claro, sem cor de faixa), com rótulo "intervalo: ‹categorias› indisponíveis" na dica e na tabela | S39 §4.4, §8.6 |
| Dia sem dados (`K(S)` vazio) | Sem ponto; dica "sem dados" | S39 §8.6 |
| EWMA | `α = 2/(ewma_n + 1)`; reinicia em todo marcador de descontinuidade; nos dias sem snapshot usa o valor repetido | S39 §9.3 |
| Áreas de faixa | Uma área por faixa do perfil **vigente em cada trecho**: se `faixas` mudou dentro da janela, as áreas mudam no marcador. Rótulo de texto dentro da área ("Alto ≥ 70") | S39 §4.5, §9.3 |
| Linhas de referência | Limites das faixas; na série REG, teto de apetite × 10 (da entidade do nó, senão global; rótulo "teto de apetite") e, quando não há teto, nada; no modo pleno, nível-alvo (M44, T180) | S39 §6.6, §9.3 |
| Sombreamento de piso | Trechos em que ICR ≠ ICR\*, com rótulo "piso: Portão A". Nunca no interino | S39 §4.4, §7 |
| Marcadores de descontinuidade | Linha vertical tracejada + triângulo no topo nos dias com: nova versão de perfil (inclui fonte habilitada e preset), troca de modo, mudança de teto de apetite, reestruturação do mapa de entidades, mudança do hash de insumos externos. Como cada um é detectado está na §9.3 | S39 §9.5 |
| Dica do marcador | "dd/mm — Perfil v2 → v3 (rebaseline) · Δ da versão −3,4 · seta e EWMA reiniciam"; para insumo externo: "SLA alterado (sla_configurations) · efeito de configuração +0,8" | S39 §9.5 |
| Anotações de fornecedor | Losango vazio no rodapé, **sem quebra**: VPR v2 da Tenable (2026-07-01), recalibração e penalidade de violação da SSC, algoritmo do CRI do Vision One | S39 §9.5 |
| Ponte de rebaseline | Linha pontilhada "v{n} recalculado" nos 90 dias anteriores à troca, rotulada "parcial" quando a mudança envolve âncora ou multiplicador | S39 §9.5 |
| Lacunas | Dia sem snapshot: valor repetido, ponto vazio, dica "lacuna: sem snapshot neste dia" | S39 §9.3 |
| Dica do ponto | Data, valor, ICR com piso (se diferente), Δ em relação ao dia anterior; quando `abs(ΔICR*) ≥ delta_detalhe`, as três maiores parcelas de "o que mudou" | S39 §9.3, §9.4 |
| Clique no ponto | Leva "O que mudou" (§5.7) para aquele dia | — |
| Ver como tabela | `Button.type3` (ícone `TableLarge`) cujo rótulo alterna entre "Ver como tabela" e "Ver como gráfico"; troca o gráfico por um `DataGrid` (data, ICR\*, ICR, EWMA, Δ, estado de exibição, intervalo, marcador, lacuna). É também o caminho de acessibilidade (§10.8) | — |
| ⓘ | Dica `StrRiskTrendHelp`: o que é ICR\*, a EWMA e os marcadores, com a referência à S39 §9.3 | — |

### 5.4 Categorias contribuintes

```
Categorias contribuintes                                    Peso         Pontos  Sustentado por          Cob.
REG  Cenários do registro       68 [▲ Médio] ███████░░░   0,30          20      R1 — vazamento… (piso) ›  83%
     ◆ 2 cenários acima do teto de apetite (R1, R3) › · ◆ 1 com revisão vencida, inerente acima do teto (R2) ›
EXP  Exposição técnica          74 [⬣ Alto ] ███████▌░░   0,30          22      APP-03 (piso) ›           91%
AME  Ameaça e eventos           60 [▲ Médio] ██████░░░░   0,15           9      INC-2 em APP-03 (piso) ›  100%
CTL  Controles e conformidade   20 [● Baixo] ██░░░░░░░░   0,10           2      R1 (piso) ›               100%
GOV  Governança e tratamento    67 [▲ Médio] ██████▋░░░   0,15          10      R1 (piso) ›               97%
Pontos arredondados: a soma pode diferir do valor exibido em até ±2. TER entra a partir do M48.
```

| Coluna | Regra | Origem |
|---|---|---|
| Valor | `C_k` inteiro do dia; barra 0–100 com o número ao lado | S39 §4.3 |
| Faixa | `Faixa` sobre o inteiro, texto + glifo | S39 §4.5 |
| Peso | ω configurado; quando há categoria indisponível, também o efetivo renormalizado: "0,30 → 0,40" | S39 §4.4 |
| Pontos | Contribuição da categoria à manchete pela alocação de Euler (`g_k · C_k`), inteira | S39 §4.8 |
| Sustentado por | No ramo do piso, o objeto de maior `R_o` e "(piso)"; no ramo da média, "média"; empate: "empate entre N objetos". O rótulo do objeto segue a regra de redação da §5.0 | S39 §4.8 |
| Cobertura | `cov_k`; os pontos de presunção aparecem na dica | S39 §4.3, §8.4 |
| Selos (só REG) | Linha abaixo de REG com os selos de apetite e de revisão do nó ("N cenários acima do teto de apetite", "M cenários com revisão vencida…", "Teto de apetite não configurado"), vindos de `RiskIndexCategoryDto.Seals`; repetidos no topo do detalhe da categoria REG | S39 §6.6 item 2 |
| Link › | `Button.detailButton` (ícone `ChevronRight`). Abre o **detalhe da categoria**: o mesmo nó com a tendência na série da categoria (na série REG, a linha do teto de apetite), a lista de objetos e as contribuições filtradas por ela. Substitui as abas de topo do Vision One (§2.1) | S39 §6.6 item 3, §9.3 |

| Estado da categoria | Exibição | Origem |
|---|---|---|
| Disponível | Como acima | — |
| **Indisponível** | Linha mantida, valor "—", texto "Indisponível — requer ‹integração›" (por exemplo "intake de incidentes inativo", "requer M39" em atividades; no processo, EXP é parcial, §5.6.3); peso efetivo 0 e cobertura 0 | S39 §8.1, §10.1 |
| **Não aplicável** | Linha esmaecida (`TextBlock.metric_detail`), "não se aplica a este tipo de nó" | S39 §8.1, §10.1 |
| Desligada (ω = 0) | Não listada; TER aparece só como nota até o M48 | S39 §2.3 |

### 5.5 Abas de classe de ativo

As abas repetem o padrão da captura e são **só de classe de ativo**. Unidades, processos, atividades,
aplicações e os demais recortes ficam no painel **Explorar** (§5.6), sempre visível. O cabeçalho de
cada aba mostra o ícone, o nome e "Nível: ‹faixa›" do nó corrente ∩ classe, em texto e glifo; ou
"Indisponível" / "Não aplicável". A dica do cabeçalho lista as categorias aplicáveis da aba
("Categorias: EXP, GOV, CTL"), e uma legenda fixa abaixo da faixa de abas
(`StrRiskTabsNotComparableNotice`) diz "Níveis não comparáveis entre abas: cada classe tem categorias
diferentes", porque só se comparam nós com o mesmo conjunto de categorias (S39 §8.6, §10.1).

| Aba | Classes (S39 §5.4) | Categorias no nó | Interino | Quando indisponível |
|---|---|---|---|---|
| **Dispositivos** | servidor, estação, rede, OT/IoT, desconhecida | EXP, GOV (ativos), CTL (avaliações de hosts) | [H] | — |
| **Internet-facing** | internet-facing, domínio externo | EXP | [H] parcial: domínios SSC; FQDN/IP do Vision One [N] T272 | — |
| **Identidades** | identidade | EXP | [N] T272 | "Indisponível — requer contas do Vision One (T272)" |
| **Aplicações** | aplicação (achados sem host; apps locais do Vision One [N]) | EXP, GOV (ativos) | [H] parcial; [C] D-21 | — |
| **Nuvem** | nuvem | EXP | [N] T272 | "Indisponível — requer ativos de nuvem do Vision One (T272)" |

Uma aba indisponível continua selecionável e mostra um cartão de estado (`Border.riskEmptyState`) com
o motivo, a tarefa que a habilita e, para quem tem `risk_index_configure`, o `Button.link` "Fontes e
frescor" (o *Extend Visibility* da captura), que abre a Administração nessa aba por
`INavigationService.ShowAdministration` (§10.2). REG e AME são "não aplicáveis" nas abas de classe
(S39 §10.1).

O conteúdo de uma aba é o nó corrente ∩ classe da aba; a aba **não** muda o nó da página nem a árvore do
Explorar (precedência na §5.6.2). Se o nó corrente já tem um termo de classe, só a aba dessa classe fica
habilitada; as demais ficam desabilitadas com "O nó atual já é recortado por ‹classe›".

#### 5.5.1 Conteúdo de uma aba de classe

```
┌ [▣ Dispositivos  Nível: ▲ Médio] [⊕ Internet-facing  ● Baixo] [⊘ Identidades  Indisponível] [▦ Aplicações ⬣ Alto] [☁ Nuvem ⊘] ┐
│ Níveis não comparáveis entre abas: cada classe tem categorias diferentes                                 │
│ Resumo mensal (média dos C_k diários) ⓘ  [☰ Ver como tabela] │ Fatores de risco ⓘ                  pts │
│ 100 ┤                                              │ 1.204 achados expostos sem exploração     18 › │
│  50 ┤ ▆▅▃  ▇▅▂  ▅▆▃  ▄▆▃  ▃▅▅† ▃▄▅                  │    31 achados com exploração conhecida      9 › │
│   0 ┼──────────────────────────────                │    12 hosts com score de fornecedor ≥ 70    4 › │
│      mai  jun  jul  ago  set  out*                 │   410 hosts com SLA vencido                 3 › │
│   barras rotuladas "EXP 74" · ordem fixa EXP, CTL, GOV │  88 hosts presumidos (sem leitura válida)   2 › │
│   * mês parcial  † troca de perfil                 │ contagens são volume, não risco                  │
├────────────────────────────────────────────────────┴────────────────────────────────────────────────┤
│ Visibilidade do inventário ⓘ  Inventário de EXP: 16.831 hosts ativos (ciclo de vida: CMDB, Vision One, varreduras)  [Fontes e frescor] │
│ Vision One    ███████████████▒▒▒▒▒░░░░░·················  ● Atual 7.241  ◐ Obsoleta 2.931  ◌ Expirada 412  ∅ Sem leitura 6.247 │
│ Varreduras    ████████▒▒░░░░··································  ● 4.120  ◐ 610  ◌ 1.020  ∅ 11.081 │
│ Tenable VM    ⊘ Fonte não habilitada no perfil vigente (requer T266)                                   │
│ Geral (EXP)   ██████████████████▒▒▒░░░░····  ● Avaliado 9.120  ◐ Obsoleto 3.004  ◌ Presumido 4.707     │
│ Distribuição de R dos 9.120 avaliados:  ● Baixo 1.226 · ▲ Médio 7.858 · ⬣ Alto 36                      │
└─────────────────────────────────────────────────────────────────────────────────────────────────────────┘
```

| Bloco | Regra | Origem |
|---|---|---|
| Resumo mensal | Barras agrupadas por mês: média mensal dos `C_k` diários do nó ∩ classe, uma série por categoria aplicável, **na ordem fixa REG, EXP, AME, CTL, GOV, TER** (só as aplicáveis aparecem). Cada barra leva **rótulo de dado** com o código e o valor ("EXP 74"), de modo que a série se lê sem a cor; "Ver como tabela" (`Button.type3`, rótulo alternante) troca o gráfico por um `DataGrid` mês × categoria. 6 meses, alternável para 12. Mês corrente marcado "parcial"; mês com troca de perfil marcado "†" com dica "a média mistura versões" | S39 §9.3; ui-standard §2.6 |
| Fatores de risco | Uma linha por fator (ou sub-fator útil: exploração, SLA, presunção) com **volume** e **pontos** da alocação de Euler atribuída ao fator dominante do objeto (MAX em EXP; repartição do MRA nas demais), ordenadas por pontos. O `Button.detailButton` de cada linha abre a lista de objetos filtrada pelo fator. Nota: "contagens são volume, não risco" | S39 §3.5, §4.8 |
| Visibilidade por fonte | Para cada fonte habilitada na categoria: objetos do inventário com leitura **atual** (`v = 1`), **obsoleta** (`0 < v < 1`), **expirada** (`v = 0`, leitura retida) e **sem leitura** dessa fonte. Fonte não habilitada: linha com o motivo | S39 §8.2, §8.3 |
| Visibilidade geral | Estados da S39 §8.1 do inventário na categoria: avaliado, obsoleto, presumido e, em GOV, não avaliado | S39 §8.1 |
| Distribuição de R | Contagem dos objetos **avaliados** por faixa de `100·R`; a soma é igual ao total "Avaliado" da visibilidade geral | — |
| ⓘ | Dica `Str*` por bloco: o que o bloco mede e a seção da S39 (o *What is the Attack Surface?* da captura) | — |

Cada segmento das barras de visibilidade tem rótulo com a contagem na legenda e um glifo próprio
(§10.7); a cor nunca é a única informação.

### 5.6 Drill-down: painel Explorar

#### 5.6.1 Painel Explorar e árvore de nós

O painel **Explorar** fica sempre visível entre as categorias contribuintes e as abas de classe
(§5.0). À esquerda (60 %), a árvore dos filhos do nó corrente; à direita, o resumo mensal do nó
corrente com todas as categorias aplicáveis (mesmo gráfico, mesmos rótulos e mesma alternativa em
tabela da §5.5.1). O seletor de dimensão existe **só aqui**.

```
Explorar   Dimensão [Unidade ▾]   Filtros: [Dispositivos ×] [Vision One ×] [+ Filtro ▾]
┌ Nó ───────────────────────────┬ Tipo ─────────┬ ICR ┬ Faixa ────┬ Δ7d ┬ Cob. ┬ Objetos ┬ Compart. ┬ Estado ─────────────┐
│ ▾ Unidade X                   │ Unidade       │  64 │ ▲ Médio   │ +4  │ 92%  │   1.204 │       30 │ oficial             │
│   ▸ Lab L1                    │ Subunidade    │  28 │ ● Baixo   │  0  │ 81%  │      21 │        1 │ oficial             │
│   ▸ Secretaria Acadêmica      │ Subunidade    │  —  │ —         │  —  │ 33%  │     140 │        0 │ indicativo 41 [20–79] │
│ ▸ Unidade Z                   │ Unidade       │  47 │ ▲ Médio   │ −3  │ 90%  │     610 │        4 │ oficial             │
│ ┄┄ não comparáveis com as linhas acima: visão parcial ┄┄                                                                 │
│ ▸ Unidade Y                   │ Unidade       │  55 │ ▲ Médio   │  —  │ 88%  │     902 │       12 │ parcial             │
│ ┄┄ não comparáveis: tipo diferente ┄┄                                                                                    │
│ ▸ Não atribuído               │ Não atribuído │  58 │ ▲ Médio   │ −1  │ 70%  │     930 │        — │ 12% do peso ⚠       │
└───────────────────────────────┴───────────────┴─────┴───────────┴─────┴──────┴─────────┴──────────┴─────────────────────┘
Ordenar por ICR compara só nós do mesmo tipo, com as mesmas categorias aplicáveis e a mesma procedência. Δ não é ordenável.
```

- **Controle:** `TreeDataGrid` com `HierarchicalTreeDataGridSource`, filhos carregados ao expandir
  (`GET /RiskIndex/Nodes`). Colunas montadas no code-behind, como em `VulnerabilitiesView.axaml:454-455`,
  com cabeçalhos ligados a `Str*`.
- **Dimensão:** `ComboBox` → `DrillState.Dimension` (§5.6.2). Muda os **filhos** listados, não o nó
  corrente.
- **Filhos por dimensão** (S39 §2.2, §10.1–§10.2):

  | Dimensão | Filhos de um nó entidade |
  |---|---|
  | Unidade | Filhos por `entities.parent` dos tipos organizationUnit e subOrganizationUnit; na raiz, também "Não atribuído" |
  | Processo | Processos cuja propriedade `organizationUnit` contém o nó ou um descendente |
  | Atividade | Atividades (tipo `activity`, T242) dos processos do fecho |
  | Aplicação | Aplicações listadas em `applications` dos processos do fecho, mais as filhas pela árvore |
  | Classe de ativo | Nó ∩ cada classe/grupo de classe com objetos |
  | Fonte | Nó ∩ cada fonte com leitura |
  | Criticidade | Nó ∩ criticidade efetiva 1…5 |
  | Ambiente | Nó ∩ cada ambiente presente, normalizado pelo vocabulário `ambientes_nao_producao` do perfil: um filho por termo do vocabulário presente (`dev`, `test`…), mais "produção" (valor não vazio fora do vocabulário) e "sem ambiente". Só objetos host têm ambiente; os demais ficam fora desse recorte e a nota do painel diz quantos |

- **Colunas:** Nó, Tipo, ICR (inteiro), Faixa (texto + glifo), Δ7d (regra da §5.2.2; **não
  ordenável**), Cobertura, Objetos (contagem deduplicada **visível**), Compartilhados (objetos que
  também pertencem a irmãos, S39 §10.3), Estado (oficial, derivado, parcial, indicativo com valor e
  intervalo, sem dados).
- **Teclado:** setas navegam; →/← expandem e recolhem; **Enter** abre o nó; **Alt+←** sobe um nível
  do breadcrumb; Ctrl+F filtra por nome (IX-8).
- **Abrir um nó:** Enter, duplo clique ou o `Button.detailButton` (ícone `ChevronRight`) da linha →
  `BtOpenNodeClicked(node)`. O módulo inteiro passa a mostrar o nó (§5.6.3).

#### 5.6.2 Estado do drill-down e filtros combinados

- Um nó é uma **interseção**: no máximo um termo de entidade (unidade, processo, atividade, aplicação
  ou "Não atribuído") e no máximo um termo de cada tipo entre classe, fonte, criticidade e ambiente
  (S39 §10.1: "Unidade U × Dispositivos = O(U) ∩ Dispositivos"). Gramática da chave na §8.1.
- **Estado único.** `RiskOverviewViewModel` guarda um só `DrillState` (registro imutável em
  `Tools/RiskIndex/DrillState.cs`, lógica pura): `NodeKey` (nó corrente, normalizado), `Dimension`
  (dimensão dos filhos da árvore), `ChildFilters` (termos `c:`/`s:`/`k:`/`a:` dos chips do Explorar) e
  `ClassTab` (grupo da aba de classe selecionada). Nenhum outro view-model guarda nó, dimensão ou
  filtro; os painéis recebem o `DrillState` e derivam dele a chave que consultam.
- **Precedência** (cada ação produz um `DrillState` novo):

  | Ação | Efeito no estado | Recarrega |
  |---|---|---|
  | Escolher a dimensão | `Dimension` muda; um chip do mesmo tipo da nova dimensão sai de `ChildFilters`, com toast "Filtro ‹x› removido: agora é a dimensão da árvore" | Só a árvore |
  | "+ Filtro" ou remover um chip | `ChildFilters` muda. "+ Filtro" (`Button.type2` com `Flyout`) oferece só os tipos diferentes da dimensão e ausentes do `NodeKey`; cada chip é `Border.riskChip` + `Button.link` "×" | Só a árvore: cada filho é o termo do filho + os termos de filtro do `NodeKey` + `ChildFilters` |
  | Selecionar uma aba de classe | `ClassTab` muda | Só o conteúdo da aba: `NodeKey` + `c:‹grupo›` |
  | Abrir um filho | `NodeKey` := chave do filho (que já contém os `ChildFilters`); `ChildFilters` := vazio; `Dimension` e `ClassTab` mantidos | A página inteira |
  | Breadcrumb | `NodeKey` := prefixo escolhido (segmento de entidade ou de filtro); `ChildFilters` := vazio | A página inteira |
  | Data do snapshot | Só a data muda | A página inteira |

- Os chips do Explorar nunca mudam o nó da página; os termos de filtro do nó corrente aparecem no
  breadcrumb e saem por ele. A aba de classe nunca muda a árvore.
- `DrillState` e a formatação dos segmentos (`NodeKeyDisplay`) são testados em `GUIClient.Tests`
  (`DrillStateTest`, §12.4), sobre o mesmo parser `Model.RiskIndex.RiskIndexNodeKey` do servidor.

#### 5.6.3 Página do nó

A página do nó é **o mesmo módulo**, reescopado: barra superior com o breadcrumb estendido, cartão da
manchete, tendência (conforme a procedência), faixas de qualidade e de governança, categorias (com o
conjunto de categorias do tipo do nó, S39 §10.1), Explorar, abas de classe, "o que mudou",
contribuições, objetos e qualidade, todos do conjunto `O(n)`. Um selo de tipo ("Processo", "Classe de
ativo: Dispositivos") fica ao lado do título.

**Processo e atividade no interino.** REG, GOV, AME e CTL completos. No processo, EXP é **parcial**:
só os objetos de achado sem host das suas aplicações, com o chip "hosts e serviços requerem M39"
(T284); na atividade, EXP é "indisponível — requer M39". Nos dois, o intervalo de ignorância fica
sempre visível (S39 §10.1–§10.2). **CTL** segue o conjunto de categorias da
S39 §10.1 ("Unidade, processo e aplicação: todas"): CTL.P sobre os cenários atribuídos ao nó e CTL.A
sobre as avaliações cujo alvo está no fecho; se o fecho não tem nenhum dos dois, CTL é "não aplicável"
ao nó. É também o que a S39 §10.2 e §15.1 dizem para processo e atividade.

#### 5.6.4 Comparabilidade

- Só se comparam nós **do mesmo tipo, com o mesmo conjunto de categorias aplicáveis e a mesma
  procedência** (S39 §8.6, §10.1, §10.4). A árvore agrupa os filhos por esse trio
  (`RiskIndexNodeRowDto.ComparabilityGroup`); a ordenação por ICR acontece **dentro** do grupo. Os
  grupos de procedência parcial vêm depois dos oficiais, com a nota "não comparáveis: visão parcial";
  entre grupos de tipo ou categorias diferentes aparece "não comparáveis: categorias diferentes" ou
  "tipo diferente".
- Nós "indicativos" e "sem dados" vão para o fim do grupo, sem faixa.
- Ao comparar unidades de tamanhos muito diferentes, a coluna Objetos e o "sustentado por" ficam
  visíveis, porque o piso de cauda é sensível ao tamanho (S39 §1.1).
- "Soma dos filhos ≠ pai": contagens e Σ E[L] são recontadas no conjunto do pai, com a nota "n objetos
  compartilhados" (S39 §10.3).

#### 5.6.5 Visibilidade restrita ("parcial")

Quando as claims do usuário, já expandidas para os descendentes na árvore (T292), não cobrem o fecho
do nó (S39 §10.4). Com o escopo hierárquico, o gestor de uma unidade vê o oficial da sua unidade e das
subunidades; o "parcial" sobra para nós cujo fecho inclui um processo ou uma aplicação ligados só por
propriedade EAV, ou objetos atribuídos cuja entidade de escopo fica fora da subárvore. A dica da faixa
de aviso diz qual das duas causas se aplica e sugere tornar o processo filho da unidade na árvore.

- faixa de aviso `Border.riskPartialBanner`: "Parcial — visibilidade restrita: o valor considera só os
  objetos do seu escopo e não é comparável ao valor oficial do nó";
- cálculo pelas fórmulas da S39 §4 sobre as linhas de objeto do dia que passam no filtro de escopo;
- **sem tendência**, sem seta de 7 dias, sem "o que mudou" (a atribuição exige o conjunto inteiro) e
  sem indicadores de tenant;
- contagens, cobertura, selos e "Não atribuído" calculados **só sobre o visível**: nenhum "n de N" com
  N oculto, nenhum total que revele objetos fora do escopo;
- exportação CSV permitida e marcada "parcial" em cada seção; PDF indisponível (§5.14).

### 5.7 "O que mudou"

```
O que mudou   Período [Desde o snapshot anterior ▾]   05/10 → 06/10   ICR* 64 → 60 (−4)
┌ Efeito ──────┬ Categoria ┬ Origem ────────────────────────────────────────────┬ Pontos ┬ % da variação ┐
│ Risco        │ GOV       │ R1 — vazamento de dados de alunos (aceite vencido   │   −3   │  78%          │
│              │           │ e escalonamento resolvidos por revisão qualificada) │        │               │
│ Risco        │ EXP       │ APP-03 (Alta corrigida)                             │   −1   │  18%          │
│ Risco        │ REG       │ R1 (crédito de tratamento restaurado)               │    0   │   4%          │
│ Contexto     │ —         │ sem mudança de peso                                 │    0   │   —           │
│ Cobertura    │ —         │ nenhuma categoria entrou ou saiu                    │    0   │   —           │
│ Configuração │ —         │ —                                                   │    0   │   —           │
│ Não atribuído│ —         │ resíduo da integração                               │    0   │   —           │
│ Piso         │ —         │ ICR − ICR* (sem piso no interino)                   │    0   │   —           │
└──────────────┴───────────┴─────────────────────────────────────────────────────┴────────┴───────────────┘
Eventos do dia:  ● R1: revisão qualificada (J. Silva)   ◐ BKP-04 ficou obsoleto   ⊕ 0 objetos novos   ⊖ 0 removidos
```

| Regra | Origem |
|---|---|
| Linhas = efeitos da atribuição Aumann–Shapley por trechos: **risco** (variação de R por objeto e categoria), **contexto** (variação de peso: criticidade, impacto, rank, **com o autor**), **cobertura**, **configuração** (ponte de versão ou insumo externo), **reestruturação** (objetos que entraram ou saíram só por reatribuição, `A_estr`), **não atribuído** (resíduo), **piso** (`ΔICR − ΔICR*`) | S39 §9.4 |
| Parcelas em pontos inteiros ou percentual da variação, ordenadas por magnitude; as duas casas decimais ficam só na exportação | S39 §9.4 |
| Período: "desde o snapshot anterior" (padrão), "7 dias", "30 dias". Janelas longas somam as atribuições diárias; se atravessam um marcador, a variação é mostrada em trechos e o salto da versão entra como efeito de configuração (`Δ_versão`) | S39 §9.4, §9.5 |
| Eventos: objetos novos e removidos, pisos ligados e desligados, aceites vencidos e renovados, fontes que ficaram obsoletas, troca de perfil. Objeto que entra ou sai do nó **só por mudança de atribuição** (o objeto existia nos dois dias e mudou de entidade, por edição do mapa ou por regra de mapeamento) é listado como "entrou/saiu por reatribuição", e a sua parcela vai ao **efeito de reestruturação** (`A_estr`), nunca ao efeito de risco | S39 §9.4, §9.5 |
| Clicar numa linha abre o detalhe do objeto (§5.9) | — |
| Indisponível em visão parcial e em prévia não oficial, com o motivo | S39 §10.4 |

### 5.8 Contribuições e sensibilidade do indicador

```
┌ ⓘ Contribuição e sensibilidade mostram quanto o número depende de cada objeto. Não são ordem de     ┐
│   tratamento: a priorização vem dos Portões C e D e dos fluxos de risco, não deste painel (S39 R-1). │
└──────────────────────────────────────────────────────────────────────────────────────────────────────┘
Peso efetivo do registro: 51% da manchete é sustentado pela severidade de cenários (REG + GOV cenários + CTL.P)
┌ Objeto ───────────────────┬ Cat. ┬ Pontos ┬ Parte da cat. ┬ Ramo ──────────────┬ Sensib. (Δ ICR*) ┬ Próximo a sustentar ┬ Estado de decisão ─────────────┐
│ R1 — vazamento… (SIS)     │ REG  │   20   │ 100%          │ piso: sustentado    │ −4               │ R2                  │ aceite expirado 20/09; Escalated 25/08 │
│ APP-03 (portal)           │ EXP  │   22   │ 100%          │ piso: sustentado    │ −1               │ SRV-01 (72)         │ —                              │
│ R1 — vazamento… (SIS)     │ GOV  │   10   │ 100%          │ piso: sustentado    │ −3               │ R3                  │ idem                           │
│ IRP de R1                 │ CTL  │    2   │ 100%          │ piso: sustentado    │  0               │ Unidade X           │ plano aprovado, teste há 400 d │
└───────────────────────────┴──────┴────────┴───────────────┴─────────────────────┴──────────────────┴─────────────────────┴────────────────────────────────┘
```

| Regra | Origem |
|---|---|
| Pontos = `contrib_o = g_k · parte_o` (soma ao ICR\* sem piso); "parte da categoria" = `parte_o / C_k`; presumidos rotulados "presunção" | S39 §4.8 |
| Lista: os `top_contribuicoes` (padrão 50) do snapshot, ordenados por **pontos**. Colunas ordenáveis: Objeto, Categoria, Pontos. **Sensibilidade não é ordenável** (desvio declarado da ui-standard §6.1, §15.2) | S39 §1.3, §4.8 |
| Sensibilidade: `Δ_{o,k} = ICR*(S) − ICR*(S \| R_{o,k} = 0)`; para eventos de AME, o Δ de uma meia-vida; arredondada a inteiro | S39 §4.8 |
| No ramo do piso: "retido por ‹objeto›" e o próximo objeto que passaria a sustentar | S39 §4.8 |
| Estado de decisão ao lado (somente leitura): aceite válido até, última decisão de campanha, tarefas; Portões "não disponível (M43/M44)"; no modo pleno, o benefício líquido do Portão C/D (M44). Sem a permissão do módulo dono (`riskmanagement` para cenários), a célula mostra "Requer a permissão riskmanagement" (redação, §11.3) | S39 §4.8 |
| Peso efetivo do registro: fração da manchete sustentada pela severidade dos cenários (REG, GOV cenários, CTL.P) pela alocação de Euler | S39 §11.2, §13.2 |
| Sem botão de ação; cada linha só navega (§5.9) | S39 §1.3 |

### 5.9 Objetos e detalhe do objeto

#### 5.9.1 Lista

```
Objetos   Categoria [Todas ▾]  Estado [Todos ▾]  Classe [Todas ▾]  Fonte [Todas ▾]  Crit. [Todas ▾]  [🔍 nome…]
┌ Objeto ─────────┬ Tipo ┬ Classe ──┬ Cat. ┬ R (0–100) ┬ Crit. (origem) ─┬ Estado ───┬ Leitura ───┬ Pontos ┬ Compart. ┐
│ APP-03 (portal)  │ App  │ aplicação│ EXP  │ 93        │ 5 (declarada)   │ avaliado  │ 04/10      │ 22     │ 2        │
│ SRV-01           │ Host │ servidor │ EXP  │ 90        │ 5 (CMDB)        │ avaliado  │ 05/10      │ 0      │ 1        │
│ LEG-05           │ Host │ servidor │ EXP  │ 50        │ 5 (manual)      │ presumido │ 27/07      │ 0      │ 1        │
└──────────────────┴──────┴──────────┴──────┴───────────┴─────────────────┴───────────┴────────────┴────────┴──────────┘
1–50 de 1.204   [◀] [▶]
```

- `DataGrid` paginado pelo servidor (`ListQuery`, 50 por página, máximo 500), total por
  `X-Total-Count`. Filtros e ordenações permitidas na §8.2. **Não há ordenação por sensibilidade nem
  por Δ.**
- **A ordenação é sempre no servidor:** o view-model trata o evento `Sorting` do `DataGrid`, cancela a
  ordenação local (`e.Handled = true`), traduz a coluna para o campo de `Sorts` (§8.2) e recarrega a
  primeira página. Ordenar a página no cliente ordenaria só 50 linhas e mentiria sobre o conjunto.
  Colunas sem ordenação no servidor têm `CanUserSort="False"`.
- Rótulos de objeto seguem a redação da §5.0.
- A lista mostra uma linha por (objeto, categoria) do nó; o filtro de categoria vem pré-selecionado
  quando se chega pelo detalhe de uma categoria, de um fator (§5.5) ou de um selo (§5.2).
- Duplo clique ou `Button.detailButton` → detalhe (§5.9.2).

#### 5.9.2 Detalhe do objeto

**Tipo de janela:** diálogo utilitário modal, somente leitura (IX-1), `DialogWindowBase<NavigationTarget?>`.
Ele fecha devolvendo o destino de navegação escolhido, e o módulo navega (IX-2: quem chamou reage ao
resultado). A linha de ações é a variante de visualização da IX-3: só "Fechar", centralizado; os
destinos de navegação ficam numa seção "Navegação" do corpo.

```
┌ Detalhe do objeto — APP-03 (portal do aluno) ─────────────────────────────────────── Snapshot 05/10 ┐
│ Tipo: Aplicação · Classe: aplicação · Compartilhado com 2 nós (P-Matrícula, P-Pesquisa)             │
│ Criticidade efetiva 5 = max(própria 5, herdada —) + dados 0 · origem: declarada (não BIA) ·          │
│   autor: M. Souza em 12/09/2026 · internet-facing: sim (m_net 1,2)                                   │
├ Por categoria ──────────────────────────────────────────────────────────────────────────────────────┤
│ EXP  R 0,928  avaliado   população única  peso 1,00   ramo: sustenta o piso (C_EXP 74)              │
│ GOV  R 0      avaliado   ativos           peso 1,00   nada vencido                                   │
│ AME  R 0,750  avaliado   (entidade-alvo)  peso 1,00   INC-2 aberto                                   │
├ Fatores (EXP) ──────────────────────────────────────────────────────────────────────────────────────┤
│ Fator  Tipo  x      v     u_f  F_eff  Leitura     Fonte                                               │
│ EXP.V  V     0,928  1,00  1,0  0,928  04/10 22:10 SAST (importação #812)                             │
├ Sinais (EXP.V, até 50) ─────────────────────────────────────────────────────────────────────────────┤
│ s 0,900  Alta · sem CVSS · sem exploração · m_net 1,2 · rule semgrep:xss-01 · achado #45122  ›       │
│ s 0,600  Média · m_net 1,2 · rule semgrep:sqli-03 · achado #45125  ›                                 │
├ Chips ──────────────────────────────────────────────────────────────────────────────────────────────┤
│ —                                                                                                    │
├ Navegação ──────────────────────────────────────────────────────────────────────────────────────────┤
│ [Abrir achados da aplicação]  [Abrir incidente INC-2]  [Abrir entidade]                              │
├─────────────────────────────────────────────────────────────────────────────────────────────────────┤
│                                           [Fechar]                                                   │
└─────────────────────────────────────────────────────────────────────────────────────────────────────┘
```

| Bloco | Conteúdo | Origem |
|---|---|---|
| Cabeçalho | Tipo, classe, nós a que pertence ("compartilhado com N"), criticidade própria, herdada, Δ dados e efetiva; **origem e autor** da criticidade; contexto internet-facing; rótulo "declarada, não BIA" quando vier da propriedade EAV | S39 §5.1–§5.3, §10.3 |
| Por categoria | `R_{o,k}`, estado (S39 §8.1), população, peso `w_o`, ramo (sustenta o piso, média, presunção) | S39 §4.2–§4.3 |
| Fatores | Tipo V/C/S, `x`, validade `v`, `u_f`, `F_eff`, carimbo da leitura e fonte | S39 §4.2, §8.2 |
| Sinais | Até `k_sinais_max` (50) por fator, com `s`, flags usados (exploração, KEV, EPSS, `m_x`, `m_net`) e link para o registro de origem | S39 §9.1 |
| Cenário | Nível de impacto, rank e `n` da campanha, `r_res` e `r_inh`, `T`, atraso, `cred`, `σ_ref`, `r_eff`. O residual efetivo aparece **ao lado** do armazenado, com a nota "ajuste do indicador; o residual armazenado não é alterado" (I9). "Tratamento não avaliado" quando `r_res` é nulo. Só com `riskmanagement` (redação, §11.3) | S39 §6.1, I9 |
| Estado de decisão | Somente leitura: aceitação (válida até / expirada / revogada), última decisão de campanha, tarefas, selos de apetite do cenário. Só com `riskmanagement` | S39 §4.8, §6.2 |
| Redação | Sem a permissão do módulo dono, o diálogo mostra "‹Tipo› #id", os valores do índice (R, estado, população, peso, ramo, fatores sem texto de origem) e, no lugar de cenário, estado de decisão, sinais e navegação, a nota "Requer a permissão ‹módulo›" | §11.3 |
| Navegação | Seção do corpo com um `Button.type3` por destino: **Abrir risco**, **Abrir host**, **Abrir achados do host**, **Abrir achados da aplicação**, **Abrir incidente**, **Abrir avaliação**, **Abrir entidade**. Cada botão tem `Is‹Destino›Enabled` = a mesma regra de permissão que habilita o módulo na barra de navegação, lida do helper puro `ModuleAccess` (§10.3), e, desabilitado, a dica `Str‹Destino›DisabledReason` ("Requer a permissão riskmanagement"; IX-4, IX-7). Objetos de fornecedor sem tela no NetRisk ([N]) mostram o id do fornecedor como texto selecionável | S39 §1.3 |
| Fechar | `Button.dialog2` "Fechar", único e centralizado (IX-3, variante de visualização) | — |

A navegação para um registro específico é nova: `INavigationService.NavigateTo(NavigationTarget)`
(§10.3). Ela recusa, por defesa em profundidade, um módulo que o usuário não pode abrir (toast "Você não
tem permissão para abrir ‹módulo›"). Se o registro não é visível no escopo do usuário, o módulo de
destino abre sem seleção e mostra o toast "O registro não está no seu escopo".

### 5.10 Painel monetário

```
Painel monetário — fora do índice (exemplo na raiz da organização)
Σ E[L] residual ........ não disponível: requer a média residual persistida (quant_residual_ale_mean)
Σ E[L] inerente ........ R$ 1.240.000 · 3 de 41 cenários quantificados · 9% do peso de REG · parcial
P95 / CVaR do portfólio  não disponível (requer M45)
```

| Regra | Origem |
|---|---|
| Σ E[L] **residual** com "n de N cenários quantificados" e a fração do peso de REG quantificada; abaixo de `cobertura_quantificacao_min` o total é rotulado "parcial"; nunca há total nu | S39 §3.2 (REG-ALE) |
| No interino, enquanto a coluna residual não existir, mostra-se o inerente (`ScoringMethod = 3`) rotulado "inerente". A coluna `risk_scoring.quant_residual_ale_mean` é entregue pela T290 (M58, Estágio A3 da S39 §15.2); a partir dela o painel mostra o residual | S39 §15.1, §15.2 |
| P95/CVaR: "não disponível (requer M45)"; no pleno, o valor do M45 como selo de Portão B (T287). Percentis nunca são somados | S39 §1.2, §6.6 |
| Em visão parcial, N conta só os cenários visíveis | S39 §10.4 |
| Moeda: a da configuração FAIR-lite do M38 | — |

### 5.11 Top Risks e elementos do modo pleno

O espaço **Top Risks** existe desde o interino e mostra só: "Top Risks — não disponível até o M43
(T172). O painel hospeda a lista quando ela existir; não há aproximação no modo interino." (S39 §1.2
item 5, §15.1).

| Elemento | Interino | Pleno (tarefa) |
|---|---|---|
| Top Risks | Placeholder acima | Lista do M43 (T172) somente leitura, com navegação (T286) |
| Piso do Portão A | Chips "evidência para avaliação do Portão A" | Piso PA-1 com valor sem piso ao lado (T286) |
| P95/CVaR | "não disponível (requer M45)" | Valor do M45 e selo "P95 acima da tolerância" (T287) |
| Portão B | "não disponível (requer M45/M46)" | Selos "P95 acima da tolerância" e "KRI acima da tolerância" (T287) |
| Confiança da evidência | "não disponível (requer M40)" no painel de qualidade | Distribuição confirmada / indicativa / hipótese, **à parte** da qualidade (T287) |
| Nível-alvo | — | Linha de referência na tendência (T287) |

### 5.12 Qualidade dos dados

#### 5.12.1 Faixa de qualidade (sempre visível)

A S39 §8.6 manda que um conjunto de itens de qualidade fique **sempre visível**. Eles formam uma faixa
logo abaixo do cartão da manchete e da tendência (§5.0), que **não tem controle de recolher** (não é
`Expander`) e acompanha o nó corrente:

```
Qualidade: [⚠ Não atribuído 12% ›] [Criticidade padrão 34% ›] [Fonte obsoleta: Vision One, 27/09 ›]
           [Críticos presumidos 7 ›] [Aplicações órfãs 18 ›] [Pseudo-hosts SSC 4 ›] [Saídos com leitura ruim 2 ›]
```

| Item (S39 §8.6) | Valor |
|---|---|
| Registros "Não atribuído" | Fração por peso, com `⚠` acima de `limiar_nao_atribuido` |
| Aplicações órfãs na raiz | Contagem |
| Objetos com criticidade padrão | Fração por peso |
| Pseudo-hosts da SSC | Contagem |
| Fontes obsoletas | Uma por fonte, com a data da última leitura |
| Ativos críticos presumidos | Contagem (crit ≥ 4) |
| Hosts que saíram do inventário por inatividade com última leitura ruim | Contagem |

Cada item é `Border.riskChip` com `Button.detailButton` para a lista de objetos filtrada; item zerado
continua visível com "0". Em visão parcial os valores contam só o visível (§5.6.5). Um teste de
varredura de fonte garante que a faixa não está dentro de `Expander` nem tem `IsVisible` ligado a um
estado recolhível (`RiskQualityStripAlwaysVisibleTest`, §12.4).

#### 5.12.2 Detalhe (painel recolhível)

```
Qualidade dos dados                                           Valor          Limiar
Não atribuído (fração por peso)                               12%   ⚠        10% ›
Objetos com criticidade padrão (por peso)                     34%                ›
Fontes obsoletas: Vision One — última leitura 27/09/2026      1 fonte            ›
Ativos críticos presumidos (crit ≥ 4)                         7                  ›
Vínculos divergentes (árvore × propriedade organizationUnit)  3                  ›
Aplicações órfãs na raiz                                      18                 ›
Pseudo-hosts da SecurityScorecard (fora do inventário)        4                  ›
Saídos do inventário por inatividade com última leitura ruim  2                  ›
Achados com Status legado × LifecycleStatus divergentes       141                ›
Cenários com residual > inerente                              5                  ›
Confiança da evidência (M40)                                  não disponível (requer M40)
```

Cada linha leva à lista de objetos filtrada. O aviso de "Não atribuído" usa `limiar_nao_atribuido`
e, acima dele, aparece também no drill-down por unidade (S39 §8.5, D-20). As linhas são as da S39
§8.5–§8.6 e §3.3 (métrica de discordância da D-04). A confiança da evidência nunca entra nesta
qualidade (S39 §8.5).

### 5.13 Estados vazio, erro, ocupado e parcial

| Estado | Condição | O que aparece | Ações |
|---|---|---|---|
| Ocupado | Requisição de um painel > 300 ms | `ProgressRing` no painel; o resto continua utilizável (desvio declarado da ui-standard §4.7: as cargas são independentes, §15.2) | Atualizar desabilitado com "Carregando…" |
| Erro de painel | Falha de leitura | Mensagem de `ExplainError` no lugar do painel, dados anteriores preservados, `Button.type2` "Tentar novamente" | — |
| Sem permissão | Sem `risk_index_view` | Botão de navegação visível e desabilitado com a dica de permissão (IX-7) | — |
| Piloto em sombra | Publicação `shadow` e usuário sem `risk_index_approve` explícita nem papel Admin (S39 §13.3: "visível só ao comitê e aos administradores"; §8.2) | Cartão: "O índice está em piloto em sombra e ainda não foi publicado" | — |
| Nenhum perfil vigente | Nenhum perfil aprovado | "Nenhum perfil aprovado: o índice ainda não é calculado"; com `risk_index_configure` ou `risk_index_approve`, `Button.link` para a configuração (`ShowAdministration`, §10.2) | — |
| Aguardando o primeiro snapshot | Perfil aprovado, nenhum snapshot | "O primeiro snapshot será gravado às 05:00 UTC" | — |
| Publicação suspensa | Perfil vigente aposentado sem sucessor (§6.4) | Último snapshot com faixa "Publicação suspensa em dd/mm (perfil vN aposentado)" | Exportação do histórico |
| Sem dados | `K(S)` vazio | §5.2.3 | — |
| Indicativo | `COV < cov_indicativo` | §5.2.3 | — |
| Parcial | §5.6.5 | §5.6.5 | CSV |
| Histórico | Data anterior escolhida | Chip "Histórico: dd/mm" | `Button.link` "Voltar ao último" |
| Prévia não oficial | §5.1 | Chip de procedência, horário da prévia | Exportação desabilitada |
| Painel sem dados próprios | Ex.: tendência de nó derivado em 365 d | Opção desabilitada com o motivo | — |

### 5.14 Exportação

| Formato | Quem | Como | Conteúdo |
|---|---|---|---|
| **CSV** | Quem tem `risk_index_view`, em qualquer procedência exceto prévia | Download direto, sem armazenamento, como `GovernanceEvidenceCsv` (`AuditTrailController.cs:129-191`), com a mesma neutralização de fórmula: célula que começa com `=`, `+`, `-` ou `@` recebe uma tabulação à frente (`GovernanceEvidenceCsv.cs:181`), porque rótulos de ativos e tags vêm de fornecedores. Rótulos de objeto seguem a redação da §5.0 | Seções: cabeçalho (nó, data, perfil, modo, procedência), manchete, categorias, selos e chips, tendência da janela, "o que mudou" com duas casas decimais, contribuições e sensibilidade, qualidade. Em visão parcial, cada seção leva "parcial" |
| **PDF** | Só escopo global (e, em `shadow`, só comitê e Admin, §8.2) | **Só por `GET /RiskIndex/Export?format=pdf`**, que confere permissão, escopo e publicação e chama `IRiskIndexReportService.CreatePdfAsync`. Esse serviço renderiza `RiskIndexPdfReport : TemplatedPdfReport` (MigraDoc), grava o `Report` com o novo `ReportParameters.RiskIndexReportType = 4` e o arquivo como `NrFile`. **`POST /Reports` recusa o tipo 4** (`ReportsService.CreateAsync` → `400 report_type_not_creatable`), porque aquela rota só exige `RequireValidUser` e despacha por tipo sem checar permissão nem escopo (`ReportsController.cs:12`, `:35-58`; `ReportsService.cs:45-62`) | As mesmas seções, em tabelas; tendência como gráfico de linha do MigraDoc com tabela ao lado. Faixas sempre em texto |

O PDF fica restrito ao escopo global porque `GET /Reports` lista todos os relatórios para qualquer
usuário válido (`ReportsController.cs:25-33`): um PDF de unidade armazenado vazaria para quem não tem
escopo nela. Quando a listagem for escopada, a restrição pode cair (pergunta P-5).

**Sem agendamento nesta especificação.** O `ScheduledReportJob` existente não serve: ele só renderiza
layouts QuestPDF de `ReportTemplateVersion` e envia por e-mail aos destinatários que qualquer usuário
válido configura em `ReportSchedulesController` (`ScheduledReportJob.cs:31-80`;
`ReportSchedulesController.cs:35`), sob o escopo global dos jobs. Ligar o tipo 4 a ele mandaria o índice
da organização para endereços arbitrários. A leitura mensal do comitê e o relatório trimestral ao
Conselho (S39 §1.4) são gerados sob demanda por quem tem escopo global; um agendamento próprio fica como
pergunta (P-15).

---

## 6. Telas de configuração

### 6.0 Acesso à Administração e entrada "Índice de risco"

Hoje a janela de Administração só abre para `IsAdmin` (`NavigationBar.axaml:158-160`) e o
`AdminViewModel` inicializa todos os painéis no construtor (`AdminViewModel.cs:152-180`). O autor do
perfil é da **segunda linha** e o aprovador é membro do Comitê de Risco de TI (S39 §13.1); nenhum dos
dois precisa ser administrador do sistema. Por isso:

| Mudança | Regra |
|---|---|
| Botão Administração | `IsEnabled = CanOpenAdministration = IsAdmin ∨ risk_index_configure ∨ risk_index_approve ∨ entity_risk_context`. Dica de permissão inalterada |
| Nova entrada na barra da Administração | `Border.tooltip` com `ToolTip.Tip` em `MultiBinding` do `ActionTooltipConverter` (parâmetro `permission`) sobre `StrRiskIndexHint` (chave `AdminRiskIndexHintMSG`: "Índice de risco: perfis de pesos, prévia, aprovação, fontes e contexto de risco das entidades") e `IsRiskIndexPermitted`; dentro, `Button.navigation`, ícone `TuneVariant`, `IsEnabled="{Binding IsRiskIndexNavEnabled}"`, `Command="{Binding BtRiskIndexClicked}"` |
| `IsRiskIndexPermitted` / `IsRiskIndexNavEnabled` | `IsRiskIndexPermitted = IsAdmin ∨ risk_index_configure ∨ risk_index_approve ∨ entity_risk_context`; `IsRiskIndexNavEnabled = IsRiskIndexPermitted ∧ !RiskIndexIsVisible` |
| Demais entradas | `IsEnabled = Is‹Painel›Permitted ∧ !‹Painel›IsVisible`, com `Is‹Painel›Permitted = IsAdmin`. Cada `Str‹Painel›Hint` continua **estático** (`public string X { get; } = Localizer["…"]`) e descreve o que o ícone abre; a razão do bloqueio vem do `ActionTooltipConverter` ligado a `Is‹Painel›Permitted` (não a `IsEnabled`, que também fica falso quando o painel já está aberto), e a dica fica "‹hint› — ‹texto de `NoPermissionForActionMSG`›" só quando falta a permissão. Nenhum hint é trocado pela razão: numa barra só de ícones, ele é o único rótulo |
| Painel inicial | Administrador: Usuários (como hoje). Demais: Índice de risco |
| Inicialização | Os painéis de administração só são construídos e inicializados quando `IsAdmin`; `RiskIndexAdminViewModel` só quando `IsRiskIndexPermitted`, e dentro dele cada aba só é construída quando a regra da tabela de abas a habilita. Evita uma rajada de 403 para o não administrador |
| Integrações (`configuration`) | **Continua só de administrador na GUI**, como hoje: o painel Integrações (com as abas Tenable, Mapeamento de entidades e as opções novas de Vision One e SSC, §6.6, §6.8, §6.9) segue `IsEnabled = IsAdmin`. `configuration` é a guarda da API (`[PermissionAuthorize("configuration")]`); um não administrador com `configuration` usa a API, não esta janela. Abrir a GUI a ele é decisão fora deste escopo |
| Abertura direta num painel | `INavigationService.ShowAdministration(AdminTarget target)` (§10.2): abre a janela (ou reutiliza a singleton) e chama `AdminViewModel.Select(target)`. Alvo cujo painel ou aba o usuário não pode abrir pelas regras desta seção abre a janela no painel inicial e mostra o toast com a razão ("Requer risk_index_configure") |
| Alterações não salvas | Painéis e abas com edição implementam `IHasUnsavedChanges` (§6.2.1). Trocar de painel, trocar de aba do Índice de risco, fechar a janela ou pressionar Esc com alterações pede confirmação; recusar mantém a seleção e a janela |

A lógica fica em helpers puros, `GUIClient/Tools/Admin/AdminPaneAccess.cs` (painéis, abas, alvos) e
`GUIClient/Tools/Admin/UnsavedChangesGuard.cs` (decisão de confirmar), compilados em `GUIClient.Tests`
(§12.4).

O painel **Índice de risco** (`Views/Admin/RiskIndex/RiskIndexAdminView.axaml`,
`RiskIndexAdminViewModel`) é um `TabControl` com seis abas. Uma aba sem permissão fica visível e
desabilitada, com dica que nomeia a permissão (IX-4, IX-7). Como um controle desabilitado não recebe
ponteiro, o `TabItem` declara `ToolTip.ShowOnDisabled="True"` com a dica ligada a
`Str‹Aba›DisabledReason` (onde o tema não mostrar a dica em item desabilitado, o cabeçalho vai num
`Border.tooltip`, como em `NavigationBar.axaml:44-56`). Quem só tem `risk_index_view` não chega a esta
janela: o chip de perfil do painel (§5.1) abre o diálogo somente leitura de perfis, que é também a
leitura da terceira linha até o M47 (S39 §10.4).

**Toda aba que lê perfis, fontes ou execuções exige também `risk_index_view`,** porque essas leituras
estão atrás dela no servidor (§8.2, §8.4) e `[PermissionAuthorize]` não aceita "uma de várias". Sem
`risk_index_view`, a aba fica desabilitada com "Requer risk_index_view". A concessão conjunta é a regra
operacional da §11.1, mas a janela não depende dela.

| Aba | View-model | Habilitada para | Somente leitura para | Sem `risk_index_view` |
|---|---|---|---|---|
| Perfis | `RiskProfilesViewModel` | configure (lê e cria/exclui rascunho) | approve, `entity_risk_context` | Desabilitada |
| Rascunho | `RiskProfileEditorViewModel` | configure | approve | Desabilitada |
| Prévia e submissão | `RiskProfilePreviewViewModel` | configure | approve | Desabilitada |
| Aprovação | `RiskProfileApprovalViewModel` | approve | configure | Desabilitada |
| Fontes e frescor | `RiskSourcesViewModel` (vista do mesmo rascunho do editor, §6.5) | configure | approve, `entity_risk_context` | Desabilitada |
| Contexto de risco | `EntityRiskContextViewModel` | `entity_risk_context` | configure, approve | Habilitada (lê só `/Entities`, §8.5) |

Convenções comuns às abas: comandos `Bt‹Ação›Clicked`; habilitação `Is‹Ação›Enabled` com
`Str‹Ação›DisabledReason` na dica; escrita por `RunAsync(msg, op)` com toast de sucesso e a frase de
recusa do servidor no erro (`ViewModelBase.cs:84`); leitura com `ExplainError`; `WithBusyAsync` +
`ProgressRing` acima de 300 ms; cartões `Border.formCard` com `TextBlock.sectionCaption`,
`fieldLabel`, `hint`, `notice` e `formCard.caution` (S37). Prefixos de chave de localização por aba
na §10.5.

### 6.1 Perfis

```
┌ Perfis ───────────────────────────────────────────────────────────────────────────────────────────┐
│ [+] [⧉ Comparar] [⟳] [🗑]                                                                           │
│ ┌ Versão ┬ Nome ─────────────┬ Preset ──────┬ Status ──────────────┬ Vigência ─────────────┬ Autor ───┬ Aprovador ┬ Rebaseline ┬ Comitê ───────────┐ │
│ │ v4     │ Calibração T4/26  │ Equilibrado  │ ✎ Rascunho           │ —                     │ A. Lima  │ —         │ não        │ —                 │ │
│ │ v3     │ Calibração T3/26  │ Equilibrado  │ ● Vigente            │ 02/09/2026 →          │ A. Lima  │ C. Rocha  │ não        │ Ata CRTI 2026-08  │ │
│ │ v2     │ Piloto            │ Equilibrado  │ ◌ Aposentado         │ 01/06 → 02/09/2026    │ A. Lima  │ C. Rocha  │ não        │ Ata CRTI 2026-05  │ │
│ │ v1     │ Rascunho inicial  │ CTEM         │ ✕ Rejeitado          │ —                     │ B. Dias  │ —         │ —          │ —                 │ │
│ └────────┴───────────────────┴──────────────┴──────────────────────┴───────────────────────┴──────────┴───────────┴────────────┴───────────────────┘ │
├ Detalhe — v3 (somente leitura) ────────────────────────────────────────────────────────────────────┤
│ Justificativa · Referência do comitê + anexos · Ref. do Conselho · Parâmetros por grupo · Relatório   │
│ de sensibilidade anexado · Histórico de auditoria (autor, campo, de, para, quando)                    │
└───────────────────────────────────────────────────────────────────────────────────────────────────┘
```

No detalhe, o relatório de sensibilidade e o histórico de auditoria só aparecem com escopo irrestrito;
para os demais ficam visíveis e desabilitados com "Requer escopo global" (§8.4).

**Comparar duas versões** abre um diálogo utilitário com dois `ComboBox` (A e B) e uma tabela
Grupo · Parâmetro · A · B · Mudança, com o filtro "só alterados" ligado por padrão. A diferença usa as
chaves da S39 §12.2 e mostra listas e objetos (faixas, âncoras, cadências) por item.

| Comando | `Is…Enabled` | Motivo quando desabilitado | Efeito |
|---|---|---|---|
| `BtNewDraftClicked` (`Button.operation`, `Plus`) | `risk_index_configure ∧ ¬HasOpenDraft` | "Já existe um rascunho ou perfil em aprovação (vN)"; "Requer risk_index_configure" | Diálogo "Novo rascunho": origem (vigente · preset · cópia de vN) e nome. Sem perfil vigente, o preset é obrigatório (Estágio A1, S39 §12.3). "Média pura" não é oferecido |
| `BtCompareClicked` (`Button.type3`, `CompareHorizontal`) | duas versões existem | "Selecione duas versões" | Diálogo de diferença |
| `BtOpenDraftClicked` (`Button.type2`, `PencilOutline`) | selecionado é Rascunho | "Selecione um rascunho" | Vai para a aba Rascunho |
| `BtRefreshClicked` (`Button.type2`, `Reload`) | `!IsBusy` | "Carregando…" | Relê a lista |
| `BtDeleteDraftClicked` (`Button.operation`, `Delete`) | selecionado é Rascunho ∧ `risk_index_configure` | "Só rascunhos podem ser excluídos"; "Requer risk_index_configure" | `ConfirmDeleteAsync`; toast "Rascunho vN excluído". Último da barra (IX-5 A) |

Regra de rascunho único: há no máximo **um** perfil em Rascunho ou Em aprovação por vez, porque a
validação de ±0,05 e de dois parâmetros compara com a linha de base do trimestre (§6.2.3) e dois
rascunhos concorrentes consumiriam o mesmo orçamento. O rascunho aberto é **um objeto só na GUI**:
`RiskIndexAdminViewModel` mantém uma `RiskProfileDraftSession` (parâmetros, `Revision`, `IsDirty`,
Salvar, Descartar) que as abas Rascunho e Fontes e frescor editam juntas (§6.2.1, §6.5).

### 6.2 Editor de perfil (rascunho)

Arquétipo **IX-5 D (Form pane)**: edição em linha, Salvar + Descartar, validação aplicada e
rastreamento de alterações.

```
┌ Rascunho v4 · baseado em v3 · última gravação por A. Lima em 05/10 14:02 ────────────────────────────┐
│ ✓ Sem erros na validação local · sem gatilho de rebaseline · sem exigência de ref. do Conselho        │
├ Grupos ──────────────┬─────────────────────────────────────────────────────────────────────────────┤
│ ▸ Categorias e pesos  │ ┌ CATEGORIAS E PESOS ──────────────────────────────────────────────────────┐ │
│   Agregação           │ │ Preset de partida [Equilibrado ▾] [Aplicar preset]                        │ │
│   Criticidade         │ │                                                                           │ │
│   Normalização        │ │ Categoria   Vigente  Rascunho      Δ       Faixa permitida                │ │
│   Tempo e frescor     │ │ REG          0,30    [0,32 ⇵]    +0,02    [0,20; 0,60]                    │ │
│   Revisões e gov.     │ │ EXP          0,30    [0,28 ⇵]    −0,02    0 ou [0,05; 0,60]               │ │
│   Pisos e faixas      │ │ AME          0,15    [0,15 ⇵]     0       0 ou [0,05; 0,60]               │ │
│   Exibição/cobertura  │ │ CTL          0,10    [0,10 ⇵]     0       0 ou [0,05; 0,60]               │ │
│                       │ │ GOV          0,15    [0,15 ⇵]     0       0 ou [0,05; 0,60]               │ │
│                       │ │ TER          0,00     0,00 🔒      —      travado em 0 até o M48           │ │
│                       │ │ Σω = 1,00 ✓   ω_REG ≥ 0,20 ✓   3+ categorias com ω > 0 ✓                  │ │
│                       │ │ |Δω| ≤ 0,05 no trimestre ✓   Parâmetros alterados no ciclo: 1 de 2 ✓       │ │
│                       │ └───────────────────────────────────────────────────────────────────────────┘ │
│                       │ ┌ COTAS POR POPULAÇÃO ─┐ ┌ TETOS DOS FATORES (u_f) ─────────────────────────┐ │
├───────────────────────┴─────────────────────────────────────────────────────────────────────────────┤
│ [💾 Salvar rascunho] [↺ Descartar alterações]   [✔ Validar]   [→ Ir para a prévia]                    │
└─────────────────────────────────────────────────────────────────────────────────────────────────────┘
```

#### 6.2.1 Comandos e estados

| Comando | `Is…Enabled` | Motivo quando desabilitado | Efeito |
|---|---|---|---|
| `BtSaveDraftClicked` (`Button.dialog1`, `ContentSave`) | `IsDirty ∧ ¬IsBusy ∧ IsStructurallyValid ∧ status = Rascunho` | "Sem alterações"; "Corrija os valores fora da faixa do campo"; "v{n} está em aprovação; rejeite-a ou aguarde" | `PUT` com `If-Match: <revision>`; toast "Rascunho salvo". É o mesmo comando da aba Fontes e frescor (sessão única, §6.1) |
| `BtResetClicked` (`Button.dialog2`, `Restore`) | `IsDirty` | "Sem alterações" | Volta ao último salvo, com confirmação |
| `BtValidateClicked` (`Button.type3`, `CheckAll`) | `¬IsDirty` | "Salve antes de validar no servidor" | `POST …/Validate`; o resultado substitui a validação local |
| `BtApplyPresetClicked` (`Button.type2`, `AutoFix`) | preset escolhido ≠ atual | "Escolha um preset diferente" | Substitui os parâmetros do preset (S39 §12.3); marca "exige rebaseline" se já existe perfil vigente |
| `BtGoToPreviewClicked` (`Button.type2`, `ArrowRight`) | `¬IsDirty` | "Salve o rascunho primeiro" | Vai para a aba Prévia |

- **Validade estrutural** (bloqueia Salvar): cada controle já limita o valor à faixa do parâmetro;
  texto que não converte bloqueia. **Regras entre campos** (Σω, trava conjunta, faixas contíguas…)
  aparecem ao vivo como erro e bloqueiam a **submissão**, não o salvamento: o rascunho é trabalho em
  andamento; o portão é a submissão (§6.3). As **regras de ritmo** (±0,05, dois parâmetros, troca de
  preset) aparecem como aviso "exige rebaseline" e não bloqueiam nada antes da aprovação (§6.2.3).
- **Validação ao vivo** pela mesma função do servidor, `Model.RiskIndex.RiskIndexProfileRules`
  (§8.4), com mensagens inline (`TextBlock.warning` sob o campo, IX-4) e o resumo no topo.
- **Rastreamento:** `IsDirty` compara com o último salvo; sair da aba, trocar de painel ou fechar a
  janela com alterações pede confirmação. Mecanismo (nada disso existe hoje: `AuxiliaryWindowBase`
  fecha no Esc sem veto, `AuxiliaryWindowBase.cs:37-46`, e a troca de painel só inverte `IsVisible`):
  - contrato `IHasUnsavedChanges { bool HasUnsavedChanges { get; } string UnsavedChangesSummary { get; } }`,
    implementado por `RiskProfileDraftSession` (lido pelas abas Rascunho e Fontes) e por
    `EntityRiskContextViewModel`;
  - `AuxiliaryWindowBase` sobrescreve `OnClosing`: se algum painel visível relata alterações, cancela o
    fechamento, aguarda `ConfirmationDialog.ConfirmAsync` e fecha só com "Descartar e fechar"; o Esc de
    `CloseOnEscape` passa pelo mesmo caminho;
  - `AdminViewModel` consulta o painel visível antes de trocar de painel; `RiskIndexAdminViewModel`
    consulta a aba antes de trocar de aba. Como a seleção do `TabControl` não é cancelável, o
    view-model **reverte** `SelectedTab` quando o usuário recusa;
  - a decisão (confirmar ou não, e o texto) fica em `UnsavedChangesGuard`, puro e testado em
    `GUIClient.Tests` (§12.4).
- **Concorrência:** o token é a coluna `revision` do perfil (§7.1), incrementada a cada `PUT`; o
  `updated_at` tem precisão de segundo e não distingue duas gravações no mesmo segundo. `409
  stale_draft` mantém a edição e mostra "O rascunho foi alterado por ‹nome› em ‹hora›. Recarregue para
  continuar." A prévia nunca grava na linha do perfil (§9.6), então ela não provoca 409.
- **Parâmetros travados** aparecem com cadeado, somente leitura, e o motivo na dica.

#### 6.2.2 Parâmetros por grupo

Todo parâmetro da S39 §12.2 está num grupo. A coluna "Ajuda" resume o texto da chave
`RiskParam_‹chave›_Help` (pt-BR); o rótulo é `RiskParam_‹chave›_Label`. "🔒" = travado.

**Grupo 1 — Categorias e pesos**

| Parâmetro | Controle | Faixa / validação | Padrão | Ajuda |
|---|---|---|---|---|
| `omega.REG` | NumericUpDown 0,01 | [0,20; 0,60] | 0,30 | Peso dos cenários do registro; nunca marginal |
| `omega.EXP`, `omega.AME`, `omega.CTL`, `omega.GOV` | NumericUpDown 0,01 | 0 ou [0,05; 0,60] | 0,30 / 0,15 / 0,10 / 0,15 | 0 desliga a categoria; Σω = 1; ≥ 3 categorias com ω > 0 |
| `omega.TER` 🔒 | somente leitura | 0 até o M48; depois [0,05; 0,60] | 0 | Entra como nova versão com 0,05 e as demais × 0,95 (S39 §2.3) |
| `cotas_populacao` | NumericUpDown por população | cada cota em [0,2; 0,8]; soma 1 por categoria | GOV {cenários 0,5; ativos 0,5}; CTL {avaliações 0,5; cenários 0,5} | Impede que hosts afoguem a governança dos cenários (S39 §4.3) |
| `u.<fator>` (EXP.D, EXP.V, EXP.X, EXP.I, EXP.N, AME.I, CTL.A, CTL.P, CTL.C, GOV.A, GOV.C, GOV.T, GOV.R, GOV.G, TER) | NumericUpDown 0,05 | [0,5; 1,0] | S39 §2.3 | Valor máximo que o fator atinge sozinho no objeto. `u_AME.Q` é `fator_quase_incidente` (grupo 4) |

**Grupo 2 — Agregação**

| Parâmetro | Controle | Faixa / validação | Padrão | Ajuda |
|---|---|---|---|---|
| `p` | NumericUpDown 1 | [2; 16] | 4 | Expoente da média de potência entre objetos |
| `tau` | NumericUpDown 0,01 | [0,60; 0,95] **e trava conjunta** | 0,80 | Piso de cauda da categoria: `C_k ≥ 100·τ·max R` |
| `tau_g` | NumericUpDown 0,01 | [0,50; 1,00] **e trava conjunta** | 0,70 | Piso de cauda da manchete |
| `rho` | NumericUpDown 0,01 | [0; 0,5] | 0,30 | Restante amortecido do MRA |
| `k_sinais` | NumericUpDown 1 | [2; 50] | 10 | Sinais considerados no MRA (50 são guardados) |
| Trava conjunta (calculada) | texto ao vivo | `τ·τ_G·L_topo ≥ L_2` | 39,2 ≥ 31 | "Um objeto sozinho na faixa mais alta nunca deixa a manchete na mais baixa" (S39 §12.4). Repetida no grupo 7 |

**Grupo 3 — Criticidade**

| Parâmetro | Controle | Faixa / validação | Padrão | Ajuda |
|---|---|---|---|---|
| `m_a` | NumericUpDown 0,05 | [0,20; 0,60] | 0,40 | Piso do modulador `m(crit)`; mostra a tabela crit 1…5 → m ao vivo |
| `crit_padrao` / `crit_padrao_nao_producao` | NumericUpDown 1 | [2; 4] / [1; 3] | 3 / 2 | Criticidade desconhecida |
| `ambientes_nao_producao` | lista editável | lista de textos | dev, test, homolog, lab | Vocabulário controlado de `hosts.environment` |
| `crit_max_vision_one` | NumericUpDown 1 | [3; 5] | 4 | Teto da criticidade de origem Vision One |
| `crit_ext` | NumericUpDown 1 | [3; 5] | 4 | Criticidade de domínio externo e tenant |
| `passo_classificacao` / `niveis_sensiveis` | NumericUpDown / seleção múltipla de `securityClassificationLevel` | [0; 2] / ids existentes | +1 / vazio | Δ_dados para aplicações com classificação sensível |
| `beta_rank` / `validade_rank_ciclos` | NumericUpDown | [0; 0,6] / [1; 4] | 0,30 / 2 | Peso do rank de campanha; nunca move o valor do cenário |

**Grupo 4 — Normalização e âncoras**

| Parâmetro | Controle | Faixa / validação | Padrão | Ajuda |
|---|---|---|---|---|
| `ancoras_severidade` | 4 campos | monótonas em (0, 1] | 0,20 / 0,50 / 0,75 / 0,95 | Base de achado sem VPR/CVSS |
| `m_x_disponivel` | NumericUpDown 0,05 | [1; 1,5] | 1,15 | Exploit disponível ou EPSS; só sobre CVSS/severidade |
| `teto_sem_exploracao` / `piso_exploracao` | NumericUpDown 0,01 | [0,70; 0,95] / [0,80; 1,0]; teto < piso | 0,90 / 0,95 | Todo achado com exploração fica acima de todo achado sem |
| `epss_limiar` | NumericUpDown 0,01 | [0,01; 0,5] | 0,10 | Interino; T165 substitui |
| `m_net` | NumericUpDown 0,05 | [1; 1,5] | 1,2 | Só em EXP.V de objeto internet-facing |
| `heuristica_ip_publico` / `faixas_publicas_internas` | CheckBox / lista CIDR (`formCard.caution`) | booleano / CIDR válidos | desligada / vazio | Contexto de baixa confiança; exclui faixas públicas usadas internamente |
| `ancoras_ssc` | tabela de 7 pontos | monótonas; s(100) = 0, s(0) = 1 | 100:0 · 90:0,10 · 80:0,35 · 70:0,55 · 60:0,75 · 50:0,90 · 0:1 | Rating SSC → s, com prévia "72 → 0,51" |
| `ancoras_incidente_categoria` / `ancoras_incidente_severidade` | tabelas | [0; 1] | S39 §3.1 | Incidente → s; severidade substitui a categoria quando existir |
| `fator_quase_incidente` | NumericUpDown 0,05 | [0,25; 1] | 0,5 | `u_AME.Q` (M40) |
| `irp_limiar_sigma` / `irp_rampa` / `irp_teste_validade` | NumericUpDown | [0,5; 0,9] / [0,05; 0,2] / [180; 730] d | 0,70 / 0,10 / 365 | Rampa contínua de CTL.P e validade do teste do plano |

**Grupo 5 — Tempo e frescor**

| Parâmetro | Controle | Faixa / validação | Padrão | Ajuda |
|---|---|---|---|---|
| `fontes_habilitadas` | editado na aba Fontes e frescor (§6.5), que é outra vista da mesma `RiskProfileDraftSession`: a edição lá marca este grupo como alterado e o Salvar é o mesmo | subconjunto das fontes da S39 §3.2 | fontes [H] | Habilitar fonte é nova versão, com marcador |
| `frescor_W` | idem, por fonte | [1; 730] d; W > 0 | V1 7 · SSC 7 · scanners 30 · Tenable 30 · CMDB 30 · avaliações 365 | Validade por fonte; depois de W a leitura desliza para o prior |
| `pi_prior` | NumericUpDown 0,05 | [0,3; 1,0] | 0,5 | Prior conservador de obsolescência e presunção |
| `meia_vida_incidente` / `janela_incidente` | NumericUpDown | [7; 90] / [30; 365] d; janela ≥ 3 × meia-vida | 30 / 90 | Decaimento de incidentes encerrados |
| `janela_intake_incidentes` | NumericUpDown | [30; 365] d | 90 | Sem intake, AME fica indisponível, não zero |
| `janela_inventario_sinal` | NumericUpDown | [60; 730] d; ≥ 2W da fonte mais lenta | 400 | Saída de host sem fonte de ciclo de vida |
| `tipos_avaliaveis` | seleção múltipla de tipos de entidade | tipos existentes | tipos com execução Submitted | Inventário de CTL.A |

**Grupo 6 — Revisões e governança**

| Parâmetro | Controle | Faixa / validação | Padrão | Ajuda |
|---|---|---|---|---|
| `lambda_credito` / `credito_minimo` | NumericUpDown | [0,25; 2] / [0; 0,5] | 1,0 / 0 | Perda de crédito por cadência de atraso |
| `cadencia_revisao` | 4 campos (dias) | [7; 365], crescente de Very High para Low | 30 · 90 · 120 · 240 | Cópia aprovada de `review_levels`; mostra a tabela do produto ao lado e o aviso de divergência |
| `limites_registro` | 4 campos | crescentes em [0; 10]; Very High ≤ 10 | 0 · 4,0 · 7,0 · 9,0 | Cópia aprovada de `risk_levels`, Very High alcançável (D-02) |
| `base_cadencia` | ComboBox | InherentRisk / ResidualRisk | InherentRisk | Cópia de `next_review_date_uses` |
| `janela_revisao_derivada` | NumericUpDown | [10; 300] s | 60 | Reconhece revisões gravadas por aceitação ou campanha |
| `s.APT_BRK` (sem / com tratamento) | 2 campos | [0; 1] | 1,0 / 0,5 | GOV.A, acima do teto sem aceite válido |
| `s.ACC_APT` | NumericUpDown | [0; 1] | 0,5 | Aceite anterior ao apetite vigente |
| `s.ACC_EXP` / `s.ACC_REV` / `carencia_aceite` | NumericUpDown | [0; 1] / [0; 1] / [0; 30] d | 1,0 / 0,5 / 7 | Aceite expirado / revogado sem nova decisão |
| `acc_drift_inicio` / `acc_drift_amplitude` | NumericUpDown | [0; 2] / [0,5; 5] | 0,5 / 1,5 | ACC-DRIFT |
| `s.APT_DUAL` / `prazo_contra_assinatura` | NumericUpDown | [0; 1] / [1; 30] d | 0,5 / 7 | Contra-assinatura pendente |
| `s.CMP_PEND` / `s.CMP_ESC` / `s.CMP_MIT` | NumericUpDown | [0; 1] | 1,0 / 0,7 / 0,5 | GOV.C |
| `prazo_seguimento_decisao` | NumericUpDown | [7; 90] d | 30 | CMP-ESC |
| `prazo_ref_tarefa` | NumericUpDown | [7; 90] d | 30 | TSK-OVD |
| `s.REV_REQ` / `prazo_revisao_solicitada` | NumericUpDown | [0; 1] / [3; 60] d | 1,0 / 14 | GOV.R |
| `s.DIV` / `delta_div` | NumericUpDown | [0; 1] / [0,1; 0,5] | 0,8 / 0,3 | Divergência entre evidência técnica e residual (heurística) |
| `janela_aviso_expiracao` | NumericUpDown | [7; 60] d | 30 | Só exibição |
| `desconto_aceite` 🔒 | somente leitura | travado em 0 | 0 | "Aceite não é mitigação nem revisão" (S39 I10) |

**Grupo 7 — Pisos e faixas**

| Parâmetro | Controle | Faixa / validação | Padrão | Ajuda |
|---|---|---|---|---|
| `faixas` | grade editável (rótulo pt-BR, rótulo en-US, limite inferior), 2 a 5 linhas | primeiro limite 0; contíguas, crescentes em [10, 95]; os dois rótulos obrigatórios e únicos em cada idioma; **trava conjunta** | Baixo/Low 0 · Médio/Medium 31 · Alto/High 70 | Categorias de comunicação, não tolerância. Mudança exige `board_approval_ref` na aprovação; botão `Button.type2` "Usar preset Registro" (0/40/70/90) |
| `piso_portao_a` 🔒 parcial | NumericUpDown | [70; 100]; **não desligável** | 70 | Só o Portão A gera piso; no interino não se aplica (S39 §7). O valor é editável dentro da faixa; desligar não existe |
| Trava conjunta | texto ao vivo | `τ·τ_G·L_topo ≥ L_2` | — | Recalculada ao mudar faixas, τ ou τ_G |

**Grupo 8 — Exibição e cobertura**

| Parâmetro | Controle | Faixa / validação | Padrão | Ajuda |
|---|---|---|---|---|
| `cov_ponto` / `cov_indicativo` | NumericUpDown 0,05 | [0,5; 0,95] / [0,2; 0,6] | 0,75 / 0,40 | Quando mostrar intervalo e quando o valor é "indicativo" |
| `qualidade_alta` / `qualidade_media` | NumericUpDown | crescentes em (0, 1) | 0,75 / 0,50 | Selo de qualidade |
| `limiar_nao_atribuido` | NumericUpDown | [0,02; 0,5] | 0,10 | Aviso de "Não atribuído" no drill-down |
| `cobertura_quantificacao_min` | NumericUpDown | [0,5; 1] | 0,80 | Σ E[L] "parcial" abaixo disso |
| `ewma_n` / `delta_seta` / `delta_detalhe` | NumericUpDown | [3; 30] / [1; 10] / [0,5; 5] | 7 / 3 / 1 | Tendência e seta de 7 dias |
| `top_contribuicoes` / `passos_atribuicao` / `residuo_max` | NumericUpDown | [10; 200] / [16; 1024] / [0,01; 0,5] | 50 / 64 / 0,05 | "O que mudou" e contribuições |

#### 6.2.3 Regras de validação (S39 §12.4) e como aparecem

| Regra | Onde aparece | Bloqueia |
|---|---|---|
| Σω = 1 (10⁻⁶); cada ω em 0 ou na faixa; ω_REG ≥ 0,20; ≥ 3 categorias com ω > 0 | Cartão de pesos, Σ ao vivo | Submissão |
| Trava conjunta `τ·τ_G·L_topo ≥ L_2` | Grupos 2 e 7, fórmula com números | Submissão |
| `desconto_aceite = 0`; `piso_portao_a ≥ 70` e ligado | Cadeado | Impossível editar |
| Multiplicadores ≥ 1; `teto_sem_exploracao < piso_exploracao`; âncoras monótonas; SSC s(100) = 0 e s(0) = 1 | Campo | Submissão |
| Faixas: primeiro limite 0, contíguas, crescentes, rótulos em pt-BR e en-US preenchidos e únicos em cada idioma, 2–5 | Grade de faixas | Submissão |
| W > 0; janela ≥ 3 × meia-vida; `janela_inventario_sinal ≥ 2W` da fonte mais lenta | Campo | Submissão |
| Cotas somando 1 por categoria | Cartão de cotas | Submissão |
| `limites_registro` e `cadencia_revisao` crescentes | Campo | Submissão |
| \|Δω\| ≤ 0,05 no trimestre | Cartão de pesos | **Não bloqueia a submissão**: aciona "exige rebaseline" (ver abaixo) |
| No máximo dois parâmetros alterados no ciclo | Resumo do topo ("1 de 2") | **Não bloqueia a submissão**: aciona "exige rebaseline" (S39 §12.4) |
| Troca de preset ou de modo é rebaseline | Resumo do topo | Aciona "exige rebaseline" |
| `faixas` diferente da vigente, **ou nenhum perfil vigente** | Resumo do topo | Exige `board_approval_ref` na aprovação |

**Regras de ritmo não são erro de validação.** A S39 §12.4 admite as situações acima "salvo
aprovação explícita de rebaseline", e quem confirma o rebaseline é o aprovador, depois da submissão.
Bloquear a submissão tornaria impossível chegar à aprovação. Por isso:

- `POST …/Validate` devolve `IsValid` só com as regras estruturais e entre campos das linhas anteriores, e
  `RequiresRebaseline = true` com a lista `RebaselineTriggers` (`preset_change`, `omega_pace`,
  `parameter_count`, `mode_change`) quando alguma delas acontece;
- o rascunho válido com `RequiresRebaseline` **pode ser submetido**; a aba Prévia e submissão mostra o
  aviso "Esta submissão é uma proposta de rebaseline: ‹gatilhos›", e o autor escreve na justificativa o
  motivo do rebaseline (a submissão recusa `400 rebaseline_reason_required` se a justificativa não tiver
  pelo menos 40 caracteres nesse caso);
- só o `Approve` impõe `confirmRebaseline = true`, com a mensagem que lista os gatilhos (§6.4).

**Primeiro perfil e Conselho.** Só o primeiro perfil de todos (nenhum perfil aprovado antes) dispensa
linha de base e regras de ritmo (S39 §12.4). As `faixas` do primeiro perfil **diferem** do vigente
inexistente e exigem `board_approval_ref`: a S39 §4.5 diz que as faixas são aprovadas pelo
Conselho/Reitoria "para comunicação" desde o início.

**Como as regras de ritmo se contam** (S39 §12.4): a linha de base é o perfil vigente no primeiro dia
do trimestre civil corrente (UTC) ou, se nenhum perfil vigorava nesse dia, o primeiro perfil aprovado
no trimestre (assim o perfil de publicação aprovado no mesmo trimestre do perfil de piloto conta contra
ele); trocar de modo é sempre rebaseline, como trocar de preset; o vetor ω conta como **um**
parâmetro, e `frescor_W`, `fontes_habilitadas`, `faixas` e `cotas_populacao` contam como um cada; a
aprovação de rebaseline libera as duas travas de ritmo (±0,05 e dois parâmetros). O resumo do topo
mostra a linha de base usada e a contagem ("1 de 2").

### 6.3 Prévia de sensibilidade e submissão

```
┌ Prévia de sensibilidade — rascunho v4 ────────────────────────────────────────────────────────────┐
│ Estado: ✓ Concluída em 05/10 14:31 (90 dias + hoje) · parâmetros conferem   [▶ Executar prévia]     │
├ Nós ───────────────────────────────────────────────────────────────────────────────────────────────┤
│ Nó               Tipo        Vigente  Rascunho   Δ     Faixa                       Mudou de faixa │
│ Organização      Organização   61       62      +1    Médio → Médio                               │
│ Secretaria Acad. Subunidade    69       70      +1    Médio → Alto                ⚠ sim           │
│ …                                                                                                 │
├ Ranking ─────────────────────────────┬ Peso efetivo do registro ─────────────────────────────────────┤
│ τ de Kendall entre unidades: 0,97     │ vigente 51% → rascunho 53%                                    │
│ (34 unidades comparadas)              │ "Média pura" (só prévia): Unidade X 33 · Organização 29       │
├ Tornado (±20%, um por vez) ──────────────────────────────────────────────────────────────────────────┤
│ τ            ████████████████|████████████████████████   −8,0 / +11,9                              │
│ σ cenário dominante (R1)  ██████████|███████████          −5,1 / +6,0                              │
│ ω_CTL                           ██|██                     +1,0 / −1,0                              │
│ …  Presets: Equilibrado 63,7 · Centrado no registro 65,8 · CTEM 64,0 · Conservador 71,7 · Média pura 33,0 │
├ Objetos que mais se movem ─────────────────────────────────────────────────────────────────────────┤
│ …                                                                                                 │
├ Submissão ─────────────────────────────────────────────────────────────────────────────────────────┤
│ Justificativa *  [ texto multilinha ............................................................ ]   │
│ ✓ Rascunho válido  ✓ Prévia concluída para estes parâmetros  ⓘ Sem exigência de rebaseline nem de ref. do Conselho │
│                                                                        [📤 Submeter para aprovação] │
└───────────────────────────────────────────────────────────────────────────────────────────────────┘
```

| Bloco | Conteúdo | Origem |
|---|---|---|
| Estado | Não executada · Na fila · Em execução (n %) · Concluída em · **Desatualizada** (parâmetros mudaram desde a prévia, por hash) · Falhou (mensagem) | S39 §13.2 |
| Nós | Δ por nó pré-calculado, faixa vigente → rascunho, "mudou de faixa". Aqui Δ é **efeito de parâmetro** e pode ser ordenado; não é lista de objetos nem de trabalho | S39 §13.2 |
| Ranking | τ de Kendall do ranking das unidades entre vigente e rascunho, com o número de unidades. No primeiro perfil (sem vigente), τ e Δ ficam vazios e a prévia cobre só o dia corrente | S39 §13.2 |
| Tornado | ±20 % em cada ω (demais reescaladas para Σω = 1), p, τ, τ_G, ρ, β e cotas, mais cada preset; gráfico de barras horizontais e tabela | S39 §13.2, §11.6 |
| Peso efetivo do registro | Vigente e rascunho, e a linha de tornado "σ do cenário dominante ±20 %" | S39 §13.2 |
| Média pura | Valores do preset "Média pura" (p = 1, sem pisos), só para mostrar a diluição | S39 §12.3 |
| Objetos que mais se movem | Os 20 maiores \|Δ R·w\| entre versões | S39 §13.2 |
| Sobreposição | Rotulada "parcial" quando o rascunho muda âncora ou multiplicador (o replay de 90 dias não cobre isso) | S39 §9.5 |

| Comando | `Is…Enabled` | Motivo quando desabilitado | Efeito |
|---|---|---|---|
| `BtRunPreviewClicked` (`Button.type2`, `Play`) | `risk_index_configure ∧ rascunho salvo ∧ ¬IsDirty ∧ estado ∉ {Na fila, Em execução}` | "Salve o rascunho"; "Prévia em andamento" | `POST …/Preview` (202); a aba consulta o estado a cada 5 s com `ProgressRing` e percentual |
| `BtSubmitClicked` (`Button.dialog1`, `Send`) | rascunho válido no servidor (`IsValid`; `RequiresRebaseline` não impede) ∧ prévia Concluída com o hash atual ∧ justificativa não vazia (≥ 40 caracteres quando `RequiresRebaseline`) | "Corrija os erros do rascunho"; "Execute a prévia para estes parâmetros"; "Escreva a justificativa"; "Proposta de rebaseline: explique o motivo na justificativa" | `POST …/Submit`; toast "v4 enviado para aprovação"; a aba Perfis passa a mostrar "Em aprovação" |
| `BtExportPreviewClicked` (`Button.type2`, `Download`) | prévia Concluída | "Sem prévia concluída" | CSV do relatório |

Depois da submissão o rascunho fica imutável; para mudar, o aprovador rejeita e o autor cria outro
rascunho.

**Onde o relatório vive e quem o lê.** O job de prévia grava o relatório em
`risk_index_preview_requests.report`, nunca na linha do perfil (§9.6). A submissão copia o relatório da
prévia concluída cujo hash confere para `risk_index_profiles.sensitivity_report` ("prévia anexada",
S39 §13.2). O relatório traz valores da organização, de outras unidades e nomes de objetos, então só é
lido com **escopo irrestrito** (`GET /{id}/Preview`, §8.4); durante o piloto em sombra, só pelo autor e
pelos editores do rascunho (com `risk_index_configure`), por quem tem `risk_index_approve` ou pelo papel
Admin (S39 §13.3: o autor lê a prévia do próprio rascunho). A lista "objetos que mais se movem" segue a redação da §5.0.

### 6.4 Aprovação

```
┌ Aprovação ─────────────────────────────────────────────────────────────────────────────────────────┐
│ Em aprovação: v4 "Calibração T4/26" · autor A. Lima · submetido em 05/10 14:40                       │
├ Resumo ─────────────────────────────────────────────────────────────────────────────────────────────┤
│ Diferenças em relação ao vigente (v3): ω_REG 0,30 → 0,32 · ω_EXP 0,30 → 0,28   [Ver diferença completa] │
│ Justificativa: "…"                                         [Ver prévia completa (somente leitura)]  │
├ Segregação de funções (do servidor: GET …/ApprovalEligibility) ─────────────────────────────────────┤
│ ✓ Você não é autor nem editou este rascunho                                                         │
│ ✓ Você não detém business_risk_review nem é revisor de negócio nomeado                              │
│ ✓ A permissão risk_index_approve está concedida a você (papel 7 "Comitê CRTI"), não só pela flag Admin │
│ ✓ Essa concessão não foi feita por você mesmo                                                       │
├ Referências ────────────────────────────────────────────────────────────────────────────────────────┤
│ Decisão do Comitê de Risco de TI *  [Ata CRTI 2026-10, item 4       ]  Anexos * [📎 ata-crti-2026-10.pdf] [+] │
│ Aprovação do Conselho/Reitoria      [—  (exigida se as faixas mudarem ou se não há perfil vigente)]   │
│ ☐ Confirmo a aprovação explícita de rebaseline (exigida quando há gatilho: troca de preset,          │
│   |Δω| > 0,05 no trimestre ou mais de dois parâmetros alterados)                                     │
│ ⓘ Vigente a partir do snapshot de 06/10/2026 05:00 UTC; gera descontinuidade e ponte de 90 dias       │
│                                                         [✓ Aprovar]  [✕ Rejeitar]                     │
├ Última ativação — ponte v2 → v3 em 02/09 ───────────────────────────────────────────────────────────┤
│ Organização 58 → 61 (Δ da versão +2,7) · Unidade X 60 → 64 · …   sobreposição: completa   [Ver na tendência] │
├ Publicação ─────────────────────────────────────────────────────────────────────────────────────────┤
│ Estado: Piloto em sombra   [Publicar para usuários com escopo]   [Suspender publicação…]             │
└────────────────────────────────────────────────────────────────────────────────────────────────────┘
```

O bloco "Última ativação" e "Ver prévia completa" trazem valores da organização e de outras unidades:
como no diálogo do chip de perfil, só aparecem com escopo irrestrito; senão ficam visíveis e
desabilitados com "Requer escopo global" (§8.4). "Ver diferença completa", "Ver prévia completa" e
"Ver na tendência" são `Button.type3`.

**Checagens de segregação vêm do servidor.** O cliente não sabe quem editou o rascunho (autores das
linhas de auditoria), quem é revisor nomeado em `entity_risk_reviewers`, nem se a permissão do usuário
é explícita ou só a passagem do Admin (`PermissionAuthorizationHandler.cs:26-34`). Por isso a aba chama
`GET /RiskIndex/Profiles/{id}/ApprovalEligibility` (§8.4) ao abrir e depois de cada anexo, e liga a lista
de ✓/✕ e `StrApproveDisabledReason` à resposta. A chamada só é feita para quem tem `risk_index_approve`;
para quem vê a aba somente leitura (`risk_index_configure`), o bloco mostra a nota "As checagens de
segregação aparecem para o aprovador" e nenhuma chamada devolve 403. O servidor calcula a elegibilidade com o **mesmo
método** que o `Approve` usa para recusar (`RiskIndexProfilesService.EvaluateApprovalAsync`), para que os
dois nunca divirjam.

| Comando | `Is…Enabled` | Motivo quando desabilitado | Efeito |
|---|---|---|---|
| `BtApproveClicked` (`Button.dialog1`, `CheckDecagram`) | todas as checagens de `ApprovalEligibility` passam ∧ ref. do comitê ∧ ≥ 1 anexo ∧ (ref. do Conselho se exigida) ∧ (rebaseline marcado se há gatilho) | A mensagem da primeira checagem que falha (tabela abaixo) | `POST …/Approve`; toast "v4 aprovado; vigente a partir de 06/10 05:00 UTC" |
| `BtRejectClicked` (`Button.dialog2`, `Close`) | `risk_index_approve` ∧ status = Em aprovação | "Requer risk_index_approve"; "O perfil não está em aprovação" | Diálogo com motivo obrigatório; `POST …/Reject`; toast |
| `BtAttachEvidenceClicked` (`Button.subButton`, `Paperclip`) | `risk_index_approve` ∧ status = Em aprovação | "Requer risk_index_approve"; "O perfil não está em aprovação" | Upload por `FilesController` e vínculo por `POST …/{id}/Attachments` (§8.4), que só aceita arquivo enviado pelo próprio usuário e sem outro vínculo |
| `BtPublishClicked` (`Button.type2`, `Earth`) | `risk_index_approve` ∧ publicação = `shadow` ∧ perfil vigente com ref. do comitê | "Requer risk_index_approve"; "Requer perfil vigente aprovado pelo comitê" | `PUT /RiskIndex/Publication` → `published`; confirmação que lembra T258 (piloto de 8–12 semanas) |
| `BtSuspendClicked` (`Button.dialog2`, `PauseCircleOutline`) | `risk_index_approve` ∧ há perfil vigente | "Requer risk_index_approve"; "Nenhum perfil vigente" | Diálogo com justificativa e ref. do comitê; `POST …/{id}/Retire`; snapshots param até nova aprovação |

| Checagem / recusa (servidor → texto exibido) | Regra |
|---|---|
| `sod_author_cannot_approve` → "Quem criou ou editou o rascunho não pode aprová-lo." | Aprovador ≠ `created_by_id` e ≠ todo autor de linha de auditoria do rascunho; **sem bypass de administrador** (S39 §13.1) |
| `sod_business_reviewer` → "Quem detém business_risk_review não aprova os pesos que medem a própria unidade." | Recusa quem tem `business_risk_review` no conjunto **explícito** (definição abaixo) ou é revisor nomeado em `entity_risk_reviewers` (S39 §13.1). A flag de administrador sozinha não conta como deter a permissão |
| `approver_permission_not_explicit` → "A permissão risk_index_approve precisa estar concedida a você, não só pela flag de administrador." | Ver "permissão explícita" abaixo |
| `approver_role_required` → "Só o Gerente de Riscos ou o Administrador de Riscos aprova perfis." | A `risk_index_approve` tem de vir de um **papel aprovador**: um papel listado na configuração `risk_index_approver_roles` (semeada com `RiskManager` e `RiskAdministrator` pela T293; a instalação que usa papéis próprios para essas funções os acrescenta ali). Concessão direta ao usuário ou por outro papel **não** basta (S39 §13.1) |
| `sod_self_granted_approver` → "Sua permissão de aprovar foi concedida por você mesmo; peça a outro administrador." | A concessão vigente de `risk_index_approve` do aprovador (no papel ou direta) tem como autor, na trilha, o próprio aprovador |
| `committee_ref_required` / `committee_attachment_required` → "Informe a decisão do Comitê de Risco de TI e anexe a ata." | S39 §13.1 |
| `board_ref_required` → "As faixas mudaram (ou este é o primeiro perfil): informe a aprovação do Conselho/Reitoria." | S39 §4.5, §12.4 (§6.2.3) |
| `rebaseline_confirmation_required` → "Este perfil é rebaseline (‹gatilhos: troca de preset · troca de modo · ω além de ±0,05 · mais de dois parâmetros›): confirme o rebaseline." | S39 §12.4; gatilhos da §6.2.3 |
| `invalid_transition` → "O perfil não está em aprovação." | S39 §13.1 |

**Permissão explícita.** É explícita a permissão presente em
`IPermissionsService.GetUserPermissionsAsync(user)`, **lida do banco** (papel ∪ concessões diretas ao
usuário, `PermissionsService.cs:69-92`), ignorando a flag `Admin` e as claims do token. Para aprovar, a
permissão tem ainda de vir de um papel aprovador do usuário (`approver_role_required`, acima): uma
concessão direta não basta. Na aprovação, o serviço grava em `risk_index_profiles.approval_grant_source`
o papel aprovador que a sustentou (`role:{id}`, §7.1), que a auditoria do perfil registra. Para que "sem bypass de administrador" não se
contorne com um Admin que se concede a permissão, aprova e a retira, a concessão e a revogação de
`risk_index_approve`, `risk_index_configure`, `risk_index_view`, `entity_risk_context` e
`business_risk_review` (no papel ou ao usuário) passam a gravar uma linha explícita em `audit_logs`
com o autor (tipo `PermissionGrant`, pelo `IAuditTrailService`), e o `Approve` recusa
`sod_self_granted_approver` quando o autor da concessão vigente é o próprio aprovador.

**Ativação.** Aprovar torna o perfil `Active` com `effective_from` = instante do próximo snapshot
(05:00 UTC); o vigente anterior passa a `Retired` com `retired_at` = esse mesmo instante. O perfil que
vale para o dia D é o definido na §9.3 (passo 1). No primeiro dia, o job calcula a ponte (S39 §9.5) e
grava `risk_index_bridges` (§7.2); o bloco "Última ativação" lê dali.

**Perfil do piloto.** O snapshot exige um perfil aprovado (S39 §9.1: cada snapshot carrega o id do
perfil), então o piloto em sombra da T258 **começa** com a aprovação de um perfil de piloto (o primeiro
perfil, com referência do comitê que autoriza o piloto e do Conselho para as faixas) e roda com a
publicação em `shadow`. A calibração do piloto termina com a aprovação do perfil de publicação: se ele
difere do de piloto além das regras de ritmo, é aprovado como rebaseline explícito (§6.2.3). A S39
§15.2 e §13.3 definem assim o Estágio A1: perfil de piloto aprovado antes do primeiro snapshot, perfil
de publicação aprovado ao fim da calibração (o mesmo ou uma versão posterior).

**Suspender publicação** é o freio de emergência (por exemplo, defeito descoberto num parâmetro): o
vigente vai a `Retired` sem sucessor, com `retired_at` = agora, `retire_justification`,
`retire_decision_ref` e `retired_by_id` gravados em colunas próprias (§7.1), sem tocar `justification`
nem `committee_decision_ref`, que são a evidência da ativação. Os snapshots param (§9.3, passo 1) e o
painel mostra "Publicação suspensa". Volta só com nova aprovação. É a interpretação desta
especificação para o "retire" da T250.

### 6.5 Fontes e frescor

```
┌ Fontes e frescor ──────────────────────────────────────────────────────────────────────────────────┐
│ ┌ formCard.caution ⚠ ────────────────────────────────────────────────────────────────────────────┐ │
│ │ Fontes habilitadas e janelas de frescor são parâmetros do perfil. Esta aba é outra vista do     │ │
│ │ rascunho da aba Rascunho (mesmo Salvar e Descartar); sem rascunho, Salvar cria v4 a partir do   │ │
│ │ vigente. Só vale depois de aprovado, com marcador.                                              │ │
│ └─────────────────────────────────────────────────────────────────────────────────────────────────┘ │
│ Fonte    Cat.·fator  Disp.   Vigente  Rascunho  W vig.  W rasc.  Conexões  Última sinc.    Status   Objetos  Obsol. │
│ V1-DEV   EXP.D       [H]       ☑        ☑        7 d    [ 7 ]   1         05/10 03:12 ✓   ok       16.831   18% │
│ V1-CVE   EXP.V       [H]       ☑        ☑        7 d    [ 7 ]   1         05/10 03:12 ✓   ok        9.004    9% │
│ V1-IFA   EXP.X       [N]T272   ☐        ☐🔒      7 d     —      —          —              —           —      —  │
│ SSC-TOT  EXP.X       [H]       ☑        ☑        7 d    [ 7 ]   2         05/10 04:01 ✓   ok            2    0% │
│ TEN-VM   EXP.V       [N]T266   ☐        ☐🔒     30 d     —      0          —              —           —      —  │
│ TEN-AES  EXP.D       [N]T267   ☐        ☐🔒     30 d     —      0          —              —           —      —  │
│ TEN-ACR  crit.       [N]T267   ☐        ☐🔒     30 d     —      0          —              —           —      —  │
│ TEN-NES  EXP.V       [H]       ☑        ☑       30 d    [30 ]   —         import 03/10    —        1.120   22% │
│ INC      AME.I       [H]       ☑        ☑        —       —      —         intake ativo    ok          184    —  │
│ ASM      CTL.A       [C]D-16   ☑        ☑      365 d   [365]    —          —              experim.     37    5% │
│ …                                                                                                  │
├ Execuções do índice ───────────────────────────────────────────────────────────────────────────────┤
│ Snapshot 05/10 05:00–05:07 UTC ✓ · 35.210 linhas de objeto · 3.731 linhas de nó                      │
│ Retenção 05/10 02:40 ✓ · 34.880 linhas expurgadas      Prévia v4 ✓ 14:31                              │
└───────────────────────────────────────────────────────────────────────────────────────────────────┘
```

| Coluna | Origem |
|---|---|
| Fonte, categoria, fator, disponibilidade [H]/[C]/[N] com o defeito ou tarefa | Catálogo da S39 §3.2 no servidor, com a disponibilidade **da versão instalada**: o [C] de uma fonte passa a [H] quando a tarefa que corrige o defeito entra (por exemplo, V1-DEV depois da T237) |
| Habilitada (vigente / rascunho) | `fontes_habilitadas` dos dois perfis |
| W (vigente / rascunho) | `frescor_W` |
| Conexões, última sincronização, status | Conexões Vision One, SSC e Tenable; `integration_sync_logs`; para importadores, o último `scan_imports` |
| Objetos e % obsoletos | Último snapshot. **Só com escopo irrestrito**: são contagens da organização inteira e revelariam o tamanho de inventários fora do escopo (S39 §10.4). Para os demais, as duas colunas mostram "—" com a dica "Requer escopo global" |
| TEN-ACR e TEN-AES | Estão no catálogo como as demais fontes. Coletar ACR e AES na conexão Tenable (§6.8) só guarda as leituras em `vendor_scores`; o motor só as usa quando TEN-ACR (criticidade, S39 §5.1) ou TEN-AES (EXP.D) está em `fontes_habilitadas` do perfil do snapshot. Habilitar é nova versão, com marcador |

| Comando | `Is…Enabled` | Motivo | Efeito |
|---|---|---|---|
| Marcar/desmarcar fonte, editar W | `risk_index_configure` ∧ fonte não [N] ∧ nenhum perfil Em aprovação | "Requer ‹tarefa›" para [N]; "Requer risk_index_configure"; "v{n} está em aprovação; rejeite-a ou aguarde" | Altera a `RiskProfileDraftSession` (§6.1); o grupo 5 do editor fica marcado como alterado |
| `BtSaveDraftClicked` (`Button.dialog1`, `ContentSave`) — **o mesmo comando da aba Rascunho** | `IsDirty ∧ ¬IsBusy` ∧ nenhum perfil Em aprovação | "Sem alterações"; "v{n} está em aprovação; rejeite-a ou aguarde" | Se não há rascunho, confirma "Criar o rascunho v{n+1} a partir do vigente?" e cria; depois **um** `PUT` com `If-Match: <revision>` de todos os parâmetros alterados nas duas abas; toast |
| `BtResetClicked` (`Button.dialog2`, `Restore`) — o mesmo da aba Rascunho | `IsDirty` | "Sem alterações" | Descarta as alterações das duas abas, com confirmação |

Uma fonte [C] pode ser habilitada, com o chip "requer correção D-xx (S39 §14)" ao lado. A publicação
(`shadow`/`published`) fica na aba Aprovação (§6.4). Como as duas abas editam a mesma sessão, não há
dois `IsDirty` nem dois `PUT`, e o `409 stale_draft` contra a própria gravação anterior não acontece; a
habilitação dos controles com perfil Em aprovação é testada em `RiskSourcesEnablementTest` (§12.4).

**Execuções do índice** (`GET /RiskIndex/Runs`) trazem contagens de linhas da organização inteira e só
aparecem com escopo irrestrito; para os demais, o bloco fica desabilitado com "Requer escopo global".

### 6.6 Mapeamentos fornecedor → entidade

**Onde:** nova aba **Mapeamento de entidades** em Integrações (`IntegrationsView`), com
`EntityMappingsViewModel` exposto como `IntegrationsViewModel.EntityMappings`, sob a permissão
`configuration` na API, como as demais integrações (na GUI, o painel Integrações continua só de
administrador, §6.0). Não fica no painel do índice porque muda a **atribuição
e o escopo** de hosts e objetos (quem os enxerga), o que é configuração de integração, e porque a
regra se aplica na sincronização da conexão.

```
┌ Mapeamento de entidades ───────────────────────────────────────────────────────────────────────────┐
│ Provedor [Vision One ▾]  Conexão [Tenant FGV ▾]                                         [+] [⟳]    │
│ ┌ Prior. ┬ Tipo ─────────────────┬ Valor ──────────────────┬ Entidade ───────────┬ Recarimbo ─────────┬ Hab. ┐ │
│ │ 1      │ Tag personalizada V1  │ BU: Escola de Direito   │ Escola de Direito   │ só não atribuídos  │ ☑    │ │
│ │ 2      │ Tag personalizada V1  │ Site: Botafogo          │ Campus Botafogo     │ seguir mapeamento  │ ☑    │ │
│ │ 3      │ Grupo de ativos V1    │ Servidores Acadêmicos   │ Unidade X           │ (indicador)        │ ☑    │ │
│ └────────┴───────────────────────┴─────────────────────────┴─────────────────────┴────────────────────┴──────┘ │
│ ┌ formCard: CORRESPONDÊNCIA ────────────────┐ ┌ formCard: DESTINO ─────────────────────────────────┐ │
│ │ Tipo [Tag personalizada V1 ▾]             │ │ Entidade [Escola de Direito ▾]                      │ │
│ │ Valor [BU: Escola de Direito ▾] (da última│ │ Recarimbo [Só não atribuídos ▾]                     │ │
│ │ sincronização de tags)                    │ │ Prioridade [1]   ☑ Habilitado                       │ │
│ └───────────────────────────────────────────┘ └─────────────────────────────────────────────────────┘ │
│ ┌ formCard: PRÉVIA ─────────────────────────────────────────────────────────────────────────────────┐ │
│ │ Correspondem 412 objetos · 37 seriam atribuídos (hoje sem entidade) · 12 seriam reatribuídos       │ │
│ │ (outra entidade) · 3 em conflito com a regra 2 (vence a menor prioridade) · 360 já atribuídos      │ │
│ │ ⓘ Aplicado na próxima sincronização da conexão. Reatribuir muda quem enxerga o host.               │ │
│ └───────────────────────────────────────────────────────────────────────────────────────────────────┘ │
│ [💾 Salvar] [🗑 Excluir]                                                                              │
└───────────────────────────────────────────────────────────────────────────────────────────────────┘
```

| Tipo de regra | Efeito | Tarefa |
|---|---|---|
| **Tag personalizada Vision One** (`assetCustomTags` do dispositivo e dos objetos de superfície) | Atribui o host ou objeto à entidade | T263 |
| **Grupo de ativos Vision One** (`assetGroups`) | **Não atribui dispositivos**: associa o subíndice do grupo ao nó como indicador de referência (§5.2.4). A API não devolve a pertença de dispositivo a grupo; a pertença vem das tags | T263, T271 |
| **Tag Tenable** (`categoria:valor` do registro de ativo) | Atribui o host à entidade | T268 |
| **Domínio SSC → entidade** | É a própria `securityscorecard_connections.entity_id`, agora com seletor no formulário da conexão (§6.9) | T263 |
| **Entidade padrão da conexão** | `entity_id` das conexões Vision One, Tenable e SSC, com seletor (§6.9). Vale quando nenhuma regra casa | T263, T265 |

**Regras de aplicação**

- Ordem: regra habilitada de menor prioridade que casa; depois a entidade padrão da conexão; senão,
  "Não atribuído" (S39 §10.2).
- **Política de recarimbo** (a política explícita que a D-20 pede, T282): *só não atribuídos* (padrão:
  grava `entity_id` apenas em host com `entity_id` nulo) ou *seguir o mapeamento* (regrava sempre que a
  regra aponta outra entidade). Toda regravação passa pela auditoria de `Host`, que já está na
  allowlist (`GovernanceAuditInterceptor.cs:48-61`).
- A regra é aplicada na sincronização seguinte da conexão (manual ou agendada), dentro do mesmo job,
  com contagens no `integration_sync_logs` ("12 hosts reatribuídos por regra").
- Valor das regras: `ComboBox` com os candidatos vistos na última sincronização
  (`GET …/Candidates`), mais digitação livre para tags ainda não vistas.

| Comando | `Is…Enabled` | Motivo | Efeito |
|---|---|---|---|
| `BtNewMappingClicked` (`Button.operation`, `Plus`) | conexão escolhida | "Escolha uma conexão" | Linha nova em edição |
| Prévia (automática, 500 ms depois da última edição; não é botão) | regra completa | O cartão diz "Complete tipo, valor e entidade para ver a prévia" | `POST …/Preview`; atualiza o cartão de prévia |
| `BtSaveMappingClicked` (`Button.dialog1`, `ContentSave`) | `IsDirty` ∧ regra completa ∧ sem duplicata | "Já existe regra para este valor nesta conexão" | `POST`/`PUT`; toast "Regra salva; aplicada na próxima sincronização" |
| `BtDeleteMappingClicked` (`Button.dialog2`, `Delete`) | regra selecionada | "Selecione uma regra" | `ConfirmDeleteAsync`; os hosts **mantêm** a entidade atual |

### 6.7 Contexto de risco das entidades

**Decisão: um painel novo, aba "Contexto de risco" do painel Índice de risco — não a tela Entidades.**

1. **Uma tarefa, uma interface (IX-5).** `criticality`, `internetFacing` e `securityClassification`
   passam a ter permissão própria (T241). Se continuassem editáveis no formulário genérico de
   entidade, haveria dois editores para o mesmo dado com regras diferentes. Na tela Entidades eles
   ficam **somente leitura**, com a dica "Editado em Administração › Índice de risco › Contexto de
   risco (requer entity_risk_context)". Mecanismo: o formulário já é XAML sobre `EntityFieldViewModel`
   (`EntityForm.axaml:13-50`), que hoje não tem estado somente leitura; ganha `IsReadOnly` e
   `ReadOnlyReason`, preenchidos para `criticality`, `internetFacing` e `securityClassification` pelo
   helper puro `Tools/Entities/RiskContextFieldPolicy.cs` (sempre, nesta tela). Cada `DataTemplate` do
   `EntityForm.axaml` liga `IsEnabled="{Binding !IsReadOnly}"` no controle e mostra
   `TextBlock.hint` com `ReadOnlyReason` abaixo dele. O helper é testado em `GUIClient.Tests`
   (`RiskContextFieldPolicyTest`, §12.4).
2. **Edição em lote com justificativa.** Calibrar criticidade é uma revisão de muitos processos e
   aplicações de uma vez, comparando lado a lado; o formulário de entidade edita uma entidade por vez.
3. **Histórico com motivo.** Toda mudança move pesos (S39 §4.6, exceção e) e é um vetor de Goodhart;
   o painel mostra a trilha de auditoria com a justificativa de cada lote.
4. **A trava é no servidor.** `POST /Entities` e `PUT /Entities/{id}` passam a recusar valor ou
   mudança dessas três propriedades sem `entity_risk_context` (T241, §8.5). A tela somente leitura é
   cortesia; a regra é a API.
5. **Escopo global para escrever.** Uma criticidade de processo move todas as unidades que o processo
   integra e todos os objetos que ele serve (S39 §5.2), e um processo pode estar em várias unidades;
   mesmo com o escopo hierárquico (T292), uma checagem pela subárvore de quem edita não conteria o
   efeito nas outras unidades. Por isso a escrita de contexto
   exige escopo irrestrito (`403 risk_context_requires_global_scope`); a leitura segue o mapa de
   entidades, visível a todo usuário autenticado. Gestores de unidade **não** editam o contexto por
   ora, porque ainda não usam o sistema (decisão de 2026-10-05). **Previsão para o futuro (T295,
   backlog):** a checagem vive numa única política (`RiskContextWritePolicy`, chamada por
   `EntitiesService` e pelo lote de `PUT /Entities/RiskContext`), de modo que a regra futura (permitir
   a quem tem a subárvore da entidade, e exigir escopo global quando a entidade é um processo
   compartilhado por mais de uma unidade) entra nessa política sem mudar a API nem a tela.

```
┌ Contexto de risco ─────────────────────────────────────────────────────────────────────────────────┐
│ [🔍 entidade…]  Tipo [Todos ▾]  ☐ Só sem criticidade declarada              [+] Nova atividade [⟳]  │
│ ┌ Entidade ─────────────────────┬ Tipo ─────────┬ Criticidade ┬ Origem ────────────┬ Internet ┬ Classificação ──┬ Última alteração ───────┐ │
│ │ ▾ Unidade X                   │ Unidade       │ [3 ▾]       │ declarada          │   —      │  —              │ M. Souza 12/09           │ │
│ │   ▾ P-Matrícula               │ Processo      │ [5 ▾] ●     │ declarada, não BIA │   —      │  —              │ M. Souza 12/09           │ │
│ │       • Emissão de diploma    │ Atividade     │  n/a        │ —                  │   —      │  —              │ —                        │ │
│ │   ▸ P-Pesquisa                │ Processo      │ [3 ▾]       │ declarada, não BIA │   —      │  —              │ —                        │ │
│ │   APP-03 Portal do aluno      │ Aplicação     │ [5 ▾]       │ declarada          │  [☑]     │ [Restrita ▾]    │ M. Souza 12/09           │ │
│ └───────────────────────────────┴───────────────┴─────────────┴────────────────────┴──────────┴─────────────────┴──────────────────────────┘ │
│ ● = alterado nesta sessão (2)                                   [💾 Salvar alterações…] [↺ Descartar]  │
├ Histórico — P-Matrícula ───────────────────────────────────────────────────────────────────────────┤
│ 12/09/2026 10:41 · M. Souza · criticality 4 → 5 · "Processo de matrícula declarado crítico pela      │
│ Reitoria na revisão do BIA preliminar (memorando 2026/77)"                                           │
└───────────────────────────────────────────────────────────────────────────────────────────────────┘
```

| Elemento | Regra |
|---|---|
| Árvore | `TreeDataGrid` com os tipos organization, organizationUnit, subOrganizationUnit, businessProcess (com as atividades como filhas), application. Processos aparecem sob cada unidade da propriedade `organizationUnit` (podem aparecer mais de uma vez; a edição é a mesma linha) |
| Criticidade | `ComboBox` "Não declarada", 1…5 com rótulos (S38 §3.5). Só onde a S39 §5.2 põe a propriedade: businessProcess, application, organizationUnit e subOrganizationUnit. Nos demais tipos (organization, activity) a célula mostra "n/a"; uma atividade como entidade-alvo usa o padrão (S39 §5.4) |
| Origem | "declarada, não BIA" no interino; "BIA (M41)" quando o M41 fornecer — então a célula fica somente leitura (S39 §5.2) |
| Internet-facing | `CheckBox` só em application (S39 §5.3) |
| Classificação | `ComboBox` de `securityClassificationLevel`, em application (e organizationData quando for exibido) |
| Salvar | `BtSaveContextClicked` (`Button.dialog1`, `ContentSave`): `IsDirty ∧ entity_risk_context ∧ IsGlobalScope`; desabilitado com "Sem alterações", "Requer entity_risk_context" ou "Requer escopo global". Abre um diálogo de **justificativa obrigatória** (mínimo 20 caracteres) que mostra a lista do que muda e envia **um** `PUT /Entities/RiskContext` com o lote inteiro (§8.5), aplicado num único `SaveChanges`: tudo ou nada, um `CorrelationId` e uma nota de justificativa. Sucesso: toast "N entidades atualizadas; o efeito aparece no próximo snapshot como efeito de contexto". Falha: nenhuma entidade muda, as linhas continuam marcadas como alteradas e o erro aparece por `ExplainError` (com a linha recusada, quando o servidor a nomeia) |
| Descartar | `BtDiscardContextClicked` (`Button.dialog2`, `Restore`): `IsDirty`; desabilitado com "Sem alterações" |
| Nova atividade | `BtNewActivityClicked` (`Button.operation`, `Plus`): processo selecionado ∧ `entities_manage` (a permissão de CRUD de entidades da T241, §11.1); desabilitado com "Selecione um processo" ou "Requer entities_manage". Abre o `EditEntityDialog` existente com tipo `activity` e pai = processo (um editor por objeto, IX-5) |
| Recarregar | `BtRefreshContextClicked` (`Button.operation`, `Reload`): `¬IsBusy ∧ ¬IsDirty`; desabilitado com "Carregando…" ou "Salve ou descarte as alterações antes de recarregar" |
| Histórico | `GET /Entities/{id}/History` (linhas de auditoria agrupadas por `CorrelationId`, com a justificativa) |
| Sem permissão | Grade somente leitura; Salvar desabilitado com "Requer entity_risk_context" |

### 6.8 Conexão Tenable (aba em Integrações)

Nova aba **Tenable** em `IntegrationsView`, com `TenableIntegrationViewModel` exposto como
`IntegrationsViewModel.Tenable` (o `IntegrationsViewModel` já tem 2.565 linhas; o padrão de sub-VM já
existe em `Jira`). O layout espelha a aba de postura (`IntegrationsView.axaml:858-1150`).

```
┌ Tenable ───────────────────────────────────────────────────────────────────────────────────────────┐
│ Conexões  [+] [⟳]                                                                                   │
│ ┌ Nome ───────────┬ Produto ─────────────────┬ URL base ──────────────────┬ Última sinc. ┬ Status ┐ │
│ │ Tenable FGV     │ Vulnerability Management │ https://cloud.tenable.com  │ 05/10 01:58  │ ✓      │ │
│ └─────────────────┴──────────────────────────┴────────────────────────────┴──────────────┴────────┘ │
│ ┌ CONEXÃO ─────────────────────────────────┐ ┌ AUTENTICAÇÃO ─────────────────────────────────────┐ │
│ │ Nome [Tenable FGV               ]        │ │ Access key [•••••••••••] [🔑] [⊘]                  │ │
│ │ Produto [Vulnerability Management ▾]     │ │ Secret key [•••••••••••] [🔑] [⊘]                  │ │
│ │ URL base [https://cloud.tenable.com] 🔒   │ │ 🛡 vault: cofre-prod › tenable/netrisk#access      │ │
│ │ Entidade padrão [Organização FGV ▾]      │ │ ⓘ Use um usuário dedicado à integração (Tenable).  │ │
│ └──────────────────────────────────────────┘ └────────────────────────────────────────────────────┘ │
│ ┌ SINCRONIZAÇÃO ───────────────────────────┐ ┌ MAPEAMENTO ───────────────────────────────────────┐ │
│ │ Intervalo (h) [24]                        │ │ 4 regras de tag → entidade                         │ │
│ │ ☑ Sincronizar achados (export de vulns)   │ │ [Abrir mapeamentos]                                │ │
│ │ ☑ Sincronizar ativos (export de ativos)   │ └────────────────────────────────────────────────────┘ │
│ │ ☑ Coletar ACR                             │ ┌ formCard.caution (Security Center, T269) ⚠ ───────┐ │
│ │ ☑ Coletar AES                             │ │ ☐ Aceitar certificado TLS inválido                 │ │
│ │ ⓘ O uso de ACR e AES no índice depende do  │ └────────────────────────────────────────────────────┘ │
│ │   perfil aprovado (Fontes e frescor)      │                                                        │
│ │ Severidade mínima [Baixa ▾]               │                                                        │
│ │ Janela inicial (dias) [90]                │                                                        │
│ │ ☐ Incluir ativos não licenciados          │                                                        │
│ │ ☑ Habilitada                              │                                                        │
│ └──────────────────────────────────────────┘                                                         │
│ [💾] [⇄ Testar] [⟳ Sincronizar agora] [🗑]                                                           │
├ Log de sincronização (Tenable) ────────────────────────────────────────────────────────────────────┤
│ Conexão · Status · Início · Resumo        | trilha de progresso (monoespaçada, selecionável)        │
└───────────────────────────────────────────────────────────────────────────────────────────────────┘
```

| Campo | Regra |
|---|---|
| Produto | No M56, só **Vulnerability Management** (nuvem), que é o produto da organização, com licença Tenable One (decisão de 2026-10-05): URL fixa `https://cloud.tenable.com`, somente leitura, e o seletor fica oculto. **Security Center** entra com a T269 (backlog): URL https obrigatória, editável; destino privado permitido pela política de SSRF salvo `Integrations:BlockPrivateNetworks`, quando entra em `Integrations:AllowedPrivateHosts` (`OutboundUrlPolicy.cs:28-46`) |
| Entidade padrão | Seletor de entidade; vazio = "Não atribuído", com `notice` "ativos sem regra de mapeamento ficarão em Não atribuído" |
| Access key / secret key | Dois campos com o mesmo seletor de cofre do Vision One (`VaultSecretFieldState`); nunca devolvidos ao cliente (só `HasAccessKey`, `HasSecretKey` e as referências de cofre). O botão ⊘ (`Button.subButton`, `CloseCircleOutline`) marca `ClearAccessKey`/`ClearSecretKey` no `TenableConnectionUpsert` (§8.3); chave omitida sem a marca mantém a salva |
| Intervalo | 1–168 h, padrão 24. O `TenableSyncJob` roda a cada 15 min e sincroniza a conexão quando `agora ≥ início da última execução + intervalo − 15 min` (contado do **início**, não do fim, para não repetir o defeito D-13 da T276; a tolerância de um tique impede o deslizamento diário), §9.1 |
| Achados / ativos | Liga o export de vulnerabilidades (T266) e o de ativos (T267) |
| Coletar ACR / Coletar AES | **Só controlam a coleta**: com a opção ligada, a sincronização guarda `ratings.acr.score` e `ratings.aes.score` em `vendor_scores` (TenableAcr, TenableAes). A sincronização **nunca** grava `hosts.criticality` nem `hosts.criticality_source` a partir do ACR. O motor usa a leitura só quando TEN-ACR (criticidade, na precedência da S39 §5.1) ou TEN-AES (EXP.D) está em `fontes_habilitadas` do perfil do snapshot (§6.5), o que exige nova versão aprovada. Sem isso, nenhum `configuration` move o peso `w_o = m(crit)` (S39 §5.1, §4.6 e) sem rascunho, aprovação e a permissão de contexto (S39 I8). Exigem licença Tenable One ou Lumin, que a organização tem (2026-10-05); se a licença faltar numa instalação, os campos vêm vazios e a sincronização registra "ACR/AES ausentes: verifique a licença" (§15) |
| Severidade mínima | Info, Baixa, Média, Alta, Crítica; padrão Baixa |
| Janela inicial | 30–365 dias, padrão 90: o `since` do primeiro export (sem filtro de tempo a Tenable devolve só 30 dias) |
| Não licenciados | `include_unlicensed`, padrão desligado |
| Certificado inválido | Só Security Center (T269, backlog), em `formCard.caution` com `MaterialIcon.cautionIcon`; não aparece no M56 |

| Comando | `Is…Enabled` | Motivo | Efeito |
|---|---|---|---|
| `BtNewTenableClicked` (`Button.subButton`, `Plus`) | `¬IsDirty` | "Salve ou descarte a conexão em edição" | Rascunho vazio |
| `BtSaveTenableClicked` (`Button.dialog1`, `ContentSave`) | `IsDirty ∧ nome ∧ credenciais (digitadas, do cofre ou já salvas)` | "Informe nome e credenciais" | `POST`/`PUT`; toast "Conexão salva" |
| `BtTestTenableClicked` (`Button.dialog2`, `LanConnect`) | conexão salva | "Salve antes de testar" | `POST …/test`; toast com o resultado por etapa (§9.4) |
| `BtSyncTenableClicked` (`Button.dialog2`, `Sync`) | conexão salva ∧ habilitada ∧ sem execução na fila ou em curso | "Salve antes de sincronizar"; "Conexão desabilitada"; "Já há uma sincronização na fila ou em execução" | `POST …/sync` (202): grava o pedido na fila (§8.7); o `TenableSyncJob` o executa no próximo tique, no host de jobs; o log mostra "Na fila" e depois a execução |
| `BtOpenMappingsClicked` (`Button.type3`, `TagArrowRight`) | conexão salva | "Salve a conexão primeiro" | Vai para a aba de mapeamento filtrada pela conexão |
| `BtDeleteTenableClicked` (`Button.dialog2`, `Delete`) | conexão selecionada | "Selecione uma conexão" | `ConfirmDeleteAsync` ("os achados e hosts importados permanecem"); toast. Último da linha de ações |

### 6.9 Opções novas em Vision One e SecurityScorecard

Na aba de postura existente (`IntegrationsView.axaml:858-1150`):

| Conexão | Cartão | Campo novo | Tarefa |
|---|---|---|---|
| Vision One | Conexão | **Entidade padrão** (seletor; hoje inexistente na tela) | T263 |
| Vision One | Sincronização | ☐ Postura e grupos de ativos (CRI e subíndices, **indicadores**) | T271 |
| Vision One | Sincronização | ☐ Ativos internet-facing, contas, nuvem e apps locais (**objetos pontuados**; exige créditos Flex alocados ao CREM) | T272 |
| Vision One | Sincronização | ☐ Alertas do Workbench (**indicador** de intensidade de ataque; exige permissão de Workbench) | T273 |
| Vision One | Sincronização | ☐ Tags personalizadas (necessário para o mapeamento por tag) | T263 |
| SecurityScorecard | Conexão | **Entidade** (seletor) | T263 |
| SecurityScorecard | Sincronização | Recuperar histórico de score (dias) `NumericUpDown` 0–365, padrão 365 | T274 |
| SecurityScorecard | Sincronização | ☐ Guardar impacto no score por tipo de issue | T274 |

O teste de conexão do Vision One passa a sondar cada endpoint habilitado e a nomear o que falta
(permissão "Third-party auditing (API only)" para `securityPosture`, créditos CREM, permissão de
Workbench) em vez de uma falha genérica.

---

## 7. Modelo de dados

Convenções do Track 6 em tudo o que é novo: tabelas e colunas `snake_case` (C# em PascalCase via
`HasColumnName`), `created_at` NOT NULL e `updated_at` NULL em UTC, booleanos `tinyint(1)`, enums `int`
com `HasConversion`, `varchar(n)` (nunca `char(n)`), `fk_<tabela>_<coluna>`, `idx_…`, `uq_…`. O mapeamento
EF fica em `src/DAL/Context/NRDbContext.RiskIndex.cs` e `NRDbContext.Integrations.cs`, chamados por
`OnModelCreatingPartial`.

### 7.1 Tabelas da S39 §12.5 (referência) e o que esta especificação acrescenta a elas

As três tabelas `risk_index_profiles`, `risk_index_snapshots` e `risk_index_object_days` são
**exatamente** as da S39 §12.5 (colunas, tipos, chaves únicas, enum de status, regras de
imutabilidade).

**Detalhes de implementação de colunas que já estão na S39 §12.5** (não são colunas novas; não as
declare duas vezes):

| Tabela | Coluna da S39 §12.5 e detalhe de implementação | Por quê |
|---|---|---|
| `risk_index_profiles` | `revision int NOT NULL DEFAULT 0` (token de concorrência do EF, `IsConcurrencyToken`, incrementado a cada `PUT`) | `If-Match` exato: `updated_at` tem precisão de segundo (§6.2.1) |
| `risk_index_profiles` | `retire_justification TEXT NULL`, `retire_decision_ref varchar(200) NULL`, `retired_by_id int NULL` (`fk_risk_index_profiles_retired_by_id` → `users`) | A suspensão (Active → Retired sem sucessor) precisa de motivo e referência próprios sem sobrescrever `justification` e `committee_decision_ref`, que são a evidência da ativação (§6.4) |
| `risk_index_profiles` | `approval_grant_source varchar(32) NULL` (`role:{id}` do papel aprovador; concessão direta não aprova) | Proveniência da permissão explícita do aprovador (§6.4, §11.4) |

**Acréscimos** desta especificação:

| Tabela | Acréscimo | Por quê |
|---|---|---|
| `risk_index_object_days` | `id bigint` PK auto-incremento (a chave única da S39 continua) | Chave simples para o EF numa tabela só de inserção |
| `risk_index_object_days` | `asset_class int NULL` (enum da S39 §5.4), `source_mask int NOT NULL DEFAULT 0` (um bit por fonte da S39 §3.2 com leitura no objeto) e `environment varchar(32) NULL` (ambiente normalizado do host pelo vocabulário `ambientes_nao_producao` do perfil: o termo do vocabulário, `prod` ou `none`; nulo para objetos que não são host) | Filtros por classe, fonte e ambiente (§5.5, §5.6.1–§5.6.2) sem ler JSON; o ambiente é a dimensão da S39 §2.2 e da CMDB-ENV (§3.2) |
| `risk_index_object_days` | `idx_risk_index_object_days_date_profile_category` (`snapshot_date`, `profile_id`, `category`); `idx_risk_index_object_days_entity_id`; `idx_risk_index_object_days_date_class` (`snapshot_date`, `asset_class`) | Leitura por dia e nó |
| `risk_index_snapshots` | `idx_risk_index_snapshots_scope_category_date` (`scope_kind`, `scope_ref`, `category`, `snapshot_date`); `idx_risk_index_snapshots_entity_id` | Tendência por nó |
| `risk_index_snapshots` | `top_contributions` preenchido **só na linha `category = 0`** (as partes por categoria são recalculadas das linhas de objeto, que vivem 400 dias) | Volume (§7.7) |

**Sem FK de `entity_id`** nas tabelas de snapshot, objeto e atribuição: são históricas e só de
inserção, e precisam sobreviver à exclusão de uma entidade (que hoje cascateia, `NRDbContext.cs:808-811`).
`profile_id` tem FK.

### 7.2 Tabelas novas

| Tabela | Colunas (além de `id`, `created_at`, `updated_at` quando houver) | Chaves e índices | Tarefa |
|---|---|---|---|
| `risk_index_object_attributions` | `snapshot_date` date · `profile_id` int FK · `object_kind` int · `object_ref` varchar(64) · `entity_id` int NULL (entidade de atribuição; nulo = Não atribuído) · `scope_entity_id` int NULL (entidade de escopo da linha de origem) | `idx_risk_index_object_attributions_date_profile_entity`; filtro de escopo em `scope_entity_id` | T246 |
| `risk_index_node_members` | `snapshot_date` · `profile_id` FK · `node_entity_id` int · `member_entity_id` int · `member_kind` int (Closure 1: entidade do fecho; Scope 2: entidade de escopo de algum objeto atribuído ao nó naquele dia) | `idx_risk_index_node_members_date_profile_node` | T246 |
| `risk_index_class_days` | `snapshot_date` date · `profile_id` FK · `node_entity_id` int NULL (nulo = raiz) · `tab_group` int (Devices 1, InternetFacing 2, Identities 3, Applications 4, Clouds 5) · `category` int · `value` double | `uq_risk_index_class_days_date_profile_node_tab_category`; `idx_risk_index_class_days_node_tab_date`; filtro de escopo em `node_entity_id` como em `risk_index_snapshots` | T247, T253 |
| `risk_index_bridges` | `from_profile_id` FK · `to_profile_id` FK · `bridge_date` date · `scope_kind` int · `scope_ref` varchar(64) · `entity_id` int NULL · `category` int · `value_old` double · `value_new` double · `overlay` LONGTEXT (JSON, 90 pontos) · `overlay_partial` tinyint(1) | `uq_risk_index_bridges_to_scope_category`; filtro em `entity_id` | T250 |
| `risk_index_preview_requests` | `profile_id` FK **ON DELETE CASCADE** (`fk_risk_index_preview_requests_profile_id`; excluir um rascunho leva as prévias) · `parameters_hash` varchar(64) · `status` int (Queued 1, Running 2, Completed 3, Failed 4) · `progress` int · `requested_by_id` FK `users` · `started_at` NULL · `heartbeat_at` datetime NULL (atualizado a cada avanço de `progress`) · `finished_at` NULL · `error_message` TEXT NULL · `report` LONGTEXT NULL (JSON do relatório, §9.6) | `idx_risk_index_preview_requests_status` | T250, T261 |
| `risk_index_annotations` | `annotation_date` date · `kind` int (VendorModel 1, VendorRecalibration 2, VendorBreachPenalty 3, Note 4) · `source` varchar(32) · `label` varchar(200) · `details` TEXT NULL · `created_by_id` int NULL FK `users` | `idx_risk_index_annotations_date` | T252, T274 |
| `entity_risk_context_notes` | `entity_id` FK `entities` ON DELETE CASCADE · `batch_id` varchar(32) · `justification` TEXT · `created_by_id` FK `users` | `idx_entity_risk_context_notes_entity_id` | T264 |
| `vendor_assets` | `provider` int (`IntegrationKind`) · `connection_id` int · `asset_kind` int (Fqdn 1, PublicIp 2, DomainAccount 3, ServiceAccount 4, CloudAsset 5, LocalApp 6) · `external_id` varchar(255) · `name` varchar(255) · `criticality` tinyint NULL · `criticality_raw` varchar(32) NULL · `entity_id` int NULL FK ON DELETE SET NULL · `tags` TEXT NULL (JSON) · `first_seen_at` · `last_seen_at` · `status` int (Active 1, Gone 2) | `uq_vendor_assets_provider_connection_kind_external_id`; filtro de escopo em `entity_id` | T272 |
| `vendor_scores` | `source` int (V1Device 1, TenableAes 2, TenableAcr 3, V1Cri 4, V1AssetGroup 5, V1SecurityConfig 6, V1AttackIntensity 7, TenableCes 8, V1VendorAsset 9) · `connection_id` int · `object_kind` int (Host 1, VendorAsset 2, Tenant 3, AssetGroup 4) · `host_id` int NULL FK ON DELETE CASCADE · `vendor_asset_id` bigint NULL FK ON DELETE CASCADE · `external_ref` varchar(128) NULL · `entity_id` int NULL · `value` double · `scale_max` double · `level` varchar(16) NULL · `details` LONGTEXT NULL · `first_captured_at` · `last_captured_at` | `idx_vendor_scores_source_host`, `idx_vendor_scores_source_asset`, `idx_vendor_scores_source_ref`; filtro em `entity_id` | T270 |
| `host_external_ids` | `host_id` FK ON DELETE CASCADE · `provider` varchar(64) · `external_id` varchar(255) · `tags` TEXT NULL (JSON) · `first_seen_at` · `last_seen_at` | `uq_host_external_ids_provider_external_id`; filtro pelo host | T267, T275 |
| `vendor_entity_mappings` | `provider` int · `connection_id` int · `match_kind` int (V1CustomTag 1, V1AssetGroup 2, TenableTag 3) · `match_value` varchar(255) · `match_label` varchar(255) NULL · `entity_id` FK ON DELETE CASCADE · `priority` int · `restamp_policy` int (OnlyUnassigned 1, FollowMapping 2) · `enabled` tinyint(1) · `created_by_id` FK `users` | `uq_vendor_entity_mappings_provider_connection_kind_value` | T263, T268 |
| `tenable_connections` | `name` varchar(120) · `product` int (VulnerabilityManagement 1, SecurityCenter 2) · `base_url` varchar(255) · `encrypted_access_key` text NULL · `encrypted_secret_key` text NULL · `entity_id` FK NULL ON DELETE SET NULL · `enabled` · `sync_interval_hours` int · `sync_findings` · `sync_assets` · `collect_acr` · `collect_aes` · `min_severity` int · `initial_window_days` int · `include_unlicensed` · `allow_invalid_certificate` · `findings_cursor` datetime NULL · `assets_cursor` datetime NULL · `last_sync_at` NULL · `last_sync_status` int NULL · `last_sync_error` TEXT NULL | `idx_tenable_connections_entity_id` | T265 |
| `security_scorecard_issue_summaries` | `factor_id` FK `security_scorecard_factors` ON DELETE CASCADE · `issue_type` varchar(128) · `severity` varchar(16) NULL · `issue_count` int NULL · `total_score_impact` double NULL | `idx_security_scorecard_issue_summaries_factor_id` | T274 |

`vendor_scores` é **por trecho constante**: a sincronização grava uma linha nova quando o valor muda e,
quando não muda, só avança `last_captured_at`. Assim a tabela guarda histórico (o valor em qualquer
data) e frescor (a última leitura) sem uma linha por dispositivo por dia. A "leitura vigente" da S39
§8.2 é a linha de maior `last_captured_at` do objeto.

`risk_index_class_days` guarda, para a raiz e cada nó pré-calculado de entidade, o `C_k` diário do nó ∩
cada grupo de aba de classe. É o mesmo cálculo "oficial derivado" das linhas de objeto (S39 §9.1),
feito uma vez no job em vez de a cada leitura: o resumo mensal das abas (§5.5.1) lê 6 a 12 meses
desses valores sem varrer milhões de linhas de objeto (§8.1, orçamento de cálculo derivado). Não muda
nenhum valor nem a tabela de snapshots da S39 §12.5.

### 7.3 Colunas novas em tabelas existentes

| Coluna | Tipo | Tarefa |
|---|---|---|
| `nr_files.risk_index_profile_id` | int NULL, `fk_nr_files_risk_index_profile_id` ON DELETE SET NULL (precedente: `risk_acceptance_id`, `NRDbContext.Aspm.cs:55-59`). O `FileAccessAuthorizer` ganha o ramo correspondente (§11.3) | T262 (anexo da decisão do comitê) |
| `trendmicro_connections.sync_posture`, `sync_attack_surface_objects`, `sync_workbench_alerts`, `sync_custom_tags` | tinyint(1) NOT NULL DEFAULT 0 | T271–T273, T263 |
| `securityscorecard_connections.history_backfill_days` | int NOT NULL DEFAULT 365 | T274 |
| `securityscorecard_connections.history_backfilled_from` | date NULL | T274 |
| `securityscorecard_connections.store_issue_impact` | tinyint(1) NOT NULL DEFAULT 1 | T274 |
| `security_scorecard_factors.score` | passa a `int NULL` (D-12) | T274 |
| `security_scorecard_factors.is_backfill` | tinyint(1) NOT NULL DEFAULT 0 | T274 |
| `hosts.criticality_source` | varchar(32) NULL (`manual`, `cmdb`, `visionone`: quem gravou `hosts.criticality`; nulo = origem desconhecida). O ACR nunca grava `hosts.criticality` (§6.8); a origem `tenable_acr` só aparece em `risk_index_object_days.criticality_source`, quando o motor resolve a precedência com TEN-ACR habilitada | T275 (D-10) |
| `integration_sync_logs.heartbeat_at` | datetime NULL, atualizado a cada gravação de progresso; o `IntegrationSyncLedger` passa a medir o abandono por `coalesce(heartbeat_at, started_at)` | T265 (§9.4) |
| `vulnerabilities.exploit_attempt_count` | int NULL | T275 (D-11) |
| `vulnerabilities.global_exploit_activity` | varchar(16) NULL | T275 (D-11) |
| `vulnerabilities.epss_score` | double NULL — criada por quem entrar primeiro entre T275 e T162; a outra reutiliza | T275, T162 |

As colunas da S39 §12.5 "Também" que não são deste documento (`hosts.last_assessed_at`,
`incidents.severity`, `incidents.resolved_at`, `risk_scoring.quant_residual_ale_mean`) entram nas
versões seguintes, pela ordem de merge do M58 e do Estágio A3: `hosts.last_assessed_at` pela T281,
as duas de incidentes pela T279 e `quant_residual_ale_mean` pela T290.

Enum sem DDL: `IntegrationSyncStatus.Queued = 5` (pedido manual de sincronização na fila, §8.7) e
`IntegrationKind.Tenable = 7`, `IntegrationKind.RiskIndex = 8` (§9.3, §9.4).

### 7.4 Filtros de escopo

Em `NRDbContext.RiskIndex.cs`, no padrão de `NRDbContext.EntityScope.cs:91-98`
(`ScopeIsUnrestricted || (EntityId != null && ScopeEntityIds.Contains(EntityId.Value))`), e as
entidades marcadas `IEntityScoped` para a guarda de escrita:

| Entidade | Coluna filtrada | Observação |
|---|---|---|
| `RiskIndexSnapshot` | `entity_id` | Nós sem entidade (organização, classe e fonte na raiz) só para escopo irrestrito. É defesa em profundidade: o serviço ainda exige claims ⊇ fecho (§11.3) |
| `RiskIndexObjectDay` | `entity_id` = **entidade de escopo da linha de origem** (`risks.entity_id`, `hosts.entity_id`, `incidents.entity_id`, `assessments.entity_id`, `vendor_assets.entity_id`, conexão) | O mesmo predicado que o usuário veria ao vivo; é o que torna exato o cálculo parcial |
| `RiskIndexObjectAttribution` | `scope_entity_id` | Igual |
| `RiskIndexBridge` | `entity_id` | — |
| `RiskIndexClassDay` | `node_entity_id` | Linha da raiz (nulo) só para escopo irrestrito; o serviço aplica a mesma regra de fecho dos snapshots (§11.3) |
| `VendorAsset`, `VendorScore` | `entity_id` | Linhas de tenant (CRI, CES) têm a entidade da conexão; o serviço aplica a regra de tenant (S39 §10.4) |
| `HostExternalId` | via host (`Hosts.Any(h => h.Id == e.HostId)`), como os filhos de `NRDbContext.EntityScope.cs:114-160` | — |
| Perfis, prévias, anotações, mapeamentos, conexões, membros de nó, notas de contexto | sem filtro | Configuração global; o mapa de entidades já é visível a todo usuário autenticado |

### 7.5 Allowlist da auditoria

| Tipo | Na allowlist | Por quê |
|---|---|---|
| `RiskIndexProfile` | **Sim** (S39 §12.5). `SensitivityReport` e `Revision` entram em `IgnoredFields`: o primeiro é um JSON grande, regenerável, e o hash dos parâmetros que ele cobre já está em `risk_index_preview_requests`; o segundo é só o token de concorrência. `ApprovalGrantSource` e os campos `Retire*` são auditados | S39 §13.1 |
| `VendorEntityMapping` | **Sim** | Move atribuição e, com ela, o escopo de hosts |
| `EntityRiskContextNote` | **Sim** | Sua linha de criação compartilha o `CorrelationId` (`GovernanceAuditInterceptor.cs:104`) com as mudanças de propriedade do mesmo `SaveChanges`: é assim que o histórico liga o motivo à mudança |
| `TrendMicroConnection`, `SecurityScorecardConnection`, `TenableConnection` | **Sim**, com `EncryptedApiKey`, `EncryptedApiToken`, `EncryptedAccessKey`, `EncryptedSecretKey`, `LastSyncAt`, `LastSyncStatus`, `LastSyncError`, `FindingsCursor`, `AssetsCursor` e `HistoryBackfilledFrom` em `IgnoredFields` | A entidade padrão e as opções de sincronização mudam atribuição e fontes; credenciais nunca vão para a trilha (teste na §12.2) |
| `Entity`, `EntitiesProperty` | Sim, por T241 | S39 D-17 |
| Snapshots, linhas de objeto, atribuições, membros de nó, pontes, pedidos de prévia, anotações, `vendor_scores`, `vendor_assets`, `host_external_ids`, resumos de issue | **Não** | Só de inserção ou de sincronização: dezenas de milhares de linhas por dia soterrariam a trilha (S39 §12.5) |

### 7.6 Scripts numerados

O alvo atual é 87. A S39 §12.5 coloca as tabelas do índice no "próximo par"; como as tarefas de dados
do M52 têm de entrar antes de qualquer snapshot (S39 §14.1), o plano é entregar a **T243 no mesmo par
que o M52**, para que 88 seja, como a S39 diz, o par das tabelas do ICR. Se as tarefas entrarem em
releases separadas, cada release toma o próximo número na ordem de merge; o conteúdo não muda.

| Versão | Entrega | `Structure/{n}.sql` (sem transação; cada comando guardado) | `Data/{n}.sql` (DML numa transação) |
|---|---|---|---|
| **88** | M52 (T239, T240, T241) + T243 + acréscimos da §7.1–§7.3 ligados ao índice | `CREATE TABLE IF NOT EXISTS` das três tabelas da S39 §12.5 (com os acréscimos da §7.1) e de `risk_index_object_attributions`, `risk_index_node_members`, `risk_index_class_days`, `risk_index_bridges`, `risk_index_preview_requests`, `risk_index_annotations`, `entity_risk_context_notes`; `ALTER TABLE nr_files ADD COLUMN IF NOT EXISTS risk_index_profile_id …`; `CREATE INDEX IF NOT EXISTS …`; `ADD CONSTRAINT fk_… FOREIGN KEY IF NOT EXISTS (…)` | Linha de `__EFMigrationsHistory` com `ON DUPLICATE KEY UPDATE`; `INSERT IGNORE INTO permissions (key, name, description, order)` **sem id** para `risk_index_view`, `risk_index_configure`, `risk_index_approve`, `entity_risk_context` e `entities_manage` (a permissão de CRUD de entidades da T241), no padrão de `Data/81.sql:21-23`; `INSERT INTO settings … ('risk_index_publication', 'shadow') ON DUPLICATE KEY UPDATE value = value`; `INSERT INTO settings … ('risk_index_approver_roles', 'RiskManager,RiskAdministrator') ON DUPLICATE KEY UPDATE value = value` (papéis aprovadores, §6.4); anotação "VPR v2 da Tenable" em 2026-07-01 por `INSERT … SELECT … WHERE NOT EXISTS`; os backfills da T239 e da T240, cujo predicado (`WHERE status_id = 1 AND …`, `WHERE entity_id IS NULL AND …`) já os torna reaplicáveis; `update settings set value = '88' where name = 'db_version'` dentro da transação |
| **89** | Armazenamento de fontes do M56 e do M57, entregue antes do primeiro dos dois | `CREATE TABLE IF NOT EXISTS` de `vendor_assets`, `vendor_scores`, `host_external_ids`, `vendor_entity_mappings`, `tenable_connections`, `security_scorecard_issue_summaries`; `ADD COLUMN IF NOT EXISTS` em `trendmicro_connections`, `securityscorecard_connections`, `security_scorecard_factors`, `hosts`, `vulnerabilities`, `integration_sync_logs` (`heartbeat_at`); `ALTER TABLE security_scorecard_factors MODIFY COLUMN score int(11) NULL` (idempotente por natureza: reaplicar converge); índices e FKs guardados | EF; `INSERT IGNORE INTO host_external_ids (host_id, provider, external_id, first_seen_at, last_seen_at, created_at) SELECT … FROM hosts WHERE external_id IS NOT NULL` (copia a identidade única de hoje); `db_version = 89` |

Regras que valem para os dois: nenhum `ADD` guardado cujo nome uma ação irmã do mesmo `ALTER` remove;
nenhum `char(n)` (`StringColumnTypeGuardTest`); `targetVersion` em `DB/DatabaseInformation.yaml`
atualizado; migração EF gerada por `./migrationAdd.sh` e SQL dividido como manda o CLAUDE.md.

### 7.7 Volume

Estimativa para o porte da captura (≈ 17 mil dispositivos), centenas de cenários e ≈ 500 entidades
pontuadas:

| Tabela | Linhas por dia | Retenção | Total aproximado |
|---|---|---|---|
| `risk_index_object_days` | ≈ 35 mil (host em EXP e GOV; cenário em REG, GOV e CTL; entidade em AME e CTL) | 400 d | ≈ 14 milhões |
| `risk_index_object_attributions` | ≈ 18 mil | 400 d | ≈ 7 milhões |
| `risk_index_snapshots` | ≈ 3,7 mil (≈ 530 nós × 7 categorias) | 1825 d | ≈ 6,8 milhões |
| `risk_index_node_members` | ≈ 5 mil (fecho) + as entidades de escopo dos objetos atribuídos | 400 d | ≈ 2–4 milhões |
| `risk_index_class_days` | ≈ 8 mil (≈ 530 nós × 5 grupos × até 3 categorias) | 400 d | ≈ 3,2 milhões |
| `vendor_scores` | só mudanças | 730 d | depende da volatilidade; tipicamente < 2 milhões |

Medidas: linhas de objeto "avaliado limpo" sem fatores gravam `factors` mínimo e `signals` nulo; os
sinais gravados são só os com `s > 0`, até `k_sinais_max`; `top_contributions` só na linha do ICR;
o job de retenção, depois de 400 dias, anula `top_contributions` e `change_attribution` dos snapshots
(o detalhe já não é reproduzível sem as linhas de objeto) e mantém os valores pelos 1825 dias. A
partição por mês fica como alternativa se a medição da §12.7 estourar o orçamento (§15.1).

---

## 8. Contrato de API

### 8.1 Convenções

- **Chave de nó** (`node`, parâmetro de consulta, padrão `org`):

  ```
  node   := "org" | "scope" | termo (";" termo)*
  termo  := "e:" entityId        nó de entidade (unidade, subunidade, processo, atividade, aplicação…)
          | "u"                  Não atribuído
          | "c:" classe          server|workstation|network|identity|application|internet_facing|
                                 cloud|external_domain|ot_iot|unknown, ou grupo de aba:
                                 devices|internetfacing|identities|applications|clouds
          | "s:" fonte           id da S39 §3.2 (V1-DEV, SSC-TOT, TEN-NES, INC, ASM…)
          | "k:" 1..5            criticidade efetiva
          | "a:" ambiente        termo de ambientes_nao_producao do perfil, "prod" ou "none" (só hosts)
  ```

  No máximo um termo `e:` ou `u`, e no máximo um termo de cada tipo `c`, `s`, `k`, `a`. Termos são
  normalizados (ordem canônica `e|u; c; s; k; a`) antes de virar chave de cache. `scope` é a raiz "Meu escopo" (§5.1). Parser e formatador em
  `Model.RiskIndex.RiskIndexNodeKey`.
- **Datas:** `date` e `from`/`to` em `yyyy-MM-dd` (UTC); ausente = último snapshot.
- **Erros:** `400` com `{ error, message, details? }` (mesmo formato de `AuditTrailController.cs:149-154`);
  `403` para permissão, escopo e segregação de funções, com `error`; `404` nó, objeto ou perfil
  inexistente ou não visível; `409` conflito de estado; listas seguem `HostsController.cs:60-90`
  (`409` para erro do mapeador Gridify, `400` para filtro inválido, `X-Total-Count`).
- **Cache** (`IRiskIndexReadCache`, `IMemoryCache` com limite de tamanho): chave = (endpoint, nó
  normalizado, data resolvida, `profile_id`, modo, **chave de escopo**, **chave de permissões**), onde a
  chave de escopo é `global` ou o SHA-256 da lista ordenada das entidades do **escopo expandido** (claims
  mais descendentes pela T292) junto com a **revisão da hierarquia** de `IEntityHierarchy`, e não das
  claims cruas: mover uma subárvore para fora da unidade do usuário muda a chave mesmo com as claims
  iguais. Além disso, toda mutação da árvore (criação, troca de pai, exclusão de entidade) incrementa a
  revisão e **despeja** as entradas do `IRiskIndexReadCache` (teste: subárvore movida para fora da
  unidade deixa de aparecer na resposta seguinte do nó pai, sem esperar a validade do cache). A chave de
  permissões é o conjunto ordenado das permissões de módulo que a redação consulta (`riskmanagement`,
  `hosts`, `vulnerabilities`, `incident_management`, `assessments`, mais a flag Admin), porque duas
  pessoas com o mesmo escopo e permissões diferentes recebem cargas diferentes (§11.3). Snapshot oficial ou derivado:
  24 h (a data entra na chave, então o snapshot novo nunca colide com o antigo); prévia ao vivo:
  15 min (S39 §9.1). "Último snapshot" é resolvido antes, com cache de 60 s. O cache do Master
  Dashboard, sem chave de escopo (`MasterDashboardService.cs:31`), é o anti-exemplo.
- **Orçamento de cálculo derivado.** Nós derivados e parciais são calculados das linhas de objeto sob
  demanda. Para que um usuário não esgote o servidor: o resumo mensal lê `risk_index_class_days` e os
  snapshots sempre que o nó é pré-calculado ou uma interseção nó × grupo de aba; os valores diários
  derivados de datas passadas, que não mudam, ficam em cache por (nó, data, perfil, chave de escopo,
  chave de permissões); cada cálculo derivado tem limite de 20 s e o serviço admite no máximo 2
  cálculos derivados simultâneos por usuário e 4 por processo. Acima disso, `503
  derived_computation_busy` com `Retry-After`; estouro de tempo, `503 derived_computation_timeout`. A
  GUI mostra a mensagem no painel com "Tentar novamente".
- **DTOs** em `src/Model/RiskIndex/` (namespace `Model.RiskIndex`); conectores em
  `src/Model/Integrations/`.

### 8.2 Leitura do painel — `RiskIndexController` (`/RiskIndex`)

Classe com `[PermissionAuthorize("risk_index_view")]`; as poucas ações de escrita declaram também a
própria permissão, e as duas se somam (por isso a concessão de `risk_index_configure` ou
`risk_index_approve` vem sempre com `risk_index_view`, §11.1, e a Administração não constrói aba de
leitura sem ela, §6.0). Regras comuns:

- **Piloto em sombra** (S39 §13.3: "visível só ao comitê e aos administradores"): com a publicação em
  `shadow`, toda ação que devolve valor do índice responde `403 risk_index_in_shadow` a quem não tem
  `risk_index_approve` **explícita** (§6.4) nem o papel Admin. Ficam fora da regra, porque não devolvem
  valor do índice: `Snapshots/Latest`, `Snapshots/Dates`, `Publication`, `Annotations`, `Sources` (cujas contagens de
  inventário seguem só a regra de escopo) e `Runs` (que exige escopo global). Assim o autor configura
  fontes durante o piloto sem receber 403. A prévia e a ponte do autor
  estão em `RiskIndexProfilesController` (§8.4), com regra própria;
- nó não visível → `404`; nó parcialmente visível → resposta com `Provenance = PartialRestricted`;
- toda resposta que nomeia objeto passa pela redação da §11.3.

| Verbo e rota | Parâmetros | Resposta | Regras de escopo | Erros |
|---|---|---|---|---|
| `GET /RiskIndex/Snapshots/Latest` | — | `RiskIndexLatestDto` | — | `204` sem snapshot |
| `GET /RiskIndex/Snapshots/Dates` | `from`, `to` | `List<DateOnly>` (dias com snapshot, para o `BlackoutDates` do seletor) | — | — |
| `GET /RiskIndex/Overview` | `node`, `date` | `RiskIndexOverviewDto` | `org` exige escopo global; `scope` lista as entidades das claims sem manchete | `400 invalid_node`, `403 org_requires_global_scope` |
| `GET /RiskIndex/Trend` | `node`, `window` (30/90/365), `series` (Icr/Reg/Exp/Ame/Ctl/Gov/Ter), `date` | `RiskIndexTrendDto` | Parcial → `409 trend_unavailable_partial`; nó derivado com 365 → `400 window_requires_precomputed_node` | idem |
| `GET /RiskIndex/Categories` | `node`, `date` | `List<RiskIndexCategoryDto>` | — | idem |
| `GET /RiskIndex/Nodes` | `parent`, `dimension` (Unit/Process/Activity/Application/AssetClass/Source/Criticality/Environment), `filters` (termos `c:`/`s:`/`k:`/`a:` dos chips), `date` | `RiskIndexNodeChildrenDto` | Filhos sem nenhum objeto visível são omitidos; contagens só do visível | `400 invalid_dimension`, `invalid_node` |
| `GET /RiskIndex/Objects` | `node`, `date`, `Filters`, `Sorts`, `Page`, `PageSize` (≤ 500) | `List<RiskIndexObjectRowDto>` + `X-Total-Count` | Linhas de objeto filtradas por escopo | `400`/`409` Gridify |
| `GET /RiskIndex/Objects/{kind}/{ref}` | `date`, `node` | `RiskIndexObjectDetailDto` | Objeto fora do escopo → `404` | `404` |
| `GET /RiskIndex/Inventory` | `node`, `tab`, `date` | `RiskIndexInventoryDto` | — | `400 invalid_tab` |
| `GET /RiskIndex/MonthlySummary` | `node`, `tab` (grupo de aba, ou ausente para o próprio nó no Explorar), `months` (6/12), `date` | `RiskIndexMonthlySummaryDto` | Lê `risk_index_snapshots` (nó) e `risk_index_class_days` (nó ∩ aba) quando o nó é pré-calculado; senão, cálculo derivado sob o orçamento da §8.1 | `503 derived_computation_busy`, `derived_computation_timeout` |
| `GET /RiskIndex/RiskFactors` | `node`, `tab`, `date` | `List<RiskIndexRiskFactorDto>` | — | — |
| `GET /RiskIndex/Changes` | `node`, `to`, `from` (padrão: snapshot anterior) | `RiskIndexChangesDto` | Parcial → `409 changes_unavailable_partial` | `400 invalid_period` (> 30 dias ou `from ≥ to`) |
| `GET /RiskIndex/Contributions` | `node`, `date`, `category` | `RiskIndexContributionsDto` | Parcial: só objetos visíveis, recalculado | — |
| `GET /RiskIndex/Quality` | `node`, `date` | `RiskIndexQualityDto` | Frações sobre o visível | — |
| `GET /RiskIndex/GovernanceIndicators` | `node`, `date` | `RiskIndexGovernanceIndicatorsDto` (§5.2.5) | Contagens sobre o visível; volume, não risco | — |
| `GET /RiskIndex/Monetary` | `node`, `date` | `RiskIndexMonetaryDto` | N = cenários visíveis | — |
| `GET /RiskIndex/Indicators` | `node`, `date` | `List<RiskIndexReferenceIndicatorDto>` | Só escopo irrestrito ou o nó da entidade da conexão; senão lista vazia | — |
| `GET /RiskIndex/Dimensions` | `node` | `RiskIndexDimensionsDto` (classes, fontes, criticidades e ambientes presentes, com disponibilidade) | Presença e contagens calculadas **só sobre os objetos visíveis** ao usuário | — |
| `GET /RiskIndex/Export` | `node`, `date`, `format` (csv/pdf) | CSV (`text/csv`) ou `Report` (pdf) | PDF exige escopo global e é o **único** caminho que cria o relatório tipo 4 (§5.14) | `400 unsupported_format`, `403 pdf_requires_global_scope` |
| `GET /RiskIndex/LivePreview/Overview` | `node` | `RiskIndexOverviewDto` com `Provenance = UnofficialPreview` | Escopo global **e** `risk_index_configure` ou `risk_index_approve` (verificado no serviço) | `403 live_preview_not_allowed` |
| `GET /RiskIndex/Publication` | — | `RiskIndexPublicationDto` | — | — |
| `PUT /RiskIndex/Publication` | `RiskIndexPublicationRequest` | `RiskIndexPublicationDto` | `[PermissionAuthorize("risk_index_approve")]`; `published` exige perfil vigente com `committee_decision_ref` | `409 no_approved_profile` |
| `GET /RiskIndex/Annotations` | `from`, `to` | `List<RiskIndexAnnotationDto>` | — | — |
| `POST /RiskIndex/Annotations` | `RiskIndexAnnotationCreateRequest` | `201 RiskIndexAnnotationDto` | `[PermissionAuthorize("risk_index_configure")]` | `400` |
| `GET /RiskIndex/Sources` | — | `List<RiskIndexSourceDto>` | Catálogo, habilitação, W, conexões e última sincronização para todos; `ObjectCount` e `StaleFraction` só com escopo irrestrito (senão `null`), porque são totais da organização | — |
| `GET /RiskIndex/Runs` | `kind` (Snapshot/Retention/Preview), `limit` | `List<IntegrationSyncLog>` | Escopo irrestrito (contagens de linhas da organização inteira) | `403 runs_require_global_scope` |

Filtros aceitos em `/RiskIndex/Objects` (mapa em `ApplicationEntityFilterMapperProvider`): `category`,
`state`, `kind`, `assetClass`, `source`, `criticality`, `environment`, `label`, `factor`, `isPresumed`,
`seal`, `chip`. O filtro `label` casa só rótulos que o usuário pode ver (redação, §11.3): buscar pelo
título de um cenário redigido não revela a linha.
Ordenações: `label`, `r`, `contribution`, `criticality`, `lastReading`. **Não há** ordenação por
sensibilidade nem por Δ (S39 §1.3); o mapa não as declara, e um teste garante isso (§12.3).

### 8.3 DTOs (`Model.RiskIndex`, `Model.Integrations`)

Todo tipo citado nas §8.2 e §8.4–§8.7 está definido aqui, e as frentes C e D da §14.2 constroem
*stubs* contra esta lista. Convenções: `record` posicional (propriedades `init`, desserializadas pelo
construtor no System.Text.Json); coleções como `IReadOnlyList<T>` não nulas (lista vazia, nunca
`null`); enums serializados como número, como no resto da API (os valores são explícitos e só se acrescentam ao fim); datas de dia
como `DateOnly`, instantes como `DateTime` UTC. Os vocabulários que antes eram texto livre (efeito,
ramo, tipo de marcador, motivo de supressão, módulo de navegação) são enums. Respostas com objetos
levam o rótulo em `RiskIndexObjectLabelDto`, cujo `RequiresPermission` não nulo indica redação (§11.3).
Erros de API (`error`) continuam texto: são códigos estáveis, traduzidos por `RiskApi_‹código›MSG`.
Os tipos existentes reutilizados são `AuditLog`, `IntegrationSyncLog` e `FileListing`
(`AuditTrailController.cs:46`, `TrendMicroController.cs:133`, `FilesController.cs:49`).

```csharp
namespace Model.RiskIndex;

// ---------- enums ----------
public enum RiskIndexCategory { Icr = 0, Reg = 1, Exp = 2, Ame = 3, Ctl = 4, Gov = 5, Ter = 6 } // = snapshots.category
public enum RiskIndexMode { Interim = 1, Full = 2 }
public enum RiskIndexProvenance { Official = 1, OfficialDerived = 2, PartialRestricted = 3, UnofficialPreview = 4 }
public enum RiskIndexDisplayState { Point = 1, PointWithInterval = 2, Indicative = 3, NoData = 4 }
public enum RiskIndexCategoryState { Available = 1, Unavailable = 2, NotApplicable = 3 }
public enum RiskIndexObjectKind { Scenario = 1, Host = 2, ExternalDomain = 3, Application = 4, VendorAsset = 5,
                                  TargetEntity = 6, Tenant = 7, Supplier = 8, UnassignedGroup = 9 }
public enum RiskIndexObjectState { Assessed = 1, AssessedClean = 2, Stale = 3, Presumed = 4, NotAssessed = 5 } // S39 §8.1
public enum RiskIndexDimension { Unit = 1, Process = 2, Activity = 3, Application = 4, AssetClass = 5,
                                 Source = 6, Criticality = 7, Environment = 8 }
public enum RiskIndexTabGroup { Devices = 1, InternetFacing = 2, Identities = 3, Applications = 4, Clouds = 5 }
public enum RiskIndexPublicationState { Shadow = 1, Published = 2 }
public enum RiskIndexProfileStatus { Draft = 1, PendingApproval = 2, Active = 3, Retired = 4, Rejected = 5 } // S39 §12.5
public enum RiskIndexDeltaDirection { Worse = 1, Better = 2, Stable = 3 }
public enum RiskIndexDeltaSuppressedReason { BelowThreshold = 1, Discontinuity = 2, MissingBaseline = 3 }
public enum RiskIndexMarkerKind { Profile = 1, Mode = 2, Appetite = 3, EntityMap = 4, ExternalInput = 5 } // §9.3
public enum RiskIndexAnnotationKind { VendorModel = 1, VendorRecalibration = 2, VendorBreachPenalty = 3, Note = 4 }
public enum RiskIndexAttributionEffect { Risk = 1, Context = 2, Coverage = 3, Configuration = 4, Unattributed = 5, Floor = 6, Restructuring = 7 }
public enum RiskIndexContributionBranch { Mean = 1, HeldBy = 2, Presumption = 3 }
public enum RiskIndexNavigationModule { Risk = 1, Host = 2, HostFindings = 3, ApplicationFindings = 4,
                                        Incident = 5, Assessment = 6, Entity = 7 }
public enum RiskIndexPreviewState { NotRun = 0, Queued = 1, Running = 2, Completed = 3, Failed = 4, Stale = 5 }
public enum RiskIndexRebaselineTrigger { PresetChange = 1, OmegaPace = 2, ParameterCount = 3, ModeChange = 4 }
public enum RiskIndexApprovalCheckCode { SodAuthorCannotApprove = 1, SodBusinessReviewer = 2,
    ApproverPermissionNotExplicit = 3, SodSelfGrantedApprover = 4, InvalidTransition = 5 }

// ---------- referências ----------
public record RiskIndexNodeRef(string Key, string Label, string NodeType);
public record RiskIndexObjectRef(RiskIndexObjectKind Kind, string Ref);
public record RiskIndexObjectLabelDto(RiskIndexObjectRef Object, string Label, string? RequiresPermission);
public record DateRange(DateOnly From, DateOnly To);
public record RiskIndexNavigationTargetDto(RiskIndexNavigationModule Module, int RecordId, string LabelKey);

// ---------- painel (§8.2) ----------
public record RiskIndexLatestDto(DateOnly Date, int ProfileId, RiskIndexMode Mode, RiskIndexPublicationState Publication);
public record RiskIndexPublicationDto(RiskIndexPublicationState State, DateTime ChangedAtUtc, string ChangedBy);

public record RiskIndexOverviewDto(RiskIndexNodeRef Node, IReadOnlyList<RiskIndexNodeRef> Breadcrumb,
    DateOnly SnapshotDate, DateTime ComputedAtUtc, RiskIndexProvenance Provenance,
    RiskIndexProfileSummaryDto Profile, RiskIndexMode Mode, RiskIndexHeadlineDto? Headline, // null na raiz "Meu escopo"
    IReadOnlyList<RiskIndexCategoryDto> Categories, IReadOnlyList<RiskIndexSealDto> Seals,
    IReadOnlyList<RiskIndexChipDto> Chips, RiskIndexQualitySummaryDto Quality, int ObjectCount,
    IReadOnlyList<RiskIndexBandDto> Bands, IReadOnlyList<RiskIndexNodeRef> ScopeEntities); // preenchido só em "Meu escopo"
public record RiskIndexHeadlineDto(int? Displayed, double? Unfloored, double? Floored, string? BandKey, int? BandIndex,
    RiskIndexDisplayState DisplayState, double? IntervalLow, double? IntervalHigh, bool ShowIntervalOnCard,
    RiskIndexDeltaDto? Delta7d, IReadOnlyList<RiskIndexFloorReasonDto> FloorReasons,
    bool MeanBranchActive, RiskIndexCategory? HeldByCategory);
public record RiskIndexDeltaDto(int? Points, RiskIndexDeltaDirection? Direction, DateOnly? ComparedWith,
    RiskIndexDeltaSuppressedReason? SuppressedReason, DateOnly? DiscontinuityDate);
public record RiskIndexFloorReasonDto(string GateCode, RiskIndexObjectLabelDto Scenario, string? FlagKey); // modo pleno
public record RiskIndexCategoryDto(RiskIndexCategory Category, RiskIndexCategoryState State, double? Value, int? Displayed,
    string? BandKey, double Weight, double EffectiveWeight, double? Points, RiskIndexHeldByDto? HeldBy,
    double? Coverage, double? Freshness, double? PresumptionPoints, string? UnavailableReasonKey, string? RequiresRef,
    IReadOnlyList<RiskIndexSealDto> Seals); // Seals só em REG (S39 §6.6)
public record RiskIndexHeldByDto(RiskIndexObjectLabelDto Object, double R, RiskIndexObjectLabelDto? Next, bool IsTie, int TieCount);
public record RiskIndexSealDto(string Key, int? Count, IReadOnlyDictionary<string, string> Args, string? ObjectFilter);
public record RiskIndexChipDto(string Key, IReadOnlyDictionary<string, string> Args, RiskIndexObjectLabelDto? Object, string? ObjectFilter);
public record RiskIndexBandDto(string Key, string LabelPtBr, string LabelEnUs, int Index, int RampStep, int LowerInclusive); // RampStep = ChartPaletteMap.BandStep; o cliente mostra o rótulo da cultura da interface (en-US como padrão, como o Localization.resx neutro)
public record RiskIndexQualitySummaryDto(double Coverage, double Freshness, double Quality, string QualityBandKey,
    IReadOnlyList<RiskIndexQualityStripItemDto> Strip); // §5.12.1
public record RiskIndexQualityStripItemDto(string Key, double? Fraction, int? Count, DateTime? LastReadingUtc,
    bool Warning, string? ObjectFilter);

public record RiskIndexTrendDto(RiskIndexNodeRef Node, int WindowDays, RiskIndexCategory Series,
    IReadOnlyList<RiskIndexTrendPointDto> Points, IReadOnlyList<RiskIndexBandSegmentDto> BandSegments,
    IReadOnlyList<RiskIndexMarkerDto> Markers, IReadOnlyList<RiskIndexAnnotationDto> Annotations,
    IReadOnlyList<DateRange> FlooredRanges, IReadOnlyList<RiskIndexOverlayDto> Overlays,
    double? AppetiteLine, double? TargetLine);
public record RiskIndexTrendPointDto(DateOnly Date, int? Displayed, double? Unfloored, int? DisplayedFloored, double? Ewma,
    RiskIndexDisplayState DisplayState, double? IntervalLow, double? IntervalHigh,
    IReadOnlyList<RiskIndexCategory> UnavailableCategories, bool IsGap, bool IsFloored, int? DeltaFromPrevious,
    IReadOnlyList<RiskIndexAttributionRowDto> TopMovers);
public record RiskIndexBandSegmentDto(DateOnly From, DateOnly To, int ProfileVersion, IReadOnlyList<RiskIndexBandDto> Bands);
public record RiskIndexMarkerDto(DateOnly Date, RiskIndexMarkerKind Kind, IReadOnlyDictionary<string, string> Args, double? VersionDelta);
public record RiskIndexAnnotationDto(int Id, DateOnly Date, RiskIndexAnnotationKind Kind, string Source, string Label,
    string? Details, string? CreatedBy);
public record RiskIndexAnnotationCreateRequest(DateOnly Date, RiskIndexAnnotationKind Kind, string Source, string Label, string? Details);
public record RiskIndexOverlayDto(int FromProfileVersion, int ToProfileVersion, bool IsPartial, IReadOnlyList<RiskIndexOverlayPointDto> Points);
public record RiskIndexOverlayPointDto(DateOnly Date, double Value);

public record RiskIndexNodeChildrenDto(RiskIndexNodeRef Parent, RiskIndexDimension Dimension,
    IReadOnlyList<RiskIndexComparabilityGroupDto> Groups, IReadOnlyList<RiskIndexNodeRowDto> Rows,
    int ObjectsOutsideDimension); // ex.: objetos que não são host na dimensão Ambiente
public record RiskIndexComparabilityGroupDto(string Key, string NodeType, IReadOnlyList<RiskIndexCategory> ApplicableCategories,
    RiskIndexProvenance Provenance, string? NotComparableNoteKey);
public record RiskIndexNodeRowDto(RiskIndexNodeRef Node, int? Displayed, string? BandKey, RiskIndexDisplayState State,
    RiskIndexProvenance Provenance, int? Delta7d, double? Coverage, int ObjectCount, int SharedObjects, bool HasChildren,
    string ComparabilityGroup, double? IntervalLow, double? IntervalHigh, double? UnassignedWeightFraction);

public record RiskIndexObjectRowDto(RiskIndexObjectLabelDto Object, string? AssetClass, RiskIndexCategory Category, double R,
    int Criticality, string? CriticalitySource, RiskIndexObjectState State, double? Validity, DateTime? LastReadingUtc,
    double ContributionPoints, bool IsPresumed, int SharedWith, IReadOnlyList<string> Sources, string? Environment);

public record RiskIndexObjectDetailDto(RiskIndexObjectLabelDto Object, string? AssetClass, DateOnly SnapshotDate,
    RiskIndexCriticalityDto Criticality, bool? InternetFacing, IReadOnlyList<RiskIndexObjectCategoryDto> Categories,
    RiskIndexScenarioInputsDto? Scenario,          // null se não é cenário ou se redigido
    RiskIndexDecisionStateDto? DecisionState,      // somente leitura; null se redigido
    IReadOnlyList<RiskIndexNodeRef> Attribution, IReadOnlyList<RiskIndexChipDto> Chips,
    IReadOnlyList<RiskIndexNavigationTargetDto> Navigation); // só destinos que o usuário pode abrir
public record RiskIndexCriticalityDto(int? Own, int? Inherited, int DataDelta, int Effective, string Source,
    string? AuthorName, DateTime? ChangedAtUtc, bool DeclaredNotBia);
public record RiskIndexObjectCategoryDto(RiskIndexCategory Category, double R, RiskIndexObjectState State, string Population,
    double Weight, RiskIndexContributionBranch Branch, IReadOnlyList<RiskIndexFactorDto> Factors);
public record RiskIndexFactorDto(string FactorKey, string Type, double X, double V, double UF, double FEff,
    DateTime? ReadingUtc, string SourceKey, IReadOnlyList<RiskIndexSignalDto> Signals);
public record RiskIndexSignalDto(double S, IReadOnlyList<string> Flags, string? Text, // Text e Link nulos se redigidos
    RiskIndexNavigationTargetDto? Link);
public record RiskIndexScenarioInputsDto(int ImpactLevel, int? Rank, int? CampaignSize, double? RRes, double RInh,
    int CadenceDays, int DelayDays, double? Credit, double SigmaRef, double REff);
public record RiskIndexDecisionStateDto(string? AcceptanceStateKey, DateTime? AcceptanceValidUntilUtc,
    string? LastCampaignDecisionKey, DateTime? LastCampaignDecisionAtUtc, int OpenTasks, int OverdueTasks,
    IReadOnlyList<string> SealKeys, string GatesStateKey, double? NetBenefit); // NetBenefit só no modo pleno (M44)

public record RiskIndexChangesDto(RiskIndexNodeRef Node, DateOnly From, DateOnly To, double DeltaUnfloored, int DeltaDisplayed,
    IReadOnlyList<RiskIndexAttributionRowDto> Rows, IReadOnlyList<RiskIndexChangeEventDto> Events, double Residual,
    IReadOnlyList<DateOnly> CrossedMarkers);
public record RiskIndexAttributionRowDto(RiskIndexAttributionEffect Effect, RiskIndexCategory? Category,
    RiskIndexObjectLabelDto? Object, string LabelKey, double Points, int PointsRounded, double? PercentOfChange,
    string? AuthorName, bool IsReattribution);
public record RiskIndexChangeEventDto(string Kind, RiskIndexObjectLabelDto? Object, IReadOnlyDictionary<string, string> Args);

public record RiskIndexContributionsDto(RiskIndexNodeRef Node, double EffectiveRegisterWeight, IReadOnlyList<RiskIndexContributionRowDto> Rows);
public record RiskIndexContributionRowDto(RiskIndexObjectLabelDto Object, RiskIndexCategory Category, double Points,
    double ShareOfCategory, RiskIndexContributionBranch Branch, double? Sensitivity, RiskIndexObjectLabelDto? NextHolder,
    RiskIndexDecisionStateDto? DecisionState);

public record RiskIndexInventoryDto(RiskIndexTabGroup Tab, int InventoryCount, string InventoryBasisKey,
    IReadOnlyList<RiskIndexSourceVisibilityDto> Sources, RiskIndexStateCountsDto Overall,
    IReadOnlyDictionary<string, int> RDistributionByBand); // soma = Overall.Assessed
public record RiskIndexSourceVisibilityDto(string SourceKey, string Availability, bool Enabled, int Current, int Stale,
    int Expired, int NoReading, DateTime? LastReadingUtc, string? DisabledReasonKey);
public record RiskIndexStateCountsDto(int Assessed, int Stale, int Presumed, int NotAssessed);
public record RiskIndexMonthlySummaryDto(RiskIndexTabGroup? Tab, IReadOnlyList<RiskIndexCategory> SeriesOrder,
    IReadOnlyList<RiskIndexMonthDto> Months, RiskIndexProvenance Provenance);
public record RiskIndexMonthDto(string YearMonth, bool IsPartial, bool HasProfileChange,
    IReadOnlyDictionary<RiskIndexCategory, double?> MeanByCategory);
public record RiskIndexRiskFactorDto(string FactorKey, string LabelKey, int Count, int Points, string ObjectFilter);
public record RiskIndexQualityDto(double Coverage, double Freshness, double Quality, string QualityBandKey,
    double UnassignedWeightFraction, bool UnassignedWarning, double DefaultCriticalityWeightFraction,
    IReadOnlyList<RiskIndexStaleSourceDto> StaleSources, int PresumedCritical, int DivergentLinks, int OrphanApplications,
    int SscPseudoHosts, int ExitedWithBadReading, int FindingStatusDisagreements, int ResidualAboveInherent,
    string EvidenceConfidenceStateKey);
public record RiskIndexStaleSourceDto(string SourceKey, DateTime? LastReadingUtc);
public record RiskIndexMonetaryDto(decimal? ResidualExpectedLossSum, decimal? InherentExpectedLossSum, int Quantified, int Total,
    double QuantifiedRegWeightFraction, bool IsPartial, string Currency, string P95StateKey);
public record RiskIndexReferenceIndicatorDto(string Key, double? Value, string? Level, DateTime? ReadAtUtc, bool IsStale, int? ConnectionId);
public record RiskIndexGovernanceIndicatorsDto(int AcceptancesExpiringSoon, int ExpiryWindowDays, int SegregationOverrides90d,
    double? CampaignCompletion, double? MeanDaysToDecide, int OverdueCampaigns, double? SlaCompliance,
    string FlagsStateKey, string GateBStateKey); // §5.2.5
public record RiskIndexDimensionsDto(IReadOnlyList<RiskIndexDimensionValueDto> AssetClasses,
    IReadOnlyList<RiskIndexDimensionValueDto> Sources, IReadOnlyList<RiskIndexDimensionValueDto> Criticalities,
    IReadOnlyList<RiskIndexDimensionValueDto> Environments);
public record RiskIndexDimensionValueDto(string Term, string LabelKey, int VisibleObjects, string Availability);
public record RiskIndexSourceDto(string SourceId, string Category, string Factor, string Availability, string? RequiresRef,
    bool EnabledActive, bool? EnabledDraft, int? WindowActiveDays, int? WindowDraftDays, int Connections,
    DateTime? LastSyncUtc, string? LastSyncStatus, int? ObjectCount, double? StaleFraction); // contagens só com escopo global

// ---------- perfis (§8.4) ----------
public record RiskIndexProfileSummaryDto(int Id, int Version, string Name, string? Preset, RiskIndexProfileStatus Status,
    DateTime? EffectiveFrom, DateTime? RetiredAt, string CreatedBy, string? ApprovedBy, bool IsRebaseline,
    string? CommitteeDecisionRef);
public record RiskIndexProfileDto(int Id, int Version, string Name, string? Preset, RiskIndexMode Mode, RiskIndexProfileStatus Status,
    RiskIndexParameters Parameters, string? Justification, string? CommitteeDecisionRef, string? BoardApprovalRef,
    bool IsRebaseline, string? RejectedReason, string CreatedBy, DateTime CreatedAtUtc, DateTime? SubmittedAtUtc,
    string? ApprovedBy, DateTime? ApprovedAtUtc, string? ApprovalGrantSource, DateTime? EffectiveFrom, DateTime? RetiredAt,
    string? RetireJustification, string? RetireDecisionRef, string? RetiredBy, int Revision,
    bool HasSensitivityReport); // o relatório em si só por GET /{id}/Preview (§8.4)
public record RiskIndexProfileCreateRequest(string Name, int? FromProfileId, string? Preset);
public record RiskIndexProfileUpdateRequest(string Name, RiskIndexParameters Parameters);
public record RiskIndexProfileDiffDto(int A, int B, IReadOnlyList<RiskIndexParameterChangeDto> Changes);
public record RiskIndexParameterChangeDto(string Group, string Key, string? ValueA, string? ValueB, bool Changed);
public record RiskIndexParameterSchemaDto(IReadOnlyList<RiskIndexParameterSchemaItemDto> Items);
public record RiskIndexParameterSchemaItemDto(string Key, string Group, string Type, string? Min, string? Max, string Default,
    bool Locked, string? LockedReasonKey, string HelpKey, string S39Ref);
public record RiskIndexPresetDto(string Key, string NameKey, RiskIndexParameters Parameters, bool Activatable);
public record RiskIndexValidationResultDto(bool IsValid, IReadOnlyList<RiskIndexValidationIssueDto> Errors,
    IReadOnlyList<RiskIndexValidationIssueDto> Warnings, IReadOnlyList<string> ChangedKeys, bool RequiresRebaseline,
    IReadOnlyList<RiskIndexRebaselineTrigger> RebaselineTriggers, bool RequiresBoardRef, int? TrimesterBaselineProfileId);
public record RiskIndexValidationIssueDto(string Code, string? ParameterKey, IReadOnlyDictionary<string, string> Args);
public record RiskIndexPreviewStatusDto(int? RequestId, RiskIndexPreviewState State, int Progress, string? ParametersHash,
    DateTime? FinishedAtUtc, string? ErrorMessage);
public record RiskIndexPreviewDto(RiskIndexPreviewStatusDto Status, IReadOnlyList<RiskIndexPreviewNodeDto> Nodes,
    double? KendallTau, int ComparedUnits, IReadOnlyList<RiskIndexTornadoRowDto> Tornado,
    IReadOnlyList<RiskIndexPresetValueDto> Presets, double RegisterWeightActive, double RegisterWeightDraft,
    IReadOnlyList<RiskIndexMovingObjectDto> TopMovers, IReadOnlyList<RiskIndexPresetValueDto> PureMean, bool OverlayPartial);
public record RiskIndexPreviewNodeDto(RiskIndexNodeRef Node, double Active, double Draft, string BandKeyActive,
    string BandKeyDraft, bool BandChanged);
public record RiskIndexTornadoRowDto(string ParameterKey, double Minus, double Plus);
public record RiskIndexPresetValueDto(string PresetKey, RiskIndexNodeRef Node, double Value);
public record RiskIndexMovingObjectDto(RiskIndexObjectLabelDto Object, RiskIndexCategory Category, double DeltaRw);
public record RiskIndexSubmitRequest(string Justification);
public record RiskIndexAttachRequest(int FileId);
public record RiskIndexApproveRequest(string CommitteeDecisionRef, string? BoardApprovalRef, bool ConfirmRebaseline, string? Note);
public record RiskIndexRejectRequest(string Reason);
public record RiskIndexRetireRequest(string Justification, string CommitteeDecisionRef);
public record RiskIndexPublicationRequest(RiskIndexPublicationState State);
public record RiskIndexApprovalEligibilityDto(IReadOnlyList<RiskIndexApprovalCheckDto> Checks, string? GrantSource,
    bool RequiresBoardRef, IReadOnlyList<RiskIndexRebaselineTrigger> RebaselineTriggers);
public record RiskIndexApprovalCheckDto(RiskIndexApprovalCheckCode Code, bool Passed, string Message);
public record RiskIndexBridgeDto(int FromVersion, int ToVersion, DateOnly BridgeDate, IReadOnlyList<RiskIndexBridgeNodeDto> Nodes,
    bool OverlayPartial);
public record RiskIndexBridgeNodeDto(RiskIndexNodeRef Node, RiskIndexCategory Category, double Old, double New,
    IReadOnlyList<RiskIndexOverlayPointDto> Overlay);

// ---------- contexto de entidades (§8.5) ----------
public record EntityRiskContextRowDto(int Id, int? ParentId, string Type, string Name, int? Criticality,
    string CriticalitySourceKey, bool? InternetFacing, int? SecurityClassificationId, string? LastChangedBy,
    DateTime? LastChangedAtUtc, bool CriticalitySupported, bool InternetFacingSupported, bool ClassificationSupported);
public record EntityRiskContextChange(int EntityId, int? Criticality, bool ClearCriticality, bool? InternetFacing,
    int? SecurityClassificationId, bool ClearClassification);
public record EntityRiskContextBatchRequest(string Justification, IReadOnlyList<EntityRiskContextChange> Changes);
public record EntityRiskContextBatchResultDto(string CorrelationId, IReadOnlyList<EntityRiskContextRowDto> Rows);
```

```csharp
namespace Model.Integrations;

public enum VendorMatchKind { V1CustomTag = 1, V1AssetGroup = 2, TenableTag = 3 }
public enum RestampPolicy { OnlyUnassigned = 1, FollowMapping = 2 }
public enum TenableProduct { VulnerabilityManagement = 1, SecurityCenter = 2 }

public record VendorEntityMappingDto(int Id, int Provider, int ConnectionId, VendorMatchKind Kind, string Value, string? Label,
    int EntityId, string EntityName, int Priority, RestampPolicy Policy, bool Enabled); // Provider = IntegrationKind
public record VendorEntityMappingUpsert(int Provider, int ConnectionId, VendorMatchKind Kind, string Value, string? Label,
    int EntityId, int Priority, RestampPolicy Policy, bool Enabled);
public record VendorMappingCandidateDto(VendorMatchKind Kind, string Value, string? Label, int ObjectsSeen);
public record VendorEntityMappingPreviewDto(int Matched, int WouldAssign, int WouldReassign, int Conflicts, int AlreadyAssigned,
    IReadOnlyList<VendorMappingPreviewSampleDto> Sample); // amostra de até 20
public record VendorMappingPreviewSampleDto(string ObjectLabel, string? CurrentEntity, string? NewEntity, string OutcomeKey);

public record TenableProductDto(TenableProduct Product, string NameKey, string? DefaultBaseUrl, bool BaseUrlEditable);
public record TenableConnectionView(int Id, string Name, TenableProduct Product, string BaseUrl, int? EntityId, bool Enabled,
    int SyncIntervalHours, bool SyncFindings, bool SyncAssets, bool CollectAcr, bool CollectAes, int MinSeverity,
    int InitialWindowDays, bool IncludeUnlicensed, bool AllowInvalidCertificate, bool HasAccessKey, bool HasSecretKey,
    string? AccessKeyVaultReference, string? SecretKeyVaultReference, DateTime? LastSyncAtUtc, int? LastSyncStatus,
    string? LastSyncError); // nunca traz chave
public record TenableConnectionUpsert(string Name, TenableProduct Product, string? BaseUrl, int? EntityId, bool Enabled,
    int SyncIntervalHours, bool SyncFindings, bool SyncAssets, bool CollectAcr, bool CollectAes, int MinSeverity,
    int InitialWindowDays, bool IncludeUnlicensed, bool AllowInvalidCertificate,
    string? AccessKey, string? SecretKey, string? AccessKeyVaultReference, string? SecretKeyVaultReference,
    bool ClearAccessKey, bool ClearSecretKey); // chave omitida sem Clear* = mantém
public record TenableTestResult(bool Success, IReadOnlyList<TenableTestStepDto> Steps);
public record TenableTestStepDto(string StepKey, bool Passed, string? MessageKey, string? Detail); // Detail nunca contém credencial
public record TenableSyncQueuedDto(int LogId);
```

### 8.4 Perfis — `RiskIndexProfilesController` (`/RiskIndex/Profiles`)

Sem atributo de classe: cada ação declara a sua permissão (coluna "Permissão"). Duas regras de serviço
valem para as leituras que trazem **valores do índice** (relatório de sensibilidade, ponte e histórico,
que mostram nós da organização, de outras unidades e objetos):

- **escopo irrestrito** obrigatório (`403 profile_values_require_global_scope`): é a mesma regra da
  manchete da organização (S39 §10.4), e a terceira linha que as lê tem escopo global;
- **piloto em sombra**: com a publicação em `shadow`, só `risk_index_configure`, `risk_index_approve`
  ou o papel Admin (`403 risk_index_in_shadow`). Com `risk_index_configure`, só o autor e os editores
  do rascunho leem a prévia e a ponte daquele rascunho, como diz a S39 §13.3; o serviço verifica
  (`403 risk_index_in_shadow` para os demais).

`GET /{id}` e `GET /Active` **não** trazem o relatório (só `HasSensitivityReport`); parâmetros, status,
referências e datas são configuração, legíveis com `risk_index_view` em qualquer escopo.

| Verbo e rota | Corpo / parâmetros | Resposta | Permissão | Erros |
|---|---|---|---|---|
| `GET /` | — | `List<RiskIndexProfileSummaryDto>` | `risk_index_view` | — |
| `GET /{id}` | — | `RiskIndexProfileDto` (sem o relatório) | `risk_index_view` | `404` |
| `GET /Active` | — | `RiskIndexProfileDto` (sem o relatório) | `risk_index_view` | `204` sem vigente |
| `GET /Diff` | `a`, `b` | `RiskIndexProfileDiffDto` | `risk_index_view` | `404` |
| `GET /Schema` | — | `RiskIndexParameterSchemaDto` (grupo, tipo, faixa, padrão, travado e motivo, chave de ajuda, referência S39) | `risk_index_view` | — |
| `GET /Presets` | — | `List<RiskIndexPresetDto>` (com `Activatable = false` para Média pura) | `risk_index_configure` | — |
| `POST /` | `RiskIndexProfileCreateRequest` | `201 RiskIndexProfileDto` | `risk_index_configure` | `409 open_draft_exists`; `400 preset_required_for_first_profile`, `preset_not_activatable` |
| `PUT /{id}` | `RiskIndexProfileUpdateRequest` + `If-Match: <revision>` | `RiskIndexProfileDto` | `risk_index_configure` | `409 stale_draft`, `invalid_transition`; `428 if_match_required`; `400 parameter_out_of_range` (estrutural) |
| `DELETE /{id}` | — | `204` (as prévias do rascunho vão junto, FK em cascata) | `risk_index_configure` | `409 invalid_transition` (só Draft) |
| `POST /{id}/Validate` | — | `RiskIndexValidationResultDto` | `risk_index_configure` | — |
| `POST /{id}/Preview` | — | `202 RiskIndexPreviewStatusDto` | `risk_index_configure` | `409 preview_in_progress`, `invalid_transition` |
| `GET /{id}/Preview` | — | `RiskIndexPreviewDto`: de `risk_index_preview_requests.report` enquanto Rascunho; de `sensitivity_report` depois da submissão | `risk_index_view` + escopo irrestrito + regra de sombra | `404 preview_missing`; `403 profile_values_require_global_scope`, `risk_index_in_shadow` |
| `POST /{id}/Submit` | `RiskIndexSubmitRequest` | `RiskIndexProfileDto`; copia o relatório da prévia concluída com o hash atual para `sensitivity_report` | `risk_index_configure` | `400 justification_required`, `rebaseline_reason_required`, `profile_invalid` (com erros); `409 preview_missing`, `preview_stale`, `invalid_transition` |
| `POST /{id}/Attachments` | `RiskIndexAttachRequest` | `204` | `risk_index_approve` | `404`; `403 file_not_owned` (o arquivo não foi enviado pelo usuário); `409 file_already_attached` (o arquivo já tem outro vínculo: risco, mitigação, aceitação, incidente, plano ou outro perfil), `invalid_transition` |
| `GET /{id}/Attachments` | — | `List<FileListing>` | `risk_index_view` | — |
| `GET /{id}/ApprovalEligibility` | — | `RiskIndexApprovalEligibilityDto`: as checagens `SodAuthorCannotApprove`, `SodBusinessReviewer`, `ApproverPermissionNotExplicit`, `SodSelfGrantedApprover`, `InvalidTransition`, cada uma com `Passed` e a mensagem; a origem da concessão; se exige ref. do Conselho; os gatilhos de rebaseline | `risk_index_approve` | `404` |
| `POST /{id}/Approve` | `RiskIndexApproveRequest` | `RiskIndexProfileDto` (com `EffectiveFrom`) | `risk_index_approve` (+ explícita no serviço) | `403 sod_author_cannot_approve`, `sod_business_reviewer`, `approver_permission_not_explicit`, `sod_self_granted_approver`; `400 committee_ref_required`, `committee_attachment_required`, `board_ref_required`, `rebaseline_confirmation_required`; `409 invalid_transition` |
| `POST /{id}/Reject` | `RiskIndexRejectRequest` | `RiskIndexProfileDto` | `risk_index_approve` | `400 reason_required`; `409 invalid_transition` |
| `POST /{id}/Retire` | `RiskIndexRetireRequest` | `RiskIndexProfileDto`; grava `retire_justification`, `retire_decision_ref`, `retired_by_id` e `retired_at` sem tocar `justification` nem `committee_decision_ref` | `risk_index_approve` | `400 justification_required`, `committee_ref_required`; `409 invalid_transition` (só Active) |
| `GET /{id}/Bridge` | — | `RiskIndexBridgeDto` (Δ da versão por nó, sobreposição de 90 dias, parcial) | `risk_index_view` + escopo irrestrito + regra de sombra | `404` sem ponte; `403 profile_values_require_global_scope`, `risk_index_in_shadow` |
| `GET /{id}/History` | `limit` | `List<AuditLog>` | `risk_index_view` + escopo irrestrito | `403 profile_values_require_global_scope` |

`ApprovalEligibility` e `Approve` chamam o mesmo `RiskIndexProfilesService.EvaluateApprovalAsync`; o
`Approve` recusa com o código da primeira checagem que falha. `RiskIndexParameters` é o espelho tipado
das chaves da S39 §12.2, serializado em `parameters`. As regras da S39 §12.4 e as leituras da §6.2.3
ficam numa classe pura,
`Model.RiskIndex.RiskIndexProfileRules.Validate(RiskIndexParameters draft, RiskIndexParameters? baseline,
RiskIndexParameters? trimesterBaseline)`, usada pelo servidor (autoritativo) e pela GUI (validação ao
vivo), para que as duas nunca divirjam.

### 8.5 Entidades — acréscimos e mudanças no `EntitiesController`

| Verbo e rota | Corpo | Resposta | Permissão | Erros |
|---|---|---|---|---|
| `GET /Entities/RiskContext` | `types` | `List<EntityRiskContextRowDto>` | `RequireValidUser` (o mapa já é visível a todo usuário) | — |
| `PUT /Entities/RiskContext` | `EntityRiskContextBatchRequest` (justificativa e a lista de mudanças) | `EntityRiskContextBatchResultDto` | `[PermissionAuthorize("entity_risk_context")]` + escopo irrestrito (no serviço) | `400 field_not_supported_for_type`, `criticality_out_of_range`, `justification_too_short`, `empty_batch` (com o `entityId` da linha recusada em `details`); `403 risk_context_requires_global_scope`; `404` (entidade inexistente, com o id); `409 criticality_owned_by_bia` (quando o M41 fornecer) |
| `GET /Entities/{id}/History` | `limit` (≤ 5000) | `List<AuditLog>` agrupável por `CorrelationId`, com a justificativa | `RequireValidUser` | `400 limit` |
| `POST /Entities` (existente) | — | — | **passa a** `[PermissionAuthorize("entities_manage")]` (T241) | **novo** `403 risk_context_requires_permission` se o corpo traz `criticality`, `internetFacing` ou `securityClassification` sem `entity_risk_context`; com a permissão, também exige escopo irrestrito (`403 risk_context_requires_global_scope`) |
| `PUT /Entities/{id}` (existente) | — | — | **passa a** `[PermissionAuthorize("entities_manage")]` (T241) | **novo** `403 risk_context_requires_permission` se o corpo muda uma das três propriedades sem `entity_risk_context`; com a permissão, também exige escopo irrestrito |
| `DELETE /Entities/{id}` (existente) | — | — | **passa a** `[PermissionAuthorize("entities_manage")]` (T241) | — |

- `PUT /Entities/RiskContext` aplica o lote inteiro num único `SaveChanges`: um `CorrelationId`, uma
  `EntityRiskContextNote` por entidade com a mesma justificativa e o mesmo `batch_id`, e nenhuma
  mudança se qualquer linha for recusada (validação de todas antes de gravar; exceção no `SaveChanges`
  desfaz tudo). Lote com uma entidade é o caso de uma edição isolada; não há rota por entidade.
- A checagem `risk_context_requires_permission` fica no `EntitiesService` (não só no controller), para
  que **todo** caminho de escrita EAV dessas três propriedades a cumpra, incluindo importações que
  criam entidades (o Jira Assets cria aplicações na raiz, `JiraIntegrationService.Assets.cs:480`, sem
  essas propriedades: o teste garante que continua sem elas).

### 8.6 Mapeamentos — `EntityMappingsController` (`/Integrations/EntityMappings`)

Classe com `[PermissionAuthorize("configuration")]`.

| Verbo e rota | Corpo / parâmetros | Resposta | Erros |
|---|---|---|---|
| `GET /` | `provider`, `connectionId` | `List<VendorEntityMappingDto>` | — |
| `GET /Candidates` | `provider`, `connectionId` | `List<VendorMappingCandidateDto>` (`kind`, `value`, `label`, objetos vistos) vindos de `host_external_ids.tags`, `vendor_assets.tags` e das linhas `V1AssetGroup` de `vendor_scores` | — |
| `POST /` | `VendorEntityMappingUpsert` | `201` | `400 invalid_kind_for_provider`; `409 duplicate_mapping` |
| `PUT /{id}` | idem | `200` | `404`; `409 duplicate_mapping` |
| `DELETE /{id}` | — | `204` | `404` |
| `POST /Preview` | `VendorEntityMappingUpsert` (ainda não salvo) | `VendorEntityMappingPreviewDto` (`Matched`, `WouldAssign`, `WouldReassign`, `Conflicts`, `AlreadyAssigned`, amostra de 20) | `400` |

### 8.7 Tenable — `TenableController` (`/Tenable`)

Classe com `[PermissionAuthorize("configuration")]`, espelho de `TrendMicroController.cs:31-131`.

| Verbo e rota | Corpo | Resposta | Erros |
|---|---|---|---|
| `GET /products` | — | `List<TenableProductDto>` (VM, SC, URL padrão) | — |
| `GET /` | — | `List<TenableConnectionView>` | — |
| `GET /{id:int}` | — | `TenableConnectionView` | `404` |
| `POST /` | `TenableConnectionUpsert` | `201` | `400 base_url_invalid` (SC sem https; VM com URL diferente da padrão), `credentials_required` |
| `PUT /{id:int}` | idem; chave omitida sem `ClearAccessKey`/`ClearSecretKey` = mantém; com a marca, apaga | `200` | `404`, `400` |
| `DELETE /{id:int}` | — | `204` | `404` |
| `POST /{id:int}/test` | — | `TenableTestResult` (etapas: autenticação, privilégio de export de vulnerabilidades, de ativos, licença ACR/AES quando aplicável) | — |
| `POST /{id:int}/sync` | — | `202 TenableSyncQueuedDto`: grava uma linha de `integration_sync_logs` com `Status = Queued` e devolve o id. **Não executa nada na API** | `409 sync_running` (já há linha `Queued` ou `Running` da conexão); `409 connection_disabled` |
| `GET /log` | `limit` | `List<IntegrationSyncLog>` de `IntegrationKind.Tenable` | — |

`TenableConnectionView` nunca traz chave: só `HasAccessKey`, `HasSecretKey`, `AccessKeyVaultReference`
e `SecretKeyVaultReference`, como `TrendMicroService.cs:1152-1153` faz para o Vision One.

**Por que a sincronização manual não roda na API.** O `TrendMicroController` executa a sincronização
dentro da requisição (`TrendMicroController.cs:116-128`), sob o `HttpContext` do chamador, e o
`DalService` aplica o escopo dele (`DALService.cs:172-188`). Para a Tenable isso quebraria a cadeia de
identidade (um host fora do escopo de quem clicou não seria encontrado e viraria duplicata, ou a guarda
de escrita lançaria `EntityScopeViolationException`), e manteria um export de horas no processo web. Por
isso o pedido vai para a fila e o `TenableSyncJob` o executa no host de jobs, sob o principal global da
T237 (§9.4).

### 8.8 Vision One e SecurityScorecard

| Endpoint existente | Mudança |
|---|---|
| `POST/PUT /TrendMicro`, `GET /TrendMicro/{id}` | Contrato ganha `EntityId` editável (já existe no banco), `SyncPosture`, `SyncAttackSurfaceObjects`, `SyncWorkbenchAlerts`, `SyncCustomTags` |
| `POST /TrendMicro/{id}/test` | Resultado por endpoint habilitado, com a permissão ou o crédito que falta |
| `POST/PUT /SecurityScorecard`, `GET /SecurityScorecard/{id}` | Ganha `EntityId` editável, `HistoryBackfillDays`, `StoreIssueImpact` |
| `POST /SecurityScorecard/{id}/sync` | Executa também a recuperação de histórico pendente |
| `GET /SecurityScorecard/{id}/history` | Inalterado e ainda atrás de `configuration`; o painel lê o rating por `/RiskIndex/Indicators` |

### 8.9 ClientServices

| Interface (`ClientServices.Interfaces`) | Implementação | Cobre |
|---|---|---|
| `IRiskIndexService` | `RiskIndexRestService` | §8.2 |
| `IRiskIndexConfigService` | `RiskIndexConfigRestService` | §8.4, §8.5, publicação, anotações, fontes, execuções |
| `IIntegrationsService` (existente) | `IntegrationsRestService` | §8.6, §8.7, §8.8 |

Registro em `GUIClient/GeneralServicesBootstrapper.cs` como o `DashboardRestService` (`:178`).
Mapeamento de erros como em `DashboardRestService.cs:17-55`: 401 descarta o token; 403 e 409 viram
`InvalidHttpRequestException` com o `error` e a mensagem do servidor, que a GUI mostra pelo
`RunAsync`/`ExplainError`; `X-Total-Count` lido nas listas.

---

## 9. Jobs e integrações

### 9.1 Agenda (UTC)

| Hora | Job | Novo? | Observação |
|---|---|---|---|
| a cada 15 min | `TenableSyncJob` | sim | `"*/15 * * * *"`. Em cada tique: consome os pedidos manuais `Queued` (§8.7) e sincroniza as conexões devidas (`agora ≥ início da última execução + intervalo − 15 min`, contado do início, §6.8). Com o intervalo padrão de 24 h, a primeira execução fixa o horário diário; recomenda-se dispará-la de madrugada (antes das 05:00) para que o snapshot leia o export do dia |
| 02:20 | `ResidualRiskCalculation` | não | (`JobsManager.cs:46-47`) |
| 02:30 | `GovernanceRetention` | não | |
| 02:40 | `RiskIndexRetentionJob` | sim | Único caminho de expurgo (S39 §9.1) |
| 03:00 | `TrendMicroSync` (+ fases novas) | estendido | Postura, grupos, superfície, alertas, tags |
| 04:00 | `SecurityScorecardSync` (+ histórico) | estendido | Recupera o histórico pendente |
| **05:00** | `RiskIndexSnapshotJob` | sim | `"0 5 * * *"`. Depois do residual, do Vision One e da SSC; antes dos digests das 07:00 (S39 §9.1) |
| a cada minuto | `RiskIndexPreviewJob` | sim | Consome `risk_index_preview_requests`; `[DisableConcurrentExecution]` e reivindicação atômica (§9.6) |

Registro em `JobsManager` com `RecurringJob.AddOrUpdate<T>("Id", x => x.Run(), cron)`; forma do job
copiada de `Jobs/Governance/ResidualRiskCalculation.cs` (`Run() => RunAsync().GetAwaiter().GetResult()`).

### 9.2 Injeção de dependências e escopo

- `services.AddScoped<RiskIndexSnapshotJob>()`, `RiskIndexRetentionJob`, `RiskIndexPreviewJob` e
  `TenableSyncJob` em `src/BackgroundJobs/ConfigurationManager.cs` (junto de `:116-118`).
- Serviços por uma extensão `AddRiskIndex()` (`ServerServices/RiskIndex/RiskIndexServiceRegistration.cs`),
  chamada pela API (`ServicesBootstrapper`) e pelos jobs; Tenable dentro de `AddTrack4Integrations()`.
- **Escopo:** todos os jobs dependem da T237 (principal de background com `scope=global`). Nenhum usa
  `bypassEntityScope`. O teste da T237 que resolve todo job recorrente e vê linhas semeadas cobre os
  quatro novos.
- **Workers:** `WorkerCount` do Hangfire passa de 2 para **3** (`ConfigurationManager.cs:140`) na mesma
  mudança que registra os jobs. É requisito, não contenção opcional: com uma prévia longa e uma
  sincronização Tenable de horas ocupando dois workers, o snapshot das 05:00 e o despacho de
  notificações esperariam. `JobScheduleOrderTest` verifica o valor (§12.6).

### 9.3 `RiskIndexSnapshotJob`

1. Resolve o perfil em vigor no instante `t_D` do snapshot de D (05:00 UTC): o perfil com
   `effective_from ≤ t_D` e (`retired_at` nulo **ou** `retired_at > t_D`), entre os de status Active ou
   Retired. Um perfil suspenso (Retired sem sucessor, §6.4) tem `retired_at` = instante da suspensão,
   então deixa de valer a partir do snapshot seguinte. Sem perfil, registra "sem perfil vigente" e sai;
   o painel mostra "Nenhum perfil vigente" ou "Publicação suspensa" (§5.13).
2. Se já há linhas para (D, perfil), sai: o dia não é recalculado e a série nunca é reescrita.
3. Calcula o fecho de cada nó pré-calculado (T246), as linhas de objeto (T244, T245), os nós
   (S39 §4), a composição, a sensibilidade e a atribuição em relação a D−1 (T248), os selos, chips e
   marcadores do dia (detecção na tabela abaixo). Grava em `risk_index_node_members` as entidades do
   fecho (`member_kind = Closure`) **e** as entidades de escopo de todo objeto atribuído ao nó naquele
   dia (`member_kind = Scope`), que é o que a regra de leitura oficial compara com as claims (§11.3).
   Grava também os `C_k` de cada nó pré-calculado de entidade ∩ grupo de aba em
   `risk_index_class_days` (§7.2).
4. Se o perfil é novo, calcula também a versão anterior e grava `risk_index_bridges` (ponte e
   sobreposição de 90 dias, S39 §9.5).
5. Grava tudo numa **única transação** (snapshots, linhas de objeto, atribuições, membros de nó,
   pontes), em lotes de 2 mil linhas pelo EF.
6. Registra a execução em `integration_sync_logs` com o novo `IntegrationKind.RiskIndex = 8`
   (contagens, duração, erro), ao lado do `IntegrationKind.Tenable = 7`.

Falha em qualquer passo: rollback, log `Failed`, nenhum dado parcial; o dia vira lacuna na tendência.
O `IntegrationSyncReaper` existente fecha execuções abandonadas.

**Detecção dos marcadores** (S39 §9.5). Cada marcador é por nó: o dia ganha o marcador no nó afetado,
e a seta e a EWMA reiniciam nesse nó.

| Tipo (`RiskIndexMarkerKind`) | Como o job detecta | Nós afetados | Efeito em "o que mudou" |
|---|---|---|---|
| `Profile` | `profile_id` de D ≠ de D−1 (inclui fonte habilitada e troca de preset, que são versões) | Todos | `A_cfg` = Δ da versão pela ponte (S39 §9.4) |
| `Mode` | `mode` de D ≠ de D−1 | Todos | A troca de modo só acontece com versão nova de perfil (S39 §9.4), então o efeito vai a `A_cfg` pela ponte; o job recusa um snapshot cujo modo difere do perfil |
| `Appetite` | Linha de `audit_logs` de `RiskAppetite` (já auditado, S39 §9.5) com instante em (t_{D−1}, t_D] | Teto global: todos; teto de entidade: os nós cujo fecho contém a entidade | Pela S39 §9.4, sem parcela própria (o teto move APT-BRK e ACC-APT, efeito de risco em GOV); a dica do marcador nomeia a mudança |
| `EntityMap` | O conjunto `Closure` do nó em D difere do de D−1 (mudança de `entities.parent`, de tipo, ou das propriedades `organizationUnit`/`applications`); **ou** uma regra de mapeamento regravou `hosts.entity_id` de hosts do nó naquele ciclo (contagem do `integration_sync_logs`) | O nó cujo fecho mudou ou que recebeu ou perdeu hosts por regra | As parcelas dos objetos que entraram ou saíram só por reatribuição formam `A_estr`, o efeito de reestruturação (S39 §9.4, §5.7). Reatribuição manual de um host isolado (edição do host) gera o evento e a parcela em `A_estr`, sem marcador |
| `ExternalInput` | `external_inputs_hash` de D ≠ de D−1 | Todos | `A_cfg` "insumo externo alterado" (S39 §9.5) |


### 9.4 `TenableSyncJob` (T265–T268; Security Center na T269, backlog)

A coluna Security Center fica registrada para a T269; o M56 entrega só Vulnerability Management.

| Etapa | Vulnerability Management | Security Center (T269, backlog) |
|---|---|---|
| Autenticação | `X-ApiKeys: accessKey=…; secretKey=…;`, chaves por `ISecretResolver.ResolveAsync` a cada execução; o cabeçalho é montado só no `HttpRequestMessage` e nunca é logado (regras de não divulgação na §11.6) | `x-apikey: accesskey=…; secretkey=…;` |
| Teste | Autenticação, depois consulta ao estado de exports de vulnerabilidades e de ativos (privilégio de export); a licença de ACR/AES é confirmada na primeira sincronização | Usuário corrente e sistema |
| Achados | `POST /vulns/export` com `num_assets = 500`, `filters.since = findings_cursor` (ou `now − initial_window_days`), `state` OPEN/REOPENED/FIXED, severidade mínima; consulta de estado a cada 10 s até 60 min; download sequencial dos chunks | `POST /rest/analysis` (`vulndetails`, `cumulative`), paginado por `startOffset/endOffset`, filtro `lastSeen` |
| Mapeamento | `TenableNormalizer` → `NormalizedFinding`: `ToolUniqueId = finding_id`, `RuleId = plugin.id`, VPR, CVSS3, CVEs, EPSS (coluna da §7.3), exploit, severidade 0–4, datas; FIXED → ciclo de vida pelo pipeline de ingestão; `ImportSource = tenable-vm` ou `tenable-sc` | mesmo normalizador |
| Ativos | `POST /assets/v2/export` (`chunk_size = 1000`, `updated_at ≥ assets_cursor`) → hosts pela cadeia de identidade: `host_external_ids` (provedor + uuid), MAC, FQDN, hostname, IP (id externo antes de IP, D-14) | `/hosts` com ACR e AES |
| Pontuações | Com "Coletar AES": `ratings.aes.score` → `vendor_scores` (TenableAes, escala 1000). Com "Coletar ACR": `ratings.acr.score` → `vendor_scores` (TenableAcr). A sincronização **não** grava `hosts.criticality`; o motor lê TenableAcr como candidata à criticidade (`⌈ACR/2⌉`, abaixo de manual e CMDB, S39 §5.1) e TenableAes em EXP.D só quando TEN-ACR / TEN-AES estão em `fontes_habilitadas` do perfil do snapshot (§6.8) | idem |
| Atribuição | Regras de tag (§6.6), depois entidade padrão da conexão | idem |
| Cursor | Avança só depois do sucesso completo da etapa | idem |
| Limites | 429 respeita `Retry-After`; 409 de export duplicado reutiliza o export em curso; nunca mais de um export simultâneo por conexão; `MaxResponseBytes` de 64 MiB nos chunks | 429 idem |
| Log | `integration_sync_logs` com `IntegrationKind.Tenable = 7`, contagens e trilha de progresso; cada gravação de progresso atualiza `heartbeat_at` | idem |
| Pedido manual | Reivindicado atomicamente (`UPDATE … SET status = Running, started_at = agora WHERE id = ? AND status = Queued`, conferindo as linhas afetadas) e executado sob o principal global da T237 | idem |
| Abandono | O `IntegrationSyncLedger` passa a medir `StaleAfter` (2 h) a partir de `coalesce(heartbeat_at, started_at)`: uma execução longa que ainda avança não é encerrada, e o *single-flight* da conexão não libera um segundo export | idem |

VPR anterior a 2026-07-01 vem do modelo antigo: a anotação semeada na §7.6 marca a data na tendência
(S39 §3.2, TEN-VM).

### 9.5 Vision One (T271–T273, T275) e SecurityScorecard (T274)

| Fase | Endpoint | Grava | Requisito do tenant |
|---|---|---|---|
| Postura | `GET /v3.0/asrm/securityPosture` | `vendor_scores` V1Cri (riskIndex, níveis por categoria em `details`) e V1SecurityConfig (média de `agentFeatureStatus[].adoptionRate`, para CTL.C) | Créditos CREM; permissão "Third-party auditing (API only)" |
| Grupos de ativos | `GET /v3.0/asrm/assetGroups` | `vendor_scores` V1AssetGroup por grupo | Sem crédito CREM |
| Tags | `GET /v3.0/asrm/attackSurfaceCustomTags` e `assetCustomTags` dos dispositivos | `host_external_ids.tags`, `vendor_assets.tags` | Sem crédito CREM |
| Superfície | `attackSurfaceGlobalFqdns`, `attackSurfacePublicIpAddresses`, `attackSurfaceDomainAccounts`, `attackSurfaceServiceAccounts`, `attackSurfaceCloudAssets`, `attackSurfaceLocalApps` | `vendor_assets` + `vendor_scores` V1VendorAsset | Créditos CREM |
| Alertas | `GET /v3.0/workbench/alerts` (30 dias) | `vendor_scores` V1AttackIntensity, contagem por dia e severidade | Permissão de Workbench |
| Dispositivos (corrigido) | `attackSurfaceDevices` | `lastDetectDateTime`/`firstSeenDateTime` em `host_external_ids`; `vulnerabilities.exploit_attempt_count`, `global_exploit_activity`, `epss_score`; `hosts.criticality_source = visionone` | — |
| SSC histórico | `GET /companies/{d}/history/score` e `/history/factors/score` desde `history_backfilled_from` | `security_scorecard_factors` com `is_backfill = 1` | — |
| SSC impacto | `issue_summary[]` de `/factors` | `security_scorecard_issue_summaries` | — |
| SSC fator ausente | — | `score = NULL` (D-12) | — |
| SSC penalidade | eventos de mudança de score | `risk_index_annotations` (VendorBreachPenalty) | — |

As sincronizações **deixam de gravar** `entities.cyber_risk_index` (T270, D-09). A coluna fica como
legado não lido.

### 9.6 `RiskIndexPreviewJob` e `RiskIndexRetentionJob`

- **Prévia.** `[DisableConcurrentExecution]` no job, e reivindicação **atômica** do pedido mais antigo
  em `Queued` (`UPDATE … SET status = Running, started_at = agora, heartbeat_at = agora WHERE id = ? AND
  status = Queued`, conferindo as linhas afetadas): duas execuções sobrepostas nunca processam o mesmo
  pedido. Reaplica o rascunho aos últimos 90 dias de linhas de objeto e ao dia corrente com dados
  brutos (S39 §13.2), atualiza `progress` e `heartbeat_at`, grava o relatório em
  `risk_index_preview_requests.report` com o hash dos parâmetros e marca `Completed`. **Nunca grava na
  linha do perfil** (nem `sensitivity_report`, nem `updated_at`, nem `revision`): a cópia para
  `sensitivity_report` é feita pela submissão (§8.4), e o autor não recebe `409 stale_draft` por causa
  da prévia. Se o rascunho mudou durante a execução (hash diferente), marca `Failed` com "parâmetros
  alterados durante a prévia". Roda nos jobs, não na API, para não disputar CPU com as requisições e
  para sobreviver a reinício: um pedido `Running` cujo `heartbeat_at` tem mais de 15 min (sem avanço,
  não "começou há muito") volta a `Queued` na passada seguinte, porque o Hangfire em memória perde o
  estado no reinício.
- **Retenção.** Apaga em lotes de 10 mil: linhas de objeto, atribuições, membros de nó e
  `risk_index_class_days` com mais de 400 dias; o `report` de pedidos de prévia encerrados há mais de 90
  dias (o relatório anexado ao perfil fica em `sensitivity_report`); snapshots com mais de 1825 dias; `vendor_scores` encerrados há mais de 730 dias. Anula
  `top_contributions` e `change_attribution` de snapshots com mais de 400 dias. Registra contagens.

---

## 10. GUI: implementação

### 10.1 Arquivos

| Caminho (`src/GUIClient/…`) | Conteúdo | Tarefa |
|---|---|---|
| `Views/RiskOverview/RiskOverviewView.axaml` + `ViewModels/RiskOverview/RiskOverviewViewModel.cs` | Módulo (IX-1). Comentário de cabeçalho: "painel somente leitura; arquétipo próprio, fora de IX-5 A–E" | T251 |
| `Views/RiskOverview/RiskHeadlineCard.axaml` + `RiskHeadlineViewModel` | §5.2, incluindo indicadores de referência | T251 |
| `Views/RiskOverview/RiskTrendChart.axaml` + `RiskTrendViewModel` | §5.3 | T252 |
| `Views/RiskOverview/RiskCategoriesPanel.axaml` + `RiskCategoriesViewModel` | §5.4 | T253 |
| `Views/RiskOverview/RiskAssetClassTabs.axaml` + `RiskAssetClassTabsViewModel`, `RiskAssetClassTabViewModel` | §5.5 | T253, T256 |
| `Views/RiskOverview/RiskExplorePanel.axaml` + `RiskExploreViewModel` | Painel Explorar (§5.6.1): dimensão, chips, árvore e resumo mensal do nó | T254 |
| `Views/RiskOverview/RiskNodeTree.axaml(.cs)` + `RiskNodeTreeViewModel` | §5.6 (colunas do `TreeDataGrid` no code-behind, como em `VulnerabilitiesView`) | T254 |
| `Views/RiskOverview/RiskQualityStrip.axaml`, `RiskGovernanceIndicatorsStrip.axaml` + VMs | Faixas sempre visíveis (§5.12.1, §5.2.5) | T256 |
| `Views/RiskOverview/RiskChangesPanel.axaml` + `RiskChangesViewModel` | §5.7 | T255 |
| `Views/RiskOverview/RiskContributionsPanel.axaml` + `RiskContributionsViewModel` | §5.8 | T255 |
| `Views/RiskOverview/RiskObjectList.axaml` + `RiskObjectListViewModel` | §5.9.1 | T254 |
| `Views/Dialogs/RiskIndexObjectDetailDialog.axaml` + `RiskIndexObjectDetailDialogViewModel` | §5.9.2, `DialogWindowBase<NavigationTarget?>` | T255 |
| `Views/Dialogs/RiskProfileInfoDialog.axaml` + VM | Chip de perfil (§5.1) | T251 |
| `Views/RiskOverview/RiskQualityPanel.axaml`, `RiskMonetaryPanel.axaml`, `RiskTopRisksPlaceholder.axaml` + VMs | §5.10–§5.12 | T256 |
| `Views/Admin/RiskIndex/RiskIndexAdminView.axaml` + `ViewModels/Admin/RiskIndex/RiskIndexAdminViewModel.cs` | Hospedeiro das abas (§6.0) | T259 |
| `Views/Admin/RiskIndex/RiskProfilesView.axaml`, `RiskProfileEditorView.axaml`, `RiskProfilePreviewView.axaml`, `RiskProfileApprovalView.axaml`, `RiskSourcesView.axaml`, `EntityRiskContextView.axaml(.cs)` + VMs | §6.1–§6.5, §6.7 | T259–T264 |
| `Views/Dialogs/NewProfileDraftDialog`, `ProfileDiffDialog`, `RejectProfileDialog`, `SuspendPublicationDialog`, `RiskContextJustificationDialog` + VMs | Diálogos utilitários | T259–T264 |
| `Views/Admin/TenableIntegrationView.axaml` + `ViewModels/Admin/TenableIntegrationViewModel.cs` | §6.8, hospedado por um `TabItem` de `IntegrationsView` (como o de Jira Assets em `IntegrationsView.axaml:653-655`) | T265 |
| `Views/Admin/EntityMappingsView.axaml` + `ViewModels/Admin/EntityMappingsViewModel.cs` | §6.6 | T263, T268 |
| `Tools/RiskIndex/HeadlineFormatter.cs`, `BandGlyph.cs`, `ChartPaletteMap.cs`, `NodeKeyDisplay.cs`, `DrillState.cs`, `RiskIndexNavigationMap.cs` | Lógica pura, sem Avalonia, compilada em `GUIClient.Tests` | T251, T254, T255, T257 |
| `Tools/RiskIndex/ChartPalette.cs` | Adaptador Avalonia: resolve tokens → `SKColor` | T257 |
| `Tools/Admin/AdminPaneAccess.cs`, `Tools/Admin/UnsavedChangesGuard.cs`, `Navigation/AdminTarget.cs` | Regras da §6.0 e do §6.2.1 | T259, T260 |
| `Tools/Navigation/ModuleAccess.cs` | Permissão de cada módulo, a mesma da barra de navegação (§10.3) | T255 |
| `Tools/Entities/RiskContextFieldPolicy.cs` | Campos somente leitura no formulário de entidade (§6.7) | T264 |
| `Navigation/NavigationTarget.cs`, `Navigation/IHasUnsavedChanges.cs` | §10.3, §6.2.1 | T255, T260 |

### 10.2 Shell

| Arquivo | Mudança |
|---|---|
| `Models/AvaliableViews.cs` | `RiskOverview` acrescentado **ao fim** do enum |
| `ViewModels/MainWindowViewModel.cs` | `RiskOverviewIsVisible`, `RiskOverviewViewModel` (criado na primeira navegação, como o Master Dashboard em `:296-298`), `case` em `NavigateTo` e linha em `HideAllViews` |
| `Views/MainWindow.axaml` | `<views:RiskOverviewView Margin="10" DataContext="{Binding RiskOverviewViewModel}" IsVisible="{Binding #MWindow.DataContext.RiskOverviewIsVisible}"/>` |
| `Views/NavigationBar.axaml` | Botão `BtnRiskOverview` (`nav-base nav-icon`, ícone `Gauge`) dentro de `Border.tooltip` com `ActionTooltipConverter` / `permission`, `IsEnabled="{Binding HasRiskIndexViewPermission}"`, `Command="{Binding BtRiskOverviewClicked}"`; botão Administração passa a `IsEnabled="{Binding CanOpenAdministration}"` (§6.0) |
| `ViewModels/NavigationBarViewModel.cs` | `HasRiskIndexViewPermission = UserPermissions.Contains("risk_index_view") ∨ IsAdmin`; `CanOpenAdministration`; `StrRiskOverview` (chave `NavRiskOverview`); comando `BtRiskOverviewClicked` → `Navigation.NavigateTo(AvaliableViews.RiskOverview)` |
| `ViewModels/AdminViewModel.cs`, `Views/AdminWindow.axaml` | Entrada "Índice de risco" e regras da §6.0; `AdminViewModel.Select(AdminTarget)` seleciona painel e aba |
| `Navigation/INavigationService.cs` + implementação | Novo `void ShowAdministration(AdminTarget target)`: monta ou reutiliza a janela singleton (`ShowAuxiliaryWindow<AdminWindow>` só sabe ativá-la, `INavigationService.cs:18-24`) e chama `Select(target)`. `AdminTarget(AdminPane Pane, RiskIndexAdminTab? Tab = null, int? ProfileId = null)`, com fábricas como `AdminTarget.RiskIndex(RiskIndexAdminTab.Sources)`. Alvo não permitido por `AdminPaneAccess` abre no painel inicial com toast da razão. `NavigationBarViewModel.ExecuteOpenAdministration` passa a chamar `ShowAdministration(AdminTarget.Default)` |

O módulo não tem *timer* de atualização (a tela inicial tem um de 1 minuto): o snapshot muda uma vez
por dia.

### 10.3 Navegação para um registro

`INavigationService` ganha `void NavigateTo(NavigationTarget target)`, com
`record NavigationTarget(AvaliableViews View, int RecordId, string? SubView = null)`. É o **único** tipo
de destino na GUI; o diálogo de detalhe devolve `NavigationTarget?` (§5.9.2). O DTO da API
(`RiskIndexNavigationTargetDto`, §8.3) é convertido por `RiskIndexNavigationMap` (puro, testado):

| `RiskIndexNavigationModule` | `NavigationTarget` | Permissão do módulo (`ModuleAccess`) |
|---|---|---|
| `Risk` | `(Risk, riskId)` | `riskmanagement` |
| `Host` | `(Devices, hostId)` | `hosts` |
| `HostFindings` | `(Vulnerabilities, hostId, "host")` | a do módulo Vulnerabilidades na barra |
| `ApplicationFindings` | `(Vulnerabilities, entityId, "entity")` | idem |
| `Incident` | `(Incidents, incidentId)` | a do módulo Incidentes na barra |
| `Assessment` | `(Assessment, assessmentId)` | `assessments` |
| `Entity` | `(Entities, entityId)` | `asset` |

`ModuleAccess` (puro) expõe, por `AvaliableViews`, a mesma regra que habilita o botão do módulo na
barra (`NavigationBarViewModel.cs:273-278` e os conversores dos botões de Vulnerabilidades e
Incidentes), e a barra passa a lê-la também, para que as duas nunca divirjam. Os botões de navegação
do detalhe usam `ModuleAccess` para `Is‹Destino›Enabled` e a razão; `NavigateTo` consulta o mesmo
helper e recusa um módulo não permitido com toast, como defesa em profundidade.

Os view-models de destino (Riscos, Hosts/Devices, Incidentes, Avaliações, Vulnerabilidades, Entidades)
implementam `ISelectableModule.SelectRecordAsync(int id, string? subView)`, que seleciona o registro na
lista (ou, em Vulnerabilidades, aplica o filtro do host ou da entidade). Registro fora do escopo ou
inexistente: toast "O registro não está no seu escopo" e nenhuma seleção. É IX-7: o view-model pede ao
serviço; nada percorre a árvore visual.

### 10.4 Gráficos (LiveCharts 2)

Não existe medidor (*gauge*) no projeto, e um medidor só de cor é proibido (ui-standard §2.6): o valor
da manchete é texto. Os gráficos:

| Gráfico | Construção | Observação |
|---|---|---|
| Tendência | `CartesianChart`; X `DateTimeAxis` diário, Y fixo 0–100. Séries: `LineSeries<DateTimePoint>` ICR\*; `LineSeries` EWMA com `DashEffect`; `LineSeries` pontilhada da sobreposição de ponte; marcadores em `ScatterSeries<DateTimePoint, VariableSVGPathGeometry>` com um caminho SVG de triângulo (o build fixado não tem geometria de triângulo: tem `CircleGeometry`, `DiamondGeometry`, `RectangleGeometry`, `StarGeometry`, `CrossGeometry` e as de caminho SVG); anotações em `ScatterSeries<…, DiamondGeometry>` só com contorno, no rodapé; lacunas em `ScatterSeries<…, CircleGeometry>` só com contorno; dias indicativos em `ScatterSeries<…, RectangleGeometry>` só com contorno, mais um `LineSeries` por dia para o bigode do intervalo | A linha diária é contínua: a lacuna repete o valor (S39 §9.3) e só o ponto muda |
| Áreas de faixa | `Sections` com `RectangularSection { Yi, Yj, Fill }` por faixa e trecho de versão (`Xi`/`Xj` nas trocas de `faixas`), como já se faz em `RisksVsCostsViewModel.cs:49-80`, mas com cor de token | Rótulo por `Label`/`LabelSize`/`LabelPaint` da seção (`CoreSection.Label` existe no build fixado) |
| Piso e descontinuidades | `RectangularSection` com `Xi`/`Xj` = intervalo do piso (preenchimento) e `Xi = Xj` = dia do marcador (traço tracejado) | — |
| Dicas | `YToolTipLabelFormatter` monta o texto a partir do DTO do ponto (valor, Δ, três maiores parcelas) | Toda informação da dica também existe na tabela (§10.8) |
| Clique | `DataPointerDownCommand` → `BtTrendPointClicked(date)` | Primeiro tratador de clique em gráfico do projeto (§3.1) |
| Resumo mensal | `ColumnSeries<double?>` agrupadas, uma por categoria, na ordem fixa REG, EXP, AME, CTL, GOV, TER; `DataLabelsPaint` com formatador "‹código› ‹valor›" ("EXP 74") em cada barra; alternativa "Ver como tabela" (`DataGrid` mês × categoria) | `null` = categoria sem valor no mês, sem barra e com "—" na tabela |
| Tornado | Duas `RowSeries` (−20 % e +20 %) com `Pivot` no valor base; rótulo de dado com o sinal e o valor ("−8,0", "+11,9"), de modo que o lado não dependa da cor; a tabela Parâmetro · −20 % · +20 % fica sempre ao lado | Só na prévia (§6.3) |
| Barras de visibilidade e de categoria | **Não usam LiveCharts**: `Grid` com colunas proporcionais e `Border` com classe (`visibilitySegment.current`, `riskMeterFill`) | A cor fica em `WindowStyles.axaml` e o `LintUi` enxerga |

**Paleta (T257).** O `LintUi` não lê C#, e os gráficos de hoje usam `SKColor` literal
(`DashboardViewModel.cs:143`, `:152`). Por isso:

- `Tools/RiskIndex/ChartPaletteMap.cs` (puro) mapeia **papéis** para **chaves de token**: rampa de
  faixas por quantidade, exposta como `BandStep(posição, quantidade)` (2 → passos 1, 5; 3 → 1, 3, 5;
  4 → 1, 3, 4, 5; 5 → 1…5), que dá o token `NrCriticality{passo}` do gráfico **e** a classe `b{passo}`
  da pílula (§10.6), para que a mesma faixa nunca tenha duas cores; séries de categoria (`NrChartSeries1`…`6` para REG, EXP, AME, CTL, GOV, TER), EWMA
  (`NrChartEwma`), marcador (`NrChartMarker`), sombreamento de piso (`NrChartFloorShade`), grade e eixo
  (`NrChartGrid`, `NrChartAxis`), e as constantes de opacidade das áreas.
- `Tools/RiskIndex/ChartPalette.cs` resolve cada chave em `Application.Current` para `SKColor`, e
  reconstrói as tintas quando o tema muda (`ActualThemeVariantChanged`).
- Tokens novos em `Styles/Tokens.axaml`: `NrChartSeries1..6`, `NrChartEwma`, `NrChartMarker`,
  `NrChartFloorShade`, `NrChartGrid`, `NrChartAxis`, todos apontando para recursos de cor Semi
  existentes (nenhum hex novo), propostos pelo processo da ui-standard §2.6 no PR da T257.

### 10.5 Localização

Todo texto visível é uma propriedade `Str*` ligada a uma chave presente em **`Localization.resx`,
`Localization.en-US.resx` e `Localization.pt-BR.resx`**. Os rótulos das faixas são **dado do perfil**
aprovado (S39 §12.2 `faixas`), não chave de recurso, mas seguem a mesma regra de idiomas do sistema:
cada faixa guarda o rótulo em **português (pt-BR) e em inglês (en-US)**, os dois aprovados juntos, e a
GUI, a exportação CSV e o PDF usam o da cultura da interface, com inglês como padrão (o
`Localization.resx` neutro é inglês). Prefixos:

| Prefixo | Uso |
|---|---|
| `NavRiskOverview`, `AdminRiskIndexHintMSG` | Shell (a razão de permissão dos ícones da Administração reutiliza `NoPermissionForActionMSG`, §6.0) |
| `RiskOverview…`, `RiskExplore…`, `RiskQualityStrip…`, `RiskGovernance…` | Títulos, botões e dicas do módulo, do painel Explorar e das faixas sempre visíveis |
| `RiskProvenance_…`, `RiskMode_…`, `RiskState_…` | Chips de procedência, modo e estados (§5.0, §5.2.3, §5.13) |
| `RiskCategory_‹REG…›_Name`, `RiskCategoryState_…` | Categorias e seus estados |
| `RiskSeal_‹chave›MSG`, `RiskChip_‹chave›MSG` | Selos e chips (texto com argumentos) |
| `RiskTab_…`, `RiskFactor_…`, `RiskVisibility_…`, `RiskSource_‹id›` | Abas, fatores, visibilidade e fontes |
| `RiskEffect_…`, `RiskEvent_…`, `RiskMarker_…`, `RiskAnnotation_…` | "O que mudou", marcadores e anotações |
| `RiskR1NoticeMSG` | Aviso R-1 (§5.8) |
| `RiskProfile…`, `RiskParamGroup_…`, `RiskParam_‹chave›_Label`, `RiskParam_‹chave›_Help`, `RiskValidation_‹código›MSG` | Configuração de perfil |
| `RiskPreview…`, `RiskApproval…`, `RiskApi_‹código›MSG` | Prévia, aprovação e tradução dos códigos de erro da API |
| `RiskSources…`, `RiskContext…`, `EntityMapping…`, `Tenable…` | Demais telas |

A GUI traduz o campo `error` da API por `RiskApi_‹código›MSG` e cai na mensagem do servidor quando a
chave não existe. As chaves dos parâmetros são geradas a partir de `GET /RiskIndex/Profiles/Schema`;
um teste exige que toda chave do esquema tenha `_Label` e `_Help` nos três arquivos (§12.4).

### 10.6 Estilos (`Styles/WindowStyles.axaml`)

| Classe | Uso |
|---|---|
| `Border.riskHeadline`, `TextBlock.riskHeadlineValue`, `TextBlock.riskHeadlineScale` | Cartão e número da manchete |
| `Border.riskBand` + `.b1`…`.b5`, `.indicative`; `TextBlock.riskBandText` + `.b1`…`.b5` | Pílula de faixa (fundo `NrCriticality{n}`, texto `NrCriticality{n}Text`, contraste AA já medido em `Tokens.axaml:110-121`). `n` é o **passo da rampa** `ChartPaletteMap.BandStep(posição, quantidade)`, não a posição: com 3 faixas, as classes são `b1`, `b3`, `b5`. O view-model calcula a classe pela mesma função que o gráfico usa |
| `Border.riskChip` + `.info`, `.warning`, `.unavailable` | Chips |
| `Border.riskSeal` | Selos |
| `Border.riskNotice` | Aviso R-1 |
| `TextBlock.riskDelta` + `.worse`, `.better`, `.neutral` | Linha do Δ 7 dias (sempre com glifo e palavra) |
| `Border.riskPartialBanner`, `Border.riskEmptyState` | Faixa "parcial" e cartões de estado |
| `Border.riskMeter`, `Border.riskMeterFill` | Barra de valor das categorias |
| `Border.visibilitySegment` + `.current`, `.stale`, `.expired`, `.noReading`, `.assessed`, `.presumed`, `.notAssessed` | Barras de visibilidade |
| `TabControl.riskTabs` | Abas de classe |
| `avalonia\|MaterialIcon.riskGlyph` | Tamanho e alinhamento dos glifos (o seletor nomeia `MaterialIcon`, regra S37) |

Botões, pela taxonomia da ui-standard §4.1: `link` (breadcrumb, filtros removíveis, "Voltar ao
último", "Fontes e frescor" no estado indisponível, chip de perfil), `type2` (Atualizar, Exportar,
"+ Filtro" com `Flyout`, Executar prévia, Aplicar preset, Publicar, Ir para), `type3` (Prévia ao vivo,
Comparar, Validar, "Ver como tabela"/"Ver como gráfico", "Ver diferença completa", "Ver prévia
completa", "Ver na tendência", "Abrir na configuração", navegação do detalhe do objeto, Abrir
mapeamentos), `detailButton` (abrir nó ou objeto na linha; o › de selos, categorias, fatores de risco,
itens de qualidade e indicadores de governança), `operation` (novo/excluir rascunho, nova regra, nova
atividade, recarregar o contexto de risco), `subButton` (anexar, nova conexão, limpar chave), `dialog1`
(Salvar, Submeter, Aprovar), `dialog2` (Descartar, Rejeitar, Suspender, Testar, Sincronizar, Excluir,
Fechar). Nenhum elemento clicável fica sem classe (`LintUi` R6).

### 10.7 Glifos (forma além da cor)

| Uso | Glifos (`MaterialIcon Kind`) |
|---|---|
| Faixa, 2 faixas | `CheckCircleOutline`, `AlertOctagonOutline` |
| Faixa, 3 faixas | `CheckCircleOutline`, `AlertOutline`, `AlertOctagonOutline` |
| Faixa, 4 faixas | `CheckCircleOutline`, `AlertOutline`, `AlertRhombusOutline`, `AlertOctagonOutline` |
| Faixa, 5 faixas | `CheckCircleOutline`, `InformationOutline`, `AlertOutline`, `AlertRhombusOutline`, `AlertOctagonOutline` |
| Indicativo / sem dados | `HelpCircleOutline` / `DatabaseOffOutline` |
| Δ pior / melhor / estável | `ArrowTopRightThick` / `ArrowBottomRightThick` / `ArrowRightThin` |
| Visibilidade: atual, obsoleta, expirada, sem leitura | `CheckCircle`, `ClockAlertOutline`, `TimerSandComplete`, `CircleOffOutline` |
| Estados S39: avaliado, obsoleto, presumido, não avaliado | `CheckCircle`, `ClockAlertOutline`, `HelpCircleOutline`, `MinusCircleOutline` |
| Indisponível / não aplicável | `CloudOffOutline` / `Cancel` |
| Marcador / anotação / lacuna / dia indicativo (legenda) | `TriangleDown` / `RhombusOutline` / `CircleOutline` / `SquareOutline` |

### 10.8 Acessibilidade

- Faixa sempre em texto e forma; barras e segmentos rotulados com contagem; nenhuma informação só em
  cor (ui-standard §2.6).
- `AutomationProperties.Name` do cartão: "Índice consolidado de risco 64 de 100, faixa Médio, piorou 4
  pontos em 7 dias, cobertura 92 %".
- Todo gráfico tem a alternativa em tabela com as mesmas informações das dicas: a tendência (§5.3), o
  resumo mensal das abas e do Explorar (§5.5.1) e o tornado da prévia (§6.3). Dica não é o único
  caminho para nenhum dado.
- As séries do resumo mensal se distinguem por rótulo de dado ("EXP 74") e ordem fixa, e as do tornado
  pelo sinal do rótulo ("−8,0" / "+11,9"); a cor é reforço (ui-standard §2.6).
- Os pontos da tendência se distinguem por forma: linha contínua, círculo vazio (lacuna), quadrado
  vazio (indicativo), triângulo (marcador), losango (anotação).
- Teclado: ordem de Tab explícita (barra superior → cartão → tendência → categorias → abas →
  painéis); árvore com setas, Enter e Alt+←; Ctrl+F na árvore e na lista de objetos (IX-8); Esc fecha
  o detalhe do objeto.

### 10.9 Desempenho

- Leitura só de snapshot e linhas de objeto; o cliente não recalcula nada.
- Painéis carregam em paralelo; cada um com o seu estado de ocupado.
- Cálculo derivado no servidor sob orçamento (§8.1): 20 s por cálculo, 2 simultâneos por usuário e 4
  por processo; resumo mensal de nós pré-calculados lido de `risk_index_class_days`, sem varrer linhas
  de objeto.
- Lista de objetos paginada no servidor (50; máximo 500) sobre `DataGrid` virtualizado; árvore com
  carga sob demanda ao expandir; tendência com no máximo 365 pontos.
- Cache no cliente por (nó, data) dos últimos 20 nós visitados, para voltar no breadcrumb sem
  requisição; o servidor cacheia por escopo (§8.1).
- A única consulta periódica é o estado da prévia (5 s, só com a aba aberta e a prévia em execução).

---

## 11. Segurança e autorização

### 11.1 Permissões e semeadura

| Permissão | Concede | Semeadura |
|---|---|---|
| `risk_index_view` | Painel e API de leitura; leitura de perfis, e, com escopo irrestrito, de prévias, pontes e histórico (é também a leitura da terceira linha até o M47, S39 §10.4). Dá o **valor** do índice, não o conteúdo dos registros de outros módulos (redação, §11.3) | `Data/88.sql`, `INSERT IGNORE` sem id |
| `risk_index_configure` | Rascunho, validação, prévia, submissão, anotações, edição de fontes | idem |
| `risk_index_approve` | Aprovar, rejeitar, anexar evidência, suspender, publicar; ver o índice durante o piloto em sombra (S39 §13.3) | idem |
| `entity_risk_context` | `criticality`, `internetFacing`, `securityClassification` (T241), em criação e edição, **com escopo irrestrito** (§6.7, §8.5) | idem |
| `entities_manage` | Criar, editar e excluir entidades (`POST`/`PUT`/`DELETE /Entities`), a permissão de CRUD da T241 | idem |
| `configuration` (existente) | Tenable, mapeamentos, opções novas de Vision One e SSC (API; na GUI o painel Integrações segue só de administrador, §6.0) | — |

**Semeadura de papéis (T293, decisão do product owner de 2026-10-05).** O aprovador do perfil é o
**Gerente de Riscos** ou o **Administrador de Riscos**, que representam o Comitê de Risco de TI no
sistema. Hoje só existem os papéis `Administrator` e `RiskAnalyst` (`Data/1.sql:294-295`), então o
`Data/88.sql`:

- cria os papéis `RiskManager` (Gerente de Riscos) e `RiskAdministrator` (Administrador de Riscos)
  **só se não houver papel com esse nome** (`INSERT … SELECT … WHERE NOT EXISTS`), com `admin = 0`;
- concede a eles `risk_index_view`, `risk_index_configure`, `risk_index_approve` e
  `entity_risk_context` em `role_responsibilities`, buscando os ids por nome e por `key`
  (`INSERT IGNORE`), nunca por id literal;
- concede a `RiskAnalyst` `risk_index_view` e `risk_index_configure` (o autor de segunda linha);
- **não** concede nada ao `Administrator`: a aprovação exige a permissão explícita (§11.4), e o
  administrador do sistema não aprova pesos por ser administrador.

**Público na publicação (P-7, 2026-10-05):** o painel fica, por ora, com a equipe de riscos (analistas,
gerentes e administradores de riscos). Nenhum outro papel recebe `risk_index_view` na publicação;
gestores de unidade e diretoria entram depois, por concessão na tela de papéis, sem mudança de código
(o escopo hierárquico da T292 já limita cada um à sua subárvore).

Uma instalação que já tem papéis próprios para essas funções concede as mesmas permissões a eles pela
tela de papéis; os papéis semeados podem ficar sem usuários. Como o Gerente e o Administrador de Riscos
também podem ser autores, a segregação de funções da §11.4 continua impedindo que aprovem o próprio
rascunho. Quem recebe `risk_index_configure` ou `risk_index_approve` recebe também `risk_index_view`,
porque as ações de escrita do `RiskIndexController` somam a permissão da classe (§8.2); a Administração
não depende dessa regra e desabilita, com a razão, as abas de leitura de quem não tem `risk_index_view`
(§6.0). O papel Admin passa em todas por `PermissionAuthorizationHandler.cs:26-34`, exceto na aprovação,
que exige `risk_index_approve` explícita (§6.4, §11.4). No piloto em sombra, o índice é visível ao papel
Admin e a quem tem `risk_index_approve` explícita (o comitê), como diz a S39 §13.3 (§8.2).
Conceder ou revogar as permissões `risk_index_*`, `entity_risk_context` e `business_risk_review`
grava linha explícita em `audit_logs` com o autor (§6.4).

### 11.2 Inventário de autorização

Toda ação nova tem `[Authorize]` ou `[PermissionAuthorize]` na classe ou no método (controladores
`RiskIndexController`, `RiskIndexProfilesController`, `TenableController`, `EntityMappingsController`
e as ações novas e alteradas de `EntitiesController`). `ControllerAuthorizationInventoryTest` cobre
todos sem edição, e **nenhum** `[AllowAnonymous]` é acrescentado.

Esse teste só verifica que **algum** atributo existe (`ControllerAuthorizationInventoryTest.cs:104-176`),
e os testes de controller chamam os métodos diretamente, sem passar pelos atributos. Um atributo errado
(por exemplo, `Approve` sob `risk_index_configure`, ou `EntityMappings` sob `RequireValidUser`)
passaria por todos eles. Por isso entra `RiskIndexEndpointPermissionMapTest` (API.Tests, §12.3): por
reflexão sobre `RiskIndexController`, `RiskIndexProfilesController`, `TenableController`,
`EntityMappingsController` e as ações novas e alteradas de `EntitiesController`, compara o conjunto
exato de políticas e permissões (classe ∪ método) de cada ação com uma tabela copiada das §8.2 e
§8.4–§8.7, e falha se alguma ação nova se apoiar só em `RequireValidUser`, exceto as declaradas
(`GET /Entities/RiskContext` e `GET /Entities/{id}/History`). As regras de serviço que o atributo não
expressa (escopo irrestrito, sombra, permissão explícita) têm teste próprio na §12.

### 11.3 Escopo, sombra e redação

| Regra | Implementação | Teste |
|---|---|---|
| **Escopo hierárquico** (S39 §10.4, T292): claim numa entidade concede os descendentes por `entities.parent`, nunca ascendentes nem irmãos; vale para leitura e para a guarda de escrita; vínculo só-EAV não concede | `DALService.GetCurrentEntityScope` expande as claims por um `IEntityHierarchy` (fecho de descendentes em cache, invalidado por criação, troca de pai e exclusão de entidade, com proteção contra ciclo), então uma troca de pai vale sem novo login. O fecho de descendentes é o mesmo componente que o serviço do ICR usa para a parte de árvore do fecho do nó (S39 §10.1) | `EntityScopeHierarchyTest` e `DAL.IntegrationTests` (§12.2, §12.7) |
| Snapshot oficial só se claims ⊇ **membros(nó) ∪ entidades de escopo dos objetos atribuídos ao nó no dia**, ou escopo global | `RiskIndexReadService` compara as claims com as linhas `Closure` **e** `Scope` de `risk_index_node_members` do dia (§9.3). Comparar só com o fecho deixaria passar o snapshot oficial a quem cobre as entidades do nó mas não a entidade de escopo de um objeto atribuído por `organizationUnit`, `applications` ou união de cenários (S39 §10.2–§10.3), e `top_contributions`, "sustentado por", "o que mudou" e as contagens nomeariam esse objeto | `RiskIndexReadServiceTest` (aplicação sob a Unidade Y atribuída à Unidade X: claims só no fecho de X → parcial) |
| Senão, parcial: fórmulas sobre linhas de objeto filtradas por escopo; sem tendência, sem "o que mudou", sem contagem oculta | Filtros da §7.4 + serviço | idem, com objetos ocultos semeados |
| Manchete da organização exige escopo global | `403 org_requires_global_scope` | `RiskIndexControllerTest` |
| Valores de perfil (prévia, ponte, histórico) exigem escopo global | `403 profile_values_require_global_scope` em `GET /{id}/Preview`, `/{id}/Bridge`, `/{id}/History`; `GET /{id}` e `/Active` não trazem o relatório (§8.4); o diálogo do chip de perfil e a aba Aprovação desabilitam as seções com a razão | `RiskIndexProfilesControllerTest`, `RiskIndexProfilesServiceTest` (usuário escopado: 403 e nenhum nó ou objeto na resposta) |
| `Sources` e `Runs` não revelam totais fora do escopo | `ObjectCount`/`StaleFraction` nulos e `403 runs_require_global_scope` para escopo restrito; `Dimensions` conta só o visível | `RiskIndexControllerTest`, `RiskIndexReadServiceTest` com objetos ocultos semeados |
| Indicadores de tenant só irrestrito ou no nó da conexão | Serviço | `RiskIndexReadServiceTest` |
| **Redação por módulo** | `RiskIndexRedactionPolicy` (puro, `Model.RiskIndex`) aplicado pelo serviço depois do filtro de escopo, em toda resposta que nomeia objeto (manchete, categorias, "o que mudou", contribuições, objetos, detalhe, prévia, CSV): sem a permissão do módulo dono (`riskmanagement` para cenários, `hosts` para hosts, `vulnerabilities` para achados e sinais de achado, `incident_management` para incidentes, `assessments` para avaliações; o papel Admin passa), o rótulo vira "‹Tipo› #id" com `RequiresPermission`, e `Scenario`, `DecisionState`, `Signals[].Text`/`Link` e os destinos de navegação desse módulo saem. Valores do índice (R, pontos, categoria, estado, população, peso) ficam | `RiskIndexReadServiceTest` (usuário só com `risk_index_view`, sem `riskmanagement`: nenhum título de cenário, entrada de cenário ou estado de decisão na resposta) |
| Cache por (nó, data, perfil, modo, hash das claims, hash das permissões de módulo) | `IRiskIndexReadCache` (§8.1) | Dois usuários com escopos diferentes recebem resultados diferentes no mesmo nó; dois usuários com o mesmo escopo e permissões de módulo diferentes também |
| PDF só com escopo global e só pela exportação | `403 pdf_requires_global_scope`; `POST /Reports` com tipo 4 → `400 report_type_not_creatable` | `RiskIndexControllerTest`; `ReportsControllerRiskIndexTypeTest` (usuário escopado sem `risk_index_view` e, em `shadow`, usuário com `risk_index_view` sem `risk_index_approve`: nenhum dos dois cria o relatório tipo 4) |
| Piloto em sombra: só comitê (`risk_index_approve` explícita) e Admin veem o índice | `403 risk_index_in_shadow` nas ações de valor do `RiskIndexController` (§8.2); prévia e ponte também para o autor e os editores do próprio rascunho com `risk_index_configure` (S39 §13.3), verificado no serviço (§8.4) | `RiskIndexControllerTest` (um caso por papel: só view, configure, approve, Admin); `RiskIndexProfilesServiceTest` e `RiskIndexProfilesShadowReadTest` (em sombra, autor ou editor lê a prévia e a ponte do próprio rascunho; outro detentor de `risk_index_configure` recebe `403 risk_index_in_shadow`) |
| Anexo do perfil | `FileAccessAuthorizer.RequiredPermissionAsync` ganha o ramo `RiskIndexProfileId → risk_index_view` e, para o perfil, a regra de escopo irrestrito e de sombra das leituras de valor; sem isso, o arquivo seria "sem pai" e só o autor do upload o leria (`FileAccessAuthorizer.cs:46-115`). `POST /{id}/Attachments` só vincula arquivo enviado pelo próprio usuário e sem nenhum outro vínculo | `FileAccessAuthorizerRiskIndexTest` (novo, ServerServices.Tests, no padrão de `Track8/DeferredSecurityFixesInMemoryTest`); `RiskIndexProfilesServiceTest` (revincular anexo de risco → `409 file_already_attached`; arquivo de outro usuário → `403 file_not_owned`) |
| Contexto de entidade exige escopo global para escrever | `403 risk_context_requires_global_scope` no serviço (§8.5) | `EntityRiskContextServiceTest` |

### 11.4 Segregação de funções na aprovação

Sem bypass de administrador (S39 §13.1): o aprovador não pode ser o autor nem quem editou o rascunho
(linhas de auditoria do perfil); não pode deter `business_risk_review` nem ser revisor nomeado em
`entity_risk_reviewers`; e precisa ter `risk_index_approve` **explícita**, isto é, presente em
`IPermissionsService.GetUserPermissionsAsync(user)` lido do banco (papel ∪ concessões diretas,
`PermissionsService.cs:69-92`), ignorando a flag `Admin` e as claims do token, porque
`PermissionAuthorizationHandler.cs:26-34` aprova qualquer permissão para o papel Admin; essa permissão
tem de vir de um papel listado em `risk_index_approver_roles` (`approver_role_required`). O papel que a
sustentou vai para `approval_grant_source`; a concessão feita pelo próprio aprovador é recusada
(`sod_self_granted_approver`), o que exige que conceder e revogar essas permissões grave linha
explícita em `audit_logs` (§6.4). Códigos de recusa e textos na §6.4; a elegibilidade é exposta antes
do clique por `GET …/ApprovalEligibility` (§8.4).

### 11.5 O ICR não é entrada de decisão

- **`IcrIsNotADecisionInputTest`** (`ServerServices.Tests`, T247): por reflexão sobre **todos os
  projetos não de teste de `src/netrisk.sln`**, exceto o `GUIClient` (coberto pela varredura de fonte
  abaixo, porque carregá-lo puxaria Avalonia), inspeciona campos, propriedades, parâmetros de
  construtor e de método e tipos de retorno. A lista de assemblies vem da leitura do `.sln`, não de uma
  lista escrita à mão, e o teste **falha** quando um assembly listado não pode ser carregado do
  diretório do teste: uma referência faltando aparece em vermelho, não como varredura mais estreita.
  Para isso o `ServerServices.Tests.csproj` ganha `ProjectReference` para `BackgroundJobs`,
  `ConsoleClient`, `ClientServices`, `RiskPortal`, `SharedServices`, `Tools`, `WebSite`, `Model`, `DAL`
  e os projetos de `Plugins` (hoje referencia só `API`, `ServerServices` e o plugin de fixture,
  `ServerServices.Tests.csproj:21-33`). Só podem referenciar `ServerServices.RiskIndex.*`,
  `Model.RiskIndex.*` ou as entidades `RiskIndex*`:
  - os próprios `ServerServices.RiskIndex.*`, `Model.RiskIndex.*` e as entidades/configurações do DAL
    (`NRDbContext` e seus mapeamentos);
  - `BackgroundJobs.Jobs.RiskIndex.*` (snapshot, retenção, prévia);
  - `RiskIndexController`, `RiskIndexProfilesController` e `ServerServices.Reports.RiskIndexPdfReport`
    (o `ReportsService` não: ele só recusa o tipo 4 pelo número, §5.14);
  - as raízes de composição (`ServicesBootstrapper`, `ConfigurationManager`);
  - `ClientServices` `IRiskIndexService`/`RiskIndexRestService` e `IRiskIndexConfigService`/
    `RiskIndexConfigRestService`.

  Qualquer outro tipo falha, nomeadamente serviços de governança, workflow, aceitação, campanha, SLA,
  notificação, KRI, portões e o RiskPortal (até a T289, que entrará como leitor nomeado). A allowlist
  é a da S39 §1.3: o serviço e os seus jobs (snapshot, retenção, prévia), os controllers do painel, de
  perfis, de exportação e de relatórios, a camada REST do `ClientServices` (leitura do índice e ciclo
  de vida do perfil), as raízes de composição e as telas do `GUIClient`; "cliente" proibido é quem
  decide com base no número.
- **GUI** (`GUIClient.Tests`, por varredura de fonte, porque o projeto não referencia Avalonia):
  `RiskIndexReferenceScopeTest` (só `ViewModels/RiskOverview`, `ViewModels/Admin/RiskIndex`,
  `Tools/RiskIndex`, os diálogos da §10.1 e o bootstrapper referenciam `Model.RiskIndex` e
  `IRiskIndexService`); `RiskOverviewHasNoDecisionActionsTest` (todo `Command` das views do módulo
  pertence a uma allowlist de leitura e navegação); `RiskOverviewDeltaColumnsNotSortableTest`.

### 11.6 Credenciais e saída HTTP (Tenable)

- Chaves digitadas guardadas por `ISecretProtector.Protect` (AES-GCM sob `IMasterKeyProvider`); a
  coluna também aceita referência `vault:v1:…`. `ClearAccessKey`/`ClearSecretKey` apagam a chave salva
  (§8.3, §8.7).
- Consumo **sempre** por `ISecretResolver.ResolveAsync` a cada uso, nunca `Unprotect` (CLAUDE.md, Track 7).
- `encrypted_access_key` e `encrypted_secret_key` entram nos **dois** registros de colunas de credencial:
  `SecretVaultService.CountReferencesAsync` (`SecretVaultService.cs:772-786`; sem isso, apagar uma
  conexão de cofre em uso seria permitido) e `SecretReferenceNormalizer.NormalizeAsync`
  (`SecretReferenceNormalizer.cs:47-71`, cujo comentário diz que precisa andar junto com o primeiro;
  sem isso, `netrisk-console vault normalize-references` nunca reescreveria as referências da Tenable).
  Cada um com teste de regressão que falha antes da edição, e um teste novo,
  `SecretColumnRegistriesAgreeTest`, exige que os dois registros enumerem o mesmo conjunto de colunas.
- Toda chamada por `IOutboundHttpClient`; política de SSRF da `OutboundUrlPolicy`; certificado
  inválido só no Security Center (T269, backlog), em `formCard.caution`.
- **Nenhum endpoint devolve segredo** (§8.7) e as credenciais ficam fora da auditoria (§7.5). **Não
  divulgação em log**, com o mecanismo e o teste que a estabelecem: o `TenableClient` monta `X-ApiKeys`
  / `x-apikey` só no `HttpRequestMessage` e nunca registra cabeçalhos de requisição; mensagens de erro,
  a trilha de progresso, `summary`/`progress_log` do `integration_sync_logs` e `last_sync_error` são
  montados a partir do status e do corpo da **resposta** da Tenable, nunca do pedido; falha de
  resolução do cofre registra o nome da referência, não o valor. `TenableServiceTest` (§12.2) usa um
  `ILogEventSink` de teste do Serilog e o `FakeOutboundHttpClient` respondendo 401, 403, 429 e erro de
  transporte, e exige que nem o valor das chaves nem o texto do cabeçalho apareçam nos eventos de log,
  nas linhas de `integration_sync_logs` nem na linha da conexão.

### 11.7 Auditoria

- `RiskIndexProfile` campo a campo (S39 §12.5): criação, cada edição de rascunho, submissão,
  aprovação (com `approval_grant_source`), rejeição, suspensão (com `retire_*`).
- Concessão e revogação de `risk_index_*`, `entity_risk_context` e `business_risk_review`, no papel ou
  ao usuário: linha explícita `PermissionGrant` com o autor (§6.4).
- Contexto de entidade com a justificativa ligada por `CorrelationId` (§7.5).
- Regras de mapeamento e mudanças de entidade das conexões.
- Recarimbos de host pela trilha de `Host`.
- Mudança de publicação: registrada em log do servidor (Serilog) até a T283 trazer `settings` para a
  auditoria (S39 D-22).

### 11.8 Casos de abuso considerados

| Abuso | Contenção |
|---|---|
| Baixar o índice mexendo em criticidade, internet-facing ou classificação (Goodhart) | Permissão própria, justificativa obrigatória, auditoria, e o efeito aparece em "o que mudou" como **contexto, com autor** (S39 §4.6 e) |
| "Aceitar para ficar verde" | Aceite nunca baixa REG nem EXP (S39 I10); o painel não tem ação de aceitar |
| Inferir objetos fora do escopo por contagens | Visão parcial conta só o visível; nenhum "n de N" com N oculto |
| Vazar PDF de unidade pela listagem de relatórios | PDF só com escopo global (§5.14) |
| Gerar o PDF do índice por `POST /Reports` (só `RequireValidUser`), sem permissão, com escopo restrito ou durante a sombra | `ReportsService.CreateAsync` recusa o tipo 4; só `GET /RiskIndex/Export` o cria, depois das checagens (§5.14) |
| Agendar o relatório do índice para endereços externos | Tipo 4 fora do `ScheduledReportJob` (§5.14) |
| Ler registros de outros módulos pelo índice (títulos de cenário, aceitações, achados, incidentes) sem a permissão do módulo | Redação por módulo e cache chaveado pelas permissões (§11.3) |
| Ler valores da organização pelo relatório de sensibilidade ou pela ponte com escopo restrito | Escopo irrestrito obrigatório (§8.4) |
| Revincular um anexo de risco ou incidente ao perfil para lê-lo | Só arquivo próprio e sem vínculo (§11.3) |
| Admin que se concede `risk_index_approve` para aprovar | Permissão explícita, origem gravada, concessão auditada e recusa da autoconcessão (§11.4) |
| Mover a criticidade (peso) pelo ACR da Tenable sem aprovação | Coletar ACR só guarda a leitura; o uso exige TEN-ACR no perfil aprovado (§6.8) |
| Criar entidade já com criticidade 5 para fugir da trava do `PUT` | A checagem vale no `POST` e em todo caminho do `EntitiesService` (§8.5) |
| Mudar a criticidade de outra unidade com escopo restrito | Escrita de contexto exige escopo irrestrito (§6.7) |
| Envenenar cache entre escopos | Chave com hash das claims (§8.1) |
| Esgotar recursos com prévias | Um pedido por perfil; fila no job com reivindicação atômica; só `risk_index_configure` |
| Esgotar recursos com nós derivados | Orçamento de 20 s, 2 por usuário e 4 por processo, `503` com `Retry-After` (§8.1) |
| Injeção de fórmula no CSV por rótulos de fornecedor | Neutralização de `=`, `+`, `-`, `@` (§5.14) |
| Exportação massiva | CSV de objetos limitado a 5.000 linhas, com aviso de truncamento |

---

## 12. Plano de testes

Frameworks e convenções do `src/AI_TESTING_INSTRUCTIONS.md`: xUnit v3, NSubstitute, contêiner de DI
por projeto, nada de rede ou banco real fora de `DAL.IntegrationTests`. Cada correção de defeito tem um
teste que falha antes e passa depois.

### 12.1 Motor de referência e números de ouro (`ServerServices.Tests`)

`RiskIndexEngineGoldenTest` reproduz os números da S39 com tolerância de 0,01 nos valores de duas
casas:

| Caso | Valores esperados |
|---|---|
| S39 §4.7, caso canônico | média ponderada atual 26,0; aritmética 13,8; média pura 18,9; `M_4` 52,0; piso 72,0; `C_EXP` 72; 200 VMs → `M_4` 30,1 e `C_EXP` 72; 2.000 VMs → 17,4 e 72; inventário duplicado → 72; servidor 90 → 35 → `M_4` 20,5, piso 28,0, `C_EXP` 28; VMs corrigidas → 72 "sustentado por SRV"; servidor sozinho → manchete 50,4 |
| S39 §11.1–§11.2, Unidade X | `M_4(Aval)` EXP 54,53 (54,31 e 53,91 com presumidos); `C_EXP` 74,25; `C_AME` 60,00 (`M_4` 53,27); `C_REG` 67,73 (`M_4` 66,90); `C_GOV` 67,20 (`M_4` 52,80); `C_CTL` 20,16 (`M_4` 17,53); contribuições 20,32 / 22,27 / 9,00 / 2,02 / 10,08; **ICR\* 63,69 → 64 Médio**; `cov_EXP` 0,914, `cov_REG` 0,833, `cov_GOV` 0,974, COV 0,920, `fresh` 0,993, qualidade 0,914 (Alta); intervalo [63,69; 65,42]; sensibilidade −4,32, −2,88, −0,67, −0,45, −4,50 |
| S39 §11.3, Exemplo C | `C_EXP` 72,00, `C_GOV` 48,00, `C_REG` 67,20; ICR\* 63,69 → 59,98 (Δ −3,71); atribuição −2,88 / −0,67 / −0,16 / 0,00 e resíduo 0,00 (0,02 sem a divisão em trechos) |
| S39 §11.4, Exemplo D | P-Matrícula 84,13 (78,88 com o presumido); P-Pesquisa 75,71 (74,25); Lab L1 28,00 (10,00 sem presunção); Unidade X 74,25 |
| S39 §11.5, Exemplo E | ICR\* 70,23 → 70 Alto; COV 0,670; intervalo [52,67; 79,40] |
| S39 §11.6 | Presets 63,7 / 65,8 / 64,0 / 71,7 / 33,0 e caso canônico 72,0 / 72,0 / 72,0 / 81,0 / 18,9; tornado τ −7,99 / +11,94, σ de R1 −5,11 / +5,98, ω_CTL +0,97 / −0,97, ω_EXP −0,90 / +0,90, ω_REG −0,35 / +0,35, p 0,00 / +0,25, ρ −0,13 / +0,13 |

E as propriedades da S39 §4.6 como testes de propriedade sobre conjuntos gerados: limitado em
[0, 100]; piorar um R nunca baixa ICR\*; duplicar o inventário não muda o valor; `ICR* ≥ 56·R_o` com
o padrão; arredondamento meio-para-longe-de-zero (63,5 → 64) e nunca "70 Médio".

### 12.2 `ServerServices.Tests` — serviços

Os testes sensíveis a escopo (`RiskIndexReadServiceTest`, `EntityRiskContextServiceTest`,
`RiskIndexProfilesServiceTest` nos casos de escopo, `FileAccessAuthorizerRiskIndexTest`) derivam de
`InMemoryServiceTestBase` e usam `ScopeTo(...)`/`ScopeToEverything()` e `SeedUnscoped(...)`
(`InMemoryServiceTestBase.cs:180-191`), porque o `MockDalService` não aplica os filtros de escopo.

| Classe | Caminho feliz | Guardas e erros |
|---|---|---|
| `EntityScopeHierarchyTest` (T292; regressão que falha no código de hoje) | Claim numa unidade vê riscos, achados, hosts, incidentes, avaliações e aceitações das subunidades e dos processos e atividades filhos na árvore; escrita numa subunidade é aceita | Claim numa subunidade **não** vê a unidade-mãe nem as irmãs; processo ligado só por `organizationUnit` (EAV) não é concedido; troca de pai move o acesso sem novo login (cache invalidado); ciclo de pais não trava nem amplia o escopo; claims vazias continuam `DenyAll`; escopo global e jobs (T237) continuam irrestritos; escrita fora da subárvore lança `EntityScopeViolationException` |
| `RiskIndexProfileRulesTest` | Perfil padrão válido | Cada regra estrutural e entre campos da S39 §12.4; τ 0,60 e τ_G 0,50 recusados (21 < 31); τ 0,60 exige τ_G ≥ 0,74; preset Registro exige τ·τ_G ≥ 0,45; travados imutáveis; Média pura não ativável; regras de ritmo devolvem `RequiresRebaseline` com os gatilhos e **não** invalidam; primeiro perfil de todos sem travas de ritmo e com `RequiresBoardRef`; ±0,05 contra a linha de base do trimestre civil; **sem perfil vigente no 1º dia do trimestre, o segundo perfil aprovado no trimestre conta contra o primeiro aprovado nele**; troca de modo devolve `mode_change`; vetor ω conta como um parâmetro; rebaseline libera a regra dos dois parâmetros (S39 §12.4) |
| `RiskIndexProfilesServiceTest` | Rascunho → submissão → aprovação → vigência no snapshot seguinte; anterior aposentado | Um rascunho aberto; `stale_draft` por `revision`; `PUT` depois de uma prévia concluída **não** dá 409; excluir rascunho com prévia apaga as prévias; submissão sem prévia, com prévia desatualizada, sem justificativa; **rascunho com ω_REG +0,10 é submetido (`RequiresRebaseline`) e só aprovado com `confirmRebaseline`**, idem com três parâmetros; os `sod_*`/`approver_permission_not_explicit` (incluindo **Admin sem a permissão explícita**, **editor que não é autor**, **permissão direta ao usuário aceita** e **concessão feita pelo próprio aprovador recusada**); `ApprovalEligibility` e `Approve` concordam em cada caso; `approval_grant_source` gravado; comitê e anexo; Conselho quando faixas mudam e no primeiro perfil; rejeição sem motivo; suspensão só de Active, **preservando `justification` e `committee_decision_ref`** e gravando `retire_*`; anexo: revincular arquivo de risco → `409 file_already_attached`, arquivo de outro usuário → `403 file_not_owned`; usuário escopado não lê prévia, ponte nem histórico (403, e nenhum nó ou objeto na resposta); **em `shadow`, detentor de `risk_index_configure` que não é autor nem editor do rascunho não lê a prévia nem a ponte dele (`risk_index_in_shadow`)**; cada transição gera linhas de auditoria |
| `RiskIndexReadServiceTest` | Snapshot oficial do nó | Organização sem escopo global; raiz "Meu escopo"; parcial sem contagem oculta (objetos ocultos semeados); **objeto atribuído ao nó com entidade de escopo fora das claims → parcial**; tendência e "o que mudou" indisponíveis em parcial; indicadores de tenant ocultos; `Sources` sem contagens e `Runs` recusado para escopo restrito; `Dimensions` só com o visível; **redação: usuário só com `risk_index_view` não recebe título de cenário, entradas de cenário, estado de decisão nem texto de sinal**; cache separado por escopo **e por permissões de módulo**; publicação em sombra (um caso por papel); nó derivado sem 365 d; dia indicativo com `DisplayState` e intervalo; orçamento derivado (`503` acima do limite) |
| `RiskIndexEngineGoldenTest` (acréscimo) | — | TenableAcr coletado mas TEN-ACR fora de `fontes_habilitadas` deixa `w_o` inalterado; com TEN-ACR habilitada, `crit = ⌈ACR/2⌉` abaixo de manual e CMDB |
| `EntityClosureResolverTest` | Árvore + `organizationUnit` multivalorada + atividades + aplicações | Vínculo divergente sinalizado; aplicação órfã; Não atribuído; membros `Scope` gravados |
| `ChangeAttributionTest` | Exemplo C | Objeto que entra e sai; troca de argmax; marcador vira efeito de configuração; resíduo publicado; objeto reatribuído leva `IsReattribution` sem mudar o valor |
| `IcrIsNotADecisionInputTest` | Allowlist atual passa | O próprio verificador acusa um tipo de teste que referencia `IRiskIndexReadService`; falha se um projeto do `.sln` não pode ser carregado |
| `VendorEntityMappingServiceTest` | Regra de tag atribui host | Prioridade e conflito; "só não atribuídos" não toca host atribuído; "seguir" reatribui e gera auditoria de `Host`; grupo de ativos não atribui dispositivo; prévia com contagens corretas |
| `TenableNormalizerTest` | Registro no formato documentado do export | VPR nulo sem CVE cai em CVSS; FIXED → mitigado; EPSS na coluna; AES em escala 1000; o normalizador não grava `hosts.criticality` |
| `TenableServiceTest` | Sincronização completa com cursor | Cursor não avança em falha; 429 com `Retry-After`; 409 reutiliza export; limite de resposta; usa `ISecretResolver`; cabeçalho SC; validação de URL; visão sem chaves; `ClearAccessKey`/`ClearSecretKey` apagam e a omissão mantém; **não divulgação**: com 401, 403, 429 e erro de transporte do `FakeOutboundHttpClient`, nenhum evento do `ILogEventSink` de teste, nenhuma linha de `integration_sync_logs` e nenhuma coluna da conexão contém o valor das chaves nem o texto do cabeçalho; **pedido manual de usuário escopado** ainda casa por id externo um host fora do escopo dele (roda sob o principal global) |
| `SecretVaultServiceInMemoryTest` (acréscimo) | `CountReferencesAsync` conta as duas chaves Tenable | Regressão: falha antes da edição do registro |
| `SecretReferenceNormalizerTest` (acréscimo) e `SecretColumnRegistriesAgreeTest` | Uma referência Tenable é listada e reescrita | Os dois registros enumeram o mesmo conjunto de colunas |
| `FileAccessAuthorizerRiskIndexTest` | Anexo de perfil legível com `risk_index_view` e escopo global | Sem a permissão → 403; escopo restrito → 403; em sombra, só comitê e Admin |
| `ReportsServiceRiskIndexTypeTest` | — | `CreateAsync` com tipo 4 lança e nada é gravado |
| `VendorScoreStoreTest` | Valor igual estende o trecho | Valor diferente abre trecho; leitura vigente correta |
| `TrendMicroPostureSyncTest` | `securityPosture`, grupos, superfície e alertas a partir de fixtures do OAS | **D-09**: sincronização não grava `entities.cyber_risk_index`; **D-11**: tentativas e atividade global gravadas separadas; **D-10**: `criticality_source` e teto de origem Vision One |
| `SecurityScorecardBackfillTest` | Histórico com `is_backfill` | Reaplicação idempotente; **D-12**: fator ausente gravado como NULL; resumo de issues guardado |
| `EntityRiskContextServiceTest` | Lote com justificativa e notas no mesmo `SaveChanges`, um `CorrelationId` | Sem permissão; escopo restrito → `risk_context_requires_global_scope`; campo não suportado no tipo; justificativa curta; **uma linha inválida desfaz o lote inteiro**; **D-17**: `PUT /Entities/{id}` com mudança de criticidade sem permissão é recusado, e `POST /Entities` com `criticality = 5` sem permissão também |
| `ConnectionAuditNeverRecordsCredentialsTest` | Mudança de entidade da conexão auditada | Nenhuma linha de auditoria contém campo `Encrypted*` |
| `RiskIndexCsvTest`, `RiskIndexPdfReportTest` | Seções completas | Marcação "parcial"; faixa sempre em texto; rótulos que começam com `=`, `+`, `-` e `@` saem neutralizados; rótulos redigidos para quem não tem a permissão do módulo |
| `PermissionGrantAuditTest` | Conceder `risk_index_approve` a um papel grava `PermissionGrant` com o autor | Revogar também; concessão direta ao usuário também |
| `RiskIndexSnapshotPerformanceTest` e `RiskIndexDerivedReadPerformanceTest` (`[Fact(Explicit = true)]` do xUnit v3: só rodam quando pedidos, nunca na execução padrão) | 50 mil objetos sintéticos; resumo mensal de 12 meses de um nó derivado sobre 35 mil linhas/dia | Snapshot em menos de 10 min; atribuição dentro do orçamento; leitura derivada dentro dos 20 s |

### 12.3 `API.Tests`

Cada serviço novo resolvido por controller ganha uma fábrica estática `Create()` em
`API.Tests.Mock`, que a `ServiceRegistration` registra por convenção: `MockedRiskIndexReadService`,
`MockedRiskIndexProfilesService`, `MockedEntityRiskContextService`, `MockedVendorEntityMappingService`,
`MockedTenableService` e `MockedRiskIndexReportService`.

| Classe | Cobertura |
|---|---|
| `RiskIndexControllerTest` | Cada ação da §8.2: sucesso, `X-Total-Count`, `400` (nó, dimensão, janela, período, formato, filtro Gridify), `409` (mapeador, parcial), `403` (sombra por papel, organização, PDF, prévia ao vivo, `Runs`), `404`, `503` do orçamento derivado |
| `RiskIndexProfilesControllerTest` | Cada ação da §8.4 e cada código de erro, incluindo `ApprovalEligibility`, `profile_values_require_global_scope` e `risk_index_in_shadow` em prévia e ponte |
| `RiskIndexProfilesShadowReadTest` | Em `shadow`, sobre `InMemoryDalService`: o autor e o editor do rascunho, com `risk_index_configure`, leem `GET /{id}/Preview` e `GET /{id}/Bridge` desse rascunho; outro detentor de `risk_index_configure` recebe `403 risk_index_in_shadow` nos dois; `risk_index_approve` explícita e Admin leem qualquer perfil (S39 §13.3) |
| `EntitiesControllerRiskContextTest` | §8.5, incluindo o `403 risk_context_requires_permission` no `PUT` e no `POST` existentes e o lote |
| `EntityMappingsControllerTest`, `TenableControllerTest` | §8.6, §8.7 (o `sync` devolve 202 sem executar e `409 sync_running` com pedido na fila) |
| `ReportsControllerRiskIndexTypeTest` | `POST /Reports` com tipo 4 → `400 report_type_not_creatable`, para usuário escopado sem `risk_index_view` e para usuário com `risk_index_view` em `shadow` |
| `ControllerAuthorizationInventoryTest` | Passa sem edição; nenhum `[AllowAnonymous]` novo |
| `RiskIndexEndpointPermissionMapTest` | Conjunto exato de políticas e permissões de cada ação nova ou alterada contra a tabela das §8.2 e §8.4–§8.7 (§11.2) |
| `RiskIndexSnapshotTablesHaveNoWriteEndpointsTest` | Nenhuma ação com PUT/PATCH/DELETE recebe ou devolve snapshot, linha de objeto ou atribuição (S39 §12.5) |
| `RiskIndexObjectSortMapTest` | O mapa de filtros de objetos não declara ordenação por sensibilidade ou Δ |

Controladores que leem o banco diretamente usam `InMemoryDalService` com base própria por classe.

### 12.4 `GUIClient.Tests` (lógica pura e varredura de fonte)

| Classe | O que garante |
|---|---|
| `AdminPaneAccessTest` | Regras da §6.0 para administrador, configurador, aprovador, contexto e usuário comum; **usuário só com `entity_risk_context`** (só Contexto de risco habilitada, demais com "Requer risk_index_view"); configurador e aprovador **sem** `risk_index_view`; mapeamento `AdminTarget` → painel/aba e alvo não permitido |
| `UnsavedChangesGuardTest` | Fechar, Esc, trocar de painel e trocar de aba com e sem alterações; recusar reverte a seleção |
| `RiskSourcesEnablementTest` | Controles de Fontes desabilitados com perfil Em aprovação e com a razão certa; Salvar compartilhado com o editor |
| `DrillStateTest` | Precedência da §5.6.2 (dimensão remove chip do mesmo tipo; aba não muda o nó; abrir filho incorpora os chips; breadcrumb limpa os chips); formatação de `NodeKeyDisplay` sobre `RiskIndexNodeKey` |
| `HeadlineFormatterTest` | Estados da §5.2.3; textos do Δ; quando mostrar o intervalo; indicativo sem faixa; dia indicativo da tendência sem faixa na dica |
| `BandGlyphTest` | Glifos e posições da rampa para 2 a 5 faixas; a classe `b{n}` da pílula e o token do gráfico vêm do mesmo `ChartPaletteMap.BandStep` |
| `ChartPaletteMapTest` | Todo papel aponta para um token definido em `Tokens.axaml`; nenhum `SKColor(`, `SKColors.` ou `SKColor.Parse` em `ViewModels/RiskOverview`, `ViewModels/Admin/RiskIndex` e `Tools/RiskIndex` (fora do adaptador) |
| `RiskIndexNavigationMapTest` | Cada `RiskIndexNavigationModule` vira o `NavigationTarget` da §10.3 e a permissão de `ModuleAccess`; destino sem permissão desabilitado com a razão |
| `ModuleAccessTest` | A regra de cada módulo é a mesma que a barra de navegação usa |
| `RiskContextFieldPolicyTest` | As três propriedades ficam somente leitura com a razão no formulário de entidade |
| `RiskQualityStripAlwaysVisibleTest` | A faixa de qualidade e a de governança não estão dentro de `Expander` nem têm `IsVisible` ligado a estado recolhível |
| `RiskOverviewHasNoDecisionActionsTest` | Comandos do módulo só de leitura e navegação |
| `RiskOverviewDeltaColumnsNotSortableTest` | Colunas de Δ e sensibilidade não ordenáveis (`CanUserSort="False"` e o code-behind da árvore); a lista de objetos trata `Sorting` e não ordena no cliente |
| `RiskIndexReferenceScopeTest` | §11.5 |
| `RiskParameterLocalizationTest` | Toda chave do esquema de parâmetros tem `_Label` e `_Help` nos três `.resx` |
| `PostureConnectionsExposeEntityPickerTest` | As formas de Vision One, SSC e Tenable têm seletor de entidade |
| `AdminNavigationHintsTests` (**editado na mesma mudança**) | A contagem de ícones passa de 10 para **11** (`AdminNavigationHintsTests.cs:51`); a extração do hint passa a aceitar também a forma `<ToolTip.Tip><MultiBinding …><Binding Path="X"/>` que o `ActionTooltipConverter` exige (hoje só lê `ToolTip.Tip="{Binding X}"`, `:37-38`); a lista de rótulos de seção ganha "Risk index"; cada hint continua uma propriedade estática `public string X { get; } = Localizer["…"]` (`:107-113`) |
| `SettingsFormVocabularyTest` (**editado na mesma mudança**) | As duas teorias ganham `[InlineData]` para `Views/Admin/TenableIntegrationView.axaml`, `Views/Admin/EntityMappingsView.axaml`, `Views/Admin/RiskIndex/RiskProfileEditorView.axaml`, `RiskSourcesView.axaml`, `RiskProfileApprovalView.axaml` e `RiskProfilePreviewView.axaml` (hoje cobrem só arquivos existentes, `SettingsFormVocabularyTest.cs:91-97`, `:109-111`); a regra "nenhum input em StackPanel horizontal dentro de formCard" passa a olhar também `NumericUpDown`, `CalendarDatePicker` e `CheckBox` (hoje só `TextBox` e `ComboBox`, `:129-130`), e uma view existente que passe a falhar é corrigida na mesma mudança |
| Existentes sem edição | `LocalizationCoverageTest`, `LocalizedLabelsTests`, `StyleClassReferenceTest`, `ThemeTokenLayerTests`, e `./build.sh LintUi` sem violação |

### 12.5 `ClientServices.Tests`

`RiskIndexRestServiceTest`, `RiskIndexConfigRestServiceTest` e os acréscimos de
`IntegrationsRestServiceTest`: cada método, rota e verbo, incluindo `ApprovalEligibility`, o lote de
contexto de risco e o `sync` enfileirado; 401 descarta o token; 403, 409 e 503 viram
`InvalidHttpRequestException` com o código; `X-Total-Count`; `If-Match: <revision>` no `PUT` do
rascunho. Tudo por `MockSetup.GetRestClient()`.

### 12.6 `BackgroundJobs.Tests`

| Classe | Cobertura |
|---|---|
| `RiskIndexSnapshotJobTest` | Grava todos os tipos de linha numa transação; falha no meio → nenhuma linha; segunda execução do dia não faz nada; sem perfil → registra e sai; **depois de suspender (Retire sem sucessor), a execução seguinte não grava linha e a leitura devolve o estado suspenso**; perfil novo → ponte; membros `Scope` e `risk_index_class_days` gravados; cada tipo de marcador da §9.3 detectado; log `IntegrationKind.RiskIndex` |
| `RiskIndexRetentionJobTest` | Só apaga além das janelas; anula o JSON depois de 400 dias; apaga `risk_index_class_days` e os relatórios de prévia antigos; lotes |
| `RiskIndexPreviewJobTest` | Ordem da fila; **duas execuções concorrentes processam o pedido uma vez**; pedido com `heartbeat_at` recente não é devolvido à fila, com `heartbeat_at` velho é; hash alterado → `Failed`; relatório gravado no pedido e **não** na linha do perfil |
| `TenableSyncJobTest` | Só conexões devidas e habilitadas; intervalo de 6 h roda quatro vezes por dia; vencimento contado do início e sem deslizar; pedido manual reivindicado uma vez; execução com progresso há menos de 2 h não é encerrada pelo ceifador mesmo tendo começado há mais |
| Resolução de jobs (da T237) | Os quatro jobs novos resolvem no contêiner e veem linhas semeadas com o acessor de background real |
| `JobScheduleOrderTest` | Snapshot às 05:00, depois de 02:20, 03:00 e 04:00 e antes de 06:00; retenção 02:40; Tenable a cada 15 min; prévia a cada minuto; `WorkerCount = 3` |

### 12.7 `ConsoleClient.Tests` e `DAL.IntegrationTests`

- `SchemaUpgradeIdempotenceTest`, `SchemaUpgradeTableReferencesTest` e `SchemaUpgradeFilesTest` cobrem
  88 e 89 sem edição. Novo `Data88SeedsTest`: permissões por `INSERT IGNORE` sem id; publicação
  `shadow` com `ON DUPLICATE KEY UPDATE value = value`; anotação guardada por `NOT EXISTS`; papéis
  `RiskManager` e `RiskAdministrator` criados por `NOT EXISTS` e concessões por nome e `key`, sem id
  literal, e nenhuma concessão ao `Administrator` (T293).
- `DAL.IntegrationTests` (Docker, `Category=Integration`): `SchemaUpgradeRetryTests` passa a ir até 89
  com cada Structure aplicado duas vezes; `StringColumnTypeGuardTest` cobre as colunas novas;
  `RiskIndexScopeFilterTests` (contexto escopado vê só as próprias linhas em snapshots, linhas de
  objeto, atribuições, `vendor_assets` e `host_external_ids`); `EntityScopeHierarchyIntegrationTests`
  (MariaDB real: claim na unidade vê as subunidades e não o contrário, em cada tabela escopada, T292); `RiskIndexBulkInsertTests` (35 mil
  linhas de objeto numa transação dentro do orçamento); `SecurityScorecardScoreNullableTest`.

### 12.8 `Tools.Tests`

A função `Faixa` é da T238 (S39 §4.5) e o painel depende dela: limites inclusivos (30 → Baixo,
31 → Médio, 69 → Médio, 70 → Alto com o padrão) e preset Registro.

---

## 13. Critérios de aceitação verificáveis

"Teste" nomeia a classe da §12; "cliente" significa observar no desktop contra uma base semeada com o
cenário da Unidade X (S39 §11).

**Painel**

1. Sem `risk_index_view`, o botão do módulo fica visível e desabilitado com a dica de permissão; com a
   permissão e a publicação em `published`, o módulo abre. (Cliente; `AdminPaneAccessTest` para a
   Administração.)
2. Na Unidade X, o cartão mostra **"64 /100"** e a faixa **"Médio"** em texto e glifo, sem casa decimal;
   as categorias mostram 68, 74, 60, 20 e 67, os pontos 20, 22, 9, 2 e 10 e a cobertura 92 %. (Cliente;
   `RiskIndexEngineGoldenTest`, `RiskIndexControllerTest`.)
3. A seta de 7 dias aparece só com variação ≥ 3 pontos inteiros, nunca através de descontinuidade, e
   sempre com texto. (`HeadlineFormatterTest`, `RiskIndexReadServiceTest`.)
4. Com `COV < 0,40` o cartão diz "ICR indicativo", mostra o intervalo e não mostra faixa, cor nem
   seta; com `K(S)` vazio mostra "Sem dados". (`HeadlineFormatterTest`; cliente.)
5. Com AME e CTL indisponíveis, o cartão mostra o intervalo [53–79] e o chip "AME e CTL
   indisponíveis" (S39 §11.5). (Teste de ouro; cliente.)
6. No modo interino nenhum valor tem piso, e um incidente `data_breach` aberto produz o chip
   "evidência para avaliação do Portão A" sem mudar o número. (`RiskIndexReadServiceTest`.)
7. A tendência oferece 30, 90 e 365 dias; mostra marcador nos dias de troca de perfil, de teto de
   apetite e de insumo externo, com a EWMA reiniciando no marcador; mostra lacuna em dia sem snapshot e
   a anotação "VPR v2" em 2026-07-01 sem quebra. (Cliente; `RiskIndexControllerTest`.)
8. Clicar num ponto da tendência leva "O que mudou" àquele dia; "Ver como tabela" mostra os mesmos
   valores. (Cliente.)
9. No interino, as abas Identidades e Nuvem aparecem "Indisponível — requer … (T272)" e são
   selecionáveis; Dispositivos mostra visibilidade por fonte com contagens rotuladas. (Cliente.)
10. O painel Explorar está sempre visível e é o único lugar com seletor de dimensão; na árvore, Enter
    abre o nó, Alt+← sobe, as colunas de Δ não ordenam, nós de tipos diferentes não são ordenados
    juntos e linhas parciais ficam depois das oficiais com a nota "não comparáveis: visão parcial";
    selecionar uma aba de classe não muda a árvore nem o nó. (Cliente; `DrillStateTest`,
    `RiskOverviewDeltaColumnsNotSortableTest`.)
11. O nó "Unidade X × Dispositivos" tem procedência "Oficial derivado" e o mesmo valor que o
    recálculo das linhas de objeto do dia. (`RiskIndexReadServiceTest`.)
12. Um usuário com claim só na Unidade X (e não no Lab L1) vê "Parcial — visibilidade restrita", sem
    tendência nem "o que mudou", e nenhuma contagem inclui objetos do Lab L1. (`RiskIndexReadServiceTest`;
    cliente.)
13. Um usuário escopado não recebe a manchete da organização (`403 org_requires_global_scope`) nem os
    indicadores de tenant fora do nó da conexão. (`RiskIndexControllerTest`.)
14. O detalhe do objeto mostra a criticidade com origem e autor, os fatores com validade e carimbo,
    e o residual efetivo ao lado do armazenado; a navegação fica numa seção do corpo e a linha de ações
    tem só "Fechar", centralizado; "Abrir risco" seleciona o risco no módulo Riscos, e, para quem não tem
    `riskmanagement`, fica desabilitado com "Requer a permissão riskmanagement" e o diálogo não mostra o
    título nem as entradas do cenário. (Cliente; `RiskIndexNavigationMapTest`, `RiskIndexReadServiceTest`.)
15. Nenhuma view do módulo tem comando de decisão. (`RiskOverviewHasNoDecisionActionsTest`.)
16. O painel de contribuições mostra o aviso R-1, e a coluna de sensibilidade não ordena.
    (Cliente; teste de varredura.)
17. O painel monetário mostra "n de N"; P95 aparece "não disponível (requer M45)"; Top Risks aparece
    "não disponível até o M43". (Cliente.)
18. Com "Não atribuído" acima de 10 % do peso, a faixa de qualidade, o painel de qualidade e a linha do
    nó na árvore mostram o aviso; a faixa de qualidade com os sete itens da S39 §8.6 fica visível sem
    nenhum controle de recolher. (Cliente; `RiskQualityStripAlwaysVisibleTest`.)
19. O CSV de um nó parcial leva "parcial" em cada seção; o PDF é recusado para escopo restrito e, para
    escopo global, aparece na lista de relatórios; `POST /Reports` com tipo 4 é recusado para qualquer
    usuário. (`RiskIndexControllerTest`, `RiskIndexCsvTest`, `ReportsControllerRiskIndexTypeTest`.)
20. "Prévia ao vivo" só habilita para `risk_index_configure`/`risk_index_approve` com escopo global e
    desabilita a exportação. (Cliente; `RiskIndexControllerTest`.)
21. Com a publicação em `shadow`, quem tem `risk_index_view` ou `risk_index_configure` sem
    `risk_index_approve` vê "piloto em sombra" no painel; quem tem `risk_index_approve` explícita
    (comitê) e o papel Admin veem o painel; o autor (`risk_index_configure`, escopo global) continua
    vendo a própria prévia e a ponte. (`RiskIndexControllerTest`, `RiskIndexProfilesControllerTest`.)

**Configuração**

22. Um usuário com `risk_index_configure` e `risk_index_view` abre a Administração; só a entrada
    "Índice de risco" habilita, as demais mostram o hint com a razão de permissão, e nenhuma chamada da
    janela devolve 403. Um usuário com só `entity_risk_context` abre a Administração com só a aba
    Contexto de risco habilitada e as demais com "Requer risk_index_view", também sem 403. Fechar a
    janela ou trocar de aba com alterações não salvas pede confirmação. (`AdminPaneAccessTest`,
    `UnsavedChangesGuardTest`; cliente com log do servidor.)
23. O editor mostra Σω ao vivo e a trava conjunta com números; τ 0,60 com τ_G 0,50 bloqueia a
    submissão com a mensagem da trava. (`RiskIndexProfileRulesTest`; cliente.)
24. Um rascunho com ω_REG +0,10 no trimestre pode ser submetido como proposta de rebaseline e só é
    aprovado com "rebaseline" marcado; um com três parâmetros alterados também (S39 §12.4).
    (`RiskIndexProfilesServiceTest`.)
25. Não é possível submeter sem prévia concluída para os parâmetros atuais; editar depois da prévia
    a marca "desatualizada". (Cliente; `RiskIndexProfilesServiceTest`.)
26. A prévia mostra Δ por nó, mudanças de faixa, τ de Kendall, tornado, peso efetivo do registro e
    "média pura". (Cliente.)
27. O autor ou um editor do rascunho não consegue aprovar; um Admin sem a permissão explícita recebe
    `approver_permission_not_explicit`; quem tem `risk_index_approve` só por concessão direta ou por um
    papel fora de `risk_index_approver_roles` recebe `approver_role_required`; quem tem
    `business_risk_review` recebe `sod_business_reviewer`;
    quem concedeu a si mesmo `risk_index_approve` recebe `sod_self_granted_approver`; a aba Aprovação
    mostra essas checagens antes do clique, vindas de `ApprovalEligibility`. (`RiskIndexProfilesServiceTest`.)
28. A aprovação exige a referência do comitê com anexo e, se as faixas mudaram ou é o primeiro perfil,
    a referência do Conselho; suspender grava motivo e referência próprios sem apagar os da ativação, e
    o snapshot seguinte não é gravado. (`RiskIndexProfilesServiceTest`, `RiskIndexSnapshotJobTest`.)
29. Depois da aprovação, o snapshot seguinte usa o perfil novo, grava a ponte, e a tendência mostra o
    marcador e a sobreposição; as linhas da série antiga ficam idênticas. (`RiskIndexSnapshotJobTest`.)
30. Cada transição do perfil gera linhas campo a campo em `audit_logs`; `sensitivity_report` não.
    (`RiskIndexProfilesServiceTest`.)
31. Em Fontes e frescor, habilitar uma fonte [H] altera o mesmo rascunho da aba Rascunho (criando-o,
    com confirmação), e um único Salvar grava as duas abas; uma fonte [N] fica desabilitada com a tarefa
    que a habilita; com um perfil Em aprovação, nada é editável e a razão aparece. (Cliente;
    `RiskSourcesEnablementTest`.)
32. Em Contexto de risco, salvar exige justificativa e escopo global e grava o lote inteiro ou nada; o
    histórico mostra autor, campo, valores e justificativa; na tela Entidades os três campos são
    somente leitura com a dica; `PUT /Entities/{id}` mudando a criticidade e `POST /Entities` com
    criticidade, sem `entity_risk_context`, devolvem 403. (`EntityRiskContextServiceTest`,
    `EntitiesControllerRiskContextTest`, `RiskContextFieldPolicyTest`.)
33. Uma mudança de criticidade aparece no "O que mudou" do dia seguinte como efeito de **contexto**,
    com o autor. (`ChangeAttributionTest`; cliente.)
34. Uma regra de mapeamento mostra a prévia com contagens, é aplicada na sincronização seguinte, não
    altera hosts já atribuídos na política "só não atribuídos", e cada recarimbo gera auditoria de
    `Host`. (`VendorEntityMappingServiceTest`.)
35. A conexão Tenable testa por etapas; a sincronização cria achados `tenable-vm` com VPR e EPSS e
    hosts pela cadeia de identidade; ACR e AES vão só para `vendor_scores` e só movem o índice quando
    TEN-ACR/TEN-AES estão no perfil aprovado (então ACR é criticidade abaixo de manual e CMDB); o cursor
    só avança em sucesso; "Sincronizar agora" enfileira e o job executa; a API nunca devolve chave e as
    chaves não aparecem em log; a referência de cofre é contada e normalizada nos dois registros.
    (`TenableServiceTest`, `TenableNormalizerTest`, `RiskIndexEngineGoldenTest`,
    `SecretVaultServiceInMemoryTest`, `SecretColumnRegistriesAgreeTest`.)
36. As sincronizações de Vision One e SSC não gravam mais `entities.cyber_risk_index`; um fator SSC
    ausente fica NULL; o histórico SSC recuperado leva `is_backfill = 1`. (`TrendMicroPostureSyncTest`,
    `SecurityScorecardBackfillTest`.)

**Infraestrutura**

37. `./build.sh LintUi` sem violações; `ControllerAuthorizationInventoryTest`,
    `RiskIndexEndpointPermissionMapTest` e `IcrIsNotADecisionInputTest` verdes.
38. As versões 88 e 89 reaplicadas duas vezes convergem para o mesmo esquema
    (`SchemaUpgradeRetryTests`).
39. O snapshot das 05:00 registra a execução em `integration_sync_logs`; uma falha no meio não deixa
    linha nenhuma do dia. (`RiskIndexSnapshotJobTest`.)
40. Os quatro jobs novos resolvem no contêiner e leem linhas semeadas com o acessor de background
    real. (Teste da T237.)

**Segurança e indicadores ao lado**

41. Um usuário com escopo restrito não recebe relatório de sensibilidade, ponte, histórico de perfil,
    contagens de `Sources` nem `Runs`, e o diálogo do chip de perfil mostra essas seções desabilitadas
    com "Requer escopo global". (`RiskIndexProfilesServiceTest`, `RiskIndexReadServiceTest`; cliente.)
42. A faixa "Governança e revisões — indicadores (fora do ICR)" mostra as aceitações a vencer em
    `janela_aviso_expiracao`, o uso de exceção à segregação, os indicadores de campanha, a
    conformidade de SLA e "Flags (M43): não disponível". (`RiskIndexReadServiceTest`; cliente.)
43. Um usuário com claim só na Unidade X vê o snapshot oficial da Unidade X e das suas subunidades, e
    recebe "parcial" ou nada na unidade-mãe e nas irmãs; com claim só numa subunidade, não vê a Unidade
    X. Uma troca de pai na árvore muda o acesso sem novo login. (`EntityScopeHierarchyTest`,
    `EntityScopeHierarchyIntegrationTests`; cliente.)
44. Depois da versão 88, os papéis `RiskManager` e `RiskAdministrator` existem (ou os já existentes
    foram mantidos) com as quatro permissões do índice, `RiskAnalyst` tem `risk_index_view` e
    `risk_index_configure`, e o `Administrator` não tem `risk_index_approve`; um Gerente de Riscos aprova
    um rascunho de outro autor e não aprova o próprio. (`Data88SeedsTest`, `RiskIndexProfilesServiceTest`.)
45. Depois da T294, a barra de navegação não mostra o Master Dashboard, `GET /Dashboard/Master` deixa
    de existir e nenhum teste ou view referencia `MasterDashboardService`. (`ControllerAuthorizationInventoryTest`
    sem a rota; varredura de fonte em `GUIClient.Tests`.)

---

## 14. Plano de implementação

### 14.1 Ordem e dependências

```
M52 (Estágio A0)  T236 ──► T237 ──► { T238, T239, T240, T241, T242 }        T236 antes ou junto da T237
                  T292 (escopo hierárquico) independente, com revisão de segurança; antes da publicação (A2)
                   └─ versão 88 = T239/T240/T241 (dados) + T243 (tabelas do índice)

M53 (motor)       T243 ──► T293 (papéis aprovadores, mesma versão 88)
                  T243 ──► { T244, T245, T246 } ──► T247 ──► T248 ──► T249
                              └──────────────► T250 (ciclo de vida já; prévia depois de T244)

M54 (painel)      T257 ──► { T251, T252, T253 }   T254 ◄ T246, T249   T255 ◄ T248, T249   T256 ◄ T249
M55 (config.)     T259 ──► T260 ──► T261 ──► T262   T263 (fontes ◄ T250; mapeamentos V1 ◄ versão 89)   T264 ◄ T241, T242

Piloto (A1)       T258 começa com T251–T253 e T259–T262 prontos → aprovação do perfil de piloto (v1, com
                  referências do comitê e do Conselho) → snapshots com a publicação em shadow por 8–12
                  semanas → aprovação do perfil de publicação (v1 ou posterior; rebaseline explícito se a
                  calibração passou das regras de ritmo, §6.4) → publicação (A2, exige T292)
                  → T294 (retirada do Master Dashboard)

M56 / M57 (A3)    depois do M52 (os jobs de sincronização dependem da T237, §9.2)
                  versão 89 ──► T270 ──► { T267, T271, T272, T273, T274, T275 }
                  T265 ──► T266
                  T265 ──► T267 ──► T268                (T268 precisa das tags do export de ativos da T267)
                  T276 independente (S39)
                  cada fonte habilitada = nova versão de perfil com marcador
M58               independente (S39); retira contornos à medida que entra
                  T290 (quant_residual_ale_mean) libera o Σ E[L] residual do painel monetário (§5.10)
                  T291 (internet-facing e relação app↔servidor do Jira Assets) alimenta `m_net` e os vínculos
M59 (A4)          depois de M39–M49 (Track 9): T284–T288
Backlog           T289 depois da T249; T269 (Security Center) reaproveita o normalizador da T266
```

### 14.2 Frentes paralelas

| Frente | Tarefas | Pode começar |
|---|---|---|
| A — dados e motor | T236–T248 | Já |
| B — API | T249, T250 | Contratos da §8 em paralelo com A; implementação depois de T247/T244 |
| C — painel | T257, T251–T256 | Contra *stubs* da API (DTOs da §8.3) logo que a §8 for aprovada |
| D — configuração | T259–T264 | Idem, contra os DTOs da §8.4–§8.5 |
| E — integrações | T265–T275 | Depois da versão 89; independente do painel |
| F — qualidade de dados | T277–T283 (S39) | Já |

### 14.3 O que é entregável em cada marco

| Marco | Entregável para o usuário |
|---|---|
| M52 | Nada novo no painel. Jobs passam a ver dados; mapa de entidades com permissões e auditoria; propriedades `criticality` e `internetFacing`; tipo `activity` |
| M53 | API e snapshots diários em homologação; nada para o usuário final |
| M54 + M55 | Painel e configuração para comitê e administradores, publicação em `shadow` (Estágio A1) |
| Início da T258 | Perfil de piloto aprovado com ata e referência do Conselho; snapshots diários visíveis só ao comitê e aos administradores |
| T258 concluída | Perfil de publicação aprovado com ata (rebaseline explícito se a calibração o exigir); publicação para a equipe de riscos (analistas, gerentes e administradores de riscos, P-7), rótulos "Interino" e "parcial", aviso de "Não atribuído" (Estágio A2) |
| M56 | Tenable Vulnerability Management (Tenable One) como fonte de EXP e de criticidade, por nova versão de perfil; Security Center fica no backlog (T269) |
| M57 | Indicadores de referência; abas Identidades, Nuvem e Internet-facing completas; histórico SSC |
| M58 | Contornos interinos retirados (S39 §14.2) |
| M59 | Modo pleno: piso do Portão A, Top Risks, P95/CVaR, KRIs, confiança da evidência, nível-alvo, TER |

### 14.4 Mapa tarefa → seção

| Tarefa | Seções desta especificação |
|---|---|
| T243 | §7.1, §7.2 (tabelas do índice), §7.6 (versão 88), §11.1 |
| T292 | §4.2 item 12, §5.6.5, §11.3 (escopo hierárquico), §12.2, §12.7 |
| T293 | §11.1 (semeadura de papéis), §11.4, §12.7 |
| T246 | §7.2 (`risk_index_object_attributions`, `risk_index_node_members`), §7.4, §9.3 |
| T249 | §8.1–§8.3, §11.3 |
| T250 | §8.4, §6.4 (ativação, ponte, suspensão), §9.6 (prévia) |
| T251 | §5.0–§5.2.4, §5.13, §10.1–§10.2 |
| T252 | §5.3, §10.4 |
| T253 | §5.4, §5.5 |
| T254 | §5.6 (painel Explorar, `DrillState`, dimensão Ambiente), §5.9.1 |
| T255 | §5.7, §5.8, §5.9.2, §10.3 (`NavigationTarget`, `ModuleAccess`), redação da §11.3 na GUI |
| T256 | §5.2.5, §5.5.1 (visibilidade), §5.10, §5.12 |
| T257 | §10.4 (paleta), §10.6 |
| T259 | §6.0 (acesso, `ShowAdministration`, hints), §6.1 |
| T260 | §6.2 (incluindo `IHasUnsavedChanges` e `UnsavedChangesGuard`) |
| T261 | §6.3 |
| T262 | §6.4, §11.4 (`ApprovalEligibility`, permissão explícita, auditoria de concessão) |
| T263 | §6.5, §6.6 (regras do Vision One), §6.9, §8.6 |
| T264 | §6.7, §8.5 (lote, `entities_manage`, escopo global) |
| T265 | §6.8, §8.7 (fila de sincronização), §9.4 (batimento), §11.6 |
| T266, T269 | §9.4, §7.2 (T269 no backlog) |
| T267 | §9.4, §7.2; Coletar ACR/AES (§6.8) |
| T268 | §6.6 (tag Tenable), §9.4 |
| T270 | §7.2 (`vendor_scores`), §9.5 |
| T271, T272, T273 | §9.5, §5.2.4, §5.5, §6.9 |
| T274, T275 | §9.5, §7.3 |
| T286 | §5.2.2, §5.11 |
| T287 | §5.3, §5.10, §5.11 |
| T289 | §4.2 item 10 |
| T294 | §4.2 item 13 |
| T295 | §6.7 item 5 (`RiskContextWritePolicy`; backlog) |

As tarefas especificadas pela S39 (T236–T242, T244–T248, T258, T276–T285, T288 e T290–T293) têm lá a
sua regra; esta especificação registra o que o painel precisa delas: da T241, o nome `entities_manage`
e a checagem no `POST /Entities` (§8.5); da T246 e da T247, os membros `Scope`, a tabela `risk_index_class_days` e a
detecção de marcadores (§9.3); da T258, o perfil de piloto (§6.4); da T290, o Σ E[L] residual (§5.10).

---

## 15. Riscos, desvios e decisões deliberadas

### 15.1 Riscos

| Risco | Efeito | Contenção |
|---|---|---|
| **LiveCharts 2 é build de desenvolvimento** (`2.1.0-dev-798`, `Directory.Packages.props:51-53`) | As APIs existem no build fixado (`CoreSection.Label`/`LabelSize`/`LabelPaint`, `DataPointerDownCommand`, `DashEffect`, `X/YToolTipLabelFormatter`, `DateTimeAxis`, `Series.Pivot`, `DiamondGeometry`, `VariableSVGPathGeometry`); não há geometria de triângulo, por isso o marcador é caminho SVG (§10.4). O risco é de comportamento, não de ausência | Primeira atividade da T252: *spike* restrito a três pontos: o rótulo de seção desenhado sobre `DateTimeAxis`, o `Pivot` do tornado e o acerto do `DataPointerDownCommand` em pontos só com contorno. Alternativa prevista para o clique: seleção na tabela. Atualizar para a versão estável quando houver, com o teste visual da tendência |
| Volume das linhas de objeto | ≈ 14 milhões de linhas e vários GB em 400 dias (§7.7) | Linhas limpas compactas, sinais só com `s > 0`, contribuições só no ICR, retenção em lotes; medir em `RiskIndexBulkInsertTests`; partição mensal se estourar |
| Duração do job noturno | Snapshot que não termina antes das 06:00 atrasa a leitura do dia | Orçamento de 10 min para 50 mil objetos (`RiskIndexSnapshotPerformanceTest`); inserção em lotes; `MySqlBulkCopy` como plano B; `WorkerCount = 3` é requisito (§9.2) |
| **Licença Tenable para ACR/AES** | Sem Tenable One/Lumin, `ratings.acr`/`ratings.aes` vêm vazios: TEN-AES e TEN-ACR ficam indisponíveis | Teste de conexão e log avisam; a fonte TEN-VM (achados) funciona sem a licença |
| **Créditos CREM do Vision One** | `securityPosture` e os endpoints de superfície exigem créditos Flex alocados ao CREM (desde 2024-11-01); a postura exige também "Third-party auditing (API only)" e o Workbench outra permissão | Opções separadas por fase (§6.9); o teste nomeia o que falta; `assetGroups` e tags funcionam sem crédito |
| VPR v2 desde 2026-07-01 | Valores de VPR mudam de modelo | Anotação semeada; não é quebra (S39 §9.5) |
| Formatos de fornecedor não verificados | `issue_count`/`issue_summary` da SSC, tags no export v2 de ativos da Tenable e campos de superfície do Vision One foram lidos na documentação, não num tenant | Fixtures montadas pelo esquema publicado; validação contra um tenant real é critério de saída da T266, T272 e T274 |
| Usuários com escopo ainda veem "parcial" em alguns nós | Com o escopo hierárquico (T292), só nós com processo ou aplicação ligados por propriedade EAV, ou com objetos de escopo fora da subárvore | A faixa diz a causa e sugere tornar o processo filho da unidade na árvore (§5.6.5) |
| Escopo hierárquico muda a autorização do produto inteiro | Um erro na expansão vaza registros de uma unidade para outra | Revisão de segurança, regressão que falha no código de hoje, teste de integração em MariaDB por tabela escopada e comportamento observado no cliente (regras do Track 7) |
| Mudança de acesso à Administração | Regressão para administradores ou exposição de painéis | `AdminPaneAccessTest`; inicialização condicional; o servidor continua a autorizar cada ação |
| API em mais de uma instância | Cache em memória por processo | Correto (só cache, chave por escopo), mas sem compartilhamento; cache distribuído fica fora deste escopo |

### 15.2 Desvios deliberados

| Desvio | Em relação a | Justificativa |
|---|---|---|
| O módulo não segue um arquétipo IX-5 A–E | ux-interaction-standard IX-5 | É um painel somente leitura; declarado no cabeçalho da view |
| A Administração abre para quem não é administrador | `NavigationBar.axaml:158-160` | Autor e aprovador do perfil são segunda linha e comitê (S39 §13.1); os outros painéis seguem só de administrador |
| Sem FK de `entity_id` nas tabelas históricas | Convenção de FK do Track 6 | O histórico precisa sobreviver à exclusão em cascata de entidades |
| `top_contributions` só na linha do ICR e anulado depois de 400 dias | Leitura literal da S39 §9.1 | Volume; as partes por categoria são recalculáveis das linhas de objeto enquanto elas existem |
| Versão 88 reúne dados do M52 e tabelas do índice | Uma versão por marco | Honra o "próximo par" da S39 §12.5 |
| Δ ordenável só na prévia de sensibilidade | Regra "Δ não ordena" do painel | Na prévia, Δ é efeito de parâmetro sobre nós, não lista de objetos nem de trabalho |
| Conexões de postura entram na auditoria com campos ignorados | Allowlist atual | A entidade padrão e as opções movem atribuição e fontes (S39 I8) |
| Indicador de ocupado por painel | ui-standard §4.7 ("loading is a window-level concern") | Os painéis carregam de endpoints independentes e falham sozinhos; um anel único esconderia o painel pronto atrás do lento. IX-4 continua valendo em cada painel |
| Colunas de Δ e de sensibilidade não ordenáveis | ui-standard §6.1 ("User MUST be able to … sort") | S39 §1.3: nenhuma lista ordena por Δ nem por sensibilidade. A lista paginada ordena no servidor pelas colunas permitidas (§5.9.1) |

### 15.3 Decisões tomadas por esta especificação

| Decisão | Alternativa rejeitada | Por quê |
|---|---|---|
| Ler só snapshot e linhas de objeto; cálculo derivado para interseções | Calcular ao vivo no servidor a cada tela | Reprodutível e auditável (S39 §9.1); o ao vivo é prévia |
| Parcial recalculado das linhas de objeto filtradas por escopo | Consultar as tabelas de origem ao vivo | Mesmo predicado de escopo, custo baixo, resultado reprodutível |
| Raiz "Meu escopo" sem manchete para escopo com várias entidades | Manchete da união das claims | A S39 reserva a manchete da organização ao escopo global; a união seria um nó sem tipo |
| Detalhe do objeto como diálogo utilitário que devolve destino de navegação | Janela auxiliar | IX-2: o chamador reage ao resultado; não há edição |
| Contexto de risco num painel próprio, somente leitura em Entidades | Editar na tela Entidades | Um editor por dado, edição em lote com justificativa, trava no servidor (§6.7) |
| Mapeamentos em Integrações, sob `configuration` | No painel do índice | Mudam atribuição e escopo e se aplicam na sincronização |
| Grupo de ativos do Vision One só como indicador | Atribuir dispositivos pelo grupo | A API não expõe a pertença; a pertença vem das tags |
| Prévia no host de jobs com fila em tabela | Na API | Não disputa CPU com requisições e sobrevive a reinício |
| "Retire" = suspender a publicação | Voltar ao perfil anterior | Perfis aposentados são imutáveis (S39 §12.5); voltar exige nova aprovação |
| Aprovação exige a permissão no papel | Aceitar o Admin implícito | "Sem bypass de administrador" (S39 §13.1) |
| Aprovador ≠ autor e ≠ editor do rascunho | Só ≠ autor | Mesma intenção da segregação de funções, sem brecha por coautoria |
| Regras de ritmo não bloqueiam a submissão; o aprovador confirma o rebaseline | Bloquear a submissão | A S39 §12.4 admite a exceção "salvo aprovação explícita de rebaseline", que só existe depois da submissão (§6.2.3) |
| Redação por módulo dentro do painel | `risk_index_view` como superconjunto das permissões de módulo | Executivos leem o valor do índice sem ganhar acesso ao conteúdo de riscos, achados e incidentes; a alternativa exigiria todas as permissões de módulo para ver o painel (§11.3) |
| Sincronização manual da Tenable enfileirada no host de jobs | Executar na requisição, como o Vision One | Escopo do chamador quebraria a cadeia de identidade; export de horas não cabe no processo web (§8.7) |
| Resumo mensal de abas lido de `risk_index_class_days` | Recalcular das linhas de objeto a cada leitura | Orçamento de servidor; mesmo valor, calculado uma vez no job (§7.2) |
| PDF só para escopo global e só pela exportação do índice | PDF para todos; PDF pelo `POST /Reports` ou pelo `ScheduledReportJob` | `GET /Reports` não é escopado (`ReportsController.cs:25-33`); `POST /Reports` e os agendamentos não checam permissão, escopo nem publicação (§5.14) |
| `vendor_scores` por trecho constante | Uma linha por objeto por dia | Histórico e frescor com fração das linhas |
| Rampa de faixas reaproveita `NrCriticality*` | Tokens novos de faixa | Já tem contraste AA medido e é a linguagem de "quão grave" do produto |
| Publicação `shadow`/`published` como configuração com guarda no servidor | Só não conceder a permissão | Conceder `risk_index_view` cedo não vaza o índice antes da T258: o painel, a prévia, a ponte, o PDF e o anexo seguem a guarda (§8.2, §8.4, §11.3) |

### 15.4 Perguntas para o product owner

**Respondidas em 2026-10-05:**

| # | Pergunta | Resposta | Onde entrou |
|---|---|---|---|
| P-1 | O escopo deve passar a ser hierárquico? | Sim: quem tem acesso a uma unidade tem acesso às subunidades, não o oposto | T292; §4.2 item 12, §5.6.5, §11.3; S39 §10.4 |
| P-2 | Quem aprova o perfil pelo Comitê de Risco de TI? | O Gerente de Riscos ou o Administrador de Riscos | T293; §11.1; S39 §13.1 |
| P-3 | Tenable VM ou Security Center? Há licença para ACR/AES? | Tenable em nuvem (Vulnerability Management) com Tenable One | M56 sem Security Center (T269 no backlog); §6.8, §9.4 |
| P-4 | Créditos CREM e permissões de API do Vision One? | Sim e sim | T271–T273 viáveis |
| P-6 | Retirar o Master Dashboard depois do Estágio A2? | Sim | T294; §4.2 item 13 |
| P-7 | Quem recebe as permissões do índice na publicação? | Por ora, só analistas e gerentes de risco (com o Administrador de Riscos): a semeadura padrão | T293; §11.1, §14.3 |
| P-8 | Rótulos das faixas só em pt-BR ou por idioma? | Português e inglês, como o resto do sistema | §6.2 (grupo 7), §8.3 (`RiskIndexBandDto`), §10.5; S39 §12.2 |
| P-11 | Gestores de unidade editam o contexto de risco dos próprios processos? | Não por ora, porque não usam o sistema; deixar previsto para o futuro | §6.7 item 5 (`RiskContextWritePolicy`); T295 (backlog) |

**Em aberto** (até a resposta, vale o padrão desta especificação indicado na seção citada):

| # | Pergunta | Por que importa |
|---|---|---|
| P-5 | Quando a listagem de relatórios for escopada, o PDF deve ser liberado a usuários com escopo? | §5.14 |
| P-9 | O limite de 5.000 linhas na exportação CSV de objetos é aceitável? | Volume e vazamento |
| P-10 | Quem registra as anotações de mudança de algoritmo do Vision One (o fornecedor não as publica por API)? | Sem isso, a mudança aparece sem explicação na tendência |
| P-15 | O comitê precisa do relatório PDF do índice agendado (mensal e trimestral)? Se sim, com que destinatários internos e quem cria o agendamento? | O `ScheduledReportJob` atual não serve (§5.14); um agendamento próprio é trabalho novo |

---

## 16. Fontes

**Internas**

- Metodologia: [S39 — ICR](../methodology/icr-indice-consolidado-de-risco.md); [MIGR-TI/IA](../methodology/migr-ti-ia.md).
- Roadmap: [ROADMAP.md — Track 10](../../ROADMAP.md).
- Padrões: [ui-standard](../ui-standard.md) (§2.6, §4.1, §6.2.1); [ux-interaction-standard](../ux-interaction-standard.md)
  (IX-1, IX-4, IX-5, IX-7, IX-8); [SETTINGS_FORM_ROLLOUT](../../roadmap/SETTINGS_FORM_ROLLOUT.md) (S37);
  [hosts-view-redesign](hosts-view-redesign.md) (S38, rampa de criticidade e histórico).
- Integrações e segurança: [posture-integrations](posture-integrations.md); [secret-vaults](secret-vaults.md);
  [docs/security](../security/README.md); [src/AI_TESTING_INSTRUCTIONS.md](../../src/AI_TESTING_INSTRUCTIONS.md).

**Trend Micro Vision One**

- *More Than a Number: Your Cyber Risk Index Explained* (abr/2026):
  https://www.trendmicro.com/content/dam/trendmicro/global/en/core/docs/report/rpt-risk-score-explained.pdf
- Risk Overview: https://docs.trendmicro.com/en-us/documentation/article/trend-vision-one-risk-overview
- Devices view: https://docs.trendmicro.com/en-us/documentation/article/trend-vision-one-devices-view
- Internet-facing assets view: https://docs.trendmicro.com/en-us/documentation/article/trend-vision-one-internet-facing-assets-view
- Cyber risk subindexes: https://docs.trendmicro.com/en-us/documentation/article/trend-vision-one-get-started-risk-subindexes
- Cyber Risk Index v3: https://docs.trendmicro.com/en-us/documentation/article/trend-vision-one-cyber-risk-index-version-3
- Atualizações de algoritmo: https://docs.trendmicro.com/en-us/documentation/article/trend-vision-one-risk-index-algorithm-updates
- Especificação OpenAPI v3.0: https://automation.trendmicro.com/sp-api-open-v3.0.json

**Tenable**

- Export de vulnerabilidades: https://developer.tenable.com/reference/exports-vulns-request-export ,
  https://developer.tenable.com/reference/exports-vulns-export-status ,
  https://developer.tenable.com/reference/exports-vulns-download-chunk ,
  https://developer.tenable.com/docs/retrieve-vulnerability-data-from-tenableio
- Export de ativos v2: https://developer.tenable.com/reference/export-assets-v2 ;
  ACR/AES no export: https://developer.tenable.com/changelog/vm-new-acr-and-aes-scores-in-asset-exports
- Autenticação, limites e concorrência: https://developer.tenable.com/docs/authorization ,
  https://developer.tenable.com/docs/rate-limiting , https://developer.tenable.com/docs/concurrency-limiting
- VPR v2: https://developer.tenable.com/changelog/vulnerability-priority-rating-transition-to-version-2
- Métricas (VPR, ACR, AES, CES): https://docs.tenable.com/vulnerability-management/Content/Lumin/LuminMetrics.htm
- Exposure View: https://docs.tenable.com/exposure-management/Content/exposure-view/exposure-view.htm
- Security Center: https://docs.tenable.com/security-center/Content/APIKeyAuthentication.htm ,
  https://docs.tenable.com/security-center/api/Analysis.htm , https://docs.tenable.com/security-center/api/Hosts.htm

**SecurityScorecard**

- Histórico de score: https://securityscorecard.readme.io/reference/get_companies-scorecard-identifier-history-score
- Histórico de fatores: https://securityscorecard.readme.io/reference/get_companies-scorecard-identifier-history-factors-score
- Fatores (`issue_summary`): https://securityscorecard.readme.io/reference/get_companies-scorecard-identifier-factors
- Limites: https://securityscorecard.readme.io/docs/rate-limits
- *Methodology Deep Dive 3.0*: https://securityscorecard.com/wp-content/uploads/2025/10/MethodologyDeepDive-3.0-Ebook_102325_SD.pdf

**Microsoft Defender**

- Exposure score: https://learn.microsoft.com/en-us/defender-vulnerability-management/tvm-exposure-score

