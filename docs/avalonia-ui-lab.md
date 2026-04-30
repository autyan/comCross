# Avalonia UI Lab

## Status

- `UILab` project lifecycle is complete
- the standalone lab application has been retired from the solution
- retained value lives in this document, the exported references, and [shell-ui-semantics.md](/home/autyan/SourceCode/comCross/docs/shell-ui-semantics.md:1)
- all future UI implementation work should land directly in `src/Shell`

## Goal

This lab exists to replicate the Figma Make prototype in a standalone Avalonia 11 project before touching the production shell.

This document focuses on visual replication and Avalonia rendering rules.
The semantic composition model that should drive Shell refactors is documented separately in:

- `docs/shell-ui-semantics.md`

Primary reference inputs:

- `references/figma-make-export/`
- `references/figma-make-export/DESIGN_SYSTEM.md`
- `references/figma-make-export/src/styles/theme.css`
- Figma Make file: `https://www.figma.com/make/cvJZkNuw5fFOeLSJoQ9LI3/SukperCom_CrossPlatform?p=f&t=BDOP7HXo9kDjgZ0L-0`

The acceptance target for the lab is the rendered result, not the apparent correctness of XAML or token definitions.

## Current Baseline

- Lab project: `src/UILab`
- Target framework: `.NET 8`
- Avalonia version: `11.2.2`
- Shell version alignment: same Avalonia major/minor line as `src/Shell`

Why this baseline:

- The production shell is on Avalonia 11.
- Visual experiments need to be directly portable back into the shell.
- Avalonia 12 introduces documented breaking changes and API churn that would contaminate the visual experiment with migration noise.

## What The Lab Covers

First-pass replica scope:

- Workload tab strip
- Activity rail
- Session summary and session list
- Message stream header, search row, and log rows
- Right send panel
- Bottom status bar
- Quick-create flow
- Settings overlay
- Session detail drawer and reconnect entry

Shared infrastructure:

- Design tokens in `src/UILab/Assets/Styles/DesignTokens.axaml`
- Control overrides in `src/UILab/Assets/Styles/ControlThemes.axaml`
- State-to-visual mapping converters in `src/UILab/Converters/UiLabConverters.cs`
- Avalonia-ready icon geometry imported from the Figma Make export

## Avalonia Rendering Notes

These are the constraints already confirmed during setup.

1. Template output from Avalonia 12 must not be trusted for an Avalonia 11 project.
   The generated app used `net10.0`, `Avalonia 12.0.1`, and `WithDeveloperTools()`. All of that had to be corrected.

2. Avalonia 11 XAML syntax is less forgiving than the latest templates imply.
   `ColumnSpacing` and some newer Path property usage had to be removed for the lab to compile cleanly on `11.2.2`.

3. Default control visuals are a real source of drift.
   The lab keeps reusable visual rules in explicit token and control style dictionaries so we can isolate how much of the final look comes from Avalonia defaults versus our own definitions.

4. Browser CSS effects should be treated as intent, not implementation.
   Gradients, glows, and subtle interaction polish from the React export should be mapped to stable Avalonia equivalents instead of copied mechanically.

5. Final verification must be render-based.
   A successful build is only a syntax checkpoint. Visual sign-off still requires running the lab and comparing the rendered output with the Figma Make target.

## Next Work

- Run the lab in a desktop session and compare against the prototype screenshot.
- Measure the highest-variance areas: tab geometry, list density, message row typography, and right-dock spacing.
- Decide whether IBM Plex Sans and JetBrains Mono should be vendored into the repo for deterministic typography.
- Promote proven tokens and component rules into the production shell only after the lab matches the prototype closely enough.

## Review Verdict

The lab is now sufficient as a first design regression harness for the production shell.

Why it is sufficient:

- It covers the primary shell chrome: top workload bar, left activity rail, left navigation content, center content, right dock, and bottom status bar.
- It covers the three interaction layers that matter most for Avalonia styling work: base page, modal overlay, and right-side drawer.
- It covers the main visual systems that are hardest to port from React/CSS into Avalonia: dark surfaces, layered borders, cold glow highlights, icon stroke rendering, and compact data-dense layouts.
- It covers the highest-value interaction families: tab switch, list selection, expandable tree, send action, advanced option reveal, quick create flow, settings overlay, and session detail drawer.

This means the lab is good enough to drive Shell styling decisions without guessing.

## Remaining Gaps

The current lab is not a complete product simulator. It is a focused visual and interaction harness.

The remaining gaps are second-order rather than blocking:

- Notification overlay is not yet represented as a standalone scene.
- Focus mode and panel collapse states are not represented.
- Empty state, long-list stress state, and narrow-width stress state are not yet part of the regression matrix.
- Drawer content still represents the reconnect branch in a compact form rather than a full production editor.

These gaps should be treated as future coverage expansion, not as blockers for extracting design rules.

## Regression Matrix

The following states should be treated as the minimum visual regression set when migrating into Shell.

1. Base sessions view
   Includes top tabs, activity rail, left session list, center message stream, right send dock, bottom status bar.

2. Quick-create view
   Verifies that the left rail can swap sidebar content without disturbing the main shell grid.

3. Settings overlay
   Verifies centered modal layout, dimmed backdrop, content framing, and layered z-order.

4. Session detail drawer
   Verifies right-edge drawer behavior, summary card styling, primary/secondary action hierarchy, and reconnect-entry flow.

5. Advanced options expanded
   Verifies collapsible density, inline controls, and motion timing in the send dock.

6. Active session and active listener states
   Verifies selection border language, subtle glow overlays, status dot hierarchy, and icon emphasis.

7. Message stream filtering and send flow
   Verifies search bar spacing, row rhythm, TX/RX color semantics, and scroll-to-end behavior.

## Design Rules

### Layout

- The shell is a five-zone composition: top bar, left rail, left content panel, center content panel, right tool dock, plus a persistent bottom bar.
- Left rail width is fixed and should never participate in content resizing.
- Left content and right dock are bounded side panels; the center content must remain the dominant visual plane.
- Overlays do not replace shell structure. They sit above it while preserving spatial context.
- Drawers attach to the right edge and should feel like an extension of the shell, not a separate window.

### Density

- Use compact heights consistently: `32` for tabs and compact controls, `34-36` for actions, `38-40` for list rows, `42` for message rows.
- Prefer narrow vertical rhythm with crisp separators over large card spacing.
- In data-heavy surfaces, typography and spacing should carry structure before color does.

### Color and Surfaces

- The visual stack uses three primary dark surfaces: app background, panel background, and card background.
- Borders are always present and low-contrast; they define edges more than shadows do.
- Accent blue is reserved for action, active borders, and active icon emphasis.
- Accent cyan is reserved for status-success and live-data emphasis.
- Error red is used sparingly and should remain isolated to destructive or disconnected actions.

### Glow and Effects

- Treat glow as a layered highlight, not as a blur-first effect.
- Prefer a combination of:
  - active border
  - low-opacity surface overlay
  - thin highlight edge
- For selected list rows in Avalonia, use a three-layer model:
  - outer cold-blue halo with negative margin
  - inner soft overlay tied to the selected surface
  - 1px top edge highlight
- Avoid relying on large-radius blur or heavy drop shadow for core state communication.
- If a glow can be removed without breaking state recognition, the glow is correctly secondary.
- Hover and active are separate signals. Hover may add a low background lift and slight upward motion, but it must not replace or overpower the active state.
- Active list-row emphasis must remain inside the row surface. Avoid negative-margin glow layers that spill into the row container unless the target explicitly requires an outer halo.

### Typography

- Sans text carries navigation, labels, and actions.
- Mono text is reserved for endpoints, message payloads, and transport-oriented values.
- Font size hierarchy should stay tight:
  - `10-11` for metadata
  - `12` for standard controls and values
  - `13-16` for headings and key stats

### Icons

- Icons must be treated as a system, not per-control decoration.
- Small icons should use a consistent `24x24` design space and then scale down in a controlled container.
- Stroke icons in Avalonia need explicit `StrokeLineCap` and `StrokeJoin` to avoid web-to-native drift.
- Prefer shared `Path`-based stroke icon styles over `PathIcon` when fidelity at `14-18px` matters.
- Reuse shared geometries such as `ServerIcon`, `NetworkIcon`, `CableIcon`, `BellIcon`, `SettingsIcon` instead of re-drawing per surface.
- If a geometry degrades at small sizes, replace it with a handcrafted small-size variant rather than forcing the original path.

### Search Controls

- Search bars should be built as a shell container plus a borderless inner `TextBox`, not as a default `TextBox` with an overlaid icon.
- The shell owns border, fill, radius, fixed height, and left icon slot.
- The inner input owns only text behavior, watermark, and focus handling.
- Prototype-aligned center-search widths should be explicit. Do not let Avalonia content measurement shrink them to placeholder width.

### Tabs

- Top workload tabs need an explicit full-width indicator layer attached to the tab surface, not to the text stack.
- In Avalonia grids, the active top border must span the full visual tab width. Do not rely on implicit column placement for the indicator.
- Tab chrome and tab content should be separate layers:
  - base tab surface
  - active top indicator
  - content row
- Adjacent workload tabs need explicit spacing. Do not let neighboring 1px borders visually merge into a single strip.
- Badge pills inside tabs must not define tab height. The tab surface owns vertical padding and final height; the badge stays compact and center-aligned.
- Avoid duplicating active-state semantics. If active dot plus active top border already communicate the current workload, do not add an extra current badge.

### Session List

- Listener rows and child connection rows should both use the shared selectable-row language.
- Do not draw connector lines unless they are visually necessary in the final render. They tend to create stray line artifacts in dense Avalonia trees.
- Prefer shared transport icons from the icon system instead of per-row custom canvases. This reduces drift and alignment bugs between listener and connection rows.
- If a dedicated current-session summary card already exists, do not add an extra textual header block above the session list. It wastes vertical space and breaks parity with the prototype.
- For selectable listener rows, keep the glow stack to halo plus overlay unless the top edge highlight is visually verified. A 1px highlight line often reads as a rendering artifact in Avalonia.

## Motion Rules

- Hover feedback should be positional first: a subtle upward shift is preferred over opacity flicker or generic shadow inflation.
- Expand/collapse motion should animate opacity plus height or max-height, never only visibility.
- Modal and drawer motion should reinforce spatial origin:
  - settings overlay fades in over the shell
  - session detail enters from the right edge
- Motion should remain short and structural. Recommended range in this lab: `120-180ms`.

## Avalonia Implementation Rules

- Do not trust default control templates for pixel work. Use explicit style dictionaries and plain button templates where precision matters.
- Encapsulate visual state in brushes, converters, and small reusable layout patterns rather than embedding ad hoc values everywhere.
- When a UI bug repeats across multiple surfaces, first promote it into a shared primitive:
  - icon stroke style
  - search shell
  - selectable row shell
  - active tab indicator shell
- Pointer interaction should be opt-in and shared. Use a reusable hover-lift surface for interactive objects, and disable that lift when the object is already active or selected.
- For states that exist in React as box-shadow or blur, first ask what information that effect is conveying. Recreate the information, not the CSS.
- Validate with rendered output, not with XAML neatness.

## Migration Guidance For Shell

When moving these patterns into the real shell, preserve this order:

1. Migrate tokens and icon rules.
2. Migrate primitive control styles and button templates.
3. Migrate shell chrome layout.
4. Migrate stateful list items and message rows.
5. Migrate overlays and drawers last.

This order keeps Shell changes reviewable and prevents Avalonia default styles from leaking back into already-validated surfaces.
