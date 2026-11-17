# Fase 35: hotfix 0.4.2, `NetApiFlow.MediatR` quebra em runtime com `MediatR` 12.5+

Achado de uma sexta rodada de revisão externa, escalado de "corrigir antes de um bump futuro"
(roadmap, D-139) pra bug em produção agora. Reproduzido do zero nesta sessão contra o pacote
`NetApiFlow.MediatR` 0.4.1 publicado de verdade (nuget.org, hash conferido), não aceito só pelo
relato: qualquer host que resolva `MediatR` 12.5.0 ou mais novo (o `.nuspec` publicado não tem
limite superior) estoura `System.MissingMethodException` na primeira requisição que passa pelo
pipeline, dentro de `FlowTrackingBehavior<,>.Handle`. Detalhe completo, causa raiz e fonte da
licença em D-140.

Duas coisas na mesma correção: o binário quebrado (`MediatR` 12.5+ mudou a assinatura do delegate
de pipeline behavior, mudança compatível em código-fonte mas binária-breaking) e o teto de versão
que o pacote pode adotar daqui em diante (`MediatR` 13.0.0 virou dual-licensed, RPL-1.5 mais
licença comercial, fora da régua de `.claude/rules/dependencies.md`, mesmo motivo do ADR-0008
pra FluentAssertions v8 e Moq). `12.5.0` é a última versão Apache-2.0, por isso é o alvo, não
`12.4.1` mantido como está nem qualquer versão 13+.

Sem mudança de API pública. PATCH, cortado de `main`, sem esperar o ciclo normal de `develop`.

## Regra geral desta fase

A Tarefa 35.1 escreve primeiro o teste de regressão contra o pacote empacotado de verdade,
confirma que ele falha exatamente como o achado descreve, só depois aplica a correção.

## Tarefa 35.1: subir `MediatR` pra `12.5.0` e propagar `cancellationToken` no pipeline behavior

`Directory.Packages.props` fixa `MediatR` em `12.4.1`. `NetApiFlow.MediatR/FlowTrackingBehavior.cs`
chama `next()` sem argumento. A partir da 12.5.0, a assinatura do delegate
`RequestHandlerDelegate<TResponse>` ganhou um parâmetro opcional de `CancellationToken`; é
compatível em código-fonte (compila contra qualquer uma das duas), mas o binário compilado contra
a assinatura antiga não encontra o método novo em runtime, estourando
``System.MissingMethodException: Method not found: 'System.Threading.Tasks.Task`1<System.__Canon>
MediatR.RequestHandlerDelegate`1.Invoke()'`` assim que um handler roda pela primeira vez. Causa
documentada na issue 1104 do repositório oficial do `MediatR`, commit `cca7861`.

Dois ajustes:

1. `Directory.Packages.props`: `MediatR` sobe de `12.4.1` pra `12.5.0` (última versão Apache-2.0;
   13.0.0 em diante é dual-licensed, fora de cogitação, D-140).
2. `FlowTrackingBehavior.cs`: a chamada ao delegate `next` passa a propagar `cancellationToken`
   explicitamente, eliminando a dependência da assinatura antiga e, de brinde, corrigindo o
   `CA2016` que já tinha derrubado o build de uma tentativa anterior de subir o `MediatR` (D-139).

Teste de regressão novo em `tests/manual-verification/`, irmão do `DashboardPackageVerification`:
um host mínimo instala o `.nupkg` empacotado de `NetApiFlow.MediatR` (não `ProjectReference`,
que nunca pegaria incompatibilidade binária) junto com `MediatR` 12.5.0 explícito, registra um
handler real de `IRequest<string>` com `AddNetApiFlowMediatR()` no pipeline, e confirma que a
chamada via `IMediator.Send(...)` retorna o valor esperado sem exceção. Esse é o mesmo repro desta
sessão, só formalizado como projeto versionado.

**Pronto quando:** o teste de regressão novo falha contra o estado atual do repositório (antes da
correção, reproduzindo o `MissingMethodException`) e passa depois da correção, instalando o
`.nupkg` recém-empacotado, não via `ProjectReference`. `dotnet build NetApiFlow.slnx -c Release`
limpo nos dois TFMs, suíte completa de `NetApiFlow.MediatR.Tests` (se existir) ou os testes atuais
do pacote continuam passando.

## Fora de escopo

- Subir `MediatR` além da `12.5.0`: a `13.0.0` é dual-licensed, decisão fora do alcance de um
  PATCH, precisa de ADR próprio se algum dia fizer sentido reconsiderar (D-140).
- Travar um teto de versão no `.nuspec` (`MediatR` `[12.5.0,13.0.0)`, por exemplo): ideia
  discutida (opção A da revisão externa), descartada em favor de corrigir o binário de verdade
  (opção B), porque trava a porta sem resolver o problema pra quem já tem `12.5.0`+ instalado por
  outro motivo. Se uma rodada futura achar necessário reforçar isso também, é decisão da
  Planejadora, registrada à parte.
- Qualquer outro pacote opcional (`NetApiFlow.EntityFrameworkCore`, storages, OTel, MCP): o achado
  é específico de `NetApiFlow.MediatR`, confirmado que não afeta os outros seis pacotes nem o
  núcleo.

## Aceite da fase

`dotnet build NetApiFlow.slnx -c Release` e `dotnet format NetApiFlow.slnx --verify-no-changes`
limpos nos dois TFMs. Suíte completa, incluindo o teste de regressão novo contra o `.nupkg`
empacotado. `dotnet pack NetApiFlow.slnx -c Release` conferido pacote por pacote, com a versão
nova do `MediatR` declarada no `.nuspec` do `NetApiFlow.MediatR`.

## Ao terminar

Resumo pra Planejadora: o que foi feito, como verificar (comandos exatos), o que ficou pendente.
Antes de considerar o hotfix pronto pra lançar, a Planejadora reinstala o `0.4.2` publicado do
zero, reproduzindo o cenário exato desta fase (host novo, `MediatR` 12.5.0 explícito, handler
real), não substituível por revisão de código.
