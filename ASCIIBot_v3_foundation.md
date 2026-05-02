# ASCIIBot - v3 Foundation Document

> This is a living document for ASCIIBot v3. It captures direction, product principles, open questions, and early decisions before they are translated into a formal v3 implementation specification.

---

## Purpose

v1 proved the core ASCIIBot loop: a Discord user submits a static image, the bot acknowledges the request, converts the image into ASCII-oriented output, and returns the result in Discord.

v2 improved completed render artifacts by adding richer render output, PNG delivery, detail control, and original image display.

v3 begins from a new foundation: bounded support for animated GIF and animated WebP inputs.

ASCIIBot remains a small, Discord-native image-to-ASCII bot. The purpose of v3 is not to become a general animation editor, video renderer, or media pipeline. It is to decide how animated source images can be accepted, sampled, rendered, bounded, and delivered while preserving the bot's existing reliability, clarity, and procedural tone.

---

## Product Philosophy

**Accept motion only when the result stays understandable.**

Animated input is a meaningful expansion because GIFs and WebPs are common Discord image formats. However, animation can easily create large outputs, slow processing, unclear delivery behavior, and surprising failures.

v3 should prefer explicit limits, predictable behavior, and clear rejection over trying to support every possible animation.

The guiding question for v3 is:

> What is the smallest useful animated-image workflow that feels native to ASCIIBot?

This means v3 favors predictable, bounded output over maximum animation fidelity. When the bot cannot produce a reliable animated ASCII render under the v3 contract, it should reject or fail visibly rather than silently degrade into an unclear substitute.

---

## v3 Goals

- Accept animated GIF inputs when they meet v3 limits.
- Accept animated WebP inputs when they meet v3 limits.
- Preserve static PNG, JPEG, BMP, GIF, and WebP behavior from v1 and v2.
- Convert animated source frames into ASCII-oriented animated output.
- Keep successful animated output Discord-friendly and previewable.
- Preserve existing acknowledgement, validation, concurrency, delivery, and failure guarantees.
- Keep output bounded enough for hobby-scale deployment.
- Preserve the cold, efficient bureaucratic response tone.
- Treat animation support as a first bounded pass, not as exhaustive media support.

---

## v3 Non-Goals

- Not a general-purpose animation editor
- Not arbitrary video support
- Not MP4, MOV, WebM, or APNG support by default
- Not audio support
- Not frame-by-frame user editing
- Not animation compositing or effects beyond decoding source animation semantics
- Not multi-image batch processing
- Not a hosted web gallery
- Not persistence, user accounts, analytics, or moderation
- Not unlimited-duration or unlimited-frame animation processing
- Not adaptive animation degradation, downshifting, or automatic static fallback

---

## Animated Input Direction

v1 and v2 reject animated GIF and animated WebP inputs. v3 should replace that rejection with bounded support.

| Input | v1/v2 behavior | v3 direction |
|---|---|---|
| Static GIF | Accepted | Preserve existing behavior |
| Animated GIF | Rejected | Accept within frame, duration, size, compositing, and output limits |
| Static WebP | Accepted | Preserve existing behavior |
| Animated WebP | Rejected | Accept within frame, duration, size, compositing, and output limits |

v3 should continue to validate by file contents rather than filename extension alone.

Animated input support requires reliable animation inspection. The implementation must be able to determine animation duration, frame timing, frame count or equivalent frame enumeration safety, and reliable composited frame output before accepting the request for animated rendering.

If reliable inspection or composited frame access is not available for a submitted animation, the request should be rejected rather than rendered best-effort.

---

## Animated Output Direction

v3 animated ASCII output means a generated animated WebP render.

Candidate delivery formats:

| Format | Strength | Concern |
|---|---|---|
| Animated WebP render | Discord-native preview, better compression, stronger color and alpha behavior | Requires implementation verification in the .NET image stack |
| Animated GIF render | Broad legacy compatibility, preserves motion | Limited color depth, weaker compression, less aligned with Discord's current animated-image direction |
| ZIP of frame `.txt` files | Preserves text frames | Deferred; not previewable and awkward for casual users |
| Single static contact sheet | Simple and previewable | Deferred; does not preserve motion |
| First-frame static fallback | Easy degradation | Deferred; may deliver something the user did not ask for |

Decision: successful animated input should produce a previewable animated WebP render when possible. If animated output cannot be produced or delivered within limits, the request should be visibly rejected or failed rather than automatically replaced with static output.

Animated GIF output remains a deferred compatibility option if implementation testing exposes a serious animated WebP blocker.

---

## Frame Sampling And Timing

Animated images introduce separate constraints:

- how many source frames are inspected
- how many source frames are sampled
- how sample timestamps are chosen
- how output frame timing is produced

v3 uses deterministic uniform time sampling.

Decisions:

- v3 samples animated input by time rather than rendering every source frame.
- v3 emits no more than 48 output frames.
- v3 rejects source animations longer than 12 seconds.
- v3 uses a target sampling interval of 100 ms.
- v3 clamps output frame delays below 100 ms upward.
- v3 derives output frame timing from the sampling grid, not directly from original source-frame delays.
- Duplicate sampled frames are allowed in the v3 baseline.
- Duplicate-frame collapse is deferred.

### Sampling Algorithm Direction

Given:

```text
duration = total accepted source animation duration
targetInterval = 100 ms
maxFrames = 48
````

The output frame count is:

```text
frameCount = min(maxFrames, floor(duration / targetInterval))
```

If `duration < 100 ms`, `frameCount` is set to `1`.

Sample timestamps are distributed uniformly across the source animation duration:

```text
sampleTime[i] = i * (duration / frameCount)
for i = 0 .. frameCount - 1
```

For each sample timestamp:

* select the nearest composited source frame by timestamp
* if two source frames are equally near, choose the earlier source frame
* always include the first frame at `t = 0`
* do not force inclusion of the final source frame
* do not collapse duplicate sampled frames in the v3 baseline

This model gives v3 predictable output cost, deterministic behavior, and acceptable first-pass motion preservation without trying to preserve every source frame.

### Timing Normalization Direction

Output frame durations are reconstructed from sample intervals:

```text
duration[i] = sampleTime[i + 1] - sampleTime[i]
```

For the last output frame:

```text
duration[last] = duration[last - 1]
```

Then apply the minimum delay clamp:

```text
duration[i] = max(duration[i], 100 ms)
```

Implications:

* Output timing is derived from the sampling grid.
* Output timing approximately preserves the source duration.
* Clamping may stretch the output animation slightly.
* No redistribution pass is performed.
* No frame dropping is performed to preserve exact duration.
* Exact source frame pacing is not a v3 guarantee.

---

## Frame Composition Policy

Animated GIF and animated WebP files may contain partial frames, transparency, disposal metadata, and timing behavior that require decoding into full composited frames before rendering.

v3 requires reliable fully composited frames.

Decision:

* Animated input must yield a reliable sequence of fully composited frames before rendering.
* Sampling operates only on fully composited frames.
* Raw frame deltas are not rendered directly.
* Disposal or transparency behavior is not guessed.
* If reliable composited frames cannot be obtained, the request is rejected.
* No best-effort compositing is attempted in the v3 baseline.

A submitted animation is considered unsuitable for v3 animated processing when:

* animation metadata cannot be inspected
* frame timing cannot be determined
* frame enumeration is unreliable
* composited frames cannot be produced
* frame decoding fails during inspection or composition
* the selected image stack cannot represent the animation semantics reliably enough for rendering

Preferred failure category:

```text
The submitted animation could not be inspected. Processing has been rejected.
```

This keeps v3 predictable. The bot should render animation correctly according to decoded semantics or refuse the request.

---

## Render Model Compatibility

v2 introduced a rich render model for one static render.

v3 should extend that concept to animation rather than bypass it.

Conceptual shape:

```text
AnimatedAsciiRender
  Width: int
  Height: int
  LoopCount: int        // 0 = infinite; always set to 0 in v3 baseline
  Frames: AnimatedAsciiFrame[]

AnimatedAsciiFrame
  Duration: TimeSpan
  Render: RichAsciiRender
```

Requirements to consider:

* Every frame in one animated render should share the same width and height.
* `size`, `color`, and `detail` should apply consistently across all frames.
* Static inputs should remain representable as a single `RichAsciiRender`.
* Animated exporters should consume animated render models rather than resampling source pixels independently.
* Frame sampling should occur before rich render generation for each selected frame.
* Each sampled composited frame should be rendered using the existing rich render model path where practical.

---

## Delivery Policy

v3 should preserve v2 delivery for static images.

Animated delivery uses attachment delivery.

Decisions:

* Animated inputs do not produce inline text output in v3.
* Animated outputs do not include a `.txt` artifact in v3.
* A successful animated conversion returns a generated animated WebP render as the primary artifact.
* `show_original=true` includes the original animated file only when total upload limits allow it.
* Generated animated output takes priority over original image display.
* Original image display is omitted before generated animated output when limits require it.
* Static fallback is not allowed automatically.
* Animated output that cannot fit as a single previewable attachment is rejected or failed visibly.

Baseline artifact set:

```text
asciibot-render.webp
asciibot-original.<ext>
```

The generated render is mandatory for a successful animated conversion. The original source is contextual and optional.

If the original animated source is omitted because `show_original=true` but delivery limits prevent inclusion, completion text should note the omission. If `show_original=false`, no omission note should be emitted.

---

## Reject Versus Fail Policy

v3 should preserve the distinction between requests that violate known boundaries and requests that break during valid processing.

Core rule:

```text
Reject = known boundary or policy violation.
Fail = unexpected runtime, rendering, encoding, permission, or delivery breakdown.
```

### Rejected Conditions

Use:

```text
Processing has been rejected.
```

when the request violates a defined product, validation, processing, or delivery limit.

Examples:

* unsupported file type
* source file too large
* decoded dimensions too large
* animation duration exceeds the v3 limit
* animation metadata cannot be inspected
* composited frames cannot be obtained
* source-frame safety fuse exceeded
* total output-cell cost fuse exceeded
* generated animated WebP exceeds byte limit
* total upload size exceeds limits
* output cannot fit delivery constraints

A rejection means the system behaved correctly and the request was outside the allowed v3 envelope.

### Failed Conditions

Use:

```text
Processing has failed.
```

when the request is valid but the system cannot complete it due to runtime, dependency, rendering, encoding, permission, transport, cancellation, or unexpected internal error.

Examples:

* rendering pipeline exception
* animated WebP encoder failure
* Discord upload failure not caused by known size limits
* permission failure
* cancellation
* unknown internal error

A failure means the input was acceptable but the system did not complete the operation.

### Explicit Classification Decisions

* Generated animated WebP too large: rejected.
* Source-frame safety fuse exceeded: rejected.
* Animation inspection or compositing unsupported: rejected.
* WebP encoder failure: failed.
* Discord permission or transport failure: failed.

---

## Limits And Safety

Animated inputs need stricter operational boundaries than static inputs.

v3 limits exist for three different reasons:

1. Discord-facing delivery constraints
2. Product and readability constraints
3. Local processing safety constraints

The expected deployment is a personal local bot on a high-end desktop with fewer than four concurrent users, so local compute is not the primary limiting factor. Discord delivery and product shape remain the main boundaries.

### Settled Default Limits

| Limit                                     |             Default | Purpose                                                        |
| ----------------------------------------- | ------------------: | -------------------------------------------------------------- |
| Maximum source byte size                  |              10 MiB | Preserve v1/v2 download safety                                 |
| Maximum decoded dimensions                |           4096x4096 | Preserve v1/v2 image safety                                    |
| Maximum source animation duration         |          12 seconds | Keep animation short and Discord-native                        |
| Maximum output frame count                |           48 frames | Bound rendering and encoding work                              |
| Minimum output frame delay                |              100 ms | Keep motion readable and avoid pathological timing             |
| Maximum generated animated WebP byte size |               8 MiB | Fit conservative Discord delivery expectations                 |
| Maximum total upload size                 |    10,000,000 bytes | Keep default responses compatible with non-Nitro upload limits |
| Maximum total output cells                |             300,000 | Bound render cost across sampled animation frames              |
| Maximum source frames safety fuse         | 1,000 source frames | Defensively reject pathological source animations              |

### Cost Model

v3 uses a preflight output-cell cost fuse.

After determining the animated output grid and sampled frame count, compute:

```text
totalOutputCells = outputWidth * outputHeight * sampledFrameCount
```

Reject if:

```text
totalOutputCells > 300,000
```

This protects future tuning of size presets, frame counts, or animation behavior from accidentally expanding processing cost beyond the v3 envelope.

### Source-Frame Safety Fuse

Raw source frame count is not the primary product acceptance model. Duration and sampled output frame count are more important for user-visible behavior.

However, source frame count remains a defensive fuse.

Reject if inspection determines:

```text
sourceFrameCount > 1,000
```

This protects against short but pathological animations with excessive raw frame counts.

### No Adaptive Degradation

If limits are exceeded, v3 does not automatically:

* reduce frame count
* reduce render size
* lower detail
* lower color fidelity
* lower encoder quality
* switch to GIF
* return first-frame static output
* return a partial animation

The request is rejected with a visible procedural message.

### Post-Render Size Enforcement

Generated animated WebP byte size is enforced after encoding.

If the generated WebP exceeds the configured generated animation byte limit, the request is rejected.

No automatic re-encoding attempt is required in the v3 baseline.

---

## Decision Tracks

This section tracks decisions that shaped v3 before translation into implementation specification.

### 1. Primary Animated Output Format

Status: Decided.

Decision:

* Generated animated ASCII should be delivered as animated WebP in the v3 baseline.

Key considerations:

* Discord preview behavior matters more than theoretical format quality.
* GIF is broadly previewable but has limited color depth and weaker compression.
* WebP is now a Discord-supported animated attachment and embed format.
* The output format should be easy to generate and validate in the .NET image stack.

Rationale:

* ASCIIBot is Discord-first, and animated WebP aligns with the desired Discord-native viewing experience.
* WebP better preserves color and alpha than GIF and should usually produce smaller generated artifacts.
* The current ImageSharp package exposes WebP encoder and animation metadata APIs, making WebP output plausible without introducing a separate native toolchain at the foundation stage.
* Animated GIF output can remain a deferred compatibility option if implementation testing exposes a WebP blocker.

---

### 2. Animated Delivery Artifacts

Status: Decided.

Decision:

* A successful animated conversion should return a generated animated WebP render as the primary artifact.
* A successful animated conversion may include the original animated source when `show_original=true` and total upload limits allow it.
* Generated artifacts take priority over original image display.

Baseline artifact set:

```text
asciibot-render.webp
asciibot-original.<ext>
```

Notes:

* Animated conversions do not include a `.txt` attachment in the v3 baseline.
* Text export for animated renders is deferred.
* The original source is contextual and must be omitted before generated output when limits require it.

Rationale:

* Animated WebP is the complete Discord-native viewing artifact for animated input.
* A first-frame `.txt` attachment under-represents the animation.
* An all-frame `.txt` attachment would be bulky, noisy, and less useful for casual Discord sharing.
* Omitting animated `.txt` output keeps v3 delivery clear while leaving explicit animated text export as a possible future feature.

---

### 3. Animated Text Export Semantics

Status: Decided.

Decision:

* Animated renders do not include `.txt` output in the v3 baseline.
* Animated text export is deferred.

Candidate approaches:

| Approach                           | Strength            | Concern                             |
| ---------------------------------- | ------------------- | ----------------------------------- |
| First frame only                   | Simple and bounded  | Does not represent the animation    |
| All sampled frames with separators | Honest and complete | Can become large and noisy          |
| No `.txt` for animated renders     | Clean delivery      | Breaks v2's copyable-artifact habit |

Rationale:

* First-frame text is incomplete for an animated source.
* All-frame text is likely to be large, awkward to read, and poorly aligned with Discord-native viewing.
* v3 explicitly supersedes v2's non-inline `.txt` habit for animated renders only. Static render delivery remains unchanged.

---

### 4. Frame Selection

Status: Decided.

Decision:

* v3 uses deterministic uniform time sampling for animated inputs.
* v3 enforces a maximum output frame count.
* v3 enforces a maximum source animation duration.
* Raw source frame count informs validation and logging, but is not the primary acceptance model by itself.
* Duplicate sampled frames are allowed in the v3 baseline.

Sampling rules:

```text
targetInterval = 100 ms
maxFrames = 48
frameCount = min(maxFrames, floor(duration / targetInterval))
```

If `duration < 100 ms`, `frameCount = 1`.

```text
sampleTime[i] = i * (duration / frameCount)
for i = 0 .. frameCount - 1
```

For each sample timestamp:

* choose the nearest composited source frame by timestamp
* choose the earlier frame when two source frames are equally close
* do not force inclusion of the final source frame
* do not collapse duplicate sampled frames

Rationale:

* Time-based sampling treats animation duration as the user-visible quantity that matters most.
* A maximum output frame count bounds rendering and encoding work.
* Uniform sampling is deterministic and understandable.
* A short high-frame-count animation can still be accepted without rendering every source frame.
* Very long animations remain outside v3 scope and should be rejected rather than silently reduced into a misleading summary.
* Duplicate-frame collapse can be revisited later as an optimization, not as part of the baseline contract.

---

### 5. Timing Normalization

Status: Decided.

Decision:

* v3 reconstructs output frame timing from the sampling grid.
* v3 does not inherit exact source-frame durations for sampled frames.
* v3 clamps output frame delays below 100 ms upward.
* Total duration is approximately preserved but may stretch slightly due to clamping.

Timing rules:

```text
duration[i] = sampleTime[i + 1] - sampleTime[i]
duration[last] = duration[last - 1]
duration[i] = max(duration[i], 100 ms)
```

Rationale:

* Approximate timing preserves the character of the source animation better than fixed first-frame fallback.
* Deriving timing from the sampling grid keeps behavior predictable.
* A minimum delay prevents pathological fast frames from inflating output size or rendering poorly in Discord.
* Clamping very short delays is less surprising than rejecting otherwise valid short animations only because of source timing quirks.
* Exact timing fidelity is less important than stable previewable output in v3.

---

### 6. Animation Limits

Status: Decided.

Decision:

* Maximum source byte size remains 10 MiB.
* Maximum decoded dimensions remain 4096x4096.
* Maximum source animation duration defaults to 12 seconds.
* Maximum output frame count defaults to 48 frames.
* Minimum output frame delay defaults to 100 ms.
* Maximum generated animated WebP byte size defaults to 8 MiB.
* Maximum total upload size defaults to 10,000,000 bytes.
* Maximum total output cells defaults to 300,000.
* Maximum source-frame safety fuse defaults to 1,000 frames.
* Generated animated WebP dimensions should use the existing rendered image dimension ceiling unless implementation testing shows animation-specific constraints are needed.

Rationale:

* The expected deployment is a personal local bot on a high-end desktop with fewer than four concurrent users.
* Local compute is not the primary constraint, so cost fuses should avoid unnecessary kneecapping while still preventing pathological cases.
* Discord delivery and product readability remain the important external constraints.
* A 12-second duration and 48-frame output cap allow recognizable short Discord animations without turning ASCIIBot into a general video renderer.
* A 100 ms minimum delay keeps ASCII motion readable and avoids pathological rapid-frame output.
* A 300,000-cell cost fuse gives headroom over the current theoretical maximum while still bounding future expansion.
* A 1,000-source-frame safety fuse avoids rejecting ordinary weird-but-valid animations too aggressively while still blocking abusive or pathological files.
* Source byte and decoded-dimension limits continue to protect the bot before animation work begins.

---

### 7. Static Fallback

Status: Decided.

Decision:

* v3 should not automatically fall back to static output when animated output cannot be delivered.
* If an animated input is accepted for animated rendering but the generated animated output exceeds limits or cannot be delivered, the request should be rejected or failed with the appropriate visible message.
* First-frame-only rendering is deferred unless introduced later as an explicit user control.

Key considerations:

* Static fallback can save some user requests.
* It may also surprise users who expected motion.
* A visible note could make fallback understandable.

Rationale:

* Animated input creates an expectation of animated output.
* A static fallback may deliver something the user did not ask for.
* Rejecting is clearer than jumping through delivery hoops for a compromised result.
* Users can resubmit with a smaller, shorter, or otherwise adjusted source if needed.

---

### 8. User Controls

Status: Decided.

Decision:

* v3 should not add animation-specific command options.
* Animated input should automatically use animated rendering when it passes v3 limits.
* Existing controls continue to apply: `size`, `color`, `detail`, and `show_original`.

Candidate controls deferred:

```text
animation: animated | first_frame
speed: original | normalized
frames: low | normal | high
```

Rationale:

* The baseline behavior is simple: animated input produces animated output.
* Static fallback is not part of v3, so a first-frame option is unnecessary.
* Timing and frame count are governed by bounded defaults.
* Avoiding new controls keeps the slash-command surface focused and consistent with v2.

---

### 9. Original Image Display

Status: Decided.

Decision:

* `show_original` should behave the same for animated source files as it does for static source files.
* Default remains `true`.
* Original animated source bytes should be reattached when total upload limits allow it.
* Original image display is omitted before generated animated output when limits require it.
* Omission due to delivery limits should be noted in completion text.
* Omission because `show_original=false` must not emit an omission note.

Key considerations:

* Reattaching the original animated file gives useful before-and-after context.
* Animated originals can consume a large share of upload budget.
* Generated artifacts must remain higher priority.

Rationale:

* Keeping `show_original` consistent avoids making animated inputs feel like a separate product surface.
* Original animated input remains contextual; generated animated output is the core artifact.
* Under the default 10,000,000-byte total upload budget, original animated files may be omitted often.
* Frequent omission is acceptable as long as it is explicit when the option was enabled and delivery limits caused the omission.

---

### 10. Failure Language

Status: Decided.

Decision:

* v3 should add animation-specific failure messages that preserve the v1 bot tone: formal, precise, affectless, consistently impersonal, and procedural.
* Existing v2 failure messages should be reused when the condition is not animation-specific.
* v3 distinguishes rejection from failure.

Core distinction:

```text
Reject = known boundary or policy violation.
Fail = unexpected runtime, rendering, encoding, permission, or delivery breakdown.
```

New animation-specific public responses:

| Condition                         | Public response                                                                                 |
| --------------------------------- | ----------------------------------------------------------------------------------------------- |
| Duration too long                 | `The submitted animation exceeds the maximum supported duration. Processing has been rejected.` |
| Generated animation too large     | `The rendered animation exceeds delivery limits. Processing has been rejected.`                 |
| Animation metadata unsupported    | `The submitted animation could not be inspected. Processing has been rejected.`                 |
| Composited frames unavailable     | `The submitted animation could not be inspected. Processing has been rejected.`                 |
| Source-frame safety fuse exceeded | `The submitted animation exceeds processing limits. Processing has been rejected.`              |
| Output-cell cost fuse exceeded    | `The submitted animation exceeds processing limits. Processing has been rejected.`              |
| Animation rendering failure       | `The submitted animation could not be rendered. Processing has failed.`                         |
| Animation encoding failure        | `The submitted animation could not be rendered. Processing has failed.`                         |
| Animation delivery failure        | `The rendered animation could not be delivered. Processing has failed.`                         |

Reuse existing messages:

| Condition             | Public response                                                                                      |
| --------------------- | ---------------------------------------------------------------------------------------------------- |
| Unsupported file type | `The submitted file type is not supported. Processing has been rejected.`                            |
| Source file too large | `The submitted image exceeds the maximum source file size. Processing has been rejected.`            |
| Dimensions too large  | `The submitted image exceeds the maximum supported dimensions. Processing has been rejected.`        |
| Decode failure        | `The submitted image could not be decoded. Processing has been rejected.`                            |
| Permission failure    | `The rendered output could not be delivered due to insufficient permissions. Processing has failed.` |
| Unknown failure       | `Processing failed due to an internal error.`                                                        |

Rationale:

* The messages follow v1's administrative rejection tone.
* They name the failing category without exposing stack traces, exact limits, frame counts, local paths, raw Discord API payloads, or implementation details.
* Animation-specific language is used only where it improves precision.
* The reject/fail distinction helps users and operators understand whether a request exceeded product limits or the system broke.

---

### 11. Loop And Repeat-Count Policy

Status: Decided.

Decision:

* Generated animated WebP output should always use infinite looping.
* Source repeat count should not be preserved or inspected as an acceptance criterion.
* The `LoopCount` field on `AnimatedAsciiRender` is always set to `0` in the v3 baseline.

Key considerations:

* GIF and WebP source files may carry finite repeat counts or infinite looping.
* Discord displays animated attachments inline; users expect them to loop continuously.
* Preserving a finite source repeat count could cause generated renders to stop unexpectedly after n plays in Discord.

Rationale:

* Infinite looping matches the conventional Discord animated-image expectation.
* A generated render that stops after a source-defined number of plays is surprising and offers no user benefit.
* The repeat-count field exists in the render model to allow future policy changes without a structural revision; the v3 default is always infinite.

---

### 12. Composited Frame Requirement

Status: Decided.

Decision:

* Animated input must produce reliable fully composited frames before rendering.
* If reliable composited frames cannot be obtained, the request is rejected.
* v3 does not attempt best-effort compositing.

Rejected conditions include:

* unsupported animation metadata
* unreadable frame timing
* unreliable frame enumeration
* failure to produce full composited frames
* disposal or transparency semantics that cannot be represented reliably by the selected image stack

Rationale:

* ASCIIBot v3 prefers predictable output over maximum format coverage.
* Best-effort compositing can create ghosting, flicker, incorrect transparency, and misleading output.
* Refusing unreliable animation is clearer than producing a render that appears broken.
* This policy keeps the spec and test surface smaller and more trustworthy.

---

### 13. Cost Model And Safety Fuses

Status: Decided.

Decision:

* v3 uses a preflight total-output-cell fuse.
* v3 uses a source-frame safety fuse.
* Both fuse violations are rejections.
* No adaptive degradation is attempted.

Rules:

```text
totalOutputCells = outputWidth * outputHeight * sampledFrameCount
reject if totalOutputCells > 300,000
reject if sourceFrameCount > 1,000
```

Post-render byte enforcement:

```text
reject if generatedAnimatedWebPBytes > configured generated animation byte limit
```

Rationale:

* The bot runs on capable local hardware and is expected to serve fewer than four concurrent users, so limits should not unnecessarily kneecap ordinary animation processing.
* The output-cell fuse protects against future size or frame-count expansion.
* The source-frame fuse protects against pathological inputs that are short in duration but expensive to inspect or decode.
* No adaptive degradation keeps v3 behavior transparent and consistent.

---

## User Controls

v3 should reuse existing controls where possible:

```text
size: small | medium | large
color: on | off
detail: low | normal | high
show_original: true | false
```

Decisions:

* v3 adds no animation-specific controls.
* Users cannot request first-frame-only rendering in the v3 baseline.
* Animation support is automatic when the input is animated and passes v3 limits.
* Existing controls remain: `size`, `color`, `detail`, and `show_original`.

The mental model remains:

```text
size = output budget / Discord footprint
detail = bounded refinement within that budget
animation support = automatic when valid
```

For animated inputs, `size`, `color`, and `detail` apply consistently across all sampled frames.

---

## Failure Modes

v3 should preserve existing validation and delivery failure messages, while replacing the old animated-image rejection with bounded animated support failures.

New animation-specific failure messages:

| Condition                         | Public response                                                                                 |
| --------------------------------- | ----------------------------------------------------------------------------------------------- |
| Duration too long                 | `The submitted animation exceeds the maximum supported duration. Processing has been rejected.` |
| Generated animation too large     | `The rendered animation exceeds delivery limits. Processing has been rejected.`                 |
| Animation metadata unsupported    | `The submitted animation could not be inspected. Processing has been rejected.`                 |
| Composited frames unavailable     | `The submitted animation could not be inspected. Processing has been rejected.`                 |
| Source-frame safety fuse exceeded | `The submitted animation exceeds processing limits. Processing has been rejected.`              |
| Output-cell cost fuse exceeded    | `The submitted animation exceeds processing limits. Processing has been rejected.`              |
| Animation rendering failure       | `The submitted animation could not be rendered. Processing has failed.`                         |
| Animation encoding failure        | `The submitted animation could not be rendered. Processing has failed.`                         |
| Animation delivery failure        | `The rendered animation could not be delivered. Processing has failed.`                         |

Existing messages reused for non-animation-specific conditions:

| Condition             | Public response                                                                                      |
| --------------------- | ---------------------------------------------------------------------------------------------------- |
| Unsupported file type | `The submitted file type is not supported. Processing has been rejected.`                            |
| Source file too large | `The submitted image exceeds the maximum source file size. Processing has been rejected.`            |
| Dimensions too large  | `The submitted image exceeds the maximum supported dimensions. Processing has been rejected.`        |
| Decode failure        | `The submitted image could not be decoded. Processing has been rejected.`                            |
| Permission failure    | `The rendered output could not be delivered due to insufficient permissions. Processing has failed.` |
| Unknown failure       | `Processing failed due to an internal error.`                                                        |

Note: raw source frame count is not a primary rejection criterion. Oversized animations are caught by duration, output-cell cost, delivery size, or the source-frame safety fuse.

All failure language remains formal, precise, affectless, and impersonal.

---

## Compatibility With v1 And v2

v3 should preserve these behaviors unless explicitly superseded:

* `/ascii image:<attachment> [size] [color] [detail] [show_original]` remains available.
* Static image behavior remains unchanged.
* Accepted requests receive visible acknowledgement before rendering completes.
* Validation failures are visible and procedural.
* The rich render model remains the canonical source for export.
* Inline output remains preferred for eligible static renders.
* Non-inline static output still uses PNG plus `.txt` when within delivery limits.
* Original image display remains controlled by `show_original`.
* Generated render artifacts are preserved before optional original image display.
* The bot does not silently split one render across multiple Discord messages.
* The bot remains hobby-scale and Discord-first.
* The bot retains its cold, efficient bureaucratic response tone.

v3 supersedes v2 only for animated GIF and animated WebP handling:

* animated GIF and animated WebP are no longer categorically rejected
* animated inputs use animated WebP output
* animated inputs do not receive inline output
* animated inputs do not receive `.txt` output
* animated inputs do not automatically fall back to static output

---

## Open v3 Idea Queue

This section is intentionally reserved for v3 ideas before they are promoted into decisions.

Candidate areas:

* Animated GIF output as a future compatibility option
* Duplicate-frame collapse
* Animated text-frame export as an explicit future feature
* First-frame-only rendering as an explicit future control
* Manual Discord preview testing
* Implementation verification for ImageSharp animated WebP encoding
* Motion-aware frame sampling
* Optional advanced animation controls
* Configurable animation limit profiles

---

## Remaining Before Spec

The v3 product decisions are substantially closed. The following implementation checks and spec-shaping tasks remain before a v3 implementation specification is considered ready:

* Verify animated WebP output through the selected .NET image stack.
* Verify reliable animated GIF and animated WebP metadata inspection.
* Verify reliable production of fully composited frames for supported animated inputs.
* Confirm animated render model and exporter structure.
* Confirm generated animated WebP dimensions against the existing rendered image ceiling.
* Decide exact configuration variable names for v3 animation limits.
* Define the implementation-level delivery decision tree for animated output.
* Define deterministic test cases for frame sampling, timing normalization, and fuse behavior.
* Conduct manual Discord preview testing for generated animated WebP attachments.

---

## Decision Log

### Animated GIF And WebP Support

**Decision:** v3 should add bounded support for animated GIF and animated WebP inputs.

**Reasoning:** Animated GIFs and WebPs are common Discord image formats. Supporting them is a natural expansion of ASCIIBot's existing image-to-ASCII identity, provided the implementation uses strict limits and preserves clear failure behavior.

---

### Animated WebP Render Output

**Decision:** v3 should use animated WebP as the baseline generated animation format.

**Reasoning:** ASCIIBot optimizes for Discord-native viewing. Animated WebP is the preferred generated artifact because it better preserves color and alpha than GIF and is expected to produce smaller artifacts. Animated GIF output remains a deferred compatibility option rather than part of the v3 baseline.

---

### Animated Delivery Artifact Set

**Decision:** Successful animated conversions should return `asciibot-render.webp` as the only required generated artifact. Animated conversions do not include a `.txt` attachment in the v3 baseline. When `show_original=true`, the original animated source may also be reattached as contextual output if total upload limits allow it.

**Reasoning:** The animated WebP is the Discord-native viewing result and the complete generated artifact for animated input. First-frame text under-represents motion, while all-frame text is bulky, noisy, and less useful for casual Discord sharing. Generated output remains higher priority than optional original image display whenever upload limits constrain the response.

---

### Animated Text Export Deferral

**Decision:** Text export for animated renders is deferred.

**Reasoning:** v3 should not force a weak `.txt` representation onto animation. Static render delivery keeps the v2 PNG plus `.txt` behavior, but animated delivery intentionally uses the animated visual artifact as the complete output. Future explicit text-frame export can be considered separately if a real use case appears.

---

### Time-Based Frame Sampling

**Decision:** v3 should sample animated input by deterministic uniform time sampling, bounded by a maximum output frame count and maximum source animation duration. Raw source frame count should be inspected and logged, but it should not be the primary acceptance model by itself.

Sampling rules:

```text
targetInterval = 100 ms
maxFrames = 48
frameCount = min(maxFrames, floor(duration / targetInterval))
```

If `duration < 100 ms`, `frameCount = 1`.

```text
sampleTime[i] = i * (duration / frameCount)
for i = 0 .. frameCount - 1
```

For each sample timestamp, choose the nearest composited source frame. If two frames are equally close, choose the earlier frame.

**Reasoning:** Animation duration is the user-visible property that best describes the workload and expected result. Time-based sampling lets short high-frame-count animations remain eligible without rendering every source frame, while a maximum output frame count bounds rendering and encoding cost. Very long animations remain outside v3 scope and should be rejected rather than silently compressed into a misleading summary.

---

### Timing Reconstruction And Minimum Delay

**Decision:** v3 should derive output frame durations from sample intervals and clamp any output delay below 100 ms upward.

Timing rules:

```text
duration[i] = sampleTime[i + 1] - sampleTime[i]
duration[last] = duration[last - 1]
duration[i] = max(duration[i], 100 ms)
```

**Reasoning:** Approximate timing keeps the generated render recognizably connected to the source animation, while a minimum delay prevents pathological fast frames from bloating output or playing poorly in Discord. Deriving timing from the sample grid keeps v3 predictable and testable.

---

### Animation Limit Defaults

**Decision:** v3 should default to a 12-second maximum source animation duration, 48 maximum output frames, 100 ms minimum output frame delay, 300,000 maximum total output cells, and 1,000 maximum source frames as a defensive safety fuse. Static source byte and decoded-dimension limits remain 10 MiB and 4096x4096. Generated animated WebP output remains capped at 8 MiB within a 10,000,000-byte default total upload budget.

**Reasoning:** The expected deployment is a personal local bot running on a high-end desktop with fewer than four concurrent users. These defaults give short Discord animations enough room to remain recognizable while keeping rendering, encoding, and delivery bounded. Upload limits remain conservative because Discord delivery, not local compute, is the hard external constraint.

---

### No Automatic Static Fallback

**Decision:** v3 should not automatically fall back to a static first-frame render when animated output cannot be delivered. First-frame-only rendering is deferred unless later introduced as an explicit user control.

**Reasoning:** Animated input creates an expectation of animated output. Returning a static substitute would deliver something the user did not ask for and could obscure why the request exceeded limits. A visible rejection is clearer and lets the user resubmit a smaller or shorter source.

---

### No Animation-Specific Controls

**Decision:** v3 should not add animation-specific command options. Animated input should automatically use animated rendering when it passes v3 limits. Existing controls continue to apply: `size`, `color`, `detail`, and `show_original`.

**Reasoning:** The baseline behavior is simple and predictable: animated input produces animated output. Timing, duration, and frame count are governed by bounded defaults, and static fallback is not part of v3. Avoiding new controls keeps the command surface focused and consistent with v2.

---

### Original Animated Source Display

**Decision:** `show_original` should behave the same for animated source files as it does for static source files. The default remains `true`, and the original animated source should be reattached when total upload limits allow it. If the original is omitted due to delivery limits, completion text should note the omission.

**Reasoning:** Keeping `show_original` consistent avoids creating a separate option model for animated inputs. The original source remains contextual, while generated animated output is the core artifact. Under the default 10,000,000-byte total upload budget, original animated files may be omitted often; that is acceptable as long as the omission is explicit when the option was enabled.

---

### Animation Failure Language

**Decision:** v3 should add animation-specific failure messages while preserving the v1 bot tone: formal, precise, affectless, impersonal, and procedural. v3 should distinguish rejection from failure.

**Reasoning:** Animation support adds new rejection states, but the bot's voice should remain consistent. User-facing messages should identify the failing category without exposing internal limits, stack traces, raw API payloads, local paths, or implementation details. The reject/fail distinction gives the specification a clean way to separate product-boundary violations from system breakdowns.

---

### Infinite Loop Output

**Decision:** Generated animated WebP output should always use infinite looping. Source repeat count is not preserved.

**Reasoning:** Discord users expect animated images to loop continuously. Preserving finite source repeat counts would cause generated renders to stop unexpectedly in Discord after a source-defined number of plays, offering no benefit and creating a confusing experience. The `LoopCount` field is always 0 in the v3 baseline.

---

### Reliable Composited Frames Required

**Decision:** Animated input must yield reliable fully composited frames. If reliable composited frames cannot be obtained, processing is rejected.

**Reasoning:** ASCIIBot v3 values predictable output over broad best-effort compatibility. Rendering raw deltas, guessing disposal behavior, or ignoring transparency errors can produce misleading output. Rejection is clearer than generating an animation that appears broken.

---

### Cost Fuses

**Decision:** v3 should use a maximum total output-cell fuse of 300,000 and a source-frame safety fuse of 1,000. Violations are rejected. No adaptive degradation or retry is attempted.

**Reasoning:** Local hardware and low expected concurrency allow more breathing room than the initial conservative fuse values. The selected limits avoid unnecessary kneecapping while retaining clear operational boundaries. Delivery byte limits remain the tighter external constraint.

---

## Next Pass Focus

The v3 product decisions are closed. The next pass is the v3 implementation specification and implementation verification:

* Translate settled product decisions into concrete implementation guidance.
* Verify animated WebP output through the selected .NET image stack.
* Verify reliable animation metadata inspection for GIF and WebP.
* Verify reliable fully composited frame production.
* Define exact configuration variable names and defaults.
* Define animated validation, sampling, rendering, encoding, and delivery decision trees.
* Confirm animated render model and exporter structure.
* Conduct manual Discord preview testing for generated animated WebP attachments.
* Confirm generated animated WebP dimensions against the existing rendered image ceiling.
* Add automated tests for frame sampling, timing normalization, reject/fail classification, compositing rejection, output-cell fuse behavior, source-frame safety fuse behavior, and animated delivery limits.

---

*Last updated: 2026-05-02*