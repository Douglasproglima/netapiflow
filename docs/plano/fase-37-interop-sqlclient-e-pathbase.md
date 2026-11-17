# Fase 37: interop SqlClient e `PathBase` real no Runner

Dois itens aditivos do `roadmap-valor.md`, triados pela Planejadora (D-142): ambos pedem campo ou
valor novo em API pública já congelada em `docs/plano/contratos.md` nesta mesma entrada, nenhum
precisa de ADR novo porque reaplicam uma decisão já tomada (ADR-0018) ou são só um valor novo lido
em tempo de requisição, não uma decisão de arquitetura. MINOR, sem breaking change. Cada tarefa é
um commit.

## Tarefa 37.1: `ExternalActivitySource.SqlClient`, interop com Microsoft.Data.SqlClient (item 8)

Mesmo problema que o ADR-0018 já resolveu pro lado PostgreSQL (Fase 19, R-06): código misto EF
Core mais Dapper sobre SQL Server perde o nó `Database` na parte em Dapper, porque só o
interceptor do `NetApiFlow.EntityFrameworkCore` gera span, nunca o acesso via Dapper puro.
`Microsoft.Data.SqlClient` emite sua própria `ActivitySource` nativamente (mesma premissa já
confirmada pro Npgsql na investigação da Fase 19, D-91: não precisa de pacote nem configuração
extra, só escutar).

`ExternalActivitySource` ganha `SqlClient = 4` (contrato já congelado nesta entrada, em
`docs/plano/contratos.md`). Implementação espelha exatamente `NetApiFlow.Npgsql`
(`ExternalActivitySource.Npgsql`, ADR-0018): escuta a `ActivitySource` nativa do
`Microsoft.Data.SqlClient`, mapeia pra `ComponentType.Database`, deduplica contra o span do
`NetApiFlow.EntityFrameworkCore` pra o mesmo comando via `Activity.Parent` (mesmo mecanismo,
mesmo teste de dedup sob condição adversa que a Fase 19 já tem, adaptado pro driver novo). Sem
pacote novo: entra como mais uma flag do núcleo, assim como `Npgsql` também é só uma flag, sem
pacote próprio.

**Pronto quando:** teste de integração em `NetApiFlow.IntegrationTests` ou projeto equivalente
(Testcontainers, SQL Server real) confirma span `Database` gerado por uma consulta Dapper pura
(sem EF Core no caminho) quando `ExternalSources` inclui `SqlClient`, e confirma dedup (span único,
não duplicado) quando a mesma consulta passa pelo `NetApiFlow.EntityFrameworkCore`.

## Tarefa 37.2: `hostPathBase` em `GET /config`, URL real do endpoint no Runner (item 1)

Contrato já congelado nesta entrada: `GET /config` ganha `hostPathBase` (string, vazia quando o
host não usa `PathBase`), lido de `HttpContext.Request.PathBase` no momento da própria chamada.
Hoje o Runner monta a URL do endpoint do usuário a partir do `<base href>` combinado
(`{PathBase}{DashboardPath}/`), herdando um `/flow` (ou qualquer que seja o `DashboardPath`)
indevido quando o host não usa `PathBase` nenhum (D-126; metade da tarefa 32.11 que tentou
resolver isso foi revertida de propósito, por trocar um bug raro por um bug comum). Com
`hostPathBase` disponível via `GET /config`, o Runner monta a URL final como
`{origin}{hostPathBase}{caminho do OpenAPI}`, nunca a partir do `<base href>` do próprio dashboard.

**Pronto quando:** teste de integração confirma `hostPathBase` vazio sem `UsePathBase`, e o valor
configurado (ex. `/app`) quando o host usa `UsePathBase("/app")`. Spec do Angular confirma a URL
final montada sem o `DashboardPath` do dashboard embutido, com e sem `PathBase` configurado no
host, cobrindo o caso que a 32.11 deixou em aberto.

## Fora de escopo

- Qualquer outro campo de `ExternalActivitySource` ou combinação de drivers além de SqlClient: o
  achado é específico de Dapper sobre SQL Server (item 8), confirmado que não afeta nenhum outro
  storage nem driver.
- Mudar o `<base href>` em si, ou qualquer outra rota além da montagem de URL do Runner: o restante
  do dashboard já resolve corretamente contra o `<base href>` combinado, o bug é só no Runner
  (D-126).

## Aceite da fase

`dotnet build NetApiFlow.slnx -c Release` e `dotnet format NetApiFlow.slnx --verify-no-changes`
limpos nos dois TFMs. `npm run lint`, `npm run format:check`, `npm run test:ci` e `npm run build`
limpos no `ClientApp`. Suíte completa (`.NET`, incluindo Testcontainers de SQL Server; Angular)
verde. `dotnet pack NetApiFlow.slnx -c Release` conferido, sem pacote novo (os dois itens entram
no núcleo e no `NetApiFlow.Dashboard` já existentes).

## Ao terminar

Resumo pra Planejadora: o que foi feito, como verificar (comandos exatos), o que ficou pendente.
