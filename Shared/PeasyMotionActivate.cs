//#define MEASUREEXECTIME

using System;
using System.Drawing;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio;
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

        private InputListener inputListenerUserQueryPhase = null;
        private string userQueryAccumulatedKeyChars = null;


        private const string VsVimSetDisabled = "VsVim.SetDisabled";
        private const string VsVimSetEnabled = "VsVim.SetEnabled";
        private static bool disableVsVimCmdAvailable = false;
        private static bool enableVsVimCmdAvailable = false;
        private CommandExecutorService cmdExec = null;
        public IVsUIShell iVsUiShell = null;
        public IVsUIShell4 iVsUiShell4 = null;

        private static string ViEmuEnableDisableCommand = "ViEmu.EnableDisableViEmu";
        private static bool viEmuPluginPresent = false;

        private JumpMode activeJumpMode = JumpMode.InvalidMode;

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
                if (!VsSettings.IsInitialized) {
                    VsSettings.Initialize(this.pkg, efms);
                }
            }

            CreateMenu();
            cmdExec = new CommandExecutorService() {};
            disableVsVimCmdAvailable = cmdExec.IsCommandAvailable(VsVimSetDisabled);
            enableVsVimCmdAvailable = cmdExec.IsCommandAvailable(VsVimSetEnabled);
            viEmuPluginPresent = cmdExec.IsCommandAvailable(ViEmuEnableDisableCommand);
            iVsUiShell = Package.GetGlobalService(typeof(SVsUIShell)) as IVsUIShell;
            iVsUiShell4 = iVsUiShell as IVsUIShell4;
            JumpLabelUserControl.WarmupCache();
            // warmup options
            GeneralOptions.Instance.caretPositionSensivity = GeneralOptions.Instance.caretPositionSensivity;
            // warmp up:
#if MEASUREEXECTIME
            var watch2_0 = System.Diagnostics.Stopwatch.StartNew();
#endif
            var wfs = iVsUiShell.GetDocumentWindowFrames().GetValueOrDefault();
            if (wfs.Count > 0) {
                Trace.WriteLine("GetDocumentWindowFrames warmed up");
                foreach(var wf in wfs) {
                    wf.GetProperty((int)VsFramePropID.Caption, out var oce);
                    wf.SetProperty((int)VsFramePropID.Caption, (string)oce);
                }
            }
#if MEASUREEXECTIME
                watch2_0.Stop();
                Trace.WriteLine($"PeasyMotion Adornment warmup document tabs: {watch2_0.ElapsedMilliseconds} ms");
#endif
        }

        private void CreateMenu() {
            // one day this copy-pasta will exceed one page of code... and then! and only than we gona do smth about that :}
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

            var jumpToDocTabMenuCommandID = new CommandID(PeasyMotion.PackageGuids.guidPeasyMotionPackageCmdSet,
                PeasyMotion.PackageIds.PeasyMotionJumpToDocumentTab);
            var jumpToDocTabMenuItem = new MenuCommand(this.ExecuteJumpToDocTab, jumpToDocTabMenuCommandID);
            commandService.AddCommand(jumpToDocTabMenuItem);

            var jumpToLineBeginingMenuCommandID = new CommandID(PeasyMotion.PackageGuids.guidPeasyMotionPackageCmdSet,
                PeasyMotion.PackageIds.PeasyMotionJumpToLineBegining);
            var jumpToLineBeginingMenuItem = new MenuCommand(this.ExecuteJumpToLineBegining, jumpToLineBeginingMenuCommandID);
            commandService.AddCommand(jumpToLineBeginingMenuItem);

            var twoCharJumpMenuCommandID = new CommandID(PeasyMotion.PackageGuids.guidPeasyMotionPackageCmdSet,
                PeasyMotion.PackageIds.PeasyMotionTwoCharJump);
            var twoCharJumphMenuItem = new MenuCommand(this.ExecuteTwoCharJump, twoCharJumpMenuCommandID);
            commandService.AddCommand(twoCharJumphMenuItem);

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
        public IVsStatusbar statusBar { get; set; }

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


            IVsStatusbar statusBar = await package.GetServiceAsync(typeof(SVsStatusbar)).ConfigureAwait(true) as IVsStatusbar;

            InfoBarService.Initialize(package);

            Instance = new PeasyMotionActivate()
            {
                pkg = package,
                commandService = commandService_,
                textMgr = textManager,
                editor = editor_,
                textSearchService = textSearchService_,
                statusBar = statusBar,
                textStructureNavigatorSelector = textStructureNavigatorSelector_,
            };

            Instance.Init();
        }

        private void ExecuteWordJump(object o, EventArgs e)
        {
            activeJumpMode = JumpMode.WordJump;
            ExecuteCommonJumpCode();
        }

        private void ExecuteSelectTextWordJump(object o, EventArgs e)
        {
            activeJumpMode = JumpMode.SelectTextJump;
            ExecuteCommonJumpCode();
        }

        private void ExecuteLineJumpToWordBegining(object o, EventArgs e)
        {
            activeJumpMode = JumpMode.LineJumpToWordBegining;
            ExecuteCommonJumpCode();
        }

        private void ExecuteLineJumpToWordEnding(object o, EventArgs e)
        {
            activeJumpMode = JumpMode.LineJumpToWordEnding;
            ExecuteCommonJumpCode();
        }

        private void ExecuteJumpToDocTab(object o, EventArgs e)
        {
            activeJumpMode = JumpMode.VisibleDocuments;
            ExecuteCommonJumpCode();
        }

        private void ExecuteJumpToLineBegining(object o, EventArgs e)
        {
            activeJumpMode = JumpMode.LineBeginingJump;
            ExecuteCommonJumpCode();
        }

        private void ExecuteTwoCharJump(object o, EventArgs e)
        {
            ShowNotificationsIfAny();

            activeJumpMode = JumpMode.TwoCharJump;

            textMgr.GetActiveView(1, null, out IVsTextView vsTextView);
            if (vsTextView == null) { Debug.Fail("MenuItemCallback: could not retrieve current view"); return; }

            IWpfTextView wpfTextView = editor.GetWpfTextView(vsTextView);
            if (wpfTextView == null) { Debug.Fail("failed to retrieve current view"); return; }

            #if MEASUREEXECTIME
            var watch2 = System.Diagnostics.Stopwatch.StartNew();
            #endif
            TryDisableVsVim();
            #if MEASUREEXECTIME
            watch2.Stop();
            Trace.WriteLine($"PeasyMotion ExecuteTwoCharJump - TryDisableVsVim: {watch2.ElapsedMilliseconds} ms");
            #endif

            #if MEASUREEXECTIME
            var watch7 = System.Diagnostics.Stopwatch.StartNew();
            #endif
            TryDisableViEmu();
            #if MEASUREEXECTIME
            watch7.Stop();
            Trace.WriteLine($"PeasyMotion ExecuteTwoCharJump - TryDisableViEmu: {watch7.ElapsedMilliseconds} ms");
            #endif

            setStatusBarText("Two character jump activated. Waiting for two keys to execute search >");

            ThreadHelper.ThrowIfNotOnUIThread();
            CreateInputListenerUserQueryPhase(vsTextView, wpfTextView);
            wpfTextView.LostAggregateFocus += OnTextViewFocusLost;
        }

        private void unfreezeStatusBar() {
            int frozen; statusBar.IsFrozen(out frozen);
            if (frozen != 0) {// Make sure the status bar is not frozen
                statusBar.FreezeOutput(0);
            }
        }

        private void freezeStatusBar() {
            int frozen; statusBar.IsFrozen(out frozen);
            if (frozen == 0) {// Make sure the status bar is not frozen
                statusBar.FreezeOutput(1);
            }
        }
        private void setStatusBarText(string statusBarText)
        {
            unfreezeStatusBar();
            statusBar.SetText($"| PeasyMotion |> {statusBarText}"); // Set the status bar text and make its display static.
            //statusBar.FreezeOutput(1); // Freeze the status bar.
            //string text; statusBar.GetText(out text); // Get the status bar text.
            //Debug.WriteLine($"Status bar text = {text}");
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
            Trace.WriteLine($"PeasyMotion Deactivate(): {watch3.ElapsedMilliseconds} ms");
            #endif

            #if MEASUREEXECTIME
            var watch2 = System.Diagnostics.Stopwatch.StartNew();
            #endif
            TryDisableVsVim();
            #if MEASUREEXECTIME
            watch2.Stop();
            Trace.WriteLine($"PeasyMotion TryDisableVsVim: {watch2.ElapsedMilliseconds} ms");
            #endif

            #if MEASUREEXECTIME
            var watch7 = System.Diagnostics.Stopwatch.StartNew();
            #endif
            TryDisableViEmu();
            #if MEASUREEXECTIME
            watch7.Stop();
            Trace.WriteLine($"PeasyMotion TryDisableViEmu: {watch7.ElapsedMilliseconds} ms");
            #endif

            ITextStructureNavigator textStructNav = this.textStructureNavigatorSelector.GetTextStructureNavigator(wpfTextView.TextBuffer);

            var args = new PeasyMotionEdAdornmentCtorArgs{
                vsTextView = vsTextView,
                wpfView = wpfTextView,
                textStructNav = textStructNav,
                jumpMode = activeJumpMode,
                twoCharSearchJumpKeys = this.userQueryAccumulatedKeyChars?.ToLowerInvariant()
            };
            adornmentMgr = new PeasyMotionEdAdornment(args);

            ThreadHelper.ThrowIfNotOnUIThread();
            CreateInputListener(vsTextView, wpfTextView);
            wpfTextView.LostAggregateFocus += OnTextViewFocusLost;

            #if MEASUREEXECTIME
            watch.Stop();
            Trace.WriteLine($"PeasyMotion FullExecTime: {watch.ElapsedMilliseconds} ms");
            #endif

            if (!adornmentMgr.anyJumpsAvailable()) { // empty text? no jump labels
                Deactivate();
            }
        }

        private void OnTextViewFocusLost(object sender, EventArgs e) {
            if ((adornmentMgr != null) || (inputListenerUserQueryPhase != null)) {
                this.Deactivate();
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
            setStatusBarText($"Waiting for keys to execute jump >");
        }

        private void CreateInputListenerUserQueryPhase(IVsTextView view, IWpfTextView textView)
        {
            inputListenerUserQueryPhase = new InputListener(view, textView) { };
            inputListenerUserQueryPhase.AddFilter();
            inputListenerUserQueryPhase.KeyPressed += InputListenerOnKeyPressedCharAccumulation;
            userQueryAccumulatedKeyChars = null;
        }

        // separate listener, used to accumulate keys for twochar jump mode
        private void InputListenerOnKeyPressedCharAccumulation(object sender, KeyPressEventArgs keyPressEventArgs)
        {
            //TODO: if we decide to use this InputListenerOnKeyPressedCharAccumulation for other modes (not only for a TwoCharJump mode)
            //      we must consider current mode inside this function! and current mode is not storead anywhere, except adornmentMgr

            ThreadHelper.ThrowIfNotOnUIThread();
            if (adornmentMgr != null) {
                Trace.WriteLine("PeasyMotion: InputListenerOnKeyPressedCharAccumulation - adornmentMgr is not null!!! LOGIC ERROR!!!");
            }
            try {
                var ch = keyPressEventArgs.KeyChar;
                Debug.WriteLine("Key pressed " + ch);
                if (Char.IsSeparator(ch) || Char.IsPunctuation(ch) || Char.IsLetterOrDigit(ch))
                {
                    if (null == userQueryAccumulatedKeyChars)
                    {
                        userQueryAccumulatedKeyChars = new string(keyPressEventArgs.KeyChar, 1);
                    }
                    else
                    {
                        userQueryAccumulatedKeyChars += keyPressEventArgs.KeyChar;
                    }
                    setStatusBarText($"Keys pressed: {userQueryAccumulatedKeyChars}");
                    if (activeJumpMode == JumpMode.TwoCharJump)
                    {
                        if (userQueryAccumulatedKeyChars.Length == 2) {
                            var wpfTextView = inputListenerUserQueryPhase.textView as IWpfTextView;
                            if (null != wpfTextView) {
                                wpfTextView.LostAggregateFocus -= OnTextViewFocusLost;
                            }
                            StopListening2Keyboard(); // kill user key query accumulator / listener
                            ExecuteCommonJumpCode(); // start regular jumping code
                        }
                    }
                } else {
                    Deactivate();
                }
            } catch (ArgumentException ex) { // happens sometimes: "The supplied SnapshotPoint is on an incorrect snapshot."
                Trace.Write(ex.ToString());
                System.Diagnostics.Debug.Write(ex.ToString());
            }
        }

        private void InputListenerOnKeyPressed(object sender, KeyPressEventArgs keyPressEventArgs)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (adornmentMgr == null) {
                //Trace.WriteLine("PeasyMotion: InputListenerOnKeyPressed - adornmentMgr is null!");
            //    Deactivate();
            }
            try {
                //Debug.WriteLine("Key pressed " + keyPressEventArgs.KeyChar);
                setStatusBarText($"Keys pressed: {accumulatedKeyChars}");
                if (adornmentMgr.jumpLabelKeyArray.IndexOf(keyPressEventArgs.KeyChar) != -1)
                {
                    if (null == accumulatedKeyChars)
                    {
                        accumulatedKeyChars = new string(keyPressEventArgs.KeyChar, 1);
                    }
                    else
                    {
                        accumulatedKeyChars += keyPressEventArgs.KeyChar;
                    }

                    if (adornmentMgr.JumpTo(accumulatedKeyChars, out var jumpToResult)) // this was final jump char
                    {
                        var wpfTextView = adornmentMgr.view;
                        var jumpMode = adornmentMgr.CurrentJumpMode;
                        Deactivate();
                        if (jumpMode != JumpMode.VisibleDocuments)
                        {
                            //adornmentMgr.TraceLine("before labelSnapshotSpan");
                            var labelSnapshotSpan = new SnapshotSpan(wpfTextView.TextSnapshot, jumpToResult.jumpLabelSpan);

                            switch (jumpMode) {
                            case JumpMode.InvalidMode:
                                Trace.WriteLine("PeasyMotion: OOOPS! JumpMode logic is broken!");
                                break;
                            case JumpMode.WordJump:
                            case JumpMode.LineJumpToWordBegining:
                            case JumpMode.LineJumpToWordEnding:
                            case JumpMode.LineBeginingJump:
                            case JumpMode.TwoCharJump:
                            { // move caret to label
                                wpfTextView.Caret.MoveTo(labelSnapshotSpan.Start);
                            }
                            break;
                            case JumpMode.SelectTextJump:
                            { // select text, and move caret to selection end label
                                int c = jumpToResult.currentCursorPosition;
                                int s = jumpToResult.jumpLabelSpan.Start;
                                int e = jumpToResult.jumpLabelSpan.End;
                                var selectionSpan = c < s ? Span.FromBounds(c, s) : Span.FromBounds(s, c);
                                // 1. select
                                wpfTextView.Selection.Select(new SnapshotSpan(wpfTextView.TextSnapshot, selectionSpan), c > e);
                                // 2. move
                                wpfTextView.Caret.MoveTo(labelSnapshotSpan.Start);
                            }
                            break;
                            }

                            //adornmentMgr.TraceLine("after switch labelSnapshotSpan");
                        }
                        else if (jumpMode == JumpMode.VisibleDocuments) {
                            jumpToResult.windowFrame.Show();
                        }
                    } else if (adornmentMgr.NoLabelsLeft()){ // in case wrong (not used in active labels) key was pressed and we ran out of labels
                        Deactivate();
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
            if (adornmentMgr != null) {
                adornmentMgr.view.LostAggregateFocus -= OnTextViewFocusLost;
            }
            StopListening2Keyboard();
            TryEnableVsVim();
            TryEnableViEmu();
            adornmentMgr?.Reset();
            adornmentMgr = null;
            activeJumpMode = JumpMode.InvalidMode;
            statusBar.Clear();
        }

        private void StopListening2Keyboard()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (null != inputListener) {
                inputListener.KeyPressed -= InputListenerOnKeyPressed;
                inputListener.RemoveFilter();
                inputListener = null;
            }
            if (null != inputListenerUserQueryPhase) {
                inputListenerUserQueryPhase.KeyPressed -= InputListenerOnKeyPressedCharAccumulation;
                inputListenerUserQueryPhase.RemoveFilter();
                inputListenerUserQueryPhase = null;
            }
        }

    }
}

//sanbox:
#if false
            for (int i = 0; i < 256; i++) {
                Debug.WriteLine("Char.IsSeparator(" + ((char)i) + " = " + Char.IsLowSurrogate((char)i));
                Debug.WriteLine("Char.IsControl(" + ((char)i) + " = " + Char.IsControl((char)i));
                Debug.WriteLine("Char.IsDigit(" + ((char)i) + " = " + Char.IsDigit((char)i));
                Debug.WriteLine("Char.IsHighSurrogate(" + ((char)i) + " = " + Char.IsHighSurrogate((char)i));
                Debug.WriteLine("Char.IsLetterOrDigit(" + ((char)i) + " = " + Char.IsLetterOrDigit((char)i));
                Debug.WriteLine("Char.IsLowSurrogate(" + ((char)i) + " = " + Char.IsLowSurrogate((char)i));
                Debug.WriteLine("Char.IsNumber(" + ((char)i) + " = " + Char.IsNumber((char)i));
                Debug.WriteLine("Char.IsPunctuation(" + ((char)i) + " = " + Char.IsPunctuation((char)i));
                Debug.WriteLine("Char.IsSeparator(" + ((char)i) + " = " + Char.IsSeparator((char)i));
                Debug.WriteLine("Char.IsSymbol(" + ((char)i) + " = " + Char.IsSymbol((char)i));
                Debug.WriteLine("-----");
            }
#endif

//how to:
//howtos:
//var Site = PeasyMotionActivate.Instance.ServiceProvider;
//var dte = Package.GetGlobalService(typeof(SDTE)) as EnvDTE80.DTE2;
//wf.SetProperty((int)VsFramePropID.EditorCaption, null);
//wf.SetProperty((int)VsFramePropID.OwnerCaption, newCaption);
//wf.SetProperty((int)VsFramePropID.ShortCaption, newCaption);
//if (VSConstants.S_OK == wf.GetProperty((int)VsFramePropID.OverrideCaption, out var c))
//wf.GetProperty((int)VsFramePropID.OverrideCaption, out var oovcap);
//wf.GetProperty((int)VsFramePropID.EditorCaption, out var oecap);
//wf.GetProperty((int)VsFramePropID.OwnerCaption, out var oocap);
//string ecap = (string)oecap;
//string ocap = (string)oocap;
//string ovcap = (string)oovcap;
//var _ = wf.IsOnScreen(out int isonscreen_int);  // unreliable
//var inOnScreen = isonscreen_int != 0; // unreliable
//var isVisible = VSConstants.S_OK == wf.IsVisible(); // unreliable
//int x = -9, y = -9, px = -9, py = -9;
//wf.GetFramePos(null, out Guid _, out x, out y, out px, out py);
//if (wf is IVsWindowFrame4 wf4) { wf4.GetWindowScreenRect(out x, out y, out px, out py); }
//Trace.WriteLine($"WindowFrame[{wfi++}] Current={currentWindowFrame==wf} Caption={ce} OwnerCaption={ocap} "+
//    $"EditorCaption={ecap} OverrideCaption={ovcap} "+
//    $"IsOnScreen={inOnScreen} IsVisible={isVisible} {x} {y} {px} {py}");