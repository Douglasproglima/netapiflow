# ADR-0005: Dashboard Angular embutido no assembly como recurso

## Status
Aceito

## Issue
[#65](https://github.com/Douglasproglima/netapiflow/issues/65)

## Contexto
`netapiflow-prompt.md` (seções 2, 7.5 e 13-Fase5) exige que instalar o pacote e chamar
`UseNetApiFlowDashboard("/flow")` baste para abrir o dashboard, sem passo extra de build ou
deploy de um site separado. O frontend é Angular 22, compilado para arquivos estáticos.

## Decisão
O build de produção do Angular (`ng build`) roda antes do `dotnet pack` via target MSBuild no
projeto `NetApiFlow.Dashboard`, e `dist/` é incluído como `EmbeddedResource`. O middleware serve
os arquivos via `ManifestEmbeddedFileProvider`, reescrevendo `<base href>` para o caminho
configurado. O pacote nunca pode ser publicado sem a UI embutida ou com UI desatualizada: o CI
falha o `pack` se `dist/` não existir (ver risco R-08 em `docs/plano/riscos.md`).

## Consequências
- Usuário final não instala Node nem roda `npm` para ver o dashboard: só o `.nupkg`.
- Tamanho do pacote `NetApiFlow.Dashboard` cresce com os assets do Angular (aceitável, é o
  mesmo modelo do Hangfire Dashboard e do Scalar).
- Build do pacote exige Node disponível no agente de CI que faz o `pack` (documentado em
  `.claude/rules/packaging.md`).
