# Os quatro advisories da `0.6.0` (D-292)

Um lugar só, para o dono confirmar de uma vez antes do release. Quem publica é a `AUDITORIA` (D-198), junto do Release e **depois** de os pacotes `0.6.0` estarem no nuget.org, para o aviso apontar para uma correção que dá para instalar.

Os textos abaixo são os campos do formulário de advisory do GitHub, em inglês e sem dado de app real. **Daqui em diante esta página é a versão que se publica.** O rascunho de cada fase (Tarefa 50.10, Fase 54, Fase 56 e Tarefa 71.3) é o histórico de como ele foi escrito, e uma correção nova entra aqui.

## A severidade de cada um, e o que falta

Os quatro têm a severidade decidida pelo dono. Os dois últimos foram confirmados em 25/11/2026, relato da auditoria, e a Planejadora não leu a escolha na sessão dele.

| # | Advisory | Nota e vetor | Quem decidiu a severidade | O que falta antes de publicar |
|---|---|---|---|---|
| 1 | 50.10: os portões do painel e do MCP confiam em `RequireAuthorization()` mesmo com `AllowAnonymous()` (D-199, D-206) | **Média 4,8**, `CVSS:3.1/AV:N/AC:H/PR:N/UI:N/S:U/C:L/I:L/A:N` | O dono, no D-206 ("Média, texto completo") | Nada: a auditoria reproduziu em 25/11/2026 o `NetApiFlow.Dashboard` `0.1.0` (painel, sem MCP nessa versão), o `0.2.0` (painel e MCP) e a `0.5.3` (D-305). **Liberado para publicar** |
| 2 | Fase 54: segredos na mensagem de exceção saem sem filtro no OTLP e filtrados pela metade no painel (D-221, D-223) | **Média 5,3**, `CVSS:3.1/AV:N/AC:H/PR:L/UI:N/S:U/C:H/I:N/A:N` | O dono, no D-223 ("o texto verdadeiro decide o número") | Nada: reproduzido na `0.5.3` em 25/11/2026, com os 13 casos e um coletor (D-305). **Liberado para publicar** |
| 3 | Fase 56: o painel e o MCP abertos aceitam qualquer página (DNS rebinding) e o painel pode ser emoldurado (D-224, D-226) | **Média 4,2**, `CVSS:3.1/AV:N/AC:H/PR:N/UI:R/S:U/C:L/I:L/A:N` | **O dono, em 25/11/2026: Média 4,2.** A escolha substitui a "severidade baixa" do D-226, porque o `DELETE /flow/api/traces` conta como integridade (Baixa, 3,1, só saía ignorando-o) | Nada: o S6 e o S7 foram refeitos com o pacote real `0.6.0-audit.20` e o defeito foi reproduzido na `0.5.3` publicada (25/11/2026, D-249, D-305). **Liberado para publicar** |
| 4 | Fase 71: as tools do `NetApiFlow.Mcp` aparecem no endpoint MCP do próprio app (D-278, D-279, D-282, D-283) | **Média 5,3**, `CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:L/I:N/A:N`. Com o `/mcp` do app exigindo autenticação, `PR:L` e **4,3** | **O dono, em 25/11/2026: Média 5,3, CWE-862 (o lado das traces) e CWE-863 (o espelho), versões `0.2.0` a `0.5.3`.** O espelho fica fora da nota (D-283) | Nada: já reproduzido na `0.5.3` pela auditoria |

As quatro notas foram recalculadas por script pela Planejadora em 25/11/2026, pela fórmula do CVSS 3.1: 4,8, 5,3, 4,2 e 5,3 (4,3 com `PR:L`). A conta bate com cada rascunho.

## Publicados

A auditoria publicou os quatro em 25/11/2026, **depois** de os pacotes `0.6.0` estarem no nuget.org e **antes** de criar a página de Release, para o Release já ter os números como link. O GitHub calculou as mesmas notas dos rascunhos.

| # | Advisory | Número | Nota |
|---|---|---|---|
| 1 | 50.10, os portões | `GHSA-wwmr-9vv7-4234` | Média 4,8 |
| 2 | Fase 54, os segredos | `GHSA-r234-jvm5-qgr2` | Média 5,3 |
| 3 | Fase 56, `Host`, `Origin` e iframe | `GHSA-9649-h9h4-626h` | Média 4,2 |
| 4 | Fase 71, as tools do MCP | `GHSA-7jm3-c37r-vjmw` | Média 5,3 |

## A faixa de versões vai até a `0.5.3`

Os rascunhos 1, 2 e 3 foram escritos quando a última versão publicada era a `0.5.2`, e diziam `<= 0.5.2`. A `0.5.3` (o hotfix do `RetentionService`, D-235) saiu depois. `git diff v0.5.2 v0.5.3 --stat -- src` lista cinco arquivos: `PersistenceWriter.cs`, `RetentionInterval.cs`, `RetentionService.cs`, `NetApiFlowServiceCollectionExtensions.cs` e o `config.json` da demo. Nenhum é um portão, o sanitizador ou a superfície HTTP, então os três defeitos existem na `0.5.3` pela identidade do código, e os textos abaixo dizem `<= 0.5.3`. A Planejadora não reproduziu nada na `0.5.3`. **A auditoria não aceitou a faixa só pela identidade do código e reproduziu os quatro contra a `0.5.3` publicada, em 25/11/2026 (D-305):** o 1 (S4, e também o `NetApiFlow.Dashboard` `0.1.0` e o `NetApiFlow.Mcp` `0.2.0`), o 2 (S1 e S2, com um coletor), o 3 (S6 e S7) e o 4. A faixa `<= 0.5.3` está confirmada nos quatro. O rascunho 4 já nasceu com `<= 0.5.3`.

## Ordem e condição de publicação

1. O dono já decidiu a severidade dos quatro (25/11/2026).
2. Os pacotes `0.6.0` estão no nuget.org e o Release do GitHub existe (`/release`, passos 10 e 11).
3. A auditoria cria cada advisory como rascunho privado no repositório, cola os campos abaixo, confere a faixa e a versão corrigida (`0.6.0`) e publica. O S6 e o S7 já foram refeitos (D-305), então os quatro estão liberados.
4. A auditoria diz à Planejadora o número de cada advisory (GHSA), e a Planejadora o registra e o põe no `SECURITY.md` ou no CHANGELOG, se o dono quiser.

## 1. 50.10, os portões

> **Title:** Dashboard and MCP routes are served to anonymous callers when `RequireAuthorization()` and
> `AllowAnonymous()` are chained
>
> **Ecosystem and packages:** NuGet. `NetApiFlow.Dashboard`, affected `>= 0.1.0, <= 0.5.3`.
> `NetApiFlow.Mcp`, affected `>= 0.2.0, <= 0.5.3`. Patched version: `0.6.0` for both.
>
> **CWE:** CWE-863, Incorrect Authorization.
>
> **Severity:** Moderate. CVSS 3.1 base score 4.8,
> `CVSS:3.1/AV:N/AC:H/PR:N/UI:N/S:U/C:L/I:L/A:N`.
>
> **Description:** Outside the Development environment, the access gate of the NetApiFlow dashboard and
> of the NetApiFlow MCP endpoint is meant to answer 404 unless the application protected the routes. It
> decided that by checking only that the endpoint carries authorization metadata. ASP.NET Core ignores
> that authorization when the endpoint also carries `IAllowAnonymous`. An application that chained
> `RequireAuthorization(...)` and `AllowAnonymous()` on the NetApiFlow endpoints, outside Development
> and without registering an `IFlowAccessFilter`, therefore served every dashboard and MCP route to
> anonymous callers, while the configuration looked protected.
>
> **Impact:** An anonymous caller can read trace metadata: route templates, step names, durations,
> statuses, truncated and sanitized error messages, SQL command text when `CaptureSqlText` is enabled,
> and any step tags the application set. Through the dashboard, the caller can also delete every stored
> trace (`DELETE /flow/api/traces`). NetApiFlow never records request or response bodies, headers, query
> strings, method arguments or SQL parameter values, so none of those are exposed.
>
> **Not affected:** applications that chain only `RequireAuthorization(...)` (the policy is enforced),
> only `AllowAnonymous()` (the routes answer 404), or that register an `IFlowAccessFilter` (the filter
> decides every request). The Development environment is open by design.
>
> **Patches:** From `0.6.0`, outside Development and without an `IFlowAccessFilter`, a route is served
> only when it carries authorization metadata and no `AllowAnonymous` metadata. Otherwise it answers 404.
> An application that relied on chaining both to open the dashboard now gets 404 and should register an
> `IFlowAccessFilter`.
>
> **Workarounds:** Do not chain `AllowAnonymous()` and `RequireAuthorization()` on the NetApiFlow
> endpoints. Chain `RequireAuthorization("YourPolicy")` alone, or chain `AllowAnonymous()` and register
> an `IFlowAccessFilter`. Upgrade to `0.6.0`.

## 2. Fase 54, os segredos

> **Title:** Secrets in exception messages are exported to OpenTelemetry unredacted, and only partly
> redacted in the dashboard
>
> **Ecosystem and packages:** NuGet. `NetApiFlow`, affected `>= 0.1.0, <= 0.5.3`.
> `NetApiFlow.OpenTelemetry`, affected `>= 0.2.0, <= 0.5.3`. Patched version: `0.6.0` for both.
>
> **CWE:** CWE-532, Insertion of Sensitive Information into Log File.
>
> **Severity:** Moderate. CVSS 3.1 base score 5.3, `CVSS:3.1/AV:N/AC:H/PR:L/UI:N/S:U/C:H/I:N/A:N`.
>
> **Description:** NetApiFlow documents that it never captures request or response bodies, headers, query
> strings, method arguments or SQL parameter values, and that error messages are truncated to 300 characters and
> sanitized. Two defects broke that promise for the error message of a step. First, `FlowStep.MarkError` wrote the
> raw exception message to an activity tag, so anything that listens to the `NetApiFlow` activity source,
> including the OpenTelemetry export, received it without redaction and without truncation. Second, the
> sanitizer redacted only a short list of forms: a key spelled exactly as `password`, `token`, `authorization`,
> `api-key` or `secret`, followed by `:` or `=`. Common forms such as access tokens, client secrets,
> connection-string passwords, JSON keys and an `Authorization` header with a scheme were left unredacted in the
> dashboard, the REST API, the event stream, the MCP tools, the correlated logs and the storage. The message was
> also truncated before it was sanitized.
>
> **Trigger:** An application records an exception on a step, for example a traced endpoint or service that
> throws, and the message of that exception contains a secret.
>
> **Impact:** The secret reaches the OTLP backend without any filtering and at any length, and it is shown in the
> dashboard in most common secret shapes (10 of the 12 tested). The severity depends on what the application puts
> in exception messages. A leaked credential often gives access to more than the telemetry itself.
> NetApiFlow does not add secrets itself.
>
> **Not affected:** Applications whose exception messages contain no secret. Step tags set explicitly by the
> application are documented as stored without sanitization and are not part of this issue.
>
> **Patches:** From `0.6.0`, the message is sanitized and truncated before it is written to the activity, so
> every consumer receives the clean value. The sanitizer matches any key that contains a sensitive word,
> redacts the whole value (quoted values and `Authorization` schemes included), and the final message never
> exceeds 300 characters.
>
> **Workarounds:** Do not put secrets in exception messages. Drop the `netapiflow.error.message` attribute in the
> OpenTelemetry Collector, for example with the `attributes` processor and the `delete` action, or disable the
> OTLP export. Restrict access to the dashboard. Upgrade to `0.6.0`.

## 3. Fase 56, DNS rebinding e iframe

> **Title:** The dashboard and the MCP endpoint accept requests from any web page when they are open, and the
> dashboard can be framed
>
> **Ecosystem and packages:** NuGet. `NetApiFlow.Dashboard`, affected `>= 0.1.0, <= 0.5.3`. `NetApiFlow.Mcp`,
> affected `>= 0.2.0, <= 0.5.3`. Patched version: `0.6.0` for both.
>
> **CWE:** CWE-346, Origin Validation Error. CWE-1021, Improper Restriction of Rendered UI Layers or Frames.
>
> **Severity:** Moderate. CVSS 3.1 base score 4.2, `CVSS:3.1/AV:N/AC:H/PR:N/UI:R/S:U/C:L/I:L/A:N`.
>
> **Description:** When the dashboard or the MCP endpoint is open, which is the default in the Development
> environment, it answered requests regardless of their `Host` and `Origin` headers. It also sent no
> `X-Frame-Options`, `Content-Security-Policy` or `X-Content-Type-Options` header. A web page controlled by an
> attacker can use DNS rebinding so that the browser treats the page and the local dashboard as the same origin,
> and then read the traces, delete all of them, and call the MCP tools. The MCP transport specification requires
> servers to validate the `Origin` header on all incoming connections to prevent exactly this. Separately, a
> page can embed the dashboard in an iframe and lead the developer to click its delete button (clickjacking).
>
> **Trigger:** The dashboard or the MCP endpoint is reachable without access protection, and the developer
> visits a malicious web page while the application is running.
>
> **Impact:** Reading and deleting the stored traces, calling the read-only MCP tools, and clickjacking of the
> delete button. NetApiFlow never records request bodies, headers, query strings, method arguments or SQL
> parameter values, so none of those are exposed.
>
> **Not affected:** Applications that protect the dashboard and the MCP endpoint with authorization are not
> exposed to the rebinding part, because a request to the attacker's host name carries no credential issued for
> the application's host name. The framing part applies in every mode.
>
> **Patches:** From `0.6.0`, the dashboard and the MCP endpoint reject requests whose `Host` is not a loopback
> name or explicitly declared, and requests whose `Origin` is not their own origin or a loopback origin, and
> they send security headers that forbid framing.
>
> **Workarounds:** Do not leave a development application running while browsing untrusted sites, or protect the
> dashboard and the MCP endpoint with an `IFlowAccessFilter`, which is enforced in Development too. Upgrade to
> `0.6.0`.

## 4. Fase 71, as tools do MCP

> **Title:** The MCP tools of NetApiFlow are served on the MCP endpoint of the application, outside the access rule of the
> dashboard
>
> **Ecosystem and packages:** NuGet. `NetApiFlow.Mcp`, affected `>= 0.2.0, <= 0.5.3`. Patched version: `0.6.0`.
>
> **CWE:** CWE-862, Missing Authorization. CWE-863, Incorrect Authorization.
>
> **Severity:** Moderate. CVSS 3.1 base score 5.3, `CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:L/I:N/A:N`. The score applies when the
> MCP endpoint of the application is anonymous. When that endpoint requires authentication, the privileges required are low
> and the score is 4.3.
>
> **Description:** `NetApiFlow.Mcp` adds three read-only tools to the MCP server of the process: `list_recent_traces`,
> `get_trace` and `get_component_stats`. The MCP SDK it uses, `ModelContextProtocol.AspNetCore`, has one MCP server per
> process, so every endpoint mapped with `MapMcp` serves everything that server has. In an application that hosts its own MCP
> server in the same process, the three tools were served on the endpoint of the application, outside the access rule that
> protects the NetApiFlow endpoint (`/flow/mcp`): not found outside the Development environment unless authorization or an
> `IFlowAccessFilter` is configured. `list_recent_traces` returns the id, method, route
> template, status and duration of recent requests, and `get_trace` returns a trace with its steps (component names and SQL
> text without parameter values). The reverse also held: the NetApiFlow endpoint listed and ran the tools of the application,
> and served its prompts, resources and completions, under the access rule of the dashboard instead of the rule of the
> application.
>
> **Trigger:** The application hosts its own MCP server in the same process (`AddMcpServer` and `MapMcp`) and also calls
> `AddNetApiFlowMcp`.
>
> **Impact:** Anyone who can reach the MCP endpoint of the application can read the metadata of recent requests and their
> traces, even where the NetApiFlow endpoint is closed. NetApiFlow never records request bodies, headers, query strings,
> method arguments or SQL parameter values, so none of those are exposed. The three tools only read. Through the reverse path,
> a client admitted to the NetApiFlow endpoint could call the tools of the application, which may write, under the rule of the
> dashboard.
>
> **Not affected:** Applications that do not host their own MCP server in the same process. For them the NetApiFlow endpoint
> was, and is, the only place where the tools exist.
>
> **Patches:** From `0.6.0`, the three tools exist only on the NetApiFlow endpoint, and the decision is made for every JSON-RPC
> message.
> The endpoint of the application does not serve them. The NetApiFlow endpoint answers only the handshake, `ping`,
> `logging/setLevel`, `tools/list` and `tools/call` for its three tools. Any other method gets `-32601`, so the prompts,
> resources and completions of the application are not reachable through it. When the application registers a tool with the
> same name as a NetApiFlow tool first, that name serves the tool of the application on its own endpoint, the NetApiFlow tool
> with that name is not served on any endpoint, and a warning at startup names the tool to rename. When the application sets
> `PerSessionExecutionContext = true` with sessions, the tools are not served on any endpoint, the NetApiFlow endpoint answers
> 404, and a warning at startup names the two ways out (`PerSessionExecutionContext = false`, or
> `SessionMode = HttpServerSessionMode.Stateless`).
>
> **Workarounds:** Require authorization on the MCP endpoint of the application (`MapMcp(...).RequireAuthorization()`). Its
> authenticated users can still read the traces. Or do not host the MCP server of the application in the same process as
> `NetApiFlow.Mcp`. Upgrade to `0.6.0`.
