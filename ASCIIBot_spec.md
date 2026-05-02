# ASCIIBot - v1 Implementation Specification

> This specification translates the ASCIIBot foundation document into concrete implementation requirements for v1.

---

## 1. Purpose

ASCIIBot is a Discord bot that converts one user-supplied static image into ASCII-oriented text and returns the result in Discord.

v1 must optimize for:

- explicit slash-command invocation
- fast public acknowledgement
- reliable image validation
- readable ASCII output
- clear public failure messages
- small hobby-scale deployment

v1 must not introduce unrelated image editing, moderation, persistence, analytics, account, or platform features.

---

## 2. Target Stack

| Concern | Decision |
|---|---|
| Runtime | .NET 10 |
| Process model | Long-lived console application using Microsoft.Extensions.Hosting |
| Discord library | Discord.Net |
| Image library | SixLabors.ImageSharp |
| Deployment posture | Local-first and Docker-friendly |
| Persistence | None required for v1 |

The process should start through the .NET generic host, connect to Discord, register the slash command globally, and remain running until terminated.

The implementation should use `Microsoft.Extensions.Hosting`, dependency injection, `IOptions<T>` or equivalent options binding, and `ILogger<T>` for structured console logging. Manual wiring is acceptable only for narrow leaf objects where DI would add noise.

---

## 3. Configuration

Configuration is supplied through environment variables.

| Variable | Required | Default | Purpose |
|---|---:|---|---|
| `ASCIIBot_DiscordToken` | yes | none | Discord bot token |
| `ASCIIBot_MaxGlobalJobs` | no | `3` | Maximum active jobs across the process |
| `ASCIIBot_MaxJobsPerUser` | no | `1` | Maximum active jobs per Discord user |
| `ASCIIBot_LogLevel` | no | `Information` | Minimum application log level |
| `ASCIIBot_AttachmentByteLimit` | no | `1000000` | Maximum generated text attachment size in bytes |
| `ASCIIBot_InlineCharacterLimit` | no | `2000` | Maximum inline Discord message characters, including formatting overhead |

The Discord token must never be hardcoded, committed, logged, or echoed in diagnostics.

Invalid optional configuration values should be rejected during startup with a clear operator-facing log message.

---

## 4. Discord Command Surface

v1 exposes one global slash command:

```text
/ascii image:<attachment> [size] [color]
```

### 4.1 Options

| Option | Required | Type | Values | Default |
|---|---:|---|---|---|
| `image` | yes | attachment | Discord attachment | none |
| `size` | no | enum string | `small`, `medium`, `large` | `medium` |
| `color` | no | enum string | `on`, `off` | `on` |

The command should be registered globally. Guild-specific registration is not part of the v1 baseline.

### 4.2 Message Visibility

All v1 bot responses are public in-channel:

- acknowledgement
- completion
- inline ASCII output
- attachment fallback
- validation failures
- conversion failures
- busy-state rejections

No v1 response should be ephemeral.

### 4.3 Discord Intents and Permissions

The bot should request the minimum practical Discord capabilities required to:

- receive slash command interactions
- read command attachment metadata
- defer/respond to interactions
- post messages
- upload fallback text attachments

The design must avoid requiring message content intent.

---

## 5. Interaction Flow

### 5.1 Successful Request

1. User invokes `/ascii`.
2. Bot validates that the command includes one image attachment.
3. Bot checks per-user and global concurrency limits.
4. Bot sends a public acknowledgement by deferring the interaction.
5. Bot downloads and validates the attachment.
6. Bot converts the image to ASCII.
7. Bot returns either inline ASCII output or a text attachment.
8. Bot releases the active job slot.

### 5.2 Acknowledgement Requirement

The bot must visibly acknowledge accepted work before conversion completes.

Implementation requirement:

- Use Discord's deferred interaction response mechanism.
- The deferred response must be public.
- Send a public follow-up acknowledgement immediately after deferral.
- The visible wording should be terse and procedural.

Preferred acknowledgement text:

```text
Request received. Processing has begun.
```

Discord.Net's deferred response is expected to show a loading state rather than custom acknowledgement text. v1 should therefore defer publicly, then immediately send the public acknowledgement follow-up. The product requirement is fixed: the user must see a public acknowledgement before conversion completes.

### 5.3 Long-Running Work

If processing takes more than 10 seconds after acknowledgement, the bot should edit the public acknowledgement follow-up when possible:

```text
Processing remains active.
```

Only one long-running status notice is required for v1. If editing the acknowledgement is not possible, the bot may send one additional public status message instead.

---

## 6. Input Validation

v1 accepts exactly one static image per request.

### 6.1 Supported Formats

Supported static formats:

- PNG
- JPEG
- BMP
- GIF, single-frame only
- WebP, static only

Validation must be based on file contents, not filename extension alone.

Animated WebP detection should use ImageSharp metadata/frame inspection where available. If a WebP exposes more than one frame, reject it as animated. If frame count or equivalent static-image confidence cannot be read for a WebP, reject the input conservatively rather than risk silently accepting animation.

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

### 6.3 Download Limit

The bot must not intentionally download more than 10 MiB for a candidate input image.

If Discord attachment metadata reports a size above 10 MiB, reject before download.

If metadata is absent or unreliable, enforce a streaming download ceiling and reject once the limit is exceeded.

---

## 7. Rendering Model

### 7.1 Size Presets

The `size` option maps to target visible output dimensions.

| Size | Target columns | Maximum lines | Use |
|---|---:|---:|---|
| `small` | 48 | 18 | compact chat-safe render |
| `medium` | 72 | 26 | default balanced render |
| `large` | 100 | 35 | largest inline-readable render |

The renderer should preserve source aspect ratio within the selected maximum dimensions.

Because terminal characters are taller than they are wide, the renderer should apply an aspect-ratio correction factor before sampling. The v1 default correction factor is `0.5`, meaning rendered row count should be roughly half of the raw image aspect-derived row count.

The `large` preset intentionally reaches the inline column ceiling. Large renders are allowed, but they are more likely to exceed line-count or message-character thresholds and fall back to a text attachment, especially when color is enabled.

### 7.2 Downscaling

Images may be downscaled internally before conversion.

Implementation requirement:

- Decode the image.
- Normalize orientation when metadata is available.
- Resize to the selected target character grid while preserving aspect ratio.
- Sample one representative color/luminance value per output cell.

### 7.3 Monochrome Character Ramp

The default dark-to-light ramp is:

```text
@%#*+=-:. 
```

The ramp maps darker pixels to denser characters and lighter pixels to sparser characters.

Transparent pixels should be treated as white.

### 7.4 Luminance Calculation

Use perceptual luma for grayscale mapping:

```text
luma = 0.2126 * R + 0.7152 * G + 0.0722 * B
```

RGB values are interpreted after alpha compositing onto white.

---

## 8. Color Model

Color is enabled by default but remains a best-effort enhancement.

### 8.1 Color Modes

| User option | Behavior |
|---|---|
| `color=on` | Attempt ANSI foreground-color output in a Discord `ansi` code block |
| `color=off` | Produce monochrome ASCII with no ANSI escape sequences |

### 8.2 ANSI Palette

v1 uses foreground colors only.

Supported ANSI colors:

- black
- red
- green
- yellow
- blue
- magenta
- cyan
- white
- bright black
- bright red
- bright green
- bright yellow
- bright blue
- bright magenta
- bright cyan
- bright white

Each sampled cell should be mapped to the nearest supported ANSI color by RGB distance.

### 8.3 Escape Minimization

The renderer should group consecutive characters with the same ANSI color to reduce escape-sequence overhead.

The renderer must reset ANSI formatting at the end of each colored render.

### 8.4 Color Degradation

Color is attempted only after the visible render size is eligible for inline delivery. If a visible render is too wide or too tall for inline delivery, skip ANSI generation for delivery purposes and use the plain text attachment path.

If a visible render is inline-eligible but the colored ANSI payload would exceed `ASCIIBot_InlineCharacterLimit`, the bot should fall back to a plain `.txt` attachment with monochrome ASCII text.

If color generation itself fails, the bot may retry the render as monochrome before returning a failure.

---

## 9. Output Delivery

### 9.1 Inline Output

Use inline output only when all thresholds are satisfied:

- no more than 100 visible columns
- no more than 35 visible lines
- no more than `ASCIIBot_InlineCharacterLimit` total Discord message characters, including code fences and ANSI escape sequences

Inline colored output format:

````text
```ansi
<ansi ascii render>
```
````

Inline monochrome output may still use an `ansi` code block for consistency, but must contain no ANSI escape sequences.

The completion text should be public and procedural:

```text
Rendering complete.
```

The render may be included in the same follow-up message as the completion text if Discord limits allow.

The default inline character limit of 2000 aligns with Discord's message character cap. This value has been validated against real render sizes across all three size presets.

### 9.2 Attachment Fallback

If inline output exceeds any inline threshold, return:

- a short public completion message
- one `.txt` attachment containing the full plain text ASCII render

Attachment fallback must not include raw ANSI escape sequences.

Preferred fallback completion text:

```text
Rendering complete. Output has been attached as text.
```

### 9.3 Delivery Decision Tree

Output delivery must use this order:

1. Generate the plain visible ASCII render for the requested size.
2. If visible columns or lines exceed inline thresholds, use attachment fallback.
3. If `color=off`, test the monochrome inline payload against `ASCIIBot_InlineCharacterLimit`.
4. Otherwise, if `color=on`, generate the ANSI payload and test it against `ASCIIBot_InlineCharacterLimit`.
5. If the selected inline payload fits, post inline.
6. If the selected inline payload does not fit, use attachment fallback with the plain monochrome render.
7. If the plain text attachment exceeds `ASCIIBot_AttachmentByteLimit`, reject the request.
8. If attachment upload fails, report delivery failure.

### 9.4 Attachment Too Large

If the generated `.txt` attachment would exceed `ASCIIBot_AttachmentByteLimit`, reject the request with a public failure message.

Preferred text:

```text
The rendered output exceeds delivery limits. Processing has been rejected.
```

### 9.5 Fallback Failure

If attachment fallback is selected but upload fails, the request is considered failed.

Preferred text:

```text
The rendered output could not be delivered. Processing has failed.
```

If Discord reports a missing permission or access error while posting the inline result or uploading the fallback attachment, the bot should use the permission failure message from Section 11 when it is still able to respond publicly. If it cannot respond in-channel due to permissions, the failure should still be logged with operator-facing diagnostics.

v1 must not split one render across multiple Discord messages.

---

## 10. Concurrency

v1 uses immediate rejection instead of deep queueing.

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
- conversion failure
- delivery failure
- cancellation
- unexpected exception

---

## 11. Failure Messages

Bot language should be formal, precise, affectless, and impersonal.

| Condition | Public response |
|---|---|
| Missing image | `No image attachment was submitted. Processing has been rejected.` |
| Unsupported file type | `The submitted file type is not supported. Processing has been rejected.` |
| Animated image | `Animated images are not supported in this version. Processing has been rejected.` |
| Source file too large | `The submitted image exceeds the maximum source file size. Processing has been rejected.` |
| Dimensions too large | `The submitted image exceeds the maximum supported dimensions. Processing has been rejected.` |
| Decode failure | `The submitted image could not be decoded. Processing has been rejected.` |
| Conversion failure | `The submitted image could not be rendered. Processing has failed.` |
| Output too large | `The rendered output exceeds delivery limits. Processing has been rejected.` |
| Permission failure | `The rendered output could not be delivered due to insufficient permissions. Processing has failed.` |
| Unknown failure | `Processing failed due to an internal error.` |

Error messages should not include stack traces, exception details, tokens, local paths, or raw Discord API payloads.

---

## 12. Logging

The application should log operator-facing diagnostics to console.

Minimum useful events:

- startup
- missing or invalid required configuration
- Discord connection ready
- slash command registration success/failure
- request accepted
- request rejected with reason
- conversion completed
- delivery mode selected, inline or attachment
- delivery failure
- unexpected exception

Logs should include Discord IDs where useful for operation, but not user display names as the primary identifier.

The bot token and image binary content must never be logged.

---

## 13. Suggested Internal Structure

The implementation should keep Discord interaction concerns separate from image conversion concerns.

v1 should use a multi-project solution:

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
| `ImageDownloadService` | bounded attachment download |
| `ImageValidationService` | content-based format, animation, size, and dimension checks |
| `AsciiRenderService` | image-to-ASCII conversion |
| `AnsiColorService` | nearest-palette mapping and escape grouping |
| `OutputDeliveryService` | inline vs attachment selection and Discord delivery |
| `ConcurrencyGate` | per-user and global active-job enforcement |

This structure is guidance, not a requirement, but equivalent boundaries should exist.

---

## 14. Testing Requirements

v1 should include focused automated tests for behavior that does not require live Discord access.

Required test coverage:

- size preset dimension calculations
- aspect-ratio preservation
- luminance-to-character mapping
- transparent pixel handling
- monochrome render output
- ANSI palette nearest-color mapping
- ANSI escape grouping
- inline threshold decision
- attachment fallback decision
- attachment-too-large rejection decision
- concurrency gate per-user limit
- concurrency gate global limit
- content-based rejection of unsupported input
- rejection of animated GIF/WebP when detectable by ImageSharp

Live Discord command testing may be manual for v1.

---

## 15. Acceptance Criteria

v1 is complete when:

- `/ascii image:<attachment> [size] [color]` is globally registered.
- The bot accepts static PNG, JPEG, BMP, GIF, and WebP inputs.
- The bot rejects animated images.
- The bot rejects images above 10 MiB or 4096x4096 decoded dimensions.
- Every accepted request receives a public acknowledgement before conversion completes.
- The bot returns readable ASCII for valid images.
- `small`, `medium`, and `large` produce distinct, bounded output sizes.
- `color=on` attempts Discord `ansi` colored output.
- `color=off` produces plain monochrome output.
- Inline output respects visible-size and message-character thresholds.
- Oversized inline output falls back to one plain `.txt` attachment.
- Oversized attachment output is rejected.
- Attachment fallback failure is treated as a visible failure state.
- The bot enforces one active job per user and three active global jobs by default.
- The bot never fails silently after seeing a valid request.
- All user-visible language follows the cold, efficient bureaucratic tone defined in the foundation.

---

## 16. Deferred Features

The following are explicitly out of v1:

- reply-to-image command flow
- message context-menu command
- passive image watching
- multi-image batch mode
- collage generation
- animated image frame extraction
- configurable character ramps
- image editing controls
- multi-message render splitting
- persistence or user profiles
- web UI
- analytics
- moderation workflows

---

*Last updated: 2026-05-01*
