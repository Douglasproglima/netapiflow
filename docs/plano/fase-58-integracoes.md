# Fase 58: integrações que não mentem (D-210, F2, F3, F4, S8)

**Estado (24/11/2026):** **a Parte A está entregue em `develop` (#491, squash `9c29835`, D-285) e conferida pela auditoria com o pacote real.** Falta a Parte C (58.11, D-284) e a B. O plano foi escrito quando a última versão publicada era a `0.5.2`. Hoje é a `0.5.3` (a de `tests/real-package/previous-published-version.txt`), e as referências operacionais da 58.8 e do ponto de parada foram atualizadas. Os `0.5.2` das linhas dos achados são onde cada um foi reproduzido, e ficam como estão.

Quatro pontos onde o NetApiFlow se integra com outra coisa, e a integração diz uma coisa e faz outra. O Npgsql 10 sai
sem o SQL. A resposta 500 sai sem o cabeçalho de trace, e o Runner fica mudo. O MediatR precisa de uma chamada que o
README não cita. E os pacotes do próprio NetApiFlow não garantem que andam juntos.

Vai no `0.6.0` (D-210, D-232). São defeitos antigos e não regressões, então não são hotfix (D-205).

Duas partes, como a Fase 50, mais a 58.11 (D-284): três PRs.

- **Parte A (58.0 a 58.5):** F2, F3 e F4, as integrações com terceiros.
- **Parte B (58.6 a 58.9):** S8, os pacotes do NetApiFlow entre si (D-231).
- **Parte C (58.11):** o SqlClient com vários comandos no mesmo método, achado da auditoria na 58A (D-284). Vem depois da 58A e antes da B.

## Os achados e o que a Planejadora conferiu

| # | Achado | Conferi |
|---|---|---|
| F2 | O Npgsql 10 sai sempre como "SQL command", sem SQL, e o `CONNECT` vira uma segunda etapa `Database` idêntica à do comando | **Código:** `NpgsqlActivityTagNames.Statement = "db.statement"`. **Reproduzido pela auditoria** com o Npgsql `10.0.0` em `net10.0` (detalhe em `auditoria-de-base.md`). Eu não reproduzi |
| F3 | O Runner fica mudo quando a resposta não traz `X-NetApiFlow-TraceId` | **Reproduzi no `0.5.2`** o 500 sem o cabeçalho. A parte do cabeçalho é a Tarefa 50.13. O que fica aqui é o Runner dizer por quê |
| F4 | O README não diz que o `NetApiFlow.MediatR` exige `AddNetApiFlowMediatR()` | **Reproduzido pela auditoria** no `0.5.2`. Não reproduzi |
| S8 | Os pacotes satélites pedem o núcleo `>=` a uma versão, e o núcleo dá `InternalsVisibleTo` ao painel e aos dois storages: atualizar só o núcleo pode quebrar o satélite antigo com `MissingMethodException`, a classe do D-140 | **Código:** li `src/NetApiFlow/AssemblyInfo.cs`: `InternalsVisibleTo` para `NetApiFlow.Dashboard`, `NetApiFlow.Storage.PostgreSql` e `NetApiFlow.Storage.SqlServer`. **Auditoria:** os `.nuspec` publicados da `0.5.2` do painel, do MCP, do EF Core e do Testing dizem `<dependency id="NetApiFlow" version="0.5.2"/>`, que no NuGet é `>= 0.5.2`. **Ninguém reproduziu a quebra ainda:** a Tarefa 58.8 mede |
| 58.11 | O `SqlClientCommandListener` abre o passo no `WriteCommandBefore` e o deixa como `Activity.Current` do código do usuário: vários comandos no mesmo método gravam um passo só, e o que vem depois fica com o pai errado | **Reproduzido pela auditoria** com o pacote `0.6.0-audit.11` e na `0.5.3` (D-284). A Planejadora não reproduziu e não leu o código. Entra por decisão do dono, na Tarefa 58.11 |

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

## Resultado da Parte A (D-285, 24/11/2026)

Entregue em `develop` na #491 (squash `9c29835`). O relato abaixo é da Executora, no corpo da PR, e da auditoria, que mediu com o pacote `0.6.0-audit.11`. A Planejadora não repetiu as medições.

**A tabela de tags (58.0), medida em host real, `net10.0`, PostgreSQL 16:**

| O que | Npgsql 8.0.5 e 9.0.4 | Npgsql 10.0.0 e 10.0.3 |
|---|---|---|
| Texto do comando | `db.statement` | `db.query.text` |
| Nome da atividade do comando | o nome do banco (`postgres`) | `postgresql`, ou o que o `ConfigureCommandSpanNameProvider` devolver; `COPY FROM` para um COPY |
| Sistema e banco | `db.system`, `db.name` | `db.system.name`, `db.namespace` |
| Servidor | `net.peer.name`, `net.peer.ip`, `net.peer.port` | `server.address`, `server.port` |
| Usuário | `db.user` | não manda |
| Connection string e id da conexão | `db.connection_string`, `db.connection_id` | `db.npgsql.data_source`, `db.npgsql.connection_id` |
| Comando que falha | `Activity.Status` sem valor, `otel.status_code=ERROR`, `otel.status_description=<SQLSTATE>`, evento `exception` | `Activity.Status` Error, `error.type` e `db.response.status_code` iguais ao SQLSTATE, evento `exception` |
| Abertura de conexão | nenhuma atividade | `CONNECT <banco>`, sem tag de texto SQL, também quando falha (`Status` Error, `error.type=28P01`) |

O NetApiFlow lê uma só dessas tags, o texto do comando. As outras não são lidas, ou já funcionam (`NpgsqlCommandFailure` lê o status nativo primeiro e cai no `otel.status_code`). O `Microsoft.Data.SqlClient` 6.1.8 não emite `ActivitySource`, só os eventos do `SqlClientDiagnosticListener`, e o NetApiFlow escuta os três de comando, então a conexão nunca é passo.

- **58.1:** o texto vem de `db.query.text` e, na falta dela, de `db.statement`.
- **58.2:** o `CONNECT` não abre passo no início, e não há passo retirado. O critério, o desvio do pedido de "retirar" e o custo estão na D-285.
- **58.3:** o projeto `NetApiFlow.Npgsql10.IntegrationTests`, só em `net10.0`, nas duas listas do `ci.yml` e do `release.yml` e no `.slnx`. O Npgsql 8 fica fora da matriz (D-284, item 55 do roadmap).
- **58.4:** o Runner nomeia as causas de uma resposta sem trace, nos dois idiomas, e lê a resposta como texto. O 5xx sem cabeçalho é o limite da 50.13, e a nota o diz.
- **58.5:** a linha do `AddNetApiFlowMediatR()` nos três READMEs.
- **58.10:** o rótulo é a tabela, no núcleo e no EF Core, juntos.
- **Testes e guardas:** cada correção vista falhando antes (sete corridas vermelhas listadas no corpo da PR), os guardas que nasceram verdes provados por mutação, 26 linhas novas no `guards.json` e o CI com 162 de 162.
- **Aberto:** a Parte C (58.11, D-284) e a Parte B. O detector do dashboard não rodou numa build do `develop`.

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

# Parte C: o SqlClient com vários comandos no mesmo método (D-284)

## Tarefa 58.11: o passo do SqlClient não pode ficar como `Activity.Current` do código do usuário

**O achado, da auditoria.** Pacote `0.6.0-audit.11` contra a `0.5.3`, num app novo com controller, Postgres 16 e SQL Server 2022, com `ExternalSources` em Npgsql e SqlClient e `CaptureSqlText` ligado. **É idêntico na `0.5.3`: defeito antigo,** que entra por decisão do dono (D-284).

- Depois de um `ExecuteScalarAsync`, o `Activity.Current` do controller é `SqlClient.SELECT ...` com `IsStopped = true`.
- Quatro comandos SqlClient no mesmo método gravam um passo só: o `ExistingDatabaseStep.Covers` descarta os outros três.
- Num mesmo método, um comando SqlClient, um comando Npgsql e um `Flow.Step("Service", "Work")`: o comando Npgsql some da trace, e o `Service.Work` vira filho do passo SQL.
- **A causa, pela leitura da auditoria (a Planejadora não leu o código):** o `SqlClientCommandListener` abre o passo por `Flow.Step` dentro do `WriteCommandBefore`, que roda no contexto de quem chamou, e só o encerra no `WriteCommandAfter`, em outro contexto. A atividade do passo segue como `Activity.Current` no código do usuário depois do `await`.
- **Por que os testes não pegam:** cada comando roda no próprio método async (`RunAsync`), que isola o `AsyncLocal`. Um repositório Dapper com duas consultas no mesmo método perde a segunda, e o que vem depois fica com o pai errado.

**O que a Executora faz:**

1. **Reproduzir antes de corrigir,** em `SqlClient.IntegrationTests`, com host real e **vários comandos no mesmo método**, e não um método async por comando. (a) Três comandos em sequência dão três passos, filhos do mesmo pai e na ordem. (b) Depois do `await` de cada comando, o `Activity.Current` é o do código do usuário, e não a atividade parada do passo SQL. (c) Um `Flow.Step` aberto depois dos comandos é filho do código do usuário, e não do passo SQL. Visto falhando antes.
2. **Corrigir pela raiz:** o passo aberto no `WriteCommandBefore` não pode deixar o `Activity.Current` do contexto de quem chamou apontando para ele. A Planejadora palpita que guardar e restaurar o `Activity.Current` ao abrir o passo resolve (o passo mantém o pai certo e continua sendo encerrado no `WriteCommandAfter`), mas é palpite: a Executora mede o mecanismo e diz qual escolheu. Medir também com erro (`WriteCommandError`), com cancelamento e com `ExecuteReader`, onde o `After` pode vir depois.
3. **A cobertura pelo passo do EF Core não muda.** Um comando de EF Core com o listener do SqlClient ligado continua gravando um passo só, e não dois (ADR-0018, `ExistingDatabaseStep.Covers`). Teste que prova isso depois da correção. O que muda é que um passo SQL já parado deixa de cobrir os comandos seguintes do mesmo método.
4. **O Npgsql não é afetado** (a auditoria mediu: o `CONNECT custom` ficou entre os irmãos, com o pai certo). Uma asserção sobre o `Activity.Current` depois de um comando Npgsql, no teste do mesmo método, confirma.
5. **Custo no caminho quente:** nenhuma alocação nova por comando além da que já existe, e nenhum lock. A Executora mede e diz o número.
6. **Ponto de parada:** se a correção pedir API pública nova, ou mudar o jeito de abrir o passo para além de não vazar o `Activity.Current` (por exemplo trocar o listener por um `ActivityListener`), **para e relata**.
7. **`CHANGELOG.md`, `### Fixed`, em inglês:** vários comandos no mesmo método passam a gravar um passo cada, e o código depois deles passa a ter o pai certo. A linha da 58.10 ("no longer light a false N+1 badge") só vale para o SqlClient depois desta tarefa, e a Executora confere que as duas frases não se contradizem.
8. **A linha do `guards.json`** e a seção "Consumidores do contrato" na PR. O cenário permanente do job do pacote real só entra se o job tiver SQL Server. Se não tiver, o teste de host real de `SqlClient.IntegrationTests`, que roda no CI com contêiner, é a guarda, e o relatório diz por que o cenário não cabe.

**Pronto quando:** os testes do item 1 vistos falhando antes e passando depois, o caso do item 3 verde, e o relatório com o `Activity.Current` depois de cada comando, antes e depois da correção.

Branch `bugfix/58c-sqlclient-varios-comandos`, a partir de `develop` atualizado, **depois da 58A mergeada** e antes da Parte B. Abrir a PR e parar.

## Fora de escopo

- O aviso de log do MediatR (item 29 do roadmap).
- Reduzir o `InternalsVisibleTo` por refatoramento.
- Qualquer mudança nos pisos de terceiros.

## Critério que vale para a fase inteira

- Build, testes, lint e formato limpos nos dois TFMs. `AotCoreVerification` com zero aviso.
- Cada achado com código tem teste **visto falhando antes**.
- A correção do S8 e cada achado com código entram com a sua linha em `tests/regression-guards/guards.json` (D-266), e a auditoria o
  cobra na PR. O snapshot dos `.nuspec` já mostra a mudança do S8.
- A auditoria reconfere cada frase de README tocada e refaz F2, F3, F4, S8 e o achado da 58.11 com o pacote real.

## Versionamento e fluxo

- `0.6.0`. Parte A: `bugfix/58a-integracoes`. Parte B: `bugfix/58b-pacotes-de-um-release`. Parte C: `bugfix/58c-sqlclient-varios-comandos`, depois da 58A mergeada e antes da B. As três a partir de
  `develop` atualizado. A 58.4 espera a 50.13 e a Parte B da Fase 50 em `develop`.
- Commits pequenos, em inglês, uma linha, sem linha de coautor. Abrir a PR e parar.

## Relatório final

Para cada achado: o teste, a saída dele falhando antes e passando depois. A tabela de tags da 58.0. O resultado dos três
casos da 58.8, com a saída real.
