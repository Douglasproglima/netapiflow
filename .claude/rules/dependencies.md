# Dependências

## Antes de adicionar pacote (NuGet ou npm)

1. **Núcleo (`NetApiFlow`) não ganha dependência nova**, nunca. Só `FrameworkReference` para
   `Microsoft.AspNetCore.App`. Qualquer necessidade de pacote externo é sinal de que a
   funcionalidade pertence a outro pacote (EF Core, MediatR, storage), não ao núcleo.
2. Licença compatível com MIT (MIT, Apache 2.0, BSD-2/3-Clause). Checar explicitamente antes de
   adicionar: FluentAssertions (v8+) e Moq (pacote `SponsorLink`, desde 4.20) são exemplos já
   descartados neste projeto por esse motivo (ADR-0008).
3. Manutenção ativa (release nos últimos 12 meses) e sem vulnerabilidade conhecida
   (`dotnet list package --vulnerable`, `npm audit`).
4. Compatível com `net8.0` **e** `net10.0` (ADR-0003), e com a versão atual do Angular.
5. Resolve algo que não dá para fazer com poucas linhas de código próprio. Ver D-08 em
   `docs/plano/decisoes.md` (glob de rotas escrito do zero em vez de trazer um pacote de
   pattern-matching).
6. **Nenhuma dependência pode vir do apiflow** nem reutilizar código dele (identidade do projeto,
   seção 1.1 do `netapiflow-prompt.md`). "Dependência" aqui inclui copiar um trecho de código de
   um projeto sem licença declarada.

Pacote novo é decisão que a Executora registra no resumo da fase, com o motivo. Se a Executora
tiver dúvida sobre se um pacote deveria ser dependência do núcleo ou de um pacote opcional,
parar e perguntar à Planejadora antes de adicionar.

## Centralização de versão

Toda versão de pacote fica em `Directory.Packages.props`
(`ManagePackageVersionsCentrally`, tarefa 0.4). Nenhum `.csproj` individual fixa versão.

## Piso das dependências publicadas (D-181)

O que um pacote declara como dependência vira o mínimo que quem o instala é obrigado a ter. Aqui,
`Directory.Packages.props` é a versão de build e o mínimo do `.nuspec` ao mesmo tempo: a versão que
está lá é o piso publicado. Subir essa linha sem querer tira o pacote de quem usa a versão anterior.
Foi assim que o `NetApiFlow.EntityFrameworkCore` passou a pedir EF Core `9.0.22` e deixou de
funcionar com EF Core 8.

O princípio é o do D-172: o pacote pede a **menor versão que funciona**.

- **Funcionar é medido, não suposto.** Compila contra o piso e a suíte relevante passa nele, ou o
  teste com o pacote real passa. "Funciona porque a versão nova funciona" não vale.
- **O piso só sobe em dois casos.** O pacote passa a usar uma API que só existe na versão nova, ou o
  piso atual tem vulnerabilidade conhecida. Sair versão nova não é motivo.
- **Piso com vulnerabilidade conhecida não serve de piso.** O menor piso aceitável é a menor versão
  que funciona e passa em `dotnet list package --vulnerable --include-transitive`. Foi o que decidiu
  o EF Core 8: da `8.0.0` à `8.0.8` puxam `Microsoft.Extensions.Caching.Memory` `8.0.0`, e o piso é
  a `8.0.10`.
- **Subir é decisão, nunca PR automática.** Uma PR própria diz o motivo (API nova ou vulnerabilidade)
  e entra em `### Changed` do `CHANGELOG.md`, porque afeta quem instala.
- **A vulnerabilidade no piso aparece sozinha.** `NuGetAudit` mais aviso tratado como erro derrubam o
  `restore` (foi assim que o `Pkcs` apareceu, D-170). O Dependabot não é o detector.
- **Dependência só de teste ou de build** (não entra no `.nuspec`) acompanha as versões novas, e pode
  ficar mais nova que o piso: é a forma de provar "compila no piso, roda no mais novo". Precedente:
  `Microsoft.Data.SqlClient` `6.1.8` só em `NetApiFlow.SqlClient.IntegrationTests` (D-172). A exceção
  são os pacotes de teste que dividem família com um piso (`Microsoft.EntityFrameworkCore.Sqlite`,
  por exemplo): ficam fora do Dependabot junto com a família e sobem à mão.
- **Segundo precedente (D-285):** `Npgsql.EntityFrameworkCore.PostgreSQL` tem duas linhas `PackageVersion`, escolhidas por `$(MSBuildProjectName)`: `9.0.4` para os projetos de sempre e `10.0.0` para `NetApiFlow.Npgsql10.IntegrationTests`, que só existe em `net10.0` porque o provider 10 não tem `net8.0`. O `Npgsql` `10.0.0` chega pelo provider, sem referência direta e com a pinagem transitiva desligada, e a linha do `Npgsql` `8.0.5`, piso do `NetApiFlow.Storage.PostgreSql`, fica como está. Sem `VersionOverride`.

## Atualização

Dependabot abre PR semanal agrupado contra `develop` (tarefa 0.10). Ler o changelog da
dependência antes de aprovar. Atualização major do Angular segue `ng update`, nunca troca manual
de versão no `package.json`.

**Pisos ficam fora do Dependabot de versão.** Todo pacote referenciado por `src/` que entra no
`.nuspec`, e a família dele, tem `ignore` por nome sem `update-types`, o que ignora toda
atualização, não só a versão principal: um patch (`9.0.22` para `9.0.23`) também sobe o piso. A
lista exata está em `.github/dependabot.yml`. Hoje: `MediatR`, `Microsoft.Data.SqlClient`,
`Microsoft.EntityFrameworkCore*`, `Microsoft.Extensions.FileProviders.Embedded`,
`ModelContextProtocol*`, `Npgsql*` e `OpenTelemetry*`. Pacote novo em `src/` que entre no `.nuspec`
entra nessa lista na mesma PR.

Duas coisas que não são óbvias:

- O `dependabot.yml` só vale na branch padrão do repositório (`main`). Uma mudança nele feita em
  `develop` só passa a valer depois do próximo release ou hotfix.
- Com `target-branch` apontando para `develop`, as opções do Dependabot não se aplicam às
  atualizações de segurança, que sempre usam a branch padrão (GitHub Docs, "Dependabot options
  reference", seção `target-branch`). O `ignore` acima não esconde PR de segurança.

**Projeto de verificação que fixa o próprio NetApiFlow publicado, de propósito (D-313).** `tests/manual-verification/Fase59DesempenhoVerification` compara o `Flow.Step` com o `NetApiFlow` `0.5.3` publicado, e por isso fixa essa versão. O Dependabot pode propor subir essa linha, o que trocaria a linha de base da comparação sem ninguém ver. O `dependabot.yml` só vale a partir da `main`, e a auditoria decidiu que pôr `NetApiFlow*` no `ignore` entra na `0.7.0`. Até lá, uma PR do Dependabot que suba o `NetApiFlow` desse projeto se fecha, e não se mergeia.

**Pino só de teste para sair de uma vulnerabilidade transitiva (D-323).** `SQLitePCLRaw.bundle_e_sqlite3` `2.1.13`, só no
`NetApiFlow.EntityFrameworkCore.Ef8Tests`, tira o `SQLitePCLRaw.lib.e_sqlite3` `2.1.6` (GHSA-2m69-gcr7-jv3q) que o
`Microsoft.EntityFrameworkCore.Sqlite` `8.0.10` puxa. Não é piso e não tem o nome da família do EF Core, então nenhuma das regras
de cima o cobre. O patch e o minor seguem o Dependabot, porque uma `2.1.x` nova pode trazer outra correção de segurança. A major
sobe à mão, junto da família do EF Core, porque os providers foram construídos contra a linha `2.1`. O lugar disso é uma linha
`SQLitePCLRaw*` com `update-types: ["version-update:semver-major"]` no `dependabot.yml`, o item 73 do roadmap, que espera o dono
por causa do congelamento da `0.7.0`. Até ela entrar, quem segura a `3.x` é o "@dependabot ignore this major version" da #477,
guardado no Dependabot e invisível no repositório. Uma PR que suba o `SQLitePCLRaw` para a `3.x` se fecha, e não se mergeia.
