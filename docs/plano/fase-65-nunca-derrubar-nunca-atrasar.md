# Fase 65: nunca derrubar e nunca atrasar a API, além do hotfix (D-236, B5)

O hotfix 0.5.3 (Fase 61) fecha o que derrubava o host. Esta fase fecha o resto do mesmo princípio ("nunca derrubar, travar ou
atrasar a API do usuário", `.claude/rules/tracing.md`) que a varredura final achou em três lugares.

Vai no `0.6.0` (D-236). São defeitos antigos, sem regressão, então não são hotfix (D-205). Vem **depois das Fases 53 e 55**,
porque as três mexem no middleware e no listener de atividades.

## Os achados e o que a Planejadora conferiu

| # | Achado | Conferi |
|---|---|---|
| B5a | Os callbacks do `ActivityListener` e a parte inicial do middleware não têm `try/catch` com log | Não verifiquei |
| B5b | O `NetApiFlowOptionsValidator` não valida os padrões: `IncludePaths("/x", null!)` passa hoje e dá `NullReferenceException` em **toda requisição** | Existe `NetApiFlowOptionsValidator.cs`. **Não li o corpo** nem reproduzi |
| B5c | O `FlowEventPublisher` roda os listeners na thread da requisição, contra o "never blocks" do resumo da classe | **Código:** o `Publish` faz `_ = DispatchAsync(flowEvent)`, e `DispatchAsync` percorre os listeners com `await`. Um `async` roda **inline até o primeiro `await` que realmente espera**, então a parte síncrona de um listener corre na thread de quem publicou. **Não medi** |

## Ponto de parada

- Se o B5c, medido, **não** atrasar a requisição com um listener lento, a Executora **não implementa o canal**: relata, e a
  decisão é da Planejadora e da auditoria. O critério da auditoria: **um listener que leva 200 ms atrasa a resposta?** Se não atrasa, a frase "never blocks" do XML doc do `FlowEventPublisher` é conferida, ou corrigida se for mais forte do que o código (D-238).
- Se o canal mudar a **ordem** dos eventos por trace, ou perder evento no uso normal, para.
- Se o B5b pedir que a validação **derrube o app na partida** onde hoje ele sobe, a regra é: configuração **explicitamente
  inválida** (um padrão nulo ou vazio) falha na partida com mensagem acionável, como o validador já faz com as outras. Qualquer
  outra coisa, para e pergunta.

## Tarefa 65.0: reproduzir e medir, antes de corrigir

Teste de host real, visto falhando antes:

1. **B5a.** Um `ActivityListener` de teste que faz o callback lançar (um span com um valor que quebra o NetApiFlow, e um
   `FlowActivityListener` com a dependência quebrada): a requisição **responde normalmente** e o erro é logado. Hoje: confere
   se a exceção chega à requisição, e relata o que achou. **O teste da Fase 55** `InvokeAsync_WhenCompletingTheTraceThrows_ReleasesTheRequestFromAContextCapturedDuringIt` afirma `Should.ThrowAsync<InvalidOperationException>`, ou seja, fixa que um assinante de `TraceCompleted` que lança chega ao usuário. Quando esta tarefa puser o `try/catch` nos callbacks, a exceção deixa de chegar, e esse teste muda no mesmo commit: a requisição responde, e a liberação do contexto capturado continua conferida (D-271).
2. **B5b.** `IncludePaths("/x", null!)`: hoje a primeira requisição dá `NullReferenceException`. Depois: o app **não sobe**,
   com uma mensagem que diz qual padrão está inválido.
3. **B5c.** Um `IFlowEventListener` de teste cuja parte síncrona demora 200 ms. A latência de uma requisição com esse
   listener registrado, contra sem ele. **O número vai no relatório.** Se não houver diferença, ponto de parada.

## Tarefa 65.1: `try/catch` com log

Nos callbacks do `ActivityListener` e na parte inicial do middleware: capturar, logar (a mensagem diz que a instrumentação
falhou e que a requisição não foi afetada) e seguir. **Sem** engolir `OperationCanceledException` por engano.

## Tarefa 65.2: o validador

`NetApiFlowOptionsValidator` rejeita padrão nulo, vazio ou só de espaços em `IncludePaths`, `ExcludePaths` e nos outros
padrões que a opção aceita, com a mensagem acionável. O que já era válido continua válido.

## Tarefa 65.3: o canal do `FlowEventPublisher`

O `Publish` só **escreve** num `Channel` limitado, e **um consumidor em background** entrega os eventos aos listeners, um
por vez, na ordem. O limite do canal é o da fila de SSE que as regras já fixam (5000, `.claude/rules/tracing.md`). A Executora confere se essa fila é a mesma que fica entre o publicador e o SSE, e, se for outra, o canal novo usa o mesmo valor. Cheio:
**descarta e conta** no `Meter("NetApiFlow")`, nunca bloqueia nem lança. O consumidor é um `BackgroundService`, e **segue a
regra da Fase 61**: nenhuma exceção escapa dele.

## Tarefa 65.4: os testes

Os três testes da 65.0, agora **verdes**, mais: a ordem dos eventos de uma trace se mantém, um listener que lança não impede os
outros de receber, o canal cheio descarta e conta, e o consumidor para limpo no desligamento.

## Fora de escopo

- Mudar o contrato dos eventos SSE ou dos listeners.
- Os limites fixos virarem `LimitsOptions` (0.7.0, D-236).

## Critério que vale para a fase inteira

- Build, testes e formato limpos nos dois TFMs. `AotCoreVerification` com zero aviso.
- Os três achados com teste **visto falhando antes**, e a latência medida antes e depois (B5c).
- `CHANGELOG.md`, em inglês: `### Fixed` (os callbacks, o validador) e `### Changed` (a entrega dos eventos fora da thread da
  requisição).

## Versionamento e fluxo

- `0.6.0`. Branch `bugfix/65-nunca-derrubar` a partir de `develop` atualizado, **depois** das Fases 53 e 55.
- Commits pequenos, em inglês, uma linha, sem linha de coautor. Abrir a PR e parar.

## Relatório final

Para cada achado: o teste, a saída dele falhando antes e passando depois. A latência do B5c antes e depois.

## Resultado da fase (PR #471, 22/11/2026)

Entregue em `develop` (`c13a2a5`, head `cd24865`) e fechada pela auditoria, em duas rodadas.

- **B5a:** os quatro callbacks do `FlowActivityListener` e a abertura e o fechamento do `NetApiFlowMiddleware` capturam a
  exceção, logam no máximo uma vez a cada 30 segundos e seguem. Se a abertura falha, a requisição roda sem trace. A exceção
  do próprio usuário continua chegando a ele.
- **B5b:** o validador recusa padrão nulo, vazio ou só de espaços em `IncludePaths` e `ExcludePaths`, e o app não sobe. A
  65.0 mostrou que o defeito era outro do que o plano descrevia: o padrão nulo não chegava à requisição, só deixava toda
  requisição sem trace em silêncio. Na `0.5.3`, o vazio valia como `/` e o só de espaços nunca casava. Por isso a entrada
  do CHANGELOG está em `### Changed`, em negrito, e não em `### Fixed`.
- **B5c, com desvio da 65.3 decidido pela auditoria:** uma fila limitada de 5000 e um consumidor por listener, no lugar do
  consumidor único. A fila única da primeira rodada fazia um listener assíncrono de 50 ms segurar os outros. Na sonda, o
  listener que fazia o papel do painel ao vivo recebeu 332 de cerca de 600 eventos, com 11,9 s de atraso, contra 500 de 500
  com 66 ms na `0.5.3`. A ordem vale dentro de cada listener, e um listener lento só descarta o que é dele. No desligamento,
  a entrega para no token, e o que sobra na fila não é entregue.
- **Vistos falhando antes (corpo da PR e logs do CI):** as runs 44976182008 e 44977383272, na primeira rodada, e as runs
  44979526035 e 44979699685, na segunda. Dois testes de host foram vistos vermelhos só localmente, porque nenhuma run do CI
  chegou a eles enquanto estavam vermelhos. Os equivalentes unitários estão na primeira run.
- **Latência do B5c,** medida pela Executora: com um listener de 200 ms, o `GET /ping` caiu de uma mediana de 811,4 ms para
  0,5 ms. Sem o listener, ficou entre 0,4 e 0,5 ms.
- **O registro de guardas** ganhou 12 linhas `B5*`. O `expected-differences.json` não ganhou linha, porque nenhum cenário
  da comparação usa padrão inválido.
- **Pacote real, pela auditoria, contra a `0.5.3`:** a etapa com tag reservada do tipo errado dava 500 e agora dá 200. O
  listener síncrono de 200 ms fazia o `/ping` levar 410 ms, e agora leva de 3 a 14 ms. O app com padrão inválido não sobe,
  com a mensagem certa. 91 de 91 traces completas e nenhum evento fora de ordem. Depois da correção, o listener de 50 ms
  não segura mais os outros (700 de 700 eventos, 6 ms de atraso), e o desligamento caiu de 10 s e 23 s para 0,3 s e 0,4 s.
- **O achado da fila única** veio do revisor independente da auditoria, sem a hipótese dela, e ela o reproduziu.

**Não verificado pela Planejadora,** pelo regime mínimo (D-272): o diff, as medições e o `AotCoreVerification` com zero
aviso. Este último roda no `build-test`, que ficou verde. Ela leu o corpo da PR, os checks, o CHANGELOG e o `guards.json`.
