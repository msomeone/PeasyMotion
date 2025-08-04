using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Threading;

namespace PeasyMotion
{
    internal sealed class VsPeasyMotionAdapter
    {
        private readonly IAdornmentLayer                        layer;
        private readonly IVsTextView                            vsTextView;
        private readonly IWpfTextView                           view;
        private readonly VsSettings                             vsSettings;
        private readonly JumpLabelAlgorithm                     algorithm;
        private readonly JumpLabelUserControl.CachedSetupParams jumpLabelCachedSetupParams;
        private readonly List<VsJump>                           currentJumps  = new List<VsJump>();
        private readonly List<VsJump>                           inactiveJumps = new List<VsJump>();
        private readonly JumpMode                               jumpMode;
        private readonly ITextStructureNavigator                textStructureNavigator; 
        private readonly bool                                   vimOrBulkyCaretPresent; 
#if MEASUREEXECTIME
        private Stopwatch adornmentCreateStopwatch;
        private Stopwatch createAdornmentUIElem;
#endif

        private struct VsJump
        {
            public JumpLabel            Label                 { get; }
            public JumpLabelUserControl Adornment             { get; }
            public IVsWindowFrame       WindowFrame           { get; }
            public IVsTextView          WindowPrimaryTextView { get; }
            public string               VanillaTabCaption     { get; }

            public VsJump(JumpLabel   label,                 JumpLabelUserControl adornment, IVsWindowFrame windowFrame,
                          IVsTextView windowPrimaryTextView, string               vanillaTabCaption)
            {
                Label                 = label;
                Adornment             = adornment;
                WindowFrame           = windowFrame;
                WindowPrimaryTextView = windowPrimaryTextView;
                VanillaTabCaption     = vanillaTabCaption;
            }
        }

        public JumpMode     JumpMode => jumpMode;
        public IWpfTextView View     => view;

        public VsPeasyMotionAdapter()
        {
            jumpMode                   = JumpMode.InvalidMode;
            algorithm                  = null;
            layer                      = null;
            vsTextView                 = null;
            view                       = null;
            vsSettings                 = null;
            jumpLabelCachedSetupParams = new JumpLabelUserControl.CachedSetupParams();
            textStructureNavigator     = null;
            vimOrBulkyCaretPresent     = true;
        }

        public VsPeasyMotionAdapter(PeasyMotionEdAdornmentCtorArgs args)
        {
#if MEASUREEXECTIME
            var watch0 = System.Diagnostics.Stopwatch.StartNew();
#endif
            jumpMode               = args.jumpMode;
            vimOrBulkyCaretPresent = args.vimOrBulkyCaretPresent;
            textStructureNavigator = args.textStructNav; // Restore from original
            vsTextView             = args.vsTextView;    // Restore from original
            view                   = args.wpfView;

            algorithm = new JumpLabelAlgorithm(
                                               allowedKeys: GeneralOptions.Instance.AllowedJumpKeys,
                                               jumpMode: args.jumpMode,
                                               caretPositionSensivity: Math.Min(int.MaxValue >> 2,
                                                                                    Math.Abs(GeneralOptions.Instance
                                                                                                .caretPositionSensivity)),
                                               nCharSearchJumpKeys: args.nCharSearchJumpKeys
                                              );

            layer                      =  view?.GetAdornmentLayer("PeasyMotionEdAdornment");
            vsSettings                 =  VsSettings.GetOrCreate(view);
            vsSettings.PropertyChanged += OnFormattingPropertyChanged;

            jumpLabelCachedSetupParams = new JumpLabelUserControl.CachedSetupParams
            {
                fontRenderingEmSize = view?.FormattedLineSource.DefaultTextProperties.FontRenderingEmSize ?? 0,
                typeface            = view?.FormattedLineSource.DefaultTextProperties.Typeface,
                labelFg             = vsSettings?.JumpLabelFirstMotionForegroundColor,
                labelBg             = vsSettings?.JumpLabelFirstMotionBackgroundColor,
                labelFinalMotionFg  = vsSettings?.JumpLabelFinalMotionForegroundColor,
                labelFinalMotionBg  = vsSettings?.JumpLabelFinalMotionBackgroundColor
            };
            jumpLabelCachedSetupParams.Freeze();

            InitializeJumps(args.jumpMode);

            if (jumpMode == JumpMode.VisibleDocuments)
            {
                SetupJumpToDocumentTabFinalPhase();
            }
#if MEASUREEXECTIME
            watch0.Stop();
            Trace.WriteLine($"PeasyMotion Adornment ctor settings, members init, etc: {watch0.ElapsedMilliseconds} ms");
#endif
        }

        public bool anyJumpsAvailable() => algorithm != null && currentJumps.Count > 0; // Null-safe

        public static string getDocumentTabCaptionWithLabel(string originalCaption, string jumpLabel)
        {
            return $"{jumpLabel.ToUpper()} |{originalCaption.Substring(jumpLabel.Length + 2)}";
        }

        private void InitializeJumps(JumpMode jumpMode)
        {
            if (jumpMode == JumpMode.VisibleDocuments)
            {
                InitializeDocumentTabJumps();
            }
            else
            {
                InitializeTextViewJumps();
            }
        }

        private void InitializeTextViewJumps()
        {
#if MEASUREEXECTIME
            var watch1 = Stopwatch.StartNew();
#endif
            int startPos  = view.TextViewLines.FirstVisibleLine.Start.Position;
            int endPos    = view.TextViewLines.LastVisibleLine.EndIncludingLineBreak.Position;
            int cursorPos = view.Caret.Position.BufferPosition.Position;

            string text    = view.TextSnapshot.GetText(startPos, endPos                   - startPos);
            var    targets = algorithm.CollectJumpTargets(text, cursorPos, 0, text.Length - 1);
            if (GeneralOptions.Instance.getJumpLabelAssignmentAlgorithm() == JumpLabelAssignmentAlgorithm.CaretRelative)
            {
#if MEASUREEXECTIME
                var watch2 = Stopwatch.StartNew();
#endif
                targets = targets.OrderBy(t => t.DistanceToCursor).ToList();
#if MEASUREEXECTIME
                watch2.Stop();
                Trace.WriteLine($"PeasyMotion Adornment sort words: {watch2.ElapsedMilliseconds} ms");
#endif
            }

            var labels = algorithm.AssignLabels(targets);

#if MEASUREEXECTIME
            adornmentCreateStopwatch = Stopwatch.StartNew();
#endif
            foreach (var label in labels)
            {
                var span     = new SnapshotSpan(view.TextSnapshot, label.Target.Position + startPos, 1);
                var geometry = view.TextViewLines.GetTextMarkerGeometry(span);
                if (geometry != null)
                {
#if MEASUREEXECTIME
                    if (createAdornmentUIElem == null)
                    {
                        createAdornmentUIElem = Stopwatch.StartNew();
                    }
                    else
                    {
                        createAdornmentUIElem.Start();
                    }
#endif
                    var adornment = JumpLabelUserControl.GetFreeUserControl();
                    adornment.setup(label.Label, geometry.Bounds, jumpLabelCachedSetupParams);
#if MEASUREEXECTIME
                    createAdornmentUIElem.Stop();
#endif
                    layer.AddAdornment(AdornmentPositioningBehavior.TextRelative, span, null, adornment,
                                       JumpLabelAdornmentRemovedCallback);
                    currentJumps.Add(new VsJump(label, adornment, null, null, null));
#if DEBUG_LABEL_ALGO
                    Trace.WriteLine($"POS={label.Target.Position,5} Adding jumplabel adornment");
#endif
                }
            }
#if MEASUREEXECTIME
            adornmentCreateStopwatch.Stop();
            Trace.WriteLine($"PeasyMotion Adornments create: {adornmentCreateStopwatch?.ElapsedMilliseconds} ms");
            Trace.WriteLine($"PeasyMotion Adornments UI Elem create: {createAdornmentUIElem?.ElapsedMilliseconds} ms");
            Trace.WriteLine($"PeasyMotion Adornment total jump labels - {labels?.Count}");
            createAdornmentUIElem = null;
            adornmentCreateStopwatch = null;
#endif
#if MEASUREEXECTIME
            watch1.Stop();
            Trace.WriteLine($"PeasyMotion Adornment find words: {watch1.ElapsedMilliseconds} ms");
#endif
        }

        private void InitializeDocumentTabJumps()
        {
#if MEASUREEXECTIME
            var watch2_0 = Stopwatch.StartNew();
            Stopwatch getCodeWindwowsSW = null;
            Stopwatch getPrimaryViewSW = null;
#endif
            var wfs                = PeasyMotionActivate.Instance.iVsUiShell.GetDocumentWindowFrames().GetValueOrDefault();
            var currentWindowFrame = vsTextView.GetWindowFrame().GetValueOrDefault(null);
            var targets            = new List<JumpTarget>();
            int index              = 0;

            foreach (var wf in wfs)
            {
                if (wf == currentWindowFrame) continue;
#pragma warning disable VSTHRD010
                wf.GetProperty((int)VsFramePropID.Caption, out var caption);
#pragma warning restore VSTHRD010
                string tabCaption = (string)caption;
#if MEASUREEXECTIME
                if (getCodeWindwowsSW == null) { getCodeWindwowsSW = Stopwatch.StartNew(); }
                else { getCodeWindwowsSW.Start(); }
                getCodeWindwowsSW.Stop();
                if (getPrimaryViewSW == null) { getPrimaryViewSW = Stopwatch.StartNew(); }
                else { getPrimaryViewSW.Start(); }
#endif
                IVsTextView wptv = wf.GetPrimaryTextView();
#if MEASUREEXECTIME
                getPrimaryViewSW.Stop();
#endif
                targets.Add(new JumpTarget(index++, tabCaption, index,
                                           new { WindowFrame = wf, PrimaryTextView = wptv }));
            }

            var labels = algorithm.AssignLabels(targets);
            foreach (var label in labels)
            {
                var metadata = label.Target.Metadata as dynamic;
                var jump     = new VsJump(label, null, metadata.WindowFrame, metadata.PrimaryTextView, label.Target.Text);
                currentJumps.Add(jump);
            }
#if MEASUREEXECTIME
            watch2_0.Stop();
            Trace.WriteLine($"PeasyMotion Adornment find visible document tabs: {watch2_0.ElapsedMilliseconds} ms");
            Trace.WriteLine($"PeasyMotion get code window total : {getCodeWindwowsSW?.ElapsedMilliseconds} ms");
            Trace.WriteLine($"PeasyMotion get primary views total : {getPrimaryViewSW?.ElapsedMilliseconds} ms");
#endif
        }

        private async void SetupJumpToDocumentTabFinalPhase()
        {
#if MEASUREEXECTIME
            var setCaptionTiming = Stopwatch.StartNew();
#endif

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var primaryViewsToUpdate = new List<IVsTextView>();
            foreach (var jump in currentJumps)
            {
                primaryViewsToUpdate.Add(jump.WindowPrimaryTextView);
                jump.WindowFrame?.SetDocumentWindowFrameCaptionWithLabel(jump.VanillaTabCaption, jump.Label.Label);
            }
#if MEASUREEXECTIME
            setCaptionTiming.Stop();
            Trace.WriteLine($"PeasyMotion document tabs set caption: {setCaptionTiming?.ElapsedMilliseconds} ms");
#endif
            UpdateViewFramesCaptions(primaryViewsToUpdate);
        }

        public static async void UpdateViewFramesCaptions(List<IVsTextView> tviews)
{
#if MEASUREEXECTIME
            var timing2 = Stopwatch.StartNew();
#endif
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                foreach (var v in tviews)
                {
                    v?.UpdateViewFrameCaption();
                }
#if MEASUREEXECTIME
            timing2.Stop();
            Trace.WriteLine($"PeasyMotion document tabs view update captions (UpdateViewFramesCaptions): {timing2?.ElapsedMilliseconds} ms");
#endif
        }

        public bool JumpTo(string label, out JumpToResult jumpToResult)
        {
#if MEASUREEXECTIME
            var timing1 = Stopwatch.StartNew();
#endif
            var jump = currentJumps.FirstOrDefault(j => j.Label.Label == label);
            if (jump.Label.Label != null)
            {
                jumpToResult = new JumpToResult(
                    currentCursorPosition: ThreadHelper.JoinableTaskFactory.Run(async () => // Marshal to UI thread
                    {
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                        return view.Caret.Position.BufferPosition.Position;
                    }),
                    jumpLabelSpan: jump.Adornment != null ? new SnapshotSpan(view.TextSnapshot, jump.Label.Target.Position, 1) : new SnapshotSpan(),
                    windowFrame: jump.WindowFrame
                );
                return true;
            }

            var remainingLabels = algorithm.FilterLabels(currentJumps.Select(j => j.Label).ToList(), label);
            var jumpsToRemove = currentJumps.Where(j => !remainingLabels.Any(l => l.Label == j.Label.Label)).ToList();
            var textViewsToUpdate = new List<IVsTextView>();

            ThreadHelper.Generic.Invoke(() => // Marshal to UI thread
            {
                foreach (var j in jumpsToRemove)
                {
                    currentJumps.Remove(j);
                    if (j.Adornment != null)
                    {
                        layer.RemoveAdornment(j.Adornment);
                    }
                    else if (jumpMode == JumpMode.VisibleDocuments)
                    {
                        j.WindowFrame?.SetDocumentWindowFrameCaptionWithLabel(j.VanillaTabCaption, new string(' ', j.Label.Label.Length));
                        textViewsToUpdate.Add(j.WindowPrimaryTextView);
                        inactiveJumps.Add(j);
                    }
                }

                foreach (var j in currentJumps)
                {
                    var remainingLabel = j.Label.Label.Substring(label.Length);
                    j.Adornment?.UpdateView(remainingLabel, jumpLabelCachedSetupParams);
                    j.WindowFrame?.SetDocumentWindowFrameCaptionWithLabel(j.VanillaTabCaption, new string(' ', j.Label.Label.Length - label.Length) + remainingLabel);
                    textViewsToUpdate.Add(j.WindowPrimaryTextView);
                }
            });

            UpdateViewFramesCaptions(textViewsToUpdate);
#if MEASUREEXECTIME
            timing1.Stop();
            Trace.WriteLine($"PeasyMotion document tabs update caption: {timing1?.ElapsedMilliseconds} ms");
#endif
            jumpToResult = new JumpToResult();
            return false;
        }
        private void OnFormattingPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            var val = vsSettings[e.PropertyName];
            switch (e.PropertyName)
            {
                case nameof(VsSettings.JumpLabelFirstMotionForegroundColor):
                {
                    var brush = val as SolidColorBrush;
                    foreach (var j in currentJumps)
                    {
                        if ((j.Adornment != null) && (j.Label.Label.Length > 1))
                        {
                            j.Adornment.Foreground = brush;
                        }
                    }
                }
                    break;
                case nameof(VsSettings.JumpLabelFirstMotionBackgroundColor):
                {
                    var brush = val as SolidColorBrush;
                    foreach (var j in currentJumps)
                    {
                        if ((j.Adornment != null) && (j.Label.Label.Length > 1))
                        {
                            j.Adornment.Background = brush;
                        }
                    }
                }
                    break;
                case nameof(VsSettings.JumpLabelFinalMotionForegroundColor):
                {
                    var brush = val as SolidColorBrush;
                    foreach (var j in currentJumps)
                    {
                        if ((j.Adornment != null) && (j.Label.Label.Length == 1))
                        {
                            j.Adornment.Foreground = brush;
                        }
                    }
                }
                    break;
                case nameof(VsSettings.JumpLabelFinalMotionBackgroundColor):
                {
                    var brush = val as SolidColorBrush;
                    foreach (var j in currentJumps)
                    {
                        if ((j.Adornment != null) && (j.Label.Label.Length == 1))
                        {
                            j.Adornment.Background = brush;
                        }
                    }
                }
                    break;
            }
        }

        private void JumpLabelAdornmentRemovedCallback(object _, UIElement element)
        {
            JumpLabelUserControl.ReleaseUserControl(element as JumpLabelUserControl);
        }

        public void Dispose()
        {
            if (vsSettings != null)
            {

                vsSettings.PropertyChanged -= OnFormattingPropertyChanged;
            }
            Reset();
        }

        public void Reset()
        {
            ThreadHelper.Generic.Invoke(() => // Marshal to UI thread
            {
                if (layer != null)
                {
                    layer.RemoveAllAdornments();
                }
            });

            var textViewsToUpdate = new List<IVsTextView>();
            foreach (var j in currentJumps.Concat(inactiveJumps))
            {
                textViewsToUpdate.Add(j.WindowPrimaryTextView);
                ThreadHelper.Generic.Invoke(() => // Marshal to UI thread
                {
                    j.WindowFrame?.RemoveJumpLabelFromDocumentWindowFrameCaption(j.VanillaTabCaption);
                });
            }
            currentJumps.Clear();
            inactiveJumps.Clear();
            UpdateViewFramesCaptions(textViewsToUpdate);
        }

        public bool NoLabelsLeft() => currentJumps.Count == 0;
    }
}