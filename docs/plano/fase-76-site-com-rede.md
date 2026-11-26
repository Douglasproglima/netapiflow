# Fase 76: o site com uma rede (D-320, grupo F da `0.7.0`)

**Estado (26/11/2026):** planejada, não iniciada. Corre **em paralelo** às Fases 72 a 75 e não espera nenhuma delas: o
trabalho é no repositório do site (`netapiflow-site`), que é outro (D-320).

O quick start do site já quebrou uma vez (Fase 62) e foi refeito à mão na `0.6.0` (Tarefa 62.4, `netapiflow-site#2`).
Nada o confere sozinho: o README do repositório principal tem um job que o cola e compila num app novo (Fase 70), e o
site não tem nenhum. Esta fase dá ao site o mesmo.

## O achado e o que a Planejadora conferiu

| # | Item do roadmap | Conferi |
|---|---|---|
| 50 | O quick start do site (repositório `netapiflow-site`) não tem rede nenhuma, e já quebrou antes. Dar ao site um passo que cola e compila o quick start dele num app novo, como o job do pacote real da Fase 70 faz com o README | Li o item e a D-319. A prova da 62.4 foi à mão, pela Executora, em `net10.0` e `net8.0`. **Não li o repositório do site** |

## O que a divulgação deixa de fora

Os itens 1 (divulgar o `NetApiFlow.Testing` como coisa separada), 20 (a seção "Why not OpenTelemetry?") e 21 (o
`NetApiFlow.Testing` como porta de entrada) **não estão nesta fase**. O dono ainda não decidiu canal e data da divulgação
(a pergunta 2 do `versao-0.7.0.md` segue aberta, D-320), e isso é decisão de produto, não de código. Quando ele responder,
a Planejadora escreve o que for preciso.

## Ponto de parada

- O repositório do site tem o padrão próprio de branch e de commit. A Executora o segue, **empurra a branch, abre a PR e
  para**. A auditoria audita, faz o merge e publica (D-198).
- A prova do passo vem do **pacote do nuget.org**, nunca de uma referência de projeto nem de um feed local.
- Se o passo precisar de um segredo, de uma permissão nova do repositório ou de uma ação de terceiro sem versão fixa, a
  Executora para e relata.

## Tarefa 76.0: ver como o quick start do site está escrito hoje

As duas páginas (inglês e português): onde mora o bloco do quick start, em que formato (HTML, Markdown, um componente), e a
nota do `net8.0`. A Executora diz se dá para **extrair** o código do bloco sem ele estar duplicado em outro lugar. Se o
código estiver só na página, o passo lê a página.

## Tarefa 76.1: o passo que cola e compila

1. Um workflow no repositório do site que, para cada idioma, **extrai o código do quick start da página**, cria um
   `dotnet new webapi` novo, instala os pacotes do **nuget.org** na versão que a própria página declara, cola os blocos
   **na ordem em que o leitor os cola** (a lição da #409) e compila.
2. `net10.0` e `net8.0`. A nota do `net8.0` da página é o que o passo segue para esse TFM.
3. Roda em toda PR do site, **e em agenda** (uma vez por semana): o nuget.org pode publicar uma versão que quebre o quick start
   sem nenhuma PR do site.
4. O passo falha com uma mensagem que diz qual idioma, qual TFM e o erro do compilador.
5. **Provado vermelho:** uma sonda com o quick start quebrado de propósito (um nome antigo, como
   `UseNetApiFlowDashboard`) deixa o passo vermelho, e depois verde com o texto certo.

## Fora de escopo

- Redesenho, textos novos de marketing, novas páginas, e a divulgação (itens 1, 20 e 21).
- A demo do site (refeita da tag no release, como na 62.4).
- Qualquer mudança no repositório `netapiflow`.

## Critério que vale para a fase inteira

- O passo roda sozinho nas duas páginas, nos dois TFMs, com o pacote do nuget.org.
- O vermelho da sonda e o verde visto no CI.
- `grep -rn "—"` sem ocorrência nos textos em português e nos READMEs do site (o critério da Fase 62).

## Versionamento e fluxo

- Repositório `netapiflow-site`, branch e commits no padrão dele. A Executora empurra a branch, abre a PR e para. A
  auditoria audita, faz o merge e publica.

## Relatório final

Onde o código do quick start mora e como o passo o extrai. A saída do passo verde e do vermelho da sonda, nos dois TFMs. A
agenda escolhida.
