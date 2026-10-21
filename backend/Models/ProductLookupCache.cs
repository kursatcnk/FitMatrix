namespace FitMatrix.Api.Models;

public class ProductLookupCache
{
    public Guid Id { get; set; }
    public required string NormalizedQuery { get; set; }
    public required string ResponseJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
