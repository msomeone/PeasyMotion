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
        /// Command ID.
        /// </summary>
        public const int CommandId = 0x0100;

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

        private InputListener inputListener;
        private string accumulatedKeyChars = null;

        private const string VsVimSetDisabled = "VsVim.SetDisabled";
        private const string VsVimSetEnabled = "VsVim.SetEnabled";
        private static bool disableVsVimCmdAvailable = false;
        private static bool enableVsVimCmdAvailable = false;
        private CommandExecutorService cmdExec = null;

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
            ThreadHelper.ThrowIfNotOnUIThread();

             try {
                var dte = Package.GetGlobalService(typeof(SDTE)) as EnvDTE.DTE;
                var props = dte.Properties["FontsAndColors", "TextEditor"];

                var fac = (EnvDTE.FontsAndColorsItems)props.Item("FontsAndColorsItems").Object;
                var enumfac = fac.GetEnumerator();
                while (false != enumfac.MoveNext())
                {
                    var i = enumfac.Current;
                    var i2 = (EnvDTE.ColorableItems)i;
                    Trace.WriteLine($"{i2.Name}");
                }

                var colors = ConvertDTEColor(((EnvDTE.ColorableItems)fac.Item("Plain Text")).Foreground);
                Trace.WriteLine($"{colors}");

                //FontFamily = props.Item("FontFamily").Value.ToString();
                //FontSize = (float)(short)props.Item("FontSize").Value;
                //FontBold = colors.Bold;
                //ForeColor = ColorTranslator.FromOle((int)colors.Foreground);
                //BackColor = ColorTranslator.FromOle((int)colors.Background);

                //colors = (EnvDTE.ColorableItems)fac.Item("Selected Text");

                //HighlightFontBold = colors.Bold;
                //HighlightForeColor = ColorTranslator.FromOle((int)colors.Foreground);
                //HighlightBackColor = ColorTranslator.FromOle((int)colors.Background);
            } catch (Exception ex) {
                Trace.WriteLine("Error loading text editor font and colors");
                Trace.WriteLine(ex.ToString());
            }


            var A = new EditorHostFactory();
            var B = A.CreateCompositionContainer();
            IEditorFormatMapService efms = B.GetExportedValue<IEditorFormatMapService>();
            VsSettings.Initialize(this.pkg, efms);

            CreateMenu();
            cmdExec = new CommandExecutorService() {};
            disableVsVimCmdAvailable = cmdExec.IsCommandAvailable(VsVimSetDisabled);
            enableVsVimCmdAvailable = cmdExec.IsCommandAvailable(VsVimSetEnabled);
            JumpLabelUserControl.WarmupCache();
        }

        private void CreateMenu() {
            var menuCommandID = new CommandID(CommandSet, CommandId);
            var menuItem = new MenuCommand(this.Execute, menuCommandID);
            commandService.AddCommand(menuItem);
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

        /// <summary>
        /// This function is the callback used to execute the command when the menu item is clicked.
        /// See the constructor to see how the menu item is associated with this function using
        /// OleMenuCommandService service and MenuCommand class.
        /// </summary>
        /// <param name="sender">Event sender.</param>
        /// <param name="e">Event args.</param>
        private void Execute(object sender, EventArgs e)
        {
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

            ITextStructureNavigator textStructNav = this.textStructureNavigatorSelector.GetTextStructureNavigator(wpfTextView.TextBuffer);

            adornmentMgr = new PeasyMotionEdAdornment(wpfTextView, textStructNav);

            ThreadHelper.ThrowIfNotOnUIThread();
            CreateInputListener(vsTextView, wpfTextView);

            #if MEASUREEXECTIME
            watch.Stop();
            Trace.WriteLine($"PeasyMotion FullExecTime: {watch.ElapsedMilliseconds} ms");
            #endif
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
                if (adornmentMgr.JumpTo(accumulatedKeyChars))
                {
                    Deactivate();
                }
            } 
            else 
            {
                Deactivate();
            }
        }

        private void Deactivate()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            adornmentMgr?.Reset();
            adornmentMgr = null;
            StopListening2Keyboard();
        }
        private void StopListening2Keyboard()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            inputListener.KeyPressed -= InputListenerOnKeyPressed;
            inputListener.RemoveFilter();
            TryEnableVsVim();
            //TODO: detect ViEmu presence!!!
            SendKeys.Send("{ESC}"); // <- workaround: ViEmu finalize 
        }

    }
}
