# Fase 57: a visão ao vivo diz a verdade (D-210, F1)

O painel promete duas coisas. Enquanto a requisição roda, mostra o que ela já fez e o que ainda deve fazer
("Aguardando"). Depois que ela termina, mostra o que **não** aconteceu naquela execução ("Não executado"). A
auditoria de base achou (F1) que as duas promessas falham, de dois jeitos diferentes.

Vai no `0.6.0` (D-210, D-232). É defeito antigo e não regressão, então não é hotfix (D-205).

## Os dois defeitos e o que a Planejadora conferiu

| # | Defeito | Conferi |
|---|---|---|
| F1a | "Aguardando" e "Não executado" não funcionam ao vivo. A trace abre com a rota `(unmatched)` e só grava a rota verdadeira no fim, e o painel busca o blueprint por essa rota, então nunca acha | **Código:** o middleware abre com `UnmatchedRouteTemplate` (`NetApiFlowMiddleware.cs:43`). `flow-page.component.ts` (linhas 230 a 240) chama `getBlueprint(trace.method, trace.routeTemplate)` **uma vez por trace**, com a rota do momento da abertura, e não repete. **Não reproduzi no navegador** |
| F1b | O blueprint é a última execução com sucesso do endpoint, **mesmo posterior** à trace que está sendo vista. A trace B, mais nova, vira o blueprint da trace A, mais antiga, e o desenho de A muda com o tempo | **Código:** `GetLastSuccessfulAsync(method, routeKey, ct)` não recebe âncora de tempo, e os três stores fazem `ORDER BY started_at DESC` com uma linha. **Reproduzida pela auditoria no `0.5.2`** (comandos em `auditoria-de-base.md`). Não reproduzi de novo |

## O que decide o desenho

- **F1b não depende de nada.** A âncora de tempo (store, REST, frontend) pode ser feita já.
- **F1a depende da Fase 53.** Com a trace abrindo no `EndpointMatched` (ADR-0027), ela abre com a rota verdadeira.
  Antes disso, não há o que consertar no painel. Para requisição sem endpoint casado a rota continua `(unmatched)`,
  e o painel **não** pede blueprint nesse caso.
- **O blueprint de uma trace é a última execução com sucesso que começou antes dela.** É a única definição que não
  muda com o tempo e que nunca usa a própria trace. Se não existe nenhuma anterior, não há blueprint, e o desenho
  mostra só o que aconteceu. Isso é verdade e é menos que hoje, e é a escolha certa.

## Ponto de parada

- Se a 57.0 não reproduzir F1a ou F1b pelos comandos, **para e relata**.
- Se a consulta com âncora precisar de **índice novo** (mudança de schema nos dois storages), para e relata. É
  decisão da Planejadora e da auditoria.
- Se o membro novo do `ITraceStore` com implementação padrão não compilar ou se comportar igual nos dois TFMs
  (`net8.0` e `net10.0`), para e relata.

## Tarefa 57.0: reproduzir e medir, antes de qualquer código

Host real e navegador de verdade, porque o curl vê o status e não o que o JavaScript do painel faz depois. Um endpoint
de exemplo com retorno antecipado, como o da auditoria:

```csharp
app.MapGet("/orders/{id:int}", async (int id, bool cached) => {
    using (NetApiFlow.NetApiFlow.Step("OrderCache", "TryGet")) { await Task.Delay(5); }
    if (cached) return "cached";
    using (NetApiFlow.NetApiFlow.Step("OrderRepository", "Find")) { await Task.Delay(5); }
    return "loaded";
}).WithNetApiFlowTracking();
```

1. **F1a.** Uma variante lenta (`Task.Delay(1500)` entre as etapas). Enquanto ela roda, ler `GET /flow/api/traces/{id}` e
   o evento `TraceStarted` do SSE e anotar a rota. Abrir a trace ao vivo no painel e capturar a tela: aparece algum
   nó "Aguardando"?
2. **F1b.** A sequência da auditoria (apagar, A com `cached=true`, B com `cached=false`, `GET /blueprint`). Depois, no
   navegador, abrir `/flow?trace=<A>`: o nó `OrderRepository.Find` aparece como "Não executado" no desenho de A,
   que é de uma execução **posterior**? Captura de tela.
3. **O custo da consulta com âncora**, nos dois bancos (PostgreSQL 16 e SQL Server 2022, Testcontainers): a consulta de
   hoje contra a mesma com `AND started_at < @before`, com 100 mil traces da mesma rota e 10 mil de outras, plano
   de execução real, com os índices que já existem (`ix_netapiflow_traces_method_route_key` e `..._started_at`).
   Se for preciso índice novo, **para**.
4. **Quem chama `GetLastSuccessfulAsync`**, por `grep`: confirmar que é só o painel e testes.

**Pronto quando:** os quatro pontos respondidos com saída real e captura de tela.

## Resultado da 57.0, medido pela Executora (21/11/2026)

Host real (Kestrel, `net10.0`) com o endpoint de exemplo da auditoria e o painel de verdade no Edge por CDP. Nenhum ponto de parada disparou.

- **F1a reproduzido.** Com a requisição lenta em execução, `GET /flow/api/traces/live`, o detalhe da trace e o evento SSE `TraceStarted` trazem `routeTemplate` `(unmatched)`. O painel ao vivo mostra os nós em execução e concluído, nenhum nó "Aguardando" e "Não executadas 0".
- **F1b reproduzido.** `GET /blueprint` sem âncora devolve a trace B, que começou depois da A, e `/flow/?trace=A` mostra `OrderRepository.Find` como "Não executado" por causa de uma execução posterior. Antes de a B existir, o blueprint da A era a própria A, o que a definição "estritamente antes" também corrige.
- **Custo da consulta com âncora**, com 110 050 traces e os índices que os `schema.sql` dos pacotes distribuem: **não precisa de índice novo, nos dois bancos**. No PostgreSQL 16, a rota principal fica entre 0,09 e 0,44 ms com a âncora (0,15 ms hoje), e a rota rara cai de 2,4 a 6,2 ms para 1,2 a 2,4 ms. No SQL Server 2022, a rota principal usa 7 leituras lógicas nos dois casos, e a rota rara cai de 6766 para 3500 leituras. A âncora não piora nenhum caso e melhora o pior.
- **Quem chama `GetLastSuccessfulAsync`:** em produção só o `GetBlueprint` do painel. Os três `ITraceStore` de mentira dos testes (`FlowEventPublisherTests`, `FlowMetricsTests`, `PersistenceWriterTests`) implementam o membro, o que confirma que o membro novo precisa ser de interface com implementação padrão.
- **Dívida anterior, fora desta fase:** a condição de método (`ILIKE` no PostgreSQL, `UPPER` no SQL Server) impede o índice `(method, route_key)`, e a rota rara varre o índice por data. Roadmap, item 48.

## Tarefa 57.1: a âncora no store

`ITraceStore` ganha

```csharp
ValueTask<FlowTraceDetail?> GetLastSuccessfulAsync(string method, string routeKey, DateTime before, CancellationToken cancellationToken);
```

como **membro de interface com implementação padrão**. Não como membro abstrato: um membro abstrato novo numa interface
pública quebra com `TypeLoadException` quem tem um storage de um release anterior e atualiza só o núcleo, a mesma
classe do D-140. A implementação padrão chama o método de hoje e devolve o resultado **só se** ele começou antes de
`before`, senão `null`. Ela nunca devolve um blueprint errado, e devolve menos quando não tem certeza.

Os três stores do repositório (memória, PostgreSQL e SQL Server) implementam o método de verdade, com
`started_at < @before` (estrito, então a própria trace nunca é o blueprint dela mesma) e o mesmo `ORDER BY started_at
DESC` com uma linha. Só trace com `Success` entra, e a rota é comparada normalizada, como hoje.

## Tarefa 57.2: o REST

`GET /blueprint` ganha `before`, **opcional**, em ISO 8601 (o `startedAt` que a própria API devolve). Sem `before`, o
comportamento de hoje (a mais recente). `before` que não é data válida dá `400` (`ProblemDetails`). A `contratos.md` é
atualizada pela Planejadora quando a 57.0 fecha.

## Resultado da 57.1 e da 57.2 (#412, 21/11/2026)

Entregues em `develop` (`67029a5`). A Planejadora reproduziu os vermelhos num worktree limpo, nos dois TFMs e com PostgreSQL 16 e SQL Server 2022 reais: a implementação padrão falha 3 de 6, o REST 7 dos 41 testes, e cada store 2 testes.

- **`DateTime2` explícito no SQL Server é necessário.** Com `AddWithValue`, ao menos um teste falhou em 4 de 4 rodadas. A coluna `started_at` é `datetimeoffset`.
- **O truncamento da âncora a microssegundo no PostgreSQL saiu.** Sem ele o teste dos ticks extras passou 8 de 8, e uma sonda com 1, 3, 5, 7 e 9 ticks extras passou 5 de 5: o Npgsql converte o parâmetro como converte o valor gravado.
- **O `before` é UTC.** O store do PostgreSQL lança `ArgumentException` para `Kind` `Local` ou `Unspecified`, e os outros dois aceitam. O XML doc diz isso, e o endpoint sempre manda `UtcDateTime`.

## Tarefa 57.3: o frontend

- `getBlueprint(method, route, before)`, e o painel manda o `startedAt` da trace que está vendo.
- **Trace com a rota `(unmatched)` não pede blueprint.** A constante `UNMATCHED_ROUTE_TEMPLATE` já existe em `core/models.ts`, desde a Tarefa 51.8 (#411). A 57.3 a reaproveita e não cria outra.
- A chave do efeito continua sendo o id da trace.
- **A demo estática passa pela mudança (D-251).** O manifest da demo é indexado pela URL com a query, então o `before` novo muda a chave e dá 404. O `build:demo` tem de responder o blueprint respeitando a âncora (estritamente antes), o `subscribe` trata o erro, e a prova são as 26 traces da demo com 0 erros no console, vista falhando antes. A Planejadora não tinha escrito isso, e a auditoria devolveu a #416 por isso.

## Resultado da 57.3 (#416, 21/11/2026)

Entregue em `develop` (`6f88e2a`). A primeira versão foi **devolvida pela auditoria** por causa da demo estática: `GET api/blueprint` dava 404 e um erro no console em 26 de 26 traces da demo, contra 0 de 26 no `develop`. O plano não falava da demo (D-251).

- **O painel.** `getBlueprint` manda o `before`, a página manda o `startedAt` da trace que mostra, uma trace com rota `(unmatched)` não pede blueprint, e a resposta de outra trace que chega atrasada é descartada. O `subscribe` trata o erro.
- **A demo.** O manifest passou a gravar o blueprint de cada trace com a âncora (`api/blueprint?before=...`), apontando para a trace anterior, e a lista `noContent` responde 204 para a primeira trace de cada rota. O `scripts/generate-demo-fixtures.mjs` foi atualizado: a regeneração chama `anchorBlueprints`, com a mesma regra da API (só `Success`, mesmo método, mesma rota normalizada, início estritamente anterior). A regeneração do release (62.4) mantém o formato novo.
- **Provas.** Navegador de verdade, host real do exemplo da auditoria: A dá 204, B devolve A, C devolve B, e C mostra `OrderRepository.Find` como "Não executado", nos temas claro e escuro e em 390x844. A demo construída dá 0 erros em 26 de 26 traces, e a auditoria conferiu na rede que o blueprint vem da execução anterior.
- **Lacuna que fica.** A demo gravada não tem nenhuma execução que pule um passo, então o "Não executado" nunca aparece na demo pública. Roadmap, item 49.

## Tarefa 57.4: "Aguardando" ao vivo (depende da 53.2)

Com a rota conhecida na abertura, o blueprint chega com a trace ainda `Running`. Os passos que ele prevê e que ainda
não aconteceram aparecem como "Aguardando", e viram "Não executado" quando a trace termina sem passar por eles. O
`graph-builder` já tem as duas regras (`inProgress`). A tarefa é provar no navegador que o fluxo todo funciona, e
corrigir o que a 57.0 mostrar que ainda falta.

## Tarefa 57.5: testes, README e CHANGELOG

**Teste que falha antes**, nos três storages onde couber:

- Store: a última execução com sucesso **estritamente anterior** a `before`. A própria trace nunca é o blueprint dela.
  Trace com erro não entra. Rota normalizada. Nenhuma anterior dá `null`. Implementação padrão: nunca devolve uma
  execução posterior, e devolve `null` quando a última é posterior. Memória, PostgreSQL e SQL Server (Testcontainers).
- REST: `before` válido, ausente (comportamento antigo) e inválido (`400`).
- Host real: o roteiro de F1b. Para A (`cached=true`), `GET /blueprint?...&before=<início de A>` dá `204`. Para B, devolve
  A. Para C (`cached=true`, depois de B), devolve B.
- Componente do painel: uma trace concluída pede o blueprint com `before` igual ao `startedAt` dela. Resposta `null` não
  inventa nó "Não executado". Trace `(unmatched)` não faz requisição. Trace `Running` com blueprint mostra
  "Aguardando", e a mesma trace concluída mostra "Não executado".
- **Navegador de verdade** (Playwright), no exemplo da 57.0: A (`cached=true`) aberta mostra só `OrderCache`. B mostra
  as duas etapas executadas. C (`cached=true` depois de B) mostra `OrderRepository.Find` como "Não executado". A
  variante lenta, aberta ao vivo, mostra "Aguardando" e depois "Não executado". Os dois temas e `390x844`.
- O detector (`design-quality.md`) nas telas afetadas, com a contagem de avisos antes e depois.

**Documentação:** a frase do README sobre "Não executado", conferida (a Fase 59 faz a varredura geral, e esta frase só
pode ficar verdadeira com o código desta fase). `CHANGELOG.md`, em inglês, `### Fixed`: o estado "Aguardando" que nunca
aparecia, e o desenho de uma trace antiga que mudava com a chegada de uma execução nova. `### Added`: o parâmetro
`before` de `GET /blueprint` e o membro novo do `ITraceStore`.

## Fora de escopo

- Escolher como blueprint a execução "mais completa" em vez da última com sucesso.
- Blueprint para trace importada de arquivo.
- Mudar a retenção. Se a execução anterior foi removida pela retenção, o blueprint dessa trace muda ou some, e isso é
  dito num parágrafo do README.

## Critério que vale para a fase inteira

- Build, testes, lint e formato limpos nos dois TFMs e no frontend. `AotCoreVerification` com zero aviso.
- Cada defeito com código tem teste **visto falhando antes**, e a captura de tela do navegador antes e depois.
- A auditoria reconfere a frase do README e refaz F1a e F1b no pacote real.

## Versionamento e fluxo

- `0.6.0`. Branch `bugfix/57-visao-ao-vivo` a partir de `develop` atualizado. A 57.4 espera a 53.2 em `develop`, e as
  tarefas 57.0 a 57.3 não esperam nada.
- Commits pequenos, em inglês, uma linha, sem linha de coautor. Abrir a PR e parar.

## Relatório final

Para cada defeito: o teste, a saída dele falhando antes e passando depois, e as capturas de tela. O número da
consulta com âncora nos dois bancos. O que mudou de comportamento.
