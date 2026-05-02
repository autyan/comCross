# Shell UI Semantic Model

## Goal

This document defines the UI and UX semantics that must be extracted from the Figma Make prototype before visual replication continues.

The purpose is not to describe controls by framework type such as `Button`, `Border`, or `Grid`.
The purpose is to describe each UI object by semantic layers:

- what it is
- what it can do
- which layer owns layout
- which layer owns interaction
- which layer owns state
- which layer owns motion
- which layer owns visual emphasis

Once the semantics are stable, Avalonia implementation should be treated as a mapping problem rather than a visual guessing problem.

## Core Principle

Do not model the shell as a tree of generic Avalonia controls.

Model it as a tree of semantic objects.

Each semantic object is composed from a small number of semantic layers.
Only after those layers are defined should we decide whether the Avalonia implementation uses:

- a style
- a templated control
- a custom `Decorator`
- a custom drawable chrome
- a plain layout container

## Semantic Layer Model

All interactive shell objects should be decomposed using the following layer model.

### 1. Host

Purpose:

- participates in parent layout
- owns spacing between sibling objects
- owns indentation or grouping context
- remains visually transparent

Must not own:

- hover glow
- selected glow
- active border
- direct visual emphasis

Examples:

- list item host in session list
- top-tab slot in workload strip
- action slot in right dock

Avalonia mapping:

- `Grid`, `StackPanel`, `Panel`, `ContentPresenter`, or transparent `Border`
- no visual state styling

### 2. Structure Slot

Purpose:

- represents structural affordances around the primary object
- reserves space for optional parts
- expresses hierarchy without becoming the content surface

Examples:

- listener expand slot
- connection indent slot
- accessory slot for count or status badge

Avalonia mapping:

- explicit fixed columns or rows
- structure-only containers
- no highlight semantics unless the slot itself is interactive

### 3. Trigger

Purpose:

- independent hit target for a specific action
- may own hover and focus
- does not redefine the state of neighboring surfaces

Examples:

- expand/collapse button
- session detail info button
- icon-only settings trigger

Avalonia mapping:

- `Button.prototype-plain`
- future `prototype-icon-button`
- hit target and motion defined locally

### 4. Surface

Purpose:

- primary visible object body
- owns border, fill, radius, clipping expectation, interactive background
- owns selected and active rendering

Examples:

- workload tab surface
- session row surface
- quick command card surface
- search shell surface

Avalonia mapping:

- ordinary surfaces may use styled `Border`
- high-fidelity chrome surfaces should use custom chrome primitives
- state rendering must remain inside this layer

### 5. Content Cluster

Purpose:

- aligns icon, status dot, text, value, and badge as a single semantic group
- owns visual centering and truncation behavior
- must not own object state

Examples:

- status dot + `tcp #1`
- icon + endpoint + count
- send icon + text

Avalonia mapping:

- a dedicated `Grid` or horizontal cluster primitive
- avoid “independent columns plus arbitrary margins” where the cluster should behave as one unit

### 6. State Overlay

Purpose:

- encodes hover, active, selected, disabled, connected, disconnected
- must attach to the correct semantic layer

Rules:

- hover belongs to interactive surface or trigger, not host
- selected belongs to the selected object itself
- active must override hover
- already active objects do not animate as if they were merely hovered

Avalonia mapping:

- style classes
- state brushes
- chrome primitives
- transitions

### 7. Motion

Purpose:

- provides structural motion feedback
- indicates interactivity without becoming the primary state signal

Rules:

- hover motion is subtle upward lift plus low background emphasis
- active objects do not perform hover lift
- motion must stay attached to the interactive object, not its parent host

Avalonia mapping:

- `RenderTransform`
- transitions on transform and opacity
- never attach hover motion to layout-only host containers

### 8. Action Semantics

Purpose:

- maps user intent to the correct layer
- ensures that “which thing was clicked” matches “which semantic object changed”

Examples:

- session surface click selects session
- expand trigger click toggles listener tree
- info trigger opens session detail
- reconnect action triggers reconnect

Avalonia mapping:

- commands on the owning semantic object
- hit target boundaries must match semantic ownership

## Semantic Object Definitions

Below are the shell objects that should be treated as first-class semantics.

## Workload Tab

### Semantic composition

1. host
   top strip slot

2. surface
   tab chrome surface

3. content cluster
   active dot + title + optional default badge

4. state overlay
   inactive, hover, active

5. motion
   hover lift only when not active

### Rules

- the tab chrome owns the top accent, border, radius, and active rendering
- the content cluster must not define tab height
- badge is secondary metadata, not primary active signal
- active tab must not animate as hovered

### Avalonia mapping

- current primitive: `PrototypeTopTabChrome`
- current content composition: [WorkloadTabs.axaml](/home/autyan/SourceCode/comCross/src/Shell/Views/WorkloadTabs.axaml:1)

### Current gap

- top accent geometry still needs final prototype-level tuning
- hover motion exists in style, but semantic documentation must treat it as part of tab chrome rather than generic button behavior

## Workload Create Trigger

### Semantic composition

1. host
   top strip slot

2. trigger surface
   same chrome family as workload tab

3. content cluster
   plus icon centered

4. state overlay
   hover only

### Rules

- must share the same chrome height and border language as workload tabs
- must not become visually taller or flatter than neighboring tabs

### Avalonia mapping

- should reuse `PrototypeTopTabChrome`

## Session List Item

This is the most important semantic example because it contains multiple nested layers.

### Semantic composition

1. item host
   transparent list slot

2. structure slot
   expand trigger column for listener items or indent slot for child items

3. trigger
   expand/collapse trigger for listeners only

4. session surface
   visible bordered object body

5. content cluster
   transport icon + optional status dot + text + count

6. state overlay
   hover, selected, active

7. motion
   hover lift only when interactive and not selected

### Rules

- item host must remain visually transparent
- selected glow must never be attached to the item host
- the session surface owns border, fill, and selected emphasis
- listener structure and session surface are different semantic layers
- expand trigger must not visually shift the selected session surface background

### Avalonia mapping

- current session surface primitive: `Border.prototype-interactive-surface`
- current host implementation: `ListBoxItem` with local item template in [LeftSidebar.axaml](/home/autyan/SourceCode/comCross/src/Shell/Views/LeftSidebar.axaml:289)

### Current risk

- Avalonia `ListBoxItem` theme templates can still leak default selection behavior unless locally neutralized
- this means list container semantics must be explicitly overridden whenever high-fidelity row rendering is required

## Session Summary Card

### Semantic composition

1. host
   left panel top section

2. card surface
   current session summary block

3. content stack
   title row, endpoint, traffic metrics

4. action group
   reconnect button and info trigger

### Rules

- card height must be content-driven, not fixed if actions can overflow
- info trigger is a separate trigger semantic, not part of the title text
- action group owns hover and button motion

### Avalonia mapping

- current implementation is still partly layout-driven and should move to content-driven sizing

## Icon Button

### Semantic composition

1. trigger surface
   hit target shell

2. icon cluster
   centered icon

3. state overlay
   hover, focus, active if applicable

### Rules

- determine explicitly whether the shell is visible or invisible
- do not mix “bare icon trigger” and “bordered icon button” semantics without naming the difference

### Avalonia mapping

- current shell often uses `Button.prototype-plain` plus nested `Border`
- future primitive needed: `prototype-icon-button`

## Action Button

### Semantic composition

1. action host
   layout slot

2. action surface
   primary, secondary, destructive, or quiet action

3. content cluster
   icon + label

4. motion
   hover lift for eligible actions

### Rules

- action height belongs to the action surface semantic
- action should not exceed parent section height because of fixed parent rows
- primary and secondary actions share motion semantics even if fill differs

### Avalonia mapping

- current buttons are split between global `Button`, `Button.accent`, and custom nested borders
- future primitive needed: `prototype-action-button`

## Search Control

### Semantic composition

1. host
   toolbar slot

2. surface
   search shell

3. content cluster
   icon slot + input

4. state overlay
   focus, hover, disabled

### Rules

- icon and text alignment must be owned by the content cluster
- input focus must not break outer shell geometry

### Avalonia mapping

- current primitive already exists:
  - `Border.prototype-search-shell`
  - `TextBox.prototype-search-input`

## Overlay and Drawer

### Semantic composition

1. overlay host
   full-window backdrop

2. panel surface
   modal or drawer body

3. content sections
   header, body, footer or action groups

4. action semantics
   close, submit, secondary actions

### Rules

- backdrop is host, not panel
- panel surface owns border and framing
- drawer content height must be content-driven

### Avalonia mapping

- settings and notifications use overlay host + panel surface
- session detail uses drawer panel semantics

## State Priority Model

The following priority order must be used consistently.

1. disabled
2. selected or active
3. hover
4. idle

Implications:

- hover must not replace selected
- active must not lift as hovered
- selected surface glow stays local to the selected surface

## Motion Model

### Allowed hover motion

- slight upward translation
- low-strength background lightening

### Disallowed motion

- whole-list-item host lift
- hover animation on already selected objects
- glow expansion beyond semantic surface unless explicitly required

## Avalonia Mapping Strategy

The following mapping should guide implementation.

### Use plain layout containers when the layer is only Host or Structure

- `Grid`
- `StackPanel`
- transparent `Border`

### Use shared styled surfaces when the layer is a standard Surface

- `prototype-interactive-surface`
- `prototype-search-shell`
- future `prototype-action-button`

### Use custom chrome controls when the layer is a high-fidelity chrome Surface

- `PrototypeTopTabChrome`
- future chrome primitives for any surface where ordinary `Border` semantics are insufficient

### Use theme overrides only after semantic ownership is clear

Do not start from framework defaults.

First decide:

- which semantic object owns the state
- which layer owns the hit target
- which layer owns the glow

Then neutralize default Avalonia templates if they conflict.

## Current Primitive Inventory

### Existing

- `PrototypeTopTabChrome`
- `prototype-interactive-surface`
- `prototype-search-shell`
- `prototype-search-input`
- `prototype-lift`
- `prototype-plain`
- `prototype-icon-button`
- `prototype-action-button`
- shared path icon styles

### Missing

- `prototype-inline-cluster`
- `prototype-session-surface`
- `prototype-drawer-action-group`
- `prototype-toolbar-trigger`
- `prototype-badge`

## Semantic Gaps That Must Stay Explicit

The following semantics are not fully defined yet.
Do not silently invent them during implementation.
When work touches one of these areas, stop and explicitly choose the semantic model first.

### 1. Parameter Editing Flow

Current state:

- reconnect parameters are functionally driven by plugin-provided editor UI
- shell now distinguishes between “open parameter editor” and “execute reconnect”

Still undefined:

- whether parameter editing is a drawer section, a modal flow, or a persistent session-side surface
- whether edited-but-unsaved parameters need explicit dirty-state semantics
- whether “last used parameters” and “current live session parameters” are distinct semantics

Reminder:

- any future reconnect UX change must first define the parameter state model before changing visuals

### 2. Toolbar Trigger Families

Current state:

- top-right notification and settings triggers exist
- left summary info trigger exists
- some triggers are bare icon actions, some are framed icon actions

Still undefined:

- exact semantic boundary between `bare trigger`, `icon button`, and `toolbar trigger`
- which trigger families own visible shells and which must remain visually bare

Reminder:

- if a new icon trigger is added, classify it first instead of copying a nearby button

### 3. Badge Semantics

Current state:

- workload default badge exists
- notification unread badge exists
- session count labels exist

Still undefined:

- distinction between metadata badge, count badge, state badge, and alert badge
- shared size, radius, typography, and priority rules for badge families

Reminder:

- do not implement new pills or badges ad hoc; first decide badge family

### 4. Inline Cluster Taxonomy

Current state:

- several places already use ad hoc icon + dot + text clusters

Still undefined:

- formal reusable primitives for:
  - status cluster
  - icon-label cluster
  - icon-value cluster
  - dot-label cluster

Reminder:

- alignment issues in rows, drawers, and tabs should push toward a formal inline-cluster primitive, not more one-off grids

### 5. Session Surface Variants

Current state:

- listener and connection rows are semantically closer now
- but they still share only part of the same primitive vocabulary

Still undefined:

- full variant model for:
  - listener surface
  - child connection surface
  - active summary surface
  - disconnected summary surface

Reminder:

- if session rows diverge again, define the variant family before styling

### 6. Drawer Section Model

Current state:

- session detail drawer now combines inspect content and reconnect parameter editing

Still undefined:

- standard section semantics for:
  - metadata section
  - status card
  - action group
  - editor section

Reminder:

- future drawers should compose from section semantics, not raw stacked cards

## Avalonia Implementation Rules

These rules should be applied whenever semantic objects are mapped into Avalonia.

### 1. Border is for ordinary surfaces, not precision chrome

- use `Border` for standard surfaces and panels
- do not use stacked `Border` layers to fake high-fidelity chrome if the object identity depends on exact edge behavior
- when border color, top accent, and radius must behave as one object, move to a custom chrome primitive

### 2. Container defaults are hostile to high-fidelity semantics

- `ListBoxItem`, `Button`, and framework theme templates may inject selection, hover, padding, or focus visuals
- neutralize defaults before attributing state to a semantic object

### 3. Motion must attach to the semantic owner

- hover lift belongs to the actual interactive surface or trigger
- never attach lift to transparent hosts or list containers

### 4. Content alignment should be solved structurally

- if dot, icon, and text must read as one object, build a cluster
- do not solve cluster alignment with extra margins on unrelated columns

### 5. Visible emphasis must stay local

- hover glow, selected border, and active fill must stay within the owning surface
- if emphasis bleeds outward, the wrong layer owns it

## Next Refactor Order

1. Define semantic owners for remaining problem areas before changing visuals.
2. Introduce missing primitives only after semantics are explicit.
3. Move current ad hoc nested borders into semantic primitives.
4. Re-run visual regression after each primitive migration.

## Working Rule

When a UI bug appears, ask this first:

“Which semantic layer owns this behavior?”

Do not ask:

“Which Avalonia property should I tweak first?”
