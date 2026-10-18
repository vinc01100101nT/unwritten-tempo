// THE PAPER every screen of the game is printed on: a faint static fibre speck locked to screen pixels. The world's
// ink overlay (InkScreen) and the front screens' pictures (FrontScreenPaper) both draw it from here, so the world and
// the sign-in screen cannot end up on two different papers.
#ifndef UNWRITTEN_PAPER_GRAIN
#define UNWRITTEN_PAPER_GRAIN

// How many screen pixels one fibre speck covers.
#define PAPER_GRAIN_PX 2.5

float PaperHash21(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

// How much ink the paper puts on this screen pixel: 0 to strength.
float PaperGrainAlpha(float2 screenPixel, float strength)
{
    return (1.0 - PaperHash21(floor(screenPixel / PAPER_GRAIN_PX))) * strength;
}

#endif
