# Fase 34: hotfix 0.4.1, lacunas de credibilidade achadas em auditoria externa

Segunda auditoria externa pós-lançamento do `v0.4.0`, desta vez olhando o próprio pacote (não só
o site) com a régua de um dev .NET sênior hostil. Nove achados reais, cada um conferido pela
Planejadora contra o código ou contra fonte oficial antes de entrar aqui, não aceito só por ter
sido dito. Nenhum deles adiciona API pública nova: todos cabem num hotfix PATCH, cortado de
`main`, sem esperar o ciclo normal de `develop`.

Duas coisas ficaram de fora de propósito, por exigirem decisão de arquitetura própria, não
correção: renomear `public static class NetApiFlow` (viola CA1724, mas é breaking change sem
alias de compatibilidade) e a investigação de suporte a `net11.0` (o ASP.NET Core 11 passou a
emitir atributos de semantic convention do OpenTelemetry nativamente na própria `Activity` HTTP
que o NetApiFlow já usa como `TraceId`, fato real, confirmado na documentação oficial da
Microsoft, não especulação). As duas estão registradas em `docs/plano/roadmap-valor.md` para
decisão própria, fora deste hotfix.

## Regra geral desta fase

Toda tarefa escreve primeiro o teste que prova o comportamento esperado, confirma que ele falha
quando aplicável, só depois implementa.

## Tarefa 34.1: anotar `AddFlowTracked`/`DecorateFlowTracked` como incompatíveis com Native AOT

`FlowInterceptorProxy.Create` (`src/NetApiFlow/Marking/FlowInterceptorProxy.cs:31`) chama
`DispatchProxy.Create<TInterface, FlowInterceptorProxy>()`, que gera um tipo de proxy em tempo de
execução e não funciona sob Native AOT. `DecorateFlowTracked`
(`FlowTrackedServiceCollectionExtensions.cs:64`) agrava isso com `MakeGenericMethod` sobre um
`Type` só conhecido em tempo de execução, caso clássico de `RequiresDynamicCode`. Hoje nenhum dos
dois pontos de entrada tem anotação nenhuma: quem publica Native AOT só descobre o problema
rodando.

Anotar com `[RequiresDynamicCode(...)]` o método público `AddFlowTracked<TInterface,
TImplementation>` e o método público `DecorateFlowTracked`, mais `[RequiresUnreferencedCode(...)]`
em `DecorateFlowTracked` (o casamento de `predicate` contra `Type` de implementação depende de
reflexão que corte de trimming pode remover). A mensagem de cada atributo explica o porquê, não
repete a assinatura. `NetApiFlow.Step(...)` (forma manual) e o interceptor do EF Core não usam
`DispatchProxy` nem `MakeGenericMethod`, continuam seguros, e isso vira frase explícita no README.

**Pronto quando:** projeto de verificação com `<PublishAot>true</PublishAot>` chamando
`AddFlowTracked` ou `DecorateFlowTracked` gera o aviso `IL3050`/`IL2026` do analisador de trimming
na build, não silenciosamente passa batido. README.md e README.pt-BR.md ganham uma frase na seção
de limitações conhecidas dizendo exatamente quais formas de marcação são seguras sob Native AOT e
quais não são.

## Tarefa 34.2: documentar o limite de `/traces/live` em implantação com múltiplas réplicas

Achado já registrado em D-132: `GetLiveTraces` (`DashboardApiEndpoints.cs`) lê o `FlowRecorder`
injetado, estado em memória de um único processo. Com várias réplicas atrás de um balanceador sem
sticky session, `/flow` mostra só o que a réplica que respondeu àquela chamada viu, e o próprio
navegador pode pular de réplica a cada requisição. É consequência do desenho, não bug, mas nunca
foi escrito em lugar nenhum.

Um parágrafo novo na seção de limitações conhecidas do README.md e do README.pt-BR.md, explicando
o comportamento e por que a trace persistida (quando há storage configurado) não sofre o mesmo
problema, só a visão ao vivo.

**Pronto quando:** a frase está nos dois READMEs, dizendo explicitamente que é por processo, não
por cluster, e que afeta só `/traces/live`, não o histórico persistido.

## Tarefa 34.3: `Microsoft.Extensions.FileProviders.Embedded` não pode forçar 10.x no alvo `net8.0`

`Directory.Packages.props` fixa esse pacote em `10.0.0` com um comentário dizendo que é
"build-time only (netstandard2.0 MSBuild tooling): uma versão serve qualquer TargetFramework".
Isso está errado: `ManifestEmbeddedFileProvider`, o tipo principal do pacote, é usado em tempo de
execução em `DashboardEmbeddedFiles.cs`, pra servir o `dist/` embutido do Angular. É dependência
de runtime de verdade, confirmado na própria página do `NetApiFlow.Dashboard` no nuget.org: um app
`net8.0` acaba carregando um pacote `10.x`.

Dividir a versão por TFM, no mesmo molde já usado para `Microsoft.AspNetCore.Mvc.Testing` duas
linhas acima no mesmo arquivo: `8.0.34` (última da trilha de servicing 8.x, confirmada publicada
no nuget.org) para `net8.0`, `10.0.0` continua para `net10.0`. O comentário errado é removido, não
mantido ao lado do código corrigido.

**Pronto quando:** `dotnet pack NetApiFlow.Dashboard -c Release` gera um `.nupkg` cuja dependência
declarada pro grupo `net8.0` é `Microsoft.Extensions.FileProviders.Embedded >= 8.0.34`, não
`10.0.0`. Teste de integração existente que resolve `index.html` via `ManifestEmbeddedFileProvider`
continua passando nos dois TFMs sem alteração.

## Tarefa 34.4: documentar por que `net8.0` continua suportado mesmo fora do ciclo de vida oficial

`net8.0` e `net9.0` saíram de suporte em 10/11/2026 (confirmado na política oficial de suporte da
Microsoft), mesmo dia em que `net11.0` foi lançado. O ADR-0003 já previa esse momento ("quando
`net8.0` sair de suporte, trocar por `net12.0`"), mas a decisão de quando trocar ainda não foi
tomada, e enquanto não for, o README não diz nada sobre o assunto: alguém que notar a
inconsistência vai levantar isso como achado, quando já é uma decisão consciente de alcance.

Uma frase no README.md e no README.pt-BR.md, na seção de requisitos ou de target frameworks,
dizendo que `net8.0` segue suportado pelo pacote por decisão deliberada de alcance mesmo fora do
ciclo de vida oficial da Microsoft, e que a troca por `net12.0` está prevista no ADR-0003 quando a
Planejadora decidir o momento.

**Pronto quando:** a frase está nos dois READMEs. Não decide quando trocar, só documenta a decisão
de continuar como está por enquanto.

## Tarefa 34.5: `FlowStep.MarkError` não pode gravar em `exception.Data` do usuário

`MarkError` (`FlowStep.cs:29-44`) usa `exception.Data[OriginClaimedKey] = true` pra saber se já é
a origem de um erro já marcado por uma camada mais interna. Isso modifica um objeto que pertence
ao código do usuário, de forma observável por fora da biblioteca: qualquer logger que percorra
`Data` pra enriquecer o evento vê uma entrada estranha, com chave que nem é string. Viola o
princípio do projeto de nunca interferir no código do usuário.

Trocar por `ConditionalWeakTable<Exception, object>`: mesma semântica (associa um marcador à
exceção, sem impedir coleta de lixo, sem exigir que a exceção saiba de nada disso), sem tocar no
objeto do usuário. `OriginClaimedExceptions.TryGetValue(exception, out _)` no lugar de
`exception.Data.Contains(...)`, `OriginClaimedExceptions.Add(exception, Sentinel)` no lugar de
`exception.Data[...] = true`.

**Pronto quando:** teste novo em `StepTests.cs` confirma que, depois de `MarkError`, a exceção
lançada tem `exception.Data.Count` igual a zero (hoje seria um). O teste existente
`MarkError_SetsErrorTagsAndStatus` continua passando sem alteração, porque as tags na `Activity`
não mudam, só onde o marcador de origem é guardado.

## Tarefa 34.6: `ConfigureAwait(false)` em todo `src/`, travado por regra de analyzer

`ConfigureAwait` não aparece em nenhum arquivo de `src/`. Em ASP.NET Core não existe
`SynchronizationContext` capturado, então o comportamento em produção não muda nada, mas é
biblioteca publicada, e ausência total é indistinguível de esquecimento pra quem olha de fora.
Travar a decisão no build, não deixar na memória de quem escreve código novo depois.

Adicionar `.ConfigureAwait(false)` em todo `await` dentro de `src/` (núcleo e pacotes opcionais,
não nos samples nem nos testes, onde o contexto de execução é diferente e a regra não se aplica
do mesmo jeito). Habilitar `dotnet_diagnostic.CA2007.severity = warning` no `.editorconfig`
raiz, que vira erro de build por causa do `TreatWarningsAsErrors` já ligado.

**Pronto quando:** `dotnet build NetApiFlow.slnx -c Release` continua com zero avisos depois da
mudança. Remover um `.ConfigureAwait(false)` de propósito, num teste manual local, faz o build
falhar com CA2007, confirmando que a regra está travada de verdade, não só documentada.

## Tarefa 34.7: documentar que `GetRecentTrace(IServiceProvider, string)` é cenário de teste

`GetRecentTrace` é `static` e recebe `IServiceProvider` diretamente, um service locator. Em
`NetApiFlow.GetRecentTrace` usado dentro de teste de integração com `WebApplicationFactory`
(exatamente o cenário documentado na seção "Asserting on flow in tests" do README), isso é
idiomático. O risco é alguém usar o mesmo padrão dentro do código de produção, achando que é a
forma recomendada de acessar uma trace.

Comentário XML de `GetRecentTrace` ganha uma frase explícita: é pensado pra teste de integração,
não para uso dentro do pipeline normal de requisição. Sem mudança de assinatura nem de
comportamento.

**Pronto quando:** o comentário XML está lá. Critério de pronto é só documentação, não tem teste
automatizado pra isso.

## Fora de escopo

- Renomear `public static class NetApiFlow` (CA1724): breaking change real, sem alias de
  compatibilidade possível (mesma régua do D-75), decisão que precisa da Planejadora antes da
  1.0, não cabe num hotfix PATCH. Registrado em `roadmap-valor.md`.
- Qualquer trabalho sobre `net11.0`: precisa de fase de investigação própria antes de qualquer
  ADR, não é tarefa de hotfix. Registrado em `roadmap-valor.md`.
- Rodar a suíte completa de integração com Testcontainers, testar Native AOT publicado de
  verdade, e reinstalar o `0.4.1` do zero numa API de terceiro: três verificações que valem mais
  que qualquer leitura de código, fora do escopo das tarefas em si, mas exigidas no "Aceite da
  fase" abaixo antes de liberar o hotfix.

## Aceite da fase

Critérios de pronto das sete tarefas acima. `dotnet build NetApiFlow.slnx -c Release` e
`dotnet format NetApiFlow.slnx --verify-no-changes` limpos nos dois TFMs. Suíte completa,
incluindo `NetApiFlow.Storage.Tests` com Testcontainers real (Postgres e SQL Server), rodada pelo
menos uma vez antes do merge, não só a suíte rápida. `dotnet pack NetApiFlow.slnx -c Release`
conferido pacote por pacote.

## Ao terminar

Resumo pra Planejadora: o que foi feito por tarefa, como verificar (comandos exatos), o que ficou
pendente. Antes de considerar o hotfix pronto pra lançar, a Planejadora ainda precisa reinstalar o
`0.4.1` do zero numa API de terceiro (mesmo padrão das Fases 29/30), não substituível por revisão
de código.
