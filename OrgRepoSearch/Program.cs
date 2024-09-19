// See https://aka.ms/new-console-template for more information
using CsvHelper;
using Octokit;
using OrgRepoSearch;
using System.Globalization;
using System.IO;
using Amazon.BedrockRuntime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Logging.Debug;
using System.Reflection.PortableExecutable;
using CsvHelper.Configuration;
using Amazon.Auth.AccessControlPolicy;
using System;
using System.Text.Json;

public static class OrgRepoSearchRunner
{
    public static async Task Main(string[] args)
    {
        // Set up dependency injection for the Amazon service.
        using var host = Host.CreateDefaultBuilder(args)
            .ConfigureLogging(logging =>
                logging.AddFilter("System", LogLevel.Debug)
                    .AddFilter<DebugLoggerProvider>("Microsoft", LogLevel.Information)
                    .AddFilter<ConsoleLoggerProvider>("Microsoft", LogLevel.Trace))
            .ConfigureServices((_, services) =>
                services.AddAWSService<IAmazonBedrockRuntime>()
                    .AddTransient<BedrockService>()
            )
            .Build();

        var bedrockService = host.Services.GetRequiredService<BedrockService>();
        // Load configuration
        var searchConfig = new SearchConfig();
        using (StreamReader r = new StreamReader("repo_search_config.json"))
        {
            string json = r.ReadToEnd();
            searchConfig = JsonSerializer.Deserialize<SearchConfig>(json, new JsonSerializerOptions(){PropertyNameCaseInsensitive = true});
        }
        bedrockService.GenerateSpec(searchConfig);

        var pat = "";
        Console.WriteLine("Enter your GitHub user access token:");
        pat = Console.ReadLine();

        var orgName = "aws-samples";
        Console.WriteLine("Enter the org to search:");
        orgName = Console.ReadLine();

        var yearsToInclude = searchConfig!.YearsIncluded;
        var languageList = searchConfig.SdkLanguages;

        Console.WriteLine($"Generating repository list from {orgName} organization:");

        try
        {
            var client = new GitHubClient(new ProductHeaderValue("tributaries-search"));

            var tokenAuth = new Credentials(pat);
            client.Credentials = tokenAuth;

            //var repos = await client.Repository.GetAllForOrg(orgName,
           //     new ApiOptions() { PageCount = 40, PageSize = 10 });

            var repos = await client.Repository.GetAllForOrg(orgName);
            var minUpdatedDate = DateTime.Today.AddYears(-yearsToInclude);
            List<RepoDetails> repoDetailsList = new List<RepoDetails>();
            var totalRepoCount = repos.Count;
            Console.WriteLine($"***Found {totalRepoCount} total repos in {orgName}.");
            var limitcount = 0;
            var taskList = new List<Task<bool>>();
            // Order by most recent first.
            repos = repos.OrderByDescending(r => r.PushedAt?.DateTime.Date).ToList();
            // Discard those not updated in the last year and those marked to ignore.
            repos = repos.Where(r => r.PushedAt?.DateTime.Date > DateTime.Today.AddYears(-yearsToInclude) && !searchConfig.IgnoreRepos.Contains(r.Name)).ToList();
            var discardCount = totalRepoCount - repos.Count;
            Console.WriteLine($"***Rejected {discardCount} repos not updated after {minUpdatedDate.ToShortDateString()}.");
            var noReadmeCount = 0;
            var notSdkLanguageCount = 0;
            foreach (var repository in repos)
            {
                if (languageList.Contains(repository.Language))
                {
                    // create a repo object
                    var repoDetails = new RepoDetails()
                    {
                        Name = repository.Name,
                        Language = repository.Language,
                        Url = repository.HtmlUrl,
                        LastModified = repository.PushedAt?.DateTime.Date ??
                                       DateTime.MinValue.Date,
                        Summary = repository.Description,
                        OpenIssuesCount = repository.OpenIssuesCount
                    };
                    foreach (var repoCriteria in searchConfig.RepoCriteria)
                    {
                        var value = repository.GetType().GetProperty(repoCriteria.DataField).GetValue(repository);
                        if (value is int)
                        {
                            repoDetails.AddCriterion(repoCriteria.Name,
                                repoCriteria.Description, repoCriteria.Weight, (int)value);
                        }
                    }

                    // Try to get the content of the README file, to be used to generate tool results with Bedrock.
                    Console.WriteLine($"Fetching README contents for repository {repoDetails.Name}.");
                    try
                    {
                        var readme =
                            await client.Repository.Content.GetReadme(repository.Id);
                        var readmeContent = readme.Content;

                        byte[] byteArray =
                            System.Text.Encoding.ASCII.GetBytes(readmeContent);
                        MemoryStream stream = new MemoryStream(byteArray);
                        Thread.Sleep(200);
                        var summaryResponseTask =
                            bedrockService.MakeRequest("readmeContent", stream, repoDetails, searchConfig);
                        taskList.Add(summaryResponseTask);

                        repoDetailsList.Add(repoDetails);
                    }
                    catch (Exception ex)
                    {
                        noReadmeCount++;
                        Console.WriteLine(
                            $"unable to get readme for {repoDetails.Name}, {ex.Message}");
                    }
                }
                else
                {
                    notSdkLanguageCount++;
                }
            }
            
            var summaryResults = await Task.WhenAll(taskList);
            Console.WriteLine($"AI results returned true: {summaryResults.Count(s => s)}.");

            Console.WriteLine($"***Rejected {noReadmeCount} repos that do not have a README.");
            Console.WriteLine($"***Rejected {notSdkLanguageCount} repos that do not use an SDK language.");

            var deprecatedCount = repoDetailsList.Where(r => r.IsDeprecated).Count();

            // Remove deprecated repositories before ranking.
            repoDetailsList =
                repoDetailsList.Where(r => !r.IsDeprecated).ToList();
            Console.WriteLine($"***Rejected {deprecatedCount} repos marked as deprecated.");
            var criteriaWeights = repoDetailsList.First().Criterion;

            var totalCount = repoDetailsList.Count;
            foreach (var cr in criteriaWeights)
            {
                // get the distinct values
                var distinctSortedValues = repoDetailsList
                    .Where(s => s.Criterion.Any(c => c.Name == cr.Name)).Select(s => s.Criterion.First(c => c.Name == cr.Name).Value)
                    .Distinct().OrderBy(v => v);
                foreach (var detail in repoDetailsList)
                {
                    if (detail.Criterion.Any(c => c.Name == cr.Name))
                    {
                        var singleCriteria =
                            detail.Criterion.First(c => c.Name == cr.Name);
                        singleCriteria.SetDistinctRank(distinctSortedValues.ToList(),
                            totalCount);
                    }
                }
            }

            // Order the list.
            repoDetailsList =
                repoDetailsList.OrderByDescending(r => r.TotalWeightCalc).ToList();

            var repoDetailsOutput =
                repoDetailsList.Select(r => r.GenerateOutputRow()).ToList();

            var fileName = searchConfig.OutputFile;
            await using var fileStream = File.Create(fileName);
            await using var streamWriter = new StreamWriter(fileStream);

            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = false,
            };

            await using var csvWriter =
                new CsvWriter(streamWriter, config);

            // Write the headers
            var firstRow = repoDetailsOutput.FirstOrDefault();
            var outputRowAsDictionary = firstRow as IDictionary<string, object>;
            foreach (var header in outputRowAsDictionary.Keys)
            {
                csvWriter.WriteField(header);
            }

            await csvWriter.NextRecordAsync();
            await csvWriter.WriteRecordsAsync(repoDetailsOutput);
            await csvWriter.FlushAsync();
            await streamWriter.FlushAsync();

            Console.WriteLine($"Repo list written to {fileName}.");

            // Print some summary information.

            Console.WriteLine($"\tTotal repositories found: {repoDetailsList.Count}");

            foreach (var language in languageList)
            {
                var languageCount = repoDetailsList.Count(r => r.Language == language);
                Console.WriteLine($"\t\t{language}: {languageCount} repos found.");
            }

        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }
}