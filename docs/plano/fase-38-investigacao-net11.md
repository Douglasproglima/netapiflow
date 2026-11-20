# Fase 38: investigação `net11.0` e o furo de doze meses do ADR-0003

Item 19 do `roadmap-valor.md` (D-133/D-134). Mesmo molde das Fases 19, 23 e 27: investigar antes
de escrever qualquer código de produção, entregar um relatório, não uma decisão. A Executora não
cria ADR (ver `.claude/rules/git-workflow.md`): o resultado desta fase vira insumo pra Planejadora
decidir se o ADR-0003 é substituído, e como. Nenhum commit desta fase toca `src/` fora de um spike
descartável, se necessário pra confirmar algo na prática (mesmo padrão da tarefa 27.1, aposentada
depois do ADR correspondente).

## Tarefa 38.1: compilar e rodar a suíte inteira em `net11.0`

Confirmar, com SDK `net11.0` real instalado (preview ou RC, o que estiver disponível no momento),
se `NetApiFlow.slnx` compila sem erro acrescentando `net11.0` como terceiro TFM num projeto de
spike isolado (não no `Directory.Build.props` real, que continua `net8.0;net10.0` até esta
investigação concluir), e se a suíte de testes passa sem alteração de comportamento.

**Pronto quando:** relatório diz sim ou não pra cada um (compila, testa), com o erro ou aviso
exato quando a resposta for não.

## Tarefa 38.2: atributos nativos de semantic convention do OpenTelemetry em `net11.0`

Confirmar contra a `Activity` HTTP real (mesma usada como `TraceId` do NetApiFlow, ADR-0002) se o
ASP.NET Core 11 passou a emitir, por padrão, atributos de semantic convention do OpenTelemetry
nessa mesma `Activity`, conforme as release notes oficiais já citadas na D-134. Se sim, confirmar
se esses atributos colidem ou duplicam alguma tag que o NetApiFlow já grava nos próprios spans
(`FlowSpan.Tags`, Fase 20), ou se convivem sem conflito (namespaces de tag diferentes).

**Pronto quando:** relatório lista os atributos novos observados (se houver) e classifica cada um
como "sem conflito" ou "colide com `<tag existente>`", com evidência (captura real da `Activity`,
não só leitura de changelog).

## Tarefa 38.3: `Activity.Current` via `ExecutionContext` sob runtime-async

Confirmar se a propagação de `Activity.Current` por `ExecutionContext` (mecanismo do qual todo o
rastreamento de hierarquia de span depende, desde a Fase 1) sobrevive ao runtime-async, já ligado
por padrão no shared framework do ASP.NET Core em `net11.0` (confirmado nas release notes oficiais
citadas na D-134), mesmo sem o NetApiFlow optar pelo preview dele. Reaproveitar o spike de stress
da D-92 (Fase 19, 3800+ `Activity` concorrentes) como ponto de partida, rodando contra `net11.0`.

**Pronto quando:** relatório confirma, com evidência real (não leitura de documentação), se a
hierarquia de span continua correta sob `net11.0` com runtime-async ligado, incluindo o caso de
`Task.WhenAll` que já é o cenário mais sensível da base de testes atual.

## Tarefa 38.4: o que fazer com `net8.0` agora

A cláusula original do ADR-0003 ("trocar `net8.0` por `net12.0` quando `net8.0` sair de suporte")
não tem como ser cumprida: `net8.0` já saiu de suporte (confirmado na D-134) e `net12.0` só deve
lançar em novembro de 2027, quase um ano de lacuna que o ADR nunca previu. Levantar as opções
reais (manter `net8.0;net10.0` até `net12.0` sair, mesmo fora de suporte oficial da Microsoft;
trocar `net8.0` por `net11.0` apesar de `net11.0` não ser LTS; alguma terceira opção), com o
trade-off de cada uma (suporte oficial vs. matriz de TFM que o resto do ecossistema .NET já usa),
sem escolher: a escolha e o ADR de substituição são da Planejadora, depois deste relatório.

**Pronto quando:** relatório lista as opções reais com trade-off de cada uma, sem recomendação
forçada numa direção só, dados os resultados das tarefas 38.1 a 38.3.

## Fora de escopo

- Qualquer mudança em `Directory.Build.props`, `global.json`, ou matriz de CI: esta fase só produz
  relatório. Mudar o TFM suportado é decisão da Planejadora, registrada como ADR novo depois do
  relatório, implementada numa fase própria.
- Qualquer código de produção: só spike descartável, se necessário, mesmo tratamento das
  investigações das Fases 19/23/27.

## Aceite da fase

Relatório escrito cobrindo as quatro tarefas, com evidência real (não leitura de documentação sem
reprodução) em cada afirmação que permitir reprodução. Nenhum arquivo em `src/`, `tests/` ou
`samples/` alterado de forma permanente; qualquer spike descartável removido antes do relatório
final.

## Ao terminar

Relatório completo pra Planejadora, no formato usual de resumo de fase: o que foi investigado,
como cada achado foi confirmado, e as opções levantadas na tarefa 38.4. A Planejadora decide o
futuro do ADR-0003 a partir daqui; esta fase não decide.
