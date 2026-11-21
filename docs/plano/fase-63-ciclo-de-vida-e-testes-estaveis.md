# Fase 63: o ciclo de vida dos steps e os testes estáveis (D-236, B1, B2, B3, B15)

Quatro defeitos pequenos e independentes, três de vazamento ou de estado errado no rastreamento (B1, B2, B3) e dois testes
que dependem de tempo (B15). Vão juntos porque são curtos, e porque o B15 tira o ruído do CI antes das fases grandes.

Vai no `0.6.0` (D-236). São defeitos antigos, sem regressão, então não são hotfix (D-205).

## Os achados e o que a Planejadora conferiu

| # | Achado | Conferi |
|---|---|---|
| B1 | O `FlowDbCommandInterceptor` não sobrescreve `CommandCanceled` nem `CommandCanceledAsync`: toda query cancelada deixa o step em `_steps` **para sempre** (vazamento num singleton), e o span fica `Running` | **Código:** o interceptor sobrescreve `*Executed` e `CommandFailed(Async)`, e **não** tem `CommandCanceled`. A auditoria confirmou `[conf]`. Não reproduzi o vazamento |
| B2 | `DecorateFlowTracked` cria o original com `ActivatorUtilities` dentro da fábrica, e o contêiner nunca o descarta: um `Foo : IDisposable` com escopo deixa de ser descartado | **A auditoria confirmou `[conf]`.** Não li o código. Lembrete: o `AddFlowTracked` já registra o original como serviço próprio, e é a forma de copiar |
| B3 | `FlowInterceptorProxy`: um `Task` cancelado volta como `faulted`, e não como `canceled`, e o span fecha como `Success` | Li que o proxy usa `ContinueWith` e trata `IsFaulted`. **Não verifiquei** o cancelamento |
| B15 | `CancellationClassificationIntegrationTests` (um `Delay` de 300 ms) e `DashboardSseTests` (dorme 3 s e afirma uma **ausência**) dependem de tempo | Não verifiquei |

## Ponto de parada

- Se o teste vermelho de um achado **passar** antes da correção, **para e relata**.
- Se o B2 não puder ser corrigido sem mudar o que o `DecorateFlowTracked` devolve ao usuário (o tipo, o tempo de vida), para.
- O B15 não pode virar um teste que **deixa de afirmar** o que afirmava: trocar o tempo por sincronização, nunca apagar a
  asserção.

## Tarefa 63.0: os testes vermelhos, antes de corrigir

Teste de host real, visto falhando antes, `net8.0` e `net10.0` onde couber:

1. **B1.** Uma query EF Core cancelada pelo `CancellationToken`: o step **sai** de `_steps` e o span **fecha** como `Error`, com `ErrorType` do tipo da exceção de cancelamento (D-242: o produto só classifica cancelamento no trace, não no span). Hoje: fica `Running` e o step vaza.
2. **B2.** Um serviço `Scoped` que implementa `IDisposable`, registrado com `DecorateFlowTracked`: ao fim do escopo, ele é
   **descartado**. Hoje: não é.
3. **B3.** Uma chamada pelo proxy a um método que devolve um `Task` cancelado: o `Task` devolvido ao chamador está
   **cancelado** (`await` lança `TaskCanceledException`), e o span fecha como `Error` com a exceção de cancelamento, não `Success` (D-242).

## Tarefa 63.1: as correções

- **B1:** sobrescrever `CommandCanceled` e `CommandCanceledAsync`, fechando o step por `MarkError` com a exceção de cancelamento, como todo cancelamento num step já fecha hoje, e removendo-o de `_steps`.
- **B2:** registrar o original no contêiner **com uma chave privada** e decorar sobre ele (D-242), o que o contêiner passa a descartar com o tempo de vida original. O registro sem chave do `AddFlowTracked` exporia o tipo concreto e colidiria com o registro do usuário. Consequência dita no XML doc e no CHANGELOG: o contêiner precisa suportar serviços com chave.
- **B3:** preservar o cancelamento (o `Task` devolvido fica `canceled`), e fechar o span por `MarkError` com a exceção de cancelamento.

## Tarefa 63.2: os testes estáveis (B15)

Trocar o `Delay` de 300 ms do `CancellationClassificationIntegrationTests` e o sono de 3 s do `DashboardSseTests` por
`TaskCompletionSource` (ou outra sincronização explícita). O de ausência afirma "nada chegou" **depois de um sinal**, não depois
de um tempo. Cada um roda 50 vezes seguidas em `net8.0` e `net10.0` sem falhar, e o resultado vai no relatório.

## Fora de escopo

- Qualquer mudança no que o `DecorateFlowTracked` ou o proxy devolvem além do que os três testes exigem.
- Outros testes instáveis por tempo. O que a Executora achar vai à auditoria.

## Critério que vale para a fase inteira

- Build, testes e formato limpos nos dois TFMs. `AotCoreVerification` com zero aviso.
- Cada achado de B1 a B3 com teste **visto falhando antes**. B15 com a rodada de 50 execuções.
- `CHANGELOG.md`, em inglês, `### Fixed`: o vazamento do step em query cancelada, o descarte do serviço decorado, o
  cancelamento preservado no proxy.

## Versionamento e fluxo

- `0.6.0`. Branch `bugfix/63-ciclo-de-vida` a partir de `develop` atualizado.
- Commits pequenos, em inglês, uma linha, sem linha de coautor. Abrir a PR e parar.

## Relatório final

Para cada achado: o teste, a saída dele falhando antes e passando depois. As 50 execuções dos dois testes do B15.
