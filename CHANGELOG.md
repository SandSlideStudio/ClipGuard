# Changelog

Notable changes to ClipGuard. Format loosely follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versioning is
[semantic](https://semver.org/).

## [Unreleased]

Nothing yet.

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
