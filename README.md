# flightlab

`flightlab` is a read-only [Model Context Protocol](https://modelcontextprotocol.io/) server for FlightLab users. It lets AI assistants and MCP clients, such as ChatGPT, Claude, Grok, etc., read and analyze your flights.

This is the public, user-scoped MCP server. It does not include admin tools.

## What It Can Do

- List your visible FlightLab flights.
- Fetch one visible flight by id.
- Resolve FlightLab flight and FlightSet links.
- Read FlightSet metadata and member summaries.
- Analyze a FlightSet with simple summary statistics.
- Compare flights in a FlightSet using metadata-level differences.

## Security Model

The server uses your normal FlightLab bearer token and calls the public FlightLab API. Flight ownership and access checks are enforced by FlightLab.

You do not enter your email address into the MCP server. Instead, sign in to FlightLab with the email address that owns or can access the flights, then give the MCP server the access token from that signed-in session. That token lets your AI read and analyze your flights.

This project intentionally has:

- No admin API key support.
- No calls to `/api/admin/*`.
- No write tools.
- No direct database access.

Keep your bearer token private. Do not commit it to git, paste it into issues, or share it in screenshots.

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

The copied setup includes a private FlightLab access token. That token identifies your FlightLab account and lets your AI read and analyze your flights. The MCP server does not need a separate email setting.

Tokens expire. If your MCP client starts getting `401` responses, sign in to FlightLab again and copy fresh setup instructions from Settings.

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
        "FLIGHTLAB_ACCESS_TOKEN": "paste-your-flightlab-access-token-here"
      }
    }
  }
}
```

Restart or reload your MCP client after changing the configuration.

## Environment Variables

`FLIGHTLAB_ACCESS_TOKEN` is required. It must be a normal user-scoped FlightLab/Supabase bearer token.

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
- `includeDeleted`: include soft-deleted flights. Defaults to false.

### `flightlab_get_flight`

Gets one visible flight by FlightLab flight id.

Arguments:

- `flightId`: FlightLab flight id.

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

Returns summary statistics for a FlightSet, including flight count, mode counts, apogee range, and duration range.

Arguments:

- `setId`: FlightSet id.

### `flightlab_compare_flight_set`

Returns a metadata-level comparison of flights in a FlightSet, ordered by apogee.

Arguments:

- `setId`: FlightSet id.

## Example Prompts

After configuring the MCP server, try prompts like:

- "List my latest FlightLab flights."
- "Analyze this FlightLab set: `https://flightlab.jollylogic.com/app/set.html?id=...`"
- "Compare the flights in FlightSet `...` by apogee and duration."
- "Get details for FlightLab flight `...`."

## Troubleshooting

`FLIGHTLAB_ACCESS_TOKEN is required`

Set `FLIGHTLAB_ACCESS_TOKEN` in the MCP server environment.

`401`

Your token is missing, expired, or invalid. Sign in to FlightLab again and copy a fresh token.

`404`

The requested flight or FlightSet does not exist or is not visible to the authenticated user.

`dotnet: command not found`

Install the .NET SDK and make sure `dotnet` is available on your PATH.
