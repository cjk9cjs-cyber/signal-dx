namespace SignalDx.Dsp;

/// <summary>
/// Power-domain moving average over the last few spectra (dB in, dB out). Averaging calms noise
/// speckle so a narrow, weak trace such as WSPR stands out of the waterfall.
/// </summary>
public sealed class SpectrumAverager
{
    readonly int _depth;
    readonly float[][] _power;
    readonly float[] _db;
    int _pos;
    int _fill;

    public SpectrumAverager(int bins, int depth)
    {
        if (bins < 1) throw new ArgumentOutOfRangeException(nameof(bins));
        _depth = Math.Max(1, depth);
        _power = new float[_depth][];
        for (int i = 0; i < _depth; i++)
            _power[i] = new float[bins];
        _db = new float[bins];
    }

    public IReadOnlyList<float> MagnitudesDb => _db;

    public void Reset()
    {
        _pos = 0;
        _fill = 0;
    }

    /// <summary>Add one spectrum and return the averaged result; <paramref name="offsetDb"/> is added to the output.</summary>
    public IReadOnlyList<float> Add(IReadOnlyList<float> magnitudesDb, float offsetDb = 0f)
    {
        int n = Math.Min(magnitudesDb.Count, _db.Length);
        var slot = _power[_pos];
        for (int i = 0; i < n; i++)
            slot[i] = MathF.Pow(10f, magnitudesDb[i] / 10f);
        _pos = (_pos + 1) % _depth;
        if (_fill < _depth) _fill++;

        for (int i = 0; i < n; i++)
        {
            double sum = 0;
            for (int k = 0; k < _fill; k++)
                sum += _power[k][i];
            _db[i] = 10f * MathF.Log10((float)(sum / _fill) + 1e-20f) + offsetDb;
        }
        return _db;
    }
}
