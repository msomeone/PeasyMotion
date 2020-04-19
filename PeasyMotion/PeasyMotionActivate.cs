//#define MEASUREEXECTIME

using System.Drawing;
using System;
using System.ComponentModel.Composition;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using EnvDTE;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio;
//using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.TextManager.Interop;
using Task = System.Threading.Tasks.Task;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Formatting;
using Microsoft.VisualStudio.Imaging;

namespace PeasyMotion
{
    public class CommandExecutorService
    {
        readonly DTE _dte;

        public CommandExecutorService()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _dte = Package.GetGlobalService(typeof(SDTE)) as DTE;
        }

        public bool IsCommandAvailable(string commandName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return FindCommand(_dte.Commands, commandName) != null;
        }

        public void Execute(string commandName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _dte.ExecuteCommand(commandName);
        }

        private static dynamic FindCommand(Commands commands, string commandName)
        {
            foreach (var command in commands)
            {
                if (((dynamic)command).Name == commandName)
                {
                    return command;
                }
            }
            return null;
        }
    }
    /// <summary>
    /// Command handler
    /// </summary>
    [Export]
    internal sealed class PeasyMotionActivate
    {
        /// <summary>
        /// Command menu group (command set GUID).
        /// </summary>
        public static readonly Guid CommandSet = new Guid("921fde78-c60b-4458-af50-fbb52d4b6a63");

        /// <summary>
        /// VS Package that provides this command, not null.
        /// </summary>
        private AsyncPackage pkg = null;

        private PeasyMotionEdAdornment adornmentMgr = null;

        private IVsTextManager textMgr = null;
        private IVsEditorAdaptersFactoryService editor = null;
        private OleMenuCommandService commandService = null;

        private InputListener inputListener = null;
        private string accumulatedKeyChars = null;

        private const string VsVimSetDisabled = "VsVim.SetDisabled";
        private const string VsVimSetEnabled = "VsVim.SetEnabled";
        private static bool disableVsVimCmdAvailable = false;
        private static bool enableVsVimCmdAvailable = false;
        private CommandExecutorService cmdExec = null;

        private static string ViEmuEnableDisableCommand = "ViEmu.EnableDisableViEmu";
        private static bool viEmuPluginPresent = false;

        private JumpMode currentMode = JumpMode.InvalidMode;

        /// <summary>
        /// Initializes a new instance of the <see cref="PeasyMotionActivate"/> class.
        /// Adds our command handlers for menu (commands must exist in the command table file)
        /// </summary>
        private PeasyMotionActivate()
        {
        }

        private static System.Drawing.Color ConvertDTEColor(uint oleColor)
        {
            var sdColor = System.Drawing.ColorTranslator.FromOle((int)oleColor);
            return System.Drawing.Color.FromArgb(sdColor.A, sdColor.R, sdColor.G, sdColor.B);
        }

        public void Init()
        {
            {
                var A = new EditorHostFactory();
                var B = A.CreateCompositionContainer();
                IEditorFormatMapService efms = B.GetExportedValue<IEditorFormatMapService>();
                VsSettings.Initialize(this.pkg, efms);
            }

            CreateMenu();
            cmdExec = new CommandExecutorService() {};
            disableVsVimCmdAvailable = cmdExec.IsCommandAvailable(VsVimSetDisabled);
            enableVsVimCmdAvailable = cmdExec.IsCommandAvailable(VsVimSetEnabled);
            viEmuPluginPresent = cmdExec.IsCommandAvailable(ViEmuEnableDisableCommand);
            JumpLabelUserControl.WarmupCache();
        }

        private void CreateMenu() {
            var wordJumpMenuCommandID = new CommandID(PeasyMotion.PackageGuids.guidPeasyMotionPackageCmdSet, 
                PeasyMotion.PackageIds.PeasyMotionActivateId);
            var wordJumpMenuItem = new MenuCommand(this.ExecuteWordJump, wordJumpMenuCommandID);
            commandService.AddCommand(wordJumpMenuItem);

            var selectionWordJumpMenuCommandID = new CommandID(PeasyMotion.PackageGuids.guidPeasyMotionPackageCmdSet, 
                PeasyMotion.PackageIds.PeasyMotionSelectTextActivateId);
            var selectionWordJumpMenuItem = new MenuCommand(this.ExecuteSelectTextWordJump, selectionWordJumpMenuCommandID);
            commandService.AddCommand(selectionWordJumpMenuItem);

            var lineJumpToWordBeginingMenuCommandID = new CommandID(PeasyMotion.PackageGuids.guidPeasyMotionPackageCmdSet, 
                PeasyMotion.PackageIds.PeasyMotionLineJumpToWordBeginingId);
            var lineJumpToWordBeginingMenuItem = new MenuCommand(this.ExecuteLineJumpToWordBegining, lineJumpToWordBeginingMenuCommandID);
            commandService.AddCommand(lineJumpToWordBeginingMenuItem);

            var lineJumpToWordEndingMenuCommandID = new CommandID(PeasyMotion.PackageGuids.guidPeasyMotionPackageCmdSet, 
                PeasyMotion.PackageIds.PeasyMotionLineJumpToWordEndingId);
            var lineJumpToWordEndingMenuItem = new MenuCommand(this.ExecuteLineJumpToWordEnding, lineJumpToWordEndingMenuCommandID);
            commandService.AddCommand(lineJumpToWordEndingMenuItem);
        }

        /// <summary>
        /// Gets the instance of the command.
        /// </summary>
        public static PeasyMotionActivate Instance
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the service provider from the owner package.
        /// </summary>
        private Microsoft.VisualStudio.Shell.IAsyncServiceProvider ServiceProvider
        {
            get
            {
                return this.pkg;
            }
        }

        public ITextSearchService textSearchService { get; set; }

        public ITextStructureNavigatorSelectorService textStructureNavigatorSelector { get; set; }


        /// <summary>
        /// Initializes the singleton instance of the command.
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        ///
        private static void ThrowAndLog(string msg)
        {
            Debug.Fail(msg);
            throw new Exception(msg);
        }

        public static async Task InitializeAsync(AsyncPackage package)
        {
            // Switch to the main thread - the call to AddCommand in PeasyMotionActivate's constructor requires
            // the UI thread + we gona check if VsVim commands are available
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            OleMenuCommandService commandService_ = await package.GetServiceAsync(typeof(IMenuCommandService)).ConfigureAwait(true) as OleMenuCommandService;

            IVsTextManager textManager = await package.GetServiceAsync(typeof(SVsTextManager), false).ConfigureAwait(true) as IVsTextManager;
            if (null == textManager) {
                ThrowAndLog(nameof(package) + ": failed to retrieve SVsTextManager");
            }

            IComponentModel componentModel = await package.GetServiceAsync(typeof(SComponentModel)).ConfigureAwait(true) as IComponentModel;
            if (componentModel == null) 
            {
                ThrowAndLog(nameof(package) + ": failed to retrieve SComponentModel");
            }

            IVsEditorAdaptersFactoryService editor_ = componentModel.GetService<IVsEditorAdaptersFactoryService>();
            if (editor_ == null) 
            {
                ThrowAndLog(nameof(package) + ": failed to retrieve IVsEditorAdaptersFactoryService");
            }

            ITextSearchService textSearchService_ = componentModel.GetService<ITextSearchService>();
            if (textSearchService_ == null)
            {
                ThrowAndLog(nameof(package) + ": failed to retrieve ITextSearchService");
            }

            ITextStructureNavigatorSelectorService textStructureNavigatorSelector_ = componentModel.GetService<ITextStructureNavigatorSelectorService>();
            if (textStructureNavigatorSelector_ == null)
            {
                ThrowAndLog(nameof(package) + ": failed to retrieve ITextStructureNavigatorSelectorService");
            }

            InfoBarService.Initialize(package);

            Instance = new PeasyMotionActivate()
            {
                pkg = package,
                commandService = commandService_,
                textMgr = textManager,
                editor = editor_,
                textSearchService = textSearchService_,
                textStructureNavigatorSelector = textStructureNavigatorSelector_,
            };

            Instance.Init();
        }

        private void ExecuteWordJump(object o, EventArgs e)
        {
            currentMode = JumpMode.WordJump;
            ExecuteCommonJumpCode();
        }
        
        private void ExecuteSelectTextWordJump(object o, EventArgs e)
        {
            currentMode = JumpMode.SelectTextJump;
            ExecuteCommonJumpCode();
        }

        private void ExecuteLineJumpToWordBegining(object o, EventArgs e)
        {
            currentMode = JumpMode.LineJumpToWordBegining;
            ExecuteCommonJumpCode();
        }       

        private void ExecuteLineJumpToWordEnding(object o, EventArgs e)
        {
            currentMode = JumpMode.LineJumpToWordEnding;
            ExecuteCommonJumpCode();
        }       

        private void ShowNotificationsIfAny() 
        {
            var pkgVersion = System.Version.Parse(GeneralOptions.getCurrentVersion());
            System.Version cfgPkgVersion;
            try {
                cfgPkgVersion = System.Version.Parse(GeneralOptions.Instance.getInstalledVersionStr());
            } catch(Exception ex){
                Trace.Write($"Failed to parse package version stored(if there was any) in options registry! Exception: {ex.ToString()}");
                cfgPkgVersion = pkgVersion; //System.Version.Parse("0.0.0"); //TODO!!!! for release - change to =pkgVersion!! No notification is needed for 'whats new'
            } 
            Debug.WriteLine($"cfgPkgVersion = {cfgPkgVersion} | pkgVersion = {pkgVersion}");
            if (!InfoBarService.Instance.anyInfoBarActive() && (pkgVersion > cfgPkgVersion))  {
                InfoBarService.Instance.ShowInfoBar(new WhatsNewNotification(), 
                    new Action( () => { // in case info bar is closed propeprly, stop showing notification
                        GeneralOptions.Instance.setInstalledVersionToCurrentPkgVersion();
                        GeneralOptions.Instance.Save();
                    })
                );
            }
        }

        private void ExecuteCommonJumpCode()
        {
            ShowNotificationsIfAny();

            #if MEASUREEXECTIME
            var watch = System.Diagnostics.Stopwatch.StartNew();
            #endif
            textMgr.GetActiveView(1, null, out IVsTextView vsTextView);
            if (vsTextView == null) {
                Debug.Fail("MenuItemCallback: could not retrieve current view");
                return;
            }
            IWpfTextView wpfTextView = editor.GetWpfTextView(vsTextView);
            if (wpfTextView == null) {
                Debug.Fail("failed to retrieve current view");
                return;
            }

            #if MEASUREEXECTIME
            var watch3 = System.Diagnostics.Stopwatch.StartNew();
            #endif
            if (adornmentMgr != null) {
                Deactivate();
            }
            #if MEASUREEXECTIME
            watch3.Stop();
            Debug.WriteLine($"PeasyMotion Deactivate(): {watch3.ElapsedMilliseconds} ms");
            #endif

            #if MEASUREEXECTIME
            var watch2 = System.Diagnostics.Stopwatch.StartNew();
            #endif
            TryDisableVsVim();
            #if MEASUREEXECTIME
            watch2.Stop();
            Debug.WriteLine($"PeasyMotion TryDisableVsVim: {watch2.ElapsedMilliseconds} ms");
            #endif

            #if MEASUREEXECTIME
            var watch7 = System.Diagnostics.Stopwatch.StartNew();
            #endif
            TryDisableViEmu();
            #if MEASUREEXECTIME
            watch7.Stop();
            Debug.WriteLine($"PeasyMotion TryDisableViEmu: {watch7.ElapsedMilliseconds} ms");
            #endif

            ITextStructureNavigator textStructNav = this.textStructureNavigatorSelector.GetTextStructureNavigator(wpfTextView.TextBuffer);

            adornmentMgr = new PeasyMotionEdAdornment(wpfTextView, textStructNav, currentMode);

            ThreadHelper.ThrowIfNotOnUIThread();
            CreateInputListener(vsTextView, wpfTextView);

            #if MEASUREEXECTIME
            watch.Stop();
            Debug.WriteLine($"PeasyMotion FullExecTime: {watch.ElapsedMilliseconds} ms");
            #endif

            if (!adornmentMgr.anyJumpsAvailable()) { // empty text? no jump labels
                Deactivate();
            }
        }
        private void TryDisableVsVim()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (disableVsVimCmdAvailable)
            {
                cmdExec.Execute(VsVimSetDisabled);
            }
        }

        private void TryEnableVsVim()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (enableVsVimCmdAvailable)
            {
                cmdExec.Execute(VsVimSetEnabled);
            }
        }

        private void TryToggleViEmu() {
            if (viEmuPluginPresent) {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                cmdExec.Execute(ViEmuEnableDisableCommand);
                watch.Stop();
                Debug.WriteLine($"PeasyMotion ViEmuEnableDisableCommand exec took: {watch.ElapsedMilliseconds} ms");
            }
        }
        private void TryDisableViEmu() => TryToggleViEmu(); 

        private void TryEnableViEmu() => TryToggleViEmu();

        private void CreateInputListener(IVsTextView view, IWpfTextView textView)
        {
            inputListener = new InputListener(view, textView) { };
            inputListener.AddFilter();
            inputListener.KeyPressed += InputListenerOnKeyPressed;
            accumulatedKeyChars = null;
        }

        private void InputListenerOnKeyPressed(object sender, KeyPressEventArgs keyPressEventArgs)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try {
                Debug.WriteLine("Key pressed " + keyPressEventArgs.KeyChar);

                if (keyPressEventArgs.KeyChar != '\0')
                {
                    if (null == accumulatedKeyChars)
                    {
                        accumulatedKeyChars = new string(keyPressEventArgs.KeyChar, 1);
                    }
                    else
                    {
                        accumulatedKeyChars += keyPressEventArgs.KeyChar;
                    }
                    JumpToResult jtr = adornmentMgr.JumpTo(accumulatedKeyChars);
                    if (null != jtr) // this was final jump char
                    {
                        var wpfTextView = adornmentMgr.view;
                        var jumpMode = this.currentMode;
                        var labelSnapshotSpan = new SnapshotSpan(wpfTextView.TextSnapshot,jtr.jumpLabelSpan);
                        Deactivate();

                        switch(jumpMode) {
                        case JumpMode.InvalidMode:
                            Debug.WriteLine("PeasyMotion: OOOPS! JumpMode logic is broken!");
                            break;
                        case JumpMode.WordJump:
                        case JumpMode.LineJumpToWordBegining:
                        case JumpMode.LineJumpToWordEnding:
                            { // move caret to label
                                wpfTextView.Caret.MoveTo(labelSnapshotSpan.Start);
                            }
                            break;
                        case JumpMode.SelectTextJump:
                            { // select text, and move caret to selection end label
                                int c = jtr.currentCursorPosition; 
                                int s = jtr.jumpLabelSpan.Start;
                                int e = jtr.jumpLabelSpan.End;
                                var selectionSpan = c < s ? Span.FromBounds(c,s) : Span.FromBounds(s,c);
                                // 1. select
                                wpfTextView.Selection.Select(new SnapshotSpan(wpfTextView.TextSnapshot,selectionSpan), c > e);
                                // 2. move
                                wpfTextView.Caret.MoveTo(labelSnapshotSpan.Start);
                            }
                            break;
                        }
                    }
                }
                else
                {
                    Deactivate();
                }
            } catch (ArgumentException ex) { // happens sometimes: "The supplied SnapshotPoint is on an incorrect snapshot."
                Trace.Write(ex.ToString());
                System.Diagnostics.Debug.Write(ex.ToString());
            }
        }

        public void Deactivate()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            StopListening2Keyboard();
            TryEnableVsVim();
            TryEnableViEmu();
            adornmentMgr?.Reset();
            adornmentMgr = null;
            currentMode = JumpMode.InvalidMode;
        }
        private void StopListening2Keyboard()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (null != inputListener) {
                inputListener.KeyPressed -= InputListenerOnKeyPressed;
                inputListener.RemoveFilter();
                inputListener = null;
            }
        }

    }
}
