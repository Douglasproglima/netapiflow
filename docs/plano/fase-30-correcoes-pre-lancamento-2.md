# Fase 30: Correções pré-lançamento, segunda rodada

Achados reais de uma segunda rodada de dogfooding (D-114), desta vez testando rastreamento
automático de Service/Repository (`AddFlowTracked`/`DecorateFlowTracked`) no mesmo projeto de
produção alheio da Fase 29 (`rajamineiothub`), depois da correção da Fase 29 já estar instalada.

A tarefa 30.1 é bloqueante de divulgação pública: é uma regressão real da Fase 29, não uma lacuna
de documentação nova. As demais podem acompanhar a mesma fase, mas não bloqueiam sozinhas.

## Tarefa 30.1: grafo invisível com volume real de rotas (R-27, regressão da Fase 29)

O painel "Testar um endpoint", em modo acoplado na grade do workspace (`>= 1600px` de largura,
ver `docs/design/flow-workspace/guide.md` seção 6.3), não limita a altura da lista de rotas. Com
uma API de volume real (248 rotas no projeto de teste), a lista mede milhares de pixels, estica a
linha inteira da grade (`.flow-workspace__body`), e o grafo, que divide essa mesma linha, fica
centralizado bem abaixo da dobra da página, sem nenhum mecanismo de rolagem que leve até lá.

Causa confirmada pela Planejadora: nenhum ancestral na cadeia de flexbox do workspace
(`.flow-workspace` → `.flow-workspace__body` → `.flow-workspace__action`, este último também o
`:host` de `EndpointFormComponent`) fixa uma altura absoluta. Todos usam `min-height`/
`height: 100%`, que não funcionam como teto de verdade sem um ancestral com altura fixa pra
calcular a porcentagem. O modo *drawer* (abaixo de 1600px) não sofre disso porque usa
`position: fixed` com `top`/`bottom`, que não depende dessa cadeia pra ter altura definida.

Isso é regressão direta da Fase 29: antes dela, o painel nunca carregava o documento OpenAPI de
verdade (`OpenApiDocumentPath` não existia), então a lista de rotas nunca tinha conteúdo
suficiente pra expor o problema. Os samples do repositório, com poucas rotas, também nunca
reproduzem: só aparece com volume real.

**Correção**: dar um limite de altura real à lista do painel quando acoplado (`max-height` com
`overflow-y: auto`, mesmo efeito que o modo *drawer* já tem por natureza da posição fixa), ou
resolver a cadeia de altura do workspace inteiro de forma que `height: 100%`/`min-height: 0` se
propaguem com um teto de verdade. Decisão de qual caminho tomar é livre pra quem implementar,
desde que o critério de pronto abaixo passe.

**Pronto quando**: com um documento OpenAPI de volume real (pelo menos uma centena de rotas)
carregado no painel, numa largura de tela de 1600px ou mais, o grafo permanece inteiramente
dentro da área visível do canvas, sem precisar rolar a página pra enxergá-lo. Testado contra o
cenário real que reproduzia o problema (lista completa, sem filtro), não só contra uma lista
filtrada/curta.

## Tarefa 30.2: `using NetApiFlow.Marking` nunca aparece no README

`AddFlowTracked`/`DecorateFlowTracked` vivem em `namespace NetApiFlow.Marking`
(`src/NetApiFlow/Marking/FlowTrackedServiceCollectionExtensions.cs`). O exemplo de
`AddFlowTracked` na seção "Marking forms" do README (os dois idiomas) não tem nenhum `using` pra
esse namespace. O sample real (`samples/SampleApi.Controllers/Program.cs`) precisa dessa linha;
só a prosa do README que nunca a mostrou. Mesmo padrão do achado original da Fase 29 (tarefa
29.2), só numa seção diferente.

**Correção**: acrescentar `using NetApiFlow.Marking;` ao bloco de código da seção "Marking forms",
nos dois READMEs.

**Pronto quando**: o bloco de código da seção "Marking forms", copiado isoladamente, compila sem
erro de método não encontrado.

## Tarefa 30.3: `DecorateFlowTracked` sem exemplo de código, predicado não documentado

`DecorateFlowTracked` tem linha na tabela de formas de marcação, mas nenhum exemplo de código,
diferente de `AddFlowTracked`. O predicado recebe o **tipo de implementação**, não a interface
(`predicate(entry.implementationType)`, confirmado no código), informação que só está no XML doc
do método, nunca no README. É a forma que escala pra um projeto real (decorou 10 serviços e 11
repositórios com duas linhas, por convenção de nome, no teste real), e hoje é a que tem menos
documentação das duas.

**Correção**: acrescentar um exemplo de código pra `DecorateFlowTracked` logo depois do exemplo de
`AddFlowTracked` já existente, usando convenção de nome (`type.Name.EndsWith("Repository")`, ou
similar) como caso de uso, e deixando explícito no texto que o predicado recebe o tipo de
implementação concreta, não a interface.

**Pronto quando**: o README mostra `DecorateFlowTracked` com um exemplo de código real, e o texto
ao redor deixa claro que o predicado recebe o tipo concreto registrado no DI, não a interface.

## Fora de escopo, de propósito

- Cobertura de Dapper sobre SQL Server via `ExternalActivitySource`: achado real (achado 4 de
  D-114), mas é escopo novo (uma fonte de ingestão que não existe hoje), não correção. Registrado
  em `docs/plano/roadmap-valor.md`, sem fase atribuída.

## Aceite da fase

Tarefa 30.1 com prova contra o cenário real (volume alto de rotas, sem filtro), não um caso fácil.
Tarefas 30.2 e 30.3 com os critérios de pronto acima. Build/test/format dos dois lados, Impeccable
rodado no painel de ação (`/runner` e o painel embutido em `/flow`), tema claro e escuro, viewport
desktop acima de 1600px (o que reproduz o problema) além do padrão `390x844`.

## Ao terminar

Resumo pra Planejadora: confirmação de que a correção da 30.1 foi testada contra volume real de
rotas (não só uma lista curta), o que ficou pendente. A Planejadora roda `/revisar-fase 30` antes
de considerar o item fechado.
