# Fase 41: `ExternalSources` ligado por padrão em `Development`

Item 15 do `roadmap-valor.md` (D-132, segunda auditoria externa do site, seção 4), só o primeiro
dos dois degraus que o item descreve. Decisão de forma fechada pela Planejadora nesta entrada, sem
ADR: muda só um valor padrão, dentro de `Development`, sem API pública nova. O segundo degrau
(rastreamento sem exigir interface) continua fora do alcance desta fase, ver "Fora de escopo".

## Tarefa 41.1: `ExternalSources` default muda só em `Development`

Hoje `NetApiFlowOptions.ExternalSources` nasce `ExternalActivitySource.None`
(`src/NetApiFlow/Configuration/NetApiFlowOptions.cs`), sempre, em qualquer ambiente: concorrentes
reais (Spector, DotNetScope, AsGuard) instrumentam sozinhos, o NetApiFlow pede configuração
explícita até pra captura nativa que o `HttpClient`/Npgsql/`Microsoft.Data.SqlClient` já emitem
de graça. `AddNetApiFlow` (`src/NetApiFlow/DependencyInjection/NetApiFlowServiceCollectionExtensions.cs`)
não tem acesso a `IHostEnvironment` no momento em que `configure` roda (a `IServiceCollection`
ainda não virou `IServiceProvider`), então o default não pode ser decidido ali.

Registrar um `Configure<NetApiFlowOptions, IHostEnvironment>` **antes** de `services.Configure(configure)`
(ordem de registro no Options pattern: quem registra primeiro roda primeiro, e a configuração do
usuário, registrada depois, sempre tem a última palavra sobre qualquer propriedade que ela toque):

```csharp
services.AddOptions<NetApiFlowOptions>().Configure<IHostEnvironment>((options, env) =>
{
    if (env.IsDevelopment())
    {
        options.ExternalSources = ExternalActivitySource.HttpClient
            | ExternalActivitySource.Npgsql
            | ExternalActivitySource.SqlClient;
    }
});
```

Com isso: em `Development`, sem nenhuma configuração explícita, as três fontes nativas já existentes
(Fase 8, ADR-0012; Fase 19, ADR-0018; Fase 37) ficam ligadas. Fora de `Development`, continua
`None`, sem mudança. Um usuário que explicitamente chamar
`o.ExternalSources = ExternalActivitySource.None` dentro do próprio `configure` continua
desligando, porque o `configure` dele roda depois e vale por último, sem precisar de nenhum
sentinela extra pra distinguir "nunca tocado" de "tocado e zerado de propósito".

**Pronto quando:** teste de integração prova as três situações: `Development` sem configuração
explícita (as três fontes ligadas); `Production` sem configuração explícita (continua `None`,
sem mudança de comportamento); `Development` com `o.ExternalSources = ExternalActivitySource.None`
explícito no `configure` do usuário (continua `None`, a escolha do usuário sempre vence).

## Fora de escopo

- **Degrau 2 do item 15 (rastrear Service/Repository sem exigir interface registrada no DI):**
  o próprio texto do roadmap já identifica que isso pode exigir um source generator novo
  (pacote inteiro, não extensão de `NetApiFlowOptions`), decisão de arquitetura real. Precisa de
  uma fase de investigação própria (molde da Fase 38) antes de qualquer decisão de forma, não
  entra nesta fase nem no loop autônomo atual. Permanece registrado no roadmap como pendente.
- Qualquer mudança no default fora de `Development`: `Production`/`Staging` continuam exigindo
  configuração explícita, sem exceção.

## Aceite da fase

`dotnet build NetApiFlow.slnx -c Release` e `dotnet format NetApiFlow.slnx --verify-no-changes`
limpos nos dois TFMs. Suíte completa verde, incluindo o teste novo da tarefa 41.1. Nenhuma mudança
em `contratos.md` (é mudança de valor padrão, não de forma pública).

## Ao terminar

Resumo pra Planejadora: o que foi feito, como verificar (comandos exatos), o que ficou pendente
(degrau 2 continua em aberto, precisa de investigação própria antes de qualquer decisão).
