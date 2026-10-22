using ErrorOr;

namespace PF.BlazorReference.Web.Features.Registration;

/// <summary>Maps use-case errors onto form fields. Error codes are field names by convention.</summary>
public static class FieldErrors
{
    public static IReadOnlyDictionary<string, string[]> From(IEnumerable<Error> errors)
    {
        var list = errors.ToList();

        // Validation and conflict errors are expected outcomes the user can fix. Anything else is
        // unexpected, so let it reach the error handling instead of dressing it up as a field message.
        if (list.Any(e => e.Type is not (ErrorType.Validation or ErrorType.Conflict)))
            throw new InvalidOperationException(
                "Unexpected error: " + string.Join("; ", list.Select(e => $"{e.Code}: {e.Description}")));

        return list
            .GroupBy(e => e.Code)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());
    }
}
