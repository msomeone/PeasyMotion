PeasyMotion
===
[![Build status](https://ci.appveyor.com/api/projects/status/dm1x4gin96pp9oy2/branch/master?svg=true)](https://ci.appveyor.com/project/msomeone/peasymotion/branch/master)

![Animated demonstration](https://raw.githubusercontent.com/msomeone/PeasyMotion/master/preview.gif)

Implements "word-motion" mode ~same way as it is done in vim-easymotion.
This extension differs from other motion/jump extensions as it assigns jump labels to all words in text viewport, without asking specific "jump" key.
Such a behaviour may lead to faster motion and navigation in certain scenarios.
Inspired by original [vim-easymotion](https://github.com/easymotion/vim-easymotion) script for VIM.


## Compatibility with other plugins
VsVim 
just bind PeasyMotion command in your .vimrc (or .vsvimrc) file:
```vimscript
nmap ;; gS:vsc Tools.InvokePeasyMotion<CR>
```

ViEmu
just bind PeasyMotion command in your _viemurc file:
```vimscript
nmap ;; gS:vsc Tools.InvokePeasyMotion<CR>i
```
'i' is needed to enter into input mode.

Download this extension from the [VS Gallery](!!!!!!!!!!!)
or get the [CI build](http://vsixgallery.com/extension/PeasyMotion.a87d2837-6b54-4518-b014-3b29b4dcd902/).

## Bugreports, Feature requests and contributions
PeasyMotion can be developed using Visual Studio 2017 or 2019. Contributions are welcomed.
Check out the [contribution guidelines](CONTRIBUTING.md)

## License
All code in this project is covered under the MIT license a copy of which 
is available in the same directory under the name LICENCE.txt.

## Latest Builds
The build representing the latest source code can be downloaded from the
[Open Vsix Gallery](http://vsixgallery.com/extension/PeasyMotion.a87d2837-6b54-4518-b014-3b29b4dcd902/).

## Buidling
For cloning and building this project yourself, make sure
to install the
[Extensibility Essentials](https://marketplace.visualstudio.com/items?itemName=MadsKristensen.ExtensibilityEssentials)
extension for Visual Studio which enables some features
used by this project.
You may also want to check awesome [Mads Kristensen guide](https://devblogs.microsoft.com/visualstudio/getting-started-writing-visual-studio-extensions/) 
