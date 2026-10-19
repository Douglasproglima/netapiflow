# Fase 0: Fundação

Objetivo: solução compilável, vazia de lógica de produto, com CI verde. Nenhuma linha de
negócio nesta fase.

Pré-requisitos: `docs/gitflow.md`, `docs/adr/`, `.claude/rules/` e este plano já existem
(entregues pela Planejadora no bootstrap). `.editorconfig`, `.gitignore`, `.gitattributes` e
`.gitleaks.toml` já existem na raiz (decisão D-13 em `decisoes.md`).

## Tarefas

### 0.1: Reconferir disponibilidade dos 6 IDs de pacote (bloqueante)

A Planejadora já conferiu os seis IDs em 2026-10-19 e registrou o resultado (livres) como D-14
em `docs/plano/decisoes.md`. Antes de iniciar a Fase 0, **reconfirmar** em
`https://www.nuget.org/packages/<Id>` que cada um continua livre. Tempo pode ter passado entre
o planejamento e a execução, e um nome livre então pode ter sido registrado por outra pessoa:
`NetApiFlow`, `NetApiFlow.Dashboard`, `NetApiFlow.EntityFrameworkCore`, `NetApiFlow.MediatR`,
`NetApiFlow.Storage.PostgreSql`, `NetApiFlow.Storage.SqlServer`.

**Pronto quando:** resultado da reconfirmação (livre ou ocupado, data, método usado) citado no
resumo de fim de fase. **Você não edita `docs/plano/decisoes.md`** — quem acrescenta o resultado
a D-14 é a Planejadora, durante `/revisar-fase 0`. Se algum ID estiver ocupado, **parar e
relatar antes de continuar**: não escolher um nome alternativo sozinho.

### 0.2: `global.json`

```json
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestFeature"
  }
}
```

**Pronto quando:** `dotnet --version` na raiz do repo resolve um SDK `10.0.x`.

### 0.3: `Directory.Build.props`

Na raiz. Propriedades comuns a todo projeto de produto (`src/**`):

- `<TargetFrameworks>net8.0;net10.0</TargetFrameworks>` (ADR-0003)
- `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`
- `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, `<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>`
- `<LangVersion>latest</LangVersion>`, `<AnalysisLevel>latest-recommended</AnalysisLevel>`
- `<Authors>Douglas Lima</Authors>`, `<PackageLicenseExpression>MIT</PackageLicenseExpression>`,
  `<RepositoryUrl>https://github.com/Douglasproglima/netapiflow</RepositoryUrl>`,
  `<RepositoryType>git</RepositoryType>`
- SourceLink: `<PublishRepositoryUrl>true</PublishRepositoryUrl>`,
  `<EmbedUntrackedSources>true</EmbedUntrackedSources>`,
  `<ContinuousIntegrationBuild Condition="'$(CI)' == 'true'">true</ContinuousIntegrationBuild>`,
  **e** o `PackageReference` de `Microsoft.SourceLink.GitHub` (via `Directory.Packages.props` +
  um `ItemGroup` em `src/Directory.Build.props`, ou direto em cada `.csproj` de `src/`) — as três
  propriedades acima não embutem nada sozinhas, sem o provedor do SourceLink instalado
  (`.claude/rules/packaging.md`).
- `<IncludeSymbols>true</IncludeSymbols>`, `<SymbolPackageFormat>snupkg</SymbolPackageFormat>`
- `<GenerateDocumentationFile>true</GenerateDocumentationFile>` (exigido pela regra de comentário
  XML em `.claude/rules/dotnet-core.md`)
- `<Version>0.1.0</Version>` (ADR-0001/D-01): sem isso, `dotnet pack` usa o default `1.0.0` do
  SDK, que contradiz "API instável até 1.0.0". O workflow de release (Fase 7, tarefa 7.7)
  sobrescreve este valor via `/p:Version=` extraído da tag; aqui é só o valor usado em build e
  pack locais/de CI fora de uma tag.

`tests/Directory.Build.props` (ou um `<PropertyGroup>` condicional por caminho) desliga
`GenerateDocumentationFile` e mantém os dois TFMs (D-11). O SourceLink não precisa ser desligado
para `tests/`, mas também não faz efeito prático lá, já que projeto de teste não é empacotado.

**Pronto quando:** `dotnet restore` passa sem warning de propriedade desconhecida; `dotnet pack`
gera pacotes com versão `0.1.0` (não `1.0.0`); o `.nuspec` de cada pacote lista
`Microsoft.SourceLink.GitHub` nas dependências de build (conferir com
`dotnet pack --no-build -o <pasta> && unzip -p <pasta>/NetApiFlow.0.1.0.nupkg "*.nuspec"` ou
equivalente).

### 0.4: `Directory.Packages.props`

`<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>` na raiz. Toda versão de
pacote (xUnit, Shouldly, NSubstitute, Npgsql, Microsoft.Data.SqlClient, MediatR, EF Core,
Testcontainers, Microsoft.AspNetCore.Mvc.Testing) entra aqui como `<PackageVersion>`, nunca como
atributo `Version` direto num `.csproj`.

**Pronto quando:** nenhum `.csproj` tem `Version=` num `PackageReference`.

### 0.5: Solução e projetos vazios

`NetApiFlow.slnx` com as pastas `/src` e `/tests`, contendo os 9 projetos abaixo (cada um com só
um `.csproj` e uma classe/arquivo placeholder, sem lógica):

```text
src/
  NetApiFlow/                       (classlib, núcleo, ver docs/plano/contratos.md)
  NetApiFlow.Dashboard/             (classlib)
  NetApiFlow.EntityFrameworkCore/   (classlib)
  NetApiFlow.MediatR/               (classlib)
  NetApiFlow.Storage.PostgreSql/    (classlib)
  NetApiFlow.Storage.SqlServer/     (classlib)
tests/
  NetApiFlow.Tests/                 (xUnit)
  NetApiFlow.IntegrationTests/      (xUnit + Microsoft.AspNetCore.Mvc.Testing)
  NetApiFlow.Storage.Tests/         (xUnit + Testcontainers)
```

Referências de projeto conforme a tabela de dependência da seção 5 do prompt:
`NetApiFlow.Dashboard`, `NetApiFlow.EntityFrameworkCore`, `NetApiFlow.MediatR`,
`NetApiFlow.Storage.PostgreSql` e `NetApiFlow.Storage.SqlServer` referenciam `NetApiFlow`. Nenhum
projeto de `src/` referencia outro projeto de `src/` fora dessa lista (`NetApiFlow` não referencia
nada em `src/`).

**Pronto quando:** `dotnet build NetApiFlow.slnx` passa nos dois TFMs.

### 0.6: `LICENSE`

MIT, `Copyright (c) 2026 Douglas Lima`.

**Pronto quando:** arquivo na raiz, GitHub detecta a licença automaticamente no repositório.

### 0.7: Ícone do pacote

`docs/images/icon.png`, 128×128, referenciado como `<PackageIcon>icon.png</PackageIcon>` +
`<None Include="..\..\docs\images\icon.png" Pack="true" PackagePath="\" />` em cada `.csproj` de
`src/` (ou centralizado no `Directory.Build.props` se o caminho relativo resolver igual para
todos).

**Pronto quando:** `dotnet pack` não emite os warnings `NU5048` (ícone ausente) nem `NU5047`.

### 0.8: `.github/workflows/ci.yml`

Matrix `ubuntu-latest` e `windows-latest`. Por job: `actions/setup-dotnet` com `8.0.x` e `10.0.x`
(ou `global-json-file`), `dotnet restore NetApiFlow.slnx`,
`dotnet format NetApiFlow.slnx --verify-no-changes --no-restore`,
`dotnet build NetApiFlow.slnx -c Release --no-restore`,
`dotnet test NetApiFlow.slnx -c Release --no-build --collect:"XPlat Code Coverage"`,
`dotnet pack NetApiFlow.slnx -c Release --no-build` como verificação (sem publicar).

Adicionar um job `secret-scan` com gitleaks (`.gitleaks.toml` já existe na raiz), modelo:

```yaml
- run: |
    docker run --rm -v "$PWD:/repo" zricethezav/gitleaks:latest \
      detect --source=/repo --config=/repo/.gitleaks.toml -v
```

**Pronto quando:** workflow verde nos dois sistemas operacionais, numa PR de teste.

### 0.9: `.github/workflows/pr-lint.yml`

Valida, num PR contra `develop` ou `main`:

- Nome de branch bate com `^(feature|bugfix|chore|release|hotfix)/[a-z0-9.-]+$` (ou `main` e
  `dependabot/*`), seguindo `docs/gitflow.md`.
- Título do PR bate com `^(feat|fix|refactor|test|docs|chore)(\(.+\))?: .+` (Conventional
  Commits), exceto quando a base é `main` vindo de `release/*` ou `hotfix/*`, caso em que o
  título pode ser só a versão (`0.1.0`).

**Pronto quando:** PR com título fora do padrão falha o check; PR correto passa.

### 0.10: `.github/dependabot.yml`

Ecossistemas `nuget` e `github-actions` agora. `npm` (diretório do `ClientApp`, que só existe a
partir da Fase 4) pode ser adicionado já, porque o Dependabot não falha por diretório
inexistente até a Fase 4 existir. Semanal, agrupado, target branch `develop`.

**Pronto quando:** arquivo válido (`dependabot.yml` não quebra o parse do GitHub: conferir na
aba Insights → Dependency graph → Dependabot do repositório).

### 0.11: Templates

`.github/ISSUE_TEMPLATE/bug.yml`, `.github/ISSUE_TEMPLATE/feature.yml`,
`.github/pull_request_template.md`.

**Pronto quando:** visíveis ao clicar em "New issue" e ao abrir um PR no GitHub.

### 0.12: Documentos de projeto

`CONTRIBUTING.md` (como rodar local, como abrir PR, aponta para `docs/gitflow.md`),
`CODE_OF_CONDUCT.md` (Contributor Covenant), `SECURITY.md` (como reportar vulnerabilidade),
`CHANGELOG.md` (Keep a Changelog, seção `[Unreleased]` com "Initial project scaffolding").

**Pronto quando:** todo link do README (ainda não escrito nesta fase, mas os arquivos precisam
existir para a Fase 7 linkar) resolve para um arquivo real.

## Aceite da fase

- `dotnet build NetApiFlow.slnx` e `dotnet test NetApiFlow.slnx` verdes em Linux e Windows, nos
  dois TFMs.
- `dotnet pack NetApiFlow.slnx -c Release` gera 6 `.nupkg` e 6 `.snupkg`, **com versão `0.1.0`**,
  não o default `1.0.0` do SDK.
- `Microsoft.SourceLink.GitHub` presente como dependência de build nos pacotes gerados.
- Os 6 IDs de pacote confirmados livres (tarefa 0.1).

## Ao terminar

Resumo para a Planejadora (seção 16.7 do prompt): o que foi feito, como verificar (comandos
exatos rodados), o que ficou pendente. A Planejadora roda `/revisar-fase 0` antes de liberar a
Fase 1.
