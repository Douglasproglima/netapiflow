# Fase 28: Anotação de deploy na timeline do histórico

Item do roadmap de valor sem posição fixa (D-60): "anotação de deploy na linha do tempo do
histórico". Toca núcleo (interface pública nova), os três storages e a API REST do dashboard.
Decisão de persistência já travada pelo usuário em D-106: compartilhada entre todo mundo que abre
o dashboard, nunca só no navegador de quem registrou o marco, porque o propósito declarado da
feature é correlacionar um incidente investigado depois com um deploy feito por outra pessoa,
provavelmente em outra máquina, outro dia.

## O que a pesquisa já confirmou

- `history.component` (`features/history/`) hoje é uma tabela simples e paginada
  (`<table class="history__table">`), sem nenhum eixo temporal, sem SVG, sem `d3`.
- `GET /flow/api/config` já tem um campo `version` (D-46), mas é a versão do próprio pacote
  `NetApiFlow` (`AssemblyInformationalVersionAttribute`), não a versão da API do usuário: não serve
  pra correlacionar "quando o usuário fez deploy da própria aplicação".
- Nenhuma tabela ou storage grava hoje o início do processo do host do usuário, nem existe uso de
  `IHostApplicationLifetime`/`ApplicationStarted` em nenhum lugar do núcleo ou dos storages: não
  há sinal interno a reaproveitar, a feature é nova de ponta a ponta.

## Tarefa 28.1: contrato novo

Interface irmã dedicada, não extensão de `ITraceStore` (sugestão do usuário em D-106, decisão de
forma a confirmar nesta tarefa se uma alternativa mais simples aparecer durante a implementação):

```csharp
namespace NetApiFlow;

public sealed record DeployAnnotation
{
    public required string Id { get; init; }
    public required DateTime Timestamp { get; init; }
    public required string Label { get; init; }
}

public interface IDeployAnnotationStore
{
    ValueTask SaveAsync(DeployAnnotation annotation, CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<DeployAnnotation>> QueryAsync(DateTime from, DateTime to, CancellationToken cancellationToken);
}
```

Sem `DeleteAsync` nesta fase (YAGNI até aparecer um pedido real de apagar um marco). Pasta
`Abstractions/` (`.claude/rules/dotnet-core.md`), ao lado de `ITraceStore`.

`InMemoryDeployAnnotationStore` no núcleo, implementação default, mesmo padrão de
`InMemoryTraceStore`. `PostgreSqlDeployAnnotationStore` e `SqlServerDeployAnnotationStore` nos
pacotes de storage já existentes (`NetApiFlow.Storage.PostgreSql`, `NetApiFlow.Storage.SqlServer`),
com uma tabela nova em cada `schema.sql` (ex. `netapiflow_deploy_annotations`: `id`, `timestamp`,
`label`). Provado nos três via Testcontainers contra os dois bancos reais, mesma régua de
`TraceStoreTestsBase` (classe de teste base irmã, ou reaproveitada se fizer sentido na
implementação).

**Pronto quando:** uma anotação salva via qualquer uma das três implementações é lida de volta
com os mesmos valores, provado por teste contra os três stores.

## Tarefa 28.2: API REST

`POST /flow/api/deploys`: cria uma anotação a partir de `{ label: string }`. O `timestamp` é
sempre `DateTime.UtcNow` gerado no servidor, nunca recebido do cliente. Evita um relógio de
cliente divergente contaminar a ordenação na timeline.

`GET /flow/api/deploys?from=&to=`: lista as anotações da janela, pra popular `/history`.

Mesma régua de segurança de toda rota `/flow/api/*` (`.claude/rules/tracing.md`): 404 fora de
`Development` sem `IFlowAccessFilter`/autorização explícita configurada. `POST
/flow/api/deploys` é rota de escrita: some da lista do Runner por padrão, mesmo tratamento que
`DELETE /flow/api/traces` já recebe (D-33/D2).

**Pronto quando:** `POST` cria e `GET` lista de volta, nos dois sentidos, fora de `Development`
responde 404 sem autorização configurada (mesmo teste de regra que as outras rotas do dashboard já
têm).

## Tarefa 28.3: como o marco é registrado

A dor documentada em D-60 (correlacionar incidente investigado depois com o deploy de outra
pessoa) sugere que o caso de uso mais realista é uma chamada automatizada do próprio pipeline de
release do usuário logo depois do deploy (`curl -X POST .../flow/api/deploys -d '{"label":
"v1.4.2"}'`), não necessariamente um clique manual no dashboard no momento do deploy. Documentar
esse uso no README (exemplo de chamada via `curl`/script de CI), e decidir durante a implementação
se também vale a pena um botão manual simples em algum canto do dashboard pra quem quiser registrar
o marco na hora, sem pipeline de CI. Se os dois caminhos forem suportados, os dois usam a mesma
rota da tarefa 28.2, sem contrato duplicado.

## Tarefa 28.4: marcação em `/history`

Decisão de escopo (D-106, por YAGNI): os marcos de deploy entram como **linhas intercaladas na
mesma tabela** que já existe em `history.component`, ordenadas cronologicamente junto com as
execuções (por `startedAt`/`timestamp`), visualmente distintas (ex. linha de largura total, estilo
e ícone próprios, rótulo + horário). Não é um eixo temporal gráfico novo: `history.component` hoje
não tem nenhum, e `.claude/rules/frontend.md` reserva renderização de gráfico customizado só pro
que o grafo já usa. Se essa forma se mostrar insuficiente depois de implementada e usada de
verdade, promover pra uma timeline visual própria é decisão de uma fase futura, não implícita
nesta.

**Pronto quando:** uma anotação criada via `POST /flow/api/deploys` aparece em `/history` na
posição cronológica certa entre as execuções, visualmente distinguível de uma linha de trace.

## Fora de escopo, de propósito

- Editar ou apagar uma anotação já criada (YAGNI, sem `DeleteAsync` na tarefa 28.1).
- Mostrar os marcos em qualquer tela além de `/history` (`/insights`, `/architecture`) nesta fase.
- Qualquer validação de quem pode registrar um marco além da mesma régua de autorização que já
  protege toda rota de escrita do dashboard.

## Aceite da fase

Critérios de pronto das quatro tarefas acima. Testes nos três stores via Testcontainers. Build/
test/format dos dois lados. Impeccable rodado em `/history`, tema claro e escuro, desktop e
`390x844`.

## Ao terminar

Resumo pra Planejadora: o que foi feito, como verificar (criar uma anotação via `curl`, conferir
que aparece em `/history` na posição certa), qual caminho de registro foi implementado (só
automatizado, só manual, ou os dois), o que ficou pendente. A Planejadora roda `/revisar-fase 28`
e atualiza `docs/plano/roadmap-valor.md`/`README.md` com o roadmap de valor zerado.
