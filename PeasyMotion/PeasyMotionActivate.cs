#define MEASUREEXECTIME

using System.Drawing;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using EnvDTE;
using EnvDTE80;
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
using System.Runtime.InteropServices;



namespace PeasyMotion
{
    public readonly struct Result
    {
        private readonly bool _isSuccess;
        private readonly int _hresult;

        public bool IsSuccess
        {
            get { return _isSuccess; }
        }

        public bool IsError
        {
            get { return !_isSuccess; }
        }

        public int HResult
        {
            get
            {
                if (!IsError)
                {
                    throw new InvalidOperationException();
                }
                return _hresult;
            }
        }

        private Result(int hresult)
        {
            _hresult = hresult;
            _isSuccess = ErrorHandler.Succeeded(hresult);
        }

        public static Result Error
        {
            get { return new Result(VSConstants.E_FAIL); }
        }

        public static Result Success
        {
            get { return new Result(VSConstants.S_OK); }
        }

        public static Result<T> CreateSuccess<T>(T value)
        {
            return new Result<T>(value);
        }

        public static Result<T> CreateSuccessNonNull<T>(T value)
            where T : class
        {
            if (value == null)
            {
                return Result.Error;
            }

            return new Result<T>(value);
        }

        public static Result CreateError(int value)
        {
            return new Result(value);
        }

        public static Result CreateError(Exception ex)
        {
            return CreateError(Marshal.GetHRForException(ex));
        }

        public static Result<T> CreateSuccessOrError<T>(T potentialValue, int hresult)
        {
            return ErrorHandler.Succeeded(hresult)
                ? CreateSuccess(potentialValue)
                : new Result<T>(hresult: hresult);
        }
    }

    public readonly struct Result<T>
    {
        private readonly bool _isSuccess;
        private readonly T _value;
        private readonly int _hresult;

        public bool IsSuccess
        {
            get { return _isSuccess; }
        }

        public bool IsError
        {
            get { return !_isSuccess; }
        }

        // TOOD: Get rid of this.  Make it a method that says throws
        public T Value
        {
            get
            {
                if (!IsSuccess)
                {
                    throw new InvalidOperationException();
                }

                return _value;
            }
        }

        public int HResult
        {
            get
            {
                if (IsSuccess)
                {
                    throw new InvalidOperationException();
                }

                return _hresult;
            }
        }

        public Result(T value)
        {
            _value = value;
            _isSuccess = true;
            _hresult = 0;
        }

        public Result(int hresult)
        {
            _hresult = hresult;
            _isSuccess = false;
            _value = default;
        }

        public T GetValueOrDefault(T defaultValue = default)
        {
            return IsSuccess ? Value : defaultValue;
        }

        public bool TryGetValue(out T value)
        {
            if (IsSuccess)
            {
                value = Value;
                return true;
            }

            value = default;
            return false;
        }

        public static implicit operator Result<T>(Result result)
        {
            return new Result<T>(hresult: result.HResult);
        }

        public static implicit operator Result<T>(T value)
        {
            return new Result<T>(value);
        }
    }

    public static class Extensions
    {
        public static void SetDocumentWindowFrameCaptionWithLabel(this IVsWindowFrame wf, 
                    IVsTextView primaryView, string vanillaTabCaption, string jumpLabel)
        {
            //TODO: (check for length (what if vanilla caption is shorter than label & decor ???!!!)
            var newCaption = PeasyMotionEdAdornment.getDocumentTabCaptionWithLabel(vanillaTabCaption, jumpLabel);
            wf.SetProperty((int)VsFramePropID.OverrideCaption, newCaption);
            primaryView.UpdateViewFrameCaption();
            //Debug.WriteLine($"WindowFrame oldCaption={vanillaTabCaption} => newCaption={newCaption}");
        }

        public static void RemoveJumpLabelFromDocumentWindowFrameCaption(this IVsWindowFrame wf, 
                    IVsTextView primaryView, string vanillaTabCaption)
        {
            wf.SetProperty((int)VsFramePropID.OverrideCaption, null);
            //wf.SetProperty((int)VsFramePropID.Caption, vanillaTabCaption); //TODO: checl if we really need this
            primaryView.UpdateViewFrameCaption(); //TODO: maybe batch those calls and exec separately after updating all captions
        }

        public static Result<IVsWindowFrame> GetWindowFrame(this IVsTextView textView)
        {
            var textViewEx = textView as IVsTextViewEx;
            if (textViewEx == null)
            {
                return Result.Error;
            }

            return textViewEx.GetWindowFrame();
        }

        public static Result<IVsWindowFrame> GetWindowFrame(this IVsTextViewEx textViewEx)
        {
            if (!ErrorHandler.Succeeded(textViewEx.GetWindowFrame(out object frame)))
            {
                return Result.Error;
            }

            var vsWindowFrame = frame as IVsWindowFrame;
            if (vsWindowFrame == null)
            {
                return Result.Error;
            }

            return Result.CreateSuccess(vsWindowFrame);
        }

        public static Result<IVsCodeWindow> GetCodeWindow(this IVsWindowFrame vsWindowFrame)
        {
            var iid = typeof(IVsCodeWindow).GUID;
            var ptr = IntPtr.Zero;
            try
            {
                var hr = vsWindowFrame.QueryViewInterface(ref iid, out ptr);
                if (ErrorHandler.Failed(hr))
                {
                    return Result.CreateError(hr);
                }

                return Result.CreateSuccess((IVsCodeWindow)Marshal.GetObjectForIUnknown(ptr));
            }
            catch (Exception e)
            {
                // Venus will throw when querying for the code window
                return Result.CreateError(e);
            }
            finally
            {
                if (ptr != IntPtr.Zero)
                {
                    Marshal.Release(ptr);
                }
            }
        }

        public static Result<IVsTextView> GetPrimaryView(this IVsCodeWindow vsCodeWindow)
        {
            var hr = vsCodeWindow.GetPrimaryView(out IVsTextView vsTextView);
            if (ErrorHandler.Failed(hr))
            {
                return Result.CreateError(hr);
            }

            return Result.CreateSuccessNonNull(vsTextView);
        }

        public static Result<IWpfTextView> GetPrimaryTextView(this IVsCodeWindow codeWindow, IVsEditorAdaptersFactoryService factoryService)
        {
            var result = GetPrimaryView(codeWindow);
            if (result.IsError)
            {
                return Result.CreateError(result.HResult);
            }

            var textView = factoryService.GetWpfTextViewNoThrow(result.Value);
            return Result.CreateSuccessNonNull(textView);
        }

        public static Result<List<IVsWindowFrame>> GetDocumentWindowFrames(this IVsUIShell vsShell)
        {
            var hr = vsShell.GetDocumentWindowEnum(out IEnumWindowFrames enumFrames);
            return ErrorHandler.Failed(hr) ? Result.CreateError(hr) : enumFrames.GetContents();
        }

        public static Result<List<IVsWindowFrame>> GetDocumentWindowFrames(this IVsUIShell4 vsShell, __WindowFrameTypeFlags flags)
        {
            var hr = vsShell.GetWindowEnum((uint)flags, out IEnumWindowFrames enumFrames);
            return ErrorHandler.Failed(hr) ? Result.CreateError(hr) : enumFrames.GetContents();
        }

        public static Result<List<IVsWindowFrame>> GetContents(this IEnumWindowFrames enumFrames)
        {
            var list = new List<IVsWindowFrame>();
            var array = new IVsWindowFrame[16];
            while (true)
            {
                var hr = enumFrames.Next((uint)array.Length, array, out uint num);
                if (ErrorHandler.Failed(hr))
                {
                    return Result.CreateError(hr);
                }

                if (0 == num)
                {
                    return list;
                }

                for (var i = 0; i < num; i++)
                {
                    list.Add(array[i]);
                }
            }
        }

        public static IWpfTextView GetWpfTextViewNoThrow(this IVsEditorAdaptersFactoryService editorAdapter, IVsTextView vsTextView)
        {
            try {
                return editorAdapter.GetWpfTextView(vsTextView);
            } catch {
                return null;
            }
        }
    }

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

            var jumpToDocTabMenuCommandID = new CommandID(PeasyMotion.PackageGuids.guidPeasyMotionPackageCmdSet, 
                PeasyMotion.PackageIds.PeasyMotionJumpToDocumentTab);
            var jumpToDocTabMenuItem = new MenuCommand(this.ExecuteJumpToDocTab, jumpToDocTabMenuCommandID);
            commandService.AddCommand(jumpToDocTabMenuItem);

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
            ExecuteCommonJumpCode(JumpMode.WordJump);
        }
        
        private void ExecuteSelectTextWordJump(object o, EventArgs e)
        {
            ExecuteCommonJumpCode(JumpMode.SelectTextJump);
        }

        private void ExecuteLineJumpToWordBegining(object o, EventArgs e)
        {
            ExecuteCommonJumpCode(JumpMode.LineJumpToWordBegining);
        }       

        private void ExecuteLineJumpToWordEnding(object o, EventArgs e)
        {
            ExecuteCommonJumpCode(JumpMode.LineJumpToWordEnding);
        }       

        private void ExecuteJumpToDocTab(object o, EventArgs e)
        {
            ExecuteCommonJumpCode(JumpMode.VisibleDocuments);
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

        private void ExecuteCommonJumpCode(JumpMode desiredJumpMode)
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

            adornmentMgr = new PeasyMotionEdAdornment(vsTextView, wpfTextView, textStructNav, desiredJumpMode);

            ThreadHelper.ThrowIfNotOnUIThread();
            CreateInputListener(vsTextView, wpfTextView);

            #if MEASUREEXECTIME
            watch.Stop();
            Trace.WriteLine($"PeasyMotion FullExecTime: {watch.ElapsedMilliseconds} ms");
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

                if (keyPressEventArgs.KeyChar != '\0' && Char.IsLetter(keyPressEventArgs.KeyChar))
                {
                    if (null == accumulatedKeyChars)
                    {
                        accumulatedKeyChars = new string(keyPressEventArgs.KeyChar, 1);
                    }
                    else
                    {
                        accumulatedKeyChars += keyPressEventArgs.KeyChar;
                    }
                    ;
                    if (adornmentMgr.JumpTo(accumulatedKeyChars, out var jumpToResult)) // this was final jump char
                    {
                        var wpfTextView = adornmentMgr.view;
                        var jumpMode = adornmentMgr.CurrentJumpMode;
                        Deactivate();
                        if (jumpMode != JumpMode.VisibleDocuments)
                        {
                            var labelSnapshotSpan = new SnapshotSpan(wpfTextView.TextSnapshot, jumpToResult.jumpLabelSpan);

                            switch (jumpMode) {
                            case JumpMode.InvalidMode:
                                Trace.WriteLine("PeasyMotion: OOOPS! JumpMode logic is broken!");
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
            StopListening2Keyboard();
            TryEnableVsVim();
            TryEnableViEmu();
            adornmentMgr?.Reset();
            adornmentMgr = null;
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