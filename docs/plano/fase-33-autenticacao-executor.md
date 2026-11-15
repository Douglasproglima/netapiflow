# Fase 33: autenticação automática no painel "Testar um endpoint"

Achado em dogfooding real (não contra o sample, contra uma API de produção interna de terceiro):
a esmagadora maioria das rotas reais de uma API exige autenticação. Hoje, testar uma rota
protegida pelo Executor exige logar fora do dashboard, copiar o token manualmente e colar como um
header customizado genérico (Tarefa 32.10) a cada rota, e esse header não sobrevive a um refresh
da página. Numa API pequena isso é só chato; numa API real com dezenas de rotas protegidas,
inviabiliza o uso do painel.

É feature nova, não correção de bug em produção: branch normal (`feature/*`), a partir de
`develop`, sem pressa de hotfix. Nenhuma tarefa exige API pública nova no núcleo nem no
`NetApiFlow.Dashboard` (`.NET`): o documento OpenAPI que o host já expõe (`GET /openapi/v1.json`,
lido pelo Executor desde a Fase 4) já carrega, em qualquer API ASP.NET Core bem configurada,
`components.securitySchemes` e, por operação, qual esquema a protege. É a mesma metadata que
Swagger UI e Scalar já usam pra desenhar o botão "Authorize". Fase inteira é PATCH-safe.

## Regra geral desta fase

Toda tarefa escreve primeiro o teste que prova o comportamento esperado, confirma que ele falha
(quando aplicável, ex. parsing sem a função ainda existir), só depois implementa.

## Tarefa 33.1: ler `components.securitySchemes` e o `security` de cada operação

`OpenApiDocument`/`OpenApiOperation` (`core/models.ts`) não capturam nada de segurança hoje. Uma
API protegida declara isso no documento, por exemplo:

```json
{
  "components": {
    "securitySchemes": {
      "Bearer": { "type": "http", "scheme": "bearer" }
    }
  },
  "paths": {
    "/api/orders": {
      "get": { "security": [{ "Bearer": [] }] }
    }
  }
}
```

Acrescentar a `OpenApiDocument` um `components?: { securitySchemes?: Record<string,
OpenApiSecurityScheme> }`, e a `OpenApiOperation` um `security?: Array<Record<string, string[]>>`
(a chave de cada item é o nome do esquema). `OpenApiSecurityScheme` cobre pelo menos:

```typescript
interface OpenApiSecurityScheme {
  type: 'http' | 'apiKey' | 'oauth2' | 'openIdConnect' | string;
  scheme?: string; // "bearer", "basic", quando type é "http"
  in?: 'header' | 'query' | 'cookie'; // quando type é "apiKey"
  name?: string; // nome do header/query/cookie, quando type é "apiKey"
}
```

Função pura nova em `openapi-form.ts` (mesmo padrão de `listOpenApiEndpoints`):
`listSecuritySchemes(doc: OpenApiDocument): Array<{ name: string; scheme: OpenApiSecurityScheme }>`,
devolvendo só os esquemas `http`/`bearer` e `apiKey` (que esta fase sabe aplicar; `oauth2` e
`openIdConnect` entram na lista mas ficam marcados como não suportados, ver Tarefa 33.4).

**Pronto quando:** teste com um documento OpenAPI contendo os dois tipos de esquema confirma que
`listSecuritySchemes` devolve os dois, e que um documento sem `components.securitySchemes` devolve
lista vazia sem lançar exceção.

## Tarefa 33.2: painel "Autorizar", um campo por esquema distinto

No Executor, um botão/seção "Autorizar" (mesmo lugar que Swagger UI/Scalar colocam), visível só
quando `listSecuritySchemes` devolve pelo menos um esquema suportado. Um campo de texto por
esquema distinto, rotulado com o nome do esquema e o tipo (ex. "Bearer (http)",
"X-Api-Key (apiKey)"). Sem nenhum esquema no documento, a seção inteira não renderiza (não é regra
nova, mesmo padrão de "sem cenário configurado, a aba inteira some" já usado na Fase 31).

**Pronto quando:** teste confirma que a seção não aparece sem esquema nenhum no documento, aparece
com um campo por esquema quando existem, e o rótulo mostra nome e tipo corretos.

## Tarefa 33.3: persistir o valor na sessão do navegador

Guardar cada valor digitado em `sessionStorage` (sobrevive a um refresh da página, some ao fechar
a aba; diferente de `localStorage`, usado pra tema/idioma, D-123/Tarefa 32.1, que sobrevive até
fechar o navegador inteiro). Um token de autenticação não deveria durar tanto quanto uma
preferência de tema. Mesma guarda de try/catch que a Tarefa 32.1 já aplicou pra `localStorage`
(navegação privada, cota esgotada, iframe bloqueado): falha silenciosa cai no campo vazio, nunca
trava o app.

**Pronto quando:** teste com `sessionStorage` mockado pra lançar confirma que o Executor
inicializa normalmente, campo vazio, sem propagar exceção. Teste confirma que um valor digitado
sobrevive a recarregar o componente (simulando um refresh).

## Tarefa 33.4: aplicar o header certo automaticamente por rota

Em `send()` (`endpoint-form.component.ts`), antes de disparar a requisição: olhar o `security` da
operação selecionada (ou o `security` padrão do documento, se a operação não declarar o próprio);
para cada esquema exigido que o usuário já preencheu na Tarefa 33.2, calcular e anexar o header
certo:

- `type: http, scheme: bearer` → `Authorization: Bearer <valor>`;
- `type: apiKey, in: header` → header com o `name` declarado no esquema;
- `type: apiKey, in: query` → acrescentar como parâmetro de query, mesmo mecanismo que os campos
  de query já existentes usam.

Esquema `oauth2`/`openIdConnect` (fluxo completo de login redirecionando pra outro provedor): fora
do escopo desta fase. O campo aparece na lista (Tarefa 33.1/33.2) mas com uma nota de "não
suportado automaticamente" em vez de um campo de texto, sem tentar nada.

Precedência com o header manual (Tarefa 32.10, campo de headers genérico já existente): se o
usuário também adicionar manualmente um header com o mesmo nome que a autorização automática
calcularia, o manual vence (permite sobrescrever de propósito, ex. testar com token inválido de
propósito).

**Pronto quando:** teste confirma que uma rota com `security` de esquema Bearer preenchido dispara
a requisição com `Authorization: Bearer <valor>` certo; teste confirma que uma rota sem `security`
nenhum não recebe header nenhum da autorização automática; teste confirma que um header manual com
o mesmo nome sobrescreve o automático.

## Fora de escopo

- Fluxo completo de OAuth2/OpenID Connect (redirecionamento pra provedor de login externo).
- Qualquer mudança no núcleo `.NET` ou no `NetApiFlow.Dashboard` do lado servidor: a fase inteira
  lê metadata que o host já expõe, sem contrato novo.
- Indicador visual (ícone de cadeado) na lista de rotas do painel mostrando quais exigem auth:
  nice-to-have real, registrado como ideia separada em `roadmap-valor.md`, não bloqueia esta fase.

## Aceite da fase

Critérios de pronto das 4 tarefas acima. `npm run lint`, `format:check`, `test:ci`, `build`
limpos. Verificação visual manual (Impeccable não roda contra alvo local neste ambiente) na tela
`/flow/runner`, tema claro e escuro, desktop e `390x844`.

## Ao terminar

Resumo pra Planejadora: o que foi feito por tarefa, como verificar (idealmente contra uma API real
com autenticação, não só o sample), o que ficou pendente.
