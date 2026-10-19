# Fase 5: Empacotamento

Projeto: `src/NetApiFlow.Dashboard`.

Pré-requisito: Fases 1 a 4 aceitas (o pacote embute o núcleo, a API e o Angular compilado).

## Tarefas

### 5.1: Target MSBuild de build do Angular (risco R-08)

No `.csproj` de `NetApiFlow.Dashboard`, um `<Target>` que roda `npm ci` (só se
`node_modules/` não existir ou `package-lock.json` for mais novo) e `ng build` antes de
`BeforeTargets="Build"` ou `BeforeTargets="Pack"`, com `<Inputs>` apontando para
`ClientApp/src/**/*` e `ClientApp/package-lock.json`, e `<Outputs>` apontando para
`ClientApp/dist/**/*`, para o MSBuild pular o passo quando nada mudou.

**Pronto quando:** rodar `dotnet build` duas vezes seguidas sem tocar em `ClientApp/` só executa
`ng build` na primeira vez (conferir pelo log do MSBuild com `-v:detailed` ou por timestamp do
`dist/`).

### 5.2: `dist/` como `EmbeddedResource`

`<EmbeddedResource Include="ClientApp\dist\**\*" />` com
`<GenerateEmbeddedFilesManifest>true</GenerateEmbeddedFilesManifest>`.

**Pronto quando:** teste de integração resolve `index.html` via `ManifestEmbeddedFileProvider`
lendo o assembly compilado.

### 5.3: Middleware de estáticos + `<base href>` (risco R-03)

Serve os arquivos embutidos. Lê o `index.html` do recurso embutido **uma vez** (cachear), troca
`<base href="/">` por `<base href="{PathBase}{DashboardPath}/">` calculado em runtime a partir de
`HttpContext.Request.PathBase` e do caminho configurado em `UseNetApiFlowDashboard`. Cache da
versão reescrita por chave de base path (não recalcular a cada requisição se o base path não
mudou). Fallback de SPA: qualquer rota dentro do `DashboardPath` que não bata com um arquivo
estático nem com uma rota de API devolve `index.html`.

**Pronto quando:** teste de integração com `app.UsePathBase("/app")` e
`UseNetApiFlowDashboard("/qualquer/caminho")` confere que o `index.html` servido contém
`<base href="/app/qualquer/caminho/">`.

### 5.4: Autorização do dashboard (risco R-09, ADR-0006)

Fora de `IHostEnvironment.IsDevelopment()`, sem `RequireAuthorization` nem
`IDashboardAuthorizationFilter` configurado, toda rota do dashboard (API e estáticos) devolve 404.
Com um dos dois configurado, aplica a policy/filter antes de servir qualquer coisa.

**Pronto quando:** teste de integração com `UseEnvironment("Production")` e nenhuma configuração
extra espera 404 em `/flow` e em `/flow/api/traces`; com `RequireAuthorization` configurado e um
usuário sem a claim exigida, espera 403.

### 5.5: CI falha sem `dist/`

Passo no `ci.yml` (ou um alvo MSBuith customizado chamado pelo `pack`) que falha explicitamente
se `ClientApp/dist/` não existir ou estiver vazio no momento do `dotnet pack`.

**Pronto quando:** remover `dist/` manualmente e rodar `dotnet pack` falha com uma mensagem clara
(não um erro genérico de arquivo não encontrado).

### 5.6: Projeto de verificação fora da solução

Um projeto novo (pode ficar em `tests/manual-verification/` ou local, fora de
`NetApiFlow.slnx`, para não poluir o build principal) que instala os `.nupkg` locais (via
`dotnet nuget add source` apontando para a pasta de output local) e chama
`UseNetApiFlowDashboard("/flow")` e, num segundo teste, em `/qualquer/caminho`.

**Pronto quando:** abrir o navegador (ou um teste automatizado com `HttpClient`) nas duas rotas
devolve o dashboard funcional, sem erro 404 de asset nem link quebrado de `<base href>`.

## Aceite da fase

Critério da seção 13, Fase 5: projeto novo fora da solução, instalando os `.nupkg` locais, abre o
dashboard em `/flow` e em `/qualquer/caminho`; fora de Development, sem configuração, devolve 404.

## Ao terminar

Resumo para a Planejadora. `/revisar-fase 5` roda a verificação da tarefa 5.6 pessoalmente antes
de liberar a Fase 6.
