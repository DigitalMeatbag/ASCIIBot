# ASCIIBot - v2 Implementation Specification

> This specification translates the ASCIIBot v2 foundation document into concrete implementation requirements.

---

## 1. Purpose

ASCIIBot v2 improves the completed render artifact without changing the product identity. It remains a Discord bot that converts one user-supplied static image into ASCII-oriented output and returns the result in Discord.

v2 must optimize for:

- preserving color when output cannot fit inline
- returning previewable PNG render artifacts for non-inline output
- keeping plain `.txt` output available for copyability
- preserving the original image in successful responses when possible
- adding a bounded `detail` control that refines output without overriding `size`
- keeping v1 acknowledgement, validation, concurrency, failure, and tone guarantees

v2 must not introduce hosting, persistence, galleries, user accounts, moderation, analytics, batch processing, paginated PNG output, or general image editing.

---

## 2. Target Stack

| Concern | Decision |
|---|---|
| Runtime | .NET 10 |
| Process model | Long-lived console application using Microsoft.Extensions.Hosting |
| Discord library | Discord.Net |
| Image decoding and pixel processing | SixLabors.ImageSharp |
| PNG text rendering | SixLabors.ImageSharp with SixLabors.Fonts / ImageSharp.Drawing |
| Font | Bundled Cascadia Mono Regular |
| Deployment posture | Local-first and Docker-friendly |
| Persistence | None required for v2 |

The process should start through the .NET generic host, connect to Discord, register the slash command globally, and remain running until terminated.

The implementation should continue using dependency injection, options binding, and structured console logging.

---

## 3. Configuration

Configuration is supplied through environment variables.

| Variable | Required | Default | Purpose |
|---|---:|---|---|
| `ASCIIBot_DiscordToken` | yes | none | Discord bot token |
| `ASCIIBot_MaxGlobalJobs` | no | `3` | Maximum active jobs across the process |
| `ASCIIBot_MaxJobsPerUser` | no | `1` | Maximum active jobs per Discord user |
| `ASCIIBot_LogLevel` | no | `Information` | Minimum application log level |
| `ASCIIBot_AttachmentByteLimit` | no | `1000000` | Maximum generated `.txt` attachment size in bytes |
| `ASCIIBot_InlineCharacterLimit` | no | `2000` | Maximum inline Discord message characters, including formatting overhead |
| `ASCIIBot_RenderPngByteLimit` | no | `8388608` | Maximum generated PNG render size in bytes, 8 MiB |
| `ASCIIBot_TotalUploadByteLimit` | no | `12582912` | Maximum total bytes for all files attached to one completion response, 12 MiB |
| `ASCIIBot_RenderPngMaxWidth` | no | `4096` | Maximum generated PNG width in pixels |
| `ASCIIBot_RenderPngMaxHeight` | no | `4096` | Maximum generated PNG height in pixels |

The Discord token must never be hardcoded, committed, logged, or echoed in diagnostics.

Invalid optional configuration values should be rejected during startup with a clear operator-facing log message.

Byte-limit defaults use binary units. `8388608` bytes is 8 MiB and `12582912` bytes is 12 MiB. The existing 10 MiB source-image ceiling is inherited from v1.

The effective maximum PNG size in a non-inline response is constrained by both `ASCIIBot_RenderPngByteLimit` and the remaining total upload budget after the `.txt` attachment and any included original image are accounted for. Operators should set `ASCIIBot_TotalUploadByteLimit` high enough to cover the intended attachment combination, and at or below the upload capability of their Discord deployment.

---

## 4. Discord Command Surface

v2 exposes one global slash command:

```text
/ascii image:<attachment> [size] [color] [detail] [show_original]
```

### 4.1 Options

| Option | Required | Type | Values | Default |
|---|---:|---|---|---|
| `image` | yes | attachment | Discord attachment | none |
| `size` | no | enum string | `small`, `medium`, `large` | `medium` |
| `color` | no | enum string | `on`, `off` | `on` |
| `detail` | no | enum string | `low`, `normal`, `high` | `normal` |
| `show_original` | no | boolean | `true`, `false` | `true` |

The command should be registered globally. Guild-specific registration is not part of the v2 baseline.

### 4.2 Message Visibility

All v2 bot responses are public in-channel:

- acknowledgement
- completion
- inline ASCII output
- PNG render attachment
- `.txt` attachment
- original-image attachment when included
- validation failures
- rendering failures
- delivery failures
- busy-state rejections

No v2 response should be ephemeral.

### 4.3 Discord Intents and Permissions

The bot should request the minimum practical Discord capabilities required to:

- receive slash command interactions
- read command attachment metadata
- defer/respond to interactions
- post messages
- upload generated PNG and `.txt` attachments
- reattach the validated original image when requested and within delivery limits

The design must avoid requiring message content intent.

---

## 5. Interaction Flow

### 5.1 Successful Request

1. User invokes `/ascii`.
2. Bot validates that the command includes one image attachment.
3. Bot checks per-user and global concurrency limits.
4. Bot sends a public acknowledgement by deferring the interaction and posting the acknowledgement follow-up.
5. Bot downloads the attachment with a hard byte ceiling.
6. Bot validates the downloaded image.
7. Bot creates a rich render model using `size`, `color`, and `detail`.
8. Bot exports the render to the selected delivery format.
9. Bot returns inline output or attachments according to the delivery decision tree.
10. Bot releases the active job slot.

### 5.2 Acknowledgement Requirement

The bot must visibly acknowledge accepted work before conversion completes.

Preferred acknowledgement text:

```text
Request received. Processing has begun.
```

If processing takes more than 10 seconds after acknowledgement, the bot should edit the public acknowledgement follow-up when possible:

```text
Processing remains active.
```

Only one long-running status notice is required for v2.

If editing the acknowledgement is not possible, the bot may send one additional public status message instead.

---

## 6. Input Validation

v2 preserves v1 input validation.

### 6.1 Supported Formats

Supported static formats:

- PNG
- JPEG
- BMP
- GIF, single-frame only
- WebP, static only

Validation must be based on file contents, not filename extension alone.

### 6.2 Rejected Inputs

The bot must reject:

- missing attachment
- non-image attachment
- unsupported image type
- animated image
- source file larger than 10 MiB
- decoded image dimensions larger than 4096x4096
- image data that ImageSharp cannot decode
- requests that exceed concurrency limits

### 6.3 Download And Original Bytes

The bot must not intentionally download more than 10 MiB for a candidate input image.

If Discord attachment metadata reports a size above 10 MiB, reject before download.

If metadata is absent or unreliable, enforce a streaming download ceiling and reject once the limit is exceeded.

The validated downloaded image bytes should be retained for the duration of the request so the original image can be reattached when `show_original=true` and delivery limits allow it. The bot must not rely on Discord's original attachment URL for completion-response display.

---

## 7. Rich Render Model

v2 must introduce a canonical rich render model before export. Exporters must consume this model rather than each re-running image sampling independently.

Conceptual shape:

```text
RichAsciiRender
  Width: int
  Height: int
  Cells: RichAsciiCell[height][width]

RichAsciiCell
  Row: int
  Column: int
  Character: char
  Foreground: RgbColor
  Background: RgbColor? = null
```

Requirements:

- `RgbColor` is a non-premultiplied sRGB byte triple: `R`, `G`, and `B` are each `0..255`.
- `Width` and `Height` are visible character-grid dimensions.
- `Character` is the ASCII output character for the cell.
- `Foreground` is exact RGB color sampled from the source image or a monochrome foreground value.
- `Background` is optional and must be `null` for v2 baseline rendering.
- Missing `Background` means the exporter uses its default background.
- The same rich render model must feed inline ANSI, plain text, and PNG export.
- `detail` must affect the rich render model itself, so inline and attachment outputs are behaviorally consistent.

---

## 8. Rendering Model

### 8.1 Size Presets

The `size` option controls output budget and Discord footprint.

| Size | Target columns | Maximum lines | Use |
|---|---:|---:|---|
| `small` | 48 | 18 | compact chat-safe render |
| `medium` | 72 | 26 | default balanced render |
| `large` | 100 | 35 | largest inline-readable render |

The renderer must preserve source aspect ratio within the selected maximum dimensions.

Because terminal characters are taller than they are wide, the renderer must apply an aspect-ratio correction factor before sampling. The v2 default correction factor is `0.5`, matching v1.

Output grid calculation must use this formula:

```text
targetColumns = selected size target columns
maxRows = selected size maximum lines
aspectCorrection = 0.5

candidateRows = round_away_from_zero((sourceHeight / sourceWidth) * targetColumns * aspectCorrection)
candidateRows = clamp(candidateRows, 1, maxRows)

if candidateRows < maxRows:
  outputColumns = targetColumns
  outputRows = candidateRows
else:
  outputRows = maxRows
  outputColumns = round_away_from_zero((sourceWidth / sourceHeight) * (outputRows / aspectCorrection))
  outputColumns = clamp(outputColumns, 1, targetColumns)
```

All divisions in this formula are floating-point divisions. `round_away_from_zero` means rounding to the nearest integer, with midpoint values rounded away from zero; for non-negative render dimensions, it is equivalent to `floor(value + 0.5)`. This rule is required for deterministic tests.

### 8.2 Detail Presets

The `detail` option controls bounded refinement within the selected `size` budget.

| Detail | Sample window scale | Behavior |
|---|---:|---|
| `low` | `1.00` | Average the full cell region for smoother, simpler output |
| `normal` | `0.75` | Average a centered subset of the cell region for balanced output |
| `high` | `0.50` | Average a smaller centered subset of the cell region for sharper local variation |

The sample window scale is relative to the source-image region represented by a single output cell.

Rules:

- `detail` must not increase output columns, rows, PNG dimensions, or attachment limits.
- `size=small detail=high` still produces a small render.
- `detail` affects all export paths because it is applied before the rich render model is exported.
- `detail=normal` should preserve v1-like behavior as closely as practical.
- Numeric user-specified sampling ratios are deferred.

### 8.3 Sampling

The renderer should:

1. Decode and orient the source image when metadata allows it.
2. Determine the output grid from `size` and aspect-ratio correction.
3. Map each output cell to a source-image region.
4. Apply the `detail` sample window scale around the center of that region.
5. Compute representative luminance and RGB foreground color from the scaled sample window.
6. Choose the ASCII character from luminance.
7. Store the chosen character and foreground color in the rich render model.

If a scaled sample window would be smaller than one source pixel, sample at least one pixel.

Transparent source pixels must be composited against the default dark background for all rich-model sampling.

Sample window calculation must be deterministic:

```text
cellLeft = column * sourceWidth / outputColumns
cellRight = (column + 1) * sourceWidth / outputColumns
cellTop = row * sourceHeight / outputRows
cellBottom = (row + 1) * sourceHeight / outputRows

cellCenterX = (cellLeft + cellRight) / 2
cellCenterY = (cellTop + cellBottom) / 2

windowWidth = max(1.0, (cellRight - cellLeft) * detailScale)
windowHeight = max(1.0, (cellBottom - cellTop) * detailScale)

sampleLeft = clamp(cellCenterX - windowWidth / 2, 0, sourceWidth)
sampleRight = clamp(cellCenterX + windowWidth / 2, 0, sourceWidth)
sampleTop = clamp(cellCenterY - windowHeight / 2, 0, sourceHeight)
sampleBottom = clamp(cellCenterY + windowHeight / 2, 0, sourceHeight)
```

Average all source pixels whose pixel centers fall inside the sample window:

```text
sampleLeft <= x + 0.5 < sampleRight
sampleTop <= y + 0.5 < sampleBottom
```

If no pixel center falls inside the sample window, sample the single source pixel nearest to `(cellCenterX, cellCenterY)`, clamped to the image bounds.

### 8.4 Character Ramp

The default dark-to-light ramp remains:

```text
@%#*+=-:. 
```

The ramp maps darker pixels to denser characters and lighter pixels to sparser characters.

### 8.5 Luminance Calculation

Use perceptual luma for grayscale mapping:

```text
luma = 0.2126 * R + 0.7152 * G + 0.0722 * B
```

This is a simplified byte-space luma model used consistently by ASCIIBot. It does not require IEC 61966-2-1 sRGB gamma decoding before applying the weights.

---

## 9. Color And Background Model

### 9.1 Color Modes

| User option | Rich render behavior |
|---|---|
| `color=on` | Store sampled RGB foreground color for each cell |
| `color=off` | Store monochrome light foreground color for each cell |

### 9.2 Background

v2 PNG output uses a fixed dark terminal-style background:

```text
#0B0D10
```

Monochrome foreground color:

```text
#E6EDF3
```

Per-cell background rendering is deferred. The rich render model may include nullable background color, but v2 exporters should treat `null` background as "use the exporter default background."

### 9.3 ANSI Palette

Inline colored output continues to use Discord `ansi` code blocks with foreground colors only.

Supported ANSI colors use this pinned palette:

| Name | Code | RGB |
|---|---:|---|
| black | `30` | `0,0,0` |
| red | `31` | `170,0,0` |
| green | `32` | `0,170,0` |
| yellow | `33` | `170,170,0` |
| blue | `34` | `0,0,170` |
| magenta | `35` | `170,0,170` |
| cyan | `36` | `0,170,170` |
| white | `37` | `170,170,170` |
| bright black | `90` | `85,85,85` |
| bright red | `91` | `255,85,85` |
| bright green | `92` | `85,255,85` |
| bright yellow | `93` | `255,255,85` |
| bright blue | `94` | `85,85,255` |
| bright magenta | `95` | `255,85,255` |
| bright cyan | `96` | `85,255,255` |
| bright white | `97` | `255,255,255` |

Each rich render cell should be mapped to the nearest supported ANSI foreground color by squared Euclidean distance in non-premultiplied sRGB byte space:

```text
distance = (r - pr)^2 + (g - pg)^2 + (b - pb)^2
```

If two palette entries have the same distance, choose the first entry in the table order.

ANSI export should group consecutive characters with the same color to reduce escape-sequence overhead and reset formatting at the end of each colored render.

---

## 10. Export Formats

### 10.1 Plain Text Export

Plain text export must:

- contain only visible ASCII render characters and newlines
- contain no ANSI escape sequences
- preserve the rich render model's `Width` and `Height`
- be used for `.txt` attachments in non-inline delivery

### 10.2 Inline ANSI Export

Inline ANSI export must:

- use the rich render model
- map per-cell RGB foregrounds to Discord-supported ANSI foreground colors when `color=on`
- omit ANSI escape sequences when `color=off`
- fit inside the canonical inline character budget defined in Section 11.2

Format:

````text
```ansi
<ansi ascii render>
```
````

### 10.3 PNG Export

PNG export must:

- render a fixed-width terminal-like image
- use bundled Cascadia Mono Regular
- use the fixed dark background from Section 9
- render foreground text per cell
- render monochrome output as light text on the same dark background
- apply the PNG foreground contrast floor when `color=on`
- use predictable padding
- avoid decorative framing
- reject output that exceeds configured PNG pixel or byte limits

Default PNG rendering parameters:

| Parameter | Value |
|---|---:|
| Font size | `14 px` |
| Padding | `12 px` |
| Cell width | measured monospace advance, rounded up |
| Cell height | measured line height, rounded up |
| Background | `#0B0D10` |
| Monochrome foreground | `#E6EDF3` |

The generated PNG must not exceed:

- `ASCIIBot_RenderPngMaxWidth`
- `ASCIIBot_RenderPngMaxHeight`
- `ASCIIBot_RenderPngByteLimit`

PNG pagination is explicitly out of v2.

### 10.3.1 PNG Foreground Contrast

For PNG export only, sampled foreground colors should be adjusted to remain readable on the fixed dark background.

If a colored foreground's perceptual luma is below `96`, the PNG exporter must blend it toward the monochrome foreground color `#E6EDF3` until its luma is at least `96`.

Use the same luma formula as Section 8.5. Because luma is linear over RGB byte values for this specification, the blend factor is:

```text
t = (96 - sourceLuma) / (monochromeForegroundLuma - sourceLuma)
adjusted = round_away_from_zero((1 - t) * sourceRgb + t * monochromeForegroundRgb)
```

If `sourceLuma >= 96`, no adjustment is applied. This contrast adjustment affects only PNG drawing; it does not change the rich render model, ANSI export, or plain text export.

### 10.4 HTML Export

HTML export is deferred. If added later, it must escape generated content and must not treat arbitrary Discord-derived text as trusted markup.

---

## 11. Output Delivery

### 11.1 Delivery Decision Tree

Output delivery must use this branched decision tree:

1. Generate the rich render model from the request.
2. Generate plain text from the rich render model.
3. Determine inline dimension eligibility by checking only visible columns and visible lines against Section 11.2 thresholds.

If the render is dimension-eligible for inline delivery:

4. Generate exactly one inline payload:
   - if `color=on`, generate the ANSI inline payload
   - otherwise, generate the monochrome inline payload
5. Test the canonical inline character budget from Section 11.2 using the completion text that will be posted.
6. If the inline payload fits and `show_original=false`, post the inline completion response.
7. If the inline payload fits and `show_original=true`, include the validated original image bytes when upload limits allow it.
8. If `show_original=true` and the original image cannot fit, append the terse omission note to the completion text and re-test the canonical inline character budget.
9. If the inline payload still fits with the omission note, post the inline completion response without the original image.

Enter the non-inline path when any of these conditions is true:

- the render is not dimension-eligible for inline delivery
- the inline payload exceeds `ASCIIBot_InlineCharacterLimit`
- the inline completion text plus omission note would exceed `ASCIIBot_InlineCharacterLimit`
- ANSI export fails while the rich render model and plain text export remain usable

Non-inline delivery must then use this order:

10. If plain text exceeds `ASCIIBot_AttachmentByteLimit`, reject with output-too-large failure.
11. Generate PNG from the rich render model.
12. If PNG exceeds PNG limits, reject with output-too-large failure.
13. Compose non-inline attachments: render PNG and `.txt`.
14. If `show_original=true`, attempt to include the validated original image bytes.
15. If the combined size of all attachments exceeds `ASCIIBot_TotalUploadByteLimit`, omit the original image and include a terse omission note.
16. If render PNG plus `.txt` still exceed `ASCIIBot_TotalUploadByteLimit`, reject with output-too-large failure.
17. Post completion response with attachments.

With the v2 size presets and grid formula, the inline dimension gate is expected to pass for normal generated renders because `large` is capped at 100 columns and 35 lines, matching the inline visible-size thresholds. The gate remains explicit as a guard for future presets, configuration changes, and implementation errors.

### 11.2 Inline Output

Use inline output only when all thresholds are satisfied:

- no more than 100 visible columns
- no more than 35 visible lines
- no more than `ASCIIBot_InlineCharacterLimit` total Discord message characters, including code fences and ANSI escape sequences

Canonical inline character count:

```text
inlineCharacters =
  length(completionText)
  + length("\n```ansi\n")
  + length(inlinePayload)
  + length("\n```")
```

`inlinePayload` is the ANSI-escaped render when `color=on` and the plain render when `color=off`. The `ansi` code fence is used for both colored and monochrome inline output for presentation consistency.

The completion text used in this formula must be the exact completion text that will be sent. If an original-image omission note is appended, the inline character count must be recalculated with that note included before the message is posted.

Preferred completion text:

```text
Rendering complete.
```

If `show_original=true`, the inline response should include the original image attachment when `ASCIIBot_TotalUploadByteLimit` allows it. The inline render remains the primary generated artifact.

For inline responses, the original image fits when its validated byte length is less than or equal to `ASCIIBot_TotalUploadByteLimit`, since the original image is the only attachment in the inline response.

v2 does not retry as monochrome inline when a colored ANSI payload exceeds the inline character limit. Oversized colored inline payloads fall through to the PNG plus `.txt` path. If ANSI export fails but the rich render model and plain text export are usable, the bot should also fall through to PNG plus `.txt` rather than fail the whole request.

### 11.3 Non-Inline Output

If inline output is not eligible, return:

- completion text
- one generated PNG render attachment
- one `.txt` attachment containing the plain text render
- original image attachment only when `show_original=true` and delivery limits allow it

Preferred completion text:

```text
Rendering complete. Output has been attached.
```

If the original image was requested or default-enabled but omitted due to delivery limits, append a terse procedural note:

```text
Original image display was omitted due to delivery limits.
```

The bot must not silently drop the generated `.txt` attachment in a successful non-inline response.

### 11.4 Attachment Names

Use stable generated filenames:

```text
asciibot-render.png
asciibot-render.txt
asciibot-original.<ext>
```

The original extension should be derived from validated content type, not the submitted filename alone.

Use this mapping:

| Validated type | Extension |
|---|---|
| PNG | `.png` |
| JPEG | `.jpg` |
| BMP | `.bmp` |
| GIF | `.gif` |
| WebP | `.webp` |

### 11.5 Attachment Composition Priority

When delivery limits constrain a successful non-inline response:

1. Preserve generated render PNG.
2. Preserve generated `.txt`.
3. Include original image only if the response remains within upload limits.
4. Reject if generated render PNG plus generated `.txt` cannot fit.

### 11.6 Delivery Failures

If attachment upload fails because the original image makes the response too large, the bot may retry once without the original image and include the omission note.

This retry is allowed only for upload-size or payload-too-large failures from Discord when the original image was included. It must not be used for permission failures, validation failures, transient server errors, network failures, or generated artifact failures.

If generated render artifacts fail to upload, the request is considered failed.

Preferred delivery failure text:

```text
The rendered output could not be delivered. Processing has failed.
```

v2 must not split one render across multiple Discord messages.

---

## 12. Original Image Display

`show_original` controls successful completion responses.

Rules:

- Default is `true`.
- Original image display is skipped for all failure responses.
- Original image display uses the validated downloaded bytes.
- The original image is omitted first when attachment limits are exceeded.
- Omission due to delivery limits should be noted in completion text.
- Omission because `show_original=false` must not emit an omission note.

The original image is contextual. The generated render remains the primary artifact.

---

## 13. Concurrency

v2 preserves v1 concurrency behavior.

Limits:

- one active job per user
- three active jobs globally by default

The bot should check limits before downloading the image when possible.

Job slots must be released in all terminal paths:

- success
- validation rejection after slot acquisition
- rendering failure
- delivery failure
- cancellation
- unexpected exception

---

## 14. Failure Messages

Bot language should remain formal, precise, affectless, and impersonal.

v2 uses "rendering failure" for the v1 "conversion failure" condition.

| Condition | Public response |
|---|---|
| Missing image | `No image attachment was submitted. Processing has been rejected.` |
| Unsupported file type | `The submitted file type is not supported. Processing has been rejected.` |
| Animated image | `Animated images are not supported in this version. Processing has been rejected.` |
| Source file too large | `The submitted image exceeds the maximum source file size. Processing has been rejected.` |
| Dimensions too large | `The submitted image exceeds the maximum supported dimensions. Processing has been rejected.` |
| Decode failure | `The submitted image could not be decoded. Processing has been rejected.` |
| Rendering failure | `The submitted image could not be rendered. Processing has failed.` |
| Output too large | `The rendered output exceeds delivery limits. Processing has been rejected.` |
| Permission failure | `The rendered output could not be delivered due to insufficient permissions. Processing has failed.` |
| Delivery failure | `The rendered output could not be delivered. Processing has failed.` |
| Per-user busy state | `A request from this user is already being processed. Please resubmit after it has completed.` |
| Global busy state | `Processing capacity has been reached. Please resubmit later.` |
| Unknown failure | `Processing failed due to an internal error.` |

Error messages should not include stack traces, exception details, tokens, local paths, or raw Discord API payloads.

---

## 15. Logging

Minimum useful events:

- startup
- missing or invalid required configuration
- Discord connection ready
- slash command registration success/failure
- request accepted
- request rejected with reason
- selected size, color, detail, and show-original options
- rich render completed, including output columns and rows
- delivery mode selected: inline, PNG plus text, or rejection
- original image omitted due to delivery limits
- delivery failure
- unexpected exception

Logs should include Discord IDs where useful for operation, but not user display names as the primary identifier.

The bot token and image binary content must never be logged.

---

## 16. Suggested Internal Structure

v2 should continue using the multi-project solution boundary established in v1:

| Project | Purpose |
|---|---|
| `ASCIIBot` | Console-hosted Discord bot implementation |
| `ASCIIBot.Tests` | Automated tests for rendering, validation, output decisions, and concurrency behavior |

Suggested components:

| Component | Responsibility |
|---|---|
| `Program` | startup, configuration, dependency wiring |
| `BotOptions` | environment-backed settings |
| `AsciiInteractionModule` | slash command declaration and response flow |
| `ImageDownloadService` | bounded attachment download and original-byte retention |
| `ImageValidationService` | content-based format, animation, size, and dimension checks |
| `AsciiRenderService` | rich render model generation |
| `AnsiColorService` | nearest-palette mapping and escape grouping |
| `PlainTextExportService` | `.txt` export from rich render model |
| `PngRenderService` | PNG export from rich render model |
| `OutputDeliveryService` | delivery decision tree and Discord delivery |
| `ConcurrencyGate` | per-user and global active-job enforcement |

This structure is guidance, not a requirement, but equivalent boundaries should exist.

---

## 17. Testing Requirements

v2 should include focused automated tests for behavior that does not require live Discord access.

Required test coverage:

- v1 validation, delivery, and concurrency tests remain passing
- v1 rendering tests remain passing except where explicitly superseded by v2 rich-render, detail, PNG, or transparent-pixel behavior
- `detail` enum parsing and defaults
- detail sample-window behavior for `low`, `normal`, and `high`
- `detail` does not change output grid dimensions for any `size`
- output grid calculation with aspect-ratio correction
- deterministic sample-window pixel inclusion and nearest-pixel fallback
- rich render model dimensions and cell contents
- plain text export from rich render model
- ANSI export from rich render model
- ANSI palette values, RGB distance metric, and tie-breaking
- PNG export dimensions for each size preset
- PNG byte-limit rejection
- PNG pixel-dimension rejection
- dark background and monochrome foreground rendering configuration
- PNG foreground contrast floor
- transparent pixel sampling behavior
- canonical inline character count
- inline delivery decision before attachment delivery
- non-inline PNG plus `.txt` delivery decision
- original image filename extension mapping from validated content type
- original image included with inline output when enabled and within limits
- original image included with non-inline output when enabled and within limits
- original image omitted when disabled
- no omission note when original image is disabled by user option
- original image omitted first when upload limits are exceeded
- omission note emitted when original image is omitted due to delivery limits
- rejection when generated PNG plus `.txt` exceed upload limits
- failure responses do not include original image

Live Discord command testing may be manual for v2.

---

## 18. Acceptance Criteria

v2 is complete when:

- `/ascii image:<attachment> [size] [color] [detail] [show_original]` is globally registered.
- v1 input validation behavior is preserved.
- Every accepted request receives a public acknowledgement before rendering completes.
- The renderer produces a rich render model before export.
- `size` controls output budget and preserves v1 preset behavior.
- `detail=low`, `detail=normal`, and `detail=high` produce distinct bounded sampling behavior.
- `detail` does not cause output to exceed the selected `size` grid.
- Inline output still works for eligible renders.
- Oversized inline output returns a PNG render attachment plus `.txt`.
- `color=on` non-inline output returns a color PNG plus `.txt`.
- `color=off` non-inline output returns a monochrome PNG plus `.txt`.
- PNG output uses a fixed dark terminal-style background.
- Per-cell background rendering is not required for v2.
- Original image display defaults to enabled for successful responses.
- Original image display reattaches validated bytes rather than relying on source URLs.
- Original image display is omitted first when delivery limits require it.
- Generated render artifacts are never silently dropped from successful non-inline responses.
- Paginated PNG output is not implemented.
- The bot rejects output that cannot fit one inline message or one non-inline attachment response.
- All user-visible language follows the cold, efficient bureaucratic tone defined in the foundation.

---

## 19. Deferred Features

The following are explicitly out of v2:

- paginated PNG output
- HTML export
- attached ANSI files as a primary delivery mode
- per-cell background rendering
- user-selectable background modes
- numeric sample-size controls
- custom character ramps
- reply-to-image command flow
- message context-menu command
- passive image watching
- multi-image batch mode
- collage generation
- animated image frame extraction
- image editing controls
- persistence or user profiles
- web UI
- analytics
- moderation workflows

---

*Last updated: 2026-05-02*
