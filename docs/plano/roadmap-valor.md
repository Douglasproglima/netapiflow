# Roadmap por peso de valor de entrega

Registrado em D-79. Diferente de `decisoes.md` (append-only, histórico de decisões), este arquivo
é **vivo**: a Planejadora atualiza a tabela sempre que um item é entregue ou a prioridade muda,
sem precisar de uma decisão nova para isso. Mudar a nota de peso ou a posição de um item não
entregue é ajuste de planejamento normal; decidir entregar um item fora de ordem, ou mudar o
critério de peso em si, é decisão nova (vira linha em `decisoes.md`, referenciando este arquivo).

## Critério de peso

Quatro eixos, nenhum peso numérico fixo: impacto estratégico (o que o audit de mercado, D-58/D-60,
já apontou como lacuna perigosa), dor real do dev fechada, esforço de implementação, e potencial de
divulgação orgânica (projeto ainda sem lançamento público divulgado, D-70).

## Backlog, por peso

| # | Item | Fonte | Peso | Status |
|---|---|---|---|---|
| 1 | Visão agregada "Insights" | D-60 (Pulse) | 🔴 Alto | Em planejamento (Fase 14) |
| 2 | Sampling tail-based | D-58 item 3 | 🟡 Médio-alto | Backlog |
| 3 | Mapa de arquitetura vivo | D-60 item E | 🟡 Médio-alto | Backlog |
| 4 | Log/exceção correlacionados por etapa | D-58 item 5 | 🟡 Médio | Backlog |
| 5 | Trace portátil (export/import) | D-60 item C | 🟢 Médio | Backlog |
| 6 | Diff entre execuções | D-58 item 4 | 🟢 Médio-baixo | Backlog |
| 7 | Visibilidade de pré-pipeline | D-60 item D | 🟢 Médio-baixo | Backlog |
| 8 | Asserção de fluxo em teste | D-58 item 6 | 🟢 Baixo-médio | Backlog |
| 9 | D-32 (zoom visível, legenda, sparkline, cenários, delay, follow-live) | D-32 | ⚪ Baixo | Backlog |
| — | Visibilidade de cancelamento, anotação de deploy | D-60 (menores) | ⚪ Baixo | Backlog, sem posição fixa |

## Já entregues (fora deste ranking, histórico em `docs/plano/README.md`)

Interop OTel ingestão (Fase 8), detector de N+1 (Fase 9), servidor MCP (Fase 10), Impeccable
(Fase 11, governança, não é item de roadmap de produto), tempo não explicado (Fase 12),
exportação OTLP (Fase 13, era item 1 deste ranking).

## Fora do ranking, decisão de escopo em aberto

Entry points não-HTTP (D-61): não é item de roadmap, é tensão de escopo sem decisão.
