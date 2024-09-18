using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OrgRepoSearch;

public class GenAiCriterion
{
    public string Name { get; set; }
    public string Type { get; set; }
    public string Description { get; set; }
    public double Weight { get; set; }
    public int Minimum { get; set; }
    public int Maximum { get; set; }
}

public class RepoCriterion
{
    public string Name { get; set; }
    public string Description { get; set; }
    public string DataField { get; set; }
    public double Weight { get; set; }
}

public class SearchConfig
{
    public string BedrockModel { get; set; }
    public string OutputFile { get; set; }
    public int YearsIncluded { get; set; }
    public List<string> SdkLanguages { get; set; }
    public List<string> IgnoreRepos { get; set; }
    public List<RepoCriterion> RepoCriteria { get; set; }
    public string GenAiSystemText { get; set; }
    public string GenAiContentText { get; set; }
    public List<GenAiCriterion> GenAiCriteria { get; set; }
}