# ADR-0004: Storages em ADO.NET direto, sem Dapper

## Status
Aceito

## Issue
[#64](https://github.com/Douglasproglima/netapiflow/issues/64)

## Contexto
`netapiflow-prompt.md` (seção 5) deixa em aberto a escolha entre ADO.NET puro e Dapper para
`NetApiFlow.Storage.PostgreSql` e `NetApiFlow.Storage.SqlServer`, pedindo que a IA Planejadora
decida e justifique. O conjunto de operações de `ITraceStore` é pequeno e fixo (salvar trace com
spans, listar com filtro e paginação, buscar por id, métricas por componente, última execução
bem-sucedida, aplicar retenção): não há superfície de query dinâmica que justifique um
micro-ORM.

O caminho mais sensível a desempenho é a escrita em lote de trace e spans a cada requisição
rastreada. Postgres oferece `NpgsqlBinaryImporter` (protocolo `COPY`) e SQL Server oferece
`SqlBulkCopy`: os dois são bulk-insert nativo do driver, mais rápidos que `INSERT` por linha
gerado por um micro-ORM, e nenhum dos dois tem equivalente direto no Dapper.

## Decisão
ADO.NET direto (`NpgsqlCommand`/`SqlCommand`, `NpgsqlBinaryImporter`/`SqlBulkCopy`) nos dois
pacotes de storage. Sem Dapper.

## Consequências
- Cada pacote de storage tem como dependência só o driver (`Npgsql` ou
  `Microsoft.Data.SqlClient`), sem um micro-ORM a mais na árvore de dependências de quem não
  precisa dele no núcleo.
- Mais código manual de mapeamento linha-a-objeto do que teria com Dapper, concentrado nos
  próprios pacotes de storage (não vaza para o núcleo nem para o usuário final).
- Escrita em lote usa o mecanismo nativo de cada driver desde o início, sem precisar trocar de
  abordagem depois por motivo de performance.
