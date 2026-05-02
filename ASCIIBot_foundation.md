# ASCIIBot - Foundation Document

> This is a living document capturing constraints, intents, invariants, and early decisions for the ASCIIBot project. It is not a formal spec; it exists to feed one.

---

## Intent

ASCIIBot is a Discord bot whose only job is to convert user-supplied images into ASCII art and return the result as text in Discord.

The project is intentionally small, personal, and playful. It is not meant to be a platform, product, or general-purpose image processing service. Its value is simple: a user drops in an image, asks for conversion, sees immediate acknowledgement that the request was received, and gets back a recognizable ASCII rendering without ambiguity about whether the bot is working or broken.

The core use case is a member of a Discord server submitting an image and wanting a quick, legible text-art version suitable for sharing directly in chat.

---

## Philosophy

**Prefer fast acknowledgement, clear limits, and charming results over feature sprawl.**

ASCIIBot should feel responsive and understandable. The bot does not need to be smart in many ways; it needs to be dependable in one specific way. If an image is accepted, the user should know that work has begun. If a request cannot be completed, the failure should be obvious and explained in plain language.

ASCII conversion quality matters, but not at the cost of scope explosion. The project should stay narrow enough that the user experience feels polished rather than technically broad and half-finished.

---

## Non-Goals

- Not an image editor
- Not a general-purpose art bot
- Not a text-to-image or ASCII-to-image generator
- Not a commercial or multi-tenant SaaS service
- Not a moderation bot
- Not a bot framework or plugin host
- Not a persistence-heavy system with accounts, billing, or analytics
- Not a high-throughput media pipeline

---

## Constraints

| Constraint | Description |
|---|---|
| **Platform** | Must operate as a Discord bot |
| **Primary function** | Converts images to ASCII text output |
| **Input source** | User-supplied image content from Discord interactions |
| **Output target** | Returns the result in Discord, not in an external UI |
| **Acknowledgement** | Must quickly indicate that a request was received and is being processed |
| **Scope** | Hobby-scale, personal-server usage, not commercial deployment |
| **Simplicity** | Feature surface should stay intentionally small in v1 |
| **Token model** | Bot token is provided by the operator; token handling is operational, not user-facing product behavior |

---

## Invariants

- ASCIIBot only accepts requests that begin from an image.
- ASCIIBot only produces ASCII-oriented textual output; it does not generate an image as the primary artifact.
- Every accepted request must receive a visible acknowledgement before conversion completes.
- The bot must never fail silently once it has seen a valid request.
- If a request cannot be completed, the bot must emit a user-visible failure response rather than simply stop.
- The bot must remain understandable to casual server users; interaction patterns should not require technical knowledge.
- v1 scope must remain narrow enough that the main conversion flow feels complete and dependable.

---

## Core Product Concept

### Primary User Story

A user in a Discord server provides an image, asks ASCIIBot to convert it, receives immediate confirmation that the request is being handled, and then receives ASCII output in-channel.

### Product Shape

ASCIIBot is a single-purpose transformation bot:

- Input: image
- Process: image-to-ASCII conversion
- Output: Discord-friendly text rendering

Nothing outside that loop should be required for the product to feel successful.

### Success Criteria

The bot succeeds when it reliably answers these user expectations:

- Did the bot receive my request?
- Is it still working?
- Did it finish successfully?
- If not, what went wrong?
- Is the result readable and fun to share?

---

## Interaction Model

### v1 Decision

Use a slash command that requires an image attachment, such as `/ascii image:<attachment>`.

### Rationale

- It is explicit and discoverable.
- It avoids ambiguous passive triggers on any image posted in chat.
- It gives the bot a clean place to validate input and acknowledge immediately.
- It scales better than trying to infer intent from arbitrary message traffic.
- It provides the cleanest path to a visible "working" state using Discord interaction mechanics.

### Deferred Alternatives

- Reply-to-image command flow
- Context-menu command on a message containing an image
- A hybrid model with slash command as the baseline and reply/context flows added later

These remain valid future enhancements, but they are not part of the v1 baseline.

---

## Output Model

### v1 Decision

Return the ASCII art inline in a Discord `ansi` code block when it fits comfortably within message-size and readability constraints. If the result would be too large or too unreadable inline, return a short completion message plus a `.txt` attachment containing the full textual render.

### Color Direction

Colored output is part of v1, but it is intentionally limited by Discord's rendering model. v1 color should be treated as **best-effort enhancement within a constrained ANSI palette**, not as full-fidelity image color reproduction.

Proposed stance:

- Monochrome ASCII output is the baseline contract.
- v1 color uses Discord `ansi` code blocks with nearest-color matching against a limited supported palette.
- If color support is inconsistent or fragile on a given client, the bot should degrade cleanly to monochrome rather than produce confusing output.

### Rationale

This keeps the core promise stable while still allowing v1 to include a fun, visible color feature that fits the project's identity.

### Fallback Policy

- Inline output uses a fenced Discord `ansi` code block.
- Attachment fallback uses a plain text `.txt` file rather than raw ANSI escape sequences. This preserves the full textual render, but not ANSI color presentation.
- v1 does not split a single ASCII render across multiple Discord messages.

### Reasoning

Inline `ansi` code blocks are the best available Discord-native presentation for colored ASCII art. When a render no longer fits comfortably inline, a clean text attachment is more readable and less surprising than multi-message splitting or shipping a file full of escape sequences.

---

## Input Boundaries

- One image per request in v1
- Static PNG, JPEG, BMP, GIF, and WebP only in v1
- Input validation is based on detected file type from file contents, not filename extension alone
- Animated images are rejected in v1; GIF support is limited to single-frame GIFs, and multi-frame GIFs are rejected rather than silently reduced to frame 0
- Maximum accepted source file size: 10 MiB
- Large but otherwise acceptable images may be downscaled internally before conversion
- Maximum accepted decoded image dimensions: 4096x4096
- No multi-image collage or batch mode in v1

### Rationale

The point of v1 is a dependable single conversion flow, not exhaustive media support.

---

## UX Expectations

### Acknowledgement

The bot should respond quickly with an explicit working-state message after accepting a request.

Proposed examples of the product behavior:

- Immediate acknowledgement such as "Request received. Processing has begun."
- A follow-up edit or completion message such as "Rendering complete."
- A visible error response if validation or conversion fails

### Failure Transparency

Users should not have to infer failure from silence. Typical failure states should be named plainly:

- missing image
- unsupported file type
- image too large
- conversion failed
- output too large to post inline

The wording should remain plain, formal, and affectless.

---

## Failure Modes

| Condition | Behavior |
|---|---|
| No image supplied | Reject with a clear user-facing message |
| Unsupported image type | Reject with a clear user-facing message after content-based type detection |
| Animated image supplied | Reject with a clear user-facing message explaining that only static images are supported in v1 |
| Image too large | Reject with a size-related explanation |
| Image dimensions exceed safe processing ceiling | Reject with a dimension-related explanation |
| Conversion fails internally | Emit a visible failure response; never fail silently |
| Bot is at concurrency limit | Reject with a clear busy-state message and invite retry |
| Bot lacks required Discord permissions | Surface an operator-facing diagnosis where possible and fail visibly to the user when not |
| Request takes noticeably long | Keep the user informed that work is ongoing rather than appearing idle |

### Graceful Degradation

| Condition | Behavior |
|---|---|
| Output too large for a normal inline Discord message | Fall back to alternate delivery, such as a text attachment |

---

## Open Questions

No major product questions remain at the foundation level. Remaining implementation details should be handled in the specification.

Implementation questions the specification should answer early:

- Do the current inline readability thresholds hold up under real Discord transport limits once ANSI escape overhead is measured on rendered samples?

---

## Closed Decisions

### Project Identity

**Decision:** ASCIIBot is a Discord bot for converting images into ASCII art.

**Reasoning:** This is the complete project thesis. Anything outside it must justify itself against this narrow identity.

### Project Scope

**Decision:** The project is a personal hobby tool for use in the operator's own Discord servers.

**Reasoning:** This sharply reduces product and operational complexity. The bot should optimize for delight and reliability, not general-market extensibility.

### Core Transformation Direction

**Decision:** The bot converts images to ASCII text only. Text-to-image or reverse-direction features are explicitly out of scope.

**Reasoning:** The project should stay focused on one polished transformation rather than becoming a generic media toybox.

### User Feedback Requirement

**Decision:** The bot must visibly acknowledge accepted work before conversion completes.

**Reasoning:** The user experience should never leave someone guessing whether the request was received, stalled, or broken.

### Primary Interaction Flow

**Decision:** v1 uses a slash command with a required image attachment as the primary and only guaranteed invocation path.

**Reasoning:** This is the clearest and most controllable interaction surface. It is explicit for users, easy to validate, and maps naturally to Discord's acknowledgement and deferred-response model.

### Output Priority

**Decision:** Plaintext ASCII correctness and readability take priority over colored rendering.

**Reasoning:** Colored output is a fun enhancement, but it cannot become a dependency that destabilizes the core experience.

### v1 Color Support

**Decision:** Color is part of v1, implemented as limited ANSI palette matching in Discord `ansi` code blocks. Monochrome remains a supported fallback mode.

**Reasoning:** Limited ANSI color is feasible within Discord's constraints and meaningfully improves the charm of the output without changing the product's identity. The color model must remain Discord-aware and degrade cleanly when clients do not render ANSI as expected.

### Output Delivery

**Decision:** v1 posts results inline in a Discord `ansi` code block when the output remains comfortably readable. When it does not, the bot falls back to a plain text `.txt` attachment. v1 does not split a single render across multiple messages.

**Reasoning:** Inline delivery is the most immediate and delightful presentation, especially when color is available. Attachment fallback preserves usability when a render becomes too large or awkward for chat, and avoiding multi-message splitting keeps output coherent.

### Inline Readability Threshold

**Decision:** v1 posts inline only when the final render is no more than 100 visible columns wide, no more than 35 visible lines tall, and no more than 2000 total message characters including formatting overhead. If any threshold is exceeded, the bot falls back to a `.txt` attachment.

**Reasoning:** This leaves intended headroom under Discord limits while protecting chat readability. The threshold intentionally distinguishes between visible render size and actual payload length, since ANSI escape sequences increase transport size without increasing visible width. The character limit of 2000 aligns with Discord's message character cap and has been validated against render sizes across all three size presets.

### v1 Customization Surface

**Decision:** v1 exposes a small set of user controls: `size` and `color`. `size` is an enum-style option with values such as `small`, `medium`, and `large`. `color` is `on` or `off`. Defaults are `size=medium` and `color=on`.

**Reasoning:** These options cover the most intuitive user needs without turning the bot into a dense rendering control panel. They preserve simplicity while giving users a meaningful way to influence readability and presentation.

### Command Surface

**Decision:** The v1 command surface is `/ascii image:<attachment> [size] [color]`. `image` is required. `size` is a strict enum with `small`, `medium`, and `large`. `color` is a strict enum with `on` and `off`. Defaults are `size=medium` and `color=on`.

**Reasoning:** Discord slash commands are strongest when the option model is explicit and discoverable. Strict enums reduce ambiguity, simplify validation, and prevent a long tail of spelling variants and loosely interpreted values.

### Operator Assumptions

**Decision:** v1 assumes a single operator managing the bot for personal Discord servers. The Discord token is operator-supplied and must be provided through a secret-bearing runtime configuration mechanism such as environment variables, not hardcoded into source or committed to the repository. The bot should be runnable as a simple long-lived process and should remain Docker-friendly.

**Reasoning:** These assumptions match the hobby-project scope while still setting a healthy baseline for secret handling and deployment simplicity.

### Implementation Stack

**Decision:** The initial implementation stack is .NET 10, Discord.Net, SixLabors.ImageSharp, and a console-application process model. Discord.Net is used for slash commands and deferred responses. ImageSharp is used for image loading, frame detection, and pixel access. The runtime model is a long-lived, local-first console process.

**Reasoning:** This stack matches the project's Discord-first interaction model, supports the required image validation and processing needs, and fits the intended hobby-scale deployment posture without adding unnecessary infrastructure.

### Discord Permissions and Intents

**Decision:** v1 should request only the Discord capabilities needed for slash-command-based operation and result delivery. The design should avoid requiring message content intent. Required capabilities should be limited to the minimum practical set for accepting slash-command attachments, acknowledging work, posting results, and attaching fallback text files.

**Reasoning:** A narrow permission and intent footprint reduces complexity, lowers operator friction, and aligns with the project's single-purpose design.

### Bot Tone

**Decision:** ASCIIBot should read as a cold, efficient bureaucrat, without overtly announcing that persona. Its language should be formal, precise, affectless, and consistently impersonal.

Locked language patterns:

- Prefer passive constructions where appropriate, such as "Your request has been processed."
- Use precise but affectless acknowledgements, such as "Rendering complete."
- Avoid contractions in formal responses.
- Write error messages in the tone of administrative rejections, such as "The submitted image does not meet processing requirements. Please resubmit."

Avoid:

- Any response implying enthusiasm or personal investment in the result
- Hedging language such as "hopefully" or "something like this"
- Warm, encouraging, or overtly helpful phrasing
- Quirky or jokey failure messages that break character

**Reasoning:** A dry bureaucratic voice is more coherent and funnier than generic playful bot language, especially in validation and error handling. It also reinforces clarity by making responses terse, consistent, and procedural.

### Supported Input Formats

**Decision:** v1 supports static PNG, JPEG, BMP, GIF, and WebP images. Animated images are rejected. GIF support is limited to single-frame GIFs, and multi-frame GIFs are rejected rather than silently reduced to the first frame.

**Reasoning:** This covers a practical hobby-project set of common static image formats while keeping the conversion model straightforward and predictable. Rejecting multi-frame GIFs is clearer and more honest than silently discarding animation by selecting frame 0.

### Input Validation Strategy

**Decision:** ASCIIBot validates image type from file contents rather than trusting filename extension alone.

**Reasoning:** Content-based validation is more trustworthy and avoids false positives from mislabeled files. The bot does not need deep forensic parsing, but it should make a hard determination of supported type from the actual file data before accepting a request.

### Input Size Boundaries

**Decision:** v1 accepts source images up to 10 MiB and rejects images whose decoded dimensions exceed 4096x4096. Large but otherwise acceptable images may be downscaled internally before conversion.

**Reasoning:** These limits are generous enough for normal Discord image use while still protecting the bot from wasteful or abusive inputs. Internal downscaling is friendlier than requiring perfectly sized source images.

### Concurrency Policy

**Decision:** v1 allows one active job per user and a small global concurrency cap of 3 active jobs at once. Requests beyond those limits are rejected with a visible busy-state message rather than placed into a deep queue.

**Reasoning:** This keeps the bot responsive and predictable on hobby-scale servers. It prevents one user from monopolizing the bot while avoiding the complexity and opacity of a large queue.

---

## Next Pass Focus

The next pass should convert this foundation into a concrete implementation specification.

---

*Last updated: 2026-05-01*
