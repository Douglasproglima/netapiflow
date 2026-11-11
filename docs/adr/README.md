# ADRs do NetApiFlow

Prefixo `ADR-`. Modelo em [`0000-template.md`](0000-template.md). ADR novo começa como
`Proposto` e entra neste índice na mesma PR. ADR aceito não é editado para mudar a decisão:
uma decisão nova que o substitui vira outro ADR, com status `Substituído por ADR-YYYY` no
antigo.

Todo ADR aceito ganha uma Issue correspondente no GitHub (rastro público, comentário da
comunidade, sem reabrir a decisão sem justificativa técnica nova).

| ADR | Título | Status | Issue |
|---|---|---|---|
| [ADR-0001](0001-gitflow-e-semver.md) | Gitflow clássico com SemVer, não `VERSAO.ANOMES.RELEASE.FIX` | Aceito | [#61](https://github.com/Douglasproglima/netapiflow/issues/61) |
| [ADR-0002](0002-activitysource-nativo-sem-otel-sdk.md) | Captura via `ActivitySource`/`ActivityListener` nativos, sem SDK do OpenTelemetry | Aceito | [#62](https://github.com/Douglasproglima/netapiflow/issues/62) |
| [ADR-0003](0003-multi-targeting-net8-net10.md) | Multi-targeting `net8.0` e `net10.0` em todos os pacotes | Aceito | [#63](https://github.com/Douglasproglima/netapiflow/issues/63) |
| [ADR-0004](0004-storages-ado-net-direto.md) | Storages em ADO.NET direto, sem Dapper | Aceito | [#64](https://github.com/Douglasproglima/netapiflow/issues/64) |
| [ADR-0005](0005-dashboard-angular-embutido.md) | Dashboard Angular embutido no assembly como recurso | Aceito | [#65](https://github.com/Douglasproglima/netapiflow/issues/65) |
| [ADR-0006](0006-dashboard-restrito-a-development.md) | Dashboard acessível só em Development por padrão | Aceito | [#66](https://github.com/Douglasproglima/netapiflow/issues/66) |
| [ADR-0007](0007-nunca-capturar-payload.md) | Nunca capturar corpo, argumento, header ou SQL parametrizado | Aceito | [#71](https://github.com/Douglasproglima/netapiflow/issues/71) |
| [ADR-0008](0008-ferramentas-de-teste.md) | Shouldly para asserções, NSubstitute para mocks | Aceito | [#67](https://github.com/Douglasproglima/netapiflow/issues/67) |
| [ADR-0009](0009-identidade-visual-guia-de-marca.md) | Guia de marca v1 como referência canônica de identidade visual | Aceito | [#68](https://github.com/Douglasproglima/netapiflow/issues/68) |
| [ADR-0010](0010-layout-de-workspace-unico.md) | `/flow` vira um workspace denso, as demais rotas continuam existindo | Aceito | [#69](https://github.com/Douglasproglima/netapiflow/issues/69) |
| [ADR-0011](0011-i18n-em-tempo-de-execucao-e-tema-manual.md) | i18n do dashboard em tempo de execução, tema com alternador manual | Aceito | [#70](https://github.com/Douglasproglima/netapiflow/issues/70) |
| [ADR-0012](0012-ingestao-httpclient-via-activitysource-externo.md) | Ingestão da `ActivitySource` nativa do `HttpClient`, opt-in, sem exportação OTLP | Aceito | [#83](https://github.com/Douglasproglima/netapiflow/issues/83) |
| [ADR-0013](0013-servidor-mcp-somente-leitura.md) | Servidor MCP embutido (`NetApiFlow.Mcp`), só leitura, restrito a Development | Aceito | [#92](https://github.com/Douglasproglima/netapiflow/issues/92) |
| [ADR-0014](0014-impeccable-como-ferramenta-de-qualidade-visual.md) | Impeccable como guarda de qualidade visual do dashboard, ferramenta de dev only | Aceito | [#94](https://github.com/Douglasproglima/netapiflow/issues/94) |
| [ADR-0015](0015-filtro-de-acesso-unico-no-nucleo.md) | `IFlowAccessFilter` único no núcleo, compartilhado por Dashboard e MCP | Aceito | [#96](https://github.com/Douglasproglima/netapiflow/issues/96) |
| [ADR-0016](0016-exportacao-otlp-pacote-opcional.md) | Exportação OTLP (`NetApiFlow.OpenTelemetry`) como pacote opcional, sobre a própria `ActivitySource` | Aceito | [#110](https://github.com/Douglasproglima/netapiflow/issues/110) |
| [ADR-0017](0017-correlacao-de-log-e-excecao-por-etapa.md) | Correlação de log e exceção por etapa, via `ILoggerProvider` opt-in | Aceito | [#125](https://github.com/Douglasproglima/netapiflow/issues/125) |
| [ADR-0018](0018-ingestao-npgsql-dedup-por-parent.md) | Ingestão nativa da `ActivitySource` do Npgsql, dedup por cadeia de `Parent` | Aceito | [#140](https://github.com/Douglasproglima/netapiflow/issues/140) |
| [ADR-0019](0019-visibilidade-pre-pipeline.md) | Visibilidade de pré-pipeline: Authorization via `DiagnosticSource`, Resource via filtro nativo | Aceito | [#156](https://github.com/Douglasproglima/netapiflow/issues/156) |
