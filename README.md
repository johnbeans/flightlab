# flightlab

`flightlab` is a read-only [Model Context Protocol](https://modelcontextprotocol.io/) server for FlightLab users. It lets AI assistants and MCP clients, such as ChatGPT, Claude, Grok, etc., read and analyze your flights.

This is the public, user-scoped MCP server. It does not include admin tools.

## What It Can Do

- List your visible FlightLab flights.
- Fetch one visible flight by id.
- Fetch decoded, windowed altitude and acceleration samples for one flight.
- List FlightLab mode names, numeric indexes, and aliases.
- Resolve FlightLab flight and FlightSet links.
- Read FlightSet metadata and member summaries.
- Analyze a FlightSet with simple summary statistics.
- Compare flights in a FlightSet using metadata-level differences.

## Security Model

The server uses your normal FlightLab bearer token and calls the public FlightLab API. Flight ownership and access checks are enforced by FlightLab.

You do not enter your email address into the MCP server. Instead, sign in to FlightLab with the email address that owns or can access the flights, then give the MCP server the credentials from that signed-in session. Those credentials let your AI read and analyze your flights.

This project intentionally has:

- No admin API key support.
- No calls to `/api/admin/*`.
- No write tools.
- No direct database access.

Keep your FlightLab credentials private. Do not commit them to git, paste them into issues, or share them in screenshots.

## Requirements

- [.NET SDK 10.0](https://dotnet.microsoft.com/download) or newer.
- A FlightLab account, signed in with the email address that owns or can access the flights you want to analyze.
- An MCP client that can launch stdio servers, such as Cursor.

## Install

Clone the repository:

```bash
git clone https://github.com/johnbeans/flightlab.git
cd flightlab
dotnet restore
```

Run a quick build check:

```bash
dotnet build
```

## Connect FlightLab To Your AI

1. Sign in to [FlightLab](https://flightlab.jollylogic.com/app/) using the email address that owns or can access your flights.
2. Open Settings.
3. Find **Connect AI**.
4. Choose **Copy setup instructions** and paste them into ChatGPT, Claude, Grok, etc.

The copied setup includes private FlightLab credentials. They identify your FlightLab account and let your AI read and analyze your flights. The MCP server does not need a separate email setting.

The setup includes a refresh token so the MCP can usually recover when the short-lived access token expires. If your MCP client still reports that credentials expired, sign in to FlightLab again and copy fresh setup instructions from Settings.

## Configure Cursor

Add this MCP server to your Cursor MCP configuration:

```json
{
  "mcpServers": {
    "flightlab": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "/path/to/flightlab/FlightLab.UserMcp.csproj"
      ],
      "env": {
        "FLIGHTLAB_API_BASE_URL": "https://flights.jollylogic.com",
        "FLIGHTLAB_ACCESS_TOKEN": "paste-your-flightlab-access-token-here",
        "FLIGHTLAB_REFRESH_TOKEN": "paste-your-flightlab-refresh-token-here",
        "FLIGHTLAB_SUPABASE_URL": "https://your-project.supabase.co",
        "FLIGHTLAB_SUPABASE_ANON_KEY": "paste-flightlab-supabase-anon-key-here"
      }
    }
  }
}
```

Restart or reload your MCP client after changing the configuration.

## Environment Variables

`FLIGHTLAB_ACCESS_TOKEN` is required. It must be a normal user-scoped FlightLab/Supabase bearer token.

`FLIGHTLAB_REFRESH_TOKEN`, `FLIGHTLAB_SUPABASE_URL`, and `FLIGHTLAB_SUPABASE_ANON_KEY` are optional but recommended. When all three are present, the MCP will try to refresh the access token once after a `401` and retry the original request.

`FLIGHTLAB_API_BASE_URL` is optional and defaults to the production FlightLab API. For production use:

```bash
FLIGHTLAB_API_BASE_URL=https://flights.jollylogic.com
```

## Tools

### `flightlab_list_flights`

Lists flights visible to the authenticated user.

Arguments:

- `top`: maximum flights to return, capped at 200. Defaults to 50.
- `skip`: number of flights to skip. Defaults to 0.
- `mode`: optional numeric FlightLab mode filter.
- `modeName`: optional mode name or alias, such as `rocket`, `drone`, `falcon`, `avian`, or `water rocket`.
- `includeDeleted`: include soft-deleted flights. Defaults to false.

Returns:

- `items`: the visible flight rows.
- `paging`: `top`, `skip`, `returned`, `hasMore`, and `nextSkip`.

### `flightlab_get_flight`

Gets one visible flight by FlightLab flight id.

Arguments:

- `flightId`: FlightLab flight id.

### `flightlab_get_flight_samples`

Gets decoded sample-level data for one visible flight. This is the tool to use for questions about detailed flight behavior, such as apogee behavior, descent ringing, or acceleration/altitude disagreement.

Arguments:

- `flightId`: FlightLab flight id.
- `startMs`: optional start time in milliseconds relative to launch/time zero.
- `endMs`: optional end time in milliseconds relative to launch/time zero.
- `maxSamples`: optional maximum samples per series to return. Defaults to 1000 and is capped by the API.

Returns altitude and acceleration only:

- `altitude.samples[]`: `timeMs`, `sampleIndex`, `altitudeFt`.
- `acceleration.samples[]`: `timeMs`, `sampleIndex`, `xG`, `yG`, `zG`.
- Each series includes `sampleRateHz`, `totalSamples`, `windowSamples`, `returnedSamples`, `downsampled`, and trim metadata.

If `downsampled` is true, narrow the `startMs`/`endMs` window or lower-level analysis should request another page around the interesting time range.

### `flightlab_list_modes`

Lists mode indexes, names, aliases, and whether the mode normally has acceleration data.

Current modes:

- `0` Airplane: aliases `airplane`, `plane`, `aircraft`
- `1` Drone: aliases `drone`, `quad`, `quadcopter`, `multirotor`
- `2` Glider: aliases `glider`, `sailplane`
- `3` Helicopter: aliases `helicopter`, `heli`
- `4` Rocket: aliases `rocket`, `model rocket`, `rocketry`
- `5` Avian: aliases `avian`, `falcon`, `falconry`, `bird`, `raptor`
- `6` Kite: alias `kite`
- `7` Experimental: aliases `experimental`, `test`
- `8` Water Rocket: aliases `water rocket`, `waterrocket`

### `flightlab_resolve_reference`

Resolves a FlightLab flight or FlightSet link visible to the user.

Arguments:

- `url`: FlightLab link or FlightSet id.

### `flightlab_get_flight_set`

Gets metadata for a visible FlightSet.

Arguments:

- `setId`: FlightSet id.

### `flightlab_get_flight_set_members`

Gets visible member summaries for a FlightSet.

Arguments:

- `setId`: FlightSet id.

### `flightlab_analyze_flight_set`

Returns summary statistics for a FlightSet:

- `flightCount`
- `modeCounts`
- `apogee.min`, `apogee.max`, `apogee.average`
- `duration.min`, `duration.max`, `duration.average`

Use this for quick, deterministic FlightSet summaries. Use `flightlab_get_flight_samples` when the user asks for sample-level analysis of a specific flight.

Arguments:

- `setId`: FlightSet id.

### `flightlab_compare_flight_set`

Returns a metadata-level comparison of flights in a FlightSet, ordered by apogee. It includes flight id, name, mode, apogee, duration, and delete status. It does not inspect sample-level data.

Arguments:

- `setId`: FlightSet id.

## Example Prompts

After configuring the MCP server, try prompts like:

- "List my latest FlightLab flights."
- "List my falcon flights."
- "Analyze this FlightLab set: `https://flightlab.jollylogic.com/app/set.html?id=...`"
- "Compare the flights in FlightSet `...` by apogee and duration."
- "Get altitude and acceleration samples for flight `...` from 9000ms to 16000ms."
- "Get details for FlightLab flight `...`."

## Structured Results And Errors

Tool responses are JSON text with a stable shape.

Success:

```json
{
  "ok": true,
  "data": {}
}
```

Error:

```json
{
  "ok": false,
  "error": {
    "code": "token_expired",
    "status": 401,
    "message": "FlightLab access token is missing, invalid, or expired.",
    "action": "Ask the user to open FlightLab Settings > Connect AI and copy fresh setup instructions."
  }
}
```

Common error codes:

- `token_expired`: ask the user to copy fresh setup instructions from FlightLab Settings > Connect AI.
- `unauthorized`: the account cannot perform the request.
- `not_found_or_no_access`: the id/link is wrong, or this FlightLab account cannot access it.
- `requires_subscription`: ask the user to check their FlightLab subscription.
- `bad_request`: fix the tool arguments.
- `server_error`: retry later or contact FlightLab support.

## Troubleshooting

`FLIGHTLAB_ACCESS_TOKEN is required`

Set `FLIGHTLAB_ACCESS_TOKEN` in the MCP server environment. The easiest path is FlightLab Settings > Connect AI > Copy setup instructions.

`401`

Your token is missing, expired, or invalid. If refresh-token settings are configured, the MCP retries once automatically. If it still fails, sign in to FlightLab again and copy fresh setup instructions.

`404`

The requested flight or FlightSet does not exist or is not visible to the authenticated user.

`dotnet: command not found`

Install the .NET SDK and make sure `dotnet` is available on your PATH.
