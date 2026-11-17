---
description: Let an AI agent on your computer work beside you in NetRisk — with your permissions, only while you allow it, under your eyes.
---

# 🤖 Agent co-work

**Agent co-work** lets an AI agent running on your own computer — Claude Code, Claude Desktop or any other
[MCP](https://modelcontextprotocol.io) client — read and, if you allow it, change NetRisk items **as you**: with exactly
your permissions, only during the minutes you grant, and with every call visible on your screen. It is a feature of the
desktop client; nothing changes on the server, and no other person's session is involved.

Design and threat model: [S55 — Agent co-work MCP server](../features/agent-mcp-server.md).

> **Before you start: your data leaves NetRisk.** What an agent reads — item text as written, **including personal data
> or passwords someone typed into it**, and people's names — is sent to the agent's AI model provider, outside NetRisk and
> possibly outside Brazil (LGPD). NetRisk withholds some data (see [What NetRisk withholds](#what-netrisk-withholds)), but
> everything else you can read can reach the provider. **It is your responsibility to use only an agent your
> organisation has approved.**

> **An agent with a shell is not bound by these limits.** Every limit on this page lives in the NetRisk client's agent
> server. An agent that runs as you with a shell or file access — Claude Code can run commands — can read your NetRisk
> profile folder, which holds your session and these settings, and act as you directly. **Run the agent sandboxed, deny
> it the NetRisk profile folder, and never grant edit access to an agent that has an unrestricted shell.** See
> [Run the agent sandboxed](#run-the-agent-sandboxed).

## How it works in one picture

1. You switch the agent server on (it is **off by default**).
2. The agent registers once; you approve it in NetRisk after checking a short **fingerprint**. It receives an **agent
   key**, shown once.
3. Each time it needs to work, the agent asks for **access in blocks of 10 minutes**. You grant it — shorter, read-only, or
   not at all.
4. While access lasts, the agent calls NetRisk **tools**; the status bar shows the time left, and the activity panel lists
   every call. When the time runs out, the agent is refused until you grant access again.

## Enabling agent co-work

1. Open **Application → AI agents…** (on macOS, in the application menu).
2. In **Agent connection**, tick **Allow AI agents on this computer to connect to NetRisk**.
3. The first time, NetRisk shows what this means for your data (the LGPD notice above). Choose **Yes** to switch it on, or
   **No** to leave it off.
4. The status line then shows the local address it listens on. Untick the box to stop it: the server closes at once and
   every access ends. It also closes when you sign out, and every access ends when you close NetRisk.

The server listens **only on this computer** (`127.0.0.1`), on a port chosen at each start, and only for programs that can
read your private discovery file. Agents on other computers cannot connect.

## Registering an agent

Agents reach NetRisk through the small **`netrisk-mcp-bridge`** program that comes with the client, which they start as a
"stdio" MCP server.

### Connect an agent (one click)

In **Application → AI agents…**, choose **Connect an agent…**. NetRisk lists the AI clients it knows — Claude Code,
Claude Desktop, GitHub Copilot in VS Code and VS Code Insiders, GitHub Copilot CLI (whose file VS Code's current Copilot
also reads) and ChatGPT — with each one's configuration file and its state:

| State | Meaning |
|---|---|
| Not detected | NetRisk did not find it. If it is installed somewhere else, select it and **Choose file…** to point at its configuration file. |
| Not registered | Installed, without a NetRisk entry. |
| Registered | Its entry starts this NetRisk's bridge. |
| Registered, but stale | It has an entry, but for another bridge path or settings (for example after NetRisk moved). Register it again. |
| Cannot be changed | The file cannot be used as it is (the *Detail* column says why — for example it is not valid JSON). NetRisk leaves it alone. |
| Not automatic | ChatGPT: see the steps shown under the list. |

1. **Close the client** if it is running (it may rewrite its own file while open).
2. Select it and choose **Register…** (or **Unregister…**). NetRisk shows the exact lines that will change in that file;
   nothing is written yet.
3. Choose **Apply** to write it, or **Cancel**. NetRisk keeps a copy of the file beside it
   (`<file>.netrisk-<date and time>.bak`), changes only its own `netrisk` entry and leaves every other server and setting
   as it was. A file that is not valid is never changed.
4. Start the client again so it reads the change.

The entry only tells the client how to start the bridge: it holds **no port, token or key**, and it grants nothing — the
agent still has to register and you still approve it (below). **Unregister** removes only the `netrisk` entry.

If NetRisk says the bridge was not found, your installation does not include it yet; Unregister still works. Clients
NetRisk does not configure — and **ChatGPT**, whose desktop app has its own MCP settings — are set up by hand: add a
"stdio" server whose command is the bridge's path (the ChatGPT steps in the window show the exact command). For Claude
Code by hand: `claude mcp add netrisk -- <path to>/netrisk-mcp-bridge` (add `-e DOTNET_ROOT=<your .NET folder>` if .NET
is not installed in its default location; *Connect an agent* adds it for you when needed).

Then ask the agent to connect to NetRisk. It calls `register_agent`, shows you a fingerprint such as `AAE5-84D5`, and
NetRisk opens **An AI agent wants to connect**:

- Approve **only if the fingerprint matches** the one your agent shows. The Approve button arms itself after a second, so a
  stray key press cannot approve.
- On approval the agent receives its key **once**. NetRisk keeps only a hash of it.

Your registered agents are listed in **Application → AI agents…**, where you can **rename** or **revoke** them. Revoking
takes effect immediately.

## Access blocks

An approved agent still can do nothing until it asks for access with a reason and a number of 10-minute blocks (1 to 6).
NetRisk opens **An AI agent is asking for access**, showing the agent, its fingerprint, its reason, what it asked for and
the LGPD notice with the data classes withheld right now. You may:

- grant **fewer minutes** than requested (one block is preselected);
- grant **read** instead of **read and edit** (read is preselected);
- for a read-and-edit grant, keep **Confirm each change before it is saved** on (on by default — recommended for a new
  agent);
- **Deny**.

Access ends when its time runs out, when you **End access**, sign out, lock the session (Windows), close NetRisk, switch
the server off, or revoke the agent. It never renews itself: more time is a new request you grant again.

## Read and edit access

- **Read**: the agent can search and read risks, mitigations and reviews, findings and their lifecycle, incidents and
  response plans, assessments, runs, answers and evidence lists, entities and hosts, and the Cyber Risk Overview — only
  what **you** can read.
- **Read and edit**: in addition, twelve edit tools change a few descriptive fields of one item at a time: risk text, a
  finding's open-work status, fix team or a note, incident text or a new incident timeline entry, an unanswered
  assessment question or a run's comments, an entity's descriptive fields, a host's comment or OS. Each change uses the
  item's `version`; if the item changed since the agent read it, the change is refused and nothing is written. The agent
  can also **create a risk or an incident** with descriptive text only: you become its owner or reporter, it starts as
  New, and every person, entity, status and score stays for you to set. **A new risk or incident always waits for your
  approval** — whatever you chose for confirmations — in a dialog that lists everything the new item will get and what
  NetRisk then does by itself: it notifies the subscribers of new risks or incidents, and a new incident may start the
  response plans whose automation matches it.

Under a read grant the edit tools do not exist for the agent; if it tries one anyway it is refused as `read_only`.

## Confirm each change

With **Confirm each change before it is saved**, every edit waits for you in **An AI agent wants to change an item**,
showing the agent, the item and, for each field, **what changes** (the changed span only), the size of the old and new
value in lines and characters, and the whole value **before → after**. Item text is shown cleaned: hidden and
right-to-left characters are removed and long runs of blank lines are replaced by a "[n blank lines]" marker, so text
cannot hide below the fold. An agent's new text of more than 20 lines, or with more than 3 blank lines in a row, is
refused. **Deny** is focused; **Approve** arms after a second. If you do not answer within 45 seconds the change is not
made. An agent can have only one change waiting at a time.

Even without this option, a grant allows **20 changes**; from the 21st on, every change of that grant waits for your
approval, and the activity panel shows `confirmation_required`.

## Activity panel and status bar

While an agent holds access, a bar at the bottom of the main window shows the agent, the mode and the time left, with
**Pause access**, **End access** and **Activity**. In the last minute it warns you and offers **Grant another 10 min**, which you
then confirm in the normal access dialog.

**Activity** lists every call: time, tool, item, read or edit, and the result (for example `ok`, `withheld`,
`read_only`, `access_expired`). Screens show when an agent is reading or editing an item, and refresh when it changed one;
if you were editing the same item, your save is refused so nothing is overwritten — reopen the item. Each field an agent
changed stays **highlighted** — underlined on a coloured background, with a tooltip naming the agent — and the notice above
names every changed field, until you click **Mark as seen** or edit and save the item yourself.

## Revoking and stopping

- **End access** (status bar, Activity, or the Agents window): the current access ends now; the agent keeps its
  registration.
- **Pause access / Resume**: calls are refused while paused; the clock keeps running.
- **Revoke** (Agents window): the key stops working at once and any access ends.
- **Untick the server switch**: nothing can connect.

## What an agent can never do

- Anything **you** cannot do: every call goes through your own session and the server decides.
- Delete anything, approve, sign off, accept a risk, vote, decide a gate, change roles, permissions, settings, plugins,
  integrations or backups — the agent can prepare, you decide.
- Read a stored credential: credential fields, vault references and keys are not part of any tool's answer.
- Use your session token **through NetRisk**: it holds its own key, which is useless without access you granted.
- Change which data is withheld from it **through NetRisk**.

These hold for an agent that reaches NetRisk only through its tools. An agent that can run commands or read files as you
can bypass all of them — see [Run the agent sandboxed](#run-the-agent-sandboxed).

## What NetRisk withholds

- **Fields no tool returns at all** — stored credentials, vault references in credential fields, keys, hosts' scanner
  properties.
- **Entity properties named like contact data or credentials** — an entity's e-mail, phone, address, CPF and similar
  properties, and properties named or shaped like a password, token or key (a pattern check, not a guarantee).
- **Vault references** anywhere in a result.

**Free text is not screened.** Titles, descriptions, notes, comments and scanner output go to the agent as written: an
e-mail address, a phone number or a password someone typed into them goes too.
- **Data classes your organisation excludes.** In the Agents window, **Data withheld from agents** lists the classes, drawn
  from the LGPD data catalogue and the security classification:
  - Personal data
  - **Sensitive personal data (LGPD art. 11)** — withheld by default
  - **Data of children or adolescents (LGPD art. 14)** — withheld by default
  - Large volume of data
  - Strategic research
  - **Security classification marked sensitive** — withheld by default

  **Sensitive personal data and data of children or adolescents are always withheld** (the organisation minimum); the
  list can add classes, never remove those two.

  Withheld: an entity in a withheld class (a data record so catalogued, an application, data group or data record at a
  sensitive security level) and everything under it in the entity map; a risk linked to one through its "Entity" field or
  its chain, or carrying flag 5; a finding linked to one through its application, its host or its risks; an incident,
  host, assessment or assessment run whose entity is withheld. Such an item is never listed, read or edited by an agent.
  A search only says **how many** items were withheld; asking for one by id answers `withheld`. **An item whose
  classification is missing or cannot be read is withheld as well** — for example a data record not yet catalogued, or
  every data record if you cannot read the data catalogue. **Incident response plans are not classified.**

  Only a user with the **data-catalogue permission** (the DPO's office) or an administrator can change the list; everyone
  else sees it read-only. The list is kept in **this computer's** NetRisk settings and applies to agents on this computer
  only.

Agents are told that every item text is **untrusted data** — written by other people and systems — and must not be
followed as instructions. Treat odd agent behaviour (for example, an attempt to edit something you did not ask for) as a
reason to end access.

## Run the agent sandboxed

NetRisk keeps your session and these settings in your **profile folder**:

| System | Profile folder |
|---|---|
| Windows | `%APPDATA%\NRGUIClient` |
| macOS, Linux | `~/.config/NRGUIClient` |

(For a non-production environment, `<that parent>/<environment>/NRGUIClient`.) An agent that can read this folder can act
as you outside every limit on this page. For **Claude Code**:

1. Add deny rules for the folder to your Claude Code settings (for example `~/.claude/settings.json`):

   ```json
   {
     "permissions": {
       "deny": [
         "Read(~/.config/NRGUIClient/**)",
         "Edit(~/.config/NRGUIClient/**)"
       ]
     }
   }
   ```

   On Windows, write the same rules for your `%APPDATA%\NRGUIClient` path in the absolute-path form your Claude Code
   version documents.
2. Turn on Claude Code's **sandboxed Bash** (`/sandbox`). A deny rule on `Read` does not by itself stop a shell command
   such as `cat`; the sandbox is what applies filesystem restrictions to commands. Check that asking the agent to list the
   profile folder fails.
3. **Never grant read-and-edit access to an agent that has an unrestricted shell**, and prefer read-only grants in
   general.

These steps reduce the risk; they are **not a guarantee** — the rules and the sandbox are Claude Code's, they change
between versions, and a misconfiguration is silent. Check your version's documentation. Other agents: use whatever
sandbox or file-access restriction they offer, to the same effect.

## Security and LGPD notes

- Use only an agent and an AI provider **approved by your organisation**; their terms decide what happens to what they
  receive. NetRisk cannot see or control the provider.
- Grant the **shortest, read-only** access that does the job; keep **Confirm each change** on for new agents.
- Run the agent **sandboxed**, without access to the NetRisk profile folder ([above](#run-the-agent-sandboxed)).
- Server-side records show your requests as made **through an agent** (the audit trail of risks, hosts and entities, and
  the server log for everything), next to your name.
- The local log records which tools ran on which items, never the values.

## Troubleshooting

| What you see | What to do |
|---|---|
| The agent says NetRisk is not running or not reachable | Sign in to NetRisk and tick the server switch; the bridge finds the running client through the discovery file. |
| The status line says the server could not start | Untick and tick the switch again; check the client log. |
| The agent fails to start the bridge with "You must install .NET" | Register it again from **Connect an agent…**, or pass the .NET location to the bridge by hand (`-e DOTNET_ROOT=…` in `claude mcp add`). |
| *Connect an agent* shows a client as **Cannot be changed** | Read the *Detail* column: fix the file in that client (or remove a duplicate `netrisk` entry) and choose **Refresh**. NetRisk never rewrites a file it cannot read. |
| *Connect an agent* says the file changed after the preview | The client saved its file meanwhile. Close the client and choose **Register…** again. |
| `unauthorized` | The key is unknown or revoked: the agent must register again. |
| `access_expired` | Access ended or was never granted: the agent asks again with `request_access`. |
| `paused` | You paused access; resume it from the status bar. |
| `read_only` | The grant is read-only; the agent must ask for `read_write`. |
| `withheld` | The item's data class is withheld from agents (or could not be classified); work on it yourself. |
| `stale` | The item changed since the agent read it; it must read it again. |
| `write_denied` / `not_confirmed` | You denied, or did not answer, the change dialog. Nothing was written. |
| The agent sees fewer items than you | Items in withheld classes, contact fields and credentials are left out by design. |
