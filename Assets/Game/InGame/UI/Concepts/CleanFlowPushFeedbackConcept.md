# Clean Flow Push Feedback Concept

This concept visualizes how continuous forward cleaning can feel satisfying without adding a large detached HUD widget.

## Feedback layers

- **Contact:** A thin cyan glow and small spark ticks sit directly on the front scrubber. They react whenever the tool is touching a cleanable surface.
- **Continuity:** Four short translucent light streaks remain inside the freshly cleaned lane. They communicate uninterrupted pushing without using screen-wide speed lines.
- **Rhythm:** The five-segment `FLOW` strip fills one segment at a time while forward cleaning is maintained. It is temporary and fades when the chain ends.
- **Payoff:** `FLOW x4` appears only when a meaningful threshold is crossed, then quickly settles and disappears.
- **Shared progress:** The sparkle at the active edge of the top progress bar gives each sustained chain a clear destination.

## Animation direction

- Contact glow: pulse between 0.82 and 1.0 opacity over 0.18 seconds while cleaning.
- Flow segments: `DOScale(0.82 -> 1.12 -> 1.0)` with `Ease.OutBack`, staggered by 0.05 seconds.
- Threshold sticker: rise 12 px and scale from 0.8 to 1.05 to 1.0, hold briefly, then fade.
- Clean-lane streaks: spawn near the scrubber, stretch slightly backward, then fade in place. Do not slide the whole ring or selector.
- Progress sparkle: one short scale-and-opacity burst at the current fill edge, not a constant loop.

## Extensibility

Keep the system tool-agnostic. Each cleaning tool supplies its own contact anchor, accent color, and small badge icon; the five flow segments, threshold logic, and progress-bar payoff remain shared.

## Visual constraints

- Match the existing cream outline, navy shadow, cyan/teal accent, rounded 2.5D sticker language.
- Keep the bottom-right tool selector as one static ellipse.
- Avoid arrows, rotating rings, giant combo text, confetti, and permanent center-screen UI.
