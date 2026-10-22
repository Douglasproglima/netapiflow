# Estilo de escrita

Vale para texto corrido em português: `docs/plano/`, `docs/adr/`, comentário interno, mensagem de
commit de governança. Não vale para código, comando, flag, nem para texto em inglês (README,
comentário XML, log, mensagem de exceção, ver `.claude/rules/dotnet-core.md` e D-02 em
`docs/plano/decisoes.md`).

## Regras (texto em português)

- Frases curtas. Uma ideia por frase.
- Não usar travessão (`—`) nem hífen duplo (`--`) para colar duas ideias numa frase. Trocar por
  ponto final ou vírgula. Flag de comando (`--verify-no-changes`) é sintaxe, pode.
- Parágrafo curto. Um assunto por parágrafo.
- Escrever como quem explica para um colega. Sem linguagem de relatório corporativo.
- Afirmação técnica tem fonte real (seção do prompt, ADR, número medido) ou não entra no texto.

## Regras (texto em inglês: README, XML doc comment, log, exceção)

- Direto, sem floreio. Frase curta também ajuda em inglês.
- `<summary>` de comentário XML descreve o que o membro faz, não repete o nome.
- Mensagem de exceção é acionável: diz o que está errado e, quando possível, como corrigir.

## Evitar (nos dois idiomas)

- "Serve como", "funciona como", "representa" quando "é" resolve.
- "Não só X, mas também Y".
- Elogio vago ("robusto", "poderoso", "incrível").
- Dizer que algo é importante sem dizer por quê.
- Negrito em todo item de lista.
- Citar fase ou etapa de plano em comentário de código. Comentário pode citar ADR
  (`// see ADR-0007`).

## Antes de entregar texto

Rodar `grep -rn "—" <arquivos alterados>` e revisar cada ocorrência em texto português.
