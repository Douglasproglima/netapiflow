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

- **IA Planejadora**: escreve `docs/plano/`, `docs/adr/`, `.claude/rules/` e `.claude/skills/`. Nunca escreve
  `src/`, `tests/` ou `samples/`. Revisa cada fase entregue (`/revisar-fase <N>`) antes de liberar a próxima, no
  **regime mínimo** (D-272): confere os critérios "Pronto quando" do plano, o contrato, a linha do `guards.json`, a tabela de
  consumidores e o README e o CHANGELOG contra o corpo da PR e os logs do CI, sem reler o diff e sem rodar nada, e diz o que não
  verificou. A leitura de código e as sondas contra o pacote publicado são da auditoria.
- **IA Executora**: lê `docs/plano/fase-N-*.md` e implementa. Não altera `docs/plano/` nem
  `docs/adr/`. Decisão nova que valha além da tarefa corrente é relatada à Planejadora, que
  decide se vira ADR. A Executora não cria ADR sozinha.
- **EXECUTORA-RELEASE** (D-272): uma sessão de Executora só para o release. Faz os passos de código do `/release` (a versão, o
  CHANGELOG e, depois da publicação, a lista de diferenças esperadas, o `previous-published-version.txt` e os
  `PublicAPI.*.txt`), abre as PRs e para. Os merges, a tag, o Release, os advisories e os checks da `main` são da auditoria, e a
  Planejadora só mantém a skill e registra.

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

## Quando um achado de auditoria vira hotfix, não roadmap

Inspeção de código e leitura de documentação não têm fim: sempre dá pra olhar mais fundo e achar
mais uma coisa. Rodada de auditoria externa atrás de rodada de auditoria externa, cada uma achando
algo que a anterior não via, prova isso na prática (D-141).

Critério travado: só vira hotfix um achado com **falha reproduzida de verdade**. Uma exceção real,
um comando que qualquer um roda e vê quebrar, um cenário real que falha, não uma leitura de código
ou de changelog que sugere que algo pode dar errado. O exemplo que fixou a régua foi o
`MissingMethodException` do `NetApiFlow.MediatR` contra `MediatR` 12.5+ (D-140): reproduzido do
zero contra o pacote publicado de verdade, não aceito só pelo relato da auditoria.

Achado sem essa reprodução, por mais correto que pareça, vira item de `docs/plano/roadmap-valor.md`
e espera o ciclo normal de fase. Não é questão de gravidade: um achado teórico grave ainda é
roadmap.

**Reproduzir é condição necessária para hotfix, não suficiente (D-205).** Hotfix só para uma de duas
coisas: regressão nova, um comportamento que funcionava na versão anterior e quebrou; ou falha de
severidade alta, que é perda de dado, exposição grave ou **derrubar a API de produção do usuário** (D-235). Defeito antigo, mesmo reproduzido contra o
pacote publicado, vai na próxima minor. O motivo é que, desde a `0.4.0`, cada versão foi conferida
só pelo próprio diff, e cada furo antigo achado depois virou hotfix. O resto dos critérios do
hotfix (PATCH, sem mudança de API pública) continua valendo para os dois casos.

## A `0.6.0` só sai depois da auditoria de base (D-205)

A `AUDITORIA-16-11` fecha, antes da `0.6.0`, uma auditoria de base do sistema inteiro, feita por
inventário e não por diff: o README frase a frase num app novo, o mapa de cobertura do CI, a matriz
de autorização, a matriz de integrações, todas as telas e a demo. `PathBase` e proxy reverso entram
no inventário. O inventário e o fechamento são da auditoria, e a Planejadora não fecha nenhum item no
lugar dela. Se a regra vale também para as minors seguintes, é a auditoria quem diz.

## Quando um item do roadmap pode entrar num loop autônomo da Executora

A Executora nunca cria ADR nem escreve `docs/plano/`/`docs/adr/` sozinha (ver acima). Um item do
roadmap que ainda depende de uma decisão de arquitetura, de produto, ou de uma fase ainda não
desenhada não pode entrar num loop sem supervisão: ou a Executora trava perguntando pro vazio, ou
decide sozinha algo que devia passar pela Planejadora. Antes de liberar qualquer lote do roadmap
para rodar sem supervisão, a Planejadora separa o que é mecânico (fase já escrita, sem decisão em
aberto, precedente já estabelecido) do que exige decisão primeiro, e só o primeiro grupo vai para o
loop (D-142).

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
