# Fase 55: a trace do NetApiFlow tem identidade própria (D-227, S3, ADR-0028)

O NetApiFlow usa o W3C trace id da requisição como identidade da trace, e o cliente escolhe esse id. A
auditoria de base achou (S3) e a Planejadora mediu nos dois bancos: o mesmo `traceparent` em duas requisições
perde uma trace, duas requisições ao mesmo tempo misturam as etapas numa trace só, e um cliente anônimo
sobrescreve uma trace guardada. O ADR-0028 decide o desenho: um id próprio, aleatório, e o W3C trace id como tag.

O ADR-0028 está **aceito** (D-228). **Nenhum código desta fase começa antes de ele ser coerente com o ADR-0027**
(`PathBase`), porque os dois decidem em que momento a trace abre. O ADR-0027 está escrito, Proposto (D-229): o id
nasce no início do middleware e a trace abre no `EndpointMatched`. Vai no `0.6.0` (D-210, D-212). É defeito
antigo e não regressão, então não é hotfix (D-205).

## Os achados e o que a Planejadora conferiu

| # | Achado | Conferi |
|---|---|---|
| S3 | A identidade da trace é o W3C trace id que o cliente manda. O mesmo `traceparent` apaga a primeira trace, um serviço que chama a API duas vezes vira etapas dentro da trace do chamador, e um cliente anônimo escolhe o id e sobrescreve uma trace guardada | **Reproduzi** contra os pacotes `0.5.2`, em PostgreSQL 16 e SQL Server 2022: em sequência, **uma** linha (a primeira) e as outras duas falham com erro; ao mesmo tempo, a segunda requisição não recebe `X-NetApiFlow-TraceId` e a trace guardada tem as etapas das duas. A tabela está no ADR-0028. **A auditoria** mediu a store em memória (o último vence) e o cliente anônimo sobrescrevendo. Eu não reproduzi esses dois |

## Ponto de parada

Se a Tarefa 55.0 achar um caso que **funciona hoje** e deixaria de funcionar com o id próprio (uma etapa que hoje é
anexada pelo W3C trace id e que a cadeia de pais não alcança), a Executora **para e relata**. É decisão da
Planejadora e da auditoria, não dela.

## Tarefa 55.0: reproduzir e medir, antes de qualquer código

Testes de **host real**, vistos falhando antes de qualquer mudança, nos **três** storages (memória, PostgreSQL e
SQL Server, estes dois com Testcontainers, em `NetApiFlow.Storage.Tests`):

- **Em sequência:** três requisições com o mesmo `traceparent`. O esperado é **uma trace por requisição**, as três
  guardadas, nenhum erro no log. Hoje: memória, uma (a última); bancos, uma (a primeira) mais dois erros.
- **Ao mesmo tempo:** duas requisições com o mesmo `traceparent` a uma rota lenta com uma etapa. O esperado: as duas
  recebem `X-NetApiFlow-TraceId`, **diferentes**, e cada trace tem só as etapas dela. Hoje: a segunda não recebe o
  cabeçalho e a trace tem as etapas das duas.
- **O cliente escolhe a chave:** uma requisição com um `traceparent` cujo trace id é o de uma trace já guardada não
  a sobrescreve.

**E mede, sem corrigir**, os cinco pontos que o ADR-0028 deixa para antes do código:

1. **As duas formas de achar a trace, medidas lado a lado** (decisão da auditoria): (1) subir a cadeia de
   `Activity.Parent` até a atividade do servidor, (2) um `AsyncLocal` com o id, gravado pelo middleware. Os casos:
   `Task.Run`, `Task.WhenAll`, `IAsyncEnumerable`, `HttpClient` de saída, EF Core, SqlClient, Npgsql,
   `ExecutionContext.SuppressFlow` e uma atividade criada com um `ActivityContext` explícito. Para cada um, a
   etapa é anexada hoje? E seria com cada forma? A escolhida é a que passar em **todos** os casos com o menor custo
   medido, e, se empatarem, a mais simples. Um pai remoto explícito quebra a subida pelos pais e não quebra o
   `AsyncLocal`, e o `SuppressFlow` quebra os dois, como o README já diz.
2. **A tag `netapiflow.trace_id` em toda atividade do NetApiFlow**: confirmar que um span exportado pelo
   `NetApiFlow.OpenTelemetry` a carrega, e medir o custo de uma tag por atividade.
3. **O custo de cada forma**, com `tests/NetApiFlow.Benchmarks` e `[MemoryDiagnoser]`.
4. **Os cinco pontos que leem `activity.TraceId`** hoje (o middleware, três no `FlowActivityListener`, e o
   `NetApiFlow.CurrentTraceId`): confirmar a lista com `grep`, porque o ADR a diz pelo que a Planejadora leu.
5. **O que a Fase 53 muda** no momento em que a trace abre, e se o id nascer no início da atividade do servidor
   continua coerente com o mecanismo do ADR-0027.

O relatório da 55.0 diz o que foi reproduzido, os números, e o que muda de comportamento.

## Tarefa 55.1: o id próprio e o mapeamento

Conforme o ADR-0028: o middleware gera o id aleatório de 32 caracteres hexadecimais, e **uma função só** resolve a
trace de uma atividade pela forma que a 55.0 escolheu (a subida pelos pais, ou o `AsyncLocal`). Os quatro pontos
que leem `activity.TraceId` passam por ela. Sem API pública nova.

## Tarefa 55.2: as tags, o OTLP, o `CurrentTraceId` e os dois ids no detalhe

- A tag `w3c.trace_id` da trace, quando há `Activity`.
- **A tag `netapiflow.trace_id` em toda atividade do NetApiFlow**, para o backend OTLP chegar à trace do painel.
- `NetApiFlow.CurrentTraceId` devolve o id do NetApiFlow, e `null` quando não há trace da requisição em andamento.
  O XML doc diz em uma linha que **não** é o W3C trace id e onde achá-lo (`Activity.Current.TraceId`).
- **O detalhe da trace mostra os dois ids rotulados**, "Trace" e "W3C trace" (e os dois idiomas do painel), lendo
  o W3C da tag. Teste de componente. Os dois têm o mesmo formato, e sem o rótulo quem cola o id do painel no Jaeger
  não acha nada.

## Tarefa 55.3: os testes

Os três testes da 55.0, agora **verdes** nos três storages, mais:

- O cabeçalho `X-NetApiFlow-TraceId` é o id próprio, e `NetApiFlow.Testing.GetFlowTrace` e `GetRecentTrace` funcionam
  com ele.
- O `traceparent` de entrada aparece na tag `w3c.trace_id` da trace.
- **Um span exportado** (o exportador em memória do SDK) carrega o trace id W3C nativo e a tag `netapiflow.trace_id`.
- O detalhe da trace mostra os dois ids com os rótulos "Trace" e "W3C trace".
- Cada caso de cadeia de pais da 55.0 que funcionava hoje continua funcionando.
- Uma trace gravada **antes** (com o id antigo, o W3C trace id) continua legível.

## Tarefa 55.4: README, contrato e CHANGELOG

- `contratos.md`: já atualizado pela Planejadora na aceitação do ADR (o significado de `FlowTrace.TraceId` e de
  `NetApiFlow.CurrentTraceId`).
- `README.md` e `README.pt-BR.md`: o id da trace não é o W3C trace id, **numa frase**, onde achar o W3C trace id (a
  tag e `Activity.Current.TraceId`), e o que mudou para quem usava o id nos testes.
- `CHANGELOG.md`, em inglês: `### Changed` para o significado de `CurrentTraceId` e do id, e `### Fixed` para a
  perda e a mistura de traces em chamadas distribuídas. **Não** vai em `### Security` (D-228).

## Sem quarto advisory (D-228)

A auditoria decidiu **não** publicar um quarto advisory, pela classificação técnica: o S3 é um defeito de correção e
não uma vulnerabilidade. Um cliente só consegue colidir com um trace id que ele mesmo escolheu, e não lê nem apaga a
trace de outra pessoa, porque não conhece os ids aleatórios dos outros. O dano real é a perda e a mistura de
traces em chamadas distribuídas legítimas. Vai em `### Fixed`, bem explicado.

## Fora de escopo

- Buscar uma trace pelo W3C trace id no painel: item 30 do roadmap.
- Migrar as traces já gravadas.
- Qualquer mudança na forma como o W3C trace id é propagado para fora (o `traceparent` de saída continua o do
  runtime).

## Critério que vale para a fase inteira

- Build, testes e formato limpos nos dois TFMs. `AotCoreVerification` com zero aviso.
- Cada achado com código tem teste de host real **visto falhando antes**, nos três storages.
- A auditoria reconfere a frase do README e refaz a medição com o pacote real.

## Versionamento e fluxo

- `0.6.0`. Branch `bugfix/55-identidade-da-trace` a partir de `develop` atualizado, **depois** do ADR-0028 aceito e
  coerente com o ADR-0027.
- Commits pequenos, em inglês, uma linha, sem linha de coautor. Abrir a PR e parar.

## Relatório final

Para cada achado: o teste, a saída dele falhando antes e passando depois, nos três storages. Os números da 55.0
(cadeia de pais, custo, a atividade da tag). O que mudou de comportamento.
