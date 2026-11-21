# Fase 60: o acabamento que um revisor vê no primeiro dia (D-210 nível 3, D-236 B12 e B14)

O nível 3 da auditoria de base, mais dois itens da varredura final (B12, o filtro de rota do MCP, e B14, os samples). Nada
aqui derruba o produto, mas é o que um purista abre primeiro: o celular, textos que se contradizem, o Runner pela metade,
um README de sample que não roda.

Vai no `0.6.0` (D-236). Vem **perto do fim**, depois das Fases 57 e 68 (as três mexem no painel) e **antes da Fase 59**.

## Duas PRs, como a Fase 50

- **Parte A, o painel (60.1 a 60.8):** só `ClientApp`.
- **Parte B, a API, o MCP e os samples (60.9 a 60.11).**

Cada tarefa **reproduz antes de corrigir** e tem o teste que a mostra falhando. Tarefa de painel roda o detector
(`design-quality.md`) nas telas afetadas, nos dois temas, no desktop e no `390x844`, com a contagem de avisos antes e depois.
**No celular, a escolha aberta é empilhar na vertical, não rolar na horizontal.**

## Ponto de parada

- Se um item exigir **desenho novo** (um componente que não existe, uma decisão de produto), a Executora para e pergunta.
- Se não achar a causa de uma corrida do formulário (60.3) depois de a medir, para e relata com o que mediu. Não inventa causa.
- O que a Executora **não** conseguir fechar no prazo vai à auditoria, que decide se vai para a `0.7.0` (D-232).

# Parte A: o painel

## 60.1 O celular

| Tela | Defeito |
|---|---|
| Comparar | As colunas B e Delta ficam escondidas |
| Insights | O p95 fica fora da tela |
| Histórico | O botão Comparar é cortado, e a rota é truncada |

## 60.2 Textos do painel

"Etapa mais lenta" aponta a **raiz** e não a folha. "Origem do erro" mostra a duração da **trace**, não a da etapa. "0% do
tempo (812 µs de 402,3 ms)" lê contraditório. "Status Success" sem tradução.

## 60.3 O Runner

Sem o status HTTP e sem o corpo da resposta. "Trace: <id>" não é link, e o `/runner` sozinho, depois de enviar, **navega para o
grafo ou tem o link** (vale para o produto, não só para a demo). Rótulos colados ("idcaminho", "bodycorpo"). POST sem exemplo de
corpo. Mensagem genérica no 500 (liga com a Tarefa 58.4). **A corrida do formulário:** o campo de rota preenchido poucas dezenas de
milissegundos depois de escolher o endpoint volta vazio, e o envio diz "Preencha o parâmetro de rota" (1 em 25 no `develop`, 3 em 25 num
branch, causa não determinada): medir, achar a causa e corrigir.

## 60.4 Rótulos

`aria-label="Request summary"` fixo em inglês em `flow-details-panel.component.html`, mesmo em português.

## 60.5 Rotas cortadas sem `title`

A rota do cabeçalho do Fluxo (`flow-workspace__route`) e a do painel de detalhes (`flow-details__route`) são cortadas pela
elipse sem tooltip. A Tarefa 51.10 cobre só o nó do grafo e o rótulo da cascata.

## 60.6 Pequenos de acessibilidade e de estado

No celular, o botão ▷ do cabeçalho do Fluxo, que abre o Runner, tem `aria-label` **vazio**. A lista "ETAPAS" não é clicável. O chip
"Ao vivo" fica aceso numa trace importada.

## 60.7 Arquitetura

Legenda com estados que não existem num mapa estático. N+1 sem contagem. O SVG exportado quebra fora do painel (`_ngcontent` sem CSS).

## 60.8 Histórico

A 3ª caixa marcada desmarca outra em silêncio. "Só lentas" aparece duplicado.

# Parte B: a API, o MCP e os samples

## 60.9 Sobras da API

"Expected at most 1 Database calls" (plural). **`Enabled = false` não desliga com outro listener:** reproduzir primeiro, e corrigir só se
for defeito do NetApiFlow.

## 60.10 O filtro de rota (B12)

O filtro de rota do MCP exige o **template exato**, e o exemplo da própria tool (`/api/orders/{id}`) devolve vazio para
`{id:int}`. O filtro passa a casar **ignorando a restrição**, pelo `RouteTemplateNormalizer`, **no REST, no MCP e no Histórico**
(o OpenAPI lista `/api/orders/{id}` e o histórico mostra `/api/orders/{id:int}`). Teste de host real nos três.

## 60.11 Os samples (B14)

O README do `SampleApi.Controllers`: o `dotnet run` sem `-f` falha por multi-target, cita `GET /api/orders` (que dá 404) e não diz como
subir o PostgreSQL. O `SampleApi.MinimalMediatR` **não tem README**. Os dois funcionam pelo caminho do usuário, com os nomes novos da
Fase 67, e cada comando do README foi **executado**.

## O que **não** está aqui, e onde está

| Item | Onde |
|---|---|
| `dbo.Users` vira "SELECT dbo" | Tarefa 58.10 (B11) |
| `Tag` só aceita `string`, `UseNetApiFlowDashboard` virar `Map*` | Fase 67 (B8) |
| O OTLP sai com spans órfãos | Fase 59 (documentar) |
| `DELETE` e `POST /deploys` sem papel só leitura | Fase 59 (documentar), roadmap `0.7.0` |
| O atributo `sql` do OTLP no lugar de `db.query.text` | Roadmap `0.7.0` |
| `NetApiFlow.Storage.SqlServer` traz o `SqlClient` 5.2.2, que não grava comandos, e a licença do MediatR 13 | Fase 59 (README) |

## Critério que vale para a fase inteira

- Parte A: `npm run lint`, `npm run format:check`, `npm run test:ci` e `npm run build` limpos. Parte B: build, testes e formato limpos nos
  dois TFMs.
- Cada item com teste **visto falhando antes**. Os do painel também no navegador, com captura de tela antes e depois.
- Zero achado de `low-contrast` e de `undersized-ui-text` no detector.
- **Pendência pequena dos scripts, sem PR própria:** o guarda `import.meta.url === pathToFileURL(process.argv[1]).href` quebra com `ERR_INVALID_ARG_TYPE` quando o módulo é importado por `node -e`, porque `process.argv[1]` não existe (reproduzido em `verify-built-demo.mjs` e em `verify-live-panel.mjs`). A mesma linha está em oito scripts de `scripts/`. Não afeta o CI. Se a fase tocar um deles, corrige nos oito de uma vez, com um teste do módulo importado sem `argv[1]`.

## Versionamento e fluxo

- `0.6.0`. Parte A: `bugfix/60a-acabamento-painel`. Parte B: `bugfix/60b-acabamento-api`. Ambas a partir de `develop` atualizado.
- Commits pequenos, em inglês, uma linha, sem linha de coautor. Abrir a PR e parar.

## Relatório final

Por tarefa: o teste, a saída dele falhando antes e passando depois, e a captura de tela dos itens do painel. A contagem do detector. O que
ficou de fora, com o motivo.
