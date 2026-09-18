# Etapa 9.N — <título>

> Status: **Rascunho** · Track: [Track 9](../TRACK_9_MIGR_TI_IA.md) — substitua por link para a seção da etapa ·
> Roadmap: [ROADMAP.md → Stage 9.N](../../../ROADMAP.md#track-9-migr-tiia-methodology-alignment)
>
> Revisores: <nomes> · Mesclada em: <data ou "pendente">
> Revisão de segurança necessária: **sim/não** — obrigatória se a etapa toca autenticação,
> autorização, segredos, esquema de configuração ou o composition root.

## Emendas

| Data | O que mudou | Motivo |
|---|---|---|
| — | — | — |

*Uma especificação mesclada muda por emenda datada aqui, com o motivo. Um desvio descoberto na
implementação volta para cá — não fica anotado apenas na mensagem de commit.*

---

## 1. Lacuna e fase

Qual lacuna da [§ 12 da análise de aderência](../../methodology/migr-ti-ia-coverage.md), qual fase da
MIGR-TI/IA e qual campo do modelo de registro de risco esta etapa fecha.

*Se a etapa não fecha nada da metodologia, ela não pertence a este track.*

## 2. Estado atual, com evidência

O que existe hoje, **nomeando a entidade, o serviço, o endpoint ou o teste**.

*Uma afirmação sem código apontado é tratada como lacuna, não como cobertura.*

## 3. Estado alvo e escopo negativo

O comportamento pretendido — e, explicitamente, **o que esta etapa não fará**, com o motivo.

*O escopo negativo é o que evita a etapa crescer durante a implementação, e é o que impede a etapa de
parecer fechar uma fase que ela só cobre em parte.*

## 4. Modelo de dados

Entidades e colunas novas, conformes à convenção Track 6:

- tabelas `snake_case` plurais; colunas `snake_case` via `HasColumnName` (C# fica PascalCase)
- FK: coluna `<entidade>_id`, constraint `fk_<tabela>_<coluna>`, relacionamento EF configurado
- índices `idx_<tabela>_<colunas>`; único `uq_…`; fulltext `ftx_…`
- `created_at` DATETIME NOT NULL, `updated_at` DATETIME NULL, sempre UTC
- booleano `tinyint(1)`; status/enum `int` + enum C# com `HasConversion`
- texto `varchar(n)` quando limitado, `TEXT`/`LONGTEXT` quando livre — **nunca** BLOB para texto
- **nunca `char(n)` para uma coluna `string`** — ver a nota em [CLAUDE.md](../../../CLAUDE.md)

## 5. Caminho de esquema

O ritual de duas etapas:

1. migração EF (mantém modelo e `NRDbContextModelSnapshot` em sincronia, gera o SQL via `migrationScript.sh`)
2. arquivos numerados `src/ConsoleClient/DB/Structure/{n}.sql` + `DB/Data/{n}.sql`, e `targetVersion` em `DB/DatabaseInformation.yaml`

`Structure` **sem transação e com todo statement guardado** (`IF NOT EXISTS`/`IF EXISTS`, ou a sonda
`information_schema` com `BINARY` para rename). `Data` como **DML puro em transação real**, com o bump
de `db_version` dentro da transação como ponto de commit. Se a etapa for uma fase Track 6, a entrada
correspondente em `SchemaUpgradePhases.yaml`.

## 6. Contrato de API

| Verbo | Rota | DTO entrada | DTO saída | Erros | Atributo e permissão |
|---|---|---|---|---|---|
| | | | | | |

*Toda ação declara `[Authorize]` ou `[PermissionAuthorize]` com a permissão nomeada. Um endpoint sem
atributo é reprovado por `API.Tests/Security/ControllerAuthorizationInventoryTest` — a especificação
decide qual permissão, não a implementação. `[AllowAnonymous]` novo exige justificativa na allowlist.*

## 7. Superfície de cliente e GUI

Views Avalonia afetadas; classes de estilo da taxonomia §4.1 de [ui-standard.md](../../ui-standard.md);
chaves de string nos **três** arquivos `Resources/Localization*.resx` e a propriedade `Str*` de
view-model que as expõe; serviços REST de `ClientServices`.

*Sem cor inline, sem literal em `Text`/`Content`/`Title`/`Header`/`ToolTip.Tip`/`Watermark`, sem
`Button` sem classe — `./build.sh LintUi` falha o build.*

## 8. Plano de teste

Por camada, **quais casos** serão escritos. Revisado junto com esta especificação, não depois.

| Camada / projeto | Casos |
|---|---|
| `ServerServices.Tests` | caminho feliz + **cada** ramo de guarda/erro |
| `API.Tests` | subtipo concreto de `ActionResult<T>` e `.Value`; casos negativos de autorização |
| `ClientServices.Tests` | via `MockSetup.GetRestClient()` |
| `Tools.Tests` | cálculo puro, semeado e determinístico |
| `GUIClient.Tests` | chave de localização que não resolve; token `Classes` que nenhum estilo define |
| `ConsoleClient.Tests` / `DAL.IntegrationTests` | idempotência, ordem de referência, replay |
| `BackgroundJobs.Tests` | idempotência e ordem relativa aos jobs existentes |
| `RiskPortal.Tests` | fluxo do revisor, incluindo o caminho sem JavaScript |

**Casos-limite que a metodologia nomeia para esta etapa:**

- …

**Observação em runtime** (obrigatória quando o comportamento é observável em execução): como será
observado, e não apenas testado.

## 9. Critérios de aceitação verificáveis

Cada critério é uma afirmação que um **teste nomeado** ou um **comando nomeado** decide.

1. …

*"Funciona" não é critério.*

## 10. Efeito na análise de aderência

| Linha de [migr-ti-ia-coverage.md](../../methodology/migr-ti-ia-coverage.md) | De | Para |
|---|---|---|
| | | |

**Permanece parcial ou ausente, e por quê:**

- …

## 11. Riscos, desvios e decisões deliberadas

O que pode não funcionar como especificado, e o que se decidiu **não** fazer.
