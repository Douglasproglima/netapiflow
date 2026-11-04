# ADR-0003: Multi-targeting `net8.0` e `net10.0` em todos os pacotes

## Status
Aceito

## Issue
[#63](https://github.com/Douglasproglima/netapiflow/issues/63)

## Contexto
`netapiflow-prompt.md` (seção 4.1) exige `net8.0` e `net10.0`, as duas versões LTS do .NET no
momento. `net9.0` (STS) fica de fora de propósito. A diferença mais visível entre os dois alvos é
a API de Server-Sent Events: `net10.0` tem `TypedResults.ServerSentEvents` nativo; `net8.0` não
tem e precisa de implementação manual escrevendo `text/event-stream` (seção 4.1, confirmado no
toolchain local: SDK 10.0.402 e 8.0.426, ref packs ASP.NET Core 8.0.32 e 10.0.13 presentes).

## Decisão
Todo `.csproj` de produto usa `<TargetFrameworks>net8.0;net10.0</TargetFrameworks>`. Divergência
de API entre os dois alvos é isolada com `#if NET10_0_OR_GREATER`, concentrada no menor número de
arquivos possível (o caso principal é o endpoint de stream SSE, ver `docs/plano/riscos.md`,
risco R-04). Os projetos de teste também multi-targetam (decisão D-11 em
`docs/plano/decisoes.md`), para que "funciona nos dois TFMs" seja uma afirmação verificada, não
assumida.

## Consequências
- Usuário em .NET 9 (STS) resolve o pacote pelo alvo `net8.0`, inclusive o caminho manual de SSE.
  Isso é uma limitação conhecida, documentada no README, não um bug.
- CI precisa rodar a suíte de testes duas vezes (uma por TFM) nos projetos que compilam para os
  dois, o que aumenta o tempo de pipeline. Aceito como custo necessário.
- Quando `net8.0` sair de suporte, trocar por `net12.0` (próxima LTS) é uma mudança de
  `Directory.Build.props` e deste ADR, não uma reescrita de código.
