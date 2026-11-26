# A `0.7.0`: o que o roadmap oferece e o que a Planejadora recomenda (D-317 e D-318)

**Rascunho de 25/11/2026, para o dono e a auditoria confirmarem.** Escopo e prioridade são do dono (D-198). Esta página junta o que o `roadmap-valor.md` marcou como `0.7.0`, agrupa por tema e diz o que a Planejadora faria. Nada aqui é plano até o dono responder as perguntas do fim. Nenhuma fase foi escrita ainda, e nenhum item foi reordenado no roadmap por causa deste rascunho.

## De onde a `0.7.0` parte

Conferido na árvore do `develop` em `da11427` (a #531, o passo 19 do release):

- A linha de base da comparação é a `0.6.0` (`tests/real-package/previous-published-version.txt`), e `tests/real-package/expected-differences.json` está vazio. Toda mudança de comportamento visível a quem atualiza entra nessa lista e no `[Unreleased]` do `CHANGELOG.md` **na mesma PR**, senão o `comparison` fica vermelho (D-310 e D-315).
- Os sete `PublicAPI.Unshipped.txt` só têm `#nullable enable`, e o `CompatibilitySuppressions.xml` do `NetApiFlow.Mcp` não existe mais. Quebra de API pública derruba o `pack` contra a `0.6.0`, então ela só entra com o nome antigo `[Obsolete]` encaminhando, como a Fase 67 fez (D-237 e D-277), ou com uma supressão que a PR justifica.
- O congelamento da `0.6.0` (D-232 e D-236) acabou com a tag. A regra do hotfix continua (D-205): só regressão nova ou falha de severidade alta. Defeito antigo vai para a minor seguinte.

## O que está na mesa

Quarenta e um itens do roadmap dizem `0.7.0`. Agrupados por tema, cada um aparece uma vez. O número é o do roadmap de hoje, e o texto curto está aqui porque a tabela do roadmap é renumerada.

### A. A verdade do fluxo (8 itens, defeitos antigos que mostram algo errado)

| Item | O que é | Peso |
|---|---|---|
| 52 | A rota gravada de uma requisição tratada por `UseExceptionHandler` ou `UseStatusCodePagesWithReExecute` é a da página de erro: o `/boom` aparece como `GET /error 500`. A `0.5.3` faz igual | 🟡 |
| 51 | A visão ao vivo de uma requisição sem endpoint casado só aparece quando termina (na `0.5.3` aparecia como `Running`) | 🟡 |
| 46 | Um status `Cancelled` de verdade para o span, hoje fechado como `Error` | 🟡 |
| 45 | No 500 que o Kestrel monta em `Production`, o trace sai com `Status = Error` e `HttpStatus = 200` | 🟢 |
| 47 | Os listeners de `SqlClient` e de autorização assinam por `AllListeners` e não conferem de quem é a requisição (efeito de teste, com dois hosts no processo) | 🟢 |
| 53 | O `Detach` do `FlowTraceResolver` não solta a referência à atividade do servidor no holder | 🟢 |
| 48 | A consulta do blueprint compara o método com `ILIKE` e `UPPER`, e isso impede o índice `(method, route_key)` | 🟢 |
| 44 | Em `Production`, `DELETE` e `PUT` em `/flow/mcp` respondem 405 e o resto do produto responde 404 | 🟢 |

### B. A API pública antes da 1.0 (12 itens, mais o caminho para a 1.0)

| Item | O que é | Peso |
|---|---|---|
| 22 | Caminho para a 1.0: congelar a API pública e a política de depreciação. Pede ADR, e não está marcado `0.7.0` | 🟡 |
| 32 | Namespaces espalhados e nomes inconsistentes (`IncludePatterns` e `IncludePaths`, `Retention*`, `DateTime` e `DateTimeOffset`) | 🟡 |
| 34 | Os limites fixos (500 traces, 300 spans, fila de 1000) virarem `LimitsOptions` | 🟡 |
| 41 | Papel só leitura para `DELETE /flow/api/traces` e `POST /flow/api/deploys` | 🟡 |
| 59 | Os satélites conferirem a versão do núcleo na partida e avisarem quando diferir | 🟡 |
| 31 | Unificar o `DashboardAccessGate` e o `McpAccessGate` | 🟢 |
| 33 | O `Meter` estático virar `IMeterFactory` | 🟢 |
| 36 | Sobrecargas dos storages com `NpgsqlDataSource` e `IOptions` | 🟢 |
| 37 | `Problem` com `detail` nas respostas de erro do REST | 🟢 |
| 38 | Schema e `enum` nas tools do MCP | 🟢 |
| 42 | O atributo `sql` do OTLP virar `db.query.text` | 🟢 |
| 57 | Tirar dois `InternalsVisibleTo` do núcleo (os storages) | 🟢 |
| 58 | Tirar o `InternalsVisibleTo` do painel, com uma interface pública pequena e só de leitura sobre as traces vivas | 🟢 |

### C. Medida e caminho quente (3 itens, mais o benchmark sob concorrência)

| Item | O que é | Peso |
|---|---|---|
| 65 | Achar a causa de o `Flow.Step` com `Enabled = true` custar mais que na `0.5.3`: no `net8.0`, +41 ns de mediana (cerca de 15%) em 8 de 8 rodadas, dentro do ruído no `net10.0` | 🟢 |
| 43 | No `net8.0`, o `HttpClientActivityTagBackfill` usa reflexão a cada evento de `HttpClient` | 🟢 |
| 35 | As alocações do caminho quente | 🟢 |
| 9 | Benchmark sob concorrência. Não está marcado `0.7.0`, mas é a única afirmação numérica do README ("cerca de 20μs por requisição") que nunca foi medida com várias requisições ao mesmo tempo | 🟡 |

### D. Painel, teclado e demo (5 itens)

| Item | O que é | Peso |
|---|---|---|
| 39 | O resto do teclado: setas no `radiogroup`, link de pular, `Esc` no painel da etapa, região `live` | 🟡 |
| 60 | Levar o foco do teclado ao título da tela nova em toda troca de rota | 🟡 |
| 49 | A demo gravada não tem nenhuma execução que pule um passo, então o nó "Não executado" nunca aparece nela. Pede um endpoint de retorno antecipado no sample | 🟡 |
| 40 | O painel mostrar o erro de schema ausente (hoje só o log diz) | 🟢 |
| 72 | A tela Arquitetura escreve "1 execuções nesta janela" | 🟢 |

### E. A rede, as ferramentas e os pequenos (11 itens)

| Item | O que é | Peso |
|---|---|---|
| 56 | O job do pacote real não tem banco: nenhuma integração de SQL tem cenário permanente com o pacote publicado | 🟡 |
| 71 | Um tipo `node-test` no `check-regression-guards.mjs`, para os testes de `scripts/*.test.mjs` ganharem linha no `guards.json` | 🟡 |
| 54 | Simplificar a espera em `TraceIdentityHost.SpansOfAsync` (só teste) | 🟢 |
| 55 | O Npgsql 8, piso do `Storage.PostgreSql`, não está na matriz de integração | 🟢 |
| 61 | O gerador do `DESIGN.md` quebra com "Expected 7 typography rows in guide.md, found 0" | 🟢 |
| 63 | Um aviso em código para o `OpenTelemetry.Instrumentation.AspNetCore` 1.21.0 ou posterior num app `net8.0` | 🟢 |
| 64 | Estender a checagem de citações a `scripts/` e `.github/` | 🟢 |
| 66 | A mensagem do aviso do `FallbackPolicy` diz "answers 401", e com cookie o desafio é um 302 | 🟢 |
| 67 | O `detect-changes` ligar o `real-package` e o `comparison` também quando só o `CHANGELOG.md` muda | 🟢 |
| 68 | O `verify.sh` do `OptionsFromConfigVerification` grava logs dentro da própria pasta | 🟢 |
| 70 | `NetApiFlow*` no `ignore` do `dependabot.yml` (só vale a partir da `main`) | 🟢 |

Casam com este grupo, sem a marca `0.7.0`: o item 11 (testes sensíveis a timing sob carga de CI, seis ocorrências) e o item 15 (a limpeza da regra de ouro em escala, que pede um levantamento antes de virar fase).

### F. O site e a divulgação (não é código deste repositório)

| Item | O que é | Peso |
|---|---|---|
| 50 | O quick start do site não tem rede nenhuma e já quebrou antes. Dar ao site um passo que cola e compila o quick start num app novo | 🟡 |
| 1 | Divulgar o `NetApiFlow.Testing` como coisa separada. O D-190 pôs a divulgação depois da `0.6.0`, e ela saiu | 🔴 |
| 20 | A seção "Why not OpenTelemetry?" no README, nos dois idiomas | 🟡 |
| 21 | `NetApiFlow.Testing` como porta de entrada: orçamento de performance no CI | 🟡 |

### Em espera

O item 62 (a corrida do formulário do Runner) não foi reproduzido em 100 tentativas. Fica de fora até alguém reproduzir com um backend real. O item 69 é uma nota, sem trabalho.

## O que a Planejadora recomenda

**Uma `0.7.0` sem quebra de API, e a janela da API na versão seguinte.** Os grupos A, C, D, E e F entram na `0.7.0`. O grupo B, junto do ADR do item 22, vira a versão seguinte, com a migração toda de uma vez.

Os motivos, em ordem de peso:

- O D-205 existe porque cada versão foi conferida só pelo próprio diff, e cada furo antigo achado depois virou hotfix. Uma versão menor é uma versão que a auditoria consegue conferir inteira.
- O grupo B mexe em nome, namespace e opção pública. Quem atualiza paga isso uma vez só se vier junto, com a política de depreciação escrita antes (item 22). A `0.6.0` já carregou a troca de `NetApiFlow` para `Flow`. Quem atualiza ganha com uma `0.7.0` tranquila.
- O grupo A é o que o usuário vê: a rota certa no `/boom`, o status certo no 500, a requisição aparecendo ao vivo. É o que a promessa do produto pede, e o item 52 já está reproduzido contra o pacote publicado.
- O grupo C responde uma pergunta aberta: o README publica um número e a `0.6.0` mediu +41 ns no `net8.0` sem achar a causa. Medir sob concorrência (item 9) e achar a causa (item 65) juntos fecham a tabela de desempenho.

Ordem sugerida, que muda sem custo:

1. **E, o lote barato,** primeiro. Protege o que vem depois: o `real-package` com banco (56) e o `node-test` (71) são o que faltou para pegar defeitos de script e de integração.
2. **A**, começando pelos itens 52, 45 e 51, que mudam o que se vê. O 46 pede desenho e, se mexer num enum público, passa para a versão da API.
3. **C**, com o item 65 antes dos itens 43 e 35: só se mexe no caminho quente depois de saber qual é a causa.
4. **D**, por último, porque é a parte que mais depende do navegador e da demo.
5. **F** corre em paralelo e não espera nenhuma fase. O dono decide canal e data (item 1).

Fica de fora, por precisar de decisão de arquitetura ou de produto antes de virar fase: os itens 2, 7, 10, 13, 14 e 23 do roadmap, o entry point não-HTTP (D-61) e os itens 4, 5 e 6.

## Como a `0.7.0` não vira outra `0.6.0`

A `0.6.0` cresceu até o congelamento (D-232 e D-236). A recomendação é congelar na abertura: só entra segurança, regressão de uma PR da própria `0.7.0` e o que o dono decidir de forma explícita. Achado novo vai para o roadmap. Cada grupo fecha com a leitura da auditoria antes de o próximo abrir, e a `0.7.0` sai quando os grupos combinados fecharem, sem grupo novo no caminho.

## Perguntas ao dono

1. **O tamanho.** A `0.7.0` leva os grupos A, C, D, E e F (recomendado), e a API (B) fica para a versão seguinte? Ou tudo numa versão só?
2. **A divulgação do `NetApiFlow.Testing` (item 1).** Começa agora, em paralelo, ou espera a `0.7.0`?
3. **A política de depreciação (item 22).** A Planejadora escreve o ADR agora, para a versão da API já nascer com ele?
4. **O congelamento.** Vale desde a abertura, com a regra de cima?

**A pergunta que era da auditoria já foi respondida (D-318):** a auditoria de base vale para as minors seguintes de forma reduzida. A Planejadora tinha sugerido só atualizar as matrizes pelo que mudou, e a auditoria mostrou que não basta, porque os furos de 25/11 estavam em partes que nenhuma PR do ciclo tocou. O que ela exige está na seção seguinte. Se o dono quiser outra régua, ele decide.

## O que a auditoria exige antes do release (D-318)

Relato da auditoria. Vale para a `0.7.0` e para as minors seguintes:

1. As matrizes de autorização, de integrações, de `PathBase` e proxy e o mapa de cobertura do CI, atualizadas pelo que mudou.
2. Um núcleo fixo, refeito inteiro com o pacote real antes do release, mudando ou não: o quick start no `net10.0` e no `net8.0` colado na ordem, todas as telas do painel e da demo num navegador (nos dois temas e em 390 px), `Host` e `Origin`, os cabeçalhos e o 404 em `Production`, um banco real (PostgreSQL e SQL Server) com o filtro e o reinício, e a comparação com a versão publicada. A auditoria estima uma ou duas horas.
3. O inventário completo, o README frase a frase e o mapa inteiro, antes da `1.0` e sempre que uma minor mudar a API pública.

Isto pesa no desenho da `0.7.0`. Se ela não mudar a API pública, o terceiro item não é exigido, e é mais um motivo para deixar o grupo B para a versão seguinte. Quem diz se uma mudança conta como mudança da API pública é a auditoria. O banco real do segundo item é manual até o item 56 do roadmap (o job do pacote real não tem banco) entrar no grupo E, e então vira cenário do CI.

## O que a Planejadora faz depois da resposta

Escreve os arquivos de fase do que o dono confirmou, na ordem que ele confirmar, atualiza o `README.md` do plano e marca no roadmap o que entrou. Não abre branch de código, não conduz nenhum passo público e não fecha nenhum grupo no lugar da auditoria.
