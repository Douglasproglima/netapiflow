# Qualidade visual do dashboard (Impeccable)

Ferramenta de desenvolvimento (ADR-0014), nunca entra no `.nupkg` nem em `ClientApp/dist/`
(ADR-0005). Contexto do detector: `PRODUCT.md` e `DESIGN.md`, na raiz do repositório, derivados de
`docs/brand/tokens.md` (fonte canônica, ADR-0009).

## Quando rodar

Toda tarefa que toca `src/NetApiFlow.Dashboard/ClientApp` roda o detector nas telas afetadas
antes de a Executora dizer que terminou:

```
npx impeccable detect <url-ou-arquivo> --viewport 390x844
```

Nos dois temas (claro e escuro) e no viewport padrão de desktop, além do `390x844` acima. Rodar
contra as quatro telas (`/flow?trace=<id>`, `/history`, `/components`, `/runner`) quando a
mudança afeta algo compartilhado entre elas (tema, tipografia, espaçamento); só a tela afetada
quando a mudança é local.

## Detector contra um host vivo no Windows

Medido pela Executora em 20/11/2026, no Windows 10, com o `impeccable` da versão fixada no
`package.json`. É a receita que funcionou, não regra de produto.

- `npx impeccable detect --viewport 390x844 "http://localhost:<porta>/flow?trace=<id>"` contra o host
  vivo estoura com `Error: Navigation timeout of 30000 ms exceeded`, também sem `--viewport`, com
  `127.0.0.1` e em `/flow/history`. Contra a demo estática, `npx impeccable detect
  "http://127.0.0.1:<porta>/"` termina com exit 0, e o mesmo comando funciona no CI do ubuntu. A causa
  provável é o stream SSE, mas **isso não foi provado**: o que se mediu foi só "host vivo estoura,
  estático não".
- O que funciona é emular o tema e a largura por CDP e provar a página antes de rodar o detector. O
  procedimento abaixo foi medido pela Executora em 20/11/2026, em 32 execuções, todas com sonda. As flags de
  linha de comando para tema e largura **não** funcionam: `--blink-settings=preferredColorScheme=1` deixou
  `prefers-color-scheme: dark` em `false`, e `--window-size=390,844` deu `innerWidth` 496. Esta seção dizia o
  contrário até 20/11/2026, e estava errada.

  1. Subir o Edge sem janela, com porta de depuração e um perfil temporário de nome único, para poder matar
     só os processos dele: `msedge.exe --headless=new --remote-debugging-port=9333
     --user-data-dir=<pasta temporária única> --window-size=1280,800 --no-first-run --disable-extensions
     "http://localhost:<porta><rota>"`.
  2. Esperar `GET http://127.0.0.1:9333/json/version` responder. Depois `GET http://127.0.0.1:9333/json` e
     escolher a aba `type=page` cuja `url` contém `localhost:<porta><rota>`. A aba de boas-vindas do Edge vem
     antes e não serve.
  3. Abrir um WebSocket em `webSocketDebuggerUrl` dessa aba (o `WebSocket` global do Node 22 ou superior) e
     **mantê-lo aberto até o detector terminar**. A emulação vale só enquanto essa sessão existe.
  4. Pela sessão, enviar `Emulation.setEmulatedMedia` com
     `{ features: [{ name: "prefers-color-scheme", value: "dark" }] }` (ou `"light"`),
     `Emulation.setDeviceMetricsOverride` com `{ width: 390, height: 844, deviceScaleFactor: 1, mobile: false }`
     (ou 1280 e 800), `Page.enable` e `Page.reload`.
  5. **A sonda.** `Runtime.evaluate` com `returnByValue` em `JSON.stringify({ width: innerWidth, prefersDark:
     matchMedia("(prefers-color-scheme: dark)").matches, background:
     getComputedStyle(document.body).backgroundColor, nodes: document.querySelectorAll("*").length,
     appHtmlLength: document.querySelector("app-root")?.innerHTML.length ?? 0, textLength:
     document.body.innerText.length })`. Repetir de 500 em 500 ms até `appHtmlLength` passar de 1000, esperar
     mais 1,5 s e ler de novo. Se não passar de 1000, a execução é **inválida**: a página está vazia e um zero
     do detector não vale. A prova de cada execução é `width` igual ao pedido, `prefersDark` igual ao tema
     pedido e `appHtmlLength` grande (de 50 mil a 70 mil nas telas reais).
  6. Com a sessão ainda aberta, em `src/NetApiFlow.Dashboard/ClientApp`: `npx impeccable detect --json --cdp
     9333 --target "localhost:<porta><rota>"`. Saída `[]` e exit 0 são zero achados. A chave de cada achado
     no JSON é `antipattern` (por exemplo, `low-contrast`).
  7. Fechar o WebSocket e matar o Edge **pelo perfil, nunca pelo nome da imagem**, porque esse seria o Edge
     do dono. Em PowerShell, `Get-CimInstance Win32_Process -Filter "Name='msedge.exe'"` onde `CommandLine`
     é `-like '*<nome único do perfil>*'`, com `Stop-Process` num laço, porque o Edge recria os filhos, até a
     contagem zerar. Um Edge antigo preso na `9333` faz o detector varrer a página errada: conferir que a
     porta está livre antes de cada execução.
- **Saída vazia só vale como "zero achados" com a sonda do passo 5.** Uma matriz feita sem ela deu zeros
  vazios que não significavam nada.
- **Um script pronto (D-288).** `tests/manual-verification/Fase68AcessibilidadeVerification/detect-matrix.mjs` faz os passos acima nas cinco telas (Fluxo com uma trace, Fluxo com a gaveta aberta, Histórico, Componentes e Runner), em dois temas e dois tamanhos (`1440x900` e `390x844`), com a sonda de cada execução. Recebe a pasta do `ClientApp`, um rótulo e uma pasta temporária, e espera a demo estática servida em `localhost:4871` pelo `serve-spa.py` da mesma pasta (o README dela diz como). É um ponto de partida medido pela Executora, não uma regra de produto.
- **No Git Bash, um argumento que começa com `/` é reescrito** (`/history` vira
  `C:/Program Files/Git/history`). Rodar com `MSYS_NO_PATHCONV=1`.
- **O painel redireciona `/flow?trace=` para `/flow/?trace=`.** O `--target` do detector precisa casar com a URL
  **final**, senão ele sai com exit 1 e uma saída vazia, que parece "zero achados". Abrir a URL final e provar a
  aba (o passo 5) antes de varrer.

## Conferir qual build o navegador está mostrando

Servir `dist/demo/browser` com `python -I -m http.server <porta> --bind 127.0.0.1` não manda
`Cache-Control`, e o navegador pode continuar mostrando o build antigo depois de reconstruir. Medido: o
navegador listou `main-Z6W4SS2D.js` enquanto `ls dist/demo/browser` e o `curl` do `index.html` davam
`main-NGI2J5HA.js`. Antes de olhar qualquer captura, comparar, no navegador,
`[...document.scripts].map((s) => s.src.split('/').pop()).filter(Boolean)` com
`ls dist/demo/browser | grep '^main-'`. Se diferirem, abrir por outra origem (`localhost` no lugar de
`127.0.0.1`) e conferir de novo.

## O guia manda, o detector verifica (D-76)

`docs/brand/tokens.md` e `docs/design/*/guide.md` são a fonte de verdade de cor, tipografia e
espaçamento (ADR-0009). O detector confere se a implementação bate com essa fonte, não o
contrário. Quando um achado do detector e o guia parecem discordar, a correção é sempre alinhar o
código ao guia, nunca inventar um valor novo só para calar o aviso, e nunca mudar o guia para
agradar o detector. Prova real disso (D-74): um aviso de hierarquia tipográfica parecia pedir um
`h1` maior, mas a causa real era a implementação ter divergido de uma escala que o guia já
definia.

## Regra de bloqueio (D-76: só o que é objetivo e mensurável)

Só duas categorias de achado bloqueiam a tarefa: `low-contrast` (razão de contraste abaixo de
4,5:1, número calculável) e `undersized-ui-text` (tamanho abaixo de 11px, número comparável).
Essas duas não dependem de gosto, só de medir.

Todo o resto (`ai-color-palette`, `overused-font`, `flat-type-hierarchy`, `nested-cards` e
qualquer regra nova de estética) é **aviso, não bloqueio**: aparece no resumo de fim de fase, mas
não impede a tarefa de ser dada como concluída. Decisão explícita (D-76) depois de rodar a Fase
11 sem bloqueio: ver se o sinal desses achados vale a pena promover a bloqueio numa fase futura,
ou se fica aviso para sempre.

Exceção consciente (ADR-0014: `ai-color-palette` para o roxo da marca, `overused-font` para a
Inter) só muda o *texto* do aviso, não a categoria: já eram aviso, continuam aviso, só documentado
como decisão tomada, não pendência. Exceção nova não registrada em ADR não é motivo para rodar
`impeccable ignores`: primeiro avisa a Planejadora, que decide se vira ADR ou se o código precisa
corrigir o achado de verdade.

Preferir `impeccable ignores add-value` (ignora um valor específico, ex. uma cor da marca) a
`impeccable ignores add-rule` (ignora a regra inteira) sempre que o valor específico resolver o
caso. `add-rule` só quando a regra inteira não se aplica a este projeto, decisão que também passa
pela Planejadora antes de virar exceção permanente.

## Versão fixa, nunca `npx impeccable` solto

A ferramenta recebe commit quase todo dia; uma regra nova pode quebrar o CI sem nenhuma mudança
no código do projeto. `package.json` (devDependency) e o workflow de CI fixam uma versão exata,
igual a qualquer outra dependência. Atualizar é decisão deliberada (ler o changelog, não um
`npx` que sempre pega a última), mesma régua de `.claude/rules/dependencies.md`.

## `npx impeccable install` é passo de setup local, não versionado

`.claude/skills/impeccable/` (gerado por `npx impeccable install`) é código de terceiro
genérico, sem nada específico do NetApiFlow, e reproduzível a qualquer momento pelo próprio
comando. Mesmo tratamento de `node_modules/`: fica no `.gitignore`, nunca commitado. Cada dev
roda `npx impeccable install` uma vez, localmente, ao clonar o repositório ou trocar de máquina.
Se o comando for atualizado numa versão nova do pacote (`package.json` fixa a versão, ver acima),
rodar `install` de novo é parte do passo de atualização.

## `DESIGN.md` é gerado, não mantido à mão

Para não existir duas referências de marca (`docs/brand/tokens.md` e `DESIGN.md`) que podem
divergir em silêncio, `DESIGN.md` é gerado a partir de `docs/brand/tokens.md` por um script
(tarefa da Fase 11). Editar `DESIGN.md` manualmente é sinal de que o script não rodou ou está
desatualizado, não uma correção válida.

## Resumo de fim de fase

Toda fase/tarefa que toca o dashboard traz, no resumo de fim de fase, a contagem de avisos do
detector antes e depois da mudança (ex.: "12 avisos antes, 0 depois" ou "0 antes, 0 depois, sem
regressão"). Não é só para provar que não piorou: é para a Planejadora saber se algum achado novo
apareceu e precisa virar decisão.
