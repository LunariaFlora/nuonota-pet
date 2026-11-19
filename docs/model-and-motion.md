# Model and motion notes

Version by version, what was changed and why. Useful if you want to reproduce a build or understand
why a detail is the way it is.

## Sources

Model page: <https://models.spriters-resource.com/pc_computer/genshinimpact/asset/512863/>

The download contains COLLADA, FBX, textures and 47 bones, but **no animation clips**. The original
files are kept locally; the app consumes the COLLADA import. Character and game assets belong to their
original owners, and a public download is not a commercial licence.

Choreography references were player recordings, inspected frame by frame:

- A clip with a long panel opening around 01:00–01:01, then front, side and rear crescent close-ups
  with panel gathering between 01:10 and 01:41. Main body reads bright blue-violet; the crescent is
  pale yellow-ivory, not metallic gold.
- A close idle with a lowered head, giving side-panel droop and small local sway, used to calibrate
  amplitude and colour.
- A passing and rear-quarter view around 03:44–03:56, where the camera moves rather than the
  character, so it was not used as evidence that the character turns on its own.

Hover and appendage open/close are re-choreographed from the recordings. Drag lean, speed-dependent
turning and the full manual turn are desktop interactions added here. The moon ring and star points
are rebuilt with local geometry and gradients: an approximation, not the game shader.

Recordings and the original export stay out of the portable package.

## v0.5 — mirrored ear axis

Ears are now driven through a model-space mirror axis, which removes the one-sided fold caused by
inconsistent local axes. Idle ear spread widened; click became a head bow with a small ear spread.
Day-to-day motion uses the side glance; the full turn moved to the manual menu entry, with wind-up,
lift, lean, head leading and rear-ribbon lag. The droop-ear rest refers to the lowered-head close-up
but remains a re-authored program animation. Body brightening and rim light do not reduce opacity;
moon material, arcs and star parameters are unchanged.

## v0.5.1 — ribbon centrelines

Only the two rear light ribbons changed: the lower section pulls inward by about 65%, the backward
reach is reduced, and the root stays attached. Width and material are kept, with a small per-segment
delayed wave added. Body, ears, moon ring, stars and interactions are untouched.

## v0.6 — appendage inertia

Side panels and rear ribbons are now driven jointly by translation velocity, acceleration and turn
angular velocity, using segmented damped follow. Panel tips and ribbon ends respond late and keep a
residual swing on reversal or stop. The gathering step runs first, then a shared trailing offset is
applied, so gathering does not cancel the dynamics or reopen the silhouette into a wide V. Static
shape, moon ring and materials are unchanged.

Checks for this version: root and tip lag, continuity across direction changes, residual swing after
release, final settling, and spin-driven motion. A renderer-driven preview of repeated reversals and a
spin was exported and inspected frame by frame.
