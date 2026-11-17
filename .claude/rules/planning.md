# Onde encontrar o quê

- Identidade do produto, stack, comandos: `CLAUDE.md`.
- Fonte única de verdade do produto (objetivo, não objetivos, arquitetura completa):
  `netapiflow-prompt.md` (fora deste repositório, ver `docs/plano/README.md`).
- Plano mestre, estado de cada fase: `docs/plano/README.md`.
- Tarefas de uma fase específica, com critério de pronto: `docs/plano/fase-N-*.md`.
- Contratos (API pública do núcleo, evento SSE, API REST): `docs/plano/contratos.md`. Não
  implementar divergindo deles sem avisar a Planejadora.
- Decisões travadas, com justificativa: `docs/plano/decisoes.md`.
- Riscos técnicos e como cada um é provado por teste: `docs/plano/riscos.md`.
- Decisão estrutural que vale para o projeto todo: `docs/adr/` (prefixo `ADR-`). Não contradizer
  ADR aceito sem escrever um ADR novo que o substitua. Todo ADR aceito ganha uma Discussion
  correspondente no GitHub (categoria "Architecture Decisions"), criada na mesma PR, como rastro
  público e espaço de comentário da comunidade (D-62, migrado de Issue para Discussion pela
  D-110).
- Regra que vale para todo código, sempre: `.claude/rules/`.

## Papéis

- **IA Planejadora**: escreve `docs/plano/`, `docs/adr/`, `.claude/rules/`. Nunca escreve
  `src/`, `tests/` ou `samples/`. Revisa cada fase entregue (`/revisar-fase <N>`) antes de
  liberar a próxima.
- **IA Executora**: lê `docs/plano/fase-N-*.md` e implementa. Não altera `docs/plano/` nem
  `docs/adr/`. Decisão nova que valha além da tarefa corrente é relatada à Planejadora, que
  decide se vira ADR. A Executora não cria ADR sozinha.

## Fases

- A Executora só avança de fase quando todos os critérios de aceite da fase atual passarem
  (seção 13 do prompt). Não pular fase nem critério.
- Ao fim de cada fase, a Executora entrega um resumo: o que foi feito, como verificar (comandos
  exatos), o que ficou pendente (seção 16.7 do prompt).
- Conflito entre um `docs/plano/fase-N-*.md` e o `netapiflow-prompt.md`: **o prompt prevalece**.
  Parar e relatar, não improvisar (seção 16.6 do prompt).

## Pendências

Risco marcado como `Planejado` (não `Mitigado`) em `docs/plano/riscos.md` não bloqueia a fase,
a menos que o próprio arquivo de fase diga o contrário. Mas a tarefa que prova a mitigação
precisa estar na fase indicada na coluna "Fase" daquele risco. Se chegar lá sem a prova, é
critério de aceite não cumprido.

## Citar `roadmap-valor.md` dentro de `decisoes.md`

`decisoes.md` é append-only; `roadmap-valor.md` é vivo, a Planejadora reordena e renumera a
tabela sempre que um item é entregue ou a prioridade muda. Uma entrada de `decisoes.md` que cita
só "item N do roadmap" aponta pra uma posição que pode já ter mudado quando alguém ler a decisão
meses depois, e aí a referência leva pro item errado sem avisar.

Toda entrada nova de `decisoes.md` que citar um item do `roadmap-valor.md` cita o número junto
com um trecho curto do próprio texto do item (ex. "item 18 do roadmap, renomear `NetApiFlow`
por causa do CA1724"), não só o número sozinho. O trecho sobrevive à renumeração, o número não.
Entradas antigas que só têm o número (várias existem, de antes desta regra) não precisam de
correção retroativa.
