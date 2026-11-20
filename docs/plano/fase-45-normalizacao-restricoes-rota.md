# Fase 45: Normalização de restrições de parâmetro de rota (complemento do A5, Fase 31)

Projeto tocado: núcleo (`NetApiFlow`) e os dois pacotes de storage (`NetApiFlow.EntityFrameworkCore`
não se aplica aqui; `NetApiFlow.Storage.PostgreSql`, `NetApiFlow.Storage.SqlServer`). Sem mudança
no dashboard Angular.

A Fase 31 (hotfix `0.3.1`, já aceita e mergeada) resolveu o achado A5 (tarefa 31.17): template de
rota gravado sem barra inicial contra listado com barra. Achado novo, no mesmo sample que gerou o
A5, visto depois da Fase 31 já fechada: **uma terceira variação**, não coberta pelo helper de
normalização daquela tarefa.

A Fase 31 está aceita e mergeada; este achado não reabre o arquivo dela (fase fechada é registro
histórico do que foi feito, não é editada para incluir trabalho novo). Vira fase própria,
referenciando o A5 original.

## O achado

- O runner lista `/api/orders/{id}` (vem do documento OpenAPI, sem restrição de tipo).
- Execuções recentes e traces mostram `/api/orders/{id:int}` (o `RawText` gravado, com restrição).

O helper de normalização da tarefa 31.17 resolve barra inicial/final, mas não resolve a presença
ou ausência de restrição de parâmetro: hoje as duas formas nunca são reconhecidas como a mesma
rota pelas mesmas comparações que o A5 original corrigiu (mapa de Arquitetura, blueprint, filtro
de rota do Histórico, Insights).

## Tarefa 45.1: função pura de comparação

**Entregue (PR #291):** `RouteTemplateNormalizer.NormalizeForComparison`, estendendo o helper da
tarefa 31.17 (não um segundo helper paralelo). Remove restrição (`:int`, `:guid`, `:min(1)`),
`?` opcional e o `*` do catch-all, mantendo `Normalize` (com restrição) intacto para exibição.
Testado isoladamente, sem tocar storage nem nenhum ponto de comparação real ainda.

## Tarefa 45.4: persistir a chave de comparação (D-164)

Parada original da Tarefa 45.1 resolvida: D-164 trava coluna nova, não sobrescrever
`route_template`. Sem isso, a restrição sumiria também da exibição, contradizendo o que a própria
fase pede.

1. Coluna `route_key` nos dois storages persistentes (`PostgreSqlTraceStore`/`SqlServerTraceStore`),
   computada por `NormalizeForComparison` no mesmo lugar que `route_template` já é gravado
   (`NetApiFlowMiddleware.cs`, antes de `UpdateRouteTemplate`). Índice próprio, espelhando o
   `ix_..._method_route` que já existe em `(method, route_template)`.
2. Migração dos dados já gravados: estender o `UPDATE` idempotente que
   `PostgreSqlSchemaInitializer`/`SqlServerSchemaInitializer` já rodam na primeira execução
   (D-124) para também preencher `route_key` de toda linha existente. `InMemoryTraceStore` não
   precisa de coluna (mesma razão do D-124: não sobrevive a restart).
3. `DashboardApiEndpoints.cs`: os dois pontos que hoje filtram por `route_template` com um valor
   vindo do Runner (`GetLastSuccessfulAsync`/blueprint, `GetArchitectureMapAsync`/arquitetura)
   passam a normalizar o `route` recebido por `NormalizeForComparison` e filtrar por `route_key`.
   `route_template` continua sendo o valor de exibição em todo o resto (inclusive `GET /endpoints`,
   que não muda).

**Pronto quando:** teste com Testcontainers nos dois bancos insere um registro com `route_key`
ausente (simulando dado anterior a esta fase), inicializa o store, confirma que `route_key` foi
preenchido. Teste de integração com uma rota tipada (`{id:int}`) chamada pelo runner (envia
`{id}`, sem restrição) encontrada pelo blueprint e pela arquitetura via `route_key`, sem precisar
que o chamador normalize nada além do que `NormalizeForComparison` já faz.

## Tarefa 45.2: todos os pontos de comparação usam a mesma regra

A tarefa 31.17 já listou os pontos de comparação de rota. Esta tarefa confirma, pra cada um,
**se ele de fato cruza um valor vindo do Runner/OpenAPI contra um `route_template` gravado** (só
esse caso precisa de `route_key`) ou se os dois lados de uma comparação específica sempre vêm do
próprio `ITraceStore` (já nascem consistentes, não precisam de nada novo):

- runner (OpenAPI) contra traces: cruza, usa `route_key` (Tarefa 45.4).
- blueprint: cruza, usa `route_key` (Tarefa 45.4).
- mapa de arquitetura: cruza, usa `route_key` (Tarefa 45.4).
- filtro de rota do Histórico: confirmar a origem do valor filtrado antes de decidir.
- `GetRouteStatsAsync`/Insights: confirmar a origem do valor filtrado antes de decidir.

**Pronto quando:** os cinco pontos documentados com a conclusão (precisa de `route_key` ou não,
com o motivo), nenhum com lógica de comparação própria e divergente dos outros que estão na mesma
situação.

## Tarefa 45.3: testes

1. Teste unitário do helper cobrindo as variações: `api/orders/{id}` vs `/api/orders/{id:int}`,
   `{id:guid}` vs `{id}`, `{id:min(1)}` vs `{id}`, `{id?}` vs `{id}`, `{*path}` vs `{path}`.
   **Entregue (PR #291)**.
2. Teste de integração com uma rota de parâmetro tipado (ex. `{id:int}`), executada pelo runner
   (chamando via OpenAPI, que não tem a restrição) e encontrada pelo blueprint e pela arquitetura
   (que comparam contra `route_key`, Tarefa 45.4). Mesmo padrão de prova da tarefa 31.17: nenhuma
   rota real aparece como morta por divergência de formato.

**Pronto quando:** os dois testes acima passam, e a suíte completa (dois TFMs, dois storages via
Testcontainers) continua verde.

## Fora de escopo

- Qualquer achado novo de template de rota que não seja presença/ausência de restrição de
  parâmetro: relatar à Planejadora para decisão de fase futura, não decidir sozinha.
- Dados já gravados antes desta fase com restrição em formato divergente: a tarefa 31.17 (D-124)
  já decidiu migrar dado gravado em vez de aceitar os dois formatos na consulta; esta fase segue a
  mesma régua, migração idempotente no mesmo lugar que a 31.17 já usa, não um mecanismo novo.

## Aceite da fase

Critérios de pronto das três tarefas acima. Build/test/lint/format dos dois lados (núcleo e os
dois pacotes de storage), nos dois TFMs.

## Ao terminar

Resumo para a Planejadora: o que foi feito por tarefa, como verificar, o que ficou pendente.
