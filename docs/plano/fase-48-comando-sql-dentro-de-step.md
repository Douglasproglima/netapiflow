# Fase 48: comando SQL dentro de um step some do trace (D-179, ADR-0024)

**Esta fase bloqueia o `0.5.0`.** O release (`release/0.5.0`) só abre com ela mergeada. Decisão do
dono do projeto (D-179). Branch `bugfix/48-comando-sql-dentro-de-step` a partir de `develop`
atualizado (que já tem a Fase 47, PR #309), PR contra `develop`.

O caso do Npgsql é bug publicado desde a `0.3.0`. O do SqlClient nasce no `0.5.0`. Como nenhum dos
dois pode ficar de fora do `0.5.0`, a correção entra junto com ele e não vira hotfix (D-177: não
existe `0.5.1`).

## Ponto de parada: regra de recuo

A correção é pequena: trocar um critério de descarte por outro, num lugar só. O que pode dar errado é
a regra nova deixar passar um duplicado. Se a Executora vir qualquer um destes sinais, **ela para e
relata, sem inventar outra regra por conta própria**:

- EF Core com o interceptor registrado, dentro de um controller, gravando dois spans de banco depois
  da mudança, em qualquer dos dois drivers (cenário R5 abaixo). Quer dizer que o step `Database` do
  interceptor nem sempre é o pai imediato do comando.
- Npgsql com um segundo `ActivityListener` no processo gravando a mesma fonte, e o cenário R7
  duplicando.
- Um teste que já existia nos projetos de interop (Fases 19 e 37) passando a falhar.
- Um teste de `NetApiFlow.SqlClient.IntegrationTests` falhando na primeira rodada, antes de qualquer
  mudança. Ver Tarefa 48.4: esse projeto nunca rodou no CI, então pode haver falha antiga escondida.

## O achado

O descarte de comando SQL do driver acontece quando existe **qualquer** step do NetApiFlow em volta.
Está escrito assim, nos dois lugares:

```csharp
// FlowActivityListener.cs
private static bool IsCommandAlreadyCoveredByAStep(string sourceName, Activity? step) =>
    sourceName == NpgsqlActivityTagNames.SourceName &&
    step?.Source.Name == FlowActivitySource.Name;

// SqlClientCommandListener.cs
private static bool IsAlreadyCoveredByAStep() =>
    Activity.Current?.Source.Name == FlowActivitySource.Name;
```

Nenhum dos dois olha o tipo do step. Numa action MVC o `FlowControllerActionFilter` abre um step
`Controller` em toda requisição, então o SQL direto feito dentro de um controller é sempre descartado.
Idem dentro de um serviço ou repositório rastreado, de um `NetApiFlow.Step(...)` manual e de um
endpoint Minimal API com `WithNetApiFlowTracking()`.

Reproduzido pela Executora com pacote real, sem EF Core, SQL direto:

| Driver | Ambiente | Resultado |
|---|---|---|
| Npgsql 8.0.5 | Postgres 16, `net8.0` | Só vira span fora de step e fora de controller. Dentro de controller, dentro de `NetApiFlow.Step` ou nos dois, só aparecem os steps. |
| `Microsoft.Data.SqlClient` 7.1.2 | SQL Server 2022, `net8.0` e `net11.0` | O mesmo. |

É o item 8 do roadmap ("dedup checando a coisa errada"), levantado na revisão da Fase 37 por leitura
de código e agora confirmado.

**Por que os testes não pegaram.** Os testes de interop dos dois drivers montam o trace com uma
`Activity` raiz solta, que não é do NetApiFlow, e rodam o comando embaixo dela. Nenhum step fica
aberto em volta do comando, então o descarte nunca disparava. É a mesma lacuna do D-176: o teste não
tinha a forma do uso real.

## A regra (ADR-0024)

O descarte existe para um caso só: o `NetApiFlow.EntityFrameworkCore` já abriu um step `Database` em
volta do comando, então o span do driver seria o mesmo comando duas vezes. A pergunta certa é "já
existe um step que representa este comando?", não "existe algum step aberto?".

| Step imediatamente em volta do comando | O que acontece com o comando do driver |
|---|---|
| Nenhum, ou uma `Activity` que não é do NetApiFlow | Gravado |
| Do NetApiFlow e do tipo `Database` (o interceptor do EF Core, ou um step manual `Database`) | Descartado |
| Do NetApiFlow e de qualquer outro tipo (`Endpoint`, `Controller`, `UseCase`, `Service`, `Repository`, `Http`, `Custom`) | Gravado, como filho desse step |

"Imediatamente em volta" continua como no ADR-0018: `activity.Parent` na checagem de
`OnActivityStarted` do Npgsql, `Activity.Current` no `Sample` do Npgsql e no evento `CommandBefore` do
SqlClient. Só o step imediato conta. Nada nos ancestrais.

O tipo do step vem da tag reservada `netapiflow.type` (`FlowTagNames.ComponentType`), que
`NetApiFlow.Step` já grava na criação da `Activity`. Não existe tag nova, opção nova nem mudança no
`FlowDbCommandInterceptor`.

## Tarefa 48.0: reproduzir de novo nesta branch, antes de corrigir

Os testes abaixo entram primeiro, e a Executora cola no resumo final a saída dos que falham contra o
código atual. Cada cenário roda nos dois drivers, cada um no seu projeto
(`NetApiFlow.Npgsql.IntegrationTests` com Postgres real, `NetApiFlow.SqlClient.IntegrationTests` com
SQL Server real), com host real, requisição real, driver real e banco real (D-176,
`.claude/rules/testing.md`). O trace é lido do `FlowRecorder` do próprio host.

| # | Cenário (forma real de uso) | Esperado | Hoje |
|---|---|---|---|
| R1 | SQL direto dentro de uma action de um controller MVC de verdade | Um span `Database` do driver, com `ParentSpanId` igual ao span do controller | Falha |
| R2 | SQL direto dentro de um `NetApiFlow.Step(...)` manual, num endpoint Minimal API | Um span do driver, filho do step | Falha |
| R3 | SQL direto dentro de um repositório registrado com `AddFlowTracked` como `Repository`, chamado de um controller | Um span do driver, filho do span do repositório | Falha |
| R4 | SQL direto dentro de um step manual do tipo `Database` | Um span `Database` só, o do próprio step, nenhum do driver. É o limite documentado da regra | Passa |
| R5 | EF Core com o interceptor, dentro de um controller | Exatamente um span `Database`, com `Component` `EntityFrameworkCore`, nenhum do driver | Passa |
| R6 | EF Core **sem** o interceptor registrado, fonte ligada, dentro de um controller | Um span `Database` do driver | Falha |
| R7 | Só Npgsql: um segundo `ActivityListener` no processo amostrando a fonte `"Npgsql"` como gravada, e os cenários R1 e R5 de novo | R1 com um span do driver, R5 com um span do interceptor, nenhum duplicado | R1 falha, R5 passa |
| R8 | 16 requisições concorrentes, cada uma com o cenário R1 | Cada trace com um span do driver, nenhum cruzado entre traces | Falha |
| R9 | Fonte desligada de forma explícita (`ExternalSources` em `None`), SQL direto dentro de um controller | Nenhum span do driver | Passa |

**Pronto quando (parte 1):** os cenários que falham hoje (R1, R2, R3, R6, R7 no R1, R8) commitados e
vistos falhando contra o código atual, e os que passam hoje (R4, R5, R7 no R5, R9) vistos passando.
Os testes que já existiam nos dois projetos continuam como estão.

Reaproveitar `SqlClientHostRegistrationTests` como molde de host. O controller, o endpoint e o
repositório de teste ficam dentro do próprio projeto de teste. Se subir um host com MVC exigir
`Microsoft.AspNetCore.TestHost` nos dois projetos, usar o mesmo pacote que
`NetApiFlow.IntegrationTests` já usa, versionado em `Directory.Packages.props`, e registrar a adição
no resumo. Subir o host em Kestrel numa porta livre, sem pacote novo, também serve: a Executora decide.

## Tarefa 48.1: um critério só, num lugar só

Um tipo `internal static` novo em `src/NetApiFlow/Instrumentation/`, com um nome que diga a intenção
(algo como "um step de banco já cobre este comando"), recebendo a `Activity?` que está em volta e
devolvendo se ela é do NetApiFlow **e** do tipo `Database`. Os dois predicados atuais
(`IsCommandAlreadyCoveredByAStep` e `IsAlreadyCoveredByAStep`) saem. Duas cópias da regra, uma por
driver, é como o erro nasceu nos dois.

- A leitura da tag acontece só quando a fonte é a do driver, como hoje: o curto-circuito por nome de
  fonte continua na frente. `HttpClient` e os steps do próprio NetApiFlow não pagam nada novo no
  `Sample`.
- Sem alocação, sem reflexão. `AotCoreVerification` continua com zero aviso (critério da fase, abaixo).
- Os `<summary>` dos dois predicados antigos carregam a regra velha e saem junto com eles. Regra de
  ouro do `CLAUDE.md`: o nome do tipo novo diz o que ele decide. O único comentário que vale ficar é o
  porquê de a checagem rodar duas vezes no Npgsql (`Sample` e `OnActivityStarted`), que já existe e é
  uma restrição externa real. Reescrever sem citar fase nem ADR.

**Pronto quando:** teste unitário em `NetApiFlow.Tests`, um `[Theory]` que passa todos os valores de
`ComponentType` e prova que só `Database` devolve verdadeiro. Mais três casos: `null`, uma `Activity`
de outra fonte com `netapiflow.type` igual a `Database` (falso), e uma `Activity` do NetApiFlow sem a
tag (falso).

## Tarefa 48.2: os dois pontos de uso

`FlowActivityListener` usa o critério em `Sample` (com `Activity.Current`) e em `OnActivityStarted`
(com `activity.Parent`). `SqlClientCommandListener.OnCommandBefore` usa com `Activity.Current`. Nada
mais muda: `FlowDbCommandInterceptor`, `FlowStep`, as opções e a superfície pública ficam como estão.

**Pronto quando:** os cenários R1 a R9 passam, nos dois drivers, e a Executora confirma, revertendo o
critério para o antigo, que R1, R2, R3, R6 e R8 voltam a falhar (a mesma prova de que o teste testa a
regra, não o acaso, que a Fase 29 já exigiu).

## Tarefa 48.3: `ParentSpanId` e o resto do trace

Conferir, no trace gravado, que o span novo do driver entra na árvore do jeito certo: filho do step
que o chamou, com `Sequence` coerente e duração dentro da do pai. O Npgsql resolve o pai por
`ResolveParentSpanId`, e o SqlClient abre um `NetApiFlow.Step`, então os dois já herdam isso. A tarefa
existe para que o teste de R1 e R3 afirme a relação de pai, não só a contagem de spans.

## Tarefa 48.4: o `NetApiFlow.SqlClient.IntegrationTests` nunca rodou no CI

Achado desta rodada, lendo `.github/workflows/ci.yml`: o passo `Test` roda `NetApiFlow.Tests`,
`NetApiFlow.IntegrationTests` e, fora do Windows, `NetApiFlow.Storage.Tests` e
`NetApiFlow.Npgsql.IntegrationTests`. O projeto `NetApiFlow.SqlClient.IntegrationTests` está no
`NetApiFlow.slnx` e compila no `Build`, mas **nenhum workflow o executa**. As PRs #266, #268 e #305
passaram com ele fora do CI.

Isso é a mesma classe do D-176, num lugar novo: o teste existe, o CI fica verde, e nada prova que ele
passa.

1. Antes de qualquer mudança, rodar o projeto localmente e colar o resultado no resumo. Se algum teste
   falhar sem relação com esta fase, parar e relatar (ponto de parada).
2. Acrescentar `dotnet test tests/NetApiFlow.SqlClient.IntegrationTests -c Release --no-build
   --collect:"XPlat Code Coverage"` ao bloco do `ci.yml` que já roda só fora do Windows, ao lado do
   Npgsql. Mesma razão: Testcontainers precisa de Docker. A imagem
   (`mcr.microsoft.com/mssql/server:2022-latest`) é a que `NetApiFlow.Storage.Tests` já usa no CI.
3. `release.yml` roda só `NetApiFlow.Tests`, `NetApiFlow.IntegrationTests` e
   `NetApiFlow.Storage.Tests`, sem nenhum dos dois projetos de interop. **Não mexer nele nesta fase.**
   A Executora só registra no resumo, e a Planejadora decide depois.

**Pronto quando:** o `ci.yml` roda o projeto e o job `build-test (ubuntu-latest)` mostra os testes dele
executando (conferir no log real da PR, não só o selo verde).

## Tarefa 48.5: fumaça com pacote real

Pacote empacotado de verdade, `-p:Version=0.5.0-verify.<n>` (nunca o número real, nunca
`-p:VersionSuffix`, `<n>` novo a cada rodada; ver `.claude/rules/packaging.md` e D-174). Pasta padrão
`tests/manual-verification/`, com o `nuget.config` próprio que já isola o cache.

### a) A receita do SqlClient, que já existe

`tests/manual-verification/OptionsFromConfigVerification` (Fase 47). Hoje o README dela registra o caso
do controller como falha conhecida. Passa a ser resultado esperado:

- o span `SqlClient` do `/api/smoke` aparece, **filho do step** `SmokeController/Get`, nos modos `on` e
  `control` (nenhum dos dois liga a fonte pelo modo: `Development` já a liga por padrão);
- o parágrafo "Known failure, not a regression" sai do README da receita;
- o `verify.sh` passa a imprimir o `parentSpanId` do span do SqlClient e o `spanId` do step, para a
  relação de pai ficar à vista.

### b) A receita do Npgsql, nova

`tests/manual-verification/NpgsqlCommandInsideStepVerification`, no mesmo molde da anterior
(`verify.sh`, `global.json`, seção própria no `README.md` da pasta com os comandos exatos), contra
Postgres 16 em container, `Npgsql` 8.0.x, `net8.0` e `net11.0` (que resolve o asset `lib/net10.0`).
`AddNetApiFlow()` sem lambda nenhuma, em `Development`, com storage real. Endpoints:

| Rota | Forma | Esperado no trace |
|---|---|---|
| `/api/raw` | Controller, `NpgsqlCommand` direto | Um span `Npgsql`, filho do step do controller |
| `/api/step` | Controller, `NpgsqlCommand` dentro de um `NetApiFlow.Step(...)` | Um span `Npgsql`, filho do step |
| `/mini/step` | Minimal API, `NpgsqlCommand` dentro de um `NetApiFlow.Step(...)` | Um span `Npgsql`, filho do step |
| `/api/ef` | Controller, `DbContext` do EF Core com o interceptor do pacote `NetApiFlow.EntityFrameworkCore` | Exatamente um span `Database`, `EntityFrameworkCore`, e nenhum `Npgsql` |

O `/api/ef` é o que importa: a regra nova afrouxa o descarte, e a propriedade em risco é o EF Core
gravar uma vez só. Provar com o pacote de verdade, não só com `ProjectReference`.

**Controle desligado.** O mesmo app com `ExternalSources` em `None` dito na configuração: nenhum span
`Npgsql` em nenhuma das três primeiras rotas, e `/api/ef` ainda com o span do interceptor.

**Matriz.** `net8.0` e `net11.0`, nos dois modos.

### c) O `--vulnerable`

O resumo da rodada anterior dizia "`--vulnerable` sem resultado", o que não distingue "nenhum pacote
vulnerável" de "o comando não avaliou nada". Rodar
`dotnet list package --vulnerable --include-transitive` **sem filtro de `grep`** em cada app da fumaça
e colar a saída literal no resumo. Se vier vazia, dizer isso e verificar por quê (feed inalcançável
costuma aparecer como `NU1900`).

**Pronto quando:** as duas receitas estão documentadas em `tests/manual-verification/` e foram rodadas
nos dois alvos, ligadas e desligadas, com o resultado no resumo final. O release repete as duas com a
versão final.

## Tarefa 48.6: documentação pública

A Executora troca toda frase que declara a regra velha. Achar com
`git grep -n -i "already wraps\|step already\|deduplicat"` em `README.md`, `README.pt-BR.md`,
`docs/` (fora de `docs/plano/` e `docs/adr/`) e os `<summary>` de `src/`. O que dizem hoje, em
resumo, é que o driver é descartado "sempre que um step do NetApiFlow já cobre a chamada".

- `README.md` e `README.pt-BR.md`, seção das fontes externas: o comando do driver é descartado só
  quando um step `Database` já o cobre (o que o interceptor do EF Core abre). Dentro de um controller,
  um serviço, um repositório ou qualquer outro step, o comando é gravado como filho dele. Uma frase
  diz que um step manual `Database` em volta de SQL direto engole os comandos do driver que ficam
  dentro dele. Conteúdo equivalente nos dois, sem travessão.
- `<summary>` de `ExternalActivitySource.Npgsql` e `ExternalActivitySource.SqlClient` em `src/`: o
  texto novo está em `docs/plano/contratos.md`. Portar sem citar fase nem ADR.
- `CHANGELOG.md`, `[Unreleased]`:
  - **`### Fixed`**, uma entrada para o Npgsql, em termos de usuário. Algo como: com
    `ExternalActivitySource.Npgsql` ligada, um comando SQL feito dentro de uma action, de um serviço,
    de um repositório ou de qualquer outro step do NetApiFlow (Dapper, um `NpgsqlCommand` direto) não
    gerava span. Só os feitos fora de qualquer step eram gravados, o que numa action MVC quer dizer
    nenhum. Agora são gravados como filhos do step que os emitiu, e os comandos do EF Core continuam
    gravados uma vez só.
  - **O SqlClient não ganha `Fixed`.** Ele nasce no `0.5.0`, então a entrada que já existe em
    `### Added` ("SQL Server commands issued outside EF Core ... become database spans") só passa a ser
    verdadeira. Não mexer nela.
  - Sem citar número de tarefa nem de fase.

## Critério que vale para a fase inteira

- `tests/manual-verification/AotCoreVerification` continua com zero aviso (`dotnet publish -c Release`
  na pasta, ver o `README.md` de `tests/manual-verification/`). A fase mexe no `FlowActivityListener`,
  que está no caminho do `AddNetApiFlow()`.
- Build, testes, formato e lint limpos nos dois TFMs (`net8.0` e `net10.0`), incluindo os dois
  projetos de interop rodados de verdade, com Docker.
- `git grep -n -i "IsCommandAlreadyCoveredByAStep\|IsAlreadyCoveredByAStep"` não acha mais nada.

## Fora de escopo

- Procurar o step `Database` nos ancestrais, e não só no pai imediato. O ADR-0018 provou o pai
  imediato, e não há caso real que peça mais.
- Opção para escolher a regra de descarte. Seria a quarta alternativa do ADR-0024, e reabre o R-06.
- Qualquer mudança no `FlowDbCommandInterceptor`, no `HttpClient` ou em MassTransit.
- Rodar os projetos de interop no `release.yml` (Tarefa 48.4, item 3): a Executora só registra.
- O terceiro TFM (`net11.0` de verdade em `Directory.Build.props`): item do `0.6.0` (D-173).

## Versionamento e fluxo

- Branch `bugfix/48-comando-sql-dentro-de-step` a partir de `develop` atualizado. O `pr-lint` só aceita
  minúscula, dígito, ponto e hífen no nome.
- `Directory.Build.props` não muda. O número `0.5.0` é posto pelo release (passo 7 da skill
  `/release`), não por esta fase.
- Commits pequenos, um por tarefa quando fizer sentido, em inglês, uma linha, sem linha de coautor.
- Ao terminar: abrir a PR contra `develop` e parar. Tem código e mexe em workflow, então o merge exige
  confirmação do dono.
- Depois do merge, o release `0.5.0` segue a skill `/release`, e o passo 4 repete as duas receitas da
  Tarefa 48.5 com a versão final.

## Relatório final

Os cenários R1 a R9 antes (os que falham) e depois (todos passando), nos dois drivers. A prova de que
reverter o critério volta a quebrar R1, R2, R3, R6 e R8. O resultado do `NetApiFlow.SqlClient.IntegrationTests`
rodado antes de qualquer mudança. O log real do CI mostrando esse projeto executando. O resultado das
duas receitas nos dois alvos, ligadas e desligadas, com o span do `/api/ef` e a relação de pai de cada
rota. A saída literal do `--vulnerable`. O resultado do `AotCoreVerification`. A confirmação de que
os dois predicados antigos não existem mais. Se algum pacote de teste foi adicionado, qual e por quê.
