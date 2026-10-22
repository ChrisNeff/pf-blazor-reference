namespace PF.BlazorReference.Core;

/// <summary>
/// The use-case abstraction: one request plus one handler per file under Features/&lt;Feature&gt;/.
/// Hand-rolled on purpose, with no mediator package. Pages inject the concrete use-case class.
/// </summary>
public interface IUseCase<in TRequest, TResult>
{
    Task<TResult> Handle(TRequest request, CancellationToken ct);
}
