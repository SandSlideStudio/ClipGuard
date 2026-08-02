# Security policy

ClipGuard reads clipboard contents and rewrites them. If that goes wrong the
consequences are real, so reports are genuinely welcome.

## Reporting a vulnerability

Use GitHub's private vulnerability reporting: **Security → Report a
vulnerability** on this repository. That keeps the report private until there's
a fix.

Please don't open a public issue for anything exploitable.

Useful to include:

- What an attacker gains, and what they need in order to try it
- Steps to reproduce, ideally with the clipboard content involved
- Which version, from `About` in the app

This is a spare-time project with no support contract behind it. Expect a reply
within a week or so rather than within hours.

## What counts

In scope:

- Detection bypasses: a clipboard swap or ClickFix payload that gets past the
  rules in `DetectionPolicy` or `RiskAnalyzer`
- Anything that reads `events.json` without the user's Windows credentials
- Getting ClipGuard to run, write or leak content it shouldn't
- Privilege escalation through the app, its installer, or the startup entry

Out of scope, because they're known and documented:

- Missing detections that the README's **Known limits** section already names,
  including slow swaps and denylist gaps in `RiskAnalyzer`. New evasion
  *techniques* are still interesting, so send those.
- SmartScreen warnings on unsigned builds. Known, and explained in the README.
- Anything requiring an attacker who already has code execution as your user.
  At that point the clipboard is the least of it.

## Threat model

Worth stating plainly, because it shapes what "vulnerability" means here.

ClipGuard defends against **hostile content arriving on the clipboard** from
applications and web pages running as you. It does not defend against malware
already running as you, another user on the machine, or anyone with
administrator rights.

Two design decisions people ask about:

- **No keyboard hook.** The paste guard watches foreground-window changes
  instead. It fires at the same moment while observing no keystrokes. A tool
  that reads your clipboard should not also read your typing.
- **Blocked means restored.** ClipGuard cannot stop a clipboard write from
  landing. It detects the swap and puts your content back before you can paste.

## Data

Everything stays on the machine. There is no network path out of the app, no
telemetry and no account.

`events.json` holds truncated previews of copied content and is encrypted at
rest with Windows DPAPI, scoped to your user account. Settings → *Remember what
was copied* turns preview storage off entirely.

A report showing that data leaving the device, or readable without the user's
credentials, is the most serious thing you could send.
