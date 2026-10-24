using System.Threading.Channels;
using PF.BlazorReference.Core;

namespace PF.BlazorReference.Infrastructure.Queues;

/// <summary>
/// <see cref="IInvitationQueue"/> over a bounded in-memory channel. Its contents die with the process,
/// which is safe because the rows hold the requests and the invitation worker sweeps them.
/// </summary>
public sealed class ChannelInvitationQueue : IInvitationQueue
{
    public const int Capacity = 100;

    private readonly Channel<Guid> channel = Channel.CreateBounded<Guid>(new BoundedChannelOptions(Capacity)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.Wait, // TryWrite then refuses instead of dropping a queued id
    });

    public bool TryEnqueue(Guid registrationId) => channel.Writer.TryWrite(registrationId);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken ct) => channel.Reader.ReadAllAsync(ct);
}
