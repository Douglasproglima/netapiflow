# Fase 12: Tempo não explicado no waterfall

Projeto: `src/NetApiFlow.Dashboard/ClientApp`. Nenhuma mudança no núcleo .NET nem em
`docs/plano/contratos.md`: todo dado necessário (`startedAt`, `endedAt`, `durationMicros` por
span, `durationMicros` do trace) já trafega na API, em `FlowTraceDetail`.

Pré-requisito: nenhum funcional. Item B do roadmap D-60 (D-78), liberado por D-70 sem esperar as
fases anteriores "em produção".

## Contexto e a conta certa (ler antes de implementar)

D-60 item B pediu só a versão aritmética: soma das etapas registradas vs. duração total do
trace, virando um selo "X% não explicado". **Soma ingênua de `durationMicros` de cada span dá
conta errada**, por dois motivos que já existem de verdade neste projeto:

1. **Span aninhado**: o tempo do filho já está contido no tempo do pai. Somar os dois conta o
   mesmo intervalo de tempo duas vezes.
2. **Branches paralelas** (`Task.WhenAll`, já suportado e testado desde a Fase 1): duas etapas
   irmãs podem se sobrepor no relógio de parede. Somar as duas também conta tempo em dobro.

A conta certa é **união de intervalos**: juntar todos os intervalos `[startedAt, endedAt)` de
spans reais (status diferente de `Running`, com os dois carimbos presentes), mesclar os que se
sobrepõem ou se tocam, somar o comprimento dos intervalos já mesclados. Isso cobre os dois casos
acima corretamente, sem precisar saber de antemão se um span é filho ou irmão de outro.

```
não-explicado = max(0, duração-total-do-trace − soma-dos-intervalos-mesclados)
```

A duração total vem de `trace.durationMicros` (autoritativa), nunca derivada de timestamp de
span. `max(0, ...)` é defesa contra imprecisão de relógio, não deveria disparar na prática.

## Fora de escopo, de propósito (D-60)

Só aritmética sobre os carimbos já registrados. Nenhuma métrica de infraestrutura (fila do
`ThreadPool`, GC, CPU): isso cruzaria o não-objetivo "no infrastructure metrics" do README. Se o
usuário real pedir isso depois, é uma fase nova e uma conversa nova sobre esse não-objetivo, não
uma extensão silenciosa desta.

## Tarefas

### 12.1: Função pura de cálculo

Nova função em `details/waterfall.ts` (mesmo arquivo de `buildWaterfall`, mesmo padrão de função
pura sem Angular/HTTP/DOM), ex. `computeUnexplainedTime(trace: FlowTraceDetail): UnexplainedTime
| null`. Devolve `null` quando o trace ainda está `Running` (dado parcial enganaria) ou não tem
nenhum span. `UnexplainedTime` tem pelo menos `unexplainedMicros` e `unexplainedPercent`.

**Pronto quando:** teste unitário cobre: span único sem sobreposição (resultado óbvio); span pai
com filho totalmente contido (não-explicado não conta o filho duas vezes); duas etapas irmãs
sobrepostas no tempo, tipo `Task.WhenAll` (não-explicado não conta a sobreposição duas vezes);
trace `Running` (devolve `null`); trace sem span nenhum (devolve `null` ou 100%, decisão do
teste, documentada no comentário do teste).

### 12.2: Selo no painel de detalhes

`flow-details-panel.component` (ou onde o waterfall já é renderizado) mostra o selo quando
`computeUnexplainedTime` não devolve `null`: texto com o percentual e os dois valores absolutos
(ex. "38% do tempo (192 ms de 500 ms) não está coberto por nenhuma etapa registrada"), i18n
pt-BR/en. Nunca aparece para trace ainda em execução.

**Pronto quando:** teste do componente confirma que o selo aparece com o texto certo para um
trace completo, e que não aparece para um trace `Running`.

### 12.3: Documentação

README e README.pt-BR.md ganham uma linha curta sobre o selo, na mesma seção onde o detector de
N+1 já foi documentado (Fase 9).

**Pronto quando:** README e README.pt-BR.md atualizados, conteúdo equivalente.

## Aceite da fase

Todos os critérios de pronto acima. `npm run lint`, `format:check`, `test:ci` e `build` limpos.
Nenhuma mudança em `src/NetApiFlow` (núcleo), `docs/plano/contratos.md` ou qualquer pacote .NET.

## Ao terminar

Resumo para a Planejadora: o que foi feito, como verificar (descrição ou print do selo com um
trace real que tenha `Task.WhenAll`, para provar que a união de intervalos não superestima o
não-explicado), o que ficou pendente. A Planejadora roda `/revisar-fase 12` antes de decidir a
próxima fatia do roadmap.
