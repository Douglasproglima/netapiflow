# Fase 69: a suíte roda no runtime .NET 11, e o README diz a verdade sobre o `net8.0` e o `net11.0` (D-250, ADR-0025)

O ADR-0025 decidiu duas coisas em 20/11/2026: o CI roda a suíte também no runtime .NET 11, contra a build de `net10.0`, e o
`net8.0` sai do pacote numa data anunciada, 10 de maio de 2027 (D-195). Nenhuma das duas foi feita. O item 16 do roadmap dizia
"`0.6.0`, junto com a Fase 51. Não planejada em tarefas ainda", e ninguém o quebrou em tarefas.

Entra no `0.6.0` por decisão da auditoria (21/11/2026): o item já era escopo da `0.6.0` pelo D-189, antes do congelamento, então
não é achado novo (D-232). É a única prova do que o README promete para um app `net11.0`.

Esta fase é curta e independente. Entra na fila depois que a Tarefa 53.2 abrir a PR.

## O que a Planejadora conferiu

| Afirmação | Conferi |
|---|---|
| Nada roda no runtime 11 | **Confirmado.** `ci.yml` e `release.yml` instalam só `8.0.x` e `10.0.x`. Não há alvo `net11.0`, `DOTNET_ROLL_FORWARD` nem roll-forward em `.github/`, `tests/` ou `Directory.Build.props` |
| O README contradiz o ADR-0025 | **Confirmado.** `README.md:991-1000` e `README.pt-BR.md:1003-1011` dizem que o `net8.0` "só deixa de ser publicado quando o `net12.0` puder substituí-lo" e que "um artefato próprio para o `net11.0` está planejado". Nenhum dos dois tem a data de 10/05/2027 |
| O CHANGELOG tem a mesma pendência | A frase "when to stop shipping `net8.0` is still an open question" está na seção **já publicada** `0.4.1`. Não se edita. O anúncio entra como entrada nova do `[Unreleased]` |
| O mecanismo funciona | **Medido pela Planejadora** em `develop` (`19dd20d`), nesta máquina, com os runtimes `10.0.15` e `11.0.0` lado a lado. Ver a seção seguinte |

## O que a Planejadora mediu

Build de `net10.0`, `dotnet test --no-build -f net10.0`, com `DOTNET_ROLL_FORWARD=LatestMajor`.

- **A prova de que rodou no 11.** Um teste de sonda lê `NETAPIFLOW_EXPECT_RUNTIME_MAJOR` e compara com
  `Environment.Version.Major`. Esperando `11` **sem** o roll-forward, ele **falha** (o teste rodou no 10). Esperando `11`
  **com** `LatestMajor`, ele passa. Sem essa prova, um roll-forward que não aconteceu deixaria o passo verde rodando no 10.
- **A suíte no runtime 11, na build de `net10.0`:** `NetApiFlow.Tests` 347 de 347 (346 mais a sonda), `Mcp.Tests` 85 de 85,
  `OpenTelemetry.Tests` 17 de 17, `PrePipelineInteropSpike` 6 de 6 e `EntityFrameworkCore.Ef8Tests` 4 de 4.
  `IntegrationTests` 410 de 411: a falha é `DashboardSecurityHeadersTests.TheEmbeddedIndexHtml_HasNoInlineScript...`, e **o
  mesmo teste falha no runtime 10 com o mesmo `dist`**. A causa é o `dist` que a Planejadora copiou de um checkout antigo,
  não o runtime.
- **Não medido:** os projetos com Docker (`Storage.Tests`, `Npgsql.IntegrationTests`, `SqlClient.IntegrationTests`) e a
  `IntegrationTests` com um `dist` novo. A Tarefa 69.0 mede.

## Ponto de parada

- Se um teste falhar de verdade no runtime 11 (não por `dist` velho nem por ambiente), **para e relata**. É um achado sobre o
  ASP.NET Core 11 ou sobre o NetApiFlow nele, e a auditoria decide. Não pular o teste, não filtrá-lo e não rodar só o que passa.
- Se o passo ficar verde sem o guarda provar que rodou no 11, a tarefa **não está pronta**.
- Se instalar o `11.0.x` mudar o SDK que o `dotnet build` e o `dotnet pack` usam (o `global.json` fixa o `10.0.100` com
  `latestFeature`), para: o build e o pacote têm de continuar no SDK 10.

## Tarefa 69.0: a linha de base, antes de tocar no CI

Num checkout limpo de `develop`, com `npm ci` e `npm run build` para o `dist` ser o do commit, rodar a lista de testes do `ci.yml`
na build de `net10.0` com `DOTNET_ROLL_FORWARD=LatestMajor` e o guarda esperando `11`, incluindo os projetos com Docker. Dizer, por
projeto, quantos passaram. Cada projeto de teste da lista tem de ter o alvo `net10.0`; se algum não tiver, dizer qual e por quê.

## Tarefa 69.1: o guarda

Um teste em `NetApiFlow.Tests` que lê `NETAPIFLOW_EXPECT_RUNTIME_MAJOR`. Sem a variável, não faz nada. Com ela, afirma
`Environment.Version.Major` igual ao valor. Atenção: o analisador do repositório recusa `ToString()` sem cultura (CA1305), então
a comparação é de inteiros, com `int.Parse(..., CultureInfo.InvariantCulture)`.

## Tarefa 69.2: o passo no `ci.yml` e no `release.yml`

Na mesma lista de testes nos dois arquivos (D-183). Só no `ubuntu-latest`, que é onde os projetos com Docker rodam.

- Acrescentar `11.0.x` à lista de `dotnet-version` do `setup-dotnet`, ao lado de `8.0.x` e `10.0.x`.
- Depois do passo de testes de hoje, um passo novo que roda a **mesma lista** com `-c Release --no-build -f net10.0`,
  `DOTNET_ROLL_FORWARD=LatestMajor` e `NETAPIFLOW_EXPECT_RUNTIME_MAJOR=11`.
- Imprimir no log o `dotnet --list-runtimes` e o `dotnet --version` que o build usa, para a prova ficar à vista.
- Medir quantos minutos o passo acrescenta e dizer no relatório. Se passar de um terço do tempo do job, parar e perguntar.

## Tarefa 69.3: a prova vermelha no CI

Como na 50.11 e na 50.12: um commit de propósito que faz o passo falhar (o guarda esperando `12`, ou o roll-forward removido), a
saída colada no relatório, e depois o commit desfeito. Sem o vermelho visto no CI, o passo não está provado.

## Tarefa 69.4: o texto, nos dois idiomas

Só depois de a 69.2 estar na mesma PR, porque o texto promete o que o passo prova.

- `README.md` e `README.pt-BR.md`: trocar o parágrafo do `net8.0` e do `net11.0` (o de `README.md:991-1000` e o equivalente em
  português) por frases que dizem, em linguagem simples:
  - o `net8.0` saiu do suporte da Microsoft em 10/11/2026, o NetApiFlow ainda publica um artefato para ele, e o `net8.0` sai do
    pacote na **primeira versão publicada a partir de 10/05/2027**. Quem precisar dele depois disso fica na última versão que o
    tem;
  - um app `net11.0` usa o artefato de `net10.0`, e o CI roda a suíte no runtime .NET 11 contra a build de `net10.0`;
  - um artefato próprio para o `net11.0` só existe no dia em que o código usar uma API exclusiva do 11.
- Apagar as duas frases que contradizem o ADR-0025: "só deixa de ser publicado quando o `net12.0` puder substituí-lo" e "um
  artefato próprio para o `net11.0` está planejado".
- `CHANGELOG.md`, em inglês, no `[Unreleased]`, em `### Changed`: o anúncio da saída do `net8.0` com a data, e uma linha dizendo
  que a suíte passa a rodar no runtime .NET 11. **Não editar** a seção `0.4.1`.
- Procurar no repositório (fora de `docs/adr/` e `docs/plano/`) qualquer outra frase com "net12", "artefato próprio" ou "asset of
  its own", listar o que achou e corrigir. Estilo: `.claude/rules/writing-style.md`, sem travessão.

## Fora de escopo

- Criar o alvo `net11.0` no `Directory.Build.props`. O ADR-0025 só o permite quando o código precisar de uma API exclusiva do 11.
- Rodar o `AotCoreVerification` no runtime 11, e rodar a suíte no Windows no runtime 11.
- O texto do R-32 e o item 16 do roadmap: são da Planejadora (D-250).

## Critério que vale para a fase inteira

- Build, testes, formato e lint limpos nos dois TFMs. `AotCoreVerification` com zero aviso.
- O passo do runtime 11 verde no `ci.yml` e no `release.yml`, com o guarda provando no log que rodou no 11, e a prova vermelha
  vista no CI.
- O README diz só o que o CI prova.

## Versionamento e fluxo

- `0.6.0`. Branch `chore/ci-runtime-net11` a partir de `develop` atualizado. Commits pequenos, em inglês, uma linha, sem linha de
  coautor. Tem código e workflow: abrir a PR e parar, o merge é da auditoria.

## Relatório final

A linha de base por projeto (69.0). O guarda vermelho e verde. O link da execução do CI com o `dotnet --list-runtimes` e a prova
do runtime 11. A saída da prova vermelha. Os minutos que o passo acrescenta. O diff dos textos e a lista do que a busca por
"net12" e "artefato próprio" achou.
