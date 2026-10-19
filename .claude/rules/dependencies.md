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

## Atualização

Dependabot abre PR semanal agrupado contra `develop` (tarefa 0.10). Ler o changelog da
dependência antes de aprovar. Atualização major do Angular segue `ng update`, nunca troca manual
de versão no `package.json`.
