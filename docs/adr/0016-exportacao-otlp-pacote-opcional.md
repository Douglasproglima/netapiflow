# ADR-0016: Exportação OTLP como pacote opcional, sobre a própria `ActivitySource`

## Status
Aceito

## Discussion
[#188](https://github.com/Douglasproglima/netapiflow/discussions/188)

## Contexto
D-58 dividiu a interop com OpenTelemetry em dois itens: ingestão (1a, entregue na Fase 8,
ADR-0012) e exportação OTLP (1b). D-79 ranqueou a exportação OTLP como item de maior peso do
roadmap pós `0.1.0`, por fechar a lacuna estratégica que o próprio ADR-0002 já previa: o mercado
.NET converge para OpenTelemetry, e hoje o NetApiFlow não entrega trace nenhum para um backend
OTel (Jaeger, Tempo, Aspire Dashboard, Honeycomb, qualquer coletor compatível).

ADR-0002 já registrou que o núcleo usa `System.Diagnostics.ActivitySource`/`Activity` nativos da
BCL, sem depender do SDK do OpenTelemetry, e que múltiplos `ActivityListener` podem coexistir na
mesma fonte sem conflito. Isso significa que cada span que o NetApiFlow já cria é, ao mesmo tempo,
uma `Activity` real na fonte `"NetApiFlow"`. O SDK do OpenTelemetry sabe processar e exportar
qualquer `Activity` de qualquer `ActivitySource` que ele seja instruído a observar, via
`TracerProviderBuilder.AddSource(string)`. Não existe nenhuma serialização própria a escrever: o
trabalho é registrar o SDK, apontar `AddSource("NetApiFlow")` e configurar um exportador.

Pesquisa feita antes desta ADR (mesma régua de `.claude/rules/dependencies.md` usada para o SDK
do MCP em D-72): `OpenTelemetry.Exporter.OpenTelemetryProtocol` e `OpenTelemetry.Extensions.Hosting`,
mantidos pelo próprio projeto OpenTelemetry, Apache-2.0 (compatível com MIT), `net8.0`+, versão
`1.20.1` (2026-10-20), ambos sem vulnerabilidade conhecida.

Efeito colateral real, que precisa ser documentado, não tratado como bug: ligar o `TracerProvider`
do SDK do OTel na fonte `"NetApiFlow"` ativa o sampler padrão do próprio SDK (always-on) para
decidir se uma `Activity` é processada pelo pipeline de exportação. Isso é um voto independente do
`FlowActivityListener.Sample()` (ADR-0002, decide se a `Activity` vira `FlowSpan` no dashboard). Os
dois podem divergir: uma requisição que o NetApiFlow descarta cedo (tracing desligado, limite de
500 traces atingido) ainda pode ser processada e exportada pelo SDK do OTel, porque o SDK não
pergunta nada ao `FlowActivityListener` antes de decidir. Nenhum dos dois pipelines bloqueia o
outro, mas o volume de export pode ser maior do que o volume de traces visíveis no dashboard.

## Decisão
Novo pacote opcional, `NetApiFlow.OpenTelemetry` (ID confirmado livre no nuget.org), com
dependência do SDK (`OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Exporter.OpenTelemetryProtocol`),
no mesmo molde dos pacotes de storage e do MCP: o núcleo (`NetApiFlow`) continua sem nenhuma
dependência nova.

API pública mínima, um único método de extensão:

```csharp
namespace NetApiFlow.OpenTelemetry;

public static class NetApiFlowOpenTelemetryServiceCollectionExtensions
{
    public static IServiceCollection AddNetApiFlowOtlpExport(
        this IServiceCollection services,
        Action<NetApiFlowOtlpExportOptions>? configure = null);
}

public sealed class NetApiFlowOtlpExportOptions
{
    /// <summary>Also observe "System.Net.Http", mirroring NetApiFlowOptions.ExternalSources
    /// (ADR-0012), so the exported trace has the same outbound HTTP spans the dashboard shows.
    /// Default: false.</summary>
    public bool IncludeHttpClientSource { get; set; }

    /// <summary>Forwarded as-is to AddOtlpExporter. Default: the SDK's own default
    /// (http://localhost:4317).</summary>
    public Action<OtlpExporterOptions>? ConfigureOtlpExporter { get; set; }
}
```

`AddNetApiFlowOtlpExport` registra `AddOpenTelemetry().WithTracing(builder => builder
.AddSource("NetApiFlow")` e, se `IncludeHttpClientSource` for `true`, também
`.AddSource("System.Net.Http")`, antes de `.AddOtlpExporter(...)`. Não adiciona nenhuma outra
fonte por padrão: cada ecossistema que o usuário já instrumenta (Npgsql, MassTransit, outra
biblioteca) é escolha explícita dele via `ConfigureOtlpExporter`/configuração própria do SDK do
OTel, não responsabilidade deste pacote.

`IncludeHttpClientSource` existe para resolver a paridade com o dashboard sem duplicar a decisão
de sanitização: a `Activity` nativa do `HttpClient` já chega sanitizada pelo próprio runtime (não
passa pelo `OutboundUrlSanitizer` do NetApiFlow, que só atua no `FlowSpan`), então habilitar essa
fonte aqui pode exportar a URL de saída inteira, inclusive query string, para o backend OTel. Essa
diferença (sanitizado no dashboard, não sanitizado na exportação) é registrada como risco R-16,
não escondida.

O README perde a frase "no OTLP export" do non-goals assim que este pacote for aceito e mergeado,
tarefa do plano de fase, não ajuste solto.

## Consequências
- Segundo incremento real da interop OTel (D-58), primeiro de maior peso estratégico (D-79): o
  NetApiFlow passa a alimentar qualquer backend compatível com OTLP, sem nenhuma mudança no
  núcleo nem no dashboard.
- Nenhuma mudança em `ComponentType`, `FlowSpan`, `ITraceStore` ou em qualquer contrato de
  `docs/plano/contratos.md` além da adição deste pacote: a exportação lê a mesma `Activity` que já
  existe, não duplica o modelo de domínio.
- Novo risco R-16, registrado nesta ADR: `IncludeHttpClientSource = true` pode exportar query
  string de URL de saída para um backend OTel externo, fora do controle do NetApiFlow. Mitigação:
  opt-in explícito (`false` por padrão) e aviso no XML doc do próprio `IncludeHttpClientSource`.
- Divergência documentada, não corrigida: volume exportado ao OTel pode ser maior que o volume
  visível no dashboard, porque os dois pipelines de amostragem (`FlowActivityListener.Sample()` e
  o sampler do SDK do OTel) decidem de forma independente. Vira nota no README, seção de
  comportamento esperado, não "limitação conhecida" (não é um bug, é a arquitetura de dois
  `ActivityListener` coexistindo, já prevista no ADR-0002).
