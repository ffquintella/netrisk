#!/usr/bin/env bash
# T360 (S55): observe a REAL MCP client (official MCP Python SDK) against the agent co-work server through the real
# netrisk-mcp-bridge. Dev-only; nothing here is packaged. Needs uv with the `mcp` package already in its cache (the run
# is --offline). Usage: scripts/agents/observe-mcp.sh [transcript.md]
set -euo pipefail
cd "$(dirname "$0")/../.."
out="${1:-$(mktemp -d)/observed-run.md}"
dotnet build src/McpBridge/McpBridge.csproj -nologo -v q
dotnet build src/GUIClient.Tests/GUIClient.Tests.csproj -nologo -v q
NETRISK_OBSERVE_MCP=1 NETRISK_OBSERVE_OUT="$out" \
  src/GUIClient.Tests/bin/Debug/net11.0/GUIClient.Tests -class "GUIClient.Tests.Agents.AgentObservedRunTest"
echo "Transcript: $out"
