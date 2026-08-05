# Análise da Spot Engine

> Diagnóstico técnico do projeto: arquitetura, bugs, performance, refatorações e roadmap de features.
> Data: 2026-08-04 · Build limpo (exit 0), testes passando (43 engine + 9/10 build).

## Visão geral

Spot é uma engine surpreendentemente coesa para o tamanho (~17k linhas). A arquitetura em
camadas (`Renderer` → `Renderer2D/3D` → `RenderSystem`), o ECS-lite, o sistema de "nunca
crashar" e o inspector orientado por reflexão/atributos são **maduros e bem pensados**. Os
maiores problemas não são de qualidade de código isolado, e sim de **algumas decisões
estruturais** (serialização manual, "Play" via build externo) e **lacunas de features
essenciais** (áudio, campos de script serializáveis, prefabs).

Pontos fortes que vale preservar:

- Rede de segurança de exceções (`Application.Run`, `ScriptSystem` quarentena) — muito bem feita.
- `ComponentInspector` (reflexão + atributos) — inspector 100% automático, excelente.
- Carregamento assíncrono de modelos (`ModelImporter.RequestAsync` + budget de upload) — sofisticado.
- Portabilidade de paths (`AssetPath`) e o toolchain `Spot.Build`.

---

## Bugs e problemas de correção (prioridade alta)

**1. Física sem timestep fixo nem clamp de delta** — `Application.Update`
(`engine/Core/Application.cs:283`) passa o delta cru do frame para `Scene.UpdateRuntime`, que roda
gravidade, character controller e resolução de colisão. Consequências reais:

- Comportamento depende do framerate (VSync ligado ⇒ física atrelada ao refresh do monitor).
- Um hitch (arrastar a janela, GC, carregar modelo) gera um `deltaTime` gigante que **explode
  molas e faz corpos atravessarem colliders** (tunneling). A `ItemGrabber`
  (`body.Velocity = diff * SpringForce`) é especialmente sensível.
- Correção mínima: `deltaTime = MathF.Min(deltaTime, 0.1f)`. Correção correta: acumulador de passo
  fixo para a física.

**2. Check de "grounded" frágil no character controller** — `CharacterController3DSystem.cs:77`:
`cc.IsGrounded = MathF.Abs(body.Velocity.Y) < 0.001f;`. No ápice do pulo a velocidade Y cruza zero
⇒ lê como "no chão" ⇒ permite **pulo duplo no ápice**; encostar no teto também zera Y e falsifica
grounded. Deveria vir de uma flag de contato real emitida pela física.

**3. "Camera position" falsa nos shaders 3D** — em `Renderer3D` o fragment shader deriva a câmera
de `uInverseViewProjection * vec4(0,0,-1,1)` (comentado como "approximate"). Isso **não é** a
posição do olho em projeção perspectiva ⇒ specular, fresnel da água e direção das point lights
ficam incorretos conforme a câmera se move. Deveria receber a posição real da câmera como uniform.

**4. Colliders ignoram rotação** — `Physics2D/3DSystem` e `BoxCollider*.GetWorldBounds` usam só
`WorldPosition`/`WorldScale`. Um box collider num objeto rotacionado gera AABB errado. Aceitável
como limitação documentada, mas hoje é silencioso.

**5. `WorldRotation` é soma de ângulos de Euler** (`TransformComponent.cs:41-53`) e
`CameraComponent.GetViewProjection` depende dela. Compor rotações de pai/filho por soma de Euler é
matematicamente incorreto ⇒ câmera filha de um pai rotacionado aponta errado (o character
controller contorna isso manualmente). Deveria compor por matriz/quaternion.

**6. Shadow map preso à origem do mundo** — `RenderSystem.cs:98-106`: ortho 100×100 centrado em
(0,0,0), near/far 1..200. Sombras somem longe da origem e em cenas grandes. Não segue a câmera nem
tem cascatas.

**7. Risco de spam no console** — `Shader.GetUniformLocation` (`Shader.cs:107`) chama
`Log.CoreWarn` toda vez que um uniform retorna -1, e é chamado **por frame**. Qualquer uniform
otimizado pelo compilador GLSL vira warning contínuo.

---

## Performance

**1. Sem cache de uniform location** — `Shader.SetUniform` faz `glGetUniformLocation` a cada
chamada. `DrawMesh` seta ~15 uniforms + até 16 de point lights **por mesh por frame**. Cachear em
`Dictionary<string,int>` é o ganho de perf mais barato da engine.

**2. `View<T>()` aloca uma `List<Entity>` nova a cada chamada** (`Scene.cs:233`), várias vezes por
frame (RenderSystem, física, scripts) — e o `ScriptSystem` ainda faz `.ToList()` por cima. Pressão
de GC constante. Vale um overload sem alocação ou buffers reutilizáveis.

**3. `TransformComponent.Matrix` recomputa a hierarquia recursivamente a cada acesso**, e
`WorldPosition`/`WorldScale` chamam `Matrix` de novo (`Decompose`). No RenderSystem o mesmo
transform é acessado múltiplas vezes por frame. Falta dirty-flag/cache do mundo.

**4. `Matrix4x4.Invert(viewProjection)` recomputado por mesh** em `DrawMesh` (e de novo em
skybox/clouds/grid). Deveria ser calculado uma vez em `BeginScene`.

**5. Sem frustum culling, batching ou instancing no 3D** — 1 draw call + re-upload completo de
uniforms por mesh. Broadphase de física é O(n²).

**6. Dirty-check do editor serializa a cena inteira** a cada 15 frames por cena aberta
(`EditorScene.UpdateSceneStatus`) — JSON completo só para detectar mudança.

---

## Refatorações estruturais

**1. `SceneSerializer` (712 linhas de boilerplate manual)** — o maior débito de manutenção. Cada
componente exige: um DTO + um bloco de serialize + um bloco de deserialize + um campo em
`EntityData`. Isso contrasta com o inspector, que é 100% automático via reflexão. Esse padrão é um
ímã para o bug "esqueci de serializar o campo X" e **é a causa direta** da lacuna de campos de
script não persistidos. Migrar para serialização orientada por reflexão/atributos (ou polimorfismo
do `System.Text.Json` por tipo de componente) eliminaria ~600 linhas e unificaria com o inspector.

**2. "Play" faz um build de release completo a cada vez** — `EditorScene.OnPlay` chama
`ProjectBuilder.Build`, que roda `dotnet publish -c Release -r win-x64 --self-contained true
-p:PublishSingleFile=true` (`ProjectBuilder.cs:64`) e só então lança o exe. Ou seja, **a iteração
mais rápida do editor é a operação de build mais lenta possível**. Mesmo mantendo o modelo
out-of-process, um build **Debug, framework-dependent, sem single-file** cortaria o tempo de Play
em ordens de magnitude. Ideal futuro: play in-process com `AssemblyLoadContext` recarregável.

**3. Doc drift** — `AGENTS.md` diz que `tests/` está vazio (já há 53 testes) e descreve o Play como
"serialize-snapshot round-trip" in-process (agora é processo externo). Campos mortos:
`EditorScene._sceneSnapshot` (`:56`) e `EditorScene.ImportModel()` (`:1114`) não são mais usados.

**4. `View<T>` só busca por tipo concreto** — não há query por classe-base/interface (ex.: "todas
as luzes", "todos os colliders"). Limita extensibilidade de sistemas.

---

## Features essenciais faltando

| Feature | Impacto | Nota |
|---|---|---|
| **Áudio** | Crítico | Não existe *nada* de som. Uma engine de jogos precisa disso. |
| **Campos de script serializáveis/editáveis** | Crítico | `ItemGrabber` tem `GrabDistance`, `SpringForce` etc. públicos, mas o inspector só guarda `ClassNames` — nenhum parâmetro de script é editável nem persistido. Depende da refatoração do serializer. |
| **Prefabs / templates de entidade** | Alto | Sem reutilização de hierarquias. |
| **Undo/Redo no editor** | Alto | Ausente. |
| **Referência de asset por GUID (.meta)** | Alto | Refs são por path; renomear/mover quebra a cena. |
| **UI/Texto em runtime** | Alto | Só ImGui (editor); jogos não têm como desenhar texto/HUD. |
| **Animação (esqueletal)** | Médio | Assimp importa só malhas, sem bones. |
| **Multi-seleção / duplicar / copiar-colar entidades** | Médio | Fluxo básico de editor. |
| **Gamepad + action mapping** | Médio | Input só teclado/mouse, sem rebinding. |
| **Hot-reload de scripts** | Médio | Hoje exige rebuild completo. |

---

## Ganhos rápidos (alto valor, baixo esforço)

Priorizados — todos são pontuais:

- [x] **1. Cache de uniform location no `Shader`** — maior ganho de perf por linha; também elimina o
   risco de spam de warning. (~15 linhas) ✅ `Shader._uniformLocations`
- [x] **2. Clamp de `deltaTime`** em `Application.Update` (`MathF.Min(dt, 0.1f)`) — mata
   explosões/tunneling de física em hitches. (1 linha) ✅ `Application.MaxDeltaTime`
- [x] **3. Play em build Debug/framework-dependent** em vez de Release self-contained single-file — corta
   drasticamente o tempo de Play. (poucas linhas) ✅ `ProjectBuilder.Build(fastDebug)` → `Build/play`
- [x] **4. Inverter view-projection uma vez em `BeginScene`** e reusar em DrawMesh/skybox/clouds/grid. ✅ `Renderer3D.s_inverseViewProjection`
- [x] **5. Passar posição real da câmera como uniform** — corrige specular/fresnel/point lights. ✅ `uCameraPos` via `RenderSystem.Render(..., cameraPosition)`
- [x] **6. Corrigir o check de "grounded"** (usar contato real ou limiar negativo pequeno) — mata o pulo
   duplo no ápice. ✅ `PhysicsBody3DComponent.Grounded` setado no contato de chão
- [ ] **7. Remover código morto** (`_sceneSnapshot`, `ImportModel`) e **atualizar `AGENTS.md`**.
- [x] **8. `near`/`far` da câmera configuráveis** (`CameraComponent` fixa 0.1/1000; ortho 3D clipa em -1/1). ✅ `CameraComponent.NearClip`/`FarClip` (serializados; ortho usa `-far..far`)

---

## Roadmap de features sugerido

- **Curto prazo:** os ganhos rápidos acima + refatorar o `SceneSerializer` para reflexão (destrava
  campos de script).
- **Médio prazo:** áudio (ex.: OpenAL/Silk.NET.OpenAL), prefabs, undo/redo, GUIDs de asset com
  `.meta`.
- **Longo prazo:** frustum culling + batching/instancing, shadow cascades, animação esqueletal, UI
  de runtime, play in-process com hot-reload.
