"""T360 (S55): a REAL MCP client against the NetRisk agent co-work server, through the real stdio bridge.

This is the official MCP Python SDK (package `mcp`) driving `netrisk-mcp-bridge` over stdio, exactly as Claude Code or
any stdio MCP client would. It is not shipped and not part of the .NET solution: `GUIClient.Tests`
`AgentObservedRunTest` hosts the real loopback server and runtime, launches this script with
`uv run --offline --with mcp==<version>`, and answers its control lines.

Scenario: initialize -> tools/list -> register_agent -> (stand-in approval) -> key -> request_access 1 block read ->
get_status -> tools/list -> read calls -> an edit call refused under the read lease -> lease expiry (the host advances its
clock) -> a call refused after expiry.

Control protocol with the host, on this process's own stdout/stdin (the MCP traffic uses the bridge's pipes):
  "@@CONTROL CLOCK <minutes>"  the host advances its test clock and answers "@@OK"
  "@@CONTROL DONE"             the scenario finished; the transcript is written

Usage: observe_mcp_client.py --bridge <netrisk-mcp-bridge> --discovery <mcp-discovery.json> --transcript <file>
"""

import argparse
import asyncio
import importlib.metadata
import json
import os
import sys
from datetime import datetime, timezone

from mcp import ClientSession, StdioServerParameters, types
from mcp.client.stdio import stdio_client

SECRET_KEYS = {"agent_key"}


def redact(value):
    """Never write an agent key to the transcript: only its length."""
    if isinstance(value, dict):
        return {k: (f"<redacted: {len(v)} chars>" if k in SECRET_KEYS and isinstance(v, str) else redact(v))
                for k, v in value.items()}
    if isinstance(value, list):
        return [redact(v) for v in value]
    if isinstance(value, str) and len(value) > 400:
        return value[:400] + f"... [{len(value) - 400} more characters cut by the transcript]"
    return value


class Transcript:
    def __init__(self):
        self.lines = []
        self.step = 0
        self.raw = []  # every tool result as the client received it: text content and structured content

    def call(self, what, arguments=None):
        self.step += 1
        args = "" if arguments is None else " " + json.dumps(redact(arguments), ensure_ascii=False)
        self.lines.append(f"{self.step:>2}. -> {what}{args}")

    def answer(self, text):
        self.lines.append(f"    <- {text}")

    def note(self, text):
        self.lines.append(f"    ({text})")


def control(line):
    print("@@CONTROL " + line, flush=True)
    if line.startswith("CLOCK"):
        reply = sys.stdin.readline().strip()
        if reply != "@@OK":
            raise RuntimeError("the host did not acknowledge the clock: " + repr(reply))


def record(t, name, result: types.CallToolResult):
    texts = [c.text for c in result.content if getattr(c, "type", None) == "text"]
    t.raw.append({"tool": name, "is_error": result.is_error, "text": texts,
                  "structured": result.structured_content})
    return result


def summary(result: types.CallToolResult):
    structured = result.structured_content or {}
    shown = {k: v for k, v in structured.items() if k not in ("notice", "data")}
    data = structured.get("data")
    if isinstance(data, dict):
        if "items" in data:
            shown["data.items"] = [{k: i.get(k) for k in ("id", "subject", "status") if k in i} for i in data["items"]]
            shown["data.total"] = data.get("total")
        else:
            shown["data"] = {k: data[k] for k in list(data)[:6]}
    return f"is_error={result.is_error} " + json.dumps(redact(shown), ensure_ascii=False)


async def run(bridge, discovery, transcript_path):
    t = Transcript()
    sdk = importlib.metadata.version("mcp")
    client_info = types.Implementation(name="netrisk-t360-observer (official MCP Python SDK)", version=sdk)
    started = datetime.now(timezone.utc)

    # The SDK starts the server with a minimal environment (PATH, HOME…). netrisk-mcp-bridge is framework-dependent, so a
    # .NET outside the default location must be named to it — as a real client's config must (see the S55 checklist).
    env = {k: os.environ[k] for k in ("DOTNET_ROOT", "DOTNET_ROOT_ARM64", "DOTNET_ROOT_X64") if k in os.environ}
    server = StdioServerParameters(command=bridge, args=["--discovery", discovery], env=env or None)
    async with stdio_client(server) as (read, write):
        async with ClientSession(read, write, client_info=client_info) as session:
            t.call("initialize (through netrisk-mcp-bridge over stdio)")
            init = await session.initialize()
            t.answer(f"protocol={init.protocol_version} server={init.server_info.name} {init.server_info.version}")

            t.call("tools/list (no key yet)")
            tools = await session.list_tools()
            t.answer(f"{len(tools.tools)} tools: " + ", ".join(sorted(x.name for x in tools.tools)))

            args = {"name": "T360 observer", "description": "Observed run of the agent co-work server (S55 T360)"}
            t.call("tools/call register_agent", args)
            first = record(t, "register_agent", await session.call_tool("register_agent", args))
            t.answer(summary(first))
            pending = first.structured_content

            args = {"pending_id": pending["pending_id"]}
            t.call("tools/call register_agent", args)
            t.note("the host's stand-in for the approval dialog approves")
            approved = record(t, "register_agent", await session.call_tool("register_agent", args))
            t.answer(summary(approved))
            key = approved.structured_content["agent_key"]

            args = {"agent_key": key, "blocks": 1, "mode": "read", "reason": "T360: observe a read lease"}
            t.call("tools/call request_access", args)
            t.note("the host's stand-in for the access dialog grants 1 block, read")
            granted = record(t, "request_access", await session.call_tool("request_access", args))
            t.answer(summary(granted))

            args = {"agent_key": key}
            t.call("tools/call get_status", args)
            t.answer(summary(record(t, "get_status", await session.call_tool("get_status", args))))

            t.call("tools/list (this MCP session now belongs to the agent; read lease)")
            tools = await session.list_tools()
            names = sorted(x.name for x in tools.tools)
            t.answer(f"{len(names)} tools; edit tools listed: "
                     + str([n for n in names if n.startswith(("update_", "add_", "answer_", "comment_"))]))

            args = {"agent_key": key}
            t.call("tools/call search_risks", args)
            t.answer(summary(record(t, "search_risks", await session.call_tool("search_risks", args))))

            args = {"agent_key": key, "risk_id": 1}
            t.call("tools/call get_risk", args)
            risk = record(t, "get_risk", await session.call_tool("get_risk", args))
            t.answer(summary(risk))
            version = risk.structured_content["data"]["version"]

            args = {"agent_key": key, "risk_id": 2}
            t.call("tools/call get_risk (an item the T362 policy withholds)", args)
            t.answer(summary(record(t, "get_risk", await session.call_tool("get_risk", args))))

            args = {"agent_key": key, "risk_id": 1, "version": version, "notes": "Changed by the T360 observer"}
            t.call("tools/call update_risk (an edit under a read lease)", args)
            t.answer(summary(record(t, "update_risk", await session.call_tool("update_risk", args))))

            t.call("(host) advance the test clock by 11 minutes: the 10-minute lease expires")
            control("CLOCK 11")
            t.answer("clock advanced")

            args = {"agent_key": key}
            t.call("tools/call search_risks (after expiry)", args)
            t.answer(summary(record(t, "search_risks", await session.call_tool("search_risks", args))))

            t.call("tools/call get_status (after expiry)", args)
            t.answer(summary(record(t, "get_status", await session.call_tool("get_status", args))))

    with open(transcript_path, "w", encoding="utf-8") as f:
        f.write(f"client: netrisk-t360-observer on the official MCP Python SDK, package mcp {sdk}\n")
        f.write(f"started: {started.isoformat()}\n")
        f.write("\n".join(t.lines) + "\n")
    # The raw results, for the host's leak checks — never the key, which appears in register_agent's own answer.
    raw = json.dumps(t.raw, ensure_ascii=True).replace(json.dumps(key)[1:-1], "<redacted-agent-key>")
    with open(transcript_path + ".raw.json", "w", encoding="utf-8") as f:
        f.write(raw)
    control("DONE")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--bridge", required=True)
    parser.add_argument("--discovery", required=True)
    parser.add_argument("--transcript", required=True)
    a = parser.parse_args()
    asyncio.run(run(a.bridge, a.discovery, a.transcript))


if __name__ == "__main__":
    main()
