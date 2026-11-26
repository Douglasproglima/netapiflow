# Fase 72: a rede, as ferramentas e os pequenos (D-320, grupo E da `0.7.0`)

**Estado (26/11/2026):** planejada, não iniciada. É a **primeira** fase da `0.7.0` (D-320), e a auditoria a fecha com o pacote real antes de a Fase 73 abrir.

A `0.6.0` terminou com um aprendizado: três furos (a Arquitetura da demo, o `Activity.Current` preso no `SqlClient`, o
`comparison` sem ler o `CHANGELOG`) ficaram fora do diff de qualquer PR do ciclo, e só uma passada pelo sistema os achou
(D-318). Esta fase é o que ensina o CI a pegar esse tipo de furo sozinho, mais as ferramentas pequenas que ficaram para
trás e dois ajustes pequenos no produto. Onze itens do roadmap, em três partes.

A `0.7.0` **não quebra a API pública** (D-320). Nenhuma tarefa daqui pode acrescentar ou remover uma linha do
`PublicAPI.Shipped.txt` ou do `PublicAPI.Unshipped.txt`. Se uma precisar, a Executora **para** e relata.

## Os itens e o que a Planejadora conferiu

| # | Item do roadmap | Conferi |
|---|---|---|
| 56 | "O job do pacote real não tem banco": nenhum cenário permanente do pacote publicado exercita o Npgsql nem o SqlClient. A guarda da 58.11 vive só no `SqlClient.IntegrationTests`, que roda no ubuntu com contêiner e não roda no windows | Li o texto do item e a D-286. Não li o `real-package.yml` |
| 71 | O `check-regression-guards.mjs` só conhece os tipos `dotnet`, `vitest` e `ci-scenario`. Os testes de `scripts/*.test.mjs` rodam no CI e nada os lê | Li o texto do item e a D-315 |
| 67 | O `detect-changes` do `ci.yml` só liga o `real-package` e o `comparison` quando um README muda (`^README(\.pt-BR)?\.md$`). O `comparison` também lê o `CHANGELOG.md` | Conferi a regra no `ci.yml` e na D-310. Uma PR só de `CHANGELOG.md` passou sem o job na rodada da `0.6.0` |
| 55 | O Npgsql 8, piso do `Storage.PostgreSql`, não está na matriz: `Npgsql.IntegrationTests` roda no 9.0.3 e `Npgsql10.IntegrationTests` no 10.0.0 | Li o item e a D-284 |
| 54 | A espera em `TraceIdentityHost.SpansOfAsync` existe porque a trace podia ficar sem os spans, e desde a Fase 64 a trace e os spans entram juntos. É só teste | Li o item. O arquivo é `tests/NetApiFlow.Storage.Tests/TraceIdentityHost.cs` |
| 61 | O gerador do `DESIGN.md` quebra com "Expected 7 typography rows in guide.md, found 0": a escala saiu do `guide.md` e foi para o `tokens.md` | Vi o erro eu mesma ao tentar regenerar em 25/11/2026. O script é `src/NetApiFlow.Dashboard/ClientApp/scripts/generate-design-md.js` |
| 64 | A checagem de citações (`scripts/check-citations.mjs`) cobre `src/` e `tests/`. Os comentários dos workflows e dos scripts ainda citam tarefa, PR e decisão (por exemplo `tarefa 7.7` no `release.yml`) | Li o item e a D-300 |
| 68 | O `verify.sh` do `OptionsFromConfigVerification` grava `app-<tfm>-<modo>.log` dentro da própria pasta, e o `.gitignore` não os cobre | Achado da `EXECUTORA-RELEASE`, D-311 |
| 70 | `NetApiFlow*` no `ignore` do `dependabot.yml`, para o Dependabot não subir o `NetApiFlow` `0.5.3` que o `Fase59DesempenhoVerification` fixa de propósito. O arquivo só vale a partir da `main` | Li o item e `dependencies.md` |
| 66 | O aviso do `FallbackPolicy` diz que o middleware de autorização "answers 401", e com autenticação por cookie ele responde um 302 | Medido pela auditoria na 59.5 (D-309). O arquivo é `src/NetApiFlow.Dashboard/DashboardFallbackPolicyWarning.cs` |
| 63 | Um aviso em código para o `OpenTelemetry.Instrumentation.AspNetCore` 1.21.0 ou posterior num app `net8.0` (puxa o `System.Diagnostics.DiagnosticSource` 11.0.0 e o app deixa de responder com `MissingMethodException`) | Achado da Executora na 59.0 (D-298). É do ecossistema, não do produto |

## Ponto de parada

- Qualquer tarefa que acrescente ou remova uma linha do `PublicAPI` **para**. A `0.7.0` não quebra a API (D-320), e uma
  linha nova ou removida já conta como mudança (D-318).
- **O `real-package.yml` é chamado pelo `release.yml`** (`uses: ./.github/workflows/real-package.yml`) e é o `needs` do job
  de publicação. Quem muda esse arquivo mantém a interface dele e diz no relatório que **não exercitou** o caminho do
  `release.yml`, porque só a tag o roda. Se a mudança exigir mexer na interface, para e relata.
- Na 72.1(a): se o job `real-package` mais que dobrar de duração, ou se o SQL Server não subir de forma confiável no
  runner, para e relata com os tempos medidos. Não fica com um cenário que falha ao acaso.
- Na 72.3(b): o aviso do item 63 só entra se a checagem for uma **comparação de versão de assembly já carregado**, sem
  reflexão além de `Assembly.GetName().Version`, sem carregar nada, limpa para o AOT (`AotCoreVerificationNet8` sem
  aviso `IL`) e incapaz de lançar. Se não couber nisso, o item 63 fica como está (o README e a página do pacote já
  mandam usar a 1.20.1) e a Executora relata.

## Tarefa 72.0: reproduzir e medir, antes de mexer

Num `develop` limpo, nos dois TFMs quando se aplica:

1. **56.** A duração do job `real-package` e do `comparison` hoje (de três runs recentes do `develop`), e a confirmação
   de que nenhum cenário toca banco.
2. **71.** O que o `check-regression-guards.mjs` aceita hoje, e quais testes de `scripts/*.test.mjs` o CI roda sem que
   nenhuma linha do `guards.json` os cite.
3. **67.** Uma PR de teste que muda só o `CHANGELOG.md` e **não** liga o `real-package` nem o `comparison`. Fechada sem merge.
4. **55.** As versões de Npgsql de cada projeto de integração, e se o piso `8.0.5` do `Storage.PostgreSql` já foi usado em
   algum teste automático.
5. **61.** O comando e a saída do erro do gerador.
6. **64.** `node scripts/check-citations.mjs` apontado para `scripts/` e `.github/`: a contagem de achados e a lista.
7. **66 e 63.** O texto do aviso com autenticação por cookie e por JWT (a saída real), e, para o 63, se a versão de assembly
   do `System.Diagnostics.DiagnosticSource` carregado no processo `net8.0` com o OpenTelemetry 1.21.0 é legível sem
   reflexão.

Se um item **não** reproduzir, a Executora para nele e relata.

## Tarefa 72.1: a rede (itens 56, 71, 67, 55, 54)

**(a) Item 56: o banco no job do pacote real.**

- Serviços de PostgreSQL e de SQL Server no job `real-package`, e **um cenário permanente por integração** (Npgsql e
  `SqlClient`), contra o pacote publicado e contra o build do `develop`.
- O app do cenário faz **vários comandos no mesmo método**: é o caso que a 58.11 corrigiu e que hoje só o
  `SqlClient.IntegrationTests` guarda. A prova é que cada comando vira o seu passo, com o SQL sem parâmetro.
- O mesmo cenário cobre o que o D-318 pede do núcleo fixo e hoje é manual: gravar traces no banco, **reiniciar o host** e
  ver as traces voltando em `GET /flow/api/traces`, e filtrar por rota e por status. O `schema.sql` vem do pacote real.
- A prova de que o cenário pega o defeito: com a correção da 58.11 desfeita numa sonda, o cenário **fica vermelho**
  no CI.

**(b) Item 71: o tipo `node-test`.** O `check-regression-guards.mjs` passa a ler a saída do `node --test`, e o
`guards.json` ganha uma linha para cada teste de `scripts/*.test.mjs` que guarda um defeito de CI, em especial o do
`comparison` com o `CHANGELOG` renomeado (D-315) e o da seção `[Unreleased]` (D-310). O teste do próprio verificador
(`check-regression-guards.test.mjs`) cobre o tipo novo, com o caso de um teste que some e o de um que falha.

**(c) Item 67: o `CHANGELOG.md` na regra do `detect-changes`.** Uma PR que muda só o `CHANGELOG.md` liga o
`real-package` e o `comparison`. A prova é a PR de sonda da 72.0, agora **rodando** os jobs.

**(d) Item 55: o Npgsql 8 na matriz.** Um terceiro projeto de integração, ao lado do `Npgsql10.IntegrationTests`, com o
provider do piso (`8.0.5`). Não muda o piso do pacote (D-181), só o prova.

**(e) Item 54.** A espera em `TraceIdentityHost.SpansOfAsync` sai, e o teste continua verde, 50 de 50 execuções em cada
TFM.

## Tarefa 72.2: as ferramentas (itens 61, 64, 68, 70)

- **61.** O gerador lê a escala de tipografia do `docs/brand/tokens.md`. A saída tem de **bater com o `DESIGN.md` de hoje**,
  e a Executora mostra o `diff` vazio, ou explica cada linha que mudar. Um teste do script cobre o formato da seção.
- **64.** `check-citations.mjs` passa a cobrir `scripts/` e `.github/`. Cada achado é limpo, ou entra numa lista de exceção
  do script com o motivo. O `release.yml` e o `real-package.yml` mantêm o comportamento, e só os comentários mudam.
- **68.** O `verify.sh` grava os logs fora da pasta da receita (num diretório temporário que ele remove), e o README da
  receita deixa de mandar "não commitar nenhum". Depois de uma execução, `git status` fica limpo.
- **70.** `NetApiFlow*` entra no `ignore` do `dependabot.yml`. O arquivo só vale a partir da `main`, então a mudança
  chega com a próxima release. Até lá, a PR do Dependabot que subir esse pacote se fecha sem merge. `dependencies.md` é da
  Planejadora, e a Executora não o edita.

## Tarefa 72.3: os pequenos no produto (itens 66 e 63)

- **66.** A mensagem do aviso do `FallbackPolicy` diz "o desafio do esquema de autenticação" (como o README e o CHANGELOG já
  dizem desde o D-309), e não "401". O nome do método `DashboardWouldAnswer401` é interno, e muda junto. As duas frases do
  README que dizem que o aviso "nomeia o 401" mudam nos dois idiomas. Teste: a mensagem com cookie e com JWT.
- **63.** Só se passar na regra do ponto de parada. Se entrar: uma linha de log de aviso na partida, uma vez, que diz o que
  fazer (usar a 1.20.1 do `OpenTelemetry.Instrumentation.AspNetCore`, a versão do SDK). Teste com a versão carregada por
  uma sonda, e o caso de a checagem não achar nada (sem aviso, sem exceção).

## Fora de escopo

- Os itens 11 (testes sensíveis a timing sob carga de CI) e 15 (a limpeza da regra de ouro em escala), que casam com este
  grupo mas não têm a marca `0.7.0`.
- Mudar qualquer piso de dependência (D-181).
- O `release.yml`, além do que a 72.2 (item 64) toca nos comentários dele.

## Critério que vale para a fase inteira

- `dotnet build NetApiFlow.slnx`, `dotnet test NetApiFlow.slnx`, `dotnet format NetApiFlow.slnx` limpos nos dois TFMs, e
  `npm run lint`, `npm run format:check`, `npm run test:ci` no `ClientApp` (a 72.2 toca o gerador).
- **Cada mudança de CI ou de script vista vermelha no CI antes do verde**, **um push por run** (um commit de mutação por
  vez, e esperar o run dele). Cada defeito que a fase passa a pegar ganha a linha no `tests/regression-guards/guards.json`.
- Nenhuma linha nova ou removida no `PublicAPI`.
- `CHANGELOG.md`, em inglês: o item 66 (e o 63, se entrar) em `### Changed`. O resto é ferramenta interna e não entra.

## Versionamento e fluxo

- `0.7.0`. Branch `chore/72-rede-e-ferramentas` a partir de `develop` atualizado. Uma tarefa por commit, em inglês, uma
  linha, sem linha de coautor. Pode ser mais de uma PR (a rede, as ferramentas, os pequenos), na ordem que a Executora
  preferir. Abrir a PR e parar.
- A auditoria fecha a fase com o pacote real e o núcleo fixo do D-318 antes de a Fase 73 abrir.

## Relatório final

Para cada item: o comando e a saída antes e depois. A duração do job `real-package` antes e depois. O que **não** foi
exercitado (o `release.yml`). Os itens que a Executora não implementou e por quê (em especial o 63).
