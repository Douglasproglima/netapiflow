# Fase 66: o registro é idempotente, e o exportador OTLP não pega o que não é do NetApiFlow (D-236, B6, B7)

Dois problemas de quem integra o NetApiFlow numa aplicação que já tem sua própria configuração. B6: chamar um `Add*` duas
vezes dá resultado errado, e é comum (um método de extensão do usuário, um teste que chama de novo). B7: o exportador OTLP
muda o que a aplicação **inteira** exporta.

Vai no `0.6.0` (D-236). São defeitos antigos, sem regressão, então não são hotfix (D-205). Vem **depois da Fase 55**, que mexe no
pacote `NetApiFlow.OpenTelemetry` (a tag `netapiflow.trace_id`).

## Os achados e o que a Planejadora conferiu

| # | Achado | Conferi |
|---|---|---|
| B6a | `AddNetApiFlow` chamado duas vezes **descarta** o `configure` da segunda chamada | A auditoria confirmou `[conf]`. Não li o código |
| B6b | `AddNetApiFlowDashboard` registra o `IFlowEventListener` **sem `TryAdd`**: chamado duas vezes, cada evento SSE sai em dobro | **Código:** `services.AddSingleton<IFlowEventListener>(sp => ...)` em `NetApiFlowDashboardServiceCollectionExtensions.cs`, sem `TryAdd`. Confirmado pela auditoria `[conf]` |
| B6c | O mesmo vale para o MediatR (dois steps por chamada) e para o OpenTelemetry (dois exporters) | A auditoria confirmou `[conf]`. Não li o código |
| B7 | `AddNetApiFlowOtlpExport` pendura o exporter no `TracerProvider` **compartilhado**: quem já usa OpenTelemetry passa a exportar **todas as fontes dele** para esse exporter | A auditoria confirmou `[conf]`. Não li o código |

## Ponto de parada

- Se a regra "cada chamada aplica o seu `configure`" mudar o resultado para um app que chama `AddNetApiFlow` **uma vez** (o
  caso comum), para.
- O B7 acrescenta **uma extensão pública** (API nova, `NetApiFlow.OpenTelemetry`). Está autorizada pela auditoria. Nenhuma
  outra API pública.
- Se a extensão nova não conseguir exportar **só** a fonte do NetApiFlow, para e relata.

## Tarefa 66.0: os testes vermelhos, antes de corrigir

Host real, vistos falhando antes, `net8.0` e `net10.0`:

1. **B6a.** Duas chamadas a `AddNetApiFlow` com `configure` diferentes: as **duas** valem, na ordem das chamadas. Hoje: a
   segunda some.
2. **B6b.** `AddNetApiFlowDashboard` duas vezes: **um** listener, e um evento SSE chega **uma** vez a um cliente.
3. **B6c.** `AddNetApiFlowMediatR()` duas vezes: **um** step por requisição. `AddNetApiFlowOtlpExport` duas vezes: **um**
   exporter (contar os exporters registrados).
4. **B7.** Um app com OpenTelemetry próprio e uma fonte `MinhaFonte`: com `AddNetApiFlowOtlpExport`, hoje os spans de
   `MinhaFonte` chegam ao exporter do NetApiFlow. O esperado com a extensão nova: **só** os spans do NetApiFlow.

## Tarefa 66.1: o registro idempotente (B6)

- `AddNetApiFlow`: o `configure` de **cada** chamada é aplicado (acumula, na ordem). Os registros de serviço usam `TryAdd`.
- `AddNetApiFlowDashboard`, `AddNetApiFlowMediatR` e o do OpenTelemetry: `TryAdd` ou `TryAddEnumerable`, de modo que a segunda
  chamada não duplica nada.

## Tarefa 66.2: a extensão que só adiciona a fonte (B7)

Uma extensão de `TracerProviderBuilder` em `NetApiFlow.OpenTelemetry`, que só faz `AddSource("NetApiFlow")` (o nome da
extensão fica com a Executora, proposta: `AddNetApiFlowSource`). O `AddNetApiFlowOtlpExport` continua existindo, sem mudar o que
faz hoje. **O README diz quando usar cada forma**: a extensão nova para quem **já** configura o OpenTelemetry, o
`AddNetApiFlowOtlpExport` para quem quer só exportar o NetApiFlow sem montar o resto.

## Tarefa 66.3: contrato, README e CHANGELOG

- `contratos.md`: a Planejadora atualiza com a assinatura quando a 66.0 fecha.
- `README.md` e `README.pt-BR.md`: quando usar cada forma, e que as chamadas repetidas são seguras.
- `CHANGELOG.md`, em inglês: `### Fixed` (as chamadas repetidas), `### Added` (a extensão).

## Resultado da fase (PR #473, 24/11/2026)

Entregue em `develop` (`be11734`, head `4695067`) e fechada pela auditoria.

- **B6a:** `AddNetApiFlow` aplica o `configure` de cada chamada, na ordem. Os serviços seguem registrados uma vez, atrás do marcador que já existia.
- **B6b:** `AddNetApiFlowDashboard` usa `TryAddSingleton<SseBroadcaster>` e `TryAddEnumerable` para o `IFlowEventListener`.
- **B6c:** `AddNetApiFlowMediatR` usa `TryAddEnumerable` para o comportamento genérico aberto. Uma segunda chamada de `AddNetApiFlowOtlpExport` não acrescenta um segundo exportador, e cada chamada aplica o seu `configure` a um único `NetApiFlowOtlpExportOptions`. O `IncludeHttpClientSource` passou a ser lido quando o provider é montado (`IDeferredTracerProviderBuilder.Configure`). Para uma chamada só, o resultado é o de antes.
- **B7:** a extensão pública `NetApiFlowTracerProviderBuilderExtensions.AddNetApiFlowSource(this TracerProviderBuilder)` só faz `AddSource("NetApiFlow")`. É a única API pública acrescentada, e está em `PublicAPI.Unshipped.txt` e no `contratos.md`. O `AddNetApiFlowOtlpExport` não mudou o que exporta, e o README avisa, em negrito e nos dois idiomas, que num app com OpenTelemetry próprio ele manda todas as fontes do app para o endpoint do NetApiFlow.
- **Vistos falhando antes (corpo da PR e logs do CI):** a run 44983812709 (B6a, B6b e B6c do MediatR, 3 falhas por TFM) e a run 44984088082 (B6c do OTLP, 1 por TFM). O B7 nasceu verde, porque a API é nova, então a prova foi por mutação: a run 44984788820 (a extensão com um exportador a mais) e a run 44985080165 (com a fonte `System.Net.Http` a mais, que derrubou também o `IncludeHttpClientSourceTests`). O guarda caiu nas duas. O comportamento documentado que fica, `AddNetApiFlowOtlpExport_OnAnAppWithItsOwnTracing_ExportsTheSourcesOfTheAppToo`, é verde antes e depois e não tem linha no `guards.json`, porque não é defeito corrigido.
- **O registro de guardas** ganhou cinco linhas: `B6a-add-netapiflow-twice`, `B6b-dashboard-twice`, `B6c-mediatr-twice`, `B6c-otlp-export-twice` e `B7-netapiflow-source-only`. Nenhum teste existente mudou.
- **Com o pacote real,** por relato da auditoria: duas chamadas de exportação mandavam cada etapa duas vezes na `0.5.3`, e na PR saem iguais a uma chamada, uma chamada única dá o mesmo que no `develop`, e com `AddNetApiFlowSource` só o exportador do app recebe os dados. O revisor independente levantou que um serviço registrado com chave quebraria o `AddNetApiFlowOtlpExport` no .NET 8, e a auditoria testou nos runtimes 8.0.34 e 10.0.15: não quebra, e não é achado.
- **README:** as chamadas repetidas são seguras, e o `using NetApiFlow.OpenTelemetry;` que faltava no bloco do OTLP entrou, nos dois idiomas. A Executora relata os blocos colados na ordem do leitor em dois apps novos, com 0 avisos e 0 erros.

## Fora de escopo

- Mudar o nome ou o comportamento do `AddNetApiFlowOtlpExport`.
- O atributo `sql` do OTLP virar `db.query.text` (0.7.0, proposto pela Planejadora no D-236).

## Critério que vale para a fase inteira

- Build, testes e formato limpos nos dois TFMs. Os quatro grupos de teste **vistos falhando antes**.
- Um app com chamadas únicas se comporta **exatamente** como antes (a suíte inteira passa sem edição).

## Versionamento e fluxo

- `0.6.0`. Branch `bugfix/66-registro-idempotente` a partir de `develop` atualizado, **depois** da Fase 55.
- Commits pequenos, em inglês, uma linha, sem linha de coautor. Abrir a PR e parar.

## Relatório final

Para cada achado: o teste, a saída dele falhando antes e passando depois. A assinatura da extensão nova.
