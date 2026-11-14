# Estilo de escrita

Vale para texto corrido em português (`docs/plano/`, `docs/adr/`, comentário interno, mensagem de
commit de governança) e para texto em inglês visível a quem usa o pacote (README, README.pt-BR.md
é o próprio texto em português). Não vale para código, comando nem flag: flag de comando
(`--verify-no-changes`) é sintaxe, pode usar hífen duplo.

## Regras (texto em português e em inglês dos READMEs)

- Frases curtas. Uma ideia por frase.
- Não usar travessão (`—`) nem hífen duplo (`--`) para colar duas ideias numa frase, em nenhum
  dos dois idiomas (D-48, atualizado de uma isenção anterior do inglês). Trocar por ponto final,
  vírgula, ou reformular a frase.
- Parágrafo curto. Um assunto por parágrafo.
- Escrever como quem explica para um colega. Sem linguagem de relatório corporativo.
- Tom humano, natural, próximo, como se uma pessoa estivesse conversando, não preenchendo um
  formulário. Vocabulário simples. Um toque de empatia quando fizer sentido (reconhecer que algo
  foi difícil, confuso ou arriscado, por exemplo), sem perder a objetividade.
- Afirmação técnica tem fonte real (seção do prompt, ADR, número medido) ou não entra no texto.

## Regras (texto em inglês: comentário XML, log, exceção)

- Direto, sem floreio. Frase curta também ajuda em inglês.
- `<summary>` de comentário XML descreve o que o membro faz, não repete o nome.
- Mensagem de exceção é acionável: diz o que está errado e, quando possível, como corrigir.

## Evitar (nos dois idiomas)

- "Serve como", "funciona como", "representa" quando "é" resolve.
- "Não só X, mas também Y".
- Elogio vago ("robusto", "poderoso", "incrível").
- Dizer que algo é importante sem dizer por quê.
- Negrito em todo item de lista.
- Jargão desnecessário: se uma palavra simples diz o mesmo, usa a simples.
- Frase genérica que serviria pra qualquer projeto. Se a frase não ficar errada trocando
  "NetApiFlow" por outro nome qualquer, ela não disse nada de específico.
- Lista engessada só porque o assunto tem vários itens: cada item ainda precisa soar como frase
  de gente, não como campo de formulário preenchido (ex. a seção "Consequências" de um ADR, hoje
  em fragmentos telegráficos, é candidata a virar frase de verdade).
- Citar fase, etapa de plano ou número de ADR em comentário de código (regra de ouro do
  `CLAUDE.md`, sem exceção desde D-87).

## Antes de entregar texto

Rodar `grep -rn "—" <arquivos alterados>` e revisar cada ocorrência, em português ou em inglês de
README.
