# Fase 64: uma transação por gravação nos dois storages (D-236, B4)

Os storages de PostgreSQL e de SQL Server gravam a trace e depois os spans, **sem transação**. Se a gravação dos spans
falha, a trace já foi gravada com `span_count > 0` e **nenhum span**. O painel mostra uma trace que diz ter etapas e não
mostra nenhuma.

Vai no `0.6.0` (D-236). É defeito antigo e não regressão, então não é hotfix (D-205). Vem **depois da Fase 55** (que muda a
chave da trace e mexe nos mesmos arquivos) e depois da Tarefa 50.14.

## O achado e o que a Planejadora conferiu

| # | Achado | Conferi |
|---|---|---|
| B4 | A gravação do trace e a dos spans ficam sem transação, e uma falha nos spans deixa trace com `span_count > 0` e nenhum span | **Código:** busquei `BeginTransaction`, `NpgsqlTransaction`, `SqlTransaction` e `TransactionScope` nos dois storages e **não há nenhum**. Não reproduzi a falha parcial |

## Ponto de parada

- Se a transação mudar o **custo** da gravação de forma que a 64.0 meça como relevante (a gravação roda no
  `PersistenceWriter`, longe da requisição, mas a fila é de 1000), **para e relata** com o número.
- Se o `COPY` (PostgreSQL) ou o `SqlBulkCopy` (SQL Server) não funcionar dentro da transação do jeito esperado, para.
- Nenhuma mudança de schema.

## Tarefa 64.0: reproduzir e medir, antes de corrigir

Com os bancos reais (Testcontainers, `NetApiFlow.Storage.Tests`; no Windows desta máquina, `DOCKER_HOST` para o pipe do
contexto `desktop-linux`), nos dois:

1. **A falha parcial.** Forçar uma falha na gravação dos spans (por exemplo, um valor que estoura o tamanho de uma coluna).
   O esperado, depois da correção: **nenhuma** linha da trace. Hoje: a linha da trace fica, com `span_count > 0`.
2. **O custo.** 1000 gravações de trace com 10 spans, sem e com transação, tempo total e por gravação.
3. **A retenção.** O `ApplyRetentionAsync` apaga traces e spans: confere se deixa span órfão, ou trace sem span. **Só mede e
   relata.** Se der achado, é item da auditoria.

## Tarefa 64.1: a correção

Uma transação por `SaveAsync`, nos dois bancos: a trace e os spans entram juntos ou nenhum entra. A falha continua sendo
logada pelo `PersistenceWriter` e contada, como hoje. O `COPY` e o `SqlBulkCopy` ficam dentro da transação.

## Tarefa 64.2: os testes

Os dois testes vermelhos da 64.0, agora **verdes**, nos dois bancos, mais: gravação feliz inalterada (trace e spans lidos de
volta), e duas gravações seguidas com a primeira falhando e a segunda inteira.

## Fora de escopo

- Reescrever a retenção. Só a medição.
- Mudar o formato das tabelas ou a chave (Fase 55).

## Critério que vale para a fase inteira

- Build, testes e formato limpos nos dois TFMs. Os dois bancos nos testes de storage.
- A falha parcial **vista** antes da correção, e o custo medido antes e depois.
- `CHANGELOG.md`, em inglês, `### Fixed`: uma trace nunca fica gravada sem os spans dela.

## Versionamento e fluxo

- `0.6.0`. Branch `bugfix/64-storages-transacao` a partir de `develop` atualizado, **depois** da Fase 55.
- Commits pequenos, em inglês, uma linha, sem linha de coautor. Abrir a PR e parar.

## Relatório final

A saída da falha parcial antes e depois, nos dois bancos. O custo medido. O que a retenção mostrou.
