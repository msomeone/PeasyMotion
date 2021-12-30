//#define MEASUREEXECTIME
//#define DEBUG_LABEL_ALGO

using System;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Media;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Formatting;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Operations;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Collections.Generic;
using System.Windows;
using Microsoft.VisualStudio.Utilities;
using Microsoft.VisualStudio.PlatformUI;

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
using Microsoft.VisualStudio.Shell;
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
                int currentCursorPosition = -1,
                SnapshotSpan jumpLabelSpan = new SnapshotSpan(),
                IVsWindowFrame windowFrame = null
            )
        {
            this.currentCursorPosition = currentCursorPosition;
            this.jumpLabelSpan = jumpLabelSpan;
            this.windowFrame = windowFrame;
        }
        public int currentCursorPosition {get;}
        public SnapshotSpan jumpLabelSpan {get;} // contains Span of finally selected label
        public IVsWindowFrame windowFrame {get;}
    };

    public enum JumpMode {
        InvalidMode,
        WordJump,
        SelectTextJump,
        LineJumpToWordBegining,
        LineJumpToWordEnding,
        VisibleDocuments,
        LineBeginingJump,
        TwoCharJump
    }

    class PeasyMotionEdAdornmentCtorArgs
    {
        public PeasyMotionEdAdornmentCtorArgs() {}

        public IVsTextView vsTextView{ get; set; }
        public IWpfTextView wpfView{ get; set; }
        public ITextStructureNavigator textStructNav{ get; set; }
        public JumpMode jumpMode{ get; set; }
        public string twoCharSearchJumpKeys{ get; set; }
    }

    /// <summary>
    /// PeasyMotionEdAdornment places red boxes behind all the "a"s in the editor window
    /// </summary>
    internal sealed class PeasyMotionEdAdornment
    {
        private ITextStructureNavigator textStructureNavigator{ get; set; }
            /// <summary>
            /// The layer of the adornment.
            /// </summary>
        private readonly IAdornmentLayer layer;

        /// <summary>
        /// Text view where the adornment is created.
        /// </summary>
        public IVsTextView vsTextView;
        public IWpfTextView view;
        private VsSettings vsSettings;
        private JumpLabelUserControl.CachedSetupParams jumpLabelCachedSetupParams = new JumpLabelUserControl.CachedSetupParams();

        private readonly struct Jump
        {
            public Jump(
                    SnapshotSpan span,
                    string label,
                    JumpLabelUserControl labelAdornment,
                    IVsWindowFrame windowFrame,
                    IVsTextView windowPrimaryTextView,
                    string vanillaTabCaption
                )
            {
                this.span = span;
                this.label = label;
                this.labelAdornment = labelAdornment;
                this.windowFrame = windowFrame;
                this.windowPrimaryTextView = windowPrimaryTextView;
                this.vanillaTabCaption = vanillaTabCaption;
            }

            public SnapshotSpan span {get; }
            public string label {get; }
            public JumpLabelUserControl labelAdornment {get; }
            public IVsWindowFrame windowFrame { get; }
            public IVsTextView windowPrimaryTextView { get; }
            public string vanillaTabCaption { get; }
        };

        private readonly struct JumpWord
        {
            public JumpWord(
                    int distanceToCursor,
                    Rect adornmentBounds,
                    SnapshotSpan span,
                    string text,
                    IVsWindowFrame windowFrame,
                    IVsTextView windowPrimaryTextView,
                    string vanillaTabCaption
#if DEBUG_LABEL_ALGO
                    , int textViewPosDbg
#endif
                )
            {
                this.distanceToCursor = distanceToCursor;
                this.adornmentBounds = adornmentBounds;
                this.span = span;
                this.text  = text;
                this.windowFrame = windowFrame;
                this.windowPrimaryTextView = windowPrimaryTextView;
                this.vanillaTabCaption = vanillaTabCaption;
#if DEBUG_LABEL_ALGO
                this.textViewPosDbg = textViewPosDbg;
#endif
            }

            public int distanceToCursor {get; }
            public Rect adornmentBounds {get; }
            public SnapshotSpan span {get; }
            public string text {get; }
            public IVsWindowFrame windowFrame {get; }
            public IVsTextView windowPrimaryTextView { get; }
            public string vanillaTabCaption { get; }
#if DEBUG_LABEL_ALGO
            public int textViewPosDbg { get; }
#endif
        };

        private List<Jump> currentJumps = new List<Jump>();
        private List<Jump> inactiveJumps = new List<Jump>();
        public bool anyJumpsAvailable() => currentJumps.Count > 0;

        public string jumpLabelKeyArray = null;

        private JumpMode jumpMode = JumpMode.InvalidMode;
        public JumpMode CurrentJumpMode { get { return jumpMode; } }

        public PeasyMotionEdAdornment() { // just for listener
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PeasyMotionEdAdornment"/> class.
        /// </summary>
        /// <param name="view">Text view to create the adornment for</param>
        public PeasyMotionEdAdornment(PeasyMotionEdAdornmentCtorArgs args)
        {
            this.jumpLabelKeyArray = GeneralOptions.Instance.AllowedJumpKeys;
#if MEASUREEXECTIME
            var watch0 = System.Diagnostics.Stopwatch.StartNew();
#endif
            jumpMode = args.jumpMode;

            var jumpLabelAssignmentAlgorithm = GeneralOptions.Instance.getJumpLabelAssignmentAlgorithm();
            var caretPositionSensivity = Math.Min(Int32.MaxValue >> 2, Math.Abs(GeneralOptions.Instance.caretPositionSensivity));


            this.textStructureNavigator = args.textStructNav;

            this.vsTextView = args.vsTextView;
            this.view = args.wpfView;

            this.layer = view.GetAdornmentLayer("PeasyMotionEdAdornment");
            //this.view.LayoutChanged += this.OnLayoutChanged;

            this.vsSettings = VsSettings.GetOrCreate(view);
            // subscribe to fmt updates, so user can tune color faster if PeasyMotion was invoked
            this.vsSettings.PropertyChanged += this.OnFormattingPropertyChanged;

            this.jumpLabelCachedSetupParams.fontRenderingEmSize = this.view.FormattedLineSource.DefaultTextProperties.FontRenderingEmSize;
            this.jumpLabelCachedSetupParams.typeface = this.view.FormattedLineSource.DefaultTextProperties.Typeface;
            this.jumpLabelCachedSetupParams.labelFg = this.vsSettings.JumpLabelFirstMotionForegroundColor;
            this.jumpLabelCachedSetupParams.labelBg = this.vsSettings.JumpLabelFirstMotionBackgroundColor;
            this.jumpLabelCachedSetupParams.labelFinalMotionFg = this.vsSettings.JumpLabelFinalMotionForegroundColor;
            this.jumpLabelCachedSetupParams.labelFinalMotionBg = this.vsSettings.JumpLabelFinalMotionBackgroundColor;
            this.jumpLabelCachedSetupParams.Freeze();

            var jumpWords = new List<JumpWord>();
#if MEASUREEXECTIME
            watch0.Stop();
            Trace.WriteLine($"PeasyMotion Adornment ctor settings, members init, etc: {watch0.ElapsedMilliseconds} ms");
#endif


            if (jumpMode == JumpMode.VisibleDocuments) {
                SetupJumpToDocumentTabMode(jumpWords);
            } else {
                SetupJumpInsideTextViewMode(jumpWords, jumpLabelAssignmentAlgorithm, caretPositionSensivity, args.twoCharSearchJumpKeys);
            }

            if (JumpLabelAssignmentAlgorithm.CaretRelative == jumpLabelAssignmentAlgorithm)
            {
#if MEASUREEXECTIME
                var watch2 = System.Diagnostics.Stopwatch.StartNew();
#endif
                // sort jump words from closest to cursor to farthest
                jumpWords.Sort((a, b) => +a.distanceToCursor.CompareTo(b.distanceToCursor));
#if MEASUREEXECTIME
                watch2.Stop();
                Trace.WriteLine($"PeasyMotion Adornment sort words: {watch2.ElapsedMilliseconds} ms");
#endif
            }

#if MEASUREEXECTIME
            var watch3 = System.Diagnostics.Stopwatch.StartNew();
#endif
            _ = computeGroups(0, jumpWords.Count-1, (jumpLabelKeyArray), "", jumpWords);

#if MEASUREEXECTIME
            watch3.Stop();
            Trace.WriteLine($"PeasyMotion Adornments group&create: {watch3?.ElapsedMilliseconds} ms");
#endif

#if MEASUREEXECTIME
            Trace.WriteLine($"PeasyMotion Adornments create: {adornmentCreateStopwatch?.ElapsedMilliseconds} ms");
            Trace.WriteLine($"PeasyMotion Adornments UI Elem create: {createAdornmentUIElem?.ElapsedMilliseconds} ms");
            Trace.WriteLine($"PeasyMotion Adornment total jump labels - {jumpWords?.Count}");
            createAdornmentUIElem = null;
            adornmentCreateStopwatch = null;
#endif

            if (jumpMode == JumpMode.VisibleDocuments) {
                SetupJumpToDocumentTabFinalPhase();
            }
        }

        ~PeasyMotionEdAdornment()
        {
            if (view != null) {
                this.vsSettings.PropertyChanged -= this.OnFormattingPropertyChanged;
            }
        }

        public void TraceLine(string message,
                [CallerFilePath] string filePath = "",
                [CallerLineNumber] int lineNumber = 0)
        {
            Trace.WriteLine($"{filePath}:{lineNumber} -> {message}");
        }

        private void SetupJumpInsideTextViewMode(
                List<JumpWord> jumpWords,
                JumpLabelAssignmentAlgorithm jumpLabelAssignmentAlgorithm,
                int caretPositionSensivity,
                string twoCharSearchJumpKeys // null if jumpMode != TwoCharJump
            )
        {
#if MEASUREEXECTIME
            var watch1 = System.Diagnostics.Stopwatch.StartNew();
#endif

            int currentTextPos = this.view.TextViewLines.FirstVisibleLine.Start;
            int lastTextPos = this.view.TextViewLines.LastVisibleLine.EndIncludingLineBreak;
            int currentLineStartTextPos = currentTextPos; // used for line word b/e jump mode
            int currentLineEndTextPos = currentTextPos; // used for line word b/e jump mode

            var cursorSnapshotPt = this.view.Caret.Position.BufferPosition;
            int cursorIndex = this.view.TextViewLines.FirstVisibleLine.Start.Position;
            bool lineJumpToWordBeginOrEnd_isActive = (jumpMode == JumpMode.LineJumpToWordBegining) || (jumpMode == JumpMode.LineJumpToWordEnding);
            if ((JumpLabelAssignmentAlgorithm.CaretRelative == jumpLabelAssignmentAlgorithm) || lineJumpToWordBeginOrEnd_isActive)
            {
                cursorIndex = cursorSnapshotPt.Position;
                if ((cursorIndex < currentTextPos) || (cursorIndex > lastTextPos))
                {
                    cursorSnapshotPt = this.view.TextSnapshot.GetLineFromPosition(currentTextPos + (lastTextPos - currentTextPos) / 2).Start;
                    cursorIndex = cursorSnapshotPt.Position;
                }
                // override text range for line jump mode:
                if (lineJumpToWordBeginOrEnd_isActive) {
                    var currentLine = this.view.TextSnapshot.GetLineFromPosition(cursorIndex);
                    currentLineStartTextPos = currentTextPos = currentLine.Start;
                    currentLineEndTextPos = lastTextPos = currentLine.EndIncludingLineBreak;
                }

                // bin caret to virtual segments accroding to sensivity option, with sensivity=0 does nothing
                int dc = caretPositionSensivity + 1;
                cursorIndex = (cursorIndex / dc) * dc + (dc / 2);
            }

            // collect words and required properties in visible text
            //char prevChar = '\0';
            var startPoint = this.view.TextViewLines.FirstVisibleLine.Start;
            var endPoint = this.view.TextViewLines.LastVisibleLine.EndIncludingLineBreak;
            if (lineJumpToWordBeginOrEnd_isActive) {
                startPoint = new SnapshotPoint(startPoint.Snapshot, currentLineStartTextPos);
                endPoint = new SnapshotPoint(endPoint.Snapshot, currentLineEndTextPos);
            }

            var snapshot = startPoint.Snapshot;
            int lastJumpPos = -100;
            char tmpCh = '\0';
            bool prevIsSeparator = Char.IsSeparator(tmpCh);
            bool prevIsPunctuation = Char.IsPunctuation(tmpCh);
            bool prevIsLetterOrDigit = Char.IsLetterOrDigit(tmpCh);
            bool prevIsControl = Char.IsControl(tmpCh);
            SnapshotPoint currentPoint = new SnapshotPoint(snapshot, startPoint.Position);
            SnapshotPoint nextPoint = currentPoint;
            SnapshotPoint prevPoint = currentPoint;
            int firstPosition = startPoint.Position;
            int i = firstPosition;
            int lastPosition = Math.Max(endPoint.Position, 0);
            if (startPoint.Position == lastPosition) {
                i = lastPosition + 2; // just skip the loop. noob way :D
            }

            // EOL convention reminder | Windows = CR LF \r\n | Unix = LF \n | Mac = CR \r
#if DEBUG_LABEL_ALGO
            int dbgLabelAlgo_TraceStart = i;
            int dbgLabelAlgo_TraceEnd = lastPosition;
#endif
            int EOL_charCount = 0; // 0 - uninitalized
            bool EOL_Windows = false;
            const int MinimumDistanceBetweenLabels = 3; const char CR ='\r'; const char LF ='\n';

            bool prevNewLine = false;
            int jumpPosModifierBase = 0;
            if (jumpMode == JumpMode.LineBeginingJump) {
                jumpPosModifierBase = 1; // use next pos as label pos for LineBeginingJump
            }
            for (; i <= lastPosition; i++)
            {
                var ch = currentPoint.GetChar();
                //TraceLine($"before prevpt new SnapshotPoint {i-1} <- pos | {lastPosition} | {i-1}");
                prevPoint = new SnapshotPoint(snapshot, Math.Max(i-1, 0));
                //TraceLine($"after prevpt new SnapshotPoint {i-1} <- pos | {lastPosition} | {i-1}");
                //TraceLine($"{i} <- pos | {lastPosition} | {i+1}");
                var prevChar = prevPoint.GetChar();
                //TraceLine($"before new SnapshotPoint {i} <- pos | {lastPosition} | {i+1}");
                nextPoint = new SnapshotPoint(snapshot, Math.Min(i+1, lastPosition-1));
                //TraceLine($"after new SnapshotPoint {i} <- pos | {lastPosition} | {i+1}");
                var nextCh = nextPoint.GetChar();
                //TraceLine("nextCh");
                bool curIsSeparator = Char.IsSeparator(ch);
                bool curIsPunctuation = Char.IsPunctuation(ch);
                bool curIsLetterOrDigit = Char.IsLetterOrDigit(ch);
                bool curIsControl = Char.IsControl(ch);
                bool nextIsControl = Char.IsControl(nextCh);
                if ((EOL_charCount == 0) && curIsControl) {
                    if ((prevChar == CR) && (ch == LF)) { EOL_charCount = 2; EOL_Windows = true;}
                    else if ((ch == CR) && (nextCh == LF)) { EOL_charCount = 2; EOL_Windows = true;}
                    else if ((ch == CR) && !prevIsControl && ((nextCh == CR) || !nextIsControl)) { EOL_charCount = 1; }
                    else if ((ch == LF) && !prevIsControl && ((nextCh == LF) || !nextIsControl)) { EOL_charCount = 1; }
#if DEBUG_LABEL_ALGO
                    Trace.WriteLine($"EOL chars count = {EOL_charCount}");
#endif
                }
                bool newLine = ( EOL_Windows && ((prevChar == CR) && (ch == LF))) ||
                               (!EOL_Windows && ((ch == LF) || (ch == CR    )))   ;
                int jumpPosModifier = jumpPosModifierBase;
                bool candidateLabel = false;
                //TODO: anything faster and simpler ? will regex be faster? maybe symbols
                // LUT with BITS (IsSep,IsPunct, etc as bits in INT record of LUT?)
                switch (jumpMode) {
                case JumpMode.LineJumpToWordBegining:
                    candidateLabel = curIsLetterOrDigit && !prevIsLetterOrDigit;
                    break;
                case JumpMode.LineJumpToWordEnding:
                    {
                        bool nextIsLetterOrDigit = Char.IsLetterOrDigit(nextCh);
                        candidateLabel = curIsLetterOrDigit && !nextIsLetterOrDigit;
                    }
                    break;
                case JumpMode.LineBeginingJump:
                    {
                        bool firstLine = i == firstPosition;
                        candidateLabel = (newLine && (i < lastPosition-1)) || firstLine;
                        jumpPosModifier = firstLine ? 0 : jumpPosModifier;
                        // search till we find non empty char or next EOL
                        int j = i + 1;
                        while (j <= lastPosition) {
                            var pos_j = new SnapshotPoint(snapshot, Math.Min(j, lastPosition-1));
                            var ch_j = pos_j.GetChar();
                            bool EOL_j = (ch_j == CR) && (ch_j == LF);
                            if ((ch_j != ' ') && !EOL_j) {
                                jumpPosModifier = jumpPosModifier + (j - i - 1);
                                break;
                            }
                            else if (EOL_j) {
                                break;
                            }
                            j++;
                        }
                    }
                    break;
                case JumpMode.TwoCharJump:
                    {
                        candidateLabel = (Char.ToLowerInvariant(ch) == twoCharSearchJumpKeys[0]) &&
                                         (Char.ToLowerInvariant(nextCh) == twoCharSearchJumpKeys[1]) && (i < lastPosition);
                    }
                    break;
                default:
                    candidateLabel =
                        (curIsLetterOrDigit && !prevIsLetterOrDigit) ||
                        ((prevIsControl || prevNewLine) && newLine) ||
                        ((!prevIsControl && !prevNewLine) && !prevIsLetterOrDigit && newLine);
                        //(!curIsControl && nextIsControl) ||
                        //((prevChar!= ' ') && !prevIsLetterOrDigit && curIsControl && nextIsControl);
                    break;
                }
                bool distanceToPrevLabelAcceptable = (lastJumpPos + jumpPosModifier + MinimumDistanceBetweenLabels) < i;
                // do not duplicate jump label on CRLF, place on CR only
                //distanceToPrevLabelAcceptable &= (!EOL_Windows) || (EOL_Windows && (ch == LF));
                distanceToPrevLabelAcceptable = distanceToPrevLabelAcceptable || (prevNewLine && newLine);

                candidateLabel = candidateLabel && ((distanceToPrevLabelAcceptable));// make sure there is a lil bit of space between adornments
                candidateLabel = candidateLabel && (i < lastPosition);

                //if (jumpMode == JumpMode.LineJump) {

                //}
#if DEBUG_LABEL_ALGO
                string cvtChar(char c) {
                    if (ch == '\0') return new string('l', 1);
                    switch (c) {
                    case '\r': return "<CR>"; case '\n': return "<LF>"; case '\t': return "<TAB>";
                    case ' ': return "<SPC>"; case '\0': return "NULL"; default: break;
                    } return new string(c,1);
                };
                var dbgCh = cvtChar(ch); var dbgPrevCh = cvtChar(prevChar); var dbgNextCh = cvtChar(nextCh);
                Trace.WriteLine(
                        $"CAND={candidateLabel,5} POS={i,5} currentChar={dbgCh,5}({(int)ch,5}) isSep={curIsSeparator,5} isPunct={curIsPunctuation,5} "+
                        $"IsLetOrDig={curIsLetterOrDigit,5} isCtrl={curIsControl} ||| "+
                        $" prevChar={dbgPrevCh,5}({(int)prevChar,5}) isSep={prevIsSeparator,5} isPunct={prevIsPunctuation,5} "+
                        $"IsLetOrDig={prevIsLetterOrDigit,5} isCtrl={prevIsControl} ||| "+
                        $"nextChar={dbgNextCh,5}({(int)nextCh,5}) isSep={Char.IsSeparator(nextCh),5} "+
                        $"isPunct={Char.IsPunctuation(nextCh),5} "+
                        $"IsLetOrDig={Char.IsLetterOrDigit(nextCh),5} isCtrl={nextIsControl,5}" +
                        $"(lastJumpPos+MD)<i = {(lastJumpPos + MinimumDistanceBetweenLabels) < i,5} " +
                        $"lastJumpPos = {lastJumpPos}"
                    );
#endif

                //TraceLine("if (candidateLabel)");

                if (candidateLabel)
                {
                    //TraceLine("INSIDE if (candidateLabel)");

                    int jumpPosModified = (jumpPosModifier + i) < lastPosition ? (jumpPosModifier + i) : i;
                    //TraceLine(string.Format($"before new SnapshotSpan, {jumpPosModified}, " + $"{jumpPosModified + 1}, {lastPosition}"));
                    SnapshotSpan firstCharSpan = new SnapshotSpan(this.view.TextSnapshot, Span.FromBounds(jumpPosModified, jumpPosModified + 1));
                    //TraceLine("before GetTextMarkerGeometry");
                    Geometry geometry = this.view.TextViewLines.GetTextMarkerGeometry(firstCharSpan);
                    if (geometry != null)
                    {
                        var jw = new JumpWord(
                            distanceToCursor : Math.Abs(jumpPosModified - cursorIndex),
                            adornmentBounds : geometry.Bounds,
                            span : firstCharSpan,
                            text : null,
                            windowFrame : null,
                            windowPrimaryTextView : null,
                            vanillaTabCaption : null
#if DEBUG_LABEL_ALGO
                            ,textViewPosDbg : jumpPosModified
#endif
                        );
                        jumpWords.Add(jw);
                        lastJumpPos = jumpPosModified;
#if DEBUG_LABEL_ALGO
                        Trace.WriteLine($"POS={i,5} Adding candidate jump word, lastJumpPos = {lastJumpPos}");
#endif
                        // reset lastJumpPos index if newline encountered, so we wont skip label on line border
                        lastJumpPos = newLine ? -100 : lastJumpPos;
                    }
                    //TraceLine("after GetTextMarkerGeometry");
                }
                prevIsSeparator = curIsSeparator;
                prevIsPunctuation = curIsPunctuation;
                prevIsLetterOrDigit = curIsLetterOrDigit;
                prevIsControl = curIsControl;

                currentPoint = nextPoint;
                prevNewLine = newLine;

            }
#if MEASUREEXECTIME
            watch1.Stop();
            Trace.WriteLine($"PeasyMotion Adornment find words: {watch1.ElapsedMilliseconds} ms");
#endif
        }

        private void SetupJumpToDocumentTabMode(List<JumpWord> jumpWords)
        {
#if MEASUREEXECTIME
            var watch2_0 = System.Diagnostics.Stopwatch.StartNew();
            Stopwatch getCodeWindwowsSW = null;
            Stopwatch getPrimaryViewSW = null;
#endif
            //var wfs = vsUIShell4.GetDocumentWindowFrames(__WindowFrameTypeFlags.WINDOWFRAMETYPE_Document).GetValueOrDefault();
            var wfs = PeasyMotionActivate.Instance.iVsUiShell.GetDocumentWindowFrames().GetValueOrDefault();
            var currentWindowFrame = this.vsTextView.GetWindowFrame().GetValueOrDefault(null);

            Rect emptyRect = new Rect();
            SnapshotSpan emptySpan = new SnapshotSpan();
            Trace.WriteLine($"GetDocumentWindowFrames returned {wfs.Count} window frames");
            int wfi = 0; // there is no easy way to determine document tab coordinates T_T
            foreach(var wf in wfs)
            {
                wf.GetProperty((int)VsFramePropID.Caption, out var oce);
                string ce = (string)oce;

                if (currentWindowFrame == wf) {
                    continue;
                }

                //GetCodeWindow || GetPrimaryView are fucking slow! when more than 10 documents to be processed.
                // IsOnScreen & IsVisible properties are lying, no easy way to optimize View query
#if MEASUREEXECTIME
                if (getCodeWindwowsSW == null) { getCodeWindwowsSW = Stopwatch.StartNew();
                } else { getCodeWindwowsSW.Start(); }
#endif
#if MEASUREEXECTIME
                getCodeWindwowsSW.Stop();
                if (getPrimaryViewSW == null) { getPrimaryViewSW = Stopwatch.StartNew();
                } else { getPrimaryViewSW.Start(); }
#endif
                //IVsCodeWindow cw = wf.GetCodeWindow().GetValueOrDefault(null);
                //IVsTextView wptv = cw?.GetPrimaryView().GetValueOrDefault(null);

                IVsTextView wptv = wf.GetPrimaryTextView();
#if MEASUREEXECTIME
                getPrimaryViewSW.Stop();
#endif
                // VANILLA:
                //IVsTextView wptv = wf.GetCodeWindow().GetValueOrDefault(null) ? .GetPrimaryView().GetValueOrDefault(null);

                var distToCurrentDocument = Math.Abs(wfi);
                var jw = new JumpWord(
                    distanceToCursor : wfi,
                    adornmentBounds : emptyRect,
                    span : emptySpan,
                    text : null,
                    windowFrame : wf,
                    windowPrimaryTextView : wptv,
                    vanillaTabCaption : ce
#if DEBUG_LABEL_ALGO
                    , textViewPosDbg : wfi
#endif
                );
                jumpWords.Add(jw);
            }
#if MEASUREEXECTIME
            watch2_0.Stop();
            Trace.WriteLine($"PeasyMotion Adornment find visible document tabs: {watch2_0.ElapsedMilliseconds} ms");
            Trace.WriteLine($"PeasyMotion get code window total : {getCodeWindwowsSW?.ElapsedMilliseconds} ms");
            Trace.WriteLine($"PeasyMotion get primary views total : {getPrimaryViewSW?.ElapsedMilliseconds} ms");
#endif
        }

        private void SetupJumpToDocumentTabFinalPhase() {
#if MEASUREEXECTIME
                var setCaptionTiming = System.Diagnostics.Stopwatch.StartNew();
#endif
                var primaryViewsToUpdate = new List<IVsTextView>();
                foreach(var jump in currentJumps) {
                    jump.windowFrame.SetDocumentWindowFrameCaptionWithLabel(jump.vanillaTabCaption, jump.label);
                    primaryViewsToUpdate.Add(jump.windowPrimaryTextView);
                }
#if MEASUREEXECTIME
                setCaptionTiming.Stop();
                Trace.WriteLine($"PeasyMotion document tabs set caption: {setCaptionTiming?.ElapsedMilliseconds} ms");
#endif
                UpdateViewFramesCaptions(primaryViewsToUpdate);

        }

        public static void UpdateViewFramesCaptions(List<IVsTextView> tviews) {
#if MEASUREEXECTIME
            var timing2 = System.Diagnostics.Stopwatch.StartNew();
#endif
            foreach(var v in tviews) {
                v?.UpdateViewFrameCaption();
            }
#if MEASUREEXECTIME
            timing2.Stop();
            Trace.WriteLine($"PeasyMotion document tabs view update captions (UpdateViewFramesCaptions): {timing2?.ElapsedMilliseconds} ms");
#endif
        }

        public static string getDocumentTabCaptionWithLabel(string originalCaption, string jumpLabel) {
            return $"{jumpLabel.ToUpper()} |{originalCaption.Substring(jumpLabel.Length+2)}"; // 2 <<= for '| ' chars
        }

        public void Dispose()
        {
            this.vsSettings.PropertyChanged -= this.OnFormattingPropertyChanged;
        }

        public void OnFormattingPropertyChanged(object o, System.ComponentModel.PropertyChangedEventArgs prop)
        {
            var val = vsSettings[prop.PropertyName];
            switch (prop.PropertyName)
            {
            case nameof(VsSettings.JumpLabelFirstMotionForegroundColor):
                {
                    var brush = val as SolidColorBrush;
                    foreach(var j in currentJumps) {
                        if ((j.labelAdornment != null) && ((j.labelAdornment.Content as string).Length > 1)) {
                            j.labelAdornment.Foreground = brush;
                        }
                    }
                }
                break;
            case nameof(VsSettings.JumpLabelFirstMotionBackgroundColor):
                {
                    var brush = val as SolidColorBrush;
                    foreach(var j in currentJumps) {
                        if ((j.labelAdornment != null) && ((j.labelAdornment.Content as string).Length > 1)) {
                            j.labelAdornment.Background = brush;
                        }
                    }
                }
                break;
            case nameof(VsSettings.JumpLabelFinalMotionForegroundColor):
                {
                    var brush = val as SolidColorBrush;
                    foreach(var j in currentJumps) {
                        if ((j.labelAdornment != null) && ((j.labelAdornment.Content as string).Length == 1)) {
                            j.labelAdornment.Foreground = brush;
                        }
                    }
                }
                break;
            case nameof(VsSettings.JumpLabelFinalMotionBackgroundColor):
                {
                    var brush = val as SolidColorBrush;
                    foreach(var j in currentJumps) {
                        if ((j.labelAdornment != null) && ((j.labelAdornment.Content as string).Length == 1)) {
                            j.labelAdornment.Background = brush;
                        }
                    }
                }
                break;
            }
        }

        public struct JumpNode
        {
            public int jumpWordIndex;
            public Dictionary<char, JumpNode> childrenNodes;
        };

#if MEASUREEXECTIME
        private Stopwatch adornmentCreateStopwatch = null;
        private Stopwatch createAdornmentUIElem = null;
#endif

        private Dictionary<char, JumpNode> computeGroups(int wordStartIndex, int wordEndIndex, string keys0, string prefix, List<JumpWord> jumpWords)
        {
            // SC-Tree algorithm from vim-easymotion script with minor changes
            var wordCount = wordEndIndex - wordStartIndex + 1;
            var keyCount = keys0.Length;

            Dictionary<char, JumpNode> groups = new Dictionary<char, JumpNode>();

            var keys = keys0; //Reverse(keys0);

            var keyCounts = new int[keyCount];
            var keyCountsKeys = new Dictionary<char, int>(keyCount);
            var j = 0;
            foreach(char key in keys)
            {
                keyCounts[j] = 0;
                keyCountsKeys[key] = j;
                j++;
            }

            var targetsLeft = wordCount;
            var level = 0;
            var i = 0;

            while (targetsLeft > 0)
            {
                var childrenCount = level == 0 ? 1 : keyCount - 1;
                foreach(char key in keys)
                {
                    keyCounts[keyCountsKeys[key]] += childrenCount;
                    targetsLeft -= childrenCount;
                    if (targetsLeft <= 0) {
                        keyCounts[keyCountsKeys[key]] += targetsLeft;
                        break;
                    }
                    i += 1;

                }
                level += 1;
            }

            var k = 0;
            var keyIndex = 0;
            Array.Reverse(keyCounts);
            foreach (int KeyCount2 in keyCounts)
            {
                if (KeyCount2 > 1)
                {
                    groups[keys[keyIndex]] = new JumpNode()
                    {
                        jumpWordIndex = -1,
                        childrenNodes = computeGroups(wordStartIndex + k, wordStartIndex + k + KeyCount2 - 1, Reverse(string.Copy(keys)),
                            prefix + keys[keyIndex], jumpWords )
                    };
                }
                else if (KeyCount2 == 1)
                {
                    groups[keys[keyIndex]] = new JumpNode() {
                        jumpWordIndex = wordStartIndex + k,
                        childrenNodes = null
                    };
                    var jw = jumpWords[wordStartIndex + k];
                    string jumpLabel = prefix + keys[keyIndex];

                    JumpLabelUserControl adornment = null;
                    if (jw.windowFrame == null)  // winframe=null => regular textview navigation.
                    {
#if MEASUREEXECTIME
                        if (createAdornmentUIElem == null) {
                            createAdornmentUIElem = Stopwatch.StartNew();
                        } else {
                            createAdornmentUIElem.Start();
                        }
#endif
                        adornment = JumpLabelUserControl.GetFreeUserControl();
                        adornment.setup(jumpLabel, jw.adornmentBounds, this.jumpLabelCachedSetupParams);

#if MEASUREEXECTIME
                        createAdornmentUIElem.Stop();
#endif

#if MEASUREEXECTIME
                        if (adornmentCreateStopwatch == null) {
                            adornmentCreateStopwatch = Stopwatch.StartNew();
                        } else {
                            adornmentCreateStopwatch.Start();
                        }
#endif
                        //TraceLine("before AddAdornment");
                        this.layer.AddAdornment(AdornmentPositioningBehavior.TextRelative, jw.span, null, adornment, JumpLabelAdornmentRemovedCallback);
#if DEBUG_LABEL_ALGO
                        Trace.WriteLine($"POS={jw.textViewPosDbg,5} Adding jumplabel adornment");
#endif

#if MEASUREEXECTIME
                        adornmentCreateStopwatch.Stop();
#endif
                    }

                    //Debug.WriteLine(jw.text + " -> |" + jumpLabel + "|");
                    var cj = new Jump(
                        span : jw.span,
                        label : jumpLabel,
                        labelAdornment : adornment,
                        windowFrame : jw.windowFrame,
                        vanillaTabCaption : jw.vanillaTabCaption,
                        windowPrimaryTextView : jw.windowPrimaryTextView
                    );
                    currentJumps.Add(cj);
                }
                else
                {
                    continue;
                }
                keyIndex += 1;
                k += KeyCount2;
            }

            return groups;
        }

        public void JumpLabelAdornmentRemovedCallback(object _, UIElement element)
        {
            JumpLabelUserControl.ReleaseUserControl(element as JumpLabelUserControl);
        }

        public static string Reverse( string s )
        {
            char[] charArray = s.ToCharArray();
            Array.Reverse( charArray );
            return new string( charArray );
        }

        /// <summary>
        /// Handles whenever the text displayed in the view changes by adding the adornment to any reformatted lines
        /// </summary>
        /// <remarks><para>This event is raised whenever the rendered text displayed in the <see cref="ITextView"/> changes.</para>
        /// <para>It is raised whenever the view does a layout (which happens when DisplayTextLineContainingBufferPosition is called or in response to text or classification changes).</para>
        /// <para>It is also raised whenever the view scrolls horizontally or when its size changes.</para>
        /// </remarks>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        //internal void OnLayoutChanged(object sender, TextViewLayoutChangedEventArgs e) { }

        internal void Reset()
        {
            this.layer.RemoveAllAdornments();
            List<Jump> cleanupJumps = new List<Jump>(this.currentJumps);
            cleanupJumps.AddRange(inactiveJumps);

            List<IVsTextView> textViewsToUpdate = new List<IVsTextView>();
            foreach (var j in cleanupJumps) {
                j.windowFrame?.RemoveJumpLabelFromDocumentWindowFrameCaption(j.vanillaTabCaption);
                textViewsToUpdate.Add(j.windowPrimaryTextView);
            }

            this.currentJumps.Clear();
            this.inactiveJumps.Clear();

            UpdateViewFramesCaptions(textViewsToUpdate);
        }

        internal bool NoLabelsLeft() => (this.currentJumps.Count == 0);

        // returns true if jump succeded
        internal bool JumpTo(string label, out JumpToResult jumpToResult) // returns null if jump motion is not finished yet
        {
            int idx = currentJumps.FindIndex(0, j => j.label == label);
            if (-1 < idx)
            {
                jumpToResult = new JumpToResult(
                    currentCursorPosition : this.view.Caret.Position.BufferPosition.Position,
                    jumpLabelSpan : currentJumps[idx].span,
                    windowFrame : currentJumps[idx].windowFrame
                );
                return true;
            }
            else
            {
#if MEASUREEXECTIME
                var timing1 = System.Diagnostics.Stopwatch.StartNew();
#endif
                var jumpsToRemoveOrMakeInactive = currentJumps.Where(
                    j => !j.label.StartsWith(label, StringComparison.InvariantCulture)).ToList();

                var textViewsToUpdateCaptions = new List<IVsTextView>();
                if (jumpMode == JumpMode.VisibleDocuments)
                { // keep all labeled document tabs
                    foreach(Jump j in jumpsToRemoveOrMakeInactive) { // set empty caption and dont touch anymore
                        j.windowFrame?.SetDocumentWindowFrameCaptionWithLabel(
                            j.vanillaTabCaption,
                            new string(' ', j.label.Length)
                        );
                        textViewsToUpdateCaptions.Add(j.windowPrimaryTextView);
                    }
                    inactiveJumps.AddRange(jumpsToRemoveOrMakeInactive);
                }

                jumpsToRemoveOrMakeInactive.ForEach(delegate(Jump j) {// remove adornments that do not match motion
                        currentJumps.Remove(j);
                        if (null != j.labelAdornment) {
                            this.layer.RemoveAdornment(j.labelAdornment);
                        }
                    }
                );

                foreach(Jump j in currentJumps)
                {
                    var labelRemainingMotionSubstr = j.label.Substring(label.Length);

                    j.labelAdornment?.UpdateView(labelRemainingMotionSubstr, this.jumpLabelCachedSetupParams);

                    //Stabilize tab caption, keeping it same width as before:
                    //      Best results with fixed width fonts in Tools->Fonts..->Environment
                    // replace parts of document caption with label & decor(space char), trying to
                    //preserve same tab caption width!
                    j.windowFrame?.SetDocumentWindowFrameCaptionWithLabel(
                        j.vanillaTabCaption,
                        new string(' ', j.label.Length-label.Length) + labelRemainingMotionSubstr
                    );
                    textViewsToUpdateCaptions.Add(j.windowPrimaryTextView);
                }
#if MEASUREEXECTIME
                timing1.Stop();
                Trace.WriteLine($"PeasyMotion document tabs update caption: {timing1?.ElapsedMilliseconds} ms");
#endif

                UpdateViewFramesCaptions(textViewsToUpdateCaptions);
            }
            jumpToResult = new JumpToResult();
            return false;
        }
    }
}
