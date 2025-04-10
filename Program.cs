using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

public static class Program
{
    // --- Configuration & Defaults ---
    private const string AppName = "openai-csharp";
    private const string AppVersion = "1.0"; // Equivalent version for C# port
    private const string DefaultApiName = "chat/completions";
    private const string DefaultTopic = "General";

    private static readonly string _openAIDataDir = Environment.GetEnvironmentVariable("OPENAI_DATA_DIR") ??
                                        Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ??
                                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".openai");
    private static readonly string _openAIProvider = Environment.GetEnvironmentVariable("OPENAI_COMPATIBLE_PROVIDER") ?? "OPENAI";
    private static readonly string _openAIApiEndpoint = Environment.GetEnvironmentVariable($"{_openAIProvider}_API_ENDPOINT") ?? "https://api.openai.com/v1";
    private static readonly string _openAIApiKey = Environment.GetEnvironmentVariable($"{_openAIProvider}_API_KEY") ?? "";
    private static readonly string _openAIApiModel = Environment.GetEnvironmentVariable($"{_openAIProvider}_API_MODEL") ?? "gpt-4o";

    private static readonly HttpClient HttpClient = new HttpClient();

    // --- State Variables ---
    private static bool _chatMode = false;
    private static bool _dryRun = false;
    private static string _apiName = DefaultApiName;
    private static string _topic = DefaultTopic;
    private static string? _dumpFile = null;
    private static string? _dumpedFile = null;
    private static string? _promptFile = null;
    private static string _prompt = "";
    private static List<string> _restArgs = new List<string>();
    private static string _dataFile = "";
    private static string? _tempDir = null;
    private static JsonObject _requestProperties = new JsonObject();

    // --- Entry Point ---
    public static async Task<int> Main(string[] args)
    {
        // Ensure data directory exists
        Directory.CreateDirectory(_openAIDataDir);
        _dataFile = Path.Combine(_openAIDataDir, $"{_topic}.json"); // Initial default

        // Create temp directory for this run
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);

        try
        {
            // Show compatible provider if applicable
            if ((Environment.GetEnvironmentVariable("SUPPRESS_PROVIDER_TIPS") ?? "0") == "0" &&
                !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OPENAI_COMPATIBLE_PROVIDER")))
            {
                Console.Error.WriteLine($"OpenAI compatible provider: {Environment.GetEnvironmentVariable("OPENAI_COMPATIBLE_PROVIDER")}");
            }

            // Check required config
            if (string.IsNullOrEmpty(_openAIApiEndpoint)) RaiseError($"Missing environment variable: {_openAIProvider}_API_ENDPOINT.", 1);
            if (string.IsNullOrEmpty(_openAIApiKey)) RaiseError($"Missing environment variable: {_openAIProvider}_API_KEY.", 1);
            if (string.IsNullOrEmpty(_openAIApiModel)) RaiseError($"Missing environment variable: {_openAIProvider}_API_MODEL.", 1);

            ParseArgs(args);

            _dataFile = Path.Combine(_openAIDataDir, $"{_topic}.json"); // Update dataFile based on parsed topic

            if (_topic == DefaultTopic || File.Exists(_dataFile))
            {
                // Dynamically call the method corresponding to the api_name
                var methodName = $"OpenAI_{_apiName.Replace('/', '_')}";
                var method = typeof(Program).GetMethod(methodName, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.IgnoreCase);

                if (method is null)
                {
                    RaiseError($"API '{_apiName}' is not available.", 12);
                }

                // Assuming all API methods are async Task
                var task = (Task?)method.Invoke(null, null);
                if (task is not null)
                {
                    await task;
                }
                else
                {
                     RaiseError($"Could not invoke API method '{methodName}'.", 12);
                }
            }
            else
            {
                if (!_restArgs.Any()) RaiseError("Prompt for new topic is required", 13);
                CreateTopic();
            }

            return 0; // Success
        }
        catch (Exception ex)
        {
            // RaiseError already prints and exits, but catch other potential exceptions
            Console.Error.WriteLine($"{AppName}: Unexpected error: {ex.Message}");
            return 1; // General error exit code
        }
        finally
        {
            // Cleanup temp directory
            if (!string.IsNullOrEmpty(_tempDir) && Directory.Exists(_tempDir))
            {
                try
                {
                    Directory.Delete(_tempDir, true);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"{AppName}: Warning: Failed to delete temp directory '{_tempDir}': {ex.Message}");
                }
            }
        }
    }

    // --- Helper Functions ---
    private static void RaiseError(string message, int exitCode = 1, bool noPrefix = false)
    {
        if (!noPrefix) Console.Error.Write($"{AppName}: ");
        Console.Error.WriteLine(message);
        // Cleanup temp dir before exiting
        if (!string.IsNullOrEmpty(_tempDir) && Directory.Exists(_tempDir))
        {
             try { Directory.Delete(_tempDir, true); } catch { /* Ignore cleanup errors on exit */ }
        }
        Environment.Exit(exitCode);
    }

    private static JsonObject LoadConversation()
    {
        if (File.Exists(_dataFile))
        {
            try
            {
                var content = File.ReadAllText(_dataFile);
                var parsed = JsonNode.Parse(content);
                return parsed as JsonObject ?? new JsonObject { ["messages"] = new JsonArray() };
            }
            catch (Exception ex)
            {
                RaiseError($"Error reading or parsing conversation file '{_dataFile}': {ex.Message}", 5);
                return new JsonObject { ["messages"] = new JsonArray() }; // Unreachable, but compiler needs it
            }
        }
        else
        {
            // Return default structure if file doesn't exist
            return new JsonObject { ["messages"] = new JsonArray(), ["total_tokens"] = 0 };
        }
    }

     private static void UpdateConversation(string role, JsonNode contentNode)
    {
        var data = LoadConversation();
        var messages = data["messages"] as JsonArray ?? new JsonArray();

        var entry = new JsonObject { ["role"] = role };

        // Handle simple string content vs function call object
        if (contentNode is JsonValue val && val.TryGetValue<string>(out var stringContent))
        {
            entry["content"] = stringContent;
        }
        else if (contentNode is JsonObject objContent) // For function calls or other complex content
        {
             // If it's a function call structure from the response
             if (objContent.ContainsKey("function_call"))
             {
                 entry["function_call"] = objContent["function_call"]?.DeepClone(); // Add the function call details
                 entry["content"] = null; // Per OpenAI spec, content is null for function calls
             }
             else // Otherwise, assume it's a regular content object (though usually it's a string)
             {
                 entry["content"] = objContent.DeepClone();
             }
        }
        else
        {
            // Fallback or error? Assuming string content if not an object.
            entry["content"] = contentNode?.ToJsonString() ?? "";
        }


        messages.Add(entry);
        data["messages"] = messages;

        try
        {
            File.WriteAllText(_dataFile, data.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            RaiseError($"Error writing conversation file '{_dataFile}': {ex.Message}", 6);
        }
    }


    // Overload for simple string content
    private static void UpdateConversation(string role, string content)
    {
         UpdateConversation(role, JsonValue.Create(content)!);
    }


    private static void SaveTokens(int num)
    {
        // Update topic file
        if (File.Exists(_dataFile))
        {
            var data = LoadConversation();
            var currentTokens = data["total_tokens"]?.GetValue<int>() ?? 0;
            data["total_tokens"] = currentTokens + num;
            try
            {
                File.WriteAllText(_dataFile, data.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }
             catch (Exception ex)
            {
                 RaiseError($"Error writing token count to conversation file '{_dataFile}': {ex.Message}", 7);
            }
        }

        // Update global tokens file (less critical if fails)
        var tokensFile = Path.Combine(_openAIDataDir, "total_tokens");
        long totalTokens = 0;
        try
        {
            if (File.Exists(tokensFile))
            {
                long.TryParse(File.ReadAllText(tokensFile), out totalTokens);
            }
            File.WriteAllText(tokensFile, (totalTokens + num).ToString());
        }
        catch (Exception ex)
        {
             Console.Error.WriteLine($"{AppName}: Warning: Failed to update global token file '{tokensFile}': {ex.Message}");
        }
    }

    private static void ReadPrompt()
    {
        bool acceptsProps = true;
        var realPromptParts = new List<string>();
        _requestProperties = new JsonObject(); // Reset properties for this run

        foreach (var word in _restArgs)
        {
            if (acceptsProps && word.StartsWith('+'))
            {
                var prop = word.Substring(1);
                var parts = prop.Split('=', 2);
                var key = parts[0];
                var value = parts.Length > 1 ? parts[1] : ""; // Default to empty string if no '='

                // Determine value type for JsonNode
                JsonNode? jsonValue;
                if (bool.TryParse(value, out var boolVal))
                {
                    jsonValue = JsonValue.Create(boolVal);
                }
                else if (decimal.TryParse(value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var decVal))
                {
                     // Use decimal for precision, check if it's an integer
                    if (decVal == Math.Truncate(decVal)) {
                        jsonValue = JsonValue.Create((long)decVal);
                    } else {
                        jsonValue = JsonValue.Create(decVal);
                    }
                }
                else if ((value.StartsWith('{') && value.EndsWith('}')) || (value.StartsWith('[') && value.EndsWith(']')))
                {
                    try { jsonValue = JsonNode.Parse(value); }
                    catch { jsonValue = JsonValue.Create(value); } // Treat as string if JSON parsing fails
                }
                else
                {
                    jsonValue = JsonValue.Create(value);
                }
                _requestProperties[key] = jsonValue;
            }
            else
            {
                realPromptParts.Add(word);
                acceptsProps = false; // Stop accepting properties once a non-property word is encountered
            }
        }

        _prompt = string.Join(" ", realPromptParts);

        if (!string.IsNullOrEmpty(_prompt))
        {
            if (!string.IsNullOrEmpty(_promptFile))
            {
                Console.Error.WriteLine($"* Prompt file `{_promptFile}` will be ignored as prompt parameters are provided.");
            }
            // Prompt is already stored in _prompt
        }
        else if (!string.IsNullOrEmpty(_promptFile))
        {
            if (!File.Exists(_promptFile)) RaiseError($"File not found: {_promptFile}.", 3);
            try
            {
                _prompt = File.ReadAllText(_promptFile);
                if (string.IsNullOrWhiteSpace(_prompt) && new FileInfo(_promptFile).Length == 0) // Check if truly empty
                {
                     RaiseError($"Empty file: {_promptFile}.", 4);
                }
                 _prompt = _prompt.Trim(); // Trim whitespace like shell would implicitly do
            }
            catch (Exception ex)
            {
                 RaiseError($"Error reading prompt file '{_promptFile}': {ex.Message}", 3);
            }
        }
        else
        {
             // Read from standard input if no prompt args and no -f file
            using (var reader = new StreamReader(Console.OpenStandardInput()))
            {
                _prompt = reader.ReadToEnd().Trim();
            }
        }
         // Ensure prompt isn't empty after all checks
        if (string.IsNullOrEmpty(_prompt) && string.IsNullOrEmpty(_dumpedFile)) // Don't require prompt if using dumped file
        {
             RaiseError("Prompt is required.", 13);
        }
    }

    // --- API Call Functions ---

    // Placeholder/Example for other API endpoints
    public static async Task OpenAI_Models()
    {
        var payload = new JsonObject(); // No payload needed for models list
        var response = await CallApi(payload, HttpMethod.Get);
        Console.WriteLine(response?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "{}");
    }

     public static async Task OpenAI_Moderations()
    {
        ReadPrompt(); // Read prompt and +properties

        var payload = new JsonObject
        {
            ["model"] = "text-moderation-latest",
            ["input"] = _prompt
        };

        // Merge properties from command line
        foreach (var prop in _requestProperties)
        {
            payload[prop.Key] = prop.Value?.DeepClone();
        }


        var response = await CallApi(payload);
        // Process and print results
        var results = response?["results"] as JsonArray;
        if (results != null)
        {
            foreach(var result in results)
            {
                 Console.WriteLine(result?.ToJsonString() ?? "{}");
            }
        } else {
             Console.WriteLine(response?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "{}");
        }
    }

     public static async Task OpenAI_Images_Generations()
    {
        ReadPrompt(); // Read prompt and +properties

        var payload = new JsonObject
        {
            ["n"] = 1,
            ["size"] = "1024x1024", // Default size
            ["response_format"] = "url", // Force URL format as in script
            ["prompt"] = _prompt
        };

        // Merge properties from command line
        foreach (var prop in _requestProperties)
        {
             // Ensure response_format stays "url"
            if(prop.Key.ToLower() != "response_format")
            {
                payload[prop.Key] = prop.Value?.DeepClone();
            }
        }


        var response = await CallApi(payload);
         // Process and print results
        var data = response?["data"] as JsonArray;
        if (data != null)
        {
            foreach(var item in data)
            {
                 Console.WriteLine(item?["url"]?.GetValue<string>() ?? "");
            }
        } else {
             Console.WriteLine(response?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "{}");
        }
    }

     public static async Task OpenAI_Embeddings()
    {
        ReadPrompt(); // Read prompt and +properties

        var payload = new JsonObject
        {
            ["model"] = "text-embedding-ada-002",
            ["input"] = _prompt
        };

        // Merge properties from command line
        foreach (var prop in _requestProperties)
        {
            payload[prop.Key] = prop.Value?.DeepClone();
        }

        var response = await CallApi(payload);
        Console.WriteLine(response?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "{}");
    }


    public static async Task OpenAI_Chat_Completions()
    {
        bool streaming = true; // Default to streaming
        string responseContent = "";
        string responseRole = "assistant"; // Default role
        string? functionName = null;
        JsonObject? functionArgs = null;
        JsonObject payload;
        var accumulatedFnArgs = new StringBuilder();

        if (!string.IsNullOrEmpty(_dumpedFile))
        {
            // Try to load non-streamed content first
            try
            {
                var dumpedContent = await File.ReadAllTextAsync(_dumpedFile);
                var jsonDoc = JsonDocument.Parse(dumpedContent);
                if (jsonDoc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0 &&
                    choices[0].TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content))
                {
                    responseContent = content.GetString() ?? "";
                    Console.WriteLine(responseContent);
                    // No need to save conversation history when using dumped file
                    return;
                }
// If above fails, assume it might be a streamed dump (though script doesn't explicitly support dumping streams)
                // Or handle streamed dump if needed (more complex)
                Console.Error.WriteLine($"{AppName}: Warning: Could not extract content from dumped file '{_dumpedFile}'. Assuming stream or invalid format.");
// Fall through to potentially re-request if logic allows, or just exit?
                 // The bash script exits after cat-ing the file, so we mimic that.
                 // If the file was a stream dump, the bash script wouldn't process it here either.
                 // Let's just output the file content as is, like the bash script does.
                Console.Write(dumpedContent);
                return;

            }
            catch (Exception ex)
            {
                RaiseError($"Error reading or parsing dumped file '{_dumpedFile}': {ex.Message}", 8);
                return; // Unreachable
            }
        }
        else // Normal API request
        {
             ReadPrompt(); // Read prompt and +properties

            payload = new JsonObject
            {
                ["model"] = _openAIApiModel,
// stream defaults to true unless overridden by +stream=false
            };

             // Merge properties from command line, setting stream default
            payload["stream"] = true; // Default
            foreach (var prop in _requestProperties)
            {
                payload[prop.Key] = prop.Value?.DeepClone();
            }
            // Determine final streaming flag
            streaming = payload["stream"]?.GetValue<bool>() ?? true;


            // Prepare messages
            var messages = new JsonArray();
            var conversation = LoadConversation();
            var existingMessages = conversation["messages"] as JsonArray;

            if (_topic != DefaultTopic && existingMessages != null)
            {
                if (_chatMode)
                {
                    // Load all messages for chat mode
                    foreach(var msg in existingMessages) messages.Add(msg.DeepClone());
                }
                else if (existingMessages.Count > 0)
                {
                    // Load only the first (system) message for non-chat mode
                     messages.Add(existingMessages[0].DeepClone());
                }
            }

            // Add user's prompt
            messages.Add(new JsonObject { ["role"] = "user", ["content"] = _prompt });
            payload["messages"] = messages;

             // Check o1 parameters (approximation) - C# doesn't have direct equivalent of test/select easily
            // This check is complex in bash jq; skipping direct C# equivalent for brevity unless critical.
            // Consider adding if needed, potentially checking model name and presence of specific keys.
            // if (payload["model"]?.GetValue<string>().StartsWith("o1") ?? false) { ... check other params ... }

        }


        // --- Perform API Call ---
        if (streaming)
        {
            var responseStream = await CallApiStream(payload);
            if (responseStream == null) return; // Error handled in CallApiStream

            var accumulatedContent = new StringBuilder();

            using (var reader = new StreamReader(responseStream))
            {
                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (line.StartsWith("data: ")) line = line.Substring(6);
                    if (string.IsNullOrWhiteSpace(line) || line == "[DONE]") continue;

                    try
                    {
                        var jsonChunk = JsonNode.Parse(line);
                        var delta = jsonChunk?["choices"]?[0]?["delta"];
                        if (delta == null) continue;

                        // Get role and function info from the first relevant chunk
                        if (string.IsNullOrEmpty(responseRole) || responseRole == "assistant") // Role might change
                        {
                             responseRole = delta["role"]?.GetValue<string>() ?? responseRole;
                        }
                        if (string.IsNullOrEmpty(functionName))
                        {
                            functionName = delta["function_call"]?["name"]?.GetValue<string>();
                        }


                        string? chunkContent = null;
                        if (!string.IsNullOrEmpty(functionName)) {
                             // Accumulate function arguments
                             chunkContent = delta["function_call"]?["arguments"]?.GetValue<string>();
                             if(chunkContent != null) accumulatedFnArgs.Append(chunkContent);
                        } else {
                            // Accumulate regular content
                            chunkContent = delta["content"]?.GetValue<string>();
                             if(chunkContent != null) accumulatedContent.Append(chunkContent);
                        }


                        if (chunkContent != null)
                        {
                            Console.Write(chunkContent); // Write chunk to output immediately
                        }

                        var finishReason = jsonChunk?["choices"]?[0]?["finish_reason"]?.GetValue<string>();
                        if (finishReason == "stop" || finishReason == "function_call") break;
                        if (!string.IsNullOrEmpty(finishReason))
                        {
                             RaiseError($"API error: {finishReason}", 10);
                        }
                    }
                    catch (JsonException ex)
                    {
                        Console.Error.WriteLine($"{AppName}: Warning: Failed to parse JSON stream chunk: {ex.Message} - Line: '{line}'");
                    }
                }
            }
             Console.WriteLine(); // Add newline after streaming finishes

             // Determine final response content for saving history
             if (!string.IsNullOrEmpty(functionName))
             {
                 try
                 {
                     // Attempt to parse the accumulated arguments as JSON
                     functionArgs = JsonNode.Parse(accumulatedFnArgs.ToString()) as JsonObject ?? new JsonObject();
                     // Prepare a structure similar to the bash script for saving
                     responseContent = new JsonObject { ["function_call"] = new JsonObject { ["name"] = functionName, ["arguments"] = functionArgs } }.ToJsonString();
                 }
                 catch (JsonException)
                 {
                     // If args are not valid JSON, save them as a string (though API usually guarantees JSON)
                     responseContent = new JsonObject { ["function_call"] = new JsonObject { ["name"] = functionName, ["arguments"] = accumulatedFnArgs.ToString() } }.ToJsonString();
                     Console.Error.WriteLine($"{AppName}: Warning: Function call arguments were not valid JSON.");
                 }
             }
             else
             {
                 responseContent = accumulatedContent.ToString();
             }

        }
        else // Non-streaming
        {
            var responseJson = await CallApi(payload);
            responseRole = responseJson?["choices"]?[0]?["message"]?["role"]?.GetValue<string>() ?? "assistant";
            responseContent = responseJson?["choices"]?[0]?["message"]?["content"]?.GetValue<string>() ?? "";
            // Check for function call in non-streaming response
            var fnCall = responseJson?["choices"]?[0]?["message"]?["function_call"];
            if (fnCall != null)
            {
                 responseContent = fnCall.ToJsonString(); // Save the function call object stringified
// Output might need adjustment depending on desired non-streaming function call format
                 Console.WriteLine(fnCall.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            } else {
                 Console.WriteLine(responseContent);
            }

        }

        // Save conversation history for chat mode
        if (_chatMode && !_dryRun) // Don't save if dry run
        {
             // Save user prompt first
            UpdateConversation("user", _prompt);

            // Save assistant response (or function call)
             JsonNode responseNode;
             if (!string.IsNullOrEmpty(functionName) || (functionArgs != null)) // Check if it was a function call
             {
                 // Reconstruct the function call object for saving
                 var fnSaveObject = new JsonObject { ["function_call"] = new JsonObject { ["name"] = functionName } };
                 if(functionArgs != null) {
                     ((JsonObject)fnSaveObject["function_call"]!)["arguments"] = functionArgs.DeepClone();
                 } else {
                     // Handle case where args might not have been valid JSON during streaming
                     ((JsonObject)fnSaveObject["function_call"]!)["arguments"] = accumulatedFnArgs.ToString();
                 }
                 responseNode = fnSaveObject;
             }
             else if (!string.IsNullOrEmpty(responseContent) && responseContent.TrimStart().StartsWith('{'))
             {
                 // Handle non-streaming function call saved as stringified JSON
                 try { responseNode = JsonNode.Parse(responseContent)!; }
                 catch { responseNode = JsonValue.Create(responseContent)!; } // Fallback to string if parse fails
             }
             else
             {
                 responseNode = JsonValue.Create(responseContent)!;
             }
             UpdateConversation(responseRole, responseNode); // Use the determined role
        }

         // Save token count (approximation, as C# doesn't get it directly from stream easily)
         // The bash script also doesn't save tokens from stream response.
         // We could potentially parse the non-streaming response for tokens if needed.
         // if (!_dryRun && !streaming && responseJson != null && responseJson.TryGetProperty("usage", out var usage) && usage.TryGetProperty("total_tokens", out var tokens))
         // {
         //     SaveTokens(tokens.GetInt32());
         // }

    }


    private static async Task<JsonNode?> CallApi(JsonObject payload, HttpMethod? method = null)
    {
        if (method is null)
        {
            method = HttpMethod.Post;
        }

        // Handle dumped file input
        if (!string.IsNullOrEmpty(_dumpedFile))
        {
            try
            {
                var content = await File.ReadAllTextAsync(_dumpedFile);
                return JsonNode.Parse(content);
            }
            catch (Exception ex)
            {
                 RaiseError($"Error reading or parsing dumped file '{_dumpedFile}': {ex.Message}", 8);
                 return null; // Unreachable
            }
        }

        var url = $"{_openAIApiEndpoint}/{_apiName}";
        // System.Console.WriteLine(url);
        var authHeader = new AuthenticationHeaderValue("Bearer", _openAIApiKey);

        // Dry-run mode
        if (_dryRun)
        {
            Console.Error.WriteLine("Dry-run mode, no API calls made.");
            Console.Error.WriteLine($"\nRequest URL:\n--------------\n{url}");
            Console.Error.Write($"\nAuthorization:\n--------------\nBearer {_openAIApiKey.Substring(0, Math.Min(_openAIApiKey.Length, 3))}****\n"); // Mask key
            Console.Error.WriteLine("\nPayload:\n--------------");
            Console.Error.WriteLine(payload.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            Environment.Exit(0);
        }

        try
        {
            using var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = authHeader;

            if (method != HttpMethod.Get)
            {
                request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
            }

            using var response = await HttpClient.SendAsync(request);

            var responseBody = await response.Content.ReadAsStringAsync();

            // Dump response if requested
            if (!string.IsNullOrEmpty(_dumpFile))
            {
                await File.WriteAllTextAsync(_dumpFile, responseBody);
                 Console.Error.WriteLine($"Response dumped to '{_dumpFile}'.");
                Environment.Exit(0);
            }


            if (!response.IsSuccessStatusCode)
            {
                 // Try to parse error details from response
                string errorDetails = responseBody;
                try {
                    var errorJson = JsonNode.Parse(responseBody);
                    errorDetails = errorJson?["error"]?["message"]?.GetValue<string>() ?? responseBody;
                } catch {} // Ignore parsing errors, use raw body

                RaiseError($"API call failed: {(int)response.StatusCode} {response.ReasonPhrase}\nDetails: {errorDetails}", 9);
            }


            return JsonNode.Parse(responseBody);
        }
        catch (HttpRequestException ex)
        {
            RaiseError($"HTTP request failed: {ex.Message}", 9);
            return null; // Unreachable
        }
         catch (JsonException ex)
        {
            RaiseError($"Failed to parse API response: {ex.Message}", 9);
            return null; // Unreachable
        }
        catch (Exception ex) // Catch other potential errors
        {
             RaiseError($"An unexpected error occurred during API call: {ex.Message}", 9);
             return null; // Unreachable
        }
    }

     private static async Task<Stream?> CallApiStream(JsonObject payload)
    {
         // Dumped file doesn't make sense for streaming in this context
        if (!string.IsNullOrEmpty(_dumpedFile))
        {
             RaiseError("Cannot use dumped file with streaming API call.", 8);
             return null;
        }

        var url = $"{_openAIApiEndpoint}/{_apiName}";
        var authHeader = new AuthenticationHeaderValue("Bearer", _openAIApiKey);

        // Dry-run mode
        if (_dryRun)
        {
            Console.Error.WriteLine("Dry-run mode, no API calls made.");
            Console.Error.WriteLine($"\nRequest URL:\n--------------\n{url}");
             Console.Error.Write($"\nAuthorization:\n--------------\nBearer {_openAIApiKey.Substring(0, Math.Min(_openAIApiKey.Length, 3))}****\n"); // Mask key
            Console.Error.WriteLine("\nPayload:\n--------------");
            Console.Error.WriteLine(payload.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            Environment.Exit(0);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = authHeader;
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

            // Use HttpCompletionOption.ResponseHeadersRead for streaming
            var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

             // Dump response headers/info if requested? The bash script dumps the body.
             // Dumping a stream is tricky. We'll skip dumping for stream requests.
            if (!string.IsNullOrEmpty(_dumpFile))
            {
                 Console.Error.WriteLine($"{AppName}: Warning: Dumping response to file is not supported for streaming requests. Ignoring -o option.");
            }


            if (!response.IsSuccessStatusCode)
            {
                // Read the error body even for streams
                var errorBody = await response.Content.ReadAsStringAsync();
                 string errorDetails = errorBody;
                try {
                    var errorJson = JsonNode.Parse(errorBody);
                    errorDetails = errorJson?["error"]?["message"]?.GetValue<string>() ?? errorBody;
                } catch {} // Ignore parsing errors, use raw body
                RaiseError($"API call failed: {(int)response.StatusCode} {response.ReasonPhrase}\nDetails: {errorDetails}", 9);
                return null; // Unreachable
            }

            return await response.Content.ReadAsStreamAsync();
        }
        catch (HttpRequestException ex)
        {
            RaiseError($"HTTP request failed: {ex.Message}", 9);
            return null; // Unreachable
        }
        catch (Exception ex) // Catch other potential errors
        {
             RaiseError($"An unexpected error occurred during API call: {ex.Message}", 9);
             return null; // Unreachable
        }
    }


    private static void CreateTopic()
    {
        var initialPrompt = string.Join(" ", _restArgs);
        UpdateConversation("system", initialPrompt);
        RaiseError($"Topic '{_topic}' created with initial prompt '{initialPrompt}'", 0, noPrefix: true);
    }

    private static void Usage()
    {
        // Mimic the bash script's usage message
        var usageText = $@"OpenAI Client (C#) v{AppVersion}

SYNOPSIS
  ABSTRACT
    {AppName} [-n] [-a api_name] [-o dump_file] [INPUT...]
    {AppName} -i dumped_file

  DEFAULT_API ({DefaultApiName})
    {AppName} [-c] [+property=value...] [@TOPIC] [-f file | prompt ...]
    prompt
            Prompt string for the request to OpenAI API. This can consist of multiple
            arguments, which are considered to be separated by spaces.
    -f file
            A file to be read as prompt. If neither this parameter nor a prompt
            is specified, read from standard input.
    -c
            Continues the topic, the default topic is '{DefaultTopic}'.
    +property=value
            Overwrites default properties in payload. Prepend a plus sign '+'.
            eg: +model=gpt-3.5-turbo-0301 +stream=false

    TOPICS
            Topic starts with an at sign '@'.
            To create new topic, use `{AppName} @new_topic initial prompt`

  OTHER APIS
    {AppName} -a models
    {AppName} -a moderations [+property=value...] [-f file | prompt ...]
    {AppName} -a images/generations [+property=value...] [-f file | prompt ...]
    {AppName} -a embeddings [+property=value...] [-f file | prompt ...]


GLOBAL OPTIONS
  Global options apply to all APIs.
  -a name
        API name, default is '{DefaultApiName}'.
  -n
        Dry-run mode, don't call API.
  -o filename
        Dumps API response body to a file and exits (not supported for streams).
  -i filename
        Uses specified dumped file instead of requesting API.
        Any request-related arguments and user input are ignored.

  --
          Ignores rest of arguments, useful when unquoted prompt consists of '-'. (Handled implicitly by parser)

  -h
          Shows this help";

        RaiseError(usageText, 0, noPrefix: true);
    }

    private static void ParseArgs(string[] args)
    {
        var remainingArgs = new List<string>(args);
        _restArgs = new List<string>();

        for (int i = 0; i < remainingArgs.Count; i++)
        {
            string arg = remainingArgs[i];

            if (arg == "--") // Stop processing options
            {
                 _restArgs.AddRange(remainingArgs.Skip(i + 1));
                 break;
            }

            if (!arg.StartsWith('-') || arg.Length == 1) // Treat as positional arg (prompt part or topic)
            {
                 // Check if it's a topic first
                 if (arg.StartsWith('@'))
                 {
                     if (i == 0 || !remainingArgs[i-1].StartsWith('-')) // Ensure it's the first positional or follows non-option
                     {
                         _topic = arg.Substring(1);
                         // Don't add topic to _restArgs
                         continue; // Move to next argument
                     }
                 }
                 // Otherwise, it's part of the prompt/restArgs
                 _restArgs.Add(arg);
                 continue;
            }


            // Handle options
            switch (arg)
            {
                case "-c":
                    _chatMode = true;
                    break;
                case "-n":
                    _dryRun = true;
                    break;
                case "-h":
                case "--help": // Add common help flag
                    Usage();
                    break;
                case "-a":
                    if (i + 1 < remainingArgs.Count)
                    {
                        _apiName = remainingArgs[i + 1];
                        i++;
                    }
                    else RaiseError($"Option {arg} requires an argument.", 2);
                    break;
                case "-f":
                     if (i + 1 < remainingArgs.Count)
                    {
                        _promptFile = remainingArgs[i + 1];
                         if (_promptFile == "-") _promptFile = null; // Read from stdin if '-'
                        i++;
                    }
                    else RaiseError($"Option {arg} requires an argument.", 2);
                    break;
                case "-i":
                     if (i + 1 < remainingArgs.Count)
                    {
                        _dumpedFile = remainingArgs[i + 1];
                        i++;
                    }
                    else RaiseError($"Option {arg} requires an argument.", 2);
                    break;
                case "-o":
                     if (i + 1 < remainingArgs.Count)
                    {
                        _dumpFile = remainingArgs[i + 1];
                        i++;
                    }
                    else RaiseError($"Option {arg} requires an argument.", 2);
                    break;
                default:
                    _restArgs.Add(arg);
                    break;
            }
        }


        // Validation after parsing all args
        if (_chatMode && (_topic == DefaultTopic && !File.Exists(Path.Combine(_openAIDataDir, $"{_topic}.json")))) // Allow default topic if it exists
        {
            RaiseError("Topic is required for chatting (-c). Use @topic_name or create one first.", 2);
        }

         // If _restArgs contains items now, they are the prompt (unless -f was used)
         // The ReadPrompt function will handle combining _restArgs or reading from _promptFile/stdin
    }
}

// Helper classes for potential future structured JSON handling (optional)
// public class ConversationMessage { public string Role { get; set; } public string Content { get; set; } }
// public class ConversationData { public List<ConversationMessage> Messages { get; set; } public int TotalTokens { get; set; } }
