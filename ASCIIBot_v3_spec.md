# ASCIIBot - v3 Implementation Specification

> This specification translates the ASCIIBot v3 foundation document into concrete implementation requirements.

---

## 1. Purpose

ASCIIBot v3 adds bounded animated GIF and animated WebP support without changing the core product identity.

ASCIIBot remains a Discord bot that converts one user-supplied image into ASCII-oriented output and returns the result in Discord.

v3 must optimize for:

- accepting animated GIF and animated WebP inputs when they meet defined limits
- producing previewable animated ASCII output as animated WebP
- preserving all v2 static-image behavior unless explicitly superseded
- keeping animation processing deterministic and bounded
- rejecting unsupported or excessive animated inputs clearly
- failing visibly when accepted processing breaks
- preserving the cold, efficient bureaucratic response tone
- staying local-first, hobby-scale, and Discord-native

v3 must not introduce:

- general video support
- MP4, MOV, WebM, APNG, or audio support
- frame-by-frame editing
- animation effects or compositing features beyond decoding source animation semantics
- automatic static fallback for animated inputs
- adaptive degradation when animation limits are exceeded
- multi-image batch mode
- hosted gallery, persistence, accounts, analytics, moderation, or web UI

The v3 baseline is intentionally a first bounded pass at animation support. Predictability takes priority over broad compatibility or maximum animation fidelity.

---

## 2. Target Stack

v3 preserves the v2 stack and extends it for animated WebP output.

| Concern | Decision |
|---|---|
| Runtime | .NET 10 |
| Process model | Long-lived console application using Microsoft.Extensions.Hosting |
| Discord library | Discord.Net |
| Static image decoding and pixel processing | SixLabors.ImageSharp |
| Animated image inspection and compositing | SixLabors.ImageSharp, verified during implementation |
| Static PNG text rendering | SixLabors.ImageSharp with SixLabors.Fonts / ImageSharp.Drawing |
| Animated WebP rendering and encoding | SixLabors.ImageSharp WebP encoder, verified during implementation |
| Font | Bundled Cascadia Mono Regular |
| Deployment posture | Local-first and Docker-friendly |
| Persistence | None required for v3 |

The process should start through the .NET generic host, connect to Discord, register the slash command globally, and remain running until terminated.

The implementation should continue using:

- dependency injection
- options binding
- structured console logging
- clear separation between Discord interaction code, validation, rendering, exporting, delivery, and concurrency

Animated WebP output through the selected .NET image stack is a required implementation verification item. If the selected image stack cannot produce reliable animated WebP output, v3 cannot be considered complete under this specification.

---

## 3. Configuration

Configuration is supplied through environment variables.

### 3.1 Common Configuration

v3 preserves v2 configuration behavior unless explicitly superseded. The total upload budget is common to both static and animated completion responses in v3.

| Variable | Required | Default | Purpose |
|---|---:|---|---|
| `ASCIIBot_DiscordToken` | yes | none | Discord bot token |
| `ASCIIBot_MaxGlobalJobs` | no | `3` | Maximum active jobs across the process |
| `ASCIIBot_MaxJobsPerUser` | no | `1` | Maximum active jobs per Discord user |
| `ASCIIBot_LogLevel` | no | `Information` | Minimum application log level |
| `ASCIIBot_SourceImageByteLimit` | no | `10485760` | Maximum downloaded source image size in bytes, 10 MiB |
| `ASCIIBot_MaxDecodedImageWidth` | no | `4096` | Maximum decoded image canvas width |
| `ASCIIBot_MaxDecodedImageHeight` | no | `4096` | Maximum decoded image canvas height |
| `ASCIIBot_AttachmentByteLimit` | no | `1000000` | Maximum generated `.txt` attachment size in bytes for static non-inline output |
| `ASCIIBot_InlineCharacterLimit` | no | `2000` | Maximum inline Discord message characters, including formatting overhead |
| `ASCIIBot_RenderPngByteLimit` | no | `8388608` | Maximum generated PNG render size in bytes, 8 MiB |
| `ASCIIBot_RenderPngMaxWidth` | no | `4096` | Maximum generated PNG width in pixels |
| `ASCIIBot_RenderPngMaxHeight` | no | `4096` | Maximum generated PNG height in pixels |
| `ASCIIBot_TotalUploadByteLimit` | no | `10000000` | Maximum total bytes for all files attached to one completion response |

v3 changes the default total upload budget to `10,000,000` bytes for both static and animated responses. This supersedes the v2 default total upload budget and keeps default behavior conservative for ordinary Discord upload limits.

### 3.2 Animation Configuration

| Variable | Required | Default | Purpose |
|---|---:|---|---|
| `ASCIIBot_AnimationMaxDurationMs` | no | `12000` | Maximum accepted source animation duration |
| `ASCIIBot_AnimationMaxOutputFrames` | no | `48` | Maximum sampled output frames |
| `ASCIIBot_AnimationTargetSampleIntervalMs` | no | `100` | Target interval used to derive sampled output frame count |
| `ASCIIBot_AnimationMinFrameDelayMs` | no | `100` | Minimum emitted output frame delay |
| `ASCIIBot_AnimationWebPByteLimit` | no | `8388608` | Maximum generated animated WebP byte size, 8 MiB |
| `ASCIIBot_AnimationMaxOutputCells` | no | `300000` | Maximum `outputWidth * outputHeight * sampledFrameCount` |
| `ASCIIBot_AnimationMaxSourceFrames` | no | `1000` | Defensive source-frame safety fuse |

The animated WebP raster dimensions should use the same effective image-dimension ceiling as static PNG output unless implementation testing shows animation-specific constraints are needed. In the v3 baseline, the generated animated WebP frame dimensions must not exceed:

- `ASCIIBot_RenderPngMaxWidth`
- `ASCIIBot_RenderPngMaxHeight`

### 3.3 Configuration Rules

The Discord token must never be:

- hardcoded
- committed
- logged
- echoed in diagnostics
- included in user-facing messages

Invalid optional configuration values should be rejected during startup with a clear operator-facing log message.

Invalid values include:

- non-numeric values for numeric limits
- zero or negative byte limits
- zero or negative frame limits
- zero or negative duration or delay values
- maximum decoded width or height less than 1
- total upload byte limit less than 1

The implementation may warn if `ASCIIBot_AnimationMaxOutputFrames` is greater than `ASCIIBot_AnimationMaxSourceFrames`, but it must not reject startup solely for that condition. Duplicate sampled frames are allowed, and raw source-frame count is not the primary animated acceptance model.

The implementation may enforce additional startup validation when a configuration combination cannot produce a viable response.

Byte-limit defaults intentionally mix inherited binary-unit source and generated-artifact limits with a decimal total upload budget.

---

## 4. Discord Command Surface

v3 exposes one global slash command:

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

v3 adds no animation-specific command options.

The command should be registered globally. Guild-specific registration is not part of the v3 baseline.

### 4.2 Automatic Animation Routing

The user does not choose static or animated rendering.

Routing is automatic:

| Submitted content | v3 behavior |
|---|---|
| Static supported image | Use v2 static rendering and delivery |
| Animated GIF within v3 limits | Use v3 animated rendering and delivery |
| Animated WebP within v3 limits | Use v3 animated rendering and delivery |
| Animated GIF/WebP outside v3 limits | Reject visibly |
| Unsupported animated or video format | Reject visibly |

### 4.3 Message Visibility

All v3 bot responses are public in-channel:

- acknowledgement
- completion
- inline static ASCII output
- static PNG render attachment
- static `.txt` attachment
- animated WebP render attachment
- original-image attachment when included
- validation failures
- rendering failures
- encoding failures
- delivery failures
- busy-state rejections

No v3 response should be ephemeral.

### 4.4 Discord Intents and Permissions

The bot should request the minimum practical Discord capabilities required to:

- receive slash command interactions
- read command attachment metadata
- defer/respond to interactions
- post messages
- upload generated PNG, text, WebP, and original-image attachments
- reattach the validated original image when requested and within delivery limits

The design must avoid requiring message content intent.

---

## 5. Interaction Flow

### 5.1 Common Request Flow

1. User invokes `/ascii`.
2. Bot validates that the command includes one attachment.
3. Bot checks per-user and global concurrency limits.
4. Bot sends a public acknowledgement by deferring the interaction and posting the acknowledgement follow-up.
5. Bot downloads the attachment with a hard byte ceiling.
6. Bot validates the downloaded file by content.
7. Bot routes the request to the static or animated pipeline.
8. Bot renders and exports according to the selected pipeline.
9. Bot returns the completion response or a visible rejection or failure response.
10. Bot releases the active job slot.

### 5.2 Static Request Flow

For static inputs, v3 preserves the v2 flow:

1. Validate supported static image type.
2. Decode and orient the image when metadata allows it.
3. Create one `RichAsciiRender` using `size`, `color`, and `detail`.
4. Export to inline ANSI, PNG, and/or plain text according to the static delivery decision tree.
5. Include original image when `show_original=true` and delivery limits allow it.

### 5.3 Animated Request Flow

For animated GIF or animated WebP inputs:

1. Validate supported animated type.
2. Inspect animation metadata and timing.
3. Enforce source byte, canvas dimension, duration, and source-frame safety limits.
4. Verify that reliable fully composited frames can be obtained.
5. Determine output grid from the decoded animation canvas and selected `size`.
6. Derive sampled output frame count using the v3 sampling algorithm.
7. Enforce the total-output-cell cost fuse.
8. Select sampled source frames using deterministic uniform time sampling.
9. Render each sampled composited frame into a `RichAsciiRender`.
10. Assemble an `AnimatedAsciiRender`.
11. Export the animated render to animated WebP.
12. Enforce animated WebP byte limits and total upload limits.
13. Return animated WebP attachment and optional original image when allowed.
14. Release the active job slot.

### 5.4 Acknowledgement Requirement

The bot must visibly acknowledge accepted work before conversion completes.

Preferred acknowledgement text:

```text
Request received. Processing has begun.
```

Implementation requirement:

- Use Discord's deferred interaction response mechanism.
- The deferred response must be public.
- Send a public follow-up acknowledgement immediately after deferral.
- The acknowledgement must be sent before rendering or encoding completes.

### 5.5 Long-Running Work

If processing takes more than 10 seconds after acknowledgement, the bot should edit the public acknowledgement follow-up when possible:

```text
Processing remains active.
```

Only one long-running status notice is required for v3.

If editing the acknowledgement is not possible, the bot may send one additional public status message instead.

The long-running notice must remain procedural and impersonal.

---

## 6. Input Validation

v3 accepts exactly one image per request.

Validation must be based on file contents, not filename extension alone.

### 6.1 Supported Formats

Supported static formats:

- PNG
- JPEG
- BMP
- GIF, single-frame
- WebP, static

Supported animated formats:

- GIF, animated
- WebP, animated

Rejected by default:

- MP4
- MOV
- WebM
- APNG
- AVIF animation
- TIFF
- SVG
- PDF
- ZIP archives
- files that are not decodable supported image content

PNG inputs containing APNG animation chunks must be rejected as unsupported animated content in the v3 baseline rather than silently treated as static PNG.

### 6.2 Attachment Presence

If no attachment is supplied, reject publicly:

```text
No image attachment was submitted. Processing has been rejected.
```

### 6.3 Download Limit

The bot must not intentionally download more than `ASCIIBot_SourceImageByteLimit` for a candidate image.

Rules:

- If Discord attachment metadata reports a size above the configured source byte limit, reject before download.
- If metadata is absent or unreliable, enforce a streaming download ceiling.
- If the streaming download exceeds the limit, stop reading and reject.
- The bot must not log image binary content.

Preferred rejection text:

```text
The submitted image exceeds the maximum source file size. Processing has been rejected.
```

### 6.4 Content-Based Type Detection

The bot must determine image type from file contents.

Filename extension may be used only for:

- user-visible filename preservation when safe
- diagnostics
- fallback extension selection after validated content type

Filename extension alone must not allow unsupported or mislabeled content through validation.

### 6.5 Decoded Dimension Limit

Decoded static image dimensions or animated canvas dimensions must not exceed:

- `ASCIIBot_MaxDecodedImageWidth`
- `ASCIIBot_MaxDecodedImageHeight`

Preferred rejection text:

```text
The submitted image exceeds the maximum supported dimensions. Processing has been rejected.
```

### 6.6 Static Versus Animated Detection

For GIF and WebP:

- one-frame content routes to the static path
- multi-frame content routes to the animated path
- if frame count cannot be determined but animation confidence is required, inspect conservatively
- if the implementation cannot confidently determine static vs animated behavior, reject rather than silently misroute

Static GIF and static WebP behavior from v2 must remain unchanged.

Animated GIF and animated WebP are no longer rejected merely because they are animated.

### 6.7 Decode Failure

If the submitted image cannot be decoded as a supported image, reject:

```text
The submitted image could not be decoded. Processing has been rejected.
```

This message is used for invalid or corrupt image data. Runtime exceptions after validation should be classified according to the reject/fail policy in Section 20.

---

## 7. Static Rich Render Model

v3 preserves the v2 `RichAsciiRender` model.

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

- `RgbColor` is a non-premultiplied sRGB byte triple.
- `R`, `G`, and `B` are each `0..255`.
- `Width` and `Height` are visible character-grid dimensions.
- `Character` is the ASCII output character for the cell.
- `Foreground` is exact RGB color sampled from the source image or a monochrome foreground value.
- `Background` is optional and must be `null` for v3 baseline rendering.
- Missing `Background` means the exporter uses its default background.
- The same rich render model must feed static inline ANSI, plain text, PNG export, and animated frame export.
- `detail` must affect the rich render model itself so all export paths are behaviorally consistent.

---

## 8. Static Rendering Model

v3 preserves the v2 static rendering model.

### 8.1 Size Presets

The `size` option controls output budget and Discord footprint.

| Size | Target columns | Maximum lines | Use |
|---|---:|---:|---|
| `small` | 48 | 18 | compact chat-safe render |
| `medium` | 72 | 26 | default balanced render |
| `large` | 100 | 35 | largest inline-readable render |

The renderer must preserve source aspect ratio within the selected maximum dimensions.

Because terminal characters are taller than they are wide, the renderer must apply an aspect-ratio correction factor before sampling.

Default correction factor:

```text
aspectCorrection = 0.5
```

Output grid calculation:

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

All divisions are floating-point divisions.

`round_away_from_zero` means rounding to the nearest integer, with midpoint values rounded away from zero. For non-negative render dimensions, it is equivalent to:

```text
floor(value + 0.5)
```

This rule is required for deterministic tests.

### 8.2 Detail Presets

The `detail` option controls bounded refinement within the selected `size` budget.

| Detail | Sample window scale | Behavior |
|---|---:|---|
| `low` | `1.00` | Average the full cell region for smoother, simpler output |
| `normal` | `0.75` | Average a centered subset of the cell region for balanced output |
| `high` | `0.50` | Average a smaller centered subset of the cell region for sharper local variation |

Rules:

- `detail` must not increase output columns, rows, PNG dimensions, WebP dimensions, or attachment limits.
- `size=small detail=high` still produces a small render.
- `detail` affects all export paths because it is applied before the rich render model is exported.
- `detail=normal` should preserve v2-like behavior as closely as practical.
- Numeric user-specified sampling ratios are deferred.

### 8.3 Cell Sampling

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

If no pixel center falls inside the sample window, sample the single source pixel nearest to `(cellCenterX, cellCenterY)`, clamped to image bounds.

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

v3 preserves the v2 color and background model for static renders and uses the same visual model for animated WebP frames.

### 9.1 Color Modes

| User option | Rich render behavior |
|---|---|
| `color=on` | Store sampled RGB foreground color for each cell |
| `color=off` | Store monochrome light foreground color for each cell |

### 9.2 Background

PNG and animated WebP output use a fixed dark terminal-style background:

```text
#0B0D10
```

Monochrome foreground color:

```text
#E6EDF3
```

Per-cell background rendering is deferred.

The rich render model may include nullable background color, but v3 exporters should treat `null` background as:

```text
use the exporter default background
```

### 9.3 ANSI Palette

Inline static colored output continues to use Discord `ansi` code blocks with foreground colors only.

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

If two palette entries have the same distance, choose the first entry in table order.

ANSI export should group consecutive characters with the same color to reduce escape-sequence overhead and reset formatting at the end of each colored render.

### 9.4 Raster Foreground Contrast

For PNG export and animated WebP frame rendering, sampled foreground colors should be adjusted to remain readable on the fixed dark background.

If a colored foreground's perceptual luma is below `96`, the raster exporter must blend it toward the monochrome foreground color `#E6EDF3` until its luma is at least `96`.

Use the same luma formula as Section 8.5.

Because luma is linear over RGB byte values for this specification, the blend factor is:

```text
t = (96 - sourceLuma) / (monochromeForegroundLuma - sourceLuma)
adjusted = round_away_from_zero((1 - t) * sourceRgb + t * monochromeForegroundRgb)
```

If `sourceLuma >= 96`, no adjustment is applied.

This contrast adjustment affects raster drawing only. It does not change the rich render model, ANSI export, or plain text export.

---

## 10. Static Export Formats

v3 preserves v2 static export behavior.

### 10.1 Plain Text Export

Plain text export must:

- contain only visible ASCII render characters and newlines
- contain no ANSI escape sequences
- preserve the rich render model's `Width` and `Height`
- be used for `.txt` attachments in static non-inline delivery

### 10.2 Inline ANSI Export

Inline ANSI export must:

- use the rich render model
- map per-cell RGB foregrounds to Discord-supported ANSI foreground colors when `color=on`
- omit ANSI escape sequences when `color=off`
- fit inside the canonical inline character budget

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
- use the fixed dark background
- render foreground text per cell
- render monochrome output as light text on the same dark background
- apply the raster foreground contrast floor when `color=on`
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

PNG pagination remains out of scope.

### 10.4 HTML Export

HTML export is deferred. If added later, it must escape generated content and must not treat arbitrary Discord-derived text as trusted markup.

---

## 11. Static Output Delivery

v3 preserves v2 static delivery behavior.

### 11.1 Static Delivery Decision Tree

Static output delivery must use this decision tree:

1. Generate the rich render model.
2. Generate plain text from the rich render model.
3. Determine inline dimension eligibility by checking visible columns and visible lines against inline thresholds.

If the render is dimension-eligible for inline delivery:

4. Generate exactly one inline payload:
   - if `color=on`, generate the ANSI inline payload
   - otherwise, generate the monochrome inline payload
5. Test the canonical inline character budget using the completion text that will be posted.
6. If the inline payload fits and `show_original=false`, post the inline completion response.
7. If the inline payload fits and `show_original=true`, include the validated original image bytes when upload limits allow it.
8. If `show_original=true` and the original image cannot fit, append the omission note to the completion text and re-test the inline character budget.
9. If the inline payload still fits with the omission note, post the inline completion response without the original image.

Enter the static non-inline path when any of these conditions is true:

- the render is not dimension-eligible for inline delivery
- the inline payload exceeds `ASCIIBot_InlineCharacterLimit`
- the inline completion text plus omission note would exceed `ASCIIBot_InlineCharacterLimit`
- ANSI export fails while the rich render model and plain text export remain usable

Static non-inline delivery must then use this order:

10. If plain text exceeds `ASCIIBot_AttachmentByteLimit`, reject with the output-too-large rejection message.
11. Generate PNG from the rich render model.
12. If PNG exceeds PNG limits, reject with the output-too-large rejection message.
13. Compose static non-inline attachments: render PNG and `.txt`.
14. If `show_original=true`, attempt to include the validated original image bytes.
15. If all attachments exceed `ASCIIBot_TotalUploadByteLimit`, omit the original image and include the omission note.
16. If render PNG plus `.txt` still exceed `ASCIIBot_TotalUploadByteLimit`, reject with the output-too-large rejection message.
17. Post completion response with attachments.

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

Preferred completion text:

```text
Rendering complete.
```

### 11.3 Static Non-Inline Output

If static inline output is not eligible, return:

- completion text
- one generated PNG render attachment
- one `.txt` attachment containing the plain text render
- original image attachment only when `show_original=true` and delivery limits allow it

Preferred completion text:

```text
Rendering complete. Output has been attached.
```

If the original image was requested or default-enabled but omitted due to delivery limits, append:

```text
Original image display was omitted due to delivery limits.
```

The bot must not silently drop the generated `.txt` attachment in a successful static non-inline response.

---

## 12. Animated Inspection And Composition

Animated GIF and animated WebP inputs require reliable inspection before rendering.

### 12.1 Required Inspection Data

The implementation must be able to determine:

- validated animated format
- decoded animation canvas width and height
- raw source-frame count or safe bounded frame enumeration
- source-frame presentation timestamps
- source-frame delays or equivalent timing information
- total source animation duration
- whether reliable fully composited frames can be produced

If any required inspection data cannot be determined, reject:

```text
The submitted animation could not be inspected. Processing has been rejected.
```

### 12.2 Duration Calculation

The source animation duration is the sum of source-frame display durations as decoded from the file's animation metadata.

The source duration must be positive.

Reject if:

```text
sourceDuration <= 0
```

Preferred message:

```text
The submitted animation could not be inspected. Processing has been rejected.
```

Reject if:

```text
sourceDuration > ASCIIBot_AnimationMaxDurationMs
```

Preferred message:

```text
The submitted animation exceeds the maximum supported duration. Processing has been rejected.
```

A duration exactly equal to the configured maximum is allowed.

### 12.3 Source-Frame Safety Fuse

Raw source-frame count is not the primary product acceptance model, but it is a defensive operational fuse.

If metadata exposes source-frame count, reject before full rendering when:

```text
sourceFrameCount > ASCIIBot_AnimationMaxSourceFrames
```

If source-frame count is not directly exposed, the implementation may enumerate frames up to the configured fuse plus one.

Reject when the enumerated count exceeds the configured fuse.

Preferred message:

```text
The submitted animation exceeds processing limits. Processing has been rejected.
```

If safe bounded frame enumeration is not possible, reject as inspection failure.

### 12.4 Composited Frame Requirement

Animated input must yield reliable fully composited frames before rendering.

Requirements:

- Sampling operates only on full composited image frames.
- Raw frame deltas must not be rendered directly.
- Disposal behavior must not be guessed.
- Transparency behavior must not be guessed.
- If the selected image stack cannot represent the animation semantics reliably enough for rendering, reject.
- No best-effort compositing is attempted in the v3 baseline.

Reject as inspection failure when:

- animation metadata cannot be inspected
- frame timing cannot be determined
- frame enumeration is unreliable
- composited frames cannot be produced
- frame decoding fails during inspection or composition
- disposal or transparency semantics cannot be represented reliably

Preferred message:

```text
The submitted animation could not be inspected. Processing has been rejected.
```

### 12.5 Animation Canvas Dimensions

Animated validation uses the decoded animation canvas dimensions, not individual raw frame rectangles.

Reject when:

```text
canvasWidth > ASCIIBot_MaxDecodedImageWidth
canvasHeight > ASCIIBot_MaxDecodedImageHeight
```

Preferred message:

```text
The submitted image exceeds the maximum supported dimensions. Processing has been rejected.
```

---

## 13. Animated Sampling Algorithm

v3 uses deterministic uniform time sampling.

### 13.1 Sampling Inputs

Given:

```text
sourceDuration
targetInterval = ASCIIBot_AnimationTargetSampleIntervalMs
maxFrames = ASCIIBot_AnimationMaxOutputFrames
minFrameDelay = ASCIIBot_AnimationMinFrameDelayMs
```

### 13.2 Output Frame Count

Compute:

```text
frameCount = min(maxFrames, floor(sourceDuration / targetInterval))
```

If:

```text
sourceDuration < targetInterval
```

then:

```text
frameCount = 1
```

After calculation, `frameCount` must be at least `1`.

### 13.3 Sample Times

Sample timestamps are distributed uniformly across the source animation duration.

The implementation should calculate sample times in `TimeSpan` ticks or an equivalent integer time unit:

```text
sampleTimeTicks[i] = floor(i * sourceDurationTicks / frameCount)
for i = 0 .. frameCount - 1
```

Rules:

- The first sample time is always `0`.
- The final source frame is not forced into the output.
- Sample-time conversion uses floor rounding to avoid producing a timestamp at or beyond the source duration.
- Sampling must be deterministic across runs for the same input and configuration.

### 13.4 Source Frame Selection

For each sample timestamp:

- select the composited source frame with the nearest presentation timestamp
- if two source frames are equally near, select the earlier source frame
- duplicate selected source frames are allowed
- duplicate-frame collapse is not part of the v3 baseline

A source frame's presentation timestamp is the start timestamp of that frame in the decoded source animation timeline.

This v3 baseline intentionally selects by nearest source-frame start timestamp. Selecting the frame active at each sample time may be more semantically faithful for animations with long irregular holds, but that behavior is deferred.

### 13.5 Single-Frame Sampled Animations

When `frameCount = 1`, the sampled output contains one animated frame.

The frame's emitted duration is:

```text
max(sourceDuration, minFrameDelay)
```

This applies to short animations and to any accepted animation whose configured sampling calculation yields one output frame.

The result is still treated as animated input for delivery purposes:

- animated WebP output is used
- no inline static output is generated
- no `.txt` output is generated
- no automatic static fallback occurs

---

## 14. Animated Timing Normalization

Output frame durations are reconstructed from the sampling grid.

### 14.1 Multi-Frame Duration Calculation

For sampled animations where `frameCount > 1`:

```text
outputDuration[i] = sampleTime[i + 1] - sampleTime[i]
for i = 0 .. frameCount - 2
```

For the last output frame:

```text
outputDuration[last] = outputDuration[last - 1]
```

Then apply the configured minimum frame delay:

```text
outputDuration[i] = max(outputDuration[i], minFrameDelay)
```

### 14.2 Timing Guarantees

v3 guarantees:

- output timing is deterministic
- output timing is derived from the sampling grid
- delays below the configured minimum are clamped upward
- the source animation's approximate duration is preserved when clamping does not stretch the output

v3 does not guarantee:

- exact source frame pacing
- preservation of source-frame delays
- preservation of source repeat count
- exact preservation of total duration after clamping
- frame dropping to preserve duration
- redistribution of timing after clamping

### 14.3 Timing Stretch

If delay clamping causes the output animation to become longer than the source animation, the stretched duration is accepted.

No additional normalization pass is required.

---

## 15. Animated Cost Model

v3 uses a preflight output-cell cost fuse.

### 15.1 Output Grid

Before rendering sampled frames, determine the animated output grid using:

- decoded animation canvas width and height
- selected `size`
- the same aspect-ratio correction and grid formula used for static rendering

All sampled frames in one animated render must share this grid.

### 15.2 Total Output Cells

Compute:

```text
totalOutputCells = outputWidth * outputHeight * sampledFrameCount
```

Reject if:

```text
totalOutputCells > ASCIIBot_AnimationMaxOutputCells
```

Preferred message:

```text
The submitted animation exceeds processing limits. Processing has been rejected.
```

### 15.3 No Adaptive Degradation

If the cost fuse is exceeded, v3 must not automatically:

- reduce frame count
- reduce render size
- lower detail
- lower color fidelity
- lower WebP quality
- switch to GIF
- return first-frame static output
- return a partial animation

The request is rejected.

---

## 16. Animated Render Model

v3 introduces an animated render model downstream of sampling and upstream of WebP export.

```text
AnimatedAsciiRender
  Width: int
  Height: int
  LoopCount: int
  Frames: AnimatedAsciiFrame[]

AnimatedAsciiFrame
  Index: int
  SampleTime: TimeSpan
  Duration: TimeSpan
  Render: RichAsciiRender
```

Requirements:

- `Width` and `Height` are visible character-grid dimensions.
- `LoopCount` is always `0` in the v3 baseline.
- `0` means infinite looping.
- Every `AnimatedAsciiFrame.Render` must have the same `Width` and `Height`.
- Every frame must be generated from a reliable composited source frame.
- `SampleTime` records the source timeline timestamp used for sampling.
- `Duration` records the emitted output frame delay after minimum-delay clamping.
- `size`, `color`, and `detail` must apply consistently across all frames.
- Animated exporters must consume this model rather than resampling source pixels independently.

### 16.1 Rendering Sampled Frames

For each selected composited source frame:

1. Treat the composited source frame as a static source image.
2. Use the animated output grid determined for the full animation.
3. Apply the selected `detail` setting.
4. Apply the selected `color` setting.
5. Generate a `RichAsciiRender`.
6. Store it in an `AnimatedAsciiFrame`.

The same character ramp, luma model, transparency policy, color model, and foreground contrast policy used for static raster output apply to animated frames.

### 16.2 Loop Policy

Generated animated WebP output must loop infinitely.

The source animation repeat count is not preserved and is not used as an acceptance criterion.

The animated render model must set:

```text
LoopCount = 0
```

---

## 17. Animated WebP Export

Animated WebP is the only generated animated output format in the v3 baseline.

### 17.1 Frame Rasterization

Animated WebP export should reuse the static PNG terminal-style raster renderer for each frame.

Each frame must use:

| Parameter | Value |
|---|---:|
| Font | bundled Cascadia Mono Regular |
| Font size | `14 px` |
| Padding | `12 px` |
| Cell width | measured monospace advance, rounded up |
| Cell height | measured line height, rounded up |
| Background | `#0B0D10` |
| Monochrome foreground | `#E6EDF3` |
| Color contrast floor | same as static PNG raster export |

Every WebP frame must have identical pixel dimensions.

The generated animated WebP frame dimensions must not exceed:

- `ASCIIBot_RenderPngMaxWidth`
- `ASCIIBot_RenderPngMaxHeight`

If animated frame rasterization would exceed these dimensions, reject:

```text
The rendered animation exceeds delivery limits. Processing has been rejected.
```

### 17.2 Encoder Behavior

The animated WebP encoder must:

- emit an animated WebP file
- preserve the frame order from `AnimatedAsciiRender.Frames`
- apply each `AnimatedAsciiFrame.Duration`
- encode infinite looping
- produce a single `.webp` file suitable for Discord attachment delivery
- preserve readable terminal-like output

Exact ImageSharp API calls are implementation-verification details. The implementation must pin encoder settings once verified so output behavior is deterministic enough for testing.

The implementation should evaluate lossless animated WebP first because the output is text-like and legibility matters. If lossless output is impractically large under the configured byte limit, a fixed lossy encoder mode and fixed quality value may be selected during implementation verification. The selected encoder mode and quality must remain deterministic and must not vary per request in the v3 baseline.

If the encoder cannot produce animated WebP output for an otherwise valid `AnimatedAsciiRender`, the request has failed:

```text
The submitted animation could not be rendered. Processing has failed.
```

### 17.3 Byte Limit

After encoding, compute the generated WebP byte length.

Reject if:

```text
generatedWebPBytes > ASCIIBot_AnimationWebPByteLimit
```

Preferred message:

```text
The rendered animation exceeds delivery limits. Processing has been rejected.
```

No automatic re-encoding attempt is required in the v3 baseline.

### 17.4 No Animated GIF Export

Animated GIF output is deferred.

The implementation must not silently switch from WebP to GIF if WebP export fails or exceeds limits.

---

## 18. Animated Output Delivery

Animated delivery is attachment-only.

### 18.1 Animated Delivery Rules

Animated inputs do not produce:

- inline text output
- inline ANSI output
- `.txt` attachments
- static PNG output
- first-frame static fallback

Successful animated conversions return:

```text
asciibot-render.webp
```

They may also return:

```text
asciibot-original.<ext>
```

when `show_original=true` and upload limits allow it.

### 18.2 Animated Delivery Decision Tree

Animated output delivery must use this decision tree:

1. Generate `AnimatedAsciiRender`.
2. Export animated WebP.
3. If WebP export fails due to encoder/runtime failure, fail.
4. If generated WebP exceeds `ASCIIBot_AnimationWebPByteLimit`, reject.
5. If generated WebP alone exceeds `ASCIIBot_TotalUploadByteLimit`, reject.
6. Compose completion response with `asciibot-render.webp`.
7. If `show_original=false`, post completion response with generated WebP only.
8. If `show_original=true`, attempt to include validated original source bytes.
9. If generated WebP plus original source exceeds `ASCIIBot_TotalUploadByteLimit`, omit the original source.
10. If the original source is omitted due to delivery limits, append omission note to completion text.
11. Post completion response.

### 18.3 Animated Completion Text

Preferred animated completion text:

```text
Rendering complete. Animated output has been attached.
```

If the original image was requested or default-enabled but omitted due to delivery limits, append:

```text
Original image display was omitted due to delivery limits.
```

If `show_original=false`, do not emit an omission note.

### 18.4 Attachment Names

Use stable generated filenames:

```text
asciibot-render.webp
asciibot-original.<ext>
```

The original extension must be derived from validated content type, not submitted filename alone.

Use this mapping:

| Validated type | Extension |
|---|---|
| PNG | `.png` |
| JPEG | `.jpg` |
| BMP | `.bmp` |
| GIF | `.gif` |
| WebP | `.webp` |

Animated GIF originals use `.gif`.

Animated WebP originals use `.webp`.

### 18.5 Delivery Retry

If animated response upload fails because the original image makes the response too large, the bot may retry once without the original image and include the omission note.

This retry is allowed only for upload-size or payload-too-large failures from Discord when the original image was included.

This retry must not be used for:

- permission failures
- validation failures
- transient server errors
- network failures
- generated artifact failures
- encoder failures

If the generated animated WebP upload fails due to an identifiable size or payload-limit condition, classify the result as a rejection:

```text
The rendered animation exceeds delivery limits. Processing has been rejected.
```

If the generated animated WebP upload fails due to permission, network, server, transport, or unknown delivery error, classify the result as a failure.

Preferred delivery failure text:

```text
The rendered animation could not be delivered. Processing has failed.
```

v3 must not split one animated render across multiple Discord messages.

---

## 19. Original Image Display

`show_original` controls successful completion responses for both static and animated inputs.

Rules:

- Default is `true`.
- Original image display is skipped for all rejection and failure responses.
- Original image display uses the validated downloaded bytes.
- The bot must not rely on Discord's original attachment URL for completion-response display.
- Original image is omitted first when attachment limits are exceeded.
- Omission due to delivery limits should be noted in completion text.
- Omission because `show_original=false` must not emit an omission note.

The original image is contextual. Generated render artifacts remain primary.

For animated input, the generated animated WebP must be preserved before optional original image display.

---

## 20. Reject Versus Fail Classification

v3 distinguishes rejection from failure.

Core rule:

```text
Reject = known boundary or policy violation.
Fail = unexpected runtime, rendering, encoding, permission, or delivery breakdown.
```

### 20.1 Rejection

A rejection means the system behaved correctly and the request was outside the supported v3 envelope.

Use rejection for:

- missing image
- unsupported file type
- unsupported animation/video format
- source file too large
- decoded dimensions too large
- static decode failure
- animated metadata inspection failure
- animation duration too long
- source-frame safety fuse exceeded
- composited frames unavailable
- total output-cell cost fuse exceeded
- generated static output too large
- generated animated WebP too large
- total upload size exceeded by generated required artifacts
- inability to fit output within declared delivery constraints

Rejected responses use:

```text
Processing has been rejected.
```

### 20.2 Failure

A failure means the request was valid but the system could not complete it.

Use failure for:

- rendering pipeline exception after validation
- animated WebP encoder runtime failure
- static PNG encoder/runtime failure
- Discord upload failure not caused by identifiable size or payload limits
- insufficient permissions
- cancellation
- unexpected dependency failure
- unknown internal error

Failed responses use:

```text
Processing has failed.
```

### 20.3 Explicit v3 Classifications

| Condition | Classification |
|---|---|
| Animated GIF/WebP duration exceeds limit | Reject |
| Source-frame count exceeds fuse | Reject |
| Output-cell cost exceeds fuse | Reject |
| Composited frames unavailable | Reject |
| Generated animated WebP exceeds byte limit | Reject |
| Generated animated WebP cannot fit total upload budget | Reject |
| Animated WebP encoder throws or cannot encode valid render | Fail |
| Discord upload rejected because generated artifact is too large despite precheck | Reject when identifiable as size/payload limit |
| Discord upload fails due to permissions | Fail |
| Discord upload fails due to network/server/transient error | Fail |

---

## 21. Rejection And Failure Messages

Bot language should remain formal, precise, affectless, and impersonal. This section includes both rejection and failure messages.

### 21.1 Common Rejection And Failure Messages

| Condition | Public response |
|---|---|
| Missing image | `No image attachment was submitted. Processing has been rejected.` |
| Unsupported file type | `The submitted file type is not supported. Processing has been rejected.` |
| Source file too large | `The submitted image exceeds the maximum source file size. Processing has been rejected.` |
| Dimensions too large | `The submitted image exceeds the maximum supported dimensions. Processing has been rejected.` |
| Decode failure | `The submitted image could not be decoded. Processing has been rejected.` |
| Static rendering failure | `The submitted image could not be rendered. Processing has failed.` |
| Static output too large | `The rendered output exceeds delivery limits. Processing has been rejected.` |
| Static delivery failure | `The rendered output could not be delivered. Processing has failed.` |
| Permission failure | `The rendered output could not be delivered due to insufficient permissions. Processing has failed.` |
| Per-user busy state | `A request from this user is already being processed. Please resubmit after it has completed.` |
| Global busy state | `Processing capacity has been reached. Please resubmit later.` |
| Unknown failure | `Processing failed due to an internal error.` |

### 21.2 Animation-Specific Messages

| Condition | Public response |
|---|---|
| Duration too long | `The submitted animation exceeds the maximum supported duration. Processing has been rejected.` |
| Animation metadata unsupported | `The submitted animation could not be inspected. Processing has been rejected.` |
| Composited frames unavailable | `The submitted animation could not be inspected. Processing has been rejected.` |
| Source-frame safety fuse exceeded | `The submitted animation exceeds processing limits. Processing has been rejected.` |
| Output-cell cost fuse exceeded | `The submitted animation exceeds processing limits. Processing has been rejected.` |
| Generated animation too large | `The rendered animation exceeds delivery limits. Processing has been rejected.` |
| Animation rendering failure | `The submitted animation could not be rendered. Processing has failed.` |
| Animation encoding failure | `The submitted animation could not be rendered. Processing has failed.` |
| Animation delivery failure | `The rendered animation could not be delivered. Processing has failed.` |

### 21.3 Error Disclosure Restrictions

User-facing messages must not include:

- stack traces
- exception details
- local filesystem paths
- bot tokens
- raw Discord API payloads
- raw frame counts
- source image metadata dumps
- generated byte counts
- exact configured limits unless a future product decision permits them

Operator logs may include safe diagnostic values according to Section 23.

---

## 22. Concurrency

v3 preserves v2 concurrency behavior.

Limits:

- one active job per user
- three active jobs globally by default

The bot should check limits before downloading the image when possible.

If a limit is exceeded, reject publicly.

Per-user busy text:

```text
A request from this user is already being processed. Please resubmit after it has completed.
```

Global busy text:

```text
Processing capacity has been reached. Please resubmit later.
```

Job slots must be released in all terminal paths:

- success
- validation rejection after slot acquisition
- animation inspection rejection
- cost-fuse rejection
- rendering failure
- encoding failure
- delivery failure
- cancellation
- unexpected exception

The implementation must avoid retaining large image or frame buffers longer than needed after job completion.

---

## 23. Logging

The application should log operator-facing diagnostics to console.

### 23.1 Common Events

Minimum useful events:

- startup
- missing or invalid required configuration
- Discord connection ready
- slash command registration success/failure
- request accepted
- request rejected with reason
- selected size, color, detail, and show-original options
- request routed to static or animated pipeline
- static rich render completed, including output columns and rows
- delivery mode selected
- original image omitted due to delivery limits
- delivery failure
- unexpected exception

### 23.2 Animation-Specific Events

Animation logging should include:

- validated animated format
- source byte size
- decoded animation canvas dimensions
- source duration in milliseconds
- raw source-frame count when available
- sampled output frame count
- selected output grid dimensions
- total output-cell cost
- whether source-frame safety fuse was checked via metadata or bounded enumeration
- generated animated WebP byte length
- generated animated WebP pixel dimensions
- animated original omitted due to delivery limits
- animation inspection rejection reason
- animation render or encoder failure category

### 23.3 Logging Restrictions

Logs should include Discord IDs where useful for operation, but not user display names as the primary identifier.

The bot token and image binary content must never be logged.

Logs should not dump raw metadata structures or raw Discord payloads by default.

---

## 24. Suggested Internal Structure

v3 should continue using the multi-project solution boundary established in v2.

| Project | Purpose |
|---|---|
| `ASCIIBot` | Console-hosted Discord bot implementation |
| `ASCIIBot.Tests` | Automated tests for rendering, validation, output decisions, animation behavior, and concurrency behavior |

Suggested components:

| Component | Responsibility |
|---|---|
| `Program` | startup, configuration, dependency wiring |
| `BotOptions` | environment-backed settings |
| `AsciiInteractionModule` | slash command declaration and response flow |
| `ImageDownloadService` | bounded attachment download and original-byte retention |
| `ImageValidationService` | content-based format, static/animated routing, source size, dimensions |
| `AnimationInspectionService` | animation metadata, duration, source-frame fuse, compositing capability |
| `CompositedFrameProvider` | reliable composited frame access for sampled timestamps |
| `AnimationSamplingService` | deterministic sample count, sample times, frame selection, timing normalization |
| `AsciiRenderService` | rich render model generation for static images and sampled animation frames |
| `AnimatedAsciiRenderService` | animated render model assembly |
| `AnsiColorService` | nearest-palette mapping and escape grouping |
| `PlainTextExportService` | `.txt` export from rich render model |
| `PngRenderService` | static PNG export from rich render model |
| `AnimatedWebPExportService` | animated WebP export from animated render model |
| `OutputDeliveryService` | static and animated delivery decision trees and Discord delivery |
| `ConcurrencyGate` | per-user and global active-job enforcement |

This structure is guidance, not a requirement, but equivalent boundaries should exist.

### 24.1 Boundary Requirements

The implementation should preserve these boundaries:

- Discord interaction code should not perform image rendering directly.
- Exporters should consume render models, not source image bytes.
- Animated WebP export should consume `AnimatedAsciiRender`, not resample source frames.
- Animation sampling should be testable without live Discord access.
- Delivery decision trees should be testable without live Discord access.
- Concurrency gate should be testable without live Discord access.

---

## 25. Testing Requirements

v3 should include focused automated tests for behavior that does not require live Discord access.

Live Discord command testing may remain manual for v3, but generated animated WebP preview behavior must be manually verified before release.

### 25.1 Inherited Static Tests

All v2 validation, rendering, delivery, and concurrency tests should remain passing unless explicitly superseded by v3.

Required inherited coverage includes:

- `size` enum parsing and defaults
- `color` enum parsing and defaults
- `detail` enum parsing and defaults
- `show_original` default behavior
- output grid calculation with aspect-ratio correction
- detail sample-window behavior
- luminance-to-character mapping
- transparent pixel sampling behavior
- rich render model dimensions and cell contents
- plain text export
- ANSI export
- ANSI palette values and tie-breaking
- PNG export dimensions and byte-limit rejection
- canonical inline character count
- static inline delivery decision
- static PNG plus `.txt` delivery decision
- original image inclusion and omission behavior
- generated artifact priority over original image display
- failure responses do not include original image
- concurrency gate per-user limit
- concurrency gate global limit

### 25.2 Animated Validation Tests

Required animated validation tests:

- animated GIF is routed to animated path
- animated WebP is routed to animated path
- static GIF remains routed to static path
- static WebP remains routed to static path
- unsupported video files are rejected
- PNG inputs containing APNG animation chunks are rejected by default
- source byte limit is enforced before download when metadata is available
- streaming download ceiling is enforced when metadata is unavailable
- decoded animation canvas dimension limit is enforced
- animation duration greater than 12 seconds is rejected
- animation duration equal to 12 seconds is accepted
- zero or non-positive animation duration is rejected as inspection failure
- source-frame count above 1,000 is rejected
- unavailable source-frame count with unsafe enumeration is rejected
- unavailable animation timing is rejected
- unavailable composited frames are rejected
- raw frame deltas are not accepted as renderable frames

### 25.3 Sampling Tests

Required sampling tests:

- duration below 100 ms produces one output frame
- duration equal to 100 ms produces one output frame
- one-second animation produces ten output frames with default configuration
- five-second animation is capped at 48 output frames
- twelve-second animation is capped at 48 output frames
- sample time zero is always included
- final source frame is not forcibly included
- sample times are uniformly distributed
- sample-time tick conversion uses floor rounding
- nearest-frame selection chooses nearest presentation timestamp
- nearest-frame tie chooses earlier frame
- duplicate sampled frames are allowed
- sampling is deterministic across repeated runs

### 25.4 Timing Tests

Required timing tests:

- multi-frame output duration is derived from sample intervals
- last frame duration copies previous output interval
- delays below 100 ms are clamped to 100 ms
- single-frame sampled animation uses `max(sourceDuration, minFrameDelay)`
- clamping may stretch output duration
- no redistribution occurs after clamping
- source repeat count is ignored
- generated loop count is always 0

### 25.5 Cost Fuse Tests

Required cost tests:

- total output-cell cost is computed as `width * height * sampledFrameCount`
- cost equal to 300,000 is accepted
- cost above 300,000 is rejected
- cost fuse rejection uses animation processing-limits message
- source-frame fuse equal to 1,000 is accepted
- source-frame fuse above 1,000 is rejected
- no adaptive frame reduction occurs after cost rejection
- no automatic size reduction occurs after cost rejection

### 25.6 Animated Render Model Tests

Required animated render tests:

- animated render frame count matches sampled frame count
- every frame has same `Width` and `Height`
- every frame contains a `RichAsciiRender`
- `size` applies consistently across frames
- `detail` applies consistently across frames
- `color=on` stores sampled RGB foregrounds
- `color=off` stores monochrome foregrounds
- `LoopCount` is always 0
- sample times and output durations are preserved in frame metadata

### 25.7 Animated WebP Export Tests

Required animated WebP tests:

- animated WebP export receives `AnimatedAsciiRender`
- export preserves frame order
- export applies per-frame durations
- export emits infinite loop metadata when supported by selected library
- export rejects frame dimensions above configured raster ceiling
- generated WebP byte limit is enforced
- generated WebP byte overflow is classified as rejection
- encoder runtime failure is classified as failure
- selected WebP encoder mode and quality are fixed after implementation verification
- no per-request encoder quality adaptation occurs
- no GIF fallback occurs when WebP export fails
- no static fallback occurs when WebP export fails

Exact binary metadata verification may depend on ImageSharp capabilities and should be implemented where practical.

### 25.8 Animated Delivery Tests

Required animated delivery tests:

- animated input never uses inline output
- animated input never emits `.txt`
- successful animated output includes `asciibot-render.webp`
- original animated source is included when `show_original=true` and limits allow it
- original animated source is omitted when `show_original=false`
- no omission note is emitted when `show_original=false`
- original animated source is omitted first when upload limits require it
- omission note is emitted when default/requested original display is omitted due to limits
- generated animated WebP is never dropped from successful output
- generated animated WebP alone exceeding total upload budget is rejected
- generated animated WebP upload failure due to identifiable size or payload limit is classified as rejection
- generated animated WebP upload failure due to permission, network, server, transport, or unknown delivery error is classified as failure
- upload-size retry without original is attempted only for eligible original-included responses

### 25.9 Reject Versus Failure Tests

Required classification tests:

- duration too long is rejected
- source-frame fuse exceeded is rejected
- output-cell fuse exceeded is rejected
- composited frames unavailable is rejected
- generated animated WebP too large is rejected
- generated animated WebP upload payload-limit failure is rejected
- encoder failure is failed
- permission failure is failed
- unknown exception is failed
- user-facing messages use the correct rejected/failed wording

### 25.10 Manual Discord Tests

Manual Discord testing should verify:

- `/ascii` command registration
- public acknowledgement
- static path still works
- animated GIF path works
- animated WebP path works
- generated animated WebP previews correctly in Discord
- generated animated WebP loops continuously
- selected WebP encoder settings preserve readable text-like frames
- original animated source appears when included
- omission note appears when original is omitted due to limits
- failures and rejections are public and procedural
- no response is ephemeral

---

## 26. Acceptance Criteria

v3 is complete when:

- `/ascii image:<attachment> [size] [color] [detail] [show_original]` is globally registered.
- Static PNG, JPEG, BMP, GIF, and WebP behavior from v2 is preserved.
- Animated GIF inputs are accepted when they meet v3 limits.
- Animated WebP inputs are accepted when they meet v3 limits.
- Unsupported animated/video formats are rejected.
- Validation is based on file contents rather than filename extension alone.
- Source images above 10 MiB are rejected.
- Decoded dimensions above 4096x4096 are rejected.
- Animated inputs above 12 seconds are rejected.
- Animated inputs above 1,000 source frames are rejected.
- Animated inputs that cannot produce reliable composited frames are rejected.
- Animated frame sampling is deterministic and time-based.
- Animated output frame count is capped at 48.
- Output frame delays are derived from sample intervals and clamped to at least 100 ms.
- Total animated output-cell cost is capped at 300,000.
- Each sampled animated frame is rendered through the rich render model.
- Animated renders use consistent `size`, `color`, and `detail` across frames.
- Generated animated output is encoded as animated WebP.
- Generated animated WebP loops infinitely.
- Generated animated WebP output is capped at 8 MiB.
- Animated output is delivered as `asciibot-render.webp`.
- Animated output does not include inline text.
- Animated output does not include `.txt`.
- Animated output does not automatically fall back to static rendering.
- `show_original` works for animated sources when upload limits allow it.
- Original image display is omitted before generated animated output when limits require it.
- Original-image omission is noted only when the option was enabled/defaulted and delivery limits caused omission.
- Reject/fail classification is implemented consistently.
- Every accepted request receives a public acknowledgement before rendering completes.
- The bot enforces one active job per user and three active global jobs by default.
- The bot releases job slots in all terminal paths.
- The bot never fails silently after seeing a valid request.
- All user-visible language follows the cold, efficient bureaucratic tone defined in the foundation.
- Manual Discord testing confirms generated animated WebP attachments preview and loop correctly.

---

## 27. Deferred Features

The following are explicitly out of v3:

- animated GIF output
- WebM, MP4, MOV, APNG, or arbitrary video support
- audio support
- animated `.txt` export
- ZIP of text frames
- first-frame-only rendering as a user option
- automatic static fallback
- adaptive frame reduction
- adaptive render-size reduction
- adaptive encoder-quality reduction
- duplicate-frame collapse
- motion-aware sampling
- active-frame-at-sample-time selection
- animation-specific slash-command controls
- user-configurable animation speed
- user-configurable frame count
- source repeat-count preservation
- per-cell background rendering
- user-selectable background modes
- numeric sampling controls
- custom character ramps
- reply-to-image command flow
- message context-menu command
- passive image watching
- multi-image batch mode
- collage generation
- image editing controls
- paginated PNG output
- hosted web gallery
- persistence or user profiles
- web UI
- analytics
- moderation workflows

---

## 28. Implementation Verification Items

The following items must be verified during implementation before v3 can be considered complete:

- ImageSharp can inspect animated GIF metadata needed by this specification.
- ImageSharp can inspect animated WebP metadata needed by this specification.
- ImageSharp can provide reliable fully composited frames for accepted animated GIF inputs.
- ImageSharp can provide reliable fully composited frames for accepted animated WebP inputs.
- ImageSharp can encode animated WebP with per-frame durations.
- ImageSharp can encode animated WebP with infinite looping or equivalent repeat behavior.
- Generated animated WebP attachments preview correctly in Discord.
- Generated animated WebP attachments loop continuously in Discord.
- Generated animated WebP byte sizes are practical under the configured 8 MiB cap.
- Lossless animated WebP output is evaluated for text legibility and byte size.
- If lossy WebP is selected, fixed encoder quality is documented and tested.
- Rasterized ASCII frames remain legible after selected WebP encoder settings.
- Upload-size retry without original behaves correctly for animated responses.

If implementation testing disproves a required capability, the foundation or specification must be revised before implementation proceeds under a different contract.

---

*Last updated: 2026-05-02*
