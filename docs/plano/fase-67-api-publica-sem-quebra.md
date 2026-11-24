# Fase 67: a API pública com os nomes certos, sem quebrar ninguém (D-236, B8, B9)

A API pública do NetApiFlow ainda tem nomes que um revisor de .NET reprova no primeiro dia: uma classe com o mesmo nome do
namespace, `Use*` onde a convenção do ASP.NET Core manda `Map*`, uma extensão que só cobre dois tipos de builder, e um `Tag`
que só aceita `string`. Antes da `1.0` é a hora, e dá para fazer **sem quebrar** ninguém: o nome novo entra, o antigo fica com
`[Obsolete]` e encaminha.

Vai no `0.6.0` (D-236). Vem **depois da Fase 66 e antes da 58 e da 59**: o README e os samples passam a usar os nomes novos
uma vez só.

## Os achados e o que a Planejadora conferiu

| # | Achado | Conferi |
|---|---|---|
| B8a | A classe `NetApiFlow.NetApiFlow` tem o nome do namespace (CA1724), e quem chama escreve `NetApiFlow.NetApiFlow.Step(...)` | Não verifiquei |
| B8b | `UseNetApiFlowDashboard` e `UseNetApiFlowMcp` deveriam ser `Map*` pela convenção do ASP.NET Core | Não verifiquei |
| B8c | `WithNetApiFlowTracking` só existe para `RouteHandlerBuilder` e `RouteGroupBuilder` | **Código:** `NetApiFlowEndpointFilterExtensions.cs`, duas sobrecargas (`contratos.md`) |
| B8d | `Tag` só aceita `string` (um `enum` não compila) | Não verifiquei |
| B9 | Pontos de entrada sem validação, `CompositeEndpointConventionBuilder.Finally` lançando `NotImplementedException`, tools do MCP públicas com `= null!`, `serverInfo.name` do MCP, sem `EnablePackageValidation` nem `IsAotCompatible` | Não verifiquei |

## A decisão do nome (a Planejadora, D-237)

A classe passa a se chamar **`Flow`**. É curta, vive no namespace `NetApiFlow`, e se lê `Flow.Step("OrderRepository", "Find")`,
como `Log.Information(...)` do Serilog. **Antes de fixar o nome**, a Tarefa 67.0 mede se `Flow` colide com algo que um app
comum importa. Se colidir, a Executora para e a Planejadora escolhe outro. **A Planejadora avisa a auditoria do nome final antes da Tarefa 67.1, junto com o resultado da 67.0, e a auditoria confere as três coisas do item 1 da 67.0.**

## Ponto de parada

- Se `Flow` colidir (CS0104, ambíguo) com um tipo dos `using` padrão de um `dotnet new webapi` ou do EF Core, MediatR,
  Serilog ou OpenTelemetry: **para**. O nome é da Planejadora.
- Se `[Obsolete]` com encaminhamento **quebrar** qualquer usuário do nome antigo (o código antigo precisa compilar e rodar
  igual, só com aviso), para.
- **As tools do MCP só viram `internal` se o SDK do MCP aceitar** (D-238). Ele pode exigir tipo público para descobrir as
  tools pelo atributo. A Executora **mede antes de mudar a visibilidade** (Tarefa 67.0, item 4). **Qualquer outra quebra**
  acusada pela validação de pacote: para.

## Tarefa 67.0: medir, antes de mexer

1. **Colisão do nome `Flow`.** Um app novo (`dotnet new webapi`) com `using NetApiFlow;` e os `using` de EF Core, MediatR,
   Serilog e OpenTelemetry: `Flow.Step(...)` compila? Dizer o que mediu. **A auditoria acrescentou três conferências (24/11/2026), porque um nome público é difícil de trocar depois do release:** (a) o nome colide com algum tipo do BCL ou do ASP.NET Core nos `using` implícitos do `webapi` (`System`, `System.IO`, `System.Linq`, `System.Net.Http`, `System.Threading`, `System.Threading.Tasks`, `Microsoft.AspNetCore.*` e `Microsoft.Extensions.*`)? E o analisador CA1724, que acusa um tipo com o nome de um namespace, dispara? (b) colide com o namespace `NetApiFlow`, ou com algum namespace que o app importa? (c) o trecho do README que usa o nome, colado na ordem do leitor num app novo, compila com 0 avisos? O resultado vai à Planejadora **antes da 67.1**.
2. **O que a validação de pacote acusaria** contra a `0.5.2` com os nomes novos e os antigos `[Obsolete]`: nada, fora o que o
   item 4 decidir.
3. **Os pontos de entrada públicos** sem validação de argumento: a lista, por pacote, incluindo as `connection string` dos
   storages.
4. **O SDK do MCP aceita tool `internal`?** Com o `ModelContextProtocol.AspNetCore` que o pacote usa, o `tools/list` e o
   `tools/call` funcionam com as três tools como `internal`, descobertas pelo atributo? A saída real, nos dois TFMs.

## Resultado da 67.0 e as decisões (D-277, 24/11/2026)

A Executora mediu, sem código nem commit, e a Planejadora leu o relatório (não repetiu a sonda). A auditoria conferiu as três coisas do item 1.

- **O nome é `Flow`, sem colisão.** Um `webapi` novo, em `net8.0` e `net10.0`, com os `using` implícitos e os de EF Core, MediatR, Serilog e OpenTelemetry, compila `Flow.Step(...)` sem CS0104. Uma varredura por reflexão em todos os tipos públicos do app e dos frameworks `Microsoft.NETCore.App` e `Microsoft.AspNetCore.App` inteiros só achou o `Flow` da própria sonda, e nenhum namespace terminado em `Flow`. O CA1724 dispara para a classe `NetApiFlow` de hoje (controle positivo) e não para `Flow`. O Início rápido colado compila com 0 avisos em `net10.0`. **Não medido:** bibliotecas de terceiros que definam um tipo `Flow`. O risco que fica, e que a auditoria aceita, é um tipo do próprio app chamado `Flow` num namespace que o arquivo também importa (CS0104), que se resolve com um alias e acontece com qualquer nome curto.
- **A validação de pacote** passa nos 9 pacotes contra a `0.5.2` e contra a `0.5.3`, com controle positivo (tornar `AddNetApiFlowMediatR` `internal` dá CP0002). A baseline é a **última publicada**, lida de `tests/real-package/previous-published-version.txt` pelo MSBuild: uma fonte só, que o passo 13 do `/release` já atualiza. Se ler o arquivo no MSBuild não funcionar, uma propriedade fixa em `0.5.3` e uma linha no passo 13.
- **As tools do MCP viram `internal`.** O SDK (`ModelContextProtocol.AspNetCore` 2.3.1, `WithToolsFromAssembly`) aceita: o `tools/list` e o `tools/call` funcionam em `net8.0` e `net10.0`, e a suíte do MCP passa, 86 de 86. Com `CompatibilitySuppressions.xml`, as linhas `*REMOVED*` no `PublicAPI.Unshipped.txt` (`GetTraceTool`, `ListRecentTracesTool`, `GetComponentStatsTool` e `TraceLookupResult`) e `### Changed` no CHANGELOG.
- **Os pontos de entrada,** pela lista da Executora: `ThrowIfNull` e `ThrowIfNullOrEmpty` nos de configuração (`Add*`, `Map*`, `Use*`, `WithNetApiFlowTracking`, `DecorateFlowTracked`, `TrackFlowByConvention`, `GetRecentTrace`, o construtor do `FlowDbCommandInterceptor`, a `connection string` dos storages, e `IncludePaths` e `ExcludePaths` com array nulo), que lançam na partida com mensagem acionável. `FlowAssertionException(message)` fica sem validação. **`Flow.Step`, `FlowStep.Tag` e `FlowStep.MarkError` não lançam**, porque rodam no código do usuário em produção (a regra "nunca derrubar" do `tracing.md`): um argumento nulo vira uma etapa não rastreada, com o aviso de no máximo uma vez a cada 30 segundos que já existe, uma tag com chave ou valor nulo é ignorada, e `MarkError(null)` não faz nada.
- **`Tag` com enum.** O B8d diz que um enum não compila, e as quatro sobrecargas do plano (`int`, `long`, `double`, `bool`) não o resolvem, porque um enum não converte sozinho. Entra `Tag<TEnum>(string key, TEnum value) where TEnum : struct, Enum`, que grava o nome do membro. `Tag(string, object)` fica de fora, porque aceitaria um objeto inteiro e iria contra a regra de não capturar dado sensível. O `AotCoreVerification` fica com zero aviso: se `Enum.ToString` gerar aviso, usa-se `Enum.GetName<TEnum>`.

## Tarefa 67.1: os nomes novos, com o antigo encaminhando (B8)

- A classe `Flow`, com os mesmos membros. `NetApiFlow.NetApiFlow` fica `[Obsolete("Use NetApiFlow.Flow.")]` e **delega** a ela.
- `MapNetApiFlowDashboard` e `MapNetApiFlowMcp`, e os `Use*` ficam `[Obsolete]` e delegam.
- `WithNetApiFlowTracking<TBuilder>` genérica sobre `IEndpointConventionBuilder`, simétrica à `WithoutNetApiFlowTracking` da
  Fase 53. As duas sobrecargas de hoje ficam `[Obsolete]` e delegam, ou deixam de existir se a genérica as cobre sem quebra de
  resolução de sobrecarga (a Executora mede).
- Sobrecargas de `Tag` para `int`, `long`, `double` e `bool`, e a genérica `Tag<TEnum>(string key, TEnum value) where TEnum : struct, Enum`, que grava o nome do membro (D-277).
- **O README e os samples passam a usar os nomes novos.** O README diz uma frase sobre os antigos (`[Obsolete]`, e até quando), e outra dizendo que, se uma biblioteca importada também define `Flow`, o app usa `NetApiFlow.Flow` ou um alias.

## Tarefa 67.2: os pequenos (B9)

- `ArgumentNullException.ThrowIfNull` e `ArgumentException.ThrowIfNullOrEmpty` nos pontos de entrada públicos, inclusive a
  `connection string` dos storages, com mensagem acionável.
- `CompositeEndpointConventionBuilder`: implementar o `Finally`.
- **As tools do MCP, sem o `= null!`**, conforme o item 4 da 67.0:
  - **o SDK exige tipo público:** as tools **ficam públicas**, com `[EditorBrowsable(EditorBrowsableState.Never)]`. Sem quebra, e a
    validação de pacote não acusa;
  - **o SDK aceita `internal`:** viram `internal`, com `CompatibilitySuppressions.xml` explícito e `### Changed` no CHANGELOG.
- O `serverInfo.name` do MCP passa a ser `"NetApiFlow"`.
- `EnablePackageValidation` com a baseline lida de `tests/real-package/previous-published-version.txt`, a última versão publicada (D-277, no lugar da `0.5.2`), e `IsAotCompatible` no núcleo (liga com a Tarefa 50.12).

## Tarefa 67.3: contrato, testes e CHANGELOG

- `contratos.md` atualizado pela Planejadora com os nomes novos quando a 67.0 fecha.
- Testes: o nome antigo compila e roda igual, com o aviso; o novo faz o mesmo; a `Tag` com cada tipo; cada validação de
  argumento; o `Finally`; a validação de pacote passa só com a supressão das tools do MCP.
- `CHANGELOG.md`, em inglês: `### Added` (nomes novos, sobrecargas), `### Deprecated` (os antigos), `### Changed` (o `serverInfo.name`, e as tools
  do MCP internas, **só se** o item 4 da 67.0 permitir).

## Fora de escopo

- Remover qualquer nome antigo. Isso é depois da `1.0` ou numa minor decidida à parte.
- Os namespaces espalhados e os nomes inconsistentes (`IncludePatterns` e `IncludePaths`, `Retention*`, `DateTime` e
  `DateTimeOffset`): quebram API e ficam para a `0.7.0` (D-236).

## Critério que vale para a fase inteira

- Build, testes, lint e formato limpos nos dois TFMs. `AotCoreVerification` com zero aviso. A validação de pacote passa.
- O README e os samples **compilam e rodam** com os nomes novos, no app novo.

## Versionamento e fluxo

- `0.6.0`. Branch `feature/67-api-publica` a partir de `develop` atualizado, **depois** da Fase 66.
- Commits pequenos, em inglês, uma linha, sem linha de coautor. Abrir a PR e parar.

## Relatório final

A colisão medida do nome. O que a validação de pacote acusou. Os pontos de entrada validados. O diff dos samples.
