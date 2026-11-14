using System.Numerics;

namespace SignalDx.Dsp;

public sealed class SpectrumAnalyzer
{
    readonly int _fftSize;
    readonly double[] _windowed;
    readonly Complex[] _bins;
    readonly float[] _db;

    public SpectrumAnalyzer(int fftSize = 2048)
    {
        if (fftSize < 64 || (fftSize & (fftSize - 1)) != 0)
            throw new ArgumentException("FFT size must be a power of two.", nameof(fftSize));

        _fftSize = fftSize;
        _windowed = new double[fftSize];
        _bins = new Complex[fftSize];
        _db = new float[fftSize / 2];
    }

    public int FftSize => _fftSize;
    public IReadOnlyList<float> MagnitudesDb => _db;

    public void Analyze(ReadOnlySpan<float> mono)
    {
        int n = Math.Min(mono.Length, _fftSize);
        for (int i = 0; i < n; i++)
            _windowed[i] = mono[i];
        for (int i = n; i < _fftSize; i++)
            _windowed[i] = 0;

        Window.Hann(_windowed);

        for (int i = 0; i < _fftSize; i++)
            _bins[i] = new Complex(_windowed[i], 0);

        Fft.Forward(_bins);

        double scale = 1.0 / _fftSize;
        for (int i = 0; i < _db.Length; i++)
        {
            double mag = _bins[i].Magnitude * scale;
            _db[i] = (float)(20.0 * Math.Log10(Math.Max(mag, 1e-12)));
        }
    }

    public static int HzToBin(double hz, int sampleRate, int fftSize) =>
        (int)Math.Round(hz * fftSize / sampleRate);

    public static double BinToHz(int bin, int sampleRate, int fftSize) =>
        bin * (double)sampleRate / fftSize;
}
