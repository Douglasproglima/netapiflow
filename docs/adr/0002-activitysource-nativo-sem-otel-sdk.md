# ADR-0002: Captura via `ActivitySource`/`ActivityListener` nativos, sem SDK do OpenTelemetry

## Status
Aceito

## Contexto
`netapiflow-prompt.md` (seção 4.1) exige que o pacote núcleo (`NetApiFlow`) não dependa do SDK do
OpenTelemetry. O .NET já expõe `System.Diagnostics.ActivitySource` e `ActivityListener` na BCL,
sem pacote externo, com suporte nativo a `Activity.Parent` fluindo por `AsyncLocal` através de
`async`/`await`, `Task.Run` e `Task.WhenAll`.

Alternativa considerada: usar `OpenTelemetry.Api` (não o SDK completo) só pela API de tracing.
Rejeitada porque adiciona uma dependência de terceiro no pacote que o prompt explicitamente
restringe a `FrameworkReference` para `Microsoft.AspNetCore.App`, e o NetApiFlow não precisa de
exportador OTLP, contexto W3C de propagação entre serviços nem amostragem estatística (não
objetivos, seção 2.1).

## Decisão
Núcleo usa só `ActivitySource`, `Activity` e `ActivityListener` da BCL. A `ActivitySource` própria
chama-se `"NetApiFlow"`. O `TraceId` do NetApiFlow é o `TraceId` da `Activity` HTTP que o ASP.NET
Core já cria por requisição.

## Consequências
- Zero dependência de terceiro no pacote núcleo, só framework reference.
- Compatível com aplicações que também usam OpenTelemetry de verdade: as duas podem escutar as
  mesmas `Activity`, sem conflito, porque `ActivityListener` suporta múltiplos ouvintes.
- Decisão de amostragem (`ActivityListener.Sample`) é responsabilidade do NetApiFlow, não herda
  nenhuma política de SDK externo (ver risco R-05 em `docs/plano/riscos.md`).
