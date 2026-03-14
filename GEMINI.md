# Antigravity Terminal Handling Rules

### Terminal Input Rule (CRITICAL)
When using `send_command_input` to interact with running terminals, **ALWAYS include a newline character (`\n`) at the end of the `Input` string!**
Without `\n`, the command is typed but never submitted. This causes "Silent Command Drops" where you believe the command ran, but nothing actually happened. 
*(Hint: Make sure the string ends with a literal newline token in JSON, e.g. `"Input": "command\n"`).*

### Antigravity Terminal Relay Bug (MANDATORY WORKAROUND)
The Antigravity platform has a known PTY allocation timing bug where `run_command` initializes the shell session but **silently drops the actual command**. 

**Mandatory Protocol:**
1. After EVERY `run_command` call, check the output.
2. If the output shows ONLY the shell header + prompt (e.g., `Microsoft Windows [Version ...]` + `C:\path>`) with NO actual output from your command, the command was dropped.
3. Immediately use `send_command_input` with the **same command text + `\n`** to re-submit it!
4. Do NOT retry with another `run_command` (it will spawn a new shell and drop it again).
5. Always press Enter (\n) in `send_command_input` after the command text.
