using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

var apiBase = Environment.GetEnvironmentVariable("FLIGHTLAB_API_BASE_URL")?.TrimEnd('/')
              ?? "https://flightdataapi-production.up.railway.app";
var accessToken = Environment.GetEnvironmentVariable("FLIGHTLAB_ACCESS_TOKEN");

using var http = new HttpClient { BaseAddress = new Uri(apiBase) };
if (!string.IsNullOrWhiteSpace(accessToken))
    http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

while (await Console.In.ReadLineAsync() is { } line)
{
    if (string.IsNullOrWhiteSpace(line)) continue;
    JsonNode? request = null;
    try
    {
        request = JsonNode.Parse(line);
        var id = request?["id"]?.DeepClone();
        var method = request?["method"]?.GetValue<string>() ?? "";
        var result = method switch
        {
            "initialize" => JsonSerializer.SerializeToNode(new
            {
                protocolVersion = "2024-11-05",
                capabilities = new { tools = new { } },
                serverInfo = new { name = "flightlab-user-mcp", version = "0.1.0" }
            }, jsonOptions),
            "tools/list" => ToolsList(),
            "tools/call" => await CallToolAsync(request?["params"] as JsonObject),
            _ => throw new InvalidOperationException($"Unsupported MCP method '{method}'.")
        };
        WriteResponse(id, result);
    }
    catch (Exception ex)
    {
        WriteError(request?["id"]?.DeepClone(), -32000, ex.Message);
    }
}

JsonNode ToolsList() => JsonSerializer.SerializeToNode(new
{
    tools = new object[]
    {
        Tool("flightlab_list_flights", "List flights visible to the authenticated FlightLab user.", new
        {
            type = "object",
            properties = new
            {
                top = new { type = "integer", description = "Maximum flights to return, up to 200." },
                skip = new { type = "integer" },
                mode = new { type = "integer" },
                includeDeleted = new { type = "boolean" }
            }
        }),
        Tool("flightlab_get_flight", "Get one visible flight by FlightLab flight id.", new
        {
            type = "object",
            properties = new { flightId = new { type = "string" } },
            required = new[] { "flightId" }
        }),
        Tool("flightlab_resolve_reference", "Resolve a FlightLab flight or set link visible to the user.", new
        {
            type = "object",
            properties = new { url = new { type = "string" } },
            required = new[] { "url" }
        }),
        Tool("flightlab_get_flight_set", "Get a visible FlightSet's metadata.", new
        {
            type = "object",
            properties = new { setId = new { type = "string" } },
            required = new[] { "setId" }
        }),
        Tool("flightlab_get_flight_set_members", "Get visible member summaries for a FlightSet.", new
        {
            type = "object",
            properties = new { setId = new { type = "string" } },
            required = new[] { "setId" }
        }),
        Tool("flightlab_analyze_flight_set", "Analyze visible FlightSet member summaries.", new
        {
            type = "object",
            properties = new { setId = new { type = "string" } },
            required = new[] { "setId" }
        }),
        Tool("flightlab_compare_flight_set", "Compare visible flights in a FlightSet using metadata-level differences.", new
        {
            type = "object",
            properties = new { setId = new { type = "string" } },
            required = new[] { "setId" }
        })
    }
}, jsonOptions)!;

object Tool(string name, string description, object inputSchema) => new { name, description, inputSchema };

async Task<JsonNode> CallToolAsync(JsonObject? parameters)
{
    if (string.IsNullOrWhiteSpace(accessToken))
        throw new InvalidOperationException("FLIGHTLAB_ACCESS_TOKEN is required. Sign in to FlightLab and provide a user-scoped bearer token.");

    var name = parameters?["name"]?.GetValue<string>() ?? throw new InvalidOperationException("Tool name is required.");
    var args = parameters["arguments"] as JsonObject ?? new JsonObject();
    object result = name switch
    {
        "flightlab_list_flights" => await ListFlightsAsync(args),
        "flightlab_get_flight" => await GetAsync($"/tables/flight/{Uri.EscapeDataString(GetString(args, "flightId"))}"),
        "flightlab_resolve_reference" => await ResolveReferenceAsync(GetString(args, "url")),
        "flightlab_get_flight_set" => await GetAsync($"/api/flightsets/{Uri.EscapeDataString(GetString(args, "setId"))}"),
        "flightlab_get_flight_set_members" => await GetSetMembersAsync(GetString(args, "setId")),
        "flightlab_analyze_flight_set" => Analyze(await GetSetMembersAsync(GetString(args, "setId"))),
        "flightlab_compare_flight_set" => Compare(await GetSetMembersAsync(GetString(args, "setId"))),
        _ => throw new InvalidOperationException($"Unknown tool '{name}'.")
    };

    return JsonSerializer.SerializeToNode(new
    {
        content = new[] { new { type = "text", text = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }) } }
    }, jsonOptions)!;
}

async Task<JsonNode> ListFlightsAsync(JsonObject args)
{
    var top = Math.Clamp(GetInt(args, "top", 50), 1, 200);
    var skip = Math.Max(0, GetInt(args, "skip", 0));
    var includeDeleted = GetBool(args, "includeDeleted");
    var mode = args["mode"]?.GetValue<int?>();
    var filters = new List<string>();
    if (mode.HasValue) filters.Add("mode eq " + mode.Value);
    if (!includeDeleted)
    {
        filters.Add("Deleted eq false");
        filters.Add("HardDeleted eq false");
    }

    var parts = new List<string> { "$top=" + top, "$skip=" + skip, "$orderby=timeStamp%20desc" };
    if (filters.Count > 0) parts.Add("$filter=" + Uri.EscapeDataString(string.Join(" and ", filters)));
    return await GetAsync("/tables/flight?" + string.Join("&", parts));
}

async Task<JsonNode> ResolveReferenceAsync(string url)
{
    var reference = ParseReference(url);
    if (reference.Kind == "flightSetId")
    {
        var set = await GetAsync($"/api/flightsets/{Uri.EscapeDataString(reference.Id)}");
        var members = await GetSetMembersAsync(reference.Id);
        return JsonSerializer.SerializeToNode(new { type = "flightSet", set, members }, jsonOptions)!;
    }

    var flight = await GetAsync($"/tables/flight/{Uri.EscapeDataString(reference.Id)}");
    return JsonSerializer.SerializeToNode(new { type = "flight", flight }, jsonOptions)!;
}

async Task<JsonNode> GetSetMembersAsync(string setId) =>
    await GetAsync($"/api/flightsets/{Uri.EscapeDataString(setId)}/flights");

async Task<JsonNode> GetAsync(string path)
{
    using var r = await http.GetAsync(path);
    var body = await r.Content.ReadAsStringAsync();
    if (!r.IsSuccessStatusCode) throw new InvalidOperationException($"{(int)r.StatusCode}: {body}");
    return JsonNode.Parse(body) ?? new JsonObject();
}

object Analyze(JsonNode membersNode)
{
    var members = AsArray(membersNode);
    var apogees = members.Select(m => m?["apogee"]?.GetValue<int>() ?? 0).ToList();
    var durations = members.Select(m => m?["duration"]?.GetValue<uint>() ?? 0).ToList();
    return new
    {
        flightCount = members.Count,
        modeCounts = members.GroupBy(m => m?["modeName"]?.GetValue<string>() ?? "Unknown").ToDictionary(g => g.Key, g => g.Count()),
        apogee = new { min = apogees.DefaultIfEmpty().Min(), max = apogees.DefaultIfEmpty().Max(), average = apogees.Count == 0 ? 0 : apogees.Average() },
        duration = new { min = durations.DefaultIfEmpty().Min(), max = durations.DefaultIfEmpty().Max(), average = durations.Count == 0 ? 0 : durations.Average(x => (double)x) }
    };
}

object Compare(JsonNode membersNode)
{
    var members = AsArray(membersNode);
    return new
    {
        flights = members.Select(m => new
        {
            id = m?["flightId"]?.GetValue<string>(),
            name = m?["flightName"]?.GetValue<string>(),
            mode = m?["modeName"]?.GetValue<string>(),
            apogee = m?["apogee"]?.GetValue<int>() ?? 0,
            duration = m?["duration"]?.GetValue<uint>() ?? 0,
            deleted = m?["deleted"]?.GetValue<bool>() ?? false
        }).OrderByDescending(x => x.apogee).ToList()
    };
}

JsonArray AsArray(JsonNode node)
{
    if (node is JsonArray arr) return arr;
    if (node["items"] is JsonArray items) return items;
    if (node["value"] is JsonArray value) return value;
    return new JsonArray();
}

(string Kind, string Id) ParseReference(string url)
{
    if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
    {
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var id = query["id"] ?? query["setId"] ?? query["flightId"];
        if (uri.AbsolutePath.Contains("/set", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(id))
            return ("flightSetId", id);
        if (uri.AbsolutePath.Contains("/flight", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(id))
            return ("flightId", id);
    }

    return ("flightSetId", url.Trim());
}

string GetString(JsonObject args, string name) =>
    args[name]?.GetValue<string>() ?? throw new InvalidOperationException($"{name} is required.");

int GetInt(JsonObject args, string name, int defaultValue) =>
    args[name]?.GetValue<int>() ?? defaultValue;

bool GetBool(JsonObject args, string name) =>
    args[name]?.GetValue<bool>() ?? false;

void WriteResponse(JsonNode? id, JsonNode? result)
{
    Console.WriteLine(JsonSerializer.Serialize(new JsonObject
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["result"] = result
    }));
    Console.Out.Flush();
}

void WriteError(JsonNode? id, int code, string message)
{
    Console.WriteLine(JsonSerializer.Serialize(new JsonObject
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["error"] = new JsonObject { ["code"] = code, ["message"] = message }
    }));
    Console.Out.Flush();
}
