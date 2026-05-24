# FlightLab.UserMcp

Public, user-scoped MCP server for reading FlightLab flights that the authenticated user can access.

This server intentionally has no admin API key support and does not call `/api/admin/*`.

## Status

Initial read-only implementation.

## Configuration

Set:

```bash
FLIGHTLAB_API_BASE_URL=https://flightdataapi-production.up.railway.app
FLIGHTLAB_ACCESS_TOKEN=your-user-flightlab-bearer-token
```

The token must be a normal FlightLab/Supabase user token. The FlightLab API enforces ownership and future sharing/group access.

## MCP Configuration

```json
{
  "mcpServers": {
    "flightlab": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "/path/to/FlightLab.UserMcp/FlightLab.UserMcp.csproj"
      ],
      "env": {
        "FLIGHTLAB_API_BASE_URL": "https://flightdataapi-production.up.railway.app",
        "FLIGHTLAB_ACCESS_TOKEN": "..."
      }
    }
  }
}
```

## Tools

- `flightlab_list_flights`
- `flightlab_get_flight`
- `flightlab_resolve_reference`
- `flightlab_get_flight_set`
- `flightlab_get_flight_set_members`
- `flightlab_analyze_flight_set`
- `flightlab_compare_flight_set`

## Security Boundary

This project should remain publishable and safe to review publicly:

- no admin endpoints
- no admin keys
- no write tools
- no direct database access
