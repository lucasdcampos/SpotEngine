# Plano do Editor (Spot.Editor)

Documento de planejamento para um editor visual da SpotEngine, criado cedo no
desenvolvimento para acompanhar o crescimento da engine e evitar ter que construir
um editor complexo depois. Este arquivo descreve **o que** vamos construir e **como**;
ainda não é implementação.

## Objetivo

Um aplicativo editor que usa a própria engine (`Spot.Engine`) e ImGui para oferecer,
desde já, o essencial de um editor de cenas:

- **Hierarquia** da cena à esquerda
- **Inspetor** à direita
- **Console** embaixo
- **Viewport** (a cena renderizada) no centro
- Barra de menu no topo

O layout-alvo:

```
+----------------------------------------------------------+
| Menu:  Arquivo   Entidade   Janela   Ajuda               |
+-----------+----------------------------------+-----------+
| Hierarquia|                                  | Inspetor  |
|           |          [ Viewport ]            |           |
|  - Camera |     cena renderizada numa        | Transform |
|  - Player |     textura (framebuffer)        | Sprite2D  |
|  - Ball   |                                  |           |
+-----------+----------------------------------+-----------+
|  Console  (logs + comandos)                              |
+----------------------------------------------------------+
```

## Decisões de escopo (definidas)

| Tema | Decisão |
|------|---------|
| **Viewport** | Renderizar a cena para uma textura via **Framebuffer** (nova primitiva na engine) e exibi-la num painel `Viewport` com `ImGui.Image`. |
| **Layout** | **Painéis fixos** posicionados pelo editor a cada frame (`SetNextWindowPos/Size`). Funciona com o build atual do ImGui, sem docking. Estruturado para migrar para docking depois. |
| **Conteúdo** | Editor **depende só da engine**. Traz sua própria **cena-demo** (`DemoScene`), sem referência ao projeto `game`. |
| **Edição** | Inspetor **editável ao vivo** (Transform, cor do Sprite2D, nome) + **criar/excluir** entidades e **adicionar/remover** componentes pela UI. |

### Fora de escopo nesta primeira versão (próximos passos)

- **Docking** (painéis ancoráveis/rearranjáveis) — requer habilitar o branch _docking_ do ImGui (troca do build nativo do cimgui). O layout fixo é desenhado para facilitar essa migração.
- **Play / Pause / Stop** (modo edição vs. execução) — exige separar o *update* da cena do loop e um *snapshot* para voltar ao estado de edição.
- **Salvar / carregar cena** (serialização em disco) — sistema maior; ainda não existe serialização na engine.
- **Gizmos** (manipuladores de mover/escalar no viewport), *picking* por clique, *asset browser*, e **hierarquia aninhada** (pai/filho) — hoje as entidades são uma lista plana; nesting real precisa de um componente de relação na engine.

## Arquitetura de integração

O loop principal vive em `Application.Run` e é dirigido por cenas (`SceneManager`
chama `OnUpdate/OnRender/OnImGuiRender` da cena ativa). Vamos aproveitar isso: **o
editor é, ele próprio, uma `Scene`** (`EditorScene`) que hospeda a cena sendo editada.

Isso mantém as mudanças na engine ao mínimo — nenhuma alteração no loop de
`Application`. Fluxo por frame:

1. `SceneManager.Update` → `EditorScene.OnUpdate`: atualiza estado do editor e a
   câmera do viewport. **Em modo edição não roda a lógica/scripts da cena hospedada**
   (por isso Play/Pause fica para depois).
2. `Renderer.Clear()` limpa o framebuffer padrão (fundo atrás do ImGui).
3. `SceneManager.Render` → `EditorScene.OnRender`: faz *bind* do `Framebuffer`, limpa,
   desenha a cena hospedada com `RenderSystem.Render(cena, câmeraDoEditor)`, faz *unbind*
   e restaura o viewport da janela.
4. `SceneManager.ImGuiRender` → `EditorScene.OnImGuiRender`: desenha a barra de menu e
   os painéis; o painel Viewport mostra a textura de cor do framebuffer.

> **Nota de evolução:** quando chegar o modo Play, promovemos esse conceito de
> "camada editor" para um gancho dedicado na engine (uma *editor layer* / callback de
> ImGui pós-cena, e a opção de renderizar a cena ativa para um framebuffer em vez da
> tela), em vez de "uma cena que contém uma cena". Por ora, `EditorScene : Scene` é o
> caminho mais simples e não-invasivo.

## Estrutura de projetos

Novo projeto executável na raiz, referenciando **apenas** a engine, adicionado ao
`SpotEngine.slnx`:

```
editor/
  Spot.Editor.csproj        // Exe, ProjectReference -> engine
  Program.cs                // monta o ApplicationSpec e chama app.Run(new EditorScene())
  EditorScene.cs            // a "camada" do editor: dono da cena ativa, framebuffer,
                            //   câmera, seleção e do layout dos painéis
  EditorContext.cs          // estado compartilhado entre painéis (cena ativa, seleção)
  EditorCamera.cs           // pan/zoom 2D do viewport (câmera ortográfica de edição)
  Panels/
    HierarchyPanel.cs       // lista as entidades; define a seleção
    InspectorPanel.cs       // mostra/edita os componentes da entidade selecionada
    ViewportPanel.cs        // desenha a textura do framebuffer; controla resize/aspect
    ConsolePanel.cs         // reaproveita o backend do DevConsole
  Scenes/
    DemoScene.cs            // conteúdo de exemplo do editor (entidades Transform+Sprite2D)
```

`EditorContext` (uma classe simples com `Scene ActiveScene` e `Entity? Selection`) é
passado aos painéis para mantê-los desacoplados e fáceis de crescer.

## Mudanças na engine

Mínimas e reutilizáveis:

1. **`engine/Rendering/Framebuffer.cs` (nova)** — primitiva de *render-to-texture*, no
   mesmo estilo dos recursos existentes (`Texture2D`, `VertexArray`, `IDisposable`,
   usando `Renderer.Gl`):
   - Anexo de cor (textura) + *renderbuffer* de profundidade/stencil.
   - `Framebuffer(uint width, uint height)`, `Bind()`, `Unbind()`, `Resize(w, h)`,
     `ColorAttachment` (handle da textura para o ImGui) e `Dispose()`.
   - `Bind` ajusta o viewport para o tamanho do FBO; `Unbind` restaura para a janela.
   - É uma primitiva útil para a engine além do editor (pós-processamento, etc.).

2. **`engine/Console/DevConsole.cs` (refactor pequeno)** — separar o *conteúdo*
   (saída + input) do *chrome* da janela: extrair um `DrawContents()` sem
   `ImGui.Begin/End`. A janela flutuante atual vira um *wrapper* que chama
   `DrawContents()`, e o `ConsolePanel` do editor chama o mesmo método dentro do seu
   painel fixo. Assim logs e comandos são compartilhados (via `Application.Instance.Console`).

Nenhuma mudança no loop de `Application` nem no `SceneManager`.

## Detalhes técnicos a observar

- **Textura do viewport:** `ImGui.Image((IntPtr)colorAttachmentHandle, size)`. Texturas
  do OpenGL são *bottom-up*, então passar `uv0 = (0,1)` e `uv1 = (1,0)` para não exibir
  a imagem invertida.
- **Resize do framebuffer:** dimensionar o FBO por `ImGui.GetContentRegionAvail()` do
  painel Viewport e ajustar a projeção da `EditorCamera` ao *aspect ratio* do painel
  (evita distorção quando o usuário redimensiona).
- **Foco de input:** *pan/zoom* e atalhos do viewport só quando o painel estiver
  focado/*hovered*, para não conflitar com edição de texto no inspetor/console.
- **Inspetor sem serialização/reflexão:** o conjunto de componentes é pequeno
  (`TagComponent`, `Transform`, `Sprite2D`, `ScriptComponent`). O inspetor faz
  `TryGetComponent<T>` para cada tipo conhecido e desenha um editor específico
  (`DragFloat3` para Transform, `ColorEdit4` para Sprite2D, `InputText` para o nome).
  `Transform`/`TagComponent` são intrínsecos (todo `Instantiate` os cria) e não podem
  ser removidos. Uma enumeração genérica de componentes (`Entity.Components`) fica como
  melhoria futura.
- **Criar/excluir:** usa as APIs públicas existentes `Scene.Instantiate(name)` e
  `Scene.Destroy(entity)`; após excluir, os painéis checam `entity.IsValid` e limpam a
  seleção.
- **Hierarquia plana:** lista simples de entidades por enquanto (sem árvore pai/filho).

## Plano de implementação (fases)

Cada fase compila e roda; validamos visualmente antes de seguir.

1. **Scaffold + layout fixo** — criar o projeto `editor`, referenciar a engine, incluir
   no `.slnx`, `Program.cs` roda uma `EditorScene` vazia com os 4 painéis + barra de
   menu posicionados (conteúdo placeholder). Objetivo: ver o esqueleto do editor.
2. **Framebuffer + Viewport** — adicionar `Framebuffer` na engine; `EditorScene` renderiza
   a `DemoScene` no FBO; painel Viewport exibe a textura com aspect/resize corretos.
3. **Hierarquia + seleção + Inspetor (leitura)** — listar entidades, selecionar, e
   mostrar (somente leitura) os componentes da entidade selecionada.
4. **Edição ao vivo** — inspetor editável: Transform (posição/rotação/escala), cor do
   Sprite2D e nome, refletindo na cena em tempo real.
5. **Criar/excluir entidades e componentes** — botões "Add Entity"/"Delete" e menu
   "Add Component"/remover no inspetor.
6. **Console** — refactor do `DevConsole` e `ConsolePanel` reaproveitando logs/comandos.
7. **Polimento** — restrições de layout, estilo, persistência de tamanhos de painel
   (`imgui.ini`) e ações da barra de menu.

## Como rodar (após implementado)

```
dotnet run --project editor
```

O projeto `game` continua independente e executável como antes
(`dotnet run --project game`).

---

*Idioma:* este documento está em português para acompanhar a conversa; o código e os
comentários XML seguem em inglês, como no restante do repositório. Posso traduzir este
plano para inglês se preferir manter tudo no mesmo idioma.
