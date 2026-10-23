using System.Threading.Channels;
using PF.BlazorReference.Core;

namespace PF.BlazorReference.Web.Provisioning;

/// <summary>
/// The in-process signal behind <see cref="IProvisioningQueue"/>: registration ids for
/// <see cref="ProvisioningWorker"/>. In memory only, so its contents die with the process; that is
/// safe because the rows hold the requests and the worker sweeps them.
/// </summary>
public sealed class ProvisioningQueue : IProvisioningQueue
{
    public const int Capacity = 100;

    private readonly Channel<Guid> channel = Channel.CreateBounded<Guid>(new BoundedChannelOptions(Capacity)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.Wait, // TryWrite then refuses instead of dropping a queued id
    });

    public ChannelReader<Guid> Reader => channel.Reader;

    public bool TryEnqueue(Guid registrationId) => channel.Writer.TryWrite(registrationId);
}
