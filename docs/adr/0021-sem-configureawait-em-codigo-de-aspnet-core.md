# ADR-0021: `src/` não usa `ConfigureAwait(false)`

## Status
Aceito

## Discussion
A criar na PR que mergear este ADR (categoria "Architecture Decisions"), junto do resto do hotfix
`0.4.1`.

## Contexto

Uma auditoria externa do pacote `0.4.0` apontou que `ConfigureAwait` não aparece em nenhum arquivo
de `src/`. O achado é verdadeiro, e para biblioteca publicada a recomendação clássica é mesmo
`ConfigureAwait(false)` em todo `await`. A primeira versão do plano da Fase 34 aceitou o achado e
mandou espalhar a chamada por `src/`.

A revisão de uma segunda IA sobre esse plano (D-134) derrubou a tarefa antes dela ser
implementada, por um motivo concreto: a recomendação existe para biblioteca de propósito geral,
que pode acabar rodando sob um `SynchronizationContext` (WinForms, WPF, ASP.NET clássico). Não é
o caso aqui. O núcleo só tem `FrameworkReference` para `Microsoft.AspNetCore.App` (D-02, núcleo
sem dependência externa), então é código de ASP.NET Core por construção, e ASP.NET Core não
instala `SynchronizationContext` nenhum. O próprio time do ASP.NET Core não usa
`ConfigureAwait(false)` no código do framework, pela mesma razão.

O custo não era zero: cerca de cinquenta ocorrências, diff grande num hotfix, e um risco real de
deixar um `await` sem a chamada no meio de todos os outros. Uma ausência parcial é pior que uma
ausência inteira, porque passa a parecer erro em vez de decisão.

## Decisão

`src/` não usa `ConfigureAwait(false)`. A ausência é deliberada, não esquecimento.

`CA2007` fica desligada explicitamente no `.editorconfig` da raiz, com um ponteiro para este ADR
ao lado. A regra está desligada por padrão hoje, mas uma versão futura do SDK pode mudar o que
entra em `latest-recommended`, e aí ela quebraria o build sem ninguém ter decidido nada.

O que a auditoria achou de verdade não foi a ausência da chamada. Foi a ausência não estar escrita
em lugar nenhum, o que a deixa indistinguível de descuido para quem lê o código de fora. É isso
que este ADR resolve.

## Consequências

- Um pacote novo em `src/` que não seja código de ASP.NET Core sai do alcance desta decisão. Se
  algum dia existir um que rode fora do framework, ele precisa da discussão de novo, não da
  resposta daqui por herança.
- Quem ler o código esperando `ConfigureAwait(false)` por hábito tem onde descobrir por que não
  está lá: a linha de `CA2007` no `.editorconfig` aponta para cá.
- Não reabrir por recomendação genérica de blog ou de analisador. Reabrir só se o núcleo deixar de
  ser acoplado ao ASP.NET Core, que é a premissa inteira desta decisão.
