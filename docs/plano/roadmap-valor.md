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
| 1 | Fase 25: controles e sinais visuais do grafo ao vivo (zoom visível, legenda, sparkline p95, follow-live) | D-32 | ⚪ Baixo | Planejada (`docs/plano/fase-25-grafo-controles-ao-vivo.md`), próxima a iniciar |
| 2 | Fase 26: cenários de demonstração + atraso artificial configurável (samples/Runner) | D-32 | ⚪ Baixo | Planejada (`docs/plano/fase-26-cenarios-demo-e-delay.md`) |
| 3 | Fase 27: visibilidade de cancelamento (cliente vs. timeout) | D-60 (menor, sem posição fixa) | ⚪ Baixo | Planejada (`docs/plano/fase-27-visibilidade-cancelamento.md`) |
| 4 | Fase 28: anotação de deploy na timeline do histórico | D-60 (menor, sem posição fixa) | ⚪ Baixo | Planejada (`docs/plano/fase-28-anotacao-deploy.md`) |

## Já entregues (fora deste ranking, histórico em `docs/plano/README.md`)

Interop OTel ingestão (Fase 8), detector de N+1 (Fase 9), servidor MCP (Fase 10), Impeccable
(Fase 11, governança, não é item de roadmap de produto), tempo não explicado (Fase 12),
exportação OTLP (Fase 13), visão agregada "Insights" (Fase 14), sampling tail-based (Fase 15),
mapa de arquitetura vivo (Fase 16), log/exceção correlacionados por etapa (Fase 17), trace
portátil export/import (Fase 21), diff entre execuções (Fase 22), visibilidade de pré-pipeline
(Fase 23), asserção de fluxo em teste (Fase 24).

## Fora do ranking, decisão de escopo em aberto

Entry points não-HTTP (D-61): não é item de roadmap, é tensão de escopo sem decisão.
