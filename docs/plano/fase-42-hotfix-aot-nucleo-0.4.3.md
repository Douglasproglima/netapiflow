# Fase 42: hotfix 0.4.3, núcleo não é AOT-limpo e `SqlClient` pinado numa versão que nunca funciona

Dois achados reproduzidos de verdade durante a rodada autônoma que implementou a Fase 36
(Tarefa 36.10) e a Fase 37 (Tarefa 37.1), registrados em D-147/D-148. Os dois têm remédio
concreto, PATCH, sem mudança de API pública.

## Tarefa 42.1: `AddNetApiFlow()` sozinho não é AOT-limpo

`dotnet publish -p:PublishAot=true` contra um host chamando só `AddNetApiFlow()` (sem EF Core, sem
nenhuma marcação) acusa 6 avisos (`IL2026`/`IL3050`) vindos de
`src/NetApiFlow/Configuration/NetApiFlowOptionsSetup.cs`: `section.Bind(options)` (linha 14) e dois
`section.GetSection(...).Get<string[]>()` (linhas 16 e 22), todos baseados em reflexão genérica do
`ConfigurationBinder`. `NetApiFlowOptions` é um POCO com forma fechada e conhecida: trocar o
binding genérico por leitura manual, propriedade por propriedade, elimina a reflexão sem mudar
nenhum comportamento observável (mesmos valores, mesma fonte de configuração).

**Pronto quando:** o projeto `AotAnnotationVerification` (ou um projeto irmão dedicado a este
caso: só `AddNetApiFlow()`, sem `Step`/interceptor/marcação nenhuma) publica com `PublishAot=true`
e zero aviso. Teste de regressão prova que a leitura manual lê `IncludePaths`/`ExcludePaths`/resto
da seção `"NetApiFlow"` de um `IConfiguration` real exatamente como `Bind` lia antes (mesmo valor,
mesmas propriedades).

## Tarefa 42.2: corrigir a frase do README sobre a alternativa "AOT-safe"

A verificação da Tarefa 36.10 (`docs/plano/fase-36-lote-divida-tecnica.md`) provou que a frase
atual do README/comentário XML do atributo (recomendando `NetApiFlow.Step(...)` ou o interceptor
do EF Core como alternativa segura a `AddFlowTracked`/`DecorateFlowTracked` sob AOT) está errada
nos dois lados: o núcleo em si não era limpo até a Tarefa 42.1 corrigir; o caminho do interceptor
do EF Core nunca fica limpo, porque é o próprio EF Core que não é totalmente compatível com AOT
(confirmado pela Tarefa 36.10: `IL2104`/`IL3053` em três assemblies do EF Core, mais `IL3002`, nada
vindo de `NetApiFlow.EntityFrameworkCore`).

Depois da 42.1: `NetApiFlow.Step(...)` sozinho (sem EF Core) é AOT-limpo de verdade. Reescrever a
frase pra refletir isso com precisão: recomendar `NetApiFlow.Step(...)` como alternativa AOT-safe
continua valendo, mas **sem** citar o interceptor do EF Core como se fosse igualmente seguro.
Documentar explicitamente, nas limitações conhecidas, que qualquer caminho envolvendo EF Core
herda a limitação de AOT do próprio EF Core, não uma limitação do NetApiFlow.

**Pronto quando:** README.md/README.pt-BR.md e o comentário XML do atributo não afirmam mais que o
interceptor do EF Core é seguro sob AOT. `AotAnnotationVerification` (ou projeto irmão) prova que
`NetApiFlow.Step(...)` sozinho publica limpo; nenhum projeto afirma o mesmo pro caminho do EF Core.

## Tarefa 42.3: `Microsoft.Data.SqlClient` pinado numa versão onde `ExternalActivitySource.SqlClient` nunca funciona

A Tarefa 37.1 confirmou que o payload do driver só implementa `IReadOnlyList<KeyValuePair<string,object>>`
a partir da versão 6.0; na `5.2.2` hoje fixada em `Directory.Packages.props`, o payload é um tipo
anônimo que o cast recusa, e `SqlClientCommandListener` registra nada, silenciosamente. Resultado:
quem liga `ExternalActivitySource.SqlClient` hoje (inclusive por padrão em `Development`, Fase 41)
nunca vê nenhum span, sem nenhum aviso de que a feature está de fato desligada. A D-139 já tinha
confirmado que `Microsoft.Data.SqlClient` `5.2.2` → `7.1.2` builda limpo nos dois TFMs, sem
nenhuma outra mudança de comportamento. Subir a versão fixada pra `7.1.2`.

**Pronto quando:** o mesmo teste que a Fase 37 já escreveu pra confirmar o payload tipado em `6.1.1`
passa contra a versão `7.1.2` de verdade fixada no projeto (não só testada à parte). Suíte inteira
verde com a versão nova.

## Fora de escopo

- O item 21 do roadmap (suspeita de dedup largo demais entre EF Core e os drivers nativos): não
  reproduzido ainda, não é hotfix pela própria régua da D-141. Fica fora, com tarefa de reprodução
  primeiro, numa fase futura.
- Qualquer mudança em `ExternalActivitySource`/`contratos.md`: a Tarefa 42.3 só muda a versão
  fixada do driver, não o contrato público.

## Aceite da fase

`dotnet build NetApiFlow.slnx -c Release` e `dotnet format NetApiFlow.slnx --verify-no-changes`
limpos nos dois TFMs. Suíte completa verde. `dotnet publish -p:PublishAot=true` contra os dois
projetos de verificação AOT (`AddFlowTracked`/`DecorateFlowTracked`, já existente; `AddNetApiFlow()`
sozinho, novo ou estendido) com zero aviso no segundo. `dotnet pack` conferido, `.nuspec` do núcleo
e do `NetApiFlow.Storage.SqlServer` (ou onde a `PackageVersion` do SqlClient for consumida)
declarando `7.1.2`.

## Ao terminar

Resumo pra Planejadora: o que foi feito, como verificar (comandos exatos), o que ficou pendente.
Mesmo pipeline de release da Fase 35: PR contra `main`, parar ali, merge/tag/publish manuais.
