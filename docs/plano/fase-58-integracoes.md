# Fase 58: integrações que não mentem (D-210, F2, F3, F4, S8)

**Estado (24/11/2026):** liberada para a Executora, Parte A primeiro. O plano foi escrito quando a última versão publicada era a `0.5.2`. Hoje é a `0.5.3` (a de `tests/real-package/previous-published-version.txt`), e as referências operacionais da 58.8 e do ponto de parada foram atualizadas. Os `0.5.2` das linhas dos achados são onde cada um foi reproduzido, e ficam como estão.

Quatro pontos onde o NetApiFlow se integra com outra coisa, e a integração diz uma coisa e faz outra. O Npgsql 10 sai
sem o SQL. A resposta 500 sai sem o cabeçalho de trace, e o Runner fica mudo. O MediatR precisa de uma chamada que o
README não cita. E os pacotes do próprio NetApiFlow não garantem que andam juntos.

Vai no `0.6.0` (D-210, D-232). São defeitos antigos e não regressões, então não são hotfix (D-205).

Duas partes, duas PRs, como a Fase 50:

- **Parte A (58.0 a 58.5):** F2, F3 e F4, as integrações com terceiros.
- **Parte B (58.6 a 58.9):** S8, os pacotes do NetApiFlow entre si (D-231).

## Os achados e o que a Planejadora conferiu

| # | Achado | Conferi |
|---|---|---|
| F2 | O Npgsql 10 sai sempre como "SQL command", sem SQL, e o `CONNECT` vira uma segunda etapa `Database` idêntica à do comando | **Código:** `NpgsqlActivityTagNames.Statement = "db.statement"`. **Reproduzido pela auditoria** com o Npgsql `10.0.0` em `net10.0` (detalhe em `auditoria-de-base.md`). Eu não reproduzi |
| F3 | O Runner fica mudo quando a resposta não traz `X-NetApiFlow-TraceId` | **Reproduzi no `0.5.2`** o 500 sem o cabeçalho. A parte do cabeçalho é a Tarefa 50.13. O que fica aqui é o Runner dizer por quê |
| F4 | O README não diz que o `NetApiFlow.MediatR` exige `AddNetApiFlowMediatR()` | **Reproduzido pela auditoria** no `0.5.2`. Não reproduzi |
| S8 | Os pacotes satélites pedem o núcleo `>=` a uma versão, e o núcleo dá `InternalsVisibleTo` ao painel e aos dois storages: atualizar só o núcleo pode quebrar o satélite antigo com `MissingMethodException`, a classe do D-140 | **Código:** li `src/NetApiFlow/AssemblyInfo.cs`: `InternalsVisibleTo` para `NetApiFlow.Dashboard`, `NetApiFlow.Storage.PostgreSql` e `NetApiFlow.Storage.SqlServer`. **Auditoria:** os `.nuspec` publicados da `0.5.2` do painel, do MCP, do EF Core e do Testing dizem `<dependency id="NetApiFlow" version="0.5.2"/>`, que no NuGet é `>= 0.5.2`. **Ninguém reproduziu a quebra ainda:** a Tarefa 58.8 mede |

## Ponto de parada

- Se a 58.0 achar que o `CONNECT` do Npgsql não se distingue de um comando por critério estável (nome da atividade e
  tags iguais nas versões 8, 9 e 10), **para e relata**. A decisão é da Planejadora.
- Se a 58.6 não achar jeito de gerar a faixa exata no `.nuspec` sem reescrever o `pack`, para e relata.
- Se a 58.8 mostrar que o par núcleo novo com painel `0.5.3` quebra, **não é motivo para parar**: é o que se queria
  medir, e vai para o README e o CHANGELOG.

# Parte A: as integrações com terceiros

## Tarefa 58.0: medir, sem corrigir

Host real, `net10.0`, PostgreSQL 16 em contêiner (no Windows desta máquina, `DOCKER_HOST` para o pipe do contexto
`desktop-linux`).

1. **O `CONNECT`.** Com o Npgsql 8, 9 e 10, o que distingue a atividade de conexão da de comando: o nome da atividade
   e cada tag. A classificação pelo nome ou pela falta da tag de texto são as duas formas frágeis, e a Executora
   propõe o critério com a medição.
2. **Toda tag que o código lê**, não só a do SQL. As convenções novas de OpenTelemetry renomearam `db.statement` para
   `db.query.text`, `db.system` para `db.system.name`, `db.name` para `db.namespace` e `net.peer.name` para
   `server.address`. Listar as tags que `NpgsqlActivityTagNames` e o equivalente do SqlClient leem e dizer, medindo, o
   que cada driver manda hoje.
3. **O SqlClient.** O `Microsoft.Data.SqlClient` `6.1.8` que o D-172 já usa nos testes: as tags cruas que ele emite.

**Pronto quando:** a tabela de tags por driver e versão, e o critério proposto para o `CONNECT`, com a saída real.

## Tarefa 58.1: ler a tag nova, com a antiga de reserva

Ler `db.query.text` e, se ausente, `db.statement`, e o mesmo para cada tag renomeada que a 58.0 provar. Teste novo em
`Npgsql.IntegrationTests` que **falha hoje** com o Npgsql 10. O SQL só é gravado com `CaptureSqlText = true`, como hoje.

## Tarefa 58.2: o `CONNECT`

Recomendação da Planejadora: **ignorar**, porque não é um comando SQL e duplica a etapa. O critério é o da 58.0, e a
Planejadora confirma com a medição. Teste: várias conexões em sequência **não** disparam o selo de N+1.

## Tarefa 58.3: a matriz

O Npgsql 10 entra na matriz de `Npgsql.IntegrationTests`, **sem tirar** o 8 nem o 9, que continuam suportados.
Dependência só de teste, e o Dependabot ignora `Npgsql*` (família de piso, D-181), então a versão sobe à mão. Nenhum
piso de pacote publicado muda.

## Tarefa 58.4: o Runner diz por que não achou a trace (depende da 50.13 e da Parte B da Fase 50)

O Runner lê o id do cabeçalho `X-NetApiFlow-TraceId` (`endpoint-form.component.ts`, `onDispatched`). Sem o cabeçalho
numa resposta de sucesso ele não navega e não diz nada. Isso vale para `EmitTraceIdHeader = false` e para uma rota fora
do rastreamento, e (D-241) para um 500 de servidor em Production sem tratador de exceção. O Runner passa a dizer, nomeando as
causas possíveis, nos dois idiomas do painel. Medido no navegador, nas três causas. Um teste de componente por causa.

## Tarefa 58.5: o `AddNetApiFlowMediatR()` nos três READMEs

A linha que falta em `README.md`, `README.pt-BR.md` e `src/NetApiFlow.MediatR/README.md` (D-219). O aviso de log fica
fora e é o item 29 do roadmap ("Aviso de log quando o MediatR está registrado e o `AddNetApiFlowMediatR()` não foi
chamado"). A auditoria roda de novo o roteiro do F4 e confere as três frases.

## Tarefa 58.10: o rótulo do SqlClient pega a tabela, não o schema (B11, D-236)

Com `dbo.Users`, o rótulo sai `SELECT dbo` (o schema, não a tabela). Isso mistura componentes diferentes num só e pode gerar um **N+1 falso**. Corrige junto do Npgsql (58.1). A Executora reproduz com o `Microsoft.Data.SqlClient` `6.1.8`, mede o texto bruto do comando, e o rótulo passa a usar a tabela, com e sem schema (`dbo.Users`, `[dbo].[Users]`, `Users`). Teste que **falha hoje** em `SqlClient.IntegrationTests`, e um teste de que duas tabelas de um mesmo schema **não** disparam o selo de N+1.

# Parte B: os pacotes do NetApiFlow entre si (D-231)

## Tarefa 58.6: os satélites pedem o núcleo na versão exata

Todo pacote satélite pede o núcleo na faixa **exata** do mesmo release (`[0.6.0]`), e não `>= 0.6.0`. Vale também para
qualquer dependência de um pacote nosso em outro pacote nosso. Os pacotes são publicados juntos (D-01), e a versão
exata troca um erro de execução por um erro de `restore`, claro.

A Executora **mede o mecanismo**, porque o `pack` de uma `ProjectReference` gera `>=` por padrão. O resultado tem de
valer com o número real, com `-p:Version=<próxima>-verify.<n>` (D-153, D-174) e no `release.yml`.

**O teste lê o `.nuspec` empacotado** de cada satélite e exige a faixa exata, e falha se um pacote novo nascer com `>=`.
Visto falhando antes.

## Tarefa 58.7: o inventário do `InternalsVisibleTo`

Listar o que o painel e os dois storages consomem do núcleo por `InternalsVisibleTo`. **O padrão é manter**, porque a
versão exata da 58.6 já protege. Nenhum refatoramento nesta fase. O que valer a pena reduzir vira item de roadmap, um
registro por item, e o que for público de fato fica documentado.

## Tarefa 58.8: o teste com o pacote real, entre versões

Com o pacote do núcleo empacotado localmente como `0.6.0-verify.<n>` e os satélites **publicados** da `0.5.3`, a última publicada:

1. Instalar o núcleo novo com o `NetApiFlow.Dashboard` `0.5.3` e registrar o que acontece: o restore passa? O app sobe?
   Aparece `MissingMethodException` ou `TypeLoadException`?
2. Instalar o conjunto inteiro do mesmo release e confirmar que funciona.
3. Instalar o núcleo de um release com um satélite de outro e confirmar que o `restore` falha com a mensagem do NuGet
   que diz a faixa exata.

O resultado, nos três, vai no relatório com a saída real.

## Tarefa 58.9: README e CHANGELOG

- `README.md` e `README.pt-BR.md`, na seção de instalação, em destaque e numa frase: **atualize todos os pacotes
  NetApiFlow juntos**, e o que o NuGet diz quando as versões não combinam. Uma linha em cada README de pacote.
- `CHANGELOG.md`, em inglês, `### Changed`: os pacotes satélites passam a exigir a versão exata do núcleo. O que
  mudou para quem atualiza, e o que a 58.8 mediu sobre os satélites da `0.5.3`.
- `.claude/rules/packaging.md` já tem a regra (D-231). A Executora não a altera.
- D-181 não muda: isto só trava o nosso núcleo ao mesmo release, e não sobe piso de terceiro.

## Fora de escopo

- O aviso de log do MediatR (item 29 do roadmap).
- Reduzir o `InternalsVisibleTo` por refatoramento.
- Qualquer mudança nos pisos de terceiros.

## Critério que vale para a fase inteira

- Build, testes, lint e formato limpos nos dois TFMs. `AotCoreVerification` com zero aviso.
- Cada achado com código tem teste **visto falhando antes**.
- A correção do S8 e cada achado com código entram com a sua linha em `tests/regression-guards/guards.json` (D-266), e a auditoria o
  cobra na PR. O snapshot dos `.nuspec` já mostra a mudança do S8.
- A auditoria reconfere cada frase de README tocada e refaz F2, F3, F4 e S8 com o pacote real.

## Versionamento e fluxo

- `0.6.0`. Parte A: `bugfix/58a-integracoes`. Parte B: `bugfix/58b-pacotes-de-um-release`. As duas a partir de
  `develop` atualizado. A 58.4 espera a 50.13 e a Parte B da Fase 50 em `develop`.
- Commits pequenos, em inglês, uma linha, sem linha de coautor. Abrir a PR e parar.

## Relatório final

Para cada achado: o teste, a saída dele falhando antes e passando depois. A tabela de tags da 58.0. O resultado dos três
casos da 58.8, com a saída real.
