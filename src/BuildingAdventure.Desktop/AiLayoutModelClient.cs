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
        "Return precisely one JSON object with fields floorIndex, fromRoomId, " +
        "toRoomId, xMin, yMin, xMax, yMax and reason. " +
        "No Markdown. Never invent IDs, coordinates or edits outside " +
        "the permitted corridor-to-room rectangular transfer.";

    public static async Task<string> RequestAsync(
        string provider, string model, string prompt, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(model) || model.Length > 100 ||
            model.Any(char.IsControl))
            throw new ArgumentException("Enter a valid model name.");

        return provider switch
        {
            "Ollama" => await AskOllamaAsync(model, prompt, token),
            "OpenAI" => await AskOpenAiAsync(model, prompt, token),
            _ => throw new ArgumentException("Select Ollama or OpenAI.")
        };
    }

    private static async Task<string> AskOllamaAsync(
        string model, string prompt, CancellationToken token)
    {
        // The endpoint is fixed to loopback. No user-supplied arbitrary
        // destination and no external provider involved in this mode.
        var payload = new
        {
            model, stream = false, format = "json", think = false,
            messages = new object[]
            {
                new { role = "system", content = Instructions },
                new { role = "user", content = prompt }
            }
        };
        using var response = await PostAsync(
            "http://127.0.0.1:11434/api/chat", payload, null, token);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(token));
        if (document.RootElement.TryGetProperty("message", out var message) &&
            message.TryGetProperty("content", out var content) &&
            content.ValueKind == JsonValueKind.String)
            return content.GetString() ?? "";
        throw new InvalidOperationException(
            "Ollama returned no text. Ensure its model is installed and running.");
    }

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
            ["floorIndex"] = Type("integer"),
            ["fromRoomId"] = Type("string"),
            ["toRoomId"] = Type("string"),
            ["xMin"] = Type("number"),
            ["yMin"] = Type("number"),
            ["xMax"] = Type("number"),
            ["yMax"] = Type("number"),
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
                    name = "room_area_transfer",
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
        string url, object payload, string? bearer, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload),
                Encoding.UTF8, "application/json")
        };
        if (bearer is not null)
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", bearer);
        var response = await Client.SendAsync(request, token);
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
