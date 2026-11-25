# O texto do GitHub Release da `0.6.0` (D-303)

Rascunho para a `AUDITORIA` usar no passo 15 da checklist (`release-0.6.0.md`). O texto do Release é a abertura abaixo, em inglês, mais a seção `[0.6.0]` do `CHANGELOG.md` **como ela sair da `/release`**, sem
mudar uma palavra dela. A abertura é da Planejadora e a decisão de cortar ou manter cada item é da auditoria. O `CHANGELOG.md` não é tocado antes da `/release`.

## Como montar

1. Depois da `/release`, o corpo é a abertura abaixo, uma linha em branco e a seção `[0.6.0] - <data>` do `CHANGELOG.md`, sem o cabeçalho `## [0.6.0]`.
2. `gh release create v0.6.0 --title v0.6.0 --notes-file <corpo>` (passo 15).
3. **Os números dos advisories ainda não existem nesse momento,** porque os advisories saem depois do Release (passo 16, com os pacotes já no nuget.org). O Release sai com as quatro linhas de segurança sem o número.
   Depois de publicar cada advisory, a auditoria troca o `GHSA-xxxx-xxxx-xxxx` pelo número real e edita o Release: `gh release edit v0.6.0 --notes-file <corpo>`.
4. Conferir o `[Unreleased]` antes: a lista de leitura que a Planejadora mandou à auditoria (D-303) já foi aplicada (D-304), e a abertura segue o `CHANGELOG.md` final. Quando a 59C entrar, a Planejadora relê a seção e confirma que a abertura continua verdadeira, antes do passo 15.

## A abertura

```markdown
NetApiFlow 0.6.0 changes how the packages fit together, what a traced request looks like, and who can reach the dashboard and the MCP endpoint. It fixes four security issues that 0.5.3 and earlier left open.

**If you update from 0.5.3, check these first:**

- **Update every NetApiFlow package to 0.6.0 together.** Each package now asks for `NetApiFlow` at exactly its own version. NuGet gives `NU1605` (an error) when the core is older than a package, and `NU1608` (a warning) when it is newer.
- **`NetApiFlow.NetApiFlow` is now `Flow`** (`Flow.Step(...)`), and `UseNetApiFlowDashboard` and `UseNetApiFlowMcp` are now `MapNetApiFlowDashboard` and `MapNetApiFlowMcp`. The old names still work and warn with `CS0618`.
- **A SQL step is named after its table, not its schema:** `SELECT Users`, not `SELECT dbo`. Traces you already stored keep the old name.
- **A Minimal API endpoint becomes an `Endpoint` step with no marking.** Set `CaptureMinimalApiEndpoints = false` to go back.
- **`Enabled = false` now keeps every step silent,** also for another `ActivityListener` on the `NetApiFlow` source, such as an OpenTelemetry pipeline.
- **The id of a trace is now random** and no longer the W3C trace id, which is in the tag `w3c.trace_id`. The `X-NetApiFlow-TraceId` header is sent only in Development unless you set `EmitTraceIdHeader = true`, and the Runner needs it.
- **In Development, the dashboard and the MCP endpoint refuse a `Host` that is not `localhost`, `127.0.0.1`, `[::1]` or a name in `AllowedHosts`.** Opening them by a LAN address or `host.docker.internal` needs that declaration.

**Security.** Four GitHub Security Advisories describe what 0.5.3 and earlier left open, and 0.6.0 fixes all four:

- Dashboard and MCP routes are served to anonymous callers when `RequireAuthorization()` and `AllowAnonymous()` are chained (GHSA-xxxx-xxxx-xxxx).
- Secrets in exception messages are exported to OpenTelemetry unredacted, and only partly redacted in the dashboard (GHSA-xxxx-xxxx-xxxx).
- The dashboard and the MCP endpoint accept requests from any web page when they are open, and the dashboard can be framed (GHSA-xxxx-xxxx-xxxx).
- The MCP tools of NetApiFlow are served on the MCP endpoint of the application, outside the access rule of the dashboard (GHSA-xxxx-xxxx-xxxx).

`net8.0` stops shipping in the first version published on or after 10 May 2027.

The full list of changes follows.
```

## O que a abertura deixa de fora, de propósito

- O que é novo (`WithoutNetApiFlowTracking()`, `TrackFlowByConvention()`, `AddNetApiFlowSource()`, `AutoCreateSchema`, o bloco da resposta do Runner): está em `### Added`, e uma abertura sobre o que **quebra ou muda** para quem atualiza não o lista.
- Os eventos que agora saem de uma fila de fundo, o momento em que uma requisição sem rota aparece e os `### Fixed` do painel: estão na seção.
- A severidade e o vetor de cada advisory: ficam no advisory, não no Release. O Release só aponta.
