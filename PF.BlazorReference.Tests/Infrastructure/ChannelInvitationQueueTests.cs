using PF.BlazorReference.Infrastructure.Queues;

namespace PF.BlazorReference.Tests.Infrastructure;

public class ChannelInvitationQueueTests
{
    [Fact]
    public async Task Ids_come_out_in_the_order_they_went_in()
    {
        var queue = new ChannelInvitationQueue();
        Guid[] ids = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
        foreach (var id in ids)
            Assert.True(queue.TryEnqueue(id));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var read = new List<Guid>();
        await foreach (var id in queue.ReadAllAsync(cts.Token))
        {
            read.Add(id);
            if (read.Count == ids.Length)
                break;
        }

        Assert.Equal(ids, read);
    }

    [Fact]
    public void A_full_queue_refuses_instead_of_dropping_a_queued_id()
    {
        var queue = new ChannelInvitationQueue();
        for (var i = 0; i < ChannelInvitationQueue.Capacity; i++)
            Assert.True(queue.TryEnqueue(Guid.NewGuid()));

        Assert.False(queue.TryEnqueue(Guid.NewGuid()));
    }
}
