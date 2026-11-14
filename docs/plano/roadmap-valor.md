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
| 1 | Fase 31: correções da auditoria de frontend, achados Alta e Média (14 tarefas) | D-121 | 🔴 Alto, bugs reais de correção/UX já em produção | Planejada (`docs/plano/fase-31-correcoes-auditoria-frontend.md`), próxima a iniciar |
| 2 | Fase 32: correções da auditoria de frontend, achados Baixa (12 tarefas, 17 problemas atômicos: acessibilidade, i18n, validação, orçamento de build) | D-121, D-123 | ⚪ Baixo, não bloqueante | Planejada (`docs/plano/fase-32-correcoes-auditoria-frontend-baixa.md`), sai como hotfix `0.3.2` depois da 0.3.1 |
| 3 | Cenários de demonstração configuráveis pelo host: `NetApiFlowOptions` ganha uma lista de cenários, `GET /config` espelha, a aba "Scenarios" só aparece quando o host configurou pelo menos um, seletor e header de atraso artificial (`X-NetApiFlow-Demo-Delay-Ms`) seguem junto da aba, os dois samples deste repositório configuram os próprios cenários | D-122, escopo retirado da tarefa 31.1 por exigir API pública nova (MINOR), incompatível com a Fase 31 ter saído como hotfix PATCH `0.3.1` | ⚪ Baixo, sem fase atribuída | Ideia registrada, não planejada: a Fase 31 removeu a aba hardcoded (achado A1) em vez de torná-la configurável; esta é a reconstrução da função, do jeito certo, numa fase futura que já nasça MINOR |
| 4 | Ingestão de Dapper sobre SQL Server via `ExternalActivitySource` novo | D-114 (achado 4, dogfooding) | ⚪ Baixo, sem fase atribuída | Ideia registrada, não planejada: código misto EF Core + Dapper perde o nó `Database` na parte em Dapper, mesma lacuna que `ExternalActivitySource.Npgsql` já resolveu pro lado PostgreSQL |
| 5 | Piso em pixels na lista do painel "Testar um endpoint" (`.endpoint-form__list-wrapper`), hoje só `max-height: 55%` | D-116 (achado 2, dogfooding), R-28 | ⚪ Baixo, não bloqueante | Planejada: decisão de forma passa por `docs/design/flow-workspace/guide.md` antes do código |

## Já entregues (fora deste ranking, histórico em `docs/plano/README.md`)

Interop OTel ingestão (Fase 8), detector de N+1 (Fase 9), servidor MCP (Fase 10), Impeccable
(Fase 11, governança, não é item de roadmap de produto), tempo não explicado (Fase 12),
exportação OTLP (Fase 13), visão agregada "Insights" (Fase 14), sampling tail-based (Fase 15),
mapa de arquitetura vivo (Fase 16), log/exceção correlacionados por etapa (Fase 17), trace
portátil export/import (Fase 21), diff entre execuções (Fase 22), visibilidade de pré-pipeline
(Fase 23), asserção de fluxo em teste (Fase 24), controles e sinais visuais do grafo ao vivo
(Fase 25), cenários de demonstração e atraso artificial configurável (Fase 26), correções
pré-lançamento da primeira (Fase 29) e segunda (Fase 30) rodada de dogfooding, visibilidade de
cancelamento (Fase 27), anotação de deploy na timeline do histórico (Fase 28). Roadmap de valor
pós-`0.1.0` original (D-32, D-58, D-60) zerado; Fases 31/32 são um ciclo novo, de uma auditoria
real, não parte dele.

## Fora do ranking, decisão de escopo em aberto

Entry points não-HTTP (D-61): não é item de roadmap, é tensão de escopo sem decisão.
