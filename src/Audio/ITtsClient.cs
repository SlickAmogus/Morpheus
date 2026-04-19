using System.Threading;
using System.Threading.Tasks;

namespace Morpheus.Audio;

public interface ITtsClient
{
    Task<byte[]> SynthesizeAsync(string text, string voiceId, CancellationToken ct = default);
}
