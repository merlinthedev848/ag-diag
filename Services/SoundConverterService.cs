using System;
using System.Threading.Tasks;

namespace agilicomsptoolkit.Services
{
    public interface ISoundConverterService
    {
        Task<(bool success, string outputPath, string message)> ConvertToTelephonyWavAsync(string inputPath, Action<double>? progressCallback);
    }

    public sealed class SoundConverterService : ISoundConverterService
    {
        public Task<(bool success, string outputPath, string message)> ConvertToTelephonyWavAsync(string inputPath, Action<double>? progressCallback)
        {
            return AudioConverter.ConvertAsync(inputPath, progressCallback);
        }
    }
}
