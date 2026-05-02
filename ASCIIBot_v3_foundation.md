# ASCIIBot - v3 Foundation Document

> This is a living document for ASCIIBot v3. It captures direction, product principles, open questions, and early decisions before they are translated into a formal v3 implementation specification.

---

## Purpose

v1 proved the core ASCIIBot loop: a Discord user submits a static image, the bot acknowledges the request, converts the image into ASCII-oriented output, and returns the result in Discord.

v2 improved completed render artifacts by adding richer render output, PNG delivery, detail control, and original image display.

v3 begins from a new foundation: support animated GIF and animated WebP inputs.

ASCIIBot remains a small, Discord-native image-to-ASCII bot. The purpose of v3 is not to become a general animation editor or media pipeline. It is to decide how animated source images can be accepted, rendered, bounded, and delivered while preserving the bot's existing reliability, clarity, and procedural tone.

---

## Product Philosophy

**Accept motion only when the result stays understandable.**

Animated input is a meaningful expansion because GIFs and WebPs are common Discord image formats. However, animation can easily create large outputs, slow processing, unclear delivery behavior, and surprising failures.

v3 should prefer explicit limits, predictable degradation, and clear rejection over trying to support every possible animation.

The guiding question for v3 is:

> What is the smallest useful animated-image workflow that feels native to ASCIIBot?

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

---

## v3 Non-Goals

- Not a general-purpose animation editor
- Not arbitrary video support
- Not MP4, MOV, WebM, or APNG support by default
- Not audio support
- Not frame-by-frame user editing
- Not animation compositing or effects
- Not multi-image batch processing
- Not a hosted web gallery
- Not persistence, user accounts, analytics, or moderation
- Not unlimited-duration or unlimited-frame animation processing

---

## Animated Input Direction

v1 and v2 reject animated GIF and animated WebP inputs. v3 should replace that rejection with bounded support.

Candidate interpretation:

| Input | v1/v2 behavior | v3 direction |
|---|---|---|
| Static GIF | Accepted | Preserve existing behavior |
| Animated GIF | Rejected | Accept within frame, duration, size, and output limits |
| Static WebP | Accepted | Preserve existing behavior |
| Animated WebP | Rejected | Accept within frame, duration, size, and output limits |

v3 should continue to validate by file contents rather than filename extension alone.

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

---

## Frame Sampling And Timing

Animated images introduce two separate constraints:

- how many source frames are sampled
- how frame timing is preserved or normalized

Decisions:

- v3 samples animated input by time rather than rendering every source frame.
- v3 emits no more than 48 output frames.
- v3 rejects source animations longer than 12 seconds.
- v3 clamps output frame delays below 100 ms upward.
- v3 should preserve frame disposal and transparency behavior according to decoded image semantics where ImageSharp provides reliable composited frames.
- Duplicate-frame collapse is deferred.

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

- Every frame in one animated render should share the same width and height.
- `size`, `color`, and `detail` should apply consistently across all frames.
- Static inputs should remain representable as a single rich render.
- Exporters should consume render models rather than resampling source pixels independently.

---

## Delivery Policy

v3 should preserve v2 delivery for static images.

Animated delivery uses attachment delivery.

Decisions:

- Animated inputs do not produce inline text output in v3.
- Animated outputs do not include a `.txt` artifact in v3.
- `show_original=true` includes the original animated file only when total upload limits allow it.
- Original image display is omitted before generated animated output when limits require it.
- Static fallback is not allowed automatically.
- Animated output that cannot fit as a single previewable attachment is rejected or failed visibly.

---

## Limits And Safety

Animated inputs need stricter operational boundaries than static inputs.

Candidate limits to define:

| Limit | Purpose |
|---|---|
| Maximum source byte size | Preserve v1/v2 download safety |
| Maximum decoded dimensions | Preserve v1/v2 image safety |
| Maximum frame count | Bound CPU and memory |
| Maximum animation duration | Bound output size and user expectations |
| Maximum generated animation byte size | Fit Discord upload constraints |
| Maximum generated animation dimensions | Keep preview usable |
| Minimum frame delay | Avoid pathological timing |
| Maximum total sampled pixels | Bound rendering cost across all frames |
| Maximum total upload size | Keep default responses compatible with non-Nitro upload limits |

Default values for all limits above are settled in the Animation Limits decision track.

---

## Decision Tracks

This section tracks the decisions that need to be made before v3 can become an implementation specification. Candidate answers may be recorded here first, then promoted into the decision log once accepted.

### 1. Primary Animated Output Format

Status: Decided.

Decision:

- Generated animated ASCII should be delivered as animated WebP in the v3 baseline.

Key considerations:

- Discord preview behavior matters more than theoretical format quality.
- GIF is broadly previewable but has limited color depth and weaker compression.
- WebP is now a Discord-supported animated attachment and embed format.
- The output format should be easy to generate and validate in the .NET image stack.

Rationale:

- ASCIIBot is Discord-first, and Discord now treats animated WebP as a first-class animated image format for attachments and embeds.
- WebP better preserves color and alpha than GIF and should usually produce smaller generated artifacts.
- The current ImageSharp package exposes WebP encoder and animation metadata APIs, making WebP output plausible without introducing a separate native toolchain at the foundation stage.
- Animated GIF output can remain a deferred compatibility option if implementation testing exposes a WebP blocker.

### 2. Animated Delivery Artifacts

Status: Decided.

Decision:

- A successful animated conversion should return a generated animated WebP render as the primary artifact.
- A successful animated conversion may include the original animated source when `show_original=true` and total upload limits allow it.
- Generated artifacts take priority over original image display.

Baseline artifact set:

```text
asciibot-render.webp
asciibot-original.<ext>
```

Notes:

- Animated conversions do not include a `.txt` attachment in the v3 baseline.
- Text export for animated renders is deferred.
- The original source is contextual and must be omitted before generated output when limits require it.

Rationale:

- Animated WebP is the complete Discord-native viewing artifact for animated input.
- A first-frame `.txt` attachment under-represents the animation.
- An all-frame `.txt` attachment would be bulky, noisy, and less useful for casual Discord sharing.
- Omitting animated `.txt` output keeps v3 delivery clear while leaving explicit animated text export as a possible future feature.

### 3. Animated Text Export Semantics

Status: Decided.

Decision:

- Animated renders do not include `.txt` output in the v3 baseline.
- Animated text export is deferred.

Candidate approaches:

| Approach | Strength | Concern |
|---|---|---|
| First frame only | Simple and bounded | Does not represent the animation |
| All sampled frames with separators | Honest and complete | Can become large and noisy |
| No `.txt` for animated renders | Clean delivery | Breaks v2's copyable-artifact habit |

Rationale:

- First-frame text is incomplete for an animated source.
- All-frame text is likely to be large, awkward to read, and poorly aligned with Discord-native viewing.
- v3 explicitly supersedes v2's non-inline `.txt` habit for animated renders only. Static render delivery remains unchanged.

### 4. Frame Selection

Status: Decided.

Decision:

- v3 should use time-based frame sampling for animated inputs.
- v3 should enforce a maximum output frame count.
- v3 should enforce a maximum source animation duration.
- Raw source frame count should inform validation and logging, but should not be the primary acceptance model by itself.

Key considerations:

- GIF and WebP frame counts can be high even for short animations.
- Frame timing can vary.
- Rendering every frame is conceptually simple but can produce excessive work.
- Time-based sampling gives predictable output cost but may skip source frames.

Rationale:

- Time-based sampling treats animation duration as the user-visible quantity that matters most.
- Maximum output frame count bounds rendering and encoding work.
- A short high-frame-count animation can still be accepted without rendering every source frame.
- Very long animations remain outside v3 scope and should be rejected rather than silently reduced into a misleading summary.

Implications:

- v3 must define exact source-duration and output-frame-count limits.
- Sampling should be deterministic.
- Frame timing decisions are handled in the timing normalization track.

### 5. Timing Normalization

Status: Decided.

Decision:

- v3 should preserve approximate source animation timing for accepted animated inputs.
- v3 should define a minimum output frame delay.
- Output frame delays below the minimum should be clamped upward.
- Longer source pauses should be preserved when they fit within the accepted animation duration.
- The minimum output frame delay is settled at 100 ms in the Animation Limits decision track.

Key considerations:

- Some animated images use very small frame delays.
- Extremely fast frames may render poorly in Discord and inflate output size.
- Preserving exact source timing may be less important than producing stable previewable output.

Rationale:

- Approximate timing preserves the character of the source animation better than fixed-rate output.
- A minimum delay prevents pathological fast frames from inflating output size or rendering poorly in Discord.
- Clamping very short delays is less surprising than rejecting otherwise valid short animations only because of source timing quirks.

### 6. Animation Limits

Status: Decided.

Decision:

- Maximum source byte size remains 10 MiB.
- Maximum decoded dimensions remain 4096x4096.
- Maximum source animation duration defaults to 12 seconds.
- Maximum output frame count defaults to 48 frames.
- Minimum output frame delay defaults to 100 ms.
- Maximum generated animated WebP byte size defaults to 8 MiB.
- Maximum total upload size defaults to 10,000,000 bytes.
- Generated animated WebP dimensions should use the existing rendered image dimension ceiling unless implementation testing shows animation-specific constraints are needed.
- The implementation should include an operational source-frame safety cap as a defensive fuse, but raw source frame count is not the primary product acceptance model.

Candidate limits to settle:

| Limit | Candidate Direction |
|---|---|
| Maximum source byte size | Keep v1/v2 10 MiB ceiling |
| Maximum decoded dimensions | Keep v1/v2 4096x4096 ceiling |
| Maximum output frames | 48 |
| Maximum source duration | 12 seconds |
| Minimum output frame delay | 100 ms |
| Maximum generated animation byte size | Default to 8 MiB |
| Maximum generated animation dimensions | Reuse existing rendered image dimension ceiling |
| Maximum total sampled pixels | Derive from output grid and output frame count |
| Maximum total upload size | Default to 10,000,000 bytes |

Rationale:

- The expected deployment is a personal local bot on a high-end desktop with fewer than four concurrent users.
- A 12-second duration and 48-frame output cap allow recognizable short Discord animations without turning ASCIIBot into a general video renderer.
- A 100 ms minimum delay keeps ASCII motion readable and avoids pathological rapid-frame output.
- Discord-facing upload limits remain conservative and non-Nitro-compatible.
- Source byte and decoded-dimension limits continue to protect the bot from unusually large inputs before animation work begins.

### 7. Static Fallback

Status: Decided.

Decision:

- v3 should not automatically fall back to static output when animated output cannot be delivered.
- If an animated input is accepted for animated rendering but the generated animated output exceeds limits or cannot be delivered, the request should be rejected or failed with the appropriate visible message.
- First-frame-only rendering is deferred unless introduced later as an explicit user control.

Key considerations:

- Static fallback can save some user requests.
- It may also surprise users who expected motion.
- A visible note could make fallback understandable.

Rationale:

- Animated input creates an expectation of animated output.
- A static fallback may deliver something the user did not ask for.
- Rejecting is clearer than jumping through delivery hoops for a compromised result.
- Users can resubmit with a smaller, shorter, or otherwise adjusted source if needed.

### 8. User Controls

Status: Decided.

Decision:

- v3 should not add animation-specific command options.
- Animated input should automatically use animated rendering when it passes v3 limits.
- Existing controls continue to apply: `size`, `color`, `detail`, and `show_original`.

Candidate controls:

```text
animation: animated | first_frame
speed: original | normalized
frames: low | normal | high
```

Rationale:

- The baseline behavior is simple: animated input produces animated output.
- Static fallback is not part of v3, so a first-frame option is unnecessary.
- Timing and frame count are governed by bounded defaults.
- Avoiding new controls keeps the slash-command surface focused and consistent with v2.

### 9. Original Image Display

Status: Decided.

Decision:

- `show_original` should behave the same for animated source files as it does for static source files.
- Default remains `true`.
- Original animated source bytes should be reattached when total upload limits allow it.
- Original image display is omitted before generated animated output when limits require it.
- Omission due to delivery limits should be noted in completion text.
- Omission because `show_original=false` must not emit an omission note.

Key considerations:

- Reattaching the original animated file gives useful before-and-after context.
- Animated originals can consume a large share of upload budget.
- Generated artifacts must remain higher priority.

Rationale:

- Keeping `show_original` consistent avoids making animated inputs feel like a separate product surface.
- Original animated input remains contextual; generated animated output is the core artifact.
- Under the default 10,000,000-byte total upload budget, original animated files are expected to be omitted often.
- Frequent omission is acceptable as long as it is explicit when the option was enabled and delivery limits caused the omission.

### 10. Failure Language

Status: Decided.

Decision:

- v3 should add animation-specific failure messages that preserve the v1 bot tone: formal, precise, affectless, consistently impersonal, and procedural.
- Existing v2 failure messages should be reused when the condition is not animation-specific.

Candidate failure conditions:

| Condition | Public response |
|---|---|
| Duration too long | `The submitted animation exceeds the maximum supported duration. Processing has been rejected.` |
| Generated animation too large | `The rendered animation exceeds delivery limits. Processing has been rejected.` |
| Animation metadata unsupported | `The submitted animation could not be inspected. Processing has been rejected.` |
| Source-frame safety fuse exceeded | `The submitted animation exceeds processing limits. Processing has been rejected.` |
| Animation rendering failure | `The submitted animation could not be rendered. Processing has failed.` |
| Animation delivery failure | `The rendered animation could not be delivered. Processing has failed.` |

Reuse existing messages:

- Unsupported file type: `The submitted file type is not supported. Processing has been rejected.`
- Source file too large: `The submitted image exceeds the maximum source file size. Processing has been rejected.`
- Dimensions too large: `The submitted image exceeds the maximum supported dimensions. Processing has been rejected.`
- Decode failure: `The submitted image could not be decoded. Processing has been rejected.`
- Permission failure: `The rendered output could not be delivered due to insufficient permissions. Processing has failed.`
- Unknown failure: `Processing failed due to an internal error.`

Rationale:

- The messages follow v1's administrative rejection tone.
- They name the failing category without exposing stack traces, exact limits, frame counts, local paths, raw Discord API payloads, or implementation details.
- Animation-specific language is used only where it improves precision.

### 11. Loop And Repeat-Count Policy

Status: Decided.

Decision:

- Generated animated WebP output should always use infinite looping (repeat count 0).
- Source repeat count should not be preserved or inspected as an acceptance criterion.
- The `LoopCount` field on `AnimatedAsciiRender` is always set to 0 in the v3 baseline.

Key considerations:

- GIF and WebP source files may carry finite repeat counts (play n times then stop) or infinite looping.
- Discord displays animated attachments inline; users expect them to loop continuously.
- Preserving a finite source repeat count would cause generated renders to stop unexpectedly after n plays in Discord.

Rationale:

- Infinite looping matches the conventional Discord animated-image expectation.
- A generated render that stops after a source-defined number of plays is surprising and offers no user benefit.
- The repeat-count field exists in the render model to allow future policy changes without a structural revision; the v3 default is always infinite.

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

- v3 adds no animation-specific controls.
- Users cannot request first-frame-only rendering in the v3 baseline.
- Animation support is automatic when the input is animated and passes v3 limits.
- Existing controls remain: `size`, `color`, `detail`, and `show_original`.

---

## Failure Modes

v3 should preserve existing validation and delivery failure messages, while replacing the old animated-image rejection with more specific animated support failures.

New animation-specific failure messages:

| Condition | Public response |
|---|---|
| Duration too long | `The submitted animation exceeds the maximum supported duration. Processing has been rejected.` |
| Generated animation too large | `The rendered animation exceeds delivery limits. Processing has been rejected.` |
| Animation metadata unsupported | `The submitted animation could not be inspected. Processing has been rejected.` |
| Source-frame safety fuse exceeded | `The submitted animation exceeds processing limits. Processing has been rejected.` |
| Animation rendering failure | `The submitted animation could not be rendered. Processing has failed.` |
| Animation delivery failure | `The rendered animation could not be delivered. Processing has failed.` |

Existing messages reused for non-animation-specific conditions:

| Condition | Public response |
|---|---|
| Unsupported file type | `The submitted file type is not supported. Processing has been rejected.` |
| Source file too large | `The submitted image exceeds the maximum source file size. Processing has been rejected.` |
| Dimensions too large | `The submitted image exceeds the maximum supported dimensions. Processing has been rejected.` |
| Decode failure | `The submitted image could not be decoded. Processing has been rejected.` |
| Permission failure | `The rendered output could not be delivered due to insufficient permissions. Processing has failed.` |
| Unknown failure | `Processing failed due to an internal error.` |

Note: raw source frame count is not a primary rejection criterion. Oversized animations are caught by the duration limit or the source-frame safety fuse, not by a dedicated frame-count message.

All failure language remains formal, precise, affectless, and impersonal.

---

## Compatibility With v1 And v2

v3 should preserve these behaviors unless explicitly superseded:

- `/ascii image:<attachment> [size] [color] [detail] [show_original]` remains available.
- Static image behavior remains unchanged.
- Accepted requests receive visible acknowledgement before rendering completes.
- Validation failures are visible and procedural.
- The rich render model remains the canonical source for export.
- Inline output remains preferred for eligible static renders.
- Non-inline static output still uses PNG plus `.txt` when within delivery limits.
- Original image display remains controlled by `show_original`.
- Generated render artifacts are preserved before optional original image display.
- The bot does not silently split one render across multiple Discord messages.
- The bot remains hobby-scale and Discord-first.

---

## Open v3 Idea Queue

This section is intentionally reserved for v3 ideas before they are promoted into decisions.

Candidate areas:

- Animated GIF output as a future compatibility option
- Duplicate-frame collapse
- Animated text-frame export as an explicit future feature
- First-frame-only rendering as an explicit future control
- Animated render model structure
- Manual Discord preview testing
- Implementation verification for ImageSharp animated WebP encoding

---

## Remaining Before Spec

The v3 product decisions are closed. The following implementation checks and spec-shaping tasks remain before a v3 implementation specification is considered ready:

- Whether implementation testing confirms animated WebP output through the selected .NET image stack.
- Whether generated animated WebP dimensions should reuse the existing rendered image ceiling without additional animation-specific constraints.
- The exact source-frame safety fuse value.
- The exact animated render model and exporter structure.
- Manual Discord preview testing for generated animated WebP attachments.

---

## Decision Log

### Animated GIF And WebP Support

**Decision:** v3 should add bounded support for animated GIF and animated WebP inputs.

**Reasoning:** Animated GIFs and WebPs are common Discord image formats. Supporting them is a natural expansion of ASCIIBot's existing image-to-ASCII identity, provided the implementation uses strict limits and preserves clear failure behavior.

### Animated WebP Render Output

**Decision:** v3 should use animated WebP as the baseline generated animation format.

**Reasoning:** ASCIIBot optimizes for Discord-native viewing. Animated WebP is supported by Discord for attachments and embeds, preserves color and alpha better than GIF, and is expected to produce smaller artifacts. Animated GIF output remains a deferred compatibility option rather than part of the v3 baseline.

### Animated Delivery Artifact Set

**Decision:** Successful animated conversions should return `asciibot-render.webp` as the only required generated artifact. Animated conversions do not include a `.txt` attachment in the v3 baseline. When `show_original=true`, the original animated source may also be reattached as contextual output if total upload limits allow it.

**Reasoning:** The animated WebP is the Discord-native viewing result and the complete generated artifact for animated input. First-frame text under-represents motion, while all-frame text is bulky, noisy, and less useful for casual Discord sharing. Generated output remains higher priority than optional original image display whenever upload limits constrain the response.

### Animated Text Export Deferral

**Decision:** Text export for animated renders is deferred.

**Reasoning:** v3 should not force a weak `.txt` representation onto animation. Static render delivery keeps the v2 PNG plus `.txt` behavior, but animated delivery intentionally uses the animated visual artifact as the complete output. Future explicit text-frame export can be considered separately if a real use case appears.

### Time-Based Frame Sampling

**Decision:** v3 should sample animated input by time, bounded by a maximum output frame count and maximum source animation duration. Raw source frame count should be inspected and logged, but it should not be the primary acceptance model by itself.

**Reasoning:** Animation duration is the user-visible property that best describes the workload and expected result. Time-based sampling lets short high-frame-count animations remain eligible without rendering every source frame, while a maximum output frame count bounds rendering and encoding cost. Very long animations remain outside v3 scope and should be rejected rather than silently compressed into a misleading summary.

### Approximate Timing Preservation

**Decision:** v3 should preserve approximate source animation timing while clamping output frame delays below a defined minimum. Longer pauses should be preserved when they fit within the accepted animation duration.

**Reasoning:** Approximate timing keeps the generated render recognizably connected to the source animation, while a minimum delay prevents pathological fast frames from bloating output or playing poorly in Discord. Exact delay values belong in the implementation specification after the v3 limit defaults are selected.

### Non-Nitro-Compatible Upload Budget

**Decision:** v3's default total upload budget should be 10,000,000 bytes, matching Discord's current non-Nitro file sharing limit. The generated animated WebP should default to an 8 MiB byte limit.

**Reasoning:** ASCIIBot should work in ordinary Discord servers without assuming Nitro or boosted upload limits. Capping generated animated WebP output below the full response budget leaves headroom for multipart overhead, message metadata, and optional original image display when the original is small enough to fit.

### Animation Limit Defaults

**Decision:** v3 should default to a 12-second maximum source animation duration, 48 maximum output frames, and a 100 ms minimum output frame delay. Static source byte and decoded-dimension limits remain 10 MiB and 4096x4096. Generated animated WebP output remains capped at 8 MiB within a 10,000,000-byte default total upload budget.

**Reasoning:** The expected deployment is a personal local bot running on a high-end desktop with fewer than four concurrent users. These defaults give short Discord animations enough room to remain recognizable while keeping rendering, encoding, and delivery bounded. Upload limits remain conservative because Discord delivery, not local compute, is the hard external constraint.

### No Automatic Static Fallback

**Decision:** v3 should not automatically fall back to a static first-frame render when animated output cannot be delivered. First-frame-only rendering is deferred unless later introduced as an explicit user control.

**Reasoning:** Animated input creates an expectation of animated output. Returning a static substitute would deliver something the user did not ask for and could obscure why the request exceeded limits. A visible rejection is clearer and lets the user resubmit a smaller or shorter source.

### No Animation-Specific Controls

**Decision:** v3 should not add animation-specific command options. Animated input should automatically use animated rendering when it passes v3 limits. Existing controls continue to apply: `size`, `color`, `detail`, and `show_original`.

**Reasoning:** The baseline behavior is simple and predictable: animated input produces animated output. Timing, duration, and frame count are governed by bounded defaults, and static fallback is not part of v3. Avoiding new controls keeps the command surface focused and consistent with v2.

### Original Animated Source Display

**Decision:** `show_original` should behave the same for animated source files as it does for static source files. The default remains `true`, and the original animated source should be reattached when total upload limits allow it. If the original is omitted due to delivery limits, completion text should note the omission.

**Reasoning:** Keeping `show_original` consistent avoids creating a separate option model for animated inputs. The original source remains contextual, while generated animated output is the core artifact. Under the default 10,000,000-byte total upload budget, original animated files are expected to be omitted often; that is acceptable as long as the omission is explicit when the option was enabled.

### Animation Failure Language

**Decision:** v3 should add animation-specific failure messages while preserving the v1 bot tone: formal, precise, affectless, impersonal, and procedural.

**Reasoning:** Animation support adds new rejection states, but the bot's voice should remain consistent. User-facing messages should identify the failing category without exposing internal limits, stack traces, raw API payloads, local paths, or implementation details.

### Infinite Loop Output

**Decision:** Generated animated WebP output should always use infinite looping (repeat count 0). Source repeat count is not preserved.

**Reasoning:** Discord users expect animated images to loop continuously. Preserving finite source repeat counts would cause generated renders to stop unexpectedly in Discord after a source-defined number of plays, offering no benefit and creating a confusing experience. The `LoopCount` field is always 0 in the v3 baseline.

---

## Next Pass Focus

The v3 product decisions are closed. The next pass is the v3 implementation specification and implementation verification:

- Translate settled product decisions into concrete implementation guidance.
- Verify animated WebP output through the selected .NET image stack.
- Confirm animated render model and exporter structure.
- Settle the exact source-frame safety fuse value.
- Conduct manual Discord preview testing for generated animated WebP attachments.
- Confirm generated animated WebP dimensions against the existing rendered image ceiling.

---

*Last updated: 2026-05-02*
