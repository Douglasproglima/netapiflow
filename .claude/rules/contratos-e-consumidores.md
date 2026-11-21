# O contrato que muda tem consumidores: quem muda, prova (D-251, D-253)

Regra da Fase 70, parte 6. Generaliza o D-251, que dizia isso só da demo.

Em 21/11/2026, quatro regressões passaram por revisão e CI verde porque ninguém perguntou **quem mais lê isso**: o Quick start da
#409 (`CS1529` ao colar), a demo da #416 (404 em 26 de 26 traces depois de o painel passar a mandar o `before`), o piso do EF Core da
`0.5.1` e as opções da Fase 47. Cada uma virou achado depois do merge.

## O que é contrato

- Uma rota, um parâmetro ou o formato de uma resposta, na API REST, no stream SSE e no MCP.
- Um tipo, um membro ou uma opção pública do núcleo e dos pacotes.
- Um bloco de código do README, em especial o Quick start.
- O formato do `schema.sql`, dos eventos e do documento OpenAPI que o app do usuário expõe.
- O que um pacote declara como dependência (o piso, D-181).

## Quem consome

| Consumidor | O que a PR prova |
|---|---|
| A demo estática | A demo construída (`npm run build:demo`) aberta num navegador: as traces dela carregam sem erro, com o número dito, e responde com a semântica nova (D-251) |
| O painel | O gate do front (`lint`, `format:check`, `test:ci`, `build`) |
| O MCP | As ferramentas que devolvem o mesmo dado continuam devolvendo |
| O README | Cada bloco de código colado num app novo, na ordem e no lugar que o texto manda, e compilado (D-247) |
| Os samples | `dotnet build` dos samples |
| O app do usuário | O documento OpenAPI do app sem rota nossa; um storage escrito para uma versão anterior continua carregando (membro novo de interface com implementação padrão, D-246) |
| O site | A demo publicada em outro repositório usa as mesmas fixtures: a regeneração do release as refaz |

## O que a PR faz

- O corpo da PR tem a seção **Consumidores do contrato**. Para cada consumidor da tabela, diz **afetado** ou **não afetado**, e a
  prova. "Não afetado" leva uma frase com o motivo, não só "n/a".
- Se algum consumidor não puder ser provado na própria PR, a PR diz qual, e a auditoria decide.
- O plano de uma tarefa que muda contrato cita os consumidores na própria tarefa. A lacuna da 57.3 foi do plano, que não falava da
  demo.

## Quando a prova vira máquina

A Fase 70 transforma esta lista em passos de CI: o job do pacote real, o snapshot dos `.nuspec`, a demo construída, a linha de base
da API pública e o registro de guardas. Cada um que entra tira uma linha desta tabela da conta manual. Enquanto não entra, a prova é
manual e vem dita na PR. Todo achado da auditoria que dá para reproduzir de fora vira cenário permanente do job do pacote real.
