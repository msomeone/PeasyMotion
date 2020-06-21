### 1.6.1
- Two characted search mode has beed added. Invoke via Tools.InvokePeasyMotionTwoCharJump
- Jump to line begining fix - non-empty lines have jump label on first 'visible' character
- Added status bar text showing peasy motion status

### 1.5.1
- Jump to line begining added via Tools.InvokePeasyMotionJumpToLineBegining
- Improved EOL convention handling
- Improved close jump labels readability

### 1.4.2
- Jump to open document tab via Tools.InvokePeasyMotionJumpToDocumentTa has been added
- Fixed response on ';' presence in jump label
- Jump label assignment algorithm fix: no more skipped candidates (i hope).
- Autoload and warmup improved, leading to faster UI response on first command invocation

### 1.3.1
- Moved Tools->Invoke * PeasyMotion*  commands into separate Tools->PeasyMotion submenu
- Crash from uncaught exception during VsSettings initialization has been fixed
- Correct deactivation when wrong keys pressed

### 1.2.1
- Jump to word begining or ending in current line via Tools.InvokePeasyMotionLineJumpToWordBegining or Tools.InvokePeasyMotionLineJumpToWordEnding
- Jump label positioning algorithm has been improved: catches empty lines, better EOL handling, a lil bit closer to vim-easymotion behaviour

### 1.1.1
- Text Selection via JumpLabel ( Tools.InvokePeasyMotionTextSelect )
- "Whats New" notification (InfoBar) added, to notify users after auto-update
- Improved ViEmu support (no more caret one-char offset after jump)
- Minor cleanups

### 1.0.21
- Fixed wrong font color for reused jump label UI controls (color was red instead of black for some labels)

### 1.0.20
- Options added (Tools -> Options -> PeasyMotion options -> ...)
- Added caret position sensivity option.
- Added jump label assignment algorithm option.
- Shaved off 60% command execution time: (~200-400ms) -> (~50ms), slowest part is adornment addition.
- JumpLabel user controls are cached now (speeds up adornments addition),
- Cached VsVim commands availability, to prevent slow DTE.Commands search on PeasyMotion activation.