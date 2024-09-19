using System.Dynamic;

namespace OrgRepoSearch;

/// <summary>
/// Details and metrics about a GitHub repository.
/// </summary>
public class RepoDetails
{
    public string Name { get; set; } = null!;
    public string Url { get; set; } = null!;
    public string Language { get; set; } = null!;
    public string Summary { get; set; } = null!;
    public string GenAiSummary { get; set; } = null!;
    public DateTime LastModified { get; set; }
    public List<PrioritizationCriterion> Criterion { get; set; }
    public double TotalWeightCalc => Criterion.Sum(c => c.CriteriaWeightRank);
    public string ServiceNames { get; set; } = null!;
    public bool IsDeprecated { get; set; }
    public int OpenIssuesCount { get; set; }

    public RepoDetails()
    {
        Criterion = new List<PrioritizationCriterion>();
    }

    /// <summary>
    /// Add a criterion to the set of criteria.
    /// </summary>
    /// <param name="name"></param>
    /// <param name="description"></param>
    /// <param name="weight"></param>
    /// <param name="value"></param>
    public void AddCriterion(string name, string description, double weight, int value)
    {
        var criteria = new PrioritizationCriterion()
        {
            Name = name,
            Description = description,
            Weight = weight,
            Value = value
        };
        Criterion.Add(criteria);
    }

    /// <summary>
    /// Generate the dynamic object to be written out to csv spreadsheet.
    /// </summary>
    /// <returns>The dynamic object</returns>
    public object GenerateOutputRow()
    {
        dynamic outputRow = new ExpandoObject();
        outputRow.Name = this.Name;
        outputRow.Url = this.Url;
        outputRow.Language = this.Language;
        outputRow.Summary = this.Summary;
        outputRow.GeneratedSummary = this.GenAiSummary;
        outputRow.Modified = this.LastModified.ToShortDateString();
        outputRow.ServiceNames = this.ServiceNames;
        outputRow.OpenIssuesCount = this.OpenIssuesCount;

        var outputRowAsDictionary = outputRow as IDictionary<string, object>;
        foreach (var criteria in Criterion)
        {
            outputRowAsDictionary.Add($"{criteria.Name}_Rank", criteria.Rank);
            outputRowAsDictionary.Add($"{criteria.Name}_Value", criteria.Value);
            outputRowAsDictionary.Add($"{criteria.Name}_WeightRank", criteria.CriteriaWeightRank);
        }
        outputRow.TotalWeightCalc = this.TotalWeightCalc;

        return outputRow;
    }
}