# Changelog

Notable changes to ClipGuard. Format loosely follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versioning is
[semantic](https://semver.org/).

## [Unreleased]

### Added

- **Update check.** ClipGuard asks GitHub once a day whether a newer release
  exists, and says so with a banner in the window and an item in the tray menu.
  Nothing is downloaded or installed automatically: both open the release page,
  where the checksum and the provenance attestation can be checked. The request
  carries no identifier, not even the version in the `User-Agent`, and turning
  *Check GitHub for new releases* off in Settings stops it being made at all.
  This is the only outbound request in the app.

  A build on a release candidate is offered newer candidates and the final
  release. A build on a final release is only ever offered final releases, so a
  stable install is never nudged onto an untried one.

### Fixed

- **Documents that quote a command are no longer treated as attacks.** Copying a
  project README containing a single `curl ... | sh` install line was reported as
  a fake-CAPTCHA attack, and with defusing on, pages of the user's own text were
  taken off the clipboard and replaced with the inert stand-in. Detection now
  asks whether the content *is* a command as well as whether it contains one. A
  fake verification note still overrides that, so padding a payload with prose
  does not get it through.

## [1.0.0]

First public release.

### Protection

- **Swap detection.** Spots a clipboard substitution and restores the original
  within milliseconds. Requires two corroborating signals before acting, since
  any one alone produces constant false positives.
- **Content warnings.** Flags download-and-run commands the moment they land,
  whether or not anything was replaced. This is the ClickFix and fake-CAPTCHA
  case, which swap detection cannot see.
- **Defusing.** Replaces a flagged command with an inert stand-in shaped like an
  ordinary command, so pasting it after missing the warning does nothing.
  Always reversible from the notification.
- **Paste guard.** Warns when a terminal or the Run dialog takes focus while a
  command is on the clipboard. Implemented as a foreground-window hook, not a
  keyboard hook.
- **Per-app rules**, plus trusted download sources learned from the times you
  tell ClipGuard it got something wrong.
- **Auto-clear** the clipboard after 60 seconds or 5 minutes.

### Installation

- ClipGuard checks where it is running from at startup. Downloads, temporary
  folders, removable drives and network shares are refused, because the startup
  entry records the executable's path and would break silently when that folder
  is cleaned or unplugged.
- It offers to copy itself somewhere permanent, with a folder picker, then
  restarts from the new location and removes the original.
- Declining runs **test mode**: protection works normally for the session, but
  startup registration is disabled and the sidebar says so.

### Privacy

- Event history encrypted at rest with Windows DPAPI, scoped to your user.
- Settings toggle to keep verdicts without storing any copied content.
- Configurable retention: 24 hours, 7 days or 30 days.
- No network path out of the app, no telemetry, no account.

### Interface

- Tray app with a status flyout and a five-tab main window.
- Four-step first-run setup.
- Notifications scaled to severity, from a one-line toast to a sticky warning.
- Health is visible: if the clipboard listener fails to attach, the status reads
  "Not protecting" rather than silently claiming to work.

### Known limits

- Attribution is per-application, not per-website. Windows names the process,
  never the page.
- Clipboard reads are not detectable from a desktop app, so only writes appear.
- A swap arriving more than 2.5 seconds after your copy is not caught.
- Non-text clipboard content is logged but never judged.
- Releases are unsigned. SmartScreen will warn; verify the checksum and build
  provenance published with each release instead.

[Unreleased]: https://github.com/SandSlideStudio/ClipGuard/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/SandSlideStudio/ClipGuard/releases/tag/v1.0.0
