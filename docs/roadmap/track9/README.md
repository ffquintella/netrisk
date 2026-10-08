# Especificações das etapas do Track 9

Uma especificação por etapa, `9.N-<slug>.md`, **mesclada antes do primeiro commit de implementação**
da etapa. Regra completa e justificativa:
[TRACK_9_MIGR_TI_IA.md § Regra do track](../TRACK_9_MIGR_TI_IA.md#regra-do-track-nada-é-implementado-antes-da-especificação-nada-é-entregue-sem-teste).

Especificações escritas: [9.1 — Cadeia de ligação](9.1-linkage-chain.md) (S41);
[9.2 — Cenário estruturado, discriminação de registros e confiança da evidência](9.2-structured-scenario.md) (S42);
[9.3 — BIA: MTPD/MAO, RTO, RPO e dependências em cascata](9.3-bia-continuity.md) (S43);
[9.4 — Sinais de exploração: CISA KEV, EPSS de primeira classe e MITRE ATT&CK](9.4-exploitation-signals.md) (S45);
[9.5 — As onze flags obrigatórias e o Portão A](9.5-flags-gate-a.md) (S46);
[9.6 — Economia do tratamento: custo monetário, Portões C e D, as quatro opções](9.6-treatment-economics.md) (S47);
[9.7 — Estatística de cauda e portfólio: P95, CVaR, agregação e correlação](9.7-tail-statistics-portfolio.md) (S48);
[9.8 — KRIs, gatilhos obrigatórios de reavaliação e métricas da metodologia](9.8-kri-reassessment-metrics.md) (S49);
[9.9 — Arquivamento com gatilho, backtesting, comitê de risco e terceira linha](9.9-archive-backtesting-committee.md) (S50);
[9.10 — Registro de terceiros: HECVAT, SBOM, concentração e exit plan](9.10-third-party-register.md) (S51);
[9.11 — Catálogo de dados LGPD: base legal, finalidade, retenção, localização e DPIA](9.11-lgpd-data-catalogue.md) (S52);
[9.12 — Governança de IA: inventário de modelos, flag 11 e métricas de modelo](9.12-ai-governance.md) (S53). Uma etapa cuja
especificação não está aqui não pode ter PR de implementação aberto, e seu item no
[ROADMAP.md](../../../ROADMAP.md) não pode ser marcado.

Use [_TEMPLATE.md](_TEMPLATE.md) como ponto de partida: as onze seções são obrigatórias, e uma seção
que não se aplica é **declarada** como tal com o motivo, nunca removida — uma seção ausente é
indistinguível de uma seção esquecida.
