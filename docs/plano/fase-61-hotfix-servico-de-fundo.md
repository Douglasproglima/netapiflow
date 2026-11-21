# Fase 61: hotfix `0.5.3`, um serviço de fundo nunca derruba o host do usuário (D-235)

**Hotfix `0.5.3`.** Branch `hotfix/0.5.3` a partir de `main` (tag `v0.5.2`), PR para `main`, tag `v0.5.3`, página de
Release no GitHub e merge de `main` de volta em `develop`, tudo como o `docs/gitflow.md` descreve na seção "Hotfix". O
merge, a tag, o Release e o back-merge são da auditoria (D-198). A Executora abre a PR e para. **Esta fase passa à
frente de qualquer outra tarefa da Executora**, inclusive a Fase 56 e a PR da 54.5 (#388), que esperam.

## Estado

**Entregue e publicada em `v0.5.3`** (21/11/2026): PR #395 mergeada em squash (`8bf62ec`), tag, página de Release e os nove pacotes no nuget.org, e o back-merge na #396. A Planejadora reverificou em worktree limpo: 5 de 7 testes falham com a costura mantida e as correções desligadas, e 7 de 7 passam, nos dois TFMs. A auditoria rodou o teste longo com o pacote real, dois ticks com o PostgreSQL derrubado, 0 falha de requisição e o processo vivo. **Pendente, com a Executora:** a varredura 61.3 dos serviços que só existem no `develop`.

## Por que hotfix, e por que o D-205 agora cobre isto

A auditoria reproduziu contra o `0.5.2` **publicado**, num app `net10.0` com `AddNetApiFlow` e
`AddNetApiFlowPostgreSqlStorage`, em `Production`, com um PostgreSQL em contêiner:

- O app subiu às 22:15:44 e o banco foi derrubado às 22:15:45.
- `GET /hello` respondeu 200 a cada 15 s até 22:20:42. **Às 22:21:03 o processo morreu.**
- O log traz `Npgsql.NpgsqlException: Failed to connect` em `PostgreSqlTraceStore.ApplyRetentionAsync`, chamado por
  `NetApiFlow.Storage.RetentionService.ExecuteAsync`, e em seguida: "The HostOptions.BackgroundServiceExceptionBehavior
  is configured to StopHost. A BackgroundService has thrown an unhandled exception, and the IHost instance is
  stopping."

Um banco de **diagnóstico** fora do ar por cinco minutos derruba a API de produção do usuário. Isso quebra o princípio
número 1 do projeto ("nunca derrubar, travar ou atrasar a API do usuário", `.claude/rules/tracing.md`).

É defeito antigo e não regressão. Pelo D-205 ele iria na próxima minor, a menos que fosse de severidade alta. **A
auditoria decidiu que é**, e a regra do D-205 passa a dizer que derrubar a API de produção do usuário conta como
severidade alta, ao lado de perda de dado e de exposição grave. O resto dos critérios do hotfix continua valendo:
PATCH, nenhuma API pública nova, e o `.nuspec` de cada pacote muda só na versão.

## Ponto de parada

A Executora para e relata se vir qualquer um destes sinais:

- O teste vermelho da 61.0 **passar** antes da correção (o defeito não se reproduz no `develop` ou na `main`).
- A correção pedir API pública nova, ou mexer no `HostOptions` do usuário. **Isso nunca se faz:** a configuração do host
  é do usuário.
- Um serviço da varredura (61.3) só se corrigir mudando comportamento além de capturar e logar (por exemplo, um
  `StartAsync` que falha de propósito para impedir o app de subir).
- O `.nuspec` de qualquer pacote mudar em algo além da versão.

## Tarefa 61.0: reproduzir, em teste de host real, antes de corrigir

Em `NetApiFlow.IntegrationTests`, `net8.0` e `net10.0`, **vistos falhando antes** de qualquer mudança:

1. **Retenção.** Um host com `AddNetApiFlow` e um `ITraceStore` cujo `ApplyRetentionAsync` lança. O intervalo de
   retenção é curto **só no teste** e entra **sem API pública** (sugestão: o `RetentionService` recebe um valor interno
   de intervalo, registrado com 5 minutos, que o teste substitui na DI, e os projetos de teste já enxergam o núcleo por
   `InternalsVisibleTo`). O esperado: o host **continua respondendo** depois de vários ticks. Hoje: o host para.
2. **Escritor.** Um host com um `ITraceStore` cujo `SaveAsync` lança `OperationCanceledException` **sem o desligamento
   ter começado**. O esperado: o host continua, o contador de falhas sobe, e a **trace seguinte** é gravada. Hoje: o
   `ExecuteAsync` termina com a exceção e o host para.

## Tarefa 61.1: o `RetentionService`

Cada tick em `try/catch`. A falha é logada em `Warning`, com a exceção, e o laço segue para o próximo tick. O
cancelamento do desligamento continua saindo limpo, como hoje. **O limite do log é o próprio intervalo de 5 minutos:**
uma linha por tick falho, no máximo 288 por dia, e nenhum contador de supressão (KISS). A mensagem é em inglês e diz o
que acontece depois ("retention will be tried again at the next interval").

## Tarefa 61.2: o `PersistenceWriter`

O filtro `catch (Exception ex) when (ex is not OperationCanceledException)` deixa escapar uma
`OperationCanceledException` que o próprio store lança fora do desligamento, por exemplo um timeout de comando
convertido em cancelamento. Passa a ser `when (!stoppingToken.IsCancellationRequested)`, ou equivalente. Dentro do
desligamento a exceção continua propagando, e o host a ignora. O resto do corpo do `catch` (contador, métrica, log) não
muda.

## Tarefa 61.3: a varredura de todo serviço de fundo e de partida de `src/`

Nenhum `BackgroundService`, `IHostedService` ou `IStartupFilter` pode deixar exceção escapar do `ExecuteAsync`, do
`StartAsync`, do `StopAsync` ou do construtor que a DI resolve na partida. A Executora **lista o que encontrou**, uma
tabela com o serviço, o ponto onde uma exceção poderia escapar, e o que fez.

O que a Planejadora já viu, para ela conferir e não refazer do zero:

| Serviço | Em | Visto |
|---|---|---|
| `RetentionService` | `main` | **Escapa** qualquer exceção do `ApplyRetentionAsync` (61.1) |
| `PersistenceWriter` | `main` | **Escapa** um `OperationCanceledException` fora do desligamento (61.2). O resto do corpo está em `try/catch` |
| `SqlClientCommandListenerWarmupHostedService` | `main` | `StartAsync` em `try/catch` com log. Sem achado |
| `FlowAuthorizationFilterListenerWarmupHostedService` | `main` | `StartAsync` em `try/catch` com log. Sem achado |
| `NetApiFlowWarmupHostedService` | `main` | `StartAsync` e `StopAsync` vazios, mas o **construtor** resolve `FlowActivityListener` e `FlowEventPublisher` na partida. Conferir se algum faz I/O ou pode lançar |
| `NetApiFlowStartupFilter` | `main` | Conferir |
| `DashboardFallbackPolicyWarning` | **só `develop`** (#374) | `IHostedService`. Conferir |

Os serviços que só existem no `develop` (o `DashboardFallbackPolicyWarning`, e a criação de schema da #373, que roda
dentro dos stores) são **conferidos na `develop` depois do back-merge**, e uma falha achada ali é correção da `0.6.0`
(é regressão de uma PR da própria `0.6.0`, o que o D-232 permite). Fora desta varredura, e fora do hotfix, ficam os
callbacks de `ActivityListener`, que rodam na thread do usuário e já têm a regra de `tracing.md`.

## Tarefa 61.4: os testes

Os dois testes da 61.0, agora **verdes** nos dois TFMs, mais:

- **Recuperação.** A retenção falha num tick e **funciona num tick posterior**: o serviço não morreu na primeira falha.
- **A falha é logada** em `Warning`, e a requisição não é afetada.
- **Desligamento limpo.** `StopAsync` termina rápido e sem exceção, com e sem falha em curso.
- **Escritor.** A exceção fora do desligamento conta como falha e a trace seguinte é gravada.
- Um teste por serviço da 61.3 cuja varredura achar um ponto de escape.

## Tarefa 61.5: versão, CHANGELOG e o teste com o pacote real

- `Directory.Build.props`: `Version` `0.5.3`. `src/NetApiFlow.Dashboard/ClientApp/src/assets/demo-fixtures/config.json`: o
  `version` correspondente, que o `DemoFixtureVersionTests` compara (D-193).
- `CHANGELOG.md`, em inglês, `## [0.5.3]`, `### Fixed`: o host do usuário não para mais quando o banco de diagnóstico
  fica fora do ar, e o que acontecia antes. **Sem `### Security`**: é disponibilidade por falha de infraestrutura, não
  vulnerabilidade, decisão da auditoria.
- Teste com o pacote real empacotado em `tests/manual-verification/` (`-p:Version=0.5.3-verify.<n>`, D-153 e D-174),
  `AddNetApiFlow()` sem lambda, `Development`, storage real e pelo menos uma requisição, como o `docs/gitflow.md` pede.
  **O caso com o PostgreSQL real derrubado**, por mais de um intervalo de retenção, é da auditoria.

## Tarefa 61.6: a allowlist do gitleaks vai junto na `main` (D-239)

Achado durante a PR, não previsto no plano: o `secret-scan` do CI roda o gitleaks em modo git com `fetch-depth: 0`, que traz **todas as refs**, e o histórico do `develop` tem o JWT falso dos testes do sanitizador (#388). A `main` não tinha a `[allowlist]` que a #388 pôs só no `develop`, então a PR do hotfix falhou no scan com 4 achados, embora o diff dela não tivesse segredo nenhum. O hotfix levou o mesmo `[allowlist]` do `develop`, **byte a byte**, só da regex ancorada do token falso, sem caminho de arquivo e sem mexer no workflow. Com a config do `develop` o scan de todas as refs passa (512 commits, nenhum achado). A auditoria aprovou.

## Fora de escopo

- Mudar o `HostOptions` ou o `BackgroundServiceExceptionBehavior` do usuário.
- Reconexão ou fila durável quando o banco volta. Só se pede que o host não morra.
- Qualquer API pública, opção nova ou mudança de comportamento além de capturar e logar.
- O `AutoCreateSchema` e o `DashboardFallbackPolicyWarning` na `main`: não existem lá.

## Critério que vale para a fase inteira

- Build, testes, lint e formato limpos nos dois TFMs. `AotCoreVerification` com zero aviso.
- Os dois testes de host real **vistos falhando antes** e passando depois, com a saída real.
- A tabela da varredura preenchida.

## Versionamento e fluxo

- `0.5.3`, PATCH. `hotfix/0.5.3` a partir de `main`, PR para `main` (squash). Tag, Release e back-merge pela auditoria
  (D-198). O back-merge pode conflitar com o `CHANGELOG.md` e com os arquivos de storage do `develop`: quem o faz
  confere que o teste e a correção sobrevivem.
- Commits pequenos, em inglês, uma linha, sem linha de coautor. Abrir a PR e parar.

## Relatório final

Para cada um dos dois defeitos: o teste, a saída dele falhando antes e passando depois, nos dois TFMs. A tabela da
varredura. O que mudou de comportamento (nada além de capturar e logar). O resultado do teste com o pacote real.
