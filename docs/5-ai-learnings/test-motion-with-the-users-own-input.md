# Prove Motion With the User's Own Input, Frame by Frame

**Reach for this when:** you are about to report a UI change as "smooth", "fluid" or "animated" — or the user says it
still jumps after you reported it smooth.

## The rule

Test motion with the exact input the user will use (mouse wheel, touchpad, keys, a drag), sent as a real input
event, and record the position every frame. A scripted scroll or a style that exists proves nothing about wheel
scrolling: a browser applies a page's smooth-scroll style to scrolls a script asks for, never to a mouse wheel.

## Why

On 2026-10-02 the website got `scroll-behavior: smooth`. Its check sampled the page while a back-to-top button
scrolled it — a scroll the page itself asked for — and it glided over about half a second, so the report said
smooth scrolling was done. The user scrolls with a mouse wheel. A real wheel notch, sent through the browser's input
channel and recorded per frame, showed the page moving its whole 100 pixels in one frame. One round was spent
reporting a fix that the user's own hand could not feel.

## How to apply

- Drive a real browser through its debugging port and send the input as the input system does, e.g. Chrome's
  `Input.dispatchMouseEvent` with `type: 'mouseWheel', deltaY: 100` — not `window.scrollTo`.
- Record inside the page, every frame (`requestAnimationFrame` pushing `scrollY` into an array), then read the array
  back. A glide shows as many small steps that ease into the target; a jump shows the full distance in one frame.
- Headless Chrome reports `prefers-reduced-motion: reduce` and has smooth scrolling switched off by default. Emulate
  `prefers-reduced-motion: no-preference` and launch with `--enable-smooth-scrolling`, or every animation reads as
  absent and every scroll as a jump.
- Test the cases the smoothing must NOT take over too: a wheel over a box that scrolls by itself, ctrl + wheel, and
  reduced motion.
