# ASCIIBot - v4 Implementation Specification

> This specification translates the ASCIIBot v4 foundation document into concrete implementation requirements.

---

## 1. Purpose

ASCIIBot v4 adds full Docker-based deployment and bounded MP4 intake without changing the core product identity.

ASCIIBot remains a Discord bot that converts one user-supplied image into ASCII-oriented output and returns the result in Discord.

v4 must optimize for:

- providing a complete, committed Docker-based build and runtime path
- accepting bounded MP4 input as an animated source compatibility format
- routing accepted MP4 through the existing animated ASCII render pipeline
- adding an `ASCII this` message context command for Discord-native media invocation
- preserving all v3 static-image, animated GIF, and animated WebP behavior unless explicitly superseded
- keeping animation processing deterministic and bounded
- rejecting unsupported or excessive inputs clearly
- failing visibly when accepted processing breaks
- preserving the cold, efficient bureaucratic response tone
- staying local-first, hobby-scale, and Discord-native

v4 must not introduce:

- general video support
- audio extraction, analysis, preservation, or output
- YouTube, streaming URL, or remote video ingestion
- support for arbitrary long videos
- adaptive video degradation
- WebM, MOV, APNG, or additional video format support
- animated GIF output
- multi-image batch mode
- hosted gallery, persistence, accounts, analytics, moderation, or web UI

The v4 baseline is intentionally scoped. Docker deployment and MP4 intake are the only extensions. Predictability takes priority over broad compatibility.

---

## 2. Target Stack

v4 preserves the v3 stack and extends it for FFmpeg-based MP4 intake.

| Concern | Decision |
|---|---|
| Runtime | .NET 10 |
| Process model | Long-lived console application using Microsoft.Extensions.Hosting |
| Discord library | Discord.Net |
| Static image decoding and pixel processing | SixLabors.ImageSharp |
| Animated image inspection and compositing | SixLabors.ImageSharp |
| Static PNG text rendering | SixLabors.ImageSharp with SixLabors.Fonts / ImageSharp.Drawing |
| Animated WebP rendering and encoding | SixLabors.ImageSharp WebP encoder |
| MP4 container inspection and frame extraction | FFmpeg CLI (`ffmpeg`, `ffprobe`) invoked as bounded child processes |
| Font | Bundled Cascadia Mono Regular |
| Deployment posture | Docker-first with local non-Docker development preserved |
| Persistence | None |

The process should start through the .NET generic host, probe FFmpeg availability, connect to Discord, register the slash command and message context command globally, and remain running until terminated.

The implementation should continue using:

- dependency injection
- options binding
- structured console logging
- clear separation between Discord interaction code, validation, rendering, exporting, delivery, and concurrency

FFmpeg CLI availability is a startup requirement. If FFmpeg is unavailable on startup, the bot must not proceed to Discord connection.

---

## 3. Configuration

Configuration is supplied through environment variables.

### 3.1 Common Configuration

v4 preserves all v3 configuration behavior.

| Variable | Required | Default | Purpose |
|---|---:|---|---|
| `ASCIIBot_DiscordToken` | yes | none | Discord bot token |
| `ASCIIBot_MaxGlobalJobs` | no | `3` | Maximum active jobs across the process |
| `ASCIIBot_MaxJobsPerUser` | no | `1` | Maximum active jobs per Discord user |
| `ASCIIBot_LogLevel` | no | `Information` | Minimum application log level |
| `ASCIIBot_SourceImageByteLimit` | no | `10485760` | Maximum downloaded source file size in bytes, 10 MiB |
| `ASCIIBot_MaxDecodedImageWidth` | no | `4096` | Maximum decoded image canvas width |
| `ASCIIBot_MaxDecodedImageHeight` | no | `4096` | Maximum decoded image canvas height |
| `ASCIIBot_AttachmentByteLimit` | no | `1000000` | Maximum generated `.txt` attachment size in bytes for static non-inline output |
| `ASCIIBot_InlineCharacterLimit` | no | `2000` | Maximum inline Discord message characters, including formatting overhead |
| `ASCIIBot_RenderPngByteLimit` | no | `8388608` | Maximum generated PNG render size in bytes, 8 MiB |
| `ASCIIBot_RenderPngMaxWidth` | no | `4096` | Maximum generated PNG width in pixels |
| `ASCIIBot_RenderPngMaxHeight` | no | `4096` | Maximum generated PNG height in pixels |
| `ASCIIBot_TotalUploadByteLimit` | no | `10000000` | Maximum total bytes for all files attached to one completion response |

### 3.2 Animation Configuration

v4 preserves all v3 animation configuration. No MP4-specific configuration variables are introduced in the v4 baseline.

| Variable | Required | Default | Purpose |
|---|---:|---|---|
| `ASCIIBot_AnimationMaxDurationMs` | no | `12000` | Maximum accepted source animation duration |
| `ASCIIBot_AnimationMaxOutputFrames` | no | `48` | Maximum sampled output frames |
| `ASCIIBot_AnimationTargetSampleIntervalMs` | no | `100` | Target interval used to derive sampled output frame count |
| `ASCIIBot_AnimationMinFrameDelayMs` | no | `100` | Minimum emitted output frame delay |
| `ASCIIBot_AnimationWebPByteLimit` | no | `8388608` | Maximum generated animated WebP byte size, 8 MiB |
| `ASCIIBot_AnimationMaxOutputCells` | no | `300000` | Maximum `outputWidth * outputHeight * sampledFrameCount` |
| `ASCIIBot_AnimationMaxSourceFrames` | no | `1000` | Defensive source-frame safety fuse (GIF/WebP only; not applied to MP4) |

`ASCIIBot_AnimationMaxSourceFrames` is not applied to MP4 input. FFmpeg-based intake uses timestamp-based seeking rather than source-frame enumeration. Duration and the output-cell cost fuse are the binding constraints for MP4.

### 3.3 FFmpeg Timeouts

FFmpeg process timeouts are hardcoded in the v4 baseline and are not configurable through environment variables.

| Operation | Timeout |
|---|---:|
| FFmpeg inspection (`ffprobe`) | 15 seconds |
| FFmpeg frame extraction (`ffmpeg`) | 60 seconds |

Both timeouts are generous relative to what FFmpeg should take on short, bounded MP4 files under normal conditions on the expected hobby-scale deployment hardware. A personal bot does not need operator-tunable FFmpeg timeouts in the v4 baseline.

### 3.4 Configuration Rules

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

---

## 4. Discord Command Surface

v4 exposes one global slash command and one global message context command.

### 4.1 Slash Command

```text
/ascii image:<attachment> [size] [color] [detail] [show_original]
```

| Option | Required | Type | Values | Default |
|---|---:|---|---|---|
| `image` | yes | attachment | Discord attachment | none |
| `size` | no | enum string | `small`, `medium`, `large` | `medium` |
| `color` | no | enum string | `on`, `off` | `on` |
| `detail` | no | enum string | `low`, `normal`, `high` | `normal` |
| `show_original` | no | boolean | `true`, `false` | `true` |

v4 adds no animation-specific or MP4-specific slash command options. Routing remains automatic based on content validation.

### 4.2 Message Context Command

v4 adds a message context command:

```text
Right-click message → Apps → ASCII this
```

Registered name: `ASCII this`

The context command targets an existing Discord message. The resolved target message is fully populated in the interaction payload regardless of Message Content intent restrictions, per the official Discord gateway documentation carve-out for context command targets.

Discord message context commands accept no user-supplied parameters beyond the target message. The context command uses hardcoded defaults:

| Parameter | Hardcoded value |
|---|---|
| `size` | `medium` |
| `color` | `on` |
| `detail` | `normal` |
| `show_original` | `true` |

### 4.3 Media Resolution For Context Command

The context command resolves media from the target message in this priority order:

1. If the message has one or more attachments, use the first attachment.
2. If the message has no valid attachment, check embeds for a `gifv`-type embed with a non-null `Video.Url`. Use `embed.Video.Value.Url` as the media source.
3. If neither source is available, reject.

When the target message has both an attachment and a gifv embed, the attachment takes priority. There is no fallback from a failed or invalid attachment to the embed. If the attachment download or validation fails, the request fails or is rejected based on that failure — the bot does not silently retry against the embed.

Attachment CDN URLs carry expiry parameters and must be downloaded immediately. They must not be persisted.

### 4.4 Automatic Animation Routing

The user does not choose static or animated rendering. Routing is automatic:

| Submitted content | v4 behavior |
|---|---|
| Static supported image | Use v3 static rendering and delivery |
| Animated GIF within limits | Use v3 animated rendering and delivery |
| Animated WebP within limits | Use v3 animated rendering and delivery |
| MP4 within limits | Use v4 MP4 intake pipeline, then animated rendering and delivery |
| Animated GIF/WebP outside limits | Reject visibly |
| MP4 outside limits | Reject visibly |
| Unsupported animated or video format | Reject visibly |

### 4.5 Message Visibility

All v4 bot responses are public in-channel:

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

No v4 response should be ephemeral, including context command responses.

### 4.6 Discord Intents and Permissions

The bot should request the minimum practical Discord capabilities required to:

- receive slash command and message context command interactions
- read resolved interaction message data
- defer/respond to interactions
- post messages
- upload generated PNG, text, WebP, and original-image attachments
- reattach the validated original image when requested and within delivery limits

The design must not require Message Content intent.

Both commands must be registered globally. Guild-specific registration is not part of the v4 baseline.

---

## 5. Interaction Flow

### 5.1 Common Request Flow

For slash command:

1. User invokes `/ascii`.
2. Bot validates that the command includes one attachment.
3. Bot checks per-user and global concurrency limits.
4. Bot sends a public acknowledgement by deferring the interaction and posting the acknowledgement follow-up.
5. Bot downloads the attachment with a hard byte ceiling.
6. Bot validates the downloaded file by content.
7. Bot routes the request to the static, animated GIF/WebP, or MP4 pipeline.
8. Bot renders and exports according to the selected pipeline.
9. Bot returns the completion response or a visible rejection or failure response.
10. Bot releases the active job slot.

### 5.2 Context Command Request Flow

1. User invokes `ASCII this` via right-click on a message.
2. Bot resolves the media source from the target message (attachment or gifv embed `Video.Url`).
3. If no supported media source is found, reject.
4. Bot checks per-user and global concurrency limits.
5. Bot sends a public acknowledgement by deferring the interaction and posting the acknowledgement follow-up.
6. Bot downloads the resolved media source with a hard byte ceiling.
7. Bot validates the downloaded file by content.
8. Bot routes the request to the static, animated GIF/WebP, or MP4 pipeline.
9. Bot renders and exports using hardcoded defaults.
10. Bot returns the completion response or a visible rejection or failure response.
11. Bot releases the active job slot.

### 5.3 Static Request Flow

For static inputs, v4 preserves the v3 flow:

1. Validate supported static image type.
2. Decode and orient the image when metadata allows it.
3. Create one `RichAsciiRender` using `size`, `color`, and `detail`.
4. Export to inline ANSI, PNG, and/or plain text according to the static delivery decision tree.
5. Include original image when `show_original=true` and delivery limits allow it.

### 5.4 Animated GIF/WebP Request Flow

For animated GIF or animated WebP inputs, v4 preserves the v3 flow:

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

### 5.5 MP4 Request Flow

For MP4 inputs:

1. Validate content-based MP4 type using `ftyp` atom detection.
2. Download MP4 source bytes into a bounded temporary file under `Path.GetTempPath()`.
3. Inspect the MP4 container using `ffprobe` within the 15-second inspection timeout.
4. Validate that the container is readable, at least one video stream is present, the video codec is decodable, duration is a positive value, and canvas dimensions are within configured limits.
5. Enforce source duration limit.
6. Enforce canvas dimension limits.
7. Determine output grid from canvas dimensions and selected `size`.
8. Derive sampled output frame count using the v3 sampling algorithm applied to MP4 duration.
9. Enforce the total-output-cell cost fuse.
10. Extract sampled frames as lossless image files using `ffmpeg` with `-an` within the 60-second extraction timeout. Include `-an` to drop audio at the invocation level.
11. Load extracted frame image files into the animated render pipeline.
12. Render each extracted frame into a `RichAsciiRender`.
13. Assemble an `AnimatedAsciiRender`.
14. Export the animated render to animated WebP.
15. Enforce animated WebP byte limits and total upload limits.
16. Return animated WebP attachment and optional original MP4 when allowed.
17. Delete the temporary file in all terminal paths (success, rejection after download, failure, cancellation, unexpected exception).
18. Release the active job slot.

### 5.6 Acknowledgement Requirement

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

### 5.7 Long-Running Work

If processing takes more than 10 seconds after acknowledgement, the bot should edit the public acknowledgement follow-up when possible:

```text
Processing remains active.
```

Only one long-running status notice is required for v4.

If editing the acknowledgement is not possible, the bot may send one additional public status message instead.

---

## 6. Input Validation

v4 accepts exactly one image or video file per request.

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
- MP4 (bounded, Discord-style animation compatibility format)

Rejected by default:

- MOV
- WebM
- APNG
- AVIF animation
- TIFF
- SVG
- PDF
- ZIP archives
- files that are not decodable supported content

PNG inputs containing APNG animation chunks must be rejected as unsupported animated content in the v4 baseline rather than silently treated as static PNG.

### 6.2 Attachment Presence

If no attachment is supplied, reject publicly:

```text
No image attachment was submitted. Processing has been rejected.
```

For the context command, if no supported media source is found in the target message:

```text
The target message contains no supported media. Processing has been rejected.
```

### 6.3 Download Limit

The bot must not intentionally download more than `ASCIIBot_SourceImageByteLimit` for a candidate file. This limit applies to both attachments and resolved context-command media sources.

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

The bot must determine file type from file contents.

For MP4 detection, check for the ISO base media file format `ftyp` box (atom) at the expected container position. Do not rely on filename extension or declared Content-Type alone.

Filename extension may be used only for:

- user-visible filename preservation when safe
- diagnostics
- fallback extension selection after validated content type

Filename extension alone must not allow unsupported or mislabeled content through validation.

### 6.5 Decoded Dimension Limit

Decoded static image dimensions, animated canvas dimensions, or MP4 video canvas dimensions must not exceed:

- `ASCIIBot_MaxDecodedImageWidth`
- `ASCIIBot_MaxDecodedImageHeight`

Preferred rejection text:

```text
The submitted image exceeds the maximum supported dimensions. Processing has been rejected.
```

For MP4:

```text
The submitted video exceeds the maximum supported dimensions. Processing has been rejected.
```

### 6.6 Static Versus Animated Detection

For GIF and WebP:

- one-frame content routes to the static path
- multi-frame content routes to the animated path
- if frame count cannot be determined but animation confidence is required, inspect conservatively
- if the implementation cannot confidently determine static vs animated behavior, reject rather than silently misroute

For content detected as MP4 by `ftyp` atom signature, route to the MP4 intake pipeline.

### 6.7 Decode Failure

If the submitted image cannot be decoded as a supported image, reject:

```text
The submitted image could not be decoded. Processing has been rejected.
```

---

## 7. Static Rich Render Model

v4 preserves the v3 `RichAsciiRender` model.

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
- `Background` is optional and must be `null` for v4 baseline rendering.
- Missing `Background` means the exporter uses its default background.
- The same rich render model must feed static inline ANSI, plain text, PNG export, and animated frame export.
- `detail` must affect the rich render model itself so all export paths are behaviorally consistent.

---

## 8. Static Rendering Model

v4 preserves the v3 static rendering model.

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

v4 preserves the v3 color and background model for static renders and uses the same visual model for animated WebP frames.

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

v4 preserves v3 static export behavior.

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

---

## 11. Static Output Delivery

v4 preserves v3 static delivery behavior.

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

---

## 12. Animated Inspection And Composition

Animated GIF and animated WebP inputs require reliable inspection before rendering. v4 preserves v3 animated inspection behavior.

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

Reject if `sourceDuration <= 0`:

```text
The submitted animation could not be inspected. Processing has been rejected.
```

Reject if `sourceDuration > ASCIIBot_AnimationMaxDurationMs`:

```text
The submitted animation exceeds the maximum supported duration. Processing has been rejected.
```

A duration exactly equal to the configured maximum is allowed.

### 12.3 Source-Frame Safety Fuse

This fuse applies to GIF and WebP only. It is not applied to MP4 input.

If metadata exposes source-frame count, reject before full rendering when:

```text
sourceFrameCount > ASCIIBot_AnimationMaxSourceFrames
```

Preferred message:

```text
The submitted animation exceeds processing limits. Processing has been rejected.
```

### 12.4 Composited Frame Requirement

Animated input must yield reliable fully composited frames before rendering.

Requirements:

- Sampling operates only on full composited image frames.
- Raw frame deltas must not be rendered directly.
- Disposal behavior must not be guessed.
- Transparency behavior must not be guessed.
- If the selected image stack cannot represent the animation semantics reliably enough for rendering, reject.

Reject as inspection failure when composited frames cannot be produced.

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

v4 preserves v3 deterministic uniform time sampling. This algorithm is used for GIF, WebP, and MP4 inputs.

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

If `sourceDuration < targetInterval`, then `frameCount = 1`.

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

For MP4, sample times are passed to FFmpeg as seek positions for frame extraction. FFmpeg receives computed timestamps, not a source-frame index.

### 13.4 Source Frame Selection

For GIF/WebP:

- select the composited source frame with the nearest presentation timestamp
- if two source frames are equally near, select the earlier source frame
- duplicate selected source frames are allowed

For MP4:

- FFmpeg extracts the nearest decodable frame at each computed sample timestamp
- the MP4 intake pipeline produces one extracted image file per sample timestamp
- duplicate frames may result if the same video frame is nearest to two sample timestamps

### 13.5 Single-Frame Sampled Animations

When `frameCount = 1`, the sampled output contains one animated frame.

The frame's emitted duration is:

```text
max(sourceDuration, minFrameDelay)
```

The result is still treated as animated input for delivery purposes.

---

## 14. Animated Timing Normalization

v4 preserves v3 timing normalization.

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

v4 guarantees:

- output timing is deterministic
- output timing is derived from the sampling grid
- delays below the configured minimum are clamped upward

v4 does not guarantee:

- exact source frame pacing
- preservation of source-frame delays
- exact preservation of total duration after clamping
- frame dropping to preserve duration

---

## 15. Animated Cost Model

v4 preserves v3 cost model.

### 15.1 Output Grid

Before rendering sampled frames, determine the animated output grid using:

- decoded animation canvas width and height (or MP4 video canvas dimensions)
- selected `size`
- the same aspect-ratio correction and grid formula used for static rendering

All sampled frames in one animated render must share this grid.

### 15.2 Total Output Cells

Compute:

```text
totalOutputCells = outputWidth * outputHeight * sampledFrameCount
```

Reject if `totalOutputCells > ASCIIBot_AnimationMaxOutputCells`:

```text
The submitted animation exceeds processing limits. Processing has been rejected.
```

For MP4:

```text
The submitted video exceeds processing limits. Processing has been rejected.
```

### 15.3 No Adaptive Degradation

If the cost fuse is exceeded, v4 must not automatically reduce frame count, reduce render size, lower quality, or return partial output.

---

## 16. Animated Render Model

v4 preserves the v3 animated render model.

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

- `LoopCount` is always `0` in the v4 baseline.
- `0` means infinite looping.
- Every `AnimatedAsciiFrame.Render` must have the same `Width` and `Height`.
- Every frame must be generated from a reliable composited or extracted source frame.
- `size`, `color`, and `detail` must apply consistently across all frames.

### 16.1 Rendering Sampled Frames

For each selected composited source frame (GIF/WebP) or each extracted frame file (MP4):

1. Treat the frame as a static source image.
2. Use the animated output grid determined for the full animation.
3. Apply the selected `detail` setting.
4. Apply the selected `color` setting.
5. Generate a `RichAsciiRender`.
6. Store it in an `AnimatedAsciiFrame`.

The same character ramp, luma model, transparency policy, color model, and foreground contrast policy used for static raster output apply to animated frames.

### 16.2 Loop Policy

Generated animated WebP output must loop infinitely. The animated render model must set `LoopCount = 0`.

---

## 17. Animated WebP Export

Animated WebP is the only generated animated output format in the v4 baseline.

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

The generated animated WebP frame dimensions must not exceed `ASCIIBot_RenderPngMaxWidth` and `ASCIIBot_RenderPngMaxHeight`.

### 17.2 Encoder Behavior

The animated WebP encoder must:

- emit an animated WebP file
- preserve the frame order from `AnimatedAsciiRender.Frames`
- apply each `AnimatedAsciiFrame.Duration`
- encode infinite looping
- produce a single `.webp` file suitable for Discord attachment delivery
- preserve readable terminal-like output

The implementation should evaluate lossless animated WebP first. If lossless output is impractically large under the configured byte limit, a fixed lossy encoder mode and quality value may be selected during implementation verification. The selected encoder mode and quality must remain deterministic and must not vary per request.

### 17.3 Byte Limit

After encoding, reject if `generatedWebPBytes > ASCIIBot_AnimationWebPByteLimit`:

```text
The rendered animation exceeds delivery limits. Processing has been rejected.
```

No automatic re-encoding attempt is required in the v4 baseline.

---

## 18. Animated Output Delivery

Animated delivery is attachment-only. This section applies to animated GIF, animated WebP, and MP4 inputs.

### 18.1 Animated Delivery Rules

Animated inputs do not produce:

- inline text output
- inline ANSI output
- `.txt` attachments
- static PNG output
- first-frame static fallback
- video output
- audio output

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

The validated downloaded bytes are used for `show_original` display. The bot must not re-fetch the source from the original URL.

### 18.3 Animated Completion Text

Preferred animated completion text:

```text
Rendering complete. Animated output has been attached.
```

If the original image was requested or default-enabled but omitted due to delivery limits, append:

```text
Original image display was omitted due to delivery limits.
```

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
| MP4 | `.mp4` |

The MP4 original attachment filename is `asciibot-original.mp4`.

### 18.5 Delivery Retry

If animated response upload fails because the original image makes the response too large, the bot may retry once without the original image and include the omission note.

This retry is allowed only for upload-size or payload-too-large failures from Discord when the original image was included. It must not be used for permission failures, validation failures, transient server errors, network failures, or generated artifact failures.

v4 must not split one animated render across multiple Discord messages.

---

## 19. MP4 Intake Pipeline

This section specifies the complete MP4 intake pipeline from content detection through pipeline handoff.

### 19.1 Content Detection

MP4 input is detected by inspecting the `ftyp` box (atom) at the expected position in the downloaded bytes, consistent with ISO base media file format container structure. Filename extension and declared Content-Type are not used as sole detection signals.

If `ftyp` detection is not possible or the container cannot be identified as a supported MP4 variant, the file is rejected as an unsupported type.

### 19.2 Source Download And Temporary File

1. Enforce the `ASCIIBot_SourceImageByteLimit` byte ceiling before or during download.
2. Write accepted downloaded bytes to a per-job unique path under `Path.GetTempPath()`.
3. FFmpeg receives the local file path as its input. FFmpeg does not receive a remote URL or pipe.
4. The temporary file must be deleted in all terminal paths: success, rejection after download, failure, cancellation, and unexpected exception.
5. Cleanup must be guaranteed by a `finally` block or equivalent `IDisposable`/`IAsyncDisposable` pattern.

### 19.3 Container Inspection

FFmpeg inspection is performed by invoking `ffprobe` as a bounded child process.

The inspection timeout is 15 seconds. A timeout during inspection is classified as a failure.

Required inspection data:

- The container is readable.
- At least one video stream is present.
- The video codec is decodable by the installed FFmpeg build.
- Duration is present as a positive value determinable in milliseconds.
- Video canvas width and height are present and within configured dimension limits.

If any required data is absent or the container cannot be read, reject as inspection failure.

FFmpeg inspection concerns are isolated behind `IMp4InspectionService`.

### 19.4 Limits Validation

After inspection, validate against configured limits:

| Check | Configured limit | Classification on failure |
|---|---|---|
| Duration positive | n/a — zero/non-positive duration | Reject |
| Duration within bound | `ASCIIBot_AnimationMaxDurationMs` | Reject |
| Canvas width within bound | `ASCIIBot_MaxDecodedImageWidth` | Reject |
| Canvas height within bound | `ASCIIBot_MaxDecodedImageHeight` | Reject |

`ASCIIBot_AnimationMaxSourceFrames` is not applied to MP4.

### 19.5 Sampling

Apply the v3 sampling algorithm from Section 13 to the MP4 duration to compute `frameCount` and `sampleTimeTicks`.

Apply the cost fuse check from Section 15 to `outputWidth * outputHeight * frameCount`.

### 19.6 Frame Extraction

FFmpeg frame extraction is performed by invoking `ffmpeg` as a bounded child process.

The extraction timeout is 60 seconds. A timeout during extraction is classified as a failure.

Frame extraction requirements:

- Pass `-an` to drop audio at invocation level. Audio must never be extracted, preserved, or written to output.
- Extract sampled frames as individual lossless image files (PNG) written to the per-job temp directory, numbered by sample index.
- Target the sample timestamps computed by the sampling algorithm.
- If extraction does not produce the expected number of frame files, or any expected frame file is absent or unreadable after extraction completes, the request fails.

FFmpeg frame extraction concerns are isolated behind `IMp4FrameExtractionService`.

### 19.7 Pipeline Handoff

After extraction, load each extracted PNG frame file into an `Image` instance using ImageSharp.

Treat each loaded image as a composited static source frame. Pass the frame sequence into the animated render model assembly at Section 16, bypassing the GIF/WebP `AnimationInspectionService` and `CompositedFrameProvider` components.

Delete extracted frame files from the temp directory alongside the source MP4 temp file in the final cleanup path.

---

## 20. Original Image Display

`show_original` controls successful completion responses for both static and animated inputs.

Rules:

- Default is `true`.
- Original image display is skipped for all rejection and failure responses.
- Original image display uses the validated downloaded bytes for all input types, including MP4.
- The bot must not rely on Discord's original attachment URL or the resolved embed URL for completion-response display.
- The bot must not re-fetch the source from the original URL or embed URL.
- Original image is omitted first when attachment limits are exceeded.
- Omission due to delivery limits should be noted in completion text.
- Omission because `show_original=false` must not emit an omission note.

For MP4 input, the original source is the downloaded MP4 bytes, returned as `asciibot-original.mp4`.

Generated render artifacts remain primary. The validated downloaded bytes for `show_original` must be retained until delivery is complete.

---

## 21. Reject Versus Fail Classification

v4 preserves the v3 reject/fail distinction and adds MP4-specific conditions.

Core rule:

```text
Reject = known boundary, validation, policy, or delivery-limit violation.
Fail = runtime, dependency, decoding, rendering, encoding, permission, transport, cancellation, or unexpected internal breakdown.
```

### 21.1 Static And Animated GIF/WebP Classifications

Inherited from v3. See v3 spec Section 20.3 for the complete table.

### 21.2 MP4-Specific Classifications

| Condition | Classification |
|---|---|
| Unsupported or unreadable container | Reject |
| No video stream present | Reject |
| Video codec not decodable by installed FFmpeg | Reject |
| Duration absent or non-positive | Reject |
| Duration exceeds configured maximum | Reject |
| Canvas dimensions exceed configured limits | Reject |
| Output-cell cost fuse exceeded | Reject |
| Generated animated WebP exceeds byte limit | Reject |
| Generated animated WebP cannot fit total upload budget | Reject |
| FFmpeg inspection timeout | Fail |
| FFmpeg frame extraction timeout | Fail |
| Frame extraction produces missing or unreadable files | Fail |
| Animated WebP encoder failure | Fail |
| Discord delivery failure not caused by known size limits | Fail |
| Cancellation | Fail |
| Unknown exception | Fail |

FFmpeg timeout is classified as failure because it occurs on input that already passed validation. The system, not the input, is the problem.

---

## 22. Rejection And Failure Messages

Bot language remains formal, precise, affectless, and impersonal.

### 22.1 Common Rejection And Failure Messages

| Condition | Public response |
|---|---|
| Missing image | `No image attachment was submitted. Processing has been rejected.` |
| No media in context command target | `The target message contains no supported media. Processing has been rejected.` |
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

### 22.2 Animated GIF/WebP Messages

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

### 22.3 MP4-Specific Messages

MP4 messages use "video" for inspection and validation failures and "animation" for generated animated WebP delivery failures.

| Condition | Public response |
|---|---|
| Unsupported video/container | `The submitted file type is not supported. Processing has been rejected.` |
| No video stream | `The submitted video could not be inspected. Processing has been rejected.` |
| Video inspection failure | `The submitted video could not be inspected. Processing has been rejected.` |
| Duration too long | `The submitted video exceeds the maximum supported duration. Processing has been rejected.` |
| Video dimensions too large | `The submitted video exceeds the maximum supported dimensions. Processing has been rejected.` |
| Video processing limits exceeded | `The submitted video exceeds processing limits. Processing has been rejected.` |
| Video rendering failure | `The submitted video could not be rendered. Processing has failed.` |
| Video decoding failure | `The submitted video could not be rendered. Processing has failed.` |
| Generated animation too large | `The rendered animation exceeds delivery limits. Processing has been rejected.` |
| Video delivery failure | `The rendered animation could not be delivered. Processing has failed.` |

### 22.4 Error Disclosure Restrictions

User-facing messages must not include:

- stack traces
- exception details
- local filesystem paths
- bot tokens
- raw Discord API payloads
- raw frame counts
- source image metadata dumps
- generated byte counts
- exact configured limits

---

## 23. Concurrency

v4 preserves v3 concurrency behavior. Both the slash command and the context command share the same per-user and global concurrency gate.

Limits:

- one active job per user
- three active jobs globally by default

The bot should check limits before downloading the media source when possible.

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
- MP4 inspection rejection
- cost-fuse rejection
- rendering failure
- encoding failure
- delivery failure
- cancellation
- unexpected exception

The implementation must avoid retaining large image, frame buffer, or temp file resources longer than needed after job completion.

---

## 24. Logging

The application should log operator-facing diagnostics to console.

### 24.1 Startup Events

Minimum startup logging:

- startup initiated
- missing or invalid required configuration
- FFmpeg availability probe result (version detected or failure)
- Discord connection ready
- slash command registration success/failure
- context command registration success/failure

The FFmpeg version detected during startup should be logged for operator diagnostics. If FFmpeg is unavailable, log the failure and halt.

### 24.2 Common Request Events

- request accepted with invocation type (slash command or context command)
- request rejected with reason
- selected size, color, detail, and show-original options
- request routed to static, animated GIF/WebP, or MP4 pipeline
- static rich render completed, including output columns and rows
- delivery mode selected
- original image omitted due to delivery limits
- delivery failure
- unexpected exception

### 24.3 Animation-Specific Events

- validated animated format
- source byte size
- decoded animation canvas dimensions
- source duration in milliseconds
- raw source-frame count when available
- sampled output frame count
- selected output grid dimensions
- total output-cell cost
- generated animated WebP byte length
- generated animated WebP pixel dimensions
- animated original omitted due to delivery limits
- animation inspection rejection reason
- animation render or encoder failure category

### 24.4 MP4-Specific Events

- MP4 content detected
- FFmpeg inspection result (duration, canvas dimensions, codec)
- FFmpeg inspection timeout
- FFmpeg frame extraction invoked with sample count and timestamps
- FFmpeg frame extraction result (success, timeout, missing files)
- temp file created and temp file deleted

### 24.5 Logging Restrictions

Logs should include Discord IDs where useful for operation, but not user display names as the primary identifier.

The bot token, image binary content, and temp file binary content must never be logged.

Logs should not dump raw metadata structures or raw Discord payloads by default.

---

## 25. Docker Contract

v4 Docker support must satisfy all of the following conditions before it is considered complete.

### 25.1 Build Command

One documented command builds the image from a clean checkout:

```bash
docker build -t asciibot:latest .
```

### 25.2 Run Command

One documented command runs the bot with environment-based configuration:

```bash
docker run --rm -e ASCIIBot_DiscordToken=<token> asciibot:latest
```

The Discord token is supplied at runtime and never baked into the image.

### 25.3 Dockerfile

The Dockerfile must implement a two-stage build.

**Build stage:**

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0-bookworm-slim AS build
```

**Runtime stage:**

```dockerfile
FROM mcr.microsoft.com/dotnet/runtime:10.0-bookworm-slim AS runtime
```

Runtime stage requirements:

- Install `ffmpeg` from the Debian package repository using `apt-get`.
- Copy the published application output from the build stage.
- Copy all bundled font assets.
- Create a non-root user: `appuser` with UID `1000`.
- Set the runtime user to `appuser`.
- Set the entrypoint to the published application binary.
- Do not expose any network ports.
- Do not include a `HEALTHCHECK` directive.
- Do not embed the Discord token.
- Do not include SDK or build-only tools.

### 25.4 .dockerignore

A `.dockerignore` file must be committed. It must exclude at minimum:

- `.git/`
- build output directories (`bin/`, `obj/`)
- local environment files (`.env`)
- user-specific IDE files

### 25.5 docker-compose.yml

A `docker-compose.yml` file must be committed. It must provide:

- a named service for the bot
- image reference or build context
- `ASCIIBot_DiscordToken` supplied from an environment variable or `.env` file at runtime (not hardcoded)
- restart policy appropriate for a long-running personal bot

The compose file is a first-class committed artifact, not an optional example.

### 25.6 FFmpeg Startup Probe

At startup, before Discord connection is attempted, the bot must verify FFmpeg is available on `PATH`:

```text
ffmpeg -version
```

If the probe fails, log the failure and halt startup. Do not proceed to Discord connection.

The detected FFmpeg version should be logged for operator diagnostics.

### 25.7 README Updates

The README must document:

- Docker build command
- Docker run command with token environment variable
- How to use the docker-compose file
- How to view container logs
- Common configuration variables

---

## 26. Internal Structure

v4 should extend the v3 multi-project solution boundary.

| Project | Purpose |
|---|---|
| `ASCIIBot` | Console-hosted Discord bot implementation |
| `ASCIIBot.Tests` | Automated tests for rendering, validation, output decisions, animation behavior, MP4 intake behavior, and concurrency behavior |

Suggested components:

| Component | Responsibility |
|---|---|
| `Program` | startup, configuration, FFmpeg probe, dependency wiring |
| `BotOptions` | environment-backed settings |
| `AsciiInteractionModule` | slash command and context command declaration and response flow |
| `ImageDownloadService` | bounded attachment download, URL-sourced download, and original-byte retention |
| `ImageValidationService` | content-based format, static/animated/MP4 routing, source size, dimensions |
| `AnimationInspectionService` | animation metadata, duration, source-frame fuse, compositing capability (GIF/WebP) |
| `CompositedFrameProvider` | reliable composited frame access for sampled timestamps (GIF/WebP) |
| `IMp4InspectionService` | FFmpeg-backed MP4 container inspection, duration, codec, dimensions |
| `IMp4FrameExtractionService` | FFmpeg-backed sampled frame extraction to temp files |
| `AnimationSamplingService` | deterministic sample count, sample times, frame selection, timing normalization |
| `AsciiRenderService` | rich render model generation for static images and sampled animation frames |
| `AnimatedAsciiRenderService` | animated render model assembly |
| `AnsiColorService` | nearest-palette mapping and escape grouping |
| `PlainTextExportService` | `.txt` export from rich render model |
| `PngRenderService` | static PNG export from rich render model |
| `AnimatedWebPExportService` | animated WebP export from animated render model |
| `OutputDeliveryService` | static and animated delivery decision trees and Discord delivery |
| `ConcurrencyGate` | per-user and global active-job enforcement |

`IMp4InspectionService` and `IMp4FrameExtractionService` are defined as interfaces to allow unit test mocking without live FFmpeg binaries.

### 26.1 Boundary Requirements

The implementation should preserve these boundaries:

- Discord interaction code should not perform image rendering directly.
- Exporters should consume render models, not source image bytes.
- Animated WebP export should consume `AnimatedAsciiRender`, not resample source frames.
- MP4 inspection and frame extraction should be isolated behind service interfaces.
- Animation sampling should be testable without live Discord access.
- Delivery decision trees should be testable without live Discord access.
- Concurrency gate should be testable without live Discord access.
- FFmpeg process management must not leak into components outside the MP4 service boundary.

---

## 27. Testing Requirements

v4 should include focused automated tests for behavior that does not require live Discord access or live FFmpeg.

Live Discord command testing and Docker image validation may remain manual, but must be completed before v4 is considered done.

### 27.1 Inherited Static Tests

All v3 static validation, rendering, delivery, and concurrency tests must remain passing.

Required inherited coverage includes:

- size/color/detail/show_original option parsing and defaults
- output grid calculation with aspect-ratio correction
- detail sample-window behavior
- luminance-to-character mapping
- transparent pixel sampling behavior
- rich render model dimensions and cell contents
- plain text, ANSI, and PNG export
- inline and non-inline static delivery decision trees
- original image inclusion and omission behavior
- concurrency gate per-user and global limits

### 27.2 Inherited Animated GIF/WebP Tests

All v3 animated validation, sampling, timing, cost, render model, WebP export, and delivery tests must remain passing.

Required inherited coverage includes all tests from v3 Sections 25.2 through 25.9.

### 27.3 MP4 Validation Tests

Required MP4 validation tests:

- MP4 is detected by `ftyp` atom content, not filename extension
- content that passes `ftyp` check is routed to MP4 pipeline
- source byte limit is enforced before or during download
- MP4 exceeding source byte limit is rejected before FFmpeg inspection
- MP4 with inspected duration within limit is accepted
- MP4 with inspected duration exceeding limit is rejected
- MP4 with canvas dimensions within limits is accepted
- MP4 with canvas dimensions exceeding limits is rejected
- MP4 with no video stream is rejected as inspection failure
- MP4 with undecodable codec is rejected as inspection failure

### 27.4 MP4 Intake Service Tests

Required tests using mocked `IMp4InspectionService` and `IMp4FrameExtractionService`:

- successful inspection result feeds correctly into sampling algorithm
- cost fuse is computed from MP4 canvas dimensions and sampled frame count
- cost fuse rejection uses video processing-limits message
- `ASCIIBot_AnimationMaxSourceFrames` is not applied to MP4
- frame extraction is invoked with computed sample timestamps
- missing extracted frame files after extraction are classified as failure
- unreadable extracted frame files are classified as failure
- FFmpeg inspection timeout is classified as failure, not rejection
- FFmpeg extraction timeout is classified as failure, not rejection
- temp file cleanup is invoked in success, rejection, failure, and cancellation paths

### 27.5 MP4 Delivery Tests

Required MP4 delivery tests:

- MP4 input never uses inline output
- MP4 input never emits `.txt`
- successful MP4 output includes `asciibot-render.webp`
- `show_original=true` for MP4 returns `asciibot-original.mp4` when limits allow
- `show_original=false` for MP4 does not include original
- MP4 original is omitted first when upload limits require it
- MP4 original attachment filename is `asciibot-original.mp4`
- MP4 uses validated downloaded bytes for `show_original`, not re-fetched source

### 27.6 Context Command Tests

Required context command tests:

- context command resolves attachment when present
- context command resolves gifv embed `Video.Url` when no attachment is present
- context command uses attachment over gifv embed when both are present
- context command does not fall back from failed attachment to embed
- context command rejects when no supported media source is present
- context command uses hardcoded defaults (medium, color on, normal detail, show_original true)
- context command shares the same concurrency gate as the slash command
- context command response is public, not ephemeral

### 27.7 MP4 Reject Versus Fail Tests

Required classification tests:

- unreadable container is rejected
- no video stream is rejected
- duration too long is rejected
- video dimensions too large is rejected
- cost fuse exceeded is rejected
- generated animation too large is rejected
- FFmpeg inspection timeout is failed
- FFmpeg extraction timeout is failed
- missing frame files are failed
- user-facing MP4 messages use correct video/animation language convention

### 27.8 Manual Discord Tests

Manual Discord testing should verify:

- `/ascii` command registration
- `ASCII this` context command registration and visibility in right-click Apps menu
- public acknowledgement for both invocation paths
- static path still works
- animated GIF path works
- animated WebP path works
- MP4 from Discord GIF picker via context command works end-to-end
- generated animated WebP previews correctly in Discord
- generated animated WebP loops continuously
- original MP4 attachment appears when included
- failures and rejections are public and procedural
- no response is ephemeral

### 27.9 Docker Validation

Manual Docker validation must verify:

- `docker build -t asciibot:latest .` succeeds from a clean checkout
- `docker run --rm -e ASCIIBot_DiscordToken=<token> asciibot:latest` starts and connects to Discord
- FFmpeg startup probe logs a detected version
- bot registers commands and processes an `/ascii` request end-to-end inside the container
- bot processes an `ASCII this` context command request inside the container
- bot rejects an MP4 exceeding duration limits inside the container
- container runs as `appuser` (UID 1000)
- container logs are visible via `docker logs`
- compose file starts the bot correctly

---

## 28. Acceptance Criteria

v4 is complete when:

- `/ascii image:<attachment> [size] [color] [detail] [show_original]` is globally registered.
- `ASCII this` message context command is globally registered.
- Static PNG, JPEG, BMP, GIF, and WebP behavior from v3 is preserved.
- Animated GIF and animated WebP behavior from v3 is preserved.
- MP4 inputs within v4 limits are accepted and rendered as animated ASCII WebP.
- MP4 inputs exceeding duration or dimension limits are rejected visibly.
- MP4 audio is never extracted, preserved, or surfaced.
- Content-based type detection is used for all formats including MP4 (`ftyp` atom).
- `ftyp`-detected MP4 routes to the MP4 intake pipeline.
- FFmpeg is the MP4 intake dependency, isolated behind service interfaces.
- FFmpeg availability is verified at startup; bot halts if FFmpeg is unavailable.
- FFmpeg inspection respects a 15-second timeout; timeout is classified as failure.
- FFmpeg frame extraction respects a 60-second timeout; timeout is classified as failure.
- MP4 temporary files are cleaned up in all terminal paths.
- `show_original=true` for MP4 returns `asciibot-original.mp4` when delivery limits allow.
- MP4 `show_original` uses validated downloaded bytes, not re-fetched source.
- Context command resolves attachment over gifv embed when both present.
- Context command uses hardcoded defaults (medium, color on, normal detail, show_original true).
- Context command does not require Message Content intent.
- Context command responses are public.
- Both invocation paths share the same concurrency gate.
- `ASCIIBot_AnimationMaxSourceFrames` is not applied to MP4 input.
- No MP4-specific environment variables are introduced in the v4 baseline.
- FFmpeg timeouts are hardcoded and not configurable.
- Reject/fail classification is implemented correctly for all MP4 conditions.
- All user-visible language follows the cold, efficient bureaucratic tone.
- `docker build -t asciibot:latest .` succeeds from a clean checkout.
- `docker run --rm -e ASCIIBot_DiscordToken=<token> asciibot:latest` starts and connects.
- Runtime image is based on `mcr.microsoft.com/dotnet/runtime:10.0-bookworm-slim`.
- Runtime image includes FFmpeg installed via Debian apt.
- Runtime container runs as non-root `appuser` (UID 1000).
- `docker-compose.yml` is a committed artifact.
- `.dockerignore` is committed.
- README documents Docker build, run, compose, and logging.
- All inherited v3 unit tests pass.
- All new v4 unit tests pass.
- Solution builds clean with zero warnings.
- Manual Discord testing confirms end-to-end operation for all supported pipelines.
- Docker validation confirms end-to-end operation inside the container.

---

## 29. Deferred Features

The following are explicitly out of v4:

- animated GIF output
- WebM, MOV, APNG, or arbitrary additional video format support
- audio extraction, analysis, preservation, or output
- YouTube, streaming URL, or general remote URL ingestion
- direct copied-media-link input (`/ascii url:...`)
- FFmpeg version pinning (distribution package manager install accepted for v4 baseline)
- adaptive video degradation
- automatic static fallback for any animated input
- adaptive frame reduction, render-size reduction, or encoder-quality reduction
- duplicate-frame collapse
- motion-aware sampling
- active-frame-at-sample-time selection
- animation-specific slash-command controls
- user-configurable animation speed or frame count
- source repeat-count preservation
- per-cell background rendering
- user-selectable background modes
- numeric sampling controls
- custom character ramps
- container health checks
- HTML export
- paginated PNG output
- multi-image batch mode
- hosted web gallery
- persistence or user profiles
- web UI
- analytics
- moderation workflows

---

## 30. Implementation Verification Items

The following items must be verified during implementation before v4 can be considered complete:

- All v3 ImageSharp animated inspection and WebP encoding capabilities remain valid.
- FFmpeg is installable via `apt-get install ffmpeg` on `mcr.microsoft.com/dotnet/runtime:10.0-bookworm-slim`.
- The installed FFmpeg build can decode H.264 and common MP4 video streams from Discord-saved GIF-like files.
- `ffprobe` can extract duration, canvas dimensions, and codec from representative MP4 test inputs.
- `ffmpeg` with `-an` extracts individual PNG frames at computed sample timestamps correctly.
- Frame extraction produces lossless PNG files loadable by ImageSharp.
- The lossless PNG-from-FFmpeg path integrates cleanly into the existing `AsciiRenderService`.
- Animated WebP output from MP4-sourced frames is legible and loops correctly in Discord.
- Generated animated WebP byte sizes from MP4 inputs are practical under the 8 MiB cap.
- Container runs as `appuser` (UID 1000) verified via `docker run ... id`.
- `ASCII this` context command appears in the Discord right-click Apps menu after registration.
- GIF picker embed `Video.Url` from Tenor or Giphy is directly downloadable within `ASCIIBot_SourceImageByteLimit`.
- Attachment CDN URL expiry does not cause download failures in the immediate-download pattern.
- `IMp4InspectionService` and `IMp4FrameExtractionService` interfaces enable unit tests without live FFmpeg.
- Selected WebP encoder mode and quality are documented and fixed after initial verification.

If implementation testing disproves a required capability, the foundation or specification must be revised before implementation proceeds under a different contract.

---

*Last updated: 2026-05-02*
