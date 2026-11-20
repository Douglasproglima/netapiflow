# Fase 52: hotfix `0.5.2`, as rotas do NetApiFlow fora do documento OpenAPI do app (D-188)

**Hotfix `0.5.2`, com o achado (a) da Fase 50 e a correção dos workflows (Tarefa 52.6, D-194).** Branch `hotfix/0.5.2-openapi-internal-routes` a
partir de `main` (tag `v0.5.1`), PR para `main`, tag `v0.5.2`, página de Release no GitHub e merge de
`main` de volta em `develop`, tudo como o `docs/gitflow.md` descreve na seção "Hotfix", e como nas
Fases 34, 35, 42 e 49. O resto da Fase 50 vai no `0.6.0`, junto com a Fase 51.

## Por que hotfix

Pelo critério do D-141: falha reproduzida, PATCH, nenhuma mudança de API pública. Reproduzida pela
auditoria do dono e pela Planejadora, com os pacotes `0.5.0` publicados, num app novo:

- Em `Production`, com o documento OpenAPI publicado (comum em quem expõe a API a clientes), o documento
  lista **14 rotas do NetApiFlow**, `DELETE /flow/api/traces` e `POST /flow/api/deploys` entre elas.
- **Todas respondem 404** (conferido: `GET` e `DELETE /flow/api/traces`, e `GET /flow/api/config`).

É documentação pública de produção errada, e um SDK gerado a partir dela ganha 14 endpoints que não
existem. A política de cadência da auditoria pede para juntar correção não urgente. Esta é a exceção,
pelo dano em produção.

## Ponto de parada: regra de recuo

A Executora para e relata se vir qualquer um destes sinais:

- `ExcludeFromDescription()` tirar do documento uma rota **do usuário**, ou mudar o que o Runner lista.
- O `.nuspec` de qualquer um dos nove pacotes mudar em algo além da versão.
- `GET /flow/api/endpoints` (a lista que o dashboard monta a partir do `EndpointDataSource`) mudar.
- Qualquer um dos três projetos de teste da Tarefa 52.6 falhar na primeira rodada.

## O achado

Os grupos `endpoints.MapGroup(basePath + "/api")` (`DashboardApiEndpoints.cs`) e
`endpoints.MapGroup(basePath)` (`NetApiFlowDashboardEndpointRouteBuilderExtensions.cs`) não chamam
`ExcludeFromDescription()`. As rotas entram no documento de qualquer gerador que leia o
`ApiExplorer`: `Microsoft.AspNetCore.OpenApi` e Swashbuckle. O stream SSE e o endpoint do
`NetApiFlow.Mcp` também são rotas mapeadas pelo próprio NetApiFlow.

## Errata (D-193)

O plano supôs que o endpoint do MCP aparecia no documento. Não aparece com o SDK que o pacote usa
(`ModelContextProtocol.AspNetCore` 2.3.1): o `/flow/mcp/` não tem `MethodInfo`, e o ApiExplorer o
ignora. A marca no MCP fica como proteção contra um SDK mais novo, e só tem teste de marca, não de
documento. O bump de versão do hotfix também leva o `demo-fixtures/config.json`.

## Tarefa 52.0: reproduzir de novo, antes de corrigir

Teste de **host real** (`.claude/rules/testing.md`), visto falhando antes de qualquer mudança: um host
com `AddOpenApi()`, `AddNetApiFlowDashboard()`, `AddNetApiFlowMcp()` e `MapOpenApi()`, uma rota do
usuário e `GET /openapi/v1.json`. Hoje o documento traz as rotas internas. Rodar nos dois ambientes,
**`Development` e `Production`**, que é o motivo do hotfix.

## Tarefa 52.1: a correção

`ExcludeFromDescription()` nos dois grupos, no endpoint do stream SSE e no do `NetApiFlow.Mcp`: toda
rota que o próprio NetApiFlow mapeia. Nenhuma outra mudança.

## Tarefa 52.2: os testes

Todos de host real, em `net8.0` e `net10.0`:

| # | Cenário | Esperado |
|---|---|---|
| O1 | Documento OpenAPI com o dashboard e o MCP montados, em `Development` | A rota do usuário presente, nenhum caminho sob o caminho base do dashboard nem do MCP |
| O2 | O mesmo, em `Production` | O mesmo |
| O3 | Dashboard montado em outro caminho (`UseNetApiFlowDashboard("/internal/flow")`) | Nenhum caminho sob `/internal/flow` |
| O4 | `GET /flow/api/endpoints` | Resposta igual à de antes da correção |
| O5 | As rotas internas em `Production` sem autorização | Continuam respondendo 404 |

**Pronto quando:** O1 a O3 vistos falhando antes e passando depois, O4 e O5 passando nos dois momentos.

## Tarefa 52.3: teste com o pacote real, no caminho do usuário (D-186)

Pacote empacotado de verdade, `-p:Version=0.5.2-verify.<n>` (nunca o número real, nunca
`-p:VersionSuffix`, `<n>` novo a cada rodada, ver `.claude/rules/packaging.md`). Um app novo de
`dotnet new webapi` com os pacotes locais, as três linhas do Quick start do `README.md` seguidas ao pé da
letra, `AddOpenApi()` e `MapOpenApi()`. Conferir o documento em `Development` e em `Production`: nenhuma
rota do NetApiFlow nele. E `dotnet list package --vulnerable --include-transitive` **sem filtro de
`grep`**, com a saída literal no resumo.

## Tarefa 52.4: conferir os `.nuspec`

Depois do `dotnet pack` de verificação, a lista de dependências dos nove pacotes igual à do `.nuspec`
publicado da `0.5.1`
(`https://api.nuget.org/v3-flatcontainer/<id minúsculo>/0.5.1/<id minúsculo>.nuspec`), exceto a versão do
`NetApiFlow` que os pacotes pedem. Isso prova que o hotfix mexe só no documento OpenAPI.

## Tarefa 52.5: versão, changelog e fluxo

- `Directory.Build.props` ganha `0.5.2`, como os hotfixes anteriores (`git log -p -3 --
  Directory.Build.props` mostra como foi feito).
- `CHANGELOG.md`, seção `[0.5.2]`, em `### Fixed`, em termos de usuário, em inglês: as rotas do próprio
  NetApiFlow (`/flow/api/*`, a rota estática do dashboard, o stream SSE e o endpoint do MCP) deixaram de
  aparecer no documento OpenAPI do app, então um SDK gerado a partir dele não ganha mais endpoints que
  não existem. Pacotes `NetApiFlow.Dashboard` e `NetApiFlow.Mcp`. Sem citar número de tarefa nem de fase.
- Teste com o pacote real (Tarefa 52.3) antes da PR, como pede o `docs/gitflow.md`.
- PR para `main` (squash), tag `v0.5.2`, página de Release no GitHub criada à mão
  (`gh release create v0.5.2 --title v0.5.2 --notes-file <corpo> --target main`), e merge de `main` de
  volta em `develop`.
- O `release.yml` do `v0.5.2` roda a lista de 6 projetos do D-183, não a suíte inteira: `Mcp.Tests`,
  `OpenTelemetry.Tests` e `PrePipelineInteropSpike` não rodam em nenhum workflow (D-193, corrigido na
  Fase 50).
- O README da `main` já traz o Quick start corrigido (PR #329), então este hotfix não toca nele.
- Commits pequenos, em inglês, uma linha, sem linha de coautor.
- Ao terminar: abrir a PR contra `main` e parar. O merge, a tag e a página de Release exigem confirmação
  do dono.

## Tarefa 52.6: os três projetos de teste nos dois workflows (D-194)

Decisão do dono: `NetApiFlow.Mcp.Tests`, `NetApiFlow.OpenTelemetry.Tests` e
`NetApiFlow.PrePipelineInteropSpike`, que não rodam em nenhum workflow (D-193), entram no `ci.yml` e no
`release.yml` dentro desta PR, antes do merge.

1. Primeira rodada local dos três, em Release, nos dois TFMs, com a saída real colada antes do push. Se
   algum falhar, parar e relatar. Não consertar teste dentro do hotfix.
2. Os três entram antes do bloco que só roda fora do Windows, junto com o `Ef8Tests`, nos dois
   workflows. Nenhum usa Docker.
3. Um commit novo, sem emendar o `5b1c885`, em inglês, uma linha. Sem entrada de CHANGELOG: nada muda
   para quem instala.
4. Depois do push, o log real da PR mostra os três projetos executando nos dois sistemas, com a
   contagem de testes de cada um.

**Pronto quando:** os dois workflows listam os mesmos projetos, o log do CI mostra os três executando
e verdes, e a auditoria revalida o commit novo.

## Critério que vale para a fase inteira

- Build, testes, formato e lint limpos nos dois TFMs, incluindo os projetos de interop e o
  `Storage.Tests` rodados de verdade, com Docker.
- `tests/manual-verification/AotCoreVerification` continua com zero aviso.

## Fora de escopo

Todo o resto da Fase 50 (aviso do grafo vazio, conta do painel, OpenAPI fora do rastreamento, demo,
`EmitTraceIdHeader`, `AutoCreateSchema`): `0.6.0`. A demo precisa do documento corrigido, e ele chega a
`develop` pelo merge de `main` de volta.

## Relatório final

O teste de host real dos casos O1 a O5, falhando antes e passando depois. A saída da primeira rodada
local dos três projetos da Tarefa 52.6 e a contagem de testes de cada um no CI. A saída literal do
`--vulnerable`. A comparação dos nove `.nuspec` com os da `0.5.1`. A confirmação de que o `release.yml`
do `v0.5.2` rodou a lista nova de testes. O número da PR e o resultado do `AotCoreVerification`.
