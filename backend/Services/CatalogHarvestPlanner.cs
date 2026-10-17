using FitMatrix.Api.DTOs;

namespace FitMatrix.Api.Services;

public enum CatalogHarvestOutcome
{
    Imported,
    Updated,
    AlreadyInCatalog,
    NoVerifiedMatch,
    Failed
}

public sealed record CatalogHarvestPlan(CatalogHarvestOutcome? Skip, DiscoveryCatalogRequestDto? Request, string? Message);

// A model name plus the product family the curated list placed it under, e.g. [audio:headphone].
public sealed record CatalogHarvestQuery(string Query, ProductClassification? Hint = null);

// Decides what the harvester should do with one discovery response. Kept free of I/O so
// the quality rules can be unit tested without the network.
public static class CatalogHarvestPlanner
{
    private static readonly HashSet<string> VerifiedStatuses = ["official-structured", "source-structured"];

    public static CatalogHarvestPlan Plan(ProductDiscoveryResponseDto response, bool requireVerified, ProductClassification? hint = null)
    {
        // Discovery skips the network when an enriched exact local product exists; any exact name match
        // (score 90+ covers brand + model, MPN and full display name) means the model is already stored.
        var exactLocal = response.LocalMatches.FirstOrDefault(match => match.MatchScore >= 90);
        var candidate = response.ExternalMatch;

        if (candidate is null)
        {
            return exactLocal is not null
                ? new CatalogHarvestPlan(CatalogHarvestOutcome.AlreadyInCatalog, null, exactLocal.DisplayName)
                : new CatalogHarvestPlan(CatalogHarvestOutcome.NoVerifiedMatch, null, "Kaynakta ürün bulunamadı.");
        }

        if (candidate.WidthCm is not > 0 || candidate.HeightCm is not > 0 || candidate.DepthCm is not > 0)
            return new CatalogHarvestPlan(CatalogHarvestOutcome.NoVerifiedMatch, null, "Üç eksen gövde ölçüsü yayımlanmamış.");
        if (string.IsNullOrWhiteSpace(candidate.SourceUrl))
            return new CatalogHarvestPlan(CatalogHarvestOutcome.NoVerifiedMatch, null, "Ölçünün kaynak adresi yok.");
        if (candidate.MeasurementStatus == "identity-only")
            return new CatalogHarvestPlan(CatalogHarvestOutcome.NoVerifiedMatch, null, "Yalnızca ürün kimliği bulundu.");
        if (requireVerified && !VerifiedStatuses.Contains(candidate.MeasurementStatus))
            return new CatalogHarvestPlan(CatalogHarvestOutcome.NoVerifiedMatch, null, $"Ölçü güveni yetersiz: {candidate.MeasurementStatus}.");
        if (string.IsNullOrWhiteSpace(candidate.Brand))
            return new CatalogHarvestPlan(CatalogHarvestOutcome.NoVerifiedMatch, null, "Marka belirlenemedi.");

        var modelName = candidate.ModelName ?? candidate.ManufacturerPartNumber ?? candidate.ProductName;
        // The curated list knows "NZXT H5 Flow" is a case even when the product page never says so.
        var classification = hint ?? new ProductClassification(candidate.SuggestedCategory, candidate.SuggestedEditorModel, candidate.SuggestedColor);
        var (width, height, depth) = ProductTaxonomy.OrientFlatDevice(classification.EditorModel, candidate.WidthCm.Value, candidate.HeightCm.Value, candidate.DepthCm.Value);
        if (!ProductTaxonomy.IsPlausibleSize(classification.EditorModel, width, height, depth))
            return new CatalogHarvestPlan(CatalogHarvestOutcome.NoVerifiedMatch, null, $"Ölçüler bu ürün türü için olağan dışı ({width} × {height} × {depth} cm).");
        var request = new DiscoveryCatalogRequestDto(
            candidate.Provider,
            candidate.ProductName,
            candidate.Brand,
            modelName,
            candidate.ManufacturerPartNumber,
            candidate.Gtin,
            width,
            height,
            depth,
            // Matches the editor's auto-catalog behaviour: the body outline is used until a
            // manufacturer publishes a separate stand or base footprint.
            width,
            depth,
            candidate.WeightKg,
            classification.CategorySlug,
            classification.EditorModel,
            classification.DisplayColor,
            candidate.SourceUrl,
            candidate.MeasurementStatus,
            candidate.Specifications.Select(spec => new DiscoverySpecificationDto(spec.Group, spec.Name, spec.Value, spec.Unit)).ToList());

        return new CatalogHarvestPlan(null, request, null);
    }

    // Lines are model names; "# ..." is a comment and "[category:model]" sets the family for the lines below it.
    public static IReadOnlyList<CatalogHarvestQuery> ParseQueryList(IEnumerable<string> lines)
    {
        var queries = new List<CatalogHarvestQuery>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ProductClassification? hint = null;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                var parts = line[1..^1].Split(':', 2, StringSplitOptions.TrimEntries);
                hint = parts.Length == 2 ? ProductTaxonomy.FromHint(parts[0], parts[1]) : null;
                continue;
            }
            if (line.Length < 3 || line.StartsWith('#') || !seen.Add(line)) continue;
            queries.Add(new CatalogHarvestQuery(line, hint));
        }
        return queries;
    }
}
