using System.Dynamic;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.Runtime.Documents;

namespace OrgRepoSearch;

public class BedrockService
{
    private static readonly string bedrockToolName = "summarize_repository";
    private readonly IAmazonBedrockRuntime _amazonBedrockRuntime;
    public BedrockService(IAmazonBedrockRuntime amazonBedrockRuntime)
    {
        _amazonBedrockRuntime = amazonBedrockRuntime;
    }

    public async Task<bool> MakeRequest(string content, MemoryStream contentMemoryStream, RepoDetails details, SearchConfig config)
    {
        // Set the model ID, e.g., Claude 3 sonnet
        var modelId = config.BedrockModel;

        ToolSpecification toolSpecification = new ToolSpecification();

        dynamic toolSpec = new ExpandoObject();
        toolSpec.type = "object";

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
        required = required.Concat(new []{"isDeprecated", "serviceNames"}).ToArray();


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

        toolSpec.properties = toolProperties;
        toolSpec.required = required;

        Document docTest2 = Document.FromObject(toolSpec);

        Document docTest3 = Document.FromObject(
            new
            {
                type = "object",
                properties = toolProperties,
                required = required
            });

        Document docTest = Document.FromObject(
            new
            {
                type = "object",
                properties = new
                {
                    summary = new 
                    {
                        type = "string",
                        description = "A brief one-line or two-line summary of the repository contents and their suitability as example code and/or code samples based on the README document provided."
                    },
                    hasTests = new
                    {
                        type = "boolean",
                        description = "Indicates if this repository mentions running any code tests."
                    },
                    levelOfDetail = new
                    {
                        type = "integer",
                        description = "Rate the level of detail of the README on a scale from 1-10.",
                        minimum = 1,
                        maximum = 10
                    },
                    isDeprecated = new
                    {
                        type = "boolean",
                        description = "Indicates if this repository mentions being deprecated."
                    },
                    isApplication = new
                    {
                        type = "boolean",
                        description = "Indicates if this repository is a standalone application."
                    },
                    isSamples = new
                    {
                        type = "boolean",
                        description = "Indicates if this repository includes a set independent of code examples or code samples, instead of one large application."
                    },
                    serviceNames = new
                    {
                        type = "array",
                        description = "An array of AWS services mentioned in the README.",
                        items = new 
                        {
                            type = "string"
                        }
                    },
                },
                required = new[] {"summary", "hasTests", "levelOfDetail", "usesSdk", "isApplication", "isSamples", "serviceNames", "isDeprecated" }
            });

        toolSpecification.InputSchema = new ToolInputSchema() { Json = docTest3 };
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
            InferenceConfig = new InferenceConfiguration(){MaxTokens = 2000, Temperature = 0},
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
                // first tool use
                var toolUseBlock = responseContent.FirstOrDefault(t =>
                    t.ToolUse.Name == bedrockToolName);

                Console.WriteLine($"Finished Bedrock Converse request for {details.Name}.");
                var toolOutputs = toolUseBlock.ToolUse.Input.AsDictionary();
                //details.GenAiSummary = toolOutputs["summary"].ToString();

                if (toolOutputs["serviceNames"].IsList())
                {
                    details.ServiceNames = string.Join(',',
                        toolOutputs["serviceNames"].AsList().Select(s => s.ToString()));
                }

                if (toolOutputs["isDeprecated"].IsBool())
                {
                    details.IsDeprecated = toolOutputs["isDeprecated"].AsBool();
                }

                foreach (var cr in config.GenAiCriteria)
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
                        details.AddCriterion(cr.Name, cr.Description, cr.Weight,intVal);
                    }
                    else if (cr.Type == "string")
                    {
                        details.GenAiSummary = toolOutputs[cr.Name].ToString();
                    }
                }

                //if (toolOutputs["levelOfDetail"].IsInt())
                //{
                //    var lod = toolOutputs["levelOfDetail"].AsInt();
                //    details.AddCriterion("LevelOfDetail", "", 0.22,
                //        lod);
                //}

                //if (toolOutputs["isSamples"].IsBool())
                //{
                //    var ia = toolOutputs["isSamples"].AsBool();
                //    details.AddCriterion("isSamples", "", 0.15,
                //        ia ? 1 : 0);
                //}

                //if (toolOutputs["isApplication"].IsBool())
                //{
                //    var ia = toolOutputs["isApplication"].AsBool();
                //    details.AddCriterion("isApplication", "", 0.08,
                //        ia ? 1: 0);
                //}

                //if (toolOutputs["hasTests"].IsBool())
                //{
                //    var ia = toolOutputs["hasTests"].AsBool();
                //    details.AddCriterion("hasTests", "", 0.15,
                //        ia ? 1 : 0);
                //}

                return true;
            }
        }
        catch (AmazonBedrockRuntimeException e)
        {
            Console.WriteLine($"ERROR: Can't invoke '{modelId}'. Reason: {e.Message}");
        }
        catch (Exception e)
        {
            Console.WriteLine($"ERROR: Can't process Bedrock response. Reason: {e.Message}");
        }

        return false;
    }
}