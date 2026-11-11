# Fase 20: `FlowSpan.Tags` no caminho genérico (R-22)

Projeto tocado: `src/NetApiFlow` (núcleo: `FlowActivityListener`, `LimitsOptions`, `FlowMetrics`),
`tests/NetApiFlow.Tests`, `tests/NetApiFlow.IntegrationTests`.

Pré-requisito: Fase 19 aceita. Achado incidental da revisão daquela fase: `FlowStep.Tag(...)`
escreve na `Activity` (`_activity.SetTag`), mas `BuildFlowSpan`/`CompleteFlowSpan` nunca leem
`activity.Tags` de volta para dentro de `FlowSpan.Tags`. Confirmado por `grep`: zero ocorrência de
`.Tags` em `FlowActivityListener.cs` fora do código novo da Fase 19 (`CompleteNpgsqlSpan`, que já
faz a cópia certa, com curadoria). Efeito prático: `CaptureSqlText = true` com
`FlowDbCommandInterceptor` nunca grava o SQL em `span.Tags` hoje, e qualquer tag customizada do
usuário da API pública tem o mesmo destino. Não é bug introduzido por nenhuma fase recente: as
três `ITraceStore` já serializam/deserializam `FlowSpan.Tags` corretamente
(`SerializeTags`/`DeserializeTags`), nenhuma mudança de schema é necessária, só a ingestão.

Achado adicional ao planejar: `.claude/rules/tracing.md` já promete um "limite de quantidade e
tamanho" para tag, nunca implementado (`LimitsOptions` não tinha campo pra isso). Mesma causa
raiz do R-22: nada escrevia, nada precisava limitar. Esta fase entrega os dois limites junto com
a cópia, para não nascer um caminho novo sem o controle que o resto do produto já tem
(`MaxSpansPerTrace`, `MaxLogEntriesPerSpan`).

Contrato congelado em `docs/plano/contratos.md`: `LimitsOptions.MaxTagsPerSpan` (padrão 20),
`LimitsOptions.MaxTagValueLength` (padrão 4000).

## Tarefa 20.1: copiar `activity.Tags` para `FlowSpan.Tags`

`BuildFlowSpan`/`CompleteFlowSpan` (caminho genérico, qualquer `NetApiFlow.Step()` do usuário)
passam a copiar `activity.Tags` (`IEnumerable<KeyValuePair<string, string?>>`, mesma API que
`CompleteNpgsqlSpan` já usa) para `FlowSpan.Tags`, excluindo por prefixo todo nome presente em
`FlowTagNames` (`netapiflow.component`, `netapiflow.operation`, `netapiflow.type`,
`netapiflow.error.*`): essas já são propriedades de primeira classe do `FlowSpan`
(`Component`/`Operation`/`Type`/`ErrorType`/`ErrorMessage`/`ErrorOrigin`), copiar de novo pra
`Tags` seria duplicar o mesmo dado em dois lugares.

Copiar no momento certo: `FlowStep.Tag(...)` pode ser chamado a qualquer momento entre abrir e
fechar o `Step` (não só no início), então a cópia final acontece em `CompleteFlowSpan`
(`OnActivityStopped`), nunca em `BuildFlowSpan` (`OnActivityStarted`), para capturar toda tag
adicionada durante a vida do span. `BuildNpgsqlSpan`/`CompleteNpgsqlSpan` (Fase 19) não mudam:
já fazem a curadoria certa para o caso deles (só `sql`, atrás de `CaptureSqlText`).

**Pronto quando:** `FlowStep.Tag("order_id", "123")` dentro de um `NetApiFlow.Step(...)` real
aparece em `span.Tags["order_id"]` depois que o span completa. Nenhuma tag `netapiflow.*` aparece
em `Tags` (continuam só como propriedade do `FlowSpan`). `CaptureSqlText = true` com
`FlowDbCommandInterceptor` volta a gravar `sql` em `span.Tags` (reativa o teste que hoje só
afirma a ausência).

## Tarefa 20.2: `MaxTagsPerSpan` e `MaxTagValueLength`

`LimitsOptions.MaxTagsPerSpan` (padrão 20, mesmo padrão de `MaxLogEntriesPerSpan`): tag além do
limite é descartada, nunca lança exceção. Qual tag sobrevive quando o limite é excedido: ordem de
chegada, as primeiras `MaxTagsPerSpan` ganham, mesma semântica simples já usada pelos outros
limites do produto (seção 6.7 do prompt).

`LimitsOptions.MaxTagValueLength` (padrão 4000): valor de tag além do limite é truncado, não
descarta a tag inteira. Trunca o valor, mantém a chave. Motivo do default generoso (4000, não 300
como mensagem de erro): tag de SQL completo (`CaptureSqlText`) pode ser bem maior que uma mensagem
de erro sem ficar patológico; o risco aqui é memória/tamanho de payload, não ReDoS de sanitização
(tag não passa por regex, só mensagem de erro passa).

Contador `netapiflow.tags.dropped` (`Meter("NetApiFlow")`), mesma convenção de
`netapiflow.logs.dropped` (ADR-0017): incrementa quando uma tag é descartada por quantidade.
Truncamento de valor não conta como descarte (a tag sobrevive, só o valor encolhe), sem contador
próprio, mesmo critério que distinguiu "descartado" de "truncado" em outros limites do produto.

**Pronto quando:** span com 25 tags custom grava só as 20 primeiras, `netapiflow.tags.dropped`
incrementa em 5. Tag com valor de 5000 caracteres grava truncado em 4000. Teste de alocação
confirma que o caminho sem tag nenhuma (caso comum) não paga custo extra, mesmo padrão de prova
já usado para os outros limites.

## Fora de escopo, de propósito

- Nenhuma mudança em `NetApiFlow.EntityFrameworkCore`, `ITraceStore`, schema de banco: a
  persistência de `FlowSpan.Tags` já funciona, o gap era só na ingestão.
- Nenhuma mudança em `CompleteNpgsqlSpan`/`BuildNpgsqlSpan` (Fase 19): a curadoria deles já é
  própria, correta, e não depende do caminho genérico.
- Sanitização de valor de tag: regra já existente (`.claude/rules/tracing.md`), "tags são só as
  explicitamente adicionadas", confiança no que o próprio programador escolheu gravar. Esta fase
  não muda essa régua, só faz o dado chegar onde a documentação já dizia que chegava.

## Aceite da fase

Todos os critérios de pronto acima. `dotnet build`/`test`/`format --verify-no-changes` limpos nos
dois TFMs. R-22 mitigado e provado.

## Ao terminar

Resumo para a Planejadora: o que foi feito, como verificar (comando exato), o que ficou pendente.
A Planejadora roda `/revisar-fase 20` antes de decidir o próximo passo (retomar
`docs/plano/roadmap-valor.md` ou abrir a conversa do D-61).
