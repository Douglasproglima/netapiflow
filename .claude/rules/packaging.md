# Empacotamento NuGet

## Metadados obrigatórios (todo `.csproj` de `src/`)

- `PackageId` com prefixo `NetApiFlow` (`NetApiFlow`, `NetApiFlow.Dashboard`,
  `NetApiFlow.EntityFrameworkCore`, `NetApiFlow.MediatR`, `NetApiFlow.Storage.PostgreSql`,
  `NetApiFlow.Storage.SqlServer`, `NetApiFlow.Mcp`, `NetApiFlow.Testing`). Conferidos como livres
  no nuget.org antes do uso (tarefa 0.1; `NetApiFlow.Mcp` conferido pela Planejadora em
  2026-11-04, D-72; `NetApiFlow.Testing` conferido pela Planejadora em 2026-11-17, ADR-0022).
- `PackageReadmeFile`, `PackageIcon` (`docs/images/icon.png`, tarefa 0.7), `Description`,
  `PackageTags`, `RepositoryUrl` (`https://github.com/Douglasproglima/netapiflow`),
  `PackageLicenseExpression=MIT`. Autor e licença herdados de `Directory.Build.props`
  (tarefa 0.3) não precisam ser repetidos por projeto.
- `PackageReadmeFile` aponta para um `README.md` **do próprio pacote**
  (`src/<Pacote>/README.md`, curto, com link para o README principal do repositório), não o
  `README.md` da raiz diretamente: o NuGet exige o arquivo dentro do conteúdo empacotado.
- **`Description` e `PackageTags` são verificados de verdade, não só exigidos por escrito
  (D-174).** Os 8 pacotes publicados até a `0.4.4` saíram com a descrição placeholder do SDK
  ("Package Description") e sem `PackageTags`, apesar desta regra já exigir os dois desde a Fase
  0: ninguém notou porque nada falhava o build por causa disso. O alvo `VerifyPackageMetadata`
  (`src/Directory.Build.props`, `BeforeTargets="Pack"`) falha o `pack` quando `Description` está
  vazio ou é o literal "Package Description", ou quando `PackageTags` está vazio. Pacote novo
  sem os dois nunca chega a gerar `.nupkg`.

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

## `dotnet pack` de verificação nunca usa o número de versão real (D-153)

`tests/manual-verification/` empacota localmente pra provar algo contra o `.nupkg` de verdade,
não contra `ProjectReference`. Empacotar com o mesmo número de versão que será (ou já foi)
publicado contamina o cache global do NuGet (`~/.nuget/packages/<id>/<versão>/`): qualquer
`dotnet add package`/restore seguinte, nesta máquina, para essa combinação de id e versão, resolve
o build local, não o pacote real do nuget.org, **mesmo depois da publicação real acontecer**.
Sem suspeitar de nada, isso dá dois resultados errados possíveis: uma verificação acusando bug que
o pacote publicado não tem (falso vermelho), ou pior, confirmando limpo um pacote publicado que na
verdade está quebrado (falso verde, porque validou o build local sem perceber).

Aconteceu pelo menos duas vezes: na checagem do `0.4.0` e, de novo, na verificação do hotfix
`0.4.2` (D-153), mesma causa as duas vezes. Se encontrar o cache contaminado numa pasta
`<id>/<versão real>/`, apagar a pasta inteira antes de confiar em qualquer checagem feita contra
ela.

**O comando original da D-153 nunca funcionou neste repositório (D-174).**
`-p:VersionSuffix=verify` não faz nada aqui: `Directory.Build.props` fixa `<Version>`
diretamente, e o SDK só usa `VersionSuffix` pra compor `$(Version)` a partir de
`$(VersionPrefix)` quando `<Version>` **não** está setado. Confirmado reproduzindo
`dotnet msbuild src/NetApiFlow/NetApiFlow.csproj -getProperty:Version -p:VersionSuffix=verify
-p:TargetFramework=net10.0`: devolve o número real (`0.4.4`), não um sufixado. **Comando certo:
`-p:Version=<próxima>-verify.<n>`** (sobrescreve `<Version>` de verdade), nunca repetindo o mesmo
`<n>` numa mesma versão em avaliação.

## Release (ver também `docs/gitflow.md`)

- Os pacotes (9 atuais: `NetApiFlow`, `NetApiFlow.Dashboard`,
  `NetApiFlow.EntityFrameworkCore`, `NetApiFlow.MediatR`, `NetApiFlow.Mcp` desde D-72,
  `NetApiFlow.OpenTelemetry`, `NetApiFlow.Storage.PostgreSql`, `NetApiFlow.Storage.SqlServer`,
  `NetApiFlow.Testing` desde ADR-0022) são versionados e publicados juntos, na mesma tag
  `v<versao>` (D-01, ADR-0001).
- Publicação via **Trusted Publishing** (OIDC do GitHub Actions): sem API key do nuget.org
  guardada em secret do repositório. Configuração da política de Trusted Publishing no site do
  nuget.org é passo manual, fora do repositório, feito antes da primeira tag (tarefa 7.7).
