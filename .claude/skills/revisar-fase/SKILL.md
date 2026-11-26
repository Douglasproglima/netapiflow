---
name: revisar-fase
description: Confere os critérios de aceite de uma fase do plano contra o corpo da PR e os logs do CI, no regime mínimo (D-272), antes de liberar a fase seguinte. Use ao receber o resumo de fim de fase ou o pedido de revisão de uma PR da Executora.
argument-hint: "<N>"
---

Revise a Fase $ARGUMENTS do NetApiFlow no **regime mínimo** (D-272). A Planejadora confere os critérios contra o que a PR diz e
contra o que o CI mostra. **Ela não relê o diff e não roda nada:** a leitura de código e as sondas contra o pacote publicado são
da `AUDITORIA`, que tem a decisão final de qualidade.

1. Leia `docs/plano/fase-$ARGUMENTS-*.md` inteiro. Liste cada tarefa e seu "Pronto quando".
2. Leia o corpo da PR e o resumo de fim de fase da Executora (seção 16.7 do `netapiflow-prompt.md`): o que foi feito, como
   verificar, o que ficou pendente.
3. Para cada tarefa da fase, confira contra o corpo da PR e os logs do CI (`gh run view --job <id> --log`):
   - o "Pronto quando" tem a evidência dita (o teste visto falhando antes, a saída, o link do CI), e o log do CI mostra o mesmo
     número e a mesma mensagem;
   - para tarefa que toca `docs/plano/contratos.md`, o corpo da PR diz o que mudou no contrato, e o que diz bate linha a linha com ele;
   - existe a linha em `tests/regression-guards/guards.json` para cada defeito corrigido, ou a PR diz por que não tem;
   - a tabela "Consumidores do contrato" está preenchida, e `expected-differences.json`, `PublicAPI.Unshipped.txt`, o README e o
     `CHANGELOG.md` dizem o mesmo que a PR.
4. Confira `docs/plano/riscos.md`: todo risco cuja coluna "Fase" é a fase revisada precisa ter mudado para `Mitigado`, com a
   prova citada, ou continuar `Planejado` com o motivo dito (por exemplo, esperando a segunda passada da auditoria).
5. Decida: **aprovado** (avise a auditoria, que decide e faz o merge, e atualize `docs/plano/README.md` depois dele) ou
   **reprovado** (liste exatamente o que falta, tarefa por tarefa, sem vago).
6. Se a Executora tomou uma decisão nova durante a fase que vale além dela, registre como ADR em `docs/adr/` (a Planejadora
   escreve o ADR, não a Executora) e referencie em `docs/plano/decisoes.md`.
7. No aviso à auditoria e no registro, diga o que **não** foi verificado (o código, a execução local, a comparação com o pacote
   publicado). Nunca afirme um número que não tenha lido de um log ou de um arquivo.

Nunca aprovar fase com critério de aceite pendente "porque o resto está bom". Critério de aceite é binário.
