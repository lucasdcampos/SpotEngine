# Particle System — Implementation Plan & Checklist

> Living document. Updated as work progresses.
> Legend: `[ ]` todo · `[~]` in progress · `[x]` done

## Context

Spot precisa de um **sistema de partículas básico** que funcione tanto em cenários
3D quanto 2D. O objetivo **não** é replicar o Shuriken da Unity, e sim entregar uma
**base sólida e extensível**: um único componente (`ParticleSystemComponent`) que
emite quads texturizados com blending, animados ao longo da vida (cor/tamanho), e que
o usuário possa usar como ponto de partida para criar seus próprios efeitos
(fumaça, fogo, faíscas, poeira, magia, etc.).

Decisões de design derivadas da arquitetura existente:

- **Componente + Sistemas** (padrão do engine): `ParticleSystemComponent` (dados) +
  `ParticleSystem` (simulação, roda em play mode via `Scene.UpdateRuntime`) +
  `ParticleRenderSystem` (desenho, chamado de dentro de `RenderSystem.Render`).
- **Renderização por billboards batcheados**: um `ParticleRenderer` estilo
  `Renderer2D` (VBO dinâmico, batch por textura/blend, uma draw call por lote).
  Partículas 3D são quads que encaram a câmera; partículas 2D são quads planos no
  passe ortográfico.
- **Blend Alpha e Additive**, depth-test ligado mas depth-write desligado no 3D
  (transparência básica, sem sorting pesado).
- **Serialização e inspector automáticos** via `[SceneComponent]` + `[ComponentMenu]`
  e atributos de inspector (reflexão já existente). Sem novo tipo de asset cozido:
  reusa `Texture2D` (`.sptex`) via `[AssetReference]` + guid.
- **Nunca crashar**: emissão/simulação/render dentro de try/catch com quarentena,
  seguindo o padrão dos outros sistemas.

---

## Checklist

### Fase 1 — Modelo de dados (`engine`)
- [x] `ParticleSystemComponent` (`engine/Scenes/ParticleSystemComponent.cs`)
- [x] Enums: `EmitterShape` (Point/Box/Sphere/Cone), `ParticleSpace` (Local/World),
      `ParticleRenderMode` (Billboard3D/Flat2D), `ParticleBlendMode` (Alpha/Additive)
- [x] Propriedades de emissão: `MaxParticles`, `EmissionRate`, `PlayOnAwake`,
      `Looping`, `Duration`
- [x] Propriedades iniciais: `StartLifetime`, `StartSpeed`, `StartSize`,
      `StartColor`, `StartRotation`/spin
- [x] Ao longo da vida: `EndColor` (fade), `EndSize`, `Gravity`, `Damping`
- [x] Forma do emissor + parâmetros (`BoxSize`, `Radius`, `ConeAngle`) com `[ShowIf]`
- [x] Render: `RenderMode`, `BlendMode`, `Texture` (`[AssetReference]` + `TexturePath`)
- [x] Estado de runtime interno (buffer de `Particle`, acumulador, tempo, RNG) —
      `[HideInInspector]`/não serializado

### Fase 2 — Simulação (`engine`)
- [x] `ParticleSystem.Update(Scene, dt)` (`engine/Scenes/ParticleSystem.cs`)
- [x] Emissão por taxa + amostragem da forma do emissor
- [x] Integração de velocidade, gravidade e damping
- [x] Envelhecimento, reciclagem e limite `MaxParticles`
- [x] Playback: play-on-awake, looping, duração, simulação Local vs World
- [x] Registrar em `Scene.UpdateRuntime` com try/catch (quarentena)

### Fase 3 — Renderização (`engine`)
- [x] `ParticleRenderer` batcheado (`engine/Rendering/ParticleRenderer.cs`)
- [x] Shader de partículas (unlit, tint, textura, soft alpha)
- [x] `ParticleRenderSystem.Render(...)` (`engine/Scenes/ParticleRenderSystem.cs`)
- [x] Integrar passe 3D (billboards) em `RenderSystem.Render` (após opacos)
- [x] Integrar passe 2D (flat quads) no passe de sprites
- [x] Blending Alpha/Additive + depth-write off no 3D

### Fase 4 — Editor
- [x] Aparece no "Add Component" e no inspector (automático via reflexão)
- [x] Atributos de inspector afinados (ranges, cores, ShowIf, slot de textura)
- [x] Ícone de entidade + glyph em `EditorIcons`/`EditorGui.IconFor`

### Fase 5 — Serialização
- [x] `[SceneComponent("ParticleSystem")]` + round-trip salvar/carregar

### Fase 6 — API de script (base mínima)
- [x] Métodos `Play()`, `Stop()`, `Clear()`, `Emit(n)` no componente

### Fase 7 — Demo no Sandbox
- [x] Emissor de exemplo na cena do `sandbox`

### Fase 8 — Docs & validação
- [x] `docs/particles.md`
- [x] Atualizar tabela em `docs/entities-and-components.md` (e `rendering.md` se preciso)
- [x] `dotnet build SpotEngine.slnx` limpo (warnings = errors)
- [x] `dotnet test SpotEngine.slnx` passa

---

## Notas / decisões em aberto
- Preview em edit mode (simular fora do play) fica como melhoria futura — a base
  simula somente em play mode, como os demais sistemas.
- Sorting por profundidade completo é fora de escopo; usamos depth-test + additive/alpha
  como aproximação barata.
