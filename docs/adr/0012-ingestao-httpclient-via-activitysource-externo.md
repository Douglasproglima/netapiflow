# ADR-0012: Ingestão da `ActivitySource` nativa do `HttpClient`, opt-in, sem exportação OTLP

## Status
Aceito

## Issue
[#83](https://github.com/Douglasproglima/netapiflow/issues/83)

## Contexto
D-58 registrou interop com OpenTelemetry como prioridade 1 do roadmap pós `0.1.0`, dividido em
ingestão (1a) e exportação OTLP (1b). O ASP.NET Core e o `HttpClient` já propagam `Activity` via
`System.Diagnostics.ActivitySource` nativo da BCL, sem depender do SDK do OpenTelemetry: o
`HttpClientHandler` cria uma `Activity` na fonte `"System.Net.Http"` para cada requisição de
saída, com o mesmo `TraceId` da `Activity` HTTP de entrada (W3C `traceparent`). O
`FlowActivityListener` (ADR-0002) já escuta `ActivitySource` para capturar as próprias etapas do
NetApiFlow; estender `ShouldListenTo` para incluir outras fontes conhecidas é aditivo, não
contradiz ADR-0002.

Três fontes foram cogitadas para o primeiro incremento: `HttpClient`, `Npgsql` e MassTransit. Só
`HttpClient` entra nesta ADR. `Npgsql` fica de fora porque `NetApiFlow.EntityFrameworkCore` já
produz um span `Database` para a mesma chamada via `FlowDbCommandInterceptor`
(`NetApiFlow.Step(...)`, uma `Activity` própria na fonte `"NetApiFlow"`); escutar a `Activity`
nativa do Npgsql ao mesmo tempo duplicaria a duração de cada comando SQL no waterfall. Esse risco
já está registrado como R-06 (`docs/plano/riscos.md`) desde o bootstrap do projeto, sem mitigação
implementada, e dedupe por `Activity.Id` não funciona de forma direta aqui porque são duas
`Activity` distintas, de fontes diferentes, para a mesma operação lógica. Resolver isso de verdade
precisa de investigação própria (como as duas `Activity` se relacionam na prática, `Parent` ou
timing) e vira uma ADR separada, não uma tarefa acoplada a esta fase. MassTransit fica de fora por
falta de pesquisa própria: nome da fonte e formato das tags ainda não foram confirmados contra o
pacote real.

`HttpClient` tem zero sobreposição com instrumentação própria existente (não há hoje nenhum
interceptor de chamada HTTP de saída no NetApiFlow), e `ComponentType.Http` já existe na API
pública e já é renderizado pelo dashboard Angular (`component-type-icon.ts`) desde a Fase 2. Fatia
menor e de risco mais baixo para o primeiro incremento real da interop OTel.

## Decisão
`NetApiFlowOptions` ganha a propriedade `ExternalSources`, do tipo `[Flags] enum
ExternalActivitySource { None = 0, HttpClient = 1 }`, com `None` como padrão (opt-in explícito,
não automático). Quando `ExternalSources` tem o flag `HttpClient`, `FlowActivityListener` passa a
escutar também a fonte `"System.Net.Http"` e mapeia cada `Activity` de saída para um `FlowSpan`
com `Component = "HttpClient"`, `Type = ComponentType.Http`, `Operation = "{método} {host}{path}"`.

A URL de saída nunca é gravada inteira (ADR-0007 continua valendo para fonte externa igual para a
própria): só `scheme://host[:porta]/path`, nunca query string nem fragment, truncado em 300
caracteres, igual ao limite já usado para mensagem de erro. Status do span vem do código de
resposta HTTP e de exceção de rede não tratada, nunca de corpo de requisição ou resposta.

Opt-in e não automático porque muda o formato do trace sem aviso (passa a aparecer etapa que
antes não existia) e porque a garantia de sanitização de uma fonte externa depende de nomes de tag
que podem variar entre versões do runtime, risco que não existe nas etapas que o próprio
NetApiFlow cria.

## Consequências
- Primeiro incremento real da interop OTel (D-58, D-59): ecossistema `HttpClient` passa a aparecer
  no grafo sem nenhum código manual do usuário, só a flag de configuração.
- Não resolve Npgsql nem mensageria. Registrado como pendência explícita, não lacuna esquecida:
  próxima ADR, quando houver investigação própria sobre deduplicação (Npgsql) ou sobre o formato
  de `Activity` do pacote (MassTransit).
- Nenhuma mudança na superfície do dashboard Angular: `ComponentType.Http` já é suportado. Nenhuma
  mudança no README sobre exportação OTLP: esta ADR é só ingestão, a promessa atual de "no OTLP
  export" permanece válida até a 1b do D-58 ser implementada.
