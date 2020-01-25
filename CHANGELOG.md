### 1.0.21
- Fixed wrong font color for reused jump label UI controls (color was red instead of black for some labels)

### 1.0.20
- Options added (Tools -> Options -> PeasyMotion options -> ...)
- Added caret position sensivity option.
- Added jump label assignment algorithm option.
- Shaved off 60% command execution time: (~200-400ms) -> (~50ms), slowest part is adornment addition.
- JumpLabel user controls are cached now (speeds up adornments addition),
- Cached VsVim commands availability, to prevent slow DTE.Commands search on PeasyMotion activation.