# ASCIIBot - v2 Foundation Document

> This is a living document for ASCIIBot v2. It captures direction, product principles, open questions, and early decisions before they are translated into a formal v2 implementation specification.

---

## Purpose

v1 proved the core ASCIIBot loop: a Discord user submits an image, the bot acknowledges the request, converts the image into ASCII-oriented output, and returns the result in Discord.

v2 should build on that working foundation without changing the project's identity. ASCIIBot remains a small, Discord-native image-to-ASCII bot. The purpose of v2 is to improve output fidelity, delivery quality, and user control where v1 has already exposed real limits.

The first known v2 pressure point is color preservation for large output. v1 can produce colored inline `ansi` code blocks when the result fits within Discord message limits, but falls back to a plain `.txt` attachment for larger renders. That fallback preserves the text, but loses color.

---

## Product Philosophy

**Keep the core loop simple, but make completed renders feel more worth sharing.**

v2 should not become a general image editor, hosting service, gallery, or broad media pipeline. The bot should still feel procedural, focused, and understandable. Improvements should concentrate on the quality and delivery of the rendered artifact.

The guiding question for v2 is:

> Once the bot has produced a good render, how can it return that render in the most useful, faithful, Discord-friendly form?

---

## v2 Goals

- Preserve color for large output that cannot fit inline.
- Allow the response to include the original submitted image in the same response context.
- Add a controlled way to adjust render detail beyond the coarse v1 size presets.
- Keep `.txt` output available as a plain, copyable fallback.
- Introduce a richer internal output model that can support multiple export formats.
- Prefer Discord-native presentation where possible.
- Avoid requiring users to leave Discord for the primary viewing experience.
- Keep v1's acknowledgement, validation, and failure transparency guarantees.
- Leave room for additional v2 features without expanding scope prematurely.

---

## v2 Non-Goals

- Not a hosted web gallery
- Not a persistent user-account system
- Not a general-purpose image editing tool
- Not a bot framework or plugin host
- Not a moderation or analytics product
- Not a replacement for the plain text ASCII render
- Not a requirement to make every exported format equally primary

---

## Output And Color Direction

### Problem

Discord message limits are too small for many useful ASCII renders. v1 handles this by returning oversized renders as `.txt` attachments. That is readable and durable, but it discards the visible color that made inline `ansi` output more expressive.

Attached ANSI files are not a sufficient primary answer because Discord does not render attached ANSI as colored chat output. Raw escape sequences are also surprising to casual users.

### v2 Direction

The preferred v2 answer is to render large colored output as an image attachment, likely PNG, while still attaching the plain `.txt` render for copyability.

Recommended delivery tendency:

| Output size | Primary delivery | Secondary delivery |
|---|---|---|
| Small | Inline Discord `ansi` code block when color is enabled | None required |
| Medium | Inline if it fits comfortably; otherwise image attachment | Plain `.txt` when not inline |
| Large | Inline if it fits; otherwise full-color PNG attachment | Plain `.txt` attachment when not inline |
| Extremely large | Clear rejection | Plain `.txt` only if a future delivery mode supports it |

This makes PNG the primary full-color fallback for Discord viewing, not a replacement for text output.

When a render is delivered as an attachment rather than inline text, v2 should include a plain `.txt` attachment whenever the generated text is within file limits. This keeps copyable text available consistently across non-inline delivery paths.

v2 should not introduce paginated PNG delivery. If a render cannot fit within the single-image delivery limits selected for v2, the bot should reject it with a clear visible failure. Paginated PNG output can be reconsidered later as a dedicated feature.

---

## Rich Render Model

v2 should introduce a canonical internal representation for render output before export. Instead of treating the final render as only a string, the renderer should produce structured cells or spans with text and color information.

Conceptual model:

```text
render
  width: 100
  height: 35
  cells:
    row: 0
    column: 0
    char: "@"
    foreground: #d7d7d7
    background: optional
```

This model can then export to multiple delivery formats:

- plain text `.txt`
- Discord inline `ansi`
- full-color PNG
- optional HTML
- optional ANSI file for power users

The important design boundary is that export formats should be downstream of one render model. Color decisions should not be trapped inside the Discord inline formatter.

---

## PNG Export

PNG should be the primary v2 mechanism for preserving large visual output inside Discord. Color output is the motivating case, but monochrome PNG remains useful when `color=off` because it gives Discord a previewable render while `.txt` preserves copyable text.

Expected qualities:

- Fixed-width terminal-like layout
- Monospace font
- Dark background by default
- Foreground color per cell
- Optional background color in the render model, but no per-cell background rendering by default
- Stable row height and column width
- Predictable padding
- No decorative framing that competes with the ASCII art
- Output dimensions bounded enough for Discord preview usability

If a render is too tall or wide for one useful image, v2 should reject it rather than paginate it.

Example attachment set:

```text
asciibot-render.png
asciibot-render.txt
```

### Open Rendering Questions

- Which font should be embedded or used by default?
- Should the renderer use ImageSharp drawing primitives, SkiaSharp, or another library?
- What maximum PNG width, height, and byte size should be allowed?

---

## Background Policy

v2 PNG output should use a fixed dark terminal-style background by default.

Foreground color should carry the image color when `color=on`. In monochrome mode, the PNG should use a light foreground on the same dark background.

Per-cell background color should be structurally possible in the rich render model, but deferred as a rendered behavior. By default, a missing or empty cell background means "use the PNG renderer's default background."

### Rationale

A fixed dark background keeps the output readable and preserves the identity of the result as ASCII art. Per-cell background colors can preserve more source-image color, but they also risk making the output behave more like a pixel-art mosaic than text art. They can also reduce contrast when foreground and background colors are sampled from similar regions.

Starting with foreground color on a fixed background gives v2 the best first behavior: Discord-friendly, readable, terminal-like, and simpler to test. The internal model still leaves room for per-cell backgrounds later if they prove useful.

### Transparency

Transparent source pixels should render against the same default dark background unless a future background option explicitly changes that behavior.

Potential future background controls remain deferred:

```text
background: dark | light | transparent
```

---

## HTML Export

HTML is a useful archival or power-user format, but should not be the primary Discord viewing experience.

Potential benefits:

- Preserves full color
- Allows selectable text
- Can support richer presentation than `.txt`
- Opens in a browser without requiring specialized terminal support

Limitations:

- Discord users must open the file externally.
- It is less immediate than an image preview.
- It introduces more surface area for sanitization and escaping.
- Future HTML export must carefully escape all generated content and avoid treating arbitrary Discord-derived text as trusted markup.

v2 can defer HTML export until PNG delivery is working, unless another v2 feature makes HTML more urgent.

---

## Original Image Display

v2 should allow users to choose whether the original submitted image is displayed in the bot's completion response context.

The recommended default is `true`.

### Rationale

Showing the original image gives the ASCII render immediate context. Users can compare the source and output without scrolling back to the command invocation or opening the original attachment separately. This is especially useful once v2 returns PNG output, because the response can become a compact before-and-after artifact.

Defaulting to visible also fits the casual Discord use case: most users are sharing the conversion as a visible result, not trying to minimize attachments.

### Proposed Behavior

- Add an optional command control such as `show_original` or `include_original`.
- Default the option to `true`.
- When enabled, include the original image in the same completion response context as the rendered output.
- When disabled, omit the original image and return only the generated render artifacts.
- When included, reattach the validated original image bytes rather than relying on Discord's original attachment URL.
- Preserve all existing validation rules; rejected images should not be echoed back as part of a failure response.
- If validation succeeds but rendering or delivery fails, report only the failure and do not include the original image.

### Presentation Guidance

The original image should be treated as context, not as the primary result. The generated ASCII render remains the main artifact.

For Discord delivery, the bot should prefer the simplest reliable display mechanism:

- attach or embed the original image when Discord can preview it cleanly
- keep completion text short and procedural
- avoid adding captions or explanatory copy unless required for clarity
- avoid echoing the source if doing so would exceed attachment limits or make the response fail

### Open Questions

- Should the option name be `show_original`, `include_original`, or something shorter?
- Should this option name be locked during the v2 foundation stage, or deferred to the implementation specification?

---

## Render Detail And Sampling Density

v2 should include a user-facing enum control for how much source image area is represented by each ASCII character.

The clearest user-facing term is likely **detail**. Internally, the concept can be described as **sample cell size**, **sampling density**, or **pixels per character**.

Avoid using **pixel density** as the primary term. In graphics contexts, pixel density usually means display density such as DPI or PPI, not how aggressively an image is downsampled into ASCII cells.

### Concept

Every ASCII output character represents a region of the source image. Within a selected `size` budget, higher detail should preserve more local variation inside the same output grid, while lower detail should simplify more aggressively inside that grid.

Conceptual examples:

| Setting | Meaning | Result |
|---|---|---|
| Lower detail | More aggressive simplification within the selected size budget | simpler output at the selected size |
| Normal detail | Balanced sampling within the selected size budget | default output at the selected size |
| Higher detail | More local variation preserved within the selected size budget | more detailed output at the selected size |

### Recommended User Model

The user should not have to think in literal pixel ratios. A simple enum control is easier to understand and matches the v1 `size` option style:

```text
detail: low | normal | high
```

Possible future advanced controls remain deferred:

```text
columns: 80
sample_size: 6
```

The v1 `size` option already controls output dimensions at a coarse level. v2 should preserve `size` and add `detail` as a separate enum-style control.

The clean mental model is:

```text
size = output budget / Discord footprint
detail = bounded refinement within that budget
```

`size` owns the maximum output grid and delivery expectations. It determines the approximate columns, rows, and resulting artifact footprint. `detail` must not override that budget. Instead, it should tune how aggressively the renderer samples, sharpens, preserves local variation, or chooses characters within the grid allowed by `size`.

This means `size=small detail=high` should still produce a small render. It should mean "produce the most detailed small render the selected budget can support," not "produce a larger render."

### Character Aspect Ratio

ASCII characters are usually taller than they are wide when rendered in a monospace font. Because of this, a naive one-pixel-to-one-character mapping will often stretch images vertically.

The renderer should continue accounting for character aspect ratio when translating source image dimensions into output rows and columns. Any `detail` or sampling control must preserve that correction.

### Open Questions

- Should the public option be named `detail`, `density`, `resolution`, or something else?
- What exact enum values should be exposed: `low | normal | high`, `coarse | normal | fine`, or another set?
- Should the implementation target output columns first, then derive rows, or target a source-pixels-per-character ratio internally while enforcing the selected size budget?

---

## Delivery Policy

v2 should treat output delivery as a format-selection problem rather than a binary inline-or-text decision.

Proposed priority:

1. Use inline Discord `ansi` output when the render is small enough and color is enabled.
2. Use inline plain text when the render is small enough and color is disabled.
3. Use PNG plus `.txt` when the render is too large for inline delivery but within attachment limits.
4. Reject with a clear visible failure if no supported delivery path can carry the result.

The plain `.txt` attachment remains required for non-inline render delivery whenever it is within file limits. It preserves copyable text and provides a fallback for clients or users that do not want image output. PNG remains useful for both color and monochrome output because it gives Discord a previewable visual artifact.

Original image display is orthogonal to render format selection. When enabled, the original image should be included only after the bot has selected a viable delivery path for the generated render and only for successful completion responses.

If the full successful response would exceed Discord attachment count or upload-size limits, v2 should preserve generated render artifacts before optional context. The response should first omit the original image, then proceed with the render PNG and `.txt` if they fit. The completion message should include a terse procedural note when the original image was requested or enabled but omitted due to delivery limits. The original image is optional even when enabled; generated render delivery is the core obligation. If the render PNG plus `.txt` cannot fit, the bot should reject with a clear visible failure rather than silently dropping the `.txt` or returning an incomplete generated result.

Inline eligibility should be checked before attachment delivery is selected. The implementation specification should make this decision tree explicit so inline thresholds, PNG limits, `.txt` limits, and rejection behavior are evaluated predictably.

---

## Compatibility With v1

v2 should preserve these v1 behaviors unless explicitly superseded:

- `/ascii image:<attachment> [size] [color]` remains the baseline command.
- v2 may add narrowly scoped optional command controls, such as original image display.
- `size` remains the primary output budget control; `detail` may refine output only within that selected budget.
- Accepted requests receive visible acknowledgement.
- Validation failures are visible and plain.
- Monochrome output remains supported.
- `.txt` fallback remains supported.
- The bot does not silently split chat messages.
- The bot remains hobby-scale and Discord-first.
- The bot retains its cold, efficient bureaucratic response tone.

---

## Open v2 Idea Queue

This section is intentionally reserved for additional v2 ideas before they are promoted into decisions.

Candidate areas:

- Output formats and delivery
- Render quality controls
- Size and aspect-ratio options
- Alternate invocation flows
- Better progress or completion messaging
- Operator configuration
- Deployment and observability
- Failure wording and UX polish
- Paginated PNG delivery

New ideas should be added here first with enough context to discuss tradeoffs, then moved into decision sections once direction is clear.

---

## Must Close Before Spec

The following decisions must be closed before a v2 implementation specification is considered ready:

- Exact `detail` enum values and defaults
- Whether `detail` affects all render outputs uniformly, including inline text, or only generated attachment formats
- Precise interaction rules for `size`, `detail`, aspect-ratio correction, and output grid calculation
- PNG renderer library choice
- Embedded or default monospace font choice
- Maximum PNG pixel dimensions and byte-size limits
- Exact attachment composition limits for render PNG, `.txt`, and original image under Discord upload constraints
- Final command option names for original image display and render detail

These are spec-blocking because they affect public command behavior, output dimensions, delivery success, and testability.

---

## Decision Log

### Full-Color Large Output

**Decision:** v2 should prefer a full-color PNG attachment for large colored output that cannot fit inside Discord's message limits. The bot should also include a plain `.txt` attachment for copyability and fallback.

**Reasoning:** PNG is the most Discord-native way to preserve full-color visual output for large renders. `.txt` alone loses color, attached ANSI files do not render as colored Discord output, and HTML is less immediate because it requires opening an external file.

### Non-Inline Text Fallback

**Decision:** Any render delivered outside inline Discord text should include a plain `.txt` attachment whenever the generated text is within file limits.

**Reasoning:** Non-inline delivery should consistently preserve copyable text. PNG gives the best Discord viewing experience, but `.txt` remains the durable textual artifact.

### Attachment Composition Priority

**Decision:** For successful non-inline responses, generated render artifacts take priority over optional original image display. If the render PNG, `.txt`, and original image cannot all fit within Discord attachment limits, v2 should omit the original image first and note the omission in the completion message. If the generated render PNG plus `.txt` cannot fit, the bot should reject with a clear visible failure.

**Reasoning:** The generated render is the product's core output, while the original image is contextual. Dropping the original image preserves the user's requested conversion without surprising them by omitting generated artifacts. A terse note avoids silent option degradation. Rejecting when the generated artifacts cannot fit is clearer than silently returning a partial or non-copyable result.

### Monochrome PNG Output

**Decision:** When `color=off` and the render is too large for inline delivery, v2 should still return a monochrome PNG alongside the `.txt` attachment when possible.

**Reasoning:** The motivation for PNG delivery is not only color preservation; it is also Discord-native previewability. A monochrome PNG gives users an immediate visual result, while `.txt` preserves the plain text form.

### Paginated PNG Deferral

**Decision:** v2 should not include paginated PNG output. Renders that cannot fit within v2's selected single-image delivery limits should be rejected with a clear visible failure.

**Reasoning:** Pagination is meaningfully more complex than single-image attachment delivery. It introduces row splitting, attachment ordering, Discord attachment-count limits, naming, partial-failure handling, and extra UX decisions. Single PNG plus `.txt` captures the main v2 value while keeping the first implementation focused.

### Background Rendering

**Decision:** v2 PNG output should use a fixed dark terminal-style background by default. Foreground color should be sampled per cell when color is enabled, while per-cell background rendering remains deferred.

**Reasoning:** A fixed background protects readability and keeps the result recognizable as ASCII art. Per-cell backgrounds can make output more image-faithful, but they can also weaken the text-art effect and create contrast problems. Keeping background support optional in the rich model leaves room for a future mode without complicating the v2 baseline.

### Rich Output Representation

**Decision:** v2 should introduce a canonical rich render model containing characters and color information before exporting to Discord inline text, `.txt`, PNG, or other future formats.

**Reasoning:** A structured render model keeps color and text fidelity independent from any one delivery format. It prevents the implementation from baking color decisions into Discord-specific ANSI formatting and makes future exports easier to add.

### HTML Export Priority

**Decision:** HTML export is useful but should be considered secondary to PNG delivery for v2's first color-preserving fallback.

**Reasoning:** HTML can preserve color and selectable text, but it does not display as naturally inside Discord. PNG better serves the primary share-in-chat experience.

### Original Image Display

**Decision:** v2 should add an optional control for including the original submitted image in the completion response context, defaulting to enabled. The original image should be reattached from the validated downloaded bytes rather than displayed from Discord's original attachment URL. The original image should not be included in validation, rendering, or delivery failure responses.

**Reasoning:** Including the source image makes successful responses self-contained and gives users immediate before-and-after context. Defaulting to enabled matches the normal Discord sharing flow, while an opt-out keeps responses lighter when users only want the generated artifact. Reattaching the validated bytes is more reliable than relying on Discord CDN attachment URLs, which may expire. Failure responses should stay procedural and avoid unnecessary attachment spam.

### Render Detail Terminology

**Decision:** v2 should add an enum-style `detail` control for source-image-to-ASCII granularity. Internal documentation may use `sample cell size`, `sampling density`, or `pixels per character`.

**Reasoning:** `detail` describes the user's intended outcome without requiring image-processing vocabulary and matches the approachable enum pattern used by the v1 `size` option. `Pixel density` is likely to confuse the concept with DPI or display density, while `sampling density` and `pixels per character` are useful implementation terms but less friendly as slash-command options.

### Size And Detail Interaction

**Decision:** `size` is the output budget and Discord footprint control. `detail` is bounded refinement within the selected `size`. `detail` must not cause the output to exceed the grid or delivery budget implied by `size`.

**Reasoning:** The two controls overlap unless their ownership is explicit. Keeping `size` responsible for output dimensions preserves the v1 mental model, while `detail` gives users a controlled way to tune rendering quality without surprising them with a larger artifact.

---

## Next Pass Focus

Capture the remaining v2 ideas, then separate them into:

- committed v2 goals
- deferred ideas
- open questions
- implementation-spec candidates

Once the idea set is stable, this foundation can feed a dedicated v2 implementation specification.

---

*Last updated: 2026-05-02*
