# NetApiFlow: instruções do projeto

Biblioteca open source para ASP.NET Core que mostra, em tempo real, o caminho que cada
requisição HTTP percorre dentro da própria API: Controller/Endpoint → Handler/Caso de uso →
Services → Repositories → comandos SQL. Mostra duração de cada etapa, onde ocorreu a falha,
etapas lentas e etapas não executadas, com replay de execuções passadas. Experiência alvo:
instalar um pacote NuGet e adicionar duas linhas, como o Scalar ou o Hangfire Dashboard.

Inspirado na ideia do [apiflow](https://github.com/joshuamaia/apiflow) (Spring Boot). Nenhum
código, arquivo ou asset dele pode ser copiado ou portado. Tudo é escrito do zero.

Fonte única de verdade do produto: `netapiflow-prompt.md` (fora deste repositório, ver
`docs/plano/README.md` para o caminho). Em conflito entre `docs/plano/` e o prompt original,
**o prompt prevalece**: pare e relate, não improvise.

## Papel das duas IAs

Este projeto é conduzido por duas IAs com responsabilidades diferentes. Nenhuma sessão é uma ou
outra por padrão — o papel é definido por quem está operando a conversa, pelo que foi pedido no
início dela (ver "Qual é o seu papel" em `.claude/rules/git-workflow.md`):

- **IA Planejadora:** escreve e mantém `docs/plano/`, `docs/adr/` e `.claude/rules/`. Nunca
  escreve `src/`, `tests/` ou `samples/`. Revisa cada fase entregue contra os critérios de
  aceite antes de liberar a próxima.
- **IA Executora:** lê `docs/plano/fase-N-*.md` e implementa. Não altera `docs/plano/` nem
  `docs/adr/`. Ao fim de cada fase, entrega um resumo: o que foi feito, como verificar, o que
  ficou pendente (ver `.claude/rules/planning.md`).

Dentro de uma mesma rodada de trabalho o papel não troca: quem começou planejando não passa a
implementar no meio da conversa, e vice-versa.

Decisão nova tomada durante a execução que valha além da tarefa corrente vira ADR, escrito
pela Planejadora, não pela Executora.

## Stack

| Parte | Tecnologia | Pasta |
|---|---|---|
| Núcleo e pacotes .NET | .NET 8 e .NET 10 (multi-target), ASP.NET Core, xUnit | `src/`, `tests/` |
| Dashboard (frontend embutido) | Angular 22 (standalone, zoneless, Signals), Vitest | `src/NetApiFlow.Dashboard/ClientApp/` |
| Samples | ASP.NET Core Controllers e Minimal API | `samples/` |
| CI/CD | GitHub Actions | `.github/workflows/` |

## Idioma

- **Código, comentários XML, logs, mensagens de exceção, API pública, commits e títulos de
  PR: inglês.** É um pacote NuGet internacional.
- **Planejamento, interno:** `docs/plano/`, `docs/adr/` e a conversa com a Planejadora em
  português do Brasil.
- `README.md` em inglês, `README.pt-BR.md` em português, conteúdo equivalente.

## Documentação

- Plano mestre e estado de cada fase: `docs/plano/README.md`.
- Contratos (API pública do núcleo, formato dos eventos SSE, rotas REST): `docs/plano/contratos.md`.
  Não implementar divergindo deles sem avisar a Planejadora.
- Decisões travadas: `docs/plano/decisoes.md`. Riscos técnicos e mitigação: `docs/plano/riscos.md`.
- Decisão estrutural que vale para o projeto todo: `docs/adr/` (prefixo `ADR-`). Não contradizer
  ADR aceito sem escrever um ADR novo que o substitua.
- Regra que vale para todo código, sempre: `.claude/rules/`.

## Comandos

Backend (rodar da raiz do repo):
- Build: `dotnet build NetApiFlow.slnx`
- Testes: `dotnet test NetApiFlow.slnx`
- Formatar: `dotnet format NetApiFlow.slnx`
- Empacotar: `dotnet pack NetApiFlow.slnx -c Release`

Frontend do dashboard (rodar de `src/NetApiFlow.Dashboard/ClientApp/`):
- Instalar: `npm ci`
- Rodar: `npm start`
- Testes: `npm run test:ci`
- Lint e formato: `npm run lint` e `npm run format:check`
- Build: `npm run build`

Antes de dizer que uma fase terminou: build, testes, lint e formato limpos na parte que mudou,
nos dois target frameworks quando aplicável.

## Arquitetura (visão de uma tela)

```text
API do usuário → Middleware NetApiFlow → ActivityListener → FlowRecorder → Event Publisher
                                                                  │              │
                                                                  ▼              ▼
                                                        Persistence Writer   SSE /flow/api/stream
                                                        (Channel + BackgroundService)
                                                                  │
                                                                  ▼
                                                   ITraceStore (memória · Postgres · SQL Server)
                                                                  ▲
                                                     NetApiFlow.Dashboard (REST /flow/api/* + Angular embutido)
```

Diagrama completo em `docs/architecture.md` (Fase 7). Modelo de domínio e contratos em
`docs/plano/contratos.md`.

## Princípios

- **KISS e YAGNI**: a solução mais simples que atende o contrato da fase. Abstração nova só na
  terceira repetição real.
- **Núcleo sem dependência externa**: `NetApiFlow` só pode ter `FrameworkReference` para
  `Microsoft.AspNetCore.App`. EF Core, MediatR e drivers de banco ficam nos pacotes opcionais.
- **Nunca derrubar, travar ou atrasar a API do usuário.** Toda exceção da própria instrumentação
  é logada e engolida. Ver `.claude/rules/tracing.md`.
- **Nunca capturar dado sensível**: sem corpo, argumento, header, query string ou SQL com
  parâmetro. Rota sempre como template.
- **API pública pequena e documentada.** Tudo que não precisa ser público é `internal`.
- **Comentário é sintoma de nome ruim, não solução. Regra de ouro, sem exceção.** Se o código
  precisa de comentário para ser entendido, o nome de classe, método, variável ou propriedade
  está ruim. Resolve o nome. Não comenta por cima do nome ruim.
  Comentário só existe no último caso, para um porquê que nenhum nome resolve: uma decisão
  contraintuitiva, uma restrição externa (regra de negócio, bug de biblioteca, limite de API de
  terceiro), uma condição de corrida, ou uma fonte de pesquisa que sustenta a regra de negócio.
  Nunca repete o que o código já diz. Nunca descreve o quê, só o porquê. Nunca cita fase, etapa
  de plano ou número de ADR em comentário de código. Nunca serve de divisor de seção dentro de um
  arquivo grande: arquivo grande demais para navegar sem comentário-âncora precisa ser dividido
  em arquivos menores, não comentado.
  Ao mexer num trecho antigo, remover todo comentário que não passa nesse crivo, mesmo que não
  tenha sido pedido para mexer nele.

## Git

Gitflow clássico, SemVer. Resumo:

- `main` é produção, só recebe `release/*` e `hotfix/*` por PR.
- `develop` é integração. `feature/*`, `bugfix/*` e `chore/*` nascem dela e voltam por PR.
- Versão: SemVer (`0.1.0`, API instável até `1.0.0`). Tag `v<versao>` em `main` dispara a
  publicação no nuget.org.
- Commits fora de `main`: Conventional Commits em inglês (`feat:`, `fix:`, `refactor:`, `test:`,
  `docs:`, `chore:`), com escopo quando fizer sentido (`feat(core): ...`).

Detalhes em `docs/gitflow.md` e `.claude/rules/git-workflow.md`. Nunca fazer push, merge em
`develop` ou `main`, criar tag, abrir PR ou publicar release sem confirmação explícita de quem
pediu.

## Skills do projeto

- `/nova-feature <nome>`: cria `feature/<nome>` a partir de `develop` atualizado.
- `/release`: calcula a versão SemVer, abre `release/*`, prepara changelog e PR.
- `/revisar-fase <N>`: confere os critérios de aceite da fase N contra o código real antes de
  liberar a próxima.

## Escrita

Texto em português (documentação interna, commits internos, planos): frases curtas, sem
travessão (ver `.claude/rules/writing-style.md`). Texto em inglês (README, comentário XML,
mensagem de log): direto, sem floreio.
