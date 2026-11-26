# Fase 68: o essencial de acessibilidade por teclado (D-236, B13)

**Estado (25/11/2026):** **entregue em `develop` (#497, squash `3ed8b98`, D-288) e fechada: a auditoria refez com o pacote real.**

A varredura final testou o painel só com o teclado e achou três defeitos que impedem o uso, não detalhes de acabamento. Esta
fase corrige **só o objetivo**, e deixa o resto do teclado (setas no `radiogroup`, link de pular, `Esc` no painel da etapa,
região `live`) para a `0.7.0` (D-236).

Vai no `0.6.0` (D-236). Vem **depois da Fase 57** (as duas mexem no `flow-page`) e antes da Fase 60.

## Os achados e o que a Planejadora conferiu

| # | Achado | Conferi |
|---|---|---|
| B13a | O foco visível dos botões do Angular Material tem contraste de **1,22:1** contra o mínimo de **3:1** da WCAG (não texto) | Não verifiquei. Medido pela varredura |
| B13b | A gaveta "Testar um endpoint" não tem `role="dialog"`, não prende o foco, não fecha com `Esc` e não devolve o foco a quem a abriu | Não verifiquei |
| B13c | O foco cai no `body` em quatro casos. **O pior é o Runner**, que pula o campo obrigatório `id` | Não verifiquei. A varredura listou os quatro, e a Executora os reproduz |

## Ponto de parada

- A cor do foco **vem de `docs/brand/tokens.md`** (ADR-0009). Se o token que a correção precisa não existir, a Executora
  **para** e a Planejadora o escreve. Valor novo inventado só para passar o contraste é proibido (`design-quality.md`, D-76).
- Se a correção do foco mudar o desenho dos botões a ponto de o detector acusar `low-contrast` ou `undersized-ui-text` (as duas
  categorias que bloqueiam), para.
- Se o `Esc` ou a armadilha de foco colidirem com o comportamento de outro componente (o menu, o seletor de endpoint), para e relata.

## Tarefa 68.0: reproduzir e medir, antes de corrigir

Em navegador de verdade, só com o teclado (`Tab`, `Shift+Tab`, `Esc`, `Enter`), nos dois temas e no desktop e no `390x844`:

1. **B13a.** O contraste do indicador de foco de cada tipo de botão do Material, **calculado** a partir dos estilos
   computados (anel e fundo). O número de cada um, contra 3:1.
2. **B13b.** A gaveta: o `role`, onde o foco está ao abrir, se escapa com `Tab`, o que o `Esc` faz, e onde o foco fica ao
   fechar.
3. **B13c.** Os quatro casos em que o foco cai no `body`, cada um com o passo a passo, e o do Runner com o campo `id`.

## Tarefa 68.1: as correções

- **Foco visível:** um anel de foco com contraste **de pelo menos 3:1** contra o fundo, nos dois temas, em todos os botões do
  Material, a partir dos tokens da marca.
- **Gaveta:** `role="dialog"` com `aria-modal` e um nome acessível, o foco **preso** dentro dela, `Esc` fecha, e o foco volta ao
  elemento que a abriu.
- **Foco no `body`:** em cada um dos quatro casos, o foco vai para o elemento certo. No Runner, o foco **chega** ao campo
  obrigatório `id` na ordem natural do formulário.

## Tarefa 68.2: os testes

- Teste de componente para a gaveta: `role`, foco ao abrir, `Tab` não sai, `Esc` fecha, o foco volta.
- Teste de componente para a ordem de foco do Runner com o `id` obrigatório.
- **Navegador de verdade** (Playwright), só com o teclado: o roteiro dos três achados, nos dois temas.
- O contraste do anel de foco **calculado** num teste, com o limite de 3:1.
- O detector (`design-quality.md`) nas telas afetadas, com a contagem de avisos antes e depois.

## Resultado (D-288, 25/11/2026)

Entregue em `develop` na #497 (squash `3ed8b98`). O relato é da Executora, no corpo da PR, e da auditoria, que refez com o pacote real `0.6.0-audit.15`, só pelo teclado, em 390, 768, 1023, 1440 e 1920, nos dois temas. A Planejadora não repetiu as medições.

- **B13a:** o contraste do foco dos botões do Material era de 1,09 a 1,44 para 1. O anel de 2 px em `--naf-focus` dá 4,23 para 1 no escuro e 6,49 no claro, e 3,85 e 6,10 no item de menu.
- **B13b:** a gaveta é um diálogo (`role`, `aria-modal`, nome pelo título), prende o foco, fecha com `Esc` e devolve o foco a quem a abriu. Antes, 17 de 24 `Tab` caíam fora, e agora são 0.
- **B13c:** cinco casos, e não os quatro do plano: o Runner ao escolher o endpoint, ao voltar à lista e ao remover um cabeçalho, a gaveta ao fechar e o Histórico ao ordenar uma coluna.
- **Fora da 68, por decisão da Planejadora:** o foco ao abrir um trace por um link, que fica no topo da página como numa carga completa. Item 60 do roadmap.
- **Visto vermelho:** o `5b32cf6` falhou com sete testes do painel e o do script de foco. A mutação do `aa2ee39` derrubou cinco de sete no teste da gaveta, que nasceu verde. Oito linhas novas no `guards.json`, 184 guardas.
- **O detector:** zero `low-contrast` e zero `undersized-ui-text` antes e depois, em 20 varreduras cada.
- **O roteiro do navegador é manual,** em `tests/manual-verification/Fase68AcessibilidadeVerification/`, com as capturas de antes e depois.
- **Achado da auditoria na conferência, fora da 68:** a gaveta do Runner no celular deixa o botão de enviar quase todo fora da tela. É a Tarefa 60.12, por decisão do dono (D-289).
- **Aberto:** nada da fase. A auditoria a fechou.

## Fora de escopo

- Setas no `radiogroup`, link de pular para o conteúdo, `Esc` no painel da etapa, região `live`: `0.7.0`.
- Leitor de tela além do que os testes acima cobrem.

## Critério que vale para a fase inteira

- `npm run lint`, `npm run format:check`, `npm run test:ci` e `npm run build` limpos.
- Os três achados **vistos falhando antes**, com captura de tela, e passando depois.
- Zero achado de `low-contrast` e de `undersized-ui-text` no detector.
- `CHANGELOG.md`, em inglês, `### Fixed`.

## Versionamento e fluxo

- `0.6.0`. Branch `bugfix/68-acessibilidade` a partir de `develop` atualizado, **depois** da Fase 57.
- Commits pequenos, em inglês, uma linha, sem linha de coautor. Abrir a PR e parar.

## Relatório final

Os números de contraste antes e depois, por botão. O roteiro do teclado antes e depois, nos dois temas. A contagem de avisos
do detector.
