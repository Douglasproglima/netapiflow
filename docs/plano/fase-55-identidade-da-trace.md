# Fase 55: a trace do NetApiFlow tem identidade própria (D-227, S3, ADR-0028)

O NetApiFlow usa o W3C trace id da requisição como identidade da trace, e o cliente escolhe esse id. A
auditoria de base achou (S3) e a Planejadora mediu nos dois bancos: o mesmo `traceparent` em duas requisições
perde uma trace, duas requisições ao mesmo tempo misturam as etapas numa trace só, e um cliente anônimo
sobrescreve uma trace guardada. O ADR-0028 decide o desenho: um id próprio, aleatório, e o W3C trace id como tag.

O ADR-0028 está **aceito** (D-228). **Nenhum código desta fase começa antes da Tarefa 53.1**, que mede o destino
das etapas anteriores à abertura da trace. O ADR-0027 (`PathBase`) está aceito (D-230) e é coerente com este: o id
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

## Resultado da 55.0 e a decisão da forma (D-269, 21/11/2026)

A Executora reproduziu os três achados em host real, visto falhando antes de qualquer mudança em `src/`, em `net10.0` (a `net8.0` ainda por rodar): 3 testes em 3 storages, 9 vermelhos. Em sequência, a memória guarda 1 trace de 3 e os bancos guardam 1 e logam a violação de chave. Ao mesmo tempo, a segunda resposta não traz `X-NetApiFlow-TraceId` nos três. Com o cliente escolhendo a chave, a memória é sobrescrita e os bancos não guardam a segunda. As cinco medições dispararam o ponto de parada: nenhuma das duas formas passa em todos os casos que funcionam hoje. **A matriz, os seis pontos, os custos e a forma escolhida estão no adendo do ADR-0028.** Em resumo: o `AsyncLocal` com verificação do W3C trace id, e a subida pela cadeia como reserva, numa função só que os seis pontos usam. O `ActivityContext` explícito sem fluxo vira perda dita no README, ao lado do `SuppressFlow`.

Uma observação lateral da Executora, que **não é achado:** com `Microsoft.Data.SqlClient` 5.2.2 a sonda não produziu passo de SqlClient, e com 6.1.8 produziu. O README já diz que os eventos do driver só trazem o comando legível a partir da 6.0 (D-172), então a 5.2.2 está abaixo do piso documentado.

## Tarefa 55.1: o id próprio e o mapeamento

Conforme o ADR-0028 e o adendo dele: o middleware gera o id aleatório de 32 caracteres hexadecimais, grava o estado da
requisição (o id próprio e o W3C trace id) num `AsyncLocal`, e grava o id próprio como propriedade customizada da atividade do
servidor. **Uma função só** resolve a trace de uma atividade: o `AsyncLocal`, se o W3C trace id da atividade é o dele; senão a
subida pelos pais; senão nenhuma trace. Os **seis** pontos que leem o trace id da `Activity` passam por ela (o middleware, o
`Sample`, o `OnActivityStarted`, o `OnActivityStopped`, o `CurrentTraceId` e o `FlowLogger`). O log do `FlowLogger` passa a
carregar o id próprio, o mesmo que a trace guarda. `CurrentTraceId` devolve `null` quando a requisição não tem trace em
andamento, inclusive a requisição que a Fase 53 decide não rastrear. Sem API pública nova.

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
- **A matriz da 55.0 vira teste de host, uma linha por caso:** `Task.Run`, `Task.WhenAll`, `IAsyncEnumerable`, `HttpClient` de
  saída, EF Core, SqlClient, Npgsql, `Activity.Current` atribuído à mão numa thread sem fluxo, e `ActivityContext` explícito com
  fluxo mantido. O caso do `ActivityContext` explícito **sem** fluxo é um teste que **fixa a perda**: a etapa não é anexada a
  trace nenhuma, nem a outra.
- **Três casos que a matriz não tem e que o `AsyncLocal` torna perigosos,** cada um com teste de host: (a) um `Task.Run` sem
  `await` que termina **depois** do fim da requisição não pendura span numa trace já encerrada nem cria uma trace fantasma; (b) a
  próxima requisição na mesma thread do pool não herda o id; (c) uma atividade cujo W3C trace id não é o da requisição que está
  no `AsyncLocal` (um worker que guardou o fluxo da requisição A e processa a B) não é pendurada em A.
- **O S3 é reproduzível de fora, então vira cenário permanente do job do pacote real:** o mesmo `traceparent` em três requisições
  seguidas dá três traces, com cabeçalhos diferentes. E a comparação com a `0.5.3` mostrará a mudança de identidade, então
  entra em `tests/real-package/expected-differences.json` com o texto da entrada do CHANGELOG.
- O `FlowLogger`: um log escrito dentro da requisição aparece na trace com o id próprio.

## Tarefa 55.4: README, contrato e CHANGELOG

- `contratos.md`: já atualizado pela Planejadora na aceitação do ADR (o significado de `FlowTrace.TraceId` e de
  `NetApiFlow.CurrentTraceId`).
- `README.md` e `README.pt-BR.md`: o id da trace não é o W3C trace id, **numa frase**, onde achar o W3C trace id (a
  tag e `Activity.Current.TraceId`), e o que mudou para quem usava o id nos testes. **E o caso que não é coberto, ao lado do
  `SuppressFlow`, nos dois idiomas:** uma atividade criada com um `ActivityContext` explícito, com o fluxo de execução
  suprimido, não é anexada à trace da requisição.
- `CHANGELOG.md`, em inglês: `### Changed` para o significado de `CurrentTraceId` e do id, e `### Fixed` para a
  perda e a mistura de traces em chamadas distribuídas. **Não** vai em `### Security` (D-228).

## Resultado da fase (PR #464, 21/11/2026)

Entregue em `develop` (`3e85afd`, head `cff31bc`). Cada requisição tem um id aleatório de 32 caracteres hexadecimais, e uma função só (`FlowTraceResolver`) acha a trace de uma atividade na ordem do D-269, usada pelos seis pontos, o `FlowLogger` incluído. `CurrentTraceId` devolve o id do NetApiFlow, e `null` fora de trace em andamento. O W3C trace id é a tag `w3c.trace_id`, toda atividade do NetApiFlow leva `netapiflow.trace_id`, e o detalhe da trace mostra "Trace" e "W3C trace". Sem diff de API pública.

- **As provas que a Planejadora leu nos logs do CI:** a sonda #465, com o id voltando a ser o W3C, derruba o cenário `i` do pacote real, o `NO LONGER HAPPENS requests-that-share-a-traceparent` da comparação com a `0.5.3` e 2 testes de integração. O job `comparison` mostra uma única diferença nova, anunciada no CHANGELOG. Os 9 vermelhos do 55.0 (3 comportamentos em 3 storages, em `net10.0` e em `net8.0`) e o custo são relato da Executora.
- **A rodada de revisão achou cinco coisas, todas corrigidas na mesma PR com teste visto falhando antes (relato da Executora):** a frase do README sobre o `SuppressFlow`, que dizia que a etapa depois dele é gravada como nova raiz, quando não está em trace nenhuma; as tags no detalhe da trace em execução e no evento `TraceStarted`, que a auditoria exigiu porque até a `0.5.3` o `trace.traceId` do evento era o W3C, e um consumidor externo do stream perderia a correlação com o OpenTelemetry (regressão da própria `0.6.0`); uma regressão de amostragem, em que o `Sample` pendurava no `AllDataAndRecorded` uma atividade de contexto estrangeiro dentro de uma requisição rastreada, passando por cima do sampler do OpenTelemetry do usuário, onde o `develop` dava `None`; a retenção do `HttpContext` por um `ExecutionContext` capturado, que passou a ser um holder zerado no fim da requisição; e o `MarkFinished` e o `Detach` fora de um `finally`. Um resolvedor de tenant que lança descartava a trace inteira, e agora só perde a tag.
- **Custo,** medido pela Executora: +0,13 a +0,23 KB por requisição no ponta a ponta (7,67 para 7,86 KB na rota rastreada), abaixo do teto de 0,6 KB da Fase 53. O tempo oscila nos dois sentidos entre rodadas do mesmo código, então não se afirma custo de tempo nem ausência dele.
- **O registro de guardas** ganhou 21 linhas do S3, com a #464 como `fixedIn`, mais a da `live-panel:ended`, pedida pela auditoria (D-268).

**Fica:** o holder ainda segura o objeto `Activity` do servidor (item 53 do roadmap), e o teste do assinante de `TraceCompleted` que lança afirma que a exceção chega ao usuário, o que a Fase 65 muda (B5). **Critério pendente, da auditoria:** refazer a medição com o pacote real e reconferir a frase do README na segunda passada.

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
- A correção do S3 entra com a sua linha em `tests/regression-guards/guards.json` (D-266), e a auditoria o cobra na PR.
- A auditoria reconfere a frase do README e refaz a medição com o pacote real.

## Versionamento e fluxo

- `0.6.0`. Branch `bugfix/55-identidade-da-trace` a partir de `develop` atualizado, **depois** do ADR-0028 aceito e
  coerente com o ADR-0027 (aceito), e **depois** da Tarefa 53.1.
- Commits pequenos, em inglês, uma linha, sem linha de coautor. Abrir a PR e parar.

## Relatório final

Para cada achado: o teste, a saída dele falhando antes e passando depois, nos três storages. Os números da 55.0
(cadeia de pais, custo, a atividade da tag). O que mudou de comportamento.
