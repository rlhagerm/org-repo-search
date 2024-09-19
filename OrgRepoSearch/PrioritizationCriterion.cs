namespace OrgRepoSearch;

/// <summary>
/// Defines a criterion for ranking a repository based on metrics.
/// </summary>
public class PrioritizationCriterion
{
    public string Name { get; set; } = null!;
    public string Description { get; set; } = null!;
    public double Weight { get; set; }
    public int Rank { get; set; }
    public int Value { get; set; }

    /// <summary>
    /// A weighted criteria value is the item's rank multiplied by the criterion's weight.
    /// </summary>
    public double CriteriaWeightRank => Rank * Weight;

    /// <summary>
    /// Set a distinct rank within the set, allowing for duplicates and binary values.
    /// </summary>
    /// <param name="distinctSortedValues">The set of distinct and sorted values available.</param>
    /// <param name="totalCount">The total number of values, before filtering to distinct only.</param>
    public void SetDistinctRank(List<int> distinctSortedValues, int totalCount)
    {
        var index = distinctSortedValues.IndexOf(this.Value);
        var totalValues = distinctSortedValues.Count;
        var avg = totalCount / totalValues;
        this.Rank = avg * (index);
    }
}
