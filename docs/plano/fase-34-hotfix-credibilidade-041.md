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

Revisão de uma segunda IA sobre este mesmo plano (D-134) corrigiu duas coisas antes da
implementação terminar: a Tarefa 34.6 original (`ConfigureAwait(false)` espalhado por `src/`)
foi substituída por uma tarefa de documentação, porque o próprio time do ASP.NET Core
deliberadamente não usa `ConfigureAwait(false)` em código acoplado ao ASP.NET Core, e o
NetApiFlow é exatamente isso; e a Tarefa 34.5 ganhou um critério de pronto mais forte, cobrindo o
comportamento que o mecanismo existe pra resolver, não só o efeito colateral da correção.

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

**Achado da Executora, fora do que este plano previa (D-135):** fixar a versão por TFM não bastou.
O package pruning do SDK `net10.0`, em restore, colapsa a referência pro grupo `net8.0` porque o
`lib/` do pacote já é satisfeito pelo shared framework instalado, e no caminho descarta junto os
assets de `build/` (a task MSBuild que gera o manifesto embutido), quebrando em tempo de execução
sem warning nenhum no build. Fix adicional, só no grupo condicional de `net8.0`:
`<RestoreEnablePackagePruning>false</RestoreEnablePackagePruning>`. Aprovado depois de conferir a
cadeia causal e a suíte completa (775 testes, Testcontainers real) passando. O comentário antigo
removido ("build-time only") tinha uma parte de verdade que a primeira análise da Planejadora não
tinha capturado: os assets de `build/` são mesmo build-time only, só o `lib/` é que é runtime.

## Tarefa 34.4: documentar por que `net8.0` continua suportado mesmo fora do ciclo de vida oficial

`net8.0` e `net9.0` saíram de suporte em 10/11/2026 (confirmado na política oficial de suporte da
Microsoft), mesmo dia em que `net11.0` foi lançado. O ADR-0003 previa esse momento ("quando
`net8.0` sair de suporte, trocar por `net12.0`"), mas essa cláusula tem um furo que ninguém tinha
visto até a revisão de D-134: `net12.0` só deve lançar em novembro de 2027, quase um ano depois
de `net8.0` já estar fora de suporte agora. A troca direta que o ADR previa é literalmente
impossível de executar hoje. Não dá pra escrever uma frase de "decisão deliberada" que finja um
plano fechado que não existe.

Uma frase honesta no README.md e no README.pt-BR.md, na seção de requisitos ou de target
frameworks: `net8.0` segue suportado pelo pacote por enquanto, mesmo fora do ciclo de vida oficial
da Microsoft, porque a próxima LTS (`net12.0`) ainda não existe; a decisão de quando sair está sob
investigação (ligada à Tarefa 19 do `roadmap-valor.md`, que também cobre o furo de doze meses do
próprio ADR-0003), não fechada ainda.

**Pronto quando:** a frase está nos dois READMEs, e não afirma um plano que o ADR-0003 não
sustenta de verdade. Não decide quando trocar, só documenta honestamente que a decisão está em
aberto e por quê.

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

Esta é uma correção comportamental, não documental: o `OriginClaimedKey` existe pra resolver um
cenário específico (uma camada externa captura e relança a mesma instância de exceção sem
reivindicar a origem pra si), e a troca de mecanismo precisa continuar resolvendo exatamente esse
cenário, não só parar de tocar em `exception.Data`.

**Pronto quando:** teste novo em `StepTests.cs` confirma que, depois de `MarkError`, a exceção
lançada tem `exception.Data.Count` igual a zero (hoje seria um). Teste novo adicional confirma o
cenário de origem de verdade: dois `FlowStep` aninhados chamam `MarkError` com a **mesma
instância** de exceção (simulando uma camada externa capturando e relançando sem reivindicar),
e só o primeiro `MarkError` (o mais interno) marca `netapiflow.error.origin` como `true`; o
segundo marca `false`. O teste existente `MarkError_SetsErrorTagsAndStatus` continua passando
sem alteração, porque as tags na `Activity` não mudam, só onde o marcador de origem é guardado.

## Tarefa 34.6: documentar por que `src/` não usa `ConfigureAwait(false)`, em vez de adicionar

Revisado depois da implementação ter começado (D-134): espalhar `.ConfigureAwait(false)` por
todo `await` de `src/` foi a tarefa original, e está cancelada. O próprio time do ASP.NET Core
deliberadamente não usa `ConfigureAwait(false)` em código acoplado ao framework, justamente
porque não existe `SynchronizationContext` ali: não há nada pra evitar capturar. O núcleo do
NetApiFlow só tem `FrameworkReference` pro `Microsoft.AspNetCore.App` (D-02, núcleo sem
dependência externa), é uma lib de ASP.NET Core por construção, não uma lib de propósito geral
que também precisa rodar fora dele. Adicionar a chamada em umas cinquenta ocorrências é diff
grande, risco real de erro humano (um `await` sem `.ConfigureAwait` esquecido no meio de
cinquenta com fica pior que não ter nenhum), e não resolve problema nenhum que exista de verdade.

O problema que a auditoria realmente aponta não é a ausência do `ConfigureAwait`, é a ausência
**não estar escrita**: fica indistinguível de esquecimento pra quem olha de fora. Isso se resolve
registrando a decisão, não com código.

Registrar a decisão num ADR novo (próximo número livre em `docs/adr/`), curto, três ou quatro
frases: o núcleo é acoplado ao ASP.NET Core por `FrameworkReference`, não existe
`SynchronizationContext` nesse contexto, `ConfigureAwait(false)` não muda nenhum comportamento
aqui, e por isso a ausência é deliberada. `dotnet_diagnostic.CA2007.severity = none` explícito no
`.editorconfig` raiz, com a referência ao ADR ao lado (não repetindo o texto do ADR, só
apontando pra ele), pra `CA2007` nunca disparar por acidente numa versão futura do SDK que mude o
que entra em `latest-recommended`.

**Pronto quando:** o ADR existe, está em `docs/adr/README.md` (índice). `.editorconfig` tem a
linha de `CA2007` explícita. `dotnet build NetApiFlow.slnx -c Release` continua com zero avisos.

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

## Tarefa 34.8: corrigir o snippet de marcação manual do README, que não compila

Achado da Planejadora (D-136), verificando se a frase do atributo AOT na Tarefa 34.1 ("use
`NetApiFlow.Step(...)` em vez disso") era sustentada por algum teste real. O snippet do próprio
README (`using NetApiFlow;` seguido de `NetApiFlow.Step(...)`) não compila quando o código que
chama fica na global namespace, caso mais comum de todos: um `Program.cs` de top-level statements,
exatamente o estilo que o README usa em todo canto. Erro `CS0234`, porque o namespace `NetApiFlow`
e a classe estática `NetApiFlow` compartilham o nome (o mesmo achado de CA1724, item 15 do
`roadmap-valor.md`), e partindo da global namespace a busca de identificador encontra o namespace
antes do tipo importado por `using`. Dentro de qualquer namespace nomeado o problema não aparece,
por isso nenhum teste da suíte pegou isso: nenhum teste nem sample do repositório chama `Step()` a
partir da global namespace.

Trocar, nos dois READMEs, o par de snippets "Manual form"/"Tagging a step": em vez de
`using NetApiFlow;` mais `NetApiFlow.Step(...)`, usar `using static NetApiFlow.NetApiFlow;` mais
`Step(...)` sem qualificação. Essa forma compila nos dois cenários (global namespace e namespace
normal) e lê mais limpo que a atual. Só texto nos dois READMEs, nenhuma mudança de API.

**Pronto quando:** os dois snippets usam `using static NetApiFlow.NetApiFlow;`. Conferir colando o
snippet novo num `Program.cs` de top-level statements isolado (fora do repositório) e rodando
`dotnet build`: zero erro.

## Fora de escopo

- Renomear `public static class NetApiFlow` (CA1724): breaking change real, sem alias de
  compatibilidade possível (mesma régua do D-75), decisão que precisa da Planejadora antes da
  1.0, não cabe num hotfix PATCH. Registrado em `roadmap-valor.md`.
- Qualquer trabalho sobre `net11.0`: precisa de fase de investigação própria antes de qualquer
  ADR, não é tarefa de hotfix. Registrado em `roadmap-valor.md` (item 16, que agora também cobre
  o furo de doze meses do ADR-0003, achado em D-134).
- Rodar a suíte completa de integração com Testcontainers, testar Native AOT publicado de
  verdade, e reinstalar o pacote publicado do zero numa API de terceiro: três verificações que
  valem mais que qualquer leitura de código, fora do escopo das tarefas em si, mas exigidas no
  "Aceite da fase" abaixo antes de liberar o hotfix. A reinstalação do pacote publicado está
  rodando em paralelo às tarefas de código (D-134: fazer antes de fechar o escopo, não depois,
  pra qualquer achado novo entrar neste mesmo hotfix em vez de virar um `0.4.2` logo em seguida).

## Aceite da fase

Critérios de pronto das oito tarefas acima. `dotnet build NetApiFlow.slnx -c Release` e
`dotnet format NetApiFlow.slnx --verify-no-changes` limpos nos dois TFMs. Suíte completa,
incluindo `NetApiFlow.Storage.Tests` com Testcontainers real (Postgres e SQL Server), rodada pelo
menos uma vez antes do merge, não só a suíte rápida. `dotnet pack NetApiFlow.slnx -c Release`
conferido pacote por pacote.

## Ao terminar

Resumo pra Planejadora: o que foi feito por tarefa, como verificar (comandos exatos), o que ficou
pendente. Antes de considerar o hotfix pronto pra lançar, a Planejadora ainda precisa reinstalar o
`0.4.1` do zero numa API de terceiro (mesmo padrão das Fases 29/30), não substituível por revisão
de código.
