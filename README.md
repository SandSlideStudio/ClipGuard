# ClipGuard

Windows clipboard protection by **SandSlide Studio**. Watches the clipboard,
identifies the application behind every change, and puts your original content
back when something swaps it. Everything runs locally — clipboard contents never
leave the machine.

Built from the *ClipGuard UI Mockups* design handoff (consumer tier).

## Running it

```bash
dotnet run --project src/ClipGuard
```

Requires the .NET 10 SDK.

**ClipGuard is a tray app, so "nothing appears" is often the correct behaviour.**
First launch runs the setup wizard and then opens the main window. Every launch
after that respects the *Launch minimized to tray* preference — which the wizard
defaults to **on**, so the app starts with no window at all. It's running; look
for the red shield in the tray.

On Windows 11 new tray icons are hidden behind the `^` chevron by default. Click
the chevron to find the shield, and drag it onto the taskbar to pin it.

- **Left-click** the tray icon for the flyout, **double-click** to open the app.
- **Closing the window hides it** — protection keeps running. Exit via the tray menu.
- **Launching again while it's running** just surfaces the existing window; it
  won't start a second copy (two monitors would fight over the clipboard).
- Pass `--minimized` to force a tray-only start; this is what the "Start with
  Windows" preference writes into the Run key.

Deleting `%LOCALAPPDATA%\ClipGuard\` resets everything back to first run.

## What's here

| Screen | Mock | Where |
| --- | --- | --- |
| Main window, 5 tabs | `5a` / `3a` | `Views/MainWindow.xaml` + `Views/*View.xaml` |
| Setup wizard, 4 steps | `2a` | `Views/SetupWindow.xaml` |
| Tray flyout | `5b` | `Views/TrayFlyoutWindow.xaml` |
| Notification ladder | `5c` | `Views/NotificationWindow.xaml` |

Design tokens live in `Themes/Tokens.xaml` — a direct port of the handoff's
`styles.css` ramps. Colour literals appear there and nowhere else. Shared control
styles are in `Themes/Controls.xaml`, icon geometries in `Themes/Icons.xaml`.

The enterprise tier (`5d`–`5g`) is not built. The token and control layer is
tier-agnostic, so it's a theme swap (zero radius, 2px dividers, dark title bar)
plus four screens.

## What it protects against

Three separate defences, because clipboard attacks come in two shapes:

**1. Swap protection.** A page or app replaces what you copied — a wallet address
for the attacker's, `npm install …` for `curl … | sh`. ClipGuard restores your
original within milliseconds.

**2. Content warnings, and defusing.** Nothing gets swapped: the page simply
*hands* you a malicious command. This is the ClickFix / fake-CAPTCHA pattern —
"press Win+R, Ctrl+V, Enter to verify you're human" — and swap detection is
completely blind to it.

ClipGuard flags download-and-run commands the moment they land, and by default
**replaces them with an inert `echo`**. A warning only helps if it's read; taking
the payload off the clipboard means missing the notification and pasting anyway
is survivable. The failure mode becomes "nothing happened" rather than a
compromised machine.

**The replacement is disguised on purpose.** A stand-in that obviously announced
"your clipboard was tampered with" would tell someone mid-attack that the paste
failed — and the natural next move is to copy the command again, or switch the
guard off. So the stand-in is shaped like an ordinary command and exits silently:
the fake "verification step" appears to complete, nothing happens, and the attack
dies quietly. The explanation lives in the notification and History instead.

Two fixed templates, chosen by the original's shape
(`RiskAnalyzer.DefusedReplacementFor`):

| Original shape | Stand-in | What running it does |
| --- | --- | --- |
| PowerShell, mshta, LOLBins, Run-dialog payloads | `powershell -NoProfile -Command "Write-Verbose '…'"` | prints nothing, exits 0 — a console flashes and closes |
| `curl … \| sh`, `base64 -d \| sh` | `echo '…'` | prints one harmless line, exits 0 |

Neither invokes anything, touches the network, or writes a file.

> **A stand-in must contain no `$` and no backtick.** Pasting into a PowerShell
> prompt makes the *outer* shell expand the double-quoted argument before the
> child process ever sees it, so `"$m = '…'"` arrives as `= '…'` and throws a
> visible `CommandNotFoundException` — the exact "your paste failed" tell the
> disguise exists to avoid. A `Debug.Assert` in `RiskAnalyzer`'s static
> constructor enforces this.
>
> Redirection is out for the same class of reason: `> /dev/null` creates a stray
> `dev\null` file if the stand-in lands in PowerShell instead of a real shell.
>
> Verify stand-ins by **executing** them in cmd and PowerShell, not by reasoning
> about them — classification passing tells you nothing about how a shell parses
> the text.

> **Hard rule:** templates are fixed constants selected by *classifying* the
> payload — never by transforming it. Building a replacement out of attacker text
> risks splicing live fragments back onto the clipboard, which is precisely the
> failure this feature exists to prevent. `IsDefusedReplacement` additionally
> stops a stand-in from ever being re-flagged and feeding itself.

It's always reversible: the notification offers **"I need that command"**, which
restores the original verbatim from memory. That matters because legitimate
installers (`irm …/install.ps1 | iex`) are indistinguishable by shape — only you
know whether you copied it on purpose. Wallet addresses are only warned about,
never defused; an address is data, not a payload.

A command carrying fake verification text ("I am not a robot", "reCAPTCHA
Verification ID") is treated as certainly malicious — nothing you genuinely need
to run comes with a CAPTCHA note attached.

**3. Paste-time protection.** The last line of defence, at the moment that
actually causes harm. When a terminal or the Run dialog takes focus while a
download-and-run command sits on your clipboard, ClipGuard says so and offers to
clear it.

> This is a **foreground-window** hook (`EVENT_SYSTEM_FOREGROUND`), not a keyboard
> hook. It fires when Win+R opens — a beat *before* the paste — while observing no
> keystrokes at all. A clipboard security tool has no business keylogging, so
> there is deliberately no `WH_KEYBOARD_LL` anywhere in this codebase.

Both 2 and 3 have toggles in Settings, on by default.

## How detection actually works

This is the part worth understanding before trusting it.

**Attribution is per-application, not per-website.** Windows tells a desktop app
*which process* owns a clipboard change, never which page. Events therefore read
`chrome.exe`, not `docs.google.com`. Real per-site attribution needs a companion
browser extension.

**"Blocked" means restored, not prevented.** Nothing outside the browser can stop
a clipboard write from landing. ClipGuard detects the swap and rewrites your
original content within milliseconds — before you can paste the replacement. The
outcome for the user is identical, and the UI says so in Settings → *How blocking
works*.

**Only writes are detected.** Windows raises no notification when an app *reads*
the clipboard, so the mocks' "read your clipboard · allowed" events can't be
produced honestly on the desktop tier. Read detection would require ClipGuard to
take ownership of the clipboard with delayed rendering, which risks breaking
paste in other apps — deliberately not done.

### The heuristics

A change is treated as a swap when there was prior content, it actually changed,
and **two** signals agree (one alone false-positives constantly):

- `background` — the writing process isn't the focused one
- `rapid` — it landed within 2.5s of the previous change
- `riskShaped` — the new content is a wallet address or a shell command
- `sameKindSwap` — an address replaced a *different* address, or a command a
  different command

Specifically: `(background && (riskShaped || rapid)) || (rapid && sameKindSwap)`.

The `sameKindSwap` path is what catches a malicious page rewriting the clipboard
*inside your focused browser*, where `background` is false. Tune these in
`Services/ProtectionService.cs`; the shape tests live in `Services/RiskAnalyzer.cs`.

Content warnings don't use any of that — they fire on shape alone, so nothing has
to be replaced for them to trigger. That's what makes them the ClickFix defence.
Wallet addresses are the one exception: they only warn when a *browser* put them
there, since copying an address from your own wallet is routine.

### False triggers

`irm https://ollama.com/install.ps1 | iex` is a real install command and is
*byte-for-byte the same shape* as a ClickFix payload. No pattern separates them,
because as text they aren't different. Three signals exist outside the text; two
are implemented.

**1. Where the code comes from.** `KnownInstallHosts` in `RiskAnalyzer` is a
narrow allowlist of vendor-controlled install endpoints. A command is exempt from
both warning and blocking when *every* host it fetches from is trusted. Mixed
hosts fail, and a verification lure overrides the allowlist entirely — no genuine
installer arrives wearing a fake CAPTCHA note, whoever hosts it.

Redirectors and user-content hosts are deliberately absent: `aka.ms`,
`raw.githubusercontent.com` and `gist.github.com` would each let an attacker
serve from an allowlisted name.

**2. The user telling us.** Clicking *"I copied it on purpose"* or *"I need that
command"* is the only ground truth in the system — every other signal is
inference. It sets `UserOverrode` on the event and adds the command's download
hosts to `TrustedSources`, so the same command stops asking. Those learned hosts
appear in **App rules → Trusted download sources** and can be removed; unlike the
internal ignore list, this one *weakens* protection, so it is never invisible.

`FalsePositiveCount` and `FlaggedCount` on `ProtectionService` give the ratio, and
`events.json` is the full audit trail — every flag with its category and whether
it was overridden.

**3. Whether the user actually pressed copy** — *not implemented*. A page writing
the clipboard via JavaScript and a person pressing Ctrl+C are indistinguishable
from outside the browser, and that single bit would resolve almost every false
positive. Getting it means either the companion browser extension, or sampling
`GetAsyncKeyState` for the copy chord at the moment of the write. The latter is a
point-in-time read of two keys rather than a hook, but it is still keyboard state,
which this codebase currently avoids entirely. It would only ever be used to
*suppress* warnings, never to escalate — copying via right-click produces no
keystroke, so absence proves nothing.

**Known limits.** A patient swapper that waits more than 2.5s after your copy
slips past the swap rules (`SwapWindow` is one constant). Non-text clipboard
content is logged but never judged — we can't read it. And a command that dodges
every pattern in `RiskAnalyzer` won't be flagged; the pattern list is a
denylist, not proof.

Windows raises `WM_CLIPBOARDUPDATE` once per format an app offers, so a single
copy arrives two or three times. `ClipboardMonitor` collapses that burst and
ignores the echo of its own restore write — without both, history fills with
triplicates.

**ClipGuard ignores itself.** Restores, defused stand-ins and *Clear clipboard*
are ClipGuard writing to the clipboard, and none of them are events. That's on
top of the content-based echo suppression in `ClipboardMonitor`, which can't
cover an echo that arrives late or whose content doesn't match what was written.

The list (`IgnoredProcesses` in `ProtectionService`) is hardcoded and not
user-editable on purpose — it's plumbing, not a rule. Entries are matched by
**process id**, which nothing can spoof; the name is only a fallback for when the
owner window won't resolve to a pid, and even then the image path must match
ours. Matching on the name `ClipGuard.exe` alone would hand permanent silence to
any malware that renamed itself.

`_lastWriter` is deliberately not updated for ignored writes: paste warnings name
it, and "ClipGuard put this on your clipboard" would be false.

## Tests

```bash
dotnet test
```

70 tests over the parts where being wrong is expensive: `RiskAnalyzer`
classification and the trusted-source allowlist, `Storage` encryption and legacy
migration, the event model, and the defused stand-ins.

`DefusedTemplateTests` **executes** each stand-in in cmd and in PowerShell rather
than reasoning about it, feeding PowerShell through **stdin** because that is
what pasting at a prompt does — passing the text as a `-Command` argument routes
it through CreateProcess quoting instead, a different parser that hid the bug it
was meant to catch. The old `"$m = …"` template fails that harness with the exact
`= : The term '=' is not recognized` error a user hit; the shipping one passes.

Tests redirect `Storage.Root` via `CLIPGUARD_DATA_DIR` in a module initialiser,
so they never read or overwrite a real install's history.

**Still untested:** the `ProtectionService` decision matrix — the heart of the
product. It needs the swap/warn/defuse decision extracted into a pure function
before it can be covered, since today it's entangled with clipboard access and
dispatcher timers. That extraction is the highest-value next piece of work.

## Packaging and signing

```bash
./build/publish.ps1          # runs tests, then publishes
iscc build/ClipGuard.iss     # requires Inno Setup 6
```

`publish.ps1` produces a self-contained single-file `win-x64` build (~72 MB) in
`dist/`, so target machines need no .NET install. **Tests gate the publish** — it
refuses to build if any fail.

The installer is per-user (`PrivilegesRequired=lowest`): ClipGuard writes only to
HKCU and `%LOCALAPPDATA%`, and a tool selling "stays out of your way" shouldn't
demand admin. Uninstall stops the tray process and *asks* before deleting your
history rather than assuming.

**Nothing here is signed, and it needs to be.** Unsigned security software trips
SmartScreen and looks like the thing it protects against. With an EV or OV
code-signing certificate:

```bash
signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /a dist/win-x64/ClipGuard.exe
```

Sign the published exe *before* compiling the installer, then sign the installer
too. The certificate has to come from you — it can't be scripted around.

## Layout

```
src/ClipGuard/
  Themes/        tokens, control styles, icon geometry
  Controls/      PixelShield (the 8x9 brand mark), Typography (letter-spacing)
  Models/        ClipboardEvent, AppRule, AppSettings
  Services/      Win32 interop, ClipboardMonitor, ProtectionService,
                 RiskAnalyzer, ProcessResolver, Storage, Tray, Notifications
  ViewModels/    MainViewModel
  Views/         windows and tab views
  AppHost.cs     composition root
```

State lives under `%LOCALAPPDATA%\ClipGuard\` (override with `CLIPGUARD_DATA_DIR`
for a portable install). Deleting the folder resets the app to first run.

| File | Encrypted | Why |
| --- | --- | --- |
| `events.json` | **yes**, DPAPI (CurrentUser) | holds previews of copied content |
| `rules.json`, `trusted-sources.json`, `settings.json` | no | no clipboard content |
| `logs/crash-*.log` | no | exception text only, opt-in |

Encrypting the event log isn't optional politeness. Unencrypted it is a
plaintext record of everything the user ever copied — passwords, tokens,
recovery phrases — at a predictable path. A clipboard security tool must not be
the thing that creates that file. Settings → *Remember what was copied* turns
preview storage off entirely, keeping the verdicts without the content. Legacy
plaintext files from earlier builds are read once and re-saved encrypted.

**Failure is visible.** `AddClipboardFormatListener` returning false, or any
unhandled exception, flips `IsHealthy` and the status chip reads "Not protecting"
with a hollow dot — distinct from the grey dot of a user-chosen pause. Security
software that silently does nothing while claiming to stand guard is worse than
software that isn't installed.

## Notes for the next person

- **Fonts.** Archivo static weights (400/500/600/700/800) are vendored in
  `Assets/Fonts` under the SIL OFL, from the upstream Omnibus-Type repo. The
  `FontFamily` pack URIs are assembly-qualified so they resolve when another
  assembly is the entry point.
- **Letter-spacing.** WPF has no tracking property, and the mocks use it on every
  small-caps label. `Controls/Typography.cs` adds an attached `Tracking` property
  that rebuilds the text as character runs separated by scaled spacers.
- **WinForms is referenced only for `NotifyIcon`** — WPF has no tray primitive.
  That drags `System.Drawing` into every file, so `GlobalUsings.cs` points the
  ambiguous names (`Color`, `Brush`, `Application`, …) at WPF. Global aliases
  can't be shadowed per-file, so `TrayService.cs` uses `GdiColor`/`GdiBrush` for
  its GDI+ drawing.
- **`Border.CornerRadius` does not clip children.** The medium toast's flush
  progress bar needs the explicit geometry clip in `NotificationWindow.xaml.cs`.
- **Rule scope is an executable, and that changes what "block" means.** The mocks
  offered "Block site forever", which is proportionate for `sketchy-deals.net`.
  Blocking `chrome.exe` is not the same thing — it reverts every copy you make
  from the browser. So the high-risk toast hides the block action entirely for
  browsers, and `Views/RuleConfirmation.cs` gates it everywhere else it can be
  reached (History CTA, App rules add, tag toggle). Real per-site rules need the
  browser extension.
- **Restoring a blocked write must use the full text, not `ContentPreview`.**
  The preview is capped at 120 characters for display; restoring it would hand
  back mangled content. `ProtectionService` keeps the last 20 blocked payloads in
  memory (never on disk — this is content we just judged hostile) and
  `CanRestore` reports whether one is still available.
- **Neutral rule tags are invisible against rows.** `tag-neutral` and the row fill
  are both `neutral-100` in the handoff, so "Always allow" reads as untagged.
  Reproduced faithfully; worth raising with design.
