namespace Tabletop.Geometry;

/// <summary>Which models are out of coherency, by index into the input. Flagged, never prevented.</summary>
public sealed record CoherencyResult(bool Coherent, IReadOnlyList<int> OutOfCoherency);

/// <summary>Rulebook p.7: within chainDistance of at least one other model, forming one uninterrupted chain, and within maxSpread of every other model in it. p.11 allows elevatedChainDistance to a model at a different elevation.</summary>
public static class Coherency
{
    public static CoherencyResult Check(IReadOnlyList<PlacedModel> models, double chainDistance, double elevatedChainDistance, double maxSpread)
    {
        var n = models.Count;
        if (n <= 1) return new CoherencyResult(true, []);

        var d = new double[n, n];
        var chain = new double[n, n];
        for (var i = 0; i < n; i++)
            for (var j = i + 1; j < n; j++)
            {
                d[i, j] = d[j, i] = Distance.EdgeToEdge(models[i], models[j]);
                // p.11: a pair at different elevation is chained at the wider distance. The precondition
                // ("if not all models can fit on elevation") is deliberately not tested — see the plan text.
                var elevated = Distance.Vertical(models[i], models[j]) > Tolerance.Distance;
                chain[i, j] = chain[j, i] = elevated ? elevatedChainDistance : chainDistance;
            }

        var component = new int[n];
        Array.Fill(component, -1);
        var count = 0;
        for (var s = 0; s < n; s++)
        {
            if (component[s] >= 0) continue;
            var stack = new Stack<int>();
            stack.Push(s);
            component[s] = count;
            while (stack.Count > 0)
            {
                var i = stack.Pop();
                for (var j = 0; j < n; j++)
                    if (component[j] < 0 && d[i, j] <= chain[i, j] + Tolerance.Distance)
                    {
                        component[j] = count;
                        stack.Push(j);
                    }
            }
            count++;
        }

        var sizes = new int[count];
        foreach (var c in component) sizes[c]++;
        var main = Array.IndexOf(sizes, sizes.Max());
        var body = Enumerable.Range(0, n).Where(i => component[i] == main).ToList();

        var outOf = new List<int>();
        for (var i = 0; i < n; i++)
        {
            if (component[i] != main) outOf.Add(i);
            else if (body.Any(j => j != i && d[i, j] > maxSpread + Tolerance.Distance)) outOf.Add(i);
        }
        return new CoherencyResult(outOf.Count == 0, outOf);
    }

    /// <summary>How far the unit is from coherency, in inches: for each model outside the largest chain its distance to the nearest model of it,
    /// and for each model of the chain beyond maxSpread of another, the excess. Zero when coherent; a move towards coherency lowers it.</summary>
    public static double Shortfall(IReadOnlyList<PlacedModel> models, double chainDistance, double elevatedChainDistance, double maxSpread)
    {
        var check = Check(models, chainDistance, elevatedChainDistance, maxSpread);
        if (check.Coherent) return 0;
        var outOf = check.OutOfCoherency.ToHashSet();
        var body = Enumerable.Range(0, models.Count).Where(i => !outOf.Contains(i)).ToList();
        if (body.Count == 0) body = [Enumerable.Range(0, models.Count).First(i => !outOf.Contains(i) || i == 0)];
        double total = 0;
        foreach (var i in outOf)
        {
            var others = body.Where(j => j != i).ToList();
            if (others.Count == 0) continue;
            var nearest = others.Min(j => Distance.EdgeToEdge(models[i], models[j]));
            var farthest = others.Max(j => Distance.EdgeToEdge(models[i], models[j]));
            total += nearest > chainDistance ? nearest : Math.Max(0, farthest - maxSpread);
        }
        return total;
    }
}
