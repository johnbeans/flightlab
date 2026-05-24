using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

var apiBase = Environment.GetEnvironmentVariable("FLIGHTLAB_API_BASE_URL")?.TrimEnd('/')
              ?? "https://flights.jollylogic.com";
var accessToken = Environment.GetEnvironmentVariable("FLIGHTLAB_ACCESS_TOKEN");
var refreshToken = Environment.GetEnvironmentVariable("FLIGHTLAB_REFRESH_TOKEN");
var supabaseUrl = Environment.GetEnvironmentVariable("FLIGHTLAB_SUPABASE_URL")?.TrimEnd('/');
var supabaseAnonKey = Environment.GetEnvironmentVariable("FLIGHTLAB_SUPABASE_ANON_KEY");

using var http = new HttpClient { BaseAddress = new Uri(apiBase) };
ApplyBearerToken();

var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
var prettyJsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

var modes = new[]
{
    new FlightModeInfo(0, "Airplane", new[] { "airplane", "plane", "aircraft" }, true),
    new FlightModeInfo(1, "Drone", new[] { "drone", "quad", "quadcopter", "multirotor" }, false),
    new FlightModeInfo(2, "Glider", new[] { "glider", "sailplane" }, true),
    new FlightModeInfo(3, "Helicopter", new[] { "helicopter", "heli" }, false),
    new FlightModeInfo(4, "Rocket", new[] { "rocket", "model rocket", "rocketry" }, true),
    new FlightModeInfo(5, "Avian", new[] { "avian", "falcon", "falconry", "bird", "raptor" }, false),
    new FlightModeInfo(6, "Kite", new[] { "kite" }, false),
    new FlightModeInfo(7, "Experimental", new[] { "experimental", "test" }, true),
    new FlightModeInfo(8, "Water Rocket", new[] { "water rocket", "waterrocket" }, true)
};

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
                serverInfo = new { name = "flightlab-user-mcp", version = "0.2.0" }
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
                mode = new { type = "integer", description = "Optional numeric FlightLab mode index." },
                modeName = new { type = "string", description = "Optional mode name or alias, such as rocket, drone, falcon, avian, or water rocket." },
                includeDeleted = new { type = "boolean" }
            }
        }),
        Tool("flightlab_get_flight", "Get one visible flight by FlightLab flight id.", new
        {
            type = "object",
            properties = new { flightId = new { type = "string" } },
            required = new[] { "flightId" }
        }),
        Tool("flightlab_get_flight_samples", "Get decoded altitude and acceleration samples for one visible flight, with optional time windowing and downsampling.", new
        {
            type = "object",
            properties = new
            {
                flightId = new { type = "string" },
                startMs = new { type = "integer", description = "Optional start time in milliseconds relative to launch/time zero." },
                endMs = new { type = "integer", description = "Optional end time in milliseconds relative to launch/time zero." },
                maxSamples = new { type = "integer", description = "Maximum samples per series to return. Defaults to 1000, capped by the API." }
            },
            required = new[] { "flightId" }
        }),
        Tool("flightlab_list_modes", "List FlightLab flight modes, numeric indexes, and aliases.", new
        {
            type = "object",
            properties = new { }
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
        Tool("flightlab_analyze_flight_set", "Analyze visible FlightSet member summaries: flight count, mode counts, apogee min/max/average, and duration min/max/average.", new
        {
            type = "object",
            properties = new { setId = new { type = "string" } },
            required = new[] { "setId" }
        }),
        Tool("flightlab_compare_flight_set", "Compare visible flights in a FlightSet using metadata-level differences ordered by apogee.", new
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
    try
    {
        var name = parameters?["name"]?.GetValue<string>() ?? throw new FlightLabApiException(400, "bad_request", "Tool name is required.", "Call a named FlightLab MCP tool.");
        var args = parameters["arguments"] as JsonObject ?? new JsonObject();
        if (string.IsNullOrWhiteSpace(accessToken) && name != "flightlab_list_modes")
            throw new FlightLabApiException(401, "token_expired", "FLIGHTLAB_ACCESS_TOKEN is required.", "Ask the user to open FlightLab Settings > Connect AI and copy fresh setup instructions.");

        object result = name switch
        {
            "flightlab_list_flights" => await ListFlightsAsync(args),
            "flightlab_get_flight" => await GetAsync($"/tables/flight/{Uri.EscapeDataString(GetString(args, "flightId"))}"),
            "flightlab_get_flight_samples" => await GetFlightSamplesAsync(args),
            "flightlab_list_modes" => ListModes(),
            "flightlab_resolve_reference" => await ResolveReferenceAsync(GetString(args, "url")),
            "flightlab_get_flight_set" => await GetAsync($"/api/flightsets/{Uri.EscapeDataString(GetString(args, "setId"))}"),
            "flightlab_get_flight_set_members" => await GetSetMembersAsync(GetString(args, "setId")),
            "flightlab_analyze_flight_set" => Analyze(await GetSetMembersAsync(GetString(args, "setId"))),
            "flightlab_compare_flight_set" => Compare(await GetSetMembersAsync(GetString(args, "setId"))),
            _ => throw new FlightLabApiException(400, "bad_request", $"Unknown tool '{name}'.", "Call flightlab_list_modes or inspect tools/list for available FlightLab tools.")
        };

        return ToolContent(new { ok = true, data = result });
    }
    catch (FlightLabApiException ex)
    {
        return ToolContent(new
        {
            ok = false,
            error = new { ex.Code, ex.Status, ex.Message, ex.Action }
        });
    }
    catch (Exception ex)
    {
        return ToolContent(new
        {
            ok = false,
            error = new { code = "server_error", status = 500, message = ex.Message, action = "Try again. If the problem persists, contact FlightLab support." }
        });
    }
}

JsonNode ToolContent(object payload) => JsonSerializer.SerializeToNode(new
{
    content = new[] { new { type = "text", text = JsonSerializer.Serialize(payload, prettyJsonOptions) } }
}, jsonOptions)!;

async Task<object> ListFlightsAsync(JsonObject args)
{
    var top = Math.Clamp(GetInt(args, "top", 50), 1, 200);
    var skip = Math.Max(0, GetInt(args, "skip", 0));
    var includeDeleted = GetBool(args, "includeDeleted");
    var mode = ResolveMode(args);
    var filters = new List<string>();
    if (mode.HasValue) filters.Add("mode eq " + mode.Value);
    if (!includeDeleted)
    {
        filters.Add("Deleted eq false");
        filters.Add("HardDeleted eq false");
    }

    var requestTop = top + 1;
    var parts = new List<string> { "$top=" + requestTop, "$skip=" + skip, "$orderby=timeStamp%20desc" };
    if (filters.Count > 0) parts.Add("$filter=" + Uri.EscapeDataString(string.Join(" and ", filters)));
    var response = await GetAsync("/tables/flight?" + string.Join("&", parts));
    var rawItems = AsArray(response);
    var hasMore = rawItems.Count > top;
    var items = new JsonArray();
    foreach (var item in rawItems.Take(top))
        items.Add(item?.DeepClone());

    return new
    {
        items,
        paging = new
        {
            top,
            skip,
            returned = items.Count,
            hasMore,
            nextSkip = hasMore ? skip + items.Count : (int?)null
        }
    };
}

async Task<JsonNode> GetFlightSamplesAsync(JsonObject args)
{
    var flightId = GetString(args, "flightId");
    var query = new List<string>();
    AddOptionalIntQuery(query, args, "startMs");
    AddOptionalIntQuery(query, args, "endMs");
    AddOptionalIntQuery(query, args, "maxSamples");
    var suffix = query.Count == 0 ? "" : "?" + string.Join("&", query);
    return await GetAsync($"/tables/flight/{Uri.EscapeDataString(flightId)}/samples{suffix}");
}

object ListModes() => new
{
    modes = modes.Select(m => new
    {
        index = m.Index,
        name = m.Name,
        aliases = m.Aliases,
        hasAcceleration = m.HasAcceleration
    }).ToArray()
};

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
    using var response = await SendGetAsync(path, allowRefresh: true);
    var body = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode) throw CreateApiException(response.StatusCode, body);
    return string.IsNullOrWhiteSpace(body) ? new JsonObject() : JsonNode.Parse(body) ?? new JsonObject();
}

async Task<HttpResponseMessage> SendGetAsync(string path, bool allowRefresh)
{
    ApplyBearerToken();
    var response = await http.GetAsync(path);
    if (response.StatusCode == HttpStatusCode.Unauthorized && allowRefresh && await TryRefreshAccessTokenAsync())
    {
        response.Dispose();
        ApplyBearerToken();
        return await SendGetAsync(path, allowRefresh: false);
    }

    return response;
}

async Task<bool> TryRefreshAccessTokenAsync()
{
    if (string.IsNullOrWhiteSpace(refreshToken) || string.IsNullOrWhiteSpace(supabaseUrl) || string.IsNullOrWhiteSpace(supabaseAnonKey))
        return false;

    try
    {
        using var refreshClient = new HttpClient { BaseAddress = new Uri(supabaseUrl) };
        refreshClient.DefaultRequestHeaders.Add("apikey", supabaseAnonKey);
        refreshClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", supabaseAnonKey);
        using var response = await refreshClient.PostAsJsonAsync("/auth/v1/token?grant_type=refresh_token", new { refresh_token = refreshToken }, jsonOptions);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            return false;

        var node = JsonNode.Parse(body);
        var newAccessToken = node?["access_token"]?.GetValue<string>();
        var newRefreshToken = node?["refresh_token"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(newAccessToken))
            return false;

        accessToken = newAccessToken;
        if (!string.IsNullOrWhiteSpace(newRefreshToken))
            refreshToken = newRefreshToken;
        return true;
    }
    catch
    {
        return false;
    }
}

void ApplyBearerToken()
{
    http.DefaultRequestHeaders.Authorization = string.IsNullOrWhiteSpace(accessToken)
        ? null
        : new AuthenticationHeaderValue("Bearer", accessToken);
}

FlightLabApiException CreateApiException(HttpStatusCode statusCode, string body)
{
    var status = (int)statusCode;
    var message = ExtractErrorMessage(body);
    return statusCode switch
    {
        HttpStatusCode.Unauthorized => new FlightLabApiException(status, "token_expired", string.IsNullOrWhiteSpace(message) ? "FlightLab access token is missing, invalid, or expired." : message, "Ask the user to open FlightLab Settings > Connect AI and copy fresh setup instructions."),
        HttpStatusCode.Forbidden when message.Contains("RequiresSubscription", StringComparison.OrdinalIgnoreCase) => new FlightLabApiException(status, "requires_subscription", "This FlightLab account needs an active subscription to access flight data.", "Ask the user to check their FlightLab subscription."),
        HttpStatusCode.Forbidden => new FlightLabApiException(status, "unauthorized", string.IsNullOrWhiteSpace(message) ? "This FlightLab account is not authorized for that request." : message, "Ask the user to sign in with the FlightLab email that owns or can access the flights."),
        HttpStatusCode.NotFound => new FlightLabApiException(status, "not_found_or_no_access", string.IsNullOrWhiteSpace(message) ? "The flight or FlightSet was not found, or this account cannot access it." : message, "Check the id/link and make sure the user signed in with the FlightLab account that owns or can access it."),
        HttpStatusCode.BadRequest => new FlightLabApiException(status, "bad_request", string.IsNullOrWhiteSpace(message) ? "FlightLab rejected the request." : message, "Check the tool arguments and try again."),
        _ => new FlightLabApiException(status, "server_error", string.IsNullOrWhiteSpace(message) ? $"FlightLab returned HTTP {status}." : message, "Try again. If the problem persists, contact FlightLab support.")
    };
}

string ExtractErrorMessage(string body)
{
    if (string.IsNullOrWhiteSpace(body))
        return "";
    try
    {
        var node = JsonNode.Parse(body);
        return node?["error"]?.GetValue<string>()
            ?? node?["message"]?.GetValue<string>()
            ?? body;
    }
    catch
    {
        return body;
    }
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

int? ResolveMode(JsonObject args)
{
    if (args["mode"] is not null)
        return args["mode"]?.GetValue<int>();

    var modeName = args["modeName"]?.GetValue<string>();
    if (string.IsNullOrWhiteSpace(modeName))
        return null;

    var normalized = NormalizeModeName(modeName);
    var match = modes.FirstOrDefault(m => NormalizeModeName(m.Name) == normalized || m.Aliases.Any(a => NormalizeModeName(a) == normalized));
    if (match is null)
        throw new FlightLabApiException(400, "bad_request", $"Unknown FlightLab mode '{modeName}'.", "Call flightlab_list_modes to see supported mode names and aliases.");
    return match.Index;
}

string NormalizeModeName(string value) =>
    new string(value.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

void AddOptionalIntQuery(List<string> query, JsonObject args, string name)
{
    if (args[name] is null)
        return;
    query.Add(Uri.EscapeDataString(name) + "=" + Uri.EscapeDataString(args[name]!.GetValue<int>().ToString()));
}

string GetString(JsonObject args, string name) =>
    args[name]?.GetValue<string>() ?? throw new FlightLabApiException(400, "bad_request", $"{name} is required.", $"Provide the required '{name}' argument.");

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

sealed record FlightModeInfo(int Index, string Name, string[] Aliases, bool HasAcceleration);

sealed class FlightLabApiException(int status, string code, string message, string action) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public string Action { get; } = action;
}
