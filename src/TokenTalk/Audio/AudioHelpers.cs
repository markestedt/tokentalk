namespace TokenTalk.Audio;

public static class AudioHelpers
{
    /// <summary>
    /// Calculates the RMS (Root Mean Square) amplitude of the audio samples in a WAV byte array.
    /// Assumes 16-bit signed PCM audio.
    /// </summary>
    public static double CalculateRms(byte[] wavData)
    {
        if (wavData.Length < 44)
            return 0; // Too short to contain valid WAV data

        // WAV data starts at byte 44 (after the 44-byte header)
        const int headerSize = 44;
        int sampleCount = (wavData.Length - headerSize) / 2; // 16-bit = 2 bytes per sample

        if (sampleCount <= 0)
            return 0;

        double sumSquares = 0;
        for (int i = 0; i < sampleCount; i++)
        {
            short sample = BitConverter.ToInt16(wavData, headerSize + i * 2);
            sumSquares += (double)sample * sample;
        }

        return Math.Sqrt(sumSquares / sampleCount);
    }

    /// <summary>
    /// Determines if an audio segment should be considered silent based on RMS threshold.
    /// </summary>
    public static bool IsSilent(AudioSegment segment, double threshold)
    {
        if (threshold <= 0)
            return false;

        double rms = CalculateRms(segment.WavData);
        return rms < threshold;
    }

    /// <summary>
    /// Determines if an audio segment is too short to be valid.
    /// </summary>
    public static bool IsTooShort(AudioSegment segment, TimeSpan minDuration)
    {
        return segment.Duration < minDuration;
    }
}
