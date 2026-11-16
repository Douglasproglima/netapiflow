# Fase 33: autenticação automática no painel "Testar um endpoint"

Achado em dogfooding real (não contra o sample, contra uma API de produção interna de terceiro):
a esmagadora maioria das rotas reais de uma API exige autenticação. Hoje, testar uma rota
protegida pelo Executor exige logar fora do dashboard, copiar o token manualmente e colar como um
header customizado genérico (Tarefa 32.10) a cada rota, e esse header não sobrevive a um refresh
da página nem a trocar de endpoint selecionado. Numa API pequena isso é só chato; numa API real
com dezenas de rotas protegidas, inviabiliza o uso do painel.

É feature nova, não correção de bug em produção: branch normal (`feature/*`), a partir de
`develop`, sem pressa de hotfix. Nenhuma tarefa exige API pública nova no núcleo nem no
`NetApiFlow.Dashboard` (`.NET`): o documento OpenAPI que o host já expõe (`GET /openapi/v1.json`,
lido pelo Executor desde a Fase 4) já carrega, em qualquer API ASP.NET Core bem configurada,
`components.securitySchemes` e, por operação, qual esquema a protege. É a mesma metadata que
Swagger UI e Scalar já usam pra desenhar o botão "Authorize". Fase inteira é PATCH-safe.

Revisão por uma segunda IA (D-129) confirmou o desenho de fundo e trouxe três ajustes
incorporados abaixo: não excluir `oauth2`/`openIdConnect` da lista de esquemas utilizáveis, ler o
`security` efetivo (documento inteiro, não só a operação) e tratar o caso, bem comum na prática,
de documento sem `securitySchemes` nenhum.

## Por que o caminho é ler o OpenAPI, não o backend fornecer um segredo

Alternativa descartada antes de escrever esta fase: o host configurar, no próprio backend, um
mecanismo ou credencial que o dashboard já nasce sabendo usar. Dois problemas reais. Primeiro, cria
superfície nova do lado do servidor, exatamente o tipo de acoplamento que o núcleo evita por
princípio. Segundo, o caminho mais comum de "configurar uma credencial no backend" é ela acabar
em `appsettings.json` versionado, o oposto do que qualquer boa prática de segredo recomenda. Ler o
documento OpenAPI que o host já expõe resolve o mesmo problema sem nenhuma das duas desvantagens: a
metadata já existe, e o valor digitado nunca sai do navegador em direção ao NetApiFlow.

## Modelo de ameaça do `sessionStorage`, dito com honestidade

O valor digitado é guardado em `sessionStorage`, legível por qualquer script do mesmo origin: um
XSS no origin do host rouba o valor. Para uma ferramenta de desenvolvimento, já atrás de
`RequireAuthorization`/`IFlowAccessFilter` em produção (o dashboard inteiro é 404 por padrão fora
de Development, R-09) e com a sessão expirando ao fechar a aba, é um retrato de risco aceitável, e
menos arriscado que a escolha do próprio Swagger UI (`persistAuthorization` usa `localStorage`, o
valor sobrevive semanas). Três consequências que
viram regra de implementação, não só de intenção:

- **Nunca** renderizar texto vindo do documento OpenAPI (`description`, `summary`, nome de
  esquema) via `[innerHTML]` ou `bypassSecurityTrust*`. Interpolação padrão do Angular (`{{ }}`,
  `[text]`) já escapa, então isto é "não fazer", não "implementar": foi renderizar spec como HTML
  sem escapar que deu ao Swagger UI seu histórico de CVE de XSS.
- O valor digitado nunca pode aparecer em texto puro em lugar nenhum da UI depois de salvo: campo
  mascarado (ver Tarefa 33.3), nunca ecoado sem máscara em mensagem de erro, URL exibida ou
  qualquer exportação futura de trace.
- Continuar ignorando o array `servers` do documento OpenAPI e chamar sempre a rota
  root-relative, exatamente como `ApiService.callEndpoint` já faz hoje (`api.service.ts`): honrar
  `servers` apontando para outro origin tiraria cada requisição autorizada do mesmo-origin, criando
  preflight de CORS novo e mandando o valor de autorização para fora do host. Nenhuma tarefa desta
  fase muda esse comportamento; é um não-fazer que vale registrar.

## Regra geral desta fase

Toda tarefa escreve primeiro o teste que prova o comportamento esperado, confirma que ele falha
(quando aplicável, ex. parsing sem a função ainda existir), só depois implementa.

## Tarefa 33.1: ler `components.securitySchemes` e o `security` efetivo do documento

`OpenApiDocument`/`OpenApiOperation` (`core/models.ts`) não capturam nada de segurança hoje. Uma
API protegida declara isso no documento, por exemplo:

```json
{
  "components": {
    "securitySchemes": {
      "Bearer": { "type": "http", "scheme": "bearer" }
    }
  },
  "security": [{ "Bearer": [] }],
  "paths": {
    "/api/orders": {
      "get": { "security": [{ "Bearer": [] }] },
      "delete": { "security": [] }
    }
  }
}
```

Acrescentar a `OpenApiDocument` um `components?: { securitySchemes?: Record<string,
OpenApiSecurityScheme> }` e um `security?: OpenApiSecurityRequirement[]` na raiz (o requirement
que vale para toda operação que não sobrescreve o próprio). Acrescentar a `OpenApiOperation` o
mesmo `security?: OpenApiSecurityRequirement[]`. `OpenApiSecurityRequirement` é
`Record<string, string[]>` (a chave de cada item é o nome do esquema). `OpenApiSecurityScheme`
cobre pelo menos:

```typescript
interface OpenApiSecurityScheme {
  type: 'http' | 'apiKey' | 'oauth2' | 'openIdConnect' | string;
  scheme?: string; // "bearer", "basic", quando type é "http"
  in?: 'header' | 'query' | 'cookie'; // quando type é "apiKey"
  name?: string; // nome do header/query/cookie, quando type é "apiKey"
}
```

Duas funções puras novas em `openapi-form.ts` (mesmo padrão de `listOpenApiEndpoints`):

- `listSecuritySchemes(doc: OpenApiDocument): Array<{ name: string; scheme: OpenApiSecurityScheme
  }>`: devolve **todo** esquema declarado em `components.securitySchemes`, sem filtrar por tipo.
  `oauth2` e `openIdConnect` entram na lista como qualquer outro: o fluxo completo de redirect
  fica fora de escopo (Tarefa 33.3/33.4 tratam o campo como "colar um access token já obtido"),
  mas o esquema não pode ficar invisível só por isso, senão a seção "Autorizar" some exatamente
  para quem a fase foi feita pensar (API atrás de Azure AD, Keycloak, Auth0).
- `effectiveSecurity(doc: OpenApiDocument, method: string, path: string):
  OpenApiSecurityRequirement[]`: o `security` da operação, se ela declarar a própria chave
  (incluindo array vazio, que no OpenAPI significa "esta operação é pública", explicitamente
  diferente de "operação não declara nada"); senão o `security` da raiz do documento; senão lista
  vazia. É o requirement global (`AddSecurityRequirement` no Swashbuckle, o jeito mais comum de
  configurar) que faz o header ser aplicado em qualquer API que não decore operação por operação.

**Pronto quando:** teste confirma que `listSecuritySchemes` devolve esquemas `http`, `apiKey`,
`oauth2` e `openIdConnect` todos, e lista vazia (sem lançar) quando o documento não declara
`components.securitySchemes`. Teste de `effectiveSecurity` cobre os três casos: operação com
`security` próprio vence a raiz, operação com `security: []` significa pública mesmo a raiz tendo
requirement, operação sem a chave cai no `security` da raiz.

## Tarefa 33.2: serviço de credenciais com persistência em `sessionStorage`

Hoje `customHeaders` (headers manuais da Tarefa 32.10) é estado local do
`EndpointFormComponent` e é zerado em `selectEndpoint()` a cada troca de rota
(`endpoint-form.component.ts`): colar um header uma vez só dura até trocar de endpoint. Essa fase
soma um segundo valor digitado (o de cada esquema de autorização) com a mesma necessidade de
sobreviver à troca de rota e a um refresh. Os dois merecem o mesmo dono: um serviço novo,
`RunnerCredentialsService` (`core/`), que guarda:

- um `Record<string, string>` chaveado pelo **nome do esquema** (não pelo tipo: dois esquemas
  `bearer` com nomes diferentes, ex. `BearerUser` e `BearerAdmin`, são campos distintos);
- a lista de headers manuais (migrada de `EndpointFormComponent` para este serviço).

Cada mudança grava com write-through em `sessionStorage` (duas chaves separadas), com a mesma
guarda de try/catch que a Tarefa 32.1 já aplicou para `localStorage` (navegação privada, cota
esgotada, iframe bloqueado): falha silenciosa cai no valor vazio em memória, nunca trava o app. Ao
construir, o serviço lê o que já estiver salvo.

Reconciliação ao carregar o documento OpenAPI: quando um documento **novo, carregado com
sucesso**, devolve um `listSecuritySchemes(doc)` diferente do anterior, descartar do estado em
memória e do `sessionStorage` qualquer valor salvo cujo nome de esquema não aparece mais na lista
atual. Sem isso, um valor salvo para um esquema que não existe mais no documento ficaria
"autorizado" para nada, o mesmo bug que `persistAuthorization` do Swagger UI já teve. A
reconciliação só roda depois de um fetch bem-sucedido: uma falha ao buscar o documento (rede
instável, API momentaneamente fora do ar) não pode ser lida como "lista de esquemas vazia,
descartar tudo". `EndpointFormComponent` já segue esse padrão hoje (`loadError`, setado só no
`error:` do subscribe, nunca zera `openApiDocument`); o serviço de credenciais reconcilia contra o
último documento carregado com sucesso, nunca contra um estado de erro.

`EndpointFormComponent.selectEndpoint()` para de zerar `customHeaders`: os dois agora vêm do
serviço e sobrevivem à troca de rota.

**Pronto quando:** teste confirma que um valor gravado sobrevive a simular um refresh (novo
component, mesmo serviço reconstruído a partir do `sessionStorage` mockado) e a trocar de
endpoint selecionado. Teste confirma que `sessionStorage` lançando ao escrever não propaga
exceção. Teste confirma que um valor de esquema que não existe mais no documento recarregado some
do estado, sem continuar marcado como preenchido. Teste confirma que uma falha ao buscar o
documento OpenAPI não aciona a reconciliação e não apaga nenhum valor já salvo.

## Tarefa 33.3: painel "Autorizar", um campo por esquema distinto

No Executor, uma seção "Autorizar" (mesmo lugar que Swagger UI/Scalar colocam), visível só quando
`listSecuritySchemes` devolve pelo menos um esquema. Sem esquema nenhum no documento, a seção
inteira não renderiza (mesmo padrão de "sem cenário configurado, a aba inteira some" já usado na
Fase 31). Um campo por esquema distinto, rotulado com o nome do esquema e o tipo (ex. "Bearer
(http)", "X-Api-Key (apiKey)", "OAuth2Scheme (oauth2, cole um access token)").

Todo texto novo visível (rótulo do campo, preview, nota de `in: query`, avisos da Tarefa 33.4) usa
`TranslatePipe`/`core/i18n/translations` como qualquer outro texto da UI, nunca string hardcoded
num idioma só.

Detalhes que resolvem dúvidas e erros clássicos de quem já usou Swagger UI/Scalar:

- Campo `type="password"` com botão de revelar/ocultar (mascarado por padrão): o projeto já tira
  screenshot de verificação visual como rotina (Impeccable, `design-quality.md`), e o dogfooding
  real usa token de API de produção.
- Um preview, ao lado do campo, do que será enviado com o valor mascarado: para `http`/`bearer` e
  `oauth2`/`openIdConnect`, o header literal (ex. `Authorization: Bearer ••••f82a`); para `apiKey
  in: header`, o header com o `name` declarado; para `apiKey in: query`, o parâmetro de query que
  será anexado à URL (ex. `?api_key=••••f82a`), não um header. Resolve a dúvida recorrente de
  "digito `Bearer` junto ou não" e o caso de um esquema `apiKey` com `name: "Authorization"` que já
  espera o valor com prefixo.
- Esquema `apiKey` com `in: "query"`: nota discreta de que o valor vai para a URL da requisição e
  pode aparecer no log de acesso do host. Não é motivo para recusar (o `spec` declara esse
  mecanismo), só para avisar.
- Esquema `apiKey` com `in: "cookie"`: fora de alcance deste painel. `fetch` não consegue setar
  `Cookie` manualmente, e a chamada já é same-origin, então cookies de sessão reais já são
  enviados nativamente hoje, sem nenhum código novo. Nunca tentar via `document.cookie` (o Swagger
  UI tenta e é uma fonte conhecida de bug).
- `http`/`basic` fica fora desta fase (raro em ASP.NET Core, barato de somar depois se aparecer
  demanda real).

**Pronto quando:** teste confirma que a seção não aparece sem esquema nenhum no documento, aparece
com um campo por esquema quando existem (inclusive `oauth2`/`openIdConnect`), o rótulo mostra nome
e tipo corretos, e o preview do header mascarado bate com o valor digitado.

## Tarefa 33.4: aplicar o header certo automaticamente por rota

Em `send()` (`endpoint-form.component.ts`), antes de disparar a requisição: calcular
`effectiveSecurity(doc, method, path)` (Tarefa 33.1) da operação selecionada; para cada esquema
exigido que o `RunnerCredentialsService` já tem preenchido, calcular e anexar o header certo:

- `type: http, scheme: bearer` (e `oauth2`/`openIdConnect`, tratados como "colar um access
  token"): `Authorization: Bearer <valor>`, depois de normalizar o valor digitado (ver higiene de
  colagem abaixo);
- `type: apiKey, in: header`: header com o `name` declarado no esquema;
- `type: apiKey, in: query`: acrescentar como parâmetro de query, mesmo mecanismo que os campos de
  query já existentes usam em `send()`.

**Higiene da colagem** (`RunnerCredentialsService` ou no ponto de leitura, antes de montar o
header): remover espaço em branco e quebra de linha nas pontas (um `\n` colado por engano faz o
`HttpClient` lançar um erro obscuro ao montar o header); se o valor colado já começa com
`"Bearer "` (sem diferenciar maiúscula/minúscula) e o esquema é `http`/`bearer` ou
`oauth2`/`openIdConnect`, remover o prefixo antes de montar `Authorization: Bearer <valor>`, para
não duplicar.

**Precedência com o header manual** (Tarefa 32.10): se o usuário também adicionar manualmente um
header com o mesmo nome que a autorização automática calcularia, o manual vence. Implementação via
`HttpHeaders.set(nome, valor)` (substitui), nunca `.append`, para nunca sair dois `Authorization`
na mesma requisição.

**Aviso de esquema exigido vazio**: `effectiveSecurity` é uma lista de **alternativas** (semântica
OR do OpenAPI: `[{Bearer: []}, {ApiKey: []}]` significa "uma OU outra", e cada objeto dentro de uma
alternativa é AND de esquemas). O aviso não pode olhar esquema por esquema isolado: uma rota com
duas alternativas, só uma preenchida, dispararia aviso falso sobre a alternativa vazia mesmo a
requisição saindo perfeitamente autorizada pela outra. Mostrar o aviso inline no formulário da rota
selecionada ("esta operação exige autenticação e nenhuma das opções configuradas está preenchida")
só quando **nenhuma** alternativa de `effectiveSecurity` está completamente preenchida. No caso
comum, uma alternativa só, o comportamento não muda. Sem bloquear o envio.

Caso espelho, mesma área do formulário: operação sem `security` nenhum declarado (documento
incompleto, bem comum na prática), com um campo de Autorizar preenchido mesmo assim. Nada é
aplicado, a requisição sai sem o header, e o 401 que vier parece inexplicável com o painel
"preenchido". Nota discreta, não bloqueante ("esta operação não declara exigir autenticação;
nenhum valor de Autorizar será aplicado aqui"), pra o 401 mudo não voltar pelo lado oposto.

**Pronto quando:** teste confirma que uma rota com `effectiveSecurity` exigindo um esquema Bearer
preenchido dispara a requisição com `Authorization: Bearer <valor>` certo, já sem prefixo
duplicado mesmo colando `"Bearer xyz"` no campo; teste confirma que uma rota com
`effectiveSecurity` vazio (incluindo o caso `security: []` na operação) não recebe header nenhum
da autorização automática; teste confirma que um header manual com o mesmo nome sobrescreve o
automático sem duplicar; teste confirma que o aviso de "nenhuma alternativa preenchida" aparece só
quando todas as alternativas de `effectiveSecurity` estão vazias e some assim que qualquer uma é
preenchida, com um caso de teste cobrindo duas alternativas e só uma preenchida (sem aviso); teste
confirma a nota de "não declara autenticação" quando a operação não tem `security` nenhum e o
usuário já preencheu um campo de Autorizar mesmo assim.

## Fora de escopo

- Fluxo completo de OAuth2/OpenID Connect (redirecionamento pra provedor de login externo): o
  campo aceita colar um access token já obtido por fora, não inicia nenhum login.
- Esquema `http`/`basic`.
- Tentar aplicar `apiKey in: cookie` via `document.cookie`: same-origin já cobre o caso real.
- Decodificar `exp` de um JWT colado para mostrar tempo até expirar: ideia de baixo custo e valor
  real, registrada à parte em `roadmap-valor.md`, não bloqueia esta fase.
- Reenviar a própria busca do documento OpenAPI com o token aplicado quando ela mesma devolve 401
  (API que tranca o `/openapi/v1.json` fora de ambiente de desenvolvimento): registrado à parte em
  `roadmap-valor.md`.
- Qualquer mudança no núcleo `.NET` ou no `NetApiFlow.Dashboard` do lado servidor: a fase inteira
  lê metadata que o host já expõe, sem contrato novo.
- Indicador visual (ícone de cadeado) na lista de rotas do painel mostrando quais exigem auth:
  nice-to-have real, já registrado como ideia separada em `roadmap-valor.md`, não bloqueia esta
  fase.

## Aceite da fase

Critérios de pronto das quatro tarefas acima. `npm run lint`, `format:check`, `test:ci`, `build`
limpos. Verificação visual manual (Impeccable não roda contra alvo local neste ambiente) na tela
`/flow/runner`, tema claro e escuro, desktop e `390x844`, incluindo o campo mascarado e o preview
do header.

Comportamento esperado, não defeito: `sessionStorage` é por aba. Abrir o dashboard numa segunda
aba pede os valores de Autorizar de novo, mesma sessão de navegador. É a troca consciente da
Tarefa 33.2 (token não sobrevive nem a sessão inteira do navegador, só à aba), não um bug a
corrigir depois.

## Ao terminar

Resumo pra Planejadora: o que foi feito por tarefa, como verificar (idealmente contra uma API real
com autenticação, não só o sample, repetindo o cenário de dogfooding que motivou a fase), o que
ficou pendente.
