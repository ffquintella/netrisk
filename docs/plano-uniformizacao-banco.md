# Plano de Uniformização do Banco de Dados

Plano em múltiplas passagens para padronizar nomenclatura, melhorar indexação/correlação e remover tabelas/colunas sem uso — **sem perda de dados**. Cada fase gera uma ou mais migrações EF independentes (via `./migrationAdd.sh`), aplicáveis e reversíveis isoladamente, e termina com a suíte de testes verde (`dotnet test src/netrisk.sln`).

## Diagnóstico (resumo)

- **103 DbSets** no `NRDbContext` (~3.900 linhas), 125 entidades, 28 migrações.
- **Três padrões de nomenclatura**: snake_case (~95 tabelas legadas, herança SimpleRisk), PascalCase (8 tabelas novas: `Incidents`, `IncidentResponsePlan*`, `BiometricTransaction`, `FaceIDUsers`, `FixRequest`) e híbridos (`vulnerabilities_to_actions` com colunas `actionId`/`vulnerabilityId` em camelCase; `reports` com `creationDate`/`creatorId`).
- **FKs "órfãs"**: colunas de correlação sem relacionamento configurado nem constraint — `Risk.Owner`, `Risk.Manager`, `Risk.SubmittedBy`, `Risk.ProjectId`, `FrameworkControl.ControlOwner`, `FrameworkControl.Tester`, `FrameworkControlTest.Tester`, `Incident.ReportedBy` (varchar livre).
- **Índices com typos**: `idx_biometic_*`, `idx_irpt_sequencial`, `idx_irpt_optinal`.
- **Defaults inválidos** `0000-00-00` (`mgmt_reviews.next_review`, `mitigations.last_update`, `client_registration.*`) — quebram em MySQL strict mode.
- **Tipos inconsistentes**: TIMESTAMP vs DATETIME sem critério; BLOB usado para texto (`framework.Name`, `user.Email`!); status ora varchar ora int; tinyint(1) vs tinyint(4) para booleanos.
- **~24 tabelas candidatas a remoção** com zero referências fora de DAL/Migrations (lista na Fase 6).

## Convenção-alvo

| Item | Padrão |
|---|---|
| Tabelas | `snake_case`, plural (`incidents`, `incident_response_plans`) |
| Colunas | `snake_case` (`created_at`, `assigned_to_id`) |
| FKs | coluna `<entidade>_id` + constraint `fk_<tabela>_<coluna>` + relacionamento configurado no EF |
| Índices | `idx_<tabela>_<colunas>`; únicos `uq_...`; fulltext `ftx_...` |
| Criação/alteração | `created_at` DATETIME NOT NULL; `updated_at` DATETIME NULL — sempre UTC, sem TIMESTAMP com auto-update |
| Booleanos | `tinyint(1)` |
| Status/enums | `int` + enum C# com conversão explícita |
| Texto | `varchar(n)` quando limitado; `TEXT`/`LONGTEXT` quando livre; **nunca** BLOB para texto |

Racional: snake_case é o padrão de ~92% das tabelas; renomear as 8 novas é muito mais barato que renomear 95 legadas. A camada C# permanece PascalCase — o mapeamento é feito com `HasColumnName`/`ToTable`, então **nenhum DTO, serviço ou cliente muda**.

---

## Ferramenta de aplicação (homologação e produção)

As fases não serão aplicadas com `dotnet ef database update` manual em produção. Será criado um comando dedicado no **ConsoleClient** — que já possui `database status|init|update|backup|restore|fixData` (`src/ConsoleClient/Commands/DatabaseCommand.cs`, sobre `IDatabaseService` em ServerServices) — estendendo essa infraestrutura em vez de criar uma ferramenta nova:

```
netrisk-console database upgrade-schema --phase <n> [--env homolog|prod] [--dry-run] [--yes]
```

**Subcomandos/etapas do `upgrade-schema`:**

| Etapa | Comportamento |
|---|---|
| `--check` | Pré-voo: conectividade, versão do MySQL, migração atual vs. esperada pela fase, espaço em disco para backup, divergência schema × ModelSnapshot. Não altera nada. |
| `--dry-run` | Gera e exibe o SQL exato da fase (equivalente a `migrationScript.sh` entre a migração atual e a alvo) + relatório de impacto: contagem de linhas das tabelas afetadas, órfãos que serão anulados (Fase 3), tabelas que serão deprecadas/dropadas (Fase 6). Saída salva em arquivo para anexar à mudança/RDM. |
| execução | Sequência obrigatória: **backup automático** (reusa `DatabaseService.Backup()`) → relatório de órfãos/censo gravado em tabela de log → aplicação das migrações da fase via EF (`Database.Migrate()` até a migração-alvo da fase) → validações pós-aplicação (counts iguais aos do pré-voo nas tabelas renomeadas, FKs válidas, índices presentes) → registro em `schema_upgrade_log` (fase, versão, timestamp, operador, hash do backup). |
| falha | Qualquer validação pós-aplicação reprovada → instrução de restore apontando o backup recém-criado (restore não é automático em produção; é decisão do operador). |
| `--phase 6b` | Gate extra: recusa executar se a fase 6a não estiver registrada no `schema_upgrade_log` há pelo menos N dias (ciclo de observação), e exige `--yes` explícito por ser destrutiva. Antes do drop, gera os dumps por tabela (`mysqldump --tables`) e só dropa após verificar o arquivo. |

**Seleção de ambiente:** o ConsoleClient já lê a connection string de user-secrets/appsettings; `--env` seleciona o perfil (`appsettings.Homolog.json` / `appsettings.Production.json` ou secret correspondente). Produção exige confirmação interativa do nome do banco (digitar o nome para confirmar), a menos que `--yes --env prod` seja passado em pipeline.

**Mapeamento fase → migração:** um manifesto versionado no repositório (ex.: `src/ConsoleClient/DB/SchemaUpgradePhases.yaml`, ao lado do `DatabaseInformation.yaml` existente) lista, por fase, a migração EF alvo, as validações pós-aplicação e os scripts de censo/órfãos. Assim a ferramenta é dirigida por dados e cada nova fase entra como uma entrada no manifesto + sua migração.

**Fluxo recomendado por fase:** rodar em homologação (`--check` → `--dry-run` → execução → testes/smoke) → anexar o dry-run à mudança → repetir em produção com a mesma versão binária do ConsoleClient.

A construção da ferramenta é o **primeiro entregável** (junto com a Fase 0), pois as fases seguintes dependem dela.

## Fase 0 — Preparação e rede de segurança

Sem mudança de schema. Pré-requisito de todas as outras.

1. **Backup e baseline**: dump completo do banco de produção; gerar script SQL da migração atual (`./migrationScript.sh`) como referência.
2. **Censo de dados reais**: rodar `SELECT COUNT(*)` em todas as tabelas candidatas a remoção (Fase 6). Candidata com dados ≠ candidata vazia — muda o tratamento (arquivar vs. dropar).
3. **Verificar divergência schema × snapshot**: comparar o banco real com o `ModelSnapshot` (ex.: a join table `IncidentToIncidentResponsePlan` tem o mapeamento fluent comentado no contexto mas relação ativa via `UsingEntity` — confirmar o que existe de fato no banco).
4. **Congelar o padrão para código novo**: documentar a convenção-alvo no CLAUDE.md/docs para que novas entidades já nasçam em snake_case.
5. Garantir suíte de testes verde como baseline.

**Risco: nenhum.**

## Fase 1 — Correções seguras (sem rename, sem drop)

Uma migração pequena, totalmente reversível.

1. **Defaults `0000-00-00` → NULL**: alterar colunas para `NULL DEFAULT NULL` e atualizar linhas existentes com `0000-00-00` para `NULL` (`mgmt_reviews.next_review`, `mitigations.last_update`, `client_registration.last_verification_date` / `registration_date`).
2. **Typos de índice**: recriar `idx_biometic_id` → `idx_biometric_transaction_id`, `idx_biometic_anchor`, `idx_irpt_sequencial` → `..._sequential`, `idx_irpt_optinal` → `..._optional` (drop+create de índice não toca dados).
3. **Normalizar booleanos** `tinyint(4)` → `tinyint(1)` (`comments.is_anonymous`, `framework_controls.deleted`, `failed_login_attempts.expired` se sobreviver à Fase 6).
4. **Collation/charset**: padronizar `utf8mb4_unicode_ci` onde houver mistura.

**Risco: baixíssimo** — nenhuma alteração é semântica.

## Fase 2 — Uniformizar nomenclatura (renames, zero perda)

Renomes via `migrationBuilder.RenameTable` / `RenameColumn` (geram `RENAME`, preservando dados). **Atenção**: revisar a migração gerada pelo EF — se ele scaffoldar `Drop/Create`, reescrever à mão para `Rename`.

1. **Tabelas PascalCase → snake_case** (8): `Incidents`→`incidents`, `IncidentResponsePlans`→`incident_response_plans`, `IncidentResponsePlanTasks`→`incident_response_plan_tasks`, `IncidentResponsePlanExecutions`/`...TaskExecutions` idem, `IncidentToIncidentResponsePlan`→`incident_to_incident_response_plan`, `FaceIDUsers`→`face_id_users`, `BiometricTransaction`→`biometric_transactions`, `FixRequest`→`fix_requests`.
2. **Colunas PascalCase/camelCase → snake_case** nas tabelas renomeadas e nos híbridos: `vulnerabilities_to_actions.actionId`→`action_id`, `vulnerabilityId`→`vulnerability_id`; `reports.creationDate`→`created_at`, `creatorId`→`creator_id`, `fileId`→`file_id`; `hosts.FQDN`→`fqdn`, `OS`→`os`; `messages.Message`→`message`.
3. **Atualizar o mapeamento no contexto** (`ToTable`/`HasColumnName`) — entidades e DTOs C# não mudam de nome.
4. Conferir constraints/índices renomeados junto (MySQL mantém FKs em rename de tabela, mas os nomes de constraint ficam com o padrão antigo — renomear para `fk_<tabela>_<coluna>`).
5. (Opcional, passagem separada) pluralizar tabelas legadas singulares (`user`→`users`, `team`→`teams`, `audit`→`audits`…). Alto churn, baixo ganho — fazer só se desejado, uma migração por grupo.

**Risco: baixo** — renames são atômicos no MySQL; o ponto de atenção é manter o snapshot EF e o banco sincronizados, validado pelos testes e por um `migrationScript` de conferência.

## Fase 3 — Correlação: FKs e relacionamentos explícitos

Objetivo: toda coluna de correlação vira relacionamento navegável + constraint. **Antes de cada constraint, limpar órfãos** — senão o `ALTER TABLE ... ADD CONSTRAINT` falha:

```sql
-- exemplo: risks.owner sem usuário correspondente
UPDATE risks r LEFT JOIN user u ON r.owner = u.value
SET r.owner = NULL WHERE u.value IS NULL AND r.owner IS NOT NULL;
```

1. `Risk.Owner`, `Risk.Manager`, `Risk.SubmittedBy` → FK para `user` (tornar nullable onde fizer sentido, `ON DELETE SET NULL`); configurar `HasOne` no EF com navegações (`Risk.OwnerUser` etc.).
2. `FrameworkControl.ControlOwner`, `FrameworkControl.Tester`, `FrameworkControlTest.Tester` → FK para `user`.
3. `Risk.ProjectId` → avaliar: se não há tabela de projetos viva, é candidata a remoção (Fase 6); se há, FK.
4. `Incident.ReportedBy` (varchar livre): decisão de produto — ou vira `reported_by_id` FK para `user` mantendo a coluna texto para externos, ou permanece livre e documenta-se. Migração de dados: tentar casar o texto com `user.name` e popular a nova FK; nada é apagado.
5. Resolver a join table `IncidentToIncidentResponsePlan` comentada: ou mapear explicitamente a entidade, ou consolidar no `UsingEntity` — eliminar a ambiguidade.

**Risco: médio** — o saneamento de órfãos altera valores (para NULL). Mitigação: gravar antes um relatório dos órfãos (`SELECT` para tabela de log/CSV) e revisar; nenhum dado é destruído sem registro.

## Fase 4 — Indexação para performance

1. **Indexar todas as colunas de FK** criadas na Fase 3 (MySQL cria índice junto com a constraint; conferir).
2. **Colunas de filtro/ordenação quentes** sem índice — candidatas típicas neste domínio: `risks.status` + `submission_date` (composto), `vulnerabilities.status`/`first_detection`/`last_detection`/`host_id`, `comments(type, taggee_id)`, `nr_actions(object_type, object_id)`, `audit(DateTime)`. Validar contra as queries reais dos serviços (`ServerServices`) e os filtros Sieve (`ApplicationSieveProcessor` mapeia `Vulnerability` e `Host` — priorizar essas).
3. **Remover índices duplicados/inúteis** (ex.: índice UNIQUE redundante com a PK em `framework_control_tests.id`; índices em colunas booleanas de baixa seletividade que nunca aparecem sozinhas em WHERE).
4. **BLOB → tipo correto**: converter colunas BLOB que guardam texto para `varchar`/`TEXT` (`framework.name`, `framework.description`, `user.email`, `permission.name/description`, `risk_catalog.description`, …). `ALTER ... MODIFY` converte in-place sem perda; permite collation correta e índices/FULLTEXT.
5. Medir: ativar slow query log / `EXPLAIN` nas consultas dos serviços de listagem antes/depois.

**Risco: baixo-médio** — conversões BLOB→TEXT exigem validação de encoding (testar num clone do banco antes).

## Fase 5 — Padronização de tipos temporais e status

1. `created_at`/`updated_at` em DATETIME UTC; remover `ON UPDATE CURRENT_TIMESTAMP` implícitos onde a aplicação já controla o valor (comportamento de TIMESTAMP com auto-update gera updates "fantasma").
2. Status varchar → int + enum: `risks.status` (varchar(20)) é o caso principal — criar coluna `status_id`, popular por mapeamento dos valores distintos atuais, rodar em paralelo um ciclo de release, depois (Fase 6) remover a coluna antiga. Nunca dropar na mesma migração que cria a nova.
3. Enums sem conversão explícita (`BiometricTransaction.TransactionResult`) → `HasConversion` explícito.

**Risco: médio** — exige migração de dados; o padrão "criar nova → copiar → conviver → remover depois" garante rollback a qualquer momento.

## Fase 6 — Deprecação e remoção (duas passagens)

Nunca dropar direto. Padrão: **deprecar → observar um ciclo de release → arquivar → dropar**.

**Passagem 6a — Deprecar (reversível):**
1. Remover os `DbSet`s e mapeamentos das entidades mortas do `NRDbContext` (a aplicação para de enxergá-las; as tabelas e dados ficam intactos no banco).
2. Renomear as tabelas no banco com prefixo `zz_deprecated_` (rename puro, dados preservados) — qualquer acesso esquecido falha alto e rápido.
3. Marcar entidades C# com `[Obsolete]` antes de excluí-las do projeto.

**Passagem 6b — Remover (após ≥1 release sem incidente):**
4. Exportar cada tabela deprecada com dados para dump arquivado (`mysqldump --tables`).
5. Migração final com `DropTable` + exclusão dos arquivos de entidade.

**Candidatas confirmadas (zero referências fora de DAL/Migrations):**

*Tabelas funcionais mortas:* `ContributingRisksImpact`, `ContributingRisksLikelihood` (e avaliar `contributing_risks` pai), `QuestionnairePendingRisk`, `ResidualRiskScoringHistory`, `FrameworkControlTestResultsToRisk`, `FrameworkControlTypeMapping`, `PermissionToPermissionGroup`, `MitigationAcceptUser`, `RiskToAdditionalStakeholder`, `RiskToLocation`, `RiskToTechnology`, `FrameworkControlTestComment`, `FrameworkControlTestAudit`, `FailedLoginAttempt`, `UserPassHistory`.

*Tabelas de enumeração mortas:* `ControlPhase`, `ControlType`, `FileTypeExtension`, `Regulation`, `RiskFunction`, `TestStatus`, `ThreatCatalog`, `ThreatGrouping`. (`RiskGrouping` tem 1 uso em `StatisticsService` — investigar antes.)

*Colunas órfãs:* `risks.regulation`, `risks.project_id` (se Fase 3 confirmar), `risks.status` antigo (após Fase 5). Mesmo padrão: parar de mapear → release de observação → drop.

**Atenção especial:** `FailedLoginAttempt` e `UserPassHistory` são de segurança — confirmar que lockout de login e reuso de senha não são requisitos pendentes antes de remover (pode ser feature a implementar, não tabela a apagar). Notar também que `UserPassReuseHistory` existe separadamente — verificar qual das duas é a viva.

**Risco: controlado** — em nenhum momento existe drop sem dump arquivado + ciclo de observação.

---

## Ordem, dependências e validação

```
Ferramenta + Fase 0 ──► Fase 1 ──► Fase 2 ──► Fase 3 ──► Fase 4 ──► Fase 5 ──► Fase 6a ──► (1 release) ──► Fase 6b
```

- A ferramenta `database upgrade-schema` é construída junto com a Fase 0 e usada para aplicar todas as demais em homologação e produção.
- Fases 1–2 podem ser agrupadas num release; 3–4 noutro; 5 e 6 são releases próprios.
- **Cada fase**: migração EF dedicada → revisar SQL gerado (`--dry-run` / `./migrationScript.sh`) → aplicar em homologação via ferramenta → `dotnet test src/netrisk.sln` → smoke test do GUIClient → registrar no CHANGELOG → aplicar em produção via ferramenta.
- **Rollback**: toda migração precisa de `Down()` funcional; nas fases com migração de dados (3, 5), o rollback restaura a coluna antiga que nunca foi apagada na mesma release.
