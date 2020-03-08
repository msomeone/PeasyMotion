// based on infobar demo (Utkarsh Shigihalli) - https://github.com/onlyutkarsh/InfoBarDemo
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
        public override InfoBarModel getInfoBarModel() {
            InfoBarTextSpan text = new InfoBarTextSpan(
                "PeasyMotion: New feature has been added! Text selection via jump. Give it a try via Tools.InvokePeasyMotionTextSelect command.");
            InfoBarHyperlink dismiss = new InfoBarHyperlink("Dismiss", "dismiss");
            InfoBarHyperlink moreInfo = new InfoBarHyperlink("More info", 
                "https://github.com/msomeone/PeasyMotion#text-selection-jump");
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
                    IVsWindowFrame ppFrame;
                    var service = Package.GetGlobalService(typeof(IVsWebBrowsingService)) as IVsWebBrowsingService;
                    service.Navigate(ctxStr, 0, out ppFrame);
                    PeasyMotionActivate.Instance?.Deactivate();
                }
                else
                {
                    Debug.WriteLine("You clicked ??????");
                }
            }
        }

        public void ShowInfoBar(InfoBarMdl ib)
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
            }
        }
    }
}
