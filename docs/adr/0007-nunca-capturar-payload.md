# ADR-0007: Nunca capturar corpo, argumento, header ou SQL parametrizado

## Status
Aceito

## Contexto
`netapiflow-prompt.md` (seção 6.8) é explícito: o NetApiFlow não é uma ferramenta de
observabilidade de dados, é uma ferramenta de diagnóstico de fluxo. Capturar corpo de requisição,
argumento de método, header, query string ou SQL com valores de parâmetro criaria um risco de
vazamento de dado sensível (senha, token, CPF, dado de cliente) só para construir um grafo de
quem chamou quem.

## Decisão
Nenhum span ou trace grava corpo, argumento, retorno, header ou query string, em nenhuma
circunstância, nem atrás de flag de configuração. Rota é sempre gravada como **template**
(`/api/orders/{id}`), nunca a URI real. Span de banco (`Database`) grava só tipo de comando e
tabela principal; o SQL completo só é gravado sem parâmetros, e só se `CaptureSqlText = true`
(desligado por padrão). Mensagem de erro é truncada (300 caracteres) e sanitizada antes de
qualquer outra coisa: e-mail, sequência numérica longa, CPF, CNPJ e pares
`password=`/`token=`/`authorization`/`api-key`/`secret` (risco R-07 em `docs/plano/riscos.md`).
Tags são só as explicitamente adicionadas pelo usuário via `FlowStep.Tag(...)`, com limite de
quantidade e tamanho.

## Consequências
- O grafo mostra *o caminho e a duração*, nunca *o conteúdo* da requisição. Isso é o que torna o
  dashboard seguro de ligar em homologação e, com cuidado, produção.
- Usuário que precisa de payload para debug usa outra ferramenta (APM completo). Isso é um não
  objetivo explícito (seção 2.1 do prompt), não uma limitação a resolver depois.
- A sanitização de mensagem de erro é heurística, documentada como limitação conhecida: não
  substitui uma política formal de dados do usuário.
