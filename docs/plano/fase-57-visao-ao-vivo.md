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

## Tarefa 57.3: o frontend

- `getBlueprint(method, route, before)`, e o painel manda o `startedAt` da trace que está vendo.
- **Trace com a rota `(unmatched)` não pede blueprint.** O literal vira constante com nome.
- A chave do efeito continua sendo o id da trace.

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
