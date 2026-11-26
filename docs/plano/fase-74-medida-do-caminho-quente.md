# Fase 74: a medida e o caminho quente (D-320, grupo C da `0.7.0`)

**Estado (26/11/2026):** planejada, não iniciada. Abre **depois** de a auditoria fechar a Fase 73 (D-320).

O README publica um custo por requisição, e a `0.6.0` mediu duas coisas que ele não explica. Uma: ninguém mediu esse custo
com várias requisições ao mesmo tempo, que é onde o `ConcurrentDictionary` e o `Channel` do `FlowRecorder` apareceriam.
Outra: o `Flow.Step` com `Enabled = true`, dentro de uma requisição, ficou mais caro no `net8.0` que na `0.5.3`, e a causa
não foi procurada (D-313). Esta fase mede as duas, acha a causa, e só mexe no caminho quente se achar algo que valha a pena.

**Esta fase é de medida primeiro.** Nenhuma otimização entra sem o número que a justifique (KISS e YAGNI, `CLAUDE.md`).

A `0.7.0` **não quebra a API pública** (D-320). Nenhuma tarefa daqui acrescenta ou remove uma linha do `PublicAPI`.

## Os achados e o que a Planejadora conferiu

| # | Item do roadmap | Conferi |
|---|---|---|
| 9 | Benchmark sob concorrência. Os arquivos de `tests/NetApiFlow.Benchmarks` medem uma requisição por vez: sem `ThreadingDiagnoser`, sem paralelismo, sem contenção. É a única afirmação numérica do README que nunca foi medida na condição em que vai ser usada | Confirmei por inspeção em 18/11/2026 (o item). A lista de arquivos de hoje: `EnabledOverheadBenchmarks`, `DisabledOverheadBenchmarks`, `AbsentOverheadBenchmarks` e as variantes de `PathBase`, rota excluída e sem etapa de endpoint |
| 65 | O `Flow.Step` com `Enabled = true`, dentro de uma requisição, custa mais que na `0.5.3`. Em 8 rodadas pareadas: no `net8.0` mais alto em 8 de 8 (mediana +41 ns, cerca de 15%, o menor +12 ns, uns 10 µs numa requisição de 250 passos), no `net10.0` dentro do ruído (+9 ns, mais alto em 4 de 8). A alocação é igual (350 B contra 353 B, 349 B contra 352 B). Com `Enabled = false`, muito mais barato | Li a D-313 e `docs/benchmarks/0.6.0.md`. **Não repeti as medidas.** A causa não foi procurada, e o argumento de que os dois `Volatile.Read` da 60.9b não são a causa é raciocínio, não medida |
| 43 | No `net8.0`, o `HttpClientActivityTagBackfill` usa reflexão (`GetProperty(...).GetValue(...)`) a cada evento de `HttpClient`, que é caminho quente | Achado da Executora na Fase 62 (D-239). Arquivo: `src/NetApiFlow/Instrumentation/HttpClientActivityTagBackfill.cs` |
| 35 | As alocações do caminho quente, exceto compilar os padrões de rota uma vez (que entrou na Fase 53) | Li o item (D-236) |

## Ponto de parada

- **Medida honesta primeiro.** Toda medida diz a máquina, a carga dela, o runtime, as rodadas e o commit do harness, como
  `docs/benchmarks/0.6.0.md`. Uma máquina com carga (a da `0.6.0` oscilou de 18% a 96%) só dá ordem de grandeza, e o
  relatório diz isso.
- **A segurança não se troca por velocidade.** Se a causa do item 65 for um `try/catch` da Fase 65 ou a resolução da trace
  da Fase 55, que existem para "nunca derrubar a API" e "uma trace por requisição", o custo fica e o relatório o **documenta**
  com o número. A Executora não remove uma proteção para ganhar nanossegundo.
- **O núcleo é AOT-limpo** (Fase 42). Qualquer solução do item 43 passa a `AotCoreVerificationNet8` **sem nenhum aviso `IL`**.
  Se a única saída exigir reflexão não anotada, o item fica como está e o relatório diz por quê.
- **Sem otimização sem número.** Os itens 43 e 35 só entram se a 74.1 ou a 74.2 mostrar o ponto quente com a alocação ou o
  tempo medidos. Um ganho dentro do ruído não entra.
- O `Fase59DesempenhoVerification` fixa o `NetApiFlow` `0.5.3` de propósito (`dependencies.md`). A Executora não sobe essa
  linha.

## Tarefa 74.0: o benchmark sob concorrência (item 9)

1. Cenários novos em `tests/NetApiFlow.Benchmarks`, com **requisições em paralelo** (por exemplo 1, 4, 16 e 64) contra o
   mesmo host: com `Enabled = true`, com `Enabled = false` e sem o NetApiFlow. `ThreadingDiagnoser` ligado (contenção de
   lock e itens na fila do thread pool) e o `MemoryDiagnoser`.
2. O que se mede: o custo por requisição e a vazão com N em paralelo, a contenção do `FlowRecorder`
   (`ConcurrentDictionary` e `Channel`) e se a fila de gravação (1000) enche e descarta a partir de que carga.
3. **O mesmo roteiro contra o pacote publicado `0.6.0`** (e contra a `0.5.3` quando a comparação ajudar), para a Planejadora
   e a auditoria verem se a concorrência piorou algo, e não só o número isolado.
4. O resultado vai para `docs/benchmarks/` num arquivo novo, com a máquina, a carga, os runtimes, o commit e o método.
5. **O README, nos dois idiomas:** a frase do custo ganha, em ordem de grandeza, o que a concorrência mostrou, ou diz com
   clareza o que **não** foi medido. Uma afirmação numérica sem a condição dela não fica.

## Tarefa 74.1: a causa do custo do `Flow.Step` (item 65)

A medição pede isolar **uma mudança por vez**. As mudanças do caminho quente desde a `0.5.3` são as Fases 53, 55, 57 e 65 e a
60.9b.

1. Reproduzir os números da D-313 (`net8.0`, 8 rodadas pareadas contra a `0.5.3`) no mesmo harness
   (`Fase59DesempenhoVerification`), para saber se o +41 ns aparece de novo na máquina de hoje.
2. Se aparecer: **bisseção** pelos commits do `develop`, ou por referência de projeto em cada fase, medindo só o
   `Flow.Step` com `Enabled = true` dentro de uma requisição. A saída é uma tabela: o commit ou a fase, a mediana, o desvio.
3. A causa: o arquivo e a linha, e se é custo de uma proteção (então fica e se documenta) ou acidental (então se corrige).
4. Se for acidental e a correção for pequena, ela entra aqui, com a medida antes e depois, **nos dois TFMs**, e o `net10.0`
   não pode piorar.
5. Se **não** reaparecer, o relatório diz isso e o item 65 fecha como "não reproduzido na máquina de hoje", com as rodadas.

## Tarefa 74.2: reflexão no `net8.0` e alocações (itens 43 e 35), só com número

- **43.** Primeiro mede: o custo por evento de `HttpClient` da reflexão no `net8.0`, com a alocação. Se o custo for
  mensurável, trocar por uma leitura que não reflita a cada evento (um acessor guardado por tipo, por exemplo), dentro da
  regra do AOT. Se for ruído, o item fecha com a medida.
- **35.** Um perfil de alocação do caminho quente de uma requisição com `Enabled = true`. Cada alocação que sobra por
  requisição e que se evita sem deixar o código mais difícil de ler entra, com o antes e o depois. As outras ficam no relatório.

## Fora de escopo

- O grupo B (a API pública).
- Mudar o desenho do `FlowRecorder`, do `Channel` ou das stores, a menos que a 74.0 mostre contenção real e a Planejadora
  responda antes. Nesse caso a Executora **para** e relata.
- Medir storages em banco (PostgreSQL e SQL Server) sob carga: não é o custo do que o README promete.

## Critério que vale para a fase inteira

- `dotnet build`, `dotnet test`, `dotnet format` limpos nos dois TFMs, e o AOT sem aviso `IL`.
- Toda mudança de código do caminho quente com a medida antes e depois, contra o pacote publicado, nos dois TFMs.
- Cada mudança de comportamento do README com a medida que a sustenta.
- Nenhuma linha nova ou removida no `PublicAPI`.
- `CHANGELOG.md`, em inglês, só se algo visível a quem atualiza mudar (`### Changed`, ou `### Fixed` para o custo
  acidental).

## Versionamento e fluxo

- `0.7.0`. Branch `bugfix/74-caminho-quente` a partir de `develop` atualizado, depois da Fase 73 fechada. A 74.0 (só medida
  e README) pode sair numa PR própria, antes da 74.1. Commits pequenos, em inglês, uma linha, sem linha de coautor. Abrir a
  PR e parar.
- A auditoria fecha a fase com o pacote real e o núcleo fixo do D-318 antes de a Fase 75 abrir.

## Relatório final

As tabelas de medida, com a máquina e a carga. A causa do item 65, ou "não reproduzido". O que mudou no README. Cada
otimização com o antes e o depois, e as que foram descartadas por ficarem dentro do ruído.
