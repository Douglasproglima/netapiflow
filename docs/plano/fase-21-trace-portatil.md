# Fase 21: Trace portátil (export/import)

Projeto tocado: só `src/NetApiFlow.Dashboard/ClientApp`. Nenhuma mudança em `src/NetApiFlow`,
`NetApiFlow.EntityFrameworkCore`, `ITraceStore` ou qualquer `.csproj`. Nenhuma mudança em
`docs/plano/contratos.md`: a fase inteira reaproveita o contrato que já existe.

Item 2 de `docs/plano/roadmap-valor.md` (D-60 item C), escolhido pelo usuário para retomar o
roadmap de valor depois de todos os riscos técnicos abertos mitigados (D-97). Dor documentada em
D-60: "não reproduzo aqui" e "IA sem acesso ao runtime". Um dev vê um trace de erro na própria
máquina, exporta o `.json`, manda pra outro dev (ou cola numa conversa com IA); quem recebe
importa e enxerga exatamente a mesma waterfall, sem precisar da API nem do banco de quem gerou o
trace original.

Pesquisa da Planejadora confirmou que a forma pública necessária já existe inteira:
`ApiService.getTrace(traceId)` já busca `GET /traces/{traceId}`, que já devolve exatamente
`FlowTraceDetail` (`{ Trace, Spans }`, D-19 em `docs/plano/contratos.md`).
`LiveTracePlayerService.trace` já expõe um `Signal<FlowTraceDetail | null>` que toda a tela de
`/flow` (grafo, waterfall, cartões de métrica, feed de evento) já consome sem saber de onde o
valor veio. **Exportar é serializar o valor atual desse signal. Importar é popular o mesmo tipo de
signal a partir de um arquivo escolhido pelo usuário, sem nenhuma chamada HTTP em nenhum dos dois
sentidos.**

## Tarefa 21.1: exportar

Botão "Baixar trace (.json)" na tela de `/flow` quando há um trace carregado (`trace()` não nulo).
Serializa o valor atual do signal (o mesmo `FlowTraceDetail` já em memória, não uma nova busca) e
aciona o download no navegador (`Blob` + `<a download>`, sem biblioteca nova:
`.claude/rules/dependencies.md` não permite pacote novo pra isso). Nome de arquivo sugerido inclui
o `traceId`, para quem recebe o arquivo conseguir relacionar com uma conversa ("o trace que
mandei pra você é esse").

**Pronto quando:** com um trace carregado em `/flow?trace=<id>`, o botão baixa um `.json` cujo
conteúdo, reimportado (tarefa 21.2), reproduz a mesma waterfall, visualmente idêntica.

## Tarefa 21.2: importar

Ação (botão ou item de menu, local exato é decisão da Executora) que abre um seletor de arquivo
nativo (`<input type="file" accept=".json">`), lê o `.json` escolhido, valida que o formato bate
com `FlowTraceDetail` antes de tentar renderizar (JSON malformado ou de outro formato mostra uma
mensagem de erro clara, nunca quebra a tela — mesmo princípio de "nunca travar" que vale pro
núcleo .NET, agora do lado do frontend).

Um trace importado precisa renderizar através dos **mesmos componentes** que já desenham um trace
ao vivo ou em replay (grafo, waterfall, cartões, feed de evento): nenhum componente novo de
visualização, só uma fonte nova de dado alimentando o signal que eles já leem. Qual peça nova
guarda esse estado (serviço irmão de `LiveTracePlayerService`, ou um sinal adicional que
`flow-page.component.ts` combina com o de hoje) é decisão de implementação da Executora, não
forma pública travada por este plano.

Trace importado ganha um selo visível na tela ("Trace importado"), permanente enquanto ele está
sendo exibido, substituindo qualquer indicação de "ao vivo"/"replay" que não se aplica a um dado
estático sem conexão com SSE. Precisa existir uma ação clara pra sair do modo importado e voltar
pra lista de traces ao vivo da própria instância.

**Pronto quando:** o `.json` exportado na tarefa 21.1, reimportado numa aba/sessão nova do
dashboard, mostra a mesma waterfall, com o selo de importado visível. Um arquivo `.json` qualquer
(não um trace exportado) mostra erro, não tela em branco nem exceção no console.

## Fora de escopo, de propósito

- **Nenhuma persistência do trace importado.** Não entra em `ITraceStore`, não aparece em
  `/history`, `/insights`, `/metrics` nem `/architecture`: é só uma visualização temporária na
  aba atual, igual uma imagem aberta localmente. Importar um trace de outra pessoa não pode
  contaminar as próprias estatísticas.
- Nenhuma validação de assinatura/origem do arquivo além da forma dos dados: o `.json` não
  carrega segredo nenhum (mesma garantia de sempre, `.claude/rules/tracing.md`), então não há
  superfície de ataque nova a mitigar além de JSON malformado travando o parser.
- Múltiplos traces no mesmo arquivo, ou importar um trace parcialmente (só alguns spans): fora de
  escopo, YAGNI até aparecer um pedido real.

## Aceite da fase

Critérios de pronto das duas tarefas acima. `npm run lint`, `npm run format:check`,
`npm run test:ci`, `npm run build` limpos. Detector Impeccable (`.claude/rules/design-quality.md`)
rodado em `/flow` (tema claro/escuro, desktop e `390x844`): contagem de avisos antes/depois no
resumo de fim de fase.

## Ao terminar

Resumo pra Planejadora: o que foi feito, como verificar (passo a passo manual: exportar um trace
real do sample, importar em outra aba, comparar visualmente), o que ficou pendente. A Planejadora
roda `/revisar-fase 21` antes de decidir o próximo item do roadmap.
