using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace BuildingAdventure.Desktop;

/// <summary>
/// Optional model transport only. The model cannot change the document:
/// its JSON always passes through Core.AiLayoutImprover validation.
/// Never persist keys in project JSON or editor settings.
/// </summary>
internal static class AiLayoutModelClient
{
    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromMinutes(2)
    };

    private const string Instructions =
        "You are an architecture layout improvement assistant. " +
        "The application already computed and verified every allowed edit. " +
        "Choose exactly one existing candidate from the user's list. " +
        "Return a JSON object containing candidateId (integer) and reason " +
        "(short string). Do not output wall coordinates or invent a new ID. " +
        "No Markdown.";

    public static async Task<string> RequestAsync(
        string provider, string model, string prompt, CancellationToken token = default,
        IProgress<string>? progress = null)
    {
        if (string.IsNullOrWhiteSpace(model) || model.Length > 100 ||
            model.Any(char.IsControl))
            throw new ArgumentException("Enter a valid model name.");

        return provider switch
        {
            "Ollama" => await AskOllamaAsync(model, prompt, token, progress),
            "OpenAI" => await AskOpenAiAsync(model, prompt, token),
            _ => throw new ArgumentException("Select Ollama or OpenAI.")
        };
    }

    // Probe both load and actual token generation. A successful model load
    // alone does not guarantee the engine can answer a chat request.
    public static async Task<string> WarmOllamaAsync(
        string model, CancellationToken token, IProgress<string>? progress = null)
    {
        if (string.IsNullOrWhiteSpace(model) || model.Length > 100 ||
            model.Any(char.IsControl))
            throw new ArgumentException("Enter a valid Ollama model name.");
        progress?.Report("Testing Ollama generation; a cold model may load first");
        string answer = await AskOllamaAsync(model,
            "Test your JSON selection interface. There is exactly one candidate: " +
            "{\"candidateId\":1,\"kind\":\"Test\",\"summary\":\"Check local model\"}. " +
            "Select candidate 1 and explain with the word ready.",
            token, progress);
        try
        {
            using var document = JsonDocument.Parse(answer);
            if (!document.RootElement.TryGetProperty("candidateId", out var id) ||
                id.ValueKind != JsonValueKind.Number || id.GetInt32() != 1)
                throw new InvalidOperationException(
                    "Ollama replied, but did not return candidateId 1: " +
                    (answer.Length > 180 ? answer[..180] : answer));
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                "Ollama produced text but not valid JSON: " +
                (answer.Length > 180 ? answer[..180] : answer), ex);
        }
        return "Ollama loaded and completed a test generation successfully. " +
            "Model will remain loaded for up to one hour after requests, " +
            "subject to server memory pressure.";
    }

    private static async Task<string> AskOllamaAsync(
        string model, string prompt, CancellationToken token,
        IProgress<string>? progress)
    {
        // Long enough for normal sessions; not an indefinite VRAM reservation.
        // Streaming gives useful first-token status instead of waiting
        // silently for the whole non-streaming response.
        var payload = new
        {
            model, stream = true, format = "json", think = false,
            keep_alive = "1h",
            options = new { num_predict = 160, temperature = 0 },
            messages = new object[]
            {
                new { role = "system", content = Instructions },
                new { role = "user", content = prompt }
            }
        };
        // HttpClient.Timeout only covers headers when the response is
        // streamed; cap the entire NDJSON body read with a linked token.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        var requestToken = deadline.Token;
        progress?.Report("Sent to Ollama; waiting for response headers / model load");
        using var response = await PostAsync(
            "http://127.0.0.1:11434/api/chat", payload, null, requestToken,
            HttpCompletionOption.ResponseHeadersRead);
        progress?.Report("Ollama connected; waiting for the first generated token");
        using var stream = await response.Content.ReadAsStreamAsync(requestToken);
        using var reader = new StreamReader(stream);
        var content = new StringBuilder();
        bool completed = false;
        int events = 0;
        while (await reader.ReadLineAsync(requestToken) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            using var part = JsonDocument.Parse(line);
            var root = part.RootElement;
            if (root.TryGetProperty("error", out var error))
                throw new InvalidOperationException(
                    "Ollama returned an error: " + error.ToString());
            if (root.TryGetProperty("message", out var message) &&
                message.TryGetProperty("content", out var chunk) &&
                chunk.ValueKind == JsonValueKind.String)
            {
                content.Append(chunk.GetString());
                if (content.Length > 16384)
                    throw new InvalidOperationException(
                        "Ollama output exceeded the expected small JSON response.");
                events++;
                if (events == 1 || events % 15 == 0)
                    progress?.Report("Ollama generating output (" +
                        content.Length + " characters received)");
            }
            if (root.TryGetProperty("done", out var done) &&
                done.ValueKind == JsonValueKind.True)
            {
                completed = true;
                double loadSecs = DurationSeconds(root, "load_duration");
                double totalSecs = DurationSeconds(root, "total_duration");
                progress?.Report(
                    $"Ollama finished (model load {loadSecs:0.0}s, " +
                    $"total {totalSecs:0.0}s, {content.Length} characters)");
                break;
            }
        }
        if (!completed || content.Length == 0)
            throw new InvalidOperationException(
                "Ollama closed the stream before returning complete JSON. " +
                "Check the container logs and try Warm/test Ollama.");
        return content.ToString();
    }

    private static double DurationSeconds(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.Number
            ? value.GetDouble() / 1_000_000_000d : 0;

    private static async Task<string> AskOpenAiAsync(
        string model, string prompt, CancellationToken token)
    {
        var key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException(
                "Set the OPENAI_API_KEY environment variable before using OpenAI. " +
                "The key is never saved to a building or settings file.");

        static object Type(string type) => new { type };
        var fields = new Dictionary<string, object>
        {
            ["candidateId"] = Type("integer"),
            ["reason"] = Type("string")
        };
        var payload = new
        {
            model,
            instructions = Instructions,
            input = prompt,
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = "validated_layout_candidate",
                    strict = true,
                    schema = new
                    {
                        type = "object",
                        properties = fields,
                        required = fields.Keys.ToArray(),
                        additionalProperties = false
                    }
                }
            }
        };
        using var response = await PostAsync(
            "https://api.openai.com/v1/responses", payload, key, token);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(token));
        if (document.RootElement.TryGetProperty("output", out var output) &&
            output.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in output.EnumerateArray())
            {
                if (!item.TryGetProperty("content", out var parts) ||
                    parts.ValueKind != JsonValueKind.Array) continue;
                foreach (var part in parts.EnumerateArray())
                    if (part.TryGetProperty("type", out var type) &&
                        type.GetString() == "output_text" &&
                        part.TryGetProperty("text", out var text))
                        return text.GetString() ?? "";
            }
        }
        throw new InvalidOperationException(
            "OpenAI did not return an improvement. The model may have refused " +
            "or the response was incomplete.");
    }

    private static async Task<HttpResponseMessage> PostAsync(
        string url, object payload, string? bearer, CancellationToken token,
        HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload),
                Encoding.UTF8, "application/json")
        };
        if (bearer is not null)
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", bearer);
        var response = await Client.SendAsync(request, completion, token);
        if (!response.IsSuccessStatusCode)
        {
            string body = await response.Content.ReadAsStringAsync(token);
            response.Dispose();
            throw new InvalidOperationException(
                "AI provider returned HTTP " + (int)response.StatusCode + ": " +
                (body.Length > 500 ? body[..500] : body));
        }
        return response;
    }
}
