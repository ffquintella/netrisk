# Metodologia de referência: MIGR-TI/IA

Esta pasta guarda a metodologia de gestão de riscos que o NetRisk toma como referência de produto,
e a análise de aderência do sistema a ela.

| Documento | O que é |
|---|---|
| [migr-ti-ia-guide.pdf](migr-ti-ia-guide.pdf) | O relatório técnico-acadêmico original (18 páginas, 17/09/2026), versionado no repositório |
| [migr-ti-ia.md](migr-ti-ia.md) | Sumário estruturado da metodologia — as 7 fases, os 10 princípios, os 4 portões e o modelo de registro de risco |
| [migr-ti-ia-coverage.md](migr-ti-ia-coverage.md) | **Análise de aderência:** atividade por atividade, o que o NetRisk instrumenta hoje, o que instrumenta parcialmente e o que não existe |
| [icr-indice-consolidado-de-risco.md](icr-indice-consolidado-de-risco.md) | **Metodologia do ICR (S39):** como o NetRisk calcula o Índice Consolidado de Risco de 0 a 100 do painel "Visão Geral de Risco Cibernético", com pesos configuráveis, versionados e aprovados pelo Comitê de Risco de TI. Cobre os sinais de cada fonte, as revisões, aceitações e campanhas (e por que só a revisão de gestão qualificada restaura crédito de tratamento), o drill-down por conjunto, os defeitos do produto que bloqueiam o modo interino e o que fica fora do índice para não contradizer a MIGR-TI/IA: o ICR não é portão, não é KRI e não ordena tratamento |
| [Track 9](../roadmap/TRACK_9_MIGR_TI_IA.md) | **Plano de adequação:** as 12 etapas que fecham as lacunas da análise, cada uma precedida por uma especificação completa e entregue com testes |

O PDF é a fonte autoritativa. O sumário em Markdown existe para que as tabelas de aderência possam
citar fases e campos por nome sem abrir o PDF, e para que uma mudança na metodologia apareça em um
diff.

A análise de aderência é o **critério de aceitação** do Track 9, e é por isso que ela vive aqui e não
no documento do track: é medida contra o código, repetidamente, em vez de escrita uma vez.
