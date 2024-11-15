﻿// based on infobar demo (Utkarsh Shigihalli) - https://github.com/onlyutkarsh/InfoBarDemo
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System.Diagnostics;

namespace PeasyMotion
{
    abstract class InfoBarMdl {
        public abstract InfoBarModel getInfoBarModel();
    }

    class WhatsNewNotification : InfoBarMdl
    {
        /* ~ 1.0.40
        public override InfoBarModel getInfoBarModel() {
            InfoBarTextSpan text = new InfoBarTextSpan(
                "PeasyMotion: New feature has been added! Text selection via jump. Give it a try via Tools.InvokePeasyMotionTextSelect command.");
            InfoBarHyperlink dismiss = new InfoBarHyperlink("Dismiss", "dismiss");
            InfoBarHyperlink moreInfo = new InfoBarHyperlink("More info",
                "http://github.com/msomeone/PeasyMotion#text-selection-via-toolsinvokepeasymotiontextselect-command");
            InfoBarTextSpan[] spans = new InfoBarTextSpan[] { text };
            InfoBarActionItem[] actions = new InfoBarActionItem[] { moreInfo, dismiss };
            InfoBarModel infoBarModel = new InfoBarModel(spans, actions, KnownMonikers.StatusInformation, isCloseButtonVisible: true);
            return infoBarModel;
        }
        */
        /* ~ 1.1.42
        public override InfoBarModel getInfoBarModel() {
            InfoBarTextSpan text = new InfoBarTextSpan(
                "PeasyMotion: New mode has been added! In-Line word jump (begin/end). Give it a try via Tools.PeasyMotionLineJumpToWordBegining or Tools.PeasyMotionLineJumpToWordEnding command.");
            InfoBarHyperlink dismiss = new InfoBarHyperlink("Dismiss", "dismiss");
            InfoBarHyperlink moreInfo = new InfoBarHyperlink("More info",
                "https://github.com/msomeone/PeasyMotion#jump-to-word-begining-or-ending-in-current-line");
            InfoBarTextSpan[] spans = new InfoBarTextSpan[] { text };
            InfoBarActionItem[] actions = new InfoBarActionItem[] { moreInfo, dismiss };
            InfoBarModel infoBarModel = new InfoBarModel(spans, actions, KnownMonikers.StatusInformation, isCloseButtonVisible: true);
            return infoBarModel;
        }
        */
        /* ~1.4.60
        public override InfoBarModel getInfoBarModel() {
            InfoBarTextSpan text = new InfoBarTextSpan(
                "PeasyMotion: New mode has been added! Jump to document tab. Give it a try via Tools.InvokePeasyMotionJumpToDocumentTab. New option: one can set allowed characters to be used in jump labels.");
            InfoBarHyperlink dismiss = new InfoBarHyperlink("Dismiss", "dismiss");
            InfoBarHyperlink moreInfo = new InfoBarHyperlink("More info",
                "https://github.com/msomeone/PeasyMotion#jump-to-document-tab");
            InfoBarTextSpan[] spans = new InfoBarTextSpan[] { text };
            InfoBarActionItem[] actions = new InfoBarActionItem[] { moreInfo, dismiss };
            InfoBarModel infoBarModel = new InfoBarModel(spans, actions, KnownMonikers.StatusInformation, isCloseButtonVisible: true);
            return infoBarModel;
        }
        */
        /*
        public override InfoBarModel getInfoBarModel() {
            InfoBarTextSpan text = new InfoBarTextSpan(
                "PeasyMotion: New mode has been added! Jump to line begining. Give it a try via Tools.InvokePeasyMotionJumpToLineBegining. Several bugfixes");
            InfoBarHyperlink dismiss = new InfoBarHyperlink("Dismiss", "dismiss");
            InfoBarHyperlink moreInfo = new InfoBarHyperlink("More info",
                "https://github.com/msomeone/PeasyMotion#jump-to-begining-of-line");
            InfoBarTextSpan[] spans = new InfoBarTextSpan[] { text };
            InfoBarActionItem[] actions = new InfoBarActionItem[] { moreInfo, dismiss };
            InfoBarModel infoBarModel = new InfoBarModel(spans, actions, KnownMonikers.StatusInformation, isCloseButtonVisible: true);
            return infoBarModel;
        }
        */
        /*
        public override InfoBarModel getInfoBarModel() {
            InfoBarTextSpan text = new InfoBarTextSpan("PeasyMotion: Two characted search mode has beed added! Give it a try via Tools.InvokePeasyMotionTwoCharJump. LineBeginingJump bug fix.");
            InfoBarHyperlink dismiss = new InfoBarHyperlink("Dismiss", "dismiss");
            InfoBarHyperlink moreInfo = new InfoBarHyperlink("More info",
                "https://github.com/msomeone/PeasyMotion#two-char-search");
            InfoBarTextSpan[] spans = new InfoBarTextSpan[] { text };
            InfoBarActionItem[] actions = new InfoBarActionItem[] { moreInfo, dismiss };
            InfoBarModel infoBarModel = new InfoBarModel(spans, actions, KnownMonikers.StatusInformation, isCloseButtonVisible: true);
            return infoBarModel;
        }*/
        /*
        public override InfoBarModel getInfoBarModel() {
            InfoBarTextSpan text = new InfoBarTextSpan("PeasyMotion: 2022 - Visual Studio 2022 support is here! Link is in 'more info' section");
            InfoBarHyperlink dismiss = new InfoBarHyperlink("Dismiss", "dismiss");
            InfoBarHyperlink moreInfo = new InfoBarHyperlink("More info",
                "https://marketplace.visualstudio.com/items?itemName=maksim-vorobiev.PeasyMotion2022");
            InfoBarTextSpan[] spans = new InfoBarTextSpan[] { text };
            InfoBarActionItem[] actions = new InfoBarActionItem[] { moreInfo, dismiss };
            InfoBarModel infoBarModel = new InfoBarModel(spans, actions, KnownMonikers.StatusInformation, isCloseButtonVisible: true);
            return infoBarModel;
        }*/
        public override InfoBarModel getInfoBarModel() {
            InfoBarTextSpan text = new InfoBarTextSpan("PeasyMotion: ARM64 support");
            InfoBarHyperlink dismiss = new InfoBarHyperlink("Dismiss", "dismiss");
            InfoBarHyperlink moreInfo = new InfoBarHyperlink("More info",
                "https://github.com/msomeone/PeasyMotion");
            InfoBarTextSpan[] spans = new InfoBarTextSpan[] { text };
            InfoBarActionItem[] actions = new InfoBarActionItem[] { moreInfo, dismiss };
            InfoBarModel infoBarModel = new InfoBarModel(spans, actions, KnownMonikers.StatusInformation, isCloseButtonVisible: true);
            return infoBarModel;
        }
    };

    class InfoBarService : IVsInfoBarUIEvents
    {
        private readonly IServiceProvider _serviceProvider;
        private uint _cookie;
        private IVsInfoBarUIElement _element = null;
        private Action _onInfoBarCloseOrDismissAction = null;

        public bool anyInfoBarActive() {
            return null != _element;
        }

        private InfoBarService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public static InfoBarService Instance { get; private set; }

        public static void Initialize(IServiceProvider serviceProvider)
        {
            Instance = new InfoBarService(serviceProvider);
        }

        public void OnClosed(IVsInfoBarUIElement infoBarUIElement)
        {
            infoBarUIElement.Unadvise(_cookie);
        }

        public void OnActionItemClicked(IVsInfoBarUIElement infoBarUIElement, IVsInfoBarActionItem actionItem)
        {
            string ctxStr = actionItem.ActionContext as string;
            if (ctxStr != null)
            {
                if (ctxStr == "dismiss") {
                    CloseInfoBar();
                    Debug.WriteLine("You clicked dismiss!");
                }
                else if (ctxStr.StartsWith("http://") || ctxStr.StartsWith("https://"))
                {
                    PeasyMotionActivate.Instance?.Deactivate();
                    IVsWindowFrame ppFrame;
                    var service = Package.GetGlobalService(typeof(IVsWebBrowsingService)) as IVsWebBrowsingService;
                    service.Navigate(ctxStr, 0, out ppFrame);
                }
                else
                {
                    Debug.WriteLine("You clicked ??????");
                }
            }
        }

        public void ShowInfoBar(InfoBarMdl ib, Action onInfoBarClosedOrDismissed)
        {
            var shell = _serviceProvider.GetService(typeof(SVsShell)) as IVsShell;
            if (shell != null)
            {
                shell.GetProperty((int) __VSSPROPID7.VSSPROPID_MainWindowInfoBarHost, out var obj);
                var host = (IVsInfoBarHost)obj;
                if (host == null) {
                    return;
                }

                var factory = _serviceProvider.GetService(typeof(SVsInfoBarUIFactory)) as IVsInfoBarUIFactory;
                _element = factory.CreateInfoBar(ib.getInfoBarModel());
                _element.Advise(this, out _cookie);
                host.AddInfoBar(_element);
                _onInfoBarCloseOrDismissAction = onInfoBarClosedOrDismissed;
            }
        }

        public void CloseInfoBar()
        {
            if (_element != null)
            {
                var shell = _serviceProvider.GetService(typeof(SVsShell)) as IVsShell;
                if (shell != null)
                {
                    shell.GetProperty((int)__VSSPROPID7.VSSPROPID_MainWindowInfoBarHost, out var obj);
                    var host = (IVsInfoBarHost)obj;
                    if (host == null) {
                        return;
                    }
                    _element.Close();
                    host.RemoveInfoBar(_element);
                    _element = null;

                }
                if (_onInfoBarCloseOrDismissAction != null) {
                    _onInfoBarCloseOrDismissAction();
                }
            }
        }
    }
}
