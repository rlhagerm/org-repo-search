using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OrgRepoSearch;
public class PrioritizationCriterion
{
    public string Name { get; set; }

    public string Description { get; set; }
    public double Weight { get; set; }
    public int Rank { get; set; }
    public int Value { get; set; }


    public double CriteriaWeightRank => Rank * Weight;

    public void SetDistinctRank(List<int> distinctSortedValues, int totalCount)
    {
        var index = distinctSortedValues.IndexOf(this.Value);
        var totalValues = distinctSortedValues.Count();
        var avg = totalCount / totalValues;
        this.Rank = avg * (index);
    }
}
