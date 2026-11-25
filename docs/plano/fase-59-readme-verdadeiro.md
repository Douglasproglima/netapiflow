# Fase 59: o README e os textos dizem só o que o produto faz (D-210, nível 2)

**Estado (25/11/2026):** **a 59A, o texto, está entregue em `develop` (#507, squash `827d67e`, D-299) e conferida pela auditoria com o pacote real.** Faltam a 59B (as citações) e a 59C (o desempenho), e a releitura da 59.5 na segunda passada.

A auditoria de base leu o README frase por frase contra um app novo e mediu cerca de vinte e seis afirmações como falsas ou
imprecisas. Ninguém quebra por causa de uma frase, mas é no README que o usuário decide se confia no produto, e uma
frase falsa vale mais que um bug que ele nunca vê. Esta fase deixa o texto verdadeiro.

Vai no `0.6.0` (D-210, D-232). Vem **perto do fim**, porque muitas frases dependem do código das fases anteriores
(53, 55, 56, 57 e 58) e o README não deve ser reescrito duas vezes.

## A regra da fase: o texto muda, o código não

**O padrão de cada item é corrigir o texto.** O código só muda se o texto não puder ficar verdadeiro sem isso, e então
a Executora **para e pergunta** à Planejadora, item por item. O escopo da `0.6.0` está fechado (D-232), e um item que
vira código novo é escopo novo. O que a Executora achar de código nesta fase vai à auditoria, e a auditoria decide se é
`0.7.0`.

## Ponto de parada

- Se a Executora não reproduzir uma frase como falsa, **não a muda**. Pede à auditoria o comando que ela mediu.
- Se a tabela de performance medida de novo piorar além do que o README promete, para e relata. A decisão é da
  Planejadora e da auditoria, e o número real vai para o README de qualquer forma.

## Os itens, e o que a Planejadora conferiu

Nada abaixo foi reproduzido pela Planejadora. Cada linha está de pé pela medição da auditoria, e a Tarefa 59.0
reproduz uma a uma antes de mexer no texto.

| # | Frase ou parte do produto | O que a auditoria mediu |
|---|---|---|
| 1 | `SuppressFlow`: "record correctly, but as a new root" | A etapa **some**, não vira nova raiz |
| 2 | OTLP: "samples independently ... can still be exported" | Com a configuração do README dá o contrário: `Enabled=false` e `ExcludePaths` exportam 0, e um sampler de 0% do host exporta os spans do NetApiFlow |
| 3 | O aviso do `IncludeHttpClientSource` | A query não saiu no `net8.0` nem no `net10.0`, o texto está pessimista |
| 4 | "Sensitive data" | O corpo volta pela mensagem de exceção (`int.Parse` de um campo do corpo). Dizer com clareza |
| 5 | `ExternalSources`, "default is None" | Em Development o padrão é HttpClient, Npgsql e SqlClient |
| 6 | "Run an endpoint", e `scheme://host[:port]/path` | A UI diz "Try an endpoint". O gravado sai sem o esquema |
| 7 | `CapturePrePipelineSteps` | O `[Authorize]` comum não gera etapa, e o output caching não aparece no acerto de cache |
| 8 | `IAsyncEnumerable` | Não "falha": grava cerca de 0,9 ms e `Success`, mesmo quando a enumeração lança |
| 9 | AOT: "warning at build time instead of a failure at startup", e "there is a check in the repository that proves it" | O binário sobe e dá 500 na primeira requisição. A segunda frase só fica verdadeira com a Tarefa 50.12 |
| 10 | "naming convention (e.g. `I*Repository`)" | O predicado recebe o tipo concreto |
| 11 | "only compiles from inside a namespace of your own" | Com o `using` no topo também dá CS0234 |
| 12 | Os blocos de "Asserting" | Não compilam sem `using Microsoft.AspNetCore.Mvc.Testing`, e no `net8.0` precisam de `public partial class Program` |
| 13 | "in-memory buffer kept while persistence catches up" | É uma fila fixa das últimas 500 traces |
| 14 | O cartão p95 com sparkline, "Authorize ... above the request form", "preview shows the exact header" | O cartão só aparece com 1600 px ou mais. A seção fica entre os parâmetros e os headers, e a prévia é mascarada |
| 15 | Import: "same ... metric cards and event feed" | Os cartões mostram a instância local, e o feed fica vazio |
| 16 | Rotas mortas | Listam `GET /openapi/{documentName}.json`, porque `/endpoints` não filtra o documento (item 26 do roadmap) |
| 17 | pt-BR | O gênero de "trace" alterna. Padronizar no masculino, como a UI |
| 18 | Um step manual que lança sem chamar `MarkError` | É gravado como `Success`. Dizer com clareza |
| 19 | O OTLP | Sai sem o span `SERVER` e com raiz órfã. Dizer como adicionar a instrumentação do ASP.NET Core para ter o trace completo |
| 20 | O filtro por tenant e o `IFlowTenantResolver` | Não são documentados (0 ocorrências) |
| 21 | `OTEL_SERVICE_NAME`, a retenção, o `schema.sql` | O `OTEL_SERVICE_NAME` não é citado. A retenção roda a cada 5 minutos, e a primeira 5 minutos depois de subir. O caminho do `schema.sql` falta nos READMEs dos dois storages |
| 22 | Os trechos de código do MCP, do storage e do OTLP | Faltam os `using` |
| 23 | Os limites fixos | Na `0.6.0` o README só **documenta os valores** (virar `LimitsOptions` é da `0.7.0`) |
| 24 | `DELETE` e `POST /deploys` | Usam a mesma política da leitura, sem papel só leitura. Dizer |
| 25 | `NetApiFlow.Storage.SqlServer` e o MediatR | O pacote traz o `Microsoft.Data.SqlClient` `5.2.2`, que não grava comandos. O README não fala da licença do MediatR 13 ou superior |
| 26 | "Toda rota do MCP responde 404" fora de Development | `GET` e `DELETE` em `/flow/mcp` respondem **405** em `Production`, o que revela que a rota existe. O README não pode dizer que toda rota responde 404 sem essa ressalva (D-240) |


Mais itens que não são frases, e que a mesma fase leva:

- **A tabela de performance** (59.4).
- **As citações internas e os comentários que a regra de ouro manda resolver** (59.2): as citações de `ADR-`, `D-` ou `risk R-` e os comentários `//`, mais os XML docs que só repetem o nome. A varredura de 20/11/2026 contou 39 e 64. A Planejadora contou de novo em 25/11/2026, no `develop` em `833a3d9`: 70 linhas com o padrão nos arquivos rastreados de `src/` (15 em `.cs`, 14 delas em `///`, e o resto em TypeScript, SCSS e JS) e 55 linhas `//` em `.cs`, 27 delas só em `DashboardSseEndpoint.cs`. A Executora conta de novo antes de começar.
- **O `CONTRIBUTING`, o `SECURITY.md` e o template de PR** (59.6).
- **O parágrafo do README do MCP** que só aponta para o painel (59.3).

## Tarefa 59.0: reproduzir cada frase, antes de mexer em qualquer texto

Um app novo (`dotnet new webapi`) com o pacote empacotado localmente do estado atual de `develop` (número
`-verify.<n>`, D-153 e D-174), seguindo o README como um usuário. Para cada linha da tabela: o comando, a saída real,
e a classificação:

- **Falsa ou imprecisa, corrige o texto** (o caso de quase todas).
- **Verdadeira hoje, e uma fase anterior a tornou falsa ou a corrigiu.** O texto fica como está ou muda com o código novo.
- **Não reproduzida.** Para a Tarefa e pede o comando à auditoria.

A tabela preenchida vai no relatório da 59.0. **Pronto quando:** as 26 linhas classificadas com saída real.

## Tarefa 59.1: o texto, nos dois idiomas

Para cada linha classificada como "corrige o texto": `README.md` e `README.pt-BR.md` dizem a mesma coisa, em frases
curtas e sem floreio (`.claude/rules/writing-style.md`). Cada correção cita o que foi medido, não o que se supõe. A
frase 9, "there is a check in the repository that proves it", pode entrar verdadeira: a 50.12 já está em `develop` (o `ci.yml` publica o núcleo em AOT e falha com aviso), e a Executora aponta o passo do workflow.

A linha 17 padroniza o gênero de "trace" no masculino, em todo o `README.pt-BR.md`, como a UI.

**O id da trace nos textos (achado da Executora na Fase 60, D-295).** O README do `SampleApi.Controllers` dizia que o `TraceId` do Aspire era o mesmo do painel do NetApiFlow, e desde a Fase 55 (ADR-0028, D-271) o id do Aspire é o campo "W3C trace" do detalhe, não o "Trace". A 60B corrigiu o README do sample. A Executora da 59 procura a mesma ideia (o id do Aspire, do OpenTelemetry ou do `Activity.Current` igual ao do painel) no `README.md`, no `README.pt-BR.md`, nos READMEs dos pacotes e nos dos samples, e corrige onde achar. A seção do README principal sobre o id da trace (a que diz que ele é aleatório e que o W3C trace id fica na tag `w3c.trace_id`) já está certa.

**A linha 2 depois da 60.9b.** A #504 faz `Enabled = false` desligar também quando outro `ActivityListener` escuta a fonte `NetApiFlow` (por exemplo o `AddNetApiFlowSource()` no OpenTelemetry do app): os passos deixam de chegar a ele. A 59.0 mede a linha 2 depois do merge da #504, e a frase "can still be exported" diz o que isso faz de verdade, com a saída real.

**Chamadas de registro de pacote sem o `using` (D-255, a mesma classe do item 12 e do D-247).** A varredura da Planejadora achou que o README mostra a chamada e não diz de qual namespace ela vem. Os blocos de PostgreSQL (`AddNetApiFlowPostgreSqlStorage`, `NetApiFlow.Storage.PostgreSql`, dois blocos) e de OTLP (`AddNetApiFlowOtlpExport`, `NetApiFlow.OpenTelemetry`, dois blocos) chamam métodos que o `using NetApiFlow;` do Quick start não cobre. O SQL Server aparece só numa frase (`AddNetApiFlowSqlServerStorage`, `NetApiFlow.Storage.SqlServer`) e o MediatR (`AddNetApiFlowMediatR`, `NetApiFlow.MediatR`) pode estar só em prosa ou em tabela: a Executora confere o que o README mostra de fato. Cada chamada de registro de pacote que o README mostrar ganha o `using` do namespace dela, num bloco marcado ou numa frase, nos dois idiomas, no molde da #414 e da seção do MCP da Fase 70. **Critério:** cada bloco, colado num `dotnet new webapi` novo, compila. A Fase 70 não os cobre (os pacotes de storage, OTLP e MediatR num app novo ficaram para a `0.7.0`), então a prova aqui é a colagem, e a Executora diz se a fez à mão ou com um script igual ao do job. **Atualização (D-276):** a #473 (Fase 66) já pôs o `using NetApiFlow.OpenTelemetry;` nos blocos do OTLP, nos dois idiomas, e a Executora colou o resultado em dois apps novos. A Fase 59 confere esse trecho e não precisa refazê-lo.

**O Início rápido no .NET 8 (auditoria, 24/11/2026, D-277).** O selo do README diz ".NET 8 | 10", e o texto manda manter o `AddOpenApi()` e o `MapOpenApi()` "que são do template". O `dotnet new webapi -f net8.0` gera `AddSwaggerGen`, `UseSwagger` e `UseSwaggerUI` (a auditoria conferiu com o template de verdade), então quem cola o bloco no `net8.0` ganha CS1061 nos dois. Isso é "README verdadeiro", dentro do escopo da `0.6.0`. **O que a 59 prova:** o app do template de `net8.0` com as linhas do NetApiFlow coladas na ordem do leitor compila com 0 avisos, e o Runner lista as rotas. Ninguém mediu se o Runner lê o documento do Swashbuckle (o `OpenApiDocumentPath` provavelmente aponta para `/swagger/v1/swagger.json`). Se não ler, o README diz com todas as letras que o Runner precisa do documento do `Microsoft.AspNetCore.OpenApi`, e o resto funciona. Nos dois idiomas. A Fase 70 só cobre o `net10.0`, então a prova aqui é a colagem, e a Executora diz se a fez à mão ou com um script.

## Tarefa 59.6: os arquivos de comunidade

- **`CONTRIBUTING`:** os pré-requisitos (a versão do Node da `.nvmrc` do `ClientApp`, em `src/NetApiFlow.Dashboard/ClientApp/.nvmrc`, e o Docker), o `npm run format:check`, e tirar o "Phase 4".
- **Template de PR:** com o `format:check`.
- **`SECURITY.md`:** o escopo inclui o MCP.
- **`.github/ISSUE_TEMPLATE/config.yml`** com `contact_links` para a política de segurança.

O canal privado de vulnerabilidade, o `secret scanning`, o `push protection` e as atualizações de segurança do Dependabot já foram ligados pela auditoria (20/11/2026). A Executora **confere** que o `SECURITY.md` descreve o canal que existe.

## Tarefa 59.2: zerar as citações no XML doc, e travar no CI

Hoje são 70 linhas com `ADR-`, `D-` ou `risk R-` em `src/` (a contagem da lista acima, e a Executora conta de novo antes de começar), mais 55 linhas `//` em `.cs` que a regra de ouro
manda resolver e os XML docs que só repetem o nome do membro. Cada comentário `//` se resolve com um nome melhor, um método
extraído ou uma constante, e **só então** é apagado (regra de ouro do `CLAUDE.md`). Exemplos: `TailSamplingOptions.cs:3`, `LogCorrelationOptions.cs:5`,
`FlowActivityListener.cs:12`, `FlowTraceDetail.cs:5`. Várias estão em membros públicos e aparecem no IntelliSense de
quem instala o pacote, contra a regra de ouro do `CLAUDE.md`.

- Zerar todas as que a contagem da Executora achar, em C#, TypeScript, SCSS e JS. Onde a citação carrega um porquê real, reescrever **o porquê** sem o número. Nome de teste que cita decisão ou fase é citação também: renomear pelo comportamento que o teste prova.
- Pôr no CI uma checagem que falhe com o padrão (por exemplo `git grep -nE` por
  `ADR-[0-9]|\bD-[0-9]{2,3}\b|risk R-[0-9]` em `src/`). Se um achado não for citação de verdade (um texto de fixture, por exemplo), a exceção fica numa lista, uma linha por arquivo, dentro do próprio script, com o motivo.
- **Rodada de prova vermelha:** mostrar a checagem falhando numa citação plantada e passando depois de removê-la.

Esta tarefa não mexe em lógica nem troca nome público: nenhum `PublicAPI.Shipped.txt` nem `PublicAPI.Unshipped.txt` muda no diff, e essa é a prova (D-291). O XML doc dos membros públicos fica, porque a regra de .NET o exige: remove-se só a citação de fase, ADR ou risco de dentro dele. Uma exceção: o XML doc de `IncludeHttpClientSource` (em `NetApiFlowOptions` e em `NetApiFlowOtlpExportOptions`) tem duas frases falsas, que dizem que a URL inteira, com a query, pode ser exportada (e citam `R-16`). A 59.0 mediu que a query não sai, e a 59B corrige as duas frases junto com a citação. Para dispensar um `//`, é permitido renomear uma variável local ou um membro privado e extrair uma constante, desde que o diff seja só isso e os testes existentes passem. O que não couber nisso volta à auditoria, que decide se vai para a `0.7.0` (a regra da fase: o texto muda, o código não).

## Tarefa 59.3: o parágrafo do MCP

O README do pacote (`src/NetApiFlow.Mcp/README.md`) só aponta para o README principal: a página que o nuget.org mostra não diz o que vale para o MCP **em si**. Depois das Fases 56 e 71 ela diz, em frases que um leitor que chegou direto na página do pacote entende sem sair dela: o portão (fora de `Development`, `404` sem autorização ou `IFlowAccessFilter`), a regra de `Host` e `Origin` (`403`), que as três tools existem só no `/flow/mcp`, o que acontece num app que hospeda o próprio MCP, e o que a 59.0 mediu para os verbos de `/flow/mcp` em `Production`, que depende do modo de sessão e de o painel estar montado (D-299). O texto de referência é o parágrafo "Same security rule as the dashboard" do `README.md`, que a Fase 71 já reescreveu, e a Executora o resume sem copiá-lo inteiro.

## Tarefa 59.4: a tabela de performance, de novo

A tabela do README é da `0.1.0`, nunca foi medida de novo, o hardware não é dito e o relatório não está no repositório.
"32 µs more" é o overhead total do cenário Nested. Medir no estado da `0.6.0` (esta tarefa é a **última** da fase, depois
das mudanças de custo das Fases 53, 55, 57, 58, 64 e 65):

- `tests/NetApiFlow.Benchmarks` com `[MemoryDiagnoser]`, nos dois TFMs.
- **Uma rodada concorrente**, além da sequencial.
- **O `Flow.Step` com `Enabled = true` e com `Enabled = false`, contra a `0.5.3` publicada.** A 60.9b pôs dois `Volatile.Read` antes do `HasListeners` (D-296), sem lock e sem alocação, e a auditoria quer o número. Se o custo por passo subir além do ruído, a Executora para e relata, e a auditoria trata.
- O relatório **versionado no repositório**, com o hardware, o sistema operacional, o runtime e o commit.
- O README com os números novos, o hardware dito, e a frase de "32 µs more" corrigida.

Se o número novo for pior que a promessa do README, vale o ponto de parada acima.

## Tarefa 59.5: a auditoria relê

A auditoria relê cada frase tocada, nos dois idiomas, contra o app novo. A Executora entrega a tabela da 59.0 com a
saída real e o diff do README, e **não** declara a fase pronta antes disso.

## Resultado da 59A (D-299, 25/11/2026)

Entregue em `develop` na #507 (squash `827d67e`), com a 59.0, a 59.1, a 59.3 e a 59.6. O relato é da Executora, no corpo da PR, e da auditoria, que conferiu com o pacote real `0.6.0-audit.18`. A Planejadora leu o corpo no head `679b37b` e não leu o último commit, `a107109`, que trata dos verbos do MCP.

- **59.0:** 23 frases falsas ou imprecisas (texto corrigido nos dois idiomas), 3 verdadeiras hoje (1, 10 e 11) e nenhuma sem reprodução. A tabela com a saída real está em `tests/manual-verification/Fase59ReadmeVerdadeiroVerification/README.md`.
- **59.1 e 59.3:** os READMEs e as páginas dos pacotes dizem o namespace de cada registro, o `schema.sql`, a versão do `SqlClient`, a licença do MediatR 13, os limites fixos, o aviso do OpenTelemetry 1.21.0 no `net8.0` e a regra do MCP.
- **O Início rápido no `net8.0` (D-277):** a auditoria colou na ordem num app novo com Swashbuckle, com `OpenApiDocumentPath = "/swagger/v1/swagger.json"`, e compila com 0 avisos, e o Runner lista as rotas e abre o trace.
- **O `/flow/mcp` em `Production`, por modo (D-299):** sem sessão, `POST` 404 e `GET`, `DELETE` e `PUT` 405. Com sessões, `POST`, `GET` e `DELETE` 404 e `PUT` 405. Com o painel em `/flow`, o `GET` é do painel.
- **59.6:** `CONTRIBUTING`, `SECURITY.md` (o MCP no escopo), o template de PR e o `config.yml` do `ISSUE_TEMPLATE`.
- **Aberto:** a 59.2 (59B), a 59.4 (59C) e a 59.5, a releitura da auditoria sobre o `develop` com a 59 inteira.

## Fora de escopo

- Qualquer mudança de código, salvo o que a Planejadora liberar item por item (ver a regra da fase).
- Os itens do nível 3 (Fase 60) e o que a varredura final da auditoria acrescentar.
- O item 26 do roadmap (filtrar o documento OpenAPI de `/endpoints`): a frase 16 vira texto, e a correção continua
  roadmap.
- As frases do README sobre o `net8.0` e o `net11.0`: são da Fase 69 (D-250).

## Critério que vale para a fase inteira

- Cada frase da tabela com a saída real da reprodução, e a frase final conferida pela auditoria.
- Build, testes e formato limpos. A checagem de citação no CI passa e foi vista falhando.
- O Início rápido no `net8.0` (D-277) e cada chamada de registro de pacote do README, colados num `dotnet new webapi` novo, compilam com 0 avisos, e a Executora diz se colou à mão ou com um script.
- `grep -rn "—"` sem ocorrência nos textos em português e nos READMEs.

## Versionamento e fluxo

- `0.6.0`. Em três PRs, como a 58 e a 60 (D-297), todas a partir de `develop` atualizado e **depois** das Fases 53, 55, 56, 57, 58, 60, 68 e 71:
  - **59A, o texto** (`chore/59-readme-verdadeiro`): a 59.0, a 59.1, a 59.3 e a 59.6. `README.md`, `README.pt-BR.md`, os READMEs dos pacotes e dos samples, `CONTRIBUTING`, `SECURITY.md` e o template de PR. O `.github/ISSUE_TEMPLATE/config.yml` é configuração e fica fora da exceção de PR só de documentação (D-154).
  - **59B, as citações** (`chore/59b-citacoes`): a 59.2, em C#, TypeScript, SCSS e JS, com a checagem no CI vista falhando e nenhum `PublicAPI.*.txt` mudando. Renomear um teste que cita fase mexe em linhas do `guards.json`, e a tarefa toca o `ClientApp`, então o detector roda nessa PR e não na do texto.
  - **59C, o desempenho** (`chore/59c-desempenho`): a 59.4, a última tarefa, depois de nada mais mudar o custo. O README ganha aqui só a tabela nova e a frase de "32 µs more".
- Commits pequenos, em inglês, uma linha, sem linha de coautor. Abrir a PR e parar.

## Relatório final

A tabela de 26 linhas com a classificação e a saída real. As contagens de citações e de comentários `//`, antes e depois. A saída da checagem de
citação, vermelha e verde. O relatório de performance e o hardware.
