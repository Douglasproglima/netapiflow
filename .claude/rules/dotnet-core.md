---
paths:
  - "src/**/*.cs"
---

# Núcleo e pacotes .NET

## Camadas e dependência

| Projeto | Pode depender de | Nunca depende de |
|---|---|---|
| `NetApiFlow` | `Microsoft.AspNetCore.App` (FrameworkReference) | EF Core, MediatR, driver de banco, SDK do OpenTelemetry |
| `NetApiFlow.Dashboard` | `NetApiFlow` | EF Core, MediatR, driver de banco |
| `NetApiFlow.EntityFrameworkCore` | `NetApiFlow`, EF Core | MediatR, driver de banco direto |
| `NetApiFlow.MediatR` | `NetApiFlow`, MediatR | EF Core, driver de banco |
| `NetApiFlow.Storage.PostgreSql` | `NetApiFlow`, Npgsql | EF Core, Dapper (ADR-0004) |
| `NetApiFlow.Storage.SqlServer` | `NetApiFlow`, Microsoft.Data.SqlClient | EF Core, Dapper (ADR-0004) |

Violação desta tabela é bloqueante: não "ajustar depois". Teste de arquitetura na Fase 1
(tarefa 1.1) prova isso para o núcleo.

## Visibilidade e API pública

- A superfície pública de cada pacote é a definida em `docs/plano/contratos.md`. Nada além
  disso nasce `public` sem avisar a Planejadora.
- Tudo que não precisa ser público é `internal`. Preferir `sealed` por padrão em classe que não
  foi desenhada para herança.
- Todo membro `public` tem comentário XML (`<summary>`, `<param>`, `<returns>`, `<exception>`
  quando relevante), em inglês. `GenerateDocumentationFile` é `true` em `Directory.Build.props`.

## Código

- Identificadores em inglês (D-02). `Nullable` ligado e warning é erro
  (`TreatWarningsAsErrors`). Não desligar regra de análise para passar o build sem antes
  entender por que ela disparou.
- Async com `CancellationToken` em tudo que faz I/O (inclusive persistência, HTTP, banco).
  Nome do parâmetro sempre `cancellationToken`.
- `record` para modelo de domínio imutável (`FlowTrace`, `FlowSpan`), conforme
  `docs/plano/contratos.md`.
- Toda exceção lançada **dentro** da própria instrumentação (listener, interceptor, proxy,
  middleware) é capturada e logada, nunca propagada para o código do usuário. Ver
  `.claude/rules/tracing.md`.
- Sempre usar `{}` em `if`, `else`, `foreach`, `while`, sem exceção.

## Multi-targeting

- `net8.0;net10.0` em todo `.csproj` de produto (ADR-0003). Divergência de API entre os dois
  alvos isolada com `#if NET10_0_OR_GREATER`, no menor número de arquivos possível.
- Não assumir API só disponível em `net10.0` fora de um bloco `#if`.
