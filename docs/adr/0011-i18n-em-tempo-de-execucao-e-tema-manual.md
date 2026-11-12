# ADR-0011: i18n do dashboard em tempo de execução, tema com alternador manual

## Status
Aceito

## Discussion
[#182](https://github.com/Douglasproglima/netapiflow/discussions/182)

## Contexto

O dashboard ganhou um pedido novo do usuário: idioma com PT-BR como padrão e inglês como
secundário, mais um alternador manual de tema claro/escuro (hoje só reage a
`prefers-color-scheme` do sistema operacional). Roadmap explícito, pós `0.1.0`: não bloqueia a
Fase 7 nem nenhuma tarefa atual.

O dashboard é embutido no assembly `NetApiFlow.Dashboard` como um único recurso estático
(ADR-0005), servido sem build-time de locale: `dotnet pack` embute `ClientApp/dist/` uma vez, o
mesmo conjunto de arquivos para todo consumidor. O Angular oficial (`@angular/localize`) traduz
em tempo de build, um bundle por idioma, cada um numa pasta própria (`dist/en/`, `dist/pt-BR/`).
Usar isso aqui obrigaria a reabrir o pipeline de empacotamento da Fase 5 (`BuildAngularClient`,
o glob de `EmbeddedResource`, `DashboardEmbeddedFiles`/`ManifestEmbeddedFileProvider`, o
`VerifyDashboardDistExists`) para lidar com várias subpastas de `dist/` em vez de uma, e exigiria
decidir como `UseNetApiFlowDashboard` escolhe qual bundle servir por requisição (negociação de
`Accept-Language`? path prefixado por idioma?), mudança de contrato, não só de apresentação.

## Decisão

**i18n em tempo de execução**, não build-time: um dicionário de textos por idioma (`pt-BR` e
`en`), os dois dentro do mesmo bundle já embutido, trocados por um `signal` no cliente, sem
recarregar a página. Sem mudança no pipeline de empacotamento da Fase 5, sem contrato novo: o
backend nunca sabe em que idioma o dashboard está, porque não há negociação de servidor nenhuma.

- Preferência de idioma guardada em `localStorage`, lida na inicialização. Sem preferência
  salva, o padrão é `pt-BR` (D-35), não o idioma do navegador: é o produto que decide o padrão,
  não o ambiente do usuário.
- Alternador de idioma visível na topbar ou no diálogo "About" (a decidir na tarefa de
  implementação), mesmo padrão visual do seletor PT/EN já usado no site de marketing
  (`netapiflow-site`), para manter as duas superfícies do produto consistentes.
- **Tema**: mantém `prefers-color-scheme` como padrão quando não há preferência salva, e ganha
  um alternador manual que escreve um atributo (`data-theme="dark"` / `data-theme="light"`) na
  raiz do documento, guardado em `localStorage`, sobrescrevendo o `@media (prefers-color-scheme)`
  quando presente. O site de marketing (`netapiflow-site`) hoje só tem `prefers-color-scheme`,
  sem alternador manual; se um dia ganhar um, usar o mesmo mecanismo de atributo, para as duas
  superfícies do produto ficarem consistentes.
- Código, comentário XML, log, mensagem de exceção e a API pública do núcleo continuam em
  inglês, sem exceção (D-02 não muda para eles). O que muda é só o texto visível na interface do
  dashboard embutido.

## Consequências

- Nenhuma mudança na Fase 5 (empacotamento) nem em `docs/plano/contratos.md`: é camada de
  apresentação, o mesmo bundle de sempre.
- Todo texto de interface do dashboard precisa existir nos dois dicionários desde o primeiro
  dia da implementação, não só depois: tela sem tradução quebra a promessa do produto.
- `docs/design/flow-workspace/guide.md` (D-33) cita rótulos em inglês como se fossem o texto
  final da tela. Na implementação, esses mesmos textos viram a entrada do dicionário `en`, e a
  tarefa de i18n adiciona o dicionário `pt-BR` ao lado, tradução completa, não resumida (mesmo
  critério já usado para `README.pt-BR.md`, D-02).
- Sem data de entrada ainda: fica registrado em D-35 como roadmap, vira tarefa de fase quando o
  usuário decidir abrir essa fase.
