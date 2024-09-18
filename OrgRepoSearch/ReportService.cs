// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using CsvHelper;

namespace OrgRepoSearch;

/// <summary>
/// Service for creating a repository report for the organization.
/// </summary>
public class ReportService
{

    public ReportService()
    {

    }


    /// <summary>
    /// Put the work items into a CSV in a memory stream..
    /// </summary>
    /// <param name="workItems">The work item collection.</param>
    /// <param name="memoryStream">The memory stream to use.</param>
    /// <param name="streamWriter">The stream writer to use.</param>
    /// <param name="csvWriter">The CSV writer to use.</param>
    /// <returns>Async task.</returns>
    public async Task GetCsvStreamFromWorkItems(IList<RepoDetails> repoDetailsList, MemoryStream memoryStream, StreamWriter streamWriter, CsvWriter csvWriter)
    {
        csvWriter.WriteHeader<RepoDetails>();
        await csvWriter.NextRecordAsync();
        await csvWriter.WriteRecordsAsync(repoDetailsList);
        await csvWriter.FlushAsync();
        await streamWriter.FlushAsync();
        memoryStream.Position = 0;
    }
}