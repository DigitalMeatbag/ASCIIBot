# ASCIIBot - v4 Foundation Document

> This is a living document for ASCIIBot v4. It captures direction, product principles, open questions, and early decisions before they are translated into a formal v4 implementation specification.

---

## Purpose

v1 proved the core ASCIIBot loop: a Discord user submits a static image, the bot acknowledges the request, converts the image into ASCII-oriented output, and returns the result in Discord.

v2 improved completed static render artifacts by adding richer render output, PNG delivery, detail control, and original image display.

v3 added bounded animated GIF and animated WebP support, with generated animated WebP output.

v4 has two connected goals:

- make ASCIIBot fully Dockerized as a supported deployment path
- accept the MP4 files produced when Discord built-in GIFs are saved locally, without requiring the user to convert them first

The MP4 goal is not a broad expansion into general video processing. It exists to close a practical Discord workflow gap: a user saves what Discord presents as a GIF-like animation, receives an `.mp4` file, and should be able to submit that saved file directly to ASCIIBot.

ASCIIBot remains a small, Discord-native image-to-ASCII bot. v4 should preserve the existing product identity while making deployment and Discord-originated animated intake more reliable.

---

## Product Philosophy

**Package the bot like an appliance; accept MP4 only as a Discord animation compatibility format.**

Dockerization should make ASCIIBot easier to run repeatably, especially when native media tooling is required. MP4 support should be scoped as an input compatibility layer for short, silent, Discord-style animations.

The guiding question for v4 is:

> How can ASCIIBot reliably accept Discord-saved GIF-like MP4 files without becoming a general video bot?

v4 should favor explicit limits, deterministic frame sampling, and clear rejection over broad codec support, adaptive degradation, or media-pipeline sprawl.

---

## v4 Goals

- Provide a complete Docker-based build and runtime path.
- Package all runtime dependencies required by the bot, including media decoding tooling if selected.
- Preserve local non-Docker development where practical.
- Accept bounded MP4 input as an animated source format.
- Route accepted MP4 input through the existing animated ASCII render model.
- Produce the same generated animated WebP output used for v3 animated GIF and WebP inputs.
- Preserve v3 static and animated GIF/WebP behavior unless explicitly superseded.
- Keep all acknowledgement, validation, concurrency, delivery, rejection, failure, and tone guarantees.
- Keep MP4 processing short, silent, bounded, and Discord-workflow-oriented.
- Keep the bot local-first, hobby-scale, and Discord-native.

---

## v4 Non-Goals

- Not general-purpose video support
- Not MP4 editing, trimming, clipping, filters, or effects
- Not audio extraction, analysis, preservation, or output
- Not YouTube, streaming URL, or remote video ingestion
- Not support for arbitrary long videos
- Not adaptive video degradation
- Not automatic quality, frame-count, or size reduction after limits are exceeded
- Not a hosted media processing service
- Not a web UI, gallery, account system, analytics system, or moderation workflow
- Not a replacement for v3 animated GIF and animated WebP behavior

Potential future formats such as WebM, MOV, or APNG remain deferred unless a later product decision promotes them.

---

## Dockerization Direction

v4 should make Docker a first-class supported deployment path rather than a README sketch.

Expected Docker behavior:

- build ASCIIBot from source
- run the bot as a long-lived process
- accept configuration through environment variables
- include required font assets
- include required native media dependencies if MP4 support depends on them
- keep the Discord token outside the image
- remain suitable for a personal server or local machine deployment

The Docker image should not require users to install media tools on the host machine.

### Docker Success Criteria Direction

"Fully Dockerized" should mean more than "a Dockerfile exists."

The v4 specification should define a concrete Docker contract:

- one documented command builds the image from a clean checkout
- one documented command runs the bot with environment-based configuration
- the Discord token is supplied at runtime, never baked into the image
- required font assets are present in the runtime image
- required media decoding tooling is present in the runtime image
- the bot can verify media decoder availability during startup or capability initialization
- the final runtime image excludes SDK/build-only files and tools unless explicitly required
- the README documents Docker build, run, logs, and common configuration
- Docker-based validation is part of the v4 acceptance path

This contract should be tightened in the implementation specification after the runtime base image, decoder dependency, and startup behavior are decided.

### Docker Artifacts

Likely v4 artifacts:

- `Dockerfile`
- `.dockerignore`
- `docker-compose.yml`
- README updates for Docker build and run

### Runtime Image Policy

The runtime image should be boring and predictable:

- use official .NET SDK and runtime images
- use a Debian-based runtime image so FFmpeg can be installed through the distribution package manager
- install only required OS packages
- avoid embedding secrets
- avoid development-only tools in the final runtime image
- run the final container as a non-root user
- expose no network service ports unless the bot introduces one, which v4 should not do

### Open Docker Questions

No major Docker product questions remain. Implementation details should be settled in the specification.

---

## MP4 Input Direction

v4 should accept MP4 as an animated source only when it fits the same product envelope as v3 animated inputs.

The primary use case is:

```text
User saves a built-in Discord GIF-like animation.
Discord stores it locally as an MP4 file.
User submits that MP4 to /ascii.
ASCIIBot renders it as animated ASCII and returns an animated WebP.
```

MP4 support should be implemented as an intake compatibility layer:

- inspect the MP4 container
- reject unsupported, excessive, or unsafe inputs
- decode sampled video frames
- ignore or reject audio
- convert decoded frames into the existing animated render path
- export generated animated WebP as the successful output artifact

v4 should not expose an MP4-specific command option. Routing should remain automatic based on content validation.

---

## Discord Media Proxy And Invocation Direction

The Windows desktop Discord client can expose GIF-picker media through a right-click media action:

```text
Copy Media Link
```

For at least some GIF-picker results, the copied media link points to a Discord external media proxy URL whose underlying asset is an MP4 file, for example:

```text
https://images-ext-1.discordapp.net/external/.../https/media.tenor.com/.../dog-golden-retriever.mp4
```

This confirms that MP4 support is directly tied to the user-visible Discord GIF workflow.

### Link Lifetime And Reliability

Discord documents signed attachment CDN URLs with `ex`, `is`, and `hm` parameters. The `ex` value is a hex expiry timestamp, and Discord refreshes attachment URLs that appear inside the client.

The observed `images-ext-*.discordapp.net/external/...` URL is not the same shape as a signed attachment CDN URL. v4 should not assume that these copied media links are durable, stable, or suitable for long-term storage.

Product stance:

- copied Discord media links may be useful as immediate input
- copied media links should not be persisted as durable source references
- ASCIIBot should download and validate media immediately when such a link is submitted
- failures caused by expired, inaccessible, or changed proxy URLs should be visible and procedural

### Better Invocation Path

The best user experience may be a message context command:

```text
Right-click message -> Apps -> ASCII this
```

Discord message commands target an existing message and can expose resolved message data. Message objects may include attachments and embeds, and video embeds may include both source `url` and proxied `proxy_url` fields.

### Intent And Permission Posture

ASCIIBot currently avoids requiring Message Content intent. v4 should preserve that posture unless a deliberate product decision supersedes it.

**Spike result (2026-05-02):** The no-Message-Content-intent implementation spike is complete and proves that message context commands are viable without that intent.

The official Discord gateway documentation explicitly carves out message context command targets from Message Content intent restrictions. The resolved target message in an interaction payload is fully populated — `attachments`, `embeds`, `content`, and `components` are all present regardless of whether the app has Message Content intent.

Key confirmed findings:

- Discord.Net 3.x registers message context commands with `[MessageCommand("ASCII this")]` and a handler signature of `Task HandleAsync(IMessage message)`.
- `message.Attachments` is fully populated: `Url`, `ContentType`, `Filename`, `Size`, `Width`, `Height`. Attachment CDN URLs are pre-signed public URLs, directly downloadable without Discord auth, and are fresh at invocation time.
- `message.Embeds` is fully populated. GIF picker messages produce an embed of type `EmbedType.Gifv`. The direct Tenor or Giphy MP4 URL is at `embed.Video.Value.Url`. These Tenor/Giphy CDN URLs are open and directly downloadable.
- `EmbedVideo` in Discord.Net exposes `Url` but not `ProxyUrl`. `embed.Video.Value.Url` is the field to use.
- Attachment CDN URLs carry expiry parameters (`ex`, `is`, `hm`) and should be downloaded immediately rather than persisted.

Confirmed v4 stance:

- Preserve `/ascii image:<attachment>` as the baseline invocation path.
- Add `ASCII this` as a message context command in v4.
- The context command supports both real message attachments and GIF picker embeds (`gifv` type with a `Video.Url`).
- It should prefer attachment when both are present.
- It should reject clearly when the targeted message has no supported media source.
- It does not require Message Content intent.

This feature remains an invocation improvement, not a broad URL-ingestion product.

### Direct Link Input

A direct URL option could reduce friction for users who already copied a media link:

```text
/ascii url:<discord-media-url>
```

This is less Discord-native than a message context command and carries broader remote-fetch risks. If added, it should be tightly constrained to known Discord media proxy or Discord CDN URL patterns in the v4 baseline, and should use the same download limits, content validation, and rejection behavior as attachments.

Direct arbitrary internet URL ingestion remains deferred.

If direct copied-media-link input is included, it must be treated as a narrow exception to the remote-ingestion non-goal.

Required safety boundaries:

- allow only explicitly approved Discord CDN or Discord media proxy host patterns
- reject arbitrary hosts, local addresses, private network addresses, and non-HTTP(S) schemes
- follow redirects only under a strict policy that preserves the host allowlist and request limits
- distrust filename extensions and `Content-Type`; validation remains content-based
- enforce a source byte ceiling during download
- enforce connection, response-header, body-read, and total operation timeouts
- never persist submitted URLs as durable source references
- never log full signed or proxied media URLs when they may contain signatures or user-identifying data

The implementation specification should decide whether this path belongs in v4 at all.

---

## MP4 Scope Boundaries

Accepted MP4 input should be constrained to short animation-like content.

Baseline stance:

- MP4 may contain video frames.
- MP4 may contain no audio, or audio may be ignored if present.
- MP4 duration must be bounded.
- MP4 decoded dimensions must be bounded.
- MP4 sampled output frame count must be bounded.
- MP4 must produce reliable decoded frames at requested sample timestamps.
- MP4 output uses the existing animated WebP artifact path.

Rejected MP4 conditions should include:

- unsupported or unreadable container
- no video stream
- encrypted or otherwise inaccessible media
- duration above v4 limit
- decoded dimensions above existing image limits
- inability to decode frames reliably
- generated animated WebP exceeding delivery limits
- video format requiring unavailable decoder support

### Audio Policy

ASCIIBot does not produce audio output.

v4 should either ignore audio streams or reject MP4 files with audio. The foundation should settle this before the implementation spec.

Decision: ignore audio streams when a valid video stream can be decoded safely, because Discord-saved GIF-like MP4 files may carry container structure the user did not intentionally choose. Audio must never be extracted, preserved, uploaded, logged, or surfaced as part of the output.

---

## Source Fetch And Temporary Storage Policy

MP4 intake introduces source-fetch and decoder-access questions that static ImageSharp inputs did not fully expose.

The existing source image byte ceiling should remain the default safety posture unless v4 explicitly supersedes it. MP4 and copied media links must not create an unbounded remote-fetch path.

Decisions:

- MP4 source input shares `ASCIIBot_SourceImageByteLimit` in the v4 baseline.
- URL and attachment sources use the same source byte ceiling when URL input is enabled.
- Accepted MP4 candidates are downloaded into a bounded temporary file under application-controlled temp storage.
- FFmpeg receives a local file path rather than an unbounded remote URL or pipe in the v4 baseline.
- Temporary files must be cleaned up after success, rejection, failure, cancellation, and process errors.

Open implementation questions:

- where temporary files are created inside Docker and local runs
- what timeout limits apply to download, inspection, frame extraction, and decoder process execution
- whether future versions should add a separate MP4 source byte limit

---

## Decoder And Dependency Direction

MP4 decoding is outside ImageSharp's native scope. v4 will likely require a dedicated video decoding dependency.

The likely baseline is `ffmpeg` invoked as a bounded child process, packaged inside the Docker image.

Potential approaches:

| Approach | Strength | Concern |
|---|---|---|
| `ffmpeg` CLI process | Mature, predictable, broad MP4 decoding support, easy to package in Docker | Requires process boundary, timeout, stderr handling, and careful argument construction |
| .NET wrapper over FFmpeg | Friendlier API surface | Still depends on native binaries and may hide important limits |
| Pure managed MP4 decoding | Avoids external process | Likely too limited or risky for v4 |
| ImageSharp-only | Keeps current dependency model | Does not solve MP4 decoding |

Decision: use the `ffmpeg` CLI as the v4 MP4 decoder dependency, isolated behind a service boundary.

### Decoder Availability Policy

Decoder availability is an operator/runtime concern before it is a per-request user concern.

v4 should choose one explicit availability model:

| Model | Behavior | Tradeoff |
|---|---|---|
| Required decoder | Bot fails startup if the decoder is unavailable | Simple and honest when MP4 is part of v4's baseline |
| Optional decoder | Bot starts without MP4 support and rejects MP4 requests | Preserves static/GIF/WebP operation but adds capability-state complexity |
| Operator-configured mode | Operator chooses required or optional decoder behavior | Most flexible, but increases configuration surface |

Decision: Docker runtime requires the decoder and fails startup if it is missing. Local non-Docker development should use the same required-decoder posture by default unless the implementation specification introduces an explicit developer opt-out.

### Dependency Boundary

The implementation should avoid spreading FFmpeg concerns through the codebase.

Conceptual boundary:

```text
Mp4InspectionService
Mp4FrameExtractionService
```

These services should produce the same kind of decoded frame data that the existing animated pipeline already knows how to render.

---

## MP4 Sampling Direction

v4 should reuse v3's deterministic time-based sampling model wherever possible.

Existing v3 concepts to preserve:

- maximum source duration
- target sample interval
- maximum output frame count
- minimum output frame delay
- total output-cell cost fuse
- no duplicate-frame collapse in the baseline
- no automatic static fallback
- no adaptive degradation

MP4 sampling should be defined in terms of source duration and sampled timestamps, not raw video frame count.

Open questions:

- Should MP4 use the same 12-second maximum duration as animated GIF/WebP?
- Should MP4 use the same 48-frame output cap?
- Should MP4 use the same 100 ms target sampling interval?
- Should MP4 have a separate source-frame safety fuse, or is duration plus sampled decoding sufficient?
- Should variable-frame-rate MP4 inputs be treated differently from constant-frame-rate inputs?

Decision: MP4 input starts with the same animation limits as v3 animated GIF and animated WebP unless implementation testing proves that MP4 needs stricter defaults.

---

## Animated Output Compatibility

Successful MP4 input should produce the same generated artifact as other animated input:

```text
asciibot-render.webp
```

If `show_original=true`, the original MP4 may be included only when delivery limits allow it.

The generated animated WebP remains mandatory for a successful animated conversion. The original source remains contextual and optional.

MP4 input should not produce:

- inline text output
- `.txt` output
- static first-frame fallback
- animated GIF output
- video output
- audio output

---

## Delivery Policy

v4 should preserve v3 delivery behavior:

- static images use v3 static delivery
- animated GIF and animated WebP use v3 animated delivery
- accepted MP4 uses v3-style animated delivery

For MP4 input:

- generated animated WebP takes priority over original source display
- original MP4 is omitted before generated output when upload limits require it
- if generated animated WebP cannot fit delivery limits, reject or fail visibly according to classification
- no automatic static fallback is allowed
- no alternate video output is produced

---

## Reject Versus Fail Policy

v4 should preserve the v3 distinction:

```text
Reject = known boundary, validation, policy, or delivery-limit violation.
Fail = runtime, dependency, decoding, rendering, encoding, permission, transport, cancellation, or unexpected internal breakdown.
```

Likely MP4 rejections:

- unsupported MP4/container content
- no video stream
- duration exceeds the maximum supported duration
- decoded dimensions exceed supported dimensions
- generated animation exceeds delivery limits
- total output-cell cost fuse exceeded
- MP4 support is disabled or unavailable in a runtime mode that still allows the bot to start

Likely MP4 failures:

- decoder process crashes unexpectedly on otherwise accepted input
- frame extraction fails after inspection accepted the input
- animated WebP encoding failure
- Discord delivery failure not caused by known size limits
- permission failure
- cancellation
- unknown exception

The implementation specification should make exact classifications explicit.

Startup decoder unavailability is handled by the selected decoder availability policy rather than treated only as a normal request rejection.

---

## Failure Language

ASCIIBot's user-facing language remains formal, precise, affectless, and impersonal.

MP4-specific messages use "video" for inspection and validation failures and "animation" for generated animated WebP delivery failures, consistent with v3 animated delivery language.

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

---

## Compatibility With v1, v2, And v3

v4 should preserve these behaviors unless explicitly superseded:

- `/ascii image:<attachment> [size] [color] [detail] [show_original]` remains available.
- Static PNG, JPEG, BMP, GIF, and WebP behavior remains unchanged.
- Animated GIF and animated WebP behavior remains unchanged.
- Accepted requests receive visible acknowledgement before rendering completes.
- Validation failures are visible and procedural.
- The rich render model remains the canonical source for static and animated export.
- Animated outputs use generated animated WebP.
- Original image display remains controlled by `show_original`.
- Generated render artifacts are preserved before optional original display.
- The bot does not silently split one render across multiple Discord messages.
- The bot does not automatically fall back from animation to static output.
- The bot remains hobby-scale and Discord-first.
- The bot retains its cold, efficient bureaucratic response tone.

v4 supersedes v3 only by adding:

- full Dockerized deployment support
- bounded MP4 input routing into the animated pipeline

---

## Open v4 Idea Queue

Remaining open or deferred items:

- FFmpeg version pinning (deferred; package manager install accepted for v4 baseline)
- direct copied-media-link host allowlist (deferred; retained for reference if context command proves insufficient)
- MP4 test fixtures (implementation concern; resolved during spec/implementation)
- manual Discord testing for saved built-in GIF MP4 files (testing concern; resolved during acceptance)
- future WebM support
- future MOV support
- future explicit first-frame-only mode

Ideas should be added here first with enough context to discuss tradeoffs, then moved into decision sections once direction is clear.

---

## Must Close Before Spec

All pre-spec decisions are now closed. The message context command spike (2026-05-02) confirmed that:

- Message context commands are viable without Message Content intent. The Discord documentation explicitly carves out context command targets from intent restrictions.
- The context command supports both attachments and `gifv` embeds, with attachment taking priority.
- The context command uses hardcoded defaults (medium size, color on, normal detail, show_original true) because Discord message context commands accept no user-supplied parameters.
- The context command shares the slash command's concurrency gate, defer/followup acknowledgement pattern, and public (non-ephemeral) response behavior.
- Direct copied-media-link input remains deferred. The context command is the preferred path.

The v4 foundation is ready to feed an implementation specification.

---

## Decision Log

### v4 Scope

Status: Decided.

Decision:

- v4 combines full Dockerization with bounded MP4 intake support.
- These goals belong together because MP4 support likely requires native media tooling, and Docker provides the controlled runtime for that dependency.

Rationale:

- Dockerization is not unrelated infrastructure churn in v4; it is the mechanism that makes media decoding repeatable for the intended deployment.
- MP4 support is motivated by a specific Discord workflow, not a general feature-expansion push.

---

### MP4 Product Boundary

Status: Decided.

Decision:

- MP4 is accepted only as an animated source compatibility format.
- v4 should not become general video support.

Rationale:

- The motivating user flow is saving a Discord built-in GIF and receiving MP4 locally.
- The product promise remains image-to-ASCII or animation-to-animated-ASCII within Discord.
- Broad video support would introduce audio, long-duration content, codecs, seeking, sync, and delivery concerns that do not belong in the v4 baseline.

---

### MP4 Output Format

Status: Decided.

Decision:

- Successful MP4 input should produce generated animated WebP output.
- MP4 input should not produce video output, audio output, inline text output, `.txt` output, or automatic static fallback.

Rationale:

- v3 already established animated WebP as the generated artifact for animated input.
- Reusing that output keeps v4 focused on intake compatibility rather than creating a second animated delivery model.

---

### Docker As Supported Deployment

Status: Decided.

Decision:

- v4 should make Docker a supported deployment path with committed build/run artifacts and README instructions.
- The runtime image should include all dependencies needed for MP4 intake.
- v4 should include a committed Compose artifact for local/operator use.
- The final runtime container should run as a non-root user.

Rationale:

- MP4 decoding usually requires native media tooling.
- Docker prevents the operator from needing to hand-install matching decoder dependencies on the host.
- A repeatable runtime reduces implementation and support ambiguity.
- Compose is useful for the expected personal long-running bot deployment and keeps environment-variable configuration explicit.
- Non-root runtime is a low-cost safety baseline for a containerized bot.

---

### Discord-Native Media Invocation

Status: Decided. Spike complete (2026-05-02).

Decision:

- v4 adds `ASCII this` as a message context command (`Right-click message → Apps → ASCII this`).
- Message context support does not require Message Content intent. The official Discord documentation explicitly carves out message context command targets from Message Content intent restrictions. All resolved message fields are populated in the interaction payload regardless of intent.
- The context command supports both real message attachments and GIF picker embeds (`EmbedType.Gifv` with a `Video.Url`).
- When the target message has both an attachment and an embed, the attachment takes priority.
- The context command uses the same download, validation, and rendering pipeline as the slash command.
- Media from the resolved message is downloaded immediately; attachment CDN URLs are not persisted.
- The context command rejects clearly when the targeted message has no supported media source.
- Direct copied-media-link input (`/ascii url:...`) remains deferred. The context command is the preferred invocation path and no longer requires a URL fallback.
- Discord message context commands accept no user-supplied parameters beyond the target message. The context command uses the slash command's default parameter values: medium size, color on, normal detail, show_original true.
- The context command shares the same per-user and global concurrency gate as the slash command.
- The context command uses the same `DeferAsync` + followup response pattern as the slash command, including the visible processing acknowledgement.
- Context command responses are public (not ephemeral), matching the slash command behavior. The rendered output is visible to everyone in the channel.

Rationale:

- The implementation spike confirmed that `message.Attachments` and `message.Embeds` are fully populated in the resolved interaction payload without Message Content intent. This was verified against the official Discord gateway intent documentation and the Discord.Net 3.x source.
- GIF picker messages produce `EmbedType.Gifv` embeds whose `Video.Url` points to a directly downloadable Tenor or Giphy MP4. This is the same media type that drives the v4 MP4 intake path.
- A message context command removes the manual copy-link/download/re-upload loop for GIF-picker media and requires no new permissions or intents.
- This improves invocation ergonomics without changing ASCIIBot's core transformation model.
- Discord message context commands do not support additional user-supplied parameters. Hardcoded defaults preserve the existing product behavior without requiring a secondary configuration step.
- Sharing the concurrency gate preserves v3 resource guarantees uniformly across both invocation paths.
- Public responses match the slash command experience and keep the bot's output visible and consistent in a server context.

---

### MP4 Decoder Dependency

Status: Decided.

Decision:

- v4 uses the `ffmpeg` CLI as the MP4 decoder dependency.
- FFmpeg concerns should be isolated behind MP4 inspection and frame extraction service boundaries.
- The Docker runtime image must include FFmpeg.
- FFmpeg should be installed from the selected Debian runtime image's package repositories in the v4 baseline.
- The implementation should verify FFmpeg availability at startup and log the detected version for operator diagnostics.

Rationale:

- MP4 decoding is outside ImageSharp's scope.
- FFmpeg is mature, practical, and easy to package into a Docker image.
- Keeping FFmpeg behind a boundary protects the rest of the codebase from process and argument-management details.
- Using the distribution package keeps the Dockerfile simple and security-update friendly without introducing custom binary download logic.

---

### Docker Base Images

Status: Decided.

Decision:

- v4 should use official Microsoft .NET container images for both build and runtime.
- The build stage should use a .NET 10 SDK image.
- The final runtime stage should use a Debian-based .NET 10 runtime image.
- The exact Debian tag may be selected in the implementation specification, but it must support installing FFmpeg through the package manager.

Rationale:

- The project already targets .NET 10.
- Official .NET images are the least surprising baseline for a .NET console bot.
- A Debian-based runtime keeps FFmpeg installation straightforward and avoids adding a separate native media distribution mechanism.

---

### Docker Health Checks

Status: Decided.

Decision:

- v4 does not include a Docker `HEALTHCHECK`.
- Startup validation and process exit behavior are the health boundary for the v4 Docker baseline.
- The bot should fail startup for missing required configuration or missing required runtime dependencies such as FFmpeg.
- Discord connection and ready-state events should be logged for operator visibility.

Rationale:

- ASCIIBot is a Discord bot process, not an HTTP service with a natural health endpoint.
- A process-exists health check adds little beyond Docker's normal process supervision.
- A meaningful Discord connectivity health check would require adding a side channel such as an HTTP endpoint or status file, which is outside the v4 deployment goal.
- Strong startup validation plus ordinary container restart policy is sufficient for the v4 baseline.

---

### Decoder Availability

Status: Decided.

Decision:

- Both Docker and local non-Docker development require FFmpeg. The bot fails startup if FFmpeg is unavailable in either environment.
- No developer opt-out is provided.

Rationale:

- MP4 intake is part of the v4 baseline, not optional polish. Missing decoder support should not create a quietly degraded runtime in any environment.
- Failing early gives the operator or developer a clear missing-dependency signal instead of confusing per-request failures.
- Docker is the supported path for anyone who does not want to install FFmpeg locally. An opt-out would add configuration, tests, docs, and a partial-capability mode for a problem Docker already solves.

---

### MP4 Audio Policy

Status: Decided.

Decision:

- MP4 audio streams are ignored when a valid video stream can be decoded safely.
- Audio is never extracted, preserved, uploaded, logged, rendered, or exposed in output.

Rationale:

- ASCIIBot produces visual ASCII-oriented artifacts only.
- Discord-saved GIF-like MP4 files may carry container details the user did not intentionally choose.
- Rejecting otherwise valid visual input only because the container includes audio would be unnecessarily brittle for the motivating workflow.

---

### MP4 Limits And Source Fetch

Status: Decided.

Decision:

- MP4 input starts with the same animation limits as v3 animated GIF and animated WebP.
- MP4 source input shares `ASCIIBot_SourceImageByteLimit` in the v4 baseline.
- MP4 candidates are downloaded into a bounded temporary file before FFmpeg inspection or frame extraction.
- Temporary files must be cleaned up in all terminal paths.

Rationale:

- Reusing v3 animation limits keeps the first MP4 pass aligned with the existing animated product envelope.
- A shared source byte ceiling prevents MP4 support from opening a larger ingestion path by accident.
- A bounded local temp file gives FFmpeg predictable seekable input while preserving explicit download limits.

---

### Direct Copied-Media-Link Input

Status: Deferred.

Decision:

- Direct copied-media-link input is not promoted to the v4 baseline yet.
- It should be reconsidered after the message context command spike.
- If later included, it must be constrained to approved Discord CDN or Discord media proxy hosts with strict redirect, timeout, private-network blocking, and logging rules.

Rationale:

- Message context invocation is more Discord-native and avoids asking users to handle proxy URLs manually.
- Direct URL ingestion carries broader security and product-scope risks.
- Deferring the URL path keeps v4 focused while preserving it as a fallback if context invocation cannot reach GIF-picker media.

---

### MP4 Animation Limits

Status: Decided.

Decision:

- MP4 input uses the same animation limits as v3 animated GIF and animated WebP inputs.
- Maximum source duration is governed by `ASCIIBot_AnimationMaxDurationMs` (default 12 seconds).
- Maximum sampled output frames is governed by `ASCIIBot_AnimationMaxOutputFrames` (default 48).
- Minimum output frame delay is governed by `ASCIIBot_AnimationMinFrameDelayMs` (default 100 ms).
- Maximum total output cells is governed by `ASCIIBot_AnimationMaxOutputCells` (default 300,000).
- Maximum source byte size is governed by `ASCIIBot_SourceImageByteLimit` (default 10 MiB).
- Maximum decoded canvas dimensions are governed by `ASCIIBot_MaxDecodedImageWidth` and `ASCIIBot_MaxDecodedImageHeight` (default 4096x4096).
- The source-frame safety fuse (`ASCIIBot_AnimationMaxSourceFrames`) does not apply to MP4 input. FFmpeg-based intake uses timestamp-based seeking rather than source-frame enumeration, so duration and the output-cell cost fuse are the binding constraints.

Rationale:

- The expected deployment is a personal bot on a high-end desktop with fewer than four concurrent users. Local compute is not the primary constraint. Reusing v3 limits avoids introducing a parallel set of animation parameters without evidence that MP4 needs stricter defaults.
- The source-frame fuse was designed to protect against pathologically high-frame-count GIF/WebP animations. It has no equivalent meaning for FFmpeg timestamp-based seeking, where source frames are not enumerated.

---

### MP4 Configuration Variables

Status: Decided.

Decision:

- MP4 input reuses all existing animation configuration variables that apply to its pipeline stage.
- No MP4-specific configuration variables are introduced in the v4 baseline.
- `ASCIIBot_AnimationMaxSourceFrames` is not applied to MP4 input.

Rationale:

- MP4 intake routes extracted frames into the same animated render model as GIF and WebP. Shared configuration keeps the operator-visible surface consistent and avoids fragmentation between animated formats.
- Separate MP4 limits would only be justified if MP4 inputs routinely required different operational boundaries, which v4 has no evidence to assume.

---

### MP4 Timeout Policy

Status: Decided.

Decision:

- FFmpeg inspection (container metadata and stream discovery): 15-second hard timeout.
- FFmpeg frame extraction (all sampled frames): 60-second hard timeout.
- Both timeouts are hardcoded in the v4 baseline and are not configurable through environment variables.
- A timeout during FFmpeg inspection or frame extraction is classified as a failure, not a rejection, because it represents a runtime breakdown on otherwise accepted input.

Rationale:

- The expected deployment is a personal bot on capable local hardware. Both timeouts are generous relative to what FFmpeg should take on short, bounded MP4 files under normal conditions.
- Hardcoding timeouts keeps configuration surface small. A personal hobby bot does not need operator-tunable FFmpeg timeouts in the v4 baseline.
- Timeout is a failure, not a rejection, because the input passed validation and the system failed to complete processing.

---

### MP4 Temporary File Policy

Status: Decided.

Decision:

- MP4 candidates are written to a per-job unique path under `Path.GetTempPath()`.
- Temporary files must be deleted in all terminal paths: success, rejection after download, failure, cancellation, and unexpected exception.
- Cleanup must be guaranteed by a `finally` block or equivalent `IDisposable`/`IAsyncDisposable` pattern to prevent leaks across restarts, failures, and cancellations.
- FFmpeg receives a local file path; it does not receive a remote URL or pipe.

Rationale:

- `Path.GetTempPath()` is the cross-platform .NET standard for temporary storage and works correctly inside Docker containers as well as local runs.
- A local file path gives FFmpeg a seekable, bounded input, which is necessary for reliable timestamp-based frame extraction.

---

### MP4 Required Inspection Data

Status: Decided.

Decision:

Before accepting an MP4 for rendering, FFmpeg inspection must confirm:

- The container is readable.
- At least one video stream is present.
- The video codec is decodable by the installed FFmpeg.
- Duration is present as a positive value determinable in milliseconds.
- Video canvas width and height are present and within configured dimension limits.

If any required inspection data is absent or the container cannot be read, the request is rejected as an inspection failure.

Rationale:

- These are the minimum data points needed to validate an MP4 against v4 product limits before routing it into the animated render pipeline.
- The container-readable and video-stream-present checks mirror the analogous animated GIF/WebP checks that require readable animation metadata and at least one compositeable frame.

---

### MP4 Frame Extraction

Status: Decided.

Decision:

- FFmpeg extracts sampled frames as individual lossless image files (PNG or equivalent) written to the per-job temp directory, numbered by sample index.
- Frame extraction targets the sample timestamps computed by the v3 sampling algorithm applied to MP4 duration.
- If extraction does not produce the expected number of frame files, or any expected frame file is absent or unreadable, the request fails.
- The FFmpeg extraction process must respect the 60-second extraction timeout.

Rationale:

- Writing extracted frames as image files gives the rest of the pipeline a predictable, inspectable input consistent with the existing static image path.
- Failing when expected frames are missing matches v3's policy of rejecting or failing visibly rather than silently degrading.

---

### MP4 Reject/Fail Classification

Status: Decided.

Decision:

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

Rationale:

- Reject conditions are known product or validation boundaries: the input does not meet v4 requirements, and the system behaved correctly in refusing it.
- Fail conditions are runtime breakdowns: the input passed validation but the system could not complete processing.
- FFmpeg timeout is classified as failure because timeout occurs on input that already passed validation. The system, not the input, is the problem.

---

## Next Pass Focus

All foundation decisions are closed. The v4 foundation is ready to feed a v4 implementation specification.

---

*Last updated: 2026-05-02*
