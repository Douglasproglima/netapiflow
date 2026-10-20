# ADRs do NetApiFlow

Prefixo `ADR-`. Modelo em [`0000-template.md`](0000-template.md). ADR novo começa como
`Proposto` e entra neste índice na mesma PR. ADR aceito não é editado para mudar a decisão:
uma decisão nova que o substitui vira outro ADR, com status `Substituído por ADR-YYYY` no
antigo.

| ADR | Título | Status |
|---|---|---|
| [ADR-0001](0001-gitflow-e-semver.md) | Gitflow clássico com SemVer, não `VERSAO.ANOMES.RELEASE.FIX` | Aceito |
| [ADR-0002](0002-activitysource-nativo-sem-otel-sdk.md) | Captura via `ActivitySource`/`ActivityListener` nativos, sem SDK do OpenTelemetry | Aceito |
| [ADR-0003](0003-multi-targeting-net8-net10.md) | Multi-targeting `net8.0` e `net10.0` em todos os pacotes | Aceito |
| [ADR-0004](0004-storages-ado-net-direto.md) | Storages em ADO.NET direto, sem Dapper | Aceito |
| [ADR-0005](0005-dashboard-angular-embutido.md) | Dashboard Angular embutido no assembly como recurso | Aceito |
| [ADR-0006](0006-dashboard-restrito-a-development.md) | Dashboard acessível só em Development por padrão | Aceito |
| [ADR-0007](0007-nunca-capturar-payload.md) | Nunca capturar corpo, argumento, header ou SQL parametrizado | Aceito |
| [ADR-0008](0008-ferramentas-de-teste.md) | Shouldly para asserções, NSubstitute para mocks | Aceito |
| [ADR-0009](0009-identidade-visual-guia-de-marca.md) | Guia de marca v1 como referência canônica de identidade visual | Aceito |
| [ADR-0010](0010-layout-de-workspace-unico.md) | `/flow` vira um workspace denso, as demais rotas continuam existindo | Aceito |
