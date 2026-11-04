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
