---
name: revisar-fase
description: Confere os critérios de aceite de uma fase do plano contra o código real entregue pela Executora, antes de liberar a fase seguinte. Use ao receber o resumo de fim de fase da Executora.
argument-hint: "<N>"
---

Revise a Fase $ARGUMENTS do NetApiFlow. Este é o papel central da IA Planejadora: não confiar no
resumo de quem implementou, confirmar cada critério rodando o comando real ou lendo o código real.

1. Leia `docs/plano/fase-$ARGUMENTS-*.md` inteiro. Liste cada tarefa e seu "Pronto quando".
2. Leia o resumo de fim de fase entregue pela Executora (seção 16.7 do `netapiflow-prompt.md`):
   o que foi feito, como verificar, o que ficou pendente.
3. Para cada tarefa da fase, confira a evidência de verdade:
   - Rode os comandos de build/teste indicados (`dotnet build`, `dotnet test`, `npm run test:ci`,
     conforme a fase).
   - Para critério que exige cenário específico (ex.: "span dentro de `Task.WhenAll` tem o pai
     certo"), leia o teste que prova isso e confirme que ele testa o cenário descrito, não um
     substituto mais fácil.
   - Para tarefa que toca `docs/plano/contratos.md`, confira linha a linha que a implementação
     bate com o contrato, não só "parece compatível".
4. Rode (ou peça para rodar) o agent `code-reviewer` e, se a fase estiver indo para PR, o
   `merge-auditor`.
5. Confira `docs/plano/riscos.md`: todo risco cuja coluna "Fase" é a fase revisada precisa ter
   mudado para `Mitigado`, com a prova (arquivo de teste) citada. Se não mudou, é critério de
   aceite não cumprido, não um detalhe para depois.
6. Decida: **aprovado** (atualize a tabela de estado em `docs/plano/README.md` para a próxima
   fase) ou **reprovado** (liste exatamente o que falta, tarefa por tarefa, sem vago).
7. Se a Executora tomou uma decisão nova durante a fase que vale além dela, registre como ADR em
   `docs/adr/` (a Planejadora escreve o ADR, não a Executora) e referencie em
   `docs/plano/decisoes.md`.

Nunca aprovar fase com critério de aceite pendente "porque o resto está bom". Critério de aceite
é binário.
