using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using NSubstitute;
using PF.BlazorReference.Core;
using PF.BlazorReference.Core.Entities;
using PF.BlazorReference.Core.Ports.Data;

namespace PF.BlazorReference.Tests.Support;

/// <summary>
/// The data port and its factory as substitutes, for use-case unit tests. No database is involved:
/// the registrations table is an in-memory list behind an async query provider, so the EF async
/// operators (AnyAsync, ToListAsync) run over it. Behaviour that depends on real SQL (mappings, the
/// unique index, query translation, the audit interceptor) is tested against SQLite elsewhere.
/// </summary>
public sealed class FakeConsentIdentityDb
{
    private readonly List<DeveloperRegistration> pending = [];

    /// <summary>The rows already "saved". Seed it before calling the use case.</summary>
    public List<DeveloperRegistration> Registrations { get; } = [];

    /// <summary>What the use case passed to <c>DbSet.Add</c> and has not yet saved.</summary>
    public IReadOnlyList<DeveloperRegistration> Added => pending;

    public IConsentIdentityDb Db { get; } = Substitute.For<IConsentIdentityDb>();

    public DbSet<DeveloperRegistration> Set { get; }

    public IConsentIdentityDbFactory Factory { get; } = Substitute.For<IConsentIdentityDbFactory>();

    public FakeConsentIdentityDb()
    {
        Set = CreateSet(Registrations);
        Set.When(s => s.Add(Arg.Any<DeveloperRegistration>()))
            .Do(call => pending.Add(call.Arg<DeveloperRegistration>()));

        Db.DeveloperRegistrations.Returns(Set);
        Db.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            var saved = pending.Count;
            Registrations.AddRange(pending);
            pending.Clear();
            return Task.FromResult(saved);
        });

        Factory.CreateAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(Db));
    }

    /// <summary>A registration with a chosen creation time. The entity has no public setter for it, as in production.</summary>
    public static DeveloperRegistration Registration(string name, string email, DateTime? createdAtUtc = null)
    {
        var registration = DeveloperRegistration.Create(name, email).Value;
        if (createdAtUtc is { } created)
            typeof(DeveloperRegistration).GetProperty(nameof(DeveloperRegistration.CreatedAtUtc))!.SetValue(registration, created);
        return registration;
    }

    private static DbSet<T> CreateSet<T>(List<T> rows) where T : class
    {
        var set = Substitute.For<DbSet<T>, IQueryable<T>, IAsyncEnumerable<T>>();
        var query = (IQueryable<T>)set;

        // Re-read the list on each call, so rows seeded or saved after construction are visible.
        query.Provider.Returns(_ => new AsyncQueryProvider<T>(rows.AsQueryable().Provider));
        query.Expression.Returns(_ => rows.AsQueryable().Expression);
        query.ElementType.Returns(typeof(T));
        query.GetEnumerator().Returns(_ => rows.GetEnumerator());
        ((IAsyncEnumerable<T>)set).GetAsyncEnumerator(Arg.Any<CancellationToken>())
            .Returns(_ => new AsyncEnumerator<T>(rows.GetEnumerator()));
        return set;
    }

    // The EF async operators insist on IAsyncQueryProvider and IAsyncEnumerable; these adapt LINQ to objects.

    private sealed class AsyncQueryProvider<TEntity>(IQueryProvider inner) : IAsyncQueryProvider
    {
        public IQueryable CreateQuery(Expression expression) => new AsyncQuery<TEntity>(expression);

        public IQueryable<TElement> CreateQuery<TElement>(Expression expression) => new AsyncQuery<TElement>(expression);

        public object? Execute(Expression expression) => inner.Execute(expression);

        public TResult Execute<TResult>(Expression expression) => inner.Execute<TResult>(expression);

        public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken = default)
        {
            // TResult is Task<T> (or ValueTask<T>); run synchronously and wrap the result.
            var resultType = typeof(TResult).GetGenericArguments()[0];
            var result = typeof(IQueryProvider)
                .GetMethod(nameof(IQueryProvider.Execute), 1, [typeof(Expression)])!
                .MakeGenericMethod(resultType)
                .Invoke(this, [expression]);

            return (TResult)typeof(Task).GetMethod(nameof(Task.FromResult))!
                .MakeGenericMethod(resultType)
                .Invoke(null, [result])!;
        }
    }

    private sealed class AsyncQuery<T>(Expression expression) : EnumerableQuery<T>(expression), IAsyncEnumerable<T>, IQueryable<T>
    {
        IQueryProvider IQueryable.Provider => new AsyncQueryProvider<T>(this);

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
            new AsyncEnumerator<T>(this.AsEnumerable().GetEnumerator());
    }

    private sealed class AsyncEnumerator<T>(IEnumerator<T> inner) : IAsyncEnumerator<T>
    {
        public T Current => inner.Current;

        public ValueTask<bool> MoveNextAsync() => ValueTask.FromResult(inner.MoveNext());

        public ValueTask DisposeAsync()
        {
            inner.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
