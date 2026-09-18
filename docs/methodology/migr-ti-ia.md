# MIGR-TI/IA — Metodologia Integrada de Gestão de Riscos de TI e Inteligência Artificial

> Sumário estruturado do relatório técnico-acadêmico **"Gestão de Riscos em TI para Empresas e
> Universidades: Metodologia Integrada, Orientada a Processos e Apoiada por Inteligência
> Artificial"** (17 de setembro de 2026). A fonte autoritativa é
> [migr-ti-ia-guide.pdf](migr-ti-ia-guide.pdf); este arquivo existe para poder citar fases, portões e
> campos por nome, e para que mudanças na metodologia apareçam em um diff.
>
> Aderência do NetRisk à metodologia: [migr-ti-ia-coverage.md](migr-ti-ia-coverage.md).

## Tese central

A gestão de riscos em TI eficaz **começa no objetivo estratégico e no processo de negócio** — não no
inventário de ativos nem na matriz de calor. Matrizes ordinais podem servir como visualização
executiva, mas não como mecanismo único de ordenação, priorização ou decisão de investimento
(Cox, 2008).

## Bases normativas integradas

ISO/IEC 27005:2022 · NIST CSF 2.0 (perfis atual e alvo) · NIST SP 800-30 Rev. 1 · COBIT 2019 ·
Open FAIR · NIST AI RMF 1.0 (GOVERN/MAP/MEASURE/MANAGE) · ISO/IEC 42001 quando adotada ·
HECVAT/EDUCAUSE · MITRE ATT&CK · CISA KEV · FIRST EPSS · LGPD (Lei 13.709/2018).

## Os 10 princípios orientadores

1. **Objetivos e processos** — orientação a objetivos e processos, não a ativos técnicos isolados.
2. **Cenários verificáveis** — causa, evento central e consequência distinguíveis.
3. **Quantificação proporcional** — profundidade compatível com materialidade e incerteza.
4. **Participação** — donos de processo e usuários no discovery e na aceitação do residual.
5. **Rastreabilidade** — evidência, versionamento, auditabilidade e logs em todas as fases.
6. **IA como apoio** — a IA nunca é autoridade final; supervisão humana obrigatória.
7. **Privacidade desde o desenho** — segurança, privacidade e ética desde a concepção.
8. **Portfólio e sistêmica** — dependências e riscos sistêmicos correlacionados.
9. **Melhoria contínua** — monitoramento dinâmico, aprendizagem com incidentes, revisão cíclica.
10. **Proporcionalidade** — evitar burocracia onde não há valor.

## Cadeia de ligação

`Objetivo estratégico → Processo/Atividade → Serviço de TI → Informação/Dado → Ativo`

Cada risco tem **dono de negócio, dono técnico e aprovador do risco residual**. Ferramentas de
suporte previstas: BPMN ou mapa de capacidades, catálogo de serviços, CMDB/grafo de conhecimento,
RACI e BIA.

---

## Fase 0 — Governança e contexto

Condição habilitadora, não formalidade.

**Definições:** escopo (unidades, processos, tecnologias, dados); objetivos e horizonte temporal;
partes interessadas; obrigações legais/regulatórias/contratuais incluindo LGPD; **apetite e
tolerância aprovados pelo conselho/reitoria**; critérios de impacto (unidade monetária, categorias,
limiares); cadência de revisão mensal/trimestral/anual independente.

**Três linhas:** 1ª donos de processo e TI; 2ª segurança, GRC, DPO/jurídico; 3ª auditoria.

**Tipos de risco a distinguir:** inerente (sem controle), atual/residual (com os controles
existentes), alvo (após tratamento planejado, dentro do apetite).

**Entregáveis:** política; taxonomia e categorias; declaração de apetite e tolerância; RACI com
escalonamento; critérios de impacto e calendário; comitê de risco constituído.

## Fase 1 — Discovery orientado a processos

Seis frentes:

| | Frente | Conteúdo |
|---|---|---|
| A | Organizacional | Entrevistas com dirigentes, donos de processo, usuários, docentes, TI, segurança, jurídico/DPO, auditoria, compras, fornecedores; estratégia, contratos, políticas, incidentes, auditorias, BIA |
| B | Processos | Atividades, entradas, saídas, decisões, dependências, exceções, sazonalidade, controles manuais, trabalho remoto, pontos únicos de falha (BPMN) |
| C | Dados | Catálogo, classificação, linhagem, base legal/finalidade (LGPD), retenção, localização, transferências, dados pessoais/sensíveis, pesquisa, segredos, PI |
| D | Técnico | CMDB, nuvem/SaaS, CSPM/CNAPP, EASM/ASM, varredura de rede e vulnerabilidade, IAM/PAM, endpoints, APIs, logs/SIEM, backups, certificados, DNS, laboratórios, shadow IT |
| E | Terceiros | Contratos, HECVAT, SBOM, suboperadores, localização de dados, concentração, SLA, RTO/RPO, direito de auditoria, saída e portabilidade |
| F | Ameaças | Incidentes, near misses, threat intelligence, ATT&CK, KEV, EPSS, fraude, erro humano, falha física/ambiental, indisponibilidade, fornecedor, abuso interno |

**IA no discovery** (NLP/RAG, classificação, entity resolution, grafos de dependência, anomalias,
sumarização) exige: fontes explicitadas, nível de confiança declarado, revisão humana de todos os
resultados, segregação de dados de treino/produção, logs completos, testes de validação e aprovação
formal antes do uso em decisões.

## Fase 2 — Discriminação e formulação de cenários

**Risco é efeito da incerteza sobre objetivos — evento futuro incerto, não condição presente.**

Oito tipos de registro distintos: **risco**, **vulnerabilidade**, **ameaça**, **finding**,
**incidente**, **issue/problema**, **não conformidade**, **hipótese**.

**Modelo de cenário:** *"Devido a [fonte de ameaça/causa], explorando [vulnerabilidade ou condição]
em [ativo/atividade/dependência], pode ocorrer [evento central], afetando [processo/objetivo] e
causando [consequências]."*

**Regras de qualidade:** causa e consequência nunca confundidas nem omitidas; um evento central
claro por cenário; dividir cenários com donos ou tratamentos distintos; consolidar duplicatas;
registrar dependências, correlações e suposições; marcar a qualidade da evidência (confirmada,
indicativa, hipótese).

**Classificação:** origem, processo afetado, pilar CIA, privacidade, segurança humana, continuidade,
financeiro, regulatório, reputação, integridade acadêmica/científica, terceiros, projeto, modelo/IA
e risco sistêmico.

### As 11 flags obrigatórias

1. Vida, saúde ou segurança humana
2. Obrigação legal/regulatória ou LGPD
3. Exploração conhecida (CISA KEV) ou ataque ativo
4. Processo crítico com RTO/RPO ameaçado
5. Dados pessoais sensíveis, grande volume ou pesquisa estratégica
6. Risco sistêmico ou ponto único de falha
7. Concentração em terceiro, nuvem ou identidade
8. Baixa probabilidade, impacto catastrófico
9. Risco emergente ou crescimento rápido
10. Alta incerteza ou evidência fraca
11. Risco de IA: discriminação, alucinação, prompt injection, deriva

### O que **não** entra como risco autônomo

Cada CVE individual sem cenário; alertas brutos sem triagem; ativos sem dono (é *issue*); tarefas
atrasadas sem impacto avaliado; controles ausentes sem cenário; duplicatas. Ficam em
backlog/finding/issue e são agregados ao cenário correspondente.

Manter uma lista executiva **"Top Risks"** separada do registro completo, com tendência, confiança,
exposição, dono e próxima decisão.

## Fase 3 — Análise, quantificação e priorização

Triagem qualitativa **apenas para encaminhamento inicial** — nunca como mecanismo de ordenação ou
investimento. Para riscos materiais, Open FAIR com Monte Carlo:

- **LEF** — frequência anual de eventos como distribuição (mínimo, mais provável, máximo).
- **LM** — magnitude por faixas: resposta, recuperação, produtividade, receita, responsabilidade,
  multas quando juridicamente aplicáveis, reputação estimada.
- **Saídas** — E[L]; percentis **P90/P95**; **CVaR** (cauda); inerente/atual/residual/alvo;
  intervalos de confiança; correlação e **agregação de portfólio**.

**Priorização de vulnerabilidades** combina: criticidade do processo/ativo, exposição
(interna/perimetral/externa), técnicas **ATT&CK**, presença em **CISA KEV**, **EPSS** (30 dias),
CVSS *como entrada e não como saída*, privilégios e blast radius, controles compensatórios.

**BIA:** MTPD/MAO, RTO, RPO, dependências críticas e efeitos em cascata.

## Fase 4 — Corte razoável: os quatro portões

| Portão | Critério |
|---|---|
| **A — Não discricionário** | Segurança humana, ilegalidade, obrigação regulatória, risco sem aceitação legítima, exploração ativa. Nenhuma análise econômica sobrepõe. Escalonamento imediato |
| **B — Apetite/tolerância** | Tratar ou escalar se E[L], P95/CVaR, indisponibilidade, perda de dados, nº de titulares ou outro KRI exceder o limite da Fase 0 |
| **C — Economia marginal** | Priorizar controles cujo `E[L antes] − E[L depois]` exceda o custo total e os efeitos colaterais (Gordon–Loeb como referência, não regra fixa) |
| **D — Capacidade/portfólio** | Maximizar redução esperada e resiliência sob orçamento, pessoas, dependências e prazo; preservar riscos de cauda e sistêmicos |

**Quatro decisões:** 🔴 agir imediatamente · 🟡 tratar no ciclo · 🔵 monitorar/aceitar (aceitação
formal com dono, prazo e gatilhos de reabertura) · ⚪ arquivar (justificativa + gatilho de
reabertura, revisão trimestral).

Calibrar o corte por **backtesting de incidentes e near misses**, análise de sensibilidade, curvas
benefício/custo e taxa de falsos negativos. Piloto de 8–12 semanas, revisão trimestral.

## Fase 5 — Tratamento

Quatro opções: **evitar**, **reduzir**, **transferir/compartilhar**, **aceitar**. Nenhum controle
deve existir sem rastreabilidade ao cenário que mitiga; o vínculo explícito risco–controle–custo é
condição para decisão de investimento defensável (Fenz et al., 2011).

Domínios de controle prioritários: identidade (MFA resistente a phishing, zero trust, PAM,
federação), rede (microssegmentação, SASE/SSE), aplicações (secure SDLC, SAST/DAST, segredos,
assinatura de artefatos, SBOM), dados (classificação, DLP, criptografia, RBAC), continuidade
(backups imutáveis testados, RTO/RPO validados, BCP/DRP exercitados), fornecedores (HECVAT,
cláusulas, auditoria, exit plan), pessoas (conscientização por função, phishing simulado), resposta
(playbooks por cenário, tabletop semestral, CSIRT/SOC).

Cada plano de ação: **ação específica, dono, prazo, evidência de conclusão e critério de aceite**.

## Fase 6 — Uso responsável de IA

**Permitido com humano no circuito:** extração de obrigações e riscos de contratos e políticas
(NLP/RAG); classificação de ativos e dados; correlação de eventos e anomalias; priorização assistida
de vulnerabilidades; previsão de tendências e KRIs; simulação de cenários (Monte Carlo assistido);
sumarização de incidentes e relatórios executivos; construção de grafos de dependência.

**Proibições absolutas:** a IA não aceita risco residual, não aprova exceções de política, não
encerra findings materiais, não decide sanções ou ações de alto impacto, e **não é dona de nenhum
risco** no registro.

**Controles obrigatórios:** inventário de modelos (finalidade, dados, fornecedor, versão); testes,
métricas e explicabilidade proporcionais ao risco; RAG com fontes e nível de confiança; proteção
contra prompt injection e DLP; avaliação de viés, drift, robustez e red team; logs completos e
rollback; revisão independente periódica.

Os riscos dos próprios componentes de IA entram no **mesmo registro**, com os mesmos campos e a
flag 11.

## Fase 7 — Monitoramento, métricas e aprendizagem

**Gatilhos obrigatórios de reavaliação:** mudança de arquitetura ou tecnologia; novo fornecedor,
aquisição ou migração; incidente ou near miss significativo; nova regulação; implantação de novo
modelo de IA; novos dados ou ultrapassagem de tolerância de KRI.

**Cadência:** mensal (KRIs, controles, incidentes) · trimestral (portfólio e corte) · anual
(auditoria independente) · pós-incidente (cenários e premissas).

**Métricas da metodologia:** cobertura de processos críticos e ativos descobertos; % de riscos com
dono e evidência; tempo médio de discovery até decisão; exposição agregada acima do apetite (E[L] e
P95); eficácia de controles e residual por domínio; tempo de correção de itens KEV; restauração
testada vs. RTO/RPO declarado; concentração em terceiros; taxa de riscos reabertos, incidentes não
previstos e falsos negativos; IA (precisão, recall, calibração, drift, taxa de override humano).

---

## Modelo de registro de risco

| Grupo | Campos |
|---|---|
| Identificação | ID único; objetivo estratégico; processo/atividade; serviço de TI |
| Propriedade | Dono de negócio; dono técnico; aprovador do residual — **papéis humanos, nunca IA** |
| Cenário | Ativo/dado; causa/ameaça; vulnerabilidade; evento central; consequências |
| Requisitos | Requisitos legais, regulatórios e contratuais aplicáveis |
| Controles | Controles existentes; eficácia estimada; evidências/fontes |
| Quantificação | LEF; LM; E[L]; P95/CVaR — distribuições, não pontos únicos |
| Níveis de risco | Inerente; atual/residual; alvo |
| Classificação | Flags obrigatórias 1–11; nível de confiança da evidência |
| Decisão | Agir/Tratar/Monitorar/Arquivar; plano; prazo — portões A–D aplicados |
| Monitoramento | KRI/gatilhos; dependências correlacionadas; última e próxima revisão |
| Aceitação | Dono de negócio, data, **prazo de expiração**, gatilho de reabertura |

## Adaptação empresa vs. universidade

| Dimensão | Empresa | Universidade / IES |
|---|---|---|
| Objetivo dominante | Receita, produção, cliente, market share | Ensino, pesquisa, extensão, missão pública |
| Governança | Hierarquia executiva | Colegiada, descentralizada, autonomia de unidades |
| Identidades | Força de trabalho estável | Ciclo intenso de alunos, visitantes, pesquisadores |
| Dados críticos | Clientes, financeiro, PI comercial | Estudantes, pesquisa, saúde (HU), integridade acadêmica, menores |
| Tecnologia | ERP, CRM, OT industrial, e-commerce | LMS, laboratórios, HPC, federação de identidade, BYOD |
| Terceiros | Cadeia comercial e industrial | Edtech, publishers, nuvem científica, HECVAT |
| Continuidade | Operação, receita, SLA | Calendário acadêmico, matrículas, avaliações, pesquisa |
| Controles | Eficiência operacional | Sem impedir liberdade acadêmica e colaboração científica |

## Papéis

**Conselho/Reitoria** aprova apetite e tolerância, recebe relatório trimestral, decide sobre riscos
de cauda · **Comitê de Risco de TI** prioriza portfólio, aprova cortes e tratamentos de custo
relevante · **Dono do processo de negócio** responde pelo impacto e **aceita formalmente o
residual** · **TI/Segurança** mantém controles, facilita, monitora, coordena resposta ·
**DPO/Jurídico/Auditoria** avalia privacidade e LGPD, fornece assurance independente.

## Roteiro de implantação (0–12 meses)

1. **10–30 dias** — comitê, escopo, taxonomia, apetite provisório, 3–5 processos críticos, RACI, fontes de dados.
2. **31–60 dias** — mapas processo–ativo–dado, 20–40 cenários iniciais, triagem qualitativa, piloto de IA controlado, BIA.
3. **61–90 dias** — quantificar top riscos com FAIR, calibrar os quatro portões, decidir tratamentos, painel executivo, perfil NIST atual e alvo.
4. **3–6 meses** — ampliar cobertura, avaliar fornecedores (HECVAT), tabletops, testes de restauração, assurance de IA.
5. **6–12 meses** — portfólio quantitativo completo, automação de KRIs, auditoria independente, avaliação de maturidade.

## Literatura de referência

Boehm (1991) · Cox (2008) · Gordon & Loeb (2002) · Spears & Barki (2010) · Webb et al. (2014) ·
Culot et al. (2021) · Fenz et al. (2011) · Merchan-Lima et al. (2021) · Shedden et al. (2016) ·
Hommel et al. (2015). DOIs completos na página 18 do PDF.
