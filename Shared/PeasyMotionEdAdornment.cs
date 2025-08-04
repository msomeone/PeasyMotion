//#define MEASUREEXECTIME
//#define DEBUG_LABEL_ALGO

// using System.Windows.Controls;
// using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
// using Microsoft.VisualStudio.Text.Formatting;
// using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Operations;
// using System.ComponentModel.Composition;
// using System.Diagnostics;
// using Microsoft.VisualStudio.Utilities;
// using Microsoft.VisualStudio.PlatformUI;

//using System.ComponentModel.Design;
//using System.Globalization;
//using System.Threading;
//using System.Windows.Forms;
//using EnvDTE;
//using EnvDTE80;
//using Microsoft.VisualStudio.Text.Classification;
//using Microsoft.VisualStudio.ComponentModelHost;
//using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio;
//using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.Shell.Interop;
//using Microsoft.VisualStudio.Text.Editor;
//using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.TextManager.Interop;
//using Task = System.Threading.Tasks.Task;
//using Microsoft.VisualStudio.Text;
//using Microsoft.VisualStudio.Text.Formatting;
//using Microsoft.VisualStudio.Imaging;
using System.Runtime.InteropServices;

namespace PeasyMotion
{
    struct JumpToResult
    {
        public JumpToResult(
            int            currentCursorPosition = -1,
            SnapshotSpan   jumpLabelSpan         = new SnapshotSpan(),
            IVsWindowFrame windowFrame           = null
        )
        {
            this.currentCursorPosition = currentCursorPosition;
            this.jumpLabelSpan         = jumpLabelSpan;
            this.windowFrame           = windowFrame;
        }

        public int            currentCursorPosition { get; }
        public SnapshotSpan   jumpLabelSpan         { get; } // contains Span of finally selected label
        public IVsWindowFrame windowFrame           { get; }
    };

    public enum JumpMode
    {
        InvalidMode,
        WordJump,
        SelectTextJump,
        LineJumpToWordBegining,
        LineJumpToWordEnding,
        VisibleDocuments,
        LineBeginingJump,
        TwoCharJump,
        OneCharJump,
    }

    class PeasyMotionEdAdornmentCtorArgs
    {
        public PeasyMotionEdAdornmentCtorArgs()
        {
        }

        public IVsTextView             vsTextView             { get; set; }
        public IWpfTextView            wpfView                { get; set; }
        public ITextStructureNavigator textStructNav          { get; set; }
        public JumpMode                jumpMode               { get; set; }
        public string                  nCharSearchJumpKeys    { get; set; }
        public bool                    vimOrBulkyCaretPresent { get; set; }
    }


    /// <summary>
    /// PeasyMotionEdAdornment places red boxes behind all the "a"s in the editor window
    /// </summary>
    internal sealed class PeasyMotionEdAdornment
    {
        private readonly VsPeasyMotionAdapter adapter;
        public           IWpfTextView         view => adapter?.View; // Null-safe access

        public string jumpLabelKeyArray =>
            adapter != null ? GeneralOptions.Instance?.AllowedJumpKeys : null; // Null-safe access

        private bool vimOrBulkyCaretPresent = true;

        public PeasyMotionEdAdornment() // just for listener
        {
            adapter = new VsPeasyMotionAdapter(); 
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PeasyMotionEdAdornment"/> class.
        /// </summary>
        /// <param name="args">Arguments containing text view and jump mode details</param>
        public PeasyMotionEdAdornment(PeasyMotionEdAdornmentCtorArgs args)
        {
            vimOrBulkyCaretPresent = args.vimOrBulkyCaretPresent;
            adapter                = new VsPeasyMotionAdapter(args);
        }

        public bool     anyJumpsAvailable() => adapter?.anyJumpsAvailable() ?? false;
        public JumpMode CurrentJumpMode     => adapter?.JumpMode            ?? JumpMode.InvalidMode;
        public void     Dispose()           => adapter?.Dispose();

        ~PeasyMotionEdAdornment()
        {
            Dispose();
        }
        public bool JumpTo(string label, out JumpToResult jumpToResult)
        {
            if (adapter == null)
            {
                jumpToResult = new JumpToResult();
                return false;
            }

            return adapter.JumpTo(label, out jumpToResult);
        }

        public bool NoLabelsLeft() => adapter?.NoLabelsLeft() ?? true;
        public void Reset()        => adapter?.Reset();

        public static string getDocumentTabCaptionWithLabel(string originalCaption, string jumpLabel)
        {
            return VsPeasyMotionAdapter.getDocumentTabCaptionWithLabel(originalCaption, jumpLabel);
        }
    }
}

public struct JumpTarget
{
    public int    Position         { get; }
    public string Text             { get; }
    public int    DistanceToCursor { get; }
    public object Metadata         { get; }

    public JumpTarget(int position, string text, int distanceToCursor, object metadata = null)
    {
        Position         = position;
        Text             = text;
        DistanceToCursor = distanceToCursor;
        Metadata         = metadata;
    }
}

public struct JumpLabel
{
    public JumpTarget Target { get; }
    public string     Label  { get; }

    public JumpLabel(JumpTarget target, string label)
    {
        Target = target;
        Label  = label;
    }
}
