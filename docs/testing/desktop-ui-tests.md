# Testes de UI do cliente desktop (Avalonia headless)

> Status: **Especificação — implementação pendente (T319)** · Track: Track 1 — Modern Desktop
> Experience · Roadmap: [ROADMAP.md → M60](../../ROADMAP.md#track-1--modern-desktop-experience-uiux-compliance) · S54
>
> Revisores: pendentes · Mesclada em: pendente
> Revisão de segurança necessária: **não** — nenhum código de produção muda; a entrega adiciona uma
> dependência só de teste (vetada pela [política de supply chain](../security/SUPPLY_CHAIN.md), S21)
> e um workflow de CI com permissão somente de leitura e sem segredos.

## Emendas

| Data | O que mudou | Motivo |
|---|---|---|
| — | — | — |

*Uma especificação mesclada muda por emenda datada aqui, com o motivo. Um desvio descoberto na
implementação volta para cá — não fica anotado apenas na mensagem de commit.*

---

> ## ⚠️ Regra de execução — leia antes de tudo
>
> **Os testes de UI (`src/GUIClient.UiTests`) são caros e NÃO fazem parte do ciclo de
> desenvolvimento.** Eles rodam **somente**:
>
> 1. quando o **usuário pede explicitamente** ("rode os testes de UI"); ou
> 2. num **release**: antes de criar a tag `vX.Y.Z` (`./build.sh TestUi`) e no workflow
>    `ui-tests.yml`, disparado pelo push da tag `v*`.
>
> Eles **não** rodam em `dotnet test src/netrisk.sln`, em `./build.sh`/`Compile`, em CI de push ou
> pull request, nem na verificação de rotina de um agente (`test-runner`, `builder`, `architect`,
> `reviewer`). A §3 define a regra e as duas travas que a garantem.

## 1. Objetivo

Hoje o `GUIClient` tem só verificações estáticas: o linter `LintUi` (S1) e os testes de
`GUIClient.Tests`, que **de propósito** não referenciam o Avalonia e por isso só cobrem código
sem UI e varreduras do código-fonte. Nada instancia uma view, aplica estilos, resolve bindings ou
simula teclado e mouse. Defeitos que só aparecem com a árvore de controles montada (binding com
erro, classe de estilo sem seletor, validação que não reage a digitação, comando que não habilita)
chegam ao usuário.

Esta especificação cria uma camada de testes de UI **headless**, gratuita e multiplataforma, e
define quando ela roda.

## 2. Ferramenta escolhida: Avalonia.Headless.XUnit

### 2.1 Requisitos

- 100% gratuita: licença open source, **sem** taxa de uso comercial.
- Roda em **macOS, Linux e Windows**.
- Testa um aplicativo **desktop** Avalonia.
- Compatível com a stack de testes do repositório: **xUnit v3** sobre **Microsoft.Testing.Platform**
  (MTP), projetos autoexecutáveis (`OutputType=Exe`), NSubstitute.

### 2.2 Alternativas avaliadas (pesquisa de 2026-10-13)

| Ferramenta | Licença | macOS | Linux | Windows | Veredito |
|---|---|---|---|---|---|
| **Avalonia.Headless.XUnit** | MIT | ✅ | ✅ | ✅ | **Escolhida** |
| Appium 3 + Mac2 / NovaWindows / driver Linux AT-SPI | Apache/MIT | ✅ | ⚠️ só drivers comunitários | ✅ | Fora do escopo: o driver Linux é imaturo e nenhuma view declara `AutomationProperties.AutomationId` (0 ocorrências em `src/GUIClient/Views`) |
| FlaUI | MIT | ❌ | ❌ | ✅ | Só Windows |
| Dogtail / pyatspi | (L)GPL | ❌ | ✅ | ❌ | Só Linux, em Python, voltado a GTK/Qt |
| Verify.Avalonia (comparação por snapshot) | MIT + taxa | ✅ | ✅ | ✅ | **Rejeitada**: versões lançadas depois de 2026-09-01 cobram uma "Open Source Maintenance Fee" de organizações com receita e de órgãos de governo |
| WinAppDriver / Appium Windows Driver | — | — | — | ✅ | Descontinuados; a norma FGV proíbe componente sem suporte (NRM §5.4.3) |

### 2.3 Por que esta

- `Avalonia.Headless.XUnit` 12.1.3 é a **mesma versão do Avalonia do repositório** (12.1.3 em
  `src/Directory.Packages.props`), depende de `xunit.v3.extensibility.core` ≥ 3.2.2 e tem build
  para `net10.0`. Ou seja, já é xUnit v3.
- Monta a árvore de controles real, com layout, estilos, bindings e dispatcher, **em memória e
  sem janela**: não precisa de display, Xvfb nem permissão de acessibilidade em nenhum dos três
  sistemas.
- Simula entrada de teclado e mouse (`KeyTextInput`, foco, cliques) com `[AvaloniaFact]` e
  `[AvaloniaTheory]`.
- É mantida pela própria AvaloniaUI, no mesmo ritmo de releases do framework (12.1.4 publicada em
  2026-10-09).

**Limite aceito.** Não cobre janelas nativas, menu global do macOS, bandeja nem diálogos do sistema
operacional. Isso exigiria Appium, que fica fora desta entrega (§5.3).

## 3. Política de execução: sob demanda ou no release

### 3.1 Regras

| # | Regra |
|---|---|
| E1 | Os testes de UI **não rodam** em nenhum ciclo de desenvolvimento: `dotnet test src/netrisk.sln`, `./build.sh` (alvo padrão), `./build.sh Compile`, workflows de `push`/`pull_request`, e a verificação de rotina de agentes. |
| E2 | Eles rodam **só** (a) por pedido explícito do usuário ou (b) no release: `./build.sh TestUi` antes de criar a tag, e `ui-tests.yml` no push da tag `v*` nos três sistemas. |
| E3 | O projeto **compila sempre**: fica em `src/netrisk.sln`, então `Compile` pega uma quebra de API (view-model renomeado, view removida) sem rodar nada. Compilar é barato; executar é que não é. |
| E4 | Quem altera uma view ou um view-model coberto por um teste de UI **atualiza o teste na mesma mudança** (ele tem que compilar), **não o executa**, e declara no resumo: *"testes de UI não executados (S54 §3)"*. |
| E5 | Uma falha em `./build.sh TestUi` **bloqueia a tag** do release. Uma falha em `ui-tests.yml` depois da tag é tratada como defeito do release: corrigir e lançar um patch, nunca afrouxar a asserção. |
| E6 | "O usuário pediu" significa um pedido explícito no chat ou um disparo manual do workflow (`workflow_dispatch`). Um agente **não** deduz o pedido de "verifique a mudança" ou "rode os testes". |

### 3.2 Travas

A regra não pode depender só de disciplina. Duas travas independentes:

**Trava 1: fora do `dotnet test` da solução.** O `.csproj` declara
`<IsTestProject>false</IsTestProject>`, para que `dotnet test src/netrisk.sln` não descubra o
projeto. **A verificar na implementação:** que o modo MTP do SDK .NET 10 respeita essa
propriedade. Se não respeitar, o mecanismo alternativo é escolhido na T319 e registrado como
emenda. O critério de aceite A2 (§8) é o que vale, não o mecanismo.

**Trava 2: opt-in por variável de ambiente.** Todo teste usa `[UiFact]`/`[UiTheory]`, atributos
do próprio projeto que derivam de `[AvaloniaFact]`/`[AvaloniaTheory]` e preenchem `Skip` no
construtor quando `NETRISK_UI_TESTS` não é `1`:

```csharp
// Skips every UI test unless the run was opted into explicitly (S54 §3).
public sealed class UiFactAttribute : AvaloniaFactAttribute
{
    public UiFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("NETRISK_UI_TESTS") != "1")
            Skip = "UI tests run only on demand or at release — set NETRISK_UI_TESTS=1 (S54 §3).";
    }
}
```

Assim, mesmo quem rodar o executável `GUIClient.UiTests` direto, sem querer, vê tudo pulado e a
mensagem que explica por quê. Um teste de UI escrito com `[Fact]` ou `[AvaloniaFact]` puro fura
a trava, por isso o critério A4 (§8) exige um teste de varredura que reprove isso.

**Ponto de entrada único.** O alvo Nuke `TestUi` define `NETRISK_UI_TESTS=1`, compila e roda o
executável. Ele **não é dependência** de nenhum outro alvo (`Compile`, `Package*`, `Bump*`).

```bash
./build.sh TestUi
```

## 4. Arquitetura do projeto

- **Projeto novo `src/GUIClient.UiTests`**, separado de `GUIClient.Tests`. Este último continua
  sem Avalonia, como o CLAUDE.md exige.
- Formato igual aos demais projetos de teste: `OutputType=Exe`, `xunit.v3`, sem
  `Microsoft.NET.Test.Sdk`/`xunit.runner.visualstudio`/`coverlet.collector`. Mais
  `Avalonia.Headless.XUnit`, com a versão central em `src/Directory.Packages.props` **igual à do
  `Avalonia`**, porque uma versão divergente do headless quebra em runtime e não na compilação.
  Referencia `GUIClient`.
- **App de teste.** `[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]`, com
  `AppBuilder.Configure<TestApp>().UseHeadless(...)`. O `TestApp` carrega **os mesmos estilos do
  cliente** (tema Semi, `Styles/WindowStyles.axaml`) e a cultura fixa `pt-BR`. Sem os estilos
  reais, uma classe de estilo inexistente passaria despercebida.
- **Serviços.** Os view-models resolvem dependências pelo localizador de serviços (cerca de 140
  chamadas `GetService`/`Locator.Current` em `src/GUIClient/ViewModels`). Cada teste registra
  dublês NSubstitute dos serviços de `ClientServices` antes de criar a view, por meio de um
  helper do projeto. **Nenhum** teste fala com API, banco ou rede.
- **Isolamento.** O padrão é `PerTest` (Application e Dispatcher recriados a cada teste).
  Paralelização desligada (`[assembly: CollectionBehavior(DisableTestParallelization = true)]`),
  porque o modo headless não suporta execução concorrente. Trocar para `PerAssembly` é decisão
  de desempenho da T319, desde que o estado global seja limpo entre testes.
- **Erros de binding reprovam o teste.** Um sink de log do Avalonia, instalado pelo `TestApp`,
  coleta erros da área `Binding` e o teste falha se houver algum. É o principal defeito que
  esta camada pega e as outras não.

## 5. Escopo

### 5.1 Primeira entrega (T319)

1. O projeto, as duas travas, o alvo `TestUi` e o workflow `ui-tests.yml` (§6).
2. **Smoke de views:** um catálogo explícito de views representativas, no mínimo a janela
   principal (shell e navegação), um diálogo sobre `DialogWindowBase` e duas listas com grade.
   Cada uma abre com o view-model e os dublês, sem exceção e sem erro de binding.
3. **Um fluxo interativo** num diálogo com validação: digitar valor inválido, ver o erro inline,
   corrigir e ver o comando de salvar habilitar. Tudo por entrada simulada, não por chamada
   direta ao view-model.
4. As instruções para agentes (§7) e o passo no checklist de release (§6.2).

### 5.2 Fora desta entrega, candidatos a tarefa futura

- **Regressão visual** (frame capturado com Skia comparado a uma imagem de referência): a
  renderização de fontes difere entre os três sistemas, o que exige referências por sistema.
  Se vier, deve usar a captura do próprio Avalonia com comparação escrita no repositório, não
  Verify.Avalonia (§2.2).
- **Cobertura de todas as views.**

### 5.3 Fora de escopo

- **Ponta a ponta com Appium** (janela nativa, menu do macOS, bandeja). Exigiria
  `AutomationProperties.AutomationId` nas views e um driver Linux que hoje é só comunitário.

## 6. CI e release

### 6.1 Workflow `.github/workflows/ui-tests.yml`

```yaml
on:
  workflow_dispatch:          # pedido explícito (E2a)
  push:
    tags: ['v*']              # release (E2b)
# NUNCA: push em branch, pull_request, schedule
permissions:
  contents: read
```

- Matriz `ubuntu-latest`, `windows-latest`, `macos-latest`, com `fail-fast: false`.
- Mesmas actions e versões dos workflows existentes (`actions/checkout@v7` com
  `submodules: recursive`, `actions/setup-dotnet@v6`).
- Roda `./build.sh TestUi` (ou `build.cmd` no Windows), com `timeout-minutes` definido.
- Publica os resultados como artefato quando falha.
- Nenhum segredo, nenhum `GITHUB_TOKEN` com escrita.
- O cabeçalho do arquivo cita esta especificação e a regra E1, como o `ui-compliance.yml` faz
  com a S1. O objetivo é que ninguém "conserte" o workflow adicionando `pull_request`.

**Custo:** o runner macOS do GitHub tem multiplicador de minutos alto. Restringir aos gatilhos
acima também é decisão de custo, não só de tempo.

### 6.2 Checklist de release

A T319 acrescenta ao procedimento de release em
[docs/packaging/release-engineering.md](../packaging/release-engineering.md) (S10):

1. Depois do `Bump*` e antes de `git tag vX.Y.Z`: `./build.sh TestUi` no host do release.
   Falhou, não cria a tag (E5).
2. Depois do push da tag: acompanhar `ui-tests.yml` nos três sistemas.

## 7. Instruções para agentes (texto a incluir na T319)

**No `CLAUDE.md`, seção *Testing***, logo após a lista de projetos de teste:

> **`GUIClient.UiTests` runs only on demand or at release (S54).** Headless Avalonia UI tests are
> too expensive for the development loop. Never run them as routine verification: not in
> `dotnet test`, not in a `test-runner` brief, not to "double-check" a GUI change. Run them only
> when the user explicitly asks, or as the release gate (`./build.sh TestUi` before tagging). When
> you change a view or view-model a UI test covers, update the test so it compiles, do not run it,
> and say "UI tests not run (S54 §3)" in your summary.

**Em `src/AI_TESTING_INSTRUCTIONS.md`**, uma seção com a mesma regra e com as convenções do §4:
`[UiFact]`/`[UiTheory]` (nunca `[Fact]`/`[AvaloniaFact]` puros), dublês pelo helper do projeto,
nenhum acesso a rede ou banco, e erro de binding reprova o teste.

## 8. Critérios de aceite

| # | Critério | Como verificar |
|---|---|---|
| A1 | `./build.sh Compile` compila `GUIClient.UiTests` | build |
| A2 | `dotnet test src/netrisk.sln` não executa nenhum teste de `GUIClient.UiTests` | saída do comando: o projeto não aparece |
| A3 | Rodar o executável `GUIClient.UiTests` sem `NETRISK_UI_TESTS=1` pula todos os testes, com a mensagem da §3.2 | executar sem a variável |
| A4 | Um teste de varredura (dentro do próprio projeto, sem a trava) reprova qualquer método de teste que não use `[UiFact]`/`[UiTheory]` | introduzir um `[Fact]` e ver falhar |
| A5 | `./build.sh TestUi` passa em macOS, Linux e Windows | `ui-tests.yml` via `workflow_dispatch` |
| A6 | Um erro de binding introduzido de propósito numa view do catálogo reprova o smoke | mutação manual, registrada na nota da T319 |
| A7 | `ui-tests.yml` não tem gatilho de `push` em branch, `pull_request` nem `schedule` | revisão do arquivo |
| A8 | `./scripts/security/scan-dependencies.sh` passa com a nova dependência | gate local de supply chain |
| A9 | CLAUDE.md, `AI_TESTING_INSTRUCTIONS.md` e o checklist de release contêm a regra da §3 | revisão |

## 9. Conformidade (norma FGV de desenvolvimento seguro)

- **Componente sem suporte (NRM §5.4.3):** `Avalonia.Headless.XUnit` é mantido pela AvaloniaUI
  (12.1.4 em 2026-10-09). WinAppDriver e o Appium Windows Driver, descontinuados, foram
  descartados (§2.2).
- **Nenhum segredo no repositório:** os testes usam dublês. Não há credencial, URL de servidor
  nem user-secret envolvidos, e o workflow não usa segredos.
- **Dados de produção:** proibidos nos testes. As fixtures são sintéticas, porque dado de produção
  em ambiente de teste exige o script de mascaramento (NRM §5.3.2).
- **Versão e tag (NRM §5.3.6):** o gate roda contra o mesmo commit que recebe a tag.
- **Pendente de aprovação humana:** nenhum portão do ESI é acionado. Não há mudança de arquitetura
  de produção, de componente de segurança nem de integração.
- **Lacuna de informação:** o G-002 v2.0 (2026-07-29) pode conter requisitos de SDLC/DevSecOps
  sobre gates de teste que esta análise não reflete.

## 10. Riscos e questões em aberto

| Risco | Mitigação |
|---|---|
| `IsTestProject=false` não excluir o projeto no modo MTP | A2 é o critério; trocar o mecanismo e registrar emenda |
| `Avalonia.Headless.XUnit` sob MTP ainda não verificado na prática (a documentação não cita MTP) | A primeira tarefa da T319 é um spike com um único teste nos três sistemas |
| Custo de montar dublês pelo localizador de serviços | O catálogo começa pequeno (§5.1); o helper de dublês é reutilizável |
| Semi.Avalonia fixa o Avalonia em 12.1.0 exato (docs/ui-standard.md §1.1) | A versão do headless acompanha a do `Avalonia` em `Directory.Packages.props`, nunca a do Semi |
| Testes apodrecerem por rodarem pouco | E3 (compila sempre) + E4 (atualiza na mesma mudança) + gate de release |

## 11. Fontes

- [Avalonia — Testing](https://docs.avaloniaui.net/docs/testing/)
- [Avalonia — Headless Testing with XUnit](https://docs.avaloniaui.net/docs/testing/headless-xunit)
- [Avalonia — UI Testing with Appium](https://docs.avaloniaui.net/docs/testing/ui-testing-with-appium)
- [Avalonia — Desktop Linux (AT-SPI2)](https://docs.avaloniaui.net/docs/platform-specific-guides/linux)
- [NuGet — Avalonia.Headless.XUnit 12.1.3](https://www.nuget.org/packages/Avalonia.Headless.XUnit/12.1.3)
- [Appium — Drivers](https://appium.io/docs/en/latest/ecosystem/drivers/)
- [appium-windows-driver (aviso de descontinuação)](https://github.com/appium/appium-windows-driver)
- [NovaWindows Driver](https://github.com/AutomateThePlanet/appium-novawindows-driver)
- [VerifyTests/Verify (taxa de manutenção)](https://github.com/VerifyTests/Verify)
