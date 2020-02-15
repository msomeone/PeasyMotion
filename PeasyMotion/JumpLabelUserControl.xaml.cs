using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PeasyMotion
{

    public partial class JumpLabelUserControl : UserControl
    {
        public struct CachedSetupParams {
            public double fontRenderingEmSize;
            public System.Windows.Media.Typeface typeface;
            public SolidColorBrush labelBg;
            public SolidColorBrush labelFg;
        };

        private static string GetTrimmedLabel(string label) // trim max to two characters
        {
            _ = label ?? throw new ArgumentNullException(nameof(label), "cannot be null");
            return label.Substring(0, Math.Min(label.Length, 2));
            //return label; // debug: show full jump label
        }
        public JumpLabelUserControl()
        {
            InitializeComponent();
        }

        public void setup(string label, Rect bounds, JumpLabelUserControl.CachedSetupParams cachedParams)
        {
            var str = GetTrimmedLabel(label); 
            this.Content = str;
            //this.Background = Brushes.GreenYellow;
            //this.Foreground = Brushes.Sienna;
            this.Background = cachedParams.labelBg;
            this.Foreground = cachedParams.labelFg;
   
            this.FontSize = cachedParams.fontRenderingEmSize;
            this.FontFamily = cachedParams.typeface.FontFamily;
            this.FontStyle = cachedParams.typeface.Style;
            //this.FontWeight = cachedParams.typeface.Weight;

            Canvas.SetLeft(this, bounds.Left - this.Padding.Left);
            Canvas.SetTop(this, bounds.Top - this.Padding.Top);
        }

        public void UpdateView(string alreadyPressedKeys)
        {
            _ = alreadyPressedKeys ?? throw new ArgumentNullException(nameof(alreadyPressedKeys), "cannot be null");

            var str = GetTrimmedLabel(alreadyPressedKeys);
            this.Content = str;
            if (str.Length == 1) {
                this.Background = Brushes.LightGray;
                this.Foreground = Brushes.Red;
            }
        }

        private static Stack<JumpLabelUserControl> cache = new Stack<JumpLabelUserControl>(1<<13);

        // warmup just a lil bit, dont allocate whole capacity, as it may slow down on startup.
        public static void WarmupCache()
        {
            for (int i = 0; i < 4096; i++) // should be enough for viewport full of short words on ~3k screen with 14pt font
            {
                cache.Push(new JumpLabelUserControl());
            }
        }
        public static JumpLabelUserControl GetFreeUserControl()
        {
            JumpLabelUserControl ctrl = null;
            if (1 > cache.Count)
            {
                ctrl = new JumpLabelUserControl();
            } else {
                ctrl = cache.Pop();
            }
            return ctrl;
        }

        public static void ReleaseUserControl(JumpLabelUserControl ctrl) {
            cache.Push(ctrl);
        }
    }
}
