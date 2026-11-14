namespace SignalDx.Dsp;

static class Window
{
    public static void Hann(Span<double> samples)
    {
        int n = samples.Length;
        if (n <= 1) return;
        for (int i = 0; i < n; i++)
            samples[i] *= 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (n - 1)));
    }
}
