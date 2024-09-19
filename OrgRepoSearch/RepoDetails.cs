using System.Dynamic;

namespace OrgRepoSearch;

public class RepoDetails
{
    public string Name { get; set; }
    public string Url { get; set; }
    public string Language { get; set; }
    public string Summary { get; set; }

    public string GenAiSummary { get; set; }
    public DateTime LastModified { get; set; }

    public List<PrioritizationCriterion> Criterion { get; set; }

    public double TotalWeightCalc => Criterion.Sum(c => c.CriteriaWeightRank);

    public string ServiceNames { get; set; }
    public bool IsDeprecated { get; set; }
    public int OpenIssuesCount { get; set; }

    public RepoDetails()
    {
        Criterion = new List<PrioritizationCriterion>();
    }

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
            //outputRowAsDictionary.Add($"{criteria.Name}_Weight",criteria.Weight);
            outputRowAsDictionary.Add($"{criteria.Name}_Rank", criteria.Rank);
            outputRowAsDictionary.Add($"{criteria.Name}_Value", criteria.Value);
            outputRowAsDictionary.Add($"{criteria.Name}_WeightRank", criteria.CriteriaWeightRank);
        }
        outputRow.TotalWeightCalc = this.TotalWeightCalc;

        return outputRow;
    }
}