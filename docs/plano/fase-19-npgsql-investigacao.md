# Fase 19: Interop Npgsql, investigação antes de implementar (R-06)

Projeto tocado na tarefa 19.1: só investigação, nenhum código de produção ainda. Tarefas 19.2 em
diante **não estão especificadas neste arquivo de propósito**: o desenho real depende do que a
tarefa 19.1 encontrar. A Planejadora escreve o resto do plano depois de ler o relatório da
investigação, não antes.

Pré-requisito: nenhum funcional. Último item do "ataque aos riscos em aberto" (D-88), depois da
Fase 18. R-06 está aberto desde a Fase 2 (bootstrap do projeto), nunca investigado de verdade.

## Contexto (ler antes de começar)

R-06, do jeito que está registrado hoje, parte de duas suposições nunca confirmadas:

1. Que a `ActivitySource` nativa `"Npgsql"` se comporta como a do `HttpClient` (ADR-0012):
   sempre presente, só precisa de um `ActivityListener`. **Isso é falso.** Pesquisa da Planejadora
   confirmou que o Npgsql só emite `Activity` nessa fonte se a aplicação hospedeira referenciar o
   pacote `Npgsql.OpenTelemetry` e chamar `NpgsqlDataSourceBuilder.ConfigureTracing(...)` por
   conta própria. Sem isso, a fonte existe mas nunca emite nada. Isso não quebra a regra de
   núcleo sem dependência nova (o núcleo só escuta uma fonte que pode nunca emitir nada, nunca
   referencia o pacote do Npgsql), mas muda a moldura: é um **pré-requisito do lado do usuário**,
   a ser documentado, não algo que o NetApiFlow liga sozinho.
2. Que dedup por `Activity.Id` resolveria o problema de duplicação com
   `NetApiFlow.EntityFrameworkCore`. **Já descartado antes desta fase** (são duas `Activity`
   distintas, de fontes diferentes, para a mesma operação lógica). A hipótese mais provável é
   dedup por **cadeia de `Parent`**: a `Activity` nativa do Npgsql deveria nascer com
   `Parent = Activity.Current` no momento em que o driver a inicia, e se o
   `FlowDbCommandInterceptor` já abriu um `NetApiFlow.Step` (`ComponentType.Database`) em volta
   daquela mesma chamada, esse step seria o `Activity.Current` naquele instante, viraria o `Parent`
   da `Activity` nativa, e dessa forma dá pra detectar "esse Npgsql Activity é duplicata de um
   step que já existe" sem precisar de `Activity.Id`. **Mas há relato real da comunidade de que
   essa cadeia de `Parent` pode quebrar sob pool de conexão e código assíncrono** (exatamente o
   padrão de uso real do Npgsql), e isso nunca foi confirmado contra a versão do Npgsql deste
   projeto.

Uma terceira dúvida real: a convenção semântica do OTel diz que a tag `db.statement` do Npgsql
deve conter só o texto parametrizado (`$1`, `$2`), nunca o valor do parâmetro, cumprindo a mesma
regra dura que `.claude/rules/tracing.md` já exige para tudo que o NetApiFlow grava. Mas é um
"SHOULD" da especificação, não uma garantia, e o D-69 (Fase 8) já provou uma vez que a BCL pode
divergir do que a documentação promete na prática (`net8.0` sem tag nenhuma na `Activity` do
`HttpClient`, contra o esperado). Não confiar na documentação sem prova empírica.

## Tarefa 19.1: Spike de investigação (sem código de produção)

Objetivo: responder três perguntas com prova real, não com leitura de documentação.

1. **A cadeia de `Parent` funciona na prática?** Montar um teste de integração descartável
   (pode morar em `tests/NetApiFlow.IntegrationTests`, mas claramente marcado como spike no nome
   do método, ex. `Spike_...`, para a Planejadora reconhecer que não é prova de feature nenhuma
   ainda) com: `NetApiFlow.EntityFrameworkCore` configurado normalmente (`FlowDbCommandInterceptor`
   ativo), um `NpgsqlDataSource` com `ConfigureTracing(...)` ligado (referenciar
   `Npgsql.OpenTelemetry` só neste projeto de teste, nunca em `src/`), um `ActivityListener` de
   teste escutando a fonte `"Npgsql"`, e Postgres real via Testcontainers (mesmo padrão já usado
   em `NetApiFlow.Storage.Tests`). Rodar uma consulta EF Core real, sob pool de conexão padrão e
   `async`/`await` real (não sequencial e sincronizado), e inspecionar: o `Parent` da `Activity`
   nativa do Npgsql é mesmo a `Activity` do `NetApiFlow.Step`? Repetir com pelo menos duas
   consultas concorrentes (`Task.WhenAll`), que é onde a quebra de `Parent` é mais provável de
   acontecer segundo os relatos da comunidade.
2. **O que a tag `db.statement` carrega de verdade?** Na mesma consulta, com um parâmetro
   contendo um valor claramente identificável (ex. um e-mail de teste), inspecionar o valor cru da
   tag `db.statement` da `Activity` nativa. Confirmar se é só o texto parametrizado (`$1`) ou se o
   valor do parâmetro vaza, mesmo padrão de verificação que `HttpClientActivityTagBackfillTests`
   já fez para a tag crua do `HttpClient` (Fase 8, R-13).
3. **O que acontece sem `NetApiFlow.EntityFrameworkCore`?** Repetir o mesmo teste usando Npgsql
   puro (sem EF Core, sem `FlowDbCommandInterceptor`), confirmando que a `Activity` nativa nasce
   sem nenhum `Parent` da fonte `"NetApiFlow"` (cenário onde a ingestão deveria capturar
   normalmente, sem duplicação possível, já que não existe span concorrente pra mesma operação).

**Parar aqui e relatar à Planejadora** os três achados, com evidência real (valor de `Parent.Source.Name`
observado, texto cru de `db.statement` observado), antes de qualquer linha de código de produção
ou ADR. Se a cadeia de `Parent` se mostrar frágil sob pool/async (cenário 1 falhar de forma
inconsistente), isso é achado que muda a decisão de arquitetura, não um detalhe de implementação:
a Planejadora decide se vale a pena seguir com dedup por `Parent`, se existe uma segunda opção
(ex. dedup por timing/janela de sobreposição, mais caro e mais impreciso), ou se o escopo precisa
encolher (ex. só suportar quando `NetApiFlow.EntityFrameworkCore` não está em uso, documentando a
limitação em vez de resolvê-la).

## Fora de escopo desta fase, de propósito

- Qualquer código de produção além do spike da tarefa 19.1.
- MassTransit (mencionado em D-58/ADR-0012 como outro item descartado por falta de pesquisa):
  continua fora, não faz parte deste risco.
- Decisão de ADR: só depois do relatório da tarefa 19.1, escrita pela Planejadora.

## Ao terminar a tarefa 19.1

Resumo para a Planejadora: as três respostas com evidência real (não impressão, não "parece que
funciona"), incluindo qualquer falha intermitente observada ao repetir o teste de concorrência
várias vezes (mesmo rigor que o D-54 usou: rodar em loop antes de concluir que é estável). A
Planejadora escreve o restante do plano da Fase 19 (ADR, se a cadeia de `Parent` se confirmar
viável; tarefas de implementação) só depois de ler esse relatório.
