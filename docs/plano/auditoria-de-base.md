# Auditoria de base do `develop` 35bcf2d: os achados e as fases que saem deles (D-210)

A `AUDITORIA-16-11` fez, antes da `0.6.0`, a auditoria de base que a D-205 pediu: por inventário e não por
diff, com o pacote `0.6.0-audit.2` em apps novos. Fontes: três revisores independentes (o README frase a
frase, as telas no navegador, a matriz de segurança) e reproduções da própria auditoria. As evidências e
os scripts ficam com ela. Esta página é o **índice**: cada item, o que a Planejadora conferiu por conta
própria, e a fase onde ele vai ser tratado. Cada fase ganha o seu `fase-N-*.md` antes de a Executora chegar
a ela, não antes.

**Regra da `0.6.0` (decisão da auditoria):** só sai com todo o **nível 1** fechado e todo o **nível 2**
corrigido. O **nível 3** vai para uma fase de acabamento antes da `0.6.0`, se couber, e o que não couber vai
para o roadmap, com um registro por item.

**Exigência de processo, para todo item que virar tarefa:** o teste que o mostra falhando, nos dois sentidos,
e a frase do README conferida de novo pela auditoria no fim. A matriz de autorização do MCP e do dashboard
passa a ser teste de host real, para não depender de auditoria manual.

## O que a Planejadora conferiu, e o que não

Nada abaixo foi aceito só pelo relato. A coluna "Conferi" diz o que eu mesma li ou medi. Onde diz "não
verifiquei", o item está de pé pela evidência da auditoria, e a primeira tarefa da fase o reproduz de novo
antes de corrigir.

## Nível 1: bloqueia a `0.6.0`

| # | Achado | Conferi | Fase |
|---|---|---|---|
| S1 | O OTLP exporta a mensagem de erro crua, sem sanitizar e sem os 300 caracteres. O painel mostra `password=[redacted]`, o OTLP recebeu `password=PW_SECRET` com 745 caracteres | **Código:** `FlowStep.cs:44` grava `exception.Message` na tag da atividade, e só o `FlowActivityListener` sanitiza, para o span do painel. A medição no OTLP é da auditoria | 54 |
| S2 | Brechas no sanitizador: `Authorization: Bearer X` apaga só `Bearer`; `access_token=`, `client_secret=` e `DB_PASSWORD=` passam porque `\b` não casa depois de `_`; `api_key` não está na lista; trunca antes de sanitizar | **Código:** li a regex (`\b(password\|token\|authorization\|api-key\|secret)\s*[:=]\s*\S+`) e o corte antes da limpeza. As brechas decorrem do texto dela. Não rodei | 54 |
| S3 | A identidade da trace é o W3C trace id que o cliente manda: o mesmo `traceparent` em duas requisições apaga a primeira trace, um serviço que chama a API duas vezes vira etapas dentro da trace do chamador, e um cliente anônimo escolhe o id e sobrescreve uma trace | **Código:** `InMemoryTraceStore` grava `_entries[trace.TraceId] = ...`, que sobrescreve, e `FlowRecorder.TryStartTrace` recusa um id que já está ativo. A auditoria reproduziu no `0.5.2` com um `traceparent` fixo em `/orders` e `/stock` (comandos abaixo): só `/stock` fica no store. Não reproduzi de novo eu mesma | 55 |
| S4 | `AllowAnonymous` mais `RequireAuthorization` abrem o painel e o MCP | **Reproduzi no `0.5.2`** (Tarefa 50.10) | 50.10 |
| S5 | `PathBase` | **Reproduzi no `0.5.2`**, e a Executora mediu a matriz inteira (53.0) | 53 |
| S6 | O MCP e o REST não validam `Origin` nem `Host` em Development: `POST /flow/mcp` com `Origin: http://evil.example` deu 200. A especificação do MCP exige validar `Origin` contra DNS rebinding | **Reproduzido pela auditoria no `0.5.2` publicado** (comandos abaixo): `initialize` 200 com `Origin: http://evil.example`, `tools/list` lista as três ferramentas, `GET /flow/api/traces` 200 com `Origin` e `Host` falsos, `DELETE /flow/api/traces` 204. Não reproduzi de novo eu mesma | 56 |
| S7 | Sem cabeçalhos de segurança em `/flow`: CSP, `X-Frame-Options` ou `frame-ancestors`, `X-Content-Type-Options`. O painel, com o botão de apagar traces, carrega num iframe | **Reproduzido pela auditoria no `0.5.2`**: `GET /flow/` só traz `Content-Type: text/html`, sem CSP, `X-Frame-Options`, `X-Content-Type-Options` nem `Referrer-Policy`. Não reproduzi de novo eu mesma | 56 |
| F1 | O estado "Aguardando" nunca aparece: o middleware abre a trace com `(unmatched)` e só grava a rota no fim, e o painel busca o blueprint por essa rota. "Não executado" ao vivo também não funciona. E "Não executado" usa o **último sucesso**, mesmo posterior à trace aberta, então o desenho de uma trace antiga muda com o tempo | **Código:** a primeira parte confere, o middleware abre com `UnmatchedRouteTemplate` e atualiza a rota no `finally`. A parte do blueprint posterior: **reproduzida pela auditoria no `0.5.2`** (comandos abaixo): a trace B, mais nova, vira o blueprint da trace A, mais antiga, porque `/blueprint` não recebe âncora de tempo. Não reproduzi de novo eu mesma | 57 |
| F2 | O Npgsql 10 sai sempre como "SQL command", sem SQL, porque o código só lê `db.statement` e o Npgsql 10 manda `db.query.text`. A suíte testa só o Npgsql do EF 9.0.4 | **Código:** `NpgsqlActivityTagNames.Statement = "db.statement"`. **Reproduzido pela auditoria** com o Npgsql 10.0.0 em `net10.0` (ver abaixo): são três defeitos, não um. Não reproduzi de novo eu mesma | 58 |
| F3 | Uma resposta 500 de exceção não tratada sai sem `X-NetApiFlow-TraceId`, embora a trace exista. O Runner mostra só "A requisição falhou.", o `NetApiFlow.Testing` diz "did not track this request", e o primeiro bloco de "Asserting" do README quebra | **Reproduzi no `0.5.2`**: 500 sem o cabeçalho em Development e em Production, e em Development a trace existe no store (`Error`, 500) | 58 |
| F4 | O README não diz que o `NetApiFlow.MediatR` exige `AddNetApiFlowMediatR()`. Sem a chamada, nada aparece e nada avisa | **Não verifiquei** | 58 |

## Nível 2: correção do README nos dois idiomas, e do código quando o texto não puder ficar verdadeiro sem ele

Fase 59. Cada frase abaixo foi medida pela auditoria como falsa ou imprecisa. A fase começa reproduzindo cada
uma, e a frase final é conferida de novo pela auditoria.

- `SuppressFlow`: "record correctly, but as a new root" (a etapa some).
- OTLP: "samples independently ... can still be exported". Com a configuração do README dá o contrário
  (`Enabled=false` e `ExcludePaths` exportam 0; um sampler de 0% do host exporta os spans do NetApiFlow).
- O aviso do `IncludeHttpClientSource`: a query não saiu no net8 nem no net10, o texto está pessimista.
- "Sensitive data": o corpo volta pela mensagem de exceção (`int.Parse` de um campo do corpo). Dizer com
  clareza.
- `ExternalSources` "default is None": em Development o padrão é HttpClient, Npgsql e SqlClient.
- "Run an endpoint": a UI diz "Try an endpoint". `scheme://host[:port]/path`: o gravado sai sem o esquema.
- `CapturePrePipelineSteps`: o `[Authorize]` comum não gera etapa, e o output caching não aparece no acerto
  de cache.
- `IAsyncEnumerable`: não "falha". Grava cerca de 0,9 ms e Success, mesmo quando a enumeração lança.
- AOT: "warning at build time instead of a failure at startup". O binário sobe e dá 500 na primeira
  requisição. E "there is a check in the repository that proves it" só passa a ser verdade com a 50.12.
- "naming convention (e.g. I*Repository)": o predicado recebe o tipo concreto.
- "only compiles from inside a namespace of your own": com o `using` no topo também dá CS0234.
- Os blocos de "Asserting" não compilam sem `using Microsoft.AspNetCore.Mvc.Testing`, e no net8 precisam de
  `public partial class Program`.
- "in-memory buffer kept while persistence catches up": é uma fila fixa das últimas 500 traces.
- O cartão p95 com sparkline só aparece com 1600 px ou mais. "Authorize ... above the request form" e
  "preview shows the exact header": a seção fica entre os parâmetros e os headers, e a prévia é mascarada.
- Import: "same ... metric cards and event feed": os cartões mostram a instância local e o feed fica vazio.
- Rotas mortas listam `GET /openapi/{documentName}.json`, porque o `/endpoints` não filtra o documento
  (ligado ao item 26 do roadmap).
- Performance: a tabela é da `0.1.0`, nunca foi medida de novo, o hardware não é dito e o relatório não está
  no repositório. "32 µs more" é o overhead total do Nested. Medir de novo no estado da `0.6.0`, versionar o
  relatório, dizer o hardware, incluir `[MemoryDiagnoser]` e uma rodada concorrente.
- pt-BR: o gênero de "trace" alterna. Padronizar no masculino, como a UI.

## Nível 3: acabamento

Fase 60, se couber. O que não couber vira item de roadmap, um registro por item.

- **Celular:** Comparar (B e Delta escondidos), Insights (p95 fora da tela), Histórico (botão Comparar
  cortado, rota truncada).
- **Textos do painel:** "Etapa mais lenta" aponta a raiz e não a folha. "Origem do erro" mostra a duração da
  trace, não a da etapa. "0% do tempo (812 µs de 402,3 ms)" lê contraditório. "Status Success" sem tradução.
- **Runner:** sem o status HTTP e sem o corpo da resposta; "Trace: <id>" não é link; rótulos colados
  ("idcaminho", "bodycorpo"); POST sem exemplo de corpo; mensagem genérica no 500. Achados pela Executora
  na demo, sem correção: uma corrida no formulário (o campo de rota preenchido poucas dezenas de
  milissegundos depois de escolher o endpoint volta vazio e o envio diz "Preencha o parâmetro de rota":
  1 em 25 tentativas no `develop` e 3 em 25 num branch, causa não determinada); e o `/runner` sozinho
  mostra `Trace: <id>` sem grafo, embora o subtítulo diga "O grafo acompanha em tempo real".
- **Texto e rótulos:** `aria-label="Request summary"` fixo em inglês em
  `flow-details-panel.component.html`, mesmo em português. O OpenAPI lista `/api/orders/{id}` e o histórico
  mostra `/api/orders/{id:int}`.
- A lista "ETAPAS" não é clicável. O chip "Ao vivo" fica aceso numa trace importada.
- **Arquitetura:** legenda com estados que não existem num mapa estático; N+1 sem contagem; SVG exportado
  quebra fora do painel (`_ngcontent` sem CSS).
- **Histórico:** a 3ª caixa marcada desmarca outra em silêncio; "Só lentas" aparece duplicado.
- **API:** `Tag` só aceita string (um enum não compila); "Expected at most 1 Database calls" (plural);
  `dbo.Users` vira "SELECT dbo"; `UseNetApiFlowDashboard` deveria ser `Map*` pela convenção do ASP.NET Core
  (avaliar com depreciação); `DELETE` e `POST /deploys` usam a mesma política da leitura, sem papel só
  leitura; `Enabled=false` não desliga com outro listener; o OTLP sai com spans órfãos e o atributo `sql` no
  lugar de `db.query.text`.
- O `NetApiFlow.Storage.SqlServer` traz o `Microsoft.Data.SqlClient` 5.2.2, que não grava comandos. O README
  não fala da licença do MediatR 13 ou superior.

## Reproduções da auditoria, no `0.5.2` publicado

Vêm da auditoria e **não foram rodadas de novo pela Planejadora**. Pacotes `NetApiFlow`,
`NetApiFlow.Dashboard` e `NetApiFlow.Mcp` `0.5.2`, `net10.0`, Development, com `AddNetApiFlowMcp` e
`UseNetApiFlowMcp` e o painel montado. `B` é `http://127.0.0.1:<porta>`. A primeira tarefa de cada fase
parte do comando, e o teste de host real que o prova entra com a correção.

**S6, sem checagem de `Origin` nem de `Host`:**

```text
curl -s -o /dev/null -w "%{http_code}" -X POST "$B/flow/mcp" -H "Origin: http://evil.example" -H "Content-Type: application/json" -H "Accept: application/json, text/event-stream" -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"x","version":"1"}}}'
curl -s -o /dev/null -w "%{http_code}" "$B/flow/api/traces" -H "Origin: http://evil.example" -H "Host: evil.example"
curl -s -o /dev/null -w "%{http_code}" -X DELETE "$B/flow/api/traces" -H "Origin: http://evil.example"
```

Deu 200, 200 e 204. Com `{"jsonrpc":"2.0","id":2,"method":"tools/list"}` o MCP listou
`get_component_stats`, `get_trace` e `list_recent_traces`.

**S7, cabeçalhos ausentes:** `curl -s -D - -o /dev/null "$B/flow/"`. O único cabeçalho de conteúdo é
`Content-Type: text/html`. Não há `Content-Security-Policy`, `X-Frame-Options`, `X-Content-Type-Options` nem
`Referrer-Policy`.

**F1, blueprint posterior.** Um endpoint marcado com retorno antecipado:

```csharp
app.MapGet("/orders/{id:int}", async (int id, bool cached) => {
    using (NetApiFlow.NetApiFlow.Step("OrderCache", "TryGet")) { await Task.Delay(5); }
    if (cached) return "cached";
    using (NetApiFlow.NetApiFlow.Step("OrderRepository", "Find")) { await Task.Delay(5); }
    return "loaded";
}).WithNetApiFlowTracking();
```

1. `DELETE /flow/api/traces`. 2. `GET /orders/1?cached=true` (trace A). 3. `GET /orders/2?cached=false` (trace
B, depois de A). 4. `GET /flow/api/blueprint?method=GET&route=/orders/{id:int}`. O blueprint devolvido é a
trace B, posterior à A, com `OrderCache.TryGet` e `OrderRepository.Find`. A trace A, mais antiga, é desenhada
contra uma execução que aconteceu depois dela.

**S3, o mesmo `traceparent`:** `TP="00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01"`, depois
`curl -H "traceparent: $TP" $B/orders` e `curl -H "traceparent: $TP" $B/stock`. Só `/stock` fica em
`/flow/api/traces`. Um serviço que chama `/orders` e `/stock` com `HttpClient` gera uma trace só, a do
chamador, com as duas requisições como etapas filhas.

**F2, o Npgsql 10: reproduzido pela auditoria, três defeitos.** Pacotes `NetApiFlow` e
`NetApiFlow.Dashboard` `0.5.2`, Npgsql `10.0.0`, `net10.0`, Development, `postgres:16-alpine` em contêiner,
com um `ActivityListener` próprio na fonte `Npgsql` registrando as tags cruas:

```csharp
builder.Services.AddNetApiFlow(o => { o.ExternalSources = ExternalActivitySource.Npgsql; o.CaptureSqlText = true; });
app.MapGet("/users", async () => {
    await using var c = new NpgsqlConnection("Host=127.0.0.1;Port=55432;Username=postgres;Password=audit");
    await c.OpenAsync();
    await using var cmd = new NpgsqlCommand("SELECT 1 AS id", c);
    return (int)(await cmd.ExecuteScalarAsync())!;
}).WithNetApiFlowTracking();
```

Tags cruas que o Npgsql 10 emite: a atividade `postgresql` traz `db.query.text`, `db.system.name`,
`db.namespace`, `server.address`, `server.port`, `db.npgsql.data_source` e `db.npgsql.connection_id`. A
atividade `CONNECT postgres` traz `db.system.name`, `db.npgsql.data_source`, `db.namespace`, `server.address`,
`server.port` e `db.npgsql.connection_id`. O NetApiFlow gravou, na trace de `/users`:
`Endpoint | HTTP: GET /users | /users` e duas etapas `Database | Npgsql | SQL command | tags: {}`.

1. A operação é sempre `SQL command`, porque o rótulo vem de `db.statement`, que o Npgsql 10 não manda mais.
2. O SQL nunca é gravado, nem com `CaptureSqlText = true`.
3. O `CONNECT` vira uma segunda etapa `Database` idêntica à do comando. Num grafo isso parece uma consulta
   duplicada, e pode disparar o selo de N+1 com várias conexões.

**O que a Fase 58 exige:** ler `db.query.text` com fallback para `db.statement`; decidir o que fazer com o
`CONNECT`; o Npgsql 10 na matriz de `Npgsql.IntegrationTests`, sem tirar o 9 nem o 8, que continuam
suportados; e o teste novo tem de **falhar hoje** com o Npgsql 10.

**Sobre o `CONNECT`, a recomendação da Planejadora é ignorar**, porque não é um comando SQL e duplica a
etapa. A Executora mede antes o que distingue um `CONNECT` de um comando no Npgsql 8, 9 e 10 (o nome da
atividade e as tags) e propõe o critério, porque classificar pelo nome ou pela falta da tag de texto são as
duas formas frágeis, e a Planejadora decide com a medição. Um teste prova que várias conexões não disparam
o selo de N+1.

**F3, o que a Fase 58 exige além do cabeçalho.** O Runner lê o trace id do cabeçalho `X-NetApiFlow-TraceId`
(`endpoint-form.component.ts`, `onDispatched`). Sem o cabeçalho, numa resposta de sucesso, ele fica mudo: não
navega e não diz nada. Isso vale para o 500 do F3 e para `EmitTraceIdHeader = false`, a opção da Parte B,
que a Executora derivou do código e **não mediu no navegador**. A Fase 58 faz o Runner dizer por que não
achou o trace, nomeando as causas possíveis (a opção desligada, a rota fora do rastreamento), e o teste
mede isso no navegador. A Parte B (f) não ganha esse escopo: ela só liga a opção.

## Ordem aprovada, e por quê

A auditoria aprovou esta ordem em 20/11/2026, sem mudança.

Já na fila, pela ordem decidida antes: **50.10** (S4), **50.12** (AOT no CI), **Parte B** (f, g) e **50.9**,
**Fase 51** (a 51.9 primeiro), **correção da Fase 53**. A ordem aprovada encaixa as fases novas assim:

1. **50.10 e 50.12.** Seguem como estão.
2. **Fase 54 (S1, S2): os segredos nunca saem.** Sem dependência, é segurança de dado e é pequena.
3. **Fase 56 (S6, S7 e a matriz de autorização).** Segurança, sem dependência de nada que está na fila.
4. **Fase 55 (S3): a identidade da trace.** Pede ADR antes de qualquer código, e pode pedir mudança de
   chave nos dois storages. Medir o Postgres e o SQL Server primeiro (conflito de chave primária).
5. **Parte B (f, g) e 50.9, com o F3 junto do (f).** O F3 mexe no mesmo trecho do middleware que escreve o
   cabeçalho, onde o (f) já está.
6. **Fase 51** (a 51.9 primeiro). Ela introduz o `EndpointMatched`.
7. **Correção da Fase 53 e Fase 57 (F1), desenhadas juntas.** As duas dependem do mesmo ponto: a decisão e a
   rota da trace passam a vir do `EndpointMatched`. Um desenho só, um ADR (ADR-0027).
8. **Fase 58 (F2, F4, e o F3 se não entrou no passo 5).**
9. **Fase 59** (nível 2), perto do fim, porque muitas frases dependem do código das fases anteriores.
10. **Fase 60** (nível 3), se couber.
11. **A segunda passada da auditoria de base**, e só então o release.

**Por que esta ordem.** Segurança primeiro e barata (54, 56) porque exposição de dado não deve esperar a
`0.6.0` ficar redonda. A 55 vem cedo porque é estrutural e pode mexer no schema, e quanto mais tarde,
mais código depende da identidade antiga. O grupo 51, 53 e 57 tem de andar junto pelo ponto comum. O
README (59) fica para o fim para não ser reescrito duas vezes.

## O que isto faz com a `0.6.0`

**Decisão do dono (D-212): tudo na `0.6.0`, sem data.** O escopo é as Fases 50, 51 e 53 a 59, e a 60 se
couber. A `0.6.0` sai quando o nível 1 e o nível 2 estiverem fechados e a segunda passada da auditoria
passar, limpa nos dois níveis. A `0.6.0` deixou de ser as Fases 50 e 51 e ficou maior, e o dono escolheu
isso de olhos abertos.

## ADRs que precisam existir antes do código

- **ADR-0027:** a decisão de rastrear e a rota da trace passam a vir do `EndpointMatched` (Fase 53 e Fase
  57).
- **S3, identidade da trace:** o id da trace do NetApiFlow passa a ser próprio, e o W3C trace id vira um
  atributo para correlação com o OTLP. ADR escrito pela Planejadora na Fase 55, depois de medir os dois
  storages.
