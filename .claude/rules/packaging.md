# Empacotamento NuGet

## Metadados obrigatórios (todo `.csproj` de `src/`)

- `PackageId` com prefixo `NetApiFlow` (`NetApiFlow`, `NetApiFlow.Dashboard`,
  `NetApiFlow.EntityFrameworkCore`, `NetApiFlow.MediatR`, `NetApiFlow.Storage.PostgreSql`,
  `NetApiFlow.Storage.SqlServer`). Conferidos como livres no nuget.org antes do uso (tarefa 0.1).
- `PackageReadmeFile`, `PackageIcon` (`docs/images/icon.png`, tarefa 0.7), `PackageTags`,
  `RepositoryUrl` (`https://github.com/Douglasproglima/netapiflow`),
  `PackageLicenseExpression=MIT`. Autor e licença herdados de `Directory.Build.props`
  (tarefa 0.3) não precisam ser repetidos por projeto.
- `PackageReadmeFile` aponta para um `README.md` **do próprio pacote**
  (`src/<Pacote>/README.md`, curto, com link para o README principal do repositório), não o
  `README.md` da raiz diretamente: o NuGet exige o arquivo dentro do conteúdo empacotado.

## SourceLink e símbolos

- `PublishRepositoryUrl`, `EmbedUntrackedSources`, `ContinuousIntegrationBuild` (só quando
  `CI=true`), já definidos em `Directory.Build.props`.
- `.snupkg` habilitado (`IncludeSymbols`, `SymbolPackageFormat=snupkg`) em todo pacote.

## `NetApiFlow.Dashboard` especificamente

- UI Angular embutida via `EmbeddedResource` sobre `ClientApp/dist/` (ADR-0005). O pacote
  **nunca** pode sair sem a UI ou com UI desatualizada: CI falha o `pack` se `dist/` não existir
  (risco R-08, tarefa 5.5).
- Build do Angular roda via target MSBuild antes do `pack`, incremental por `Inputs`/`Outputs`
  (tarefa 5.1). Exige Node disponível no agente que roda `dotnet pack`.

## Build determinístico

- `Deterministic=true` (padrão do SDK), `ContinuousIntegrationBuild=true` só em CI (não em build
  local, para não quebrar o PathMap em debug local; ver comentário equivalente em
  `Directory.Build.props` do RajaDocs, que serviu de referência).

## Release (ver também `docs/gitflow.md`)

- Os 6 pacotes são versionados e publicados juntos, na mesma tag `v<versao>` (D-01, ADR-0001).
- Publicação via **Trusted Publishing** (OIDC do GitHub Actions): sem API key do nuget.org
  guardada em secret do repositório. Configuração da política de Trusted Publishing no site do
  nuget.org é passo manual, fora do repositório, feito antes da primeira tag (tarefa 7.7).
