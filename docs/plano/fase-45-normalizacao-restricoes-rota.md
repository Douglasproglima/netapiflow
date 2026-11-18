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

## Tarefa 45.1: decisão de normalização de restrições

Restrições de parâmetro (`:int`, `:guid`, `:min(1)`, `?` opcional, catch-all `{*path}`) precisam
de uma decisão de forma canônica antes de qualquer código:

1. A restrição é preservada para exibição (o usuário vendo `/api/orders/{id:int}` no histórico
   continua útil, mostra o tipo esperado) e removida só para comparação, ou guardada em campo
   separado?
2. Decisão travada nesta fase, registrada em `decisoes.md` antes da implementação: a forma
   exibida ao usuário nunca perde a restrição; a chave de comparação remove toda restrição
   (`{id:int}` e `{id}` comparam iguais), mesmo tratamento para `?` opcional e catch-all.
3. Estender o helper interno da tarefa 31.17 (não criar um segundo helper paralelo): a mesma
   função que já normaliza barra passa a normalizar restrição para a chave de comparação,
   mantendo o texto original para exibição.

## Tarefa 45.2: todos os pontos de comparação usam a mesma regra

A tarefa 31.17 já listou os pontos de comparação de rota (gravação, `GET /endpoints`, qualquer
rota recebida por query string e repassada a um `ITraceStore`). Esta tarefa confirma que a
extensão da 45.1 cobre os mesmos pontos, sem nenhum novo ponto de comparação de rota ter aparecido
desde a Fase 31:

- runner (OpenAPI) contra traces;
- blueprint;
- mapa de arquitetura;
- filtro de rota do Histórico;
- Insights.

**Pronto quando:** os cinco pontos usam a mesma chave de comparação (a função estendida da 45.1),
nenhum deles com lógica de comparação própria e divergente.

## Tarefa 45.3: testes

1. Teste unitário do helper cobrindo as variações: `api/orders/{id}` vs `/api/orders/{id:int}`,
   `{id:guid}` vs `{id}`, `{id:min(1)}` vs `{id}`, `{id?}` vs `{id}`, `{*path}` vs `{path}`.
2. Teste de integração com uma rota de parâmetro tipado (ex. `{id:int}`), executada pelo runner
   (chamando via OpenAPI, que não tem a restrição) e encontrada pelo blueprint e pela arquitetura
   (que comparam contra o `RawText`, que tem a restrição). Mesmo padrão de prova da tarefa 31.17:
   nenhuma rota real aparece como morta por divergência de formato.

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
