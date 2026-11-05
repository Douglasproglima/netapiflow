# Fase 13: Exportação OTLP (`NetApiFlow.OpenTelemetry`)

Projeto novo: `src/NetApiFlow.OpenTelemetry`. Testes: `tests/NetApiFlow.OpenTelemetry.Tests`
(novo projeto, mesmo molde dos outros pacotes opcionais).

Pré-requisito: Fase 1 aceita (`ActivitySource` do núcleo, ADR-0002) e Fase 8 aceita (ADR-0012,
`ExternalActivitySource.HttpClient`, reaproveitado pela opção `IncludeHttpClientSource` desta
fase). Item 1 do ranking de valor de entrega (D-79), item 1b do roadmap D-58.

Contrato congelado em `docs/plano/contratos.md`: `AddNetApiFlowOtlpExport`,
`NetApiFlowOtlpExportOptions`. Decisão de arquitetura (pacote de wiring sobre o SDK do OTel, sem
serialização própria) travada em ADR-0016, não renegociável dentro desta fase.

## Checagens antes de começar

- ID de pacote `NetApiFlow.OpenTelemetry` já confirmado livre no nuget.org pela Planejadora
  (2026-11-05, `v3-flatcontainer`, 404). Não precisa reconferir.
- `OpenTelemetry.Extensions.Hosting` e `OpenTelemetry.Exporter.OpenTelemetryProtocol` já
  verificados pela Planejadora: Apache-2.0, `net8.0`+, versão `1.20.1` (2026-10-20). Adicionar em
  `Directory.Packages.props`, versão centralizada, igual a todo outro pacote.
- Ler ADR-0002 antes de começar: a garantia de que múltiplos `ActivityListener`/`TracerProvider`
  coexistem na mesma `ActivitySource` sem conflito é a base técnica desta fase inteira. Esta fase
  não adiciona um segundo mecanismo de captura, só liga o SDK do OTel ao que já existe.

## Contexto (ler antes de implementar)

O núcleo já produz `Activity` reais na fonte `"NetApiFlow"` (ADR-0002). O SDK do OpenTelemetry
sabe observar e exportar qualquer `Activity` de qualquer `ActivitySource`, via
`TracerProviderBuilder.AddSource(string)`. Não existe nenhum dado a serializar à mão: o trabalho
desta fase é registrar o SDK, apontá-lo para a fonte certa e configurar o exportador OTLP.

Efeito colateral que precisa ser documentado, não corrigido: o sampler do SDK do OTel (always-on
por padrão) decide, de forma independente do `FlowActivityListener.Sample()` do núcleo, se uma
`Activity` é processada pelo pipeline de exportação. As duas decisões podem divergir (uma
requisição que o NetApiFlow descarta cedo ainda pode ser exportada). Ver ADR-0016, seção de
consequências.

## Fora de escopo, de propósito (ADR-0016)

- Nenhuma outra fonte além de `"NetApiFlow"` e, opcionalmente, `"System.Net.Http"`. Observar
  Npgsql, MassTransit ou qualquer outro ecossistema é escolha do próprio usuário, configurada
  fora deste pacote (ele já tem o `TracerProviderBuilder` em mãos via `ConfigureOtlpExporter` ou
  configuração própria do SDK).
- Nenhuma mudança em `ComponentType`, `FlowSpan`, `ITraceStore` ou em qualquer rota REST.
- Nenhuma métrica nem log via OTel nesta fase, só trace (`WithTracing`). Métricas/logs via OTel
  ficam para uma fase própria se houver demanda real.

## Tarefas

### 13.1: Scaffold do projeto `NetApiFlow.OpenTelemetry`

Metadados obrigatórios de `.claude/rules/packaging.md` (`PackageId=NetApiFlow.OpenTelemetry`,
`PackageReadmeFile`, ícone, tags, `RepositoryUrl`, `PackageLicenseExpression=MIT`, herdados de
`Directory.Build.props` onde já se aplica). Depende de `NetApiFlow` (núcleo, `ProjectReference`),
`OpenTelemetry.Extensions.Hosting` e `OpenTelemetry.Exporter.OpenTelemetryProtocol`
(`PackageReference`). **Não** depende de `NetApiFlow.Dashboard` nem `NetApiFlow.Mcp`.

**Pronto quando:** `dotnet build NetApiFlow.slnx` inclui o projeto novo nos dois TFMs,
`dotnet pack` gera `.nupkg`/`.snupkg` para ele junto dos outros sete.

### 13.2: `AddNetApiFlowOtlpExport`

Registra `services.AddOpenTelemetry().WithTracing(builder => builder.AddSource("NetApiFlow")
.AddOtlpExporter(configureOptions ?? defaultOptions))`. Quando
`NetApiFlowOtlpExportOptions.IncludeHttpClientSource` é `true`, adiciona também
`.AddSource("System.Net.Http")` antes do exportador. `ConfigureOtlpExporter`, se fornecido, é
repassado direto ao `AddOtlpExporter` do SDK (endpoint, protocolo, headers ficam por conta de
quem configura, não reimplementados aqui).

**Pronto quando:** teste de integração confirma que `AddNetApiFlowOtlpExport()` sem nenhuma
configuração registra um `TracerProvider` observando `"NetApiFlow"`, e que uma `Activity` real
dessa fonte é capturada por um exportador de teste (`InMemoryExporter` ou equivalente do SDK, sem
subir um coletor OTLP de verdade para o teste unitário).

### 13.3: `IncludeHttpClientSource` e a diferença de sanitização (prova R-16)

Quando `IncludeHttpClientSource = true`, a `Activity` nativa de `"System.Net.Http"` é exportada
como está, sem passar pelo `OutboundUrlSanitizer` do núcleo (que só sanitiza o `FlowSpan`, não a
`Activity` nativa compartilhada). XML doc do `IncludeHttpClientSource` avisa isso explicitamente.

**Pronto quando:** teste de integração com uma URL de teste contendo query string sensível,
`IncludeHttpClientSource = true`, confirma que a `Activity` exportada (capturada pelo exportador
de teste) contém a URL completa, **documentando o comportamento esperado**, não corrigindo-o.
Nome do teste deixa isso explícito (ex.
`OutboundCall_WithIncludeHttpClientSourceEnabled_ExportsFullUrlWithoutSanitization`). Mitiga R-16
pela documentação e pelo padrão `false`, não por código que sanitiza.

### 13.4: Amostra e verificação manual

`samples/SampleApi.Controllers` (ou outro sample) ganha `AddNetApiFlowOtlpExport()` atrás de uma
flag de configuração fácil de desligar (o sample não deve depender de um coletor OTLP rodando
para funcionar normalmente). Nota no `README` do sample de como apontar um coletor OTLP real
(Aspire Dashboard ou `otel/opentelemetry-collector` via Docker) para ver os traces chegando.

**Pronto quando:** rodar o sample com um coletor OTLP real escutando em `localhost:4317` mostra
os traces do NetApiFlow chegando no coletor, com os mesmos `TraceId` que aparecem no dashboard.
Print ou transcrição do coletor recebendo no resumo da fase.

### 13.5: Documentação, non-goals do README

README e README.pt-BR.md ganham uma seção curta sobre a exportação OTLP: o que é, como instalar
o pacote, como configurar `IncludeHttpClientSource` e o aviso de sanitização do 13.3, e uma nota
sobre a divergência de amostragem entre o dashboard e o que é exportado (ADR-0016). A frase "no
OTLP export" sai do non-goals dos dois READMEs.

**Pronto quando:** README e README.pt-BR.md atualizados, conteúdo equivalente, frase antiga
removida, nenhuma reintrodução acidental em outro trecho do arquivo.

### 13.6: CHANGELOG

`CHANGELOG.md`, seção `[Unreleased]` → `### Added`, entrada curta para o pacote novo, mesmo
padrão das entradas de Fase 8/9/10.

**Pronto quando:** entrada adicionada, em ordem cronológica junto das outras pendentes.

## Aceite da fase

Todos os critérios de pronto acima. R-16 registrado e provado (documentado, não corrigido por
código, conforme ADR-0016). `dotnet pack` continua gerando 8 pacotes (os sete existentes mais
`NetApiFlow.OpenTelemetry`), todos com `.snupkg`. Nenhuma mudança em `NetApiFlow` (núcleo) além
do que já existe hoje: este pacote só consome a `ActivitySource` pública que o núcleo já expõe.

## Ao terminar

Resumo para a Planejadora: o que foi feito, como verificar (comando exato, incluindo como subir
um coletor OTLP local para a verificação manual do 13.4), qual exportador/versão exata do SDK foi
usada (caso tenha divergido do que a Planejadora verificou nesta revisão), o que ficou pendente.
A Planejadora roda `/revisar-fase 13` antes de decidir a próxima fatia do roadmap
(`docs/plano/roadmap-valor.md`).
