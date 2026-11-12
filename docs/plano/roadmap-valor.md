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
| 1 | Fase 30: correções pré-lançamento, segunda rodada (regressão bloqueante da Fase 29 + lacunas de doc achadas numa segunda rodada de teste real) | D-114 | 🔴 Alto, bloqueante de divulgação pública | Planejada (`docs/plano/fase-30-correcoes-pre-lancamento-2.md`), próxima a iniciar |
| 2 | Fase 27: visibilidade de cancelamento (cliente vs. timeout) | D-60 (menor, sem posição fixa) | ⚪ Baixo | Planejada (`docs/plano/fase-27-visibilidade-cancelamento.md`) |
| 3 | Fase 28: anotação de deploy na timeline do histórico | D-60 (menor, sem posição fixa) | ⚪ Baixo | Planejada (`docs/plano/fase-28-anotacao-deploy.md`) |
| 4 | Ingestão de Dapper sobre SQL Server via `ExternalActivitySource` novo | D-114 (achado 4, dogfooding) | ⚪ Baixo, sem fase atribuída | Ideia registrada, não planejada: código misto EF Core + Dapper perde o nó `Database` na parte em Dapper, mesma lacuna que `ExternalActivitySource.Npgsql` já resolveu pro lado PostgreSQL |

## Já entregues (fora deste ranking, histórico em `docs/plano/README.md`)

Interop OTel ingestão (Fase 8), detector de N+1 (Fase 9), servidor MCP (Fase 10), Impeccable
(Fase 11, governança, não é item de roadmap de produto), tempo não explicado (Fase 12),
exportação OTLP (Fase 13), visão agregada "Insights" (Fase 14), sampling tail-based (Fase 15),
mapa de arquitetura vivo (Fase 16), log/exceção correlacionados por etapa (Fase 17), trace
portátil export/import (Fase 21), diff entre execuções (Fase 22), visibilidade de pré-pipeline
(Fase 23), asserção de fluxo em teste (Fase 24), controles e sinais visuais do grafo ao vivo
(Fase 25), cenários de demonstração e atraso artificial configurável (Fase 26), correções
pré-lançamento da primeira rodada de dogfooding (Fase 29).

## Fora do ranking, decisão de escopo em aberto

Entry points não-HTTP (D-61): não é item de roadmap, é tensão de escopo sem decisão.
