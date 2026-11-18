# ADR-0023: três TFMs (`net8.0;net10.0;net11.0`), fechando o furo do ADR-0003

## Status
Aceito, substitui a cláusula de sucessão do ADR-0003 (ADR-0003 continua valendo no resto)

## Discussion
[#270](https://github.com/Douglasproglima/netapiflow/discussions/270)

## Contexto

O ADR-0003 previa trocar `net8.0` por `net12.0` quando `net8.0` saísse de suporte. `net8.0` já
saiu de suporte (confirmado pela D-134) e `net12.0` só deve lançar em novembro de 2027: quase um
ano de lacuna que o ADR original nunca previu, deixando o projeto sem um plano real pra esse
período.

A Fase 38 investigou `net11.0` antes de qualquer decisão (molde das Fases 19/23/27, sem código de
produção): o núcleo compila e a suíte inteira passa em `net11.0`, idêntica a `net10.0`, sem
regressão de hierarquia de span, incluindo o cenário mais sensível da base de testes
(`Task.WhenAll` com 50 chamadas concorrentes). O `net11.0` (ASP.NET Core 11) passou a emitir, por
padrão, doze atributos de semantic convention do OpenTelemetry na própria `Activity` HTTP nativa;
confirmado que isso não afeta o NetApiFlow hoje (`FlowActivityListener` nunca escuta a fonte
`Microsoft.AspNetCore`, e `FlowSpan.Tags` só recebe o que `FlowStep.Tag(...)` grava
explicitamente), mas fica registrado como risco latente pra qualquer código futuro que um dia
pense em ler tag dessa `Activity` (ver nota em `.claude/rules/tracing.md`).

Quatro opções levantadas pela Fase 38, nenhuma bloqueada por impedimento técnico:

1. Manter `net8.0;net10.0` até `net12.0` sair: nada quebra agora, mas estende o período servindo
   um alvo fora de suporte por mais um ano.
2. Trocar `net8.0` por `net11.0` agora: fecha o furo de suporte, mas quebra a instalação de quem
   está em `net8.0` (justamente o público mais conservador, que ficou em LTS) numa troca de TFM
   que não é SemVer-neutra pra ele.
3. Três TFMs juntos, `net8.0;net10.0;net11.0`: ninguém perde nada, `net11.0` ganha asset próprio,
   custo é CI/pack/teste rodando uma terceira matriz.
4. Adiar de novo, decidir quando `net12.0` sair: não fecha nada, só empurra a mesma pergunta.

## Decisão

Opção 3. `Directory.Build.props` ganha `net11.0` como terceiro `TargetFramework`, ao lado de
`net8.0;net10.0`, que continuam como estão. `net8.0` sai da matriz só quando `net12.0` lançar e
puder substituí-lo de verdade (a mesma cláusula de sucessão do ADR-0003, agora mirando `net12.0`
em vez de pular direto de `net8.0`).

Implementação cabe a uma fase própria, planejada depois deste ADR: adicionar o TFM, `#if` onde a
API divergir (seguindo o padrão já estabelecido pelo `net10.0`/`net8.0`), CI/pack/teste rodando a
terceira combinação, `README.md` atualizado.

## Consequências

- Mais fácil: usuário em `net11.0` resolve um asset dedicado, sem cair no caminho manual de SSE
  nem em nenhuma outra divergência pensada só pra `net8.0`/`net10.0`. `net8.0` continua recebendo
  pacote normalmente, sem nenhuma mudança de comportamento pra quem está nele hoje.
- Mais difícil: toda matriz de CI (`build-test` nos dois sistemas), `dotnet pack` e a suíte de
  testes passam a rodar uma terceira vez por execução, aumentando o tempo de pipeline outra vez
  (mesmo custo já aceito no ADR-0003 ao somar o segundo TFM).
- Não reabrir sem justificativa técnica nova: `net8.0` só sai da lista quando `net12.0` estiver
  disponível e puder substituí-lo sem quebrar ninguém, não antes.
- Risco latente registrado, não resolvido aqui: os doze atributos de semantic convention na
  `Activity` HTTP nativa do `net11.0`/ASP.NET Core 11 incluem `url.query` e `url.path`, que podem
  conter dado sensível. Hoje o NetApiFlow nunca lê essa `Activity`, então não há violação do
  ADR-0007. Qualquer código futuro que passe a copiar tag dela precisa primeiro confirmar que não
  está recopiando `url.query`/`url.path` sem sanitização, mesma régua que já vale pra qualquer
  outra fonte externa.
