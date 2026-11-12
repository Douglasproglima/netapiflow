# ADRs do NetApiFlow

Prefixo `ADR-`. Modelo em [`0000-template.md`](0000-template.md). ADR novo começa como
`Proposto` e entra neste índice na mesma PR. ADR aceito não é editado para mudar a decisão:
uma decisão nova que o substitui vira outro ADR, com status `Substituído por ADR-YYYY` no
antigo.

Todo ADR aceito ganha uma Discussion correspondente no GitHub, categoria "Architecture
Decisions" (rastro público, comentário da comunidade, sem reabrir a decisão sem justificativa
técnica nova). Até D-62, esse rastro era uma Issue; migrado para Discussion por aderência à
prática mais comum de projetos open source (D-110): Issue sinaliza pendência a fechar,
Discussion é o lugar certo pra uma conversa permanente.

| ADR | Título | Status | Discussion |
|---|---|---|---|
| [ADR-0001](0001-gitflow-e-semver.md) | Gitflow clássico com SemVer, não `VERSAO.ANOMES.RELEASE.FIX` | Aceito | [#173](https://github.com/Douglasproglima/netapiflow/discussions/173) |
| [ADR-0002](0002-activitysource-nativo-sem-otel-sdk.md) | Captura via `ActivitySource`/`ActivityListener` nativos, sem SDK do OpenTelemetry | Aceito | [#174](https://github.com/Douglasproglima/netapiflow/discussions/174) |
| [ADR-0003](0003-multi-targeting-net8-net10.md) | Multi-targeting `net8.0` e `net10.0` em todos os pacotes | Aceito | [#175](https://github.com/Douglasproglima/netapiflow/discussions/175) |
| [ADR-0004](0004-storages-ado-net-direto.md) | Storages em ADO.NET direto, sem Dapper | Aceito | [#176](https://github.com/Douglasproglima/netapiflow/discussions/176) |
| [ADR-0005](0005-dashboard-angular-embutido.md) | Dashboard Angular embutido no assembly como recurso | Aceito | [#177](https://github.com/Douglasproglima/netapiflow/discussions/177) |
| [ADR-0006](0006-dashboard-restrito-a-development.md) | Dashboard acessível só em Development por padrão | Aceito | [#178](https://github.com/Douglasproglima/netapiflow/discussions/178) |
| [ADR-0007](0007-nunca-capturar-payload.md) | Nunca capturar corpo, argumento, header ou SQL parametrizado | Aceito | [#183](https://github.com/Douglasproglima/netapiflow/discussions/183) |
| [ADR-0008](0008-ferramentas-de-teste.md) | Shouldly para asserções, NSubstitute para mocks | Aceito | [#179](https://github.com/Douglasproglima/netapiflow/discussions/179) |
| [ADR-0009](0009-identidade-visual-guia-de-marca.md) | Guia de marca v1 como referência canônica de identidade visual | Aceito | [#180](https://github.com/Douglasproglima/netapiflow/discussions/180) |
| [ADR-0010](0010-layout-de-workspace-unico.md) | `/flow` vira um workspace denso, as demais rotas continuam existindo | Aceito | [#181](https://github.com/Douglasproglima/netapiflow/discussions/181) |
| [ADR-0011](0011-i18n-em-tempo-de-execucao-e-tema-manual.md) | i18n do dashboard em tempo de execução, tema com alternador manual | Aceito | [#182](https://github.com/Douglasproglima/netapiflow/discussions/182) |
| [ADR-0012](0012-ingestao-httpclient-via-activitysource-externo.md) | Ingestão da `ActivitySource` nativa do `HttpClient`, opt-in, sem exportação OTLP | Aceito | [#184](https://github.com/Douglasproglima/netapiflow/discussions/184) |
| [ADR-0013](0013-servidor-mcp-somente-leitura.md) | Servidor MCP embutido (`NetApiFlow.Mcp`), só leitura, restrito a Development | Aceito | [#185](https://github.com/Douglasproglima/netapiflow/discussions/185) |
| [ADR-0014](0014-impeccable-como-ferramenta-de-qualidade-visual.md) | Impeccable como guarda de qualidade visual do dashboard, ferramenta de dev only | Aceito | [#186](https://github.com/Douglasproglima/netapiflow/discussions/186) |
| [ADR-0015](0015-filtro-de-acesso-unico-no-nucleo.md) | `IFlowAccessFilter` único no núcleo, compartilhado por Dashboard e MCP | Aceito | [#187](https://github.com/Douglasproglima/netapiflow/discussions/187) |
| [ADR-0016](0016-exportacao-otlp-pacote-opcional.md) | Exportação OTLP (`NetApiFlow.OpenTelemetry`) como pacote opcional, sobre a própria `ActivitySource` | Aceito | [#188](https://github.com/Douglasproglima/netapiflow/discussions/188) |
| [ADR-0017](0017-correlacao-de-log-e-excecao-por-etapa.md) | Correlação de log e exceção por etapa, via `ILoggerProvider` opt-in | Aceito | [#189](https://github.com/Douglasproglima/netapiflow/discussions/189) |
| [ADR-0018](0018-ingestao-npgsql-dedup-por-parent.md) | Ingestão nativa da `ActivitySource` do Npgsql, dedup por cadeia de `Parent` | Aceito | [#190](https://github.com/Douglasproglima/netapiflow/discussions/190) |
| [ADR-0019](0019-visibilidade-pre-pipeline.md) | Visibilidade de pré-pipeline: Authorization via `DiagnosticSource`, Resource via filtro nativo | Aceito | [#191](https://github.com/Douglasproglima/netapiflow/discussions/191) |
