# Fase 24: Asserção de fluxo em teste

Projeto tocado: só `src/NetApiFlow` (núcleo). Nenhum pacote novo, nenhuma mudança em
`NetApiFlow.Dashboard`, `NetApiFlow.Mcp` ou qualquer storage. Nenhuma ADR: mecanismo já existe,
só precisa virar público.

Item 1 do roadmap de valor (D-58 item 6), depois da Fase 23. Dor documentada em D-58: afirmar em
teste de integração que um endpoint "não pode passar de N queries" (ou qualquer outra propriedade
do fluxo), hoje só dá pra fazer inspecionando manualmente, sem API pública pra isso.

Pesquisa da Planejadora confirmou que o produto já resolveu a parte difícil: `FlowRecorder`
(interno) já guarda "a trace que terminou recentemente, enquanto o writer de persistência não
processou" (`GetRecentlyCompleted`, buffer de 500, usado hoje por `GET /traces/{traceId}` do
dashboard pra responder sem esperar a fila assíncrona de `PersistenceWriter`). Isso **é**
exatamente o mecanismo que resolve o problema de um teste de integração que acabou de aguardar sua
própria chamada HTTP e precisa do resultado imediatamente, sem corrida com a persistência
assíncrona. Só nunca foi exposto como API pública, porque até agora só o próprio dashboard
precisava dele.

## Tarefa 24.1: `NetApiFlow.GetRecentTrace`

Método estático novo em `src/NetApiFlow/NetApiFlow.cs` (a fachada pública já existente, ao lado de
`Step`/`CurrentTraceId`): `public static FlowTraceDetail? GetRecentTrace(IServiceProvider
services, string traceId)`. Resolve `FlowRecorder` via `services.GetService<FlowRecorder>()`
(continua `internal`, acessível de dentro do próprio assembly) e devolve
`recorder?.GetRecentlyCompleted(traceId)`.

**Deliberadamente não cai pro `ITraceStore`** quando a trace não está no buffer em memória,
diferente de `GET /traces/{traceId}`: um teste assertando logo depois de aguardar sua própria
chamada HTTP sempre encontra a trace lá (completada de forma síncrona dentro do próprio
pipeline da requisição, antes da resposta HTTP retornar); cair pro store reintroduziria a mesma
corrida com a fila assíncrona que o método existe pra evitar. `null` quando a trace já saiu do
buffer (mais de 500 traces completadas entre a chamada e a asserção, cenário que não deveria
acontecer dentro de um teste isolado) ou quando `NetApiFlow` nunca foi registrado
(`services.GetService` em vez de `GetRequiredService`, mesmo princípio de "nunca travar" aplicado
aqui: devolve `null`, não lança).

Comentário XML da versão real em `.cs`: mantém a explicação do mecanismo (buffer em memória, sem
corrida com a fila de persistência), nunca cita fase, ADR ou decisão (regra de ouro, D-87). O
preview em `docs/plano/contratos.md` cita D-58 item 6 porque é documento de planejamento; o código
de produção não repete essa citação.

**Pronto quando:** um teste de integração real (`WebApplicationFactory`) faz uma chamada HTTP,
lê `X-NetApiFlow-TraceId` do cabeçalho da resposta, chama `NetApiFlow.GetRecentTrace(services,
traceId)` logo em seguida, e recebe de volta a `FlowTraceDetail` completa (com todos os spans já
fechados, incluindo os de banco de dados se a rota tiver EF Core), sem nenhum `Task.Delay`/retry
pra esperar a persistência assíncrona. Chamar com um `traceId` que nunca existiu devolve `null`.

## Tarefa 24.2: documentação com exemplo real

README e README.pt-BR.md ganham uma seção curta (ou um parágrafo dentro de uma seção já
existente, decisão de forma da Executora) mostrando o padrão: fazer a chamada via
`WebApplicationFactory`, ler o header, chamar `GetRecentTrace`, e then um exemplo de asserção
tipo "este endpoint não executa mais de N consultas" usando LINQ simples sobre `trace.Spans`
(`trace.Spans.Count(s => s.Type == ComponentType.Database)`), sem inventar nenhuma DSL de
asserção nem forçar uma biblioteca de asserção específica (o produto não assume que quem usa
Shouldly, FluentAssertions ou o `Assert` do próprio xUnit; `GetRecentTrace` devolve dado puro,
cada um usa a biblioteca de asserção que já tem no próprio projeto). `CHANGELOG.md`, seção
`[Unreleased]` → `### Added`.

**Pronto quando:** README e README.pt-BR.md atualizados, conteúdo equivalente.

## Fora de escopo, de propósito

- Nenhum pacote novo (`NetApiFlow.Testing` ou similar): o método não precisa de nenhuma
  dependência de framework de teste, vive bem no núcleo.
- Nenhuma DSL de asserção pronta (ex. `trace.ShouldHaveNoMoreDatabaseCallsThan(3)`): LINQ simples
  sobre `FlowSpan` já resolve, inventar uma API de asserção fixa a uma biblioteca específica seria
  YAGNI.
- Suporte a trace ainda em andamento (`GetActiveTrace`): o caso de uso é sempre "a requisição já
  terminou, estou no código depois do `await` da chamada HTTP", não precisa do caminho de trace
  em andamento.

## Aceite da fase

Critério de pronto das duas tarefas acima. `dotnet build`/`test`/`format --verify-no-changes`
limpos nos dois TFMs.

## Ao terminar

Resumo pra Planejadora: o que foi feito, como verificar (teste de integração real mostrando o
padrão completo), o que ficou pendente. A Planejadora roda `/revisar-fase 24` antes de decidir o
próximo item do roadmap de valor.
