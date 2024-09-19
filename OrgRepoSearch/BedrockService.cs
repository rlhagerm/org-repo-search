using System.Dynamic;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.Runtime.Documents;

namespace OrgRepoSearch;

/// <summary>
/// Service that wraps calls made to the Bedrock API for LLM responses.
/// </summary>
public class BedrockService
{
    private static readonly string bedrockToolName = "summarize_repository";
    private readonly IAmazonBedrockRuntime _amazonBedrockRuntime;
    public BedrockService(IAmazonBedrockRuntime amazonBedrockRuntime)
    {
        _amazonBedrockRuntime = amazonBedrockRuntime;
    }

    /// <summary>
    /// The tool specification used in the Converse API.
    /// </summary>
    public static Document ToolSpec { get; set; }

    /// <summary>
    /// Generate the Tool Specification document as a static property.
    /// </summary>
    /// <param name="config">The configuration for the search and criteria.</param>
    public void GenerateSpec(SearchConfig config)
    {
        dynamic toolProperties = new ExpandoObject();

        // These properties are added by default.
        toolProperties.isDeprecated = new ExpandoObject();
        toolProperties.isDeprecated.type = "boolean";
        toolProperties.isDeprecated.description =
            "Indicates if this repository mentions being deprecated.";

        toolProperties.serviceNames = new ExpandoObject();
        toolProperties.serviceNames.type = "array";
        toolProperties.serviceNames.description =
            "An array of AWS services mentioned in the README.";
        toolProperties.serviceNames.items = new ExpandoObject();
        toolProperties.serviceNames.items.type = "string";


        var toolPropertiesAsDictionary = toolProperties as IDictionary<string, object>;
        var required = config.GenAiCriteria.Select(c => c.Name).ToArray();
        required = required.Concat(new[] { "isDeprecated", "serviceNames" }).ToArray();

        foreach (var criteria in config.GenAiCriteria)
        {
            dynamic cr = new ExpandoObject();
            cr.type = criteria.Type;
            cr.description = criteria.Description;

            if (criteria.Type == "integer")
            {
                cr.minimum = criteria.Minimum;
                cr.maximum = criteria.Maximum;
            }
            toolPropertiesAsDictionary.Add(criteria.Name, cr);
        }

        Document toolSpecDocument = Document.FromObject(
            new
            {
                type = "object",
                properties = toolProperties,
                required = required
            });

        BedrockService.ToolSpec = toolSpecDocument;
    }

    /// <summary>
    /// Make the Bedrock Converse tool request with the README document as an attachment.
    /// </summary>
    /// <param name="contentMemoryStream">Memory stream of the README contents.</param>
    /// <param name="details">The object wrapping the repository details information.</param>
    /// <param name="config">Common search config, containing the criteria.</param>
    /// <returns>True if successful.</returns>
    public async Task<bool> MakeRepoToolRequest(MemoryStream contentMemoryStream, RepoDetails details, SearchConfig config)
    {
        // Set the model ID, e.g., Claude 3 sonnet
        var modelId = config.BedrockModel;

        ToolSpecification toolSpecification = new ToolSpecification();

        toolSpecification.InputSchema = new ToolInputSchema() { Json = BedrockService.ToolSpec };
        toolSpecification.Name = bedrockToolName;
        toolSpecification.Description = "Tool to summarize a github repository by its README contents";

        // Create a request with the model ID and the model's native request payload.
        var request = new ConverseRequest()
        {
            ModelId = modelId,
            System = new List<SystemContentBlock>()
            {
                new SystemContentBlock()
                {
                    Text = config.GenAiSystemText
                }
            },
            Messages = new List<Message>()
            {
                new Message()
                {
                    Role = ConversationRole.User,
                    Content = new List<ContentBlock>()
                    {
                        new ContentBlock()
                        {
                            Text = config.GenAiContentText
                        },
                        new ContentBlock()
                        {
                            Document = new DocumentBlock()
                            {
                                Format = DocumentFormat.Md,
                                Name = "README",
                                Source = new DocumentSource()
                                {
                                    Bytes = contentMemoryStream
                                }
                            }
                        }
                    }
                }
            },
            InferenceConfig = new InferenceConfiguration()
            {
                MaxTokens = 2000,
                Temperature = 0
            },
            ToolConfig = new ToolConfiguration()
            {
                Tools = new List<Tool>()
                {
                    new Tool()
                    {
                        ToolSpec = toolSpecification
                    }
                },
                ToolChoice = new ToolChoice()
                {
                    Tool = new SpecificToolChoice()
                    {
                        Name = bedrockToolName
                    }
                }
            }
        };

        try
        {
            // Send the request to the Bedrock Runtime and wait for the response.
            Console.WriteLine($"Making Bedrock Converse request for {details.Name}.");
            var response = await _amazonBedrockRuntime.ConverseAsync(request);
            if (response.StopReason == StopReason.Tool_use)
            {
                var responseContent = response.Output.Message.Content;
                // First tool use contains the responses for that tool.
                var toolUseBlock = responseContent.First(t =>
                    t.ToolUse.Name == bedrockToolName);

                Console.WriteLine($"Finished Bedrock Converse request for {details.Name}.");
                var toolOutputs = toolUseBlock.ToolUse.Input.AsDictionary();

                if (toolOutputs.ContainsKey("serviceNames") && toolOutputs["serviceNames"].IsList())
                {
                    details.ServiceNames = string.Join(',',
                        toolOutputs["serviceNames"].AsList().Select(s => s.ToString()));
                }

                if (toolOutputs.ContainsKey("isDeprecated") && toolOutputs["isDeprecated"].IsBool())
                {
                    details.IsDeprecated = toolOutputs["isDeprecated"].AsBool();
                }

                foreach (var cr in config.GenAiCriteria)
                {
                    if (toolOutputs.ContainsKey(cr.Name))
                    {
                        if (cr.Type == "boolean")
                        {
                            int boolVal = 0;
                            if (toolOutputs[cr.Name].IsBool())
                            {
                                boolVal = toolOutputs[cr.Name].AsBool() ? 1 : 0;
                            }

                            details.AddCriterion(cr.Name, cr.Description, cr.Weight,
                                boolVal);
                        }
                        else if (cr.Type == "integer")
                        {
                            var intVal = 0;
                            if (toolOutputs[cr.Name].IsInt())
                            {
                                intVal = toolOutputs[cr.Name].AsInt();
                            }

                            details.AddCriterion(cr.Name, cr.Description, cr.Weight,
                                intVal);
                        }
                        else if (cr.Type == "string")
                        {
                            details.GenAiSummary = toolOutputs[cr.Name].ToString();
                        }
                    }
                }

                return true;
            }
        }
        catch (AmazonBedrockRuntimeException e)
        {
            Console.WriteLine($"Bedrock ERROR: Can't invoke '{modelId}'. Reason: {e.Message}");
        }
        catch (Exception e)
        {
            Console.WriteLine($"ERROR: Failure making the request. Reason: {e.Message}");
        }

        return false;
    }
}